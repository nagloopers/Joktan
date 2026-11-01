using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Joktan.Certificates.Resources;
using Joktan.Certificates.ViewModels;

namespace Joktan.Certificates.Views;

public partial class CertificateDefaultsView : UserControl
{
    public CertificateDefaultsView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is CertificateDefaultsViewModel vm)
            vm.PickFolder = PickFolderAsync;
    }

    private async Task<string?> PickFolderAsync()
    {
        if (TopLevel.GetTopLevel(this) is not { } top)
            return null;

        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Strings.CertificateDefaultsView_ChooseTheFolderForMQTTClients,
            AllowMultiple = false,
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }
}
