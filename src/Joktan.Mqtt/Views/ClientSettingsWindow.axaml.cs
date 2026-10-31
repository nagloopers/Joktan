using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Joktan.Mqtt.ViewModels;

namespace Joktan.Mqtt.Views;

public partial class ClientSettingsWindow : Window
{
    private static readonly FilePickerFileType Certificates = new("Certificates")
    {
        Patterns = ["*.pem", "*.crt", "*.cer", "*.der", "*.pfx", "*.p12"],
    };

    private static readonly FilePickerFileType Keys = new("Private keys")
    {
        Patterns = ["*.key", "*.pem"],
    };

    public ClientSettingsWindow()
    {
        InitializeComponent();
    }

    /// <summary>Shows the editor over the window holding a view, and returns whether it was saved.</summary>
    public static async Task<bool> ShowOverAsync(Visual view, ClientSettingsViewModel vm)
    {
        if (TopLevel.GetTopLevel(view) is not Window owner)
            return false;

        return await new ClientSettingsWindow { DataContext = vm }.ShowDialog<bool>(owner);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is ClientSettingsViewModel vm)
        {
            vm.CloseRequested += saved => Close(saved);
            vm.PickFile = PickFileAsync;
        }
    }

    private async Task<string?> PickFileAsync(CertificateFileKind kind)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = kind switch
            {
                CertificateFileKind.Authority => "Choose the CA certificate",
                CertificateFileKind.Client => "Choose the client certificate",
                _ => "Choose the private key",
            },
            AllowMultiple = false,
            FileTypeFilter = [kind == CertificateFileKind.Key ? Keys : Certificates, FilePickerFileTypes.All],
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }
}
