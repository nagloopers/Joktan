using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Buffers;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using MQTTnet.Protocol;
using MQTTnet.Server;

namespace Joktan.Mqtt.Tests;

/// <summary>A real MQTT broker inside the test process, listening on this machine only.</summary>
public sealed class TestBroker : IAsyncDisposable
{
    private readonly MqttServer _server;

    private TestBroker(MqttServer server, int port)
    {
        _server = server;
        Port = port;
        server.InterceptingPublishAsync += args =>
        {
            Published.Enqueue((args.ApplicationMessage.Topic, args.ApplicationMessage.Payload.ToArray(), args.ClientId));
            return Task.CompletedTask;
        };
        server.ValidatingConnectionAsync += args =>
        {
            ClientCertificatePresented |= args.ClientCertificate is not null;
            if (Refuse is { } code)
                args.ReasonCode = code;
            else if (RequiredPassword is { } password && args.Password != password)
                args.ReasonCode = MqttConnectReasonCode.BadUserNameOrPassword;
            return Task.CompletedTask;
        };
        server.InterceptingSubscriptionAsync += args =>
        {
            if (args.TopicFilter.Topic.StartsWith("refused", StringComparison.Ordinal))
                args.Response.ReasonCode = MqttSubscribeReasonCode.NotAuthorized;
            return Task.CompletedTask;
        };
        server.InterceptingInboundPacketAsync += _ => Task.CompletedTask;
    }

    public int Port { get; }

    /// <summary>What clients have published, oldest first.</summary>
    public ConcurrentQueue<(string Topic, byte[] Payload, string ClientId)> Published { get; } = new();

    public MqttConnectReasonCode? Refuse { get; set; }

    public string? RequiredPassword { get; set; }

    public bool ClientCertificatePresented { get; private set; }

    public static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <param name="certificate">With one, the broker speaks TLS only and shows this certificate.</param>
    public static async Task<TestBroker> StartAsync(X509Certificate2? certificate = null, bool askForClientCertificate = false, int? port = null)
    {
        var chosen = port ?? FreePort();
        var factory = new MqttServerFactory();
        var builder = factory.CreateServerOptionsBuilder();
        builder = certificate is null
            ? builder.WithDefaultEndpoint().WithDefaultEndpointBoundIPAddress(IPAddress.Loopback).WithDefaultEndpointPort(chosen)
            : builder.WithoutDefaultEndpoint().WithEncryptedEndpoint().WithEncryptedEndpointBoundIPAddress(IPAddress.Loopback)
                .WithEncryptedEndpointPort(chosen).WithEncryptionCertificate(X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pfx), null));
        var options = builder.Build();
        if (certificate is not null)
            options.TlsEndpointOptions.SslProtocol = System.Security.Authentication.SslProtocols.Tls12;
        if (certificate is not null && askForClientCertificate)
        {
            options.TlsEndpointOptions.ClientCertificateRequired = true;
            options.TlsEndpointOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
        }

        var server = factory.CreateMqttServer(options);
        var broker = new TestBroker(server, chosen);
        await server.StartAsync();
        return broker;
    }

    public Task StopAsync() => _server.StopAsync();

    public Task DropClientsAsync() => Task.WhenAll(_server.GetClientsAsync().Result.Select(c => c.DisconnectAsync()));

    public Task SendAsync(string topic, string payload, bool retain = false) => _server.InjectApplicationMessage(
        new InjectedMqttApplicationMessage(new MQTTnet.MqttApplicationMessageBuilder().WithTopic(topic).WithPayload(payload).WithRetainFlag(retain).Build()));

    public IEnumerable<string> PublishedTopics => Published.Select(p => p.Topic);

    public async ValueTask DisposeAsync()
    {
        await _server.StopAsync();
        _server.Dispose();
    }
}
