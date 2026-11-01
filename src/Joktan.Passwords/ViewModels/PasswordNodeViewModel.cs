using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Joktan.Core.ViewModels;
using Joktan.Passwords.Resources;

namespace Joktan.Passwords.ViewModels;

/// <summary>A folder or an entry in the password tree.</summary>
public partial class PasswordNodeViewModel : ViewModelBase
{
    public static string Untitled => Strings.PasswordNode_Untitled;

    public PasswordNodeViewModel(PasswordFolder folder, PasswordNodeViewModel? parent)
    {
        Folder = folder;
        Parent = parent;
        IsExpanded = folder.IsExpanded;
        foreach (var child in folder.Folders)
            Children.Add(new PasswordNodeViewModel(child, this));
        foreach (var entry in folder.Entries)
            Children.Add(new PasswordNodeViewModel(entry, this));
        Sort();
    }

    public PasswordNodeViewModel(PasswordEntry entry, PasswordNodeViewModel parent)
    {
        Entry = entry;
        Parent = parent;
    }

    public PasswordFolder? Folder { get; }

    public PasswordEntry? Entry { get; }

    public bool IsFolder => Folder is not null;

    public bool IsEntry => Entry is not null;

    public PasswordNodeViewModel? Parent { get; set; }

    public ObservableCollection<PasswordNodeViewModel> Children { get; } = [];

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    partial void OnIsExpandedChanged(bool value)
    {
        if (Folder is not null)
            Folder.IsExpanded = value;
    }

    public string Name => (Folder?.Name ?? Entry!.Title) is { Length: > 0 } name && !string.IsNullOrWhiteSpace(name) ? name : Untitled;

    /// <summary>The username, shown beside an entry's title.</summary>
    public string Detail => Entry?.Username ?? "";

    /// <summary>The folders above it, outermost first, such as "Work / Servers"; empty at the top.</summary>
    public string Path => string.Join(" / ", Ancestors().Reverse().Select(n => n.Name));

    /// <summary>The folders above it, innermost first, without the root.</summary>
    public IEnumerable<PasswordNodeViewModel> Ancestors()
    {
        for (var node = Parent; node?.Parent is not null; node = node.Parent)
            yield return node;
    }

    /// <summary>Every node from this one down.</summary>
    public IEnumerable<PasswordNodeViewModel> SelfAndDescendants()
        => Children.SelectMany(c => c.SelfAndDescendants()).Prepend(this);

    public void Refresh()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Detail));
    }

    /// <summary>Folders first, then entries, each by name.</summary>
    public void Sort()
    {
        var sorted = Children
            .OrderBy(c => c.IsFolder ? 0 : 1)
            .ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        for (var i = 0; i < sorted.Count; i++)
        {
            var at = Children.IndexOf(sorted[i]);
            if (at != i)
                Children.Move(at, i);
        }
    }

    /// <summary>Whether its title, username, website or notes contain the text.</summary>
    public bool Matches(string text) => Entry is { } e
        && (e.Title.Contains(text, StringComparison.CurrentCultureIgnoreCase)
            || e.Username.Contains(text, StringComparison.CurrentCultureIgnoreCase)
            || e.Url.Contains(text, StringComparison.CurrentCultureIgnoreCase)
            || e.Notes.Contains(text, StringComparison.CurrentCultureIgnoreCase));
}

/// <summary>A folder offered as the place for an entry or folder, indented by depth.</summary>
public sealed record PasswordFolderChoice(PasswordNodeViewModel Node, string Label)
{
    public override string ToString() => Label;
}
