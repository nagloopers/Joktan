using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Core.ViewModels;
using Joktan.Settings.Resources;
using Joktan.Settings.Themes;

namespace Joktan.Settings.ViewModels;

/// <summary>Chooses the theme in use, and removes saved ones.</summary>
public partial class ThemesViewModel : PageViewModel
{
    private readonly ThemeEditor _editor;

    public ThemesViewModel(ThemeEditor editor, ThemeBarViewModel bar) : base(Strings.Themes_Themes, Strings.Themes_SettingsThemes)
    {
        _editor = editor;
        Bar = bar;
        Rebuild();
        editor.Themes.CollectionChanged += (_, _) => Rebuild();
        editor.PropertyChanged += OnEditorChanged;
    }

    public ThemeBarViewModel Bar { get; }

    public ObservableCollection<ThemeRowViewModel> Rows { get; } = [];

    /// <summary>Another theme cannot be chosen while there are changes to save or discard.</summary>
    public bool CanChoose => !_editor.IsDirty;

    private void Rebuild()
    {
        Rows.Clear();
        Rows.Add(new ThemeRowViewModel(ThemeEditor.FollowSystem, null, this));
        foreach (var theme in _editor.Themes)
            Rows.Add(new ThemeRowViewModel(theme.Name, _editor.IsBuiltIn(theme.Name) ? null : theme, this));
        Refresh();
    }

    private void Refresh()
    {
        foreach (var row in Rows)
            row.IsCurrent = row.Name == _editor.Current;
    }

    private void OnEditorChanged(object? sender, PropertyChangedEventArgs e)
    {
        Refresh();
        OnPropertyChanged(nameof(CanChoose));
    }

    internal void Choose(ThemeRowViewModel row)
    {
        Bar.Error = _editor.Choose(row.Name);
        Refresh();
    }

    internal void Delete(ThemeRowViewModel row)
    {
        if (row.Saved is { } theme)
            Bar.Error = _editor.Delete(theme);
    }
}

/// <summary>One choice on the Themes page.</summary>
public partial class ThemeRowViewModel : ViewModelBase
{
    private readonly ThemesViewModel _page;

    public ThemeRowViewModel(string name, Theme? saved, ThemesViewModel page)
    {
        Name = name;
        Saved = saved;
        _page = page;
    }

    public string Name { get; }

    public string DisplayName => ThemeEditor.DisplayName(Name);

    /// <summary>The theme, when it is one the user saved and so can be removed.</summary>
    public Theme? Saved { get; }

    public bool CanDelete => Saved is not null;

    public string Kind => Saved is null ? Strings.ThemeRow_BuiltIn : Strings.ThemeRow_Saved;

    /// <summary>Set by the radio button as well as by the theme in use changing.</summary>
    [ObservableProperty]
    public partial bool IsCurrent { get; set; }

    partial void OnIsCurrentChanged(bool value)
    {
        if (value)
            _page.Choose(this);
    }

    [RelayCommand]
    private void Delete() => _page.Delete(this);
}
