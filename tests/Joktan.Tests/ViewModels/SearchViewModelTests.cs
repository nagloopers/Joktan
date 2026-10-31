using System.Collections.Generic;
using System.Linq;
using Joktan.Core;
using Joktan.ViewModels;
using Xunit;

namespace Joktan.Tests.ViewModels;

public class SearchViewModelTests
{
    private bool _firstOn = true;
    private bool _secondOn = true;
    private readonly List<string> _opened = [];

    private SearchViewModel New() => new(
    [
        new SearchSource("first", () => _firstOn, words => Hits("First", words)),
        new SearchSource("second", () => _secondOn, words => Hits("Second", words)),
    ]);

    private IEnumerable<SearchHit> Hits(string area, IReadOnlyList<string> words) =>
        words.Contains("none") ? [] : [new SearchHit(area, "one", "", () => _opened.Add(area + " one")), new SearchHit(area, "two", "", () => _opened.Add(area + " two"))];

    [Fact]
    public void Nothing_typed_shows_nothing()
    {
        var search = New();

        search.Text = "   ";

        Assert.False(search.IsOpen);
        Assert.Empty(search.Results);
        Assert.Null(search.Highlighted);
    }

    [Fact]
    public void Typing_finds_in_each_area_in_order_and_highlights_the_first()
    {
        var search = New();

        search.Text = "x";

        Assert.True(search.IsOpen);
        Assert.Equal(["First", "First", "Second", "Second"], search.Results.Select(h => h.Area));
        Assert.Same(search.Results[0], search.Highlighted);
        Assert.False(search.HasNoMatches);
    }

    [Fact]
    public void An_area_switched_off_is_left_out()
    {
        _firstOn = false;
        var search = New();

        search.Text = "x";

        Assert.All(search.Results, h => Assert.Equal("Second", h.Area));
    }

    [Fact]
    public void A_change_of_areas_finds_again_and_changes_the_hint()
    {
        var search = New();
        search.Text = "x";
        var changed = new List<string?>();
        search.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        _secondOn = false;
        search.AreasChanged();

        Assert.All(search.Results, h => Assert.Equal("First", h.Area));
        Assert.Contains(nameof(SearchViewModel.Placeholder), changed);
    }

    [Theory]
    [InlineData(true, true, "Search first and second...")]
    [InlineData(false, true, "Search second...")]
    [InlineData(false, false, "Search is switched off")]
    public void The_hint_names_the_areas_on(bool first, bool second, string hint)
    {
        (_firstOn, _secondOn) = (first, second);

        Assert.Equal(hint, New().Placeholder);
    }

    [Fact]
    public void Nothing_found_is_told_apart_from_every_area_off()
    {
        var search = New();
        search.Text = "none";
        Assert.True(search.HasNoMatches);
        Assert.False(search.IsAllOff);

        _firstOn = _secondOn = false;
        search.AreasChanged();
        Assert.False(search.HasNoMatches);
        Assert.True(search.IsAllOff);
    }

    [Fact]
    public void The_highlight_stops_at_either_end()
    {
        var search = New();
        search.Text = "x";

        search.MoveHighlight(-1);
        Assert.Same(search.Results[0], search.Highlighted);

        search.MoveHighlight(1);
        Assert.Same(search.Results[1], search.Highlighted);

        for (var i = 0; i < 10; i++)
            search.MoveHighlight(1);
        Assert.Same(search.Results[^1], search.Highlighted);
    }

    [Fact]
    public void The_highlight_cannot_move_with_no_results()
    {
        var search = New();

        search.MoveHighlight(1);

        Assert.Null(search.Highlighted);
    }

    [Fact]
    public void With_no_highlight_moving_down_takes_the_first()
    {
        var search = New();
        search.Text = "x";
        search.Highlighted = null;

        search.MoveHighlight(1);

        Assert.Same(search.Results[0], search.Highlighted);
    }

    [Fact]
    public void Opening_a_result_hides_the_results_and_keeps_the_text()
    {
        var search = New();
        search.Text = "x";

        search.OpenCommand.Execute(search.Results[3]);

        Assert.Equal(["Second two"], _opened);
        Assert.False(search.IsOpen);
        Assert.Equal("x", search.Text);
    }

    [Fact]
    public void Opening_no_result_does_nothing()
    {
        var search = New();
        search.Text = "x";

        search.OpenCommand.Execute(null);

        Assert.Empty(_opened);
        Assert.True(search.IsOpen);
    }

    [Fact]
    public void Dismissing_hides_the_results_then_clears_the_box()
    {
        var search = New();
        search.Text = "x";

        search.Dismiss();
        Assert.False(search.IsOpen);
        Assert.Equal("x", search.Text);

        search.Dismiss();
        Assert.Null(search.Text);
        Assert.False(search.IsOpen);
    }

    [Fact]
    public void Showing_again_looks_again()
    {
        var search = New();
        search.Text = "x";
        search.Dismiss();
        _firstOn = false;

        search.Show();

        Assert.True(search.IsOpen);
        Assert.All(search.Results, h => Assert.Equal("Second", h.Area));
    }

    [Fact]
    public void The_settings_button_hides_the_results_and_goes_to_the_page()
    {
        var search = New();
        var opened = 0;
        search.OpenSettings = () => opened++;
        search.Text = "x";

        search.ShowSettingsCommand.Execute(null);

        Assert.Equal(1, opened);
        Assert.False(search.IsOpen);
    }

    [Fact]
    public void Clearing_empties_the_box_hides_the_results_and_says_so()
    {
        var search = New();
        var cleared = 0;
        search.Cleared += () => cleared++;
        search.Text = "x";
        Assert.True(search.HasText);
        Assert.Equal(["x"], search.Words);
        Assert.Equal(0, cleared);

        search.Clear();

        Assert.False(search.HasText);
        Assert.False(search.IsOpen);
        Assert.Empty(search.Results);
        Assert.Empty(search.Words);
        Assert.Equal(1, cleared);
    }
}
