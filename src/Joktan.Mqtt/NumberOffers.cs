using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace Joktan.Mqtt;

/// <summary>A number that can be drawn from a message.</summary>
/// <param name="Path">Where it is, as a <see cref="PayloadPath"/>.</param>
/// <param name="Latest">Its value in this message; the last, if it has several.</param>
/// <param name="ByPosition">The same number by its position in an array, which this path can replace.</param>
/// <param name="LooksLikeTime">Whether it looks like a time or a counter of messages, rather than a measurement.</param>
/// <param name="Units">What the <c>unit</c> fields beside it say.</param>
public sealed record NumberOffer(string Path, double Latest, string? ByPosition, bool LooksLikeTime, IReadOnlyList<string> Units);

/// <summary>A field of a message that could tell its numbers apart.</summary>
public sealed record FieldOffer(string Path, string Value);

/// <summary>
/// Works out what is worth offering from a message: its numbers, by key where an array has one, and the
/// fields that could be keys. Nobody has to know the paths, or the shape of the message, to choose from these.
/// </summary>
public static class NumberOffers
{
    private const int MaxOffers = 200;
    private const int MaxItemsByPosition = 20;
    private const double LooksLikeEpoch = 1e9;

    // In the order a key is preferred, when an array's items have several fields that could be one.
    private static readonly string[] LikelyKeys = ["sensor", "id", "key", "name", "tag", "metric", "channel", "axis", "type"];

    private static readonly HashSet<string> NotKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "unit", "units", "uom", "ts", "time", "timestamp", "date", "datetime", "seq", "sequence", "since", "epoch", "value", "status", "quality",
    };

    private static readonly HashSet<string> TimeLike = new(StringComparer.OrdinalIgnoreCase)
    {
        "ts", "time", "timestamp", "date", "datetime", "seq", "sequence", "since", "epoch", "id", "version",
    };

    /// <summary>The numbers in a message, values and arrays before single items, and times and counters last.</summary>
    /// <param name="pattern">The pattern the topic matched, to offer readings chosen by a level of it.</param>
    public static IReadOnlyList<NumberOffer> Find(ReceivedMessage message, TopicPattern? pattern = null)
    {
        if (message.Number is { } number)
            return [new NumberOffer("", number, null, false, [])];

        if (message.Json is not { } root)
            return [];

        var offers = new Dictionary<string, Building>(StringComparer.Ordinal);
        Walk(root, "", "", "", new Position(""), [], offers);

        var ordered = offers.Values
            .OrderBy(o => o.Offer.LooksLikeTime)
            .ThenBy(o => JsonNumbers.IsOneItem(o.Offer.Path))
            .Select(o => o.Offer with { Units = o.Units.ToList() })
            .ToList();
        return pattern is null ? ordered : WithLinks(ordered, pattern, message.Topic);
    }

    /// <summary>The fields outside any array that hold text, which could be keys.</summary>
    public static IReadOnlyList<FieldOffer> Fields(ReceivedMessage message)
    {
        var fields = new List<FieldOffer>();
        if (message.Json is { ValueKind: JsonValueKind.Object } root)
            AddFields(root, "", fields);
        return fields;
    }

    /// <summary>The field in the items of an array that tells them apart, or null if none does.</summary>
    public static string? KeyOfItems(JsonElement array)
    {
        var items = array.EnumerateArray().ToList();
        if (items.Count == 0 || items.Any(i => i.ValueKind != JsonValueKind.Object))
            return null;

        var candidates = items[0].EnumerateObject()
            .Where(p => !NotKeys.Contains(p.Name) && IsKeyKind(p.Value, p.Name))
            .Select(p => p.Name)
            .Where(name => TellsItemsApart(items, name))
            .ToList();
        if (candidates.Count == 0)
            return null;

        return candidates
            .OrderBy(name => Array.IndexOf(LikelyKeys, name.ToLowerInvariant()) is var rank and >= 0 ? rank : LikelyKeys.Length)
            .First();
    }

    private static bool IsKeyKind(JsonElement value, string name) => value.ValueKind switch
    {
        JsonValueKind.String => !LooksLikeDate(value.GetString()),
        JsonValueKind.Number => value.TryGetInt64(out _) && LikelyKeys.Contains(name, StringComparer.OrdinalIgnoreCase),
        _ => false,
    };

    private static bool LooksLikeDate(string? text)
        => text is { Length: >= 8 } && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    private static bool TellsItemsApart(List<JsonElement> items, string field)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (PayloadPath.ScalarAt(item, field) is not { Length: > 0 } value || !seen.Add(value))
                return false;
        }

        return true;
    }

    private static void Walk(JsonElement element, string each, string one, string name, Position position, HashSet<string> units,
        Dictionary<string, Building> offers)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var beside = new HashSet<string>(units, StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Name.Equals("unit", StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String)
                        beside.Add(property.Value.GetString() ?? "");
                }

                foreach (var property in element.EnumerateObject())
                    Walk(property.Value, Join(each, property.Name), Join(one, property.Name), property.Name, position.Then("." + property.Name), beside, offers);
                break;

            case JsonValueKind.Array:
                WalkArray(element, each, one, name, position, units, offers);
                break;

            case JsonValueKind.Number when element.TryGetDouble(out var number) && double.IsFinite(number):
                var looksLikeTime = TimeLike.Contains(name) || (Math.Abs(number) >= LooksLikeEpoch && double.IsInteger(number));
                Add(each, number, null, looksLikeTime, units, offers);
                if (one != each)
                    Add(one, number, position.HasKey ? position.Text.TrimStart('.') : null, looksLikeTime, units, offers);
                break;
        }
    }

    private static void WalkArray(JsonElement array, string each, string one, string name, Position position, HashSet<string> units,
        Dictionary<string, Building> offers)
    {
        var key = KeyOfItems(array);
        var index = 0;
        foreach (var item in array.EnumerateArray())
        {
            if (key is not null && PayloadPath.ScalarAt(item, key) is { } value)
            {
                var chosen = $"[{key}={PayloadPath.Quote(value)}]";
                Walk(item, $"{each}[{key}=*]", one + chosen, name, position.Then($"[{index}]").WithKey(), units, offers);
            }
            else
            {
                var byPosition = index < MaxItemsByPosition ? $"{one}[{index}]" : $"{each}[*]";
                Walk(item, $"{each}[*]", position.HasKey ? one + "[*]" : byPosition, name, position.Then($"[{index}]"), units, offers);
            }

            index++;
        }
    }

    private static void Add(string path, double number, string? byPosition, bool looksLikeTime, HashSet<string> units, Dictionary<string, Building> offers)
    {
        if (!offers.TryGetValue(path, out var building))
        {
            if (offers.Count >= MaxOffers)
                return;
            offers[path] = building = new Building(new NumberOffer(path, number, byPosition, looksLikeTime, []));
        }

        building.Offer = building.Offer with { Latest = number };
        building.Units.UnionWith(units);
    }

    // A level of the topic that matches what chooses a reading is offered as a link, so the topic chooses it.
    private static IReadOnlyList<NumberOffer> WithLinks(List<NumberOffer> offers, TopicPattern pattern, string topic)
    {
        var values = pattern.KeyValues(topic);
        var result = new List<NumberOffer>(offers);
        foreach (var key in pattern.Keys)
        {
            if (!values.TryGetValue(key.Name, out var value) || value.Length == 0)
                continue;

            var link = key.Name.StartsWith("level", StringComparison.Ordinal) ? $"{{{key.Level}}}" : $"{{{key.Name}}}";
            var chosen = $"={PayloadPath.Quote(value)}]";
            foreach (var offer in offers)
            {
                var linked = offer.Path.Replace(chosen, $"={link}]", StringComparison.Ordinal);
                if (linked == offer.Path)
                    linked = LinkLevel(offer.Path, value, link);

                if (linked != offer.Path && result.All(o => o.Path != linked))
                    result.Add(offer with { Path = linked, ByPosition = null });
            }
        }

        return result;
    }

    private static string LinkLevel(string path, string value, string link)
    {
        if (path.Contains('[') || !path.Split('.').Contains(value))
            return path;
        return string.Join('.', path.Split('.').Select(part => part == value ? link : part));
    }

    private static void AddFields(JsonElement element, string path, List<FieldOffer> fields)
    {
        foreach (var property in element.EnumerateObject())
        {
            var inner = Join(path, property.Name);
            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                AddFields(property.Value, inner, fields);
            }
            else if (property.Value.ValueKind == JsonValueKind.String && !NotKeys.Contains(property.Name) && property.Value.GetString() is { Length: > 0 } text
                     && !LooksLikeDate(text))
            {
                fields.Add(new FieldOffer(inner, text));
            }
        }
    }

    private static string Join(string path, string name) => path.Length == 0 ? name : $"{path}.{name}";

    private sealed class Building(NumberOffer offer)
    {
        public NumberOffer Offer { get; set; } = offer;

        public HashSet<string> Units { get; } = new(StringComparer.Ordinal);
    }

    // The path by position alongside the path by key, to say which old path a new one can replace.
    private sealed record Position(string Text, bool HasKey = false)
    {
        public Position Then(string part) => this with { Text = Text + part };

        public Position WithKey() => this with { HasKey = true };
    }
}
