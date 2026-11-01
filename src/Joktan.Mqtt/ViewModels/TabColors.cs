using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Joktan.Mqtt.Resources;

namespace Joktan.Mqtt.ViewModels;

/// <summary>A colour offered for a client's tab or page.</summary>
public sealed record Swatch(string Name, string Hex)
{
    public IBrush Brush { get; } = new ImmutableSolidColorBrush(Color.Parse(Hex));
}

/// <summary>
/// The colours a client's tab and page can be given. A colour is kept as text, <c>#RRGGBB</c>,
/// and empty means the theme's own.
/// </summary>
public static class TabColors
{
    public static IReadOnlyList<Swatch> Palette { get; } =
    [
        new(Strings.TabColors_Purple, "#5747B5"),
        new(Strings.TabColors_Blue, "#0D8BF2"),
        new(Strings.TabColors_Teal, "#00A3A3"),
        new(Strings.TabColors_Green, "#3BB001"),
        new(Strings.TabColors_Amber, "#E0920B"),
        new(Strings.TabColors_Red, "#DC3545"),
        new(Strings.TabColors_Pink, "#F10075"),
        new(Strings.TabColors_Navy, "#1C273C"),
        new(Strings.TabColors_White, "#FFFFFF"),
        new(Strings.TabColors_Lavender, "#E8E5F8"),
        new(Strings.TabColors_Sky, "#DCEEFE"),
        new(Strings.TabColors_Mint, "#D6F5F5"),
        new(Strings.TabColors_Leaf, "#E2F3D9"),
        new(Strings.TabColors_Sand, "#FCEFD8"),
        new(Strings.TabColors_Rose, "#F9DCDF"),
        new(Strings.TabColors_Blush, "#FDD9EA"),
        new(Strings.TabColors_Grey, "#EDEFF4"),
    ];

    /// <summary>The colour, or null when the text is empty or not a colour.</summary>
    public static Color? Parse(string? text)
        => text?.Trim() is { Length: > 0 } value && Color.TryParse(value, out var color) ? color : null;

    /// <summary>Whether the text is empty or a colour such as #3BB001.</summary>
    public static bool IsValid(string? text) => string.IsNullOrWhiteSpace(text) || Parse(text) is not null;

    /// <summary>The colour as #RRGGBB, for saving.</summary>
    public static string Normalize(string? text)
        => Parse(text) is { } color ? $"#{color.R:X2}{color.G:X2}{color.B:X2}" : "";

    /// <summary>A brush for the colour, or unset so the theme's own is used.</summary>
    public static object BrushOrUnset(string? text)
        => Parse(text) is { } color ? new SolidColorBrush(color) : AvaloniaProperty.UnsetValue;

    /// <summary>
    /// The tab's text colour: the one chosen, or white on a chosen fill, or null for the theme's
    /// own on a tab with no fill, where white would not show on the light theme.
    /// </summary>
    public static IBrush? TextBrush(string? text, string? fill)
    {
        if (Parse(text) is { } color)
            return new SolidColorBrush(color);
        return Parse(fill) is null ? null : Brushes.White;
    }

    /// <summary>
    /// White or navy, whichever reads better on the fill, for a tab with no text colour of its own;
    /// or null for the theme's own on a tab with no fill.
    /// </summary>
    public static IBrush? ReadableOn(string? fill)
    {
        if (Parse(fill) is not { } color)
            return null;

        // Past this relative luminance, navy has more contrast than white.
        var luminance = 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
        return luminance > 0.22 ? new SolidColorBrush(Color.Parse("#1C273C")) : Brushes.White;
    }

    private static double Linear(byte channel)
    {
        var value = channel / 255.0;
        return value <= 0.03928 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }
}
