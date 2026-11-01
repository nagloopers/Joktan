using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Joktan.Core;
using Joktan.Settings.Resources;

namespace Joktan.Settings.Backup;

/// <summary>One folder or file to put back.</summary>
public sealed class RestoreStep
{
    public string Name { get; set; } = "";

    public string ZipFolder { get; set; } = "";

    public string Target { get; set; } = "";

    public bool IsFile { get; set; }

    public List<string> Exclude { get; set; } = [];
}

/// <summary>What happened at the last restore, for saying so on the page.</summary>
public sealed class RestoreOutcome
{
    public string ZipPath { get; set; } = "";

    public string SafetyPath { get; set; } = "";

    public List<string> Restored { get; set; } = [];

    public string? Error { get; set; }
}

/// <summary>
/// A restore waiting for the app to close. Files are put back at the very start of the next run, before
/// anything has been read, because the app closing saves what it has open over whatever is on disk.
/// </summary>
public sealed class PendingRestore
{
    private const string PlanName = "restore-pending.json";
    private const string OutcomeName = "restore-outcome.json";
    private static readonly TimeSpan ExitWait = TimeSpan.FromSeconds(30);

    public string ZipPath { get; set; } = "";

    public string SafetyPath { get; set; } = "";

    /// <summary>The run that asked for this, which has to be gone before anything is put back.</summary>
    public int ProcessId { get; set; }

    public List<RestoreStep> Steps { get; set; } = [];

    /// <summary>The files in the data folder of no use to a backup. They belong to this restore.</summary>
    public static IReadOnlyList<string> OwnFiles => [PlanName, OutcomeName];

    private static string PlanPath => Path.Combine(DataFolder.Path, PlanName);

    private static string OutcomePath => Path.Combine(DataFolder.Path, OutcomeName);

    public void Save()
    {
        Directory.CreateDirectory(DataFolder.Path);
        var temporary = PlanPath + ".partial";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, JsonFormats.Indented));
        File.Move(temporary, PlanPath, overwrite: true);
    }

    /// <summary>Withdraws a restore that has not started, when the app could not go on to restart.</summary>
    public static void Cancel()
    {
        try
        {
            File.Delete(PlanPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // It will run at the next start instead, which is what was asked for.
        }
    }

    /// <summary>Puts back what is waiting, if anything. Called before anything else when the app starts.</summary>
    public static void ApplyIfAny()
    {
        PendingRestore? plan;
        try
        {
            if (!File.Exists(PlanPath))
                return;
            plan = JsonSerializer.Deserialize<PendingRestore>(File.ReadAllText(PlanPath));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            SaveOutcome(new RestoreOutcome { Error = string.Format(Strings.PendingRestore_TheRestoreCouldNotBeStarted, e.Message) });
            Cancel();
            return;
        }

        var outcome = new RestoreOutcome { ZipPath = plan?.ZipPath ?? "", SafetyPath = plan?.SafetyPath ?? "" };
        try
        {
            if (plan is not null)
            {
                WaitForExit(plan.ProcessId);
                BackupArchive.Verify(plan.ZipPath);
                foreach (var step in plan.Steps)
                {
                    BackupArchive.RestoreRoot(plan.ZipPath, step.ZipFolder, step.Target, step.IsFile, step.Exclude);
                    if (!outcome.Restored.Contains(step.Name))
                        outcome.Restored.Add(step.Name);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or BackupException)
        {
            outcome.Error = e.Message;
        }

        // Whatever happened, so that a restore that fails does not go on failing at every start.
        Cancel();
        SaveOutcome(outcome);
    }

    /// <summary>What the last restore did, once, to say so on the page.</summary>
    public static RestoreOutcome? TakeOutcome()
    {
        try
        {
            if (!File.Exists(OutcomePath))
                return null;

            var outcome = JsonSerializer.Deserialize<RestoreOutcome>(File.ReadAllText(OutcomePath));
            File.Delete(OutcomePath);
            return outcome;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static void WaitForExit(int processId)
    {
        if (processId <= 0 || processId == Environment.ProcessId)
            return;

        try
        {
            using var process = Process.GetProcessById(processId);
            process.WaitForExit(ExitWait);
        }
        catch (ArgumentException)
        {
            // It has already gone.
        }
    }

    private static void SaveOutcome(RestoreOutcome outcome)
    {
        try
        {
            Directory.CreateDirectory(DataFolder.Path);
            File.WriteAllText(OutcomePath, JsonSerializer.Serialize(outcome, JsonFormats.Indented));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The page will not be able to say how it went, but the files are as they are.
        }
    }
}
