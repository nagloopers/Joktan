using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Input;
using Joktan.Containers;
using Joktan.Core.Controls;
using Joktan.Core;
using Joktan.Core.ViewModels;
using Joktan.Dashboard.Telemetry;
using Joktan.Help.Views;
using Joktan.Settings.Themes;
using Joktan.Tests.TestSupport;
using Joktan.Mqtt;
using Joktan.ViewModels;
using Joktan.Views;
using Xunit;
using Joktan.Settings;
using Joktan.Settings.ViewModels;

namespace Joktan.Tests.Views;

[Collection(ProcessStateCollection.Name)]
public class AppWindowsTests : DataFolderTest, IDisposable
{
    private readonly MachineMonitor _monitor = new();
    private readonly string? _endpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");

    void IDisposable.Dispose()
    {
        Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT", _endpoint);
        _monitor.Dispose();
        base.Dispose();
    }

    [Fact]
    public Task The_top_bar_opens_messages_and_alerts_and_marks_them_read() => Headless.Run(() =>
    {
        var window = Main(out var main);
        Headless.Show(window);

        var messages = window.FindControl<Button>("MessagesButton")!;
        messages.Flyout!.ShowAt(messages);
        Headless.Settle();
        Assert.True(main.Inbox.IsMessagesOpen);
        Assert.Same(main.Inbox, ((Control)((Flyout)messages.Flyout).Content!).DataContext);
        messages.Flyout.Hide();
        Headless.Settle();
        Assert.False(main.Inbox.IsMessagesOpen);

        var alerts = window.FindControl<Button>("AlertsButton")!;
        alerts.Flyout!.ShowAt(alerts);
        Headless.Settle();
        Assert.True(main.Inbox.IsAlertsOpen);

        main.Inbox.ShowSettingsCommand.Execute(null);
        Headless.Settle();
        Assert.Equal("Alerts and Messages", main.CurrentPage!.Title);
        Assert.False(main.Inbox.IsAlertsOpen);
        window.Close();
    });

    private MainWindow Main(out MainViewModel main)
    {
        new ComposeSettings { ComposeFiles = [] }.Save();
        main = new MainViewModel(_monitor);
        var window = new MainWindow { DataContext = main, Width = 1400, Height = 900 };
        window.DataTemplates.Add(new ViewLocator());
        Headless.Show(window);
        return window;
    }

    private static IEnumerable<NavItemViewModel> Leaves(IEnumerable<NavItemViewModel> items) =>
        items.SelectMany(i => i.HasChildren ? Leaves(i.Children) : [i]);

    [Fact]
    public Task Every_page_of_the_menu_opens_in_the_main_window() => Headless.Run(() =>
    {
        var window = Main(out var main);

        foreach (var item in Leaves(main.MenuItems))
        {
            main.NavigateCommand.Execute(item);
            Headless.Settle();
            window.UpdateLayout();

            Assert.DoesNotContain(Headless.All<TextBlock>(window), t => t.Text?.StartsWith("Not Found", StringComparison.Ordinal) == true);
        }

        main.ToggleSidebarCommand.Execute(null);
        Headless.Settle();
        main.SaveState();
        window.Close();
    });

    [Fact]
    public Task The_main_window_has_buttons_of_its_own() => Headless.Run(() =>
    {
        var window = Main(out _);
        var maximize = window.FindControl<Button>("MaximizeButton")!;

        Headless.Click(maximize);
        Assert.Equal(WindowState.Maximized, window.WindowState);
        Assert.Equal("Restore", ToolTip.GetTip(maximize));
        window.Show();
        Headless.Settle();
        Headless.Click(maximize);
        Assert.Equal(WindowState.Normal, window.WindowState);
        Assert.Equal("Maximise", ToolTip.GetTip(maximize));

        var bar = window.FindControl<Control>("TopBar")!;
        var point = bar.TranslatePoint(new Point(bar.Bounds.Width / 2 - 200, 5), window)!.Value;
        window.MouseDown(point, MouseButton.Right);
        window.MouseUp(point, MouseButton.Right);
        Assert.Equal(WindowState.Normal, window.WindowState);
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
    public Task The_about_window_gives_the_version_and_closes() => Headless.Run(() =>
    {
        var window = new AboutWindow();
        Headless.Show(window);
        Assert.Contains(Headless.All<TextBlock>(window), t => t.Text?.StartsWith("Version ", StringComparison.Ordinal) == true);
        var closed = false;
        window.Closed += (_, _) => closed = true;

        Headless.Click(Headless.Find<Button>(window));

        Assert.True(closed);
    });

    [Fact]
    public Task The_splash_says_what_it_is_loading() => Headless.Run(() =>
    {
        var window = new SplashWindow();
        Headless.Show(window);

        window.Status = "Loading";
        Assert.Equal("Loading", window.Status);
        Assert.Contains(Headless.All<TextBlock>(window), t => t.Text == "Loading");

        window.Status = null!;
        Assert.Equal("", window.Status);
        window.Close();
    });

    [Fact]
    public Task The_view_locator_says_when_there_is_no_view() => Headless.Run(() =>
    {
        var built = new ViewLocator().Build(new NoSuchPageViewModel());

        Assert.StartsWith("Not Found: ", Assert.IsType<TextBlock>(built).Text);
    });

    private sealed class NoSuchPageViewModel : ViewModelBase;

    // Says the app is closing without closing it, as that would end the test's own dispatcher too.
    private static void RaiseExit(ClassicDesktopStyleApplicationLifetime lifetime)
    {
        var exit = (Delegate)typeof(ClassicDesktopStyleApplicationLifetime).GetField("Exit", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(lifetime)!;
        exit.DynamicInvoke(lifetime, new ControlledApplicationLifetimeExitEventArgs(0));
    }

    [Fact]
    public Task Starting_loads_everything_behind_the_splash_then_shows_the_main_window() => Headless.Run(async () =>
    {
        // A closed port on this machine, so that no telemetry goes anywhere.
        Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT", "http://127.0.0.1:9");
        new ComposeSettings { ComposeFiles = [] }.Save();
        new MqttSettings { Messaging = new MessagingSettings { Server = new ClientProfile { Name = "server", Host = "127.0.0.1", Port = "9", ClientId = "server" } } }.Save();
        var lifetime = new ClassicDesktopStyleApplicationLifetime();
        var splash = new SplashWindow();
        Headless.Show(splash);
        lifetime.MainWindow = splash;
        var themes = ThemeEditor.Load();
        var start = typeof(App).GetMethod("StartAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
        var opened = new List<HelpWindow>();
        using var subscription = Window.WindowOpenedEvent.AddClassHandler<HelpWindow>((w, _) => opened.Add(w));

        await (Task)start.Invoke(null, [lifetime, splash, themes])!;

        var window = Assert.IsType<MainWindow>(lifetime.MainWindow);

        // The alerts and messages server is tried as soon as the app has started. Disconnecting ends the retries.
        var inbox = ((MainViewModel)window.DataContext!).Inbox;
        await Headless.Until(() => inbox.Client.IsFailed);
        await inbox.Client.DisconnectCommand.ExecuteAsync(null);
        Assert.False(splash.IsVisible);
        Headless.Settle();

        // F1 opens the help on what is showing, except in the help window itself.
        window.KeyPressQwerty(PhysicalKey.F1, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.None);
        Headless.Settle();
        var help = Assert.Single(opened);
        help.KeyPressQwerty(PhysicalKey.F1, RawInputModifiers.None);
        Assert.Single(opened);
        help.Close();

        RaiseExit(lifetime);
        window.Close();
        HelpContext.Opener = null;
    });

    [Fact]
    public Task The_app_loads_its_own_styles_and_answers_its_menu() => Headless.Run(() =>
    {
        var app = new App();

        app.Initialize();

        Assert.NotEmpty(app.Styles);
        var opened = new List<string?>();
        HelpContext.Opener = opened.Add;
        try
        {
            typeof(App).GetMethod("OnHelpClick", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(app, [null, EventArgs.Empty]);
            typeof(App).GetMethod("OnAboutClick", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(app, [null, EventArgs.Empty]);
        }
        finally
        {
            HelpContext.Opener = null;
        }

        Assert.Equal([null], opened);
    });

    [Fact]
    public Task The_about_window_opens_over_the_main_window() => Headless.Run(async () =>
    {
        var owner = new Window();
        Headless.Show(owner);
        var app = new App { ApplicationLifetime = new ClassicDesktopStyleApplicationLifetime { MainWindow = owner } };
        var opened = new List<AboutWindow>();
        using var subscription = Window.WindowOpenedEvent.AddClassHandler<AboutWindow>((w, _) => opened.Add(w));

        typeof(App).GetMethod("OnAboutClick", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(app, [null, EventArgs.Empty]);
        await Headless.Until(() => opened.Count == 1);

        opened[0].Close();
        owner.Close();
    });

    private static void SaveHistory() => new MessagingHistory
    {
        Messages = Enumerable.Range(0, 60)
            .Select(i => new ChatMessage { Id = $"{i}", Sender = "other", Name = "alice", Text = i == 30 ? "the middle needle" : $"message {i}", Sent = DateTimeOffset.Now })
            .ToList(),
        Alerts = [new AlertEntry { Title = "Disk full", Text = "needle in the alert", Severity = AlertSeverity.Critical, Received = DateTimeOffset.Now }],
    }.Save();

    [Fact]
    public Task Typing_in_the_search_box_shows_results_and_keys_work_them() => Headless.Run(() =>
    {
        SaveHistory();
        var window = Main(out var main);
        var box = window.FindControl<TextBox>("SearchBox")!;
        var popup = window.FindControl<Avalonia.Controls.Primitives.Popup>("SearchResults")!;
        Assert.Equal("Search notes, help, messages and alerts...", box.PlaceholderText);
        Assert.False(popup.IsOpen);

        box.Focus();
        window.KeyTextInput("needle");
        Headless.Settle();

        Assert.Equal("needle", main.Search.Text);
        Assert.True(popup.IsOpen);
        Assert.True(box.IsFocused);
        Assert.Equal(["Messages", "Alerts"], main.Search.Results.Select(h => h.Area));
        Assert.Same(main.Search.Results[0], main.Search.Highlighted);

        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        Headless.Settle();
        Assert.Same(main.Search.Results[1], main.Search.Highlighted);
        window.KeyPressQwerty(PhysicalKey.ArrowUp, RawInputModifiers.None);
        Headless.Settle();
        Assert.Same(main.Search.Results[0], main.Search.Highlighted);

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Headless.Settle();
        Assert.False(popup.IsOpen);
        Assert.Equal("needle", main.Search.Text);

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Headless.Settle();
        Assert.True(popup.IsOpen);

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Headless.Settle();
        Assert.False(popup.IsOpen);
        Assert.True(main.Inbox.IsMessagesOpen);
        window.FindControl<Button>("MessagesButton")!.Flyout!.Hide();
        Headless.Settle();

        box.Focus();
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Headless.Settle();
        Assert.Null(main.Search.Text);
        Assert.False(popup.IsOpen);
    });

    [Fact]
    public Task Opening_a_message_from_search_scrolls_its_panel_to_it() => Headless.Run(() =>
    {
        SaveHistory();
        var window = Main(out var main);

        main.Search.Text = "middle needle";
        Headless.Settle();
        main.Search.OpenCommand.Execute(main.Search.Results.First(h => h.Area == "Messages"));
        Headless.Settle();

        var button = window.FindControl<Button>("MessagesButton")!;
        var panel = (Control)((Flyout)button.Flyout!).Content!;
        Assert.True(main.Inbox.IsMessagesOpen);
        Assert.True(main.Inbox.Messages[30].IsFound);
        var scroller = panel.FindControl<ScrollViewer>("Scroller")!;
        Assert.True(scroller.Offset.Y > 0);
        Assert.True(scroller.Offset.Y < scroller.Extent.Height - scroller.Viewport.Height - 1);

        button.Flyout.Hide();
        Headless.Settle();
        Assert.False(main.Inbox.Messages[30].IsFound);
    });

    [Fact]
    public Task Opening_an_alert_from_search_opens_its_panel() => Headless.Run(() =>
    {
        SaveHistory();
        var window = Main(out var main);

        main.Search.Text = "disk";
        Headless.Settle();
        main.Search.OpenCommand.Execute(main.Search.Results.First(h => h.Area == "Alerts"));
        Headless.Settle();

        Assert.True(main.Inbox.IsAlertsOpen);
        window.FindControl<Button>("AlertsButton")!.Flyout!.Hide();
        Headless.Settle();
    });

    [Fact]
    public Task Clicking_a_result_opens_it_and_clicking_the_box_brings_them_back() => Headless.Run(() =>
    {
        var window = Main(out var main);
        var box = window.FindControl<TextBox>("SearchBox")!;
        var popup = window.FindControl<Avalonia.Controls.Primitives.Popup>("SearchResults")!;

        main.Search.Text = "Shortcut";
        Headless.Settle();
        Assert.True(popup.IsOpen);

        var list = TopLevel.GetTopLevel(popup.Child!)!;
        list.CaptureRenderedFrame();
        Headless.Settle();
        var item = Headless.Find<ListBoxItem>(list, i => i.DataContext is SearchHit { Area: "Notes" });
        var point = item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), list)!.Value;
        list.MouseMove(point);
        list.MouseDown(point, MouseButton.Left);
        list.MouseUp(point, MouseButton.Left);
        Headless.Settle();

        Assert.False(popup.IsOpen);
        Assert.Equal("Notes", main.CurrentPage!.Title);

        Headless.Click(box);
        Assert.True(popup.IsOpen);

        // Pressing somewhere else hides them.
        var sidebar = window.FindControl<Border>("Sidebar")!;
        Headless.Click(sidebar);
        Assert.False(popup.IsOpen);
    });

    [Fact]
    public Task Switching_areas_off_on_the_settings_page_changes_the_results_and_the_hint() => Headless.Run(() =>
    {
        SaveHistory();
        var window = Main(out var main);
        var box = window.FindControl<TextBox>("SearchBox")!;
        var page = Assert.IsType<SearchSettingsViewModel>(Leaves(main.MenuItems).First(i => i.Title == "Search").Page);

        main.Search.Text = "needle";
        Headless.Settle();
        Assert.Equal(2, main.Search.Results.Count);

        page.Messages = false;
        Headless.Settle();
        Assert.Equal(["Alerts"], main.Search.Results.Select(h => h.Area));
        Assert.Equal("Search notes, help and alerts...", box.PlaceholderText);

        page.Notes = page.Help = page.Alerts = false;
        Headless.Settle();
        Assert.Empty(main.Search.Results);
        Assert.Equal("Search is switched off", box.PlaceholderText);
        Assert.True(main.Search.IsAllOff);
        Assert.False(main.Search.HasNoMatches);

        main.Search.ShowSettingsCommand.Execute(null);
        Headless.Settle();
        Assert.Same(page, main.CurrentPage);
        Assert.False(main.Search.IsOpen);
        Assert.False(SearchSettings.Load().Alerts);
    });

    [Fact]
    public Task The_results_mark_the_words_and_the_clear_button_resets_everything() => Headless.Run(() =>
    {
        var window = Main(out var main);
        var box = window.FindControl<TextBox>("SearchBox")!;
        var clear = window.FindControl<Button>("ClearSearchButton")!;
        var popup = window.FindControl<Avalonia.Controls.Primitives.Popup>("SearchResults")!;
        Assert.False(clear.IsVisible);

        main.Search.Text = "Shortcut";
        Headless.Settle();
        var root = TopLevel.GetTopLevel(popup.Child!)!;
        root.CaptureRenderedFrame();
        Headless.Settle();

        var marked = Headless.All<TextBlock>(root).SelectMany(t => t.Inlines?.OfType<Avalonia.Controls.Documents.Run>() ?? [])
            .Where(r => r.Classes.Contains(TextHighlight.MatchClass)).Select(r => r.Text).ToList();
        Assert.Contains("Shortcut", marked);
        Assert.True(clear.IsVisible);

        main.Search.OpenCommand.Execute(main.Search.Results.First(h => h.Area == "Notes"));
        Headless.Settle();
        var notes = Assert.IsType<Joktan.Notes.ViewModels.NotesViewModel>(main.CurrentPage);
        Assert.Equal(["Shortcut"], notes.HighlightWords);

        window.CaptureRenderedFrame();
        Headless.Settle();
        Headless.Click(clear);

        Assert.Null(main.Search.Text);
        Assert.False(popup.IsOpen);
        Assert.True(box.IsFocused);
        Assert.False(clear.IsVisible);
        Assert.Null(notes.HighlightWords);
        window.Close();
    });
}
