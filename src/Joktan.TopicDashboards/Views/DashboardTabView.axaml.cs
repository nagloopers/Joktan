using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Joktan.TopicDashboards.Controls;
using Joktan.TopicDashboards.ViewModels;

namespace Joktan.TopicDashboards.Views;

/// <summary>One dashboard. Moving and resizing graphs with the pointer is handled here; the rest is in the view model.</summary>
public partial class DashboardTabView : UserControl
{
    private GraphCardViewModel? _moving;
    private GraphCardViewModel? _dropTarget;
    private GraphCardViewModel? _resizing;
    private Point _resizeStart;
    private int _startWidth;
    private int _startHeight;

    public DashboardTabView()
    {
        InitializeComponent();
    }

    private DashboardGrid? Grid => GraphList.ItemsPanelRoot as DashboardGrid;

    private void OnMovePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: GraphCardViewModel card } control || !e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
            return;

        _moving = card;
        e.Pointer.Capture(control);
        e.Handled = true;
    }

    private void OnMoveMoved(object? sender, PointerEventArgs e)
    {
        if (_moving is not { } card || Grid is not { } grid || DataContext is not DashboardTabViewModel tab)
            return;

        card.IsDragging = true;
        var index = grid.IndexAt(e.GetPosition(grid));
        SetDropTarget(index >= 0 && index < tab.Graphs.Count && tab.Graphs[index] != card ? tab.Graphs[index] : null);
    }

    private void OnMoveReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_moving is { } card && _dropTarget is { } target && DataContext is DashboardTabViewModel tab)
            tab.MoveTo(card, tab.Graphs.IndexOf(target));

        EndMove();
        e.Pointer.Capture(null);
    }

    private void OnMoveCaptureLost(object? sender, PointerCaptureLostEventArgs e) => EndMove();

    private void EndMove()
    {
        if (_moving is { } card)
            card.IsDragging = false;
        _moving = null;
        SetDropTarget(null);
    }

    private void SetDropTarget(GraphCardViewModel? card)
    {
        if (_dropTarget == card)
            return;

        if (_dropTarget is { } previous)
            previous.IsDropTarget = false;
        _dropTarget = card;
        if (card is not null)
            card.IsDropTarget = true;
    }

    private void OnResizePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: GraphCardViewModel card } control || Grid is not { } grid || !e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
            return;

        _resizing = card;
        _resizeStart = e.GetPosition(grid);
        _startWidth = card.WidthSpan;
        _startHeight = card.HeightSpan;
        e.Pointer.Capture(control);
        e.Handled = true;
    }

    private void OnResizeMoved(object? sender, PointerEventArgs e)
    {
        if (_resizing is not { } card || Grid is not { } grid)
            return;

        // The graph snaps to whole columns and rows, changing size once past halfway.
        var moved = e.GetPosition(grid) - _resizeStart;

        // Counted from the columns it fills now, as a graph saved wider than will fit would otherwise have
        // to lose its unseen columns before it looked any different. Without a move across, the saved
        // width stays, for when there is room again.
        var across = (int)Math.Round(moved.X / grid.ColumnStep);
        card.WidthSpan = across == 0 ? _startWidth : Math.Clamp(Math.Min(_startWidth, grid.ShownColumns) + across, 1, grid.ShownColumns);
        card.HeightSpan = Math.Clamp(_startHeight + (int)Math.Round(moved.Y / grid.RowStep), 1, GraphProfile.MaxHeight);
    }

    private void OnResizeReleased(object? sender, PointerReleasedEventArgs e)
    {
        _resizing = null;
        e.Pointer.Capture(null);
    }
}
