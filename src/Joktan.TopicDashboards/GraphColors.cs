using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Joktan.TopicDashboards.Resources;

namespace Joktan.TopicDashboards;

/// <summary>
/// The colours a graph's values are drawn in. They are <c>Chart1Brush</c> to <c>Chart8Brush</c> in
/// Tokens.axaml, in an order checked for colour blindness, with their own steps for the dark theme.
/// </summary>
public static class GraphColors
{
    public const int Count = 8;

    public static IReadOnlyList<string> Names { get; } = [Strings.GraphColors_Blue, Strings.GraphColors_Orange, Strings.GraphColors_Aqua, Strings.GraphColors_Yellow, Strings.GraphColors_Magenta, Strings.GraphColors_Green, Strings.GraphColors_Violet, Strings.GraphColors_Red];

    public static IBrush Brush(StyledElement element, int slot)
        => element.TryFindResource(string.Format(Strings.GraphColors_ChartBrush, slot % Count + 1), (element as Control)?.ActualThemeVariant, out var found) && found is IBrush brush
            ? brush
            : Brushes.Gray;
}
