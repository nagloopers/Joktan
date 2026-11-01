using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Joktan.Core.ViewModels;
using Joktan.Settings.Resources;
using Joktan.Settings.Themes;

namespace Joktan.Settings.ViewModels;

/// <summary>Changes the colours of the theme being edited.</summary>
public partial class ColorsViewModel : PageViewModel
{
    private readonly ThemeEditor _editor;

    public ColorsViewModel(ThemeEditor editor, ThemeBarViewModel bar) : base(Strings.Colors_Colours, Strings.Colors_SettingsColours)
    {
        _editor = editor;
        Bar = bar;
        Groups = ThemeTokens.Colors
            .GroupBy(token => token.Group)
            .Select(group => new ColorGroupViewModel(group.Key, group.Select(token => new ColorRowViewModel(token, editor)).ToList()))
            .ToList();
        editor.DraftReplaced += Refresh;
        Refresh();
    }

    public ThemeBarViewModel Bar { get; }

    public IReadOnlyList<ColorGroupViewModel> Groups { get; }

    /// <summary>Whether controls such as text boxes and switches take their dark look.</summary>
    [ObservableProperty]
    public partial bool IsDark { get; set; }

    partial void OnIsDarkChanged(bool value)
    {
        if (_editor.Draft.IsDark != value)
            _editor.SetDark(value);
    }

    private void Refresh()
    {
        IsDark = _editor.Draft.IsDark;
        foreach (var row in Groups.SelectMany(group => group.Rows))
            row.Refresh();
    }
}

public sealed record ColorGroupViewModel(string Name, IReadOnlyList<ColorRowViewModel> Rows);

/// <summary>One colour, as a picker and as text. Text that is not a colour is marked and goes no further.</summary>
public partial class ColorRowViewModel : ViewModelBase
{
    private readonly ThemeToken _token;
    private readonly ThemeEditor _editor;
    private bool _refreshing;

    public ColorRowViewModel(ThemeToken token, ThemeEditor editor)
    {
        _token = token;
        _editor = editor;
    }

    public string Label => _token.Label;

    [ObservableProperty]
    public partial Color Color { get; set; }

    [ObservableProperty]
    public partial string Hex { get; set; } = "";

    [ObservableProperty]
    public partial bool IsInvalid { get; set; }

    public void Refresh()
    {
        var hex = _editor.Draft.Colors[_token.Key];
        Show(hex, Color.Parse(hex));
        IsInvalid = false;
    }

    partial void OnColorChanged(Color value)
    {
        if (_refreshing)
            return;

        var hex = Theme.ToHex(value);
        Show(hex, value);
        IsInvalid = false;
        _editor.SetColor(_token.Key, hex);
    }

    partial void OnHexChanged(string value)
    {
        if (_refreshing)
            return;

        var hex = Theme.NormalizeColor(value);
        IsInvalid = hex is null;
        if (hex is null)
            return;

        // What is being typed stays as it is typed.
        _refreshing = true;
        Color = Color.Parse(hex);
        _refreshing = false;
        _editor.SetColor(_token.Key, hex);
    }

    private void Show(string hex, Color color)
    {
        _refreshing = true;
        Color = color;
        Hex = hex;
        _refreshing = false;
    }
}
