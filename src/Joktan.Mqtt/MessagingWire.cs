using System;
using System.Text;
using System.Text.Json;
using Avalonia.Data.Converters;
using Joktan.Mqtt.Resources;

namespace Joktan.Mqtt;

/// <summary>A person's message, as it travels and as it is kept.</summary>
public sealed class ChatMessage
{
    public string Id { get; set; } = "";

    /// <summary>The client ID of the Joktan that sent it, which finds the sender's picture.</summary>
    public string Sender { get; set; } = "";

    public string Name { get; set; } = "";

    public string Text { get; set; } = "";

    public DateTimeOffset Sent { get; set; }

    /// <summary>The one person it went to, or null for everyone. On a message that arrived, this comes from its topic.</summary>
    public string? To { get; set; }

    /// <summary>Who wrote the message this replies to, or null.</summary>
    public string? ReplyName { get; set; }

    /// <summary>The start of the message this replies to.</summary>
    public string? ReplyText { get; set; }

    public bool IsRead { get; set; }
}

/// <summary>How much attention an alert asks for.</summary>
public enum AlertSeverity
{
    Info,
    Warning,
    Critical,
}

public static class AlertSeverityText
{
    /// <summary>What a AlertSeverity is called on screen.</summary>
    public static string Text(this AlertSeverity value) => value switch
    {
        AlertSeverity.Info => Strings.AlertSeverity_Info,
        AlertSeverity.Warning => Strings.AlertSeverity_Warning,
        AlertSeverity.Critical => Strings.AlertSeverity_Critical,
        _ => value.ToString(),
    };

    public static readonly FuncValueConverter<AlertSeverity, string> AlertSeverityConverter = new(value => value.Text());
}

/// <summary>An alert that arrived.</summary>
public sealed class AlertEntry
{
    /// <summary>The topic it arrived on.</summary>
    public string Topic { get; set; } = "";

    public string Title { get; set; } = "";

    public string Text { get; set; } = "";

    public AlertSeverity Severity { get; set; }

    public DateTimeOffset Received { get; set; }

    public bool IsRead { get; set; }
}

/// <summary>
/// The topics and payloads of alerts and messages, specified in docs/features/alerts-and-messages/spec.md.
/// Everything read here comes from whoever can publish on the server, so none of it is trusted.
/// </summary>
public static class MessagingWire
{
    public const int MaxTextLength = 4000;
    public const int MaxTitleLength = 200;
    public const int MaxReplyLength = 120;

    private const int MaxPictureBytes = 128 * 1024;
    private const int MaxPicturePixels = 512;

    public static string MessagesTopic(string prefix) => $"{prefix}/messages";

    /// <summary>Where messages for one person go. Only they listen here, but see the spec on what that does and does not keep private.</summary>
    public static string DirectTopic(string prefix, string name) => $"{prefix}/to/{name}";

    public static string AlertsTopic(string prefix) => $"{prefix}/alerts";

    public static string PersonTopic(string prefix, string clientId) => $"{prefix}/people/{Uri.EscapeDataString(clientId)}";

    /// <param name="ownName">The user's name, or null when they have none and so cannot be written to.</param>
    public static string[] Filters(string prefix, string? ownName) => ownName is null
        ? [MessagesTopic(prefix), $"{AlertsTopic(prefix)}/#", $"{prefix}/people/+"]
        : [MessagesTopic(prefix), DirectTopic(prefix, ownName), $"{AlertsTopic(prefix)}/#", $"{prefix}/people/+"];

    public static byte[] Write(ChatMessage message) => JsonSerializer.SerializeToUtf8Bytes(new
    {
        id = message.Id,
        sender = message.Sender,
        name = message.Name,
        text = message.Text,
        sent = message.Sent,
        replyName = message.ReplyName,
        replyText = message.ReplyText,
    });

    public static byte[] WriteAlert(string text, AlertSeverity severity) => JsonSerializer.SerializeToUtf8Bytes(new
    {
        text,
        severity = severity.ToString().ToLowerInvariant(),
    });

    /// <summary>Reads a message, or returns null for one without an ID, a sender and some text.</summary>
    public static ChatMessage? ReadMessage(byte[] payload, DateTimeOffset received)
    {
        if (Parse(payload) is not { } json)
            return null;

        var id = Text(json, "id", 100);
        var sender = Text(json, "sender", 200);
        var text = Text(json, "text", MaxTextLength);
        if (id.Length == 0 || sender.Length == 0 || text.Length == 0)
            return null;

        var replyName = Text(json, "replyName", 100);
        return new ChatMessage
        {
            Id = id,
            Sender = sender,
            Name = MessagingSettings.NormalizeName(Text(json, "name", 100)),
            Text = text,
            Sent = DateTimeOffset.TryParse(Text(json, "sent", 40), out var sent) ? sent : received,
            ReplyName = replyName.Length > 0 ? MessagingSettings.NormalizeName(replyName) : null,
            ReplyText = replyName.Length > 0 ? Text(json, "replyText", MaxReplyLength) : null,
        };
    }

    /// <summary>Reads an alert, which is JSON with a title or text, or else plain text. Returns null for an empty one.</summary>
    public static AlertEntry? ReadAlert(string topic, string prefix, byte[] payload, DateTimeOffset received)
    {
        var source = topic.Length > AlertsTopic(prefix).Length ? topic[(AlertsTopic(prefix).Length + 1)..] : "";
        var entry = new AlertEntry { Topic = topic, Received = received };
        if (Parse(payload) is { } json)
        {
            entry.Title = Text(json, "title", MaxTitleLength);
            entry.Text = Text(json, "text", MaxTextLength);
            entry.Severity = Text(json, "severity", 20).ToLowerInvariant() switch
            {
                "warning" => AlertSeverity.Warning,
                "critical" => AlertSeverity.Critical,
                _ => AlertSeverity.Info,
            };
        }
        else
        {
            var text = Encoding.UTF8.GetString(payload).Trim();
            entry.Text = text.Length > MaxTextLength ? text[..MaxTextLength] : text;
        }

        if (entry.Title.Length == 0 && entry.Text.Length == 0)
            return null;
        if (entry.Title.Length == 0)
            entry.Title = source.Length > 0 ? source : Strings.MessagingWire_Alert;
        return entry;
    }

    public static byte[] WritePerson(string name, byte[]? picture) => JsonSerializer.SerializeToUtf8Bytes(new
    {
        name,
        avatar = picture is null ? null : Convert.ToBase64String(picture),
    });

    /// <summary>The name a person published, or null for none, as with an empty payload or someone anonymous.</summary>
    public static string? ReadPersonName(byte[] payload)
    {
        if (payload.Length > MaxPictureBytes * 2 || Parse(payload) is not { } json || Text(json, "name", 100) is not { Length: > 0 } name)
            return null;

        var fitting = MessagingSettings.NormalizeName(name);
        return fitting == MessagingSettings.AnonymousName ? null : fitting;
    }

    /// <summary>The picture a person published, or null for none or for one that is not a small PNG.</summary>
    public static byte[]? ReadPersonPicture(byte[] payload)
    {
        if (payload.Length > MaxPictureBytes * 2 || Parse(payload) is not { } json || Text(json, "avatar", MaxPictureBytes * 2) is not { Length: > 0 } avatar)
            return null;

        try
        {
            var picture = Convert.FromBase64String(avatar);
            return picture.Length <= MaxPictureBytes && IsSmallPng(picture) ? picture : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    // Read from the header before anything decodes it, as a small file can unpack to a very large picture.
    private static bool IsSmallPng(byte[] picture)
    {
        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (picture.Length < 24 || !picture.AsSpan(0, 8).SequenceEqual(signature))
            return false;

        var width = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(picture.AsSpan(16, 4));
        var height = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(picture.AsSpan(20, 4));
        return width is > 0 and <= MaxPicturePixels && height is > 0 and <= MaxPicturePixels;
    }

    private static JsonElement? Parse(byte[] payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            return document.RootElement.ValueKind == JsonValueKind.Object ? document.RootElement.Clone() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Text(JsonElement json, string name, int maxLength)
    {
        if (!json.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            return "";

        var text = value.GetString()!.Trim();
        return text.Length > maxLength ? text[..maxLength] : text;
    }
}
