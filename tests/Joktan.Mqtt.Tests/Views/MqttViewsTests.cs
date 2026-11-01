using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using Joktan.Mqtt.ViewModels;
using Joktan.Mqtt.Views;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Mqtt.Tests.Views;

[Collection(ProcessStateCollection.Name)]
public class MqttViewsTests : DataFolderTest
{
    private static MqttSettings Settings(string? folder = null) => new()
    {
        Clients = [new ClientProfile { Name = "one", Host = "127.0.0.1", ClientId = "one", Folder = folder }, new ClientProfile { Name = "two", Host = "h", ClientId = "two" }],
        SelectedClient = "one",
    };

    private string Folder()
    {
        Data.Write("messages/topic/a.json", """{"a":1}""");
        Data.Write("messages/topic/a.send.json", """{"group":"G"}""");
        Data.Write("messages/topic/bad.json", "{ not json");
        return Data.At("messages");
    }

    [Fact]
    public Task The_page_shows_a_tab_for_each_client_with_its_messages_and_log() => Headless.Run(async () =>
    {
        await using var broker = await TestBroker.StartAsync();
        var settings = Settings(Folder());
        settings.Clients[0].Port = broker.Port.ToString();
        var vm = new MqttViewModel(null, settings);
        var window = Headless.Show(new MqttView(), vm);
        var client = vm.SelectedClient!;
        await Headless.Until(() => client.Rows.Count == 2);
        Headless.Settle();
        window.UpdateLayout();

        client.ManualTopic = "bad/#";
        client.ManualBody = "text";
        await client.ConnectCommand.ExecuteAsync(null);
        await Headless.Until(() => client.IsConnected);
        client.ManualTopic = "t";
        await client.SendManualCommand.ExecuteAsync(null);
        client.SelectedEntry = client.Entries.First(e => e.Body is not null);
        Headless.Settle();
        window.UpdateLayout();
        Assert.Contains("one", Headless.All<TextBlock>(window).Select(t => t.Text));

        await client.DisconnectCommand.ExecuteAsync(null);
        window.Close();
    });

    [Fact]
    public Task The_client_editor_opens_as_a_dialog() => Headless.Run(async () =>
    {
        var vm = new MqttViewModel(null, Settings());
        var view = new MqttView { DataContext = vm };
        var offScreen = new ClientSettingsViewModel(new ClientProfile { Name = "x", Host = "h" }, "", "", [], false);
        Assert.False(await vm.EditClientSettings!(offScreen));

        var window = Headless.Show(view);
        var opened = new List<Window>();
        using var subscription = Window.WindowOpenedEvent.AddClassHandler<ClientSettingsWindow>((w, _) => opened.Add(w));

        var adding = vm.AddClientCommand.ExecuteAsync(null);
        await Headless.Until(() => opened.Count == 1);
        var editor = (ClientSettingsViewModel)opened[0].DataContext!;
        editor.UseTls = true;
        editor.CertificateProblem = "A problem";
        Headless.Settle();
        Assert.Null(await editor.PickFile!(CertificateFileKind.Authority));
        Assert.Null(await editor.PickFile!(CertificateFileKind.Client));
        Assert.Null(await editor.PickFile!(CertificateFileKind.Key));
        editor.SaveCommand.Execute(null);
        await adding;

        Assert.Equal(3, vm.Clients.Count);

        var cancelling = vm.AddClientCommand.ExecuteAsync(null);
        await Headless.Until(() => opened.Count == 2);
        ((ClientSettingsViewModel)opened[1].DataContext!).CancelCommand.Execute(null);
        await cancelling;
        Assert.Equal(3, vm.Clients.Count);
        window.Close();
    });

    [Fact]
    public Task The_message_editor_opens_as_a_dialog() => Headless.Run(async () =>
    {
        var profile = new ClientProfile { Name = "one", Host = "h", ClientId = "one", Folder = Folder() };
        var client = new MqttClientViewModel(profile, new FakeCredentialStore(), () => { });
        var view = new MqttClientView { DataContext = client };
        await Headless.Until(() => client.Rows.Count == 2);
        var row = client.Rows.First(r => r.Message.Body is not null);
        Assert.False(await client.EditSettings!(new MessageSettingsViewModel(row.Message)));
        Assert.Null(await client.PickFolder!());

        var window = Headless.Show(view);
        Assert.Null(await client.PickFolder!());
        var opened = new List<MessageSettingsWindow>();
        using var subscription = Window.WindowOpenedEvent.AddClassHandler<MessageSettingsWindow>((w, _) => opened.Add(w));

        var editing = client.EditMessageCommand.ExecuteAsync(row);
        await Headless.Until(() => opened.Count == 1);
        var editor = (MessageSettingsViewModel)opened[0].DataContext!;

        // Every part of the editor is shown in turn.
        editor.AddFieldCommand.Execute(null);
        foreach (var type in new[] { FieldType.Counter, FieldType.RandomInteger, FieldType.RandomNumber, FieldType.OneOf, FieldType.Cycle, FieldType.Fixed })
        {
            editor.Fields[0].TypeIndex = (int)type;
            Headless.Settle();
        }

        foreach (var kind in Enumerable.Range(0, FieldRuleViewModel.ValueKindNames.Count))
        {
            editor.Fields[0].ValueKindIndex = kind;
            Headless.Settle();
        }

        editor.AddArrayCommand.Execute(null);
        editor.Topic = "t/{x}";
        editor.AddTopicFieldCommand.Execute(null);
        foreach (var schedule in new[] { ScheduleType.Once, ScheduleType.Interval, ScheduleType.Daily })
        {
            editor.ScheduleTypeIndex = (int)schedule;
            Headless.Settle();
        }

        opened[0].UpdateLayout();
        editor.CancelCommand.Execute(null);
        await editing;
        window.Close();
    });

    [Fact]
    public Task Leaving_the_body_lays_it_out() => Headless.Run(() =>
    {
        var client = new MqttClientViewModel(new ClientProfile { Name = "one", Host = "h" }, new FakeCredentialStore(), () => { });
        var view = new MqttClientView { DataContext = client };
        client.ManualBody = """{"a":1}""";

        typeof(MqttClientView).GetMethod("OnManualBodyLostFocus", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(view, [null, null]);

        Assert.Contains("\n", client.ManualBody);
    });
}
