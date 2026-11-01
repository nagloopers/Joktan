using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Core.ViewModels;
using Joktan.Settings.Resources;
using Joktan.Settings.Themes;

namespace Joktan.Settings.ViewModels;

/// <summary>What is being edited, and saving it in a theme. The same one is on each page that changes a theme.</summary>
public partial class ThemeBarViewModel : ViewModelBase
{
    public ThemeBarViewModel(ThemeEditor editor)
    {
        Editor = editor;
        editor.PropertyChanged += OnEditorChanged;
    }

    public ThemeEditor Editor { get; }

    public string Status => (Editor.Current == ThemeEditor.FollowSystem ? string.Format(Strings.ThemeBar_FollowingTheSystem, ThemeEditor.DisplayName(Editor.Draft.Name)) : ThemeEditor.DisplayName(Editor.Current))
                            + (Editor.IsDirty ? Strings.ThemeBar_ChangesNotSaved : "");

    /// <summary>Why Save is not offered, when the theme is one that comes with Joktan.</summary>
    public bool ShowBuiltInHint => Editor.IsDirty && !Editor.CanOverwrite;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveAsNewCommand))]
    public partial string NewName { get; set; } = "";

    [ObservableProperty]
    public partial string? Error { get; set; }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save() => Error = Editor.Save();

    private bool CanSave() => Editor.IsDirty && Editor.CanOverwrite;

    [RelayCommand(CanExecute = nameof(CanSaveAsNew))]
    private void SaveAsNew()
    {
        Error = Editor.SaveAs(NewName);
        if (Error is null)
            NewName = "";
    }

    private bool CanSaveAsNew() => !string.IsNullOrWhiteSpace(NewName);

    [RelayCommand(CanExecute = nameof(CanDiscard))]
    private void Discard()
    {
        Error = null;
        Editor.Discard();
    }

    private bool CanDiscard() => Editor.IsDirty;

    private void OnEditorChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(ShowBuiltInHint));
        SaveCommand.NotifyCanExecuteChanged();
        DiscardCommand.NotifyCanExecuteChanged();
    }
}
