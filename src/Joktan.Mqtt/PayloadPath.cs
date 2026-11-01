using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using Joktan.Mqtt.Resources;

namespace Joktan.Mqtt;

/// <summary>The numbers a path found under one set of key values.</summary>
public sealed record PathRow(IReadOnlyDictionary<string, string> Keys, IReadOnlyList<double> Values);

/// <summary>
/// Where a number is in a JSON payload. As well as the paths of <see cref="JsonNumbers"/>, an array item can
/// be chosen by a field rather than its position: <c>readings[sensor=temperature].value</c> for one,
/// <c>readings[sensor=*].value</c> for each. <c>{line}</c> or <c>{2}</c> stands for what a level of the
/// topic holds, by name or index: <c>readings[sensor={2}].value</c>, <c>{line}.value</c>.
/// </summary>
public sealed class PayloadPath
{
    private readonly List<Step> _steps = [];

    private PayloadPath(string text) => Text = text;

    public string Text { get; }

    /// <summary>Why the path cannot be read, or null.</summary>
    public string? Error { get; private set; }

    /// <summary>Whether it is a path <see cref="JsonNumbers"/> finds, with no keys or links.</summary>
    public bool IsPlain => _steps.All(s => s is not MatchStep && s is not NameStep { IsLink: true });

    /// <summary>The fields that give a value per item, as for <c>[sensor=*]</c>.</summary>
    public IReadOnlyList<string> KeyNames => _steps.OfType<MatchStep>()
        .SelectMany(s => s.Conditions)
        .Where(c => c.IsAny)
        .Select(c => c.Field)
        .Distinct()
        .ToList();

    private IEnumerable<string> Links => _steps.OfType<NameStep>().Where(s => s.IsLink).Select(s => s.Name)
        .Concat(_steps.OfType<MatchStep>().SelectMany(s => s.Conditions).Where(c => c.IsLink).Select(c => c.Value));

    public static PayloadPath Parse(string text)
    {
        var path = new PayloadPath(text);
        path.Error = path.ReadSteps();
        return path;
    }

    /// <summary>Returns why the path cannot be used with a topic, or null if it can.</summary>
    public string? ProblemWith(TopicPattern pattern)
    {
        if (Error is not null)
            return Error;

        foreach (var link in Links)
        {
            if (int.TryParse(link, NumberStyles.None, CultureInfo.InvariantCulture, out var level))
            {
                if (pattern.Keys.All(k => k.Level != level))
                    return string.Format(Strings.PayloadPath_MustBeTheIndexOfA, link, pattern.Text);
            }
            else if (pattern.Keys.All(k => k.Name != link))
            {
                return string.Format(Strings.PayloadPath_IsNotAKeyOf, link, pattern.Text);
            }
        }

        return null;
    }

    /// <summary>The numbers at this path, under the key values that tell them apart.</summary>
    /// <param name="link">What a link stands for, or null if it stands for nothing.</param>
    public IReadOnlyList<PathRow> Read(JsonElement root, Func<string, string?> link)
    {
        if (Error is not null)
            return [];

        var rows = new Dictionary<string, (Dictionary<string, string> Keys, List<double> Values)>(StringComparer.Ordinal);
        Walk(root, 0, [], link, rows);
        return rows.Values.Select(r => new PathRow(r.Keys, r.Values)).ToList();
    }

    /// <summary>The text of a field that holds text, a number, or true or false; null if it holds anything else.</summary>
    public static string? ScalarAt(JsonElement element, string field)
    {
        foreach (var name in field.Split('.'))
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out element))
                return null;
        }

        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null,
        };
    }

    /// <summary>A value as it is written in a path, in quotes if it has to be.</summary>
    public static string Quote(string value)
        => value.Length > 0 && value.All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' or ':') && value != "*"
            ? value
            : $"\"{value}\"";

    private void Walk(JsonElement element, int index, List<KeyValuePair<string, string>> keys, Func<string, string?> link,
        Dictionary<string, (Dictionary<string, string> Keys, List<double> Values)> rows)
    {
        if (index == _steps.Count)
        {
            if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var number) && double.IsFinite(number))
                AddRow(number, keys, rows);
            return;
        }

        switch (_steps[index])
        {
            case NameStep step:
                var name = step.IsLink ? link(step.Name) : step.Name;
                if (name is not null && element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var property))
                    Walk(property, index + 1, keys, link, rows);
                break;

            case EveryStep when element.ValueKind == JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    Walk(item, index + 1, keys, link, rows);
                break;

            case PositionStep position when element.ValueKind == JsonValueKind.Array && position.Index < element.GetArrayLength():
                Walk(element[position.Index], index + 1, keys, link, rows);
                break;

            case MatchStep match when element.ValueKind == JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    var added = 0;
                    if (MatchesItem(item, match, keys, link, ref added))
                        Walk(item, index + 1, keys, link, rows);
                    keys.RemoveRange(keys.Count - added, added);
                }

                break;
        }
    }

    private static bool MatchesItem(JsonElement item, MatchStep match, List<KeyValuePair<string, string>> keys, Func<string, string?> link, ref int added)
    {
        foreach (var condition in match.Conditions)
        {
            if (ScalarAt(item, condition.Field) is not { } value)
                return false;

            if (condition.IsAny)
            {
                keys.Add(new KeyValuePair<string, string>(condition.Field, value));
                added++;
            }
            else if (value != (condition.IsLink ? link(condition.Value) : condition.Value))
            {
                return false;
            }
        }

        return true;
    }

    private static void AddRow(double number, List<KeyValuePair<string, string>> keys, Dictionary<string, (Dictionary<string, string> Keys, List<double> Values)> rows)
    {
        var id = string.Join('\u001f', keys.Select(k => $"{k.Key}={k.Value}"));
        if (!rows.TryGetValue(id, out var row))
            rows[id] = row = (new Dictionary<string, string>(keys, StringComparer.Ordinal), []);

        row.Values.Add(number);
        if (row.Values.Count > JsonNumbers.MaxValuesPerField)
            row.Values.RemoveAt(0);
    }

    private string? ReadSteps()
    {
        var at = 0;
        while (at < Text.Length)
        {
            if (Text[at] == '.')
            {
                at++;
                continue;
            }

            if (Text[at] == '[')
            {
                var end = ClosingBracket(at);
                if (end < 0)
                    return string.Format(Strings.PayloadPath_HasAWithNo, Text);

                if (ReadSelector(Text[(at + 1)..end]) is { } problem)
                    return problem;
                at = end + 1;
            }
            else if (Text[at] == '{')
            {
                var end = Text.IndexOf('}', at);
                if (end < 0)
                    return string.Format(Strings.PayloadPath_HasAWithNo2, Text);

                _steps.Add(new NameStep(Text[(at + 1)..end], IsLink: true));
                at = end + 1;
            }
            else
            {
                var end = at;
                while (end < Text.Length && Text[end] is not ('.' or '['))
                    end++;
                _steps.Add(new NameStep(Text[at..end], IsLink: false));
                at = end;
            }
        }

        return null;
    }

    private int ClosingBracket(int from)
    {
        var quoted = false;
        for (var i = from + 1; i < Text.Length; i++)
        {
            if (Text[i] == '"')
                quoted = !quoted;
            else if (Text[i] == ']' && !quoted)
                return i;
        }

        return -1;
    }

    private string? ReadSelector(string selector)
    {
        if (selector == "*")
        {
            _steps.Add(new EveryStep());
            return null;
        }

        if (int.TryParse(selector, NumberStyles.None, CultureInfo.InvariantCulture, out var index))
        {
            _steps.Add(new PositionStep(index));
            return null;
        }

        if (selector.StartsWith('{'))
            return string.Format(Strings.PayloadPath_DependsOnTheOrderOfThe, selector, selector);

        var conditions = new List<Condition>();
        foreach (var part in SplitConditions(selector))
        {
            var equals = part.IndexOf('=');
            if (equals <= 0)
                return string.Format(Strings.PayloadPath_IsNotUnderstoodUseAPosition, selector);

            var field = part[..equals].Trim();
            var value = part[(equals + 1)..].Trim();
            if (value == "*")
                conditions.Add(new Condition(field, "", IsAny: true, IsLink: false));
            else if (value.StartsWith('{') && value.EndsWith('}'))
                conditions.Add(new Condition(field, value[1..^1], IsAny: false, IsLink: true));
            else
                conditions.Add(new Condition(field, value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : value, IsAny: false, IsLink: false));
        }

        _steps.Add(new MatchStep(conditions));
        return null;
    }

    private static IEnumerable<string> SplitConditions(string selector)
    {
        var part = new StringBuilder();
        var quoted = false;
        foreach (var c in selector)
        {
            if (c == '"')
                quoted = !quoted;
            if (c == ',' && !quoted)
            {
                yield return part.ToString();
                part.Clear();
            }
            else
            {
                part.Append(c);
            }
        }

        yield return part.ToString();
    }

    private abstract record Step;

    private sealed record NameStep(string Name, bool IsLink) : Step;

    private sealed record EveryStep : Step;

    private sealed record PositionStep(int Index) : Step;

    private sealed record MatchStep(IReadOnlyList<Condition> Conditions) : Step;

    private sealed record Condition(string Field, string Value, bool IsAny, bool IsLink);
}
