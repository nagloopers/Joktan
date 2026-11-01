using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Joktan.Certificates.ViewModels;

namespace Joktan.Certificates.Views;

public partial class CertificatesView : UserControl
{
    public CertificatesView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is CertificatesViewModel vm)
        {
            vm.EditNewCertificate = EditNewCertificateAsync;
            vm.EditExport = EditExportAsync;
        }
    }

    // The MQTT clients may have changed which files they use since the page was last shown.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        (DataContext as CertificatesViewModel)?.Refresh();
    }

    private async Task<bool> EditNewCertificateAsync(NewCertificateViewModel vm)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner)
            return false;

        return await new NewCertificateWindow { DataContext = vm }.ShowDialog<bool>(owner);
    }

    private async Task<bool> EditExportAsync(ExportCertificateViewModel vm)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner)
            return false;

        return await new ExportCertificateWindow { DataContext = vm }.ShowDialog<bool>(owner);
    }

    private async void OnOpenFolderClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CertificatesViewModel { Selected: { } selected } && Directory.Exists(selected.Folder)
            && TopLevel.GetTopLevel(this) is { } top)
            await top.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(selected.Folder));
    }
}
