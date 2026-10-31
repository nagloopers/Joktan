using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Joktan.Core;
using Joktan.Settings.Backup;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Settings.Tests.Backup;

// Every folder here is a temporary one passed in, so the real default folder is never read or written.
public sealed class DataFolderMoveTests : IDisposable
{
    private readonly TempFolder _defaults = new();
    private readonly TempFolder _target = new();
    private readonly string _source;
    private int _flushes;
    private int _restarts;

    public DataFolderMoveTests()
    {
        _source = _defaults.Path;
        File.WriteAllText(Path.Combine(_source, "menu.json"), "{}");
    }

    public void Dispose()
    {
        _defaults.Dispose();
        _target.Dispose();
    }

    private Task<FolderChange> Apply(string to) => DataFolderMove.ApplyAsync(to, _source, _defaults.Path, () =>
    {
        _flushes++;
        return Task.CompletedTask;
    }, () => _restarts++);

    private string At(params string[] parts) => Path.Combine([_target.Path, .. parts]);

    private void AssertNothingChosen()
    {
        Assert.Null(DataFolder.ReadChosen(_defaults.Path));
        Assert.Equal(0, _restarts);
    }

    [Fact]
    public async Task An_empty_folder_gets_a_copy_of_everything_and_the_originals_stay()
    {
        Directory.CreateDirectory(Path.Combine(_source, "notes", "deep"));
        File.WriteAllText(Path.Combine(_source, "notes", "deep", "a.md"), "note");

        var change = await Apply(_target.Path);

        Assert.Null(change.Problem);
        Assert.Contains("originals", change.Message);
        Assert.Equal("note", File.ReadAllText(At("notes", "deep", "a.md")));
        Assert.True(File.Exists(At("menu.json")));
        Assert.True(File.Exists(Path.Combine(_source, "notes", "deep", "a.md")));
        Assert.Equal(_target.Path, DataFolder.ReadChosen(_defaults.Path));
        Assert.Equal(1, _flushes);
        Assert.Equal(1, _restarts);
    }

    [Fact]
    public async Task What_is_only_about_this_folder_is_not_copied()
    {
        DataFolder.SaveChosen(Path.Combine(_source, "earlier"), _defaults.Path);
        foreach (var name in PendingRestore.OwnFiles)
            File.WriteAllText(Path.Combine(_source, name), "{}");

        await Apply(_target.Path);

        Assert.False(File.Exists(At(DataFolder.LocationFileName)));
        Assert.All(PendingRestore.OwnFiles, name => Assert.False(File.Exists(At(name))));
    }

    [Fact]
    public async Task A_folder_with_only_dot_files_counts_as_empty()
    {
        File.WriteAllText(At(".DS_Store"), "");

        Assert.Null((await Apply(_target.Path)).Problem);

        Assert.True(File.Exists(At("menu.json")));
    }

    [Fact]
    public async Task A_new_folder_is_made()
    {
        var folder = At("new", "folder");

        Assert.Null((await Apply(folder)).Problem);

        Assert.True(File.Exists(Path.Combine(folder, "menu.json")));
    }

    [Fact]
    public async Task A_folder_holding_settings_is_opened_as_it_is()
    {
        File.WriteAllText(At("mqtt.json"), "theirs");
        File.WriteAllText(Path.Combine(_source, "mqtt.json"), "ours");

        var change = await Apply(_target.Path);

        Assert.Null(change.Problem);
        Assert.Equal("theirs", File.ReadAllText(At("mqtt.json")));
        Assert.False(File.Exists(At("menu.json")));
        Assert.Equal(_target.Path, DataFolder.ReadChosen(_defaults.Path));
        Assert.Equal(1, _restarts);
    }

    [Fact]
    public async Task A_folder_holding_other_files_is_refused_and_left_alone()
    {
        File.WriteAllText(At("holiday.jpg"), "");

        var change = await Apply(_target.Path);

        Assert.Contains("other files", change.Problem);
        Assert.Equal(["holiday.jpg"], Directory.EnumerateFileSystemEntries(_target.Path).Select(Path.GetFileName));
        AssertNothingChosen();
    }

    [Fact]
    public async Task A_folder_inside_the_one_in_use_is_refused()
    {
        var change = await Apply(Path.Combine(_source, "inside"));

        Assert.Contains("inside the folder in use", change.Problem);
        Assert.False(Directory.Exists(Path.Combine(_source, "inside")));
        AssertNothingChosen();
    }

    [Fact]
    public async Task A_copy_that_fails_takes_back_what_it_made()
    {
        Directory.CreateDirectory(Path.Combine(_source, "notes"));
        File.WriteAllText(Path.Combine(_source, "notes", "a.md"), "note");

        // A file at the place of the choice's folder stops it being saved, after the copy has been made.
        var blocker = Path.Combine(_target.Path, "defaults-blocked");
        File.WriteAllText(blocker, "");
        var nowhere = Path.Combine(blocker, "defaults");
        var target = At("new");

        var change = await DataFolderMove.ApplyAsync(target, _source, nowhere, () => Task.CompletedTask, () => _restarts++);

        Assert.NotNull(change.Problem);
        Assert.False(Directory.Exists(target));
        Assert.True(File.Exists(Path.Combine(_source, "notes", "a.md")));
        Assert.Equal(0, _restarts);
    }

    [Fact]
    public async Task Paths_into_the_old_folder_are_changed_in_the_copy_only()
    {
        var old = Path.Combine(_source, "mqtt-certificates", "main", "ca.crt");
        var elsewhere = "/somewhere/else/ca.crt";
        var settings = System.Text.Json.JsonSerializer.Serialize(new { Inside = old, Outside = elsewhere }, JsonFormats.Indented);
        File.WriteAllText(Path.Combine(_source, "mqtt.json"), settings);

        await Apply(_target.Path);

        var copied = System.Text.Json.JsonDocument.Parse(File.ReadAllText(At("mqtt.json"))).RootElement;
        Assert.Equal(At("mqtt-certificates", "main", "ca.crt"), copied.GetProperty("Inside").GetString());
        Assert.Equal(elsewhere, copied.GetProperty("Outside").GetString());
        Assert.Equal(settings, File.ReadAllText(Path.Combine(_source, "mqtt.json")));
    }

    [Fact]
    public async Task The_same_folder_only_updates_the_choice_which_is_how_a_missing_one_is_given_up()
    {
        DataFolder.SaveChosen(Path.Combine(_source, "not-connected"), _defaults.Path);

        var change = await Apply(_source);

        Assert.Null(change.Problem);
        Assert.Null(DataFolder.ReadChosen(_defaults.Path));
        Assert.Equal(1, _restarts);
    }

    [Fact]
    public void A_folder_is_known_by_the_settings_it_holds()
    {
        Assert.False(DataFolderMove.HoldsSettings(_target.Path));

        File.WriteAllText(At("themes.json"), "{}");

        Assert.True(DataFolderMove.HoldsSettings(_target.Path));
    }
}
