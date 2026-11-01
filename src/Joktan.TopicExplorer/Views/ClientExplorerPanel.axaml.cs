using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Joktan.TopicDashboards.Views;
using Joktan.TopicExplorer.Resources;
using Joktan.TopicExplorer.ViewModels;

namespace Joktan.TopicExplorer.Views;

/// <summary>
/// One client's tab of the topic explorer.
/// </summary>
public partial class ClientExplorerPanel : UserControl
{
    public ClientExplorerPanel()
    {
        InitializeComponent();
    }

    private async void OnCopyTopicClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ClientExplorerViewModel { SelectedNode: { } node }
            && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(node.FullTopic);
        }
    }

    private async void OnAddToDashboardClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ClientExplorerViewModel vm
            || TopLevel.GetTopLevel(this) is not Window owner
            || vm.NewGraphEditor() is not { } editor)
            return;

        // The owner is whichever window the explorer is in, which may be its own.
        await new GraphEditorWindow { DataContext = editor }.ShowDialog(owner);
        if (editor.AddedTo is { } dashboard)
            vm.DashboardStatus = string.Format(Strings.ClientExplorerPanel_AddedToSeeDashboardMQTTDashboards, dashboard);
    }
}
