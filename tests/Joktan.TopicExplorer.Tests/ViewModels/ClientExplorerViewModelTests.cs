using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Joktan.Mqtt;
using Joktan.Mqtt.Tests;
using Joktan.TopicExplorer.ViewModels;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.TopicExplorer.Tests.ViewModels;

public class ClientExplorerViewModelTests : ExplorerTest
{
    private int _saves;

    private ClientExplorerViewModel Tab(Joktan.Mqtt.ViewModels.MqttViewModel mqtt) => new(mqtt.Clients[0], () => _saves++, Dashboards(mqtt));

    private async Task<(TestBroker Broker, ClientExplorerViewModel Tab)> Listening()
    {
        var broker = await TestBroker.StartAsync();
        var tab = Tab(Mqtt(broker.Port));
        await Connect(tab.Client);
        tab.Activate();
        await Headless.Until(() => tab.SubscribedFilters is not null);
        return (broker, tab);
    }

    private static TopicNodeViewModel Node(ClientExplorerViewModel tab, string topic)
    {
        var nodes = tab.Roots;
        TopicNodeViewModel? node = null;
        foreach (var level in topic.Split('/'))
        {
            node = nodes.First(n => n.Name == level);
            nodes = node.Children;
        }

        return node!;
    }

    [Fact]
    public Task Starts_with_nothing_and_the_filters_kept_for_the_client() => Headless.Run(() =>
    {
        var mqtt = Mqtt();
        Assert.Equal("#", Tab(mqtt).Filters);

        mqtt.Clients[0].Profile.ExplorerFilters = "a/#";
        var tab = Tab(mqtt);

        Assert.Equal("a/#", tab.Filters);
        Assert.Equal("0 topics, 0 messages", tab.Summary);
        Assert.Equal("Subscribe", tab.SubscribeText);
        Assert.False(tab.HasSelection);
        Assert.False(tab.HasShownMessage);
        Assert.Null(tab.ShownMessage);
        Assert.Null(tab.PublishTopicError);
        Assert.Null(tab.NewGraphEditor());

        mqtt.Clients[0].Profile.ExplorerFilters = "";
        Assert.Equal("#", Tab(mqtt).Filters);
    });

    [Fact]
    public Task Listens_once_it_is_first_shown_unless_it_was_unsubscribed() => Headless.Run(async () =>
    {
        var (broker, tab) = await Listening();
        await using (broker)
        {
            Assert.Equal("Unsubscribe", tab.SubscribeText);
            tab.Activate();

            await tab.ToggleSubscriptionCommand.ExecuteAsync(null);
            Assert.Null(tab.SubscribedFilters);
            Assert.False(tab.Client.Profile.ExplorerSubscribed);
            Assert.Equal(1, _saves);

            var stays = Tab(Mqtt(broker.Port));
            stays.Client.Profile.ExplorerSubscribed = false;
            stays.Activate();
            Assert.Null(stays.SubscribedFilters);
        }
    });

    [Fact]
    public Task Filters_are_checked_and_new_ones_are_kept() => Headless.Run(async () =>
    {
        var (broker, tab) = await Listening();
        await using (broker)
        {
            tab.Filters = " , ";
            Assert.Equal("Subscribe", tab.SubscribeText);
            await tab.ToggleSubscriptionCommand.ExecuteAsync(null);
            Assert.Contains("at least one", tab.FilterError);

            tab.Filters = "bad#";
            await tab.ToggleSubscriptionCommand.ExecuteAsync(null);
            Assert.NotNull(tab.FilterError);

            tab.Filters = "refused/x";
            await tab.ToggleSubscriptionCommand.ExecuteAsync(null);
            Assert.Contains("refused", tab.FilterError);

            tab.Filters = "a/#, b/#";
            await tab.ToggleSubscriptionCommand.ExecuteAsync(null);
            Assert.Null(tab.FilterError);
            Assert.Equal("a/#, b/#", tab.Client.Profile.ExplorerFilters);
            Assert.True(_saves > 0);

            // Unchanged, subscribing again saves nothing.
            await tab.ToggleSubscriptionCommand.ExecuteAsync(null);
            _saves = 0;
            tab.Client.Profile.ExplorerSubscribed = true;
            await tab.ToggleSubscriptionCommand.ExecuteAsync(null);
            Assert.Equal(0, _saves);
        }
    });

    [Fact]
    public Task An_unsubscribe_that_fails_stays_subscribed() => Headless.Run(async () =>
    {
        var (broker, tab) = await Listening();
        await using (broker)
        {
            await broker.StopAsync();
            await Headless.Until(() => !tab.Client.Connection.IsConnected);

            await tab.ToggleSubscriptionCommand.ExecuteAsync(null);

            // Not connected, so there is nothing to refuse it.
            Assert.Null(tab.SubscribedFilters);
        }
    });

    [Fact]
    public Task Messages_build_the_tree_and_the_summary() => Headless.Run(async () =>
    {
        var (broker, tab) = await Listening();
        await using (broker)
        {
            await broker.SendAsync("a/b", "1");
            await Headless.Until(() => tab.Summary == "1 topic, 1 message");
            await broker.SendAsync("a/c", "2");
            await broker.SendAsync("z", "3");
            await Headless.Until(() => tab.Summary == "3 topics, 3 messages");

            Assert.Equal(["a", "z"], tab.Roots.Select(r => r.Name));
            Assert.True(Node(tab, "a/b").HasValue);
            Assert.False(Node(tab, "a").HasValue);

            await broker.SendAsync("z", "4");
            await Headless.Until(() => tab.Summary == "3 topics, 4 messages");
        }
    });

    [Fact]
    public Task Messages_are_only_taken_while_shown_listening_and_not_paused() => Headless.Run(async () =>
    {
        await using var broker = await TestBroker.StartAsync();
        var tab = Tab(Mqtt(broker.Port));
        await Connect(tab.Client);
        await tab.Client.Connection.SetSubscriptionsAsync("other", ["#"]);
        var seen = 0;
        tab.Client.Connection.MessageReceived += _ => seen++;

        await broker.SendAsync("before", "1");
        await Headless.Until(() => seen == 1);

        tab.Filters = "wanted/#";
        tab.Activate();
        await Headless.Until(() => tab.SubscribedFilters is not null);
        await broker.SendAsync("other/topic", "1");
        await broker.SendAsync("wanted/topic", "1");
        await Headless.Until(() => tab.Summary == "1 topic, 1 message");
        Assert.Equal(["wanted"], tab.Roots.Select(r => r.Name));

        tab.IsPaused = true;
        await broker.SendAsync("wanted/paused", "1");
        await Headless.Until(() => seen >= 4);
        tab.IsPaused = false;
        await broker.SendAsync("wanted/after", "1");
        await Headless.Until(() => tab.Summary == "2 topics, 2 messages");
        Assert.DoesNotContain(tab.Roots[0].Children, c => c.Name == "paused");
    });

    [Fact]
    public Task Clearing_forgets_everything() => Headless.Run(async () =>
    {
        var (broker, tab) = await Listening();
        await using (broker)
        {
            await broker.SendAsync("a", "1");
            await Headless.Until(() => tab.Roots.Count == 1);
            tab.SelectedNode = tab.Roots[0];

            tab.ClearCommand.Execute(null);

            Assert.Empty(tab.Roots);
            Assert.Null(tab.SelectedNode);
            Assert.Equal("0 topics, 0 messages", tab.Summary);
        }
    });

    [Fact]
    public Task Selecting_a_topic_shows_its_latest_message_or_one_from_its_history() => Headless.Run(async () =>
    {
        var (broker, tab) = await Listening();
        await using (broker)
        {
            await broker.SendAsync("a", "1");
            await Headless.Until(() => tab.Roots.Count == 1);
            var node = tab.Roots[0];

            tab.SelectedNode = node;

            Assert.True(tab.HasSelection);
            Assert.Equal("a", tab.PublishTopic);
            Assert.True(tab.HasShownMessage);
            Assert.Same(node.LastMessage, tab.ShownMessage);

            await broker.SendAsync("a", "2");
            await Headless.Until(() => tab.ShownMessage!.Text == "2");

            var old = node.History.Last();
            tab.SelectedHistoryMessage = old;
            Assert.Same(old, tab.ShownMessage);

            tab.SelectedNode = null;
            Assert.Null(tab.SelectedHistoryMessage);
            Assert.False(tab.HasShownMessage);
        }
    });

    [Fact]
    public Task Publishing_needs_a_connection_and_a_good_topic_and_says_how_it_went() => Headless.Run(async () =>
    {
        await using var broker = await TestBroker.StartAsync();
        var tab = Tab(Mqtt(broker.Port));
        tab.PublishTopic = "good/topic";
        Assert.False(tab.PublishCommand.CanExecute(null));

        await Connect(tab.Client);
        await Headless.Until(() => tab.PublishCommand.CanExecute(null));

        tab.PublishTopic = "bad/#";
        Assert.NotNull(tab.PublishTopicError);
        Assert.False(tab.PublishCommand.CanExecute(null));

        tab.PublishTopic = "good/topic";
        tab.PublishBody = "hello";
        tab.PublishQos = 1;
        tab.PublishRetain = false;
        await tab.PublishCommand.ExecuteAsync(null);
        Assert.StartsWith("Sent to good/topic at ", tab.PublishStatus);
        Assert.Contains("good/topic", broker.PublishedTopics);

        tab.PublishGzip = true;
        await tab.PublishCommand.ExecuteAsync(null);
        await Headless.Until(() => broker.Published.Count == 2);
        Assert.Equal(0x1F, broker.Published.Last().Payload[0]);

        await tab.Client.Connection.DisconnectAsync();
        await tab.PublishCommand.ExecuteAsync(null);
        Assert.Equal("Not sent: Not connected", tab.PublishStatus);
    });

    [Fact]
    public Task A_stored_value_can_be_cleared() => Headless.Run(async () =>
    {
        var (broker, tab) = await Listening();
        await using (broker)
        {
            await tab.ClearRetainedCommand.ExecuteAsync(null);
            Assert.False(tab.ClearRetainedCommand.CanExecute(null));

            await broker.SendAsync("kept", "1", retain: true);
            await Headless.Until(() => tab.Roots.Count == 1);

            // It arrived live, so resubscribing brings it as a retained value.
            await tab.ToggleSubscriptionCommand.ExecuteAsync(null);
            tab.ClearCommand.Execute(null);
            await tab.ToggleSubscriptionCommand.ExecuteAsync(null);
            await Headless.Until(() => tab.Roots.Count == 1);
            tab.SelectedNode = tab.Roots[0];
            Assert.True(tab.ClearRetainedCommand.CanExecute(null));

            await tab.ClearRetainedCommand.ExecuteAsync(null);

            await Headless.Until(() => broker.Published.Any(p => p.Topic == "kept" && p.Payload.Length == 0));
        }
    });

    [Fact]
    public Task Searching_shows_what_matches_and_the_way_to_it() => Headless.Run(async () =>
    {
        var (broker, tab) = await Listening();
        await using (broker)
        {
            await broker.SendAsync("home/kitchen", "1");
            await broker.SendAsync("work/desk", "2");
            await Headless.Until(() => tab.Summary.StartsWith("2 topics", StringComparison.Ordinal));
            Node(tab, "home").IsExpanded = false;

            tab.Search = " KITCHEN ";

            Assert.True(Node(tab, "home").IsVisible);
            Assert.True(Node(tab, "home").IsExpanded);
            Assert.True(Node(tab, "home/kitchen").IsVisible);
            Assert.False(Node(tab, "work").IsVisible);

            // What arrives during a search follows it.
            await broker.SendAsync("home/hall", "3");
            await broker.SendAsync("work/kitchen", "4");
            await Headless.Until(() => tab.Summary.StartsWith("4 topics", StringComparison.Ordinal));
            Assert.False(Node(tab, "home/hall").IsVisible);
            Assert.True(Node(tab, "work/kitchen").IsVisible);
            Assert.True(Node(tab, "work").IsVisible);
            await broker.SendAsync("new/root", "5");
            await Headless.Until(() => tab.Summary.StartsWith("5 topics", StringComparison.Ordinal));
            Assert.False(tab.Roots.First(r => r.Name == "new").IsVisible);

            tab.Search = "";
            Assert.All(tab.Roots, r => Assert.True(r.IsVisible));
        }
    });

    [Fact]
    public Task The_numbers_chosen_for_a_topic_are_kept_for_the_client() => Headless.Run(async () =>
    {
        var (broker, tab) = await Listening();
        await using (broker)
        {
            await broker.SendAsync("a", """{"v":1,"w":2}""");
            await broker.SendAsync("a", """{"v":2,"w":2}""");
            await Headless.Until(() => tab.Summary.Contains("2 messages"));
            tab.SelectedNode = tab.Roots[0];
            var node = tab.Roots[0];
            Assert.True(node.HasNumbers);
            _saves = 0;

            node.Choices.First(c => c.Path == "w").IsChosen = true;

            Assert.Contains("w", tab.Client.Profile.ExplorerCharts["a"]);
            Assert.Equal(1, _saves);

            node.UseAutomaticCommand.Execute(null);
            Assert.False(tab.Client.Profile.ExplorerCharts.ContainsKey("a"));

            // Another selected, it no longer tells this tab.
            tab.SelectedNode = null;
            _saves = 0;
            node.Choices[0].IsChosen = !node.Choices[0].IsChosen;
            Assert.Equal(0, _saves);
        }
    });

    [Fact]
    public Task A_graph_starts_from_what_is_drawn_and_has_been_heard() => Headless.Run(async () =>
    {
        var (broker, tab) = await Listening();
        await using (broker)
        {
            await broker.SendAsync("sensors/temp", """{"v":1}""");
            await broker.SendAsync("sensors/temp", """{"v":2}""");
            await Headless.Until(() => tab.Summary.Contains("2 messages"));
            tab.SelectedNode = tab.Roots[0].Children[0];

            var editor = tab.NewGraphEditor()!;

            Assert.Equal(["sensors/temp"], editor.Topics);
            Assert.True(editor.Values.First(v => v.Path == "v").IsChosen);
            await editor.StopAsync();
        }
    });

    [Fact]
    public Task Disposing_stops_it_listening() => Headless.Run(async () =>
    {
        var (broker, tab) = await Listening();
        await using (broker)
        {
            tab.Dispose();

            await broker.SendAsync("a", "1");
            await Task.Delay(300, TestContext.Current.CancellationToken);

            Assert.Empty(tab.Roots);
        }
    });
}

public class TopicExplorerViewModelTests : ExplorerTest
{
    [Fact]
    public Task There_is_a_tab_for_each_client_and_the_selected_one_first() => Headless.Run(() =>
    {
        var mqtt = Mqtt(1883, "a", "b");
        mqtt.SelectedClient = mqtt.Clients[1];

        var explorer = Explorer(mqtt);

        Assert.Equal(2, explorer.Tabs.Count);
        Assert.Same(mqtt.Clients[1], explorer.SelectedTab!.Client);
        Assert.Same(mqtt, explorer.Mqtt);
        Assert.True(explorer.ShowPage);
    });

    [Fact]
    public Task Tabs_follow_the_clients() => Headless.Run(async () =>
    {
        var mqtt = Mqtt(1883, "a", "b");
        var explorer = Explorer(mqtt);
        explorer.SelectedTab = explorer.Tabs[1];
        explorer.Activate();
        explorer.Activate();

        mqtt.Clients[1].RemoveClientCommand.Execute(null);
        Assert.Same(explorer.Tabs[0], explorer.SelectedTab);
        Assert.Single(explorer.Tabs);

        mqtt.EditClientSettings = _ => Task.FromResult(true);
        await mqtt.AddClientCommand.ExecuteAsync(null);
        Assert.Equal(2, explorer.Tabs.Count);
        Assert.Same(mqtt.Clients[1], explorer.Tabs[1].Client);

        // Not yet shown, a new tab does not start listening.
        var quiet = Mqtt(1883, "x");
        var waiting = Explorer(quiet);
        quiet.EditClientSettings = _ => Task.FromResult(true);
        await quiet.AddClientCommand.ExecuteAsync(null);
        Assert.Equal(2, waiting.Tabs.Count);
    });

    [Fact]
    public Task Moving_to_a_window_is_left_to_the_view() => Headless.Run(() =>
    {
        var explorer = Explorer(Mqtt());
        explorer.PopOutCommand.Execute(null);
        explorer.DockCommand.Execute(null);

        var calls = new List<string>();
        explorer.OpenWindow = () => calls.Add("open");
        explorer.CloseWindow = () => calls.Add("close");
        explorer.PopOutCommand.Execute(null);
        explorer.DockCommand.Execute(null);

        Assert.Equal(["open", "close"], calls);
        explorer.IsPoppedOut = true;
        Assert.False(explorer.ShowPage);
    });

    [Fact]
    public Task The_messaging_server_has_a_tab_only_while_it_is_switched_on() => Headless.Run(() =>
    {
        var mqtt = Mqtt(1883, "a");
        var explorer = Explorer(mqtt);
        Assert.Equal(["a"], explorer.Tabs.Select(t => t.Client.Name));

        mqtt.IsMessagingClientShown = true;
        Assert.Equal(["a", MessagingSettings.DefaultServerName], explorer.Tabs.Select(t => t.Client.Name));

        explorer.SelectedTab = explorer.Tabs[1];
        mqtt.IsMessagingClientShown = false;
        Assert.Equal(["a"], explorer.Tabs.Select(t => t.Client.Name));
        Assert.Equal("a", explorer.SelectedTab!.Client.Name);
    });

    [Fact]
    public Task Switching_the_server_off_stops_what_its_tab_subscribed_to_but_not_the_connection() => Headless.Run(async () =>
    {
        await using var broker = await TestBroker.StartAsync();
        var mqtt = Mqtt(broker.Port, "a");
        mqtt.MessagingClient.Profile.Host = "127.0.0.1";
        mqtt.MessagingClient.Profile.Port = broker.Port.ToString();
        var explorer = Explorer(mqtt);
        explorer.Activate();
        mqtt.IsMessagingClientShown = true;
        await Connect(mqtt.MessagingClient);

        var received = 0;
        mqtt.MessagingClient.Connection.MessageReceived += _ => Interlocked.Increment(ref received);
        await Headless.Until(() =>
        {
            _ = broker.SendAsync("t/one", "1");
            return Volatile.Read(ref received) > 0;
        });

        mqtt.IsMessagingClientShown = false;
        await Task.Delay(300);
        Interlocked.Exchange(ref received, 0);
        await broker.SendAsync("t/two", "2");
        await Task.Delay(300);

        Assert.Equal(0, Volatile.Read(ref received));
        Assert.True(mqtt.MessagingClient.IsConnected);
        await mqtt.MessagingClient.DisconnectCommand.ExecuteAsync(null);
    });
}

public class FieldViewModelsTests
{
    [Theory]
    [InlineData("", "The whole payload", "value")]
    [InlineData("a", "a", "value")]
    [InlineData("a[0]", "a[0]", "item")]
    [InlineData("a[*]", "a[*]", "array")]
    public void A_choice_says_what_it_is(string path, string display, string kind)
    {
        var choice = new FieldChoiceViewModel(path, _ => { });

        Assert.Equal(display, choice.DisplayPath);
        Assert.Equal(kind, choice.Kind);
    }

    [Fact]
    public void Showing_a_choice_does_not_count_as_making_it()
    {
        var toggled = 0;
        var choice = new FieldChoiceViewModel("a", _ => toggled++);

        choice.Show(true);
        Assert.Equal(0, toggled);
        Assert.True(choice.IsChosen);

        choice.IsChosen = false;
        Assert.Equal(1, toggled);

        choice.LatestText = "1";
        Assert.Equal("1", choice.LatestText);
    }

    [Fact]
    public void A_trend_gives_its_range_and_a_flat_line_some_room()
    {
        var trend = new FieldTrendViewModel("a", [1, 3, 2]);

        Assert.True(trend.HasPath);
        Assert.Equal(1, trend.Minimum);
        Assert.Equal(3, trend.Maximum);
        Assert.Equal("1", trend.MinimumText);
        Assert.Equal("3", trend.MaximumText);
        Assert.Equal("2", trend.LatestText);

        trend.Update([5, 5]);
        Assert.Equal(4, trend.Minimum);
        Assert.Equal(6, trend.Maximum);

        var empty = new FieldTrendViewModel("", []);
        Assert.False(empty.HasPath);
        Assert.Equal("", empty.MinimumText);
        Assert.Equal("", empty.MaximumText);
        Assert.Equal("", empty.LatestText);
    }
}
