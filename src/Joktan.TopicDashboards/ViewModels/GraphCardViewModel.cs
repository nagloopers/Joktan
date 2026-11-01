using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Core.ViewModels;
using Joktan.TopicDashboards.Resources;

namespace Joktan.TopicDashboards.ViewModels;

/// <summary>One graph on a dashboard, or the preview of one being set up.</summary>
public partial class GraphCardViewModel : ViewModelBase
{
    private int _dataVersion = -1;

    /// <param name="cell">The values of the tab and graph keys this one shows, when one saved graph is shown as several.</param>
    public GraphCardViewModel(GraphProfile profile, GraphData data, SeriesKeys? cell = null)
    {
        Profile = profile;
        Data = data;
        Cell = cell ?? SeriesKeys.None;
        Apply(profile, data, Cell);
    }

    public GraphProfile Profile { get; private set; }

    public GraphData Data { get; private set; }

    /// <summary>What sets this one apart from the others made from the same saved graph.</summary>
    public SeriesKeys Cell { get; private set; }

    /// <summary>Whether this one is one of several, which can be forgotten on its own.</summary>
    public bool HasCell => !Cell.IsEmpty;

    /// <summary>The numbers being drawn.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<GraphSeriesViewModel> Series { get; private set; } = [];

    /// <summary>Changes whenever there is something new to draw.</summary>
    [ObservableProperty]
    public partial int Revision { get; private set; }

    [ObservableProperty]
    public partial string Name { get; private set; } = "";

    [ObservableProperty]
    public partial string Source { get; private set; } = "";

    /// <summary>The columns of the dashboard it spans.</summary>
    [ObservableProperty]
    public partial int WidthSpan { get; set; } = 1;

    /// <summary>The rows of the dashboard it spans.</summary>
    [ObservableProperty]
    public partial int HeightSpan { get; set; } = 1;

    /// <summary>Why nothing is drawn, or null when there is something to draw.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string? Status { get; set; }

    public bool HasStatus => Status is not null;

    /// <summary>Whether a line or bar graph has several numbers to tell apart; gauges and numbers name their own.</summary>
    public bool HasLegend => Series.Count > 1 && Profile.Kind is GraphKind.Line or GraphKind.Area;

    /// <summary>The latest value beside the name, for a graph that draws one number over time.</summary>
    public bool HasHeadlineValue => Series.Count == 1 && Profile.Kind is GraphKind.Line or GraphKind.Area or GraphKind.Bars;

    public string HeadlineValue => Series.Count > 0 ? Series[0].LatestText : "";

    /// <summary>How many lines there are that the graph has no room for.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHiddenLines))]
    [NotifyPropertyChangedFor(nameof(HiddenLinesText))]
    public partial int HiddenLines { get; private set; }

    public bool HasHiddenLines => HiddenLines > 0;

    public string HiddenLinesText => string.Format(Strings.GraphCard_AndMore, HiddenLines);

    /// <summary>This graph is being dragged to another place.</summary>
    [ObservableProperty]
    public partial bool IsDragging { get; set; }

    /// <summary>Another graph being dragged would go here.</summary>
    [ObservableProperty]
    public partial bool IsDropTarget { get; set; }

    /// <summary>Remove was pressed once, and is being asked about.</summary>
    [ObservableProperty]
    public partial bool IsConfirmingRemove { get; set; }

    /// <summary>What it says when removing it, as that takes all those made from the same saved graph.</summary>
    [ObservableProperty]
    public partial string RemoveQuestion { get; set; } = Strings.GraphCard_RemoveThisGraph;

    /// <summary>Whether it says how it has been doing.</summary>
    [ObservableProperty]
    public partial bool IsShowingDetails { get; set; }

    [ObservableProperty]
    public partial string DetailsText { get; set; } = "";

    public Action<GraphCardViewModel>? EditRequested { get; set; }

    public Action<GraphCardViewModel>? RemoveRequested { get; set; }

    public Action<GraphCardViewModel, int>? MoveRequested { get; set; }

    public Action<GraphCardViewModel>? ForgetRequested { get; set; }

    public Action<GraphCardViewModel>? RemoveAsked { get; set; }

    public Action<GraphCardViewModel>? SpanChanged { get; set; }

    /// <summary>Says how the graph has been doing, when asked.</summary>
    public Func<string>? DescribeHealth { get; set; }

    /// <summary>Changes what is drawn and how, and from which readings.</summary>
    public void Apply(GraphProfile profile, GraphData data, SeriesKeys? cell = null)
    {
        Profile = profile;
        Data = data;
        Cell = cell ?? Cell;
        Name = NameFor(profile, Cell);
        Source = string.Join(", ", profile.Topics);
        WidthSpan = profile.Width;
        HeightSpan = profile.Height;
        OnPropertyChanged(nameof(HasCell));
        _dataVersion = -1;
        Series = BuildSeries();
        OnPropertyChanged(nameof(HasLegend));
        OnPropertyChanged(nameof(HasHeadlineValue));
        OnPropertyChanged(nameof(HeadlineValue));
        Revision++;
    }

    /// <summary>Draws the readings again, after new ones have been added.</summary>
    public void Refresh()
    {
        SyncSeries();
        foreach (var series in Series)
            series.Refresh();
        OnPropertyChanged(nameof(HeadlineValue));
        Revision++;
    }

    /// <summary>Moves the graph on as time passes.</summary>
    /// <param name="visible">Whether it is on screen, so needs drawing again.</param>
    public void Tick(DateTime now, bool visible)
    {
        SyncSeries();
        if (visible)
            Revision++;
        if (IsShowingDetails)
            DetailsText = Details();
    }

    /// <summary>Every line from a key this graph has, and whether it was removed, for choosing in the editor.</summary>
    public IReadOnlyList<(GraphSeriesViewModel Line, bool IsRemoved)> LinesFromKeys()
    {
        var lines = new List<(GraphSeriesViewModel, bool)>();
        foreach (var value in Profile.Values.Where(v => GraphReader.IsSeveral(v, Profile)))
        {
            foreach (var series in LinesOf(value))
            {
                var line = LineFor(value, series, null);
                lines.Add((line, Profile.RemovedLines.Contains(RemovedId(line.Id))));
            }
        }

        return lines;
    }

    /// <summary>Takes a line from a key off this graph, or brings it back. It goes on being read either way.</summary>
    public void SetRemoved(string lineId, bool removed)
    {
        Profile.RemovedLines.Remove(RemovedId(lineId));
        if (removed)
            Profile.RemovedLines.Add(RemovedId(lineId));
        _dataVersion = -1;
        SyncSeries();
        Revision++;
    }

    public bool HasReadings => Series.Any(s => s.Samples.Samples.Count > 0);

    /// <summary>A value as the graph is set to show it, with its unit.</summary>
    public string Format(double value)
    {
        var text = Profile.Decimals >= 0 ? value.ToString($"F{Profile.Decimals}") : value.ToString("0.###");
        return Profile.Unit.Trim() switch
        {
            { Length: 0 } => text,
            "%" => $"{text}%",
            var unit => $"{text} {unit}",
        };
    }

    /// <summary>A value on the scale, shortened from a thousand up. The unit is left to the headline.</summary>
    public string FormatScale(double value)
    {
        var size = Math.Abs(value);
        return size >= 1e9 ? $"{value / 1e9:0.#}G"
            : size >= 1e6 ? $"{value / 1e6:0.#}M"
            : size >= 1e3 ? $"{value / 1e3:0.#}K"
            : value.ToString("0.###");
    }

    private static string NameFor(GraphProfile profile, SeriesKeys cell)
    {
        var filled = cell.Fill(profile.Name, out var any);
        if (any)
            return filled;

        // The tab already says the value of a tab key.
        var own = cell.Only(profile.Keys.Where(k => k.Show == KeyShow.Graph).Select(k => k.Name));
        return own.IsEmpty ? profile.Name : $"{profile.Name}: {string.Join(' ', own.Values.Values)}";
    }

    private void SyncSeries()
    {
        if (_dataVersion == Data.Version)
            return;

        var series = BuildSeries();
        if (series.Select(s => (s.Id, s.Samples)).SequenceEqual(Series.Select(s => (s.Id, s.Samples))))
            return;

        Series = series;
        OnPropertyChanged(nameof(HasLegend));
        OnPropertyChanged(nameof(HasHeadlineValue));
        OnPropertyChanged(nameof(HeadlineValue));
    }

    private List<GraphSeriesViewModel> BuildSeries()
    {
        _dataVersion = Data.Version;
        var topics = Profile.Topics.ToList();
        var topicNames = topics.Count > 1 ? TopicNames.Shorten(topics) : null;
        var rows = new List<GraphSeriesViewModel>();
        var all = new List<(GraphValue Value, KeyedSeries Series)>();
        foreach (var value in Profile.Values)
        {
            if (GraphReader.IsSeveral(value, Profile))
            {
                all.AddRange(LinesOf(value).Where(s => !Profile.RemovedLines.Contains(RemovedId(LineId(value, s)))).Select(s => (value, s)));
            }
            else
            {
                rows.Add(new GraphSeriesViewModel(this, value, Data.Series(value.Topic, value.Path), topicNames?[value.Topic]));
            }
        }

        // The most recently updated are the ones on show, and they stay in a steady order.
        var room = Math.Max(0, GraphProfile.MaxValues - rows.Count);
        HiddenLines = Math.Max(0, all.Count - room);
        var shown = all.Count <= room ? all : all
            .OrderByDescending(a => a.Series.Samples.Samples.Count > 0 ? a.Series.Samples.Samples[^1].Time : DateTime.MinValue)
            .Take(room)
            .OrderBy(a => a.Series.Keys.Id, StringComparer.Ordinal)
            .ToList();

        foreach (var (value, series) in shown)
            rows.Add(LineFor(value, series, topicNames?[value.Topic]));

        return rows;
    }

    private IEnumerable<KeyedSeries> LinesOf(GraphValue value)
        => Data.All
            .Where(s => s.Topic == value.Topic && s.Path == value.Path && s.Keys.Includes(Cell))
            .OrderBy(s => s.Keys.Id, StringComparer.Ordinal);

    private GraphSeriesViewModel LineFor(GraphValue value, KeyedSeries series, string? topicName)
    {
        var lineKeys = new SeriesKeys(series.Keys.Values.Where(v => Cell[v.Key] is null));
        var id = LineId(value, series);
        var slot = lineKeys.IsEmpty ? value.Color : SlotFor(id);
        return new GraphSeriesViewModel(this, value, series.Samples, topicName, lineKeys, slot, id, Profile.Values.Count > 1);
    }

    private string LineId(GraphValue value, KeyedSeries series)
        => $"{value.Topic}|{value.Path}|{new SeriesKeys(series.Keys.Values.Where(v => Cell[v.Key] is null)).Id}";

    // Removed from this graph only, not from others made from the same saved graph.
    private string RemovedId(string lineId) => $"{Cell.Id}\u001e{lineId}";

    // A line keeps its colour between runs, and a new one takes the first not in use.
    private int SlotFor(string id)
    {
        if (Profile.SeriesColors.TryGetValue(id, out var slot))
            return slot;

        var used = Profile.SeriesColors.Values.ToHashSet();
        slot = Enumerable.Range(0, GraphColors.Count).Where(c => !used.Contains(c)).DefaultIfEmpty(Profile.SeriesColors.Count % GraphColors.Count).First();
        Profile.SeriesColors[id] = slot;
        return slot;
    }

    private string Details()
    {
        var health = DescribeHealth?.Invoke() ?? "";
        var lines = HiddenLines > 0 ? string.Format(Strings.GraphCard_SeriesOfShown, Series.Count, Series.Count + HiddenLines) : string.Format(Strings.GraphCard_Series, Series.Count);
        return $"{health}\n{lines}";
    }

    partial void OnWidthSpanChanged(int value)
    {
        Profile.Width = value;
        SpanChanged?.Invoke(this);
    }

    partial void OnHeightSpanChanged(int value)
    {
        Profile.Height = value;
        SpanChanged?.Invoke(this);
    }

    [RelayCommand]
    private void Edit() => EditRequested?.Invoke(this);

    [RelayCommand]
    private void AskRemove()
    {
        RemoveAsked?.Invoke(this);
        IsConfirmingRemove = true;
    }

    [RelayCommand]
    private void KeepGraph() => IsConfirmingRemove = false;

    [RelayCommand]
    private void Remove() => RemoveRequested?.Invoke(this);

    [RelayCommand]
    private void Forget() => ForgetRequested?.Invoke(this);

    [RelayCommand]
    private void ToggleDetails()
    {
        IsShowingDetails = !IsShowingDetails;
        DetailsText = Details();
    }

    [RelayCommand]
    private void MoveEarlier() => MoveRequested?.Invoke(this, -1);

    [RelayCommand]
    private void MoveLater() => MoveRequested?.Invoke(this, 1);
}
