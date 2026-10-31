using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Joktan.Core;

namespace Joktan.Mqtt;

/// <summary>
/// What the MQTT page and the topic explorer remember between runs: mainly the clients, each
/// with its own tab. Passwords are not part of it; see
/// <see cref="CredentialStore"/>.
/// </summary>
public sealed class MqttSettings
{
    /// <summary>The name of the first client, made when there is none.</summary>
    public const string FirstClientName = "main";

    public List<ClientProfile> Clients { get; set; } = [];

    /// <summary>The client whose tab was open last.</summary>
    public string SelectedClient { get; set; } = "";

    /// <summary>The server for alerts and messages, and how the user appears in messages.</summary>
    public MessagingSettings Messaging { get; set; } = new();

    // The single broker of earlier versions, read once and moved into the first client.

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Host { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Port { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Username { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ClientId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Folder { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ManualTopic { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ManualBody { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ManualQos { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ManualRetain { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExplorerFilters { get; set; }

    /// <summary>Set to keep Joktan's settings somewhere other than the user's application data folder.</summary>
    public const string DataFolderVariable = Joktan.Core.DataFolder.Variable;

    private static string FilePath => Path.Combine(Joktan.Core.DataFolder.Path, "mqtt.json");

    public static MqttSettings Load()
    {
        var settings = new MqttSettings();
        try
        {
            if (File.Exists(FilePath))
                settings = JsonSerializer.Deserialize<MqttSettings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // An unreadable settings file is treated as no settings.
        }

        settings.Upgrade();
        return settings;
    }

    /// <summary>
    /// Moves the broker and page settings of an earlier version into the first client, and makes
    /// sure there is at least one client, each with a client ID.
    /// </summary>
    private void Upgrade()
    {
        Clients = Clients.Where(c => c is not null).ToList();
        if (Clients.Count == 0)
        {
            Clients.Add(new ClientProfile
            {
                Name = FirstClientName,
                Host = Host ?? "localhost",
                Port = string.IsNullOrEmpty(Port) ? "1883" : Port,
                Username = Username ?? "",
                ClientId = ClientId ?? "",
                Folder = Folder,
                ManualTopic = ManualTopic ?? "",
                ManualBody = ManualBody ?? "",
                ManualQos = ManualQos ?? 0,
                ManualRetain = ManualRetain ?? false,
                ExplorerFilters = string.IsNullOrEmpty(ExplorerFilters) ? "#" : ExplorerFilters,
            });
        }

        Host = Port = Username = ClientId = Folder = ManualTopic = ManualBody = ExplorerFilters = null;
        ManualQos = null;
        ManualRetain = null;
        foreach (var client in Clients.Where(c => c.ClientId.Length == 0))
            client.ClientId = ClientProfile.NewClientId();
    }

    /// <summary>The server for alerts and messages, set up for <c>localhost</c> the first time. Its name is not one a client already has.</summary>
    public ClientProfile MessagingServer()
    {
        Messaging ??= new();
        var server = Messaging.Server ??= new ClientProfile { Name = UnusedServerName(), Host = "localhost" };
        if (server.ClientId.Length == 0)
            server.ClientId = ClientProfile.NewClientId();
        return server;
    }

    private string UnusedServerName()
    {
        var name = MessagingSettings.DefaultServerName;
        for (var i = 2; Clients.Exists(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)); i++)
            name = $"{MessagingSettings.DefaultServerName} {i}";
        return name;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonFormats.Indented));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Settings that cannot be saved are simply not remembered.
        }
    }
}
