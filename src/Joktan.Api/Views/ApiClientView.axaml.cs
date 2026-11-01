using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Joktan.Api.Resources;
using Joktan.Api.ViewModels;

namespace Joktan.Api.Views;

public partial class ApiClientView : UserControl
{
    public ApiClientView()
    {
        InitializeComponent();

        // A new name is applied when it is finished: on Enter, or on leaving the box.
        NameBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
                (DataContext as ApiClientViewModel)?.RenameCommand.Execute(null);
        };
        NameBox.LostFocus += (_, _) => (DataContext as ApiClientViewModel)?.RenameCommand.Execute(null);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is ApiClientViewModel vm)
        {
            vm.PickFiles = PickAsync;
            vm.CopyText = CopyTextAsync;
        }
    }

    // Folders when asked for, otherwise .proto files.
    private async Task<IReadOnlyList<string>> PickAsync(bool folders)
    {
        if (TopLevel.GetTopLevel(this) is not { } top)
            return [];

        IEnumerable<IStorageItem> picked = folders
            ? await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = Strings.ApiClientView_ChooseAFolder, AllowMultiple = false })
            : await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = Strings.ApiClientView_ChooseProtoFiles,
                AllowMultiple = true,
                FileTypeFilter = [new FilePickerFileType(Strings.ApiClientView_ProtocolBuffers) { Patterns = ["*.proto"] }],
            });
        return picked.Select(i => i.TryGetLocalPath()).OfType<string>().ToList();
    }

    private async Task CopyTextAsync(string text)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(text);
    }
}
