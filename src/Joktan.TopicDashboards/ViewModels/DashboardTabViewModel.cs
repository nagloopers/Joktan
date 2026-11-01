using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Core.ViewModels;
using Joktan.Mqtt.ViewModels;
using Joktan.TopicDashboards.Resources;

namespace Joktan.TopicDashboards.ViewModels;

/// <summary>
/// One dashboard, on its own tab: its format and its graphs. A dashboard whose graphs have a key shown as
/// tabs has one of these per value of that key, all sharing what is saved, so they cannot come to differ.
/// </summary>
public partial class DashboardTabViewModel : ViewModelBase
{
    private readonly TopicDashboardsViewModel _page;

    public DashboardTabViewModel(DashboardProfile profile, TopicDashboardsViewModel page, string? tabKey = null, string? tabValue = null)
    {
        Profile = profile;
        TabKey = tabKey;
        TabValue = tabValue;
        Name = profile.Name;
        Columns = profile.Columns;
        RowHeight = profile.RowHeight;
        Gap = profile.Gap;
        Background = profile.Background;
        TabColor = profile.TabColor;

        // Set last, so that loading these does not count as changing them.
        _page = page;
        Arrange();
    }

    public DashboardProfile Profile { get; }

    /// <summary>The key this tab shows one value of, if it does.</summary>
    public string? TabKey { get; private set; }

    /// <summary>The value of the tab key it shows.</summary>
    public string? TabValue { get; private set; }

    public bool HasTabValue => TabValue is not null;

    /// <summary>What says which tab to open next time.</summary>
    public string SelectionName => TabValue is null ? Name : $"{Name}\u001f{TabValue}";

    public ObservableCollection<GraphCardViewModel> Graphs { get; } = [];

    /// <summary>The clients this dashboard's graphs subscribe through, that exist.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<MqttClientViewModel> Clients { get; private set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    public partial string Name { get; set; }

    public string DisplayName
    {
        get
        {
            var name = Name.Trim() is { Length: > 0 } text ? text : Strings.DashboardTab_Untitled;
            if (TabKey is null || TabValue is null)
                return name;

            var filled = new SeriesKeys([new(TabKey, TabValue)]).Fill(name, out var any);
            return any ? filled : $"{name}: {TabValue}";
        }
    }

    [ObservableProperty]
    public partial int Columns { get; set; }

    [ObservableProperty]
    public partial double RowHeight { get; set; }

    [ObservableProperty]
    public partial double Gap { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BackgroundBrush))]
    public partial string Background { get; set; }

    public object BackgroundBrush => TabColors.BrushOrUnset(Background);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TabFillBrush))]
    [NotifyPropertyChangedFor(nameof(TabForegroundBrush))]
    public partial string TabColor { get; set; }

    public object TabFillBrush => TabColors.BrushOrUnset(TabColor);

    public IBrush? TabForegroundBrush => TabColors.ReadableOn(TabColor);

    /// <summary>Whether the format settings are open.</summary>
    [ObservableProperty]
    public partial bool IsFormatting { get; set; }

    /// <summary>Whether graphs can be moved, resized, edited and removed.</summary>
    [ObservableProperty]
    public partial bool IsArranging { get; set; }

    /// <summary>Whether the name is being typed on the tab.</summary>
    [ObservableProperty]
    public partial bool IsRenaming { get; set; }

    /// <summary>The name typed on the tab, which replaces <see cref="Name"/> once it is finished.</summary>
    [ObservableProperty]
    public partial string RenameText { get; set; } = "";

    [ObservableProperty]
    public partial bool IsConfirmingRemove { get; set; }

    public bool HasNoGraphs => Graphs.Count == 0;

    public string RemoveQuestion
    {
        get
        {
            var tabs = _page.TabsOf(Profile).Count();
            var count = Profile.Graphs.Count;
            var question = (tabs, count) switch
            {
                (> 1, _) => string.Format(Strings.DashboardTab_RemoveThisDashboardAndAllOf, tabs),
                (_, 0) => Strings.DashboardTab_RemoveThisDashboard,
                (_, 1) => Strings.DashboardTab_RemoveThisDashboardAndItsGraph,
                _ => string.Format(Strings.DashboardTab_RemoveThisDashboardAndItsGraphs, count),
            };
            return _page.Dashboards.Count > tabs ? question : string.Format(Strings.DashboardTab_AnEmptyOneTakesItsPlace, question);
        }
    }

    public static IReadOnlyList<int> ColumnChoices { get; } = Enumerable.Range(1, DashboardProfile.MaxColumns).ToList();

    public static IReadOnlyList<Swatch> Palette => TabColors.Palette;

    public double MinRowHeight => DashboardProfile.MinRowHeight;

    public double MaxRowHeight => DashboardProfile.MaxRowHeight;

    public double MaxGap => DashboardProfile.MaxGap;

    /// <summary>Moves a graph to the place of another. Those made from one saved graph stay together.</summary>
    public void MoveTo(GraphCardViewModel card, int index)
    {
        if (!Graphs.Contains(card) || Graphs.Count == 0)
            return;

        var target = Graphs[Math.Clamp(index, 0, Graphs.Count - 1)];
        var from = Profile.Graphs.IndexOf(card.Profile);
        var to = Profile.Graphs.IndexOf(target.Profile);
        if (from < 0 || to < 0 || from == to)
            return;

        Profile.Graphs.RemoveAt(from);
        Profile.Graphs.Insert(to, card.Profile);
        _page.Arrange(Profile);
        _page.Save();
    }

    /// <summary>Makes the graphs on the tab match the saved graphs, and what they have seen.</summary>
    public void Arrange()
    {
        var wanted = new List<(GraphProfile Profile, SeriesKeys Cell)>();
        foreach (var graph in Profile.Graphs)
        {
            foreach (var cell in _page.SourceFor(graph).CellsFor(TabKey, TabValue))
                wanted.Add((graph, cell));
        }

        var cards = new List<GraphCardViewModel>();
        foreach (var (graph, cell) in wanted)
            cards.Add(Graphs.FirstOrDefault(c => c.Profile == graph && c.Cell.Equals(cell)) ?? NewCard(graph, cell));

        foreach (var card in Graphs.Where(c => !cards.Contains(c)).ToList())
            Graphs.Remove(card);

        for (var i = 0; i < cards.Count; i++)
        {
            var at = Graphs.IndexOf(cards[i]);
            if (at < 0)
                Graphs.Insert(i, cards[i]);
            else if (at != i)
                Graphs.Move(at, i);
        }

        OnPropertyChanged(nameof(HasNoGraphs));
    }

    /// <summary>Makes this tab, which was waiting for a value of its key, show one.</summary>
    public void ShowTabValue(string? tabKey, string? tabValue)
    {
        TabKey = tabKey;
        TabValue = tabValue;
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(HasTabValue));
    }

    /// <summary>Takes what another tab of this dashboard changed.</summary>
    public void TakeFormatFrom(DashboardTabViewModel other)
    {
        Name = other.Name;
        Columns = other.Columns;
        RowHeight = other.RowHeight;
        Gap = other.Gap;
        Background = other.Background;
        TabColor = other.TabColor;
    }

    /// <summary>Lets go of its graphs, when the tab goes.</summary>
    public void Detach() => Graphs.Clear();

    /// <summary>Says which clients exist now, and how each graph stands with its own.</summary>
    public void UpdateClients()
    {
        Clients = Graphs
            .Select(g => _page.FindClient(g.Profile.Client))
            .OfType<MqttClientViewModel>()
            .Distinct()
            .ToList();

        foreach (var card in Graphs)
            card.Status = _page.StatusOf(card);
    }

    /// <summary>Puts away what the page's edit mode opened on this tab.</summary>
    public void EndEditing()
    {
        FinishRename();
        IsConfirmingRemove = false;
    }

    partial void OnNameChanged(string value)
    {
        Profile.Name = value;
        _page?.Save();
        _page?.SyncFormat(this);
    }

    partial void OnColumnsChanged(int value)
    {
        Profile.Columns = value;
        _page?.Save();
        _page?.SyncFormat(this);
    }

    partial void OnRowHeightChanged(double value)
    {
        Profile.RowHeight = (int)value;
        _page?.Save();
        _page?.SyncFormat(this);
    }

    partial void OnGapChanged(double value)
    {
        Profile.Gap = (int)value;
        _page?.Save();
        _page?.SyncFormat(this);
    }

    partial void OnBackgroundChanged(string value)
    {
        Profile.Background = value;
        _page?.Save();
        _page?.SyncFormat(this);
    }

    partial void OnTabColorChanged(string value)
    {
        Profile.TabColor = value;
        _page?.Save();
        _page?.SyncFormat(this);
    }

    [RelayCommand]
    private void PickBackground(string hex) => Background = hex;

    [RelayCommand]
    private void PickTabColor(string hex) => TabColor = hex;

    [RelayCommand]
    private void Rename()
    {
        _page.SelectedDashboard = this;
        RenameText = Name;
        IsRenaming = true;
    }

    /// <summary>Takes the name typed, unless nothing was.</summary>
    [RelayCommand]
    private void FinishRename()
    {
        if (!IsRenaming)
            return;

        IsRenaming = false;
        if (RenameText.Trim() is { Length: > 0 } name)
            Name = name;
    }

    [RelayCommand]
    private void CancelRename() => IsRenaming = false;

    [RelayCommand]
    private void AskRemove()
    {
        _page.SelectedDashboard = this;
        OnPropertyChanged(nameof(RemoveQuestion));
        IsConfirmingRemove = true;
    }

    [RelayCommand]
    private void KeepDashboard() => IsConfirmingRemove = false;

    [RelayCommand]
    private void Remove()
    {
        IsConfirmingRemove = false;
        _page.Remove(this);
    }

    [RelayCommand]
    private void AddGraph() => _page.AddGraph(this);

    /// <summary>Forgets the value this tab shows, until it is seen again.</summary>
    [RelayCommand]
    private void ForgetValue()
    {
        if (TabKey is not null && TabValue is not null)
            _page.Forget(Profile, new SeriesKeys([new(TabKey, TabValue)]));
    }

    private GraphCardViewModel NewCard(GraphProfile graph, SeriesKeys cell)
    {
        var source = _page.SourceFor(graph);
        var card = new GraphCardViewModel(graph, source.Data, cell)
        {
            EditRequested = c => _page.EditGraph(this, c),
            RemoveRequested = c => _page.RemoveGraph(Profile, c.Profile),
            MoveRequested = Step,
            ForgetRequested = c => _page.Forget(Profile, c.Cell),
            SpanChanged = OnSpanChanged,
            DescribeHealth = () => source.Health.Describe(DateTime.Now),
        };
        card.RemoveAsked = c =>
        {
            var copies = Graphs.Count(g => g.Profile == c.Profile);
            c.RemoveQuestion = copies > 1 ? string.Format(Strings.DashboardTab_RemoveTheseGraphs, copies) : Strings.DashboardTab_RemoveThisGraph;
        };
        return card;
    }

    // Past those made from the same saved graph, which stay together.
    private void Step(GraphCardViewModel card, int by)
    {
        var index = Graphs.IndexOf(card) + by;
        while (index >= 0 && index < Graphs.Count && Graphs[index].Profile == card.Profile)
            index += by;
        if (index >= 0 && index < Graphs.Count)
            MoveTo(card, index);
    }

    // Those made from one saved graph are the same size, on every tab.
    private void OnSpanChanged(GraphCardViewModel card)
    {
        foreach (var tab in _page.TabsOf(Profile))
        {
            foreach (var other in tab.Graphs.Where(g => g.Profile == card.Profile && g != card))
            {
                other.WidthSpan = card.WidthSpan;
                other.HeightSpan = card.HeightSpan;
            }
        }

        _page.Save();
    }
}
