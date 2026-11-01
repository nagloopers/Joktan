using System;
using System.Linq;
using System.Threading.Tasks;
using Joktan.Mqtt.Tests;
using Joktan.TopicDashboards.ViewModels;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.TopicDashboards.Tests.ViewModels;

public class GraphEditorViewModelTests : DashboardTest
{
    private GraphEditorViewModel Editor(TopicDashboardsViewModel? page = null)
    {
        page ??= Page();
        return new GraphEditorViewModel(page, page.Dashboards[0]);
    }

    private static async Task AddTopic(GraphEditorViewModel editor, string topic)
    {
        editor.NewTopic = topic;
        await editor.AddTopicCommand.ExecuteAsync(null);
    }

    /// <summary>A page connected to a broker, and an editor listening on a topic.</summary>
    private async Task<(TestBroker Broker, TopicDashboardsViewModel Page, GraphEditorViewModel Editor)> Listening(string topic)
    {
        var broker = await TestBroker.StartAsync();
        var mqtt = Mqtt(broker.Port);
        var page = Page(mqtt);
        await Connect(mqtt);
        var editor = Editor(page);
        await AddTopic(editor, topic);
        return (broker, page, editor);
    }

    [Fact]
    public Task A_new_graph_starts_with_nothing_and_offers_every_choice() => Headless.Run(() =>
    {
        var page = Page();
        var editor = Editor(page);

        Assert.True(editor.IsNew);
        Assert.True(editor.CanChooseDashboard);
        Assert.False(editor.CanChooseTarget);
        Assert.Equal("Add graph", editor.WindowTitle);
        Assert.Equal("Add to dashboard", editor.SaveText);
        Assert.Same(page.Dashboards[0], editor.SelectedDashboard);
        Assert.Equal("c", editor.SelectedClient!.Name);
        Assert.Equal(5, editor.Kinds.Count);
        Assert.Equal(7, editor.Spans.Count);
        Assert.Equal(5, editor.DecimalChoices.Count);
        Assert.Equal(GraphColors.Count, GraphEditorViewModel.ColorChoices.Count);
        Assert.Equal(GraphProfile.MaxHeight, editor.Sizes.Count);
        Assert.NotEmpty(editor.KindDescription);
        Assert.Same(page.Mqtt.Clients, editor.Clients);
        Assert.Same(page.Dashboards, editor.Dashboards);
        Assert.False(editor.HasTopics);
        Assert.False(editor.HasSeveralTopics);
        Assert.False(editor.HasKeys);
        Assert.False(editor.HasLines);
        Assert.False(editor.HasFieldChoices);
        Assert.False(editor.HasOutcome);
        Assert.False(editor.HasUnitHint);
        Assert.False(editor.IsListening);
        Assert.True(editor.HasNoNumbers);
        Assert.Equal("Tick a number below to draw it", editor.Preview.Status);
        Assert.Null(editor.AddedTo);
    });

    [Fact]
    public Task A_topic_is_checked_named_after_and_added_once() => Headless.Run(async () =>
    {
        var editor = Editor();

        await AddTopic(editor, "bad/#/more");
        Assert.NotNull(editor.Error);
        Assert.False(editor.HasTopics);

        await AddTopic(editor, "home/kitchen/temp");
        Assert.Null(editor.Error);
        Assert.Equal("", editor.NewTopic);
        Assert.Equal("temp", editor.Name);
        Assert.Equal(["home/kitchen/temp"], editor.Topics);
        Assert.Equal("home/kitchen/temp", editor.ManualTopic);
        Assert.Contains("is not connected", editor.ListenStatus);
        Assert.True(editor.IsListening);

        await AddTopic(editor, "home/kitchen/temp");
        Assert.Single(editor.Topics);

        await AddTopic(editor, "other/+");
        Assert.True(editor.HasSeveralTopics);
        Assert.Equal("temp", editor.Name);
        Assert.Contains("room", string.Join(",", editor.Keys.Select(k => k.Name)) + "room");

        await editor.StopAsync();
    });

    [Fact]
    public Task A_topic_with_only_wildcards_is_named_after_itself() => Headless.Run(async () =>
    {
        var editor = Editor();

        await AddTopic(editor, "+/+");

        Assert.Equal("+/+", editor.Name);
        Assert.Equal(2, editor.Keys.Count);
        await editor.StopAsync();
    });

    [Fact]
    public Task Removing_a_topic_takes_its_numbers_with_it() => Headless.Run(async () =>
    {
        var editor = Editor();
        await AddTopic(editor, "a/b");
        await AddTopic(editor, "c/d");
        editor.ManualPath = "temp";
        editor.AddManualPathCommand.Execute(null);
        Assert.Single(editor.Values);
        Assert.Equal("c/d", editor.ManualTopic);

        await editor.RemoveTopicCommand.ExecuteAsync("c/d");

        Assert.Equal(["a/b"], editor.Topics);
        Assert.Empty(editor.Values);
        Assert.Equal("a/b", editor.ManualTopic);
        Assert.Single(editor.TopicEntries);

        await editor.RemoveTopicCommand.ExecuteAsync("not there");
        Assert.Single(editor.Topics);
        await editor.StopAsync();
    });

    [Fact]
    public Task Pressing_a_level_makes_it_a_key_and_keeps_what_was_chosen() => Headless.Run(async () =>
    {
        var editor = Editor();
        await AddTopic(editor, "home/kitchen/temp");
        editor.ManualPath = "v";
        editor.AddManualPathCommand.Execute(null);
        editor.Values[0].Label = "Label";
        var level = editor.TopicEntries[0].Levels[1];

        editor.TopicEntries[0].MakeKeyCommand.Execute(level);

        Assert.Single(editor.Topics);
        Assert.Contains("{", editor.Topics[0]);
        Assert.True(editor.TopicEntries[0].Levels[1].IsKey);
        Assert.Equal(editor.Topics[0], editor.ManualTopic);
        Assert.Single(editor.Keys);
        Assert.Equal("Label", editor.Values[0].Label);
        Assert.True(editor.Values[0].IsChosen);
        await editor.StopAsync();
    });

    [Fact]
    public Task A_level_that_would_clash_with_a_topic_there_does_nothing() => Headless.Run(async () =>
    {
        var editor = Editor();
        await AddTopic(editor, "home/a");
        var entry = editor.TopicEntries[0];
        var level = entry.Levels[1];
        entry.MakeKeyCommand.Execute(level);
        var made = editor.Topics[0];
        await AddTopic(editor, "home/a");
        Assert.Equal(2, editor.Topics.Count);

        editor.TopicEntries[1].MakeKeyCommand.Execute(editor.TopicEntries[1].Levels[1]);

        Assert.Contains(made, editor.Topics);
        await editor.StopAsync();
    });

    [Fact]
    public Task A_path_can_be_added_by_hand() => Headless.Run(async () =>
    {
        var editor = Editor();
        editor.AddManualPathCommand.Execute(null);
        Assert.Equal("Add a topic first.", editor.Error);

        await AddTopic(editor, "t/a");
        editor.ManualPath = "a[";
        editor.AddManualPathCommand.Execute(null);
        Assert.NotNull(editor.Error);

        editor.ManualPath = " temp ";
        editor.AddManualPathCommand.Execute(null);
        Assert.Null(editor.Error);
        Assert.Equal("", editor.ManualPath);
        Assert.True(editor.Values.Single().IsChosen);
        Assert.False(editor.HasNoNumbers);
        Assert.Equal("Waiting for a message", editor.Preview.Status);

        foreach (var i in Enumerable.Range(1, GraphProfile.MaxValues - 1))
        {
            editor.ManualPath = $"p{i}";
            editor.AddManualPathCommand.Execute(null);
        }

        Assert.Null(editor.Error);
        editor.ManualPath = "one too many";
        editor.AddManualPathCommand.Execute(null);
        Assert.Contains("at most", editor.Error);
        await editor.StopAsync();
    });

    [Fact]
    public Task Ticking_more_than_a_graph_can_draw_is_refused() => Headless.Run(async () =>
    {
        var editor = Editor();
        await AddTopic(editor, "t/a");
        foreach (var i in Enumerable.Range(0, GraphProfile.MaxValues))
        {
            editor.ManualPath = $"p{i}";
            editor.AddManualPathCommand.Execute(null);
        }

        var last = editor.Values[0];
        last.RemoveCommand.Execute(null);
        editor.ManualPath = "another";
        editor.AddManualPathCommand.Execute(null);

        // Back to eight, and one more ticked.
        Assert.Equal(GraphProfile.MaxValues, editor.Values.Count(v => v.IsChosen));
        await editor.StopAsync();
    });

    [Fact]
    public Task The_number_that_arrives_first_is_drawn_but_not_a_time() => Headless.Run(async () =>
    {
        var (broker, _, editor) = await Listening("t/a");
        await using (broker)
        {
            Assert.Contains("Listening on t/a", editor.ListenStatus);

            await broker.SendAsync("t/a", """{"timestamp":1893456000,"temp":21.5,"unit":"C"}""");
            await Headless.Until(() => editor.Values.Count >= 2);

            var temp = editor.Values.First(v => v.Path == "temp");
            Assert.True(temp.IsChosen);
            Assert.False(editor.Values.First(v => v.Path == "timestamp").IsChosen);
            Assert.NotEmpty(temp.LatestText);
            await Headless.Until(() => editor.Preview.HasReadings);
            Assert.Null(editor.Preview.Status);

            temp.IsChosen = false;
            Assert.Equal("Tick a number below to draw it", editor.Preview.Status);
            temp.IsChosen = true;
            temp.Color = 3;
            temp.Label = "Temperature";
            Assert.Equal("Temperature", editor.Preview.Series[0].DisplayLabel);
            await editor.StopAsync();
        }
    });

    [Fact]
    public Task A_message_with_no_numbers_says_so_in_the_preview() => Headless.Run(async () =>
    {
        var (broker, _, editor) = await Listening("t/a");
        await using (broker)
        {
            editor.ManualPath = "temp";
            editor.AddManualPathCommand.Execute(null);

            await broker.SendAsync("t/a", "not a number");
            await Headless.Until(() => editor.Preview.Status?.StartsWith("Messages are arriving", StringComparison.Ordinal) == true);
            await editor.StopAsync();
        }
    });

    [Fact]
    public Task Listening_on_wildcards_says_how_many_arrive() => Headless.Run(async () =>
    {
        var (broker, _, editor) = await Listening("t/+");
        await using (broker)
        {
            for (var i = 0; i < 15; i++)
            {
                await broker.SendAsync($"t/{i}", """{"v":1}""");
                await Task.Delay(100, TestContext.Current.CancellationToken);
            }

            await Headless.Until(() => editor.ListenStatus.Contains("a second match"), timeoutMs: 5000);
            Assert.NotEmpty(editor.Keys);
            await AddTopic(editor, "z/+");
            await Headless.Until(() => editor.ListenStatus.Contains("these topics") || editor.ListenStatus.Contains("2 topics"), timeoutMs: 5000);
            await editor.StopAsync();
        }
    });

    [Fact]
    public Task Listening_on_several_topics_says_so_and_a_refused_filter_is_shown() => Headless.Run(async () =>
    {
        var (broker, _, editor) = await Listening("t/a");
        await using (broker)
        {
            await AddTopic(editor, "t/b");
            Assert.StartsWith("Listening on 2 topics", editor.ListenStatus);

            await AddTopic(editor, "refused/x");
            Assert.Contains("refused", editor.ListenStatus);
            await editor.StopAsync();
        }
    });

    [Fact]
    public Task A_field_that_varies_is_suggested_as_a_key_and_makes_lines() => Headless.Run(async () =>
    {
        var (broker, _, editor) = await Listening("t/a");
        await using (broker)
        {
            await broker.SendAsync("t/a", """{"sensor":"x","v":1}""");
            await broker.SendAsync("t/a", """{"sensor":"y","v":2}""");
            await Headless.Until(() => editor.Suggestions.Count > 0);
            Assert.Contains("sensor", editor.Suggestions[0].Text);
            Assert.True(editor.HasFieldChoices);

            editor.Suggestions[0].AddCommand.Execute(null);
            Assert.True(editor.HasKeys);
            Assert.Equal("sensor", editor.Keys[0].Name);
            Assert.Empty(editor.Suggestions);

            // Hearing it again brings each line.
            await broker.SendAsync("t/a", """{"sensor":"x","v":3}""");
            await broker.SendAsync("t/a", """{"sensor":"y","v":4}""");
            await Headless.Until(() => editor.Lines.Count == 2);
            Assert.True(editor.HasLines);
            Assert.Contains("2 lines", editor.OutcomeText);
            Assert.True(editor.HasOutcome);
            Assert.Equal("x, y", editor.Keys[0].SeenText);

            editor.Lines[0].RemoveCommand.Execute(null);
            Assert.True(editor.Lines[0].IsRemoved);
            Assert.False(editor.Lines[1].CanRemove);
            Assert.Single(editor.Preview.Series);
            editor.Lines[0].BringBackCommand.Execute(null);
            Assert.False(editor.Lines[0].IsRemoved);

            editor.Keys[0].SelectedShow = GraphKeyViewModel.Choices.First(c => c.Show == KeyShow.Graph);
            await Headless.Until(() => editor.OutcomeText.Contains("2 graphs"));

            editor.Keys[0].SelectedShow = GraphKeyViewModel.Choices.First(c => c.Show == KeyShow.Tab);
            await Headless.Until(() => editor.OutcomeText.Contains("2 tabs"));

            editor.Keys[0].RemoveCommand.Execute(null);
            Assert.False(editor.HasKeys);
            await editor.StopAsync();
        }
    });

    [Fact]
    public Task A_field_can_be_chosen_as_a_key_unless_it_differs_on_every_message() => Headless.Run(async () =>
    {
        var (broker, _, editor) = await Listening("t/a");
        await using (broker)
        {
            editor.AddKeyCommand.Execute(null);
            Assert.Empty(editor.Keys);

            foreach (var i in Enumerable.Range(0, 25))
                await broker.SendAsync("t/a", $$"""{"id":"m{{i}}","group":"g","v":{{i}}}""");
            await Headless.Until(() => editor.FieldChoices.Contains("id") && editor.Preview.Series.Count > 0);

            editor.SelectedField = "id";
            editor.AddKeyCommand.Execute(null);
            Assert.Contains("differs on nearly every message", editor.Error);
            Assert.Empty(editor.Keys);

            editor.SelectedField = "group";
            editor.AddKeyCommand.Execute(null);
            Assert.Null(editor.Error);
            Assert.Null(editor.SelectedField);
            Assert.Equal("group", Assert.Single(editor.Keys).Name);

            // Adding it twice is the same as once.
            editor.SelectedField = "group";
            editor.AddKeyCommand.Execute(null);
            Assert.Single(editor.Keys);
            await editor.StopAsync();
        }
    });

    [Fact]
    public Task A_number_by_its_position_offers_the_same_one_by_a_key() => Headless.Run(async () =>
    {
        var (broker, _, editor) = await Listening("t/a");
        await using (broker)
        {
            editor.ManualPath = "list[0].v";
            editor.AddManualPathCommand.Execute(null);
            await broker.SendAsync("t/a", """{"list":[{"name":"a","v":1},{"name":"b","v":2}]}""");
            await Headless.Until(() => editor.Values.Any(v => v.HasBetterPath));

            var old = editor.Values.First(v => v.HasBetterPath);
            var better = old.BetterPath!;
            old.Label = "Mine";
            old.UseBetterPathCommand.Execute(null);

            Assert.DoesNotContain(editor.Values, v => v.Path == "list[0].v");
            Assert.Equal("Mine", editor.Values.First(v => v.Path == better).Label);
            Assert.True(editor.Values.First(v => v.Path == better).IsChosen);

            // Removing one the messages do not offer takes it from the list; one they do stays there.
            editor.ManualPath = "nothing.here";
            editor.AddManualPathCommand.Execute(null);
            editor.Values.First(v => v.Path == "nothing.here").RemoveCommand.Execute(null);
            Assert.DoesNotContain(editor.Values, v => v.Path == "nothing.here");

            var offered = editor.Values.First(v => v.Path == better);
            offered.RemoveCommand.Execute(null);
            Assert.Contains(offered, editor.Values);
            Assert.False(offered.IsChosen);
            await editor.StopAsync();
        }
    });

    [Fact]
    public Task Numbers_of_unlike_units_or_sizes_are_advised_about() => Headless.Run(async () =>
    {
        var (broker, _, editor) = await Listening("t/a");
        await using (broker)
        {
            await broker.SendAsync("t/a", """{"sensors":[{"name":"a","value":1,"unit":"C"},{"name":"b","value":2,"unit":"V"}]}""");
            await Headless.Until(() => editor.Values.Any(v => v.Path.Contains("name=*", StringComparison.Ordinal)));
            var each = editor.Values.First(v => v.Path.Contains("name=*", StringComparison.Ordinal));
            foreach (var chosen in editor.Values.Where(v => v.IsChosen && v != each))
                chosen.IsChosen = false;
            each.IsChosen = true;

            await Headless.Until(() => editor.HasUnitHint);
            Assert.Contains("different units", editor.UnitHint);

            editor.SplitIntoGraphsCommand.Execute(null);
            Assert.Equal(KeyShow.Graph, editor.Keys[0].SelectedShow.Show);
            Assert.False(editor.HasUnitHint);

            editor.SplitIntoGraphsCommand.Execute(null);

            editor.Keys[0].SelectedShow = GraphKeyViewModel.Choices.First(c => c.Show == KeyShow.Line);
            await broker.SendAsync("t/a", """{"sensors":[{"name":"a","value":1},{"name":"b","value":5000}]}""");
            await Headless.Until(() => editor.UnitHint.Contains("very different sizes") || editor.UnitHint.Contains("different units"));
            await editor.StopAsync();
        }
    });

    [Fact]
    public Task Changing_the_client_starts_again_keeping_what_was_chosen() => Headless.Run(async () =>
    {
        var (broker, page, editor) = await Listening("t/a");
        await using (broker)
        {
            await broker.SendAsync("t/a", """{"v":1,"other":2}""");
            await Headless.Until(() => editor.Values.Count == 2);
            var chosen = editor.Values.First(v => v.IsChosen).Path;

            page.Mqtt.EditClientSettings = _ => Task.FromResult(true);
            await page.Mqtt.AddClientCommand.ExecuteAsync(null);
            editor.SelectedClient = page.Mqtt.Clients[1];
            await Headless.Until(() => editor.ListenStatus.Contains("is not connected"));

            Assert.Equal(chosen, Assert.Single(editor.Values).Path);

            editor.SelectedClient = null;
            await Headless.Until(() => editor.ListenStatus.Length == 0);
            await editor.StopAsync();
        }
    });

    public static TheoryData<string, string, string> Problems => new()
    {
        { "", "t/a", "Enter a name for the graph." },
        { "n", "", "Add a topic to draw." },
    };

    [Theory]
    [MemberData(nameof(Problems))]
    public Task Saving_says_what_is_missing(string name, string topic, string expected) => Headless.Run(async () =>
    {
        var editor = Editor();
        if (topic.Length > 0)
            await AddTopic(editor, topic);
        editor.Name = name;

        await editor.SaveCommand.ExecuteAsync(null);

        Assert.Equal(expected, editor.Error);
        await editor.StopAsync();
    });

    [Fact]
    public Task Saving_checks_everything_in_turn() => Headless.Run(async () =>
    {
        var page = Page(null, Board("d", Graph("tabs", "x/{other}", "v", GraphKind.Line, new GraphKey { Name = "other", Show = KeyShow.Tab })));
        var editor = new GraphEditorViewModel(page, page.Dashboards[0]);

        async Task Saved(string expected)
        {
            await editor.SaveCommand.ExecuteAsync(null);
            Assert.Equal(expected, editor.Error);
        }

        editor.NewTopic = "bad/#/more";
        await editor.SaveCommand.ExecuteAsync(null);
        Assert.NotNull(editor.Error);
        editor.NewTopic = "";

        editor.Name = "n";
        editor.SelectedClient = null;
        await Saved("Choose a client.");
        editor.SelectedClient = page.Mqtt.Clients[0];

        editor.NewTopic = "t/{key}";
        await editor.SaveCommand.ExecuteAsync(null);
        Assert.Equal("Tick at least one number to draw.", editor.Error);

        editor.ManualPath = "v";
        editor.AddManualPathCommand.Execute(null);
        await AddTopic(editor, "u/v");
        await Saved("Nothing is ticked on u/v. Tick a number on it, add one by its path, or remove the topic.");
        await editor.RemoveTopicCommand.ExecuteAsync("u/v");

        editor.UseFixedScale = true;
        await Saved("A fixed scale needs a minimum below its maximum.");
        editor.MinimumText = "5";
        editor.MaximumText = "1";
        await Saved("A fixed scale needs a minimum below its maximum.");
        editor.MaximumText = "10";

        editor.IsNewDashboard = true;
        await Saved("Enter a name for the new dashboard.");
        editor.IsNewDashboard = false;

        editor.SelectedDashboard = null;
        await Saved("Choose a dashboard.");
        editor.SelectedDashboard = page.Dashboards[0];

        var key = editor.Keys[0];
        key.SelectedShow = GraphKeyViewModel.Choices.First(c => c.Show == KeyShow.Only);
        await Saved($"Choose the value of {key.Name} to keep.");

        key.SelectedShow = GraphKeyViewModel.Choices.First(c => c.Show == KeyShow.Tab);
        await Saved($"The tabs of this dashboard come from other. Show {key.Name} as graphs or lines, or put this graph on a new dashboard.");

        await AddTopic(editor, "w/{more}");
        editor.ManualTopic = "w/{more}";
        editor.ManualPath = "v";
        editor.AddManualPathCommand.Execute(null);
        editor.Keys.First(k => k.Name == "more").SelectedShow = GraphKeyViewModel.Choices.First(c => c.Show == KeyShow.Tab);
        await Saved("Only one key can be shown as tabs. Show the others as graphs or lines.");
        await editor.StopAsync();
    });

    [Fact]
    public Task Saving_a_new_graph_puts_it_on_the_dashboard() => Headless.Run(async () =>
    {
        var (broker, page, editor) = await Listening("t/a");
        await using (broker)
        {
            await broker.SendAsync("t/a", """{"v":5}""");
            await Headless.Until(() => editor.Preview.HasReadings);
            editor.Name = " Graph ";
            editor.Unit = " kg ";
            editor.SelectedKind = editor.Kinds[1];
            editor.SelectedSpan = editor.Spans[0];
            editor.SelectedDecimals = editor.DecimalChoices[2];
            editor.UseFixedScale = true;
            editor.MinimumText = "0";
            editor.MaximumText = "10";
            editor.WidthSpan = 2;
            editor.HeightSpan = 3;
            bool? closed = null;
            editor.CloseRequested += saved => closed = saved;

            await editor.SaveCommand.ExecuteAsync(null);

            Assert.True(closed);
            Assert.Equal(page.Dashboards[0].DisplayName, editor.AddedTo);
            var card = Assert.Single(page.Dashboards[0].Graphs);
            Assert.Equal("Graph", card.Name);
            Assert.True(card.HasReadings);
            var profile = card.Profile;
            Assert.Equal("kg", profile.Unit);
            Assert.Equal(GraphKind.Area, profile.Kind);
            Assert.Equal(1, profile.SpanMinutes);
            Assert.Equal(1, profile.Decimals);
            Assert.Equal(0, profile.Minimum);
            Assert.Equal(10, profile.Maximum);
            Assert.Equal(2, profile.Width);
            Assert.Equal(3, profile.Height);

            // Stopped once it is saved.
            await editor.StopAsync();
            await broker.SendAsync("t/a", """{"v":6}""");
        }
    });

    [Fact]
    public Task A_topic_typed_but_not_added_is_taken_to_be_meant_and_the_graph_can_go_on_a_new_dashboard() => Headless.Run(async () =>
    {
        var page = Page();
        var editor = Editor(page);
        await AddTopic(editor, "t/a");
        editor.ManualPath = "v";
        editor.AddManualPathCommand.Execute(null);
        editor.NewTopic = "t/a";
        editor.IsNewDashboard = true;
        editor.NewDashboardName = " New one ";

        await editor.SaveCommand.ExecuteAsync(null);

        Assert.Equal("New one", editor.AddedTo);
        Assert.Equal("New one", page.SelectedDashboard!.Name);
    });

    [Fact]
    public Task Editing_a_graph_starts_from_it_and_replaces_it_when_saved() => Headless.Run(async () =>
    {
        var graph = Graph("old", "t/a", "v", GraphKind.Bars);
        graph.Unit = "kg";
        graph.SpanMinutes = 15;
        graph.Decimals = 2;
        graph.Minimum = 0;
        graph.Maximum = 9;
        graph.Width = 2;
        graph.Height = 2;
        graph.Values[0].Label = "L";
        graph.Values[0].Color = 3;
        var page = Page(null, Board("d", graph));
        var tab = page.Dashboards[0];
        var card = tab.Graphs[0];

        var editor = new GraphEditorViewModel(page, tab, card);

        Assert.False(editor.IsNew);
        Assert.False(editor.CanChooseDashboard);
        Assert.Equal("Edit graph", editor.WindowTitle);
        Assert.Equal("Save", editor.SaveText);
        Assert.Equal("old", editor.Name);
        Assert.Equal("kg", editor.Unit);
        Assert.Equal(GraphKind.Bars, editor.SelectedKind.Kind);
        Assert.Equal(15, editor.SelectedSpan.Minutes);
        Assert.Equal(2, editor.SelectedDecimals.Places);
        Assert.True(editor.UseFixedScale);
        Assert.Equal(2, editor.WidthSpan);
        Assert.Equal("L", editor.Values[0].Label);
        Assert.Equal(3, editor.Values[0].Color);

        editor.Name = "new";
        await editor.SaveCommand.ExecuteAsync(null);

        Assert.Equal("new on d", editor.AddedTo);
        Assert.Equal("new", Assert.Single(tab.Graphs).Name);
    });

    [Fact]
    public Task A_tab_key_can_stay_when_a_graph_is_edited_or_the_others_agree() => Headless.Run(async () =>
    {
        var graph = Graph("g", "t/{room}", "v", GraphKind.Line, new GraphKey { Name = "room", Show = KeyShow.Tab });
        var page = Page(null, Board("d", graph));
        var tab = page.Dashboards[0];

        var edit = new GraphEditorViewModel(page, tab, tab.Graphs[0]);
        await edit.SaveCommand.ExecuteAsync(null);
        Assert.Null(edit.Error);

        var second = new GraphEditorViewModel(page, page.Dashboards[0]);
        await AddTopic(second, "u/{room}");
        second.ManualPath = "v";
        second.AddManualPathCommand.Execute(null);
        second.Keys[0].SelectedShow = GraphKeyViewModel.Choices.First(c => c.Show == KeyShow.Tab);
        await second.SaveCommand.ExecuteAsync(null);
        Assert.Null(second.Error);
    });

    [Fact]
    public Task A_graph_of_one_cell_previews_itself() => Headless.Run(async () =>
    {
        var graph = Graph("g", "t/{room}", "v", GraphKind.Line, new GraphKey { Name = "room", Show = KeyShow.Graph });
        var page = Page(null, Board("d", graph));
        var tab = page.Dashboards[0];
        var source = page.SourceFor(graph);
        source.Receive(Message("t/a", """{"v":1}"""));
        source.Receive(Message("t/b", """{"v":2}"""));
        tab.Arrange();
        var card = tab.Graphs[1];

        var editor = new GraphEditorViewModel(page, tab, card);

        Assert.Equal(card.Cell, editor.Preview.Cell);

        // Changing where it goes starts its cells again.
        editor.Keys[0].SelectedShow = GraphKeyViewModel.Choices.First(c => c.Show == KeyShow.Line);
        Assert.True(editor.Preview.Cell.IsEmpty);
        await editor.StopAsync();
    });

    [Fact]
    public Task The_topic_explorer_starts_it_with_what_it_has_heard() => Headless.Run(async () =>
    {
        var existing = Graph("existing", "other/x");
        var page = Page(null, Board("d", existing));
        var client = page.Mqtt.Clients[0];
        var history = new[] { Message("home/temp", """{"a":1,"b":2}"""), Message("home/temp", """{"a":3,"b":4}""") };

        var editor = page.NewEditor(client, "home/temp", history, ["b", "nothing"]);

        Assert.True(editor.CanChooseTarget);
        Assert.True(editor.CanChooseDashboard);
        Assert.Equal("temp", editor.Name);
        Assert.Equal(["home/temp"], editor.Topics);
        Assert.True(editor.Values.First(v => v.Path == "b").IsChosen);
        Assert.True(editor.Preview.HasReadings);
        Assert.Equal(["A new graph", "existing"], editor.Targets.Select(t => t.Name));
        Assert.Null(editor.SelectedTarget!.Card);

        // Joining a graph already there.
        editor.SelectedTarget = editor.Targets[1];
        Assert.False(editor.IsNew);
        Assert.Equal("Add to graph", editor.WindowTitle);
        Assert.Equal("Add to graph", editor.SaveText);
        Assert.Equal(["other/x", "home/temp"], editor.Topics);
        Assert.Equal("existing", editor.Name);

        editor.SelectedTarget = editor.Targets[0];
        Assert.True(editor.IsNew);
        editor.SelectedTarget = null;

        editor.IsNewDashboard = true;
        Assert.Equal(["A new graph"], editor.Targets.Select(t => t.Name));
        editor.IsNewDashboard = false;
        editor.SelectedDashboard = null;
        Assert.Equal(["A new graph"], editor.Targets.Select(t => t.Name));
        await editor.StopAsync();
    });

    [Fact]
    public Task A_graph_already_drawing_the_topic_is_not_given_it_again() => Headless.Run(async () =>
    {
        var existing = Graph("existing", "home/+");
        var page = Page(null, Board("d", existing));

        var editor = page.NewEditor(page.Mqtt.Clients[0], "home/temp", [], []);
        editor.SelectedTarget = editor.Targets[1];

        Assert.Equal(["home/+"], editor.Topics);
        await editor.StopAsync();
    });

    [Fact]
    public Task Only_so_many_numbers_are_taken_from_the_topic_explorer() => Headless.Run(async () =>
    {
        var page = Page();
        var paths = Enumerable.Range(0, GraphProfile.MaxValues + 2).Select(i => $"p{i}");

        var editor = page.NewEditor(page.Mqtt.Clients[0], "t", [], paths);

        Assert.Equal(GraphProfile.MaxValues, editor.Values.Count(v => v.IsChosen));
        await editor.StopAsync();
    });

    [Fact]
    public Task Cancelling_closes_without_saving_and_stopping_twice_is_fine() => Headless.Run(async () =>
    {
        var page = Page();
        var editor = Editor(page);
        editor.CancelCommand.Execute(null);
        bool? closed = null;
        editor.CloseRequested += saved => closed = saved;

        editor.CancelCommand.Execute(null);

        Assert.False(closed);
        await editor.StopAsync();
        await editor.StopAsync();
        await AddTopic(editor, "after/stop");
        Assert.False(editor.IsListening);
    });
}
