using Xunit;

namespace Joktan.Core.Tests;

public class TextSearchTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \t ")]
    public void Nothing_typed_is_no_words(string? typed) => Assert.Empty(TextSearch.Words(typed));

    [Fact]
    public void Words_are_split_on_any_space() => Assert.Equal(["door", "code"], TextSearch.Words(" door\t code "));

    [Fact]
    public void Every_word_has_to_appear_in_one_of_the_texts_ignoring_case()
    {
        Assert.True(TextSearch.HasAll(["door", "CODE"], "The Door", "the code"));
        Assert.False(TextSearch.HasAll(["door", "lunch"], "The Door", "the code"));
    }

    [Fact]
    public void Lines_are_tidied_and_empty_ones_left_out()
        => Assert.Equal(["a b", "c d"], TextSearch.Lines("  a   b\r\n\n| c | d |\n  \n"));

    [Fact]
    public void The_snippet_is_the_line_with_the_most_words()
        => Assert.Equal("door code", TextSearch.Snippet(["door", "other", "door code"], ["door", "code"]));

    [Fact]
    public void A_long_line_is_cut_around_the_first_word_found_at_a_space()
    {
        var line = new string('a', 30) + " " + new string('b', 30) + " needle " + new string('c', 200);

        var snippet = TextSearch.Snippet([line], ["needle"]);

        Assert.StartsWith("…needle", snippet);
        Assert.EndsWith("…", snippet);
    }

    [Fact]
    public void No_lines_give_an_empty_snippet() => Assert.Equal("", TextSearch.Snippet([], ["word"]));

    [Fact]
    public void Ranges_find_every_place_ignoring_case_and_join_overlaps()
    {
        Assert.Equal([(0, 3), (8, 3)], TextSearch.Ranges("Cat and cat", ["cat"]));
        Assert.Equal([(0, 5)], TextSearch.Ranges("abcde", ["abc", "cde"]));
        Assert.Equal([(0, 3)], TextSearch.Ranges("abc", ["abc", "b"]));
        Assert.Equal([(0, 1), (4, 1)], TextSearch.Ranges("b   a", ["a", "b"]));
        Assert.Empty(TextSearch.Ranges("abc", ["x"]));
        Assert.Empty(TextSearch.Ranges("abc", [""]));
    }
}
