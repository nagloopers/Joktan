using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Joktan.Certificates;
using Joktan.Mqtt.Resources;

namespace Joktan.Mqtt;

/// <summary>
/// Replaces fields of a JSON body with generated values. A field is addressed by its path from
/// the top of the body, for example "ts", "sensor.value" or "readings[0].ts". "[*]" stands for
/// every item of an array, so "readings[*].ts" is the "ts" of each reading.
/// </summary>
public static partial class VaryingFields
{
    /// <summary>Returns the first rule whose field is not in the body, or null.</summary>
    public static string? FindMissing(JsonNode body, IEnumerable<FieldRule> rules)
    {
        foreach (var rule in rules)
        {
            if (Locate(body, rule.Path).Count == 0)
                return rule.Path;
        }

        return null;
    }

    /// <summary>Returns why an array rule cannot be used on the body, or null.</summary>
    public static string? CheckArray(JsonNode body, ArrayRule rule)
    {
        var arrays = Locate(body, rule.Path).Select(Value).ToList();
        if (arrays.Count == 0)
            return string.Format(Strings.VaryingFields_ArrayIsNotInTheBody, rule.Path);
        if (arrays.Any(a => a is not JsonArray))
            return string.Format(Strings.VaryingFields_IsNotAnArray, rule.Path);
        if (rule.Max > 0 && arrays.Any(a => ((JsonArray)a!).Count == 0))
            return string.Format(Strings.VaryingFields_ArrayIsEmptyInTheFile, rule.Path);
        return null;
    }

    /// <summary>
    /// Returns a copy of the body with the array rules and then the field rules applied. An array
    /// is filled from the items written in the file, used in turn, so two items repeated to five
    /// give the first, second, first, second and first.
    /// </summary>
    public static JsonNode Apply(
        JsonNode body, IReadOnlyList<ArrayRule> arrays, IReadOnlyList<FieldRule> rules,
        IDictionary<string, long> counters, DateTimeOffset now)
    {
        var copy = body.DeepClone();
        foreach (var rule in arrays)
        {
            foreach (var place in Locate(copy, rule.Path))
            {
                if (Value(place) is not JsonArray array || array.Count == 0)
                    continue;

                var templates = array.Select(item => item?.DeepClone()).ToList();
                var count = rule.IsFixed ? rule.Min : Random.Shared.Next(rule.Min, rule.Max + 1);
                array.Clear();
                for (var i = 0; i < count; i++)
                    array.Add(templates[i % templates.Count]?.DeepClone());
            }
        }

        return Apply(copy, rules, counters, now);
    }

    /// <summary>
    /// Returns a copy of the body with every rule applied. Counters read and advance their
    /// position in <paramref name="counters"/>, keyed by field path. A rule with "[*]" makes a new
    /// value for each item it reaches, in order, so a counter counts up across the items.
    /// </summary>
    public static JsonNode Apply(
        JsonNode body, IReadOnlyList<FieldRule> rules, IDictionary<string, long> counters, DateTimeOffset now)
    {
        var copy = body.DeepClone();
        foreach (var rule in rules)
        {
            foreach (var (parent, key, index) in Locate(copy, rule.Path))
            {
                var value = Generate(rule, rule.Path, counters, now);
                if (parent is JsonObject o)
                    o[key!] = value;
                else
                    ((JsonArray)parent)[index] = value;
            }
        }

        return copy;
    }

    /// <summary>Finds the value at the first place the path reaches in the body.</summary>
    public static bool TryGetValue(JsonNode? body, string path, out JsonNode? value)
    {
        value = null;
        if (body is null || Locate(body, path) is not [var place, ..])
            return false;

        value = Value(place);
        return true;
    }

    /// <summary>Lists the path of every array in the body, with "[*]" for arrays inside arrays.</summary>
    public static IReadOnlyList<string> ListArrayPaths(JsonNode? body)
    {
        var paths = new List<string>();
        Walk(body, "");
        return paths.Select(p => ArrayIndex().Replace(p, "[*]")).Distinct().ToList();

        void Walk(JsonNode? node, string path)
        {
            switch (node)
            {
                case JsonObject o:
                    foreach (var (name, child) in o)
                        Walk(child, path.Length == 0 ? name : $"{path}.{name}");
                    break;
                case JsonArray a:
                    if (path.Length > 0)
                        paths.Add(path);
                    for (var i = 0; i < a.Count; i++)
                        Walk(a[i], $"{path}[{i}]");
                    break;
            }
        }
    }

    /// <summary>
    /// Lists the path of every value in the body that a rule could address, followed by the
    /// same paths with "[*]" for every item of their arrays.
    /// </summary>
    public static IReadOnlyList<string> ListPaths(JsonNode? body)
    {
        var paths = new List<string>();
        Walk(body, "");

        var everyItem = paths
            .Where(p => p.Contains('['))
            .Select(p => ArrayIndex().Replace(p, "[*]"))
            .Distinct()
            .ToList();
        return [.. paths, .. everyItem];

        void Walk(JsonNode? node, string path)
        {
            switch (node)
            {
                case JsonObject o:
                    foreach (var (name, child) in o)
                        Walk(child, path.Length == 0 ? name : $"{path}.{name}");
                    break;
                case JsonArray a:
                    for (var i = 0; i < a.Count; i++)
                        Walk(a[i], $"{path}[{i}]");
                    break;
                default:
                    if (path.Length > 0)
                        paths.Add(path);
                    break;
            }
        }
    }

    /// <summary>
    /// Makes the next value for a rule. Counters and "in turn" lists keep their position in
    /// <paramref name="counters"/> under <paramref name="key"/>.
    /// </summary>
    public static JsonNode? Generate(FieldRule rule, string key, IDictionary<string, long> counters, DateTimeOffset now)
    {
        switch (rule.Type)
        {
            case FieldType.UnixSeconds:
                return now.ToUnixTimeSeconds();

            case FieldType.UnixMilliseconds:
                return now.ToUnixTimeMilliseconds();

            case FieldType.IsoTimestamp:
                return now.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

            case FieldType.Guid:
                return Guid.NewGuid().ToString();

            case FieldType.Counter:
                var current = counters.TryGetValue(key, out var stored) ? stored : rule.Start;
                counters[key] = current + rule.Step;
                return current;

            case FieldType.RandomInteger:
                var min = (long)Math.Ceiling(rule.Min);
                var max = (long)Math.Floor(rule.Max);
                return max < min ? min : Random.Shared.NextInt64(min, max + 1);

            case FieldType.RandomNumber:
                var number = rule.Min + Random.Shared.NextDouble() * (rule.Max - rule.Min);
                return Math.Round(number, Math.Clamp(rule.Decimals, 0, 15));

            case FieldType.OneOf:
                return rule.Values.Count == 0
                    ? null
                    : rule.Values[Random.Shared.Next(rule.Values.Count)]?.DeepClone();

            case FieldType.Cycle:
                if (rule.Values.Count == 0)
                    return null;
                var position = counters.TryGetValue(key, out var next) ? next : 0;
                counters[key] = (position + 1) % rule.Values.Count;
                return rule.Values[(int)(position % rule.Values.Count)]?.DeepClone();

            case FieldType.Fixed:
                return rule.Value?.DeepClone();

            default:
                return null;
        }
    }

    // Finds every place the path reaches: the container holding each value, and the value's name
    // or index in it. A path without "[*]" reaches at most one place.
    private static List<(JsonNode Parent, string? Key, int Index)> Locate(JsonNode root, string path)
    {
        var found = new List<(JsonNode, string?, int)>();
        if (!TryParse(path, out var steps))
            return found;

        Visit(root, 0, null, null, -1);
        return found;

        void Visit(JsonNode? current, int step, JsonNode? parent, string? key, int index)
        {
            if (step == steps.Count)
            {
                if (parent is not null)
                    found.Add((parent, key, index));
                return;
            }

            switch (steps[step])
            {
                case { Name: { } name }:
                    if (current is JsonObject o && o.ContainsKey(name))
                        Visit(o[name], step + 1, o, name, -1);
                    break;

                case { Index: var i } when i >= 0:
                    if (current is JsonArray a && i < a.Count)
                        Visit(a[i], step + 1, a, null, i);
                    break;

                default:
                    if (current is JsonArray all)
                    {
                        for (var n = 0; n < all.Count; n++)
                            Visit(all[n], step + 1, all, null, n);
                    }

                    break;
            }
        }
    }

    private static JsonNode? Value((JsonNode Parent, string? Key, int Index) place)
        => place.Parent is JsonObject o ? o[place.Key!] : ((JsonArray)place.Parent)[place.Index];

    // A step is a property name, an array index, or every item of an array (index -1).
    private readonly record struct Step(string? Name, int Index);

    private static bool TryParse(string path, out List<Step> steps)
    {
        steps = [];
        var segments = path.Split('.');
        for (var s = 0; s < segments.Length; s++)
        {
            var segment = segments[s];
            var bracket = segment.IndexOf('[');
            var name = bracket < 0 ? segment : segment[..bracket];

            // Only a body that is itself an array starts with an index, as in "[*].ts".
            if (name.Length > 0)
                steps.Add(new Step(name, -1));
            else if (s > 0 || bracket != 0)
                return false;

            var rest = bracket < 0 ? "" : segment[bracket..];
            while (rest.Length > 0)
            {
                var close = rest.IndexOf(']');
                if (rest[0] != '[' || close < 0)
                    return false;

                var inside = rest[1..close];
                if (inside == "*")
                    steps.Add(new Step(null, -1));
                else if (int.TryParse(inside, NumberStyles.None, CultureInfo.InvariantCulture, out var i))
                    steps.Add(new Step(null, i));
                else
                    return false;
                rest = rest[(close + 1)..];
            }
        }

        return true;
    }

    [GeneratedRegex(@"\[\d+\]")]
    private static partial Regex ArrayIndex();
}
