using System.Linq;
using System.Text.Json;
using Xunit;

namespace Joktan.Mqtt.Tests;

public class PayloadPathTests
{
    private const string Climate = """
        { "zone": "zone1", "readings": [
            { "ts": "2026-10-31T11:33:48Z", "sensor": "temperature", "value": 20.8, "unit": "C" },
            { "ts": "2026-10-31T11:33:48Z", "sensor": "humidity", "value": 40, "unit": "%" } ] }
        """;

    private const string Reversed = """
        { "readings": [ { "sensor": "humidity", "value": 40 }, { "sensor": "temperature", "value": 20.8 } ] }
        """;

    private static PathRow[] Read(string path, string json, string? line = null)
    {
        using var document = JsonDocument.Parse(json);
        return PayloadPath.Parse(path).Read(document.RootElement, name => name is "line" or "2" ? line : null).ToArray();
    }

    [Theory]
    [InlineData(Climate)]
    [InlineData(Reversed)]
    public void An_item_chosen_by_its_key_does_not_depend_on_the_order(string json)
    {
        var row = Assert.Single(Read("readings[sensor=temperature].value", json));

        Assert.Equal([20.8], row.Values);
        Assert.Empty(row.Keys);
    }

    [Fact]
    public void A_star_gives_one_row_for_each_value_of_the_key()
    {
        var rows = Read("readings[sensor=*].value", Climate);

        Assert.Equal(2, rows.Length);
        Assert.Equal("temperature", rows[0].Keys["sensor"]);
        Assert.Equal([20.8], rows[0].Values);
        Assert.Equal("humidity", rows[1].Keys["sensor"]);
        Assert.Equal([40.0], rows[1].Values);
    }

    [Fact]
    public void No_item_with_that_key_reads_nothing()
        => Assert.Empty(Read("readings[sensor=pressure].value", Climate));

    [Fact]
    public void Several_items_with_that_key_are_one_batch()
    {
        var row = Assert.Single(Read("r[s=a].v", """{ "r": [ { "s": "a", "v": 1 }, { "s": "b", "v": 2 }, { "s": "a", "v": 3 } ] }"""));

        Assert.Equal([1.0, 3.0], row.Values);
    }

    [Fact]
    public void Every_condition_must_match()
    {
        const string json = """{ "r": [ { "s": "t", "room": "a", "v": 1 }, { "s": "t", "room": "b", "v": 2 } ] }""";

        Assert.Equal([2.0], Assert.Single(Read("r[s=t,room=b].v", json)).Values);
    }

    [Fact]
    public void Arrays_can_be_nested()
    {
        const string json = """{ "groups": [ { "id": "a", "r": [ { "s": "t", "v": 1 } ] }, { "id": "b", "r": [ { "s": "t", "v": 2 } ] } ] }""";

        Assert.Equal([2.0], Assert.Single(Read("groups[id=b].r[s=t].v", json)).Values);

        var both = Read("groups[id=*].r[s=*].v", json);
        Assert.Equal("b", both[1].Keys["id"]);
        Assert.Equal("t", both[1].Keys["s"]);
    }

    [Theory]
    [InlineData("r[meta.id=5].v")]
    [InlineData("r[meta.id=\"5\"].v")]
    public void A_key_can_be_nested_and_matches_a_number_or_text(string path)
    {
        const string json = """{ "r": [ { "meta": { "id": 5 }, "v": 1 }, { "meta": { "id": "5" }, "v": 2 }, { "meta": { "id": 6 }, "v": 3 } ] }""";

        Assert.Equal([1.0, 2.0], Assert.Single(Read(path, json)).Values);
    }

    [Fact]
    public void Quotes_hold_spaces_and_punctuation()
    {
        const string json = """{ "r": [ { "name": "north, side]", "v": 1 }, { "name": "south", "v": 2 } ] }""";

        Assert.Equal([1.0], Assert.Single(Read("r[name=\"north, side]\"].v", json)).Values);
    }

    [Fact]
    public void Values_are_compared_with_their_case()
        => Assert.Empty(Read("readings[sensor=Temperature].value", Climate));

    [Fact]
    public void Plain_paths_are_recognised_as_plain()
    {
        Assert.True(PayloadPath.Parse("sensor.value").IsPlain);
        Assert.True(PayloadPath.Parse("readings[*].value").IsPlain);
        Assert.True(PayloadPath.Parse("readings[0].value").IsPlain);
        Assert.True(PayloadPath.Parse("").IsPlain);
        Assert.False(PayloadPath.Parse("readings[sensor=t].value").IsPlain);
        Assert.False(PayloadPath.Parse("{line}.value").IsPlain);
    }

    [Fact]
    public void A_link_chooses_the_item()
    {
        Assert.Equal([40.0], Assert.Single(Read("readings[sensor={2}].value", Climate, "humidity")).Values);
        Assert.Equal([20.8], Assert.Single(Read("readings[sensor={line}].value", Climate, "temperature")).Values);
    }

    [Fact]
    public void A_link_chooses_a_field()
    {
        const string json = """{ "ch1": { "value": 7 }, "ch2": { "value": 8 } }""";

        Assert.Equal([8.0], Assert.Single(Read("{line}.value", json, "ch2")).Values);
        Assert.Equal([7.0], Assert.Single(Read("{2}.value", json, "ch1")).Values);
    }

    [Fact]
    public void A_link_with_nothing_for_it_reads_nothing()
        => Assert.Empty(Read("readings[sensor={2}].value", Climate, "pressure"));

    [Fact]
    public void A_link_is_refused_as_an_array_position()
        => Assert.Contains("depends on the order", PayloadPath.Parse("readings[{2}].value").Error);

    [Theory]
    [InlineData("readings[sensor")]
    [InlineData("{line.value")]
    [InlineData("readings[oops].value")]
    public void Says_what_is_wrong_with_a_path(string path)
    {
        Assert.NotNull(PayloadPath.Parse(path).Error);
        Assert.Empty(PayloadPath.Parse(path).Read(JsonDocument.Parse("{}").RootElement, _ => null));
    }

    [Theory]
    [InlineData("building/hvac/+/climate", "r[s={2}].v", true)]
    [InlineData("building/hvac/{sensor}/climate", "r[s={sensor}].v", true)]
    [InlineData("building/hvac/{sensor}/climate", "r[s={2}].v", true)]
    [InlineData("building/hvac/+/climate", "r[s={1}].v", false)]
    [InlineData("building/hvac/+/climate", "r[s={9}].v", false)]
    [InlineData("building/hvac/{sensor}/climate", "r[s={zone}].v", false)]
    [InlineData("building/hvac/{sensor}/climate", "{sensor}.value", true)]
    public void A_link_must_point_at_a_wildcard(string topic, string path, bool ok)
    {
        var problem = PayloadPath.Parse(path).ProblemWith(TopicPattern.Parse(topic)!);

        Assert.Equal(ok, problem is null);
    }

    [Fact]
    public void Lists_the_keys_a_path_gives()
        => Assert.Equal(["id", "s"], PayloadPath.Parse("g[id=*].r[s=*,room=a].v").KeyNames);

    [Theory]
    [InlineData("temperature", "temperature")]
    [InlineData("north side", "\"north side\"")]
    [InlineData("a,b", "\"a,b\"")]
    public void Quotes_only_when_it_has_to(string value, string expected)
        => Assert.Equal(expected, PayloadPath.Quote(value));

    [Fact]
    public void Reads_a_field_of_any_simple_kind()
    {
        using var document = JsonDocument.Parse("""{ "a": { "b": "x" }, "n": 5, "t": true, "o": {} }""");

        Assert.Equal("x", PayloadPath.ScalarAt(document.RootElement, "a.b"));
        Assert.Equal("5", PayloadPath.ScalarAt(document.RootElement, "n"));
        Assert.Equal("true", PayloadPath.ScalarAt(document.RootElement, "t"));
        Assert.Null(PayloadPath.ScalarAt(document.RootElement, "o"));
        Assert.Null(PayloadPath.ScalarAt(document.RootElement, "missing"));
    }

    private static System.Collections.Generic.IReadOnlyList<double> Numbers(string path, string json)
    {
        var parsed = PayloadPath.Parse(path);
        using var document = JsonDocument.Parse(json);
        return parsed.Read(document.RootElement, _ => null).SelectMany(r => r.Values).ToList();
    }

    [Theory]
    [InlineData("list[*].v", """{"list":[{"v":1},{"v":2}]}""", new[] { 1.0, 2.0 })]
    [InlineData("list[1].v", """{"list":[{"v":1},{"v":2}]}""", new[] { 2.0 })]
    [InlineData("list[5].v", """{"list":[{"v":1}]}""", new double[0])]
    [InlineData("list[*].v", """{"list":{"v":1}}""", new double[0])]
    public void Steps_through_arrays_in_the_middle_of_a_path(string path, string json, double[] expected)
    {
        Assert.Equal(expected, Numbers(path, json));
    }
}
