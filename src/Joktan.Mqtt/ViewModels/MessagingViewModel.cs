using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Core;
using Joktan.Core.ViewModels;

namespace Joktan.Mqtt.ViewModels;

/// <summary>
/// Settings / Alerts and Messages: the MQTT server they go through, the name and picture on the user's
/// messages, and whether the server also shows under Dev Tools / Brokers. Specified in
/// docs/features/alerts-and-messages/spec.md.
/// </summary>
public partial class MessagingViewModel : PageViewModel
{
    /// <summary>The width and height a picture is kept at.</summary>
    public const int AvatarPixels = 128;

    private readonly MqttViewModel _mqtt;
    private readonly InboxViewModel _inbox;

    public MessagingViewModel(MqttViewModel mqtt, InboxViewModel inbox)
        : base("Alerts and Messages", "Settings / Alerts and Messages")
    {
        _mqtt = mqtt;
        _inbox = inbox;
        DisplayName = mqtt.Messaging.DisplayName;
        TopicPrefix = mqtt.Messaging.TopicPrefix;
        Avatar = ReadAvatar();
    }

    /// <summary>The server's client, which owns its connection.</summary>
    public MqttClientViewModel Client => _mqtt.MessagingClient;

    /// <summary>Asks the user for a picture. Set by the view, which owns the system dialog.</summary>
    public Func<Task<string?>>? PickAvatar { get; set; }

    /// <summary>Shows the client editor and returns whether it was saved. Set by the view, as the MQTT page's view is not showing here.</summary>
    public Func<ClientSettingsViewModel, Task<bool>>? EditClientSettings
    {
        set => _mqtt.EditClientSettings = value;
    }

    public int MaxDisplayNameLength => MessagingSettings.MaxDisplayNameLength;

    public bool ShowInBrokers
    {
        get => _mqtt.IsMessagingClientShown;
        set
        {
            _mqtt.IsMessagingClientShown = value;
            OnPropertyChanged();
        }
    }

    public bool ConnectOnStart
    {
        get => _mqtt.Messaging.ConnectOnStart;
        set
        {
            _mqtt.Messaging.ConnectOnStart = value;
            _mqtt.SaveSettings();
            OnPropertyChanged();
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TopicPrefixError))]
    public partial string TopicPrefix { get; set; } = "";

    /// <summary>Why the prefix typed in cannot be used. The one before stays in use until it can.</summary>
    public string? TopicPrefixError => TopicPrefix.Trim().Length == 0 ? "Enter what the topics start with, for example joktan" : TopicRules.Validate(TopicPrefix.Trim());

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AvatarInitial), nameof(DisplayNameError), nameof(DisplayNameWarning))]
    public partial string DisplayName { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAvatar))]
    [NotifyCanExecuteChangedFor(nameof(RemoveAvatarCommand))]
    public partial IImage? Avatar { get; private set; }

    public bool HasAvatar => Avatar is not null;

    /// <summary>Why the name typed in cannot be used. The one before stays in use until it can.</summary>
    public string? DisplayNameError => MessagingSettings.CheckName(DisplayName.Trim());

    /// <summary>Says when someone else on the server has the name typed in.</summary>
    public string? DisplayNameWarning => DisplayNameError is null && _inbox.IsNameTaken(DisplayName.Trim())
        ? $"Someone else on this server is called {DisplayName.Trim()}. Messages to @{DisplayName.Trim()} would reach you both."
        : null;

    /// <summary>What stands in for a picture: the first letter of the name.</summary>
    public string AvatarInitial => InboxViewModel.InitialOf(DisplayName);

    /// <summary>Why the chosen picture could not be used.</summary>
    [ObservableProperty]
    public partial string? AvatarError { get; private set; }

    partial void OnDisplayNameChanged(string value)
    {
        if (DisplayNameError is not null)
            return;

        _mqtt.Messaging.DisplayName = value.Trim();
        _mqtt.SaveSettings();
        _inbox.IdentityChanged();
    }

    partial void OnTopicPrefixChanged(string value)
    {
        var prefix = value.Trim();
        if (TopicPrefixError is not null || prefix == _mqtt.Messaging.TopicPrefix)
            return;

        _mqtt.Messaging.TopicPrefix = prefix;
        _mqtt.SaveSettings();
        _ = _inbox.Subscribe();
        _inbox.IdentityChanged();
    }

    [RelayCommand]
    private async Task ChooseAvatarAsync()
    {
        if (PickAvatar is not null && await PickAvatar() is { } path)
            UseAvatar(path);
    }

    /// <summary>Keeps a small square copy of this picture, cut from its middle, in place of any before.</summary>
    public void UseAvatar(string path)
    {
        try
        {
            using var picture = new Bitmap(path);
            using var small = Shrink(picture);
            Directory.CreateDirectory(DataFolder.Path);
            small.Save(MessagingSettings.AvatarPath, PngBitmapEncoderOptions.Default);
            Avatar = ReadAvatar();
            AvatarError = null;
            _inbox.IdentityChanged();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            AvatarError = $"Could not use {Path.GetFileName(path)}: {e.Message}";
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            AvatarError = $"{Path.GetFileName(path)} is not a picture that can be read";
        }
    }

    private bool CanRemoveAvatar => HasAvatar;

    [RelayCommand(CanExecute = nameof(CanRemoveAvatar))]
    private void RemoveAvatar()
    {
        try
        {
            File.Delete(MessagingSettings.AvatarPath);
            Avatar = null;
            AvatarError = null;
            _inbox.IdentityChanged();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            AvatarError = $"Could not remove the picture: {e.Message}";
        }
    }

    private static RenderTargetBitmap Shrink(Bitmap picture)
    {
        var side = Math.Min(picture.Size.Width, picture.Size.Height);
        var middle = new Rect((picture.Size.Width - side) / 2, (picture.Size.Height - side) / 2, side, side);
        var small = new RenderTargetBitmap(new PixelSize(AvatarPixels, AvatarPixels));
        using var context = small.CreateDrawingContext();
        using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality }))
            context.DrawImage(picture, middle, new Rect(0, 0, AvatarPixels, AvatarPixels));
        return small;
    }

    // A picture that has gone or been damaged is treated as none.
    private static Bitmap? ReadAvatar()
    {
        try
        {
            return File.Exists(MessagingSettings.AvatarPath) ? new Bitmap(MessagingSettings.AvatarPath) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }
}
