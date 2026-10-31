using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Joktan.Mqtt.ViewModels;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Mqtt.Tests.ViewModels;

// No client here has a username or certificate, so nothing reaches the real credential store.
[Collection(ProcessStateCollection.Name)]
public class MessagingViewModelTests : DataFolderTest
{
    private static MqttSettings Settings(params string[] names) => new()
    {
        Clients = names.Select(n => new ClientProfile { Name = n, Host = "localhost", ClientId = n }).ToList(),
    };

    private static MessagingViewModel Page(MqttViewModel mqtt) => new(mqtt, new InboxViewModel(mqtt, new MessagingHistory()));

    private string Picture(int width, int height)
    {
        var path = Data.At("picture.png");
        using var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Opaque);
        bitmap.Save(path, PngBitmapEncoderOptions.Default);
        return path;
    }

    [Fact]
    public Task The_server_is_on_no_tab_and_not_saved_with_the_clients() => Headless.Run(() =>
    {
        var settings = Settings("a");
        var mqtt = new MqttViewModel(null, settings);

        Assert.Equal(["a"], mqtt.Clients.Select(c => c.Name));
        Assert.Equal(MessagingSettings.DefaultServerName, mqtt.MessagingClient.Name);
        Assert.Equal("localhost", mqtt.MessagingClient.Profile.Host);
        Assert.NotEqual("", mqtt.MessagingClient.ClientId);

        mqtt.SaveSettings();
        var read = MqttSettings.Load();
        Assert.Equal(["a"], read.Clients.Select(c => c.Name));
        Assert.Equal(MessagingSettings.DefaultServerName, read.Messaging.Server!.Name);
        Assert.False(read.Messaging.ShowInBrokers);
    });

    [Fact]
    public Task A_client_with_the_server_s_name_keeps_it() => Headless.Run(() =>
    {
        var mqtt = new MqttViewModel(null, Settings(MessagingSettings.DefaultServerName.ToUpperInvariant()));

        Assert.Equal($"{MessagingSettings.DefaultServerName} 2", mqtt.MessagingClient.Name);
    });

    [Fact]
    public Task The_switch_gives_it_a_tab_after_the_clients_and_takes_it_away() => Headless.Run(() =>
    {
        var mqtt = new MqttViewModel(null, Settings("a"));
        var page = Page(mqtt);

        page.ShowInBrokers = true;
        Assert.Equal(["a", MessagingSettings.DefaultServerName], mqtt.Clients.Select(c => c.Name));
        Assert.True(MqttSettings.Load().Messaging.ShowInBrokers);
        Assert.Equal(["a"], MqttSettings.Load().Clients.Select(c => c.Name));

        mqtt.SelectedClient = mqtt.MessagingClient;
        page.ShowInBrokers = false;
        Assert.Equal(["a"], mqtt.Clients.Select(c => c.Name));
        Assert.Equal("a", mqtt.SelectedClient!.Name);
        Assert.False(MqttSettings.Load().Messaging.ShowInBrokers);
    });

    [Fact]
    public Task A_shown_server_comes_back_on_its_tab_after_a_restart() => Headless.Run(() =>
    {
        Page(new MqttViewModel(null, Settings("a"))).ShowInBrokers = true;

        var mqtt = new MqttViewModel(null, MqttSettings.Load());

        Assert.Equal(["a", MessagingSettings.DefaultServerName], mqtt.Clients.Select(c => c.Name));
    });

    [Fact]
    public Task It_cannot_be_removed_and_does_not_count_towards_the_last_client() => Headless.Run(() =>
    {
        var mqtt = new MqttViewModel(null, Settings("a"));
        mqtt.IsMessagingClientShown = true;

        Assert.All(mqtt.Clients, c => Assert.False(c.IsRemovable));
        Assert.False(mqtt.MessagingClient.RemoveClientCommand.CanExecute(null));
        mqtt.Clients[0].RemoveClientCommand.Execute(null);
        Assert.Equal(2, mqtt.Clients.Count);
    });

    [Fact]
    public Task A_new_client_goes_before_the_server_and_cannot_take_its_name() => Headless.Run(async () =>
    {
        var mqtt = new MqttViewModel(null, Settings("a"));
        mqtt.IsMessagingClientShown = true;
        ClientSettingsViewModel? editor = null;
        mqtt.EditClientSettings = e =>
        {
            editor = e;
            return Task.FromResult(true);
        };

        await mqtt.AddClientCommand.ExecuteAsync(null);

        Assert.Equal(MessagingSettings.DefaultServerName, mqtt.Clients[^1].Name);
        Assert.Equal(3, mqtt.Clients.Count);
        Assert.True(mqtt.Clients[0].IsRemovable);
        Assert.True(mqtt.Clients[1].IsRemovable);
        Assert.False(mqtt.MessagingClient.IsRemovable);

        editor!.Name = MessagingSettings.DefaultServerName;
        Assert.NotNull(editor.Error);
    });

    [Fact]
    public Task A_hidden_server_s_name_is_still_taken() => Headless.Run(async () =>
    {
        var mqtt = new MqttViewModel(null, Settings("a"));
        ClientSettingsViewModel? editor = null;
        mqtt.EditClientSettings = e =>
        {
            editor = e;
            return Task.FromResult(false);
        };

        await mqtt.AddClientCommand.ExecuteAsync(null);
        editor!.Name = MessagingSettings.DefaultServerName;

        Assert.NotNull(editor.Error);
    });

    [Fact]
    public Task Editing_the_hidden_server_saves_it_without_giving_it_a_tab() => Headless.Run(async () =>
    {
        var mqtt = new MqttViewModel(null, Settings("a"));
        var page = Page(mqtt);
        page.EditClientSettings = e =>
        {
            e.Host = "mqtt.example.com";
            e.Name = "alerts";
            return Task.FromResult(true);
        };

        await page.Client.EditClientCommand.ExecuteAsync(null);

        Assert.Equal("alerts", page.Client.Name);
        Assert.Equal("mqtt.example.com:1883", page.Client.Address);
        Assert.Equal(["a"], mqtt.Clients.Select(c => c.Name));
        Assert.Equal("mqtt.example.com", MqttSettings.Load().Messaging.Server!.Host);
    });

    [Fact]
    public Task It_connects_while_hidden_and_stays_connected_when_switched_off() => Headless.Run(async () =>
    {
        await using var broker = await TestBroker.StartAsync();
        var settings = Settings("a");
        settings.Messaging.Server = new ClientProfile { Name = "alerts", Host = "127.0.0.1", Port = broker.Port.ToString(), ClientId = "alerts" };
        var mqtt = new MqttViewModel(null, settings);
        var page = Page(mqtt);

        Assert.False(page.Client.IsConnected);
        await page.Client.ConnectCommand.ExecuteAsync(null);
        await Headless.Until(() => page.Client.IsConnected);
        Assert.False(mqtt.Clients[0].IsConnected);

        page.ShowInBrokers = true;
        page.ShowInBrokers = false;
        Assert.True(page.Client.IsConnected);

        await page.Client.DisconnectCommand.ExecuteAsync(null);
    });

    [Fact]
    public Task The_display_name_is_saved_trimmed_and_gives_the_initial() => Headless.Run(() =>
    {
        var page = Page(new MqttViewModel(null, Settings("a")));
        Assert.Equal("?", page.AvatarInitial);

        page.DisplayName = "  ada  ";

        Assert.Equal("A", page.AvatarInitial);
        Assert.Equal("ada", MqttSettings.Load().Messaging.DisplayName);
        Assert.Equal("ada", Page(new MqttViewModel(null, MqttSettings.Load())).DisplayName);
    });

    [Fact]
    public Task A_picture_is_kept_small_and_square_and_can_be_removed() => Headless.Run(() =>
    {
        var page = Page(new MqttViewModel(null, Settings("a")));
        Assert.False(page.HasAvatar);
        Assert.False(page.RemoveAvatarCommand.CanExecute(null));

        page.UseAvatar(Picture(640, 320));

        Assert.True(page.HasAvatar);
        Assert.Null(page.AvatarError);
        using (var kept = new Bitmap(MessagingSettings.AvatarPath))
            Assert.Equal(new PixelSize(MessagingViewModel.AvatarPixels, MessagingViewModel.AvatarPixels), kept.PixelSize);
        Assert.True(File.Exists(Data.At("picture.png")));
        Assert.True(Page(new MqttViewModel(null, MqttSettings.Load())).HasAvatar);

        page.RemoveAvatarCommand.Execute(null);

        Assert.False(page.HasAvatar);
        Assert.False(File.Exists(MessagingSettings.AvatarPath));
    });

    [Fact]
    public Task A_file_that_is_no_picture_says_so_and_changes_nothing() => Headless.Run(() =>
    {
        var page = Page(new MqttViewModel(null, Settings("a")));
        page.UseAvatar(Picture(64, 64));
        Data.Write("notes.png", "this is not a picture");

        page.UseAvatar(Data.At("notes.png"));

        Assert.Contains("notes.png", page.AvatarError);
        Assert.True(page.HasAvatar);

        page.UseAvatar(Data.At("missing.png"));

        Assert.Contains("missing.png", page.AvatarError);
        Assert.True(page.HasAvatar);
    });

    [Fact]
    public Task A_damaged_picture_on_disk_counts_as_none() => Headless.Run(() =>
    {
        File.WriteAllText(MessagingSettings.AvatarPath, "damaged");

        Assert.False(Page(new MqttViewModel(null, Settings("a"))).HasAvatar);
    });
}
