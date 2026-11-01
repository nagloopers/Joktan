using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Joktan.Core.ViewModels;
using Joktan.Notes.Resources;

namespace Joktan.Notes.ViewModels;

/// <summary>One page of notes in the tree, below its parent page.</summary>
public partial class NoteNodeViewModel : ViewModelBase
{
    public static string Untitled => Strings.NoteNode_Untitled;

    public NoteNodeViewModel(string id, string title, NoteNodeViewModel? parent)
    {
        Id = id;
        Title = title;
        Parent = parent;
    }

    public string Id { get; }

    public NoteNodeViewModel? Parent { get; set; }

    public ObservableCollection<NoteNodeViewModel> Children { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayTitle))]
    public partial string Title { get; set; }

    /// <summary>The title in the tree, which is never blank.</summary>
    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? Untitled : Title;

    [ObservableProperty]
    public partial bool IsExpanded { get; set; } = true;

    /// <summary>The page's Markdown, once it has been read from its file; null until then.</summary>
    public string? Text { get; set; }

    /// <summary>Whether the text has changed since it was last written.</summary>
    public bool IsDirty { get; set; }

    /// <summary>Every page from this one down.</summary>
    public IEnumerable<NoteNodeViewModel> SelfAndDescendants()
        => Children.SelectMany(c => c.SelfAndDescendants()).Prepend(this);

    /// <summary>This page and the ones above it, outermost first.</summary>
    public IEnumerable<NoteNodeViewModel> SelfAndAncestors()
    {
        var path = new List<NoteNodeViewModel>();
        for (var node = this; node is not null; node = node.Parent)
            path.Insert(0, node);
        return path;
    }

    public NotePage ToPage() => new()
    {
        Id = Id,
        Title = Title,
        IsExpanded = IsExpanded,
        Children = Children.Select(c => c.ToPage()).ToList(),
    };

    public static NoteNodeViewModel FromPage(NotePage page, NoteNodeViewModel? parent)
    {
        var node = new NoteNodeViewModel(page.Id, page.Title, parent) { IsExpanded = page.IsExpanded };
        foreach (var child in page.Children)
            node.Children.Add(FromPage(child, node));
        return node;
    }
}
