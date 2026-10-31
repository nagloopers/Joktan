using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Joktan.Core.Controls;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Core.Tests.Controls;

public class TextHighlightTests
{
    private static string[] Parts(TextBlock text) => text.Inlines!.OfType<Run>().Select(r => (r.Classes.Contains(TextHighlight.MatchClass) ? "*" : "") + r.Text).ToArray();

    [Fact]
    public Task The_source_is_shown_with_its_words_marked_and_follows_changes() => Headless.Run(() =>
    {
        var text = new TextBlock();

        TextHighlight.SetSource(text, "The door code");
        Assert.Equal(["The door code"], Parts(text));

        TextHighlight.SetWords(text, ["DOOR"]);
        Assert.Equal(["The ", "*door", " code"], Parts(text));

        TextHighlight.SetSource(text, "door");
        Assert.Equal(["*door"], Parts(text));

        TextHighlight.SetWords(text, null);
        Assert.Equal(["door"], Parts(text));
    });

    [Fact]
    public Task Marking_plain_text_turns_it_into_runs_only_when_a_word_is_there() => Headless.Run(() =>
    {
        var text = new TextBlock { Text = "the door" };

        Assert.False(TextHighlight.Mark(text, ["window"]));
        Assert.Equal("the door", text.Text);

        Assert.True(TextHighlight.Mark(text, ["door"]));
        Assert.Equal(["the ", "*door"], Parts(text));
    });

    [Fact]
    public Task A_split_run_keeps_its_own_classes() => Headless.Run(() =>
    {
        var run = new Run("a door");
        run.Classes.Add("md-code");
        var text = new TextBlock { Inlines = [run] };

        TextHighlight.Mark(text, ["door"]);

        Assert.All(text.Inlines!, inline => Assert.Contains("md-code", inline.Classes));
    });
}
