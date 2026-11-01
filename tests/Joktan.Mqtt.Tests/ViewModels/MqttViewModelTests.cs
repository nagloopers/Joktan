using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Joktan.Certificates;
using Joktan.Mqtt.ViewModels;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Mqtt.Tests.ViewModels;

// No client here has a username or certificate, so nothing reaches the real credential store.
[Collection(ProcessStateCollection.Name)]
public class MqttViewModelTests : DataFolderTest
{
    private static MqttSettings Settings(params string[] names) => new()
    {
        Clients = names.Select(n => new ClientProfile { Name = n, Host = "localhost", ClientId = n }).ToList(),
        SelectedClient = names.LastOrDefault() ?? "",
    };

    [Fact]
    public Task Opens_the_saved_clients_with_the_one_selected_last() => Headless.Run(() =>
    {
        var vm = new MqttViewModel(null, Settings("a", "b"));

        Assert.Equal(["a", "b"], vm.Clients.Select(c => c.Name));
        Assert.Equal("b", vm.SelectedClient!.Name);
        Assert.All(vm.Clients, c => Assert.True(c.IsRemovable));
    });

    [Fact]
    public Task Without_settings_they_are_read_and_there_is_always_a_client() => Headless.Run(() =>
    {
        var vm = new MqttViewModel();

        Assert.NotEmpty(vm.Clients);
        Assert.NotNull(vm.SelectedClient);
    });

    [Fact]
    public Task An_unknown_selection_falls_back_on_the_first() => Headless.Run(() =>
    {
        var settings = Settings("a", "b");
        settings.SelectedClient = "gone";

        Assert.Equal("a", new MqttViewModel(null, settings).SelectedClient!.Name);
    });

    [Fact]
    public Task Finds_a_client_by_name_ignoring_case() => Headless.Run(() =>
    {
        var vm = new MqttViewModel(null, Settings("Alpha"));

        Assert.Equal("Alpha", vm.Find(" alpha ")!.Name);
        Assert.Null(vm.Find("beta"));
        Assert.Null(vm.Find(null));
    });

    [Fact]
    public Task Selecting_a_client_is_remembered() => Headless.Run(() =>
    {
        var vm = new MqttViewModel(null, Settings("a", "b"));

        vm.SelectedClient = vm.Clients[0];
        Assert.Equal("a", MqttSettings.Load().SelectedClient);

        vm.SelectedClient = null;
        Assert.Equal("a", MqttSettings.Load().SelectedClient);
    });

    [Fact]
    public Task A_new_client_needs_the_editor_and_is_added_when_saved() => Headless.Run(async () =>
    {
        var vm = new MqttViewModel(null, Settings("client 2"));
        await vm.AddClientCommand.ExecuteAsync(null);
        Assert.Single(vm.Clients);

        ClientSettingsViewModel? editor = null;
        vm.EditClientSettings = e =>
        {
            editor = e;
            return Task.FromResult(false);
        };
        await vm.AddClientCommand.ExecuteAsync(null);
        Assert.Single(vm.Clients);
        Assert.Equal("client 3", editor!.Name);

        vm.EditClientSettings = _ => Task.FromResult(true);
        await vm.AddClientCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.Clients.Count);
        Assert.Equal("client 3", vm.SelectedClient!.Name);
        Assert.All(vm.Clients, c => Assert.True(c.IsRemovable));
        Assert.Equal(2, MqttSettings.Load().Clients.Count);
    });

    [Fact]
    public Task Editing_a_client_applies_what_was_saved() => Headless.Run(async () =>
    {
        var vm = new MqttViewModel(null, Settings("a", "b"));
        var client = vm.Clients[0];
        vm.EditClientSettings = null;
        await client.EditClientCommand.ExecuteAsync(null);

        vm.EditClientSettings = e =>
        {
            e.Name = "b";
            Assert.NotNull(e.Error);
            e.Name = "renamed";
            return Task.FromResult(false);
        };
        await client.EditClientCommand.ExecuteAsync(null);
        Assert.Equal("a", client.Name);

        vm.EditClientSettings = e =>
        {
            e.Name = "renamed";
            return Task.FromResult(true);
        };
        await client.EditClientCommand.ExecuteAsync(null);

        Assert.Equal("renamed", client.Name);
        Assert.Contains(MqttSettings.Load().Clients, c => c.Name == "renamed");
    });

    [Fact]
    public Task Removing_a_client_moves_to_the_next_and_the_last_stays() => Headless.Run(() =>
    {
        var vm = new MqttViewModel(null, Settings("a", "b", "c"));
        vm.SelectedClient = vm.Clients[1];

        vm.Clients[1].RemoveClientCommand.Execute(null);
        Assert.Equal(["a", "c"], vm.Clients.Select(c => c.Name));
        Assert.Equal("c", vm.SelectedClient!.Name);

        vm.Clients[0].RemoveClientCommand.Execute(null);
        Assert.Equal("c", vm.SelectedClient!.Name);
        Assert.False(vm.Clients[0].IsRemovable);

        vm.Clients[0].RemoveClientRequested!(vm.Clients[0]);
        Assert.Single(vm.Clients);
    });

    [Fact]
    public Task Saving_keeps_what_each_tab_shows_and_finishes_writing() => Headless.Run(() =>
    {
        var vm = new MqttViewModel(null, Settings("a"));
        vm.Clients[0].ManualTopic = "typed";

        vm.SaveState();

        Assert.Equal("typed", MqttSettings.Load().Clients[0].ManualTopic);
    });

    [Fact]
    public Task Says_which_clients_use_a_certificate() => Headless.Run(() =>
    {
        var store = CertificateStore.Load(Data.At("store"));
        var root = store.Create(new CertificateSpec { Kind = CertificateKind.RootAuthority, Name = "root", CommonName = "root", NotAfter = DateTimeOffset.UtcNow.AddYears(1) });
        var settings = new MqttSettings
        {
            Clients =
            [
                new ClientProfile { Name = "by id", Host = "h", ClientId = "1", CaCertificateId = root.Id },
                new ClientProfile { Name = "client id", Host = "h", ClientId = "2", ClientCertificateId = root.Id },
                new ClientProfile { Name = "by path", Host = "h", ClientId = "3", ClientKey = Path.Combine(root.Folder, "x.pem") },
                new ClientProfile { Name = "bad path", Host = "h", ClientId = "4", CaCertificate = "bad\0path" },
                new ClientProfile { Name = "unrelated", Host = "h", ClientId = "5", CaCertificate = Data.At("other.pem") },
            ],
        };

        var vm = new MqttViewModel(store, settings);

        Assert.Equal(["by id", "client id", "by path"], vm.ClientsUsing(root));
    });
}
