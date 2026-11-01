using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Containers.Resources;
using Joktan.Core.ViewModels;

namespace Joktan.Containers.ViewModels;

/// <summary>
/// Dev Tools / Containers: starts, stops and takes down compose projects with Docker or Podman,
/// one per compose file, and shows their containers' logs. Specified in
/// docs/features/dev-tools/containers/spec.md.
/// </summary>
public partial class ContainersViewModel : PageViewModel
{
    public static string AllServices => Strings.Containers_AllServices;

    private const int MaxLogLines = 5000;
    private const int MaxOutputLines = 1000;

    private readonly ComposeSettings _settings;
    private readonly string? _dataFolder;
    private readonly ConcurrentQueue<string> _incomingLogs = new();
    private readonly ConcurrentQueue<string> _incomingOutput = new();
    private readonly DispatcherTimer _flushTimer;
    private readonly DispatcherTimer _statusTimer;
    private Task<ComposeTool>? _engine;
    private CancellationTokenSource? _command;
    private CancellationTokenSource? _logs;
    private string? _statusError;

    // The logs are paused while a command runs on their file, since a recreated container is not
    // followed, and continue afterwards from when they paused. They also continue after such a
    // command when they ended because every container stopped.
    private bool _wantLogs;
    private DateTimeOffset? _logsEndedAt;

    public ContainersViewModel(string? dataFolder = null)
        : base(Strings.Containers_Containers, Strings.Containers_DevToolsContainers)
    {
        _dataFolder = dataFolder;
        _settings = ComposeSettings.Load(dataFolder);
        SelectedEngine = _settings.Engine;
        IsLogPanelOpen = !_settings.LogPanelCollapsed;
        Projects.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasProjects));

        // Until files are chosen, the repository's is shown.
        var files = _settings.ComposeFiles ?? (DefaultComposeFile is { } file ? [file] : []);
        foreach (var path in files)
            Projects.Add(new ComposeProjectViewModel(Path.GetFullPath(path), this));
        SyncLogChoices();
        SelectedLog = LogChoices.FirstOrDefault();

        _flushTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) => Flush());
        _statusTimer = new DispatcherTimer(TimeSpan.FromSeconds(5), DispatcherPriority.Background, (_, _) => _ = RefreshFromTimerAsync());
    }

    public IReadOnlyList<ContainerEngine> Engines { get; } = Enum.GetValues<ContainerEngine>();

    public string? DefaultComposeFile { get; } = ComposeSettings.FindRepositoryComposeFile();

    /// <summary>The compose files, each its own project.</summary>
    public ObservableCollection<ComposeProjectViewModel> Projects { get; } = [];

    public bool HasProjects => Projects.Count > 0;

    /// <summary>The path typed, to add with Add.</summary>
    [ObservableProperty]
    public partial string NewComposeFile { get; set; } = "";

    [ObservableProperty]
    public partial ContainerEngine SelectedEngine { get; set; }

    /// <summary>Which engine and command are used, once known.</summary>
    [ObservableProperty]
    public partial string? EngineText { get; set; }

    /// <summary>Each file's services, for the logs.</summary>
    public ObservableCollection<ComposeLogChoice> LogChoices { get; } = [];

    [ObservableProperty]
    public partial string Summary { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    public partial bool IsBusy { get; set; }

    /// <summary>What is running now, such as "Starting broker".</summary>
    [ObservableProperty]
    public partial string? Activity { get; set; }

    /// <summary>A problem with no one file: finding the engine, adding a file or saving.</summary>
    [ObservableProperty]
    public partial string? Error { get; set; }

    /// <summary>What the commands run from this page wrote, each after its command line.</summary>
    public ObservableCollection<string> Output { get; } = [];

    public ObservableCollection<string> LogLines { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LogsButtonText))]
    public partial bool IsFollowingLogs { get; set; }

    public string LogsButtonText => IsFollowingLogs ? Strings.Containers_Stop : Strings.Containers_ShowLogs;

    /// <summary>Whether the logs and command output are shown, rather than collapsed to a bar.</summary>
    [ObservableProperty]
    public partial bool IsLogPanelOpen { get; set; }

    [ObservableProperty]
    public partial ComposeLogChoice? SelectedLog { get; set; }

    [ObservableProperty]
    public partial bool ShowTimestamps { get; set; }

    /// <summary>How many earlier lines of each container's log to show first.</summary>
    [ObservableProperty]
    public partial int TailLines { get; set; } = 200;

    /// <summary>Whether the log list keeps the newest line in view.</summary>
    [ObservableProperty]
    public partial bool AutoScroll { get; set; } = true;

    /// <summary>Raised on the UI thread after lines are added to <see cref="LogLines"/>.</summary>
    public event Action? LogLinesAdded;

    /// <summary>Raised when the logs are asked for from a service's menu, so the view can show them.</summary>
    public event Action? LogsShown;

    /// <summary>Asks the user for compose files; empty when they cancel. Set by the view.</summary>
    public Func<Task<IReadOnlyList<string>>>? PickComposeFiles { get; set; }

    /// <summary>Starts checking the containers every few seconds. Called while the page is shown.</summary>
    public void Activate()
    {
        _statusTimer.Start();
        _ = RefreshAllAsync();
    }

    public void Deactivate() => _statusTimer.Stop();

    /// <summary>Stops the log stream and any running command. Called when the app exits.</summary>
    public void Shutdown()
    {
        _statusTimer.Stop();
        _logs?.Cancel();
        _command?.Cancel();
    }

    partial void OnSelectedEngineChanged(ContainerEngine value)
    {
        if (_settings.Engine == value)
            return;

        _settings.Engine = value;
        SaveSettings();
        _engine = null;
        StopLogs();
        foreach (var project in Projects)
            project.Reset();
        _ = RefreshAllAsync();
    }

    partial void OnIsLogPanelOpenChanged(bool value)
    {
        if (_settings.LogPanelCollapsed == !value)
            return;

        _settings.LogPanelCollapsed = !value;
        SaveSettings();
    }

    private void SaveSettings()
    {
        try
        {
            _settings.Save(_dataFolder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Error = string.Format(Strings.Containers_CouldNotSaveTheSettings, e.Message);
        }
    }

    private void SaveFiles()
    {
        _settings.ComposeFiles = Projects.Select(p => p.File).ToList();
        SaveSettings();
    }

    [RelayCommand]
    private async Task BrowseAsync()
    {
        if (PickComposeFiles is null)
            return;
        foreach (var file in await PickComposeFiles())
            AddFile(file);
    }

    [RelayCommand]
    private void AddTyped()
    {
        if (AddFile(NewComposeFile))
            NewComposeFile = "";
    }

    private bool AddFile(string text)
    {
        var path = text.Trim();
        if (path.Length == 0)
            return false;
        if (path.StartsWith('~'))
            path = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[1..].TrimStart('/', '\\'));
        path = Path.GetFullPath(path);

        if (!File.Exists(path))
        {
            Error = string.Format(Strings.Containers_DoesNotExist, path);
            return false;
        }

        var comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        if (Projects.Any(p => string.Equals(p.File, path, comparison)))
        {
            Error = string.Format(Strings.Containers_IsAlreadyOnTheList, path);
            return false;
        }

        Error = null;
        var project = new ComposeProjectViewModel(path, this);
        Projects.Add(project);
        SaveFiles();
        ProjectRead();
        SelectedLog ??= LogChoices.FirstOrDefault();
        _ = project.RefreshStatusAsync();
        return true;
    }

    /// <summary>Stops showing a file. Its containers are left as they are.</summary>
    public void RemoveProject(ComposeProjectViewModel project)
    {
        if (SelectedLog?.Project == project)
            StopLogs();
        Projects.Remove(project);
        SaveFiles();
        ProjectRead();
        if (SelectedLog?.Project == project)
            SelectedLog = LogChoices.FirstOrDefault();
    }

    /// <summary>Brings what is worked out from all the files up to date, after one has changed.</summary>
    public void ProjectRead()
    {
        SyncLogChoices();
        var total = Projects.Sum(p => p.Services.Count);
        var running = Projects.Sum(p => p.Services.Count(r => r.IsRunning));
        Summary = !HasProjects || total == 0 ? "" : string.Format(Strings.Containers_OfRunning, running, total);

        foreach (var project in Projects)
        {
            var same = Projects.FirstOrDefault(p => p != project && string.Equals(p.Name, project.Name, StringComparison.OrdinalIgnoreCase));
            project.NameWarning = same is null
                ? null
                : string.Format(Strings.Containers_HasTheSameProjectNameSo, same.File);
        }
    }

    // Changes the list without removing the choices it keeps, so a choice still there stays chosen.
    private void SyncLogChoices()
    {
        var wanted = Projects.SelectMany(p => p.Services.Select(r => r.Service).Distinct().Select(s => new ComposeLogChoice(p, s))
            .Prepend(new ComposeLogChoice(p, null))).ToList();
        for (var i = LogChoices.Count - 1; i >= 0; i--)
        {
            // A service that has gone stays while its logs are being shown.
            if (!wanted.Contains(LogChoices[i]) && (LogChoices[i] != SelectedLog || !Projects.Contains(LogChoices[i].Project)))
                LogChoices.RemoveAt(i);
        }

        for (var i = 0; i < wanted.Count; i++)
        {
            var at = LogChoices.IndexOf(wanted[i]);
            if (at < 0)
                LogChoices.Insert(Math.Min(i, LogChoices.Count), wanted[i]);
            else if (at != i && i < LogChoices.Count)
                LogChoices.Move(at, i);
        }
    }

    [RelayCommand]
    private Task RefreshAsync()
    {
        _engine = null;
        foreach (var project in Projects)
            project.ForgetName();
        return RefreshAllAsync();
    }

    private Task RefreshAllAsync() => Task.WhenAll(Projects.Select(p => p.RefreshStatusAsync()));

    private async Task RefreshFromTimerAsync()
    {
        try
        {
            await RefreshAllAsync();
        }
        catch (Exception e)
        {
            Error = string.Format(Strings.ComposeProject_CouldNotReadTheContainers, e.Message);
        }
    }

    /// <summary>Shows only this service's logs, starting them if they are not shown.</summary>
    public void ShowLogs(ComposeProjectViewModel project, string? service)
    {
        var wasFollowing = IsFollowingLogs;
        SelectedLog = LogChoices.FirstOrDefault(c => c.Project == project && c.Service == (string.IsNullOrEmpty(service) ? null : service))
                      ?? LogChoices.FirstOrDefault(c => c.Project == project && c.Service is null);
        if (!wasFollowing)
            ToggleLogs();
        IsLogPanelOpen = true;
        LogsShown?.Invoke();
    }

    [RelayCommand]
    private void ToggleLogPanel() => IsLogPanelOpen = !IsLogPanelOpen;

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Cancel() => _command?.Cancel();

    [RelayCommand]
    private void ClearOutput() => Output.Clear();

    [RelayCommand]
    private void ToggleLogs()
    {
        if (IsFollowingLogs)
        {
            _wantLogs = false;
            _logs?.Cancel();
            // While a command runs the logs are paused, so there is nothing to cancel.
            if (_logs is null)
                IsFollowingLogs = false;
        }
        else
        {
            _wantLogs = true;
            _ = FollowLogsAsync(resume: false);
        }
    }

    private void StopLogs()
    {
        _wantLogs = false;
        _logs?.Cancel();
        IsFollowingLogs = false;
    }

    [RelayCommand]
    private void ClearLogs() => LogLines.Clear();

    // Changing what the logs show starts them again with the new choice.
    partial void OnSelectedLogChanged(ComposeLogChoice? value)
    {
        // The list sets null while it is changed.
        if (value is null)
        {
            if (LogChoices.Count > 0)
                SelectedLog = LogChoices[0];
        }
        else
        {
            RestartLogs();
        }
    }

    partial void OnShowTimestampsChanged(bool value) => RestartLogs();

    private void RestartLogs()
    {
        if (!IsFollowingLogs)
            return;
        _logs?.Cancel();
        _ = FollowLogsAsync(resume: false);
    }

    /// <summary>The tool for a compose file. The engine is looked for once, however many files there are.</summary>
    public async Task<ComposeTool?> ToolAsync(string file)
    {
        try
        {
            _engine ??= ComposeTool.ResolveAsync(SelectedEngine, file);
            var tool = await _engine;
            EngineText = string.Format(Strings.Containers_ComposeWith, tool.EngineName, tool.Executable);
            if (Error is not null && Error == _statusError)
                Error = null;
            _statusError = null;
            return tool.For(file);
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _engine = null;
            EngineText = null;
            Error = _statusError = e.Message;
            return null;
        }
    }

    /// <param name="engine">Whether the arguments are for the engine itself rather than for compose.</param>
    public async Task RunCommandAsync(ComposeProjectViewModel project, string activity, string[] arguments, bool engine = false)
    {
        if (IsBusy || await ToolAsync(project.File) is not { } tool)
            return;

        IsBusy = true;
        Activity = $"{activity} {project.Name}";
        project.Error = null;
        _command = new CancellationTokenSource();
        var logsHere = SelectedLog?.Project == project;
        if (logsHere && _logs is { } logs)
        {
            // Cleared first, so the stream ending does not count as the user stopping it.
            _logs = null;
            logs.Cancel();
            _logsEndedAt = DateTimeOffset.UtcNow;
        }

        AddOutput("");
        var commandLine = engine ? tool.EngineCommandLine(arguments) : tool.CommandLine(arguments);
        AddOutput($"{project.Name} $ {commandLine}");
        try
        {
            var exitCode = engine
                ? await tool.RunEngineAsync(arguments, AddOutput, _command.Token)
                : await tool.RunAsync(arguments, AddOutput, _command.Token);
            if (exitCode != 0)
                project.Error = string.Format(Strings.Containers_FailedWithExitCodeSeeThe, commandLine, exitCode);
        }
        catch (OperationCanceledException)
        {
            AddOutput(Strings.Containers_Cancelled);
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            project.Error = e.Message;
        }
        finally
        {
            _command.Dispose();
            _command = null;
            Activity = null;
            IsBusy = false;
        }

        await project.RefreshStatusAsync();
        if (logsHere && _wantLogs && _logs is null)
            _ = FollowLogsAsync(resume: true);
    }

    private async Task FollowLogsAsync(bool resume)
    {
        if (SelectedLog is not { } choice)
        {
            _wantLogs = false;
            Error = Strings.Containers_AddAComposeFileToSee;
            return;
        }

        if (await ToolAsync(choice.Project.File) is not { } tool)
            return;

        _logs?.Cancel();
        var cancel = _logs = new CancellationTokenSource();
        List<string> arguments = ["logs", "--follow", "--no-color"];
        if (resume && _logsEndedAt is { } since)
            arguments.AddRange(["--since", since.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ")]);
        else
            arguments.AddRange(["--tail", Math.Max(0, TailLines).ToString()]);
        if (ShowTimestamps)
            arguments.Add("--timestamps");
        if (choice.Service is { } service)
            arguments.AddRange(["--no-log-prefix", service]);

        IsFollowingLogs = true;
        _flushTimer.Start();
        if (!resume)
        {
            _incomingLogs.Clear();
            LogLines.Clear();
            _incomingLogs.Enqueue($"── {choice.Project.Name} $ {tool.CommandLine(arguments)}");
        }

        try
        {
            var exitCode = await tool.RunAsync(arguments, _incomingLogs.Enqueue, cancel.Token);
            _logsEndedAt = DateTimeOffset.UtcNow;
            if (_logs == cancel)
            {
                _incomingLogs.Enqueue(exitCode == 0
                    ? Strings.Containers_TheLogsEndedBecauseNoContainer
                    : string.Format(Strings.Containers_TheLogsStoppedWithExitCode, exitCode));
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _incomingLogs.Enqueue($"── {e.Message}");
        }

        if (_logs == cancel)
        {
            _logs = null;
            IsFollowingLogs = false;
        }

        cancel.Dispose();
    }

    private void AddOutput(string line) => _incomingOutput.Enqueue(line);

    private void Flush()
    {
        Drain(_incomingOutput, Output, MaxOutputLines);
        if (Drain(_incomingLogs, LogLines, MaxLogLines))
            LogLinesAdded?.Invoke();

        if (!IsFollowingLogs && !IsBusy && _incomingLogs.IsEmpty && _incomingOutput.IsEmpty)
            _flushTimer.Stop();
    }

    private static bool Drain(ConcurrentQueue<string> incoming, ObservableCollection<string> lines, int max)
    {
        var added = false;
        while (incoming.TryDequeue(out var line))
        {
            lines.Add(line);
            added = true;
        }

        while (lines.Count > max)
            lines.RemoveAt(0);
        return added;
    }

    partial void OnIsBusyChanged(bool value)
    {
        foreach (var project in Projects)
            project.BusyChanged();
        if (value)
            _flushTimer.Start();
    }
}
