using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Core;
using Joktan.Core.ViewModels;

namespace Joktan.Notes.ViewModels;


/// <summary>
/// Notes: pages of Markdown that nest below one another. Specified in
/// docs/features/notes/spec.md.
/// </summary>
public partial class NotesViewModel : PageViewModel
{
    // Typing is written to disk this long after the last key.
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(600);

    private const string WelcomeText = """
        # Notes

        Each page is written in **Markdown** and shown formatted beside the text. Pages can sit below other pages: use **New child page**, or **Indent** to move a page below the one above it.

        ## What you can write

        - *Italic*, **bold**, ~~struck out~~ and `code`
        - [Links](https://commonmark.org/help/) and <https://example.com>
        - [x] Task lists
        - [ ] Numbered lists, quotes, tables and code blocks

        > A quote

        | Shortcut | Does |
        | --- | ---: |
        | `# ` | Heading |
        | `- ` | List item |

        ```
        code block
        ```
        """;

    private readonly DispatcherTimer _saveTimer;
    private NoteStore _store;
    private bool _indexDirty;

    public NotesViewModel(NoteStore store) : base("Notes", "Notes")
    {
        _saveTimer = new DispatcherTimer { Interval = SaveDelay };
        _saveTimer.Tick += (_, _) => Save();
        _store = store;
        Open(store);
    }

    /// <summary>Asks the user for a folder. Set by the view, which owns the system dialog.</summary>
    public Func<Task<string?>>? PickFolder { get; set; }

    /// <summary>The top-level pages, each with the pages below it.</summary>
    public ObservableCollection<NoteNodeViewModel> Roots { get; } = [];

    public bool IsEmpty => Roots.Count == 0;

    public string Folder => _store.Folder;

    public string DefaultFolder => NoteStore.DefaultFolder;

    public bool IsDefaultFolder => NoteStore.SameFolder(Folder, DefaultFolder);

    [ObservableProperty]
    public partial bool IsChoosingFolder { get; set; }

    /// <summary>The folder typed or picked in the folder band, used once applied.</summary>
    [ObservableProperty]
    public partial string FolderText { get; set; } = "";

    /// <summary>What happened when the folder was last changed.</summary>
    [ObservableProperty]
    public partial string? FolderMessage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(TreeSelection), nameof(PageTitle), nameof(Text), nameof(IsEmptyView), nameof(Path), nameof(DeleteText))]
    [NotifyCanExecuteChangedFor(nameof(NewChildPageCommand), nameof(DeleteCommand))]
    public partial NoteNodeViewModel? Selected { get; set; }

    public bool HasSelection => Selected is not null;

    /// <summary>
    /// The selection as the tree sees it. The tree clears its selection on a Ctrl or ⌘ click; that
    /// is ignored, so a page stays selected while there are pages. A page taken out of the tree
    /// does lose the selection, and whatever took it out selects another.
    /// </summary>
    public NoteNodeViewModel? TreeSelection
    {
        get => Selected;
        set
        {
            if (value is null && Selected is { } current && AllNodes().Contains(current))
                return;
            Selected = value;
        }
    }

    /// <summary>The selected page's title.</summary>
    public string PageTitle
    {
        get => Selected?.Title ?? "";
        set
        {
            if (Selected is null || Selected.Title == value)
                return;
            Selected.Title = value;
            _indexDirty = true;
            ScheduleSave();
            OnPropertyChanged();
            OnPropertyChanged(nameof(Path));
        }
    }

    /// <summary>The selected page's Markdown.</summary>
    public string Text
    {
        get => Selected?.Text ?? "";
        set
        {
            if (Selected is null || Selected.Text == value)
                return;
            Selected.Text = value;
            Selected.IsDirty = true;
            ScheduleSave();
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsEmptyView));
        }
    }

    /// <summary>The pages the selected page sits below, such as "Projects / Joktan"; empty at the top level.</summary>
    public string Path => Selected is null ? "" : string.Join(" / ", Selected.SelfAndAncestors().SkipLast(1).Select(n => n.DisplayTitle));

    /// <summary>
    /// Whether the selected page is being edited: its title and Markdown can be changed, with the
    /// formatted page beside them. Otherwise only the formatted page is shown.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsViewing), nameof(PreviewColumn), nameof(PreviewColumnSpan), nameof(IsEmptyView))]
    public partial bool IsEditing { get; set; }

    public bool IsViewing => !IsEditing;

    // Beside the editor while editing; the whole width while viewing.
    public int PreviewColumn => IsEditing ? 2 : 0;

    public int PreviewColumnSpan => IsEditing ? 1 : 3;

    /// <summary>Whether the page being viewed has nothing written on it yet.</summary>
    public bool IsEmptyView => IsViewing && string.IsNullOrWhiteSpace(Text);

    [ObservableProperty]
    public partial string? Error { get; set; }

    [ObservableProperty]
    public partial bool IsConfirmingDelete { get; set; }

    public string DeleteText
    {
        get
        {
            if (Selected is null)
                return "";
            var below = Selected.SelfAndDescendants().Count() - 1;
            var rest = below switch
            {
                0 => "",
                1 => " and the page below it",
                _ => $" and the {below} pages below it",
            };
            return $"Delete {Selected.DisplayTitle}{rest}? This cannot be undone.";
        }
    }

    /// <summary>The words to mark on the page, when search led to it. Choosing another page or editing ends it.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<string>? HighlightWords { get; set; }

    /// <summary>Shows this page in the app. Set by the app.</summary>
    public Action? OpenPage { get; set; }

    /// <summary>
    /// The pages with all the words in their title or Markdown, for the search box of the top bar. Those
    /// named by the words come first. A page not yet opened is read from its file, and kept.
    /// </summary>
    public IEnumerable<SearchHit> Search(IReadOnlyList<string> words)
    {
        var found = new List<NoteNodeViewModel>();
        foreach (var node in AllNodes())
        {
            node.Text ??= _store.ReadText(node.Id);
            if (TextSearch.HasAll(words, node.DisplayTitle, node.Text))
                found.Add(node);
        }

        return found.OrderBy(n => TextSearch.HasAll(words, n.DisplayTitle) ? 0 : 1).Select(node => new SearchHit("Notes", node.DisplayTitle, SnippetOf(node, words), () =>
        {
            OpenPage?.Invoke();
            Selected = node;
            IsEditing = false;
            HighlightWords = words;
        }));
    }

    private static string SnippetOf(NoteNodeViewModel node, IReadOnlyList<string> words)
    {
        var text = TextSearch.Snippet(TextSearch.Lines(node.Text ?? ""), words);
        var above = string.Join(" / ", node.SelfAndAncestors().SkipLast(1).Select(n => n.DisplayTitle));
        return above.Length > 0 ? $"{above} · {text}" : text;
    }

    /// <summary>Writes what has not been written yet and waits for it. Called when the app exits.</summary>
    public void SaveState()
    {
        Save();
        _store.FlushAsync().Wait(TimeSpan.FromSeconds(5));
    }

    [RelayCommand]
    private void ToggleFolder()
    {
        IsChoosingFolder = !IsChoosingFolder;
        FolderText = Folder;
        FolderMessage = null;
    }

    [RelayCommand]
    private async Task BrowseFolderAsync()
    {
        if (PickFolder is not null && await PickFolder() is { } folder)
            FolderText = folder;
    }

    [RelayCommand]
    private void UseDefaultFolder() => FolderText = DefaultFolder;

    /// <summary>
    /// Keeps notes in the folder typed or picked. A folder that already holds notes is opened as it
    /// is; otherwise the notes open now are copied into it, and stay in the old folder as well.
    /// </summary>
    [RelayCommand]
    private async Task ApplyFolderAsync()
    {
        FolderMessage = null;
        var folder = FolderText.Trim();
        if (folder.Length == 0)
            folder = DefaultFolder;
        if (folder.StartsWith('~'))
            folder = System.IO.Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), folder[1..].TrimStart('/', '\\'));
        if (!System.IO.Path.IsPathFullyQualified(folder))
        {
            FolderMessage = "Give the whole path of the folder, such as one picked with Browse.";
            return;
        }

        folder = System.IO.Path.GetFullPath(folder);
        if (NoteStore.SameFolder(folder, Folder))
        {
            IsChoosingFolder = false;
            return;
        }

        Save();
        var copied = !NoteStore.HasNotes(folder);
        try
        {
            if (copied)
                await _store.CopyToAsync(folder);
            else
                await _store.FlushAsync();
        }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
        {
            FolderMessage = $"Could not copy the notes to {folder}: {e.Message}";
            return;
        }

        var previous = Folder;
        NoteStore.SaveChosenFolder(folder);
        Open(NoteStore.Load(folder));
        IsChoosingFolder = false;
        Message = copied
            ? $"The notes were copied to {folder}. The originals are still in {previous}."
            : $"Opened the notes already in {folder}.";
    }

    /// <summary>What happened last, such as where the notes were copied to.</summary>
    [ObservableProperty]
    public partial string? Message { get; set; }

    private void Open(NoteStore store)
    {
        _store.Failed -= OnWriteFailed;
        _store = store;
        _store.Failed += OnWriteFailed;
        Error = null;
        Message = null;

        Selected = null;
        Roots.Clear();
        foreach (var page in store.Pages)
            Roots.Add(NoteNodeViewModel.FromPage(page, null));
        foreach (var node in AllNodes())
            Track(node);

        if (store.IsNew)
        {
            var welcome = new NoteNodeViewModel(NotePage.NewId(), "Welcome", null) { Text = WelcomeText, IsDirty = true };
            Roots.Add(welcome);
            Selected = welcome;
            _indexDirty = true;
            Save();
        }
        else
        {
            Selected = AllNodes().FirstOrDefault(n => n.Id == store.Selected) ?? Roots.FirstOrDefault();
        }

        OnPropertyChanged(nameof(Folder));
        OnPropertyChanged(nameof(IsDefaultFolder));
        StructureChanged();
        _indexDirty = false;
        _saveTimer.Stop();
    }

    // Which pages are expanded is kept in the index, so the tree looks the same next time.
    private void Track(NoteNodeViewModel node) => node.PropertyChanged += (_, e) =>
    {
        if (e.PropertyName != nameof(NoteNodeViewModel.IsExpanded))
            return;
        _indexDirty = true;
        ScheduleSave();
    };

    private void OnWriteFailed(string path, Exception e)
        => Dispatcher.UIThread.Post(() => Error = $"Could not write {path}: {e.Message}");

    partial void OnIsEditingChanged(bool value)
    {
        if (value)
            HighlightWords = null;
    }

    partial void OnSelectedChanged(NoteNodeViewModel? value)
    {
        HighlightWords = null;
        IsConfirmingDelete = false;
        IsEditing = false;
        NotifyMoveCommands();
        if (value is null)
            return;

        value.Text ??= _store.ReadText(value.Id);
        foreach (var above in value.SelfAndAncestors().SkipLast(1))
            above.IsExpanded = true;
        if (_store.Selected != value.Id)
        {
            _indexDirty = true;
            ScheduleSave();
        }
    }

    [RelayCommand]
    private void Edit() => IsEditing = true;

    [RelayCommand]
    private void View() => IsEditing = false;

    [RelayCommand]
    private void ToggleEditing() => IsEditing = !IsEditing;

    [RelayCommand]
    private void NewPage() => Add(null);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void NewChildPage() => Add(Selected);

    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void MoveUp() => Move(Selected!, -1);

    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private void MoveDown() => Move(Selected!, 1);

    /// <summary>Moves the selected page below the page above it, as its last child.</summary>
    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void Indent()
    {
        var node = Selected!;
        var siblings = Siblings(node);
        var newParent = siblings[siblings.IndexOf(node) - 1];
        Reparent(node, newParent, newParent.Children.Count);
        newParent.IsExpanded = true;
    }

    /// <summary>Moves the selected page out of its parent, to just after it.</summary>
    [RelayCommand(CanExecute = nameof(CanOutdent))]
    private void Outdent()
    {
        var node = Selected!;
        var parent = node.Parent!;
        Reparent(node, parent.Parent, Siblings(parent).IndexOf(parent) + 1);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Delete() => IsConfirmingDelete = true;

    [RelayCommand]
    private void CancelDelete() => IsConfirmingDelete = false;

    [RelayCommand]
    private void ConfirmDelete()
    {
        IsConfirmingDelete = false;
        if (Selected is not { } node)
            return;

        var siblings = Siblings(node);
        var index = siblings.IndexOf(node);
        var removed = node.SelfAndDescendants().ToList();
        siblings.Remove(node);
        _store.DeleteText(removed.Select(n => n.Id));

        // The page after it, else the one before, else its parent.
        Selected = index < siblings.Count ? siblings[index]
            : index > 0 ? siblings[index - 1]
            : node.Parent;
        StructureChanged();
    }

    private bool CanMoveUp() => Selected is { } node && Siblings(node).IndexOf(node) > 0;

    private bool CanMoveDown() => Selected is { } node && Siblings(node).IndexOf(node) < Siblings(node).Count - 1;

    private bool CanOutdent() => Selected?.Parent is not null;

    private void Add(NoteNodeViewModel? parent)
    {
        var node = new NoteNodeViewModel(NotePage.NewId(), "", parent) { Text = "", IsDirty = true };
        Track(node);
        (parent?.Children ?? Roots).Add(node);
        if (parent is not null)
            parent.IsExpanded = true;
        Selected = node;
        IsEditing = true;
        StructureChanged();
    }

    private void Move(NoteNodeViewModel node, int offset)
    {
        var editing = IsEditing;
        var siblings = Siblings(node);
        var index = siblings.IndexOf(node);
        siblings.Move(index, index + offset);
        Selected = node;
        IsEditing = editing;
        StructureChanged();
    }

    private void Reparent(NoteNodeViewModel node, NoteNodeViewModel? parent, int index)
    {
        // Taking the page out of the tree clears the selection, which would end editing.
        var editing = IsEditing;
        Siblings(node).Remove(node);
        node.Parent = parent;
        (parent?.Children ?? Roots).Insert(index, node);
        Selected = node;
        IsEditing = editing;
        OnPropertyChanged(nameof(Path));
        StructureChanged();
    }

    private ObservableCollection<NoteNodeViewModel> Siblings(NoteNodeViewModel node) => node.Parent?.Children ?? Roots;

    private IEnumerable<NoteNodeViewModel> AllNodes() => Roots.SelectMany(r => r.SelfAndDescendants());

    private void StructureChanged()
    {
        _indexDirty = true;
        ScheduleSave();
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(DeleteText));
        NotifyMoveCommands();
    }

    private void NotifyMoveCommands()
    {
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
        IndentCommand.NotifyCanExecuteChanged();
        OutdentCommand.NotifyCanExecuteChanged();
    }

    private void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void Save()
    {
        _saveTimer.Stop();
        foreach (var node in AllNodes().Where(n => n.IsDirty))
        {
            node.IsDirty = false;
            _store.WriteText(node.Id, node.Text ?? "");
        }

        if (_indexDirty)
        {
            _indexDirty = false;
            _store.SaveIndex(Roots.Select(r => r.ToPage()).ToList(), Selected?.Id ?? "");
        }
    }
}
