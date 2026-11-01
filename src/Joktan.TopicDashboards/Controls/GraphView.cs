using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using Joktan.TopicDashboards.Resources;
using Joktan.TopicDashboards.ViewModels;

namespace Joktan.TopicDashboards.Controls;

/// <summary>
/// Draws one graph: lines or areas over time, bars, gauges or plain numbers. Marks carry the value's
/// colour; text stays in the theme's text colours, and values share one scale.
/// </summary>
public class GraphView : Control
{
    private const double YAxisWidth = 44;
    private const double MaxBarWidth = 24;
    private const double MinBarWidth = 4;
    private const double BarGap = 2;
    private const double BarRadius = 4;
    private const double MaxHoverDistance = 48;

    public static readonly StyledProperty<GraphCardViewModel?> CardProperty =
        AvaloniaProperty.Register<GraphView, GraphCardViewModel?>(nameof(Card));

    /// <summary>Changes whenever there is something new to draw.</summary>
    public static readonly StyledProperty<int> RevisionProperty =
        AvaloniaProperty.Register<GraphView, int>(nameof(Revision));

    public static readonly StyledProperty<IBrush?> TextBrushProperty =
        AvaloniaProperty.Register<GraphView, IBrush?>(nameof(TextBrush));

    public static readonly StyledProperty<IBrush?> MutedBrushProperty =
        AvaloniaProperty.Register<GraphView, IBrush?>(nameof(MutedBrush));

    public static readonly StyledProperty<IBrush?> GridBrushProperty =
        AvaloniaProperty.Register<GraphView, IBrush?>(nameof(GridBrush));

    public static readonly StyledProperty<IBrush?> SurfaceBrushProperty =
        AvaloniaProperty.Register<GraphView, IBrush?>(nameof(SurfaceBrush));

    private Point? _pointer;

    static GraphView()
    {
        AffectsRender<GraphView>(CardProperty, RevisionProperty, TextBrushProperty, MutedBrushProperty, GridBrushProperty, SurfaceBrushProperty);
    }

    public GraphCardViewModel? Card
    {
        get => GetValue(CardProperty);
        set => SetValue(CardProperty, value);
    }

    public int Revision
    {
        get => GetValue(RevisionProperty);
        set => SetValue(RevisionProperty, value);
    }

    public IBrush? TextBrush
    {
        get => GetValue(TextBrushProperty);
        set => SetValue(TextBrushProperty, value);
    }

    public IBrush? MutedBrush
    {
        get => GetValue(MutedBrushProperty);
        set => SetValue(MutedBrushProperty, value);
    }

    public IBrush? GridBrush
    {
        get => GetValue(GridBrushProperty);
        set => SetValue(GridBrushProperty, value);
    }

    public IBrush? SurfaceBrush
    {
        get => GetValue(SurfaceBrushProperty);
        set => SetValue(SurfaceBrushProperty, value);
    }

    public GraphView()
    {
        // A theme can change the colours and text without changing between light and dark.
        ResourcesChanged += (_, _) => InvalidateVisual();
    }

    private double AxisFontSize => FontSizeOf("GraphAxisFontSize", 11);

    private double LabelFontSize => FontSizeOf("GraphLabelFontSize", 12);

    private double XAxisHeight => Math.Max(20, AxisFontSize * 1.25 + 6);

    private double LabelHeight => Math.Max(18, LabelFontSize * 1.5);

    private double FontSizeOf(string key, double fallback)
        => this.TryFindResource(key, ActualThemeVariant, out var found) && found is double size ? size : fallback;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ThemeVariantScope.ActualThemeVariantProperty)
            InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        _pointer = e.GetPosition(this);
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _pointer = null;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        // Hit testing needs something drawn across the whole control for the hover to work.
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));

        if (Card is not { } card || card.Series.Count == 0 || Bounds.Width < 40 || Bounds.Height < 40)
            return;

        switch (card.Profile.Kind)
        {
            case GraphKind.Gauge:
                DrawTiles(context, card, DrawGauge);
                break;
            case GraphKind.Value:
                DrawTiles(context, card, DrawNumber);
                break;
            case GraphKind.Bars:
                DrawBars(context, card);
                break;
            default:
                DrawOverTime(context, card);
                break;
        }
    }

    // Lines and areas

    private void DrawOverTime(DrawingContext context, GraphCardViewModel card)
    {
        var plot = Plot();
        var end = DateTime.Now;
        var start = end - TimeSpan.FromMinutes(card.Profile.SpanMinutes);
        var isArea = card.Profile.Kind == GraphKind.Area;
        var (bottom, top) = Scale(card, card.Series.SelectMany(s => Visible(s, start)), includeZero: isArea);

        DrawValueAxis(context, card, plot, bottom, top);
        DrawTimeAxis(context, plot, start, end);

        double X(DateTime time) => plot.X + (time - start).TotalSeconds / (end - start).TotalSeconds * plot.Width;
        double Y(double value) => plot.Bottom - (value - bottom) / (top - bottom) * plot.Height;

        using (context.PushClip(plot.Inflate(new Thickness(0, 2))))
        {
            foreach (var series in card.Series)
            {
                var samples = VisibleSamples(series, start);
                if (samples.Count == 0)
                    continue;

                var brush = GraphColors.Brush(this, series.Slot);
                if (samples.Count > 1)
                {
                    if (isArea)
                    {
                        var baseline = Y(Math.Clamp(0, bottom, top));
                        var area = new StreamGeometry();
                        using (var g = area.Open())
                        {
                            g.BeginFigure(new Point(X(samples[0].Time), baseline));
                            foreach (var sample in samples)
                                g.LineTo(new Point(X(sample.Time), Y(sample.Value)));
                            g.LineTo(new Point(X(samples[^1].Time), baseline));
                            g.EndFigure(true);
                        }

                        using (context.PushOpacity(0.12))
                            context.DrawGeometry(brush, null, area);
                    }

                    var line = new StreamGeometry();
                    using (var g = line.Open())
                    {
                        g.BeginFigure(new Point(X(samples[0].Time), Y(samples[0].Value)), false);
                        for (var i = 1; i < samples.Count; i++)
                            g.LineTo(new Point(X(samples[i].Time), Y(samples[i].Value)));
                        g.EndFigure(false);
                    }

                    context.DrawGeometry(null, new Pen(brush, 2) { LineJoin = PenLineJoin.Round, LineCap = PenLineCap.Round }, line);
                }

                DrawDot(context, brush, new Point(X(samples[^1].Time), Y(samples[^1].Value)));
            }
        }

        if (_pointer is { } pointer && plot.Inflate(4).Contains(pointer))
            DrawOverTimeHover(context, card, plot, pointer, start, end, X, Y);
    }

    private void DrawOverTimeHover(DrawingContext context, GraphCardViewModel card, Rect plot, Point pointer, DateTime start, DateTime end,
        Func<DateTime, double> x, Func<double, double> y)
    {
        var at = start + (end - start) * Math.Clamp((pointer.X - plot.X) / plot.Width, 0, 1);
        var rows = new List<(int Slot, string Text)>();
        DateTime? shownTime = null;
        var nearestX = pointer.X;
        var nearestDistance = double.MaxValue;

        foreach (var series in card.Series)
        {
            var samples = VisibleSamples(series, start);
            if (samples.Count == 0)
                continue;

            var nearest = samples.MinBy(s => Math.Abs((s.Time - at).TotalSeconds));
            if (Math.Abs(x(nearest.Time) - pointer.X) > MaxHoverDistance)
                continue;

            rows.Add((series.Slot, $"{series.DisplayLabel}: {card.Format(nearest.Value)}"));
            DrawDot(context, GraphColors.Brush(this, series.Slot), new Point(x(nearest.Time), y(nearest.Value)));
            var distance = Math.Abs(x(nearest.Time) - pointer.X);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestX = x(nearest.Time);
                shownTime = nearest.Time;
            }
        }

        if (shownTime is not { } time)
            return;

        context.DrawLine(new Pen(MutedBrush), new Point(nearestX, plot.Top), new Point(nearestX, plot.Bottom));
        DrawTooltip(context, pointer, time.ToString("HH:mm:ss"), rows);
    }

    // Bars

    private void DrawBars(DrawingContext context, GraphCardViewModel card)
    {
        var plot = Plot();
        var start = DateTime.Now - TimeSpan.FromMinutes(card.Profile.SpanMinutes);
        var bars = new List<(int Slot, double Value, string Label, string Tip)>();

        if (card.Series.Count == 1)
        {
            // One number: its recent readings, as many as fit.
            var series = card.Series[0];
            var fit = (int)(plot.Width / (MinBarWidth + BarGap));
            var samples = VisibleSamples(series, start);
            foreach (var sample in samples.Skip(Math.Max(0, samples.Count - fit)))
                bars.Add((series.Slot, sample.Value, sample.Time.ToString("HH:mm:ss"), series.DisplayLabel));
        }
        else
        {
            // Several numbers: the latest of each, side by side.
            foreach (var series in card.Series)
            {
                if (series.Latest is { } latest)
                    bars.Add((series.Slot, latest, series.DisplayLabel, series.DisplayLabel));
            }
        }

        if (bars.Count == 0)
            return;

        var (bottom, top) = Scale(card, bars.Select(b => b.Value), includeZero: true);
        DrawValueAxis(context, card, plot, bottom, top);

        double Y(double value) => plot.Bottom - (Math.Clamp(value, bottom, top) - bottom) / (top - bottom) * plot.Height;
        var baseline = Y(Math.Clamp(0, bottom, top));

        var slot = plot.Width / Math.Max(bars.Count, card.Series.Count == 1 ? 12 : 1);
        var width = Math.Clamp(slot - BarGap, MinBarWidth, MaxBarWidth);
        var hovered = -1;
        for (var i = 0; i < bars.Count; i++)
        {
            // Newest at the right edge, so bars come in from there as they do on a line.
            var center = card.Series.Count == 1
                ? plot.Right - (bars.Count - 0.5 - i) * slot
                : plot.X + (i + 0.5) * slot;
            var y = Y(bars[i].Value);
            var bar = new Rect(center - width / 2, Math.Min(y, baseline), width, Math.Max(Math.Abs(baseline - y), 1));
            context.DrawGeometry(GraphColors.Brush(this, bars[i].Slot), null, RoundedBar(bar, bars[i].Value >= 0));

            if (_pointer is { } pointer && Math.Abs(pointer.X - center) <= Math.Max(slot, 24) / 2 && plot.Inflate(4).Contains(pointer))
                hovered = i;

            // Under each bar when there are several numbers; otherwise only the two ends are named.
            if (card.Series.Count > 1)
                DrawText(context, bars[i].Label, center, plot.Bottom + 3, MutedBrush, AxisFontSize, TextAlignment.Center, slot - 4);
        }

        context.DrawLine(new Pen(GridBrush), new Point(plot.X, baseline), new Point(plot.Right, baseline));

        if (card.Series.Count == 1)
        {
            DrawText(context, bars[0].Label, plot.Right - (bars.Count - 0.5) * slot, plot.Bottom + 3, MutedBrush, AxisFontSize, TextAlignment.Left);
            if (bars.Count > 1)
                DrawText(context, bars[^1].Label, plot.Right, plot.Bottom + 3, MutedBrush, AxisFontSize, TextAlignment.Right);
        }

        if (hovered >= 0 && _pointer is { } at)
        {
            var bar = bars[hovered];
            var title = card.Series.Count == 1 ? bar.Label : Strings.GraphView_Latest;
            DrawTooltip(context, at, title, [(bar.Slot, $"{bar.Tip}: {card.Format(bar.Value)}")]);
        }
    }

    private static StreamGeometry RoundedBar(Rect bar, bool upward)
    {
        var radius = Math.Min(BarRadius, Math.Min(bar.Width / 2, bar.Height));
        var corner = new Size(radius, radius);
        var geometry = new StreamGeometry();
        using var g = geometry.Open();
        if (upward)
        {
            g.BeginFigure(bar.BottomLeft);
            g.LineTo(new Point(bar.Left, bar.Top + radius));
            g.ArcTo(new Point(bar.Left + radius, bar.Top), corner, 0, false, SweepDirection.Clockwise);
            g.LineTo(new Point(bar.Right - radius, bar.Top));
            g.ArcTo(new Point(bar.Right, bar.Top + radius), corner, 0, false, SweepDirection.Clockwise);
            g.LineTo(bar.BottomRight);
        }
        else
        {
            g.BeginFigure(bar.TopLeft);
            g.LineTo(new Point(bar.Left, bar.Bottom - radius));
            g.ArcTo(new Point(bar.Left + radius, bar.Bottom), corner, 0, false, SweepDirection.CounterClockwise);
            g.LineTo(new Point(bar.Right - radius, bar.Bottom));
            g.ArcTo(new Point(bar.Right, bar.Bottom - radius), corner, 0, false, SweepDirection.CounterClockwise);
            g.LineTo(bar.TopRight);
        }

        g.EndFigure(true);
        return geometry;
    }

    // Gauges and numbers: one tile for each value

    private void DrawTiles(DrawingContext context, GraphCardViewModel card, Action<DrawingContext, GraphCardViewModel, GraphSeriesViewModel, Rect> draw)
    {
        var count = card.Series.Count;
        var columns = Math.Clamp((int)(Bounds.Width / 130), 1, count);
        var rows = (int)Math.Ceiling(count / (double)columns);
        var width = Bounds.Width / columns;
        var height = Bounds.Height / rows;
        for (var i = 0; i < count; i++)
        {
            var row = i / columns;
            draw(context, card, card.Series[i], new Rect(i % columns * width, row * height, width, height).Deflate(4));
        }
    }

    private void DrawGauge(DrawingContext context, GraphCardViewModel card, GraphSeriesViewModel series, Rect tile)
    {
        var labelled = card.Series.Count > 1;
        var labelHeight = labelled ? LabelHeight : 0;
        var scaleHeight = AxisFontSize * 1.25 + 2;
        var radius = Math.Min(tile.Width / 2 - 8, tile.Height - labelHeight - scaleHeight - 12);
        if (radius < 16)
            return;

        const double thickness = 10;
        var center = new Point(tile.Center.X, tile.Top + (tile.Height - labelHeight - scaleHeight + radius) / 2 + thickness / 2);
        var start = DateTime.Now - TimeSpan.FromMinutes(card.Profile.SpanMinutes);
        var (bottom, top) = Scale(card, card.Series.SelectMany(s => Visible(s, start)), includeZero: true);

        var left = new Point(center.X - radius, center.Y);
        var right = new Point(center.X + radius, center.Y);
        var arcSize = new Size(radius, radius);

        var track = new StreamGeometry();
        using (var g = track.Open())
        {
            g.BeginFigure(left, false);
            g.ArcTo(right, arcSize, 0, false, SweepDirection.Clockwise);
            g.EndFigure(false);
        }

        context.DrawGeometry(null, new Pen(GridBrush, thickness) { LineCap = PenLineCap.Round }, track);

        if (series.Latest is { } latest)
        {
            var ratio = Math.Clamp((latest - bottom) / (top - bottom), 0, 1);
            if (ratio > 0)
            {
                var angle = Math.PI * (1 - ratio);
                var end = new Point(center.X + radius * Math.Cos(angle), center.Y - radius * Math.Sin(angle));
                var fill = new StreamGeometry();
                using (var g = fill.Open())
                {
                    g.BeginFigure(left, false);
                    g.ArcTo(end, arcSize, 0, false, SweepDirection.Clockwise);
                    g.EndFigure(false);
                }

                context.DrawGeometry(null, new Pen(GraphColors.Brush(this, series.Slot), thickness) { LineCap = PenLineCap.Round }, fill);
            }

            var fontSize = Math.Min(radius * 0.42, 40);
            DrawFitted(context, card.Format(latest), center.X, center.Y - fontSize * 1.3, TextBrush, fontSize, radius * 1.5);
        }

        DrawText(context, card.FormatScale(bottom), left.X, center.Y + 7, MutedBrush, AxisFontSize, TextAlignment.Center, radius);
        DrawText(context, card.FormatScale(top), right.X, center.Y + 7, MutedBrush, AxisFontSize, TextAlignment.Center, radius);
        if (labelled)
            DrawText(context, series.DisplayLabel, center.X, center.Y + 7 + scaleHeight, MutedBrush, LabelFontSize, TextAlignment.Center, tile.Width);
    }

    private void DrawNumber(DrawingContext context, GraphCardViewModel card, GraphSeriesViewModel series, Rect tile)
    {
        var labelled = card.Series.Count > 1;
        var start = DateTime.Now - TimeSpan.FromMinutes(card.Profile.SpanMinutes);
        var samples = VisibleSamples(series, start);

        var hasTrend = samples.Count > 1 && tile.Height > 90;
        var trendHeight = hasTrend ? Math.Min(tile.Height * 0.35, 56) : 0;
        var labelHeight = labelled ? LabelHeight : 0;
        var numberArea = tile.Height - trendHeight - labelHeight;
        var fontSize = Math.Clamp(numberArea * 0.55, 14, 44);
        var text = series.Latest is { } latest ? card.Format(latest) : "–";
        var top = tile.Top + (numberArea - fontSize * 1.25) / 2;

        DrawFitted(context, text, tile.Center.X, top, TextBrush, fontSize, tile.Width - 8);
        if (labelled)
            DrawText(context, series.DisplayLabel, tile.Center.X, tile.Top + numberArea, MutedBrush, LabelFontSize, TextAlignment.Center, tile.Width);

        if (!hasTrend)
            return;

        var trend = new Rect(tile.Left + 4, tile.Bottom - trendHeight, tile.Width - 8, trendHeight);
        var (bottom, ceiling) = Scale(card, samples.Select(s => s.Value), includeZero: false, useFixed: false);
        var end = DateTime.Now;
        double X(DateTime t) => trend.X + (t - start).TotalSeconds / (end - start).TotalSeconds * trend.Width;
        double Y(double v) => trend.Bottom - 2 - (v - bottom) / (ceiling - bottom) * (trend.Height - 4);

        var brush = GraphColors.Brush(this, series.Slot);
        using (context.PushClip(trend))
        {
            var area = new StreamGeometry();
            using (var g = area.Open())
            {
                g.BeginFigure(new Point(X(samples[0].Time), trend.Bottom));
                foreach (var sample in samples)
                    g.LineTo(new Point(X(sample.Time), Y(sample.Value)));
                g.LineTo(new Point(X(samples[^1].Time), trend.Bottom));
                g.EndFigure(true);
            }

            using (context.PushOpacity(0.12))
                context.DrawGeometry(brush, null, area);

            var line = new StreamGeometry();
            using (var g = line.Open())
            {
                g.BeginFigure(new Point(X(samples[0].Time), Y(samples[0].Value)), false);
                for (var i = 1; i < samples.Count; i++)
                    g.LineTo(new Point(X(samples[i].Time), Y(samples[i].Value)));
                g.EndFigure(false);
            }

            context.DrawGeometry(null, new Pen(brush, 2) { LineJoin = PenLineJoin.Round, LineCap = PenLineCap.Round }, line);
        }
    }

    // What graphs share

    private Rect Plot() => new(YAxisWidth, 6, Math.Max(Bounds.Width - YAxisWidth - 8, 1), Math.Max(Bounds.Height - XAxisHeight - 8, 1));

    private static IEnumerable<double> Visible(GraphSeriesViewModel series, DateTime start)
        => VisibleSamples(series, start).Select(s => s.Value);

    // The readings in the window, and the one before it so a line enters from the edge.
    private static List<Sample> VisibleSamples(GraphSeriesViewModel series, DateTime start)
    {
        var samples = series.Samples.Samples;
        var first = 0;
        while (first < samples.Count && samples[first].Time < start)
            first++;
        first = Math.Max(0, first - 1);

        var visible = new List<Sample>(samples.Count - first);
        for (var i = first; i < samples.Count; i++)
            visible.Add(samples[i]);
        return visible;
    }

    private static (double Bottom, double Top) Scale(GraphCardViewModel card, IEnumerable<double> values, bool includeZero, bool useFixed = true)
    {
        if (useFixed && card.Profile is { Minimum: { } fixedBottom, Maximum: { } fixedTop })
            return (fixedBottom, fixedTop);

        var bottom = double.MaxValue;
        var top = double.MinValue;
        foreach (var value in values)
        {
            bottom = Math.Min(bottom, value);
            top = Math.Max(top, value);
        }

        if (bottom > top)
            return (0, 1);

        if (includeZero)
        {
            bottom = Math.Min(bottom, 0);
            top = Math.Max(top, 0);
        }

        // A flat line still needs a range to be drawn in.
        if (top - bottom < 1e-9)
        {
            bottom -= 1;
            top += 1;
        }

        var step = NiceStep((top - bottom) / 4);
        var niceBottom = Math.Floor(bottom / step) * step;
        var niceTop = Math.Ceiling(top / step) * step;
        return (niceBottom, niceTop);
    }

    private static double NiceStep(double rough)
    {
        var power = Math.Pow(10, Math.Floor(Math.Log10(rough)));
        var fraction = rough / power;
        return (fraction <= 1 ? 1 : fraction <= 2 ? 2 : fraction <= 5 ? 5 : 10) * power;
    }

    private void DrawValueAxis(DrawingContext context, GraphCardViewModel card, Rect plot, double bottom, double top)
    {
        var pen = new Pen(GridBrush);
        var rows = Math.Clamp((int)(plot.Height / 36), 1, 6);
        var step = NiceStep((top - bottom) / rows);
        var first = Math.Ceiling(bottom / step) * step;
        for (var value = first; value <= top + step * 1e-6; value += step)
        {
            var y = plot.Bottom - (value - bottom) / (top - bottom) * plot.Height;
            context.DrawLine(pen, new Point(plot.X, y), new Point(plot.Right, y));
            DrawText(context, card.FormatScale(value), plot.X - 6, y - 7, MutedBrush, AxisFontSize, TextAlignment.Right, YAxisWidth - 8);
        }
    }

    private void DrawTimeAxis(DrawingContext context, Rect plot, DateTime start, DateTime end)
    {
        var columns = Math.Clamp((int)(plot.Width / 90), 1, 6);
        for (var i = 0; i <= columns; i++)
        {
            var time = start + (end - start) * ((double)i / columns);
            var x = plot.X + plot.Width * i / columns;
            var alignment = i == 0 ? TextAlignment.Left : i == columns ? TextAlignment.Right : TextAlignment.Center;
            DrawText(context, time.ToString("HH:mm:ss"), x, plot.Bottom + 3, MutedBrush, AxisFontSize, alignment);
        }
    }

    // The ring keeps a dot readable where it crosses another line.
    private void DrawDot(DrawingContext context, IBrush brush, Point center)
        => context.DrawEllipse(brush, new Pen(SurfaceBrush, 2), center, 4, 4);

    private void DrawTooltip(DrawingContext context, Point pointer, string title, List<(int Slot, string Text)> rows)
    {
        const double padding = 8;
        const double keyWidth = 14;
        var rowHeight = LabelHeight;
        var headingHeight = AxisFontSize * 1.25 + 2;

        var heading = Text(title, MutedBrush, AxisFontSize);
        var lines = rows.Select(r => (r.Slot, Text: Text(r.Text, TextBrush, LabelFontSize))).ToList();
        var width = Math.Max(heading.Width, lines.Count == 0 ? 0 : lines.Max(l => l.Text.Width + keyWidth)) + padding * 2;
        var height = padding * 2 + headingHeight + lines.Count * rowHeight;

        var x = pointer.X + 12 + width > Bounds.Width ? pointer.X - 12 - width : pointer.X + 12;
        var y = Math.Clamp(pointer.Y - height / 2, 0, Math.Max(0, Bounds.Height - height));
        var box = new Rect(Math.Max(0, x), y, width, height);

        context.DrawRectangle(SurfaceBrush, new Pen(GridBrush), box, 4, 4);
        context.DrawText(heading, new Point(box.X + padding, box.Y + padding));
        for (var i = 0; i < lines.Count; i++)
        {
            var rowY = box.Y + padding + headingHeight + i * rowHeight;
            context.DrawEllipse(GraphColors.Brush(this, lines[i].Slot), null, new Point(box.X + padding + 4, rowY + rowHeight / 2 - 1), 4, 4);
            context.DrawText(lines[i].Text, new Point(box.X + padding + keyWidth, rowY));
        }
    }

    private void DrawFitted(DrawingContext context, string text, double centerX, double y, IBrush? brush, double fontSize, double maxWidth)
    {
        var formatted = Text(text, brush, fontSize, FontWeight.SemiBold);
        if (formatted.Width > maxWidth && formatted.Width > 0)
            formatted = Text(text, brush, Math.Max(10, fontSize * maxWidth / formatted.Width), FontWeight.SemiBold);
        context.DrawText(formatted, new Point(centerX - formatted.Width / 2, y));
    }

    private void DrawText(DrawingContext context, string text, double x, double y, IBrush? brush, double fontSize, TextAlignment alignment, double maxWidth = 0)
    {
        var formatted = Text(text, brush, fontSize);
        if (maxWidth > 0)
        {
            formatted.MaxTextWidth = maxWidth;
            formatted.MaxLineCount = 1;
            formatted.Trimming = TextTrimming.CharacterEllipsis;
        }

        var left = alignment switch
        {
            TextAlignment.Right => x - formatted.Width,
            TextAlignment.Center => x - formatted.Width / 2,
            _ => x,
        };
        context.DrawText(formatted, new Point(left, y));
    }

    private FormattedText Text(string text, IBrush? brush, double fontSize, FontWeight weight = FontWeight.Normal)
        => new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(GetValue(TextElement.FontFamilyProperty), FontStyle.Normal, weight), fontSize, brush ?? Brushes.Gray);
}
