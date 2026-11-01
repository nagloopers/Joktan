using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Api.Resources;
using Joktan.Core;
using Joktan.Core.ViewModels;

namespace Joktan.Api.ViewModels;

/// <summary>
/// Dev Tools / API Client: REST and gRPC requests kept as files in a folder, sent with the
/// variables of an environment. Specified in docs/features/dev-tools/api-client/spec.md.
/// </summary>
public partial class ApiClientViewModel : PageViewModel
{
    public static string NoEnvironment => Strings.ApiClient_NoEnvironment;

    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(500);

    private readonly DispatcherTimer _environmentsTimer;
    private ApiWorkspace _workspace;
    private ApiEnvironments _environments = new();
    private ApiNodeViewModel _root;
    private bool _loadingEnvironments;

    public ApiClientViewModel() : this(new ApiWorkspace(ApiWorkspace.ChosenFolder))
    {
    }

    public ApiClientViewModel(ApiWorkspace workspace) : base(Strings.ApiClient_APIClient, Strings.ApiClient_DevToolsAPIClient)
    {
        Environments.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasEnvironments));
        _workspace = workspace;
        _root = new ApiNodeViewModel(workspace.Folder, true, null);
        _environmentsTimer = new DispatcherTimer { Interval = SaveDelay };
        _environmentsTimer.Tick += (_, _) => SaveEnvironments();
        Open(workspace, State.LastRequest);
    }

    /// <summary>Asks for .proto files, or a folder. Set by the view.</summary>
    public Func<bool, Task<IReadOnlyList<string>>>? PickFiles { get; set; }

    /// <summary>Copies text to the clipboard. Set by the view.</summary>
    public Func<string, Task>? CopyText { get; set; }

    [ObservableProperty]
    public partial string? Error { get; set; }

    /// <summary>What happened last, such as where the workspace was copied to.</summary>
    [ObservableProperty]
    public partial string? Message { get; set; }

    // ---- The tree ----

    public ObservableCollection<ApiNodeViewModel> Roots => _root.Children;

    public bool IsEmpty => Roots.Count == 0;

    public string Folder => _workspace.Folder;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TreeSelection), nameof(HasSelection), nameof(IsFolderSelected), nameof(DeleteText), nameof(FolderSummary))]
    public partial ApiNodeViewModel? Selected { get; set; }

    /// <summary>The selection as the tree sees it; a Ctrl or ⌘ click does not clear it while the node is still there.</summary>
    public ApiNodeViewModel? TreeSelection
    {
        get => Selected;
        set
        {
            if (value is null && Selected is { } current && _root.SelfAndDescendants().Contains(current))
                return;
            Selected = value;
        }
    }

    public bool HasSelection => Selected is not null;

    public bool IsFolderSelected => Selected?.IsFolder == true;

    /// <summary>The request selected, being edited.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRequest))]
    public partial ApiRequestViewModel? Request { get; set; }

    public bool HasRequest => Request is not null;

    /// <summary>The selected item's name, renamed when the change is committed.</summary>
    [ObservableProperty]
    public partial string EditName { get; set; } = "";

    public ObservableCollection<ApiFolderChoice> FolderChoices { get; } = [];

    /// <summary>The folder the selected item is in; choosing another moves it there.</summary>
    [ObservableProperty]
    public partial ApiFolderChoice? EditFolder { get; set; }

    private bool _choosingFolder;

    public string FolderSummary
    {
        get
        {
            if (Selected is not { IsFolder: true } folder)
                return "";
            var count = folder.SelfAndDescendants().Count(n => n.IsRequest);
            return count == 1 ? Strings.ApiClient_1Request : string.Format(Strings.ApiClient_Requests, count);
        }
    }

    partial void OnSelectedChanging(ApiNodeViewModel? value) => Request?.Save();

    partial void OnSelectedChanged(ApiNodeViewModel? value)
    {
        IsConfirmingDelete = false;
        Error = null;
        Request = value is { IsRequest: true } ? MakeRequest(value) : null;
        EditName = value?.Name ?? "";

        _choosingFolder = true;
        FolderChoices.Clear();
        if (value is not null)
        {
            // A folder cannot go inside itself.
            var excluded = value.IsFolder ? value.SelfAndDescendants().ToHashSet() : [];
            FolderChoices.Add(new ApiFolderChoice(_root, Strings.ApiClient_TopLevel));
            foreach (var folder in _root.SelfAndDescendants().Where(n => n.IsFolder && n != _root && !excluded.Contains(n)))
                FolderChoices.Add(new ApiFolderChoice(folder, new string(' ', 4 * folder.Ancestors().Count()) + folder.Name));
            EditFolder = FolderChoices.FirstOrDefault(c => c.Node == value.Parent);
            foreach (var above in value.Ancestors())
                above.IsExpanded = true;
        }

        _choosingFolder = false;
        if (value is { IsRequest: true })
            SaveState(value.Path);
    }

    partial void OnEditFolderChanged(ApiFolderChoice? value)
    {
        if (!_choosingFolder && value is not null && Selected is { } node && value.Node != node.Parent)
            Move(node, value.Node);
    }

    private ApiRequestViewModel MakeRequest(ApiNodeViewModel node)
    {
        // The view sets its helpers after the page is made, so they are looked up when used.
        return new ApiRequestViewModel(node.Path, Variables, SaveVariables, Describe)
        {
            PickFiles = folder => PickFiles?.Invoke(folder) ?? Task.FromResult<IReadOnlyList<string>>([]),
            CopyText = text => CopyText?.Invoke(text) ?? Task.CompletedTask,
        };
    }

    private void Describe(ApiRequestViewModel request)
    {
        if (_root.SelfAndDescendants().FirstOrDefault(n => n.Path == request.Path) is { } node)
            node.Describe(request.ToRequest());
    }

    [RelayCommand]
    private void NewRestRequest() => NewRequest(ApiKind.Rest);

    [RelayCommand]
    private void NewGrpcRequest() => NewRequest(ApiKind.Grpc);

    private void NewRequest(ApiKind kind)
    {
        Request?.Save();
        var folder = TargetFolder();
        var path = ApiWorkspace.FreePath(folder, kind == ApiKind.Rest ? Strings.ApiClient_NewRequest : Strings.ApiClient_NewGRPCRequest, ApiWorkspace.RequestExtension);
        var request = kind == ApiKind.Rest
            ? new ApiRequest { Url = "{{baseUrl}}/" }
            : new ApiRequest { Kind = ApiKind.Grpc, Address = "{{grpcAddress}}" };
        if (Run(() => ApiWorkspace.Write(path, request)))
            Reload(path);
    }

    [RelayCommand]
    private void NewFolder()
    {
        Request?.Save();
        var path = ApiWorkspace.FreePath(TargetFolder(), Strings.ApiClient_NewFolder, "");
        if (Run(() => Directory.CreateDirectory(path)))
            Reload(path);
    }

    [RelayCommand]
    private void Duplicate()
    {
        if (Selected is not { IsRequest: true } node)
            return;
        Request?.Save();
        var path = ApiWorkspace.FreePath(Path.GetDirectoryName(node.Path)!, node.Name + Strings.ApiClient_Copy, ApiWorkspace.RequestExtension);
        if (Run(() => File.Copy(node.Path, path)))
            Reload(path);
    }

    /// <summary>Renames the selected request or folder to the name typed.</summary>
    [RelayCommand]
    private void Rename()
    {
        if (Selected is not { } node || EditName.Trim() == node.Name)
            return;
        Request?.Save();
        var folder = Path.GetDirectoryName(node.Path)!;
        var target = ApiWorkspace.FreePath(folder, EditName, node.IsFolder ? "" : ApiWorkspace.RequestExtension);
        if (Run(() =>
            {
                if (node.IsFolder)
                    Directory.Move(node.Path, target);
                else
                    File.Move(node.Path, target);
                ApiLastSent.Moved(node.Path, target);
            }))
            Reload(target);
        else
            EditName = node.Name;
    }

    [RelayCommand]
    private void Refresh()
    {
        Request?.Save();
        Reload(Selected?.Path);
        LoadEnvironments();
    }

    private void Move(ApiNodeViewModel node, ApiNodeViewModel folder)
    {
        Request?.Save();
        var target = ApiWorkspace.FreePath(folder.Path, node.Name, node.IsFolder ? "" : ApiWorkspace.RequestExtension);
        if (Run(() =>
            {
                if (node.IsFolder)
                    Directory.Move(node.Path, target);
                else
                    File.Move(node.Path, target);
                ApiLastSent.Moved(node.Path, target);
            }))
            Reload(target);
    }

    // New things go in the selected folder, or the folder of the selected request.
    private string TargetFolder() => Selected switch
    {
        { IsFolder: true } folder => folder.Path,
        { } request => Path.GetDirectoryName(request.Path)!,
        _ => _workspace.Folder,
    };

    /// <summary>Reads the tree from the folder again, keeping what was collapsed, and selects a path.</summary>
    private void Reload(string? select)
    {
        var collapsed = _root.SelfAndDescendants().Where(n => n.IsFolder && !n.IsExpanded).Select(n => n.Path).ToHashSet();
        _root = new ApiNodeViewModel(_workspace.Folder, true, null);
        Fill(_root);
        OnPropertyChanged(nameof(Roots));
        OnPropertyChanged(nameof(IsEmpty));

        var nodes = _root.SelfAndDescendants().ToList();
        Selected = null;
        Selected = nodes.FirstOrDefault(n => n != _root && n.Path == select)
                   ?? nodes.FirstOrDefault(n => n.IsRequest)
                   ?? Roots.FirstOrDefault();

        void Fill(ApiNodeViewModel folder)
        {
            foreach (var path in _workspace.Folders(folder.Path))
            {
                var child = new ApiNodeViewModel(path, true, folder) { IsExpanded = !collapsed.Contains(path) };
                folder.Children.Add(child);
                Fill(child);
            }

            foreach (var path in _workspace.Requests(folder.Path))
            {
                var child = new ApiNodeViewModel(path, false, folder);
                child.Describe(ApiWorkspace.Read(path));
                folder.Children.Add(child);
            }
        }
    }

    // ---- Deleting ----

    [ObservableProperty]
    public partial bool IsConfirmingDelete { get; set; }

    public string DeleteText => Selected switch
    {
        { IsFolder: true } folder => string.Format(Strings.ApiClient_DeleteTheFolderAndTheIn, folder.Name, FolderSummary),
        { } request => string.Format(Strings.ApiClient_DeleteItsFileIsDeleted, request.Name),
        _ => "",
    };

    [RelayCommand]
    private void Delete()
    {
        if (HasSelection)
            IsConfirmingDelete = true;
    }

    [RelayCommand]
    private void CancelDelete() => IsConfirmingDelete = false;

    [RelayCommand]
    private void ConfirmDelete()
    {
        IsConfirmingDelete = false;
        if (Selected is not { } node)
            return;
        var siblings = node.Parent!.Children;
        var index = siblings.IndexOf(node);
        var next = index + 1 < siblings.Count ? siblings[index + 1] : index > 0 ? siblings[index - 1] : node.Parent;
        Request = null;
        if (Run(() =>
            {
                if (node.IsFolder)
                    Directory.Delete(node.Path, recursive: true);
                else
                    File.Delete(node.Path);
                ApiLastSent.Forget(node.Path);
            }))
            Reload(next?.Path);
    }

    // ---- Environments ----

    /// <summary>"No environment" and the environment names, to choose the one in use.</summary>
    public ObservableCollection<string> EnvironmentChoices { get; } = [];

    [ObservableProperty]
    public partial string SelectedEnvironment { get; set; } = NoEnvironment;

    [ObservableProperty]
    public partial bool IsEditingEnvironments { get; set; }

    /// <summary>The environments being edited, each with its variables.</summary>
    public ObservableCollection<ApiEnvironmentViewModel> Environments { get; } = [];

    public bool HasEnvironments => Environments.Count > 0;

    [ObservableProperty]
    public partial ApiEnvironmentViewModel? EditedEnvironment { get; set; }

    partial void OnSelectedEnvironmentChanged(string value)
    {
        // The list of choices is rebuilt when an environment is renamed, which clears it for a moment.
        if (value is null)
        {
            if (!_loadingEnvironments)
                SelectedEnvironment = NoEnvironment;
            return;
        }

        if (_loadingEnvironments)
            return;
        _environments.Selected = value == NoEnvironment ? "" : value;
        ScheduleEnvironmentsSave();
    }

    [RelayCommand]
    private void ToggleEnvironments()
    {
        IsEditingEnvironments = !IsEditingEnvironments;
        IsChoosingWorkspace = false;
        EditedEnvironment ??= Environments.FirstOrDefault(e => e.Name == SelectedEnvironment) ?? Environments.FirstOrDefault();
    }

    [RelayCommand]
    private void NewEnvironment()
    {
        var name = Strings.ApiClient_Environment;
        for (var n = 2; Environments.Any(e => e.Name == name); n++)
            name = string.Format(Strings.ApiClient_Environment2, n);
        var environment = new ApiEnvironmentViewModel(new ApiEnvironment
        {
            Name = name,
            Variables =
            [
                new ApiPair { Name = "baseUrl", Value = "http://localhost:5000" },
                new ApiPair { Name = "grpcAddress", Value = "http://localhost:5001" },
            ],
        }, EnvironmentsChanged);
        Environments.Add(environment);
        EditedEnvironment = environment;
        EnvironmentsChanged();
    }

    [RelayCommand]
    private void DeleteEnvironment()
    {
        if (EditedEnvironment is not { } environment)
            return;
        Environments.Remove(environment);
        EditedEnvironment = Environments.FirstOrDefault();
        EnvironmentsChanged();
    }

    private void EnvironmentsChanged()
    {
        if (_loadingEnvironments)
            return;
        var selected = SelectedEnvironment;
        _environments.Environments = Environments.Select(e => e.ToEnvironment()).ToList();

        // Keep the choices in step with the names, and the one in use if it was renamed.
        _loadingEnvironments = true;
        var names = Environments.Select(e => e.Name).Prepend(NoEnvironment).ToList();
        EnvironmentChoices.Clear();
        foreach (var name in names)
            EnvironmentChoices.Add(name);
        _loadingEnvironments = false;
        SelectedEnvironment = names.Contains(selected) ? selected : NoEnvironment;
        _environments.Selected = SelectedEnvironment == NoEnvironment ? "" : SelectedEnvironment;
        ScheduleEnvironmentsSave();
    }

    private void LoadEnvironments()
    {
        _loadingEnvironments = true;
        _environments = _workspace.ReadEnvironments();
        Environments.Clear();
        foreach (var environment in _environments.Environments)
            Environments.Add(new ApiEnvironmentViewModel(environment, EnvironmentsChanged));
        EnvironmentChoices.Clear();
        EnvironmentChoices.Add(NoEnvironment);
        foreach (var environment in Environments)
            EnvironmentChoices.Add(environment.Name);
        SelectedEnvironment = EnvironmentChoices.Contains(_environments.Selected) ? _environments.Selected : NoEnvironment;
        EditedEnvironment = Environments.FirstOrDefault(e => e.Name == SelectedEnvironment) ?? Environments.FirstOrDefault();
        _loadingEnvironments = false;
    }

    private ApiVariables Variables()
        => Environments.FirstOrDefault(e => e.Name == SelectedEnvironment) is { } environment
            ? new ApiVariables(environment.Variables.ToPairs())
            : ApiVariables.None;

    // Sets the variables in the environment in use, adding those it lacks, and gives its name.
    private string? SaveVariables(IReadOnlyList<ApiPair> values)
    {
        if (Environments.FirstOrDefault(e => e.Name == SelectedEnvironment) is not { } environment)
            return null;
        foreach (var value in values)
        {
            // The last enabled one with the name is the one requests use.
            var named = environment.Variables.Where(v => v.Name.Trim() == value.Name).ToList();
            if ((named.LastOrDefault(v => v.Enabled) ?? named.LastOrDefault()) is { } variable)
            {
                variable.Value = value.Value;
                variable.Enabled = true;
            }
            else
            {
                environment.Variables.Add(new ApiPairViewModel(environment.Variables, value));
            }
        }

        return environment.Name;
    }

    private void ScheduleEnvironmentsSave()
    {
        _environmentsTimer.Stop();
        _environmentsTimer.Start();
    }

    private void SaveEnvironments()
    {
        _environmentsTimer.Stop();
        Run(() => _workspace.WriteEnvironments(_environments));
    }

    // ---- Workspace folder ----

    [ObservableProperty]
    public partial bool IsChoosingWorkspace { get; set; }

    [ObservableProperty]
    public partial string WorkspaceText { get; set; } = "";

    [ObservableProperty]
    public partial string? WorkspaceMessage { get; set; }

    public string DefaultFolder => ApiWorkspace.DefaultFolder;

    [RelayCommand]
    private void ToggleWorkspace()
    {
        IsChoosingWorkspace = !IsChoosingWorkspace;
        IsEditingEnvironments = false;
        WorkspaceText = Folder;
        WorkspaceMessage = null;
    }

    [RelayCommand]
    private async Task BrowseWorkspaceAsync()
    {
        if (PickFiles is not null && (await PickFiles(true)).FirstOrDefault() is { } folder)
            WorkspaceText = folder;
    }

    [RelayCommand]
    private void UseDefaultWorkspace() => WorkspaceText = DefaultFolder;

    /// <summary>
    /// Keeps the requests in the folder typed or picked. A folder that already holds requests is
    /// opened as it is; otherwise the workspace open now is copied into it.
    /// </summary>
    [RelayCommand]
    private void ApplyWorkspace()
    {
        WorkspaceMessage = null;
        var folder = WorkspaceText.Trim();
        if (folder.Length == 0)
            folder = DefaultFolder;
        if (folder.StartsWith('~'))
            folder = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), folder[1..].TrimStart('/', '\\'));
        if (!Path.IsPathFullyQualified(folder))
        {
            WorkspaceMessage = Strings.ApiClient_GiveTheWholePathOfThe;
            return;
        }

        folder = Path.GetFullPath(folder);
        if (ApiWorkspace.SameFolder(folder, Folder))
        {
            IsChoosingWorkspace = false;
            return;
        }

        Request?.Save();
        SaveEnvironments();
        var copied = !ApiWorkspace.HasContent(folder) && Directory.Exists(Folder);
        try
        {
            Directory.CreateDirectory(folder);
            if (copied)
                _workspace.CopyTo(folder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            WorkspaceMessage = string.Format(Strings.ApiClient_CouldNotUse, folder, e.Message);
            return;
        }

        var previous = Folder;
        ApiWorkspace.SaveChosenFolder(folder);
        Open(new ApiWorkspace(folder), null);
        IsChoosingWorkspace = false;
        Message = copied
            ? string.Format(Strings.ApiClient_TheRequestsWereCopiedToThe, folder, previous)
            : string.Format(Strings.ApiClient_OpenedTheRequestsIn, folder);
    }

    private void Open(ApiWorkspace workspace, string? select)
    {
        _workspace = workspace;
        OnPropertyChanged(nameof(Folder));
        LoadEnvironments();
        Reload(select);
    }

    /// <summary>Writes what is waiting. Called when the app exits.</summary>
    public void SaveState()
    {
        Request?.Save();
        if (_environmentsTimer.IsEnabled)
            SaveEnvironments();
    }

    // ---- Remembering the request open last ----

    private static string StatePath => Path.Combine(DataFolder.Path, "api-state.json");

    private static ApiState State
    {
        get
        {
            try
            {
                if (File.Exists(StatePath))
                    return JsonSerializer.Deserialize<ApiState>(File.ReadAllText(StatePath)) ?? new ApiState();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
            {
                // The first request is opened instead.
            }

            return new ApiState();
        }
    }

    private static void SaveState(string lastRequest)
    {
        try
        {
            Directory.CreateDirectory(DataFolder.Path);
            File.WriteAllText(StatePath, JsonSerializer.Serialize(new ApiState { LastRequest = lastRequest }, JsonFormats.Indented));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Not remembering it only means the first request opens next time.
        }
    }

    private sealed class ApiState
    {
        public string LastRequest { get; set; } = "";
    }

    private bool Run(Action action)
    {
        try
        {
            action();
            Error = null;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Error = e.Message;
            return false;
        }
    }
}

/// <summary>An environment being edited.</summary>
public partial class ApiEnvironmentViewModel : ViewModelBase
{
    private readonly Action _changed;

    public ApiEnvironmentViewModel(ApiEnvironment environment, Action changed)
    {
        Name = environment.Name;
        _changed = changed;
        Variables = new ApiPairList(environment.Variables, changed);
    }

    [ObservableProperty]
    public partial string Name { get; set; }

    partial void OnNameChanged(string value) => _changed?.Invoke();

    public ApiPairList Variables { get; }

    public ApiEnvironment ToEnvironment() => new() { Name = Name.Trim(), Variables = Variables.ToPairs() };

    public override string ToString() => Name;
}
