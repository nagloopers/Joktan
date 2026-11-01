using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Joktan.Certificates.Resources;
using Joktan.Certificates.ViewModels;

namespace Joktan.Certificates.Views;

public partial class ExportCertificateWindow : Window
{
    public ExportCertificateWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is ExportCertificateViewModel vm)
        {
            vm.CloseRequested += written => Close(written);
            vm.PickFolder = PickFolderAsync;
        }
    }

    private async Task<string?> PickFolderAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Strings.ExportCertificateWindow_ChooseTheFolderToExportTo,
            AllowMultiple = false,
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }
}
