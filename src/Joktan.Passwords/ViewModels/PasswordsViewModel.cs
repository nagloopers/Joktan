using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Core;
using Joktan.Core.ViewModels;
using Joktan.Passwords.Resources;

namespace Joktan.Passwords.ViewModels;

/// <summary>
/// Passwords: logins kept in folders, in an encrypted vault or the system keychain. Specified in
/// docs/features/passwords/spec.md.
/// </summary>
public partial class PasswordsViewModel : PageViewModel
{
    // How often the idle time of an open vault is checked.
    private static readonly TimeSpan LockCheckInterval = TimeSpan.FromSeconds(15);

    private readonly PasswordSettings _settings;
    private readonly CredentialStore _credentials;
    private readonly DispatcherTimer _lockTimer;
    private PasswordStorage _storage;
    private PasswordNodeViewModel _root = new(new PasswordFolder(), null);
    private DateTime _lastActivity = DateTime.UtcNow;

    public PasswordsViewModel(PasswordSettings settings, CredentialStore credentials) : base(Strings.Passwords_Passwords, Strings.Passwords_Passwords)
    {
        _settings = settings;
        _credentials = credentials;
        _storage = MakeStorage(settings.Storage ?? PasswordStorageKind.Vault, settings.EffectiveVaultPath);
        _lockTimer = new DispatcherTimer(LockCheckInterval, DispatcherPriority.Background, (_, _) => LockIfIdle());

        SetupVaultPath = settings.EffectiveVaultPath;
        SelectedLockChoice = LockChoices.FirstOrDefault(c => c.Minutes == settings.LockMinutes)
                             ?? new LockChoice(settings.LockMinutes, string.Format(Strings.Passwords_AfterMinutes, settings.LockMinutes));
        if (settings.Storage is null)
            State = PasswordsState.Setup;
        else if (!_storage.Exists)
            State = PasswordsState.Setup;
        else if (_storage.Kind == PasswordStorageKind.Keychain)
            TryOpen(null);
        else
            State = PasswordsState.Locked;
    }

    // ---- State ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSetup), nameof(IsLocked), nameof(IsOpen), nameof(CanLeaveSetup))]
    public partial PasswordsState State { get; set; }

    public bool IsSetup => State == PasswordsState.Setup;

    public bool IsLocked => State == PasswordsState.Locked;

    public bool IsOpen => State == PasswordsState.Open;

    public bool IsVault => _storage.Kind == PasswordStorageKind.Vault;

    public string StorageText => IsVault ? Strings.Passwords_EncryptedVaultFile : KeychainName;

    public string StorageLocation => _storage.Location;

    public bool IsKeychainAvailable => _credentials.IsAvailable;

    public static string KeychainName => OperatingSystem.IsWindows() ? Strings.Passwords_WindowsCredentialManager
        : OperatingSystem.IsMacOS() ? Strings.Passwords_MacOSKeychain
        : Strings.Passwords_SystemKeychainSecretService;

    [ObservableProperty]
    public partial string? Error { get; set; }

    /// <summary>What happened last, such as that a password was copied.</summary>
    [ObservableProperty]
    public partial string? Message { get; set; }

    /// <summary>Copies text to the clipboard and clears it again later. Set by the view.</summary>
    public Func<string, Task>? CopyText { get; set; }

    /// <summary>Opens a web address in the browser. Set by the view.</summary>
    public Func<Uri, Task>? OpenUri { get; set; }

    /// <summary>Asks where a vault file should go, or which one to open. Set by the view.</summary>
    public Func<Task<string?>>? PickVaultFile { get; set; }

    // ---- Setting up ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SetupIsKeychain), nameof(SetupVaultExists), nameof(SetupButtonText))]
    public partial bool SetupIsVault { get; set; } = true;

    public bool SetupIsKeychain
    {
        get => !SetupIsVault;
        set => SetupIsVault = !value;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SetupVaultExists), nameof(SetupButtonText))]
    public partial string SetupVaultPath { get; set; } = "";

    /// <summary>Whether a vault file is already at the chosen place, so it is opened rather than made.</summary>
    public bool SetupVaultExists => SetupIsVault && File.Exists(FullPath(SetupVaultPath));

    public string SetupButtonText => SetupIsVault ? SetupVaultExists ? Strings.Passwords_OpenVault : Strings.Passwords_CreateVault : Strings.Passwords_UseKeychain;

    [ObservableProperty]
    public partial string NewMasterPassword { get; set; } = "";

    [ObservableProperty]
    public partial string RepeatMasterPassword { get; set; } = "";

    [RelayCommand]
    private async Task BrowseVaultAsync()
    {
        if (PickVaultFile is not null && await PickVaultFile() is { } path)
        {
            SetupVaultPath = path;
            TargetVaultPath = path;
        }
    }

    [RelayCommand]
    private void Setup()
    {
        Error = null;
        if (SetupIsVault)
        {
            var path = FullPath(SetupVaultPath);
            var storage = new VaultStorage(path);
            if (storage.Exists)
            {
                // An existing vault is opened with its own master password.
                Use(storage);
                State = PasswordsState.Locked;
                return;
            }

            if (CheckNewMasterPassword() is { } problem)
            {
                Error = problem;
                return;
            }

            if (!Run(() => storage.Create(NewMasterPassword)))
                return;
            Use(storage);
        }
        else
        {
            var storage = new KeychainStorage(_credentials);
            if (!storage.Exists && !Run(() => storage.Create(null)))
                return;
            Use(storage);
            if (!TryOpen(null))
                return;
        }

        ClearMasterFields();
        Show();
    }

    /// <summary>Whether there is a storage in use to go back to from setting up.</summary>
    public bool CanLeaveSetup => _settings.Storage is not null && _storage.Exists;

    [RelayCommand]
    private void LeaveSetup()
    {
        Error = null;
        ClearMasterFields();
        if (_storage.IsOpen)
            State = PasswordsState.Open;
        else if (IsVault)
            State = PasswordsState.Locked;
        else
            TryOpen(null);
    }

    [RelayCommand]
    private void ChooseStorage()
    {
        Error = null;
        SetupIsVault = IsVault;
        SetupVaultPath = IsVault ? _storage.Location : VaultStorage.DefaultPath;
        State = PasswordsState.Setup;
    }

    // ---- Locking ----

    [ObservableProperty]
    public partial string UnlockPassword { get; set; } = "";

    public IReadOnlyList<LockChoice> LockChoices { get; } =
    [
        new(1, Strings.Passwords_After1Minute), new(5, Strings.Passwords_After5Minutes), new(15, Strings.Passwords_After15Minutes),
        new(30, Strings.Passwords_After30Minutes), new(60, Strings.Passwords_After1Hour), new(0, Strings.Passwords_Never),
    ];

    /// <summary>How long an open vault may go unused before it locks.</summary>
    [ObservableProperty]
    public partial LockChoice? SelectedLockChoice { get; set; }

    partial void OnSelectedLockChoiceChanged(LockChoice? value)
    {
        if (value is null || value.Minutes == _settings.LockMinutes)
            return;
        _settings.LockMinutes = value.Minutes;
        _settings.Save();
    }

    [RelayCommand]
    private void Unlock()
    {
        Error = null;
        if (TryOpen(UnlockPassword))
            UnlockPassword = "";
    }

    /// <summary>Closes an open vault; the keychain is protected by the computer's login instead.</summary>
    [RelayCommand]
    private void Lock()
    {
        if (!IsOpen || !IsVault)
            return;
        FinishEditing();
        _storage.Close();
        _lockTimer.Stop();
        Clear();
        Message = null;
        State = PasswordsState.Locked;
    }

    /// <summary>Notes that the page was used, which keeps an open vault from locking. Called by the view.</summary>
    public void Touch() => _lastActivity = DateTime.UtcNow;

    private void LockIfIdle()
    {
        if (IsOpen && IsVault && _settings.LockMinutes > 0
            && DateTime.UtcNow - _lastActivity > TimeSpan.FromMinutes(_settings.LockMinutes))
            Lock();
    }

    /// <summary>Saves an entry being edited and closes the storage. Called when the app exits.</summary>
    public void SaveState()
    {
        if (IsOpen)
            FinishEditing();
        _storage.Close();
    }

    // ---- The tree ----

    /// <summary>The top-level folders and entries.</summary>
    public ObservableCollection<PasswordNodeViewModel> Roots => _root.Children;

    public bool IsEmpty => Roots.Count == 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TreeSelection), nameof(HasSelection), nameof(SelectedEntry), nameof(SelectedFolder),
        nameof(IsEntrySelected), nameof(IsFolderSelected), nameof(DeleteText), nameof(FolderSummary), nameof(ModifiedText),
        nameof(EntryUsername), nameof(EntryUrl), nameof(EntryNotes), nameof(HasUsername), nameof(HasUrl), nameof(HasNotes))]
    public partial PasswordNodeViewModel? Selected { get; set; }

    /// <summary>
    /// The selection as the tree sees it. A Ctrl or ⌘ click in the tree clears its selection; that
    /// is ignored while the selected node is still in the tree.
    /// </summary>
    public PasswordNodeViewModel? TreeSelection
    {
        get => Selected;
        set
        {
            if (value is null && Selected is { } current && _root.SelfAndDescendants().Contains(current))
                return;
            Selected = value;
        }
    }

    public bool HasSelection => Selected is not null;

    public PasswordEntry? SelectedEntry => Selected?.Entry;

    public PasswordNodeViewModel? SelectedFolder => Selected?.IsFolder == true ? Selected : null;

    public bool IsEntrySelected => Selected?.IsEntry == true;

    public bool IsFolderSelected => Selected?.IsFolder == true;

    public string EntryUsername => SelectedEntry?.Username ?? "";

    public string EntryUrl => SelectedEntry?.Url ?? "";

    public string EntryNotes => SelectedEntry?.Notes ?? "";

    public bool HasUsername => EntryUsername.Length > 0;

    public bool HasUrl => EntryUrl.Length > 0;

    public bool HasNotes => EntryNotes.Trim().Length > 0;

    public string ModifiedText => SelectedEntry is { } entry
        ? string.Format(Strings.Passwords_CreatedLastChanged, entry.Created.ToLocalTime(), entry.Modified.ToLocalTime())
        : "";

    public string FolderSummary
    {
        get
        {
            if (Selected?.Folder is not { } folder)
                return "";
            var count = folder.AllEntries().Count();
            return count switch
            {
                0 => Strings.Passwords_NoEntries,
                1 => Strings.Passwords_1Entry,
                _ => string.Format(Strings.Passwords_Entries, count),
            };
        }
    }

    partial void OnSelectedChanging(PasswordNodeViewModel? value)
    {
        // Leaving an entry or folder that is being edited keeps the changes.
        if (IsEditing)
            FinishEditing();
    }

    partial void OnSelectedChanged(PasswordNodeViewModel? value)
    {
        IsConfirmingDelete = false;
        IsRevealed = false;
        RevealedPassword = "";
        if (value is null)
            return;
        foreach (var folder in value.Ancestors())
            folder.IsExpanded = true;
    }

    // ---- Search ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSearching))]
    public partial string SearchText { get; set; } = "";

    public bool IsSearching => SearchText.Trim().Length > 0;

    public ObservableCollection<PasswordNodeViewModel> SearchResults { get; } = [];

    public bool HasNoResults => IsSearching && SearchResults.Count == 0;

    partial void OnSearchTextChanged(string value) => RefreshSearch();

    private void RefreshSearch()
    {
        SearchResults.Clear();
        var text = SearchText.Trim();
        OnPropertyChanged(nameof(HasNoResults));
        if (text.Length == 0)
            return;
        foreach (var node in _root.SelfAndDescendants().Where(n => n.Matches(text)).OrderBy(n => n.Name, StringComparer.CurrentCultureIgnoreCase))
            SearchResults.Add(node);
        OnPropertyChanged(nameof(HasNoResults));
    }

    // ---- Viewing an entry ----

    [ObservableProperty]
    public partial bool IsRevealed { get; set; }

    [ObservableProperty]
    public partial string RevealedPassword { get; set; } = "";

    [RelayCommand]
    private void ToggleReveal()
    {
        if (SelectedEntry is not { } entry)
            return;
        IsRevealed = !IsRevealed;
        RevealedPassword = IsRevealed ? _storage.ReadPassword(entry) : "";
    }

    [RelayCommand]
    private Task CopyUsernameAsync() => Copy(SelectedEntry?.Username, Strings.Passwords_UsernameCopied);

    [RelayCommand]
    private Task CopyPasswordAsync()
        => Copy(SelectedEntry is { } entry ? _storage.ReadPassword(entry) : null,
            Strings.Passwords_PasswordCopiedTheClipboardIsCleared);

    [RelayCommand]
    private Task CopyUrlAsync() => Copy(SelectedEntry?.Url, Strings.Passwords_WebsiteCopied);

    [RelayCommand]
    private async Task OpenUrlAsync()
    {
        if (SelectedEntry?.Url.Trim() is not { Length: > 0 } url || OpenUri is null)
            return;
        if (!url.Contains("://", StringComparison.Ordinal))
            url = "https://" + url;
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
            await OpenUri(uri);
        else
            Error = Strings.Passwords_TheWebsiteIsNotAWeb;
    }

    private async Task Copy(string? text, string message)
    {
        if (string.IsNullOrEmpty(text) || CopyText is null)
            return;
        await CopyText(text);
        Message = message;
    }

    // ---- Editing ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsViewing))]
    public partial bool IsEditing { get; set; }

    public bool IsViewing => !IsEditing;

    // A node made by New entry or New folder, removed again if its editing is cancelled.
    private PasswordNodeViewModel? _newNode;

    /// <summary>The heading while editing, such as "New entry" or "Edit Email".</summary>
    [ObservableProperty]
    public partial string EditHeading { get; set; } = "";

    [ObservableProperty]
    public partial string EditTitle { get; set; } = "";

    [ObservableProperty]
    public partial string EditUsername { get; set; } = "";

    [ObservableProperty]
    public partial string EditPassword { get; set; } = "";

    [ObservableProperty]
    public partial string EditUrl { get; set; } = "";

    [ObservableProperty]
    public partial string EditNotes { get; set; } = "";

    [ObservableProperty]
    public partial bool ShowEditPassword { get; set; }

    [ObservableProperty]
    public partial int GenerateLength { get; set; } = 20;

    [ObservableProperty]
    public partial bool GenerateSymbols { get; set; } = true;

    /// <summary>The folders the edited entry or folder can be put in.</summary>
    public ObservableCollection<PasswordFolderChoice> FolderChoices { get; } = [];

    [ObservableProperty]
    public partial PasswordFolderChoice? EditFolder { get; set; }

    [RelayCommand]
    private void Edit()
    {
        if (Selected is not { } node)
            return;
        EditTitle = node.Folder?.Name ?? node.Entry!.Title;
        EditUsername = node.Entry?.Username ?? "";
        EditPassword = node.Entry is { } entry ? _storage.ReadPassword(entry) : "";
        EditUrl = node.Entry?.Url ?? "";
        EditNotes = node.Entry?.Notes ?? "";
        ShowEditPassword = false;

        // A folder cannot go inside itself.
        var excluded = node.IsFolder ? node.SelfAndDescendants().ToHashSet() : [];
        FolderChoices.Clear();
        FolderChoices.Add(new PasswordFolderChoice(_root, Strings.Passwords_TopLevel));
        foreach (var folder in _root.SelfAndDescendants().Where(n => n.IsFolder && n != _root && !excluded.Contains(n)))
            FolderChoices.Add(new PasswordFolderChoice(folder, new string(' ', 4 * folder.Ancestors().Count()) + folder.Name));
        EditFolder = FolderChoices.FirstOrDefault(c => c.Node == node.Parent);
        EditHeading = string.Format(Strings.Passwords_Edit, node.Name);
        Message = null;
        IsEditing = true;
    }

    [RelayCommand]
    private void Generate() => EditPassword = PasswordGenerator.Generate(GenerateLength, GenerateSymbols);

    [RelayCommand]
    private void Save()
    {
        if (FinishEditing())
            Message = Selected?.IsFolder == true ? Strings.Passwords_FolderSaved : Strings.Passwords_EntrySaved;
    }

    [RelayCommand]
    private void Cancel()
    {
        if (!IsEditing)
            return;
        IsEditing = false;
        if (_newNode is { } added)
        {
            _newNode = null;
            Remove(added);
            Selected = added.Parent == _root ? Roots.FirstOrDefault() : added.Parent;
        }
    }

    /// <summary>Applies the edits to the selected node and writes them; false when they could not be kept.</summary>
    private bool FinishEditing()
    {
        if (!IsEditing || Selected is not { } node)
            return true;

        IsEditing = false;
        var isNew = _newNode == node;
        _newNode = null;

        // A new entry left without anything in it is not kept.
        if (isNew && node.Entry is not null && EditTitle.Trim().Length == 0 && EditUsername.Length == 0
            && EditPassword.Length == 0 && EditUrl.Trim().Length == 0 && EditNotes.Trim().Length == 0)
        {
            Remove(node);
            return false;
        }

        if (node.Folder is { } folder)
        {
            folder.Name = EditTitle.Trim();
        }
        else if (node.Entry is { } entry)
        {
            var passwordChanged = isNew || EditPassword != _storage.ReadPassword(entry);
            entry.Title = EditTitle.Trim();
            entry.Username = EditUsername;
            entry.Url = EditUrl.Trim();
            entry.Notes = EditNotes;
            entry.Modified = DateTime.UtcNow;
            if (passwordChanged && !Run(() => _storage.WritePassword(entry, EditPassword)))
                return false;
        }

        if (EditFolder?.Node is { } target && target != node.Parent)
            MoveTo(node, target);
        node.Refresh();
        node.Parent?.Sort();
        if (!Run(_storage.Save))
            return false;
        RefreshSearch();
        OnPropertyChanged(nameof(FolderSummary));
        foreach (var name in (string[])[nameof(ModifiedText), nameof(EntryUsername), nameof(EntryUrl), nameof(EntryNotes),
                     nameof(HasUsername), nameof(HasUrl), nameof(HasNotes)])
            OnPropertyChanged(name);
        EditPassword = "";
        return true;
    }

    [RelayCommand]
    private void NewEntry() => Add(new PasswordNodeViewModel(new PasswordEntry(), TargetFolder()));

    [RelayCommand]
    private void NewFolder() => Add(new PasswordNodeViewModel(new PasswordFolder { Name = Strings.Passwords_NewFolder }, TargetFolder()));

    // New things go in the selected folder, or the folder of the selected entry.
    private PasswordNodeViewModel TargetFolder() => Selected switch
    {
        { IsFolder: true } folder => folder,
        { Parent: { } parent } => parent,
        _ => _root,
    };

    private void Add(PasswordNodeViewModel node)
    {
        FinishEditing();
        var parent = node.Parent!;
        if (node.Folder is { } folder)
            parent.Folder!.Folders.Add(folder);
        else
            parent.Folder!.Entries.Add(node.Entry!);
        parent.Children.Add(node);
        parent.IsExpanded = true;
        OnPropertyChanged(nameof(IsEmpty));

        SearchText = "";
        Selected = node;
        Edit();
        EditHeading = node.IsFolder ? Strings.Passwords_NewFolder : Strings.Passwords_NewEntry;
        _newNode = node;
    }

    private void MoveTo(PasswordNodeViewModel node, PasswordNodeViewModel target)
    {
        var parent = node.Parent!;
        if (node.Folder is { } folder)
        {
            parent.Folder!.Folders.Remove(folder);
            target.Folder!.Folders.Add(folder);
        }
        else
        {
            parent.Folder!.Entries.Remove(node.Entry!);
            target.Folder!.Entries.Add(node.Entry!);
        }

        // Taking it out of the tree clears the tree's selection; it is selected again once moved.
        parent.Children.Remove(node);
        node.Parent = target;
        target.Children.Add(node);
        target.IsExpanded = true;
        Selected = node;
    }

    private void Remove(PasswordNodeViewModel node)
    {
        var parent = node.Parent!;
        if (node.Folder is { } folder)
            parent.Folder!.Folders.Remove(folder);
        else
            parent.Folder!.Entries.Remove(node.Entry!);
        parent.Children.Remove(node);
        OnPropertyChanged(nameof(IsEmpty));
    }

    // ---- Deleting ----

    [ObservableProperty]
    public partial bool IsConfirmingDelete { get; set; }

    public string DeleteText
    {
        get
        {
            if (Selected is not { } node)
                return "";
            if (node.Entry is not null)
                return string.Format(Strings.Passwords_DeleteAndItsPasswordThisCannot, node.Name);

            var entries = node.Folder!.AllEntries().Count();
            var folders = node.SelfAndDescendants().Count(n => n.IsFolder) - 1;
            var inside = (entries, folders) switch
            {
                (0, 0) => "",
                (_, 0) => string.Format(Strings.Passwords_AndTheInIt, Plural(entries, Strings.Passwords_Entry, Strings.Passwords_Entries2)),
                (0, _) => string.Format(Strings.Passwords_AndTheInIt, Plural(folders, Strings.Passwords_Folder, Strings.Passwords_Folders)),
                _ => string.Format(Strings.Passwords_AndTheAndInIt, Plural(entries, Strings.Passwords_Entry, Strings.Passwords_Entries2), Plural(folders, Strings.Passwords_Folder, Strings.Passwords_Folders)),
            };
            return string.Format(Strings.Passwords_DeleteTheFolderThisCannotBe, node.Name, inside);

            static string Plural(int count, string one, string many) => count == 1 ? $"1 {one}" : $"{count} {many}";
        }
    }

    [RelayCommand]
    private void Delete()
    {
        if (HasSelection)
            IsConfirmingDelete = true;
    }

    [RelayCommand]
    private void CancelDelete() => IsConfirmingDelete = false;

    [RelayCommand]
    private void ConfirmDelete()
    {
        IsConfirmingDelete = false;
        if (Selected is not { } node)
            return;

        IsEditing = false;
        _newNode = null;
        var siblings = node.Parent!.Children;
        var index = siblings.IndexOf(node);
        var entries = node.Folder?.AllEntries().ToList() ?? [node.Entry!];
        Remove(node);
        if (!Run(_storage.Save))
            return;
        _storage.DeletePasswords(entries);
        RefreshSearch();

        Selected = index < siblings.Count ? siblings[index]
            : index > 0 ? siblings[index - 1]
            : node.Parent == _root ? Roots.FirstOrDefault() : node.Parent;
        Message = node.IsFolder ? Strings.Passwords_FolderDeleted : Strings.Passwords_EntryDeleted;
    }

    // ---- Storage and master password ----

    [ObservableProperty]
    public partial bool IsChangingStorage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TargetIsKeychain), nameof(TargetVaultExists), nameof(TargetPasswordLabel))]
    public partial bool TargetIsVault { get; set; }

    public bool TargetIsKeychain
    {
        get => !TargetIsVault;
        set => TargetIsVault = !value;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TargetVaultExists), nameof(TargetPasswordLabel))]
    public partial string TargetVaultPath { get; set; } = "";

    public bool TargetVaultExists => TargetIsVault && File.Exists(FullPath(TargetVaultPath));

    public string TargetPasswordLabel => TargetVaultExists
        ? Strings.Passwords_MasterPasswordOfTheVaultAlready
        : Strings.Passwords_MasterPasswordForTheNewVault;

    [ObservableProperty]
    public partial string TargetMasterPassword { get; set; } = "";

    [ObservableProperty]
    public partial string TargetRepeatPassword { get; set; } = "";

    [ObservableProperty]
    public partial string CurrentMasterPassword { get; set; } = "";

    [RelayCommand]
    private void ToggleStorage()
    {
        IsChangingStorage = !IsChangingStorage;
        Error = null;
        TargetIsVault = !IsVault;
        TargetVaultPath = IsVault ? _storage.Location : VaultStorage.DefaultPath;
        TargetMasterPassword = TargetRepeatPassword = CurrentMasterPassword = NewMasterPassword = RepeatMasterPassword = "";
    }

    /// <summary>
    /// Moves every entry to the storage chosen: copies them there, then removes them from where
    /// they were. Where the copy fails, nothing is removed.
    /// </summary>
    [RelayCommand]
    private void MoveStorage()
    {
        Error = null;
        FinishEditing();

        PasswordStorage target;
        var created = false;
        if (TargetIsVault)
        {
            var path = FullPath(TargetVaultPath);
            if (IsVault && string.Equals(path, _storage.Location, StringComparison.Ordinal))
            {
                Error = Strings.Passwords_TheEntriesAreAlreadyInThis;
                return;
            }

            target = new VaultStorage(path);
            if (target.Exists)
            {
                if (!Run(() =>
                    {
                        if (!target.Open(TargetMasterPassword))
                            throw new PasswordStorageException(Strings.Passwords_ThatIsNotTheMasterPassword);
                    }))
                    return;
            }
            else
            {
                if (CheckMasterPassword(TargetMasterPassword, TargetRepeatPassword) is { } problem)
                {
                    Error = problem;
                    return;
                }

                if (!Run(() => target.Create(TargetMasterPassword)))
                    return;
                created = true;
            }
        }
        else
        {
            if (!IsVault)
            {
                Error = Strings.Passwords_TheEntriesAreAlreadyInThe;
                return;
            }

            var keychain = new KeychainStorage(_credentials);
            if (!keychain.IsAvailable)
            {
                Error = Strings.Passwords_ThisComputerHasNoSystemKeychain;
                return;
            }

            target = keychain;
            created = !target.Exists;
            if (created ? !Run(() => target.Create(null)) : !Run(() => target.Open(null)))
                return;
        }

        var copy = _storage.Root.Copy();
        var passwords = _storage.Root.AllEntries().ToDictionary(e => e.Id, e => _storage.ReadPassword(e));
        var moved = Run(() =>
        {
            target.Root.Folders.AddRange(copy.Folders);
            target.Root.Entries.AddRange(copy.Entries);
            foreach (var entry in copy.AllEntries())
            {
                entry.Password = null;
                target.WritePassword(entry, passwords[entry.Id]);
            }

            target.Save();
        });
        if (!moved)
        {
            if (created)
                Run(target.Destroy);
            else
                target.Close();
            return;
        }

        var from = Describe();
        var removed = Run(_storage.Destroy);
        _settings.Storage = target.Kind;
        if (target is VaultStorage vault)
            _settings.VaultPath = vault.Path == VaultStorage.DefaultPath ? "" : vault.Path;
        _settings.Save();

        Use(target);
        Show();
        IsChangingStorage = false;
        TargetMasterPassword = TargetRepeatPassword = "";
        Message = removed
            ? string.Format(Strings.Passwords_TheEntriesWereMovedToAnd, Describe(), from)
            : string.Format(Strings.Passwords_TheEntriesWereCopiedToBut, Describe(), from);
    }

    [RelayCommand]
    private void ChangeMasterPassword()
    {
        Error = null;
        if (_storage is not VaultStorage vault)
            return;
        var check = new VaultStorage(vault.Path);
        if (!Run(() =>
            {
                if (!check.Open(CurrentMasterPassword))
                    throw new PasswordStorageException(Strings.Passwords_TheCurrentMasterPasswordIsNot);
            }))
            return;
        check.Close();

        if (CheckMasterPassword(TargetMasterPassword, TargetRepeatPassword) is { } problem)
        {
            Error = problem;
            return;
        }

        if (!Run(() => vault.ChangeMasterPassword(TargetMasterPassword)))
            return;
        CurrentMasterPassword = TargetMasterPassword = TargetRepeatPassword = "";
        IsChangingStorage = false;
        Message = Strings.Passwords_TheMasterPasswordWasChanged;
    }

    // ---- Helpers ----

    private string Describe() => IsVault ? string.Format(Strings.Passwords_TheVault, _storage.Location) : string.Format(Strings.Passwords_The, KeychainName);

    private PasswordStorage MakeStorage(PasswordStorageKind kind, string vaultPath) => kind == PasswordStorageKind.Vault
        ? new VaultStorage(vaultPath)
        : new KeychainStorage(_credentials);

    private void Use(PasswordStorage storage)
    {
        if (storage != _storage)
            _storage.Close();
        _storage = storage;
        _settings.Storage = storage.Kind;
        if (storage is VaultStorage vault)
            _settings.VaultPath = vault.Path == VaultStorage.DefaultPath ? "" : vault.Path;
        _settings.Save();
        OnPropertyChanged(nameof(IsVault));
        OnPropertyChanged(nameof(StorageText));
        OnPropertyChanged(nameof(StorageLocation));
    }

    private bool TryOpen(string? masterPassword)
    {
        var opened = false;
        if (!Run(() => opened = _storage.Open(masterPassword)))
            return false;
        if (!opened)
        {
            Error = Strings.Passwords_ThatIsNotTheMasterPassword2;
            return false;
        }

        Show();
        return true;
    }

    // Builds the tree from the open storage and shows it.
    private void Show()
    {
        Clear();
        _root = new PasswordNodeViewModel(_storage.Root, null);
        OnPropertyChanged(nameof(Roots));
        OnPropertyChanged(nameof(IsEmpty));
        Selected = _root.SelfAndDescendants().FirstOrDefault(n => n.IsEntry) ?? Roots.FirstOrDefault();
        Error = null;
        State = PasswordsState.Open;
        Touch();
        _lockTimer.Start();
    }

    private void Clear()
    {
        IsEditing = false;
        _newNode = null;
        Selected = null;
        SearchText = "";
        SearchResults.Clear();
        _root = new PasswordNodeViewModel(new PasswordFolder(), null);
        OnPropertyChanged(nameof(Roots));
        OnPropertyChanged(nameof(IsEmpty));
        EditPassword = RevealedPassword = "";
    }

    private void ClearMasterFields() => NewMasterPassword = RepeatMasterPassword = "";

    private string? CheckNewMasterPassword() => CheckMasterPassword(NewMasterPassword, RepeatMasterPassword);

    private static string? CheckMasterPassword(string password, string repeat)
    {
        if (password.Length < VaultStorage.MinimumMasterPasswordLength)
            return string.Format(Strings.Passwords_TheMasterPasswordNeedsAtLeast, VaultStorage.MinimumMasterPasswordLength);
        if (password != repeat)
            return Strings.Passwords_TheTwoMasterPasswordsAreNot;
        return null;
    }

    private static string FullPath(string path)
    {
        path = path.Trim();
        if (path.Length == 0)
            return VaultStorage.DefaultPath;
        if (path.StartsWith('~'))
            path = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[1..].TrimStart('/', '\\'));
        return Path.GetFullPath(path);
    }

    // Runs a storage action, showing its problem rather than letting it escape.
    private bool Run(Action action)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception e) when (e is PasswordStorageException or IOException or UnauthorizedAccessException)
        {
            Error = e.Message;
            return false;
        }
    }
}

public sealed record LockChoice(int Minutes, string Label)
{
    public override string ToString() => Label;
}

public enum PasswordsState
{
    /// <summary>No storage chosen yet, or choosing another.</summary>
    Setup,

    /// <summary>A vault waiting for its master password.</summary>
    Locked,

    Open,
}
