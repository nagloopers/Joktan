using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Joktan.Agent.Mcp;

/// <summary>What a tool can do, which decides whether it needs the user's approval.</summary>
public enum ToolRisk
{
    /// <summary>Only looks. Runs without asking.</summary>
    Read,

    /// <summary>An ordinary edit, as easily undone as it was made. Runs without asking.</summary>
    Change,

    /// <summary>Deletes, restores, runs containers or sends traffic out. Asks, and can be allowed for the conversation.</summary>
    Risky,

    /// <summary>Hands a stored secret to the agent. Asks every time.</summary>
    Secret,
}

/// <summary>Why a tool could not do what it was asked, in words for the agent to read.</summary>
public sealed class ToolFailure(string message) : Exception(message);

public sealed record ToolParameter(string Name, string Type, string Description, bool Required, IReadOnlyList<string>? Choices = null);

/// <summary>Shorthand for describing what a tool takes.</summary>
public static class P
{
    public static ToolParameter Text(string name, string description) => new(name, "string", description, true);

    public static ToolParameter OptText(string name, string description) => new(name, "string", description, false);

    public static ToolParameter Number(string name, string description) => new(name, "number", description, true);

    public static ToolParameter OptNumber(string name, string description) => new(name, "number", description, false);

    public static ToolParameter Flag(string name, string description) => new(name, "boolean", description, true);

    public static ToolParameter OptFlag(string name, string description) => new(name, "boolean", description, false);

    public static ToolParameter Choice(string name, string description, params string[] choices) => new(name, "string", description, true, choices);

    public static ToolParameter OptChoice(string name, string description, params string[] choices) => new(name, "string", description, false, choices);

    /// <summary>An object of names and text values, such as settings to change.</summary>
    public static ToolParameter Map(string name, string description) => new(name, "object", description, false);
}

/// <summary>One thing the agent can do in the app.</summary>
public sealed class McpTool(string name, string description, ToolRisk risk, IReadOnlyList<ToolParameter> parameters, Func<ToolArgs, Task<object?>> run)
{
    public string Name { get; } = name;

    public string Description { get; } = description;

    public ToolRisk Risk { get; } = risk;

    public IReadOnlyList<ToolParameter> Parameters { get; } = parameters;

    public Task<object?> RunAsync(ToolArgs args) => run(args);

    public static McpTool Sync(string name, string description, ToolRisk risk, IReadOnlyList<ToolParameter> parameters, Func<ToolArgs, object?> run)
        => new(name, description, risk, parameters, args => Task.FromResult(run(args)));

    public JsonObject Definition()
    {
        var properties = new JsonObject();
        foreach (var parameter in Parameters)
        {
            var property = new JsonObject { ["type"] = parameter.Type, ["description"] = parameter.Description };
            if (parameter.Choices is { } choices)
                property["enum"] = new JsonArray(choices.Select(c => (JsonNode?)JsonValue.Create(c)).ToArray());
            properties[parameter.Name] = property;
        }

        return new JsonObject
        {
            ["name"] = Name,
            ["description"] = Description,
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["required"] = new JsonArray(Parameters.Where(p => p.Required).Select(p => (JsonNode?)JsonValue.Create(p.Name)).ToArray()),
            },
        };
    }
}

/// <summary>What a tool was called with.</summary>
public sealed class ToolArgs(JsonElement arguments)
{
    public JsonElement Raw { get; } = arguments;

    public string Text(string name) => OptText(name) ?? throw new ToolFailure($"{name} is required.");

    public string? OptText(string name)
    {
        if (!TryGet(name, out var value))
            return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
    }

    public bool? Flag(string name)
    {
        if (!TryGet(name, out var value))
            return null;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            _ => throw new ToolFailure($"{name} must be true or false."),
        };
    }

    public double? Number(string name)
    {
        if (!TryGet(name, out var value))
            return null;
        if (value.ValueKind == JsonValueKind.Number)
            return value.GetDouble();
        if (value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        throw new ToolFailure($"{name} must be a number.");
    }

    public int? Whole(string name) => Number(name) is { } number ? (int)number : null;

    /// <summary>The names and values of an object, as text. Empty when it was not given.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Map(string name)
    {
        if (!TryGet(name, out var value))
            return [];
        if (value.ValueKind != JsonValueKind.Object)
            throw new ToolFailure($"{name} must be an object of names and values.");
        return value.EnumerateObject()
            .Select(p => new KeyValuePair<string, string>(p.Name, p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? "" : p.Value.GetRawText()))
            .ToList();
    }

    private bool TryGet(string name, out JsonElement value)
    {
        value = default;
        return Raw.ValueKind == JsonValueKind.Object && Raw.TryGetProperty(name, out value) && value.ValueKind != JsonValueKind.Null;
    }
}
