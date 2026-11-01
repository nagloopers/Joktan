using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Joktan.Agent.Resources;
using Joktan.Core;

namespace Joktan.Agent.Clients;

/// <summary>What one turn of the conversation asks a client to do.</summary>
/// <param name="Prompt">What the user said.</param>
/// <param name="SessionId">The client's session to carry on, if it gave one.</param>
/// <param name="McpUrl">Where the app's MCP server is.</param>
/// <param name="McpToken">The token it wants.</param>
/// <param name="Folder">A private folder for this turn's files, removed afterwards.</param>
public sealed record AgentTurn(string Prompt, string? SessionId, string McpUrl, string McpToken, string Folder, AgentClientProfile Profile, bool AllowClientTools);

/// <summary>How to start a client for one turn.</summary>
public sealed class AgentLaunch
{
    public List<string> Arguments { get; } = [];

    public Dictionary<string, string> Environment { get; } = [];

    /// <summary>Written to the program's input, which keeps it off the process list.</summary>
    public string? Input { get; init; }
}

/// <summary>Receives what a client says, as it says it.</summary>
public interface IAgentOutput
{
    void Text(string text);

    void Session(string id);

    void Problem(string message);
}

/// <summary>
/// One command-line agent: how to start it and how to read what it prints. Tool calls are not read from its
/// output, because the app's own server sees those whichever client makes them.
/// </summary>
public abstract class AgentClient
{
    public abstract AgentClientKind Kind { get; }

    public abstract string DisplayName { get; }

    /// <summary>The name of its program, without any extension.</summary>
    public abstract string ProgramName { get; }

    public abstract string ModelHint { get; }

    public abstract string InstallHint { get; }

    public abstract AgentLaunch Prepare(AgentTurn turn);

    /// <summary>Reads one line of the program's output.</summary>
    public abstract void Read(string line, IAgentOutput output);

    public override string ToString() => DisplayName;

    public static IReadOnlyList<AgentClient> All { get; } = [new ClaudeCodeClient(), new OpenCodeClient(), new CopilotClient(), new CustomClient()];

    public static AgentClient Of(AgentClientKind kind) => All.First(c => c.Kind == kind);

    protected static string WriteFile(AgentTurn turn, string name, JsonNode content)
    {
        var path = Path.Combine(turn.Folder, name);
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        // No byte order mark: Copilot's JSON parser rejects one as invalid.
        using var writer = new StreamWriter(path, new UTF8Encoding(false), options);
        writer.Write(content.ToJsonString(JsonFormats.Compact));
        return path;
    }

    /// <summary>The usual shape of an MCP configuration, naming the app's server.</summary>
    protected static JsonObject McpServers(AgentTurn turn, Action<JsonObject>? more = null)
    {
        var server = new JsonObject
        {
            ["type"] = "http",
            ["url"] = turn.McpUrl,
            ["headers"] = new JsonObject { ["Authorization"] = $"Bearer {turn.McpToken}" },
        };
        more?.Invoke(server);
        return new JsonObject { ["mcpServers"] = new JsonObject { ["joktan"] = server } };
    }

    protected static void AddExtra(AgentLaunch launch, AgentTurn turn)
        => launch.Arguments.AddRange(SplitArguments(turn.Profile.ExtraArguments));

    /// <summary>Splits on spaces, keeping what double quotes hold together.</summary>
    public static List<string> SplitArguments(string text)
    {
        var arguments = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        var any = false;
        foreach (var c in text)
        {
            if (c == '"')
            {
                quoted = !quoted;
                any = true;
            }
            else if (char.IsWhiteSpace(c) && !quoted)
            {
                if (any || current.Length > 0)
                    arguments.Add(current.ToString());
                current.Clear();
                any = false;
            }
            else
            {
                current.Append(c);
            }
        }

        if (any || current.Length > 0)
            arguments.Add(current.ToString());
        return arguments;
    }

    protected static JsonElement? Parse(string line)
    {
        if (!line.StartsWith('{'))
            return null;
        try
        {
            using var document = JsonDocument.Parse(line);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    protected static string? TextOf(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    protected static JsonElement ChildOf(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : default;
}

/// <summary>
/// Claude Code, through <c>claude -p</c> and its stream of JSON. The prompt goes in on standard input.
/// Text comes as deltas, then again whole at the end of a message, which is ignored for that reason.
/// </summary>
public sealed class ClaudeCodeClient : AgentClient
{
    public override AgentClientKind Kind => AgentClientKind.ClaudeCode;

    public override string DisplayName => "Claude Code";

    public override string ProgramName => "claude";

    public override string ModelHint => Strings.ClaudeCodeClient_SuchAsSonnetOrOpusEmpty;

    public override string InstallHint => Strings.ClaudeCodeClient_InstallItFromHttpsClaudeCom;

    public override AgentLaunch Prepare(AgentTurn turn)
    {
        var launch = new AgentLaunch { Input = turn.Prompt };
        launch.Arguments.AddRange(["-p", "--output-format", "stream-json", "--verbose", "--include-partial-messages"]);
        launch.Arguments.AddRange(["--mcp-config", WriteFile(turn, "mcp.json", McpServers(turn)), "--strict-mcp-config", "--allowedTools", "mcp__joktan"]);
        if (!turn.AllowClientTools)
            launch.Arguments.AddRange(["--tools", ""]);
        if (turn.SessionId is { } session)
            launch.Arguments.AddRange(["--resume", session]);
        if (turn.Profile.Model.Trim() is { Length: > 0 } model)
            launch.Arguments.AddRange(["--model", model]);
        AddExtra(launch, turn);
        return launch;
    }

    public override void Read(string line, IAgentOutput output)
    {
        if (Parse(line) is not { } message)
            return;

        if (TextOf(message, "session_id") is { Length: > 0 } session)
            output.Session(session);

        switch (TextOf(message, "type"))
        {
            case "stream_event":
                var delta = ChildOf(ChildOf(message, "event"), "delta");
                if (TextOf(delta, "type") == "text_delta" && TextOf(delta, "text") is { } text)
                    output.Text(text);
                break;
            case "result" when ChildOf(message, "is_error").ValueKind == JsonValueKind.True:
                output.Problem(TextOf(message, "result") ?? TextOf(message, "subtype") ?? Strings.ClaudeCodeClient_ClaudeCodeReportedAnError);
                break;
        }
    }
}

/// <summary>
/// OpenCode, through <c>opencode run</c>. A private server is asked for, as the background one would not
/// see the configuration passed here. That configuration also says which tools it may use.
/// </summary>
public sealed class OpenCodeClient : AgentClient
{
    public override AgentClientKind Kind => AgentClientKind.OpenCode;

    public override string DisplayName => "OpenCode";

    public override string ProgramName => "opencode";

    public override string ModelHint => Strings.OpenCodeClient_AsProviderModelSuchAsLmstudio;

    public override string InstallHint => Strings.OpenCodeClient_InstallItFromHttpsOpencodeAi;

    public override AgentLaunch Prepare(AgentTurn turn)
    {
        var configuration = new JsonObject
        {
            ["mcp"] = new JsonObject
            {
                ["joktan"] = new JsonObject
                {
                    ["type"] = "remote",
                    ["url"] = turn.McpUrl,
                    ["enabled"] = true,
                    ["oauth"] = false,
                    // Long enough for the user to answer an approval.
                    ["timeout"] = 600_000,
                    ["headers"] = new JsonObject { ["Authorization"] = $"Bearer {turn.McpToken}" },
                },
            },
        };
        configuration["permission"] = turn.AllowClientTools
            ? new JsonObject { ["joktan_*"] = "allow" }
            : new JsonObject { ["*"] = "deny", ["joktan_*"] = "allow" };

        var launch = new AgentLaunch();
        launch.Environment["OPENCODE_CONFIG"] = WriteFile(turn, "opencode.json", configuration);
        launch.Arguments.AddRange(["run", "--standalone", "--format", "json"]);
        if (turn.SessionId is { } session)
            launch.Arguments.AddRange(["--session", session]);
        if (turn.Profile.Model.Trim() is { Length: > 0 } model)
            launch.Arguments.AddRange(["--model", model]);
        AddExtra(launch, turn);
        launch.Arguments.AddRange(["--", turn.Prompt]);
        return launch;
    }

    public override void Read(string line, IAgentOutput output)
    {
        if (Parse(line) is not { } message)
            return;

        if (TextOf(message, "sessionID") is { Length: > 0 } session)
            output.Session(session);

        switch (TextOf(message, "type"))
        {
            case "text" when TextOf(ChildOf(message, "part"), "text") is { } text:
                output.Text(text);
                break;
            case "error":
                var error = ChildOf(message, "error");
                output.Problem(TextOf(error, "message") ?? TextOf(ChildOf(error, "data"), "message") ?? Strings.OpenCodeClient_OpenCodeReportedAnError);
                break;
        }
    }
}

/// <summary>
/// GitHub Copilot CLI, through <c>copilot -p</c> with JSON lines. Only its flags are documented, so the
/// event names read here are a best reading. Any line that is not JSON is shown as it is.
/// </summary>
public sealed class CopilotClient : AgentClient
{
    public override AgentClientKind Kind => AgentClientKind.Copilot;

    public override string DisplayName => "GitHub Copilot CLI";

    public override string ProgramName => "copilot";

    public override string ModelHint => Strings.CopilotClient_SuchAsAutoEmptyUsesThe;

    public override string InstallHint => Strings.CopilotClient_InstallItWithNpmInstallG;

    private bool _sawDelta;

    public override AgentLaunch Prepare(AgentTurn turn)
    {
        _sawDelta = false;
        var configuration = McpServers(turn, server => server["tools"] = new JsonArray("*"));
        var launch = new AgentLaunch();
        launch.Arguments.AddRange([$"--prompt={turn.Prompt}", "--output-format=json", "--no-ask-user", "--allow-tool=joktan"]);
        launch.Arguments.Add($"--additional-mcp-config=@{WriteFile(turn, "mcp.json", configuration)}");
        if (turn.SessionId is { } session)
            launch.Arguments.Add($"--resume={session}");
        if (turn.Profile.Model.Trim() is { Length: > 0 } model)
            launch.Arguments.Add($"--model={model}");
        AddExtra(launch, turn);
        return launch;
    }

    public override void Read(string line, IAgentOutput output)
    {
        if (Parse(line) is not { } message)
        {
            if (line.Trim().Length > 0)
                output.Text(line + "\n");
            return;
        }

        var data = ChildOf(message, "data");
        if ((TextOf(message, "sessionId") ?? TextOf(data, "sessionId")) is { Length: > 0 } session)
            output.Session(session);

        switch (TextOf(message, "type"))
        {
            case "assistant.message_delta" when (TextOf(data, "deltaContent") ?? TextOf(data, "delta")) is { } delta:
                _sawDelta = true;
                output.Text(delta);
                break;
            case "assistant.message" when !_sawDelta && TextOf(data, "content") is { Length: > 0 } content:
                output.Text(content);
                break;
            case "assistant.message":
                _sawDelta = false;
                break;
            case "session.error" or "error":
                output.Problem(TextOf(data, "message") ?? TextOf(message, "message") ?? Strings.CopilotClient_CopilotReportedAnError);
                break;
        }
    }
}

/// <summary>Any other program. Its arguments come from the settings, and whatever it prints is the reply.</summary>
public sealed class CustomClient : AgentClient
{
    /// <summary>Set from the settings before a turn is prepared.</summary>
    public string Arguments { get; set; } = "{prompt}";

    public override AgentClientKind Kind => AgentClientKind.Custom;

    public override string DisplayName => Strings.CustomClient_CustomCommand;

    public override string ProgramName => "";

    public override string ModelHint => Strings.CustomClient_FillsInModelInTheArguments;

    public override string InstallHint => Strings.CustomClient_GiveTheWholePathOfThe;

    public override AgentLaunch Prepare(AgentTurn turn)
    {
        var configuration = WriteFile(turn, "mcp.json", McpServers(turn));
        var launch = new AgentLaunch();

        // Split before filling in, so a prompt with spaces stays one argument and cannot add others.
        foreach (var argument in SplitArguments(Arguments))
            launch.Arguments.Add(argument
                .Replace("{prompt}", turn.Prompt)
                .Replace("{mcp_config}", configuration)
                .Replace("{mcp_url}", turn.McpUrl)
                .Replace("{mcp_token}", turn.McpToken)
                .Replace("{model}", turn.Profile.Model.Trim()));
        AddExtra(launch, turn);
        return launch;
    }

    public override void Read(string line, IAgentOutput output) => output.Text(line + "\n");
}
