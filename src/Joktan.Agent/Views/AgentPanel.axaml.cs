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

        // Held to talk, so a press and a release and not a click. Ahead of the button, which marks them handled.
        TalkButton.AddHandler(PointerPressedEvent, OnTalkPressed, RoutingStrategies.Tunnel);
        TalkButton.AddHandler(PointerReleasedEvent, OnTalkReleased, RoutingStrategies.Tunnel);
        TalkButton.AddHandler(PointerCaptureLostEvent, OnTalkCaptureLost);
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
        {
            _watched.ContentGrew -= OnContentGrew;
            _watched.DraftFocusRequested -= OnDraftFocusRequested;
        }

        _watched = agent;
        if (agent is not null)
        {
            agent.ContentGrew += OnContentGrew;
            agent.DraftFocusRequested += OnDraftFocusRequested;
        }
    }

    // Waits for the layout, without which the end is still where it was.
    private void OnContentGrew() => Dispatcher.UIThread.Post(() => Scroller.ScrollToEnd(), DispatcherPriority.Background);

    private void OnTalkPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is AgentViewModel agent && e.GetCurrentPoint(TalkButton).Properties.IsLeftButtonPressed)
            agent.StartListening();
    }

    private void OnTalkReleased(object? sender, PointerReleasedEventArgs e) => FinishTalking();

    // If the pointer is taken away while held, what was said still goes.
    private void OnTalkCaptureLost(object? sender, PointerCaptureLostEventArgs e) => FinishTalking();

    private void FinishTalking()
    {
        if (DataContext is AgentViewModel { IsListening: true } agent)
            _ = agent.FinishListeningAsync();
    }

    private void OnDraftFocusRequested()
    {
        PromptsButton.Flyout?.Hide();
        DraftBox.Focus();
        DraftBox.CaretIndex = DraftBox.Text?.Length ?? 0;
    }

    private void OnDraftKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            return;

        e.Handled = true;
        if (DataContext is AgentViewModel { SendCommand: { } send } && send.CanExecute(null))
            send.Execute(null);
    }
}
