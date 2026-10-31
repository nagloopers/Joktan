using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Core.ViewModels;
using Joktan.Mqtt;
using Joktan.Mqtt.ViewModels;
using Joktan.TopicDashboards.ViewModels;

namespace Joktan.TopicExplorer.ViewModels;

/// <summary>
/// One tab of the topic explorer: subscribes to a broker through one client and shows every topic
/// that carries messages as a tree, with the latest value of each. Every tab listens on its own,
/// whether it is showing or not.
/// </summary>
public partial class ClientExplorerViewModel : ViewModelBase, IDisposable
{
    private const string DefaultFilters = "#";
    private const int MaxMessagesPerFlush = 5000;
    private const string SubscriptionOwner = "explorer";

    private readonly MqttConnection _connection;
    private readonly Action _save;
    private readonly TopicDashboardsViewModel _dashboards;
    private readonly ConcurrentQueue<ReceivedMessage> _incoming = new();
    private readonly Dictionary<string, TopicNodeViewModel> _rootIndex = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _flushTimer;
    private volatile string[] _listeningTo = [];
    private bool _activated;
    private int _topicCount;
    private long _messageCount;

    /// <param name="save">Saves every client's settings, after this tab's filters have changed.</param>
    /// <param name="dashboards">Where a topic's graph goes when it is added to a dashboard.</param>
    public ClientExplorerViewModel(MqttClientViewModel client, Action save, TopicDashboardsViewModel dashboards)
    {
        Client = client;
        _save = save;
        _dashboards = dashboards;
        _connection = client.Connection;
        _connection.MessageReceived += OnMessageReceived;
        client.PropertyChanged += OnClientChanged;
        Filters = client.Profile.ExplorerFilters is { Length: > 0 } saved ? saved : DefaultFilters;

        _flushTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(150), DispatcherPriority.Background, OnFlushTick);
        _flushTimer.Stop();
        UpdateSummary();
    }

    /// <summary>The client this tab listens through; its settings and connection belong to the MQTT page.</summary>
    public MqttClientViewModel Client { get; }

    public ObservableCollection<TopicNodeViewModel> Roots { get; } = [];

    /// <summary>Topic filters to subscribe to, separated by commas.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SubscribeText))]
    public partial string Filters { get; set; } = DefaultFilters;

    /// <summary>The filters the tab is subscribed to, as typed; null while it is not subscribed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SubscribeText))]
    public partial string? SubscribedFilters { get; private set; }

    /// <summary>
    /// "Unsubscribe" while subscribed to the filters in the box; "Subscribe" when not subscribed, or
    /// when the box has been changed since, so that pressing it applies the new filters.
    /// </summary>
    public string SubscribeText => IsSubscribedToBox ? "Unsubscribe" : "Subscribe";

    private bool IsSubscribedToBox => SubscribedFilters is not null && SubscribedFilters == Filters;

    [ObservableProperty]
    public partial string? FilterError { get; set; }

    [ObservableProperty]
    public partial string Search { get; set; } = "";

    /// <summary>While paused, messages that arrive are discarded.</summary>
    [ObservableProperty]
    public partial bool IsPaused { get; set; }

    [ObservableProperty]
    public partial string Summary { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(ShownMessage), nameof(HasShownMessage))]
    [NotifyCanExecuteChangedFor(nameof(ClearRetainedCommand))]
    public partial TopicNodeViewModel? SelectedNode { get; set; }

    /// <summary>A message picked from the history; null shows the latest one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShownMessage), nameof(HasShownMessage))]
    public partial ReceivedMessage? SelectedHistoryMessage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PublishTopicError))]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    public partial string PublishTopic { get; set; } = "";

    [ObservableProperty]
    public partial string PublishBody { get; set; } = "";

    [ObservableProperty]
    public partial int PublishQos { get; set; }

    [ObservableProperty]
    public partial bool PublishRetain { get; set; }

    /// <summary>Whether the body is gzipped before it is sent.</summary>
    [ObservableProperty]
    public partial bool PublishGzip { get; set; }

    [ObservableProperty]
    public partial string PublishStatus { get; set; } = "";

    /// <summary>Says where the selected topic's graph went, once one has been added.</summary>
    [ObservableProperty]
    public partial string DashboardStatus { get; set; } = "";

    public bool HasSelection => SelectedNode is not null;

    public ReceivedMessage? ShownMessage => SelectedHistoryMessage ?? SelectedNode?.LastMessage;

    public bool HasShownMessage => ShownMessage is not null;

    public string? PublishTopicError => PublishTopic.Length == 0 ? null : TopicRules.Validate(PublishTopic);

    private bool CanPublish => Client.IsConnected && TopicRules.Validate(PublishTopic) is null;

    private bool CanClearRetained => Client.IsConnected && SelectedNode is { IsRetained: true };

    /// <summary>Starts listening. Called the first time the explorer is shown.</summary>
    public void Activate()
    {
        if (_activated)
            return;

        _activated = true;
        _flushTimer.Start();
        if (Client.Profile.ExplorerSubscribed)
            _ = SubscribeAsync();
    }

    [RelayCommand]
    private Task ToggleSubscriptionAsync() => IsSubscribedToBox ? UnsubscribeAsync() : SubscribeAsync();

    private async Task SubscribeAsync()
    {
        var filters = Filters.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (filters.Length == 0)
        {
            FilterError = "Enter at least one topic filter, for example #";
            return;
        }

        FilterError = filters.Select(TopicRules.ValidateFilter).FirstOrDefault(e => e is not null);
        if (FilterError is null)
        {
            // Set first, as retained values arrive as soon as the broker has the subscription.
            _listeningTo = filters;
            FilterError = await _connection.SetSubscriptionsAsync(SubscriptionOwner, filters);
        }

        if (FilterError is not null)
            return;

        SubscribedFilters = Filters;
        if (Client.Profile.ExplorerFilters != Filters || !Client.Profile.ExplorerSubscribed)
        {
            Client.Profile.ExplorerFilters = Filters;
            Client.Profile.ExplorerSubscribed = true;
            _save();
        }
    }

    private async Task UnsubscribeAsync()
    {
        _listeningTo = [];
        FilterError = await _connection.SetSubscriptionsAsync(SubscriptionOwner, []);
        if (FilterError is not null)
            return;

        SubscribedFilters = null;
        Client.Profile.ExplorerSubscribed = false;
        _save();
    }

    [RelayCommand]
    private void Clear()
    {
        while (_incoming.TryDequeue(out _))
        {
        }

        SelectedHistoryMessage = null;
        SelectedNode = null;
        Roots.Clear();
        _rootIndex.Clear();
        _topicCount = 0;
        _messageCount = 0;
        UpdateSummary();
    }

    [RelayCommand(CanExecute = nameof(CanPublish))]
    private async Task PublishAsync()
    {
        var body = Encoding.UTF8.GetBytes(PublishBody);
        await SendAsync(PublishTopic, PublishGzip ? Gzip.Compress(body) : body, PublishQos, PublishRetain);
    }

    /// <summary>Removes the selected topic's stored value by publishing an empty retained message.</summary>
    [RelayCommand(CanExecute = nameof(CanClearRetained))]
    private async Task ClearRetainedAsync()
    {
        if (SelectedNode is { } node)
            await SendAsync(node.FullTopic, [], 0, retain: true);
    }

    private async Task SendAsync(string topic, byte[] payload, int qos, bool retain)
    {
        var (ok, error) = await _connection.PublishAsync(topic, payload, qos, retain);
        if (ok)
            MqttMetrics.RecordSent(manual: true);
        else
            MqttMetrics.RecordFailed();

        PublishStatus = ok
            ? $"Sent to {topic} at {DateTime.Now:HH:mm:ss}"
            : $"Not sent: {error}";
    }

    /// <summary>The graph editor for the selected topic, starting with what is drawn here and what has been received.</summary>
    public GraphEditorViewModel? NewGraphEditor()
    {
        if (SelectedNode is not { } node)
            return null;

        var drawn = node.Trends.Select(t => t.Path).ToList();
        return _dashboards.NewEditor(Client, node.FullTopic, node.History.Reverse().ToList(), drawn);
    }

    partial void OnSelectedNodeChanged(TopicNodeViewModel? oldValue, TopicNodeViewModel? newValue)
    {
        SelectedHistoryMessage = null;
        DashboardStatus = "";
        if (oldValue is not null)
            oldValue.ChosenChanged -= OnChartsChosen;

        if (newValue is not null)
        {
            PublishTopic = newValue.FullTopic;
            newValue.ChosenPaths = Client.Profile.ExplorerCharts.GetValueOrDefault(newValue.FullTopic);
            newValue.UpdateTrends();
            newValue.ChosenChanged += OnChartsChosen;
        }
    }

    private void OnChartsChosen(TopicNodeViewModel node)
    {
        if (node.ChosenPaths is { } paths)
            Client.Profile.ExplorerCharts[node.FullTopic] = paths.ToList();
        else
            Client.Profile.ExplorerCharts.Remove(node.FullTopic);
        _save();
    }

    partial void OnSearchChanged(string value)
    {
        foreach (var root in Roots)
            ApplySearch(root, value.Trim());
    }

    // Arrives on the MQTT client's thread; the tree is only touched on the UI thread.
    private void OnMessageReceived(ReceivedMessage message)
    {
        if (_activated && !IsPaused && IsListeningTo(message.Topic))
            _incoming.Enqueue(message);
    }

    // The connection also delivers what the MQTT dashboards subscribe to.
    private bool IsListeningTo(string topic)
    {
        foreach (var filter in _listeningTo)
        {
            if (TopicRules.Matches(filter, topic))
                return true;
        }

        return false;
    }

    private void OnClientChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MqttClientViewModel.IsConnected))
        {
            PublishCommand.NotifyCanExecuteChanged();
            ClearRetainedCommand.NotifyCanExecuteChanged();
        }
    }

    private void OnFlushTick(object? sender, EventArgs e)
    {
        var taken = 0;
        var selectedUpdated = false;
        while (taken < MaxMessagesPerFlush && _incoming.TryDequeue(out var message))
        {
            taken++;
            var node = NodeFor(message.Topic);
            if (!node.HasValue)
                _topicCount++;
            node.Receive(message);
            selectedUpdated |= node == SelectedNode;
        }

        if (taken == 0)
            return;

        _messageCount += taken;
        MqttMetrics.RecordReceived(taken);
        UpdateSummary();

        if (selectedUpdated)
        {
            SelectedNode?.UpdateTrends();
            OnPropertyChanged(nameof(ShownMessage));
            OnPropertyChanged(nameof(HasShownMessage));
            ClearRetainedCommand.NotifyCanExecuteChanged();
        }
    }

    private TopicNodeViewModel NodeFor(string topic)
    {
        var levels = topic.Split('/');
        var search = Search.Trim();

        if (!_rootIndex.TryGetValue(levels[0], out var node))
        {
            node = new TopicNodeViewModel(levels[0], levels[0], null);
            _rootIndex[levels[0]] = node;
            TopicNodeViewModel.Insert(Roots, node);
            SetVisibility(node, search);
        }

        var path = levels[0];
        for (var i = 1; i < levels.Length; i++)
        {
            path = $"{path}/{levels[i]}";
            var child = node.Find(levels[i]);
            if (child is null)
            {
                child = node.Add(levels[i], path);
                SetVisibility(child, search);
            }

            node = child;
        }

        return node;
    }

    // A new node is shown if it matches the search; its ancestors are then shown and opened too.
    private static void SetVisibility(TopicNodeViewModel node, string search)
    {
        if (search.Length == 0)
            return;

        node.IsVisible = Matches(node, search);
        if (!node.IsVisible)
            return;

        for (var ancestor = node.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            ancestor.IsVisible = true;
            ancestor.IsExpanded = true;
        }
    }

    // Shows a node when it or anything below it matches. Returns whether it is shown.
    private static bool ApplySearch(TopicNodeViewModel node, string search)
    {
        var anyChild = false;
        foreach (var child in node.Children)
            anyChild |= ApplySearch(child, search);

        node.IsVisible = search.Length == 0 || anyChild || Matches(node, search);
        if (search.Length > 0 && anyChild)
            node.IsExpanded = true;
        return node.IsVisible;
    }

    private static bool Matches(TopicNodeViewModel node, string search)
        => node.FullTopic.Contains(search, StringComparison.OrdinalIgnoreCase);

    private void UpdateSummary()
    {
        var topics = _topicCount == 1 ? "1 topic" : $"{_topicCount:N0} topics";
        var messages = _messageCount == 1 ? "1 message" : $"{_messageCount:N0} messages";
        Summary = $"{topics}, {messages}";
    }

    /// <summary>Stops listening, when the client is removed.</summary>
    public void Dispose()
    {
        _flushTimer.Stop();
        _connection.MessageReceived -= OnMessageReceived;
        Client.PropertyChanged -= OnClientChanged;
    }
}
