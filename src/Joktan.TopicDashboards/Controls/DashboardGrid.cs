using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;

namespace Joktan.TopicDashboards.Controls;

/// <summary>
/// Lays graphs out on a grid, in order, each in the first place it fits. A narrow window shows fewer
/// columns than were asked for, rather than graphs too small to read.
/// </summary>
public class DashboardGrid : Panel
{
    private const double MinColumnWidth = 180;

    public static readonly StyledProperty<int> ColumnsProperty =
        AvaloniaProperty.Register<DashboardGrid, int>(nameof(Columns), 3);

    public static readonly StyledProperty<double> RowHeightProperty =
        AvaloniaProperty.Register<DashboardGrid, double>(nameof(RowHeight), 240);

    public static readonly StyledProperty<double> GapProperty =
        AvaloniaProperty.Register<DashboardGrid, double>(nameof(Gap), 16);

    public static readonly AttachedProperty<int> WidthSpanProperty =
        AvaloniaProperty.RegisterAttached<DashboardGrid, Control, int>("WidthSpan", 1);

    public static readonly AttachedProperty<int> HeightSpanProperty =
        AvaloniaProperty.RegisterAttached<DashboardGrid, Control, int>("HeightSpan", 1);

    private readonly List<Rect> _places = [];

    static DashboardGrid()
    {
        AffectsMeasure<DashboardGrid>(ColumnsProperty, RowHeightProperty, GapProperty);
        AffectsParentMeasure<DashboardGrid>(WidthSpanProperty, HeightSpanProperty);
    }

    public int Columns
    {
        get => GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    public double RowHeight
    {
        get => GetValue(RowHeightProperty);
        set => SetValue(RowHeightProperty, value);
    }

    public double Gap
    {
        get => GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    /// <summary>The width of a column and the space to the next, as last laid out.</summary>
    public double ColumnStep { get; private set; }

    /// <summary>The height of a row and the space to the next.</summary>
    public double RowStep => RowHeight + Gap;

    /// <summary>How many columns fit, which can be fewer than <see cref="Columns"/> when there is little room.</summary>
    public int ShownColumns { get; private set; } = 1;

    public static int GetWidthSpan(Control control) => control.GetValue(WidthSpanProperty);

    public static void SetWidthSpan(Control control, int value) => control.SetValue(WidthSpanProperty, value);

    public static int GetHeightSpan(Control control) => control.GetValue(HeightSpanProperty);

    public static void SetHeightSpan(Control control, int value) => control.SetValue(HeightSpanProperty, value);

    /// <summary>The position of the graph at a point in this panel, or -1.</summary>
    public int IndexAt(Point point)
    {
        for (var i = 0; i < _places.Count; i++)
        {
            if (_places[i].Contains(point))
                return i;
        }

        return -1;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? MinColumnWidth * Columns + Gap * (Columns - 1) : availableSize.Width;
        var columns = Math.Clamp((int)((width + Gap) / (MinColumnWidth + Gap)), 1, Math.Max(1, Columns));
        ShownColumns = columns;
        var columnWidth = (width - Gap * (columns - 1)) / columns;
        ColumnStep = columnWidth + Gap;

        _places.Clear();
        var taken = new List<bool[]>();
        var height = 0.0;
        foreach (var child in Children)
        {
            var across = Math.Clamp(GetWidthSpan(child), 1, columns);
            var down = Math.Max(1, GetHeightSpan(child));
            var (row, column) = FirstPlace(taken, columns, across, down);
            for (var r = row; r < row + down; r++)
            {
                for (var c = column; c < column + across; c++)
                    taken[r][c] = true;
            }

            var place = new Rect(
                column * ColumnStep,
                row * RowStep,
                across * columnWidth + (across - 1) * Gap,
                down * RowHeight + (down - 1) * Gap);
            _places.Add(place);
            child.Measure(place.Size);
            height = Math.Max(height, place.Bottom);
        }

        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        for (var i = 0; i < Children.Count && i < _places.Count; i++)
            Children[i].Arrange(_places[i]);
        return finalSize;
    }

    private static (int Row, int Column) FirstPlace(List<bool[]> taken, int columns, int across, int down)
    {
        for (var row = 0; ; row++)
        {
            while (taken.Count < row + down)
                taken.Add(new bool[columns]);

            for (var column = 0; column + across <= columns; column++)
            {
                if (IsFree(taken, row, column, across, down))
                    return (row, column);
            }
        }
    }

    private static bool IsFree(List<bool[]> taken, int row, int column, int across, int down)
    {
        for (var r = row; r < row + down; r++)
        {
            for (var c = column; c < column + across; c++)
            {
                if (taken[r][c])
                    return false;
            }
        }

        return true;
    }
}
