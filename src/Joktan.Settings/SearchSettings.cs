using System;
using System.IO;
using System.Text.Json;
using Joktan.Core;

namespace Joktan.Settings;

/// <summary>What the search box of the top bar looks through.</summary>
public sealed class SearchSettings
{
    public bool Notes { get; set; } = true;

    public bool Help { get; set; } = true;

    public bool Messages { get; set; } = true;

    public bool Alerts { get; set; } = true;

    private static string FilePath => Path.Combine(DataFolder.Path, "search.json");

    public static SearchSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<SearchSettings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // Unreadable settings leave every area switched on.
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
}
