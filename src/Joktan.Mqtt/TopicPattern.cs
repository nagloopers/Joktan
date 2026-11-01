using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Joktan.Mqtt.Resources;

namespace Joktan.Mqtt;

/// <summary>A level of a topic pattern that varies, and so says something about each message.</summary>
/// <param name="Level">Its index in the topic, counting from 0.</param>
/// <param name="ToEnd">Whether it holds the rest of the topic, as <c>#</c> does.</param>
public sealed record TopicKey(string Name, int Level, bool ToEnd);

/// <summary>
/// A topic to subscribe to, in which <c>{line}</c> is a <c>+</c> called line, <c>{area#}</c> is a <c>#</c>
/// called area, and <c>\{</c> is a real brace. Every wildcard is a key, whether it has a name or not.
/// </summary>
public sealed partial class TopicPattern
{
    private TopicPattern(string text, string filter, IReadOnlyList<TopicKey> keys)
    {
        Text = text;
        Filter = filter;
        Keys = keys;
    }

    /// <summary>The pattern as written.</summary>
    public string Text { get; }

    /// <summary>What is subscribed to.</summary>
    public string Filter { get; }

    public IReadOnlyList<TopicKey> Keys { get; }

    public bool IsExact => Keys.Count == 0;

    /// <summary>Returns why a pattern cannot be used, or null if it can.</summary>
    public static string? Validate(string? text) => TryParse(text, out _, out var problem) ? null : problem;

    /// <summary>The pattern, or null if it cannot be used.</summary>
    public static TopicPattern? Parse(string? text) => TryParse(text, out var pattern, out _) ? pattern : null;

    public bool Matches(string topic) => TopicRules.Matches(Filter, topic);

    /// <summary>The value of each key in a topic this pattern matches, in the order of the topic.</summary>
    public Dictionary<string, string> KeyValues(string topic)
    {
        var levels = topic.Split('/');
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in Keys)
        {
            values[key.Name] = key.Level >= levels.Length ? ""
                : key.ToEnd ? string.Join('/', levels[key.Level..])
                : levels[key.Level];
        }

        return values;
    }

    /// <summary>A name for a key at a level holding this value: line1 and line2 suggest line.</summary>
    public static string SuggestName(string value, int level, IEnumerable<string> taken)
    {
        var name = TrailingNumber().Replace(value, "");
        if (!NameRule().IsMatch(name))
            name = $"level{level}";

        var used = taken.ToHashSet(StringComparer.Ordinal);
        var unique = name;
        for (var i = 2; used.Contains(unique); i++)
            unique = $"{name}{i}";
        return unique;
    }

    private static bool TryParse(string? text, out TopicPattern? pattern, out string? problem)
    {
        pattern = null;
        problem = null;
        if (string.IsNullOrEmpty(text))
        {
            problem = Strings.TopicPattern_TheTopicIsEmpty;
            return false;
        }

        var levels = text.Split('/');
        var filter = new string[levels.Length];
        var keys = new List<TopicKey>();
        for (var i = 0; i < levels.Length; i++)
        {
            var level = levels[i];
            if (level.StartsWith("\\{", StringComparison.Ordinal))
            {
                filter[i] = level[1..];
            }
            else if (level is "+" or "#")
            {
                filter[i] = level;
                keys.Add(new TopicKey($"level{i}", i, level == "#"));
            }
            else if (level.StartsWith('{') && level.EndsWith('}'))
            {
                var toEnd = level.EndsWith("#}", StringComparison.Ordinal);
                var name = level[1..^(toEnd ? 2 : 1)];
                if (!NameRule().IsMatch(name))
                {
                    problem = string.Format(Strings.TopicPattern_NeedsANameThatStartsWith, level);
                    return false;
                }

                if (keys.Any(k => k.Name == name))
                {
                    problem = string.Format(Strings.TopicPattern_IsUsedTwiceInTheTopic, name);
                    return false;
                }

                filter[i] = toEnd ? "#" : "+";
                keys.Add(new TopicKey(name, i, toEnd));
            }
            else if (level.Contains('{'))
            {
                problem = string.Format(Strings.TopicPattern_InAKeyMustBeA, level);
                return false;
            }
            else
            {
                filter[i] = level;
            }
        }

        var joined = string.Join('/', filter);
        problem = TopicRules.ValidateFilter(joined);
        if (problem is not null)
            return false;

        pattern = new TopicPattern(text, joined, keys);
        return true;
    }

    [GeneratedRegex(@"^[A-Za-z_][\w-]*$")]
    private static partial Regex NameRule();

    [GeneratedRegex(@"[-_ ]?\d+$")]
    private static partial Regex TrailingNumber();
}

/// <summary>Filters that a topic can be checked against quickly, as most have no wildcards.</summary>
public sealed class TopicFilterSet
{
    private readonly HashSet<string> _exact = new(StringComparer.Ordinal);
    private readonly List<string> _wildcards = [];

    public TopicFilterSet(IEnumerable<string> filters)
    {
        foreach (var filter in filters.Distinct())
        {
            if (filter.Contains('+') || filter.Contains('#'))
                _wildcards.Add(filter);
            else
                _exact.Add(filter);
        }
    }

    public int Count => _exact.Count + _wildcards.Count;

    public bool Contains(string topic)
    {
        if (_exact.Contains(topic))
            return true;

        foreach (var filter in _wildcards)
        {
            if (TopicRules.Matches(filter, topic))
                return true;
        }

        return false;
    }
}
