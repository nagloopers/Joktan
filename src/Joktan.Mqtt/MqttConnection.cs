using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Joktan.Mqtt.Resources;
using MQTTnet;
using MQTTnet.Exceptions;
using MQTTnet.Protocol;

namespace Joktan.Mqtt;

public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
    Failed,
}

public static class ConnectionStateText
{
    /// <summary>What a ConnectionState is called on screen.</summary>
    public static string Text(this ConnectionState value) => value switch
    {
        ConnectionState.Disconnected => Strings.ConnectionState_Disconnected,
        ConnectionState.Connecting => Strings.ConnectionState_Connecting,
        ConnectionState.Connected => Strings.ConnectionState_Connected,
        ConnectionState.Reconnecting => Strings.ConnectionState_Reconnecting,
        ConnectionState.Failed => Strings.ConnectionState_Failed,
        _ => value.ToString(),
    };
}

public sealed record BrokerEndpoint(
    string Host, int Port, string ClientId, string? Username, string? Password, TlsEndpoint? Tls = null);

/// <summary>
/// How a connection uses TLS. With no <see cref="CaCertificate"/> the broker's certificate must chain
/// to a root the system trusts; with a <see cref="ClientCertificate"/> the client proves who it is
/// too (mutual TLS).
/// </summary>
public sealed record TlsEndpoint(
    string? CaCertificate, string? ClientCertificate, string? ClientKey, string? KeyPassword, bool AcceptAnyServerCertificate);

/// <summary>
/// A connection to one MQTT broker. A connection that drops is retried until it comes back or
/// <see cref="DisconnectAsync"/> is called.
/// </summary>
public sealed class MqttConnection
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);
    private static string NoAnswer => Strings.MqttConnection_NoAnswerFromTheBrokerWithin;

    private readonly IMqttClient _client = new MqttClientFactory().CreateMqttClient();
    private MqttClientOptions? _options;
    private CancellationTokenSource? _session;
    private readonly Dictionary<string, string[]> _wanted = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _subscribing = new(1, 1);
    private string[] _filters = [];
    private X509Certificate2Collection? _authorities;
    private bool _usesClientCertificate;
    private volatile string? _certificateRejection;

    public MqttConnection()
    {
        _client.DisconnectedAsync += OnDisconnectedAsync;
        _client.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;
    }

    /// <summary>The name of the client this connection belongs to, for the activity log.</summary>
    public string Name { get; set; } = "";

    public ConnectionState State { get; private set; }

    /// <summary>Why the last attempt failed; set while <see cref="State"/> is Failed.</summary>
    public string? FailureReason { get; private set; }

    public bool IsConnected => State == ConnectionState.Connected;

    /// <summary>Raised on an arbitrary thread whenever <see cref="State"/> changes.</summary>
    public event Action? StateChanged;

    /// <summary>Raised on an arbitrary thread for every message that matches a subscription.</summary>
    public event Action<ReceivedMessage>? MessageReceived;

    public async Task ConnectAsync(BrokerEndpoint endpoint)
    {
        await DisconnectAsync();

        var builder = new MqttClientOptionsBuilder()
            .WithTcpServer(endpoint.Host, endpoint.Port)
            .WithClientId(endpoint.ClientId)
            .WithTimeout(ConnectTimeout);
        if (!string.IsNullOrEmpty(endpoint.Username))
            builder = builder.WithCredentials(endpoint.Username, endpoint.Password ?? "");

        // Certificate files are read on every connect, so a replaced file takes effect without a restart.
        try
        {
            if (endpoint.Tls is { } tls)
                builder = builder.WithTlsOptions(BuildTls(tls));
        }
        catch (FormatException e)
        {
            Set(ConnectionState.Failed, e.Message);
            return;
        }

        _options = builder.Build();

        var session = _session = new CancellationTokenSource();
        Set(ConnectionState.Connecting);

        var error = await TryConnectAsync(session.Token);
        if (session.IsCancellationRequested)
            return;

        if (error is null)
        {
            await SubscribeAndSetConnectedAsync();
        }
        else
        {
            _session = null;
            Set(ConnectionState.Failed, error);
        }
    }

    public async Task DisconnectAsync()
    {
        var session = Interlocked.Exchange(ref _session, null);
        if (session is null && State is ConnectionState.Disconnected or ConnectionState.Failed)
        {
            Set(ConnectionState.Disconnected);
            return;
        }

        session?.Cancel();
        try
        {
            if (_client.IsConnected)
                await _client.DisconnectAsync();
        }
        catch (Exception)
        {
            // The connection is being dropped either way.
        }

        Set(ConnectionState.Disconnected);
    }

    /// <summary>
    /// Replaces the topic filters one part of the app listens to; the connection listens to those of
    /// every part, so each must ignore messages it did not ask for. They are applied now if connected,
    /// and again after every connect. Returns why a filter was refused, or null.
    /// </summary>
    /// <param name="owner">Who the filters are for, so that parts of the app do not replace each other's.</param>
    public async Task<string?> SetSubscriptionsAsync(string owner, IEnumerable<string> filters)
    {
        await _subscribing.WaitAsync();
        try
        {
            var before = _wanted.GetValueOrDefault(owner, []);
            var wanted = filters.Distinct().ToArray();
            _wanted[owner] = wanted;

            // A broker may deliver a message once per filter that matches it, so overlaps are left out.
            var previous = _filters;
            _filters = TopicRules.WithoutCovered(_wanted.Values.SelectMany(f => f));
            if (!IsConnected)
                return null;

            try
            {
                // New filters go first, so that messages others still want keep arriving.
                var added = _filters.Except(previous).ToArray();
                var error = await SubscribeAsync(added);
                foreach (var removed in previous.Except(_filters))
                    await _client.UnsubscribeAsync(removed);

                // A filter already covered brings no retained values, as nothing new is subscribed.
                // Subscribing to it for a moment asks the broker for them.
                foreach (var filter in wanted.Except(before).Except(added))
                {
                    error ??= await SubscribeAsync([filter]);
                    if (!_filters.Contains(filter))
                        await _client.UnsubscribeAsync(filter);
                }

                return error;
            }
            catch (Exception e)
            {
                return e.Message;
            }
        }
        finally
        {
            _subscribing.Release();
        }
    }

    public async Task<(bool Ok, string? Error)> PublishAsync(string topic, byte[] payload, int qos, bool retain)
    {
        if (!IsConnected)
            return (false, Strings.MqttConnection_NotConnected);

        try
        {
            var message = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(payload)
                .WithQualityOfServiceLevel((MqttQualityOfServiceLevel)qos)
                .WithRetainFlag(retain)
                .Build();

            var result = await _client.PublishAsync(message);
            return result.IsSuccess
                ? (true, null)
                : (false, string.IsNullOrEmpty(result.ReasonString) ? result.ReasonCode.ToString() : result.ReasonString);
        }
        catch (Exception e)
        {
            return (false, e.Message);
        }
    }

    private async Task<string?> TryConnectAsync(CancellationToken session)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(session);
        timeout.CancelAfter(ConnectTimeout);

        _certificateRejection = null;
        try
        {
            var result = await _client.ConnectAsync(_options!, timeout.Token);
            return result.ResultCode == MqttClientConnectResultCode.Success ? null : Describe(result.ResultCode);
        }
        catch (OperationCanceledException)
        {
            return _certificateRejection is { } rejected
                ? string.Format(Strings.MqttConnection_TheBrokerSCertificateWasRefused, rejected)
                : NoAnswer;
        }
        catch (Exception e)
        {
            if (_certificateRejection is { } rejected)
                return string.Format(Strings.MqttConnection_TheBrokerSCertificateWasRefused, rejected);

            // The client reports running out of time as the connection closing, or as its own timeout.
            if (timeout.IsCancellationRequested || Find<MqttCommunicationTimedOutException>(e) is not null)
                return NoAnswer;

            var cause = e.InnerException ?? e;
            if (Find<AuthenticationException>(e) is { } handshake)
            {
                var reason = Innermost(handshake).Message.TrimEnd('.');
                return _usesClientCertificate
                    ? string.Format(Strings.MqttConnection_TheTLSHandshakeFailedCheckThat, reason)
                    : string.Format(Strings.MqttConnection_TheTLSHandshakeFailedIfThe, reason);
            }

            // A broker that refuses the client certificate usually just closes the connection.
            return _usesClientCertificate && _options?.ChannelOptions?.TlsOptions?.UseTls == true
                ? string.Format(Strings.MqttConnection_CouldNotConnectCheckThatThe, cause.Message)
                : string.Format(Strings.MqttConnection_CouldNotConnect, cause.Message);
        }
    }

    private static T? Find<T>(Exception? e) where T : Exception
    {
        for (; e is not null; e = e.InnerException)
        {
            if (e is T found)
                return found;
        }

        return null;
    }

    private static Exception Innermost(Exception e)
    {
        while (e.InnerException is not null)
            e = e.InnerException;
        return e;
    }

    private MqttClientTlsOptions BuildTls(TlsEndpoint tls)
    {
        _authorities = string.IsNullOrWhiteSpace(tls.CaCertificate) ? null : CertificateLoader.LoadAuthorities(tls.CaCertificate);
        _usesClientCertificate = !string.IsNullOrWhiteSpace(tls.ClientCertificate);

        var builder = new MqttClientTlsOptionsBuilder()
            .UseTls()
            .WithCertificateValidationHandler(args => Validate(args, tls.AcceptAnyServerCertificate));
        if (_usesClientCertificate)
            builder = builder.WithClientCertificates([CertificateLoader.LoadClient(tls.ClientCertificate!, tls.ClientKey, tls.KeyPassword)]);
        return builder.Build();
    }

    // Runs during the handshake. The reason for a refusal is kept, because the error that the
    // connect attempt then fails with does not say why.
    private bool Validate(MqttClientCertificateValidationEventArgs args, bool acceptAny)
    {
        if (acceptAny)
            return true;

        var errors = args.SslPolicyErrors;
        if (errors.HasFlag(System.Net.Security.SslPolicyErrors.RemoteCertificateNotAvailable))
            return Refuse(Strings.MqttConnection_TheBrokerSentNoCertificate);
        if (errors.HasFlag(System.Net.Security.SslPolicyErrors.RemoteCertificateNameMismatch))
            return Refuse(Strings.MqttConnection_ItWasNotIssuedForThis);

        if (_authorities is null)
        {
            return errors == System.Net.Security.SslPolicyErrors.None
                || Refuse(Strings.MqttConnection_ItIsNotSignedByAn);
        }

        using var certificate = X509CertificateLoader.LoadCertificate(args.Certificate.GetRawCertData());
        return CertificateLoader.CheckChain(certificate, _authorities, args.Chain) is not { } problem || Refuse(problem);

        bool Refuse(string reason)
        {
            _certificateRejection = reason;
            return false;
        }
    }

    private Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs args)
    {
        // Failed attempts raise this too; only a drop of an established connection is retried.
        var session = _session;
        if (session is null || session.IsCancellationRequested || State != ConnectionState.Connected)
            return Task.CompletedTask;

        Set(ConnectionState.Reconnecting);
        _ = ReconnectAsync(session.Token);
        return Task.CompletedTask;
    }

    private async Task ReconnectAsync(CancellationToken session)
    {
        try
        {
            while (!session.IsCancellationRequested)
            {
                await Task.Delay(ReconnectDelay, session);
                if (await TryConnectAsync(session) is null && !session.IsCancellationRequested)
                {
                    // Subscriptions do not survive a new session, so they are made again.
                    await SubscribeAndSetConnectedAsync();
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Disconnect was requested.
        }
    }

    // Held together, so that filters changed meanwhile are not missed by this or by the change.
    private async Task SubscribeAndSetConnectedAsync()
    {
        await _subscribing.WaitAsync();
        try
        {
            await SubscribeAsync(_filters);
            Set(ConnectionState.Connected);
        }
        finally
        {
            _subscribing.Release();
        }
    }

    private async Task<string?> SubscribeAsync(IEnumerable<string> filters)
    {
        try
        {
            foreach (var filter in filters)
            {
                // QoS 2 here means messages arrive at whatever QoS they were published with.
                var result = await _client.SubscribeAsync(filter, MqttQualityOfServiceLevel.ExactlyOnce);
                if (result.Items.Any(i => i.ResultCode > MqttClientSubscribeResultCode.GrantedQoS2))
                    return string.Format(Strings.MqttConnection_TheBrokerRefusedTheFilter, filter);
            }

            return null;
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }

    private Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs args)
    {
        var message = args.ApplicationMessage;
        MessageReceived?.Invoke(new ReceivedMessage(
            message.Topic, message.Payload.ToArray(), (int)message.QualityOfServiceLevel, message.Retain, DateTime.Now));
        return Task.CompletedTask;
    }

    private void Set(ConnectionState state, string? failureReason = null)
    {
        if (State == state && FailureReason == failureReason)
            return;

        var wasConnected = State == ConnectionState.Connected;
        State = state;
        FailureReason = failureReason;
        MqttMetrics.ConnectedChanged(wasConnected, state == ConnectionState.Connected);
        StateChanged?.Invoke();
    }

    private static string Describe(MqttClientConnectResultCode code) => code switch
    {
        MqttClientConnectResultCode.BadUserNameOrPassword => Strings.MqttConnection_BadUsernameOrPassword,
        MqttClientConnectResultCode.NotAuthorized => Strings.MqttConnection_NotAuthorized,
        MqttClientConnectResultCode.ClientIdentifierNotValid => Strings.MqttConnection_TheBrokerRejectedTheClientID,
        MqttClientConnectResultCode.ServerUnavailable => Strings.MqttConnection_TheBrokerIsUnavailable,
        MqttClientConnectResultCode.UnsupportedProtocolVersion => Strings.MqttConnection_TheBrokerDoesNotSupportThis,
        _ => string.Format(Strings.MqttConnection_TheBrokerRefusedTheConnection, code),
    };
}
