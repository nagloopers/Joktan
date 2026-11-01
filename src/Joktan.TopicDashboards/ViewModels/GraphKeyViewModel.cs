using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Core.ViewModels;
using Joktan.TopicDashboards.Resources;

namespace Joktan.TopicDashboards.ViewModels;

public sealed record KeyShowChoice(KeyShow Show, string Name);

/// <summary>One key offered in the graph editor, with how its values are shown.</summary>
public partial class GraphKeyViewModel : ViewModelBase
{
    private readonly Action _changed;
    private readonly Action<GraphKeyViewModel> _remove;
    private bool _loading = true;

    public GraphKeyViewModel(GraphKey key, Action changed, Action<GraphKeyViewModel> remove)
    {
        Name = key.Name;
        Field = key.Field;
        _changed = changed;
        _remove = remove;
        SelectedShow = Choices.First(c => c.Show == key.Show);
        OnlyValue = key.Only;
        _loading = false;
    }

    public string Name { get; }

    /// <summary>The field it comes from; empty when it comes from the topic or a path.</summary>
    public string Field { get; }

    /// <summary>Whether it can be taken off: one from the topic or a path goes when they do.</summary>
    public bool CanRemove => Field.Length > 0;

    public static IReadOnlyList<KeyShowChoice> Choices { get; } =
    [
        new(KeyShow.Line, Strings.GraphKey_OneLine),
        new(KeyShow.Graph, Strings.GraphKey_OneGraph),
        new(KeyShow.Tab, Strings.GraphKey_OneTab),
        new(KeyShow.Only, Strings.GraphKey_Only),
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOnly))]
    public partial KeyShowChoice SelectedShow { get; set; }

    public bool IsOnly => SelectedShow.Show == KeyShow.Only;

    [ObservableProperty]
    public partial string OnlyValue { get; set; } = "";

    /// <summary>The values seen so far.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<string> Values { get; private set; } = [];

    [ObservableProperty]
    public partial string SeenText { get; private set; } = Strings.GraphKey_NothingYet;

    public GraphKey ToKey() => new() { Name = Name, Field = Field, Show = SelectedShow.Show, Only = OnlyValue };

    public void ShowValues(IEnumerable<string> values)
    {
        var list = values.ToList();
        if (list.SequenceEqual(Values))
            return;

        Values = list;
        SeenText = list.Count == 0 ? Strings.GraphKey_NothingYet
            : list.Count <= 4 ? string.Join(", ", list)
            : string.Format(Strings.GraphKey_AndMore, string.Join(", ", list.Take(3)), list.Count - 3);
    }

    partial void OnSelectedShowChanged(KeyShowChoice value)
    {
        if (!_loading)
            _changed();
    }

    partial void OnOnlyValueChanged(string value)
    {
        if (!_loading)
            _changed();
    }

    [RelayCommand]
    private void Remove() => _remove(this);
}

/// <summary>One level of a topic in the graph editor, which pressing makes a key.</summary>
public sealed record TopicLevelViewModel(string Text, bool IsKey, TopicEntryViewModel Entry, int Index);

/// <summary>A topic in the graph editor, with its levels shown as buttons.</summary>
public partial class TopicEntryViewModel : ViewModelBase
{
    private readonly Action<TopicEntryViewModel, int> _makeKey;

    public TopicEntryViewModel(string text, Action<TopicEntryViewModel, int> makeKey)
    {
        Text = text;
        _makeKey = makeKey;
        Levels = text.Split('/').Select((level, i) => new TopicLevelViewModel(level, level is "+" or "#" || level.StartsWith('{'), this, i)).ToList();
    }

    public string Text { get; }

    public IReadOnlyList<TopicLevelViewModel> Levels { get; }

    [RelayCommand]
    private void MakeKey(TopicLevelViewModel level)
    {
        if (!level.IsKey)
            _makeKey(this, level.Index);
    }
}

/// <summary>A field that seems to tell a topic's numbers apart, offered as a key.</summary>
public sealed partial class KeySuggestion(string text, string field, Action<string> add) : ViewModelBase
{
    public string Text { get; } = text;

    [RelayCommand]
    private void Add() => add(field);
}

/// <summary>A line from a key on the graph being edited, which can be taken off or brought back.</summary>
public sealed partial class GraphLineViewModel(string id, string label, int slot, bool isRemoved, bool canRemove, Action<string, bool> setRemoved) : ViewModelBase
{
    public string Id { get; } = id;

    public string Label { get; } = label;

    public int Slot { get; } = slot;

    public bool IsRemoved { get; } = isRemoved;

    /// <summary>Whether the × is offered, as the last line on a graph stays.</summary>
    public bool CanRemove { get; } = !isRemoved && canRemove;

    [RelayCommand]
    private void Remove() => setRemoved(Id, true);

    [RelayCommand]
    private void BringBack() => setRemoved(Id, false);
}
