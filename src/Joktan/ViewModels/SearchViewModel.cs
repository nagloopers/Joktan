using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Core;
using Joktan.Core.ViewModels;
using Joktan.Resources;

namespace Joktan.ViewModels;

/// <summary>One area the search box can look through.</summary>
/// <param name="Name">The area as the search box's hint names it, such as "notes".</param>
public sealed record SearchSource(string Name, Func<bool> IsOn, Func<IReadOnlyList<string>, IEnumerable<SearchHit>> Find);

/// <summary>The search box of the top bar and its results. Specified in docs/features/search/spec.md.</summary>
public partial class SearchViewModel : ViewModelBase
{
    private readonly IReadOnlyList<SearchSource> _sources;

    public SearchViewModel(IReadOnlyList<SearchSource> sources)
    {
        _sources = sources;
    }

    /// <summary>Opens the page that chooses the areas. Set by the app.</summary>
    public Action? OpenSettings { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasText))]
    public partial string? Text { get; set; }

    /// <summary>Whether there is anything in the box to clear.</summary>
    public bool HasText => !string.IsNullOrEmpty(Text);

    /// <summary>The words typed, for marking them in the results.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<string> Words { get; private set; } = [];

    /// <summary>Raised when the box is left with nothing to look for, so the marks search left on pages can go.</summary>
    public event Action? Cleared;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoMatches))]
    public partial IReadOnlyList<SearchHit> Results { get; private set; } = [];

    /// <summary>The result Enter opens.</summary>
    [ObservableProperty]
    public partial SearchHit? Highlighted { get; set; }

    /// <summary>Whether the results are showing.</summary>
    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    public bool IsAllOff => !_sources.Any(s => s.IsOn());

    public bool HasNoMatches => !IsAllOff && Results.Count == 0;

    /// <summary>Names the areas switched on, so the box says what it looks through.</summary>
    public string Placeholder
    {
        get
        {
            var names = _sources.Where(s => s.IsOn()).Select(s => s.Name).ToList();
            return names.Count switch
            {
                0 => Strings.Search_SearchIsSwitchedOff,
                1 => string.Format(Strings.Search_Search, names[0]),
                _ => string.Format(Strings.Search_SearchAnd, string.Join(", ", names.SkipLast(1)), names[^1]),
            };
        }
    }

    /// <summary>Looks again, as what was found may have changed, and shows the results if anything has been typed.</summary>
    public void Show()
    {
        Refresh();
        IsOpen = TextSearch.Words(Text).Length > 0;
    }

    /// <summary>Takes in a change of the areas switched on.</summary>
    public void AreasChanged()
    {
        Refresh();
        OnPropertyChanged(nameof(Placeholder));
    }

    /// <summary>Moves the highlight through the results, stopping at either end.</summary>
    public void MoveHighlight(int by)
    {
        if (Results.Count == 0)
            return;

        var index = Highlighted is { } hit ? IndexOf(hit) : -1;
        Highlighted = Results[Math.Clamp(index + by, 0, Results.Count - 1)];
    }

    /// <summary>Empties the box and hides the results.</summary>
    public void Clear()
    {
        Text = null;
        IsOpen = false;
    }

    /// <summary>Hides the results, or clears the box when they are hidden already.</summary>
    public void Dismiss()
    {
        if (IsOpen)
            IsOpen = false;
        else
            Text = null;
    }

    [RelayCommand]
    private void Open(SearchHit? hit)
    {
        if (hit is null)
            return;

        IsOpen = false;
        hit.Open();
    }

    [RelayCommand]
    private void ShowSettings()
    {
        IsOpen = false;
        OpenSettings?.Invoke();
    }

    partial void OnTextChanged(string? value)
    {
        Show();
        if (Words.Count == 0)
            Cleared?.Invoke();
    }

    private void Refresh()
    {
        var words = TextSearch.Words(Text);
        Words = words;
        Results = words.Length == 0 ? [] : _sources.Where(s => s.IsOn()).SelectMany(s => s.Find(words)).ToList();
        OnPropertyChanged(nameof(IsAllOff));
        OnPropertyChanged(nameof(HasNoMatches));

        // Changing the list clears its selection, so the highlight is set after.
        Highlighted = Results.FirstOrDefault();
    }

    private int IndexOf(SearchHit hit)
    {
        for (var i = 0; i < Results.Count; i++)
        {
            if (ReferenceEquals(Results[i], hit))
                return i;
        }

        return -1;
    }
}
