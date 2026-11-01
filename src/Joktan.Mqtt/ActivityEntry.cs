using System;
using Joktan.Mqtt.Resources;

namespace Joktan.Mqtt;

/// <summary>
/// One line of the MQTT activity log. <see cref="Body"/> is set for sent messages.
/// </summary>
public sealed record ActivityEntry(
    DateTime Time, string Event, string Topic = "", string Source = "", string Detail = "", string? Body = null)
{
    /// <summary>The event of a line that records a failure.</summary>
    public static string FailedEvent => Strings.ActivityEntry_Failed;

    public string TimeText => Time.ToString("HH:mm:ss");

    /// <summary>The detail of a sent message: its size, and for a gzipped one the size before zipping too.</summary>
    public static string SizeDetail(int bodyLength, int payloadLength, bool gzipped)
        => gzipped ? string.Format(Strings.ActivityEntry_BytesGzippedUnzipped, payloadLength, bodyLength) : string.Format(Strings.ActivityEntry_Bytes, payloadLength);
}
