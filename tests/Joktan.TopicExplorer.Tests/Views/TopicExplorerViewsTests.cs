using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Joktan.Mqtt.Tests;
using Joktan.TopicDashboards.Views;
using Joktan.TopicExplorer.Tests.ViewModels;
using Joktan.TopicExplorer.Views;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.TopicExplorer.Tests.Views;

public class TopicExplorerViewsTests : ExplorerTest
{
    private static void Invoke<T>(T view, string handler) =>
        typeof(T).GetMethod(handler, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(view, [null, null]);

    [Fact]
    public Task Shows_each_tab_with_its_tree_message_and_trends() => Headless.Run(async () =>
    {
        await using var broker = await TestBroker.StartAsync();
        var mqtt = Mqtt(broker.Port, "one", "two");
        var explorer = Explorer(mqtt);
        var window = Headless.Show(new TopicExplorerView(), explorer, 1400, 1000);
        var tab = explorer.SelectedTab!;
        Assert.Equal("#", tab.Filters);

        await Connect(tab.Client);
        await Headless.Until(() => tab.SubscribedFilters is not null);
        await broker.SendAsync("a/b", """{"v":1,"w":[1,2]}""");
        await broker.SendAsync("a/b", """{"v":2,"w":[3,4]}""");
        await Headless.Until(() => tab.Summary.Contains("2 messages"));
        tab.SelectedNode = tab.Roots[0].Children[0];
        tab.SelectedNode.IsChoosing = true;
        Headless.Settle();
        window.UpdateLayout();
        tab.PublishStatus = "A status";
        tab.DashboardStatus = "A status";
        tab.FilterError = "A problem";
        Headless.Settle();
        window.UpdateLayout();

        Assert.Contains("a", Headless.All<TextBlock>(window).Select(t => t.Text));
        await tab.Client.DisconnectCommand.ExecuteAsync(null);
        window.Close();
    });

    [Fact]
    public Task Moves_to_a_window_of_its_own_and_back() => Headless.Run(() =>
    {
        var explorer = Explorer(Mqtt());
        var window = Headless.Show(new TopicExplorerView(), explorer);
        var opened = new List<TopicExplorerWindow>();
        using var subscription = Window.WindowOpenedEvent.AddClassHandler<TopicExplorerWindow>((w, _) => opened.Add(w));

        explorer.PopOutCommand.Execute(null);
        Headless.Settle();
        var own = Assert.Single(opened);
        Assert.True(explorer.IsPoppedOut);

        explorer.PopOutCommand.Execute(null);
        Assert.Single(opened);

        explorer.DockCommand.Execute(null);
        Headless.Settle();
        Assert.False(explorer.IsPoppedOut);
        Assert.Null(explorer.CloseWindow);
        Assert.False(own.IsVisible);
        window.Close();
    });

    [Fact]
    public Task Its_window_has_buttons_of_its_own() => Headless.Run(() =>
    {
        var window = new TopicExplorerWindow { DataContext = Explorer(Mqtt()), Width = 900, Height = 600 };
        Headless.Show(window);
        var maximize = window.FindControl<Button>("MaximizeButton")!;

        Headless.Click(maximize);
        Assert.Equal(WindowState.Maximized, window.WindowState);
        Assert.Equal("Restore", ToolTip.GetTip(maximize));
        window.Show();
        Headless.Settle();
        Headless.Click(maximize);
        Assert.Equal(WindowState.Normal, window.WindowState);

        var point = new Point(window.Bounds.Width / 2, 12);
        window.MouseDown(point, MouseButton.Right);
        window.MouseUp(point, MouseButton.Right);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Assert.Equal(WindowState.Maximized, window.WindowState);
        window.WindowState = WindowState.Normal;
        Headless.Settle();

        var buttons = Headless.All<Button>(window).ToList();
        var at = buttons.IndexOf(maximize);
        Headless.Click(buttons[at - 1]);
        Assert.Equal(WindowState.Minimized, window.WindowState);
        window.WindowState = WindowState.Normal;
        Headless.Settle();
        var closed = false;
        window.Closed += (_, _) => closed = true;
        Headless.Click(buttons[at + 1]);
        Assert.True(closed);
    });

    [Fact]
    public Task A_topic_can_be_copied_and_added_to_a_dashboard() => Headless.Run(async () =>
    {
        await using var broker = await TestBroker.StartAsync();
        var mqtt = Mqtt(broker.Port);
        var explorer = Explorer(mqtt);
        var tab = explorer.Tabs[0];
        var panel = new ClientExplorerPanel { DataContext = tab };

        // Off the screen and with nothing selected, neither does anything.
        Invoke(panel, "OnCopyTopicClick");
        Invoke(panel, "OnAddToDashboardClick");

        var window = Headless.Show(panel);
        Invoke(panel, "OnCopyTopicClick");
        Invoke(panel, "OnAddToDashboardClick");

        tab.Activate();
        await Connect(tab.Client);
        await Headless.Until(() => tab.SubscribedFilters is not null);
        await broker.SendAsync("a", """{"v":1}""");
        await Headless.Until(() => tab.Roots.Count == 1);
        tab.SelectedNode = tab.Roots[0];

        Invoke(panel, "OnCopyTopicClick");
        Headless.Settle();
        Assert.Equal("a", await window.Clipboard!.TryGetTextAsync());

        var opened = new List<GraphEditorWindow>();
        using var subscription = Window.WindowOpenedEvent.AddClassHandler<GraphEditorWindow>((w, _) => opened.Add(w));
        Invoke(panel, "OnAddToDashboardClick");
        await Headless.Until(() => opened.Count == 1);
        var editor = (Joktan.TopicDashboards.ViewModels.GraphEditorViewModel)opened[0].DataContext!;
        await editor.SaveCommand.ExecuteAsync(null);
        await Headless.Until(() => tab.DashboardStatus.Length > 0);
        Assert.StartsWith("Added to ", tab.DashboardStatus);

        // Cancelled, there is nothing to say.
        tab.SelectedNode = null;
        tab.SelectedNode = tab.Roots[0];
        Invoke(panel, "OnAddToDashboardClick");
        await Headless.Until(() => opened.Count == 2);
        ((Joktan.TopicDashboards.ViewModels.GraphEditorViewModel)opened[1].DataContext!).CancelCommand.Execute(null);
        await Headless.Until(() => !opened[1].IsVisible);
        Assert.Equal("", tab.DashboardStatus);
        await tab.Client.DisconnectCommand.ExecuteAsync(null);
        window.Close();
    });
}
