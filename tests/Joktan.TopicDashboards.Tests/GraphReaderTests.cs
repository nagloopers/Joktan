using System;
using System.Linq;
using System.Text;
using Joktan.Mqtt;
using Xunit;

namespace Joktan.TopicDashboards.Tests;

public class GraphReaderTests
{
    private static readonly DateTime Time = new(2026, 11, 2, 8, 0, 0);

    private const string Climate = """
        { "zone": "zone1", "readings": [
            { "sensor": "temperature", "value": 20.8, "unit": "C" },
            { "sensor": "humidity", "value": 40, "unit": "%" } ] }
        """;

    private static ReceivedMessage Message(string topic, string payload, bool gzip = false)
    {
        var bytes = Encoding.UTF8.GetBytes(payload);
        return new ReceivedMessage(topic, gzip ? Gzip.Compress(bytes) : bytes, 0, false, Time);
    }

    private static string Vibration(string axis, string rms) => $$"""{ "ts": 1, "machine": "press-07", "axis": "{{axis}}", "rms": {{rms}} }""";

    private static GraphProfile Graph(string topic, string path, params GraphKey[] keys)
        => new() { Values = [new GraphValue { Topic = topic, Path = path }], Keys = keys.ToList() };

    private static GraphReading[] Read(GraphProfile profile, ReceivedMessage message)
        => new GraphReader(profile).Read(message).Readings.ToArray();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_plain_exact_graph_has_one_series_with_no_keys(bool gzip)
    {
        var reading = Assert.Single(Read(Graph("a/b", "x"), Message("a/b", """{ "x": 5 }""", gzip)));

        Assert.True(reading.Keys.IsEmpty);
        Assert.Equal([5.0], reading.Values);
    }

    [Fact]
    public void Other_topics_are_not_read()
        => Assert.Empty(Read(Graph("a/b", "x"), Message("a/c", """{ "x": 5 }""")));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_key_on_an_array_gives_a_series_for_each_sensor(bool gzip)
    {
        var readings = Read(Graph("climate", "readings[sensor=*].value"), Message("climate", Climate, gzip));

        Assert.Equal(["temperature", "humidity"], readings.Select(r => r.Keys["sensor"]));
        Assert.Equal(20.8, readings[0].Values[0]);
    }

    [Fact]
    public void A_field_splits_what_shares_a_topic()
    {
        var profile = Graph("vibration", "rms", new GraphKey { Name = "axis", Field = "axis" });
        var reader = new GraphReader(profile);

        var x = Assert.Single(reader.Read(Message("vibration", Vibration("x", "1.5"))).Readings);
        var y = Assert.Single(reader.Read(Message("vibration", Vibration("y", "0.5"))).Readings);

        Assert.Equal("x", x.Keys["axis"]);
        Assert.Equal("y", y.Keys["axis"]);
        Assert.NotEqual(x.Keys, y.Keys);
    }

    [Fact]
    public void A_message_without_the_field_is_skipped_and_says_why()
    {
        var profile = Graph("vibration", "rms", new GraphKey { Name = "axis", Field = "axis" });

        var read = new GraphReader(profile).Read(Message("vibration", """{ "rms": 1 }"""));

        Assert.Empty(read.Readings);
        Assert.Equal("no value of axis", read.Problem);
    }

    [Fact]
    public void A_wildcard_level_is_a_key()
    {
        var reading = Assert.Single(Read(Graph("plant/{line}/rms", ""), Message("plant/line1/rms", "5")));

        Assert.Equal("line1", reading.Keys["line"]);
        Assert.Equal("plant/{line}/rms", reading.Topic);
    }

    [Fact]
    public void An_unnamed_wildcard_is_a_key_as_well()
    {
        var reading = Assert.Single(Read(Graph("devices/+/heartbeat", "battery"), Message("devices/d1/heartbeat", """{ "battery": 80 }""")));

        Assert.Equal("d1", reading.Keys["level1"]);
    }

    [Theory]
    [InlineData("building/hvac/+/climate", "readings[sensor={2}].value")]
    [InlineData("building/hvac/{sensor}/climate", "readings[sensor={sensor}].value")]
    public void A_wildcard_can_choose_the_reading(string topic, string path)
    {
        var profile = Graph(topic, path);

        Assert.Equal(40, Read(profile, Message("building/hvac/humidity/climate", Climate))[0].Values[0]);
        Assert.Equal(20.8, Read(profile, Message("building/hvac/temperature/climate", Climate))[0].Values[0]);
    }

    [Fact]
    public void A_wildcard_with_nothing_to_choose_reads_nothing_and_says_why()
    {
        var read = new GraphReader(Graph("h/{sensor}", "readings[sensor={sensor}].value")).Read(Message("h/pressure", Climate));

        Assert.Empty(read.Readings);
        Assert.Contains("nothing at", read.Problem);
    }

    [Fact]
    public void Only_keeps_one_value_and_is_not_part_of_what_tells_series_apart()
    {
        var profile = Graph("vibration", "rms", new GraphKey { Name = "axis", Field = "axis", Show = KeyShow.Only, Only = "y" });
        var reader = new GraphReader(profile);

        var kept = reader.Read(Message("vibration", Vibration("y", "1")));
        var other = reader.Read(Message("vibration", Vibration("x", "1")));

        Assert.True(Assert.Single(kept.Readings).Keys.IsEmpty);
        Assert.Empty(other.Readings);
        Assert.Null(other.Problem);
        Assert.Equal("x", Assert.Single(other.Keys)["axis"]);
    }

    [Theory]
    [InlineData("not json", "it is not a number or JSON")]
    public void Says_why_a_payload_gives_no_numbers(string payload, string expected)
        => Assert.Equal(expected, new GraphReader(Graph("a", "x")).Read(Message("a", payload)).Problem);

    [Fact]
    public void Says_when_gzip_cannot_be_unzipped()
    {
        var zipped = Gzip.Compress(Encoding.UTF8.GetBytes(new string('x', 2000)));
        var message = new ReceivedMessage("a", zipped[..(zipped.Length / 2)], 0, false, Time);

        Assert.Contains("could not be unzipped", new GraphReader(Graph("a", "x")).Read(message).Problem);
    }

    [Fact]
    public void Says_when_gzip_unzips_past_the_limit()
    {
        var big = $$"""{ "x": 1, "pad": "{{new string('x', JsonNumbers.MaxPayloadBytes + 1)}}" }""";

        var problem = new GraphReader(Graph("a", "x")).Read(Message("a", big, gzip: true)).Problem;

        Assert.Contains("unzipped to", problem);
        Assert.Contains("limit", problem);
    }

    [Fact]
    public void Says_which_filters_to_subscribe_to()
    {
        var profile = Graph("plant/{line}/rms", "");
        profile.Values.Add(new GraphValue { Topic = "plant/{line}/rms", Path = "x" });
        profile.Values.Add(new GraphValue { Topic = "a/b", Path = "x" });

        Assert.Equal(["plant/+/rms", "a/b"], new GraphReader(profile).Filters);
    }

    [Theory]
    [InlineData("a/b", "x", false)]
    [InlineData("a/+", "x", true)]
    [InlineData("a/b", "r[s=*].v", true)]
    [InlineData("a/b", "r[s=t].v", false)]
    public void Knows_which_numbers_can_be_several_lines(string topic, string path, bool expected)
    {
        var profile = Graph(topic, path);

        Assert.Equal(expected, GraphReader.IsSeveral(profile.Values[0], profile));
    }
}

public class GraphSourceTests
{
    private static readonly DateTime Time = new(2026, 11, 2, 8, 0, 0);

    private static ReceivedMessage Message(string topic, string payload)
        => new(topic, Encoding.UTF8.GetBytes(payload), 0, false, Time);

    private static string Vibration(string machine, string axis) => $$"""{ "machine": "{{machine}}", "axis": "{{axis}}", "rms": 1 }""";

    private static GraphSource Source(Func<bool>? canAdd = null) => new(
        new GraphProfile
        {
            Values = [new GraphValue { Topic = "plant/{line}/vibration", Path = "rms" }],
            Keys =
            [
                new GraphKey { Name = "line", Show = KeyShow.Tab },
                new GraphKey { Name = "machine", Field = "machine", Show = KeyShow.Graph },
                new GraphKey { Name = "axis", Field = "axis", Show = KeyShow.Line },
            ],
        },
        new GraphData(),
        canAdd);

    [Fact]
    public void Tabs_graphs_and_lines_come_from_their_keys()
    {
        var source = Source();

        Assert.True(source.Receive(Message("plant/l1/vibration", Vibration("p1", "x"))).NewCell);
        Assert.False(source.Receive(Message("plant/l1/vibration", Vibration("p1", "y"))).NewCell);
        Assert.True(source.Receive(Message("plant/l1/vibration", Vibration("p2", "x"))).NewCell);
        Assert.True(source.Receive(Message("plant/l2/vibration", Vibration("p3", "x"))).NewCell);

        Assert.Equal(4, source.Data.Count);
        Assert.Equal(["p1", "p2"], source.CellsFor("line", "l1").Select(c => c["machine"]));
        Assert.Equal(["p3"], source.CellsFor("line", "l2").Select(c => c["machine"]));
        Assert.Empty(source.CellsFor("line", "l3"));
    }

    [Fact]
    public void With_nothing_seen_there_is_one_empty_graph()
        => Assert.Equal(SeriesKeys.None, Assert.Single(Source().CellsFor(null, null)));

    [Fact]
    public void Seen_values_are_kept_on_the_profile_for_next_time()
    {
        var source = Source();
        source.Receive(Message("plant/l1/vibration", Vibration("p1", "x")));

        var next = new GraphSource(source.Profile, new GraphData());

        Assert.Equal("p1", Assert.Single(next.CellsFor("line", "l1"))["machine"]);
    }

    [Fact]
    public void Tabs_stop_at_their_limit_and_say_why()
    {
        var source = Source();
        for (var i = 0; i < GraphProfile.MaxTabs + 2; i++)
            source.Receive(Message($"plant/l{i}/vibration", Vibration("p", "x")));

        Assert.Equal(GraphProfile.MaxTabs, source.Profile.Seen.Count);
        Assert.Contains("12 tabs", source.OverLimit);
    }

    [Fact]
    public void Graphs_stop_at_their_limit_and_say_why()
    {
        var source = Source();
        for (var i = 0; i < GraphProfile.MaxGraphs + 2; i++)
            source.Receive(Message("plant/l1/vibration", Vibration($"p{i}", "x")));

        Assert.Equal(GraphProfile.MaxGraphs, source.CellsFor("line", "l1").Count);
        Assert.Contains("24 graphs", source.OverLimit);
    }

    [Fact]
    public void A_new_series_waits_when_the_app_has_too_many()
    {
        var room = true;
        var source = Source(() => room);
        source.Receive(Message("plant/l1/vibration", Vibration("p1", "x")));
        room = false;

        source.Receive(Message("plant/l1/vibration", Vibration("p1", "y")));
        var known = source.Receive(Message("plant/l1/vibration", Vibration("p1", "x")));

        Assert.Equal(1, source.Data.Count);
        Assert.True(known.Added);
        Assert.Contains("too many series", source.OverLimit);
    }

    [Fact]
    public void Forgetting_a_tab_takes_its_graphs_and_readings()
    {
        var source = Source();
        source.Receive(Message("plant/l1/vibration", Vibration("p1", "x")));
        source.Receive(Message("plant/l2/vibration", Vibration("p1", "x")));

        Assert.True(source.Forget(new SeriesKeys([new("line", "l1")])));

        Assert.Empty(source.CellsFor("line", "l1"));
        Assert.Single(source.CellsFor("line", "l2"));
        Assert.Equal(1, source.Data.Count);
    }

    [Fact]
    public void Keeps_every_value_seen_even_those_left_out()
    {
        var source = new GraphSource(
            new GraphProfile
            {
                Values = [new GraphValue { Topic = "t", Path = "rms" }],
                Keys = [new GraphKey { Name = "axis", Field = "axis", Show = KeyShow.Only, Only = "x" }],
            },
            new GraphData());

        source.Receive(Message("t", Vibration("p", "x")));
        source.Receive(Message("t", Vibration("p", "y")));

        Assert.Equal(["x", "y"], source.KeyValues["axis"]);
    }

    [Fact]
    public void Says_how_it_has_been_doing()
    {
        var source = new GraphSource(
            new GraphProfile
            {
                Values = [new GraphValue { Topic = "t", Path = "rms" }],
                Keys = [new GraphKey { Name = "axis", Field = "axis" }],
            },
            new GraphData());

        Assert.Contains("No message", source.Health.Describe(Time));

        source.Receive(Message("t", Vibration("p", "x")));
        Assert.Null(source.Health.Problem);

        source.Receive(Message("t", """{ "rms": 1 }"""));
        var text = source.Health.Describe(Time.AddSeconds(4));

        Assert.Equal("no value of axis", source.Health.Problem);
        Assert.Contains("Last message 4s ago, on t", text);
        Assert.Contains("2 matched, 1 used, 1 skipped: no value of axis", text);
    }
}
