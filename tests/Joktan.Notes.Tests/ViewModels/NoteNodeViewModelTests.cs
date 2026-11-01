using System.Linq;
using Joktan.Notes.ViewModels;
using Xunit;

namespace Joktan.Notes.Tests.ViewModels;

public class NoteNodeViewModelTests
{
    [Theory]
    [InlineData("Page", "Page")]
    [InlineData("", "Untitled")]
    [InlineData("   ", "Untitled")]
    public void The_title_in_the_tree_is_never_blank(string title, string expected)
    {
        Assert.Equal(expected, new NoteNodeViewModel("id", title, null).DisplayTitle);
    }

    [Fact]
    public void A_page_round_trips_with_the_pages_below_it()
    {
        var page = new NotePage
        {
            Id = "a",
            Title = "A",
            IsExpanded = false,
            Children = [new NotePage { Id = "b", Title = "B", Children = [new NotePage { Id = "c", Title = "C" }] }],
        };

        var node = NoteNodeViewModel.FromPage(page, null);

        Assert.False(node.IsExpanded);
        Assert.Equal(["a", "b", "c"], node.SelfAndDescendants().Select(n => n.Id));
        var deepest = node.Children[0].Children[0];
        Assert.Equal(["a", "b", "c"], deepest.SelfAndAncestors().Select(n => n.Id));

        var back = node.ToPage();
        Assert.False(back.IsExpanded);
        Assert.Equal("C", back.Children[0].Children[0].Title);
    }
}
