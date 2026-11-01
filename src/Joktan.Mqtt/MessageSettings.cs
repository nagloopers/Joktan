using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Joktan.Core;
using Joktan.Mqtt.Resources;

namespace Joktan.Mqtt;

public enum FieldType
{
    UnixSeconds,
    UnixMilliseconds,
    IsoTimestamp,
    Guid,
    Counter,
    RandomInteger,
    RandomNumber,
    OneOf,

    /// <summary>The listed values in turn, starting again after the last.</summary>
    Cycle,

    /// <summary>
    /// The same <see cref="FieldRule.Value"/> on every send. When <see cref="FieldRule.Values"/>
    /// is not empty, it lists the values the field may be set to.
    /// </summary>
    Fixed,
}

/// <summary>
/// A field of the message body that gets a new value on every send.
/// </summary>
public sealed record FieldRule(string Path, FieldType Type)
{
    public long Start { get; init; } = 1;

    public long Step { get; init; } = 1;

    public double Min { get; init; }

    public double Max { get; init; }

    public int Decimals { get; init; } = 2;

    public IReadOnlyList<JsonNode?> Values { get; init; } = [];

    /// <summary>The value a <see cref="FieldType.Fixed"/> field is sent with; null is JSON null.</summary>
    public JsonNode? Value { get; init; }
}

/// <summary>
/// The number of items an array in the body is sent with: <see cref="Min"/> when it equals
/// <see cref="Max"/>, otherwise a number from one to the other picked on every send.
/// </summary>
public sealed record ArrayRule(string Path, int Min, int Max)
{
    public const int Limit = 10_000;

    public bool IsFixed => Min == Max;
}

/// <summary>
/// How a folder message is sent. Stored beside the message as "&lt;name&gt;.send.json".
/// </summary>
public sealed record MessageSettings
{
    public const string FileSuffix = ".send.json";

    private const string DateFormat = "yyyy-MM-dd'T'HH:mm:ss";

    private static readonly string[] DayNames = ["sun", "mon", "tue", "wed", "thu", "fri", "sat"];

    public static readonly MessageSettings Default = new();

    /// <summary>Whether the message is switched on, so that it is sent on its schedule.</summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// The group the message is listed under, empty for none. Levels are separated by "/", so
    /// "Plant/Line 1" is listed under "Line 1" inside "Plant".
    /// </summary>
    public string Group { get; init; } = "";

    /// <summary>
    /// The topic to send to instead of the folder path, or empty to use the folder path. It may
    /// contain {folder} and the {name} of a topic field.
    /// </summary>
    public string Topic { get; init; } = "";

    /// <summary>Parts of the topic, written as {name}, that get a new value on every send.</summary>
    public IReadOnlyList<FieldRule> TopicFields { get; init; } = [];

    public int Qos { get; init; }

    public bool Retain { get; init; }

    /// <summary>Whether the body is gzipped before it is sent.</summary>
    public bool Gzip { get; init; }

    public MessageSchedule Schedule { get; init; } = MessageSchedule.Manual;

    public IReadOnlyList<FieldRule> Fields { get; init; } = [];

    /// <summary>Arrays in the body that are sent with a set number of items, applied before <see cref="Fields"/>.</summary>
    public IReadOnlyList<ArrayRule> Arrays { get; init; } = [];

    /// <summary>Whether anything in the body changes on a send.</summary>
    public bool ChangesBody => Fields.Count > 0 || Arrays.Count > 0;

    /// <summary>The settings file that belongs to a message file.</summary>
    public static string PathFor(string messagePath)
        => messagePath[..^".json".Length] + FileSuffix;

    /// <exception cref="FormatException">The text is not a valid settings file.</exception>
    public static MessageSettings Parse(string json)
    {
        try
        {
            var options = new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip, AllowDuplicateProperties = false };
            if (JsonNode.Parse(json, documentOptions: options) is not JsonObject root)
                throw new FormatException(Strings.MessageSettings_ItMustBeAJSONObject);

            var qos = Int(root, "qos") ?? 0;
            if (qos is < 0 or > 2)
                throw new FormatException(Strings.MessageSettings_QosMustBe01Or);

            return new MessageSettings
            {
                Enabled = Bool(root, "enabled") ?? false,
                Group = NormalizeGroup(root["group"]?.GetValue<string>()),
                Topic = root["topic"]?.GetValue<string>().Trim() ?? "",
                TopicFields = root["topicFields"] is { } topicFields ? ParseFields(topicFields, "topicFields") : [],
                Qos = qos,
                Retain = Bool(root, "retain") ?? false,
                Gzip = Bool(root, "gzip") ?? false,
                Schedule = root["schedule"] is { } schedule ? ParseSchedule(schedule) : MessageSchedule.Manual,
                Fields = root["fields"] is { } fields ? ParseFields(fields, "fields") : [],
                Arrays = root["arrays"] is { } arrays ? ParseArrays(arrays) : [],
            };
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            throw new FormatException(Strings.MessageSettings_ItIsNotValidJSONOr, e);
        }
    }

    public string ToJson()
    {
        var root = new JsonObject
        {
            ["enabled"] = Enabled,
            ["group"] = Group,
        };

        if (Topic.Length > 0)
            root["topic"] = Topic;
        if (TopicFields.Count > 0)
        {
            var topicFields = new JsonObject();
            foreach (var field in TopicFields)
                topicFields[field.Path] = FieldToJson(field);
            root["topicFields"] = topicFields;
        }

        root["qos"] = Qos;
        root["retain"] = Retain;
        if (Gzip)
            root["gzip"] = true;
        root["schedule"] = ScheduleToJson(Schedule);

        var fields = new JsonObject();
        foreach (var field in Fields)
            fields[field.Path] = FieldToJson(field);
        root["fields"] = fields;

        if (Arrays.Count > 0)
        {
            var arrays = new JsonObject();
            foreach (var array in Arrays)
            {
                arrays[array.Path] = array.IsFixed
                    ? new JsonObject { ["count"] = array.Min }
                    : new JsonObject { ["min"] = array.Min, ["max"] = array.Max };
            }

            root["arrays"] = arrays;
        }

        return root.ToJsonString(JsonFormats.Indented);
    }

    public static JsonObject ScheduleToJson(MessageSchedule s)
    {
        var node = new JsonObject { ["type"] = CamelCase(s.Type.ToString()) };
        switch (s.Type)
        {
            case ScheduleType.Once:
                node["at"] = s.At?.ToString(DateFormat, CultureInfo.InvariantCulture);
                break;

            case ScheduleType.Interval:
                node["every"] = s.Every;
                node["unit"] = CamelCase(s.Unit.ToString());
                if (s.Start is { } start)
                    node["start"] = start.ToString(DateFormat, CultureInfo.InvariantCulture);
                if (s.End is { } end)
                    node["end"] = end.ToString(DateFormat, CultureInfo.InvariantCulture);
                if (s.MaxSends is { } max)
                    node["maxSends"] = max;
                break;

            case ScheduleType.Daily:
                node["times"] = new JsonArray(s.Times.Order()
                    .Select(t => (JsonNode)t.ToString("HH:mm", CultureInfo.InvariantCulture)).ToArray());
                node["days"] = new JsonArray(s.Days.Select(d => (JsonNode)DayNames[(int)d]).ToArray());
                break;
        }

        return node;
    }

    private static MessageSchedule ParseSchedule(JsonNode node)
    {
        if (node is not JsonObject o)
            throw new FormatException(Strings.MessageSettings_ScheduleMustBeAnObject);

        var type = Enum(o, "type", ScheduleType.Manual);
        switch (type)
        {
            case ScheduleType.Once:
                return new MessageSchedule
                {
                    Type = type,
                    At = Date(o, "at") ?? throw new FormatException(Strings.MessageSettings_AOnceScheduleNeedsAt),
                };

            case ScheduleType.Interval:
                var every = Int(o, "every") ?? throw new FormatException(Strings.MessageSettings_AnIntervalScheduleNeedsEvery);
                if (every < 1)
                    throw new FormatException(Strings.MessageSettings_EveryMustBe1OrMore);
                var max = Int(o, "maxSends");
                if (max < 1)
                    throw new FormatException(Strings.MessageSettings_MaxSendsMustBe1OrMore);
                return new MessageSchedule
                {
                    Type = type,
                    Every = every,
                    Unit = Enum(o, "unit", IntervalUnit.Seconds),
                    Start = Date(o, "start"),
                    End = Date(o, "end"),
                    MaxSends = max,
                };

            case ScheduleType.Daily:
                var times = (o["times"] as JsonArray ?? [])
                    .Select(t => TimeOnly.TryParseExact(t?.GetValue<string>(), ["HH:mm", "H:mm", "HH:mm:ss"],
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
                        ? time
                        : throw new FormatException(string.Format(Strings.MessageSettings_IsNotATimeOfDay, t)))
                    .ToArray();
                var days = (o["days"] as JsonArray ?? [])
                    .Select(d => Array.IndexOf(DayNames, d?.GetValue<string>().ToLowerInvariant()) is >= 0 and var i
                        ? (DayOfWeek)i
                        : throw new FormatException(string.Format(Strings.MessageSettings_IsNotADaySuchAs, d)))
                    .Distinct()
                    .ToArray();
                if (times.Length == 0)
                    throw new FormatException(Strings.MessageSettings_ADailyScheduleNeedsAtLeast);
                if (days.Length == 0)
                    throw new FormatException(Strings.MessageSettings_ADailyScheduleNeedsAtLeastOne);
                return new MessageSchedule { Type = type, Times = times, Days = days };

            default:
                return MessageSchedule.Manual;
        }
    }

    /// <summary>Trims each level of a group path and drops empty ones: " Plant / Line 1/" becomes "Plant/Line 1".</summary>
    public static string NormalizeGroup(string? group)
        => string.Join('/', (group ?? "").Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));

    private static List<FieldRule> ParseFields(JsonNode node, string key)
    {
        if (node is not JsonObject o)
            throw new FormatException(string.Format(Strings.MessageSettings_MustBeAnObject, key));

        var label = key == "topicFields" ? Strings.MessageSettings_TopicField : Strings.MessageSettings_Field;
        var rules = new List<FieldRule>();
        foreach (var (path, value) in o)
        {
            if (value is not JsonObject f)
                throw new FormatException(string.Format(Strings.MessageSettings_MustBeAnObjectWithA, label, path));
            if (f["type"] is null)
                throw new FormatException(string.Format(Strings.MessageSettings_NeedsAType, label, path));

            var rule = new FieldRule(path, Enum(f, "type", FieldType.Guid))
            {
                Start = Long(f, "start") ?? 1,
                Step = Long(f, "step") ?? 1,
                Min = Double(f, "min") ?? 0,
                Max = Double(f, "max") ?? 0,
                Decimals = Int(f, "decimals") ?? 2,
                Values = (f["values"] as JsonArray)?.Select(v => v?.DeepClone()).ToArray() ?? [],
                Value = f["value"]?.DeepClone(),
            };

            if (rule.Type == FieldType.Fixed && !f.ContainsKey("value"))
                throw new FormatException(string.Format(Strings.MessageSettings_NeedsAValue, label, path));

            if (CheckRule(rule) is { } problem)
                throw new FormatException($"{label} \"{path}\": {problem}");
            rules.Add(rule);
        }

        return rules;
    }

    private static List<ArrayRule> ParseArrays(JsonNode node)
    {
        if (node is not JsonObject o)
            throw new FormatException(Strings.MessageSettings_ArraysMustBeAnObject);

        var rules = new List<ArrayRule>();
        foreach (var (path, value) in o)
        {
            if (value is not JsonObject a)
                throw new FormatException(string.Format(Strings.MessageSettings_ArrayMustBeAnObjectWith, path));

            var count = Int(a, "count");
            var min = Int(a, "min") ?? count;
            var max = Int(a, "max") ?? count;
            if (min is null || max is null)
                throw new FormatException(string.Format(Strings.MessageSettings_ArrayNeedsCountOrMinAnd, path));

            var rule = new ArrayRule(path, min.Value, max.Value);
            if (CheckArray(rule) is { } problem)
                throw new FormatException(string.Format(Strings.MessageSettings_Array, path, problem));
            rules.Add(rule);
        }

        return rules;
    }

    /// <summary>Returns what is wrong with an array rule's numbers, or null.</summary>
    public static string? CheckArray(ArrayRule rule)
        => rule.Min < 0 ? Strings.MessageSettings_TheNumberOfItemsMustNot
            : rule.Max > ArrayRule.Limit ? string.Format(CultureInfo.InvariantCulture, Strings.MessageSettings_TheNumberOfItemsMustNotBe, ArrayRule.Limit)
            : rule.Min > rule.Max ? Strings.MessageSettings_MinMustNotBeGreaterThan
            : null;

    /// <summary>Returns what is wrong with a rule's settings, or null.</summary>
    public static string? CheckRule(FieldRule rule) => rule.Type switch
    {
        FieldType.RandomInteger or FieldType.RandomNumber when rule.Min > rule.Max
            => Strings.MessageSettings_MinMustNotBeGreaterThan,
        FieldType.RandomNumber when rule.Decimals is < 0 or > 15
            => Strings.MessageSettings_DecimalsMustBeFrom0To,
        FieldType.OneOf or FieldType.Cycle when rule.Values.Count == 0
            => Strings.MessageSettings_ValuesMustListAtLeastOne,
        FieldType.Fixed when rule.Values.Count > 0 && !rule.Values.Any(v => JsonNode.DeepEquals(v, rule.Value))
            => Strings.MessageSettings_ValueMustBeOneOfValues,
        _ => null,
    };

    private static JsonObject FieldToJson(FieldRule f)
    {
        var node = new JsonObject { ["type"] = CamelCase(f.Type.ToString()) };
        switch (f.Type)
        {
            case FieldType.Counter:
                node["start"] = f.Start;
                node["step"] = f.Step;
                break;
            case FieldType.RandomInteger:
                node["min"] = (long)f.Min;
                node["max"] = (long)f.Max;
                break;
            case FieldType.RandomNumber:
                node["min"] = f.Min;
                node["max"] = f.Max;
                node["decimals"] = f.Decimals;
                break;
            case FieldType.OneOf or FieldType.Cycle:
                node["values"] = new JsonArray(f.Values.Select(v => v?.DeepClone()).ToArray());
                break;
            case FieldType.Fixed:
                node["value"] = f.Value?.DeepClone();
                if (f.Values.Count > 0)
                    node["values"] = new JsonArray(f.Values.Select(v => v?.DeepClone()).ToArray());
                break;
        }

        return node;
    }

    private static string CamelCase(string name) => char.ToLowerInvariant(name[0]) + name[1..];

    private static bool? Bool(JsonObject o, string key) => o[key]?.GetValue<bool>();

    private static int? Int(JsonObject o, string key) => o[key]?.GetValue<int>();

    private static long? Long(JsonObject o, string key) => o[key]?.GetValue<long>();

    private static double? Double(JsonObject o, string key) => o[key]?.GetValue<double>();

    private static T Enum<T>(JsonObject o, string key, T fallback) where T : struct, Enum
    {
        if (o[key] is not { } node)
            return fallback;

        var text = node.GetValue<string>();
        return System.Enum.TryParse<T>(text, ignoreCase: true, out var value) && System.Enum.IsDefined(value)
            ? value
            : throw new FormatException(string.Format(Strings.MessageSettings_IsNotAValid, text, key));
    }

    private static DateTime? Date(JsonObject o, string key)
    {
        if (o[key] is not { } node)
            return null;

        var text = node.GetValue<string>();
        return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var value)
            ? value
            : throw new FormatException(string.Format(Strings.MessageSettings_IsNotADateAndTime, text));
    }
}
