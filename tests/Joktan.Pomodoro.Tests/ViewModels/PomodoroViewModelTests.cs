using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Joktan.Pomodoro.ViewModels;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Pomodoro.Tests.ViewModels;

[Collection(ProcessStateCollection.Name)]
public class PomodoroViewModelTests : DataFolderTest
{
    private sealed class FakeClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private readonly FakeClock _clock = new();
    private readonly List<string> _alerts = [];
    private readonly List<string> _spoken = [];

    private PomodoroViewModel New(PomodoroSettings? settings = null) => new(settings ?? new PomodoroSettings())
    {
        Clock = _clock,
        AddAlert = (title, text) => _alerts.Add($"{title}: {text}"),
        Speak = _spoken.Add,
    };

    private void Pass(PomodoroViewModel vm, TimeSpan time)
    {
        _clock.Now += time;
        vm.Tick();
    }

    // Starts the phase shown and lets it run out.
    private void RunOut(PomodoroViewModel vm)
    {
        if (!vm.IsRunning)
            vm.StartPauseCommand.Execute(null);
        Pass(vm, vm.Remaining);
    }

    [Fact]
    public Task Begins_on_the_first_focus_period_and_not_running() => Headless.Run(() =>
    {
        var vm = New();

        Assert.Equal(PomodoroPhase.Focus, vm.Phase);
        Assert.Equal("Focus 1 of 4", vm.PhaseName);
        Assert.Equal("25:00", vm.RemainingText);
        Assert.Equal("Start", vm.StartPauseText);
        Assert.False(vm.IsRunning);
        Assert.False(vm.IsPartway);
        Assert.Equal(0, vm.Progress);
    });

    [Fact]
    public Task Counts_down_from_the_clock_rounding_up_to_a_second() => Headless.Run(() =>
    {
        var vm = New();
        vm.StartPauseCommand.Execute(null);

        Pass(vm, TimeSpan.FromSeconds(0.5));
        Assert.Equal("25:00", vm.RemainingText);

        Pass(vm, TimeSpan.FromMinutes(5));
        Assert.Equal("20:00", vm.RemainingText);
        Assert.Equal("Pause", vm.StartPauseText);
        Assert.True(vm.IsPartway);
        Assert.InRange(vm.Progress, 20, 21);
        vm.ResetCommand.Execute(null);
    });

    [Fact]
    public Task Pausing_stops_the_time_and_resuming_carries_on() => Headless.Run(() =>
    {
        var vm = New();
        vm.StartPauseCommand.Execute(null);
        Pass(vm, TimeSpan.FromMinutes(5));

        vm.StartPauseCommand.Execute(null);
        Assert.False(vm.IsRunning);
        Assert.Equal("Resume", vm.StartPauseText);

        Pass(vm, TimeSpan.FromMinutes(10));
        Assert.Equal("20:00", vm.RemainingText);

        vm.StartPauseCommand.Execute(null);
        Pass(vm, TimeSpan.FromMinutes(1));
        Assert.Equal("19:00", vm.RemainingText);
        vm.ResetCommand.Execute(null);
    });

    [Fact]
    public Task Goes_through_a_whole_cycle() => Headless.Run(() =>
    {
        var vm = New();
        var seen = new List<string>();
        for (var i = 0; i < 9; i++)
        {
            seen.Add(vm.PhaseName);
            RunOut(vm);
        }

        Assert.Equal(
            ["Focus 1 of 4", "Short break", "Focus 2 of 4", "Short break", "Focus 3 of 4", "Short break", "Focus 4 of 4", "Long break", "Focus 1 of 4"],
            seen);
        Assert.Equal("Short break", vm.PhaseName);
        Assert.Equal(5, vm.DoneToday);
    });

    [Fact]
    public Task A_phase_that_runs_out_waits_and_announces_itself_once() => Headless.Run(() =>
    {
        var vm = New();

        RunOut(vm);

        Assert.False(vm.IsRunning);
        Assert.Equal(PomodoroPhase.ShortBreak, vm.Phase);
        Assert.Equal("05:00", vm.RemainingText);
        Assert.Equal("Focus is over. Time for a short break.", vm.Message);
        Assert.Equal(["Pomodoro: Focus is over. Time for a short break."], _alerts);
        Assert.Equal(["Focus is over. Time for a short break."], _spoken);

        vm.Tick();
        Assert.Single(_alerts);
    });

    [Fact]
    public Task A_phase_slept_through_ends_once_and_the_next_begins_in_full() => Headless.Run(() =>
    {
        var vm = New(new PomodoroSettings { AutoStart = true });
        vm.StartPauseCommand.Execute(null);

        Pass(vm, TimeSpan.FromHours(8));

        Assert.Equal(PomodoroPhase.ShortBreak, vm.Phase);
        Assert.Equal("05:00", vm.RemainingText);
        Assert.True(vm.IsRunning);
        Assert.Equal(1, vm.DoneToday);
        Assert.Single(_alerts);
        Assert.NotNull(vm.Message);
        vm.ResetCommand.Execute(null);
    });

    [Fact]
    public Task The_switches_silence_each_announcement() => Headless.Run(() =>
    {
        var vm = New();
        vm.AlertAtEnd = false;
        RunOut(vm);
        Assert.Empty(_alerts);
        Assert.Single(_spoken);

        vm.AlertAtEnd = true;
        vm.SpeakAtEnd = false;
        RunOut(vm);
        Assert.Single(_alerts);
        Assert.Single(_spoken);
        Assert.Equal("The break is over. Time to focus.", vm.Message);
    });

    [Fact]
    public Task Reset_goes_back_to_the_start_of_the_phase_and_keeps_the_cycle() => Headless.Run(() =>
    {
        var vm = New();
        RunOut(vm);
        RunOut(vm);
        vm.StartPauseCommand.Execute(null);
        Pass(vm, TimeSpan.FromMinutes(5));

        vm.ResetCommand.Execute(null);

        Assert.False(vm.IsRunning);
        Assert.False(vm.IsPartway);
        Assert.Equal("Focus 2 of 4", vm.PhaseName);
        Assert.Equal("25:00", vm.RemainingText);
    });

    [Fact]
    public Task Skipping_counts_nothing_and_announces_nothing() => Headless.Run(() =>
    {
        var vm = New();

        vm.SkipCommand.Execute(null);
        Assert.Equal(PomodoroPhase.ShortBreak, vm.Phase);

        vm.SkipCommand.Execute(null);
        Assert.Equal("Focus 1 of 4", vm.PhaseName);
        Assert.Equal(0, vm.DoneToday);
        Assert.Empty(_alerts);
        Assert.Empty(_spoken);
    });

    [Fact]
    public Task Skipping_a_long_break_begins_a_new_cycle() => Headless.Run(() =>
    {
        var vm = New(new PomodoroSettings { FocusesBeforeLongBreak = 1 });
        RunOut(vm);
        Assert.Equal(PomodoroPhase.LongBreak, vm.Phase);
        Assert.Equal("Focus is over. Time for a long break.", vm.Message);

        vm.SkipCommand.Execute(null);

        Assert.Equal("Focus 1 of 1", vm.PhaseName);
    });

    [Fact]
    public Task A_skip_starts_the_next_phase_when_it_starts_by_itself() => Headless.Run(() =>
    {
        var vm = New(new PomodoroSettings { AutoStart = true });

        vm.SkipCommand.Execute(null);

        Assert.True(vm.IsRunning);
        vm.ResetCommand.Execute(null);
    });

    [Fact]
    public Task A_new_length_applies_at_once_only_to_a_phase_not_begun() => Headless.Run(() =>
    {
        var vm = New();

        vm.FocusMinutes = 50;
        Assert.Equal("50:00", vm.RemainingText);

        vm.StartPauseCommand.Execute(null);
        Pass(vm, TimeSpan.FromMinutes(10));
        vm.FocusMinutes = 25;
        Assert.Equal("40:00", vm.RemainingText);

        vm.StartPauseCommand.Execute(null);
        vm.FocusMinutes = 30;
        Assert.Equal("40:00", vm.RemainingText);

        vm.ResetCommand.Execute(null);
        Assert.Equal("30:00", vm.RemainingText);
    });

    [Fact]
    public Task Settings_are_kept_in_range_and_saved() => Headless.Run(() =>
    {
        var vm = New();

        vm.FocusMinutes = 0;
        vm.ShortBreakMinutes = 1000;
        vm.LongBreakMinutes = 20;
        vm.FocusesBeforeLongBreak = 99;
        vm.AutoStart = true;

        Assert.Equal(PomodoroSettings.MinMinutes, vm.FocusMinutes);
        Assert.Equal(PomodoroSettings.MaxMinutes, vm.ShortBreakMinutes);
        Assert.Equal(PomodoroSettings.MaxFocuses, vm.FocusesBeforeLongBreak);

        var saved = PomodoroSettings.Load();
        Assert.Equal(20, saved.LongBreakMinutes);
        Assert.True(saved.AutoStart);
    });

    [Fact]
    public Task Lowering_the_cycle_below_where_it_is_still_reads_sensibly() => Headless.Run(() =>
    {
        var vm = New();
        RunOut(vm);
        RunOut(vm);
        RunOut(vm);
        RunOut(vm);
        Assert.Equal("Focus 3 of 4", vm.PhaseName);

        vm.FocusesBeforeLongBreak = 2;
        Assert.Equal("Focus 2 of 2", vm.PhaseName);

        RunOut(vm);
        Assert.Equal(PomodoroPhase.LongBreak, vm.Phase);
    });

    [Fact]
    public Task The_count_is_for_today_and_kept_across_a_restart() => Headless.Run(() =>
    {
        var vm = New();
        Assert.Equal("No focus periods finished today", vm.DoneTodayText);

        RunOut(vm);
        Assert.Equal("1 focus period finished today", vm.DoneTodayText);
        RunOut(vm);
        RunOut(vm);
        Assert.Equal("2 focus periods finished today", vm.DoneTodayText);

        Assert.Equal(2, New(PomodoroSettings.Load()).DoneToday);

        _clock.Now += TimeSpan.FromDays(1);
        var tomorrow = New(PomodoroSettings.Load());
        Assert.Equal(0, tomorrow.DoneToday);
        RunOut(tomorrow);
        Assert.Equal(1, tomorrow.DoneToday);
    });
}
