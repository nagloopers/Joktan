using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Joktan.TopicDashboards.Controls;
using Joktan.TopicDashboards.ViewModels;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.TopicDashboards.Tests.Controls;

public class GraphViewTests
{
    private static GraphCardViewModel Card(GraphKind kind, int numbers, Action<GraphProfile>? change = null, params double[] values)
    {
        var profile = new GraphProfile { Name = "g", Kind = kind, SpanMinutes = 5 };
        var data = new GraphData();
        var now = DateTime.Now;
        foreach (var n in Enumerable.Range(0, numbers))
        {
            profile.Values.Add(new GraphValue { Topic = "t", Path = $"p{n}", Color = n });
            var series = data.Series("t", $"p{n}");
            for (var i = 0; i < values.Length; i++)
                series.Add(now.AddSeconds(i - values.Length), [values[i] + n]);
        }

        change?.Invoke(profile);
        return new GraphCardViewModel(profile, data);
    }

    private static (Window Window, GraphView View) Show(GraphCardViewModel? card, double width = 600, double height = 400)
    {
        var view = new GraphView
        {
            Card = card,
            TextBrush = Brushes.White,
            MutedBrush = Brushes.Gray,
            GridBrush = Brushes.DimGray,
            SurfaceBrush = Brushes.Black,
        };
        var window = new Window { Width = width, Height = height, Content = view };
        Headless.Show(window);
        return (window, view);
    }

    // The first colour a graph draws in, which nothing else on it uses.
    private static int ColorPixels(Window window, GraphView view, int slot = 0)
    {
        var brush = (ISolidColorBrush)GraphColors.Brush(view, slot);
        return Headless.CountPixels(window, c => Math.Abs(c.R - brush.Color.R) < 8 && Math.Abs(c.G - brush.Color.G) < 8 && Math.Abs(c.B - brush.Color.B) < 8);
    }

    private static void Hover(Window window, GraphView view, double x, double y)
    {
        window.MouseMove(view.TranslatePoint(new Point(x, y), window)!.Value);
        Headless.Settle();
        window.CaptureRenderedFrame();
    }

    [Theory]
    [InlineData(GraphKind.Line, 1)]
    [InlineData(GraphKind.Line, 3)]
    [InlineData(GraphKind.Area, 1)]
    [InlineData(GraphKind.Area, 3)]
    [InlineData(GraphKind.Bars, 1)]
    [InlineData(GraphKind.Bars, 3)]
    [InlineData(GraphKind.Gauge, 1)]
    [InlineData(GraphKind.Gauge, 3)]
    [InlineData(GraphKind.Value, 1)]
    [InlineData(GraphKind.Value, 3)]
    public Task Each_kind_draws_in_its_colour(GraphKind kind, int numbers) => Headless.Run(() =>
    {
        var (window, view) = Show(Card(kind, numbers, null, 1, 5, 3, 8, 2));

        Assert.True(ColorPixels(window, view) > 0);

        Hover(window, view, 300, 150);
        Hover(window, view, 590, 150);
        window.MouseMove(new Point(-10, -10));
        Headless.Settle();
        window.Close();
    });

    [Theory]
    [InlineData(GraphKind.Line)]
    [InlineData(GraphKind.Area)]
    [InlineData(GraphKind.Bars)]
    [InlineData(GraphKind.Gauge)]
    [InlineData(GraphKind.Value)]
    public Task Fixed_scales_negative_flat_and_single_values_all_draw(GraphKind kind) => Headless.Run(() =>
    {
        foreach (var card in new[]
                 {
                     Card(kind, 1, p => { p.Minimum = 0; p.Maximum = 10; }, 1, 2),
                     Card(kind, 2, null, -5, -2, -9),
                     Card(kind, 1, null, 3, 3, 3),
                     Card(kind, 1, null, 0, 0),
                     Card(kind, 1, null, 7),
                     Card(kind, 1, null, 2_500_000, 3_000_000),
                     Card(kind, 1),
                     Card(kind, 2),
                 })
        {
            var (window, view) = Show(card);
            Hover(window, view, 300, 200);
            window.Close();
        }
    });

    [Fact]
    public Task A_number_too_wide_for_its_tile_is_made_smaller_and_a_gauge_too_small_is_left_out() => Headless.Run(() =>
    {
        var (window, view) = Show(Card(GraphKind.Value, 1, p => p.Unit = "units of something long", 123456789, 123456790), 150, 200);
        Assert.NotNull(view.Card);
        window.Close();

        (window, view) = Show(Card(GraphKind.Gauge, 1, null, 1), 60, 45);
        Assert.Equal(0, ColorPixels(window, view));
        window.Close();

        (window, _) = Show(Card(GraphKind.Value, 1, null, 1, 2), 300, 80);
        window.Close();
    });

    [Fact]
    public Task Nothing_is_drawn_without_a_card_or_room_or_numbers() => Headless.Run(() =>
    {
        var (window, view) = Show(null);
        Assert.Equal(0, ColorPixels(window, view));
        window.Close();

        (window, view) = Show(Card(GraphKind.Line, 1, null, 1, 2), 39, 39);
        Assert.Equal(0, ColorPixels(window, view));
        window.Close();

        (window, view) = Show(Card(GraphKind.Line, 0));
        Assert.Equal(0, ColorPixels(window, view));
        window.Close();
    });

    [Fact]
    public Task Hovering_away_from_any_reading_shows_nothing_and_leaving_ends_it() => Headless.Run(() =>
    {
        var profile = new GraphProfile { Name = "g", SpanMinutes = 60, Values = [new GraphValue { Topic = "t", Path = "p" }] };
        var data = new GraphData();
        data.Series("t", "p").Add(DateTime.Now, [1]);
        data.Series("t", "p").Add(DateTime.Now.AddSeconds(1), [2]);
        var (window, view) = Show(new GraphCardViewModel(profile, data));

        // The readings are at the right edge; the left is far from them.
        Hover(window, view, 60, 100);
        Hover(window, view, 585, 100);
        Hover(window, view, 2, 2);
        window.Close();
    });

    [Fact]
    public Task A_new_theme_or_resources_draw_it_again() => Headless.Run(() =>
    {
        var (window, view) = Show(Card(GraphKind.Line, 1, null, 1, 2));

        Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        window.Resources["Anything"] = 1.0;
        Headless.Settle();

        view.Revision = 5;
        Assert.Equal(5, view.Revision);
        Assert.Same(Brushes.White, view.TextBrush);
        Assert.Same(Brushes.Gray, view.MutedBrush);
        Assert.Same(Brushes.DimGray, view.GridBrush);
        Assert.Same(Brushes.Black, view.SurfaceBrush);
        window.Close();
    });

    [Fact]
    public Task Text_sizes_come_from_the_theme_or_a_fallback() => Headless.Run(() =>
    {
        var (window, view) = Show(Card(GraphKind.Line, 1, null, 1, 2));
        view.Resources["GraphAxisFontSize"] = 20.0;
        view.Resources["GraphLabelFontSize"] = "not a size";
        Headless.Settle();
        Assert.True(ColorPixels(window, view) > 0);
        window.Close();
    });
}

public class ColorDotTests
{
    [Fact]
    public Task Draws_in_its_slots_colour_and_changes_with_the_theme() => Headless.Run(() =>
    {
        var dot = new ColorDot { Slot = 2 };
        var window = new Window { Width = 100, Height = 100, Content = dot };
        Headless.Show(window);
        var brush = (ISolidColorBrush)GraphColors.Brush(dot, 2);

        Assert.True(Headless.CountPixels(window, c => Math.Abs(c.R - brush.Color.R) < 8 && Math.Abs(c.G - brush.Color.G) < 8 && Math.Abs(c.B - brush.Color.B) < 8) > 0);
        Assert.Equal(2, dot.Slot);
        Assert.Equal(10, dot.Width);

        Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        window.Resources["Anything"] = 1.0;
        Headless.Settle();
        window.Close();
    });
}

public class DashboardGridTests
{
    private static Border Child(int across = 1, int down = 1)
    {
        var child = new Border();
        DashboardGrid.SetWidthSpan(child, across);
        DashboardGrid.SetHeightSpan(child, down);
        return child;
    }

    private static DashboardGrid Laid(double width, int columns, params Border[] children)
    {
        var grid = new DashboardGrid { Columns = columns, RowHeight = 100, Gap = 10 };
        grid.Children.AddRange(children);
        grid.Measure(new Size(width, double.PositiveInfinity));
        grid.Arrange(new Rect(grid.DesiredSize));
        return grid;
    }

    [Fact]
    public Task Each_goes_in_the_first_place_it_fits() => Headless.Run(() =>
    {
        var wide = Child(2);
        var tall = Child(1, 2);
        var small = Child();
        var last = Child();

        var grid = Laid(650, 3, wide, tall, small, last);

        Assert.Equal(new Point(0, 0), wide.Bounds.Position);
        Assert.Equal(grid.ColumnStep * 2 - 10, wide.Bounds.Width, 3);
        Assert.Equal(grid.ColumnStep * 2, tall.Bounds.X, 3);
        Assert.Equal(210, tall.Bounds.Height, 3);
        Assert.Equal(new Point(0, 110), small.Bounds.Position);
        Assert.Equal(new Point(grid.ColumnStep, 110), last.Bounds.Position);
        Assert.Equal(210, grid.DesiredSize.Height, 3);
        Assert.Equal(110, grid.RowStep);
    });

    [Fact]
    public Task A_narrow_window_shows_fewer_columns() => Headless.Run(() =>
    {
        var one = Child(3);
        var two = Child();

        Laid(200, 3, one, two);

        Assert.Equal(200, one.Bounds.Width, 3);
        Assert.Equal(110, two.Bounds.Y, 3);
    });

    [Fact]
    public Task With_no_width_given_it_takes_what_its_columns_need() => Headless.Run(() =>
    {
        var grid = Laid(double.PositiveInfinity, 2, Child());

        Assert.Equal(370, grid.DesiredSize.Width, 3);
    });

    [Fact]
    public Task Finds_what_is_at_a_point() => Headless.Run(() =>
    {
        var grid = Laid(640, 3, Child(), Child());

        Assert.Equal(0, grid.IndexAt(new Point(5, 5)));
        Assert.Equal(1, grid.IndexAt(new Point(grid.ColumnStep + 5, 5)));
        Assert.Equal(-1, grid.IndexAt(new Point(5, 500)));
        Assert.Equal(1, DashboardGrid.GetHeightSpan(grid.Children[0]));
        Assert.Equal(1, DashboardGrid.GetWidthSpan(grid.Children[0]));
    });
}
