using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Core.ViewModels;
using Joktan.Mqtt;
using Joktan.TopicExplorer.Resources;

namespace Joktan.TopicExplorer.ViewModels;

/// <summary>
/// One level of a topic in the explorer's tree. A node can hold messages of its own, child
/// levels, or both.
/// </summary>
public partial class TopicNodeViewModel : ViewModelBase
{
    public const int HistoryLength = 100;
    public const int TrendLength = 60;
    public const int MaxTrendsShown = 12;

    private readonly Dictionary<string, TopicNodeViewModel> _index = new(StringComparer.Ordinal);

    // What the user chose to draw; null until they do, when what is changing is drawn.
    private HashSet<string>? _chosen;

    public TopicNodeViewModel(string name, string fullTopic, TopicNodeViewModel? parent)
    {
        Name = name;
        FullTopic = fullTopic;
        Parent = parent;
    }

    public string Name { get; }

    public string FullTopic { get; }

    public TopicNodeViewModel? Parent { get; }

    public string DisplayName => Name.Length == 0 ? Strings.TopicNode_EmptyLevel : Name;

    public ObservableCollection<TopicNodeViewModel> Children { get; } = [];

    /// <summary>Messages received on exactly this topic, newest first.</summary>
    public ObservableCollection<ReceivedMessage> History { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasValue), nameof(Info))]
    public partial int MessageCount { get; set; }

    /// <summary>Topics below this node that have received a message.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Info))]
    public partial int TopicCount { get; set; }

    /// <summary>Messages received on topics below this node.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Info))]
    public partial int SubtreeMessages { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Preview), nameof(IsRetained))]
    public partial ReceivedMessage? LastMessage { get; set; }

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    /// <summary>False when the search box hides this node.</summary>
    [ObservableProperty]
    public partial bool IsVisible { get; set; } = true;

    /// <summary>The numbers that can be drawn. Kept up to date while the node is selected.</summary>
    public ObservableCollection<FieldChoiceViewModel> Choices { get; } = [];

    /// <summary>Whether the list of <see cref="Choices"/> is open.</summary>
    [ObservableProperty]
    public partial bool IsChoosing { get; set; }

    /// <summary>The numbers being drawn. Kept up to date while the node is selected.</summary>
    public ObservableCollection<FieldTrendViewModel> Trends { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMoreTrends), nameof(MoreTrendsText))]
    public partial int MoreTrends { get; set; }

    public bool HasValue => MessageCount > 0;

    public bool HasTrend => Trends.Count > 0;

    public bool HasMoreTrends => MoreTrends > 0;

    public string MoreTrendsText => string.Format(Strings.TopicNode_MoreNotShown, MoreTrends);

    public bool HasNumbers => Choices.Count > 0;

    public bool IsChosenByUser => _chosen is not null;

    /// <summary>What the user chose to draw, or null for what is changing. Set when it is loaded.</summary>
    public IReadOnlyCollection<string>? ChosenPaths
    {
        get => _chosen;
        set
        {
            _chosen = value is null ? null : new HashSet<string>(value, StringComparer.Ordinal);
            OnPropertyChanged(nameof(IsChosenByUser));
        }
    }

    /// <summary>The user changed what is drawn.</summary>
    public event Action<TopicNodeViewModel>? ChosenChanged;

    public string Preview => LastMessage is { } m ? $"= {m.Preview}" : "";

    public bool IsRetained => LastMessage?.Retain == true;

    /// <summary>What sits below this node, for example "3 topics, 120 messages".</summary>
    public string Info
    {
        get
        {
            if (TopicCount == 0)
                return MessageCount > 1 ? string.Format(Strings.TopicNode_Messages, MessageCount) : "";

            var topics = TopicCount == 1 ? Strings.TopicNode_1Topic : string.Format(Strings.TopicNode_Topics, TopicCount);
            var messages = SubtreeMessages == 1 ? Strings.TopicNode_1Message : string.Format(Strings.TopicNode_Messages2, SubtreeMessages);
            return $"({topics}, {messages})";
        }
    }

    public TopicNodeViewModel? Find(string name) => _index.GetValueOrDefault(name);

    /// <summary>Adds a child level, keeping the children in alphabetical order.</summary>
    public TopicNodeViewModel Add(string name, string fullTopic)
    {
        var child = new TopicNodeViewModel(name, fullTopic, this);
        _index[name] = child;
        Insert(Children, child);
        return child;
    }

    public static void Insert(ObservableCollection<TopicNodeViewModel> list, TopicNodeViewModel node)
    {
        var low = 0;
        var high = list.Count;
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (StringComparer.OrdinalIgnoreCase.Compare(list[middle].Name, node.Name) <= 0)
                low = middle + 1;
            else
                high = middle;
        }

        list.Insert(low, node);
    }

    /// <summary>Records a message received on exactly this topic.</summary>
    public void Receive(ReceivedMessage message)
    {
        var first = MessageCount == 0;
        MessageCount++;
        LastMessage = message;

        History.Insert(0, message);
        if (History.Count > HistoryLength)
            History.RemoveAt(History.Count - 1);

        for (var ancestor = Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            ancestor.SubtreeMessages++;
            if (first)
                ancestor.TopicCount++;
        }
    }

    /// <summary>Works out the trends from the messages kept in <see cref="History"/>.</summary>
    public void UpdateTrends()
    {
        // Newest first, so a field stops collecting once it has enough, and the fields are in the
        // order the latest message has them.
        var series = new Dictionary<string, List<double>>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var message in History)
        {
            foreach (var field in message.NumericFields)
            {
                if (!series.TryGetValue(field.Path, out var values))
                {
                    series[field.Path] = values = [];
                    order.Add(field.Path);
                }

                for (var i = field.Values.Count - 1; i >= 0 && values.Count < TrendLength; i--)
                    values.Add(field.Values[i]);
            }
        }

        // Values and arrays before single items, whichever message named them first.
        order = order.OrderBy(JsonNumbers.IsOneItem).ToList();

        UpdateChoices(order);

        var changing = new List<(string Path, List<double> Values)>();
        foreach (var path in order)
        {
            var values = series[path];
            values.Reverse();
            if (_chosen?.Contains(path) ?? (!JsonNumbers.IsOneItem(path) && IsChanging(values) && !IsClock(values)))
                changing.Add((path, values));
        }

        foreach (var choice in Choices)
        {
            choice.Show(changing.Exists(c => c.Path == choice.Path));
            choice.LatestText = series[choice.Path][^1].ToString("0.###");
        }

        var shown = Math.Min(changing.Count, MaxTrendsShown);
        var same = Trends.Count == shown;
        for (var i = 0; same && i < shown; i++)
            same = Trends[i].Path == changing[i].Path;

        if (same)
        {
            for (var i = 0; i < shown; i++)
                Trends[i].Update(changing[i].Values);
        }
        else
        {
            Trends.Clear();
            for (var i = 0; i < shown; i++)
                Trends.Add(new FieldTrendViewModel(changing[i].Path, changing[i].Values));
            OnPropertyChanged(nameof(HasTrend));
        }

        MoreTrends = changing.Count - shown;
    }

    [RelayCommand]
    private void UseAutomatic()
    {
        ChosenPaths = null;
        UpdateTrends();
        ChosenChanged?.Invoke(this);
    }

    private void OnChoiceToggled(FieldChoiceViewModel choice)
    {
        // The first choice starts from what was being drawn; numbers not on the list stay chosen.
        var chosen = _chosen ?? Choices.Where(c => c.IsChosen).Select(c => c.Path).ToHashSet(StringComparer.Ordinal);
        if (choice.IsChosen)
            chosen.Add(choice.Path);
        else
            chosen.Remove(choice.Path);

        ChosenPaths = chosen;
        UpdateTrends();
        ChosenChanged?.Invoke(this);
    }

    private void UpdateChoices(List<string> order)
    {
        if (Choices.Select(c => c.Path).SequenceEqual(order))
            return;

        var existing = Choices.ToDictionary(c => c.Path, StringComparer.Ordinal);
        Choices.Clear();
        foreach (var path in order)
            Choices.Add(existing.GetValueOrDefault(path) ?? new FieldChoiceViewModel(path, OnChoiceToggled));
        OnPropertyChanged(nameof(HasNumbers));
    }

    private static bool IsChanging(List<double> values)
    {
        foreach (var value in values)
        {
            if (Math.Abs(value - values[0]) > 1e-9)
                return true;
        }

        return false;
    }

    // A timestamp always changes and only ever climbs, so a chart of it says nothing: Unix seconds
    // or milliseconds from about 2001 on, never going down.
    private static bool IsClock(List<double> values)
    {
        var seconds = values[0] is >= 1e9 and < 1e10;
        var milliseconds = values[0] is >= 1e12 and < 1e13;
        if (!seconds && !milliseconds)
            return false;

        for (var i = 1; i < values.Count; i++)
        {
            if (values[i] < values[i - 1] || (seconds ? values[i] >= 1e10 : values[i] >= 1e13))
                return false;
        }

        return true;
    }
}
