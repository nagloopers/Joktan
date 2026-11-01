using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Core.ViewModels;
using Joktan.Settings.Backup;
using Joktan.Settings.Resources;

namespace Joktan.Settings.ViewModels;

/// <summary>One folder on the page: where it is, changing it, and whether it goes in a backup.</summary>
public partial class FolderRowViewModel : ViewModelBase
{
    private readonly BackupViewModel _page;
    private bool _loading = true;
    private string _shownPath;

    public FolderRowViewModel(FolderSource source, BackupViewModel page, bool included)
    {
        Source = source;
        _page = page;
        _shownPath = source.CurrentPath();
        PathText = _shownPath;
        IsIncluded = included;
        Problem = source.StartProblem;
        _loading = false;
    }

    public FolderSource Source { get; }

    public string Name => Source.Name;

    public string Hint => Source.Hint;

    public bool HasHint => Hint.Length > 0;

    public bool CanChange => Source.Change is not null;

    public bool IsFixed => !CanChange;

    public bool HasDefault => Source.DefaultPath is not null;

    public string ChangeText => Source.ChangeText;

    public bool CanOpenPage => Source.OpenPage is not null;

    public string OpenPageText => Source.OpenPageText;

    public bool CanBackUp => Source.Item is not null;

    /// <summary>Whether there is nothing to back up yet, as no folder has been chosen.</summary>
    public bool HasNoFolder => Source.AllowNone && Source.CurrentPath().Length == 0;

    public bool HasSomethingToBackUp => !HasNoFolder;

    /// <summary>The path shown when it cannot be changed here.</summary>
    public string CurrentText => Source.CurrentPath() is { Length: > 0 } path ? path : Strings.FolderRow_NoFolderChosen;

    [ObservableProperty]
    public partial string PathText { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsWarning))]
    public partial bool IsIncluded { get; set; }

    public bool ShowsWarning => IsIncluded && Source.Warning is not null;

    /// <summary>Takes the place of the switch being off, for a row with nothing to back up.</summary>
    public bool IsIncludedAndHasFolder
    {
        get => IsIncluded && HasSomethingToBackUp;
        set
        {
            if (HasSomethingToBackUp)
                IsIncluded = value;
        }
    }

    public string Warning => Source.Warning ?? "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblem))]
    public partial string? Problem { get; private set; }

    public bool HasProblem => Problem is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    public partial string? Message { get; private set; }

    public bool HasMessage => Message is not null;

    /// <summary>Shows where it is now, unless something else has been typed.</summary>
    public void Refresh()
    {
        var current = Source.CurrentPath();
        if (PathText == _shownPath)
            PathText = current;
        _shownPath = current;
        OnPropertyChanged(nameof(CurrentText));
        OnPropertyChanged(nameof(HasNoFolder));
        OnPropertyChanged(nameof(HasSomethingToBackUp));
        OnPropertyChanged(nameof(IsIncludedAndHasFolder));
    }

    partial void OnIsIncludedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsIncludedAndHasFolder));
        if (!_loading)
            _page.SetIncluded(Source.Id, value);
    }

    [RelayCommand]
    private async Task BrowseAsync()
    {
        if (_page.PickFolder is not null && await _page.PickFolder() is { } folder)
            PathText = folder;
    }

    [RelayCommand]
    private void UseDefault() => PathText = Source.DefaultPath ?? "";

    [RelayCommand]
    private void OpenPage() => Source.OpenPage?.Invoke();

    [RelayCommand]
    private async Task ApplyAsync()
    {
        if (Source.Change is not { } change)
            return;

        Problem = Message = null;
        var text = PathText.Trim();
        string? path;
        if (text.Length == 0 && Source.DefaultPath is { } fallback)
        {
            path = fallback;
        }
        else if (text.Length == 0 && Source.AllowNone)
        {
            path = "";
        }
        else if ((path = BackupPlanner.WholePath(text)) is null)
        {
            Problem = Strings.FolderRow_GiveTheWholePathOfThe;
            return;
        }

        if (path == Source.CurrentPath() && Source.StartProblem is null)
        {
            PathText = path;
            return;
        }

        var result = await change(path);
        Problem = result.Problem;
        Message = result.Message;
        if (result.Problem is null)
        {
            PathText = Source.CurrentPath();
            Refresh();
        }
    }
}

/// <summary>One thing a backup holds, to switch on for restoring.</summary>
public partial class RestoreRowViewModel(ManifestItem item, string target) : ViewModelBase
{
    public ManifestItem Item { get; } = item;

    public string Name => Item.Name;

    public string Detail { get; } = item.FileCount == 0
        ? Strings.RestoreRow_NothingOfThisIsInThe
        : string.Format(Strings.RestoreRow_GoesTo, item.FileCount, (item.FileCount == 1 ? Strings.RestoreRow_File : Strings.RestoreRow_Files), target);

    public bool HasFiles => Item.FileCount > 0;

    [ObservableProperty]
    public partial bool IsChosen { get; set; } = item.FileCount > 0;
}

/// <summary>A backup in the backup folder, to choose from.</summary>
public sealed record BackupChoice(string Path, string Name);

/// <summary>
/// Settings / Folders and Backup: every folder Joktan keeps things in, which go in a backup, and backing up
/// and restoring. Specified in docs/features/settings/spec.md.
/// </summary>
public partial class BackupViewModel : PageViewModel
{
    private readonly Func<IReadOnlyList<FolderSource>> _sources;
    private readonly BackupSettings _settings;
    private readonly Func<Task> _flush;
    private readonly string _appVersion;

    /// <param name="sources">The folders there are now, which change as MQTT clients come and go.</param>
    /// <param name="flush">Writes everything not yet on disk, so that a backup holds what is on screen.</param>
    /// <param name="restart">Closes the app and starts it again, which is when a restore happens.</param>
    public BackupViewModel(Func<IReadOnlyList<FolderSource>> sources, BackupSettings settings, Func<Task> flush, Action restart, string appVersion = "")
        : base(Strings.Backup_FoldersAndBackup, Strings.Backup_SettingsFoldersAndBackup)
    {
        _sources = sources;
        _settings = settings;
        _flush = flush;
        Restart = restart;
        _appVersion = appVersion;
        BackupFolderText = settings.EffectiveFolder;
        KeepLast = settings.KeepLast;
        Refresh();
        if (PendingRestore.TakeOutcome() is { } outcome)
            ShowOutcome(outcome);
    }

    public ObservableCollection<FolderRowViewModel> Rows { get; } = [];

    /// <summary>One for each MQTT client, following the clients as they are added, renamed and removed.</summary>
    public ObservableCollection<FolderRowViewModel> MessageRows { get; } = [];

    public ObservableCollection<BackupChoice> Backups { get; } = [];

    public ObservableCollection<RestoreRowViewModel> RestoreRows { get; } = [];

    public ObservableCollection<string> Skipped { get; } = [];

    public Action Restart { get; }

    /// <summary>Asks the user for a folder. Set by the view, which owns the system dialog.</summary>
    public Func<Task<string?>>? PickFolder { get; set; }

    /// <summary>Asks the user for a zip. Set by the view, which owns the system dialog.</summary>
    public Func<Task<string?>>? PickZip { get; set; }

    [ObservableProperty]
    public partial string BackupFolderText { get; set; }

    [ObservableProperty]
    public partial int KeepLast { get; set; }

    public static IReadOnlyList<int> KeepChoices { get; } = [1, 3, 5, 10, 20, 30, 50, 100];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    public partial bool IsBusy { get; private set; }

    public bool IsIdle => !IsBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string? Status { get; private set; }

    public bool HasStatus => Status is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; private set; }

    public bool HasError => Error is not null;

    public bool HasSkipped => Skipped.Count > 0;

    /// <summary>The backup being looked at.</summary>
    [ObservableProperty]
    public partial BackupChoice? SelectedBackup { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBackupOpen))]
    public partial string? RestorePath { get; private set; }

    public bool HasBackupOpen => RestorePath is not null;

    [ObservableProperty]
    public partial string RestoreSummary { get; private set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRestoreError))]
    public partial string? RestoreError { get; private set; }

    public bool HasRestoreError => RestoreError is not null;

    [ObservableProperty]
    public partial bool IsConfirmingRestore { get; private set; }

    public string ConfirmText => string.Format(Strings.Backup_ThisReplacesWithWhatIsIn, string.Join(", ", RestoreRows.Where(r => r.IsChosen).Select(r => r.Name)));

    /// <summary>Shows where every folder is now. Called whenever the page is opened, as they change elsewhere.</summary>
    public void Refresh()
    {
        var known = AllRows.ToList();
        var rows = new List<FolderRowViewModel>();
        foreach (var source in _sources())
        {
            var row = known.FirstOrDefault(r => r.Source.Id == source.Id);
            if (row is null)
                row = new FolderRowViewModel(source, this, IsIncluded(source));
            else
                row.Refresh();
            rows.Add(row);
        }

        Show(Rows, rows.Where(r => !r.Source.IsMessageFolder));
        Show(MessageRows, rows.Where(r => r.Source.IsMessageFolder));
        OnPropertyChanged(nameof(HasMessageRows));
        RefreshBackups();
    }

    public bool HasMessageRows => MessageRows.Count > 0;

    /// <summary>Keeps what was chosen for something that goes by a new name, such as a renamed MQTT client.</summary>
    public void Renamed(string oldId, string newId)
    {
        if (_settings.Included.Remove(oldId, out var included))
        {
            _settings.Included[newId] = included;
            _settings.Save();
        }

        // Asked again under its new name, rather than kept as it was.
        foreach (var row in AllRows.Where(r => r.Source.Id == oldId).ToList())
        {
            Rows.Remove(row);
            MessageRows.Remove(row);
        }

        Refresh();
    }

    private IEnumerable<FolderRowViewModel> AllRows => Rows.Concat(MessageRows);

    // Changed only when it differs, so that a box being typed in is not rebuilt under the pointer.
    private static void Show(ObservableCollection<FolderRowViewModel> shown, IEnumerable<FolderRowViewModel> rows)
    {
        var wanted = rows.ToList();
        if (shown.SequenceEqual(wanted))
            return;

        shown.Clear();
        foreach (var row in wanted)
            shown.Add(row);
    }

    /// <summary>Remembers whether a folder goes in a backup.</summary>
    public void SetIncluded(string id, bool included)
    {
        _settings.Included[id] = included;
        _settings.Save();
    }

    partial void OnKeepLastChanged(int value)
    {
        if (value == _settings.KeepLast)
            return;

        _settings.KeepLast = Math.Clamp(value, 1, BackupSettings.MaxKeep);
        _settings.Save();
    }

    partial void OnSelectedBackupChanged(BackupChoice? value)
    {
        if (value is not null)
            OpenBackup(value.Path);
    }

    private bool IsIncluded(FolderSource source)
        => source.Item is not null && (_settings.Included.TryGetValue(source.Id, out var chosen) ? chosen : source.IncludedByDefault);

    private void RefreshBackups()
    {
        var selected = SelectedBackup?.Path;
        var found = new List<BackupChoice>();
        try
        {
            if (Directory.Exists(_settings.EffectiveFolder))
            {
                found = Directory.EnumerateFiles(_settings.EffectiveFolder, "joktan-*.zip")
                    .OrderByDescending(File.GetLastWriteTime)
                    .Select(p => new BackupChoice(p, Path.GetFileName(p)))
                    .ToList();
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Error = string.Format(Strings.Backup_CouldNotRead, _settings.EffectiveFolder, e.Message);
        }

        if (!Backups.Select(b => b.Path).SequenceEqual(found.Select(b => b.Path)))
        {
            Backups.Clear();
            foreach (var backup in found)
                Backups.Add(backup);
        }

        SelectedBackup = Backups.FirstOrDefault(b => b.Path == selected);
    }

    private bool TakeBackupFolder()
    {
        if (BackupPlanner.WholePath(BackupFolderText) is not { } folder)
        {
            Error = Strings.Backup_GiveTheWholePathOfThe;
            return false;
        }

        BackupFolderText = folder;
        if (folder != _settings.EffectiveFolder)
        {
            _settings.Folder = folder == BackupSettings.DefaultFolder ? "" : folder;
            _settings.Save();
        }

        return true;
    }

    [RelayCommand]
    private async Task BrowseBackupFolderAsync()
    {
        if (PickFolder is not null && await PickFolder() is { } folder)
            BackupFolderText = folder;
    }

    [RelayCommand]
    private void UseDefaultBackupFolder() => BackupFolderText = BackupSettings.DefaultFolder;

    /// <summary>Zips what is switched on into the backup folder, and deletes the oldest backups past the number kept.</summary>
    [RelayCommand]
    private async Task BackUpNowAsync()
    {
        Error = Status = null;
        Skipped.Clear();
        OnPropertyChanged(nameof(HasSkipped));
        if (!TakeBackupFolder())
            return;

        var items = AllRows.Where(r => r.CanBackUp && r.IsIncluded).Select(r => r.Source.Item!()).OfType<BackupItem>().ToList();
        if (items.Count == 0)
        {
            Error = Strings.Backup_NothingIsSwitchedOnToBack;
            return;
        }

        IsBusy = true;
        try
        {
            await _flush();
            var folder = _settings.EffectiveFolder;
            var keep = _settings.KeepLast;
            var result = await Task.Run(() =>
            {
                var made = BackupArchive.Create(BackupArchive.NewPath(folder, BackupArchive.BackupPrefix, DateTime.Now), items, _appVersion, [folder]);
                BackupArchive.Verify(made.Path);
                BackupArchive.Prune(folder, BackupArchive.BackupPrefix, keep);
                BackupArchive.Prune(folder, BackupArchive.SafetyPrefix, keep);
                return made;
            });

            Status = string.Format(Strings.Backup_BackedUpTo, result.FileCount, (result.FileCount == 1 ? Strings.Backup_File : Strings.Backup_Files), result.Path);
            foreach (var skipped in result.Skipped)
                Skipped.Add(skipped);
            OnPropertyChanged(nameof(HasSkipped));
        }
        catch (BackupException e)
        {
            Error = e.Message;
        }
        finally
        {
            IsBusy = false;
            RefreshBackups();
        }
    }

    [RelayCommand]
    private async Task BrowseBackupAsync()
    {
        if (PickZip is not null && await PickZip() is { } path)
            OpenBackup(path);
    }

    /// <summary>Reads what a backup holds, and offers it for restoring.</summary>
    public void OpenBackup(string path)
    {
        RestoreError = null;
        IsConfirmingRestore = false;
        RestoreRows.Clear();
        RestorePath = null;
        try
        {
            var manifest = BackupArchive.Read(path);
            var current = CurrentItems();
            foreach (var item in manifest.Items)
            {
                var now = current.FirstOrDefault(c => c.Id == item.Id);
                var roots = item.Roots.Where(r => r.FileCount > 0).ToList();
                var first = roots.FirstOrDefault();
                var target = first is null ? "" : now?.Roots.FirstOrDefault(r => r.Key == first.Key)?.Path is { Length: > 0 } here ? here : first.Path;
                if (roots.Count > 1)
                    target = string.Format(Strings.Backup_AndMore, target, roots.Count - 1);
                RestoreRows.Add(new RestoreRowViewModel(item, target));
            }

            RestorePath = path;
            RestoreSummary = string.Format(Strings.Backup_Made, manifest.Created, (manifest.AppVersion.Length > 0 ? string.Format(Strings.Backup_ByJoktan, manifest.AppVersion) : ""));
        }
        catch (BackupException e)
        {
            RestoreError = e.Message;
        }
    }

    [RelayCommand]
    private void AskRestore()
    {
        RestoreError = null;
        if (!RestoreRows.Any(r => r.IsChosen))
        {
            RestoreError = Strings.Backup_NothingIsSwitchedOnToRestore;
            return;
        }

        OnPropertyChanged(nameof(ConfirmText));
        IsConfirmingRestore = true;
    }

    [RelayCommand]
    private void CancelRestore() => IsConfirmingRestore = false;

    /// <summary>
    /// Zips what is there now, leaves the restore waiting, and restarts. Nothing is replaced until the app has
    /// closed, because closing saves what it has open.
    /// </summary>
    [RelayCommand]
    private async Task ConfirmRestoreAsync()
    {
        if (RestorePath is not { } zip)
            return;

        RestoreError = null;
        IsConfirmingRestore = false;
        if (!TakeBackupFolder())
        {
            RestoreError = Error;
            return;
        }

        IsBusy = true;
        try
        {
            await _flush();
            var chosen = RestoreRows.Where(r => r.IsChosen).Select(r => r.Item.Id).ToHashSet(StringComparer.Ordinal);
            var current = CurrentItems();
            var folder = _settings.EffectiveFolder;
            var plan = await Task.Run(() =>
            {
                var manifest = BackupArchive.Read(zip);
                BackupArchive.Verify(zip);
                var safety = BackupArchive.Create(BackupArchive.NewPath(folder, BackupArchive.SafetyPrefix, DateTime.Now),
                    current.Where(i => chosen.Contains(i.Id)), _appVersion, [folder]);
                BackupArchive.Verify(safety.Path);
                return new PendingRestore
                {
                    ZipPath = zip,
                    SafetyPath = safety.Path,
                    ProcessId = Environment.ProcessId,
                    Steps = BackupPlanner.Steps(manifest, chosen, current),
                };
            });

            plan.Save();
            Status = Strings.Backup_RestartingToRestore;
            Restart();
        }
        catch (Exception e) when (e is BackupException or IOException or UnauthorizedAccessException)
        {
            RestoreError = string.Format(Strings.Backup_NothingWasChanged, e.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private List<BackupItem> CurrentItems()
        => _sources().Where(s => s.Item is not null).Select(s => s.Item!()).OfType<BackupItem>().ToList();

    private void ShowOutcome(RestoreOutcome outcome)
    {
        var safety = outcome.SafetyPath.Length > 0 ? string.Format(Strings.Backup_WhatWasThereBeforeIsIn, outcome.SafetyPath) : "";
        if (outcome.Error is { } error)
            Error = string.Format(Strings.Backup_TheRestoreDidNotFinish, error, safety);
        else
            Status = string.Format(Strings.Backup_RestoredFrom, string.Join(", ", outcome.Restored), Path.GetFileName(outcome.ZipPath), safety);
    }
}
