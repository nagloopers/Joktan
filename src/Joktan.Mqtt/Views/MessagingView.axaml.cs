using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Joktan.Mqtt.ViewModels;

namespace Joktan.Mqtt.Views;

public partial class MessagingView : UserControl
{
    private static readonly FilePickerFileType Pictures = new("Pictures")
    {
        Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.gif", "*.bmp"],
    };

    public MessagingView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is MessagingViewModel vm)
        {
            vm.EditClientSettings = editor => ClientSettingsWindow.ShowOverAsync(this, editor);
            vm.PickAvatar = PickAvatarAsync;
        }
    }

    private async Task<string?> PickAvatarAsync()
    {
        if (TopLevel.GetTopLevel(this) is not { } top)
            return null;

        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a picture",
            AllowMultiple = false,
            FileTypeFilter = [Pictures, FilePickerFileTypes.All],
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }
}
