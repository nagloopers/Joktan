using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Joktan.Mqtt;
using Joktan.TopicDashboards.Resources;

namespace Joktan.TopicDashboards;

/// <summary>Numbers read for one series of a graph.</summary>
/// <param name="Topic">The graph's topic, as written, not the one the message arrived on.</param>
public sealed record GraphReading(string Topic, string Path, SeriesKeys Keys, IReadOnlyList<double> Values);

/// <summary>What a graph found in a message.</summary>
/// <param name="Problem">Why it found nothing, when that is not because it was left out on purpose.</param>
/// <param name="Keys">The key values the message held, including those left out by Only.</param>
public sealed record GraphRead(IReadOnlyList<GraphReading> Readings, string? Problem, IReadOnlyList<SeriesKeys> Keys);

/// <summary>Reads what a graph draws from a message, by its topics, paths and keys.</summary>
public sealed class GraphReader
{
    private readonly List<(GraphValue Value, TopicPattern Pattern, PayloadPath Path)> _values = [];
    private readonly List<GraphKey> _fieldKeys;
    private readonly Dictionary<string, string> _only;

    public GraphReader(GraphProfile profile)
    {
        foreach (var value in profile.Values)
        {
            if (TopicPattern.Parse(value.Topic) is { } pattern)
                _values.Add((value, pattern, PayloadPath.Parse(value.Path)));
        }

        _fieldKeys = profile.Keys.Where(k => k.Field.Length > 0).ToList();
        _only = profile.Keys.Where(k => k.Show == KeyShow.Only).ToDictionary(k => k.Name, k => k.Only, StringComparer.Ordinal);
    }

    /// <summary>The filters to subscribe to.</summary>
    public IEnumerable<string> Filters => _values.Select(v => v.Pattern.Filter).Distinct();

    public bool Matches(string topic) => _values.Any(v => v.Pattern.Matches(topic));

    /// <summary>Whether a number can be several lines, rather than exactly one.</summary>
    public static bool IsSeveral(GraphValue value, GraphProfile profile)
    {
        var pattern = TopicPattern.Parse(value.Topic);
        var path = PayloadPath.Parse(value.Path);
        return pattern is { IsExact: false } || path.KeyNames.Count > 0 || profile.Keys.Any(k => k.Field.Length > 0 && k.Show != KeyShow.Only);
    }

    public GraphRead Read(ReceivedMessage message)
    {
        var readings = new List<GraphReading>();
        var seen = new List<SeriesKeys>();
        string? problem = null;
        foreach (var (value, pattern, path) in _values)
        {
            if (!pattern.Matches(message.Topic))
                continue;

            var fromTopic = pattern.KeyValues(message.Topic);
            var rows = RowsOf(message, path, fromTopic);
            if (rows.Count == 0)
            {
                problem ??= string.Format(Strings.GraphReader_NothingAt, (value.Path.Length > 0 ? value.Path : Strings.GraphReader_TheWholePayload));
                continue;
            }

            foreach (var row in rows)
            {
                if (KeysOf(message, fromTopic, row, out var missing) is not { } keys)
                {
                    problem ??= string.Format(Strings.GraphReader_NoValueOf, missing);
                    continue;
                }

                var all = new SeriesKeys(keys);
                seen.Add(all);
                if (_only.Any(o => all[o.Key] is { } held && held != o.Value))
                    continue;

                var kept = keys.Where(k => !_only.ContainsKey(k.Key));
                readings.Add(new GraphReading(value.Topic, value.Path, new SeriesKeys(kept), row.Values));
            }
        }

        return new GraphRead(readings, readings.Count > 0 || seen.Count > 0 ? null : message.NumbersProblem ?? problem, seen);
    }

    private static IReadOnlyList<PathRow> RowsOf(ReceivedMessage message, PayloadPath path, Dictionary<string, string> fromTopic)
    {
        if (path.IsPlain)
        {
            var field = message.NumericFields.FirstOrDefault(f => f.Path == path.Text);
            return field is null ? [] : [new PathRow(new Dictionary<string, string>(), field.Values)];
        }

        return message.Json is { } root ? path.Read(root, link => LinkValue(link, message.Topic, fromTopic)) : [];
    }

    private static string? LinkValue(string link, string topic, Dictionary<string, string> fromTopic)
    {
        if (int.TryParse(link, NumberStyles.None, CultureInfo.InvariantCulture, out var level))
        {
            var levels = topic.Split('/');
            return level < levels.Length ? levels[level] : null;
        }

        return fromTopic.GetValueOrDefault(link);
    }

    // Null when a key the graph wants from the message is not in it.
    private Dictionary<string, string>? KeysOf(ReceivedMessage message, Dictionary<string, string> fromTopic, PathRow row, out string? missing)
    {
        missing = null;
        var keys = new Dictionary<string, string>(fromTopic, StringComparer.Ordinal);
        foreach (var (key, value) in row.Keys)
            keys[key] = value;

        foreach (var key in _fieldKeys)
        {
            if (keys.ContainsKey(key.Name))
                continue;
            if (message.Json is not { } root || PayloadPath.ScalarAt(root, key.Field) is not { } value)
            {
                missing = key.Name;
                return null;
            }

            keys[key.Name] = value;
        }

        return keys;
    }
}

/// <summary>One saved graph at work: its readings, what it has seen, and how it is doing.</summary>
public sealed class GraphSource
{
    private readonly Func<bool> _canAddSeries;

    /// <param name="canAddSeries">Whether there is room for one more series, across the app.</param>
    public GraphSource(GraphProfile profile, GraphData data, Func<bool>? canAddSeries = null)
    {
        Profile = profile;
        Data = data;
        Reader = new GraphReader(profile);
        _canAddSeries = canAddSeries ?? (() => true);
    }

    public GraphProfile Profile { get; }

    public GraphData Data { get; }

    public GraphReader Reader { get; }

    public GraphHealth Health { get; } = new();

    /// <summary>Every value seen of every key, including those left out by Only.</summary>
    public Dictionary<string, SortedSet<string>> KeyValues { get; } = new(StringComparer.Ordinal);

    /// <summary>Why something was left out because of a limit, or null.</summary>
    public string? OverLimit { get; private set; }

    private IEnumerable<string> PlacedKeys => Profile.Keys.Where(k => k.Show is KeyShow.Tab or KeyShow.Graph).Select(k => k.Name);

    /// <summary>The graphs this is shown as, on the tab with this value of a tab key. Never none, on a tab with no value.</summary>
    public IReadOnlyList<SeriesKeys> CellsFor(string? tabKey, string? tabValue)
    {
        var cells = Profile.Seen.Select(s => new SeriesKeys(s)).ToList();
        if (Profile.TabKey is not null && tabKey is not null && tabValue is not null)
            return cells.Where(c => c[tabKey] == tabValue).ToList();
        return cells.Count > 0 ? cells : [SeriesKeys.None];
    }

    /// <summary>Takes in a message. Returns whether anything was new, and whether a new graph or tab is needed.</summary>
    public (bool Added, bool NewCell) Receive(ReceivedMessage message)
    {
        var read = Reader.Read(message);
        foreach (var keys in read.Keys)
            NoteKeys(keys);

        var newCell = false;
        var readings = new List<GraphReading>();
        string? refused = null;
        foreach (var reading in read.Readings)
        {
            if (!Data.Has(reading.Topic, reading.Path, reading.Keys) && !_canAddSeries())
            {
                refused = OverLimit = Strings.GraphSource_ThereAreTooManySeriesIn;
                continue;
            }

            var cell = reading.Keys.Only(PlacedKeys);
            if (!cell.IsEmpty && Profile.Seen.All(s => new SeriesKeys(s).Id != cell.Id))
            {
                if (LimitOn(cell) is { } limit)
                {
                    refused = OverLimit = limit;
                    continue;
                }

                Profile.Seen.Add(new Dictionary<string, string>(cell.Values));
                newCell = true;
            }

            readings.Add(reading);
        }

        var added = Data.Add(message, readings);
        Health.Record(message, added, refused ?? read.Problem, readings.Count == 0 && read.Readings.Count == 0 && read.Problem is null);
        return (added, newCell);
    }

    /// <summary>Works out the graphs and tabs for readings there already are, after how they are shown has changed.</summary>
    public void LearnCells()
    {
        foreach (var cell in Data.All.Select(s => s.Keys.Only(PlacedKeys)).Where(c => !c.IsEmpty).Distinct().ToList())
        {
            if (Profile.Seen.All(s => new SeriesKeys(s).Id != cell.Id) && LimitOn(cell) is null)
                Profile.Seen.Add(new Dictionary<string, string>(cell.Values));
        }
    }

    /// <summary>Forgets a graph or tab: every place holding these values.</summary>
    public bool Forget(SeriesKeys cell)
    {
        var removed = Profile.Seen.RemoveAll(s => new SeriesKeys(s).Includes(cell)) > 0;
        foreach (var series in Data.All.Where(s => s.Keys.Includes(cell)).ToList())
            Data.Forget(series);
        return removed;
    }

    private void NoteKeys(SeriesKeys keys)
    {
        foreach (var (key, value) in keys.Values)
        {
            if (!KeyValues.TryGetValue(key, out var values))
                KeyValues[key] = values = new SortedSet<string>(StringComparer.Ordinal);
            if (values.Count < GraphProfile.MaxGraphs * GraphProfile.MaxTabs)
                values.Add(value);
        }
    }

    private string? LimitOn(SeriesKeys cell)
    {
        var cells = Profile.Seen.Select(s => new SeriesKeys(s)).ToList();
        if (Profile.TabKey is not { } tabKey)
            return cells.Count >= GraphProfile.MaxGraphs ? string.Format(Strings.GraphSource_ItIsAlreadyShownAsGraphs, GraphProfile.MaxGraphs) : null;

        var tabs = cells.Select(c => c[tabKey]).Distinct().ToList();
        if (!tabs.Contains(cell[tabKey]) && tabs.Count >= GraphProfile.MaxTabs)
            return string.Format(Strings.GraphSource_AlreadyHasTabsTheMostFor, tabKey, GraphProfile.MaxTabs);
        return cells.Count(c => c[tabKey] == cell[tabKey]) >= GraphProfile.MaxGraphs
            ? string.Format(Strings.GraphSource_ItIsAlreadyShownAsGraphs, GraphProfile.MaxGraphs)
            : null;
    }
}

/// <summary>How a graph has been doing lately, to say why it shows what it shows.</summary>
public sealed class GraphHealth
{
    private const int Seconds = 60;

    private readonly (long Second, int Matched, int Used, int Skipped)[] _buckets = new (long, int, int, int)[Seconds];

    public DateTime? LastMessage { get; private set; }

    public string LastTopic { get; private set; } = "";

    /// <summary>Why the last message that gave nothing did, or null if the last message was fine.</summary>
    public string? Problem { get; private set; }

    public string? LastProblem { get; private set; }

    /// <param name="leftOut">Whether it gave nothing because the graph asked for only some values.</param>
    public void Record(ReceivedMessage message, bool used, string? problem, bool leftOut)
    {
        LastMessage = message.Time;
        LastTopic = message.Topic;
        Problem = used || leftOut ? null : problem;
        if (problem is not null && !used)
            LastProblem = problem;

        var second = message.Time.Ticks / TimeSpan.TicksPerSecond;
        ref var bucket = ref _buckets[second % Seconds];
        if (bucket.Second != second)
            bucket = (second, 0, 0, 0);
        bucket.Matched++;
        if (used)
            bucket.Used++;
        else if (problem is not null)
            bucket.Skipped++;
    }

    /// <summary>Says what has happened, for showing beside the graph.</summary>
    public string Describe(DateTime now)
    {
        if (LastMessage is not { } last)
            return Strings.GraphHealth_NoMessageHasArrivedSinceThe;

        var since = now.Ticks / TimeSpan.TicksPerSecond - Seconds;
        var recent = _buckets.Where(b => b.Second > since).ToList();
        var matched = recent.Sum(b => b.Matched);
        var used = recent.Sum(b => b.Used);
        var skipped = recent.Sum(b => b.Skipped);
        var lines = new List<string>
        {
            string.Format(Strings.GraphHealth_LastMessageOn, Ago(now - last), LastTopic),
            string.Format(Strings.GraphHealth_LastMinuteMatchedUsedSkipped, matched, used, skipped, (skipped > 0 && LastProblem is not null ? $": {LastProblem}" : "")),
        };
        return string.Join('\n', lines);
    }

    private static string Ago(TimeSpan time)
        => time.TotalSeconds < 1 ? Strings.GraphHealth_JustNow
            : time.TotalMinutes < 1 ? string.Format(Strings.GraphHealth_SAgo, (int)time.TotalSeconds)
            : time.TotalHours < 1 ? string.Format(Strings.GraphHealth_MAgo, (int)time.TotalMinutes)
            : string.Format(Strings.GraphHealth_HAgo, (int)time.TotalHours);
}
