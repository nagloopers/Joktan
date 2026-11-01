using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Joktan.Certificates.Resources;
using Joktan.Certificates.ViewModels;

namespace Joktan.Certificates.Views;

public partial class NewCertificateWindow : Window
{
    public NewCertificateWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is NewCertificateViewModel vm)
        {
            vm.CloseRequested += made => Close(made);
            vm.PickFolder = PickFolderAsync;
        }
    }

    private async Task<string?> PickFolderAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Strings.NewCertificateWindow_ChooseTheFolderToMakeThe,
            AllowMultiple = false,
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }
}
