using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Joktan.Containers.Resources;
using Joktan.Containers.ViewModels;

namespace Joktan.Containers.Views;

public partial class ContainersView : UserControl
{
    private const double ServicesHeightWithLogs = 420;

    private ContainersViewModel? _vm;

    public ContainersView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null)
        {
            _vm.LogLinesAdded -= OnLogLinesAdded;
            _vm.LogsShown -= OnLogsShown;
            _vm.Output.CollectionChanged -= OnOutputChanged;
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _vm = DataContext as ContainersViewModel;
        if (_vm is not null)
        {
            _vm.PickComposeFiles = PickComposeFilesAsync;
            _vm.LogLinesAdded += OnLogLinesAdded;
            _vm.LogsShown += OnLogsShown;
            _vm.Output.CollectionChanged += OnOutputChanged;
            _vm.PropertyChanged += OnViewModelPropertyChanged;
            ApplyLogPanel();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ContainersViewModel.IsLogPanelOpen))
            ApplyLogPanel();
    }

    // With the logs open the services take what they need, up to a limit, and the logs the rest.
    // Collapsed, the logs take only their bar and the services everything else.
    private void ApplyLogPanel()
    {
        var open = _vm?.IsLogPanelOpen ?? true;
        Body.RowDefinitions[0].Height = open ? GridLength.Auto : GridLength.Star;
        Body.RowDefinitions[2].Height = open ? GridLength.Star : GridLength.Auto;
        ServicesScroll.MaxHeight = open ? ServicesHeightWithLogs : double.PositiveInfinity;

        // Later, as a list that was hidden has not been laid out yet.
        if (open)
            Dispatcher.UIThread.Post(OnLogLinesAdded, DispatcherPriority.Background);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _vm?.Activate();
        OnLogLinesAdded();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _vm?.Deactivate();
    }

    private void OnLogLinesAdded()
    {
        if (_vm is { AutoScroll: true, LogLines.Count: > 0 })
            LogList.ScrollIntoView(_vm.LogLines.Count - 1);
    }

    private void OnLogsShown() => BottomTabs.SelectedIndex = 0;

    private async void OnCopyContainerNameClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is ComposeServiceRow { HasContainer: true } row
            && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(row.Name);
    }

    private void OnOutputChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && _vm is { Output.Count: > 0 })
            OutputList.ScrollIntoView(_vm.Output.Count - 1);
    }

    private async Task<IReadOnlyList<string>> PickComposeFilesAsync()
    {
        if (TopLevel.GetTopLevel(this) is not { } top)
            return [];

        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Strings.ContainersView_ChooseComposeFiles,
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType(Strings.ContainersView_ComposeFiles) { Patterns = ["*.yml", "*.yaml"] },
                FilePickerFileTypes.All,
            ],
        });
        return files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
    }

    private async void OnCopyLogsClick(object? sender, RoutedEventArgs e)
    {
        if (_vm is not null)
            await CopyAsync(_vm.LogLines);
    }

    private async void OnCopyOutputClick(object? sender, RoutedEventArgs e)
    {
        if (_vm is not null)
            await CopyAsync(_vm.Output);
    }

    private async Task CopyAsync(IEnumerable<string> lines)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(string.Join(Environment.NewLine, lines));
    }
}
