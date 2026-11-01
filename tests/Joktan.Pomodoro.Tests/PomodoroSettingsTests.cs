using System;
using System.IO;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Pomodoro.Tests;

[Collection(ProcessStateCollection.Name)]
public class PomodoroSettingsTests : DataFolderTest
{
    [Fact]
    public void Starts_with_the_usual_lengths()
    {
        var settings = PomodoroSettings.Load();

        Assert.Equal(25, settings.FocusMinutes);
        Assert.Equal(5, settings.ShortBreakMinutes);
        Assert.Equal(15, settings.LongBreakMinutes);
        Assert.Equal(4, settings.FocusesBeforeLongBreak);
        Assert.False(settings.AutoStart);
    }

    [Fact]
    public void What_is_saved_comes_back()
    {
        new PomodoroSettings { FocusMinutes = 50, AutoStart = true, SpeakAtEnd = false, DoneOn = new DateOnly(2026, 11, 1), DoneCount = 3 }.Save();

        var settings = PomodoroSettings.Load();

        Assert.Equal(50, settings.FocusMinutes);
        Assert.True(settings.AutoStart);
        Assert.False(settings.SpeakAtEnd);
        Assert.Equal(new DateOnly(2026, 11, 1), settings.DoneOn);
        Assert.Equal(3, settings.DoneCount);
    }

    [Fact]
    public void Lengths_out_of_range_are_brought_into_it()
    {
        File.WriteAllText(Data.At("pomodoro.json"), """{ "FocusMinutes": 0, "ShortBreakMinutes": -5, "LongBreakMinutes": 9999, "FocusesBeforeLongBreak": 0 }""");

        var settings = PomodoroSettings.Load();

        Assert.Equal(PomodoroSettings.MinMinutes, settings.FocusMinutes);
        Assert.Equal(PomodoroSettings.MinMinutes, settings.ShortBreakMinutes);
        Assert.Equal(PomodoroSettings.MaxMinutes, settings.LongBreakMinutes);
        Assert.Equal(PomodoroSettings.MinFocuses, settings.FocusesBeforeLongBreak);
    }

    [Fact]
    public void An_unreadable_file_leaves_the_defaults()
    {
        File.WriteAllText(Data.At("pomodoro.json"), "not json");

        Assert.Equal(25, PomodoroSettings.Load().FocusMinutes);
    }
}
