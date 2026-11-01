using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using Joktan.Mqtt;
using Joktan.TopicExplorer.ViewModels;
using Xunit;

namespace Joktan.TopicExplorer.Tests.ViewModels;

public class TopicNodeViewModelTests
{
    private static ReceivedMessage Message(string payload = "1", bool retain = false)
        => new("a/b", Encoding.UTF8.GetBytes(payload), 0, retain, new DateTime(2026, 11, 2));

    private static TopicNodeViewModel Node(string name = "n", TopicNodeViewModel? parent = null) => new(name, name, parent);

    [Fact]
    public void An_empty_level_says_so()
    {
        Assert.Equal("(empty level)", Node("").DisplayName);
        Assert.Equal("a", Node("a").DisplayName);
    }

    [Fact]
    public void Starts_without_a_value()
    {
        var node = Node();

        Assert.False(node.HasValue);
        Assert.Equal("", node.Preview);
        Assert.False(node.IsRetained);
        Assert.Equal("", node.Info);
    }

    [Fact]
    public void Keeps_children_in_order_ignoring_case_and_after_equals()
    {
        var list = new ObservableCollection<TopicNodeViewModel>();
        var firstB = Node("b");
        var secondB = Node("B");

        foreach (var node in new[] { firstB, Node("c"), Node("A"), secondB })
            TopicNodeViewModel.Insert(list, node);

        Assert.Equal(["A", "b", "B", "c"], list.Select(n => n.Name));
        Assert.Same(firstB, list[1]);
        Assert.Same(secondB, list[2]);
    }

    [Fact]
    public void Receiving_records_the_message_newest_first()
    {
        var node = Node();
        var first = Message();
        var second = Message("2", retain: true);

        node.Receive(first);
        node.Receive(second);

        Assert.Equal(2, node.MessageCount);
        Assert.True(node.HasValue);
        Assert.Same(second, node.LastMessage);
        Assert.Equal([second, first], node.History);
        Assert.Equal("= 2", node.Preview);
        Assert.True(node.IsRetained);
    }

    [Fact]
    public void The_history_keeps_its_length()
    {
        var node = Node();

        foreach (var _ in Enumerable.Range(0, TopicNodeViewModel.HistoryLength + 5))
            node.Receive(Message());

        Assert.Equal(TopicNodeViewModel.HistoryLength, node.History.Count);
        Assert.Equal(TopicNodeViewModel.HistoryLength + 5, node.MessageCount);
    }

    [Fact]
    public void Everything_above_counts_the_message_and_the_topic_once()
    {
        var top = Node("top");
        var middle = Node("middle", top);
        var leaf = Node("leaf", middle);

        leaf.Receive(Message());
        leaf.Receive(Message());

        Assert.Equal((1, 2), (middle.TopicCount, middle.SubtreeMessages));
        Assert.Equal((1, 2), (top.TopicCount, top.SubtreeMessages));
        Assert.Equal(0, top.MessageCount);
        Assert.Equal(0, leaf.TopicCount);
    }

    [Fact]
    public void Describes_what_sits_below_it()
    {
        var top = Node("top");
        Assert.Equal("", top.Info);

        var first = Node("first", top);
        first.Receive(Message());
        Assert.Equal("(1 topic, 1 message)", top.Info);

        first.Receive(Message());
        Assert.Equal("(1 topic, 2 messages)", top.Info);
        Assert.Equal("(2 messages)", first.Info);

        Node("second", top).Receive(Message());
        Assert.Equal("(2 topics, 3 messages)", top.Info);
    }

    [Fact]
    public void Remembers_what_the_user_chose_to_draw()
    {
        var node = Node();
        Assert.False(node.IsChosenByUser);

        node.ChosenPaths = ["a", "b"];

        Assert.True(node.IsChosenByUser);
        Assert.Equal(["a", "b"], node.ChosenPaths!.Order());

        node.ChosenPaths = null;
        Assert.False(node.IsChosenByUser);
    }

    [Fact]
    public void Says_how_many_more_trends_there_are()
    {
        var node = Node();
        Assert.False(node.HasMoreTrends);

        node.MoreTrends = 3;

        Assert.True(node.HasMoreTrends);
        Assert.Equal("3 more not shown", node.MoreTrendsText);
    }
}

public class TopicNodeTrendsTests
{
    private static ReceivedMessage Message(string payload, DateTime? time = null)
        => new("a", Encoding.UTF8.GetBytes(payload), 0, false, time ?? new DateTime(2026, 11, 2));

    private static TopicNodeViewModel Node(params string[] payloads)
    {
        var node = new TopicNodeViewModel("a", "a", null);
        foreach (var payload in payloads)
            node.Receive(Message(payload));
        node.UpdateTrends();
        return node;
    }

    [Fact]
    public void Children_can_be_found_and_are_added_in_order()
    {
        var node = new TopicNodeViewModel("a", "a", null);

        var b = node.Add("b", "a/b");
        node.Add("a", "a/a");

        Assert.Same(b, node.Find("b"));
        Assert.Null(node.Find("c"));
        Assert.Equal(["a", "b"], node.Children.Select(c => c.Name));
        Assert.Same(node, b.Parent);
    }

    [Fact]
    public void Numbers_that_change_are_drawn_and_those_that_do_not_are_only_offered()
    {
        var node = Node("""{"moving":1,"still":5}""", """{"moving":2,"still":5}""");

        Assert.True(node.HasNumbers);
        Assert.True(node.HasTrend);
        Assert.Equal(["moving", "still"], node.Choices.Select(c => c.Path));
        Assert.Equal("moving", Assert.Single(node.Trends).Path);
        Assert.True(node.Choices[0].IsChosen);
        Assert.False(node.Choices[1].IsChosen);
        Assert.Equal("2", node.Choices[0].LatestText);
    }

    [Theory]
    [InlineData(1893456000, 1893456001)]
    [InlineData(1893456000000, 1893456001000)]
    public void A_clock_is_not_drawn_by_itself(long first, long second)
    {
        var node = Node($$"""{"at":{{first}}}""", $$"""{"at":{{second}}}""");

        Assert.False(node.HasTrend);
        Assert.True(node.HasNumbers);
    }

    [Theory]
    [InlineData(1893456001, 1893456000)]
    [InlineData(1893456000, 10000000001)]
    [InlineData(1893456000000, 10000000000001)]
    public void A_number_that_only_looks_like_a_clock_is_drawn(long first, long second)
    {
        Assert.True(Node($$"""{"at":{{first}}}""", $$"""{"at":{{second}}}""").HasTrend);
    }

    [Fact]
    public void Whole_arrays_come_before_their_items_and_items_are_not_drawn_by_themselves()
    {
        var node = Node("""{"list":[1,2]}""", """{"list":[3,4]}""");

        Assert.Equal("list[*]", node.Choices[0].Path);
        Assert.All(node.Trends, t => Assert.DoesNotContain("[0]", t.Path));
    }

    [Fact]
    public void Choosing_a_number_draws_it_and_going_back_follows_what_changes()
    {
        var node = Node("""{"moving":1,"still":5}""", """{"moving":2,"still":5}""");
        var chosen = new System.Collections.Generic.List<TopicNodeViewModel>();
        node.ChosenChanged += chosen.Add;

        node.Choices.First(c => c.Path == "still").IsChosen = true;

        Assert.Equal(["moving", "still"], node.Trends.Select(t => t.Path));
        Assert.Equal(2, node.ChosenPaths!.Count);
        Assert.Single(chosen);

        node.Choices.First(c => c.Path == "moving").IsChosen = false;
        Assert.Equal("still", Assert.Single(node.Trends).Path);

        node.UseAutomaticCommand.Execute(null);
        Assert.Null(node.ChosenPaths);
        Assert.Equal("moving", Assert.Single(node.Trends).Path);
        Assert.Equal(3, chosen.Count);
    }

    [Fact]
    public void What_is_drawn_is_kept_up_to_date_without_being_replaced()
    {
        var node = Node("""{"v":1}""", """{"v":2}""");
        var trend = node.Trends[0];
        var choice = node.Choices[0];

        node.Receive(Message("""{"v":9}"""));
        node.UpdateTrends();

        Assert.Same(trend, node.Trends[0]);
        Assert.Same(choice, node.Choices[0]);
        Assert.Equal("9", trend.LatestText);

        node.Receive(Message("""{"v":9,"w":1}"""));
        node.UpdateTrends();
        Assert.Same(choice, node.Choices[0]);
        Assert.Equal(2, node.Choices.Count);
    }

    [Fact]
    public void More_changing_numbers_than_there_is_room_for_are_counted()
    {
        var names = Enumerable.Range(0, TopicNodeViewModel.MaxTrendsShown + 3).Select(i => $"n{i}").ToList();
        string Payload(int value) => "{" + string.Join(",", names.Select(n => $"\"{n}\":{value}")) + "}";

        var node = Node(Payload(1), Payload(2));

        Assert.Equal(TopicNodeViewModel.MaxTrendsShown, node.Trends.Count);
        Assert.Equal(3, node.MoreTrends);
        Assert.True(node.HasMoreTrends);
        Assert.Equal("3 more not shown", node.MoreTrendsText);
    }
}
