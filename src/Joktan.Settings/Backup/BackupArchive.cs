using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using Joktan.Core;
using Joktan.Settings.Resources;

namespace Joktan.Settings.Backup;

/// <summary>What a backup holds, kept in the zip beside the files.</summary>
public sealed class BackupManifest
{
    public int Version { get; set; } = 1;

    public DateTime Created { get; set; }

    public string AppVersion { get; set; } = "";

    public List<ManifestItem> Items { get; set; } = [];
}

public sealed class ManifestItem
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public List<ManifestRoot> Roots { get; set; } = [];

    public int FileCount => Roots.Sum(r => r.FileCount);
}

public sealed class ManifestRoot
{
    public string Key { get; set; } = "";

    /// <summary>Where it was when the backup was made.</summary>
    public string Path { get; set; } = "";

    public bool IsFile { get; set; }

    /// <summary>Where its files are in the zip.</summary>
    public string Folder { get; set; } = "";

    public int FileCount { get; set; }
}

/// <summary>What a backup left out, and how many files it holds.</summary>
public sealed record BackupResult(string Path, int FileCount, IReadOnlyList<string> Skipped);

/// <summary>Why a backup could not be made or read.</summary>
public sealed class BackupException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Zips and unzips what Joktan keeps. Specified in docs/features/settings/spec.md. Files and folders whose
/// names start with a dot, such as <c>.git</c>, are left alone both ways, as they are not Joktan's.
/// </summary>
public static class BackupArchive
{
    public const string BackupPrefix = "joktan-backup-";
    public const string SafetyPrefix = "joktan-safety-";

    private const string ManifestName = "joktan-backup.json";
    private const string TemporarySuffix = ".partial";

    /// <summary>Writes a zip of these, in full or not at all. Files that cannot be read are listed, not fatal.</summary>
    /// <param name="exclude">Left out wherever it is found, such as the backup folder itself.</param>
    public static BackupResult Create(string zipPath, IEnumerable<BackupItem> items, string appVersion = "", IReadOnlyCollection<string>? exclude = null)
    {
        var temporary = zipPath + TemporarySuffix;
        var skipped = new List<string>();
        var manifest = new BackupManifest { Created = DateTime.Now, AppVersion = appVersion };
        var count = 0;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
            using (var zip = new ZipArchive(new FileStream(temporary, FileMode.Create, FileAccess.Write), ZipArchiveMode.Create))
            {
                var itemIndex = 0;
                foreach (var item in items)
                {
                    var entry = new ManifestItem { Id = item.Id, Name = item.Name };
                    var rootIndex = 0;
                    foreach (var root in item.Roots)
                    {
                        var folder = $"data/{itemIndex}/{rootIndex++}";
                        var written = 0;
                        foreach (var (file, relative) in FilesOf(root, exclude))
                        {
                            try
                            {
                                using var source = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                                using var target = zip.CreateEntry($"{folder}/{relative}", CompressionLevel.Optimal).Open();
                                source.CopyTo(target);
                                written++;
                            }
                            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                            {
                                skipped.Add($"{file}: {e.Message}");
                            }
                        }

                        if (written == 0)
                            skipped.Add(string.Format(Strings.BackupArchive_NothingFoundAt, item.Name, root.Path));
                        count += written;
                        entry.Roots.Add(new ManifestRoot { Key = root.Key, Path = root.Path, IsFile = root.IsFile, Folder = folder, FileCount = written });
                    }

                    manifest.Items.Add(entry);
                    itemIndex++;
                }

                using var writer = new StreamWriter(zip.CreateEntry(ManifestName).Open());
                writer.Write(JsonSerializer.Serialize(manifest, JsonFormats.Indented));
            }

            File.Move(temporary, zipPath, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            TryDelete(temporary);
            throw new BackupException(string.Format(Strings.BackupArchive_CouldNotWrite, zipPath, e.Message), e);
        }

        return new BackupResult(zipPath, count, skipped);
    }

    /// <summary>What a backup holds. Throws if it is not a backup that can be read through to its end.</summary>
    public static BackupManifest Read(string zipPath)
    {
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            var entry = zip.GetEntry(ManifestName) ?? throw new BackupException(string.Format(Strings.BackupArchive_IsNotAJoktanBackup, Path.GetFileName(zipPath)));
            using var stream = entry.Open();
            var manifest = JsonSerializer.Deserialize<BackupManifest>(stream) ?? throw new BackupException(string.Format(Strings.BackupArchive_IsNotAJoktanBackup, Path.GetFileName(zipPath)));
            if (manifest.Version > 1)
                throw new BackupException(string.Format(Strings.BackupArchive_WasMadeByANewerJoktan, Path.GetFileName(zipPath)));
            return manifest;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            throw new BackupException(string.Format(Strings.BackupArchive_CouldNotRead, Path.GetFileName(zipPath), e.Message), e);
        }
    }

    /// <summary>Reads every file in a zip, as what is about to be relied on must not turn out to be damaged.</summary>
    public static void Verify(string zipPath)
    {
        Read(zipPath);
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            foreach (var entry in zip.Entries)
            {
                using var stream = entry.Open();
                stream.CopyTo(Stream.Null);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            throw new BackupException(string.Format(Strings.BackupArchive_IsDamaged, Path.GetFileName(zipPath), e.Message), e);
        }
    }

    /// <summary>
    /// Puts a root back as it was in the zip: every file is written, and files it did not have are removed,
    /// apart from those excluded. The zip is unpacked first, so a damaged one changes nothing.
    /// </summary>
    public static void RestoreRoot(string zipPath, string zipFolder, string target, bool isFile, IReadOnlyCollection<string>? exclude = null)
    {
        var unpacked = Path.Combine(Path.GetTempPath(), $"joktan-restore-{Guid.NewGuid():N}");
        try
        {
            Unpack(zipPath, zipFolder, unpacked);
            if (isFile)
            {
                var file = Directory.EnumerateFiles(unpacked).FirstOrDefault();
                if (file is null)
                    return;

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite: true);
                return;
            }

            Directory.CreateDirectory(target);
            var kept = new HashSet<string>(StringComparer.Ordinal);
            foreach (var file in Directory.EnumerateFiles(unpacked, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(unpacked, file);
                var destination = Path.Combine(target, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination, overwrite: true);
                kept.Add(Path.GetFullPath(destination));
            }

            foreach (var (file, _) in FilesOf(new BackupRoot("", target, false, exclude), null))
            {
                if (!kept.Contains(Path.GetFullPath(file)))
                    File.Delete(file);
            }

            RemoveEmptyFolders(target);
        }
        finally
        {
            try
            {
                if (Directory.Exists(unpacked))
                    Directory.Delete(unpacked, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Left for the system to clear from its temporary folder.
            }
        }
    }

    /// <summary>Deletes the oldest zips made with a prefix, past a number kept. Nothing else in the folder is touched.</summary>
    public static IReadOnlyList<string> Prune(string folder, string prefix, int keep)
    {
        var deleted = new List<string>();
        if (!Directory.Exists(folder))
            return deleted;

        foreach (var old in Directory.EnumerateFiles(folder, $"{prefix}*.zip").OrderByDescending(Path.GetFileName, StringComparer.Ordinal).Skip(Math.Max(1, keep)))
        {
            if (TryDelete(old))
                deleted.Add(old);
        }

        return deleted;
    }

    /// <summary>A name for a new zip, which sorts by when it was made and has not been used.</summary>
    public static string NewPath(string folder, string prefix, DateTime time)
    {
        var name = $"{prefix}{time:yyyy-MM-dd-HHmmss}";
        var path = Path.Combine(folder, $"{name}.zip");
        for (var i = 2; File.Exists(path); i++)
            path = Path.Combine(folder, $"{name}-{i}.zip");
        return path;
    }

    private static void Unpack(string zipPath, string zipFolder, string target)
    {
        var prefix = zipFolder + "/";
        var root = Path.GetFullPath(target) + Path.DirectorySeparatorChar;
        try
        {
            Directory.CreateDirectory(target);
            using var zip = ZipFile.OpenRead(zipPath);
            foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith(prefix, StringComparison.Ordinal) && e.Name.Length > 0))
            {
                // A zip can name a path outside where it is unpacked, which must never be followed.
                var destination = Path.GetFullPath(Path.Combine(target, entry.FullName[prefix.Length..]));
                if (!destination.StartsWith(root, StringComparison.Ordinal))
                    throw new BackupException(string.Format(Strings.BackupArchive_NamesAFileOutsideItsFolder, Path.GetFileName(zipPath), entry.FullName));

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination, overwrite: true);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            throw new BackupException(string.Format(Strings.BackupArchive_CouldNotUnpack, Path.GetFileName(zipPath), e.Message), e);
        }
    }

    private static IEnumerable<(string File, string Relative)> FilesOf(BackupRoot root, IReadOnlyCollection<string>? exclude)
    {
        if (root.IsFile)
        {
            if (File.Exists(root.Path))
                yield return (root.Path, "file");
            yield break;
        }

        if (!Directory.Exists(root.Path))
            yield break;

        var left = (root.Exclude ?? []).Concat(exclude ?? []).Select(FullPath).ToHashSet(StringComparer.Ordinal);
        var waiting = new Stack<string>();
        waiting.Push(root.Path);
        while (waiting.Count > 0)
        {
            var folder = waiting.Pop();
            string[] files, folders;
            try
            {
                files = Directory.GetFiles(folder);
                folders = Directory.GetDirectories(folder);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files.Order(StringComparer.Ordinal))
            {
                if (!Path.GetFileName(file).StartsWith('.') && !left.Contains(FullPath(file)))
                    yield return (file, Path.GetRelativePath(root.Path, file).Replace(Path.DirectorySeparatorChar, '/'));
            }

            foreach (var inner in folders)
            {
                if (!Path.GetFileName(inner).StartsWith('.') && !left.Contains(FullPath(inner)))
                    waiting.Push(inner);
            }
        }
    }

    private static void RemoveEmptyFolders(string root)
    {
        foreach (var folder in Directory.GetDirectories(root))
        {
            if (Path.GetFileName(folder).StartsWith('.'))
                continue;

            RemoveEmptyFolders(folder);
            if (!Directory.EnumerateFileSystemEntries(folder).Any())
                Directory.Delete(folder);
        }
    }

    private static string FullPath(string path) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);

    private static bool TryDelete(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
