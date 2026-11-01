using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Joktan.Core;
using Joktan.Mqtt.Resources;

namespace Joktan.Mqtt;

/// <summary>
/// A message that arrived on a subscription.
/// </summary>
public sealed class ReceivedMessage
{
    private const int PreviewLength = 120;
    private const int HexBytesShown = 256;

    private static readonly UTF8Encoding StrictUtf8 = new(false, throwOnInvalidBytes: true);

    private readonly Lazy<(byte[] Payload, bool IsGzipped)> _decoded;
    private readonly Lazy<JsonElement?> _json;
    private readonly bool _looksZipped;
    private string? _text;
    private string? _formatted;
    private IReadOnlyList<NumericField>? _numericFields;

    /// <param name="payload">The payload as it arrived; a gzipped one is unzipped when it is first needed.</param>
    public ReceivedMessage(string topic, byte[] payload, int qos, bool retain, DateTime time)
    {
        Topic = topic;
        ReceivedSize = payload.Length;
        _looksZipped = Gzip.LooksZipped(payload);

        // Not here, as this runs on the MQTT client's thread for messages that may be of no interest.
        _decoded = new Lazy<(byte[], bool)>(() => Gzip.TryDecompress(payload) is { } unzipped ? (unzipped, true) : (payload, false));
        _json = new Lazy<JsonElement?>(ReadJson);
        Qos = qos;
        Retain = retain;
        Time = time;
    }

    public string Topic { get; }

    /// <summary>The payload, unzipped when it arrived gzipped.</summary>
    public byte[] Payload => _decoded.Value.Payload;

    /// <summary>True when the payload arrived gzipped and <see cref="Payload"/> is what it unzipped to.</summary>
    public bool IsGzipped => _decoded.Value.IsGzipped;

    /// <summary>The payload as JSON, if it is an object or array of no more than the size numbers are read from.</summary>
    public JsonElement? Json => _json.Value;

    /// <summary>Why no numbers can be read from the payload at all, or null.</summary>
    public string? NumbersProblem
    {
        get
        {
            if (_looksZipped && !IsGzipped)
                return Strings.ReceivedMessage_ItLooksLikeGzipButCould;
            if (Payload.Length > JsonNumbers.MaxPayloadBytes)
                return string.Format(Strings.ReceivedMessage_KBOverTheKBLimit, (IsGzipped ? Strings.ReceivedMessage_ItUnzippedTo : Strings.ReceivedMessage_ItIs), Payload.Length / 1024, JsonNumbers.MaxPayloadBytes / 1024);
            return Number is null && Json is null ? Strings.ReceivedMessage_ItIsNotANumberOr : null;
        }
    }

    /// <summary>The size of the payload as it arrived, before any unzipping.</summary>
    public int ReceivedSize { get; }

    /// <summary>"gzip" for a message that arrived gzipped, otherwise empty.</summary>
    public string GzipTag => IsGzipped ? "gzip" : "";

    public int Qos { get; }

    /// <summary>True when the broker sent this as the stored last value of the topic.</summary>
    public bool Retain { get; }

    public DateTime Time { get; }

    public string TimeText => Time.ToString("HH:mm:ss.fff");

    public string SizeText => IsGzipped
        ? string.Format(Strings.ReceivedMessage_BytesUnzipped, ReceivedSize, Payload.Length)
        : string.Format(Strings.ReceivedMessage_Bytes, Payload.Length);

    /// <summary>The payload as text, or a hex dump when it is not valid UTF-8.</summary>
    public string Text => _text ??= Decode();

    /// <summary>The payload laid out for reading: JSON is indented, anything else is shown as is.</summary>
    public string Formatted => _formatted ??= Format();

    /// <summary>The start of the payload on one line.</summary>
    public string Preview
    {
        get
        {
            if (Payload.Length == 0)
                return Strings.ReceivedMessage_Empty;

            var line = Text.ReplaceLineEndings(" ").Trim();
            return line.Length > PreviewLength ? line[..PreviewLength] + "…" : line;
        }
    }

    /// <summary>The payload as a number, if it is one: a bare number or a JSON number.</summary>
    public double? Number
        => Payload.Length is > 0 and <= 32
           && double.TryParse(Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
           && double.IsFinite(value)
            ? value
            : null;

    /// <summary>The numbers in the payload: the whole of it when it is one, or those inside its JSON.</summary>
    public IReadOnlyList<NumericField> NumericFields
        => _numericFields ??= Number is { } number ? [new NumericField("", [number])] : JsonNumbers.Find(Payload);

    private JsonElement? ReadJson()
    {
        if (Payload.Length is 0 or > JsonNumbers.MaxPayloadBytes)
            return null;

        try
        {
            using var document = JsonDocument.Parse(Payload);
            return document.RootElement.ValueKind is JsonValueKind.Object or JsonValueKind.Array ? document.RootElement.Clone() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private string Decode()
    {
        if (Payload.Length == 0)
            return "";

        try
        {
            return StrictUtf8.GetString(Payload);
        }
        catch (DecoderFallbackException)
        {
            var shown = Payload.AsSpan(0, Math.Min(Payload.Length, HexBytesShown));
            var hex = Convert.ToHexString(shown);
            var spaced = new StringBuilder(hex.Length * 3 / 2);
            for (var i = 0; i < hex.Length; i += 2)
                spaced.Append(hex, i, 2).Append(' ');
            return string.Format(Strings.ReceivedMessage_BinaryBytes, Payload.Length, spaced.ToString().TrimEnd())
                   + (Payload.Length > HexBytesShown ? " …" : "");
        }
    }

    private string Format()
    {
        var text = Text;
        if (text.Length == 0 || (text[0] != '{' && text[0] != '['))
            return text;

        try
        {
            return JsonNode.Parse(text)?.ToJsonString(JsonFormats.Indented) ?? text;
        }
        catch (JsonException)
        {
            return text;
        }
    }
}
