using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Joktan.Core;
using Joktan.Core.Controls;
using Joktan.Help.Resources;
using Joktan.Help.ViewModels;

namespace Joktan.Help.Views;

public partial class HelpWindow : Window
{
    private static HelpWindow? _open;

    public HelpWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Shows a topic in the help window, opening the window if it is not open. There is only one, and
    /// it has no owner, so it can sit beside the page or dialog being set up, or on another screen.
    /// </summary>
    public static void Open(HelpViewModel vm, string? topic)
    {
        vm.Open(topic);

        if (_open is null)
        {
            _open = new HelpWindow { DataContext = vm };
            _open.Closed += (_, _) => _open = null;
            _open.Show();
            return;
        }

        if (_open.WindowState == WindowState.Minimized)
            _open.WindowState = WindowState.Normal;
        _open.Activate();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is HelpViewModel vm)
            vm.PropertyChanged += OnViewModelPropertyChanged;
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is HelpViewModel vm)
            vm.PropertyChanged -= OnViewModelPropertyChanged;
        base.OnClosed(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WindowStateProperty)
        {
            var maximized = WindowState == WindowState.Maximized;
            MaximizeIcon.Data = maximized ? Icons.WindowRestore : Icons.WindowMaximize;
            ToolTip.SetTip(MaximizeButton, maximized ? Strings.HelpWindow_Restore : Strings.HelpWindow_Maximise);
        }
    }

    // Every topic is read from its top.
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HelpViewModel.Current))
            TopicScroller.Offset = default;
    }

    private void OnLinkClicked(object? sender, LinkClickedEventArgs e)
        => e.Handled = DataContext is HelpViewModel vm && vm.OpenLink(e.Uri);

    // The top bar stands in for the title bar, except over its buttons.
    private void OnTopBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            || (e.Source as Visual)?.FindAncestorOfType<Button>(includeSelf: true) is not null)
            return;

        if (e.ClickCount == 2)
            ToggleMaximized();
        else
            BeginMoveDrag(e);
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object? sender, RoutedEventArgs e) => ToggleMaximized();

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private void ToggleMaximized()
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
}
