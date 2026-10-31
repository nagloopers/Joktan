using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Joktan.Mqtt;
using Joktan.Mqtt.ViewModels;

namespace Joktan.TopicDashboards.ViewModels;

/// <summary>What the dashboards receive through one client: the messages on the topics their graphs draw.</summary>
internal sealed class ClientFeed : IDisposable
{
    private const string SubscriptionOwner = "dashboards";

    private readonly ConcurrentQueue<ReceivedMessage> _incoming = new();
    private volatile TopicFilterSet _filters = new([]);

    public ClientFeed(MqttClientViewModel client)
    {
        Client = client;
        Name = client.Name;
        client.Connection.MessageReceived += OnMessageReceived;
    }

    public MqttClientViewModel Client { get; }

    /// <summary>The client's name as graphs know it, to tell when it has been renamed.</summary>
    public string Name { get; set; }

    /// <summary>Subscribes to these filters, in place of those before. Returns why it could not, or null.</summary>
    public Task<string?> SetFiltersAsync(HashSet<string> filters)
    {
        _filters = new TopicFilterSet(filters);
        return Client.Connection.SetSubscriptionsAsync(SubscriptionOwner, filters);
    }

    public bool TryTake(out ReceivedMessage message) => _incoming.TryDequeue(out message!);

    // Arrives on the MQTT client's thread; graphs are only touched on the UI thread.
    private void OnMessageReceived(ReceivedMessage message)
    {
        if (_filters.Contains(message.Topic))
            _incoming.Enqueue(message);
    }

    public void Dispose()
    {
        Client.Connection.MessageReceived -= OnMessageReceived;

        // The client can stay connected without its tab, and the dashboards no longer want these.
        _ = Client.Connection.SetSubscriptionsAsync(SubscriptionOwner, []);
    }
}
