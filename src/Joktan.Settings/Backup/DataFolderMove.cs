using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Joktan.Core;
using Joktan.Settings.Resources;

namespace Joktan.Settings.Backup;

/// <summary>
/// Changes where the app settings are kept. The whole data folder goes, as features keep folders inside it by default.
/// The originals stay. The next run uses the new folder, as this run keeps saving to the one it started with.
/// </summary>
public static class DataFolderMove
{
    // Any of these says a folder is already Joktan's, rather than one that merely has files in it.
    private static readonly string[] SettingsFiles = ["mqtt.json", "menu.json", "themes.json", "search.json", "backup.json", "containers.json"];

    /// <summary>Writes what is open, changes the folder and restarts. If it cannot be done, says why and nothing changes.</summary>
    /// <param name="to">The folder to use.</param>
    /// <param name="from">The folder in use.</param>
    /// <param name="defaultFolder">The default folder, which holds the choice.</param>
    /// <param name="flush">Writes everything not yet on disk, so that the copy has what is on screen.</param>
    /// <param name="restart">Closes the app and starts it again.</param>
    public static async Task<FolderChange> ApplyAsync(string to, string from, string defaultFolder, Func<Task> flush, Action restart)
    {
        string message;
        try
        {
            await flush();
            message = await Task.Run(() => Move(to, from, defaultFolder));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new FolderChange(string.Format(Strings.DataFolderMove_CouldNotUse, to, e.Message));
        }

        restart();
        return new FolderChange(null, message);
    }

    /// <summary>Whether a folder already holds Joktan's settings.</summary>
    public static bool HoldsSettings(string folder) => SettingsFiles.Any(name => File.Exists(Path.Combine(folder, name)));

    private static string Move(string to, string from, string defaultFolder)
    {
        var source = Path.GetFullPath(from).TrimEnd(Path.DirectorySeparatorChar);
        var target = Path.GetFullPath(to).TrimEnd(Path.DirectorySeparatorChar);
        if (target == source)
        {
            DataFolder.SaveChosen(target, defaultFolder);
            return string.Format(Strings.DataFolderMove_RestartingToUse, target);
        }

        if (target.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new IOException(Strings.DataFolderMove_ItIsInsideTheFolderIn);

        var folders = new List<string>();
        CreateFolders(target, folders);
        if (HoldsSettings(target))
        {
            CheckWritable(target);
            DataFolder.SaveChosen(target, defaultFolder);
            return string.Format(Strings.DataFolderMove_RestartingToOpenTheSettingsAlready, target);
        }

        // Dot files such as .DS_Store appear in a new folder by themselves, so they do not count.
        if (Directory.EnumerateFileSystemEntries(target).Any(entry => !Path.GetFileName(entry).StartsWith('.')))
            throw new IOException(Strings.DataFolderMove_ItHoldsOtherFilesChooseAn);

        Copy(source, target, folders, () =>
        {
            RewritePaths(source, target);
            DataFolder.SaveChosen(target, defaultFolder);
        });
        return string.Format(Strings.DataFolderMove_RestartingToUseTheOriginalsAre, target, source);
    }

    private static void CheckWritable(string folder)
    {
        var probe = Path.Combine(folder, $".joktan-{Guid.NewGuid():N}");
        File.WriteAllText(probe, "");
        File.Delete(probe);
    }

    // Takes back what it made if anything goes wrong, finishing included, so a failed try leaves the folder usable for the next.
    private static void Copy(string source, string target, List<string> folders, Action finish)
    {
        var files = new List<string>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(source, file);
                if (relative == DataFolder.LocationFileName || PendingRestore.OwnFiles.Contains(relative))
                    continue;

                var destination = Path.Combine(target, relative);
                CreateFolders(Path.GetDirectoryName(destination)!, folders);
                File.Copy(file, destination);
                files.Add(destination);
            }

            finish();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            foreach (var file in files)
                TryDelete(() => File.Delete(file));
            foreach (var folder in Enumerable.Reverse(folders))
                TryDelete(() => Directory.Delete(folder));
            throw;
        }
    }

    private static void CreateFolders(string folder, List<string> created)
    {
        if (Directory.Exists(folder))
            return;

        CreateFolders(Path.GetDirectoryName(folder)!, created);
        Directory.CreateDirectory(folder);
        created.Add(folder);
    }

    private static void TryDelete(Action delete)
    {
        try
        {
            delete();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // What cannot be taken back stays, and the reason for failing is what gets reported.
        }
    }

    // Settings name files in the data folder by their whole path, such as the certificates an MQTT client uses.
    // Those files were copied too, so the copy should use them and not depend on the old folder staying.
    private static void RewritePaths(string source, string target)
    {
        var separator = Path.DirectorySeparatorChar;
        var forms = new[] { JsonFormats.Indented, JsonSerializerOptions.Default }
            .Select(options => (Old: Encoded(source + separator, options), New: Encoded(target + separator, options)))
            .Distinct()
            .ToList();

        foreach (var file in Directory.EnumerateFiles(target, "*.json"))
        {
            var text = File.ReadAllText(file);
            var rewritten = forms.Aggregate(text, (current, form) => current.Replace(form.Old, form.New, StringComparison.Ordinal));
            if (rewritten != text)
                File.WriteAllText(file, rewritten);
        }
    }

    private static string Encoded(string path, JsonSerializerOptions options) => JsonSerializer.Serialize(path, options).Trim('"');
}
