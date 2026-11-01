using Joktan.Core.ViewModels;
using Joktan.TopicDashboards.Resources;

namespace Joktan.TopicDashboards.ViewModels;

/// <summary>One line, bar or tile a graph draws, with its readings.</summary>
public partial class GraphSeriesViewModel : ViewModelBase
{
    private readonly GraphCardViewModel _card;

    /// <param name="topicName">A short name for the topic when the graph has several, otherwise null.</param>
    /// <param name="keys">The key values that tell it from the others on this graph.</param>
    /// <param name="id">What it is called in the graph's saved colours; empty for a number that has only one line.</param>
    /// <param name="namePath">Whether its name says its path as well, as other numbers on the graph have keys too.</param>
    public GraphSeriesViewModel(GraphCardViewModel card, GraphValue value, SampleSeries samples, string? topicName, SeriesKeys? keys = null, int? slot = null, string id = "", bool namePath = false)
    {
        _card = card;
        keys ??= SeriesKeys.None;
        Id = id;
        Path = value.Path;
        Source = Path.Length > 0 ? $"{value.Topic} {Path}" : value.Topic;
        Slot = slot ?? value.Color;
        Samples = samples;
        var byKeys = string.Join(' ', keys.Values.Values);
        if (namePath && byKeys.Length > 0 && Path.Length > 0)
            byKeys = $"{Path} {byKeys}";
        DisplayLabel = value.Label.Trim() is { Length: > 0 } label ? keys.Fill(label, out _)
            : byKeys.Length > 0 ? byKeys
            : topicName is not null ? (Path.Length > 0 ? $"{topicName} {Path}" : topicName)
            : Path.Length > 0 ? Path
            : Strings.GraphSeries_Value;
    }

    public string Id { get; }

    public string Path { get; }

    /// <summary>The topic and path in full.</summary>
    public string Source { get; }

    /// <summary>Which of the graph colours it is drawn in. It stays with the number, whatever else is added or removed.</summary>
    public int Slot { get; }

    public SampleSeries Samples { get; }

    public string DisplayLabel { get; }

    public double? Latest => Samples.Samples.Count > 0 ? Samples.Samples[^1].Value : null;

    public string LatestText => Latest is { } latest ? _card.Format(latest) : "–";

    /// <summary>Says that new readings have arrived.</summary>
    public void Refresh() => OnPropertyChanged(nameof(LatestText));
}
