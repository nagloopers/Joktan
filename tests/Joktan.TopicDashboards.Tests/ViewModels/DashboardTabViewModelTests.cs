using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using Joktan.TopicDashboards.ViewModels;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.TopicDashboards.Tests.ViewModels;

public class DashboardTabViewModelTests : DashboardTest
{
    [Fact]
    public Task Shows_what_is_saved_and_keeps_changes() => Headless.Run(() =>
    {
        var profile = new DashboardProfile { Name = "d", Columns = 2, RowHeight = 200, Gap = 8, Background = "#112233", TabColor = "#223344" };
        var page = Page(null, profile);
        var tab = page.Dashboards[0];

        Assert.Equal(2, tab.Columns);
        Assert.Equal(200, tab.RowHeight);
        Assert.Equal(8, tab.Gap);
        Assert.IsAssignableFrom<IBrush>(tab.BackgroundBrush);
        Assert.IsAssignableFrom<IBrush>(tab.TabFillBrush);
        Assert.NotNull(tab.TabForegroundBrush);
        Assert.True(tab.HasNoGraphs);
        Assert.Equal(DashboardProfile.MaxColumns, DashboardTabViewModel.ColumnChoices.Count);
        Assert.NotEmpty(DashboardTabViewModel.Palette);
        Assert.Equal(DashboardProfile.MinRowHeight, tab.MinRowHeight);
        Assert.Equal(DashboardProfile.MaxRowHeight, tab.MaxRowHeight);
        Assert.Equal(DashboardProfile.MaxGap, tab.MaxGap);

        tab.Columns = 4;
        tab.RowHeight = 300;
        tab.Gap = 12;
        tab.PickBackgroundCommand.Execute("#000000");
        tab.PickTabColorCommand.Execute("#FFFFFF");
        tab.Name = "renamed";

        Assert.Equal(4, profile.Columns);
        Assert.Equal(300, profile.RowHeight);
        Assert.Equal(12, profile.Gap);
        Assert.Equal("#000000", profile.Background);
        Assert.Equal("#FFFFFF", profile.TabColor);
        Assert.Equal("renamed", profile.Name);
    });

    [Fact]
    public Task A_blank_name_shows_as_untitled() => Headless.Run(() =>
    {
        var tab = Page(null, Board(" ")).Dashboards[0];

        Assert.Equal("Untitled", tab.DisplayName);
    });

    [Fact]
    public Task Renaming_takes_what_was_typed_unless_nothing_was() => Headless.Run(() =>
    {
        var page = Page(null, Board("a"), Board("b"));
        var tab = page.Dashboards[1];

        tab.FinishRenameCommand.Execute(null);

        tab.RenameCommand.Execute(null);
        Assert.Same(tab, page.SelectedDashboard);
        Assert.True(tab.IsRenaming);
        Assert.Equal("b", tab.RenameText);

        tab.RenameText = " new ";
        tab.FinishRenameCommand.Execute(null);
        Assert.Equal("new", tab.Name);
        Assert.False(tab.IsRenaming);

        tab.RenameCommand.Execute(null);
        tab.RenameText = "  ";
        tab.FinishRenameCommand.Execute(null);
        Assert.Equal("new", tab.Name);

        tab.RenameCommand.Execute(null);
        tab.RenameText = "not this";
        tab.CancelRenameCommand.Execute(null);
        Assert.Equal("new", tab.Name);
    });

    [Theory]
    [InlineData(0, "Remove this dashboard?")]
    [InlineData(1, "Remove this dashboard and its graph?")]
    [InlineData(3, "Remove this dashboard and its 3 graphs?")]
    public Task Removing_says_what_goes(int graphs, string expected) => Headless.Run(() =>
    {
        var page = Page(null, Board("a", Enumerable.Range(0, graphs).Select(i => Graph($"g{i}")).ToArray()), Board("b"));
        var tab = page.Dashboards[0];

        tab.AskRemoveCommand.Execute(null);

        Assert.True(tab.IsConfirmingRemove);
        Assert.Equal(expected, tab.RemoveQuestion);

        tab.KeepDashboardCommand.Execute(null);
        Assert.False(tab.IsConfirmingRemove);

        tab.RemoveCommand.Execute(null);
        Assert.Single(page.Dashboards);
    });

    [Fact]
    public Task The_last_dashboard_says_an_empty_one_takes_its_place() => Headless.Run(() =>
    {
        var tab = Page(null, Board("a")).Dashboards[0];

        Assert.EndsWith("An empty one takes its place.", tab.RemoveQuestion);
    });

    [Fact]
    public Task Graphs_can_be_moved_and_those_of_one_saved_graph_stay_together() => Headless.Run(() =>
    {
        var a = Graph("a");
        var b = Graph("b");
        var c = Graph("c");
        var page = Page(null, Board("d", a, b, c));
        var tab = page.Dashboards[0];

        tab.Graphs[0].MoveLaterCommand.Execute(null);
        Assert.Equal(["b", "a", "c"], tab.Graphs.Select(g => g.Name));

        tab.Graphs[2].MoveEarlierCommand.Execute(null);
        Assert.Equal(["b", "c", "a"], tab.Graphs.Select(g => g.Name));

        // Off the ends nothing moves.
        tab.Graphs[0].MoveEarlierCommand.Execute(null);
        tab.Graphs[2].MoveLaterCommand.Execute(null);
        Assert.Equal(["b", "c", "a"], tab.Graphs.Select(g => g.Name));

        tab.MoveTo(tab.Graphs[0], 99);
        Assert.Equal(["c", "a", "b"], tab.Graphs.Select(g => g.Name));

        // To its own place, or a card that is not here.
        tab.MoveTo(tab.Graphs[0], 0);
        tab.MoveTo(new GraphCardViewModel(Graph("other"), new GraphData()), 0);
        Assert.Equal(["c", "a", "b"], tab.Graphs.Select(g => g.Name));
    });

    [Fact]
    public Task A_key_of_graphs_makes_a_graph_for_each_value_of_one_size() => Headless.Run(() =>
    {
        var graph = Graph("g", "home/{room}", "temp", GraphKind.Line, new GraphKey { Name = "room", Show = KeyShow.Graph });
        var other = Graph("other");
        var page = Page(null, Board("d", graph, other));
        var tab = page.Dashboards[0];
        var source = page.SourceFor(graph);

        source.Receive(Message("home/a", """{"temp":1}"""));
        source.Receive(Message("home/b", """{"temp":2}"""));
        tab.Arrange();

        Assert.Equal(["g: a", "g: b", "other"], tab.Graphs.Select(g => g.Name));
        Assert.True(tab.Graphs[0].HasCell);

        tab.Graphs[0].AskRemoveCommand.Execute(null);
        Assert.Equal("Remove these 2 graphs?", tab.Graphs[0].RemoveQuestion);
        tab.Graphs[2].AskRemoveCommand.Execute(null);
        Assert.Equal("Remove this graph?", tab.Graphs[2].RemoveQuestion);

        tab.Graphs[0].WidthSpan = 2;
        tab.Graphs[0].HeightSpan = 3;
        Assert.Equal(2, tab.Graphs[1].WidthSpan);
        Assert.Equal(3, tab.Graphs[1].HeightSpan);

        // Moving past the others made from the same saved graph.
        tab.Graphs[0].MoveLaterCommand.Execute(null);
        Assert.Equal("other", tab.Graphs[0].Name);

        tab.Graphs[1].ForgetCommand.Execute(null);
        Assert.Equal(["other", "g: b"], tab.Graphs.Select(g => g.Name));
    });

    [Fact]
    public Task Clients_are_those_the_graphs_use_that_exist() => Headless.Run(() =>
    {
        var gone = Graph("gone");
        gone.Client = "nobody";
        var page = Page(null, Board("d", Graph(), gone));
        var tab = page.Dashboards[0];

        tab.UpdateClients();

        Assert.Equal("c", Assert.Single(tab.Clients).Name);
    });

    [Fact]
    public Task Taking_the_format_from_another_tab_copies_it_all() => Headless.Run(() =>
    {
        var page = Page(null, Board("a"), new DashboardProfile { Name = "b", Columns = 5, RowHeight = 300, Gap = 4, Background = "#111111", TabColor = "#222222" });
        var a = page.Dashboards[0];

        a.TakeFormatFrom(page.Dashboards[1]);

        Assert.Equal("b", a.Name);
        Assert.Equal(5, a.Columns);
        Assert.Equal(300, a.RowHeight);
        Assert.Equal(4, a.Gap);
        Assert.Equal("#111111", a.Background);
        Assert.Equal("#222222", a.TabColor);
    });

    [Fact]
    public Task Detaching_lets_go_of_the_graphs() => Headless.Run(() =>
    {
        var tab = Page(null, Board("d", Graph())).Dashboards[0];

        tab.Detach();

        Assert.Empty(tab.Graphs);
    });
}
