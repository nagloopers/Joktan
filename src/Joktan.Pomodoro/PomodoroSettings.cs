using System;
using System.IO;
using System.Text.Json;
using Joktan.Core;

namespace Joktan.Pomodoro;

/// <summary>How long each phase lasts, what happens when one ends, and how many focus periods were finished today.</summary>
public sealed class PomodoroSettings
{
    public const int MinMinutes = 1;
    public const int MaxMinutes = 180;
    public const int MinFocuses = 1;
    public const int MaxFocuses = 12;

    public int FocusMinutes { get; set; } = 25;

    public int ShortBreakMinutes { get; set; } = 5;

    public int LongBreakMinutes { get; set; } = 15;

    /// <summary>How many focus periods come before a long break.</summary>
    public int FocusesBeforeLongBreak { get; set; } = 4;

    /// <summary>Whether the next phase starts by itself when one ends.</summary>
    public bool AutoStart { get; set; }

    public bool AlertAtEnd { get; set; } = true;

    public bool SpeakAtEnd { get; set; } = true;

    /// <summary>The day <see cref="DoneCount"/> counts.</summary>
    public DateOnly? DoneOn { get; set; }

    public int DoneCount { get; set; }

    private static string FilePath => Path.Combine(DataFolder.Path, "pomodoro.json");

    public static PomodoroSettings Load()
    {
        try
        {
            if (File.Exists(FilePath) && JsonSerializer.Deserialize<PomodoroSettings>(File.ReadAllText(FilePath)) is { } settings)
            {
                settings.FocusMinutes = ClampMinutes(settings.FocusMinutes);
                settings.ShortBreakMinutes = ClampMinutes(settings.ShortBreakMinutes);
                settings.LongBreakMinutes = ClampMinutes(settings.LongBreakMinutes);
                settings.FocusesBeforeLongBreak = ClampFocuses(settings.FocusesBeforeLongBreak);
                return settings;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // Unreadable settings leave the usual lengths.
        }

        return new();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(DataFolder.Path);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonFormats.Indented));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Settings that cannot be saved are used until the app closes.
        }
    }

    public static int ClampMinutes(int minutes) => Math.Clamp(minutes, MinMinutes, MaxMinutes);

    public static int ClampFocuses(int focuses) => Math.Clamp(focuses, MinFocuses, MaxFocuses);
}
