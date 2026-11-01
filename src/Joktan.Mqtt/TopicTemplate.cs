using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Joktan.Mqtt.Resources;

namespace Joktan.Mqtt;

/// <summary>
/// A topic with parts that change on every send, written as {name}, for example
/// "devices/{device}/heartbeat". Each name is filled in by the topic field of the same name.
/// </summary>
public static partial class TopicTemplate
{
    /// <summary>Stands for the folder path in a topic set in the settings file.</summary>
    public const string FolderName = "folder";

    // Topic field positions are kept beside the body's counters, under a key no body path uses.
    private const string CounterPrefix = "{topic}";

    [GeneratedRegex(@"\{([A-Za-z0-9_.-]+)\}")]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"^[A-Za-z0-9_.-]+$")]
    private static partial Regex Name();

    /// <summary>Whether a topic field may be called <paramref name="name"/>.</summary>
    public static bool IsValidName(string name)
        => name != FolderName && Name().IsMatch(name);

    /// <summary>The names used in a topic, in order of first use.</summary>
    public static IReadOnlyList<string> Names(string template)
        => Placeholder().Matches(template).Select(m => m.Groups[1].Value).Distinct().ToList();

    /// <summary>
    /// The topic a message is sent to before its fields are filled in: the settings' topic with
    /// {folder} replaced by the folder path, or the folder path itself.
    /// </summary>
    public static string Effective(string folder, string topic)
        => topic.Length == 0 ? folder : topic.Replace($"{{{FolderName}}}", folder);

    /// <summary>Returns why a topic and its fields cannot be used, or null.</summary>
    public static string? Check(string template, IReadOnlyList<FieldRule> rules)
    {
        if (template.Length == 0)
            return Strings.TopicTemplate_TheTopicIsEmpty;

        var names = Names(template);
        if (names.Contains(FolderName))
            return Strings.TopicTemplate_FolderCanOnlyBeUsedIn;
        if (names.FirstOrDefault(n => rules.All(r => r.Path != n)) is { } unfilled)
            return string.Format(Strings.TopicTemplate_TopicPartHasNoTopicField, unfilled);
        if (rules.FirstOrDefault(r => !names.Contains(r.Path)) is { } unused)
            return string.Format(Strings.TopicTemplate_TopicFieldIsNotUsedIn, unused.Path, unused.Path);

        // Check the fixed parts of the topic with every field standing in as plain text.
        return TopicRules.Validate(Placeholder().Replace(template, "x"));
    }

    /// <summary>Fills in every topic field, advancing counters and lists in <paramref name="counters"/>.</summary>
    public static string Resolve(
        string template, IReadOnlyList<FieldRule> rules, IDictionary<string, long> counters, DateTimeOffset now)
    {
        if (rules.Count == 0)
            return template;

        var values = rules.ToDictionary(r => r.Path, r => Text(VaryingFields.Generate(r, CounterPrefix + r.Path, counters, now)));
        return Placeholder().Replace(template, m => values.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);
    }

    // A string goes in without its quotes; anything else as its JSON text.
    private static string Text(JsonNode? value)
        => value is JsonValue v && v.TryGetValue<string>(out var s) ? s : value?.ToJsonString() ?? "null";
}
