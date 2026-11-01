using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Joktan.Core.Controls;

/// <summary>
/// A small trend line with a tinted area beneath it. Values are plotted on a fixed
/// <see cref="Minimum"/>..<see cref="Maximum"/> scale, newest at the right edge.
/// </summary>
public class Sparkline : Control
{
    public static readonly StyledProperty<IReadOnlyList<double>?> ValuesProperty =
        AvaloniaProperty.Register<Sparkline, IReadOnlyList<double>?>(nameof(Values));

    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<Sparkline, IBrush?>(nameof(Stroke));

    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<Sparkline, double>(nameof(Minimum));

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<Sparkline, double>(nameof(Maximum), 100);

    public static readonly StyledProperty<int> CapacityProperty =
        AvaloniaProperty.Register<Sparkline, int>(nameof(Capacity), 30);

    static Sparkline()
    {
        AffectsRender<Sparkline>(ValuesProperty, StrokeProperty, MinimumProperty, MaximumProperty, CapacityProperty);
    }

    public IReadOnlyList<double>? Values
    {
        get => GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public double Minimum
    {
        get => GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    /// <summary>The number of samples that fill the full width.</summary>
    public int Capacity
    {
        get => GetValue(CapacityProperty);
        set => SetValue(CapacityProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var values = Values;
        if (values is null || values.Count < 2 || Stroke is null || Maximum <= Minimum || Capacity < 2)
            return;

        const double thickness = 2;
        var width = Bounds.Width;
        var height = Bounds.Height;
        var step = width / (Capacity - 1);
        var first = Math.Max(0, values.Count - Capacity);
        var count = values.Count - first;

        Point At(int i)
        {
            var x = width - (count - 1 - i) * step;
            var ratio = Math.Clamp((values[first + i] - Minimum) / (Maximum - Minimum), 0, 1);
            // Inset by half the stroke so the line is not clipped at 0% or 100%.
            var y = thickness / 2 + (1 - ratio) * (height - thickness);
            return new Point(x, y);
        }

        var line = new StreamGeometry();
        using (var g = line.Open())
        {
            g.BeginFigure(At(0), false);
            for (var i = 1; i < count; i++)
                g.LineTo(At(i));
            g.EndFigure(false);
        }

        var area = new StreamGeometry();
        using (var g = area.Open())
        {
            g.BeginFigure(new Point(At(0).X, height));
            for (var i = 0; i < count; i++)
                g.LineTo(At(i));
            g.LineTo(new Point(width, height));
            g.EndFigure(true);
        }

        using (context.PushOpacity(0.12))
            context.DrawGeometry(Stroke, null, area);

        context.DrawGeometry(null, new Pen(Stroke, thickness) { LineJoin = PenLineJoin.Round }, line);
    }
}
