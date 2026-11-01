using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Core.ViewModels;
using Joktan.TopicDashboards.Resources;

namespace Joktan.TopicDashboards.ViewModels;

/// <summary>One number offered in the graph editor, with its name and colour if it is drawn.</summary>
public partial class GraphValueChoiceViewModel : ViewModelBase
{
    private readonly Action<GraphValueChoiceViewModel> _toggled;
    private readonly Action _changed;
    private readonly Action<GraphValueChoiceViewModel>? _useKey;
    private readonly Action<GraphValueChoiceViewModel>? _remove;
    private bool _settingFromEditor;

    /// <param name="useKey">Replaces a number chosen by its position with the same one chosen by a key.</param>
    public GraphValueChoiceViewModel(string topic, string path, int color, Action<GraphValueChoiceViewModel> toggled, Action changed,
        Action<GraphValueChoiceViewModel>? useKey = null, Action<GraphValueChoiceViewModel>? remove = null)
    {
        Topic = topic;
        Path = path;
        _toggled = toggled;
        _changed = changed;
        _useKey = useKey;
        _remove = remove;
        Color = color;
    }

    public string Topic { get; }

    /// <summary>Where the number is in the payload; empty when it is the whole payload.</summary>
    public string Path { get; }

    public string DisplayPath => Path.Length == 0 ? Strings.GraphValueChoice_TheWholePayload : Path;

    public string Kind => Path.Contains('{') ? Strings.GraphValueChoice_Linked
        : Path.Contains("=*]", StringComparison.Ordinal) ? Strings.GraphValueChoice_Each
        : Path.Contains("[*]", StringComparison.Ordinal) ? Strings.GraphValueChoice_Array
        : Path.Contains('[') ? Strings.GraphValueChoice_Item
        : Strings.GraphValueChoice_Value;

    /// <summary>Whether it looks like a time or a counter of messages, so is not ticked by itself.</summary>
    public bool LooksLikeTime { get; set; }

    /// <summary>What the <c>unit</c> fields beside it say.</summary>
    public IReadOnlyList<string> Units { get; set; } = [];

    /// <summary>The same number chosen by a key, when this one depends on the order of an array.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBetterPath))]
    [NotifyPropertyChangedFor(nameof(BetterPathText))]
    public partial string? BetterPath { get; set; }

    public bool HasBetterPath => IsChosen && BetterPath is not null;

    public string BetterPathText => string.Format(Strings.GraphValueChoice_ThisDependsOnTheOrderOf, BetterPath);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBetterPath))]
    public partial bool IsChosen { get; set; }

    [ObservableProperty]
    public partial string Label { get; set; } = "";

    /// <summary>Which of the graph colours it is drawn in.</summary>
    [ObservableProperty]
    public partial int Color { get; set; }

    [ObservableProperty]
    public partial string LatestText { get; set; } = "";

    /// <summary>Ticks or clears it, without counting as the user's doing.</summary>
    public void Show(bool chosen)
    {
        _settingFromEditor = true;
        IsChosen = chosen;
        _settingFromEditor = false;
    }

    partial void OnIsChosenChanged(bool value)
    {
        if (!_settingFromEditor)
            _toggled(this);
    }

    partial void OnLabelChanged(string value) => _changed();

    partial void OnColorChanged(int value) => _changed();

    [RelayCommand]
    private void UseBetterPath() => _useKey?.Invoke(this);

    [RelayCommand]
    private void Remove() => _remove?.Invoke(this);
}
