using System.Collections.Generic;
using Joktan.Agent.Mcp;
using Joktan.Pomodoro.ViewModels;
using static Joktan.Agent.Tools.AgentTools;

namespace Joktan.Agent.Tools;

public static class PomodoroTools
{
    public static IEnumerable<McpTool> Create(PomodoroViewModel pomodoro) =>
    [
        Read("pomodoro_status", "Say which Pomodoro phase it is, how long is left, how many focus periods were finished today, and the settings.", [], _ => Describe(pomodoro)),
        Change("pomodoro_control", "Start or pause the Pomodoro timer, go back to the start of this phase, or skip to the next phase.",
            [P.Choice("action", "What to do.", "start", "pause", "reset", "skip")], args =>
            {
                switch (args.Text("action"))
                {
                    case "start" when !pomodoro.IsRunning:
                    case "pause" when pomodoro.IsRunning:
                        pomodoro.StartPauseCommand.Execute(null);
                        break;
                    case "start" or "pause":
                        break;
                    case "reset":
                        pomodoro.ResetCommand.Execute(null);
                        break;
                    case "skip":
                        pomodoro.SkipCommand.Execute(null);
                        break;
                    default:
                        throw new ToolFailure("The action must be start, pause, reset or skip.");
                }

                return Describe(pomodoro);
            }),
        Change("pomodoro_settings", "Change the Pomodoro settings. What is left out stays as it is.",
            [
                P.OptNumber("focusMinutes", "Length of a focus period, from 1 to 180."),
                P.OptNumber("shortBreakMinutes", "Length of a short break, from 1 to 180."),
                P.OptNumber("longBreakMinutes", "Length of a long break, from 1 to 180."),
                P.OptNumber("focusesBeforeLongBreak", "Focus periods before a long break, from 1 to 12."),
                P.OptFlag("autoStart", "Whether the next phase starts by itself."),
                P.OptFlag("alertAtEnd", "Whether an alert is added when a phase ends."),
                P.OptFlag("speakAtEnd", "Whether it is said aloud when a phase ends."),
            ], args =>
            {
                if (args.Whole("focusMinutes") is { } focus)
                    pomodoro.FocusMinutes = focus;
                if (args.Whole("shortBreakMinutes") is { } shortBreak)
                    pomodoro.ShortBreakMinutes = shortBreak;
                if (args.Whole("longBreakMinutes") is { } longBreak)
                    pomodoro.LongBreakMinutes = longBreak;
                if (args.Whole("focusesBeforeLongBreak") is { } focuses)
                    pomodoro.FocusesBeforeLongBreak = focuses;
                if (args.Flag("autoStart") is { } autoStart)
                    pomodoro.AutoStart = autoStart;
                if (args.Flag("alertAtEnd") is { } alert)
                    pomodoro.AlertAtEnd = alert;
                if (args.Flag("speakAtEnd") is { } speak)
                    pomodoro.SpeakAtEnd = speak;
                return Describe(pomodoro);
            }),
    ];

    private static object Describe(PomodoroViewModel pomodoro) => new
    {
        Phase = pomodoro.PhaseName,
        Left = pomodoro.RemainingText,
        pomodoro.IsRunning,
        pomodoro.DoneToday,
        pomodoro.FocusMinutes,
        pomodoro.ShortBreakMinutes,
        pomodoro.LongBreakMinutes,
        pomodoro.FocusesBeforeLongBreak,
        pomodoro.AutoStart,
        pomodoro.AlertAtEnd,
        pomodoro.SpeakAtEnd,
    };
}
