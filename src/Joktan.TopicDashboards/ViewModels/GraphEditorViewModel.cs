using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Core.ViewModels;
using Joktan.Mqtt;
using Joktan.Mqtt.ViewModels;
using Joktan.TopicDashboards.Resources;

namespace Joktan.TopicDashboards.ViewModels;

public sealed record GraphKindChoice(GraphKind Kind, string Name, string Description);

public sealed record SpanChoice(int Minutes, string Name);

public sealed record DecimalsChoice(int Places, string Name);

public sealed record ColorChoice(int Slot, string Name);

/// <summary>What the graph editor is making: a new graph, or more of one already on the dashboard.</summary>
public sealed record GraphTarget(GraphCardViewModel? Card, string Name);

/// <summary>
/// The graph editor: subscribes to a graph's topics while it is open, offers the numbers and keys that
/// arrive, and previews the graph as it is set up. It keeps the messages it hears, so that a change to the
/// keys can be shown at once on what has already arrived.
/// </summary>
public partial class GraphEditorViewModel : ViewModelBase
{
    private const string SubscriptionOwner = "dashboard-editor";
    private const int MaxMessages = 1000;
    private const int MaxSuggestedValues = 32;
    private const int MessagesToJudgeUnique = 20;

    private readonly TopicDashboardsViewModel _page;
    private readonly ConcurrentQueue<ReceivedMessage> _incoming = new();
    private readonly Dictionary<(string Topic, string Path), GraphValueChoiceViewModel> _rows = [];
    private readonly HashSet<string> _picked = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _timer;
    private readonly List<ReceivedMessage> _messages = [];
    private readonly Dictionary<string, List<string>> _offered = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _fieldValues = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _fieldMessages = new(StringComparer.Ordinal);

    // What the topic explorer started this editor with, if it did.
    private readonly MqttClientViewModel? _seedClient;
    private readonly string? _seedTopic;
    private readonly IReadOnlyList<ReceivedMessage> _seedHistory = [];
    private readonly IReadOnlyList<string> _seedPaths = [];

    private GraphCardViewModel? _card;
    private GraphSource _source = new(new GraphProfile(), new GraphData());
    private GraphData? _loadedData;
    private List<Dictionary<string, string>> _loadedSeen = [];
    private string _loadedIdentity = "";
    private string _loadedPlaces = "";
    private string _built = "";
    private List<Dictionary<string, string>> _seen = [];
    private Dictionary<string, int> _colors = [];
    private List<string> _removedLines = [];
    private GraphKeyViewModel? _keyToSplit;
    private MqttConnection? _listeningOn;
    private volatile TopicFilterSet _listeningTo = new([]);
    private string? _listenError;
    private bool _loaded;
    private bool _stopped;
    private int _heard;
    private DateTime _heardSince = DateTime.Now;
    private int _perSecond;

    private GraphEditorViewModel(DashboardTabViewModel? dashboard, TopicDashboardsViewModel page)
    {
        _page = page;
        Clients = page.Mqtt.Clients;
        Dashboards = page.Dashboards;
        SelectedDashboard = dashboard ?? page.SelectedDashboard;
        SelectedClient = page.Mqtt.SelectedClient ?? page.Mqtt.Clients.FirstOrDefault();
        SelectedKind = Kinds[0];
        SelectedSpan = Spans[1];
        SelectedDecimals = DecimalChoices[0];
        Preview = new GraphCardViewModel(new GraphProfile(), new GraphData());
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(150), DispatcherPriority.Background, OnTick);
        _timer.Start();
    }

    /// <summary>A new graph, starting from nothing.</summary>
    public GraphEditorViewModel(TopicDashboardsViewModel page, DashboardTabViewModel dashboard)
        : this(dashboard, page)
    {
        Load(null);
    }

    /// <summary>A topic seen in the topic explorer, for a new graph or one on the dashboard.</summary>
    /// <param name="history">The messages already received on it, oldest first.</param>
    public GraphEditorViewModel(TopicDashboardsViewModel page, MqttClientViewModel client, string topic,
        IEnumerable<ReceivedMessage> history, IEnumerable<string> paths)
        : this(null, page)
    {
        _seedClient = client;
        _seedTopic = topic;
        _seedHistory = history.ToList();
        _seedPaths = paths.ToList();
        UpdateTargets();
        Load(null);
    }

    /// <summary>A graph already on a dashboard.</summary>
    public GraphEditorViewModel(TopicDashboardsViewModel page, DashboardTabViewModel dashboard, GraphCardViewModel card)
        : this(dashboard, page)
    {
        Load(card);
    }

    /// <summary>Saved or not, once the window can close.</summary>
    public event Action<bool>? CloseRequested;

    public IReadOnlyList<MqttClientViewModel> Clients { get; }

    public IReadOnlyList<DashboardTabViewModel> Dashboards { get; }

    public GraphData Data => _source.Data;

    /// <summary>The topics of the graph, each subscribed to while the editor is open. Each can hold wildcards and keys.</summary>
    public ObservableCollection<string> Topics { get; } = [];

    /// <summary>The same topics, with their levels to press.</summary>
    public ObservableCollection<TopicEntryViewModel> TopicEntries { get; } = [];

    public bool HasSeveralTopics => Topics.Count > 1;

    public bool HasTopics => Topics.Count > 0;

    /// <summary>What tells the numbers apart, and how each is shown.</summary>
    public ObservableCollection<GraphKeyViewModel> Keys { get; } = [];

    public bool HasKeys => Keys.Count > 0;

    /// <summary>The lines keys make on the graph in the preview, which can be taken off one by one.</summary>
    public ObservableCollection<GraphLineViewModel> Lines { get; } = [];

    public bool HasLines => Lines.Count > 0;

    /// <summary>Fields that seem to tell the numbers apart, and are not keys yet.</summary>
    public ObservableCollection<KeySuggestion> Suggestions { get; } = [];

    /// <summary>The fields of the messages that could be added as keys.</summary>
    public ObservableCollection<string> FieldChoices { get; } = [];

    public bool HasFieldChoices => FieldChoices.Count > 0;

    [ObservableProperty]
    public partial string? SelectedField { get; set; }

    /// <summary>How many tabs, graphs and lines this makes, when it is more than one of each.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOutcome))]
    public partial string OutcomeText { get; private set; } = "";

    public bool HasOutcome => OutcomeText.Length > 0;

    /// <summary>Advice when the numbers on one graph are unlike each other.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnitHint))]
    public partial string UnitHint { get; private set; } = "";

    public bool HasUnitHint => UnitHint.Length > 0;

    /// <summary>Whether the graph editor started from a topic in the topic explorer, which can join a graph already there.</summary>
    public bool CanChooseTarget => _seedTopic is not null;

    /// <summary>What can be made from the topic explorer's topic on the dashboard chosen.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<GraphTarget> Targets { get; private set; } = [];

    [ObservableProperty]
    public partial GraphTarget? SelectedTarget { get; set; }

    /// <summary>The graph as it would look.</summary>
    public GraphCardViewModel Preview { get; }

    public bool IsNew => _card is null;

    /// <summary>Whether the dashboard can be chosen, which is not so for a graph already on one, unless the choice is which graph.</summary>
    public bool CanChooseDashboard => IsNew || CanChooseTarget;

    public string WindowTitle => IsNew ? Strings.GraphEditor_AddGraph : CanChooseTarget ? Strings.GraphEditor_AddToGraph : Strings.GraphEditor_EditGraph;

    public string SaveText => IsNew ? Strings.GraphEditor_AddToDashboard : CanChooseTarget ? Strings.GraphEditor_AddToGraph : Strings.GraphEditor_Save;

    public IReadOnlyList<GraphKindChoice> Kinds { get; } =
    [
        new(GraphKind.Line, Strings.GraphEditor_Line, Strings.GraphEditor_HowValuesMovedOverTimeUse),
        new(GraphKind.Area, Strings.GraphEditor_Area, Strings.GraphEditor_ALineFilledDownToZero),
        new(GraphKind.Bars, Strings.GraphEditor_Bars, Strings.GraphEditor_OneNumberItsRecentReadingsSeveral),
        new(GraphKind.Gauge, Strings.GraphEditor_Gauge, Strings.GraphEditor_TheLatestValueOnADial),
        new(GraphKind.Value, Strings.GraphEditor_Number, Strings.GraphEditor_TheLatestValueInLargeType),
    ];

    public IReadOnlyList<SpanChoice> Spans { get; } =
    [
        new(1, Strings.GraphEditor_1Minute),
        new(5, Strings.GraphEditor_5Minutes),
        new(15, Strings.GraphEditor_15Minutes),
        new(30, Strings.GraphEditor_30Minutes),
        new(60, Strings.GraphEditor_1Hour),
        new(360, Strings.GraphEditor_6Hours),
        new(1440, Strings.GraphEditor_24Hours),
    ];

    public IReadOnlyList<DecimalsChoice> DecimalChoices { get; } =
    [
        new(-1, Strings.GraphEditor_Automatic),
        new(0, "0"),
        new(1, "1"),
        new(2, "2"),
        new(3, "3"),
    ];

    public static IReadOnlyList<ColorChoice> ColorChoices { get; } = GraphColors.Names.Select((name, slot) => new ColorChoice(slot, name)).ToList();

    public IReadOnlyList<int> Sizes { get; } = Enumerable.Range(1, GraphProfile.MaxHeight).ToList();

    public ObservableCollection<GraphValueChoiceViewModel> Values { get; } = [];

    [ObservableProperty]
    public partial MqttClientViewModel? SelectedClient { get; set; }

    /// <summary>A topic typed in, to add to the graph.</summary>
    [ObservableProperty]
    public partial string NewTopic { get; set; } = "";

    [ObservableProperty]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KindDescription))]
    public partial GraphKindChoice SelectedKind { get; set; }

    public string KindDescription => SelectedKind.Description;

    [ObservableProperty]
    public partial string Unit { get; set; } = "";

    [ObservableProperty]
    public partial SpanChoice SelectedSpan { get; set; }

    [ObservableProperty]
    public partial DecimalsChoice SelectedDecimals { get; set; }

    /// <summary>Whether the scale stays between the numbers given, rather than fitting the values.</summary>
    [ObservableProperty]
    public partial bool UseFixedScale { get; set; }

    [ObservableProperty]
    public partial string MinimumText { get; set; } = "";

    [ObservableProperty]
    public partial string MaximumText { get; set; } = "";

    [ObservableProperty]
    public partial int WidthSpan { get; set; } = 1;

    [ObservableProperty]
    public partial int HeightSpan { get; set; } = 1;

    [ObservableProperty]
    public partial DashboardTabViewModel? SelectedDashboard { get; set; }

    /// <summary>Whether the graph goes on a new dashboard, rather than the one selected.</summary>
    [ObservableProperty]
    public partial bool IsNewDashboard { get; set; }

    [ObservableProperty]
    public partial string NewDashboardName { get; set; } = "";

    /// <summary>A path to add by hand, for a topic with nothing arriving on it yet.</summary>
    [ObservableProperty]
    public partial string ManualPath { get; set; } = "";

    /// <summary>The topic a path added by hand is on.</summary>
    [ObservableProperty]
    public partial string? ManualTopic { get; set; }

    public bool IsListening => _listeningOn is not null && _listeningTo.Count > 0;

    [ObservableProperty]
    public partial string ListenStatus { get; private set; } = "";

    public bool HasNoNumbers => Values.Count == 0;

    [ObservableProperty]
    public partial string? Error { get; private set; }

    /// <summary>Adds the topic typed in, and subscribes to it for as long as the editor is open.</summary>
    [RelayCommand]
    private async Task AddTopicAsync()
    {
        if (TryAddNewTopic() is { } problem)
        {
            Error = problem;
            return;
        }

        await ListenAsync();
    }

    // Returns what is wrong with the topic typed in, or null.
    private string? TryAddNewTopic()
    {
        var topic = NewTopic.Trim();
        if (TopicPattern.Validate(topic) is { } problem)
            return $"{problem}.";

        Error = null;
        NewTopic = "";
        if (Topics.Contains(topic))
            return null;

        AddTopicEntry(topic);
        if (Name.Trim().Length == 0)
            Name = LastFixedLevel(topic);
        UpdateKeys();
        UpdatePreview();
        return null;
    }

    private void AddTopicEntry(string topic)
    {
        if (Topics.Contains(topic))
            return;

        Topics.Add(topic);
        TopicEntries.Add(new TopicEntryViewModel(topic, MakeLevelKey));
        ManualTopic = topic;
        NotifyTopicsChanged();
    }

    [RelayCommand]
    private async Task RemoveTopicAsync(string topic)
    {
        Topics.Remove(topic);
        if (TopicEntries.FirstOrDefault(e => e.Text == topic) is { } entry)
            TopicEntries.Remove(entry);
        _picked.Remove(topic);
        _offered.Remove(topic);
        Data.Remove(topic);
        foreach (var key in _rows.Keys.Where(k => k.Topic == topic).ToList())
            _rows.Remove(key);

        if (ManualTopic == topic)
            ManualTopic = Topics.LastOrDefault();
        NotifyTopicsChanged();
        UpdateRows();
        UpdateKeys();
        UpdatePreview();
        await ListenAsync();
    }

    // A level pressed becomes a key: the topic then matches every value of that level.
    private void MakeLevelKey(TopicEntryViewModel entry, int index)
    {
        var levels = entry.Text.Split('/');
        var pattern = TopicPattern.Parse(entry.Text);
        var name = TopicPattern.SuggestName(levels[index], index, pattern?.Keys.Select(k => k.Name) ?? []);
        levels[index] = $"{{{name}}}";
        var topic = string.Join('/', levels);
        if (TopicPattern.Validate(topic) is { } problem)
        {
            Error = $"{problem}.";
            return;
        }

        Error = null;
        ReplaceTopic(entry.Text, topic);
    }

    private void ReplaceTopic(string old, string topic)
    {
        var at = Topics.IndexOf(old);
        if (at < 0 || Topics.Contains(topic))
            return;

        Topics[at] = topic;
        TopicEntries[at] = new TopicEntryViewModel(topic, MakeLevelKey);
        if (ManualTopic == old)
            ManualTopic = topic;
        if (_picked.Remove(old))
            _picked.Add(topic);
        _offered.Remove(old);

        foreach (var (key, row) in _rows.Where(r => r.Key.Topic == old).ToList())
        {
            _rows.Remove(key);
            var moved = Row(topic, key.Path);
            moved.Label = row.Label;
            moved.Color = row.Color;
            moved.Show(row.IsChosen);
        }

        foreach (var message in _messages)
            NoteOffers(message);

        UpdateRows();
        UpdateKeys();
        UpdatePreview();
        _ = ListenAsync();
    }

    private void NotifyTopicsChanged()
    {
        OnPropertyChanged(nameof(HasSeveralTopics));
        OnPropertyChanged(nameof(HasTopics));
    }

    // Subscribes to the topics listed, on the client chosen, and to nothing else.
    private async Task ListenAsync()
    {
        if (SelectedClient is not { } client || _stopped)
            return;

        var connection = client.Connection;
        if (_listeningOn != connection)
            await StopListeningAsync();

        var filters = Topics.Select(TopicPattern.Parse).OfType<TopicPattern>().Select(p => p.Filter).ToHashSet(StringComparer.Ordinal);
        _listeningTo = new TopicFilterSet(filters);
        if (_listeningOn is null)
        {
            _listeningOn = connection;
            connection.MessageReceived += OnMessageReceived;
        }

        _listenError = await connection.SetSubscriptionsAsync(SubscriptionOwner, filters);
        UpdateListenStatus();
    }

    private void UpdateListenStatus()
    {
        var wild = Topics.Any(t => TopicPattern.Parse(t) is { IsExact: false });
        ListenStatus = SelectedClient is not { } client || Topics.Count == 0 ? ""
            : _listenError is { } error ? error
            : !client.IsConnected ? string.Format(Strings.GraphEditor_IsNotConnectedNumbersAppearBelow, client.Name)
            : wild && _perSecond > 0 ? string.Format(Strings.GraphEditor_ListeningAboutASecondMatch, _perSecond, (_perSecond == 1 ? Strings.GraphEditor_Message : Strings.GraphEditor_Messages), (Topics.Count == 1 ? Topics[0] : Strings.GraphEditor_TheseTopics))
            : Topics.Count == 1 ? string.Format(Strings.GraphEditor_ListeningOnNumbersAppearBelowAs, Topics[0])
            : string.Format(Strings.GraphEditor_ListeningOnTopicsNumbersAppearBelow, Topics.Count);
    }

    [RelayCommand]
    private void AddManualPath()
    {
        if (ManualTopic is not { } topic || TopicPattern.Parse(topic) is not { } pattern)
        {
            Error = Strings.GraphEditor_AddATopicFirst;
            return;
        }

        var path = ManualPath.Trim();
        if (PayloadPath.Parse(path).ProblemWith(pattern) is { } problem)
        {
            Error = $"{problem}.";
            return;
        }

        var row = Row(topic, path);
        if (!row.IsChosen && Chosen().Count() >= GraphProfile.MaxValues)
        {
            Error = string.Format(Strings.GraphEditor_AGraphCanDrawAtMost, GraphProfile.MaxValues);
            return;
        }

        Choose(row);
        _picked.Add(topic);
        Error = null;
        ManualPath = "";
        UpdateRows();
        UpdateKeys();
        UpdatePreview();
    }

    /// <summary>Adds the field chosen as a key.</summary>
    [RelayCommand]
    private void AddKey()
    {
        if (SelectedField is { } field)
            AddFieldKey(field);
    }

    /// <summary>Shows each value of the key that puts unlike numbers on one graph on a graph of its own.</summary>
    [RelayCommand]
    private void SplitIntoGraphs()
    {
        if (_keyToSplit is { } key)
            key.SelectedShow = GraphKeyViewModel.Choices.First(c => c.Show == KeyShow.Graph);
    }

    private void AddFieldKey(string field)
    {
        var name = field[(field.LastIndexOf('.') + 1)..];
        if (LooksUnique(field))
        {
            Error = string.Format(Strings.GraphEditor_DiffersOnNearlyEveryMessageSo, field);
            return;
        }

        if (Keys.All(k => k.Name != name))
            Keys.Add(new GraphKeyViewModel(new GraphKey { Name = name, Field = field }, OnKeyChanged, RemoveKey));

        Error = null;
        SelectedField = null;
        OnPropertyChanged(nameof(HasKeys));
        UpdateKeys();
        UpdatePreview();
    }

    private void RemoveKey(GraphKeyViewModel key)
    {
        Keys.Remove(key);
        OnPropertyChanged(nameof(HasKeys));
        UpdateKeys();
        UpdatePreview();
    }

    private void OnKeyChanged() => UpdatePreview();

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);

    [RelayCommand]
    private async Task SaveAsync()
    {
        // A topic typed in but not yet added is taken to be meant.
        if (NewTopic.Trim().Length > 0 && TryAddNewTopic() is { } topicProblem)
        {
            Error = topicProblem;
            return;
        }

        if (Check() is { } problem)
        {
            Error = problem;
            return;
        }

        // Whatever has just arrived goes on the graph too.
        UpdatePreview();
        while (_incoming.TryDequeue(out var message))
            _source.Receive(message);

        var dashboard = IsNewDashboard ? _page.CreateDashboard(NewDashboardName.Trim()) : SelectedDashboard!;
        var profile = BuildProfile();
        _page.Commit(dashboard, _card, profile, Data);
        AddedTo = _card is null ? dashboard.DisplayName : string.Format(Strings.GraphEditor_On, profile.Name, dashboard.DisplayName);
        await StopAsync();
        CloseRequested?.Invoke(true);
    }

    /// <summary>Where the graph went, once saved.</summary>
    public string? AddedTo { get; private set; }

    /// <summary>Stops listening. Called when the window closes, however that happens.</summary>
    public async Task StopAsync()
    {
        if (_stopped)
            return;

        _stopped = true;
        _timer.Stop();
        await StopListeningAsync();
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (_loaded && e.PropertyName is nameof(Name) or nameof(Unit) or nameof(SelectedKind) or nameof(SelectedSpan) or nameof(SelectedDecimals)
            or nameof(UseFixedScale) or nameof(MinimumText) or nameof(MaximumText))
            UpdatePreview();
    }

    // What was received came through the old client, so it starts again. The topics stay.
    partial void OnSelectedClientChanged(MqttClientViewModel? value)
    {
        if (!_loaded)
            return;

        _messages.Clear();
        _offered.Clear();
        _fieldValues.Clear();
        _fieldMessages.Clear();
        _loadedData = null;
        _built = "";
        foreach (var key in _rows.Where(r => !r.Value.IsChosen).Select(r => r.Key).ToList())
            _rows.Remove(key);

        UpdateRows();
        UpdateKeys();
        UpdatePreview();
        _ = ListenAsync();
    }

    partial void OnSelectedDashboardChanged(DashboardTabViewModel? value) => UpdateTargets();

    partial void OnIsNewDashboardChanged(bool value) => UpdateTargets();

    partial void OnSelectedTargetChanged(GraphTarget? value)
    {
        if (_loaded && value is not null && value.Card != _card)
            Load(value.Card);
    }

    // Graphs already on the dashboard, that use the same client, are offered as well as a new one.
    private void UpdateTargets()
    {
        if (_seedTopic is null)
            return;

        var graphs = IsNewDashboard ? [] : SelectedDashboard?.Graphs
            .Where(g => string.Equals(g.Profile.Client, _seedClient?.Name, StringComparison.OrdinalIgnoreCase))
            .DistinctBy(g => g.Profile)
            .Select(g => new GraphTarget(g, g.Profile.Name)) ?? [];
        Targets = graphs.Prepend(new GraphTarget(null, Strings.GraphEditor_ANewGraph)).ToList();
        SelectedTarget = Targets[0];
    }

    // Sets everything from a graph, or to how a new one starts; then adds the topic explorer's topic.
    private void Load(GraphCardViewModel? card)
    {
        _loaded = false;
        _rows.Clear();
        _picked.Clear();
        _offered.Clear();
        _messages.Clear();
        _fieldValues.Clear();
        _fieldMessages.Clear();
        Topics.Clear();
        TopicEntries.Clear();
        Values.Clear();
        Keys.Clear();
        ManualTopic = null;
        Error = null;
        _card = card;

        var graph = card?.Profile ?? new GraphProfile();
        _loadedData = card?.Data.Copy();
        _loadedSeen = graph.Seen.Select(s => new Dictionary<string, string>(s)).ToList();
        _colors = new Dictionary<string, int>(graph.SeriesColors);
        _removedLines = [.. graph.RemovedLines];
        _loadedIdentity = IdentityOf(graph.Keys);
        _loadedPlaces = PlacesOf(graph.Keys);
        _built = "";

        if (card is not null)
            SelectedClient = _page.FindClient(graph.Client) ?? SelectedClient;
        else if (_seedClient is not null)
            SelectedClient = _seedClient;

        Name = graph.Name;
        Unit = graph.Unit;
        SelectedKind = Kinds.First(k => k.Kind == graph.Kind);
        SelectedSpan = Spans.FirstOrDefault(s => s.Minutes == graph.SpanMinutes) ?? Spans[1];
        SelectedDecimals = DecimalChoices.FirstOrDefault(d => d.Places == graph.Decimals) ?? DecimalChoices[0];
        UseFixedScale = graph.Minimum is not null;
        MinimumText = graph.Minimum?.ToString(CultureInfo.CurrentCulture) ?? "";
        MaximumText = graph.Maximum?.ToString(CultureInfo.CurrentCulture) ?? "";
        WidthSpan = graph.Width;
        HeightSpan = graph.Height;

        foreach (var key in graph.Keys)
            Keys.Add(new GraphKeyViewModel(key, OnKeyChanged, RemoveKey));

        foreach (var value in graph.Values)
        {
            AddTopicEntry(value.Topic);
            _picked.Add(value.Topic);
            var row = Row(value.Topic, value.Path);
            row.Show(true);
            row.Label = value.Label;
            row.Color = value.Color;
        }

        if (_seedTopic is { } topic)
        {
            // A graph already drawing the topic has these messages, and its own choice of numbers.
            if (!Topics.Any(t => TopicPattern.Parse(t)?.Matches(topic) == true))
            {
                AddTopicEntry(topic);
                foreach (var message in _seedHistory)
                {
                    _messages.Add(message);
                    NoteOffers(message);
                }

                foreach (var path in _seedPaths)
                {
                    if (Chosen().Count() < GraphProfile.MaxValues)
                    {
                        Choose(Row(topic, path));
                        _picked.Add(topic);
                    }
                }
            }

            if (card is null)
                Name = LastFixedLevel(topic);
        }

        UpdateRows();
        UpdateKeys();
        OnPropertyChanged(nameof(IsNew));
        OnPropertyChanged(nameof(CanChooseDashboard));
        OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(SaveText));
        OnPropertyChanged(nameof(HasKeys));
        _loaded = true;
        UpdatePreview();
        if (Topics.Count > 0)
            _ = ListenAsync();
    }

    private async Task StopListeningAsync()
    {
        _listenError = null;
        var connection = _listeningOn;
        _listeningOn = null;
        _listeningTo = new TopicFilterSet([]);
        if (connection is null)
            return;

        connection.MessageReceived -= OnMessageReceived;
        await connection.SetSubscriptionsAsync(SubscriptionOwner, []);
    }

    // Arrives on the MQTT client's thread.
    private void OnMessageReceived(ReceivedMessage message)
    {
        if (_listeningTo.Contains(message.Topic))
            _incoming.Enqueue(message);
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var now = DateTime.Now;
        if (now - _heardSince >= TimeSpan.FromSeconds(1))
        {
            _perSecond = (int)Math.Round(_heard / (now - _heardSince).TotalSeconds);
            _heard = 0;
            _heardSince = now;
        }

        UpdateListenStatus();
        var heard = false;
        while (_incoming.TryDequeue(out var message))
        {
            // A topic removed a moment ago may still have messages on their way.
            if (!Topics.Any(t => TopicPattern.Parse(t)?.Matches(message.Topic) == true))
                continue;

            heard = true;
            _heard++;
            _messages.Add(message);
            if (_messages.Count > MaxMessages)
                _messages.RemoveAt(0);
            NoteOffers(message);
            _source.Receive(message);
        }

        if (heard)
        {
            UpdateRows();
            UpdateKeys();
            UpdatePreview();
        }
        else
        {
            Preview.Tick(now, visible: true);
        }
    }

    // Takes note of the numbers and fields a message offers, under each topic it matches.
    private void NoteOffers(ReceivedMessage message)
    {
        var fields = NumberOffers.Fields(message);
        foreach (var field in fields)
        {
            if (!_fieldValues.TryGetValue(field.Path, out var values))
                _fieldValues[field.Path] = values = new HashSet<string>(StringComparer.Ordinal);
            if (values.Count <= MaxSuggestedValues)
                values.Add(field.Value);
            _fieldMessages[field.Path] = _fieldMessages.GetValueOrDefault(field.Path) + 1;
        }

        foreach (var topic in Topics)
        {
            if (TopicPattern.Parse(topic) is not { } pattern || !pattern.Matches(message.Topic))
                continue;

            if (!_offered.TryGetValue(topic, out var offered))
                _offered[topic] = offered = [];

            foreach (var offer in NumberOffers.Find(message, pattern))
            {
                var row = Row(topic, offer.Path);
                row.LooksLikeTime = offer.LooksLikeTime;
                row.Units = offer.Units;
                row.LatestText = offer.Latest.ToString("0.###");
                if (!offered.Contains(offer.Path))
                    offered.Add(offer.Path);
                if (offer.ByPosition is { } position && _rows.TryGetValue((topic, position), out var old))
                    old.BetterPath = offer.Path;
            }
        }
    }

    private GraphValueChoiceViewModel Row(string topic, string path)
    {
        if (!_rows.TryGetValue((topic, path), out var row))
            _rows[(topic, path)] = row = new GraphValueChoiceViewModel(topic, path, UnusedColor(), OnToggled, UpdatePreview, UseBetterPath, RemoveRow);

        return row;
    }

    // Takes a number off the graph. One the messages no longer offer, such as a position in an array that
    // now has a key, also leaves the list, as it would otherwise stay there with nothing to clear it.
    private void RemoveRow(GraphValueChoiceViewModel row)
    {
        Error = null;
        row.Show(false);
        _picked.Add(row.Topic);
        if (_offered.GetValueOrDefault(row.Topic)?.Contains(row.Path) != true)
            _rows.Remove((row.Topic, row.Path));

        UpdateRows();
        UpdateKeys();
        UpdatePreview();
    }

    private void UseBetterPath(GraphValueChoiceViewModel row)
    {
        if (row.BetterPath is not { } path)
            return;

        var better = Row(row.Topic, path);
        better.Label = row.Label;
        better.Color = row.Color;
        row.Show(false);
        _rows.Remove((row.Topic, row.Path));
        better.Show(true);
        UpdateRows();
        UpdateKeys();
        UpdatePreview();
    }

    private int UnusedColor()
    {
        var used = Chosen().Select(r => r.Color).ToHashSet();
        return Enumerable.Range(0, GraphColors.Count).First(c => !used.Contains(c) || used.Count >= GraphColors.Count);
    }

    // A number keeps its colour unless another took it while it was not ticked.
    private void Choose(GraphValueChoiceViewModel row)
    {
        if (Chosen().Any(r => r != row && r.Color == row.Color))
            row.Color = UnusedColor();
        row.Show(true);
    }

    private void OnToggled(GraphValueChoiceViewModel row)
    {
        Error = null;
        _picked.Add(row.Topic);
        if (row.IsChosen && Chosen().Count() > GraphProfile.MaxValues)
        {
            row.Show(false);
            Error = string.Format(Strings.GraphEditor_AGraphCanDrawAtMost, GraphProfile.MaxValues);
            return;
        }

        if (row.IsChosen)
        {
            row.Show(false);
            Choose(row);
        }

        UpdateKeys();
        UpdatePreview();
    }

    private IEnumerable<GraphValueChoiceViewModel> Chosen() => _rows.Values.Where(r => r.IsChosen);

    // Lists the numbers received, and those chosen that have not arrived, topic by topic in a steady order.
    private void UpdateRows()
    {
        var order = new List<GraphValueChoiceViewModel>();
        foreach (var topic in Topics)
        {
            var paths = _offered.GetValueOrDefault(topic) ?? [];
            var rows = paths.Select(p => Row(topic, p))
                .OrderBy(r => r.LooksLikeTime)
                .ThenBy(r => JsonNumbers.IsOneItem(r.Path))
                .ToList();
            order.AddRange(rows);
            order.AddRange(_rows.Values.Where(r => r.Topic == topic && !paths.Contains(r.Path)));

            // The first number to arrive on a topic is drawn, so that there is something to see at once.
            // Not a time or a counter of messages, as a graph of those is of no use to anyone.
            if (!_picked.Contains(topic)
                && rows.FirstOrDefault(r => !r.LooksLikeTime && !JsonNumbers.IsOneItem(r.Path)) is { } first
                && Chosen().Count() < GraphProfile.MaxValues)
            {
                _picked.Add(topic);
                Choose(first);
            }
        }

        if (!Values.SequenceEqual(order))
        {
            Values.Clear();
            foreach (var row in order)
                Values.Add(row);
            OnPropertyChanged(nameof(HasNoNumbers));
        }
    }

    // Lists the keys there are: every wildcard, those the chosen numbers give, and fields added by hand.
    private void UpdateKeys()
    {
        var names = new List<string>();
        foreach (var topic in Topics)
        {
            if (TopicPattern.Parse(topic) is { } pattern)
                names.AddRange(pattern.Keys.Select(k => k.Name));
        }

        foreach (var row in Chosen())
            names.AddRange(PayloadPath.Parse(row.Path).KeyNames);

        foreach (var key in Keys.Where(k => !k.CanRemove && !names.Contains(k.Name)).ToList())
            Keys.Remove(key);
        foreach (var name in names.Distinct())
        {
            if (Keys.All(k => k.Name != name))
                Keys.Add(new GraphKeyViewModel(new GraphKey { Name = name }, OnKeyChanged, RemoveKey));
        }

        foreach (var key in Keys)
            key.ShowValues(_source.KeyValues.TryGetValue(key.Name, out var values) ? values : []);

        OnPropertyChanged(nameof(HasKeys));
        UpdateFieldChoices();
    }

    private void UpdateFieldChoices()
    {
        var taken = Keys.Select(k => k.Field).ToHashSet(StringComparer.Ordinal);
        var choices = _fieldValues.Keys.Where(f => !taken.Contains(f)).ToList();
        if (!FieldChoices.SequenceEqual(choices))
        {
            FieldChoices.Clear();
            foreach (var field in choices)
                FieldChoices.Add(field);
            OnPropertyChanged(nameof(HasFieldChoices));
        }

        // A number that comes with several values of a field is several things drawn as one, which zigzags.
        var suggested = Chosen().Any()
            ? _fieldValues.Where(f => !taken.Contains(f.Key) && f.Value.Count is >= 2 and <= MaxSuggestedValues && !LooksUnique(f.Key)).ToList()
            : [];
        var texts = suggested.Select(f => string.Format(Strings.GraphEditor_ThisNumberComesWithValuesOf, f.Value.Count, f.Key)).ToList();
        if (!Suggestions.Select(s => s.Text).SequenceEqual(texts))
        {
            Suggestions.Clear();
            foreach (var field in suggested)
                Suggestions.Add(new KeySuggestion(string.Format(Strings.GraphEditor_ThisNumberComesWithValuesOf, field.Value.Count, field.Key), field.Key, AddFieldKey));
        }
    }

    private bool LooksUnique(string field)
        => _fieldMessages.GetValueOrDefault(field) is var messages and >= MessagesToJudgeUnique
           && _fieldValues.GetValueOrDefault(field)?.Count > messages * 0.8 || _fieldValues.GetValueOrDefault(field)?.Count > MaxSuggestedValues;

    private void UpdatePreview()
    {
        if (!_loaded)
            return;

        var built = BuiltFrom();
        if (built != _built)
        {
            _built = built;
            Rebuild();
        }

        var profile = BuildProfile();

        // Lines are removed from one graph, so a graph being edited shows itself rather than the first of its kind.
        var cell = _card is { HasCell: true } card && _seen.Any(s => new SeriesKeys(s).Equals(card.Cell)) ? card.Cell
            : _seen.Count > 0 ? new SeriesKeys(_seen[0])
            : SeriesKeys.None;
        Preview.Apply(profile, Data, cell);
        Preview.Status = !Chosen().Any() ? Strings.GraphEditor_TickANumberBelowToDraw
            : Preview.HasReadings ? null
            : _source.Health.Problem is { } problem ? string.Format(Strings.GraphEditor_MessagesAreArrivingBut, problem)
            : Strings.GraphEditor_WaitingForAMessage;
        UpdateKeys();
        UpdateLines();
        UpdateOutcome();
        UpdateUnitHint();
    }

    private void UpdateLines()
    {
        var lines = Preview.LinesFromKeys();
        var canRemove = lines.Count(l => !l.IsRemoved) > 1;
        var rows = lines.Select(l => new GraphLineViewModel(l.Line.Id, l.Line.DisplayLabel, l.Line.Slot, l.IsRemoved, canRemove, SetLineRemoved)).ToList();
        if (rows.Select(r => (r.Id, r.Label, r.IsRemoved, r.CanRemove)).SequenceEqual(Lines.Select(r => (r.Id, r.Label, r.IsRemoved, r.CanRemove))))
            return;

        Lines.Clear();
        foreach (var row in rows)
            Lines.Add(row);
        OnPropertyChanged(nameof(HasLines));
    }

    private void SetLineRemoved(string id, bool removed)
    {
        Preview.SetRemoved(id, removed);
        UpdateLines();
        UpdateOutcome();
    }

    // Starts the readings again from what has been heard, as what tells them apart has changed.
    private void Rebuild()
    {
        var profile = BuildProfile();
        var identity = IdentityOf(profile.Keys);
        var data = _loadedData is not null && identity == _loadedIdentity ? _loadedData.Copy() : new GraphData();
        _seen = identity == _loadedIdentity && PlacesOf(profile.Keys) == _loadedPlaces
            ? _loadedSeen.Select(s => new Dictionary<string, string>(s)).ToList()
            : [];
        profile.Seen = _seen;
        _source = new GraphSource(profile, data);
        _source.LearnCells();
        foreach (var message in _messages)
            _source.Receive(message);
    }

    private void UpdateOutcome()
    {
        var profile = _source.Profile;
        var tabKey = profile.TabKey;
        var tabs = tabKey is null ? 0 : _seen.Select(s => s.GetValueOrDefault(tabKey)).Distinct().Count();
        var graphs = tabKey is null
            ? _seen.Count
            : _seen.GroupBy(s => s.GetValueOrDefault(tabKey)).Select(g => g.Count()).DefaultIfEmpty(0).Max();
        var lines = Preview.Series.Count + Preview.HiddenLines;

        var parts = new List<string>();
        if (tabs > 1)
            parts.Add(string.Format(Strings.GraphEditor_Tabs, tabs));
        if (graphs > 1)
            parts.Add(tabs > 1 ? string.Format(Strings.GraphEditor_GraphsOnEach, graphs) : string.Format(Strings.GraphEditor_Graphs, graphs));
        if (lines > 1)
            parts.Add(tabs > 1 || graphs > 1 ? string.Format(Strings.GraphEditor_LinesOnEachGraph, lines) : string.Format(Strings.GraphEditor_Lines, lines));

        var outcome = parts.Count > 0 ? string.Format(Strings.GraphEditor_ThisMakes, string.Join(", ", parts)) : "";
        if (_source.OverLimit is { } limit)
            outcome = string.Format(Strings.GraphEditor_SomeValuesAreLeftOutAs, outcome, limit).Trim();
        OutcomeText = outcome;
    }

    private void UpdateUnitHint()
    {
        // Units differ by the key a path gives, such as sensor, so it is the one to split, and only while it makes lines.
        var fromPaths = Chosen().SelectMany(r => PayloadPath.Parse(r.Path).KeyNames).ToHashSet(StringComparer.Ordinal);
        var unitKey = Keys.FirstOrDefault(k => k.SelectedShow.Show == KeyShow.Line && fromPaths.Contains(k.Name));
        var units = Chosen().SelectMany(r => r.Units).Where(u => u.Length > 0).Distinct().ToList();
        var sizes = Preview.Series.Select(s => s.Latest).OfType<double>().Select(Math.Abs).Where(v => v > 0).ToList();
        if (unitKey is not null && units.Count > 1)
        {
            _keyToSplit = unitKey;
            UnitHint = string.Format(Strings.GraphEditor_TheseNumbersHaveDifferentUnitsAnd, string.Join(", ", units));
        }
        else if (sizes.Count > 1 && sizes.Max() / sizes.Min() >= 1000 && Keys.FirstOrDefault(k => k.SelectedShow.Show == KeyShow.Line) is { } lineKey)
        {
            _keyToSplit = lineKey;
            UnitHint = Strings.GraphEditor_TheseNumbersAreOfVeryDifferent;
        }
        else
        {
            _keyToSplit = null;
            UnitHint = "";
        }
    }

    private GraphProfile BuildProfile()
    {
        var hasScale = TryParse(MinimumText, out var minimum) & TryParse(MaximumText, out var maximum) && minimum < maximum;
        return new GraphProfile
        {
            Name = Name.Trim(),
            Client = SelectedClient?.Name ?? "",
            Kind = SelectedKind.Kind,
            Unit = Unit.Trim(),
            SpanMinutes = SelectedSpan.Minutes,
            Decimals = SelectedDecimals.Places,
            Minimum = UseFixedScale && hasScale ? minimum : null,
            Maximum = UseFixedScale && hasScale ? maximum : null,
            Width = WidthSpan,
            Height = HeightSpan,
            Values = Values.Where(r => r.IsChosen)
                .Select(r => new GraphValue { Topic = r.Topic, Path = r.Path, Label = r.Label.Trim(), Color = r.Color })
                .ToList(),
            Keys = Keys.Select(k => k.ToKey()).ToList(),
            SeriesColors = _colors,
            RemovedLines = _removedLines,
            Seen = _seen,
        };
    }

    private string BuiltFrom()
    {
        var keys = Keys.Select(k => k.ToKey()).ToList();
        var values = Values.Where(r => r.IsChosen).Select(r => $"{r.Topic}|{r.Path}");
        return $"{IdentityOf(keys)}\n{PlacesOf(keys)}\n{string.Join('\n', values)}";
    }

    // What decides which series a reading belongs to. Readings can be kept while this stays the same.
    private static string IdentityOf(IEnumerable<GraphKey> keys)
        => string.Join('\n', keys.Where(k => k.Field.Length > 0 || k.Show == KeyShow.Only).Select(k => $"{k.Name}|{k.Field}|{(k.Show == KeyShow.Only ? k.Only : "")}|{k.Show == KeyShow.Only}").Order(StringComparer.Ordinal));

    // What decides which graphs and tabs a series goes on.
    private static string PlacesOf(IEnumerable<GraphKey> keys)
        => string.Join('\n', keys.Where(k => k.Show is KeyShow.Tab or KeyShow.Graph).Select(k => $"{k.Name}|{k.Show}").Order(StringComparer.Ordinal));

    private string? Check()
    {
        if (Name.Trim().Length == 0)
            return Strings.GraphEditor_EnterANameForTheGraph;
        if (SelectedClient is null)
            return Strings.GraphEditor_ChooseAClient;
        if (Topics.Count == 0)
            return Strings.GraphEditor_AddATopicToDraw;
        if (!Values.Any(r => r.IsChosen))
            return Strings.GraphEditor_TickAtLeastOneNumberTo;

        // A graph only keeps the topics it draws, so this one would quietly go.
        if (Topics.FirstOrDefault(t => !Chosen().Any(r => r.Topic == t)) is { } unused)
            return string.Format(Strings.GraphEditor_NothingIsTickedOnTickA, unused);
        if (UseFixedScale && !(TryParse(MinimumText, out var minimum) & TryParse(MaximumText, out var maximum) && minimum < maximum))
            return Strings.GraphEditor_AFixedScaleNeedsAMinimum;
        if (CanChooseDashboard && IsNewDashboard && NewDashboardName.Trim().Length == 0)
            return Strings.GraphEditor_EnterANameForTheNew;
        if (CanChooseDashboard && !IsNewDashboard && SelectedDashboard is null)
            return Strings.GraphEditor_ChooseADashboard;
        if (Keys.FirstOrDefault(k => k.IsOnly && k.OnlyValue.Trim().Length == 0) is { } only)
            return string.Format(Strings.GraphEditor_ChooseTheValueOfToKeep, only.Name);

        var tabKeys = Keys.Where(k => k.SelectedShow.Show == KeyShow.Tab).ToList();
        if (tabKeys.Count > 1)
            return Strings.GraphEditor_OnlyOneKeyCanBeShown;

        // A dashboard is shown once per value of one key, so all its graphs must agree on which.
        if (tabKeys.Count == 1 && !IsNewDashboard && SelectedDashboard is { } dashboard
            && dashboard.Profile.Graphs.Where(g => g != _card?.Profile).Select(g => g.TabKey).FirstOrDefault(k => k is not null) is { } other
            && other != tabKeys[0].Name)
            return string.Format(Strings.GraphEditor_TheTabsOfThisDashboardCome, other, tabKeys[0].Name);
        return null;
    }

    private static bool TryParse(string text, out double value)
        => double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out value) && double.IsFinite(value);

    // The name for a graph from its topic: its last level that is not a wildcard.
    private static string LastFixedLevel(string topic)
        => topic.Split('/').LastOrDefault(l => l.Length > 0 && l is not ("+" or "#") && !l.StartsWith('{')) ?? topic;
}
