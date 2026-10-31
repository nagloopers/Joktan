using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Joktan.Settings.Backup;
using Joktan.Settings.ViewModels;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Settings.Tests.ViewModels;

[Collection(ProcessStateCollection.Name)]
public class BackupViewModelTests : DataFolderTest
{
    private readonly TempFolder _temp = new();
    private readonly string _notes;
    private readonly string _keys;
    private readonly string _backups;
    private readonly List<string> _changes = [];
    private readonly List<(string Name, string Folder)> _clients = [];
    private int _flushes;
    private int _restarts;

    public BackupViewModelTests()
    {
        _notes = Path.GetDirectoryName(_temp.Write("notes/a.md", "original"))!;
        _keys = Path.GetDirectoryName(_temp.Write("keys/private.key", "secret"))!;
        _backups = _temp.At("backups");
    }

    private string Note => Path.Combine(_notes, "a.md");

    private IReadOnlyList<FolderSource> Sources() =>
    [
        .. _clients.Select(client => new FolderSource
        {
            Id = $"client:{client.Name}",
            Name = client.Name,
            IsMessageFolder = true,
            AllowNone = true,
            CurrentPath = () => _clients.FirstOrDefault(c => c.Name == client.Name).Folder ?? "",
            Change = _ => Task.FromResult(new FolderChange(null)),
            Item = () => client.Folder.Length == 0 ? null : new BackupItem($"client:{client.Name}", client.Name, [new BackupRoot("folder", client.Folder)]),
        }),
        new FolderSource
        {
            Id = "notes",
            Name = "Notes",
            CurrentPath = () => _notes,
            DefaultPath = "/default/notes",
            Change = path =>
            {
                _changes.Add(path);
                // By name alone, as the temporary folder above it has a GUID that can hold these letters.
                return Task.FromResult(Path.GetFileName(path) == "bad" ? new FolderChange("Could not use it") : new FolderChange(null, "Done"));
            },
            Item = () => new BackupItem("notes", "Notes", [new BackupRoot("folder", _notes)]),
        },
        new FolderSource
        {
            Id = "keys",
            Name = "Keys",
            CurrentPath = () => _keys,
            IncludedByDefault = false,
            Warning = "Holds private keys",
            Item = () => new BackupItem("keys", "Keys", [new BackupRoot("folder", _keys)]),
        },
        new FolderSource { Id = "none", Name = "No backup", CurrentPath = () => "/somewhere", AllowNone = true, Change = _ => Task.FromResult(new FolderChange(null)) },
    ];

    private BackupViewModel Page(BackupSettings? settings = null)
    {
        settings ??= new BackupSettings { Folder = _backups };
        return new BackupViewModel(Sources, settings, () =>
        {
            _flushes++;
            return Task.CompletedTask;
        }, () => _restarts++, "2.0.0");
    }

    private static FolderRowViewModel Row(BackupViewModel page, string id) => page.Rows.Single(r => r.Source.Id == id);

    [Fact]
    public void Lists_every_folder_with_what_it_says_about_itself()
    {
        var page = Page();

        Assert.Equal(["Notes", "Keys", "No backup"], page.Rows.Select(r => r.Name));
        Assert.Equal(_notes, Row(page, "notes").PathText);
        Assert.True(Row(page, "notes").CanChange);
        Assert.False(Row(page, "keys").CanChange);
        Assert.False(Row(page, "none").CanBackUp);
    }

    [Fact]
    public void There_is_one_row_for_each_client_however_many_there_are()
    {
        var page = Page();
        Assert.False(page.HasMessageRows);

        foreach (var count in new[] { 1, 2, 5 })
        {
            _clients.Clear();
            _clients.AddRange(Enumerable.Range(1, count).Select(i => ($"client{i}", "")));
            page.Refresh();

            Assert.Equal(_clients.Select(c => c.Name), page.MessageRows.Select(r => r.Name));
            Assert.True(page.HasMessageRows);
        }

        _clients.RemoveAt(2);
        page.Refresh();

        Assert.Equal(["client1", "client2", "client4", "client5"], page.MessageRows.Select(r => r.Name));
        Assert.Equal(["Notes", "Keys", "No backup"], page.Rows.Select(r => r.Name));
    }

    [Fact]
    public void A_client_with_no_folder_has_nothing_to_back_up()
    {
        _clients.Add(("empty", ""));
        var row = Page().MessageRows.Single();

        Assert.True(row.HasNoFolder);
        Assert.False(row.HasSomethingToBackUp);
        Assert.False(row.IsIncludedAndHasFolder);

        row.IsIncludedAndHasFolder = false;
        Assert.True(row.IsIncluded);
    }

    [Fact]
    public void A_client_given_a_folder_can_be_backed_up_at_once()
    {
        _clients.Add(("late", ""));
        var page = Page();

        _clients[0] = ("late", _notes);
        page.Refresh();

        var row = page.MessageRows.Single();
        Assert.False(row.HasNoFolder);
        Assert.True(row.IsIncludedAndHasFolder);
        Assert.Equal(_notes, row.PathText);
    }

    [Fact]
    public async Task Every_client_with_a_folder_is_backed_up()
    {
        _clients.Add(("first", _notes));
        _clients.Add(("second", _keys));
        _clients.Add(("none", ""));
        var page = Page();
        Row(page, "notes").IsIncluded = false;

        await page.BackUpNowCommand.ExecuteAsync(null);

        var manifest = BackupArchive.Read(Directory.GetFiles(_backups).Single());
        Assert.Equal(["client:first", "client:second"], manifest.Items.Select(i => i.Id));
    }

    [Fact]
    public void A_renamed_client_keeps_what_was_chosen()
    {
        _clients.Add(("old", _notes));
        var page = Page();
        page.MessageRows.Single().IsIncluded = false;

        _clients[0] = ("new", _notes);
        page.Renamed("client:old", "client:new");

        var row = page.MessageRows.Single();
        Assert.Equal("new", row.Name);
        Assert.False(row.IsIncluded);
        Assert.False(BackupSettings.Load().Included.ContainsKey("client:old"));
    }

    [Fact]
    public void What_is_being_typed_is_left_alone_when_the_page_follows_a_change()
    {
        var page = Page();
        var row = Row(page, "notes");
        row.PathText = "/being/typed";

        page.Refresh();

        Assert.Equal("/being/typed", row.PathText);
    }

    [Fact]
    public void A_folder_changed_elsewhere_is_shown()
    {
        _clients.Add(("client", _notes));
        var page = Page();

        _clients[0] = ("client", _keys);
        page.Refresh();

        Assert.Equal(_keys, page.MessageRows.Single().PathText);
    }

    [Fact]
    public void Secrets_start_unticked_and_warn_only_when_ticked()
    {
        var page = Page();
        var keys = Row(page, "keys");

        Assert.True(Row(page, "notes").IsIncluded);
        Assert.False(keys.IsIncluded);
        Assert.False(keys.ShowsWarning);

        keys.IsIncluded = true;

        Assert.True(keys.ShowsWarning);
    }

    [Fact]
    public void What_is_ticked_is_remembered()
    {
        Row(Page(), "keys").IsIncluded = true;

        var saved = BackupSettings.Load();

        Assert.True(saved.Included["keys"]);
        Assert.True(Row(Page(saved), "keys").IsIncluded);
    }

    [Fact]
    public async Task A_backup_holds_what_is_ticked_after_saving_what_is_open()
    {
        var page = Page();

        await page.BackUpNowCommand.ExecuteAsync(null);

        Assert.Null(page.Error);
        Assert.Equal(1, _flushes);
        Assert.Contains("1 file", page.Status);
        var zip = Assert.Single(Directory.GetFiles(_backups));
        Assert.StartsWith("joktan-backup-", Path.GetFileName(zip));
        var manifest = BackupArchive.Read(zip);
        Assert.Equal("2.0.0", manifest.AppVersion);
        Assert.Equal("notes", Assert.Single(manifest.Items).Id);
        Assert.Equal(zip, Assert.Single(page.Backups).Path);
    }

    [Fact]
    public async Task With_nothing_ticked_it_says_so()
    {
        var page = Page();
        Row(page, "notes").IsIncluded = false;

        await page.BackUpNowCommand.ExecuteAsync(null);

        Assert.Contains("Nothing is switched on", page.Error);
        Assert.False(Directory.Exists(_backups));
    }

    [Fact]
    public async Task The_oldest_backups_go_past_the_number_kept()
    {
        Directory.CreateDirectory(_backups);
        for (var day = 1; day <= 3; day++)
            File.WriteAllText(Path.Combine(_backups, $"joktan-backup-2020-01-0{day}-000000.zip"), "");
        File.WriteAllText(Path.Combine(_backups, "mine.zip"), "");
        var page = Page(new BackupSettings { Folder = _backups, KeepLast = 2 });

        await page.BackUpNowCommand.ExecuteAsync(null);

        var names = Directory.GetFiles(_backups).Select(Path.GetFileName).ToList();
        Assert.Equal(3, names.Count);
        Assert.Contains("mine.zip", names);
        Assert.Contains("joktan-backup-2020-01-03-000000.zip", names);
    }

    [Fact]
    public async Task A_backup_folder_that_is_not_whole_is_refused()
    {
        var page = Page();
        page.BackupFolderText = "relative/path";

        await page.BackUpNowCommand.ExecuteAsync(null);

        Assert.Contains("whole path", page.Error);
    }

    private async Task<(BackupViewModel Page, string Zip)> Backed()
    {
        var page = Page();
        await page.BackUpNowCommand.ExecuteAsync(null);
        var zip = Directory.GetFiles(_backups).Single();
        return (page, zip);
    }

    [Fact]
    public async Task Opening_a_backup_says_what_it_holds_and_where_it_goes()
    {
        var (page, zip) = await Backed();

        page.OpenBackup(zip);

        var row = Assert.Single(page.RestoreRows);
        Assert.True(page.HasBackupOpen);
        Assert.True(row.IsChosen);
        Assert.Contains("1 file", row.Detail);
        Assert.Contains(_notes, row.Detail);
        Assert.Contains("Joktan 2.0.0", page.RestoreSummary);
    }

    [Fact]
    public async Task Something_with_no_files_cannot_be_restored_and_says_so()
    {
        var page = Page();
        File.Delete(Note);
        await page.BackUpNowCommand.ExecuteAsync(null);

        page.OpenBackup(Directory.GetFiles(_backups).Single());

        var row = Assert.Single(page.RestoreRows);
        Assert.False(row.HasFiles);
        Assert.False(row.IsChosen);
        Assert.Equal("Nothing of this is in the backup", row.Detail);
    }

    [Fact]
    public void Something_else_is_refused_when_opened()
    {
        var page = Page();

        page.OpenBackup(_temp.Write("other.zip", "not a zip"));

        Assert.False(page.HasBackupOpen);
        Assert.NotNull(page.RestoreError);
    }

    [Fact]
    public async Task Nothing_is_replaced_until_the_app_has_restarted()
    {
        var (page, zip) = await Backed();
        File.WriteAllText(Note, "changed");
        page.OpenBackup(zip);

        page.AskRestoreCommand.Execute(null);
        Assert.True(page.IsConfirmingRestore);
        Assert.Contains("Notes", page.ConfirmText);
        Assert.Equal(0, _restarts);

        await page.ConfirmRestoreCommand.ExecuteAsync(null);

        Assert.Null(page.RestoreError);
        Assert.Equal(1, _restarts);
        Assert.Equal("changed", File.ReadAllText(Note));
        Assert.True(File.Exists(Data.At("restore-pending.json")));
    }

    [Fact]
    public async Task A_restore_puts_it_back_at_the_next_start_and_says_so()
    {
        var (page, zip) = await Backed();
        File.WriteAllText(Note, "changed");
        _temp.Write("notes/since.md", "made since");
        page.OpenBackup(zip);
        await page.ConfirmRestoreCommand.ExecuteAsync(null);

        PendingRestore.ApplyIfAny();

        Assert.Equal("original", File.ReadAllText(Note));
        Assert.False(File.Exists(Path.Combine(_notes, "since.md")));
        var reopened = Page();
        Assert.Contains("Restored Notes", reopened.Status);
        Assert.Contains("joktan-safety-", reopened.Status);
    }

    [Fact]
    public async Task The_safety_zip_undoes_a_restore()
    {
        var (page, zip) = await Backed();
        File.WriteAllText(Note, "changed");
        _temp.Write("notes/since.md", "made since");
        page.OpenBackup(zip);
        await page.ConfirmRestoreCommand.ExecuteAsync(null);
        PendingRestore.ApplyIfAny();
        Assert.Equal("original", File.ReadAllText(Note));
        var safety = Directory.GetFiles(_backups, "joktan-safety-*").Single();

        var again = Page();
        again.OpenBackup(safety);
        await again.ConfirmRestoreCommand.ExecuteAsync(null);
        PendingRestore.ApplyIfAny();

        Assert.Equal("changed", File.ReadAllText(Note));
        Assert.True(File.Exists(Path.Combine(_notes, "since.md")));
    }

    [Fact]
    public async Task Only_what_is_ticked_is_put_back()
    {
        var page = Page();
        Row(page, "keys").IsIncluded = true;
        await page.BackUpNowCommand.ExecuteAsync(null);
        File.WriteAllText(Note, "changed");
        File.WriteAllText(Path.Combine(_keys, "private.key"), "changed");
        page.OpenBackup(Directory.GetFiles(_backups).Single());
        page.RestoreRows.Single(r => r.Name == "Keys").IsChosen = false;

        await page.ConfirmRestoreCommand.ExecuteAsync(null);
        PendingRestore.ApplyIfAny();

        Assert.Equal("original", File.ReadAllText(Note));
        Assert.Equal("changed", File.ReadAllText(Path.Combine(_keys, "private.key")));
    }

    [Fact]
    public async Task With_nothing_ticked_there_is_nothing_to_restore()
    {
        var (page, zip) = await Backed();
        page.OpenBackup(zip);
        page.RestoreRows[0].IsChosen = false;

        page.AskRestoreCommand.Execute(null);

        Assert.False(page.IsConfirmingRestore);
        Assert.Contains("Nothing is switched on", page.RestoreError);
    }

    [Fact]
    public async Task A_damaged_backup_changes_nothing_and_does_not_restart()
    {
        var (page, zip) = await Backed();
        page.OpenBackup(zip);
        File.WriteAllText(zip, "damaged");

        await page.ConfirmRestoreCommand.ExecuteAsync(null);

        Assert.Contains("Nothing was changed", page.RestoreError);
        Assert.Equal(0, _restarts);
        Assert.False(File.Exists(Data.At("restore-pending.json")));
    }

    [Fact]
    public async Task A_safety_zip_that_cannot_be_made_changes_nothing()
    {
        var (page, zip) = await Backed();
        var moved = _temp.At("elsewhere.zip");
        File.Move(zip, moved);
        page.OpenBackup(moved);
        _temp.Write("blocked", "a file where the folder should be");
        page.BackupFolderText = _temp.At("blocked");

        await page.ConfirmRestoreCommand.ExecuteAsync(null);

        Assert.Contains("Nothing was changed", page.RestoreError);
        Assert.Equal(0, _restarts);
    }

    [Fact]
    public async Task A_restore_goes_where_things_are_kept_now()
    {
        var (_, zip) = await Backed();
        var manifest = BackupArchive.Read(zip);
        var now = new BackupItem("notes", "Notes", [new BackupRoot("folder", "/kept/here/now", false, ["/kept/here/now/skip"])]);

        var step = Assert.Single(BackupPlanner.Steps(manifest, ["notes"], [now]));

        Assert.Equal("/kept/here/now", step.Target);
        Assert.Equal(["/kept/here/now/skip"], step.Exclude);
    }

    [Fact]
    public async Task What_is_no_longer_known_goes_back_where_it_was()
    {
        var (_, zip) = await Backed();

        var step = Assert.Single(BackupPlanner.Steps(BackupArchive.Read(zip), ["notes"], []));

        Assert.Equal(_notes, step.Target);
    }

    [Fact]
    public async Task A_folder_is_changed_by_what_owns_it()
    {
        var row = Row(Page(), "notes");
        row.PathText = _temp.At("elsewhere");

        await row.ApplyCommand.ExecuteAsync(null);

        Assert.Equal([_temp.At("elsewhere")], _changes);
        Assert.Equal("Done", row.Message);
        Assert.False(row.HasProblem);
    }

    [Fact]
    public async Task A_problem_from_what_owns_it_is_shown_and_the_box_kept()
    {
        var row = Row(Page(), "notes");
        row.PathText = _temp.At("bad");

        await row.ApplyCommand.ExecuteAsync(null);

        Assert.Equal("Could not use it", row.Problem);
        Assert.Equal(_temp.At("bad"), row.PathText);
    }

    [Fact]
    public async Task A_path_that_is_not_whole_is_refused()
    {
        var row = Row(Page(), "notes");
        row.PathText = "relative/path";

        await row.ApplyCommand.ExecuteAsync(null);

        Assert.Empty(_changes);
        Assert.Contains("whole path", row.Problem);
    }

    [Fact]
    public async Task An_empty_box_means_the_default()
    {
        var row = Row(Page(), "notes");
        row.UseDefaultCommand.Execute(null);
        Assert.Equal("/default/notes", row.PathText);
        row.PathText = "";

        await row.ApplyCommand.ExecuteAsync(null);

        Assert.Equal(["/default/notes"], _changes);
    }

    [Fact]
    public async Task The_same_folder_again_changes_nothing()
    {
        var row = Row(Page(), "notes");

        await row.ApplyCommand.ExecuteAsync(null);

        Assert.Empty(_changes);
    }

    [Fact]
    public void A_home_folder_is_made_whole()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        Assert.Equal(Path.Combine(home, "notes"), BackupPlanner.WholePath("~/notes"));
        Assert.Null(BackupPlanner.WholePath("notes"));
        Assert.Null(BackupPlanner.WholePath(""));
    }

    [Fact]
    public void Copying_goes_only_into_an_empty_folder()
    {
        var empty = _temp.At("target");
        Assert.True(BackupPlanner.CopyIfEmpty(_notes, empty));
        Assert.Equal("original", File.ReadAllText(Path.Combine(empty, "a.md")));
        Assert.Equal("original", File.ReadAllText(Note));

        File.WriteAllText(Note, "changed");
        Assert.False(BackupPlanner.CopyIfEmpty(_notes, empty));
        Assert.Equal("original", File.ReadAllText(Path.Combine(empty, "a.md")));
    }

    [Fact]
    public void A_folder_is_not_copied_into_itself()
    {
        Assert.Throws<IOException>(() => BackupPlanner.CopyIfEmpty(_notes, Path.Combine(_notes, "inner")));
    }

    [Fact]
    public void How_many_are_kept_is_remembered_within_its_limits()
    {
        var settings = new BackupSettings { Folder = _backups };
        var page = Page(settings);

        page.KeepLast = 7;
        Assert.Equal(7, BackupSettings.Load().KeepLast);

        page.KeepLast = 0;
        Assert.Equal(1, settings.KeepLast);
    }

    [Fact]
    public async Task The_backup_folder_can_be_browsed_for_or_set_to_the_default()
    {
        var page = Page();
        var before = page.BackupFolderText;

        await page.BrowseBackupFolderCommand.ExecuteAsync(null);
        page.PickFolder = () => Task.FromResult<string?>(null);
        await page.BrowseBackupFolderCommand.ExecuteAsync(null);
        Assert.Equal(before, page.BackupFolderText);

        page.PickFolder = () => Task.FromResult<string?>("/picked");
        await page.BrowseBackupFolderCommand.ExecuteAsync(null);
        Assert.Equal("/picked", page.BackupFolderText);

        page.UseDefaultBackupFolderCommand.Execute(null);
        Assert.Equal(BackupSettings.DefaultFolder, page.BackupFolderText);
    }

    [Fact]
    public async Task A_backup_can_be_picked_from_the_list_or_from_a_file()
    {
        var page = Page();
        await page.BackUpNowCommand.ExecuteAsync(null);

        page.SelectedBackup = page.Backups[0];
        Assert.NotEmpty(page.RestoreRows);

        page.SelectedBackup = null;
        Assert.NotEmpty(page.RestoreRows);

        await page.BrowseBackupCommand.ExecuteAsync(null);
        page.PickZip = () => Task.FromResult<string?>(null);
        await page.BrowseBackupCommand.ExecuteAsync(null);

        page.PickZip = () => Task.FromResult<string?>(_temp.At("not-there.zip"));
        await page.BrowseBackupCommand.ExecuteAsync(null);
        Assert.NotNull(page.RestoreError);
        Assert.True(page.HasRestoreError);
    }

    [Fact]
    public async Task A_restore_can_be_put_off_or_needs_a_backup_to_be_open()
    {
        var page = Page();
        await page.ConfirmRestoreCommand.ExecuteAsync(null);
        Assert.Equal(0, _restarts);

        await page.BackUpNowCommand.ExecuteAsync(null);
        page.OpenBackup(page.Backups[0].Path);
        page.AskRestoreCommand.Execute(null);
        Assert.True(page.IsConfirmingRestore);

        page.CancelRestoreCommand.Execute(null);
        Assert.False(page.IsConfirmingRestore);
    }

    [Fact]
    public async Task A_folder_for_backups_that_cannot_be_used_stops_a_backup_and_a_restore()
    {
        var page = Page();
        await page.BackUpNowCommand.ExecuteAsync(null);
        page.OpenBackup(page.Backups[0].Path);

        page.BackupFolderText = "not/whole";
        await page.ConfirmRestoreCommand.ExecuteAsync(null);

        Assert.NotNull(page.RestoreError);
        Assert.Equal(0, _restarts);
    }

    [Fact]
    public async Task A_backup_that_cannot_be_written_says_why()
    {
        var blocked = _temp.Write("blocked", "");
        var page = Page(new BackupSettings { Folder = Path.Combine(blocked, "below") });

        await page.BackUpNowCommand.ExecuteAsync(null);

        Assert.True(page.HasError);
        Assert.True(page.IsIdle);
    }

    [Fact]
    public void A_row_with_something_to_back_up_can_be_switched()
    {
        var page = Page();
        var row = Row(page, "notes");

        row.IsIncludedAndHasFolder = false;

        Assert.False(row.IsIncluded);
        Assert.False(row.IsIncludedAndHasFolder);
    }

    [Fact]
    public async Task A_row_that_cannot_change_does_nothing_when_applied()
    {
        var row = Row(Page(), "keys");

        await row.ApplyCommand.ExecuteAsync(null);

        Assert.False(row.HasProblem);
        Assert.False(row.HasMessage);
    }

    [Fact]
    public void A_restore_that_failed_at_the_last_start_says_so()
    {
        Data.Write(PendingRestore.OwnFiles[1], """{"Error":"It went wrong","SafetyPath":"/safe.zip"}""");

        var page = Page();

        Assert.Contains("It went wrong", page.Error);
        Assert.Contains("/safe.zip", page.Error);
    }

    [Fact]
    public Task A_problem_found_at_start_up_shows_and_applying_goes_ahead_even_with_the_folder_in_use() => Headless.Run(async () =>
    {
        var changed = new System.Collections.Generic.List<string>();
        var folder = Data.At("in-use");
        var source = new FolderSource
        {
            Id = "settings",
            Name = "App settings",
            CurrentPath = () => folder,
            ChangeText = "Use folder and restart",
            StartProblem = "It could not be found",
            Change = path =>
            {
                changed.Add(path);
                return Task.FromResult(new FolderChange(null));
            },
        };
        var row = new FolderRowViewModel(source, new BackupViewModel(() => [source], new BackupSettings(), () => Task.CompletedTask, () => { }), false);

        Assert.Equal("It could not be found", row.Problem);
        Assert.Equal("Use folder and restart", row.ChangeText);

        row.PathText = folder;
        await row.ApplyCommand.ExecuteAsync(null);

        Assert.Equal([folder], changed);
    });
}
