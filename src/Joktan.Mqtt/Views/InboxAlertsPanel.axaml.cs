using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Joktan.Mqtt.ViewModels;

namespace Joktan.Mqtt.Views;

public partial class InboxAlertsPanel : UserControl
{
    public InboxAlertsPanel()
    {
        InitializeComponent();
    }

    // The search box can open this panel to show one alert.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Dispatcher.UIThread.Post(() =>
        {
            if ((DataContext as InboxViewModel)?.FoundAlert is { } found)
                AlertList.ContainerFromItem(found)?.BringIntoView();
        }, DispatcherPriority.Background);
    }
}
