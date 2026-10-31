using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Joktan.Mqtt.ViewModels;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Mqtt.Tests.ViewModels;

// No client here has a username or certificate, so nothing reaches the real credential store.
[Collection(ProcessStateCollection.Name)]
public class InboxViewModelTests : DataFolderTest
{
    private static InboxViewModel Person(TestBroker broker, string name, string id)
    {
        var settings = new MqttSettings
        {
            Clients = [new ClientProfile { Name = "other", Host = "h", ClientId = "other" + id }],
            Messaging = new MessagingSettings
            {
                DisplayName = name,
                Server = new ClientProfile { Name = "server", Host = "127.0.0.1", Port = broker.Port.ToString(), ClientId = id },
            },
        };
        return new InboxViewModel(new MqttViewModel(null, settings), new MessagingHistory());
    }

    private static async Task<InboxViewModel> Connected(TestBroker broker, string name, string id)
    {
        var inbox = Person(broker, name, id);
        await inbox.Client.ConnectCommand.ExecuteAsync(null);
        await Headless.Until(() => inbox.Client.IsConnected);
        return inbox;
    }

    private static async Task Say(InboxViewModel inbox, string draft)
    {
        inbox.Draft = draft;
        await inbox.SendCommand.ExecuteAsync(null);
    }

    private static Task Stop(params InboxViewModel[] inboxes) =>
        Task.WhenAll(inboxes.Select(i => i.Client.Connection.DisconnectAsync()));

    [Fact]
    public Task A_message_reaches_everyone_once_and_counts_as_unread_only_for_others() => Headless.Run(async () =>
    {
        await using var broker = await TestBroker.StartAsync();
        var ada = await Connected(broker, "ada", "c-ada");
        var bob = await Connected(broker, "bob", "c-bob");

        await Say(ada, "hello all");
        await Headless.Until(() => bob.Messages.Count == 1);
        Headless.Settle();
        await Task.Delay(100);
        Headless.Settle();

        Assert.Equal("", ada.Draft);
        Assert.Single(ada.Messages);
        Assert.Equal(0, ada.UnreadMessages);
        Assert.Equal(("ada", "hello all", false), (bob.Messages[0].Name, bob.Messages[0].Text, bob.Messages[0].IsDirect));
        Assert.True(bob.HasUnreadMessages);

        bob.IsMessagesOpen = true;
        Assert.False(bob.HasUnreadMessages);
        await Stop(ada, bob);
    });

    [Fact]
    public Task An_at_name_goes_to_that_person_only() => Headless.Run(async () =>
    {
        await using var broker = await TestBroker.StartAsync();
        var ada = await Connected(broker, "ada", "c-ada");
        var bob = await Connected(broker, "bob", "c-bob");
        var eve = await Connected(broker, "eve", "c-eve");
        await Headless.Until(() => ada.IsNameTaken("bob"));

        await Say(ada, "@BOB   just you");
        await Headless.Until(() => bob.Messages.Count == 1);
        await Task.Delay(150);
        Headless.Settle();

        Assert.Empty(eve.Messages);
        Assert.Equal(("just you", "to you"), (bob.Messages[0].Text, bob.Messages[0].DirectText));
        Assert.Equal("to @bob", ada.Messages[0].DirectText);
        Assert.Contains(broker.PublishedTopics, t => t == "joktan/to/bob");
        await Stop(ada, bob, eve);
    });

    [Theory]
    [InlineData("@nobody hello")]
    [InlineData("@not valid name")]
    [InlineData("@waytoolongforaname hello")]
    [InlineData("@ hello")]
    [InlineData("@bob")]
    public Task A_draft_starting_with_an_at_is_never_sent_to_everyone(string draft) => Headless.Run(async () =>
    {
        await using var broker = await TestBroker.StartAsync();
        var ada = await Connected(broker, "ada", "c-ada");
        var bob = await Connected(broker, "bob", "c-bob");
        await Headless.Until(() => ada.IsNameTaken("bob"));

        await Say(ada, draft);
        await Task.Delay(150);
        Headless.Settle();

        Assert.NotNull(ada.SendError);
        Assert.Equal(draft, ada.Draft);
        Assert.Empty(ada.Messages);
        Assert.Empty(bob.Messages);
        await Stop(ada, bob);
    });

    [Fact]
    public Task Without_a_name_you_can_write_to_everyone_but_not_to_one_person_and_cannot_be_written_to() => Headless.Run(async () =>
    {
        await using var broker = await TestBroker.StartAsync();
        var bob = await Connected(broker, "bob", "c-bob");
        var nobody = await Connected(broker, "", "c-none");
        await Headless.Until(() => nobody.IsNameTaken("bob"));

        await Say(nobody, "@bob hi");
        Assert.NotNull(nobody.SendError);

        await Say(nobody, "hi all");
        await Headless.Until(() => bob.Messages.Count == 1);

        Assert.Equal(MessagingSettings.AnonymousName, bob.Messages[0].Name);
        Assert.False(bob.Messages[0].CanReply);
        Assert.False(bob.IsNameTaken(MessagingSettings.AnonymousName));
        await Stop(bob, nobody);
    });

    [Fact]
    public Task A_reply_goes_to_the_writer_only_and_quotes_them() => Headless.Run(async () =>
    {
        await using var broker = await TestBroker.StartAsync();
        var ada = await Connected(broker, "ada", "c-ada");
        var bob = await Connected(broker, "bob", "c-bob");
        var eve = await Connected(broker, "eve", "c-eve");

        await Say(ada, "who has the key?");
        await Headless.Until(() => bob.Messages.Count == 1 && eve.Messages.Count == 1);

        var focused = 0;
        bob.DraftFocusRequested += () => focused++;
        Assert.False(ada.Messages[0].CanReply);
        bob.ReplyCommand.Execute(bob.Messages[0]);
        Assert.Equal("@ada ", bob.Draft);
        Assert.Contains("who has the key?", bob.ReplyingText);
        Assert.Equal(1, focused);

        bob.Draft += "I do";
        await bob.SendCommand.ExecuteAsync(null);
        await Headless.Until(() => ada.Messages.Count == 2);
        await Task.Delay(150);
        Headless.Settle();

        Assert.False(bob.IsReplying);
        Assert.Equal(("I do", true, "@ada: who has the key?"), (ada.Messages[1].Text, ada.Messages[1].IsDirect, ada.Messages[1].ReplyLine));
        Assert.Single(eve.Messages);
        await Stop(ada, bob, eve);
    });

    [Fact]
    public Task Cancelling_a_reply_empties_the_draft_and_names_are_suggested_after_an_at() => Headless.Run(async () =>
    {
        await using var broker = await TestBroker.StartAsync();
        var ada = await Connected(broker, "ada", "c-ada");
        var bob = await Connected(broker, "bob", "c-bob");
        var bea = await Connected(broker, "bea", "c-bea");
        await Headless.Until(() => ada.IsNameTaken("bob") && ada.IsNameTaken("bea"));

        ada.Draft = "@b";
        Assert.Equal(["bea", "bob"], ada.Suggestions);
        ada.PickSuggestionCommand.Execute("bob");
        Assert.Equal("@bob ", ada.Draft);
        Assert.Empty(ada.Suggestions);

        await Say(bob, "hello");
        await Headless.Until(() => ada.Messages.Count == 1);
        ada.ReplyCommand.Execute(ada.Messages[0]);
        ada.CancelReplyCommand.Execute(null);
        Assert.Equal(("", false), (ada.Draft, ada.IsReplying));
        await Stop(ada, bob, bea);
    });

    [Fact]
    public Task Someone_elses_direct_message_seen_by_a_Brokers_tab_stays_out_of_the_list() => Headless.Run(async () =>
    {
        await using var broker = await TestBroker.StartAsync();
        var ada = await Connected(broker, "ada", "c-ada");
        await ada.Client.Connection.SetSubscriptionsAsync("explorer", ["#"]);
        var payload = MessagingWire.Write(new ChatMessage { Id = "1", Sender = "x", Name = "x", Text = "secret", Sent = DateTimeOffset.Now });

        await broker.SendAsync("joktan/to/bob", Encoding.UTF8.GetString(payload));
        await broker.SendAsync("joktan/to/ada", Encoding.UTF8.GetString(payload).Replace("\"1\"", "\"2\""));
        await Headless.Until(() => ada.Messages.Count > 0);
        await Task.Delay(100);
        Headless.Settle();

        Assert.Equal("2", Assert.Single(ada.Messages).Message.Id);
        await Stop(ada);
    });

    [Fact]
    public Task Alerts_arrive_with_their_levels_and_can_be_filtered() => Headless.Run(async () =>
    {
        await using var broker = await TestBroker.StartAsync();
        var ada = await Connected(broker, "ada", "c-ada");
        var bob = await Connected(broker, "bob", "c-bob");

        bob.AlertLevel = AlertSeverity.Info;
        bob.AlertDraft = "fyi";
        await bob.SendAlertCommand.ExecuteAsync(null);
        await Headless.Until(() => ada.Alerts.Count == 1);
        Assert.True(ada.IsUnreadInfo);

        await broker.SendAsync("joktan/alerts/plant", """{"title":"Hot","severity":"warning"}""");
        await Headless.Until(() => ada.Alerts.Count == 2);
        Assert.True(ada.IsUnreadWarning);

        await broker.SendAsync("joktan/alerts/plant", """{"title":"Fire","severity":"critical"}""");
        await Headless.Until(() => ada.Alerts.Count == 3);
        Assert.False(ada.IsUnreadWarning || ada.IsUnreadInfo);
        Assert.Equal(3, ada.UnreadAlerts);
        Assert.Equal(["Critical", "Warning", "Info"], ada.Alerts.Select(a => a.Level));
        Assert.Equal(("bob", "fyi"), (ada.Alerts[2].Title, ada.Alerts[2].Text));

        ada.AlertFilter = "Warning";
        Assert.Equal(["Hot"], ada.Alerts.Where(a => a.IsShown).Select(a => a.Title));
        ada.AlertFilter = ada.AlertFilters[0];
        Assert.Equal(3, ada.Alerts.Count(a => a.IsShown));

        ada.IsAlertsOpen = true;
        Assert.False(ada.HasUnreadAlerts);
        ada.ClearAlertsCommand.Execute(null);
        Assert.False(ada.HasAlerts);
        await Stop(ada, bob);
    });

    [Fact]
    public Task A_retained_alert_counts_once_across_connections() => Headless.Run(async () =>
    {
        await using var broker = await TestBroker.StartAsync();
        await broker.SendAsync("joktan/alerts/x", "still broken", retain: true);
        var ada = await Connected(broker, "ada", "c-ada");
        await Headless.Until(() => ada.Alerts.Count == 1);

        await ada.Client.DisconnectCommand.ExecuteAsync(null);
        await ada.Client.ConnectCommand.ExecuteAsync(null);
        await Headless.Until(() => ada.Client.IsConnected);
        await Task.Delay(200);
        Headless.Settle();

        Assert.Single(ada.Alerts);
        await Stop(ada);
    });

    [Fact]
    public Task The_history_comes_back_with_what_was_unread_and_the_list_stays_bounded() => Headless.Run(async () =>
    {
        await using var broker = await TestBroker.StartAsync();
        var ada = await Connected(broker, "ada", "c-ada");
        for (var i = 0; i < InboxViewModel.MaxKept + 3; i++)
            await broker.SendAsync("joktan/alerts/x", $"a{i}");
        await Headless.Until(() => ada.Alerts.Any(a => a.Text == $"a{InboxViewModel.MaxKept + 2}"));
        Assert.Equal(InboxViewModel.MaxKept, ada.Alerts.Count);
        ada.SaveState();

        var again = new InboxViewModel(ada.Mqtt, MessagingHistory.Load());
        Assert.Equal(InboxViewModel.MaxKept, again.UnreadAlerts);
        await Stop(ada);
    });

    [Fact]
    public Task Connecting_at_start_can_be_switched_off() => Headless.Run(async () =>
    {
        await using var broker = await TestBroker.StartAsync();
        var off = Person(broker, "ada", "c-off");
        off.Mqtt.Messaging.ConnectOnStart = false;

        await off.StartAsync();

        Assert.False(off.Client.IsConnected);
        Assert.Empty(off.Client.Entries);
    });

    [Fact]
    public Task Connecting_at_start_tries_once_and_then_three_more_times() => Headless.Run(async () =>
    {
        var settings = new MqttSettings
        {
            Clients = [new ClientProfile { Name = "other", Host = "h", ClientId = "other" }],
            Messaging = new MessagingSettings { Server = new ClientProfile { Name = "server", Host = "127.0.0.1", Port = TestBroker.FreePort().ToString(), ClientId = "c" } },
        };
        Assert.True(settings.Messaging.ConnectOnStart);
        var inbox = new InboxViewModel(new MqttViewModel(null, settings), new MessagingHistory()) { StartRetryDelay = TimeSpan.FromMilliseconds(10) };

        await inbox.StartAsync();
        Headless.Settle();

        Assert.Equal(InboxViewModel.MaxStartRetries + 1, inbox.Client.Entries.Count(e => e.Event == "Failed"));
        Assert.True(inbox.Client.IsFailed);
    });

    [Fact]
    public Task Connecting_at_start_gets_through_when_the_server_comes_up_during_the_retries() => Headless.Run(async () =>
    {
        var port = TestBroker.FreePort();
        var settings = new MqttSettings
        {
            Clients = [new ClientProfile { Name = "other", Host = "h", ClientId = "other" }],
            Messaging = new MessagingSettings { Server = new ClientProfile { Name = "server", Host = "127.0.0.1", Port = port.ToString(), ClientId = "c" } },
        };
        var inbox = new InboxViewModel(new MqttViewModel(null, settings), new MessagingHistory()) { StartRetryDelay = TimeSpan.FromMilliseconds(500) };

        var starting = inbox.StartAsync();
        await Headless.Until(() => inbox.Client.Entries.Any(e => e.Event == "Failed"));
        await using var broker = await TestBroker.StartAsync(port: port);
        await starting;

        Assert.True(inbox.Client.IsConnected);
        await Stop(inbox);
    });

    [Fact]
    public Task Cancelling_while_connecting_at_start_ends_the_retries() => Headless.Run(async () =>
    {
        var settings = new MqttSettings
        {
            Clients = [new ClientProfile { Name = "other", Host = "h", ClientId = "other" }],
            Messaging = new MessagingSettings { Server = new ClientProfile { Name = "server", Host = "127.0.0.1", Port = TestBroker.FreePort().ToString(), ClientId = "c" } },
        };
        var inbox = new InboxViewModel(new MqttViewModel(null, settings), new MessagingHistory()) { StartRetryDelay = TimeSpan.FromMilliseconds(300) };

        var starting = inbox.StartAsync();
        await Headless.Until(() => inbox.Client.Entries.Any(e => e.Event == "Failed"));
        await inbox.Client.Connection.DisconnectAsync();
        Headless.Settle();
        await starting;

        Assert.Equal(1, inbox.Client.Entries.Count(e => e.Event == "Failed"));
        Assert.Equal(ConnectionState.Disconnected, inbox.Client.State);
    });

    [Fact]
    public Task A_new_prefix_moves_the_conversation_and_a_bad_one_is_not_used() => Headless.Run(async () =>
    {
        await using var broker = await TestBroker.StartAsync();
        var ada = await Connected(broker, "ada", "c-ada");
        var page = new MessagingViewModel(ada.Mqtt, ada);

        page.TopicPrefix = "bad/#";
        Assert.NotNull(page.TopicPrefixError);
        Assert.Equal(MessagingSettings.DefaultTopicPrefix, ada.Mqtt.Messaging.TopicPrefix);

        page.TopicPrefix = "team";
        await Task.Delay(100);
        await Say(ada, "moved");
        Assert.Contains("team/messages", broker.PublishedTopics);
        await Stop(ada);
    });

    [Fact]
    public Task A_name_that_breaks_the_rule_is_refused_and_one_in_use_is_warned_of() => Headless.Run(async () =>
    {
        await using var broker = await TestBroker.StartAsync();
        var ada = await Connected(broker, "ada", "c-ada");
        var bob = await Connected(broker, "bob", "c-bob");
        await Headless.Until(() => ada.IsNameTaken("bob"));
        var page = new MessagingViewModel(ada.Mqtt, ada);

        page.DisplayName = "Not Valid";
        Assert.NotNull(page.DisplayNameError);
        Assert.Equal("ada", ada.Mqtt.Messaging.DisplayName);

        page.DisplayName = "bob";
        Assert.Null(page.DisplayNameError);
        Assert.Contains("@bob", page.DisplayNameWarning);
        await Stop(ada, bob);
    });
}
