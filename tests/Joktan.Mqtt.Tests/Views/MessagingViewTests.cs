using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Joktan.Mqtt.ViewModels;
using Joktan.Mqtt.Views;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Mqtt.Tests.Views;

[Collection(ProcessStateCollection.Name)]
public class MessagingViewTests : DataFolderTest
{
    private static MqttSettings Settings() => new()
    {
        Clients = [new ClientProfile { Name = "one", Host = "127.0.0.1", ClientId = "one" }],
    };

    [Fact]
    public Task The_page_shows_the_server_with_a_switch_not_a_checkbox() => Headless.Run(() =>
    {
        var mqtt = new MqttViewModel(null, Settings());
        var page = new MessagingViewModel(mqtt, new InboxViewModel(mqtt, new MessagingHistory()));
        var window = Headless.Show(new MessagingView(), page);

        Assert.Empty(Headless.All<CheckBox>(window));
        Assert.Contains(Headless.All<TextBlock>(window), t => t.Text == "localhost:1883");
        var toggles = Headless.All<ToggleSwitch>(window).ToList();
        Assert.Equal(2, toggles.Count);
        Assert.True(toggles[0].IsChecked);
        var toggle = toggles[1];
        Assert.False(toggle.IsChecked);

        toggle.IsChecked = true;
        Headless.Settle();

        Assert.True(mqtt.IsMessagingClientShown);
        window.Close();
    });

    [Fact]
    public Task The_tab_appears_on_the_MQTT_page_while_it_is_open() => Headless.Run(() =>
    {
        var mqtt = new MqttViewModel(null, Settings());
        var window = Headless.Show(new MqttView(), mqtt);
        Assert.DoesNotContain(Headless.All<TextBlock>(window), t => t.Text == MessagingSettings.DefaultServerName);

        mqtt.IsMessagingClientShown = true;
        Headless.Settle();
        window.UpdateLayout();

        Assert.Contains(Headless.All<TextBlock>(window), t => t.Text == MessagingSettings.DefaultServerName);
        window.Close();
    });
}
