using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Core.ViewModels;
using Joktan.Mqtt.ViewModels;
using Joktan.TopicDashboards.ViewModels;

namespace Joktan.TopicExplorer.ViewModels;

/// <summary>
/// Dev Tools / Brokers / Topic Explorer: one tab per MQTT client, each showing what arrives
/// on that client's broker. It can be moved into its own window. Specified in
/// docs/features/dev-tools/connectivity/topic-explorer/spec.md.
/// </summary>
public partial class TopicExplorerViewModel : PageViewModel
{
    private readonly TopicDashboardsViewModel _dashboards;
    private bool _activated;

    /// <param name="dashboards">Where a topic's graph goes when it is added to a dashboard.</param>
    public TopicExplorerViewModel(MqttViewModel mqtt, TopicDashboardsViewModel dashboards)
        : base("Topic Explorer", "Dev Tools / Brokers / Topic Explorer")
    {
        Mqtt = mqtt;
        _dashboards = dashboards;
        foreach (var client in mqtt.Clients)
            Tabs.Add(new ClientExplorerViewModel(client, mqtt.SaveSettings, dashboards));
        mqtt.Clients.CollectionChanged += OnClientsChanged;
        SelectedTab = TabFor(mqtt.SelectedClient) ?? Tabs.FirstOrDefault();
    }

    /// <summary>The MQTT page, which owns the clients and their connections.</summary>
    public MqttViewModel Mqtt { get; }

    /// <summary>One tab per client, in the same order as on the MQTT page.</summary>
    public ObservableCollection<ClientExplorerViewModel> Tabs { get; } = [];

    [ObservableProperty]
    public partial ClientExplorerViewModel? SelectedTab { get; set; }

    /// <summary>Opens the explorer in its own window. Set by the view.</summary>
    public Action? OpenWindow { get; set; }

    /// <summary>Closes the explorer's own window. Set by the view while the window is open.</summary>
    public Action? CloseWindow { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPage))]
    public partial bool IsPoppedOut { get; set; }

    public bool ShowPage => !IsPoppedOut;

    /// <summary>Starts every tab listening. Called the first time the explorer is shown.</summary>
    public void Activate()
    {
        if (_activated)
            return;

        _activated = true;
        foreach (var tab in Tabs)
            tab.Activate();
    }

    [RelayCommand]
    private void PopOut() => OpenWindow?.Invoke();

    [RelayCommand]
    private void Dock() => CloseWindow?.Invoke();

    private ClientExplorerViewModel? TabFor(MqttClientViewModel? client) => Tabs.FirstOrDefault(t => t.Client == client);

    // Tabs follow the clients on the MQTT page as they are added and removed.
    private void OnClientsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var tab in Tabs.Where(t => !Mqtt.Clients.Contains(t.Client)).ToList())
        {
            Tabs.Remove(tab);
            tab.Dispose();
        }

        for (var i = 0; i < Mqtt.Clients.Count; i++)
        {
            var client = Mqtt.Clients[i];
            if (TabFor(client) is not null)
                continue;

            var tab = new ClientExplorerViewModel(client, Mqtt.SaveSettings, _dashboards);
            Tabs.Insert(Math.Min(i, Tabs.Count), tab);
            if (_activated)
                tab.Activate();
        }

        if (SelectedTab is null || !Tabs.Contains(SelectedTab))
            SelectedTab = Tabs.FirstOrDefault();
    }
}
