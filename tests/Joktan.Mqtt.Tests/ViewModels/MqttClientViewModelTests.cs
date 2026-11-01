using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using Joktan.Mqtt.ViewModels;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Mqtt.Tests.ViewModels;

[Collection(ProcessStateCollection.Name)]
public class MqttClientViewModelTests : DataFolderTest
{
    private readonly FakeCredentialStore _credentials = new();
    private int _saves;

    private MqttClientViewModel Client(ClientProfile? profile = null) =>
        new(profile ?? new ClientProfile { Name = "one", Host = "127.0.0.1", ClientId = "c-" + Guid.NewGuid().ToString("N")[..8] }, _credentials, () => _saves++);

    private ClientProfile On(TestBroker broker, string? folder = null) => new()
    {
        Name = "one",
        Host = "127.0.0.1",
        Port = broker.Port.ToString(),
        ClientId = "c-" + Guid.NewGuid().ToString("N")[..8],
        Folder = folder,
    };

    private string Messages(params (string Name, string Settings)[] messages)
    {
        foreach (var (name, settings) in messages)
        {
            Data.Write($"messages/topic/{name}.json", """{"a":1}""");
            if (settings.Length > 0)
                Data.Write($"messages/topic/{name}.send.json", settings);
        }

        return Data.At("messages");
    }

    private const string Manual = """{"enabled":true,"group":"G","schedule":{"type":"interval","every":3600}}""";
    private const string Off = """{"group":"G","schedule":{"type":"interval","every":3600}}""";
    private const string Every1Second = """{"enabled":true,"schedule":{"type":"interval","every":1}}""";

    [Fact]
    public Task Shows_what_the_profile_says() => Headless.Run(() =>
    {
        var vm = Client(new ClientProfile
        {
            Name = "one",
            Host = " broker ",
            Port = " 1883 ",
            ClientId = "id",
            ManualTopic = "t",
            ManualBody = "b",
            ManualQos = 1,
            ManualRetain = true,
            ManualGzip = true,
            BrokerCollapsed = true,
            ManualCollapsed = true,
            FolderCollapsed = true,
            ActivityCollapsed = true,
            TabFill = "#112233",
            PageBackground = "#223344",
        });

        Assert.Equal("one", vm.Name);
        Assert.Equal("broker:1883", vm.Address);
        Assert.Equal("id", vm.ClientId);
        Assert.Equal("Anonymous", vm.Login);
        Assert.Equal("t", vm.ManualTopic);
        Assert.Equal("b", vm.ManualBody);
        Assert.Equal(1, vm.ManualQos);
        Assert.True(vm.ManualRetain);
        Assert.True(vm.ManualGzip);
        Assert.True(vm.IsBrokerCollapsed);
        Assert.True(vm.IsManualCollapsed);
        Assert.True(vm.IsFolderCollapsed);
        Assert.True(vm.IsActivityCollapsed);
        Assert.NotNull(vm.TabTextBrush);
        Assert.IsAssignableFrom<IBrush>(vm.TabFillBrush);
        Assert.IsAssignableFrom<IBrush>(vm.PageBrush);
        Assert.Equal("No folder chosen", vm.FolderText);
        Assert.Equal("Disconnected", vm.StateText);
        Assert.True(vm.CanEdit);
        Assert.False(vm.ShowDisconnect);
        Assert.False(vm.IsConnected);
        Assert.False(vm.IsPending);
        Assert.False(vm.IsFailed);
        Assert.Null(vm.FailureReason);
        Assert.Equal("Disconnect", vm.DisconnectText);
    });

    public static TheoryData<bool, string, string, bool, string, string> Securities => new()
    {
        { false, "", "", false, "No TLS", "Not encrypted" },
        { true, "", "", true, "TLS", "The broker's certificate is not checked." },
        { true, "/ca.pem", "", false, "TLS", "The broker's certificate must be signed by ca.pem." },
        { true, "", "", false, "TLS", "The broker's certificate must be trusted by this computer." },
        { true, "", "/c.pem", true, "Mutual TLS", "Client certificate c.pem. The broker's certificate is not checked." },
    };

    [Theory]
    [MemberData(nameof(Securities))]
    public Task Says_how_the_connection_is_secured(bool tls, string ca, string client, bool any, string security, string detail) => Headless.Run(() =>
    {
        var vm = Client(new ClientProfile { Name = "one", Host = "h", UseTls = tls, CaCertificate = ca, ClientCertificate = client, AcceptAnyServerCertificate = any });

        Assert.Equal(security, vm.Security);
        Assert.Equal(detail, vm.SecurityDetail);
    });

    [Fact]
    public Task Passwords_come_from_the_credential_store() => Headless.Run(() =>
    {
        _credentials.Saved["me@h:1883"] = "secret";
        _credentials.Saved["key:" + Path.GetFullPath("c.pem")] = "keysecret";

        var vm = Client(new ClientProfile { Name = "one", Host = "h", Username = " me ", UseTls = true, ClientCertificate = "c.pem" });

        Assert.Equal("me", vm.Login);
        Assert.Equal("secret", vm.Password);
        Assert.Equal("keysecret", vm.KeyPassword);

        var withKey = Client(new ClientProfile { Name = "two", Host = "h", UseTls = true, ClientCertificate = "c.pem", ClientKey = "k.pem" });
        Assert.Equal("", withKey.KeyPassword);

        var pkcs12 = Client(new ClientProfile { Name = "three", Host = "h", UseTls = true, ClientCertificate = "c.pfx", ClientKey = "k.pem" });
        Assert.Equal("", pkcs12.KeyPassword);
    });

    [Fact]
    public Task Connecting_needs_a_good_host_and_port() => Headless.Run(() =>
    {
        Assert.True(Client().ConnectCommand.CanExecute(null));
        Assert.False(Client(new ClientProfile { Name = "x", Host = "a b" }).ConnectCommand.CanExecute(null));
        Assert.False(Client(new ClientProfile { Name = "x", Host = "h", Port = "nope" }).ConnectCommand.CanExecute(null));
    });

    [Fact]
    public Task Connecting_and_disconnecting_are_logged_and_change_what_can_be_done() => Headless.Run(async () =>
    {
        await using var broker = await TestBroker.StartAsync();
        var vm = Client(On(broker));
        vm.IsRemovable = true;
        Assert.True(vm.RemoveClientCommand.CanExecute(null));

        await vm.ConnectCommand.ExecuteAsync(null);
        await Headless.Until(() => vm.Entries.Any(e => e.Event == "Connected"));

        Assert.False(vm.CanEdit);
        Assert.True(vm.ShowDisconnect);
        Assert.False(vm.RemoveClientCommand.CanExecute(null));
        Assert.False(vm.EditClientCommand.CanExecute(null));
        Assert.True(vm.IsRunning);

        await vm.DisconnectCommand.ExecuteAsync(null);
        await Headless.Until(() => vm.Entries.Any(e => e.Event == "Disconnected"));

        Assert.False(vm.IsRunning);
        Assert.Equal(0, _saves);
    });

    [Fact]
    public Task A_failure_is_logged_with_its_reason() => Headless.Run(async () =>
    {
        var vm = Client(new ClientProfile { Name = "one", Host = "127.0.0.1", Port = TestBroker.FreePort().ToString(), ClientId = "x" });

        await vm.ConnectCommand.ExecuteAsync(null);
        await Headless.Until(() => vm.Entries.Any(e => e.Event == "Failed"));

        Assert.True(vm.IsFailed);
        Assert.NotNull(vm.FailureReason);
    });

    [Fact]
    public Task A_good_connection_remembers_its_secrets_and_forgets_an_empty_one() => Headless.Run(async () =>
    {
        await using var broker = await TestBroker.StartAsync();
        var profile = On(broker);
        profile.Username = "me";
        var vm = Client(profile);
        vm.Apply(profile.Clone(), "secret", "");

        await vm.ConnectCommand.ExecuteAsync(null);
        await Headless.Until(() => vm.IsConnected);

        var account = Joktan.Core.CredentialStore.AccountFor("me", "127.0.0.1", broker.Port.ToString());
        Assert.Equal("secret", _credentials.Saved[account]);

        await vm.DisconnectCommand.ExecuteAsync(null);
        vm.Apply(vm.Profile.Clone(), "", "");
        await vm.ConnectCommand.ExecuteAsync(null);
        await Headless.Until(() => vm.IsConnected);
        Assert.False(_credentials.Saved.ContainsKey(account));
    });

    [Fact]
    public Task Tls_settings_reach_the_connection() => Headless.Run(async () =>
    {
        var vm = Client(new ClientProfile { Name = "one", Host = "127.0.0.1", ClientId = "x", UseTls = true, CaCertificate = Data.At("gone.pem"), ClientCertificate = Data.At("gone.pem") });
        vm.Apply(vm.Profile.Clone(), "", "keypass");

        await vm.ConnectCommand.ExecuteAsync(null);
        await Headless.Until(() => vm.IsFailed);
    });

    [Fact]
    public Task The_manual_message_is_sent_and_logged() => Headless.Run(async () =>
    {
        await using var broker = await TestBroker.StartAsync();
        var vm = Client(On(broker));
        vm.ManualTopic = "manual/topic";
        vm.ManualBody = "hello";
        Assert.False(vm.SendManualCommand.CanExecute(null));

        await vm.ConnectCommand.ExecuteAsync(null);
        await Headless.Until(() => vm.SendManualCommand.CanExecute(null));

        await vm.SendManualCommand.ExecuteAsync(null);
        await Headless.Until(() => broker.PublishedTopics.Contains("manual/topic"));
        Assert.Contains(vm.Entries, e => e.Event == "Sent" && e.Body == "hello");

        vm.ManualGzip = true;
        await vm.SendManualCommand.ExecuteAsync(null);
        await Headless.Until(() => broker.PublishedTopics.Count() == 2);
        Assert.Equal(0x1F, broker.Published.Last().Payload[0]);
    });

    [Fact]
    public Task A_manual_message_that_cannot_be_sent_is_logged() => Headless.Run(async () =>
    {
        await using var broker = await TestBroker.StartAsync();
        var vm = Client(On(broker));
        vm.ManualTopic = "manual/topic";
        await vm.ConnectCommand.ExecuteAsync(null);
        await Headless.Until(() => vm.IsConnected);
        await vm.Connection.DisconnectAsync();

        await vm.SendManualCommand.ExecuteAsync(null);

        Assert.Contains(vm.Entries, e => e.Event == "Failed" && e.Detail == "Not connected");
    });

    [Theory]
    [InlineData("", null)]
    [InlineData("good/topic", null)]
    public Task A_topic_that_is_fine_or_empty_has_no_error(string topic, string? error) => Headless.Run(() =>
    {
        var vm = Client();
        vm.ManualTopic = topic;

        Assert.Equal(error, vm.ManualTopicError);
    });

    [Fact]
    public Task A_topic_with_a_wildcard_has_an_error() => Headless.Run(() =>
    {
        var vm = Client();

        vm.ManualTopic = "bad/#";

        Assert.NotNull(vm.ManualTopicError);
    });

    [Fact]
    public Task Formatting_the_body_lays_out_json_and_leaves_the_rest() => Headless.Run(() =>
    {
        var vm = Client();

        vm.ManualBody = """{"a":1}""";
        vm.FormatManualBody();
        Assert.Contains("\n", vm.ManualBody);

        vm.ManualBody = "not json";
        vm.FormatManualBody();
        Assert.Equal("not json", vm.ManualBody);

        vm.ManualBody = "null";
        vm.FormatManualBody();
        Assert.Equal("null", vm.ManualBody);
    });

    [Fact]
    public Task Changes_are_saved_after_a_pause() => Headless.Run(async () =>
    {
        var vm = Client();

        vm.ManualBody = "typed";
        vm.ToggleBrokerCommand.Execute(null);
        vm.ToggleManualCommand.Execute(null);
        vm.ToggleFolderCommand.Execute(null);
        vm.ToggleActivityCommand.Execute(null);

        await Headless.Until(() => _saves > 0);
        Assert.Equal("typed", vm.Profile.ManualBody);
        Assert.True(vm.Profile.BrokerCollapsed);
        Assert.True(vm.Profile.ManualCollapsed);
        Assert.True(vm.Profile.FolderCollapsed);
        Assert.True(vm.Profile.ActivityCollapsed);
    });

    [Fact]
    public Task New_settings_keep_what_belongs_to_the_tab() => Headless.Run(() =>
    {
        var vm = Client();
        vm.ManualTopic = "kept";
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.Apply(new ClientProfile { Name = "renamed", Host = "other" }, "p", "k");

        Assert.Equal("renamed", vm.Name);
        Assert.Equal("renamed", vm.Connection.Name);
        Assert.Equal("kept", vm.Profile.ManualTopic);
        Assert.Equal("p", vm.Password);
        Assert.Equal("k", vm.KeyPassword);
        Assert.Contains(nameof(vm.Address), changed);
    });

    [Fact]
    public Task The_page_decides_about_editing_and_removing() => Headless.Run(async () =>
    {
        var vm = Client();
        await vm.EditClientCommand.ExecuteAsync(null);
        vm.IsRemovable = true;
        vm.RemoveClientCommand.Execute(null);

        var edited = 0;
        var removed = 0;
        vm.EditClientRequested = _ =>
        {
            edited++;
            return Task.CompletedTask;
        };
        vm.RemoveClientRequested = _ => removed++;

        await vm.EditClientCommand.ExecuteAsync(null);
        vm.RemoveClientCommand.Execute(null);

        Assert.Equal(1, edited);
        Assert.Equal(1, removed);
    });

    [Fact]
    public Task Reading_the_folder_lists_its_messages_by_group() => Headless.Run(async () =>
    {
        var folder = Messages(("a", Manual), ("b", ""));
        var vm = Client(new ClientProfile { Name = "one", Host = "h", Folder = folder });

        await Headless.Until(() => !vm.IsReadingFolder && vm.Rows.Count == 2);

        Assert.Equal(folder, vm.FolderText);
        Assert.Equal(["G"], vm.GroupNames);
        Assert.StartsWith("2 messages, 1 switched on", vm.FolderSummary);
        Assert.Equal(2, vm.Groups.Count);
        Assert.Equal(4, vm.Items.Count);
        Assert.NotNull(vm.RowFor("a.json") ?? vm.RowFor(vm.Rows[0].File));
        Assert.Null(vm.RowFor("nothing"));
    });

    [Fact]
    public Task A_group_can_be_collapsed_and_stays_so_when_read_again() => Headless.Run(async () =>
    {
        var folder = Messages(("a", Manual));
        var vm = Client(new ClientProfile { Name = "one", Host = "h", Folder = folder });
        await Headless.Until(() => vm.Rows.Count == 1);
        var group = vm.Groups.First();

        vm.ToggleGroupCommand.Execute(null);
        vm.ToggleGroupCommand.Execute(group);
        Assert.False(group.IsExpanded);
        Assert.Single(vm.Items);

        await vm.RescanCommand.ExecuteAsync(null);
        Assert.False(vm.Groups.First().IsExpanded);

        vm.ToggleGroupCommand.Execute(vm.Groups.First());
        Assert.Equal(2, vm.Items.Count);
    });

    [Fact]
    public Task A_message_that_is_not_valid_counts_as_such() => Headless.Run(async () =>
    {
        Data.Write("messages/topic/bad.json", "{ not json");
        var vm = Client(new ClientProfile { Name = "one", Host = "h", Folder = Data.At("messages") });

        await Headless.Until(() => vm.Rows.Count == 1);

        Assert.Contains("1 invalid", vm.FolderSummary);
        Assert.False(vm.EditMessageCommand.CanExecute(vm.Rows[0]));
        Assert.False(vm.EditMessageCommand.CanExecute(null));
    });

    [Fact]
    public Task An_empty_folder_says_so_and_a_missing_one_is_an_error() => Headless.Run(async () =>
    {
        Directory.CreateDirectory(Data.At("empty"));
        var vm = Client();
        Assert.Equal("", vm.FolderSummary);

        vm.UseFolder(Data.At("empty"));
        await Headless.Until(() => vm.FolderSummary == "No .json files found in this folder.");

        vm.UseFolder(Data.At("gone"));
        await Headless.Until(() => vm.FolderError is not null);
        Assert.Equal("", vm.FolderSummary);

        vm.UseFolder(null);
        await Headless.Until(() => vm.FolderError is null);
        Assert.Equal("No folder chosen", vm.FolderText);
        Assert.True(_saves >= 3);
    });

    [Fact]
    public Task Choosing_a_folder_uses_what_was_picked() => Headless.Run(async () =>
    {
        var folder = Messages(("a", ""));
        var vm = Client();

        await vm.ChooseFolderCommand.ExecuteAsync(null);
        vm.PickFolder = () => Task.FromResult<string?>(null);
        await vm.ChooseFolderCommand.ExecuteAsync(null);
        Assert.Null(vm.FolderPath);

        vm.PickFolder = () => Task.FromResult<string?>(folder);
        await vm.ChooseFolderCommand.ExecuteAsync(null);
        await Headless.Until(() => vm.Rows.Count == 1);
    });

    [Fact]
    public Task A_change_in_the_folder_is_read_by_itself() => Headless.Run(async () =>
    {
        var folder = Messages(("a", ""));
        var vm = Client(new ClientProfile { Name = "one", Host = "h", Folder = folder });
        await Headless.Until(() => vm.Rows.Count == 1);

        Data.Write("messages/topic/b.json", """{"b":2}""");

        await Headless.Until(() => vm.Rows.Count == 2);
        vm.Dispose();
    });

    [Fact]
    public Task A_switch_is_kept_in_the_settings_file() => Headless.Run(async () =>
    {
        var folder = Messages(("a", Off));
        var vm = Client(new ClientProfile { Name = "one", Host = "h", Folder = folder });
        await Headless.Until(() => vm.Rows.Count == 1);
        var row = vm.Rows[0];
        var path = MessageSettings.PathFor(row.Message.FilePath);

        row.IsOn = true;
        vm.FinishWrites();

        Assert.Contains("\"enabled\": true", File.ReadAllText(path));
        Assert.StartsWith("1 messages, 1 switched on", vm.FolderSummary);
    });

    [Fact]
    public Task A_group_switch_changes_every_message_in_it() => Headless.Run(async () =>
    {
        var folder = Messages(("a", Off), ("b", Off));
        var vm = Client(new ClientProfile { Name = "one", Host = "h", Folder = folder });
        await Headless.Until(() => vm.Rows.Count == 2);
        var group = vm.Groups.First(g => g.Path == "G");
        Assert.False(group.IsOn);
        Assert.True(group.CanSwitch);
        Assert.Equal("2 messages, 0 on", group.Summary);

        group.IsOn = true;

        Assert.True(vm.Rows.All(r => r.IsOn));
        Assert.True(group.IsOn);
        Assert.Equal("2 messages, 2 on", group.Summary);
    });

    [Fact]
    public Task Messages_are_sent_on_their_schedule_while_connected_and_by_hand() => Headless.Run(async () =>
    {
        await using var broker = await TestBroker.StartAsync();
        var folder = Messages(("scheduled", Every1Second), ("byhand", ""));
        var vm = Client(On(broker, folder));
        await Headless.Until(() => vm.Rows.Count == 2);
        var byHand = vm.Rows.First(r => r.File.Contains("byhand"));
        Assert.False(vm.SendNowCommand.CanExecute(byHand));
        Assert.False(vm.SendNowCommand.CanExecute(null));

        await vm.ConnectCommand.ExecuteAsync(null);
        await Headless.Until(() => vm.IsRunning);
        await Headless.Until(() => broker.Published.Count(p => p.Topic == "topic") >= 1);
        Assert.Contains(vm.Entries, e => e.Event == "Sent");
        var before = broker.Published.Count;

        Assert.True(vm.SendNowCommand.CanExecute(byHand));
        await vm.SendNowCommand.ExecuteAsync(byHand);
        await Headless.Until(() => broker.Published.Count > before);
        await vm.SendNowCommand.ExecuteAsync(null);

        await vm.DisconnectCommand.ExecuteAsync(null);
        await Headless.Until(() => !vm.IsRunning);
    });

    [Fact]
    public Task Editing_a_message_reads_the_folder_again_if_it_was_saved() => Headless.Run(async () =>
    {
        var folder = Messages(("a", ""));
        var vm = Client(new ClientProfile { Name = "one", Host = "h", Folder = folder });
        await Headless.Until(() => vm.Rows.Count == 1);
        var row = vm.Rows[0];
        await vm.EditMessageCommand.ExecuteAsync(null);
        await vm.EditMessageCommand.ExecuteAsync(row);

        vm.EditSettings = _ => Task.FromResult(false);
        await vm.EditMessageCommand.ExecuteAsync(row);
        Assert.Same(row, vm.Rows[0]);

        vm.EditSettings = _ => Task.FromResult(true);
        await vm.EditMessageCommand.ExecuteAsync(row);
        Assert.NotSame(row, vm.Rows[0]);
    });

    [Fact]
    public Task The_log_shows_a_selected_entry_keeps_its_length_and_can_be_cleared() => Headless.Run(async () =>
    {
        await using var broker = await TestBroker.StartAsync();
        var vm = Client(On(broker));
        vm.ManualTopic = "t";
        vm.ManualBody = "body";
        await vm.ConnectCommand.ExecuteAsync(null);
        await Headless.Until(() => vm.SendManualCommand.CanExecute(null));
        for (var i = 0; i < 502; i++)
            await vm.SendManualCommand.ExecuteAsync(null);
        Assert.Equal(500, vm.Entries.Count);

        vm.SelectedEntry = vm.Entries[0];
        Assert.True(vm.HasSelectedBody);
        Assert.Equal("body", vm.SelectedBody);

        vm.ClearLogCommand.Execute(null);
        Assert.Empty(vm.Entries);
        Assert.Null(vm.SelectedEntry);
        Assert.False(vm.HasSelectedBody);
    });

    [Fact]
    public Task A_lost_connection_is_logged_and_the_schedules_keep_their_place() => Headless.Run(async () =>
    {
        await using var broker = await TestBroker.StartAsync();
        var vm = Client(On(broker));
        await vm.ConnectCommand.ExecuteAsync(null);
        // The state changes at once and the view model hears of it later, so wait for what it does about it.
        await Headless.Until(() => vm.IsRunning);

        await broker.DropClientsAsync();
        await Headless.Until(() => vm.Entries.Any(e => e.Event == "Reconnecting"));

        Assert.True(vm.IsPending);
        Assert.Equal("Disconnect", vm.DisconnectText);
        Assert.True(vm.IsRunning);
        await vm.DisconnectCommand.ExecuteAsync(null);
    });

    [Fact]
    public Task A_settings_file_that_cannot_be_written_is_logged() => Headless.Run(async () =>
    {
        var folder = Messages(("a", Off));
        var vm = Client(new ClientProfile { Name = "one", Host = "h", Folder = folder });
        await Headless.Until(() => vm.Rows.Count == 1);
        var path = MessageSettings.PathFor(vm.Rows[0].Message.FilePath);
        File.Delete(path);
        Directory.CreateDirectory(path);

        vm.Rows[0].IsOn = true;

        await Headless.Until(() => vm.Entries.Any(e => e.Event == "Failed"));
        Assert.Contains("Settings not saved", vm.Entries[0].Detail);
    });

    [Fact]
    public Task Disposing_stops_everything() => Headless.Run(() =>
    {
        var vm = Client(new ClientProfile { Name = "one", Host = "h", Folder = Messages(("a", "")) });

        vm.Dispose();
        vm.Dispose();

        Assert.False(vm.IsRunning);
    });
}
