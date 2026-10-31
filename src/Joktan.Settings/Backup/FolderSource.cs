using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Joktan.Settings.Backup;

/// <summary>What came of changing a folder: why it could not be done, or what was done.</summary>
public sealed record FolderChange(string? Problem, string? Message = null);

/// <summary>
/// One folder Joktan keeps things in, as the feature that owns it describes it. The features keep their own
/// rules for changing it, so the Settings page does the same as their pages do.
/// </summary>
public sealed class FolderSource
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>What it holds, or why it cannot be changed here.</summary>
    public string Hint { get; init; } = "";

    /// <summary>Whether it is one of many that come and go, listed together, such as one per MQTT client.</summary>
    public bool IsMessageFolder { get; init; }

    public required Func<string> CurrentPath { get; init; }

    /// <summary>Changes it; null when it cannot be changed here.</summary>
    public Func<string, Task<FolderChange>>? Change { get; init; }

    /// <summary>What the button that changes it says.</summary>
    public string ChangeText { get; init; } = "Use folder";

    /// <summary>Something wrong with it found at start-up, shown on its row. Applying a folder then always goes ahead, to put it right.</summary>
    public string? StartProblem { get; init; }

    /// <summary>What an empty box means; null when it must have a path, or there is no such thing as none.</summary>
    public string? DefaultPath { get; init; }

    /// <summary>Whether it can be set to no folder at all.</summary>
    public bool AllowNone { get; init; }

    /// <summary>Opens the page where it is changed, when that needs something this page cannot ask for.</summary>
    public Action? OpenPage { get; init; }

    public string OpenPageText { get; init; } = "";

    /// <summary>What goes in a backup; null when it does not go in one.</summary>
    public Func<BackupItem?>? Item { get; init; }

    public bool IncludedByDefault { get; init; } = true;

    /// <summary>Why ticking this needs thought, such as holding private keys.</summary>
    public string? Warning { get; init; }
}

/// <summary>Works out what a restore has to do.</summary>
public static class BackupPlanner
{
    /// <summary>
    /// The steps to put these items back, each to where it is kept now, so that a backup from another
    /// folder or computer lands where the app will look. What is no longer known goes back where it was.
    /// </summary>
    public static List<RestoreStep> Steps(BackupManifest manifest, IReadOnlyCollection<string> chosen, IReadOnlyList<BackupItem> current)
    {
        var steps = new List<RestoreStep>();
        foreach (var item in manifest.Items.Where(i => chosen.Contains(i.Id)))
        {
            var now = current.FirstOrDefault(c => c.Id == item.Id);
            foreach (var root in item.Roots.Where(r => r.FileCount > 0))
            {
                var currentRoot = now?.Roots.FirstOrDefault(r => r.Key == root.Key);
                steps.Add(new RestoreStep
                {
                    Name = item.Name,
                    ZipFolder = root.Folder,
                    Target = currentRoot?.Path is { Length: > 0 } path ? path : root.Path,
                    IsFile = root.IsFile,
                    Exclude = currentRoot?.Exclude?.ToList() ?? [],
                });
            }
        }

        return steps;
    }

    /// <summary>A folder as it was typed, made whole: <c>~</c> is the home folder. Null if it cannot be.</summary>
    public static string? WholePath(string text)
    {
        text = text.Trim();
        if (text.StartsWith('~'))
            text = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), text[1..].TrimStart('/', '\\'));
        return text.Length > 0 && text.IndexOfAny(Path.GetInvalidPathChars()) < 0 && Path.IsPathFullyQualified(text) ? Path.GetFullPath(text) : null;
    }

    /// <summary>Copies a folder's files into another, if that one has none and is not inside the first.</summary>
    /// <returns>Whether anything was copied.</returns>
    public static bool CopyIfEmpty(string? from, string to)
    {
        Directory.CreateDirectory(to);
        if (string.IsNullOrEmpty(from) || !Directory.Exists(from) || Directory.EnumerateFileSystemEntries(to).Any())
            return false;

        var source = Path.GetFullPath(from).TrimEnd(Path.DirectorySeparatorChar);
        if (Path.GetFullPath(to).StartsWith(source + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new IOException($"{to} is inside {from}");

        var copied = false;
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
            copied = true;
        }

        return copied;
    }
}
