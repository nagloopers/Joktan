using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using Joktan.Settings.Resources;

namespace Joktan.Settings.Themes;

/// <summary>
/// The theme in use and the working copy the settings pages change. A change shows in the app at
/// once, and stays in the working copy until it is saved in a theme or discarded.
/// </summary>
public sealed partial class ThemeEditor : ObservableObject
{
    /// <summary>The choice that shows Light or Dark according to the operating system.</summary>
    public const string FollowSystem = "Follow system";

    /// <summary>What a theme is called on screen. Its name is what is saved, so those that come with Joktan keep theirs in every language.</summary>
    public static string DisplayName(string name) => name switch
    {
        FollowSystem => Strings.ThemeEditor_FollowSystem,
        BuiltInThemes.LightName => Strings.ThemeEditor_Light,
        BuiltInThemes.DarkName => Strings.ThemeEditor_Dark,
        BuiltInThemes.GreenDarkName => Strings.ThemeEditor_GreenDark,
        _ => name,
    };

    private readonly IReadOnlyList<Theme> _builtIn;
    private readonly ThemeResources _resources;
    private Theme _original;

    private ThemeEditor(IReadOnlyList<Theme> builtIn, IEnumerable<Theme> saved, string current, ThemeResources resources)
    {
        _builtIn = builtIn;
        _resources = resources;
        Themes = new ObservableCollection<Theme>(builtIn.Concat(saved));
        Current = current;
        _original = ThemeFor(current);
        Draft = _original.Clone();

        if (Application.Current is { } app)
            app.ActualThemeVariantChanged += (_, _) => OnSystemThemeChanged();
    }

    /// <summary>Every theme, the built-in ones first.</summary>
    public ObservableCollection<Theme> Themes { get; }

    /// <summary>The name of the theme chosen, or <see cref="FollowSystem"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOverwrite))]
    public partial string Current { get; private set; }

    /// <summary>The values showing in the app, with any changes not yet saved.</summary>
    public Theme Draft { get; private set; }

    [ObservableProperty]
    public partial bool IsDirty { get; private set; }

    /// <summary>Whether Save can write to the theme chosen; a built-in one stays as it comes.</summary>
    public bool CanOverwrite => !IsBuiltIn(Current);

    /// <summary>Raised when the working copy becomes another theme, so the pages show its values.</summary>
    public event Action? DraftReplaced;

    /// <summary>Raised when a font is changed, as one left at its default can follow another.</summary>
    public event Action? FontChanged;

    public static ThemeEditor Load()
    {
        var tokens = BuiltInThemes.LoadTokens();
        var builtIn = BuiltInThemes.Load(tokens);
        var file = ThemeFile.Load();

        var saved = new List<Theme>();
        foreach (var theme in file.Themes ?? [])
        {
            if (NameProblem(theme.Name, builtIn.Concat(saved)) is null)
                saved.Add(theme.CompletedFrom(builtIn[theme.IsDark ? 1 : 0]));
        }

        var current = file.Current is { } name && builtIn.Concat(saved).Any(t => t.Name == name) ? name : FollowSystem;

        var codeFont = tokens.TryGetResource(ThemeTokens.CodeFont, null, out var font) && font is FontFamily family ? family : FontFamily.Default;

        return new ThemeEditor(builtIn, saved, current, new ThemeResources(codeFont));
    }

    /// <summary>Puts the working copy into the app.</summary>
    public void Apply() => _resources.Apply(Draft, RequestedVariant());

    public bool IsBuiltIn(string name) => name == FollowSystem || _builtIn.Any(t => t.Name == name);

    /// <summary>Why a theme cannot go by this name, or null if it can.</summary>
    public string? NameProblem(string? name) => NameProblem(name, Themes);

    /// <summary>Changes to another theme. Changes not yet saved have to be saved or discarded first.</summary>
    /// <returns>Why the choice could not be remembered, or null.</returns>
    public string? Choose(string name)
    {
        if (name == Current || IsDirty || (name != FollowSystem && Themes.All(t => t.Name != name)))
            return null;

        Current = name;
        LoadDraft();
        return SaveFile();
    }

    public void SetColor(string key, string hex)
    {
        Draft.Colors[key] = hex;
        _resources.UpdateColor(key, hex);
        UpdateDirty();
    }

    public void SetFont(string key, string name)
    {
        Draft.Fonts[key] = name;
        _resources.UpdateFonts(Draft);
        UpdateDirty();
        FontChanged?.Invoke();
    }

    public void SetTextSize(string key, double size)
    {
        size = Math.Clamp(size, Theme.MinTextSize, Theme.MaxTextSize);
        Draft.TextSizes[key] = size;
        _resources.UpdateTextSize(key, size);
        UpdateDirty();
    }

    public void SetDark(bool isDark)
    {
        Draft.IsDark = isDark;
        UpdateDirty();
        Apply();
    }

    /// <summary>Writes the changes to the theme chosen.</summary>
    /// <returns>Why they could not be written, or null.</returns>
    public string? Save()
    {
        if (!CanOverwrite)
            return null;

        var saved = Draft.Clone();
        Themes[Themes.IndexOf(_original)] = saved;
        _original = saved;
        UpdateDirty();
        return SaveFile();
    }

    /// <summary>Saves what is showing as a new theme, and changes to it.</summary>
    /// <returns>Why it could not be saved, or null.</returns>
    public string? SaveAs(string name)
    {
        name = name.Trim();
        if (NameProblem(name) is { } problem)
            return problem;

        Draft.Name = name;
        var saved = Draft.Clone();
        Themes.Add(saved);
        _original = saved;
        Current = name;
        UpdateDirty();
        Apply();
        return SaveFile();
    }

    /// <summary>Goes back to the theme as it was last saved.</summary>
    public void Discard() => LoadDraft();

    /// <summary>Removes a saved theme. If it is the one in use, the app goes back to following the system.</summary>
    /// <returns>Why the change could not be written, or null.</returns>
    public string? Delete(Theme theme)
    {
        if (IsBuiltIn(theme.Name))
            return null;

        Themes.Remove(theme);
        if (Current == theme.Name)
        {
            Current = FollowSystem;
            LoadDraft();
        }

        return SaveFile();
    }

    private static string? NameProblem(string? name, IEnumerable<Theme> taken)
    {
        name = name?.Trim();
        if (string.IsNullOrEmpty(name))
            return Strings.ThemeEditor_GiveTheThemeAName;

        return name.Equals(FollowSystem, StringComparison.OrdinalIgnoreCase)
               || taken.Any(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            ? Strings.ThemeEditor_ThereIsAlreadyAThemeWith
            : null;
    }

    private Theme ThemeFor(string name)
    {
        if (name != FollowSystem)
            return Themes.First(t => t.Name == name);

        return _builtIn[Application.Current?.ActualThemeVariant == ThemeVariant.Dark ? 1 : 0];
    }

    // Following the system, unchanged, leaves the variant to the system. Otherwise the theme decides it.
    private ThemeVariant RequestedVariant()
    {
        if (Current == FollowSystem && !IsDirty)
            return ThemeVariant.Default;

        return Draft.IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
    }

    private void LoadDraft()
    {
        // The system's own setting only shows once the variant is left to it.
        if (Current == FollowSystem && Application.Current is { } app)
            app.RequestedThemeVariant = ThemeVariant.Default;

        _original = ThemeFor(Current);
        Draft = _original.Clone();
        UpdateDirty();
        Apply();
        OnPropertyChanged(nameof(Draft));
        DraftReplaced?.Invoke();
    }

    private void OnSystemThemeChanged()
    {
        if (Current != FollowSystem || IsDirty || ThemeFor(FollowSystem) == _original)
            return;

        LoadDraft();
    }

    private void UpdateDirty()
    {
        var wasDirty = IsDirty;
        IsDirty = !Draft.LooksLike(_original);

        // Following the system again once a change is undone, or no longer once there is one.
        if (Current == FollowSystem && wasDirty != IsDirty && Application.Current is { } app)
            app.RequestedThemeVariant = RequestedVariant();
    }

    private string? SaveFile() => new ThemeFile
    {
        Current = Current,
        Themes = Themes.Where(t => !IsBuiltIn(t.Name)).ToList(),
    }.Save();
}
