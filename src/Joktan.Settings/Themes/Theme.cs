using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;

namespace Joktan.Settings.Themes;

/// <summary>A named set of colours, fonts and text sizes, keyed by the resource each one sets.</summary>
public sealed class Theme
{
    public const double MinTextSize = 8;
    public const double MaxTextSize = 72;

    public string Name { get; set; } = "";

    /// <summary>Whether controls such as text boxes and switches take their dark look.</summary>
    public bool IsDark { get; set; }

    /// <summary>Each colour as <c>#RRGGBB</c>.</summary>
    public Dictionary<string, string> Colors { get; set; } = [];

    /// <summary>Each font family by name. Empty means the font that role comes with.</summary>
    public Dictionary<string, string> Fonts { get; set; } = [];

    public Dictionary<string, double> TextSizes { get; set; } = [];

    public Theme Clone() => new()
    {
        Name = Name,
        IsDark = IsDark,
        Colors = new(Colors),
        Fonts = new(Fonts),
        TextSizes = new(TextSizes),
    };

    /// <summary>Whether the two look the same, whatever they are called.</summary>
    public bool LooksLike(Theme other)
        => IsDark == other.IsDark
           && Same(Colors, other.Colors)
           && Same(Fonts, other.Fonts)
           && Same(TextSizes, other.TextSizes);

    private static bool Same<T>(Dictionary<string, T> one, Dictionary<string, T> other)
        => one.Count == other.Count
           && one.All(e => other.TryGetValue(e.Key, out var value) && EqualityComparer<T>.Default.Equals(e.Value, value));

    /// <summary>
    /// A copy holding exactly the tokens the app has. A token missing or unusable here, as in a
    /// file from an older version or edited by hand, takes its value from <paramref name="fallback"/>.
    /// </summary>
    public Theme CompletedFrom(Theme fallback)
    {
        var complete = new Theme { Name = Name, IsDark = IsDark };

        foreach (var token in ThemeTokens.Colors)
        {
            complete.Colors[token.Key] = Colors.GetValueOrDefault(token.Key) is { } text && NormalizeColor(text) is { } color
                ? color
                : fallback.Colors[token.Key];
        }

        foreach (var token in ThemeTokens.Fonts)
            complete.Fonts[token.Key] = Fonts.GetValueOrDefault(token.Key) ?? fallback.Fonts[token.Key];

        foreach (var token in ThemeTokens.TextSizes)
        {
            complete.TextSizes[token.Key] = TextSizes.TryGetValue(token.Key, out var size) && double.IsFinite(size)
                ? Math.Clamp(size, MinTextSize, MaxTextSize)
                : fallback.TextSizes[token.Key];
        }

        return complete;
    }

    /// <summary>The colour as <c>#RRGGBB</c>, or null when the text is not a colour.</summary>
    public static string? NormalizeColor(string? text)
        => text?.Trim() is { Length: > 0 } value && Color.TryParse(value, out var color) ? ToHex(color) : null;

    public static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}
