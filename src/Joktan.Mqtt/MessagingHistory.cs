using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Joktan.Core;

namespace Joktan.Mqtt;

/// <summary>The messages and alerts kept between runs, as an MQTT server keeps no history of them.</summary>
public sealed class MessagingHistory
{
    public List<ChatMessage> Messages { get; set; } = [];

    public List<AlertEntry> Alerts { get; set; } = [];

    private static string FilePath => Path.Combine(DataFolder.Path, "messaging-history.json");

    public static MessagingHistory Load()
    {
        try
        {
            if (File.Exists(FilePath) && JsonSerializer.Deserialize<MessagingHistory>(File.ReadAllText(FilePath)) is { } history)
            {
                history.Messages ??= [];
                history.Alerts ??= [];
                return history;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // An unreadable history is treated as none.
        }

        return new();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(DataFolder.Path);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonFormats.Indented));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A history that cannot be saved is simply not remembered.
        }
    }
}
