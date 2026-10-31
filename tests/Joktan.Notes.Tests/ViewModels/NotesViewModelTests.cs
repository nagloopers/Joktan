using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Joktan.Notes.ViewModels;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Notes.Tests.ViewModels;

[Collection(ProcessStateCollection.Name)]
public class NotesViewModelTests : DataFolderTest
{
    private NotesViewModel New(string? folder = null) => new(NoteStore.Load(folder ?? Data.At("notes")));

    /// <summary>A view model with pages A, B and C at the top level and A1 and A2 below A, and A selected.</summary>
    private NotesViewModel WithPages(string? folder = null)
    {
        folder ??= Data.At("notes");
        var store = NoteStore.Load(folder);
        store.SaveIndex(
        [
            new NotePage
            {
                Id = "a",
                Title = "A",
                Children = [new NotePage { Id = "a1", Title = "A1" }, new NotePage { Id = "a2", Title = "A2" }],
            },
            new NotePage { Id = "b", Title = "B" },
            new NotePage { Id = "c", Title = "C" },
        ], "a");
        store.FlushAsync().Wait(TestContext.Current.CancellationToken);
        return new NotesViewModel(NoteStore.Load(folder));
    }

    private static NoteNodeViewModel Node(NotesViewModel vm, string id) =>
        vm.Roots.SelectMany(r => r.SelfAndDescendants()).First(n => n.Id == id);

    private static string Shape(NotesViewModel vm) => string.Join(",", vm.Roots.Select(Shape));

    private static string Shape(NoteNodeViewModel node) =>
        node.Children.Count == 0 ? node.Id : $"{node.Id}({string.Join(",", node.Children.Select(Shape))})";

    [Fact]
    public Task A_new_folder_starts_with_a_welcome_page_that_is_saved() => Headless.Run(() =>
    {
        var vm = New();

        var welcome = Assert.Single(vm.Roots);
        Assert.Same(welcome, vm.Selected);
        Assert.Equal("Welcome", vm.PageTitle);
        Assert.Contains("Markdown", vm.Text);
        Assert.False(vm.IsEmpty);
        Assert.True(vm.HasSelection);

        vm.SaveState();
        Assert.True(File.Exists(Path.Combine(vm.Folder, welcome.Id + ".md")));
        Assert.True(NoteStore.HasNotes(vm.Folder));
    });

    [Fact]
    public Task The_page_open_last_is_selected_and_its_text_read_when_needed() => Headless.Run(() =>
    {
        Data.Write("notes/b.md", "Text of B");
        var store = NoteStore.Load(Data.At("notes"));
        store.SaveIndex([new NotePage { Id = "a", Title = "A" }, new NotePage { Id = "b", Title = "B" }], "b");
        store.FlushAsync().Wait(TestContext.Current.CancellationToken);

        var vm = new NotesViewModel(NoteStore.Load(Data.At("notes")));

        Assert.Equal("b", vm.Selected!.Id);
        Assert.Equal("Text of B", vm.Text);
    });

    [Fact]
    public Task An_unknown_page_selects_the_first_and_none_selects_nothing() => Headless.Run(() =>
    {
        var store = NoteStore.Load(Data.At("notes"));
        store.SaveIndex([new NotePage { Id = "a", Title = "A" }], "gone");
        store.FlushAsync().Wait(TestContext.Current.CancellationToken);
        Assert.Equal("a", new NotesViewModel(NoteStore.Load(Data.At("notes"))).Selected!.Id);

        store.SaveIndex([], "");
        store.FlushAsync().Wait(TestContext.Current.CancellationToken);
        var empty = new NotesViewModel(NoteStore.Load(Data.At("notes")));
        Assert.Null(empty.Selected);
        Assert.True(empty.IsEmpty);
        Assert.Equal("", empty.PageTitle);
        Assert.Equal("", empty.Text);
        Assert.Equal("", empty.Path);
        Assert.Equal("", empty.DeleteText);
        Assert.False(empty.HasSelection);
    });

    [Fact]
    public Task Text_and_title_changes_are_written() => Headless.Run(() =>
    {
        var vm = WithPages();

        vm.PageTitle = "Renamed";
        vm.Text = "New text";
        vm.SaveState();

        Assert.Equal("New text", File.ReadAllText(Path.Combine(vm.Folder, "a.md")));
        Assert.Equal("Renamed", NoteStore.Load(vm.Folder).Pages[0].Title);
    });

    [Fact]
    public Task Typing_is_saved_after_a_pause() => Headless.Run(async () =>
    {
        var vm = WithPages();

        vm.Text = "Typed";

        await Headless.Until(() =>
        {
            var path = Path.Combine(vm.Folder, "a.md");
            return File.Exists(path) && File.ReadAllText(path) == "Typed";
        });
    });

    [Fact]
    public Task Setting_the_same_value_or_with_nothing_selected_changes_nothing() => Headless.Run(() =>
    {
        var vm = WithPages();
        var changes = 0;
        vm.PropertyChanged += (_, _) => changes++;

        vm.PageTitle = "A";
        vm.Text = vm.Text;
        Assert.Equal(0, changes);

        vm.Selected = null;
        changes = 0;
        vm.PageTitle = "x";
        vm.Text = "x";
        Assert.Equal(0, changes);
    });

    [Fact]
    public Task The_tree_cannot_clear_the_selection_of_a_page_still_there() => Headless.Run(() =>
    {
        var vm = WithPages();
        var a = vm.Selected;

        vm.TreeSelection = null;
        Assert.Same(a, vm.Selected);

        vm.TreeSelection = Node(vm, "b");
        Assert.Equal("b", vm.TreeSelection!.Id);

        vm.Selected = null;
        vm.TreeSelection = null;
        Assert.Null(vm.Selected);
    });

    [Fact]
    public Task Selecting_a_page_ends_editing_and_opens_the_pages_above_it() => Headless.Run(() =>
    {
        var vm = WithPages();
        var a = Node(vm, "a");
        a.IsExpanded = false;
        vm.IsEditing = true;
        vm.IsConfirmingDelete = true;

        vm.Selected = Node(vm, "a2");

        Assert.True(a.IsExpanded);
        Assert.False(vm.IsEditing);
        Assert.False(vm.IsConfirmingDelete);
        Assert.Equal("A", vm.Path);
    });

    [Fact]
    public Task Expanding_a_page_is_remembered() => Headless.Run(() =>
    {
        var vm = WithPages();

        Node(vm, "a").IsExpanded = false;
        Node(vm, "a").Title = "No save for this one";
        vm.SaveState();

        Assert.False(NoteStore.Load(vm.Folder).Pages[0].IsExpanded);
    });

    [Fact]
    public Task Editing_and_viewing_switch_where_the_page_is_shown() => Headless.Run(() =>
    {
        var vm = WithPages();
        Assert.True(vm.IsViewing);
        Assert.Equal(0, vm.PreviewColumn);
        Assert.Equal(3, vm.PreviewColumnSpan);
        Assert.True(vm.IsEmptyView);

        vm.EditCommand.Execute(null);
        Assert.True(vm.IsEditing);
        Assert.Equal(2, vm.PreviewColumn);
        Assert.Equal(1, vm.PreviewColumnSpan);
        Assert.False(vm.IsEmptyView);

        vm.ViewCommand.Execute(null);
        Assert.False(vm.IsEditing);

        vm.ToggleEditingCommand.Execute(null);
        Assert.True(vm.IsEditing);
    });

    [Fact]
    public Task New_pages_are_added_where_asked_and_opened_for_editing() => Headless.Run(() =>
    {
        var vm = WithPages();
        var a = Node(vm, "a");
        a.IsExpanded = false;

        vm.NewChildPageCommand.Execute(null);

        Assert.Equal(3, a.Children.Count);
        Assert.True(a.IsExpanded);
        Assert.True(vm.IsEditing);
        Assert.Same(a, vm.Selected!.Parent);

        vm.NewPageCommand.Execute(null);

        Assert.Equal(4, vm.Roots.Count);
        Assert.Null(vm.Selected!.Parent);
    });

    [Fact]
    public Task The_child_page_and_delete_commands_need_a_selection() => Headless.Run(() =>
    {
        var vm = WithPages();
        Assert.True(vm.NewChildPageCommand.CanExecute(null));
        Assert.True(vm.DeleteCommand.CanExecute(null));

        vm.Selected = null;

        Assert.False(vm.NewChildPageCommand.CanExecute(null));
        Assert.False(vm.DeleteCommand.CanExecute(null));
        Assert.False(vm.MoveUpCommand.CanExecute(null));
        Assert.False(vm.MoveDownCommand.CanExecute(null));
        Assert.False(vm.IndentCommand.CanExecute(null));
        Assert.False(vm.OutdentCommand.CanExecute(null));
    });

    [Fact]
    public Task Pages_move_among_the_pages_beside_them_and_keep_being_edited() => Headless.Run(() =>
    {
        var vm = WithPages();
        vm.Selected = Node(vm, "b");
        vm.IsEditing = true;
        Assert.True(vm.MoveUpCommand.CanExecute(null));
        Assert.True(vm.MoveDownCommand.CanExecute(null));

        vm.MoveDownCommand.Execute(null);
        Assert.StartsWith("a(a1,a2),c,b", Shape(vm));
        Assert.False(vm.MoveDownCommand.CanExecute(null));
        Assert.True(vm.IsEditing);

        vm.MoveUpCommand.Execute(null);
        vm.MoveUpCommand.Execute(null);
        Assert.StartsWith("b,a(a1,a2),c", Shape(vm));
        Assert.False(vm.MoveUpCommand.CanExecute(null));
    });

    [Fact]
    public Task Indent_moves_a_page_below_the_one_above_it_and_outdent_moves_it_back() => Headless.Run(() =>
    {
        var vm = WithPages();
        var b = Node(vm, "b");
        vm.Selected = b;
        Node(vm, "a").IsExpanded = false;
        vm.IsEditing = true;
        Assert.False(vm.OutdentCommand.CanExecute(null));

        vm.IndentCommand.Execute(null);

        Assert.Equal("a(a1,a2,b),c", Shape(vm));
        Assert.True(Node(vm, "a").IsExpanded);
        Assert.Same(b, vm.Selected);
        Assert.Equal("A", vm.Path);
        Assert.True(vm.IsEditing);

        Assert.True(vm.OutdentCommand.CanExecute(null));
        vm.OutdentCommand.Execute(null);

        Assert.Equal("a(a1,a2),b,c", Shape(vm));
        Assert.Equal("", vm.Path);
    });

    [Theory]
    [InlineData("a", "Delete A and the 2 pages below it? This cannot be undone.")]
    [InlineData("c", "Delete C? This cannot be undone.")]
    public Task Deleting_says_what_goes(string id, string expected) => Headless.Run(() =>
    {
        var vm = WithPages();
        vm.Selected = Node(vm, id);

        Assert.Equal(expected, vm.DeleteText);
    });

    [Fact]
    public Task Deleting_one_child_says_the_page_below_it() => Headless.Run(() =>
    {
        var vm = WithPages();
        Node(vm, "a").Children.Remove(Node(vm, "a2"));
        vm.Selected = Node(vm, "a");

        Assert.Equal("Delete A and the page below it? This cannot be undone.", vm.DeleteText);
    });

    [Fact]
    public Task Delete_asks_first_and_can_be_cancelled() => Headless.Run(() =>
    {
        var vm = WithPages();

        vm.DeleteCommand.Execute(null);
        Assert.True(vm.IsConfirmingDelete);

        vm.CancelDeleteCommand.Execute(null);
        Assert.False(vm.IsConfirmingDelete);
        Assert.Equal(3, vm.Roots.Count);
    });

    [Fact]
    public Task Deleting_selects_the_page_after_else_before_else_the_parent() => Headless.Run(() =>
    {
        var vm = WithPages();

        vm.Selected = Node(vm, "a1");
        vm.ConfirmDeleteCommand.Execute(null);
        Assert.Equal("a2", vm.Selected!.Id);

        vm.ConfirmDeleteCommand.Execute(null);
        Assert.Equal("a", vm.Selected!.Id);

        vm.Selected = Node(vm, "c");
        vm.ConfirmDeleteCommand.Execute(null);
        Assert.Equal("b", vm.Selected!.Id);
    });

    [Fact]
    public Task Deleting_removes_the_files_and_with_nothing_selected_does_nothing() => Headless.Run(async () =>
    {
        Data.Write("notes/c.md", "C");
        var vm = WithPages();
        vm.Selected = Node(vm, "c");

        vm.ConfirmDeleteCommand.Execute(null);
        await Headless.Until(() => !File.Exists(Data.At("notes", "c.md")));

        vm.Selected = null;
        vm.ConfirmDeleteCommand.Execute(null);
        Assert.Equal(2, vm.Roots.Count);
    });

    [Fact]
    public Task The_folder_band_starts_from_the_current_folder() => Headless.Run(() =>
    {
        var vm = WithPages();
        vm.FolderMessage = "old";

        vm.ToggleFolderCommand.Execute(null);

        Assert.True(vm.IsChoosingFolder);
        Assert.Equal(vm.Folder, vm.FolderText);
        Assert.Null(vm.FolderMessage);
        Assert.True(vm.IsDefaultFolder);

        vm.FolderText = "x";
        vm.UseDefaultFolderCommand.Execute(null);
        Assert.Equal(vm.DefaultFolder, vm.FolderText);
    });

    [Fact]
    public Task Browsing_uses_the_folder_picked_if_any() => Headless.Run(async () =>
    {
        var vm = WithPages();
        vm.FolderText = "before";

        await vm.BrowseFolderCommand.ExecuteAsync(null);
        Assert.Equal("before", vm.FolderText);

        vm.PickFolder = () => Task.FromResult<string?>(null);
        await vm.BrowseFolderCommand.ExecuteAsync(null);
        Assert.Equal("before", vm.FolderText);

        vm.PickFolder = () => Task.FromResult<string?>("picked");
        await vm.BrowseFolderCommand.ExecuteAsync(null);
        Assert.Equal("picked", vm.FolderText);
    });

    [Fact]
    public Task A_relative_folder_is_refused() => Headless.Run(async () =>
    {
        var vm = WithPages();
        vm.FolderText = "some/relative";

        await vm.ApplyFolderCommand.ExecuteAsync(null);

        Assert.Contains("whole path", vm.FolderMessage);
    });

    [Fact]
    public Task Applying_the_same_folder_just_closes_the_band() => Headless.Run(async () =>
    {
        var vm = WithPages();
        vm.IsChoosingFolder = true;

        vm.FolderText = vm.Folder;
        await vm.ApplyFolderCommand.ExecuteAsync(null);
        Assert.False(vm.IsChoosingFolder);

        // A blank folder means the default one, which is where these notes already are.
        vm.IsChoosingFolder = true;
        vm.FolderText = "  ";
        await vm.ApplyFolderCommand.ExecuteAsync(null);
        Assert.False(vm.IsChoosingFolder);
    });

    [Fact]
    public Task A_tilde_means_the_home_folder() => Headless.Run(async () =>
    {
        var vm = WithPages();
        vm.IsChoosingFolder = true;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var relative = Path.GetRelativePath(home, vm.Folder);
        vm.FolderText = "~/" + relative;

        await vm.ApplyFolderCommand.ExecuteAsync(null);

        // Resolves to the folder already open, so nothing is copied into the home folder.
        Assert.False(vm.IsChoosingFolder);
    });

    [Fact]
    public Task Notes_are_copied_to_a_folder_without_any() => Headless.Run(async () =>
    {
        var vm = WithPages();
        vm.Text = "Keep me";
        var previous = vm.Folder;
        vm.IsChoosingFolder = true;
        vm.FolderText = Data.At("elsewhere");

        await vm.ApplyFolderCommand.ExecuteAsync(null);

        Assert.False(vm.IsChoosingFolder);
        Assert.Equal(Data.At("elsewhere"), vm.Folder);
        Assert.False(vm.IsDefaultFolder);
        Assert.Contains("copied", vm.Message);
        Assert.Contains(previous, vm.Message);
        Assert.Equal("Keep me", vm.Text);
        Assert.Equal(Data.At("elsewhere"), NoteStore.ChosenFolder);
    });

    [Fact]
    public Task A_folder_holding_notes_is_opened_as_it_is() => Headless.Run(async () =>
    {
        var other = NoteStore.Load(Data.At("other"));
        other.SaveIndex([new NotePage { Id = "x", Title = "X" }], "x");
        await other.FlushAsync();
        var vm = WithPages();
        vm.FolderText = Data.At("other");

        await vm.ApplyFolderCommand.ExecuteAsync(null);

        Assert.Equal("x", Assert.Single(vm.Roots).Id);
        Assert.Contains("Opened", vm.Message);
    });

    [Fact]
    public Task A_folder_that_cannot_be_written_says_so_and_changes_nothing() => Headless.Run(async () =>
    {
        var file = Data.Write("a-file", "");
        var vm = WithPages();
        var before = vm.Folder;
        vm.FolderText = Path.Combine(file, "below");

        await vm.ApplyFolderCommand.ExecuteAsync(null);

        Assert.Contains("Could not copy", vm.FolderMessage);
        Assert.Equal(before, vm.Folder);
    });

    [Fact]
    public Task A_write_that_fails_is_reported() => Headless.Run(async () =>
    {
        // The notes folder cannot be made, as a file stands in its way.
        var file = Data.Write("blocked", "");
        var vm = new NotesViewModel(NoteStore.Load(file));

        vm.SaveState();

        await Headless.Until(() => vm.Error is not null);
        Assert.Contains("Could not write", vm.Error);
    });

    [Fact]
    public Task Search_finds_words_in_titles_and_in_pages_not_yet_opened() => Headless.Run(() =>
    {
        var vm = WithPages();
        Node(vm, "a1").Text = "The door CODE changed";
        File.WriteAllText(Data.At("notes", "b.md"), "first line\nthe code for the door\nlast line");

        var hits = vm.Search(["door", "code"]).ToList();

        Assert.Equal(["A1", "B"], hits.Select(h => h.Title));
        Assert.All(hits, h => Assert.Equal("Notes", h.Area));
        Assert.Equal("A · The door CODE changed", hits[0].Text);
        Assert.Equal("the code for the door", hits[1].Text);
        Assert.Empty(vm.Search(["door", "nowhere"]));
    });

    [Fact]
    public Task Search_puts_pages_named_by_the_words_first_and_finds_untitled_ones() => Headless.Run(() =>
    {
        var vm = WithPages();
        Node(vm, "a").Text = "about C";
        Node(vm, "c").Title = "";
        Node(vm, "c").Text = "";
        Node(vm, "b").Title = "About";

        Assert.Equal(["About", "A"], vm.Search(["about"]).Select(h => h.Title));
        Assert.Equal(["Untitled"], vm.Search(["untitled"]).Select(h => h.Title));
    });

    [Fact]
    public Task Searching_changes_no_page_on_disk_and_marks_none_as_changed() => Headless.Run(() =>
    {
        var vm = WithPages();
        File.WriteAllText(Data.At("notes", "b.md"), "kept");

        _ = vm.Search(["kept"]).ToList();

        Assert.False(Node(vm, "b").IsDirty);
        Assert.Equal("kept", File.ReadAllText(Data.At("notes", "b.md")));
    });

    [Fact]
    public Task Opening_a_result_shows_the_notes_page_on_that_page() => Headless.Run(() =>
    {
        var vm = WithPages();
        var shown = 0;
        vm.OpenPage = () => shown++;
        Node(vm, "a2").Text = "needle";
        Node(vm, "a").IsExpanded = false;

        vm.Search(["needle"]).Single().Open();

        Assert.Equal(1, shown);
        Assert.Equal("a2", vm.Selected!.Id);
        Assert.True(Node(vm, "a").IsExpanded);
    });

    [Fact]
    public Task Opening_a_result_marks_the_words_until_another_page_or_editing() => Headless.Run(() =>
    {
        var vm = WithPages();
        Node(vm, "b").Text = "needle";
        var open = vm.Search(["needle"]).Single().Open;

        open();
        Assert.Equal(["needle"], vm.HighlightWords);

        vm.Selected = Node(vm, "c");
        Assert.Null(vm.HighlightWords);

        open();
        Assert.Equal(["needle"], vm.HighlightWords);
        vm.EditCommand.Execute(null);
        Assert.Null(vm.HighlightWords);

        open();
        Assert.False(vm.IsEditing);
        Assert.Equal(["needle"], vm.HighlightWords);
    });
}
