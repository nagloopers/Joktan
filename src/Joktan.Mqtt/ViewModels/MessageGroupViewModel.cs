using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using Joktan.Core.ViewModels;
using Joktan.Mqtt.Resources;

namespace Joktan.Mqtt.ViewModels;

/// <summary>
/// A group in the folder message list, with one switch for every message in it and in its
/// sub-groups. Groups nest by their path: "Plant/Line 1" is the group "Line 1" inside "Plant".
/// The list holds the groups flattened in tree order, each with its depth.
/// </summary>
public partial class MessageGroupViewModel : ViewModelBase
{
    public static string UngroupedName => Strings.MessageGroup_Ungrouped;

    private const double IndentPerLevel = 20;

    private readonly Action<MessageGroupViewModel, bool> _setEnabled;

    public MessageGroupViewModel(
        string path, MessageGroupViewModel? parent, IReadOnlyList<FolderMessageRowViewModel> rows, bool isExpanded,
        Action<MessageGroupViewModel, bool> setEnabled)
    {
        Path = path;
        Parent = parent;
        Rows = rows;
        IsExpanded = isExpanded;
        _setEnabled = setEnabled;
        Depth = parent is null ? 0 : parent.Depth + 1;
        parent?.Children.Add(this);
    }

    /// <summary>The group path as stored in the message settings; empty for ungrouped messages.</summary>
    public string Path { get; }

    public MessageGroupViewModel? Parent { get; }

    public List<MessageGroupViewModel> Children { get; } = [];

    public int Depth { get; }

    public string DisplayName => Path.Length == 0 ? UngroupedName : Path[(Path.LastIndexOf('/') + 1)..];

    public Thickness Indent => new(Depth * IndentPerLevel, 0, 0, 0);

    /// <summary>The messages directly in this group, not in its sub-groups.</summary>
    public IReadOnlyList<FolderMessageRowViewModel> Rows { get; }

    /// <summary>The messages in this group and all its sub-groups.</summary>
    public IEnumerable<FolderMessageRowViewModel> AllRows => Rows.Concat(Children.SelectMany(c => c.AllRows));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowRows))]
    public partial bool IsExpanded { get; set; }

    /// <summary>Shown while every group it is inside is expanded.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowRows))]
    public partial bool IsVisible { get; set; } = true;

    public bool ShowRows => IsVisible && IsExpanded && Rows.Count > 0;

    public bool CanSwitch => AllRows.Any(r => r.CanSwitch);

    public string Summary
    {
        get
        {
            var rows = AllRows.ToList();
            var switchable = rows.Count(r => r.CanSwitch);
            var on = rows.Count(r => r.IsOn);
            var count = rows.Count == 1 ? Strings.MessageGroup_1Message : string.Format(Strings.MessageGroup_Messages, rows.Count);
            return switchable == 0 ? count : string.Format(Strings.MessageGroup_On, count, on);
        }
    }

    /// <summary>On when every message below it that can be switched is on. Changing it switches them all.</summary>
    public bool IsOn
    {
        get => CanSwitch && AllRows.Where(r => r.CanSwitch).All(r => r.IsOn);
        set
        {
            if (value != IsOn && CanSwitch)
                _setEnabled(this, value);
        }
    }

    /// <summary>Re-reads the switch and the summary after messages in the group were switched.</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(IsOn));
        OnPropertyChanged(nameof(Summary));
    }

    /// <summary>Shows or hides this group's sub-groups to match the expanded state of the groups above.</summary>
    public void UpdateVisibility()
    {
        IsVisible = Parent is null || (Parent.IsVisible && Parent.IsExpanded);
        foreach (var child in Children)
            child.UpdateVisibility();
    }

    /// <summary>
    /// Arranges rows under their groups. Returns every group in tree order: named groups sorted
    /// level by level, including groups that only hold sub-groups, then the ungrouped messages.
    /// </summary>
    public static List<MessageGroupViewModel> Build(
        IEnumerable<FolderMessageRowViewModel> rows, Func<string, bool> isExpanded,
        Action<MessageGroupViewModel, bool> setEnabled)
    {
        var byGroup = rows
            .GroupBy(r => r.Message.Settings.Group, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        // Every group path and the paths above it, so "Plant/Line 1" also makes "Plant".
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in byGroup.Keys.Where(k => k.Length > 0))
        {
            var levels = path.Split('/');
            for (var i = 1; i <= levels.Length; i++)
                paths.Add(string.Join('/', levels[..i]));
        }

        var groups = new List<MessageGroupViewModel>();
        var made = new Dictionary<string, MessageGroupViewModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths.Order(PathComparer.Instance))
        {
            var slash = path.LastIndexOf('/');
            var parent = slash < 0 ? null : made[path[..slash]];
            var group = new MessageGroupViewModel(
                path, parent, byGroup.GetValueOrDefault(path) ?? [], isExpanded(path), setEnabled);
            made[path] = group;
            groups.Add(group);
        }

        if (byGroup.TryGetValue("", out var ungrouped))
            groups.Add(new MessageGroupViewModel("", null, ungrouped, isExpanded(""), setEnabled));

        foreach (var group in groups.Where(g => g.Parent is null))
            group.UpdateVisibility();
        return groups;
    }

    // Sorts level by level, so a group comes straight before its sub-groups.
    private sealed class PathComparer : IComparer<string>
    {
        public static readonly PathComparer Instance = new();

        public int Compare(string? x, string? y)
        {
            var a = (x ?? "").Split('/');
            var b = (y ?? "").Split('/');
            for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
            {
                var c = StringComparer.OrdinalIgnoreCase.Compare(a[i], b[i]);
                if (c != 0)
                    return c;
            }

            return a.Length.CompareTo(b.Length);
        }
    }
}
