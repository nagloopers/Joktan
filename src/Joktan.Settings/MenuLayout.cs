using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using Joktan.Core;
using Joktan.Core.ViewModels;

namespace Joktan.Settings;

/// <summary>The order of the menu's entries, and which of them are shown.</summary>
public sealed class MenuLayout
{
    public List<MenuLayoutEntry>? Entries { get; set; }

    private static string FilePath => Path.Combine(DataFolder.Path, "menu.json");

    public static MenuLayout Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<MenuLayout>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // An unreadable layout leaves the menu as it comes.
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
            // A layout that cannot be saved is simply not remembered.
        }
    }

    public static MenuLayout From(IEnumerable<NavItemViewModel> menu) => new() { Entries = Read(menu) };

    /// <summary>Puts the menu in the saved order. An entry the layout does not know, being new, stays after the one it follows in the app.</summary>
    public void ApplyTo(ObservableCollection<NavItemViewModel> menu) => Apply(menu, Entries);

    private static List<MenuLayoutEntry> Read(IEnumerable<NavItemViewModel> items) =>
        items.Select(item => new MenuLayoutEntry { Title = item.Id, Shown = item.IsShown, Children = Read(item.Children) }).ToList();

    private static void Apply(ObservableCollection<NavItemViewModel> items, List<MenuLayoutEntry>? saved)
    {
        if (saved is null)
            return;

        var defaults = items.ToList();
        var ordered = new List<NavItemViewModel>();
        foreach (var entry in saved)
        {
            if (defaults.Find(d => d.Id == entry.Title || d.FormerTitle == entry.Title) is not { } item || ordered.Contains(item))
                continue;

            ordered.Add(item);
            item.IsShown = entry.Shown || !item.CanHide;
            Apply(item.Children, entry.Children);
        }

        for (var i = 0; i < defaults.Count; i++)
        {
            if (!ordered.Contains(defaults[i]))
                ordered.Insert(i == 0 ? 0 : ordered.IndexOf(defaults[i - 1]) + 1, defaults[i]);
        }

        for (var i = 0; i < ordered.Count; i++)
            items.Move(items.IndexOf(ordered[i]), i);
    }
}

/// <summary>One entry of the menu, known by its title among the entries beside it.</summary>
public sealed class MenuLayoutEntry
{
    public string Title { get; set; } = "";

    public bool Shown { get; set; } = true;

    public List<MenuLayoutEntry>? Children { get; set; }
}
