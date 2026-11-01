using System;
using System.Threading.Tasks;
using Joktan.Mqtt;
using Joktan.Mqtt.ViewModels;
using Joktan.TopicDashboards.ViewModels;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.TopicDashboards.Tests.ViewModels;

/// <summary>A dashboards page with its own data folder, and an MQTT client called "c" that has no credentials to look up.</summary>
[Collection(ProcessStateCollection.Name)]
public abstract class DashboardTest : DataFolderTest
{
    protected static string StoreFolder => DashboardStore.DefaultFolder;

    protected MqttViewModel Mqtt(int port = 1883) => new(null, new MqttSettings
    {
        Clients = [new ClientProfile { Name = "c", Host = "127.0.0.1", Port = port.ToString(), ClientId = "c-" + Guid.NewGuid().ToString("N")[..8] }],
        SelectedClient = "c",
    });

    protected TopicDashboardsViewModel Page(MqttViewModel? mqtt = null, params DashboardProfile[] dashboards)
    {
        var store = DashboardStore.Load(StoreFolder);
        store.Dashboards.AddRange(dashboards);
        return new TopicDashboardsViewModel(mqtt ?? Mqtt(), store);
    }

    protected static GraphProfile Graph(string name = "g", string topic = "t/a", string path = "temp", GraphKind kind = GraphKind.Line, params GraphKey[] keys) => new()
    {
        Name = name,
        Client = "c",
        Kind = kind,
        Values = [new GraphValue { Topic = topic, Path = path }],
        Keys = [.. keys],
    };

    protected static DashboardProfile Board(string name = "d", params GraphProfile[] graphs) => new() { Name = name, Graphs = [.. graphs] };

    protected static async Task<MqttClientViewModel> Connect(MqttViewModel mqtt)
    {
        var client = mqtt.Clients[0];
        await client.ConnectCommand.ExecuteAsync(null);
        await Headless.Until(() => client.IsConnected);
        return client;
    }

    /// <summary>A folder of its own that holds these dashboards, as one chosen before would.</summary>
    protected async Task<string> FolderWith(string folder, params DashboardProfile[] dashboards)
    {
        var path = Data.At(folder);
        var store = DashboardStore.Load(StoreFolder);
        store.Dashboards = [.. dashboards];
        await store.CopyToAsync(path);
        return path;
    }

    protected static ReceivedMessage Message(string topic, string payload, DateTime? time = null) =>
        new(topic, System.Text.Encoding.UTF8.GetBytes(payload), 0, false, time ?? DateTime.Now);
}
