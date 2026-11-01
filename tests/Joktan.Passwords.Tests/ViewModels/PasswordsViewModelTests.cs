using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Joktan.Passwords.ViewModels;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Passwords.Tests.ViewModels;

[Collection(ProcessStateCollection.Name)]
public class PasswordsViewModelTests : DataFolderTest
{
    private const string Master = "correct horse";

    private readonly FakeCredentialStore _credentials = new();

    private string VaultPath => Data.At("my.vault");

    private PasswordSettings Settings(PasswordStorageKind? storage = null, string? vaultPath = null, int lockMinutes = 5) =>
        new() { Storage = storage, VaultPath = vaultPath ?? "", LockMinutes = lockMinutes };

    /// <summary>An open view model on the keychain, which is quick as it derives no key.</summary>
    private PasswordsViewModel Keychain()
    {
        var vm = new PasswordsViewModel(Settings(), _credentials) { SetupIsVault = false };
        vm.SetupCommand.Execute(null);
        Assert.True(vm.IsOpen);
        return vm;
    }

    private void MakeVault(string path, params PasswordEntry[] entries)
    {
        var vault = new VaultStorage(path);
        vault.Create(Master);
        foreach (var entry in entries)
        {
            vault.Root.Entries.Add(entry);
            vault.WritePassword(entry, "secret of " + entry.Title);
        }

        vault.Save();
        vault.Close();
    }

    private static PasswordNodeViewModel AddEntry(PasswordsViewModel vm, string title, string password = "pw", string username = "")
    {
        vm.NewEntryCommand.Execute(null);
        var node = vm.Selected!;
        vm.EditTitle = title;
        vm.EditPassword = password;
        vm.EditUsername = username;
        vm.SaveCommand.Execute(null);
        return node;
    }

    private static PasswordNodeViewModel AddFolder(PasswordsViewModel vm, string name)
    {
        vm.NewFolderCommand.Execute(null);
        var node = vm.Selected!;
        vm.EditTitle = name;
        vm.SaveCommand.Execute(null);
        return node;
    }

    // ---- Starting ----

    [Fact]
    public Task Without_a_choice_of_storage_it_starts_with_setting_up() => Headless.Run(() =>
    {
        var vm = new PasswordsViewModel(Settings(), _credentials);

        Assert.True(vm.IsSetup);
        Assert.False(vm.IsLocked);
        Assert.False(vm.IsOpen);
        Assert.False(vm.CanLeaveSetup);
        Assert.True(vm.IsVault);
        Assert.True(vm.IsKeychainAvailable);
        Assert.Equal("Create vault", vm.SetupButtonText);
        Assert.False(vm.SetupVaultExists);
        Assert.Equal(VaultStorage.DefaultPath, vm.SetupVaultPath);
    });

    [Fact]
    public Task A_chosen_vault_that_is_gone_goes_back_to_setting_up() => Headless.Run(() =>
    {
        var vm = new PasswordsViewModel(Settings(PasswordStorageKind.Vault, VaultPath), _credentials);

        Assert.True(vm.IsSetup);
    });

    [Fact]
    public Task A_vault_starts_locked_and_opens_with_its_master_password() => Headless.Run(() =>
    {
        MakeVault(VaultPath, new PasswordEntry { Title = "Mail" });
        var vm = new PasswordsViewModel(Settings(PasswordStorageKind.Vault, VaultPath), _credentials);
        Assert.True(vm.IsLocked);
        Assert.Equal("Encrypted vault file", vm.StorageText);
        Assert.Equal(VaultPath, vm.StorageLocation);

        vm.UnlockPassword = "wrong password";
        vm.UnlockCommand.Execute(null);
        Assert.True(vm.IsLocked);
        Assert.Equal("That is not the master password.", vm.Error);
        Assert.Equal("wrong password", vm.UnlockPassword);

        vm.UnlockPassword = Master;
        vm.UnlockCommand.Execute(null);

        Assert.True(vm.IsOpen);
        Assert.Null(vm.Error);
        Assert.Equal("", vm.UnlockPassword);
        Assert.Equal("Mail", vm.Selected!.Name);
    });

    [Fact]
    public Task The_keychain_opens_by_itself_and_a_damaged_list_is_reported() => Headless.Run(() =>
    {
        Data.Write(KeychainStorage.FileName, "{}");
        Assert.True(new PasswordsViewModel(Settings(PasswordStorageKind.Keychain), _credentials).IsOpen);

        Data.Write(KeychainStorage.FileName, "{ not json");
        var damaged = new PasswordsViewModel(Settings(PasswordStorageKind.Keychain), _credentials);
        Assert.False(damaged.IsOpen);
        Assert.Equal("The list of entries is damaged.", damaged.Error);
        Assert.False(damaged.IsVault);
        Assert.Equal(PasswordsViewModel.KeychainName, damaged.StorageText);
    });

    [Fact]
    public Task A_lock_time_that_is_not_offered_is_kept_as_its_own_choice() => Headless.Run(() =>
    {
        var vm = new PasswordsViewModel(Settings(lockMinutes: 7), _credentials);

        Assert.Equal(7, vm.SelectedLockChoice!.Minutes);
        Assert.Equal("After 7 minutes", vm.SelectedLockChoice.Label);
    });

    [Fact]
    public Task Changing_the_lock_time_is_saved() => Headless.Run(() =>
    {
        var settings = Settings();
        var vm = new PasswordsViewModel(settings, _credentials);

        vm.SelectedLockChoice = vm.LockChoices.First(c => c.Minutes == 30);
        Assert.Equal(30, PasswordSettings.Load().LockMinutes);

        File.Delete(Data.At("passwords-settings.json"));
        vm.SelectedLockChoice = null;
        vm.SelectedLockChoice = vm.LockChoices.First(c => c.Minutes == 30);
        Assert.Equal(30, settings.LockMinutes);
    });

    // ---- Setting up ----

    [Fact]
    public Task A_new_vault_needs_a_good_master_password_given_twice() => Headless.Run(() =>
    {
        var vm = new PasswordsViewModel(Settings(), _credentials) { SetupVaultPath = VaultPath };

        vm.NewMasterPassword = "short";
        vm.SetupCommand.Execute(null);
        Assert.Contains("at least", vm.Error);

        vm.NewMasterPassword = Master;
        vm.RepeatMasterPassword = "something else";
        vm.SetupCommand.Execute(null);
        Assert.Contains("not the same", vm.Error);
        Assert.False(File.Exists(VaultPath));

        vm.RepeatMasterPassword = Master;
        vm.SetupCommand.Execute(null);

        Assert.True(vm.IsOpen);
        Assert.True(File.Exists(VaultPath));
        Assert.Equal("", vm.NewMasterPassword);
        Assert.Equal("", vm.RepeatMasterPassword);
        Assert.True(vm.IsEmpty);
    });

    [Fact]
    public Task A_vault_that_cannot_be_made_is_reported() => Headless.Run(() =>
    {
        var file = Data.Write("a-file", "");
        var vm = new PasswordsViewModel(Settings(), _credentials)
        {
            SetupVaultPath = Path.Combine(file, "below.vault"),
            NewMasterPassword = Master,
            RepeatMasterPassword = Master,
        };

        vm.SetupCommand.Execute(null);

        Assert.False(vm.IsOpen);
        Assert.NotNull(vm.Error);
    });

    [Fact]
    public Task A_vault_already_there_is_opened_with_its_own_password() => Headless.Run(() =>
    {
        MakeVault(VaultPath);
        var vm = new PasswordsViewModel(Settings(), _credentials) { SetupVaultPath = VaultPath };
        Assert.True(vm.SetupVaultExists);
        Assert.Equal("Open vault", vm.SetupButtonText);

        vm.SetupCommand.Execute(null);

        Assert.True(vm.IsLocked);
        Assert.Equal(VaultPath, PasswordSettings.Load().VaultPath);
    });

    [Fact]
    public Task The_keychain_can_be_chosen_and_says_so_when_there_is_none() => Headless.Run(() =>
    {
        var vm = new PasswordsViewModel(Settings(), _credentials);
        vm.SetupIsKeychain = true;
        Assert.False(vm.SetupIsVault);
        Assert.True(vm.SetupIsKeychain);
        Assert.Equal("Use keychain", vm.SetupButtonText);
        Assert.False(vm.SetupVaultExists);
        vm.SetupCommand.Execute(null);
        Assert.True(vm.IsOpen);
        Assert.False(vm.IsVault);

        File.Delete(Data.At(KeychainStorage.FileName));
        var none = new PasswordsViewModel(Settings(), new FakeCredentialStore(isAvailable: false)) { SetupIsKeychain = true };
        Assert.False(none.IsKeychainAvailable);
        none.SetupCommand.Execute(null);
        Assert.False(none.IsOpen);
        Assert.Contains("no system keychain", none.Error);
    });

    [Fact]
    public Task A_damaged_keychain_list_keeps_setup_from_finishing() => Headless.Run(() =>
    {
        Data.Write(KeychainStorage.FileName, "{ not json");
        var vm = new PasswordsViewModel(Settings(), _credentials) { SetupIsKeychain = true, NewMasterPassword = "typed" };

        vm.SetupCommand.Execute(null);

        Assert.False(vm.IsOpen);
        Assert.Equal("The list of entries is damaged.", vm.Error);
        Assert.Equal("typed", vm.NewMasterPassword);
    });

    [Fact]
    public Task Browsing_uses_the_file_picked_if_any() => Headless.Run(async () =>
    {
        var vm = new PasswordsViewModel(Settings(), _credentials);
        var start = vm.SetupVaultPath;

        await vm.BrowseVaultCommand.ExecuteAsync(null);
        Assert.Equal(start, vm.SetupVaultPath);

        vm.PickVaultFile = () => Task.FromResult<string?>(null);
        await vm.BrowseVaultCommand.ExecuteAsync(null);
        Assert.Equal(start, vm.SetupVaultPath);

        vm.PickVaultFile = () => Task.FromResult<string?>("picked.vault");
        await vm.BrowseVaultCommand.ExecuteAsync(null);
        Assert.Equal("picked.vault", vm.SetupVaultPath);
        Assert.Equal("picked.vault", vm.TargetVaultPath);
    });

    [Fact]
    public Task Choosing_storage_again_starts_from_what_is_in_use_and_can_be_left() => Headless.Run(() =>
    {
        var vm = Keychain();
        vm.Error = "old";

        vm.ChooseStorageCommand.Execute(null);

        Assert.True(vm.IsSetup);
        Assert.Null(vm.Error);
        Assert.True(vm.SetupIsKeychain);
        Assert.Equal(VaultStorage.DefaultPath, vm.SetupVaultPath);
        Assert.True(vm.CanLeaveSetup);

        vm.NewMasterPassword = "typed";
        vm.LeaveSetupCommand.Execute(null);
        Assert.True(vm.IsOpen);
        Assert.Equal("", vm.NewMasterPassword);
    });

    [Fact]
    public Task Leaving_setup_returns_to_a_locked_vault_or_opens_the_keychain() => Headless.Run(() =>
    {
        MakeVault(VaultPath);
        var vault = new PasswordsViewModel(Settings(PasswordStorageKind.Vault, VaultPath), _credentials);
        vault.ChooseStorageCommand.Execute(null);
        Assert.Equal(VaultPath, vault.SetupVaultPath);
        Assert.True(vault.SetupIsVault);

        vault.LeaveSetupCommand.Execute(null);
        Assert.True(vault.IsLocked);

        Data.Write(KeychainStorage.FileName, "{}");
        var keychain = new PasswordsViewModel(Settings(PasswordStorageKind.Keychain), _credentials);
        keychain.SaveState();
        keychain.State = PasswordsState.Setup;

        keychain.LeaveSetupCommand.Execute(null);
        Assert.True(keychain.IsOpen);
    });

    // ---- Locking ----

    [Fact]
    public Task Locking_closes_a_vault_and_forgets_what_was_shown() => Headless.Run(() =>
    {
        MakeVault(VaultPath, new PasswordEntry { Title = "Mail" });
        var vm = new PasswordsViewModel(Settings(PasswordStorageKind.Vault, VaultPath), _credentials) { UnlockPassword = Master };
        vm.UnlockCommand.Execute(null);
        vm.EditCommand.Execute(null);
        vm.EditTitle = "Mail 2";
        vm.Message = "old";

        vm.LockCommand.Execute(null);

        Assert.True(vm.IsLocked);
        Assert.Empty(vm.Roots);
        Assert.Null(vm.Selected);
        Assert.Null(vm.Message);
        Assert.Equal("", vm.EditPassword);

        vm.UnlockPassword = Master;
        vm.UnlockCommand.Execute(null);
        Assert.Equal("Mail 2", vm.Selected!.Name);
    });

    [Fact]
    public Task The_keychain_does_not_lock() => Headless.Run(() =>
    {
        var vm = Keychain();

        vm.LockCommand.Execute(null);

        Assert.True(vm.IsOpen);
    });

    private static void CheckIdle(PasswordsViewModel vm) =>
        typeof(PasswordsViewModel).GetMethod("LockIfIdle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(vm, null);

    private static void Idle(PasswordsViewModel vm, TimeSpan time) =>
        typeof(PasswordsViewModel).GetField("_lastActivity", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(vm, DateTime.UtcNow - time);

    [Fact]
    public Task A_vault_locks_only_after_being_unused_for_long_enough() => Headless.Run(() =>
    {
        MakeVault(VaultPath);
        var vm = new PasswordsViewModel(Settings(PasswordStorageKind.Vault, VaultPath, lockMinutes: 5), _credentials) { UnlockPassword = Master };
        vm.UnlockCommand.Execute(null);

        Idle(vm, TimeSpan.FromMinutes(4));
        CheckIdle(vm);
        Assert.True(vm.IsOpen);

        Idle(vm, TimeSpan.FromMinutes(6));
        vm.Touch();
        CheckIdle(vm);
        Assert.True(vm.IsOpen);

        Idle(vm, TimeSpan.FromMinutes(6));
        CheckIdle(vm);
        Assert.True(vm.IsLocked);
    });

    [Fact]
    public Task Never_means_never() => Headless.Run(() =>
    {
        MakeVault(VaultPath);
        var vm = new PasswordsViewModel(Settings(PasswordStorageKind.Vault, VaultPath, lockMinutes: 0), _credentials) { UnlockPassword = Master };
        vm.UnlockCommand.Execute(null);

        Idle(vm, TimeSpan.FromDays(1));
        CheckIdle(vm);

        Assert.True(vm.IsOpen);
    });

    [Fact]
    public Task Saving_state_keeps_an_entry_being_edited() => Headless.Run(() =>
    {
        var vm = Keychain();
        var node = AddEntry(vm, "Mail");
        vm.EditCommand.Execute(null);
        vm.EditTitle = "Mail 2";

        vm.SaveState();

        Assert.False(vm.IsEditing);
        Assert.Equal("Mail 2", node.Entry!.Title);
        Assert.Contains("Mail 2", File.ReadAllText(Data.At(KeychainStorage.FileName)));

        // Without anything open there is nothing to keep.
        new PasswordsViewModel(Settings(), _credentials).SaveState();
    });

    // ---- The tree and selection ----

    [Fact]
    public Task The_selection_tells_what_is_selected() => Headless.Run(() =>
    {
        var vm = Keychain();
        Assert.False(vm.HasSelection);
        Assert.Equal("", vm.ModifiedText);
        Assert.Equal("", vm.FolderSummary);
        Assert.Equal("", vm.EntryUsername);
        Assert.Equal("", vm.DeleteText);
        Assert.Null(vm.SelectedFolder);

        var folder = AddFolder(vm, "Work");
        Assert.True(vm.IsFolderSelected);
        Assert.False(vm.IsEntrySelected);
        Assert.Same(folder, vm.SelectedFolder);
        Assert.Equal("No entries", vm.FolderSummary);
        Assert.Equal("Folder saved.", vm.Message);

        AddEntry(vm, "One", username: "me");
        Assert.True(vm.IsEntrySelected);
        Assert.True(vm.HasUsername);
        Assert.False(vm.HasUrl);
        Assert.False(vm.HasNotes);
        Assert.StartsWith("Created ", vm.ModifiedText);
        Assert.Equal("Entry saved.", vm.Message);

        vm.Selected = folder;
        Assert.Equal("1 entry", vm.FolderSummary);

        AddEntry(vm, "Two");
        vm.Selected = folder;
        Assert.Equal("2 entries", vm.FolderSummary);
        Assert.Equal("Work", folder.Name);
    });

    [Fact]
    public Task The_tree_cannot_clear_the_selection_of_a_node_still_there() => Headless.Run(() =>
    {
        var vm = Keychain();
        var one = AddEntry(vm, "One");
        var two = AddEntry(vm, "Two");

        vm.TreeSelection = null;
        Assert.Same(two, vm.TreeSelection);

        vm.TreeSelection = one;
        Assert.Same(one, vm.Selected);

        vm.Selected = null;
        vm.TreeSelection = null;
        Assert.Null(vm.Selected);
    });

    [Fact]
    public Task Selecting_opens_the_folders_above_and_ends_revealing() => Headless.Run(() =>
    {
        var vm = Keychain();
        var folder = AddFolder(vm, "Work");
        var inner = AddFolder(vm, "Inner");
        var entry = AddEntry(vm, "Deep", password: "hidden");
        vm.ToggleRevealCommand.Execute(null);
        Assert.Equal("hidden", vm.RevealedPassword);
        Assert.True(vm.IsRevealed);
        folder.IsExpanded = false;
        inner.IsExpanded = false;
        vm.Selected = folder;
        Assert.False(vm.IsRevealed);
        Assert.Equal("", vm.RevealedPassword);

        vm.Selected = entry;

        Assert.True(inner.IsExpanded);
        Assert.True(folder.IsExpanded);
        Assert.Equal("Work / Inner", entry.Path);
    });

    [Fact]
    public Task Leaving_an_entry_being_edited_keeps_its_changes() => Headless.Run(() =>
    {
        var vm = Keychain();
        var one = AddEntry(vm, "One");
        var two = AddEntry(vm, "Two");
        vm.Selected = one;
        vm.EditCommand.Execute(null);
        vm.EditTitle = "Changed";

        vm.Selected = two;

        Assert.False(vm.IsEditing);
        Assert.Equal("Changed", one.Entry!.Title);
    });

    // ---- Search ----

    [Fact]
    public Task Search_lists_matching_entries_by_name() => Headless.Run(() =>
    {
        var vm = Keychain();
        AddEntry(vm, "bravo mail");
        AddEntry(vm, "alpha mail");
        AddEntry(vm, "other");

        vm.SearchText = "  MAIL ";
        Assert.True(vm.IsSearching);
        Assert.False(vm.HasNoResults);
        Assert.Equal(["alpha mail", "bravo mail"], vm.SearchResults.Select(n => n.Name));

        vm.SearchText = "nothing like it";
        Assert.True(vm.HasNoResults);
        Assert.Empty(vm.SearchResults);

        vm.SearchText = "";
        Assert.False(vm.IsSearching);
        Assert.False(vm.HasNoResults);
    });

    [Fact]
    public Task Saving_and_deleting_keep_the_search_results_right() => Headless.Run(() =>
    {
        var vm = Keychain();
        var node = AddEntry(vm, "mail");
        vm.SearchText = "mail";
        Assert.Single(vm.SearchResults);
        vm.Selected = node;

        vm.ConfirmDeleteCommand.Execute(null);

        Assert.Empty(vm.SearchResults);
        Assert.True(vm.HasNoResults);
    });

    // ---- Viewing ----

    [Fact]
    public Task Reveal_shows_and_hides_the_password() => Headless.Run(() =>
    {
        var vm = Keychain();
        vm.ToggleRevealCommand.Execute(null);
        Assert.False(vm.IsRevealed);

        AddEntry(vm, "One", password: "hidden");

        vm.ToggleRevealCommand.Execute(null);
        Assert.Equal("hidden", vm.RevealedPassword);

        vm.ToggleRevealCommand.Execute(null);
        Assert.False(vm.IsRevealed);
        Assert.Equal("", vm.RevealedPassword);
    });

    [Fact]
    public Task Copying_gives_the_view_the_text_and_says_what_was_copied() => Headless.Run(async () =>
    {
        var vm = Keychain();
        var copied = new List<string>();

        // Nothing is selected, and then nothing can copy.
        vm.CopyText = t =>
        {
            copied.Add(t);
            return Task.CompletedTask;
        };
        await vm.CopyPasswordCommand.ExecuteAsync(null);
        await vm.CopyUsernameCommand.ExecuteAsync(null);
        Assert.Empty(copied);

        vm.NewEntryCommand.Execute(null);
        vm.EditTitle = "One";
        vm.EditPassword = "hidden";
        vm.EditUsername = "me";
        vm.EditUrl = "example.com";
        vm.SaveCommand.Execute(null);

        await vm.CopyUsernameCommand.ExecuteAsync(null);
        Assert.Equal("Username copied.", vm.Message);
        await vm.CopyPasswordCommand.ExecuteAsync(null);
        Assert.Contains("Password copied", vm.Message);
        await vm.CopyUrlCommand.ExecuteAsync(null);
        Assert.Equal("Website copied.", vm.Message);
        Assert.Equal(["me", "hidden", "example.com"], copied);

        vm.CopyText = null;
        vm.Message = null;
        await vm.CopyUrlCommand.ExecuteAsync(null);
        Assert.Null(vm.Message);
    });

    [Theory]
    [InlineData("example.com", "https://example.com/", null)]
    [InlineData("  http://example.com/a ", "http://example.com/a", null)]
    [InlineData("ftp://example.com", null, "The website is not a web address that can be opened.")]
    [InlineData("https://", null, "The website is not a web address that can be opened.")]
    [InlineData("   ", null, null)]
    public Task Only_web_addresses_are_opened(string url, string? opened, string? error) => Headless.Run(async () =>
    {
        var vm = Keychain();
        vm.NewEntryCommand.Execute(null);
        vm.EditTitle = "One";
        vm.EditUrl = url;
        vm.SaveCommand.Execute(null);
        Uri? asked = null;
        vm.OpenUri = uri =>
        {
            asked = uri;
            return Task.CompletedTask;
        };

        await vm.OpenUrlCommand.ExecuteAsync(null);

        Assert.Equal(opened, asked?.ToString());
        Assert.Equal(error, vm.Error);
    });

    [Fact]
    public Task Opening_needs_the_view() => Headless.Run(async () =>
    {
        var vm = Keychain();
        vm.NewEntryCommand.Execute(null);
        vm.EditTitle = "One";
        vm.EditUrl = "example.com";
        vm.SaveCommand.Execute(null);

        await vm.OpenUrlCommand.ExecuteAsync(null);

        Assert.Null(vm.Error);
    });

    // ---- Editing ----

    [Fact]
    public Task Editing_starts_from_the_entry_and_offers_every_other_folder() => Headless.Run(() =>
    {
        var vm = Keychain();
        vm.EditCommand.Execute(null);
        Assert.False(vm.IsEditing);

        var work = AddFolder(vm, "Work");
        AddFolder(vm, "Inner");
        var entry = AddEntry(vm, "One", password: "hidden", username: "me");
        vm.ShowEditPassword = true;

        vm.EditCommand.Execute(null);

        Assert.True(vm.IsEditing);
        Assert.False(vm.IsViewing);
        Assert.Equal("Edit One", vm.EditHeading);
        Assert.Equal("One", vm.EditTitle);
        Assert.Equal("me", vm.EditUsername);
        Assert.Equal("hidden", vm.EditPassword);
        Assert.False(vm.ShowEditPassword);
        Assert.Equal(["(Top level)", "Work", "    Inner"], vm.FolderChoices.Select(c => c.Label));
        Assert.Same(entry.Parent, vm.EditFolder!.Node);

        vm.CancelCommand.Execute(null);
        vm.Selected = work;
        vm.EditCommand.Execute(null);

        // A folder cannot go inside itself.
        Assert.Equal(["(Top level)"], vm.FolderChoices.Select(c => c.Label));
        Assert.Equal("", vm.EditPassword);
    });

    [Fact]
    public Task A_changed_password_is_written_and_an_unchanged_one_is_not() => Headless.Run(() =>
    {
        var vm = Keychain();
        var node = AddEntry(vm, "One", password: "first");
        Assert.Equal("first", _credentials.Saved[node.Entry!.Id]);

        vm.EditCommand.Execute(null);
        _credentials.Saved[node.Entry.Id] = "first";
        _credentials.FailWrites = true;
        vm.EditTitle = "Renamed";
        vm.SaveCommand.Execute(null);
        Assert.Null(vm.Error);
        Assert.Equal("Renamed", node.Entry.Title);

        vm.EditCommand.Execute(null);
        vm.EditPassword = "second";
        vm.Message = null;
        vm.SaveCommand.Execute(null);

        Assert.Contains("could not be saved", vm.Error);
        Assert.Null(vm.Message);
        Assert.False(vm.IsEditing);
    });

    [Fact]
    public Task Saving_with_nothing_being_edited_says_nothing_wrong() => Headless.Run(() =>
    {
        var vm = Keychain();

        vm.SaveCommand.Execute(null);
        vm.CancelCommand.Execute(null);

        Assert.Null(vm.Error);
    });

    [Fact]
    public Task A_generated_password_fills_the_field() => Headless.Run(() =>
    {
        var vm = Keychain();
        vm.GenerateLength = 24;
        vm.GenerateSymbols = false;

        vm.GenerateCommand.Execute(null);

        Assert.Equal(24, vm.EditPassword.Length);
        Assert.All(vm.EditPassword, c => Assert.True(char.IsLetterOrDigit(c)));
    });

    [Fact]
    public Task New_things_go_in_the_selected_folder_or_the_folder_of_the_selected_entry() => Headless.Run(() =>
    {
        var vm = Keychain();
        var folder = AddFolder(vm, "Work");
        Assert.Null(folder.Parent!.Parent);

        vm.NewEntryCommand.Execute(null);
        Assert.Equal("New entry", vm.EditHeading);
        Assert.Same(folder, vm.Selected!.Parent);
        vm.EditTitle = "One";
        vm.SaveCommand.Execute(null);

        vm.NewFolderCommand.Execute(null);
        Assert.Equal("New folder", vm.EditHeading);
        Assert.Same(folder, vm.Selected!.Parent);
        Assert.True(folder.IsExpanded);
        vm.SaveCommand.Execute(null);
        Assert.Equal(2, folder.Children.Count);
    });

    [Fact]
    public Task Adding_clears_the_search_and_saves_what_was_being_edited() => Headless.Run(() =>
    {
        var vm = Keychain();
        var one = AddEntry(vm, "One");
        vm.EditCommand.Execute(null);
        vm.EditTitle = "Changed";
        vm.SearchText = "x";

        vm.NewEntryCommand.Execute(null);

        Assert.Equal("Changed", one.Entry!.Title);
        Assert.Equal("", vm.SearchText);
    });

    [Fact]
    public Task Cancelling_a_new_entry_removes_it_and_cancelling_an_edit_keeps_the_old_values() => Headless.Run(() =>
    {
        var vm = Keychain();
        var folder = AddFolder(vm, "Work");

        vm.NewEntryCommand.Execute(null);
        vm.CancelCommand.Execute(null);
        Assert.Same(folder, vm.Selected);
        Assert.Empty(folder.Children);

        vm.Selected = null;
        vm.NewEntryCommand.Execute(null);
        vm.CancelCommand.Execute(null);
        Assert.Same(folder, vm.Selected);

        vm.EditCommand.Execute(null);
        vm.EditTitle = "Other";
        vm.CancelCommand.Execute(null);
        Assert.Equal("Work", folder.Name);
    });

    [Fact]
    public Task A_new_entry_left_empty_is_not_kept() => Headless.Run(() =>
    {
        var vm = Keychain();
        var one = AddEntry(vm, "One");

        vm.NewEntryCommand.Execute(null);
        vm.Message = null;
        vm.SaveCommand.Execute(null);

        Assert.Equal([one], vm.Roots);
        Assert.Null(vm.Message);
    });

    [Fact]
    public Task An_entry_or_folder_moves_to_the_folder_chosen() => Headless.Run(() =>
    {
        var vm = Keychain();
        var work = AddFolder(vm, "Work");
        vm.Selected = null;
        var entry = AddEntry(vm, "One");
        vm.Selected = null;
        var other = AddFolder(vm, "Other");

        vm.Selected = entry;
        vm.EditCommand.Execute(null);
        vm.EditFolder = vm.FolderChoices.First(c => c.Node == work);
        vm.SaveCommand.Execute(null);
        Assert.Same(work, entry.Parent);
        Assert.Contains(entry.Entry!, work.Folder!.Entries);
        Assert.Same(entry, vm.Selected);

        vm.Selected = other;
        vm.EditCommand.Execute(null);
        vm.EditFolder = vm.FolderChoices.First(c => c.Node == work);
        vm.SaveCommand.Execute(null);
        Assert.Same(work, other.Parent);
        Assert.Contains(other.Folder!, work.Folder!.Folders);
        Assert.Single(vm.Roots);
    });

    // ---- Deleting ----

    [Fact]
    public Task Deleting_says_what_goes() => Headless.Run(() =>
    {
        var vm = Keychain();
        var folder = AddFolder(vm, "Work");
        Assert.Equal("Delete the folder Work? This cannot be undone.", vm.DeleteText);

        AddEntry(vm, "One");
        Assert.Equal("Delete One and its password? This cannot be undone.", vm.DeleteText);
        vm.Selected = folder;
        Assert.Equal("Delete the folder Work and the 1 entry in it? This cannot be undone.", vm.DeleteText);

        AddEntry(vm, "Two");
        vm.Selected = folder;
        Assert.Equal("Delete the folder Work and the 2 entries in it? This cannot be undone.", vm.DeleteText);

        AddFolder(vm, "Inner");
        vm.Selected = folder;
        Assert.Equal("Delete the folder Work and the 2 entries and 1 folder in it? This cannot be undone.", vm.DeleteText);

        vm.Selected = null;
        var empty = AddFolder(vm, "Empty");
        AddFolder(vm, "A");
        vm.Selected = empty;
        Assert.Equal("Delete the folder Empty and the 1 folder in it? This cannot be undone.", vm.DeleteText);
        AddFolder(vm, "B");
        vm.Selected = empty;
        Assert.Equal("Delete the folder Empty and the 2 folders in it? This cannot be undone.", vm.DeleteText);
    });

    [Fact]
    public Task Delete_asks_first_and_can_be_cancelled() => Headless.Run(() =>
    {
        var vm = Keychain();
        vm.DeleteCommand.Execute(null);
        Assert.False(vm.IsConfirmingDelete);

        AddEntry(vm, "One");
        vm.DeleteCommand.Execute(null);
        Assert.True(vm.IsConfirmingDelete);

        vm.CancelDeleteCommand.Execute(null);
        Assert.False(vm.IsConfirmingDelete);
        Assert.Single(vm.Roots);
    });

    [Fact]
    public Task Deleting_removes_the_passwords_and_selects_what_is_next() => Headless.Run(() =>
    {
        var vm = Keychain();
        vm.ConfirmDeleteCommand.Execute(null);

        var folder = AddFolder(vm, "Work");
        var a = AddEntry(vm, "a");
        vm.Selected = folder;
        var b = AddEntry(vm, "b");
        vm.Selected = folder;
        var c = AddEntry(vm, "c");

        vm.Selected = a;
        vm.ConfirmDeleteCommand.Execute(null);
        Assert.Same(b, vm.Selected);
        Assert.Equal("Entry deleted.", vm.Message);
        Assert.False(_credentials.Saved.ContainsKey(a.Entry!.Id));

        vm.Selected = c;
        vm.ConfirmDeleteCommand.Execute(null);
        Assert.Same(b, vm.Selected);

        vm.ConfirmDeleteCommand.Execute(null);
        Assert.Same(folder, vm.Selected);

        vm.ConfirmDeleteCommand.Execute(null);
        Assert.Equal("Folder deleted.", vm.Message);
        Assert.Null(vm.Selected);
        Assert.True(vm.IsEmpty);
    });

    [Fact]
    public Task A_delete_that_cannot_be_saved_keeps_the_password() => Headless.Run(() =>
    {
        var vm = Keychain();
        var node = AddEntry(vm, "One");
        var index = Data.At(KeychainStorage.FileName);
        File.Delete(index);
        Directory.CreateDirectory(index);

        vm.ConfirmDeleteCommand.Execute(null);

        Assert.NotNull(vm.Error);
        Assert.True(_credentials.Saved.ContainsKey(node.Entry!.Id));
    });

    // ---- Moving storage and the master password ----

    [Fact]
    public Task The_storage_band_starts_from_the_other_kind() => Headless.Run(() =>
    {
        var vm = Keychain();
        vm.Error = "old";

        vm.ToggleStorageCommand.Execute(null);

        Assert.True(vm.IsChangingStorage);
        Assert.Null(vm.Error);
        Assert.True(vm.TargetIsVault);
        Assert.False(vm.TargetIsKeychain);
        Assert.Equal(VaultStorage.DefaultPath, vm.TargetVaultPath);
        Assert.Contains("new vault", vm.TargetPasswordLabel);

        vm.TargetIsKeychain = true;
        Assert.False(vm.TargetIsVault);
        Assert.False(vm.TargetVaultExists);
    });

    [Fact]
    public Task Moving_to_the_keychain_when_there_already_says_so() => Headless.Run(() =>
    {
        var vm = Keychain();
        vm.TargetIsKeychain = true;

        vm.MoveStorageCommand.Execute(null);

        Assert.Equal("The entries are already in the keychain.", vm.Error);
    });

    [Fact]
    public Task A_new_target_vault_needs_a_good_master_password() => Headless.Run(() =>
    {
        var vm = Keychain();
        vm.TargetIsVault = true;
        vm.TargetVaultPath = VaultPath;
        vm.TargetMasterPassword = "short";

        vm.MoveStorageCommand.Execute(null);

        Assert.Contains("at least", vm.Error);
        Assert.False(File.Exists(VaultPath));
    });

    [Fact]
    public Task A_target_vault_that_cannot_be_made_is_reported() => Headless.Run(() =>
    {
        var file = Data.Write("a-file", "");
        var vm = Keychain();
        vm.TargetIsVault = true;
        vm.TargetVaultPath = Path.Combine(file, "below.vault");
        vm.TargetMasterPassword = vm.TargetRepeatPassword = Master;

        vm.MoveStorageCommand.Execute(null);

        Assert.NotNull(vm.Error);
        Assert.False(vm.IsVault);
    });

    [Fact]
    public Task Entries_move_from_the_keychain_to_a_new_vault_with_their_passwords() => Headless.Run(() =>
    {
        var vm = Keychain();
        var node = AddEntry(vm, "One", password: "hidden");
        var id = node.Entry!.Id;
        vm.IsChangingStorage = true;
        vm.TargetIsVault = true;
        vm.TargetVaultPath = VaultPath;
        vm.TargetMasterPassword = vm.TargetRepeatPassword = Master;

        vm.MoveStorageCommand.Execute(null);

        Assert.Null(vm.Error);
        Assert.True(vm.IsVault);
        Assert.True(vm.IsOpen);
        Assert.False(vm.IsChangingStorage);
        Assert.Contains("moved", vm.Message);
        Assert.Equal("", vm.TargetMasterPassword);
        Assert.False(File.Exists(Data.At(KeychainStorage.FileName)));
        Assert.False(_credentials.Saved.ContainsKey(id));
        Assert.Equal(PasswordStorageKind.Vault, PasswordSettings.Load().Storage);
        Assert.Equal(VaultPath, PasswordSettings.Load().VaultPath);

        vm.ToggleRevealCommand.Execute(null);
        Assert.Equal("hidden", vm.RevealedPassword);
    });

    [Fact]
    public Task Entries_are_added_to_a_vault_already_there_given_its_password() => Headless.Run(() =>
    {
        MakeVault(VaultPath, new PasswordEntry { Title = "Already there" });
        var vm = Keychain();
        AddEntry(vm, "One");
        vm.TargetIsVault = true;
        vm.TargetVaultPath = VaultPath;
        Assert.True(vm.TargetVaultExists);
        Assert.Contains("already there", vm.TargetPasswordLabel);

        vm.TargetMasterPassword = "wrong password";
        vm.MoveStorageCommand.Execute(null);
        Assert.Equal("That is not the master password of the vault there.", vm.Error);
        Assert.False(vm.IsVault);

        vm.TargetMasterPassword = Master;
        vm.MoveStorageCommand.Execute(null);
        Assert.True(vm.IsVault);
        Assert.Equal(["Already there", "One"], vm.Roots.Select(n => n.Name));
    });

    [Fact]
    public Task A_vault_cannot_move_onto_itself() => Headless.Run(() =>
    {
        MakeVault(VaultPath);
        var vm = new PasswordsViewModel(Settings(PasswordStorageKind.Vault, VaultPath), _credentials) { UnlockPassword = Master };
        vm.UnlockCommand.Execute(null);
        vm.TargetIsVault = true;
        vm.TargetVaultPath = VaultPath;

        vm.MoveStorageCommand.Execute(null);

        Assert.Equal("The entries are already in this vault.", vm.Error);
    });

    private PasswordsViewModel OpenVault(FakeCredentialStore credentials, params PasswordEntry[] entries)
    {
        MakeVault(VaultPath, entries);
        var vm = new PasswordsViewModel(Settings(PasswordStorageKind.Vault, VaultPath), credentials) { UnlockPassword = Master };
        vm.UnlockCommand.Execute(null);
        return vm;
    }

    [Fact]
    public Task Entries_move_from_a_vault_to_the_keychain_and_the_vault_goes() => Headless.Run(() =>
    {
        var vm = OpenVault(_credentials, new PasswordEntry { Title = "Mail" });
        vm.TargetIsKeychain = true;

        vm.MoveStorageCommand.Execute(null);

        Assert.False(vm.IsVault);
        Assert.False(File.Exists(VaultPath));
        Assert.Contains("moved", vm.Message);
        Assert.Equal("secret of Mail", _credentials.Saved[vm.Selected!.Entry!.Id]);
    });

    [Fact]
    public Task A_keychain_list_already_there_takes_the_entries_too() => Headless.Run(() =>
    {
        var other = new PasswordEntry { Title = "Other" };
        Data.Write(KeychainStorage.FileName, System.Text.Json.JsonSerializer.Serialize(new PasswordFolder { Entries = [other] }));
        var vm = OpenVault(_credentials, new PasswordEntry { Title = "Mail" });
        vm.TargetIsKeychain = true;

        vm.MoveStorageCommand.Execute(null);

        Assert.Equal(["Mail", "Other"], vm.Roots.Select(n => n.Name));
    });

    [Fact]
    public Task No_keychain_means_nothing_moves() => Headless.Run(() =>
    {
        var vm = OpenVault(new FakeCredentialStore(isAvailable: false));
        vm.TargetIsKeychain = true;

        vm.MoveStorageCommand.Execute(null);

        Assert.Contains("no system keychain", vm.Error);
        Assert.True(vm.IsVault);
    });

    [Fact]
    public Task A_damaged_keychain_list_keeps_the_entries_where_they_are() => Headless.Run(() =>
    {
        Data.Write(KeychainStorage.FileName, "{ not json");
        var vm = OpenVault(_credentials, new PasswordEntry { Title = "Mail" });
        vm.TargetIsKeychain = true;

        vm.MoveStorageCommand.Execute(null);

        Assert.Equal("The list of entries is damaged.", vm.Error);
        Assert.True(File.Exists(VaultPath));
    });

    [Fact]
    public Task A_copy_that_fails_removes_a_new_target_and_keeps_the_source() => Headless.Run(() =>
    {
        var credentials = new FakeCredentialStore { FailWrites = true };
        var vm = OpenVault(credentials, new PasswordEntry { Title = "Mail" });
        vm.TargetIsKeychain = true;

        vm.MoveStorageCommand.Execute(null);

        Assert.Contains("could not be saved", vm.Error);
        Assert.True(vm.IsVault);
        Assert.True(File.Exists(VaultPath));
        Assert.False(File.Exists(Data.At(KeychainStorage.FileName)));
    });

    [Fact]
    public Task A_copy_that_fails_leaves_a_target_already_there() => Headless.Run(() =>
    {
        Data.Write(KeychainStorage.FileName, "{}");
        var credentials = new FakeCredentialStore { FailWrites = true };
        var vm = OpenVault(credentials, new PasswordEntry { Title = "Mail" });
        vm.TargetIsKeychain = true;

        vm.MoveStorageCommand.Execute(null);

        Assert.Contains("could not be saved", vm.Error);
        Assert.Equal("{}", File.ReadAllText(Data.At(KeychainStorage.FileName)));
    });

    [Fact]
    public Task The_master_password_changes_only_with_the_current_one() => Headless.Run(() =>
    {
        var vm = OpenVault(_credentials);
        vm.IsChangingStorage = true;

        vm.CurrentMasterPassword = "wrong password";
        vm.ChangeMasterPasswordCommand.Execute(null);
        Assert.Contains("current master password", vm.Error);

        vm.CurrentMasterPassword = Master;
        vm.TargetMasterPassword = "short";
        vm.ChangeMasterPasswordCommand.Execute(null);
        Assert.Contains("at least", vm.Error);

        vm.TargetMasterPassword = vm.TargetRepeatPassword = "a new master password";
        vm.ChangeMasterPasswordCommand.Execute(null);

        Assert.Null(vm.Error);
        Assert.False(vm.IsChangingStorage);
        Assert.Equal("The master password was changed.", vm.Message);
        Assert.Equal("", vm.CurrentMasterPassword);
        Assert.True(new VaultStorage(VaultPath).Open("a new master password"));
    });

    [Fact]
    public Task The_keychain_has_no_master_password_to_change() => Headless.Run(() =>
    {
        var vm = Keychain();

        vm.ChangeMasterPasswordCommand.Execute(null);

        Assert.Null(vm.Message);
        Assert.Null(vm.Error);
    });

    [Fact]
    public Task A_blank_or_home_relative_vault_path_is_made_whole() => Headless.Run(() =>
    {
        var vm = new PasswordsViewModel(Settings(), _credentials) { SetupVaultPath = "   " };
        Assert.Equal("Create vault", vm.SetupButtonText);

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        MakeVault(VaultPath);
        vm.SetupVaultPath = "~/" + Path.GetRelativePath(home, VaultPath);
        Assert.True(vm.SetupVaultExists);
    });
}
