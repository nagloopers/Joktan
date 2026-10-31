using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Joktan.Core;
using Joktan.Core.ViewModels;
using Markdig;

namespace Joktan.Help.ViewModels;

/// <summary>A topic in the help window's list.</summary>
public partial class HelpTopicViewModel : ViewModelBase
{
    private readonly string[] _lines;

    public HelpTopicViewModel(HelpTopic topic)
    {
        Topic = topic;
        Markdown = topic.ReadMarkdown();

        // Searched as the reader sees it, so link addresses and Markdown marks do not match.
        _lines = TextSearch.Lines(Markdig.Markdown.ToPlainText(Markdown));
    }

    public HelpTopic Topic { get; }

    public string Title => Topic.Title;

    /// <summary>A topic sits below its parent in the full list, but not among search results.</summary>
    public bool IsIndented => Topic.Parent is not null && !HasSnippet;

    public string Markdown { get; }

    /// <summary>Where a search found its text; null when not searching.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSnippet), nameof(IsIndented))]
    public partial string? Snippet { get; set; }

    public bool HasSnippet => Snippet is not null;

    /// <summary>Whether every word appears in the topic, in its title or its text.</summary>
    public bool Matches(IReadOnlyList<string> words)
        => words.All(word => MatchesTitle(word) || _lines.Any(line => line.Contains(word, StringComparison.OrdinalIgnoreCase)));

    public bool MatchesTitle(string word) => Title.Contains(word, StringComparison.OrdinalIgnoreCase);

    /// <summary>The line holding the most of the words, cut down to the part around the first found.</summary>
    public string SnippetFor(IReadOnlyList<string> words)
    {
        var text = TextSearch.Snippet(_lines.Skip(1), words);
        return Topic.Parent is { } parent ? $"{parent} · {text}" : text;
    }
}
