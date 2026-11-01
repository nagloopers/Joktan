using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Joktan.Agent.ViewModels;

namespace Joktan.Agent.Views;

public partial class AgentPanel : UserControl
{
    private AgentViewModel? _watched;

    public AgentPanel()
    {
        InitializeComponent();

        // Ahead of the box, which would make a new line of Enter.
        DraftBox.AddHandler(KeyDownEvent, OnDraftKeyDown, RoutingStrategies.Tunnel);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        Watch(DataContext as AgentViewModel);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Watch(DataContext as AgentViewModel);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Watch(null);
    }

    private void Watch(AgentViewModel? agent)
    {
        if (_watched is not null)
            _watched.ContentGrew -= OnContentGrew;
        _watched = agent;
        if (agent is not null)
            agent.ContentGrew += OnContentGrew;
    }

    // Waits for the layout, without which the end is still where it was.
    private void OnContentGrew() => Dispatcher.UIThread.Post(() => Scroller.ScrollToEnd(), DispatcherPriority.Background);

    private void OnDraftKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            return;

        e.Handled = true;
        if (DataContext is AgentViewModel { SendCommand: { } send } && send.CanExecute(null))
            send.Execute(null);
    }
}
