using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Platform;

namespace Joktan.Help;

/// <summary>One page of help: <c>Topics/&lt;id&gt;.md</c>, built into this assembly.</summary>
/// <param name="Parent">The topic this one sits below, or null for one at the top.</param>
public sealed record HelpTopic(string Id, string Title, string? Parent = null)
{
    public const string Home = "getting-started";

    /// <summary>The topics in the order they are listed, which follows the main menu.</summary>
    public static IReadOnlyList<HelpTopic> All { get; } =
    [
        new(Home, "Getting started"),
        new("settings-and-data", "Settings and data"),
        new("themes", "Themes", "Settings and data"),
        new("search", "Search", "Settings and data"),
        new("alerts-and-messages", "Alerts and messages", "Settings and data"),
        new("folders-and-backup", "Folders and backup", "Settings and data"),
        new("dashboard", "Dashboard"),
        new("topic-dashboards", "MQTT Dashboards"),
        new("notes", "Notes"),
        new("passwords", "Passwords"),
        new("mqtt", "MQTT"),
        new("mqtt-clients", "Clients and brokers", "MQTT"),
        new("mqtt-tls", "TLS and mutual TLS", "MQTT"),
        new("mqtt-folder-messages", "Folder messages", "MQTT"),
        new("mqtt-message-settings", "Message settings", "MQTT"),
        new("mqtt-varying-fields", "Varying fields", "MQTT"),
        new("topic-explorer", "Topic Explorer"),
        new("certificates", "Certificates"),
        new("api-client", "API Client"),
        new("api-client-grpc", "gRPC requests", "API Client"),
        new("api-client-variables", "Environments and variables", "API Client"),
        new("containers", "Containers"),
    ];

    public string ReadMarkdown()
    {
        using var stream = AssetLoader.Open(new Uri($"avares://Joktan.Help/Topics/{Id}.md"));
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
