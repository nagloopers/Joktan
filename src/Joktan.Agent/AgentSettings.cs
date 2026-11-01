using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Joktan.Core;

namespace Joktan.Agent;

/// <summary>The command-line agents the panel can run.</summary>
public enum AgentClientKind
{
    ClaudeCode,
    OpenCode,
    Copilot,
    Custom,
}

/// <summary>How one command-line agent is run.</summary>
public sealed class AgentClientProfile
{
    /// <summary>The program, when it is not found by name in the usual folders.</summary>
    public string Path { get; set; } = "";

    /// <summary>The model to ask for; empty leaves the choice to the client.</summary>
    public string Model { get; set; } = "";

    /// <summary>More arguments for the command line, split on spaces, with double quotes around any that hold one.</summary>
    public string ExtraArguments { get; set; } = "";
}

/// <summary>Settings / Agent, and where the panel was left. The token for outside clients is kept in the credential store.</summary>
public sealed class AgentSettings
{
    public const int DefaultOutsidePort = 47820;

    [JsonConverter(typeof(JsonStringEnumConverter<AgentClientKind>))]
    public AgentClientKind Client { get; set; } = AgentClientKind.ClaudeCode;

    public Dictionary<string, AgentClientProfile> Profiles { get; set; } = [];

    /// <summary>The arguments of the custom client, with {prompt}, {mcp_config}, {mcp_url} and {mcp_token} filled in.</summary>
    public string CustomArguments { get; set; } = "{prompt}";

    /// <summary>Whether the agent may also use the client's own tools, such as its shell and file tools, which Joktan cannot ask about.</summary>
    public bool AllowClientTools { get; set; }

    /// <summary>Whether clients from outside the app may connect.</summary>
    public bool OutsideEnabled { get; set; }

    public int OutsidePort { get; set; } = DefaultOutsidePort;

    public bool PanelOpen { get; set; }

    public bool PanelFloating { get; set; }

    public double PanelWidth { get; set; } = 420;

    public double WindowWidth { get; set; } = 480;

    public double WindowHeight { get; set; } = 720;

    private static string FilePath => System.IO.Path.Combine(DataFolder.Path, "agent.json");

    public AgentClientProfile ProfileOf(AgentClientKind kind)
    {
        if (!Profiles.TryGetValue(kind.ToString(), out var profile))
            Profiles[kind.ToString()] = profile = new AgentClientProfile();
        return profile;
    }

    public static AgentSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AgentSettings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // Unreadable settings start over; nothing depends on them but the panel.
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
            // Settings that cannot be saved are used until the app closes.
        }
    }
}
