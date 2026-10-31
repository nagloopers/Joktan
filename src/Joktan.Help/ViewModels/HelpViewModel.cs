using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Core;
using Joktan.Core.ViewModels;

namespace Joktan.Help.ViewModels;

/// <summary>The help window: the list of topics, search through them, and the topic being read.</summary>
public partial class HelpViewModel : ViewModelBase
{
    /// <summary>The scheme of a link from one topic to another: <c>[Certificates](help:certificates)</c>.</summary>
    public const string LinkScheme = "help";

    private readonly List<HelpTopicViewModel> _topics;
    private readonly Stack<HelpTopicViewModel> _history = new();

    public HelpViewModel()
    {
        _topics = HelpTopic.All.Select(topic => new HelpTopicViewModel(topic)).ToList();
        VisibleTopics = _topics;
        Current = _topics[0];
        ListSelection = Current;
    }

    [ObservableProperty]
    public partial IReadOnlyList<HelpTopicViewModel> VisibleTopics { get; private set; }

    /// <summary>What is selected in the list. It is empty while a search hides the topic being read.</summary>
    [ObservableProperty]
    public partial HelpTopicViewModel? ListSelection { get; set; }

    [ObservableProperty]
    public partial HelpTopicViewModel Current { get; private set; }

    [ObservableProperty]
    public partial string? SearchText { get; set; }

    /// <summary>The words to mark in the topic, when the search box of the top bar led to it. Going to another topic ends it.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<string>? HighlightWords { get; set; }

    public bool HasNoMatches => VisibleTopics.Count == 0;

    /// <summary>Shows a topic, or the first one when the id is null or not a topic's.</summary>
    public void Open(string? id)
    {
        SearchText = null;
        Show(_topics.Find(t => t.Topic.Id == id) ?? _topics[0]);
    }

    /// <summary>The topics with all the words, for the search box of the top bar. Those named by the words come first.</summary>
    public IEnumerable<SearchHit> Search(IReadOnlyList<string> words) => _topics
        .Where(t => t.Matches(words))
        .OrderBy(t => words.All(t.MatchesTitle) ? 0 : 1)
        .Select(t => new SearchHit("Help", t.Title, t.SnippetFor(words), () =>
        {
            HelpContext.Opener?.Invoke(t.Topic.Id);
            HighlightWords = words;
        }));

    /// <summary>Follows a link if it leads to another topic; other links are not for this page to open.</summary>
    public bool OpenLink(Uri uri)
    {
        if (uri.Scheme != LinkScheme)
            return false;

        Open(uri.AbsolutePath);
        return true;
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void GoBack()
    {
        Current = _history.Pop();
        SearchText = null;
        ListSelection = Current;
        GoBackCommand.NotifyCanExecuteChanged();
    }

    private bool CanGoBack() => _history.Count > 0;

    private void Show(HelpTopicViewModel topic)
    {
        if (topic != Current)
        {
            _history.Push(Current);
            Current = topic;
            GoBackCommand.NotifyCanExecuteChanged();
        }

        ListSelection = VisibleTopics.Contains(topic) ? topic : null;
    }

    partial void OnCurrentChanged(HelpTopicViewModel value) => HighlightWords = null;

    partial void OnListSelectionChanged(HelpTopicViewModel? value)
    {
        if (value is not null)
            Show(value);
    }

    partial void OnSearchTextChanged(string? value)
    {
        var words = (value ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var topic in _topics)
            topic.Snippet = words.Length > 0 && topic.Matches(words) ? topic.SnippetFor(words) : null;

        // Topics named by the search come first; the rest keep their order.
        VisibleTopics = words.Length == 0
            ? _topics
            : _topics.Where(t => t.HasSnippet).OrderBy(t => words.All(t.MatchesTitle) ? 0 : 1).ToList();
        OnPropertyChanged(nameof(HasNoMatches));

        // Changing the list clears its selection; the topic being read stays.
        ListSelection = VisibleTopics.Contains(Current) ? Current : null;
    }
}
