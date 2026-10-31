using System;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Joktan.Mqtt.ViewModels;

namespace Joktan.Mqtt.Views;

public partial class InboxMessagesPanel : UserControl
{
    public InboxMessagesPanel()
    {
        InitializeComponent();
    }

    private InboxViewModel? Inbox => DataContext as InboxViewModel;

    // The newest message is at the bottom, so that is what the list keeps in view.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (Inbox is { } inbox)
        {
            inbox.Messages.CollectionChanged += OnMessagesChanged;
            inbox.DraftFocusRequested += OnDraftFocusRequested;
        }

        // Unless the search box led to one message, which is what it keeps in view instead.
        Dispatcher.UIThread.Post(() =>
        {
            if (Inbox?.FoundMessage is { } found)
                MessageList.ContainerFromItem(found)?.BringIntoView();
            else
                Scroller.ScrollToEnd();
        }, DispatcherPriority.Background);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (Inbox is { } inbox)
        {
            inbox.Messages.CollectionChanged -= OnMessagesChanged;
            inbox.DraftFocusRequested -= OnDraftFocusRequested;
        }
    }

    private void OnDraftFocusRequested()
    {
        DraftBox.Focus();
        DraftBox.CaretIndex = DraftBox.Text?.Length ?? 0;
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e) => ScrollToEnd();

    private void ScrollToEnd() => Dispatcher.UIThread.Post(() => Scroller.ScrollToEnd(), DispatcherPriority.Background);
}
