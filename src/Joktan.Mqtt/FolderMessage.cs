using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Joktan.Mqtt.Resources;

namespace Joktan.Mqtt;

public enum FolderMessageState
{
    /// <summary>Has no schedule; it is only sent by hand.</summary>
    Manual,

    /// <summary>Has a schedule but is switched off.</summary>
    Off,

    /// <summary>Switched on, and will start when the broker is connected.</summary>
    Waiting,

    Scheduled,
    Finished,
    Missed,
    Invalid,
}

public static class FolderMessageStateText
{
    /// <summary>What a FolderMessageState is called on screen.</summary>
    public static string Text(this FolderMessageState value) => value switch
    {
        FolderMessageState.Manual => Strings.FolderMessageState_Manual,
        FolderMessageState.Off => Strings.FolderMessageState_Off,
        FolderMessageState.Waiting => Strings.FolderMessageState_Waiting,
        FolderMessageState.Scheduled => Strings.FolderMessageState_Scheduled,
        FolderMessageState.Finished => Strings.FolderMessageState_Finished,
        FolderMessageState.Missed => Strings.FolderMessageState_Missed,
        FolderMessageState.Invalid => Strings.FolderMessageState_Invalid,
        _ => value.ToString(),
    };
}

/// <summary>
/// A JSON file found in the message folder, with its settings and its progress in the current run.
/// </summary>
public sealed class FolderMessage
{
    public required string FilePath { get; init; }

    public required string FileName { get; init; }

    /// <summary>The folder path relative to the chosen folder; empty for a file in the chosen folder itself.</summary>
    public string Folder { get; init; } = "";

    /// <summary>
    /// The topic the message is sent to: the topic from its settings, or else the folder path. It
    /// may still contain {name} parts that are filled in on every send.
    /// </summary>
    public required string Topic { get; init; }

    /// <summary>The file content exactly as written.</summary>
    public string Text { get; init; } = "";

    public JsonNode? Body { get; init; }

    /// <summary>The settings from its settings file. Switching the message on or off changes them in memory first.</summary>
    public MessageSettings Settings { get; set; } = MessageSettings.Default;

    /// <summary>Why the message cannot be sent, or null.</summary>
    public string? Error { get; init; }

    public bool IsValid => Error is null;

    public FolderMessageState State { get; set; }

    public DateTime? NextDue { get; set; }

    public DateTime? LastSent { get; set; }

    /// <summary>Sends since the message was last switched on or the broker was connected.</summary>
    public int SentCount { get; set; }

    /// <summary>Scheduled sends in the same period, counted against the schedule's maximum.</summary>
    public int ScheduledSends { get; set; }

    public Dictionary<string, long> Counters { get; private set; } = [];

    /// <summary>Whether the message has a schedule that a switch can turn on.</summary>
    public bool HasSchedule => IsValid && Settings.Schedule.Type != ScheduleType.Manual;

    /// <summary>The state while nothing is being sent, for example before the broker is connected.</summary>
    public FolderMessageState RestingState
        => !IsValid ? FolderMessageState.Invalid
            : !HasSchedule ? FolderMessageState.Manual
            : !Settings.Enabled ? FolderMessageState.Off
            : FolderMessageState.Waiting;

    public void CarryOver(FolderMessage previous)
    {
        LastSent = previous.LastSent;
        SentCount = previous.SentCount;
        Counters = previous.Counters;
    }

    /// <summary>Starts counting again, as when the message is switched on.</summary>
    public void ResetProgress()
    {
        SentCount = 0;
        ScheduledSends = 0;
        Counters.Clear();
    }
}
