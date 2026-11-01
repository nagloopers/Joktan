using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Joktan.TopicDashboards.Resources;
using Joktan.TopicDashboards.ViewModels;

namespace Joktan.TopicDashboards.Views;

public partial class TopicDashboardsView : UserControl
{
    public TopicDashboardsView()
    {
        InitializeComponent();
        AddHandler(PointerPressedEvent, OnPointerPressedAnywhere, RoutingStrategies.Tunnel);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is TopicDashboardsViewModel vm)
        {
            vm.ShowEditor = ShowEditorAsync;
            vm.PickFolder = PickFolderAsync;
        }
    }

    private async Task<string?> PickFolderAsync()
    {
        if (TopLevel.GetTopLevel(this) is not { } top)
            return null;

        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Strings.TopicDashboardsView_ChooseTheFolderForDashboards,
            AllowMultiple = false,
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    private Task ShowEditorAsync(GraphEditorViewModel editor)
        => TopLevel.GetTopLevel(this) is Window owner
            ? new GraphEditorWindow { DataContext = editor }.ShowDialog(owner)
            : Task.CompletedTask;

    // Most of the page cannot take the focus, so pressing there would leave the name being typed.
    private void OnPointerPressedAnywhere(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not TopicDashboardsViewModel { SelectedDashboard: { IsRenaming: true } dashboard })
            return;
        if (e.Source is not Visual source || source.FindAncestorOfType<TextBox>(includeSelf: true) is null)
            dashboard.FinishRenameCommand.Execute(null);
    }

    private void OnTabDoubleTapped(object? sender, TappedEventArgs e)
    {
        // A quick second press on the tab's own buttons, or in the name being typed, is not a rename.
        if (e.Source is Visual source && source.FindAncestorOfType<Button>(includeSelf: true) is not null)
            return;
        if (sender is Control { DataContext: DashboardTabViewModel { IsRenaming: false } dashboard })
            dashboard.RenameCommand.Execute(null);
    }

    private void OnRenameBoxPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != IsVisibleProperty || e.NewValue is not true || sender is not TextBox box)
            return;

        // Later, as a menu that has just closed gives the focus back to where it was.
        Dispatcher.UIThread.Post(() =>
        {
            box.Focus();
            box.SelectAll();
        }, DispatcherPriority.Background);
    }

    private void OnRenameBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not Control { DataContext: DashboardTabViewModel dashboard })
            return;

        switch (e.Key)
        {
            case Key.Enter:
                dashboard.FinishRenameCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Escape:
                dashboard.CancelRenameCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End:
                // At either end of the text these would otherwise reach the tabs, and change tab.
                e.Handled = true;
                break;
        }
    }

    private void OnRenameBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: DashboardTabViewModel dashboard })
            dashboard.FinishRenameCommand.Execute(null);
    }
}
