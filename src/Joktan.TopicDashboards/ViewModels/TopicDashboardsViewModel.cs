using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
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

/// <summary>
/// Dashboard / MQTT Dashboards: dashboards of graphs, drawn from MQTT topics, one tab each. Specified
/// in docs/features/dashboards/spec.md.
/// </summary>
public partial class TopicDashboardsViewModel : PageViewModel
{
    private const int MaxMessagesPerFlush = 5000;
    private static string FirstDashboardName => Strings.TopicDashboards_Main;
    private const int MaxRoutes = 10_000;

    /// <summary>The most series the app keeps, as a key that differs on every message would otherwise fill the memory.</summary>
    public const int MaxSeries = 500;

    private readonly Dictionary<MqttClientViewModel, ClientFeed> _feeds = [];
    private readonly Dictionary<GraphProfile, GraphSource> _sources = [];
    private readonly Dictionary<(string Client, string Topic), List<GraphSource>> _routes = [];
    private int _seriesCount;
    private DashboardStore _store;

    public TopicDashboardsViewModel(MqttViewModel mqtt, DashboardStore? store = null)
        : base(Strings.TopicDashboards_MQTTDashboards, Strings.TopicDashboards_DashboardMQTTDashboards)
    {
        Mqtt = mqtt;
        _store = store ?? DashboardStore.Load();
        Open(_store);

        foreach (var client in mqtt.Clients)
            AddFeed(client);
        mqtt.Clients.CollectionChanged += OnClientsChanged;

        var flushTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(150), DispatcherPriority.Background, OnFlushTick);
        var tickTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, OnTick);
        flushTimer.Start();
        tickTimer.Start();

        GraphsChanged(save: false);
    }

    /// <summary>The MQTT page, which owns the clients and their connections.</summary>
    public MqttViewModel Mqtt { get; }

    public ObservableCollection<DashboardTabViewModel> Dashboards { get; } = [];

    [ObservableProperty]
    public partial DashboardTabViewModel? SelectedDashboard { get; set; }

    /// <summary>Whether the tabs show their buttons to rename, colour and remove them.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditModeText))]
    public partial bool IsEditing { get; set; }

    /// <summary>The mode the button beside Add goes to.</summary>
    public string EditModeText => IsEditing ? Strings.TopicDashboards_View : Strings.TopicDashboards_Edit;

    /// <summary>Why a subscription failed, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSubscriptionError))]
    public partial string? SubscriptionError { get; private set; }

    public bool HasSubscriptionError => SubscriptionError is not null;

    /// <summary>Why the dashboards are not being saved, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSaveError))]
    public partial string? SaveError { get; private set; }

    public bool HasSaveError => SaveError is not null;

    /// <summary>What was left out because of a limit, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLimitWarning))]
    public partial string? LimitWarning { get; private set; }

    public bool HasLimitWarning => LimitWarning is not null;

    public string Folder => _store.Folder;

    public string DefaultFolder => DashboardStore.DefaultFolder;

    [ObservableProperty]
    public partial bool IsChoosingFolder { get; set; }

    /// <summary>The folder typed or picked in the folder band, used once applied.</summary>
    [ObservableProperty]
    public partial string FolderText { get; set; } = "";

    /// <summary>Why the folder typed or picked could not be used.</summary>
    [ObservableProperty]
    public partial string? FolderMessage { get; set; }

    /// <summary>Whether the folder that could not be read can be started again from nothing, if that is what is wanted.</summary>
    [ObservableProperty]
    public partial bool CanStartEmpty { get; private set; }

    /// <summary>What happened when the folder was last changed.</summary>
    [ObservableProperty]
    public partial string? Message { get; set; }

    /// <summary>Asks the user for a folder. Set by the view, which owns the system dialog.</summary>
    public Func<Task<string?>>? PickFolder { get; set; }

    /// <summary>Shows the graph editor. Set by the view.</summary>
    public Func<GraphEditorViewModel, Task>? ShowEditor { get; set; }

    public MqttClientViewModel? FindClient(string name) => Mqtt.Find(name);

    /// <summary>A new graph on a topic seen in the topic explorer, with the messages already received on it.</summary>
    /// <param name="history">Oldest first.</param>
    /// <param name="paths">The numbers to start with.</param>
    public GraphEditorViewModel NewEditor(MqttClientViewModel client, string topic, IEnumerable<ReceivedMessage> history, IEnumerable<string> paths)
        => new(this, client, topic, history, paths);

    /// <summary>Why a graph draws nothing, or null.</summary>
    public string? StatusOf(GraphCardViewModel card)
    {
        if (FindClient(card.Profile.Client) is not { } client)
            return string.Format(Strings.TopicDashboards_ThereIsNoMQTTClientCalled, card.Profile.Client);
        if (card.HasReadings)
            return null;
        if (SourceFor(card.Profile).Health.Problem is { } problem)
            return string.Format(Strings.TopicDashboards_MessagesAreArrivingBut, problem);

        var topics = card.Profile.Topics.ToList();
        var source = topics.Count == 1 ? topics[0] : string.Format(Strings.TopicDashboards_ItsTopics, topics.Count);
        return client.IsConnected
            ? string.Format(Strings.TopicDashboards_WaitingForAMessageOn, source)
            : string.Format(Strings.TopicDashboards_ConnectToReceive, client.Name, source);
    }

    public void AddGraph(DashboardTabViewModel dashboard)
        => _ = ShowEditor?.Invoke(new GraphEditorViewModel(this, dashboard));

    public void EditGraph(DashboardTabViewModel dashboard, GraphCardViewModel card)
        => _ = ShowEditor?.Invoke(new GraphEditorViewModel(this, dashboard, card));

    /// <summary>A new dashboard with a name no other has.</summary>
    public DashboardTabViewModel CreateDashboard(string? name = null)
    {
        var profile = new DashboardProfile { Name = name ?? UnusedName() };
        _store.Dashboards.Add(profile);
        var dashboard = new DashboardTabViewModel(profile, this);
        Dashboards.Add(dashboard);
        return dashboard;
    }

    /// <summary>What a saved graph receives into, and how it is doing.</summary>
    public GraphSource SourceFor(GraphProfile profile)
    {
        if (!_sources.TryGetValue(profile, out var source))
            _sources[profile] = source = new GraphSource(profile, new GraphData(), () => _seriesCount < MaxSeries);
        return source;
    }

    /// <summary>The tabs showing a dashboard: one, or one per value of its tab key.</summary>
    public IEnumerable<DashboardTabViewModel> TabsOf(DashboardProfile profile) => Dashboards.Where(d => d.Profile == profile);

    /// <summary>Tells a dashboard's other tabs that something they share was changed on one of them.</summary>
    public void SyncFormat(DashboardTabViewModel changed)
    {
        foreach (var tab in TabsOf(changed.Profile).Where(t => t != changed).ToList())
            tab.TakeFormatFrom(changed);
    }

    /// <summary>Makes a dashboard's tabs and graphs match what has been seen.</summary>
    public void Arrange(DashboardProfile profile)
    {
        var tabKey = profile.TabKey;
        var values = tabKey is null
            ? []
            : profile.Graphs.SelectMany(g => g.Seen).Select(s => s.GetValueOrDefault(tabKey)).OfType<string>().Distinct().ToList();

        // A dashboard with no tab key, or none of its values yet, still has its one tab.
        List<string?> wanted = values.Count > 0 ? [.. values] : [null];
        var tabs = TabsOf(profile).ToList();
        var free = wanted.Where(v => tabs.All(t => t.TabValue != v)).ToList();

        // A tab whose value has gone shows a new one, if there is one, so the focus stays where it is.
        foreach (var tab in tabs.Where(t => !wanted.Contains(t.TabValue)).ToList())
        {
            if (free.Count > 0)
            {
                tab.ShowTabValue(tabKey, free[0]);
                free.RemoveAt(0);
            }
            else
            {
                RemoveTab(tab);
            }
        }

        tabs = TabsOf(profile).ToList();
        foreach (var value in free)
        {
            var at = tabs.Count > 0 ? Dashboards.IndexOf(tabs[^1]) + 1 : Dashboards.Count;
            var tab = new DashboardTabViewModel(profile, this, tabKey, value);
            Dashboards.Insert(at, tab);
            tabs.Add(tab);
        }

        foreach (var tab in tabs)
        {
            tab.ShowTabValue(tabKey, tab.TabValue);
            tab.Arrange();
        }
    }

    /// <summary>Removes one tab or graph made from a key's value, until that value is seen again.</summary>
    public void Forget(DashboardProfile dashboard, SeriesKeys cell)
    {
        foreach (var graph in dashboard.Graphs)
            SourceFor(graph).Forget(cell);
        Arrange(dashboard);
        GraphsChanged();
    }

    /// <summary>Removes a dashboard and its graphs. There is always one on show, so the last is replaced with an empty one.</summary>
    public void Remove(DashboardTabViewModel dashboard)
    {
        var profile = dashboard.Profile;
        var tabs = TabsOf(profile).ToList();
        if (Dashboards.Count == tabs.Count)
            CreateDashboard(FirstDashboardName);

        // A key's tabs are one dashboard, so they go together.
        foreach (var tab in tabs)
            RemoveTab(tab);

        _store.Dashboards.Remove(profile);
        foreach (var graph in profile.Graphs)
            _sources.Remove(graph);
        GraphsChanged();
    }

    /// <summary>Removes a saved graph, and everything that was showing it.</summary>
    public void RemoveGraph(DashboardProfile dashboard, GraphProfile graph)
    {
        dashboard.Graphs.Remove(graph);
        _sources.Remove(graph);
        Arrange(dashboard);
        GraphsChanged();
    }

    private void RemoveTab(DashboardTabViewModel tab)
    {
        // Chosen first, as the tabs select the first one themselves when the selected one goes.
        if (SelectedDashboard == tab)
        {
            var index = Dashboards.IndexOf(tab);
            SelectedDashboard = Dashboards.Count > 1 ? Dashboards[index < Dashboards.Count - 1 ? index + 1 : index - 1] : null;
        }

        tab.Detach();
        Dashboards.Remove(tab);
    }

    /// <summary>Applies what the graph editor was left with.</summary>
    public void Commit(DashboardTabViewModel dashboard, GraphCardViewModel? card, GraphProfile profile, GraphData data)
    {
        data.Keep(profile.Values.Select(v => (v.Topic, v.Path)).ToHashSet());
        _sources[profile] = new GraphSource(profile, data, () => _seriesCount < MaxSeries);

        var saved = dashboard.Profile;
        var index = card is null ? -1 : saved.Graphs.IndexOf(card.Profile);
        if (index >= 0)
        {
            _sources.Remove(card!.Profile);
            saved.Graphs[index] = profile;
        }
        else
        {
            saved.Graphs.Add(profile);
        }

        Arrange(saved);
        GraphsChanged();
        SelectedDashboard = TabsOf(saved).Contains(dashboard) ? dashboard : TabsOf(saved).First();
    }

    /// <summary>Subscribes to what the graphs now need, and says how each stands.</summary>
    public void GraphsChanged() => GraphsChanged(save: true);

    public void Save()
    {
        _store.Selected = SelectedDashboard?.SelectionName ?? "";
        SaveError = UnavailableMessage();
        _store.Save();
    }

    /// <summary>Writes anything not yet on disk. Called when the app exits.</summary>
    public void SaveState()
    {
        Save();
        _store.FlushAsync().GetAwaiter().GetResult();
    }

    [RelayCommand]
    private void ToggleFolder()
    {
        IsChoosingFolder = !IsChoosingFolder;
        FolderText = Folder;
        FolderMessage = null;
        CanStartEmpty = false;
    }

    [RelayCommand]
    private async Task BrowseFolderAsync()
    {
        if (PickFolder is not null && await PickFolder() is { } folder)
            FolderText = folder;
    }

    [RelayCommand]
    private void UseDefaultFolder() => FolderText = DefaultFolder;

    /// <summary>
    /// Keeps dashboards in the folder typed or picked. A folder that already holds dashboards is opened
    /// as it is; otherwise the dashboards open now are copied into it, and stay in the old folder as well.
    /// </summary>
    [RelayCommand]
    private async Task ApplyFolderAsync()
    {
        FolderMessage = null;
        CanStartEmpty = false;
        var folder = FolderText.Trim();
        if (folder.Length == 0)
            folder = DefaultFolder;
        if (folder.StartsWith('~'))
            folder = System.IO.Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), folder[1..].TrimStart('/', '\\'));
        if (!System.IO.Path.IsPathFullyQualified(folder))
        {
            FolderMessage = Strings.TopicDashboards_GiveTheWholePathOfThe;
            return;
        }

        folder = System.IO.Path.GetFullPath(folder);

        // The same folder again is how one that was missing is tried once more.
        if (DashboardStore.SameFolder(folder, Folder) && !_store.IsUnavailable)
        {
            IsChoosingFolder = false;
            return;
        }

        // Still no dashboards there, so nothing is written: they may be on their way back.
        if (!DashboardStore.HasDashboards(folder) && !_store.MayCopyTo(folder))
        {
            FolderMessage = string.Format(Strings.TopicDashboards_ThereAreStillNoDashboardsIn, folder);
            CanStartEmpty = true;
            return;
        }

        IsEditing = false;
        Save();
        var previous = Folder;
        if (DashboardStore.HasDashboards(folder))
        {
            await _store.FlushAsync();
            DashboardStore.SaveChosenFolder(folder);
            Open(DashboardStore.Load(folder));
            GraphsChanged(save: false);
            Message = string.Format(Strings.TopicDashboards_OpenedTheDashboardsAlreadyIn, folder);
        }
        else
        {
            if (!await CopyToFolderAsync(folder))
                return;

            Message = string.Format(Strings.TopicDashboards_TheDashboardsWereCopiedToThe, folder, previous);
        }

        IsChoosingFolder = false;
    }

    /// <summary>Starts a folder that could not be read again from nothing, when asked to in so many words.</summary>
    [RelayCommand]
    private async Task StartEmptyHereAsync()
    {
        FolderMessage = null;
        CanStartEmpty = false;
        if (DashboardStore.HasDashboards(Folder))
        {
            FolderMessage = string.Format(Strings.TopicDashboards_DashboardsHaveAppearedInPressUse, Folder);
            return;
        }

        if (!await CopyToFolderAsync(Folder))
            return;

        Message = string.Format(Strings.TopicDashboards_StartedEmptyIn, Folder);
        IsChoosingFolder = false;
    }

    private async Task<bool> CopyToFolderAsync(string folder)
    {
        DashboardStore copy;
        try
        {
            copy = await _store.CopyToAsync(folder);
        }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
        {
            FolderMessage = string.Format(Strings.TopicDashboards_CouldNotCopyTheDashboardsTo, folder, e.Message);
            return false;
        }

        // The same dashboards, so the graphs keep what they have drawn.
        DashboardStore.SaveChosenFolder(folder);
        UseStore(copy);
        return true;
    }

    private void UseStore(DashboardStore store)
    {
        _store.Failed -= OnWriteFailed;
        _store = store;
        _store.Failed += OnWriteFailed;
        SaveError = UnavailableMessage();
        OnPropertyChanged(nameof(Folder));
    }

    private void Open(DashboardStore store)
    {
        UseStore(store);
        if (store.Dashboards.Count == 0)
            store.Dashboards.Add(new DashboardProfile { Name = FirstDashboardName });

        // Taken first, as the tabs select the first one themselves as they fill, which is saved.
        var selected = store.Selected;
        foreach (var dashboard in Dashboards)
            dashboard.Detach();
        _sources.Clear();
        _routes.Clear();
        SelectedDashboard = null;
        Dashboards.Clear();
        foreach (var profile in store.Dashboards)
            Arrange(profile);
        SelectedDashboard = Dashboards.FirstOrDefault(d => d.SelectionName == selected) ?? Dashboards[0];
    }

    private string? UnavailableMessage() => _store.IsUnavailable
        ? string.Format(Strings.TopicDashboards_TheDashboardsInCannotBeRead, Folder)
        : null;

    private void OnWriteFailed(string path, Exception e)
        => Dispatcher.UIThread.Post(() => SaveError = string.Format(Strings.TopicDashboards_CouldNotWrite, path, e.Message));

    private void GraphsChanged(bool save)
    {
        if (save)
            Save();
        _routes.Clear();
        foreach (var dashboard in Dashboards)
            dashboard.UpdateClients();
        _ = SubscribeAsync();
    }

    private async Task SubscribeAsync()
    {
        string? error = null;
        var graphs = _store.Dashboards.SelectMany(d => d.Graphs).ToList();
        foreach (var feed in _feeds.Values.ToList())
        {
            var filters = graphs
                .Where(g => string.Equals(g.Client, feed.Name, StringComparison.OrdinalIgnoreCase))
                .SelectMany(g => SourceFor(g).Reader.Filters)
                .ToHashSet(StringComparer.Ordinal);
            error ??= await feed.SetFiltersAsync(filters);
        }

        SubscriptionError = error;
    }

    partial void OnIsEditingChanged(bool value)
    {
        if (value)
            return;

        foreach (var dashboard in Dashboards)
            dashboard.EndEditing();
    }

    partial void OnSelectedDashboardChanged(DashboardTabViewModel? value)
    {
        // Set while the first ones are loaded, before there is anything new to save.
        if (value is not null && value.SelectionName != _store.Selected && _feeds.Count > 0)
            Save();
    }

    private string UnusedName()
    {
        for (var i = Dashboards.Count + 1; ; i++)
        {
            var name = string.Format(Strings.TopicDashboards_Dashboard, i);
            if (Dashboards.All(d => !d.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                return name;
        }
    }

    [RelayCommand]
    private void ToggleEditing() => IsEditing = !IsEditing;

    [RelayCommand]
    private void AddDashboard() => SelectedDashboard = CreateDashboard();

    private void AddFeed(MqttClientViewModel client)
    {
        _feeds[client] = new ClientFeed(client);
        client.PropertyChanged += OnClientChanged;
    }

    private void OnClientChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not MqttClientViewModel client)
            return;

        if (e.PropertyName == nameof(MqttClientViewModel.IsConnected))
        {
            foreach (var dashboard in Dashboards)
                dashboard.UpdateClients();
        }
        else if (e.PropertyName == nameof(MqttClientViewModel.Name) && _feeds.TryGetValue(client, out var feed) && feed.Name != client.Name)
        {
            // Graphs know their client by name, so they follow it when it is renamed.
            foreach (var graph in _store.Dashboards.SelectMany(d => d.Graphs))
            {
                if (string.Equals(graph.Client, feed.Name, StringComparison.OrdinalIgnoreCase))
                    graph.Client = client.Name;
            }

            feed.Name = client.Name;
            GraphsChanged();
        }
    }

    // A client that is removed leaves its graphs waiting, in case one of that name comes back.
    private void OnClientsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var client in _feeds.Keys.Where(c => !Mqtt.Clients.Contains(c)).ToList())
        {
            client.PropertyChanged -= OnClientChanged;
            _feeds[client].Dispose();
            _feeds.Remove(client);
        }

        foreach (var client in Mqtt.Clients.Where(c => !_feeds.ContainsKey(c)))
            AddFeed(client);

        GraphsChanged(save: false);
    }

    private void OnFlushTick(object? sender, EventArgs e)
    {
        var changed = new HashSet<DashboardProfile>();
        foreach (var feed in _feeds.Values)
        {
            for (var taken = 0; taken < MaxMessagesPerFlush && feed.TryTake(out var message); taken++)
            {
                foreach (var source in RouteOf(feed.Name, message.Topic))
                {
                    var (added, newCell) = source.Receive(message);
                    if (newCell)
                        changed.Add(DashboardOf(source.Profile));

                    foreach (var card in CardsOf(source))
                    {
                        if (added)
                            card.Refresh();
                        if (card.HasStatus || (!card.HasReadings && source.Health.Problem is not null))
                            card.Status = StatusOf(card);
                    }
                }
            }
        }

        foreach (var dashboard in changed)
            Arrange(dashboard);
        if (changed.Count > 0)
        {
            foreach (var dashboard in Dashboards)
                dashboard.UpdateClients();
            Save();
        }
    }

    // Worked out once for each topic, as a wildcard means every graph would otherwise be checked for every message.
    private List<GraphSource> RouteOf(string client, string topic)
    {
        var key = (client.ToUpperInvariant(), topic);
        if (_routes.TryGetValue(key, out var route))
            return route;

        if (_routes.Count >= MaxRoutes)
            _routes.Clear();

        route = _store.Dashboards.SelectMany(d => d.Graphs)
            .Where(g => string.Equals(g.Client, client, StringComparison.OrdinalIgnoreCase))
            .Select(SourceFor)
            .Where(s => s.Reader.Matches(topic))
            .ToList();
        _routes[key] = route;
        return route;
    }

    private DashboardProfile DashboardOf(GraphProfile graph) => _store.Dashboards.First(d => d.Graphs.Contains(graph));

    private IEnumerable<GraphCardViewModel> CardsOf(GraphSource source)
        => Dashboards.Where(d => d.Profile.Graphs.Contains(source.Profile)).SelectMany(d => d.Graphs).Where(c => c.Profile == source.Profile);

    private void OnTick(object? sender, EventArgs e)
    {
        var now = DateTime.Now;
        var count = 0;
        string? warning = null;
        foreach (var graph in _store.Dashboards.SelectMany(d => d.Graphs))
        {
            var source = SourceFor(graph);
            var start = now - TimeSpan.FromMinutes(graph.SpanMinutes);
            source.Data.Prune(start);
            source.Data.ForgetIdle(start);
            count += source.Data.Count;
            if (source.OverLimit is { } limit)
                warning ??= string.Format(Strings.TopicDashboards_IsLeavingSomeValuesOutAs, graph.Name, limit);
        }

        _seriesCount = count;
        LimitWarning = count >= MaxSeries ? string.Format(Strings.TopicDashboards_NewValuesAreWaitingAsThe, MaxSeries) : warning;

        foreach (var dashboard in Dashboards)
        {
            var visible = dashboard == SelectedDashboard;
            foreach (var card in dashboard.Graphs)
                card.Tick(now, visible);
        }
    }
}
