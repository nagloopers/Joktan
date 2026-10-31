using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Joktan.Core;
using Joktan.Mqtt.ViewModels;
using Joktan.ViewModels;

namespace Joktan.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WindowStateProperty)
        {
            var maximized = WindowState == WindowState.Maximized;
            MaximizeIcon.Data = maximized ? Icons.WindowRestore : Icons.WindowMaximize;
            ToolTip.SetTip(MaximizeButton, maximized ? "Restore" : "Maximise");
        }
    }

    // The top bar stands in for the title bar, except over its buttons and search box.
    private void OnTopBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            || (e.Source as Visual)?.FindAncestorOfType<Button>(includeSelf: true) is not null
            || (e.Source as Visual)?.FindAncestorOfType<TextBox>(includeSelf: true) is not null)
            return;

        if (e.ClickCount == 2)
            ToggleMaximized();
        else
            BeginMoveDrag(e);
    }

    // A panel of the top bar closes when its server button goes to the settings page.
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is MainViewModel main)
        {
            main.PropertyChanged += (_, change) =>
            {
                if (change.PropertyName == nameof(MainViewModel.CurrentPage))
                {
                    MessagesButton.Flyout?.Hide();
                    AlertsButton.Flyout?.Hide();
                }
            };
        }
    }

    private InboxViewModel? Inbox => (DataContext as MainViewModel)?.Inbox;

    private void OnMessagesOpened(object? sender, EventArgs e) => SetOpen(inbox => inbox.IsMessagesOpen = true);

    private void OnMessagesClosed(object? sender, EventArgs e) => SetOpen(inbox => inbox.IsMessagesOpen = false);

    private void OnAlertsOpened(object? sender, EventArgs e) => SetOpen(inbox => inbox.IsAlertsOpen = true);

    private void OnAlertsClosed(object? sender, EventArgs e) => SetOpen(inbox => inbox.IsAlertsOpen = false);

    private void SetOpen(Action<InboxViewModel> set)
    {
        if (Inbox is { } inbox)
            set(inbox);
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object? sender, RoutedEventArgs e) => ToggleMaximized();

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private void ToggleMaximized()
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
}
