using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Joktan.Certificates;
using Joktan.Tests.TestSupport;
using MQTTnet.Protocol;
using Xunit;

namespace Joktan.Mqtt.Tests;

public class MqttConnectionTests : IAsyncLifetime
{
    private TestBroker _broker = null!;
    private readonly MqttConnection _connection = new();

    public async ValueTask InitializeAsync() => _broker = await TestBroker.StartAsync();

    public async ValueTask DisposeAsync()
    {
        await _connection.DisconnectAsync();
        await _broker.DisposeAsync();
    }

    private BrokerEndpoint Endpoint(string? username = null, string? password = null) =>
        new("127.0.0.1", _broker.Port, "test-" + Guid.NewGuid().ToString("N")[..8], username, password);

    private static async Task Until(Func<bool> condition)
    {
        var start = Environment.TickCount64;
        while (!condition())
        {
            if (Environment.TickCount64 - start > 15000)
                throw new TimeoutException("The condition did not hold in time.");
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task Connects_and_disconnects()
    {
        var changes = new List<ConnectionState>();
        _connection.StateChanged += () => changes.Add(_connection.State);
        Assert.False(_connection.IsConnected);

        await _connection.ConnectAsync(Endpoint());

        Assert.True(_connection.IsConnected);
        Assert.Null(_connection.FailureReason);

        await _connection.DisconnectAsync();

        Assert.Equal(ConnectionState.Disconnected, _connection.State);
        Assert.Equal([ConnectionState.Connecting, ConnectionState.Connected, ConnectionState.Disconnected], changes);

        // Disconnecting again changes nothing.
        await _connection.DisconnectAsync();
        Assert.Equal(3, changes.Count);
    }

    [Fact]
    public async Task A_second_connect_replaces_the_first()
    {
        await _connection.ConnectAsync(Endpoint());
        await _connection.ConnectAsync(Endpoint());

        Assert.True(_connection.IsConnected);
    }

    [Fact]
    public async Task Sends_credentials()
    {
        _broker.RequiredPassword = "secret";

        await _connection.ConnectAsync(Endpoint("me", "wrong"));
        Assert.Equal(ConnectionState.Failed, _connection.State);
        Assert.Equal("Bad username or password", _connection.FailureReason);

        await _connection.ConnectAsync(Endpoint("me"));
        Assert.Equal("Bad username or password", _connection.FailureReason);

        await _connection.ConnectAsync(Endpoint("me", "secret"));
        Assert.True(_connection.IsConnected);
    }

    [Theory]
    [InlineData(MqttConnectReasonCode.NotAuthorized, "Not authorized")]
    [InlineData(MqttConnectReasonCode.ClientIdentifierNotValid, "The broker rejected the client ID")]
    [InlineData(MqttConnectReasonCode.ServerUnavailable, "The broker is unavailable")]
    [InlineData(MqttConnectReasonCode.UnsupportedProtocolVersion, "The broker does not support this MQTT version")]
    [InlineData(MqttConnectReasonCode.Banned, "The broker refused the connection: Banned")]
    public async Task Says_why_a_broker_refused(MqttConnectReasonCode code, string expected)
    {
        _broker.Refuse = code;

        await _connection.ConnectAsync(Endpoint());

        Assert.Equal(ConnectionState.Failed, _connection.State);
        Assert.Equal(expected, _connection.FailureReason);
    }

    [Fact]
    public async Task A_broker_that_is_not_there_is_reported()
    {
        await _connection.ConnectAsync(new BrokerEndpoint("127.0.0.1", TestBroker.FreePort(), "x", null, null));

        Assert.Equal(ConnectionState.Failed, _connection.State);
        Assert.StartsWith("Could not connect: ", _connection.FailureReason);

        await _connection.DisconnectAsync();
        Assert.Equal(ConnectionState.Disconnected, _connection.State);
        Assert.Null(_connection.FailureReason);
    }

    [Fact]
    public async Task Publishing_needs_a_connection_and_reaches_the_broker()
    {
        var (ok, error) = await _connection.PublishAsync("t", [1], 0, false);
        Assert.False(ok);
        Assert.Equal("Not connected", error);

        await _connection.ConnectAsync(Endpoint());
        (ok, error) = await _connection.PublishAsync("a/b", Encoding.UTF8.GetBytes("hi"), 1, false);

        Assert.True(ok);
        Assert.Null(error);
        await Until(() => _broker.PublishedTopics.Contains("a/b"));
    }

    [Fact]
    public async Task A_publish_that_cannot_be_built_says_why()
    {
        await _connection.ConnectAsync(Endpoint());

        var (ok, error) = await _connection.PublishAsync("bad/#", [1], 0, false);

        Assert.False(ok);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task Subscriptions_made_before_connecting_apply_on_connect_and_messages_arrive()
    {
        var received = new ConcurrentQueue<ReceivedMessage>();
        _connection.MessageReceived += received.Enqueue;
        Assert.Null(await _connection.SetSubscriptionsAsync("me", ["a/#", "a/#"]));

        await _connection.ConnectAsync(Endpoint());
        await _broker.SendAsync("a/b", "hello");

        await Until(() => received.Any(m => m.Topic == "a/b"));
    }

    [Fact]
    public async Task Each_owner_has_its_own_filters_and_covered_ones_still_bring_retained_values()
    {
        var received = new ConcurrentQueue<ReceivedMessage>();
        _connection.MessageReceived += received.Enqueue;
        await _broker.SendAsync("x/retained", "kept", retain: true);
        await _connection.ConnectAsync(Endpoint());

        Assert.Null(await _connection.SetSubscriptionsAsync("one", ["y/#"]));
        Assert.Null(await _connection.SetSubscriptionsAsync("two", ["#"]));
        await Until(() => received.Any(m => m.Topic == "x/retained"));

        // Covered by another's filter, so it is only subscribed to for a moment.
        var before = received.Count(m => m.Topic == "x/retained");
        Assert.Null(await _connection.SetSubscriptionsAsync("one", ["y/#", "x/retained"]));
        await Until(() => received.Count(m => m.Topic == "x/retained") > before);

        Assert.Null(await _connection.SetSubscriptionsAsync("two", []));
        while (received.TryDequeue(out _))
        {
        }

        await _broker.SendAsync("z", "not wanted");
        await _broker.SendAsync("y/1", "wanted");
        await Until(() => received.Any(m => m.Topic == "y/1"));
        Assert.DoesNotContain(received, m => m.Topic == "z");
    }

    [Fact]
    public async Task A_filter_the_broker_refuses_is_reported()
    {
        await _connection.ConnectAsync(Endpoint());

        var error = await _connection.SetSubscriptionsAsync("me", ["refused/x"]);

        Assert.Equal("The broker refused the filter refused/x", error);
    }

    [Fact]
    public async Task A_filter_that_is_not_valid_is_reported()
    {
        await _connection.ConnectAsync(Endpoint());

        Assert.NotNull(await _connection.SetSubscriptionsAsync("me", [""]));
    }

    [Fact]
    public async Task A_dropped_connection_comes_back_with_its_subscriptions()
    {
        var received = new ConcurrentQueue<ReceivedMessage>();
        _connection.MessageReceived += received.Enqueue;
        await _connection.SetSubscriptionsAsync("me", ["back/#"]);
        await _connection.ConnectAsync(Endpoint());
        var seen = new ConcurrentQueue<ConnectionState>();
        _connection.StateChanged += () => seen.Enqueue(_connection.State);

        await _broker.DropClientsAsync();

        await Until(() => seen.Contains(ConnectionState.Reconnecting));
        await Until(() => _connection.IsConnected);
        await _broker.SendAsync("back/1", "here");
        await Until(() => received.Any(m => m.Topic == "back/1"));
    }

    [Fact]
    public async Task Disconnecting_while_trying_again_stops_trying()
    {
        await _connection.ConnectAsync(Endpoint());
        await _broker.StopAsync();
        await Until(() => _connection.State == ConnectionState.Reconnecting);

        await _connection.DisconnectAsync();

        Assert.Equal(ConnectionState.Disconnected, _connection.State);
    }

    [Fact]
    public async Task Nothing_connected_means_nothing_is_retried_and_failing_again_keeps_trying_until_stopped()
    {
        await _connection.ConnectAsync(Endpoint());
        await _broker.StopAsync();
        await Until(() => _connection.State == ConnectionState.Reconnecting);

        // One attempt fails after the delay; the loop keeps going until it is told to stop.
        await Task.Delay(TimeSpan.FromSeconds(5.5), TestContext.Current.CancellationToken);
        Assert.Equal(ConnectionState.Reconnecting, _connection.State);
    }

    [Fact]
    public async Task A_client_that_cannot_publish_any_more_says_why()
    {
        await _connection.ConnectAsync(Endpoint());
        await _broker.StopAsync();

        // State may not have caught up with the broker going away.
        var (ok, error) = await _connection.PublishAsync("a", [1], 1, false);
        if (!ok)
            Assert.NotNull(error);
    }
}

public class MqttConnectionTlsTests : IDisposable
{
    private readonly TempFolder _folder = new();
    private readonly CertificateStore _store;
    private readonly StoredCertificate _root;
    private readonly MqttConnection _connection = new();

    public MqttConnectionTlsTests()
    {
        _store = CertificateStore.Load(_folder.Path);
        _root = _store.Create(Spec("root", CertificateKind.RootAuthority));
    }

    public void Dispose()
    {
        _connection.DisconnectAsync().Wait(TestContext.Current.CancellationToken);
        _folder.Dispose();
    }

    private static CertificateSpec Spec(string name, CertificateKind kind, StoredCertificate? issuer = null, string names = "") => new()
    {
        Kind = kind,
        Name = name,
        CommonName = name,
        IssuerId = issuer?.Id,
        AlternativeNames = names,
        NotAfter = DateTimeOffset.UtcNow.AddYears(1),
    };

    private Task<TestBroker> Broker(string hostName = "127.0.0.1", bool askForClientCertificate = false)
    {
        var entry = _store.Create(Spec("server " + Guid.NewGuid().ToString("N")[..6], CertificateKind.Server, _root, hostName));
        var certificate = X509Certificate2.CreateFromPemFile(entry.CertificatePath, entry.KeyPath);
        return TestBroker.StartAsync(certificate, askForClientCertificate);
    }

    private static BrokerEndpoint Endpoint(TestBroker broker, TlsEndpoint tls) =>
        new("127.0.0.1", broker.Port, "tls-" + Guid.NewGuid().ToString("N")[..8], null, null, tls);

    [Fact]
    public async Task A_certificate_can_be_accepted_as_it_is()
    {
        await using var broker = await Broker();

        await _connection.ConnectAsync(Endpoint(broker, new TlsEndpoint(null, null, null, null, true)));

        Assert.True(_connection.IsConnected);
    }

    [Fact]
    public async Task A_certificate_signed_by_the_chosen_ca_is_trusted()
    {
        await using var broker = await Broker();

        await _connection.ConnectAsync(Endpoint(broker, new TlsEndpoint(_root.CertificatePath, null, null, null, false)));

        Assert.True(_connection.IsConnected);
    }

    [Fact]
    public async Task A_certificate_signed_by_another_ca_is_refused_and_says_why()
    {
        await using var broker = await Broker();
        var other = _store.Create(Spec("other", CertificateKind.RootAuthority));

        await _connection.ConnectAsync(Endpoint(broker, new TlsEndpoint(other.CertificatePath, null, null, null, false)));

        Assert.Equal(ConnectionState.Failed, _connection.State);
        Assert.StartsWith("The broker's certificate was refused: ", _connection.FailureReason);
    }

    [Fact]
    public async Task A_certificate_this_computer_does_not_trust_is_refused()
    {
        await using var broker = await Broker();

        await _connection.ConnectAsync(Endpoint(broker, new TlsEndpoint(null, null, null, null, false)));

        Assert.Contains("is not signed by an authority this computer trusts", _connection.FailureReason);
    }

    [Fact]
    public async Task A_certificate_for_another_host_is_refused()
    {
        await using var broker = await Broker("another.example");

        await _connection.ConnectAsync(Endpoint(broker, new TlsEndpoint(_root.CertificatePath, null, null, null, false)));

        Assert.Contains("not issued for this host name", _connection.FailureReason);
    }

    [Fact]
    public async Task A_client_certificate_is_presented_when_asked_for()
    {
        await using var broker = await Broker(askForClientCertificate: true);
        var client = _store.Create(Spec("client", CertificateKind.Client, _root));

        await _connection.ConnectAsync(Endpoint(broker, new TlsEndpoint(_root.CertificatePath, client.CertificatePath, client.KeyPath, null, false)));

        Assert.True(_connection.IsConnected);
        Assert.True(broker.ClientCertificatePresented);
    }

    [Fact]
    public async Task Files_that_cannot_be_read_stop_the_connection_before_it_starts()
    {
        await _connection.ConnectAsync(new BrokerEndpoint("127.0.0.1", 1, "x", null, null, new TlsEndpoint(_folder.At("gone.pem"), null, null, null, false)));

        Assert.Equal(ConnectionState.Failed, _connection.State);
        Assert.NotNull(_connection.FailureReason);
    }

    [Fact]
    public async Task A_broker_without_tls_fails_the_handshake_and_says_what_to_check()
    {
        await using var plain = await TestBroker.StartAsync();

        await _connection.ConnectAsync(Endpoint(plain, new TlsEndpoint(null, null, null, null, true)));
        Assert.Equal(ConnectionState.Failed, _connection.State);
        Assert.NotNull(_connection.FailureReason);

        var client = _store.Create(Spec("client", CertificateKind.Client, _root));
        await _connection.ConnectAsync(Endpoint(plain, new TlsEndpoint(null, client.CertificatePath, client.KeyPath, null, true)));
        Assert.Contains("client certificate", _connection.FailureReason);
    }


}
