using System.Linq;
using Avalonia;
using Avalonia.Media;
using Joktan.Mqtt.ViewModels;
using Xunit;

namespace Joktan.Mqtt.Tests.ViewModels;

public class TabColorsTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("  ", false)]
    [InlineData("nonsense", false)]
    [InlineData("#3BB001", true)]
    [InlineData(" #3bb001 ", true)]
    public void Parses_a_colour(string? text, bool expected)
        => Assert.Equal(expected, TabColors.Parse(text) is not null);

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("  ", true)]
    [InlineData("#3BB001", true)]
    [InlineData("nonsense", false)]
    public void Empty_or_a_colour_is_valid(string? text, bool expected)
        => Assert.Equal(expected, TabColors.IsValid(text));

    [Theory]
    [InlineData(null, "")]
    [InlineData("nonsense", "")]
    [InlineData(" #3bb001 ", "#3BB001")]
    [InlineData("#ABC", "#AABBCC")]
    [InlineData("#803BB001", "#3BB001")]
    public void Normalizes_for_saving(string? text, string expected)
        => Assert.Equal(expected, TabColors.Normalize(text));

    [Fact]
    public void Gives_a_brush_or_leaves_it_to_the_theme()
    {
        Assert.Equal(Color.Parse("#3BB001"), Assert.IsAssignableFrom<ISolidColorBrush>(TabColors.BrushOrUnset("#3BB001")).Color);
        Assert.Same(AvaloniaProperty.UnsetValue, TabColors.BrushOrUnset(""));
    }

    [Fact]
    public void Text_is_the_colour_chosen_or_white_on_a_fill_or_the_themes()
    {
        Assert.Equal(Color.Parse("#112233"), TabColors.TextBrush("#112233", "#FFFFFF")!.ToImmutable() is ISolidColorBrush chosen ? chosen.Color : default);
        Assert.Same(Brushes.White, TabColors.TextBrush("", "#000000"));
        Assert.Null(TabColors.TextBrush("", ""));
    }

    [Theory]
    [InlineData("#000000", true)]
    [InlineData("#1C273C", true)]
    [InlineData("#5747B5", true)]
    [InlineData("#FFFFFF", false)]
    [InlineData("#FCEFD8", false)]
    public void Picks_white_or_navy_for_what_reads_better(string fill, bool white)
    {
        var brush = TabColors.ReadableOn(fill);

        if (white)
            Assert.Same(Brushes.White, brush);
        else
            Assert.Equal(Color.Parse("#1C273C"), Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color);
    }

    [Fact]
    public void No_fill_leaves_the_text_to_the_theme()
    {
        Assert.Null(TabColors.ReadableOn(""));
        Assert.Null(TabColors.ReadableOn(null));
    }

    [Fact]
    public void Every_colour_on_offer_is_valid_and_distinct()
    {
        Assert.All(TabColors.Palette, swatch => Assert.Equal(swatch.Hex, TabColors.Normalize(swatch.Hex)));
        Assert.Equal(TabColors.Palette.Count, TabColors.Palette.Select(s => s.Hex).Distinct().Count());
        Assert.Equal(TabColors.Palette.Count, TabColors.Palette.Select(s => s.Name).Distinct().Count());
    }
}
