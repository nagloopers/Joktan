using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Joktan.Agent.ViewModels;
using Joktan.Agent.Views;
using Joktan.Core;
using Joktan.Mqtt.ViewModels;
using Joktan.ViewModels;

namespace Joktan.Views;

public partial class MainWindow : Window
{
    private AgentWindow? _agentWindow;
    private bool _isClosing;

    public MainWindow()
    {
        InitializeComponent();

        // Ahead of the box, which would use the arrow keys for its caret.
        SearchBox.AddHandler(KeyDownEvent, OnSearchKeyDown, RoutingStrategies.Tunnel);
        SearchBox.AddHandler(PointerPressedEvent, (_, _) => Search?.Show(), RoutingStrategies.Tunnel, handledEventsToo: true);
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
            main.Inbox.MessagesPanelRequested += () => MessagesButton.Flyout?.ShowAt(MessagesButton);
            main.Inbox.AlertsPanelRequested += () => AlertsButton.Flyout?.ShowAt(AlertsButton);
            main.Agent.PropertyChanged += (_, change) =>
            {
                if (change.PropertyName is nameof(AgentViewModel.IsOpen) or nameof(AgentViewModel.IsFloating))
                    ShowAgent(main.Agent);
            };
            main.Agent.AttentionRequested += () => _agentWindow?.Activate();
            ShowAgent(main.Agent);
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

    // The panel's column takes its width from the settings, and the splitter sets it from there on.
    private void ShowAgent(AgentViewModel agent)
    {
        var column = ContentGrid.ColumnDefinitions[2];
        if (agent.IsDocked)
        {
            column.MinWidth = 300;
            column.MaxWidth = 900;
            column.Width = new GridLength(agent.Settings.PanelWidth);
        }
        else
        {
            if (column.Width.IsAbsolute && column.ActualWidth > 0)
                agent.Settings.PanelWidth = column.ActualWidth;
            column.MinWidth = 0;
            column.Width = new GridLength(0);
        }

        if (agent.IsInWindow && _agentWindow is null)
        {
            var window = _agentWindow = new AgentWindow
            {
                DataContext = agent,
                Width = agent.Settings.WindowWidth,
                Height = agent.Settings.WindowHeight,
            };
            window.Closed += (_, _) =>
            {
                agent.Settings.WindowWidth = window.Width;
                agent.Settings.WindowHeight = window.Height;
                _agentWindow = null;

                // Closed by its own button rather than by docking, which closes the panel. As the app
                // closes, the panel is remembered as open.
                if (agent.IsInWindow && !_isClosing)
                    agent.IsOpen = false;
                agent.SaveLayout();
            };
            window.Show();
        }
        else if (!agent.IsInWindow)
        {
            _agentWindow?.Close();
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        _isClosing = !e.Cancel;
        if (_isClosing && DataContext is MainViewModel { Agent: { } agent } && ContentGrid.ColumnDefinitions[2] is { ActualWidth: > 0 } column)
        {
            agent.Settings.PanelWidth = column.ActualWidth;
            agent.SaveLayout();
        }
    }

    private InboxViewModel? Inbox => (DataContext as MainViewModel)?.Inbox;

    private SearchViewModel? Search => (DataContext as MainViewModel)?.Search;

    private void OnClearSearchClick(object? sender, RoutedEventArgs e)
    {
        Search?.Clear();
        SearchBox.Focus();
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (Search is not { } search)
            return;

        switch (e.Key)
        {
            case Key.Down when search.IsOpen:
                search.MoveHighlight(1);
                break;
            case Key.Up when search.IsOpen:
                search.MoveHighlight(-1);
                break;
            case Key.Enter when search.IsOpen:
                search.OpenCommand.Execute(search.Highlighted);
                break;
            case Key.Enter:
                search.Show();
                break;
            case Key.Escape:
                search.Dismiss();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    // A press on the scroll bar is not on a result, so has no result as its context.
    private void OnSearchResultTapped(object? sender, TappedEventArgs e)
    {
        if ((e.Source as StyledElement)?.DataContext is SearchHit hit)
            Search?.OpenCommand.Execute(hit);
    }

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
