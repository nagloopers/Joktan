using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Joktan.TopicDashboards.Controls;
using Joktan.TopicDashboards.Tests.ViewModels;
using Joktan.TopicDashboards.ViewModels;
using Joktan.TopicDashboards.Views;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.TopicDashboards.Tests.Views;

public class DashboardViewsTests : DashboardTest
{
    [Fact]
    public Task The_page_shows_its_tabs_graphs_and_every_band() => Headless.Run(() =>
    {
        var graph = Graph("A graph");
        var withKeys = Graph("Keys", "t/{k}", "v", GraphKind.Gauge, new GraphKey { Name = "k", Show = KeyShow.Graph });
        var page = Page(null, Board("d", graph, withKeys), Board("second", Graph("Number", "x", "y", GraphKind.Value)));
        var source = page.SourceFor(withKeys);
        source.Receive(Message("t/a", """{"v":1}"""));
        source.Receive(Message("t/b", """{"v":2}"""));
        page.SourceFor(graph).Receive(Message("t/a", """{"temp":3}"""));
        page.Arrange(page.Dashboards[0].Profile);
        var window = Headless.Show(new TopicDashboardsView(), page, 1400, 1000);
        Assert.Contains("A graph", Headless.All<TextBlock>(window).Select(t => t.Text));

        var tab = page.Dashboards[0];
        page.IsEditing = true;
        page.IsChoosingFolder = true;
        page.FolderMessage = "A folder message";
        page.Message = "A message";
        tab.IsFormatting = true;
        tab.IsArranging = true;
        tab.IsConfirmingRemove = true;
        tab.Graphs[0].IsConfirmingRemove = true;
        tab.Graphs[0].ToggleDetailsCommand.Execute(null);
        Headless.Settle();
        window.UpdateLayout();

        page.SelectedDashboard = page.Dashboards[1];
        Headless.Settle();
        window.UpdateLayout();
        window.Close();
    });

    [Fact]
    public Task The_view_lends_the_folder_picker_and_the_editor() => Headless.Run(async () =>
    {
        var page = Page();
        var view = new TopicDashboardsView { DataContext = page };
        Assert.Null(await page.PickFolder!());
        await page.ShowEditor!(new GraphEditorViewModel(page, page.Dashboards[0]));

        var window = Headless.Show(view);
        Assert.Null(await page.PickFolder!());
        var opened = new List<GraphEditorWindow>();
        using var subscription = Window.WindowOpenedEvent.AddClassHandler<GraphEditorWindow>((w, _) => opened.Add(w));

        page.AddGraph(page.Dashboards[0]);
        await Headless.Until(() => opened.Count == 1);
        var editor = (GraphEditorViewModel)opened[0].DataContext!;
        editor.NewTopic = "t/{room}";
        await editor.AddTopicCommand.ExecuteAsync(null);
        editor.ManualPath = "v";
        editor.AddManualPathCommand.Execute(null);
        editor.UseFixedScale = true;
        Headless.Settle();
        opened[0].UpdateLayout();

        editor.CancelCommand.Execute(null);
        await Headless.Until(() => !opened[0].IsVisible);
        window.Close();
    });

    [Fact]
    public Task Double_clicking_a_tab_renames_it_and_keys_finish_or_cancel() => Headless.Run(() =>
    {
        var page = Page(null, Board("first"));
        var window = Headless.Show(new TopicDashboardsView(), page);
        var tab = page.Dashboards[0];
        var title = Headless.All<TextBlock>(window).First(t => t.Text == "first");
        var point = title.TranslatePoint(new Point(2, 2), window)!.Value;

        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Headless.Settle();
        Assert.True(tab.IsRenaming);

        // Showing the box puts the focus in it, with its text selected.
        Headless.Settle();
        var box = Headless.Find<TextBox>(window, t => t.IsEffectivelyVisible && t.DataContext == tab);
        Assert.True(box.IsFocused);

        window.KeyPressQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.None);
        Assert.True(tab.IsRenaming);
        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Assert.False(tab.IsRenaming);
        Assert.Equal("first", tab.Name);

        tab.RenameCommand.Execute(null);
        Headless.Settle();
        tab.RenameText = "typed";
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Assert.Equal("typed", tab.Name);

        tab.RenameCommand.Execute(null);
        Headless.Settle();
        tab.RenameText = "by pressing elsewhere";
        var far = new Point(window.Bounds.Width - 5, window.Bounds.Height - 5);
        window.MouseDown(far, MouseButton.Left);
        window.MouseUp(far, MouseButton.Left);
        Assert.False(tab.IsRenaming);
        Assert.Equal("by pressing elsewhere", tab.Name);

        // Pressing nothing special when not renaming changes nothing.
        window.MouseDown(far, MouseButton.Left);
        window.MouseUp(far, MouseButton.Left);
        window.Close();
    });

    [Fact]
    public Task Double_clicking_beside_a_tabs_name_renames_it_too() => Headless.Run(() =>
    {
        var page = Page(null, Board("first"));
        var window = Headless.Show(new TopicDashboardsView(), page);
        var tab = page.Dashboards[0];
        var title = Headless.All<TextBlock>(window).First(t => t.Text == "first");

        // In the padding, as a tab with no colour of its own has to catch a press there as well.
        var point = title.TranslatePoint(new Point(-6, title.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Headless.Settle();

        Assert.True(tab.IsRenaming);
        window.Close();
    });

    [Fact]
    public Task Leaving_the_box_finishes_the_name() => Headless.Run(() =>
    {
        var view = new TopicDashboardsView();
        var page = Page(null, Board("first"));
        var tab = page.Dashboards[0];
        tab.RenameCommand.Execute(null);
        tab.RenameText = "left";

        typeof(TopicDashboardsView).GetMethod("OnRenameBoxLostFocus", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(view, [new TextBox { DataContext = tab }, new RoutedEventArgs()]);
        typeof(TopicDashboardsView).GetMethod("OnRenameBoxLostFocus", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(view, [new TextBox(), new RoutedEventArgs()]);

        Assert.Equal("left", tab.Name);
    });

    private static (Window Window, DashboardTabView View) ShowTab(DashboardTabViewModel tab)
    {
        tab.IsArranging = true;
        var view = new DashboardTabView { DataContext = tab };
        var window = new Window { Width = 1300, Height = 900, Content = view };
        Headless.Show(window);
        return (window, view);
    }

    private static Point Centre(Control control, Window window) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;

    [Fact]
    public Task Dragging_the_handle_moves_a_graph_to_another_place() => Headless.Run(() =>
    {
        var page = Page(null, Board("d", Graph("a"), Graph("b"), Graph("c")));
        var tab = page.Dashboards[0];
        var (window, view) = ShowTab(tab);
        var grid = Headless.Find<DashboardGrid>(view);
        var handles = Headless.All<Control>(view).Where(c => c.DataContext == tab.Graphs[0] && HasPressHandler(c, "OnMovePressed")).ToList();
        var handle = handles.First();
        var card = tab.Graphs[0];

        // At its right-hand end, as the middle of the bar holds its buttons.
        var start = handle.TranslatePoint(new Point(handle.Bounds.Width - 3, handle.Bounds.Height / 2), window)!.Value;
        var target = grid.TranslatePoint(new Point(grid.ColumnStep * 2 + 40, 40), window)!.Value;

        // Another button does nothing.
        window.MouseDown(start, MouseButton.Right);
        window.MouseUp(start, MouseButton.Right);
        Assert.False(card.IsDragging);

        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(target, RawInputModifiers.LeftMouseButton);
        Assert.True(card.IsDragging);
        Assert.True(tab.Graphs[2].IsDropTarget);

        // Over itself nothing would move.
        window.MouseMove(start, RawInputModifiers.LeftMouseButton);
        Assert.False(tab.Graphs[2].IsDropTarget);

        window.MouseMove(target, RawInputModifiers.LeftMouseButton);
        window.MouseUp(target, MouseButton.Left);

        Assert.Equal(["b", "c", "a"], tab.Graphs.Select(g => g.Name));
        Assert.False(card.IsDragging);
        window.Close();
    });

    [Fact]
    public Task Dragging_the_corner_resizes_a_graph_in_whole_columns_and_rows() => Headless.Run(() =>
    {
        var page = Page(null, Board("d", Graph("a")));
        var tab = page.Dashboards[0];
        var (window, view) = ShowTab(tab);
        var grid = Headless.Find<DashboardGrid>(view);
        var handle = Headless.All<Control>(view).First(c => c.DataContext == tab.Graphs[0] && HasPressHandler(c, "OnResizePressed"));
        var card = tab.Graphs[0];
        var start = Centre(handle, window);

        window.MouseDown(start, MouseButton.Right);
        window.MouseUp(start, MouseButton.Right);
        Assert.Equal(1, card.WidthSpan);

        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(start + new Point(grid.ColumnStep, grid.RowStep), RawInputModifiers.LeftMouseButton);
        window.MouseUp(start + new Point(grid.ColumnStep, grid.RowStep), MouseButton.Left);

        Assert.Equal(2, card.WidthSpan);
        Assert.Equal(2, card.HeightSpan);
        Assert.Equal(2, card.Profile.Width);
        window.Close();
    });

    private static bool HasPressHandler(Control control, string name)
    {
        var field = typeof(Interactive).GetField("_eventHandlers", BindingFlags.NonPublic | BindingFlags.Instance);
        if (field?.GetValue(control) is not System.Collections.IDictionary handlers)
            return false;
        foreach (var list in handlers.Values)
        {
            foreach (var entry in (System.Collections.IEnumerable)list)
            {
                var handler = entry.GetType().GetProperty("Handler")?.GetValue(entry) as Delegate;
                if (handler?.Method.Name == name)
                    return true;
            }
        }

        return false;
    }

    [Fact]
    public Task Saying_nothing_to_the_pointer_handlers_is_safe() => Headless.Run(() =>
    {
        var page = Page(null, Board("d", Graph("a")));
        var (window, view) = ShowTab(page.Dashboards[0]);
        var pressed = typeof(DashboardTabView).GetMethod("OnMoveCaptureLost", BindingFlags.NonPublic | BindingFlags.Instance)!;

        pressed.Invoke(view, [null, null]);

        Assert.False(page.Dashboards[0].Graphs[0].IsDragging);
        window.Close();
    });

    [Fact]
    public Task Closing_the_editor_stops_it_listening() => Headless.Run(() =>
    {
        var page = Page();
        var editor = new GraphEditorViewModel(page, page.Dashboards[0]);
        var window = new GraphEditorWindow { DataContext = editor };
        Headless.Show(window);

        window.Close();

        Assert.False(editor.IsListening);
    });

    [Fact]
    public Task A_graph_card_shows_a_status_and_a_legend() => Headless.Run(() =>
    {
        var profile = Graph();
        profile.Values.Add(new GraphValue { Topic = "t/a", Path = "other", Color = 1 });
        var card = new GraphCardViewModel(profile, new GraphData()) { Status = "Waiting" };
        var window = Headless.Show(new GraphCardView(), card);

        Assert.Contains("Waiting", Headless.All<TextBlock>(window).Select(t => t.Text));
        Assert.Contains("other", Headless.All<TextBlock>(window).Select(t => t.Text));
        window.Close();
    });
}
