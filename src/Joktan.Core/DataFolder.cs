using System;
using System.IO;
using System.Text.Json;

namespace Joktan.Core;

/// <summary>
/// Where Joktan keeps its settings and the certificates it makes. Read once for each run, as what the app has
/// open is saved here when it closes, so a change of folder takes effect when the app next starts.
/// </summary>
public static class DataFolder
{
    /// <summary>Set to keep Joktan's data somewhere other than the user's application data folder. It wins over a folder chosen in the app.</summary>
    public const string Variable = "JOKTAN_DATA_DIR";

    /// <summary>
    /// Remembers the folder chosen in the app. It stays in the default folder whatever is chosen, as
    /// it has to be found before anything else is.
    /// </summary>
    public const string LocationFileName = "data-location.json";

    private static readonly Lazy<(string Path, string? Missing)> Chosen = new(() => Resolve(DefaultPath));

    public static string DefaultPath => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Joktan");

    public static bool IsSetByVariable => Environment.GetEnvironmentVariable(Variable) is { Length: > 0 };

    public static string Path => Environment.GetEnvironmentVariable(Variable) is { Length: > 0 } folder ? folder : Chosen.Value.Path;

    /// <summary>The folder chosen in the app, when it could not be found at start-up, so the default is in use for this run.</summary>
    public static string? MissingFolder => IsSetByVariable ? null : Chosen.Value.Missing;

    /// <summary>The folder to use, given the default one: the one chosen if it is there, or else the default and what was missing.</summary>
    public static (string Path, string? Missing) Resolve(string defaultFolder)
    {
        if (ReadChosen(defaultFolder) is not { } chosen)
            return (defaultFolder, null);

        return Directory.Exists(chosen) ? (chosen, null) : (defaultFolder, chosen);
    }

    /// <summary>The folder chosen in the app, if any.</summary>
    public static string? ReadChosen(string defaultFolder)
    {
        try
        {
            var path = System.IO.Path.Combine(defaultFolder, LocationFileName);
            if (File.Exists(path)
                && JsonSerializer.Deserialize<LocationFile>(File.ReadAllText(path)) is { Folder.Length: > 0 } location
                && System.IO.Path.IsPathFullyQualified(location.Folder))
                return location.Folder;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // An unreadable choice falls back to the default folder.
        }

        return null;
    }

    /// <summary>Remembers the folder for the next start. The default folder is remembered as no choice.</summary>
    public static void SaveChosen(string folder, string defaultFolder)
    {
        var path = System.IO.Path.Combine(defaultFolder, LocationFileName);
        if (System.IO.Path.GetFullPath(folder).TrimEnd(System.IO.Path.DirectorySeparatorChar) == System.IO.Path.GetFullPath(defaultFolder).TrimEnd(System.IO.Path.DirectorySeparatorChar))
        {
            File.Delete(path);
            return;
        }

        Directory.CreateDirectory(defaultFolder);
        File.WriteAllText(path, JsonSerializer.Serialize(new LocationFile { Folder = folder }, JsonFormats.Indented));
    }

    private sealed class LocationFile
    {
        public string Folder { get; set; } = "";
    }
}
