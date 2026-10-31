using System;
using System.Linq;
using System.Threading.Tasks;
using Joktan.Help.ViewModels;
using Joktan.Tests.TestSupport;
using Xunit;
using System.Collections.Generic;
using Joktan.Core;

namespace Joktan.Help.Tests.ViewModels;

public class HelpViewModelTests
{
    [Fact]
    public Task Starts_on_the_first_topic_with_all_listed() => Headless.Run(() =>
    {
        var vm = new HelpViewModel();

        Assert.Equal(HelpTopic.Home, vm.Current.Topic.Id);
        Assert.Same(vm.Current, vm.ListSelection);
        Assert.Equal(HelpTopic.All.Count, vm.VisibleTopics.Count);
        Assert.False(vm.HasNoMatches);
        Assert.False(vm.GoBackCommand.CanExecute(null));
    });

    [Theory]
    [InlineData("mqtt", "mqtt")]
    [InlineData(null, HelpTopic.Home)]
    [InlineData("no-such-topic", HelpTopic.Home)]
    public Task Opening_shows_the_topic_or_the_first(string? id, string expected) => Headless.Run(() =>
    {
        var vm = new HelpViewModel();
        vm.SearchText = "something";

        vm.Open(id);

        Assert.Equal(expected, vm.Current.Topic.Id);
        Assert.Null(vm.SearchText);
    });

    [Fact]
    public Task Going_back_retraces_the_topics_read() => Headless.Run(() =>
    {
        var vm = new HelpViewModel();
        vm.Open("mqtt");
        vm.ListSelection = vm.VisibleTopics.First(t => t.Topic.Id == "notes");
        Assert.True(vm.GoBackCommand.CanExecute(null));

        vm.GoBackCommand.Execute(null);
        Assert.Equal("mqtt", vm.Current.Topic.Id);
        Assert.Same(vm.Current, vm.ListSelection);

        vm.GoBackCommand.Execute(null);
        Assert.Equal(HelpTopic.Home, vm.Current.Topic.Id);
        Assert.False(vm.GoBackCommand.CanExecute(null));
    });

    [Fact]
    public Task Opening_the_topic_being_read_leaves_no_history() => Headless.Run(() =>
    {
        var vm = new HelpViewModel();

        vm.Open(HelpTopic.Home);

        Assert.False(vm.GoBackCommand.CanExecute(null));
    });

    [Fact]
    public Task Help_links_open_topics_and_others_are_left_alone() => Headless.Run(() =>
    {
        var vm = new HelpViewModel();

        Assert.True(vm.OpenLink(new Uri("help:certificates")));
        Assert.Equal("certificates", vm.Current.Topic.Id);

        Assert.False(vm.OpenLink(new Uri("https://example.com")));
        Assert.Equal("certificates", vm.Current.Topic.Id);
    });

    [Fact]
    public Task Searching_lists_topics_named_by_it_first_and_clears_the_selection_if_hidden() => Headless.Run(() =>
    {
        var vm = new HelpViewModel();

        vm.SearchText = "certificates";

        Assert.Equal("certificates", vm.VisibleTopics[0].Topic.Id);
        Assert.All(vm.VisibleTopics, t => Assert.True(t.HasSnippet));
        Assert.Equal(HelpTopic.Home, vm.Current.Topic.Id);
        Assert.Equal(vm.VisibleTopics.Contains(vm.Current) ? vm.Current : null, vm.ListSelection);

        vm.SearchText = "";
        Assert.Equal(HelpTopic.All.Count, vm.VisibleTopics.Count);
        Assert.Same(vm.Current, vm.ListSelection);
        Assert.All(vm.VisibleTopics, t => Assert.False(t.HasSnippet));
    });

    [Fact]
    public Task A_search_that_finds_nothing_says_so() => Headless.Run(() =>
    {
        var vm = new HelpViewModel();

        vm.SearchText = "zzzz-not-in-any-topic";

        Assert.True(vm.HasNoMatches);
        Assert.Null(vm.ListSelection);
        Assert.Equal(HelpTopic.Home, vm.Current.Topic.Id);
    });

    [Fact]
    public Task Opening_clears_the_search_so_the_topic_is_selected_in_the_list() => Headless.Run(() =>
    {
        var vm = new HelpViewModel();
        vm.SearchText = "zzzz-not-in-any-topic";

        vm.Open("mqtt");

        Assert.Equal(HelpTopic.All.Count, vm.VisibleTopics.Count);
        Assert.Same(vm.Current, vm.ListSelection);
        Assert.Equal("mqtt", vm.Current.Topic.Id);
    });

    [Fact]
    public Task Search_for_the_top_bar_finds_topics_and_leaves_the_help_windows_list_alone() => Headless.Run(() =>
    {
        var vm = new HelpViewModel();

        var hits = vm.Search(["passwords"]).ToList();

        Assert.Equal("Passwords", hits[0].Title);
        Assert.All(hits, h => Assert.Equal("Help", h.Area));
        Assert.Empty(vm.Search(["nosuchwordanywhere"]));
        Assert.Equal(HelpTopic.All.Count, vm.VisibleTopics.Count);
        Assert.False(vm.Current.HasSnippet);
    });

    [Fact]
    public Task Opening_a_result_opens_help_on_that_topic() => Headless.Run(() =>
    {
        var opened = new List<string?>();
        var before = HelpContext.Opener;
        HelpContext.Opener = opened.Add;
        try
        {
            new HelpViewModel().Search(["passwords"]).First().Open();
        }
        finally
        {
            HelpContext.Opener = before;
        }

        Assert.Equal(["passwords"], opened);
    });

    [Fact]
    public Task Opening_a_result_marks_the_words_until_another_topic() => Headless.Run(() =>
    {
        var vm = new HelpViewModel();
        var before = HelpContext.Opener;
        HelpContext.Opener = id => vm.Open(id);
        try
        {
            vm.Search(["passwords"]).First().Open();
        }
        finally
        {
            HelpContext.Opener = before;
        }

        Assert.Equal("passwords", vm.Current.Topic.Id);
        Assert.Equal(["passwords"], vm.HighlightWords);

        vm.Open("mqtt");
        Assert.Null(vm.HighlightWords);
    });
}
