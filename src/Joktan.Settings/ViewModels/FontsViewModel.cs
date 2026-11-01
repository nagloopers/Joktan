using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Joktan.Core.ViewModels;
using Joktan.Settings.Resources;
using Joktan.Settings.Themes;

namespace Joktan.Settings.ViewModels;

/// <summary>Changes the fonts of the theme being edited.</summary>
public class FontsViewModel : PageViewModel
{
    public FontsViewModel(ThemeEditor editor, ThemeBarViewModel bar) : base(Strings.Fonts_Fonts, Strings.Fonts_SettingsFonts)
    {
        Bar = bar;

        var installed = FontManager.Current.SystemFonts
            .Select(family => family.Name)
            .Distinct()
            .OrderBy(name => name, System.StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        Rows = ThemeTokens.Fonts.Select(token => new FontRowViewModel(token, installed, editor)).ToList();

        editor.DraftReplaced += Refresh;
        editor.FontChanged += RefreshSamples;
        Refresh();
    }

    public ThemeBarViewModel Bar { get; }

    public IReadOnlyList<FontRowViewModel> Rows { get; }

    private void Refresh()
    {
        foreach (var row in Rows)
            row.Refresh();
    }

    private void RefreshSamples()
    {
        foreach (var row in Rows)
            row.RefreshSample();
    }
}

/// <summary>A font offered for a role. An empty name is the font the role comes with.</summary>
public sealed record FontChoice(string Name, string Label)
{
    public override string ToString() => Label;
}

/// <summary>One font of the theme.</summary>
public partial class FontRowViewModel : ViewModelBase
{
    private readonly ThemeToken _token;
    private readonly ThemeEditor _editor;
    private readonly FontChoice _default;
    private readonly IReadOnlyList<string> _installed;
    private bool _refreshing;

    public FontRowViewModel(ThemeToken token, IReadOnlyList<string> installed, ThemeEditor editor)
    {
        _token = token;
        _editor = editor;
        _installed = installed;
        _default = new FontChoice("", DefaultLabel(token.Key));
        Choices = [_default, .. installed.Select(name => new FontChoice(name, name))];
    }

    public string Label => _token.Label;

    /// <summary>The font this role has now, which may be another role's or a fallback.</summary>
    public FontFamily Sample => Application.Current?.TryGetResource(_token.Key, null, out var font) == true && font is FontFamily family
        ? family
        : FontFamily.Default;

    [ObservableProperty]
    public partial IReadOnlyList<FontChoice> Choices { get; private set; }

    [ObservableProperty]
    public partial FontChoice? Selected { get; set; }

    public void Refresh()
    {
        var name = _editor.Draft.Fonts[_token.Key];

        _refreshing = true;
        // A theme can name a font this machine does not have. It is kept, and falls back when drawn.
        if (name.Length > 0 && !_installed.Contains(name))
            Choices = [_default, new FontChoice(name, string.Format(Strings.FontRow_NotInstalled, name)), .. _installed.Select(n => new FontChoice(n, n))];
        else if (Choices.Count != _installed.Count + 1)
            Choices = [_default, .. _installed.Select(n => new FontChoice(n, n))];

        Selected = Choices.First(choice => choice.Name == name);
        _refreshing = false;
        RefreshSample();
    }

    public void RefreshSample() => OnPropertyChanged(nameof(Sample));

    partial void OnSelectedChanged(FontChoice? value)
    {
        if (!_refreshing && value is not null)
            _editor.SetFont(_token.Key, value.Name);
    }

    private static string DefaultLabel(string key) => key switch
    {
        ThemeTokens.InterfaceFont => Strings.FontRow_InterComesWithJoktan,
        ThemeTokens.CodeFont => Strings.FontRow_TheSystemSMonospacedFont,
        _ => Strings.FontRow_SameAsTheInterface,
    };
}
