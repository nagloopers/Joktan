using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using Joktan.Agent.Mcp;
using Joktan.Agent.Tools;
using Joktan.Containers;
using Joktan.Dashboard.Telemetry;
using Joktan.Pomodoro;
using Joktan.Pomodoro.ViewModels;
using Joktan.Tests.TestSupport;
using Joktan.ViewModels;
using Joktan.Views;
using Xunit;

namespace Joktan.Tests.ViewModels;

// With speech switched off, no phase ending here reaches the computer's real voice.
[Collection(ProcessStateCollection.Name)]
public class PomodoroWiringTests : DataFolderTest, IDisposable
{
    private readonly MachineMonitor _monitor = new();

    private sealed class FixedClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private MainViewModel New()
    {
        new ComposeSettings { ComposeFiles = [] }.Save();
        new PomodoroSettings { SpeakAtEnd = false }.Save();
        return new MainViewModel(_monitor);
    }

    void IDisposable.Dispose()
    {
        _monitor.Dispose();
        base.Dispose();
    }

    [Fact]
    public Task The_menu_has_the_page_with_its_help_after_passwords() => Headless.Run(() =>
    {
        var main = New();
        var titles = main.MenuItems.Select(i => i.Title).ToList();
        Assert.Equal(titles.IndexOf("Passwords") + 1, titles.IndexOf("Pomodoro"));

        main.OpenPomodoroCommand.Execute(null);

        Assert.Same(main.Pomodoro, main.CurrentPage);
        Assert.Equal("pomodoro", main.CurrentHelpTopic);
    });

    [Fact]
    public Task A_phase_that_ends_adds_an_alert_to_the_top_bar() => Headless.Run(() =>
    {
        var main = New();
        var clock = new FixedClock();
        main.Pomodoro.Clock = clock;
        main.Pomodoro.StartPauseCommand.Execute(null);

        clock.Now += main.Pomodoro.Remaining;
        main.Pomodoro.Tick();

        Assert.Equal(1, main.Inbox.UnreadAlerts);
        var alert = main.Inbox.Alerts.Single();
        Assert.Equal("Pomodoro", alert.Title);
        Assert.Equal("Focus is over. Time for a short break.", alert.Text);
        Assert.Equal("Info", alert.Level);
    });

    [Fact]
    public Task The_top_bar_shows_a_phase_under_way_and_opens_the_page() => Headless.Run(() =>
    {
        var main = New();
        var clock = new FixedClock();
        main.Pomodoro.Clock = clock;
        var window = Headless.Show(new MainWindow { DataContext = main });
        var button = Headless.Find<Button>(window, b => b.Name == "PomodoroButton");
        Assert.False(button.IsVisible);

        main.Pomodoro.StartPauseCommand.Execute(null);
        clock.Now += TimeSpan.FromMinutes(1);
        main.Pomodoro.Tick();
        Headless.Settle();
        Assert.True(button.IsVisible);
        Assert.Equal("Pomodoro: Focus 1 of 4, 24:00 left", ToolTip.GetTip(button));

        button.Command!.Execute(null);
        Assert.Same(main.Pomodoro, main.CurrentPage);

        main.Pomodoro.ResetCommand.Execute(null);
        Headless.Settle();
        Assert.False(button.IsVisible);
        window.Close();
    });

    [Fact]
    public Task The_agent_can_read_control_and_set_the_timer() => Headless.Run(async () =>
    {
        var pomodoro = new PomodoroViewModel(new PomodoroSettings());
        var tools = PomodoroTools.Create(pomodoro).ToDictionary(t => t.Name);
        Assert.Equal(ToolRisk.Read, tools["pomodoro_status"].Risk);

        await Run(tools["pomodoro_control"], """{ "action": "start" }""");
        Assert.True(pomodoro.IsRunning);
        await Run(tools["pomodoro_control"], """{ "action": "start" }""");
        Assert.True(pomodoro.IsRunning);
        await Run(tools["pomodoro_control"], """{ "action": "pause" }""");
        Assert.False(pomodoro.IsRunning);
        await Run(tools["pomodoro_control"], """{ "action": "skip" }""");
        Assert.Equal(PomodoroPhase.ShortBreak, pomodoro.Phase);
        await Assert.ThrowsAsync<ToolFailure>(() => Run(tools["pomodoro_control"], """{ "action": "explode" }"""));

        await Run(tools["pomodoro_settings"], """{ "shortBreakMinutes": 7, "autoStart": true }""");
        Assert.Equal("07:00", pomodoro.RemainingText);
        Assert.True(pomodoro.AutoStart);
        Assert.Equal(25, pomodoro.FocusMinutes);

        var status = JsonSerializer.Serialize(await Run(tools["pomodoro_status"], "{}"));
        Assert.Contains("\"Phase\":\"Short break\"", status);
    });

    private static Task<object?> Run(McpTool tool, string arguments) => tool.RunAsync(new ToolArgs(JsonDocument.Parse(arguments).RootElement));
}
