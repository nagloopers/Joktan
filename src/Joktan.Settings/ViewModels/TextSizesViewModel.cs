using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Joktan.Core.ViewModels;
using Joktan.Settings.Resources;
using Joktan.Settings.Themes;

namespace Joktan.Settings.ViewModels;

/// <summary>Changes the text sizes of the theme being edited.</summary>
public class TextSizesViewModel : PageViewModel
{
    public TextSizesViewModel(ThemeEditor editor, ThemeBarViewModel bar) : base(Strings.TextSizes_TextSizes, Strings.TextSizes_SettingsTextSizes)
    {
        Bar = bar;
        Groups = ThemeTokens.TextSizes
            .GroupBy(token => token.Group)
            .Select(group => new TextSizeGroupViewModel(group.Key, group.Select(token => new TextSizeRowViewModel(token, editor)).ToList()))
            .ToList();
        editor.DraftReplaced += Refresh;
        Refresh();
    }

    public ThemeBarViewModel Bar { get; }

    public IReadOnlyList<TextSizeGroupViewModel> Groups { get; }

    private void Refresh()
    {
        foreach (var row in Groups.SelectMany(group => group.Rows))
            row.Refresh();
    }
}

public sealed record TextSizeGroupViewModel(string Name, IReadOnlyList<TextSizeRowViewModel> Rows);

/// <summary>One text size of the theme.</summary>
public partial class TextSizeRowViewModel : ViewModelBase
{
    private readonly ThemeToken _token;
    private readonly ThemeEditor _editor;
    private bool _refreshing;

    public TextSizeRowViewModel(ThemeToken token, ThemeEditor editor)
    {
        _token = token;
        _editor = editor;
    }

    public string Label => _token.Label;

    public decimal MinSize => (decimal)Theme.MinTextSize;

    public decimal MaxSize => (decimal)Theme.MaxTextSize;

    /// <summary>What the box holds; it is empty while someone has cleared it to type a new size.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SampleSize))]
    public partial decimal? Size { get; set; }

    public double SampleSize => _editor.Draft.TextSizes[_token.Key];

    public void Refresh()
    {
        _refreshing = true;
        Size = (decimal)_editor.Draft.TextSizes[_token.Key];
        _refreshing = false;
        OnPropertyChanged(nameof(SampleSize));
    }

    partial void OnSizeChanged(decimal? value)
    {
        if (!_refreshing && value is { } size)
            _editor.SetTextSize(_token.Key, (double)size);
    }
}
