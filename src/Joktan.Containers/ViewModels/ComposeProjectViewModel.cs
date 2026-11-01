using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Containers.Resources;
using Joktan.Core.ViewModels;

namespace Joktan.Containers.ViewModels;

/// <summary>
/// A service of a compose project and its container, if it has one. <see cref="Project"/> is for
/// the commands in the row's menu, which is outside the page's visual tree.
/// </summary>
public sealed record ComposeServiceRow(string Service, ComposeContainer? Container, ComposeProjectViewModel Project)
{
    public string Name => Container?.Name ?? "";

    public string Status => Container is null ? Strings.ComposeServiceRow_NotCreated
        : Container.Health is { Length: > 0 } health && !Container.Status.Contains(health, StringComparison.OrdinalIgnoreCase)
            ? $"{Container.Status} ({health})"
            : Container.Status;

    public string Ports => Container?.Ports ?? "";

    public string Image => Container?.Image ?? "";

    public bool HasContainer => Container is not null;

    public bool IsRunning => Container?.IsRunning == true;

    public bool IsPaused => Container?.IsPaused == true;

    public bool IsStopped => Container is { IsRunning: false, IsPaused: false };

    /// <summary>Start shows for a service with no container, or one that is stopped.</summary>
    public bool CanStart => !IsRunning && !IsPaused;
}

/// <summary>One compose file on the Containers page: its services, their state and the commands for them.</summary>
public partial class ComposeProjectViewModel : ViewModelBase
{
    private readonly ContainersViewModel _page;
    private bool _nameRead;
    private bool _refreshing;
    private bool _refreshAgain;
    private string? _statusError;

    public ComposeProjectViewModel(string file, ContainersViewModel page)
    {
        File = file;
        _page = page;
        Name = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(file)) ?? file;
    }

    public string File { get; }

    /// <summary>The compose project's name: the file's <c>name:</c>, or else its folder's name.</summary>
    [ObservableProperty]
    public partial string Name { get; set; }

    public ObservableCollection<ComposeServiceRow> Services { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Details))]
    public partial string Summary { get; set; } = "";

    /// <summary>How many services are running, once known, and the file's path.</summary>
    public string Details => Summary.Length > 0 ? $"{Summary} · {File}" : File;

    /// <summary>Why this file's containers could not be read, or its last command failed.</summary>
    [ObservableProperty]
    public partial string? Error { get; set; }

    /// <summary>Set when another file has the same project name, which mixes up their containers.</summary>
    [ObservableProperty]
    public partial string? NameWarning { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExpandText))]
    public partial bool IsExpanded { get; set; } = true;

    public string ExpandText => IsExpanded ? "▾" : "▸";

    /// <summary>Whether Down with volumes is waiting to be confirmed.</summary>
    [ObservableProperty]
    public partial bool IsConfirmingDownWithVolumes { get; set; }

    public string DownWithVolumesText => string.Format(Strings.ComposeProject_TakeDownAndDeleteItsVolumes, Name);

    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(DownWithVolumesText));

    private bool CanRun => !_page.IsBusy;

    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;

    /// <summary>Stops showing the file. Its containers are left as they are.</summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    private void Remove() => _page.RemoveProject(this);

    /// <summary>Creates and starts the containers, building images where the file says so.</summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task UpAsync(string? service) => _page.RunCommandAsync(this, Strings.ComposeProject_Starting, ["up", "-d", .. Only(service)]);

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task StopAsync(string? service) => _page.RunCommandAsync(this, Strings.ComposeProject_Stopping, ["stop", .. Only(service)]);

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task RestartAsync(string? service) => _page.RunCommandAsync(this, Strings.ComposeProject_Restarting, ["restart", .. Only(service)]);

    /// <summary>Stops and removes the containers and networks; volumes are kept.</summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task DownAsync() => _page.RunCommandAsync(this, Strings.ComposeProject_TakingDown, ["down"]);

    /// <summary>Makes new containers even when nothing changed, and starts them.</summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task RecreateAsync(string? service) => _page.RunCommandAsync(this, Strings.ComposeProject_Recreating, ["up", "-d", "--force-recreate", .. Only(service)]);

    /// <summary>Builds the images again, then recreates and starts the containers that use them.</summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task RebuildAsync(string? service) => _page.RunCommandAsync(this, Strings.ComposeProject_Building, ["up", "-d", "--build", .. Only(service)]);

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task PullAsync(string? service) => _page.RunCommandAsync(this, Strings.ComposeProject_Pulling, ["pull", "--ignore-buildable", .. Only(service)]);

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task BuildAsync(string? service) => _page.RunCommandAsync(this, Strings.ComposeProject_Building, ["build", .. Only(service)]);

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task PauseAsync(string? service) => _page.RunCommandAsync(this, Strings.ComposeProject_Pausing, ["pause", .. Only(service)]);

    // compose unpause finds no paused containers under Podman, so the engine resumes them by name.
    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task UnpauseAsync(string? service)
    {
        var paused = Services.Where(r => r.IsPaused && (string.IsNullOrEmpty(service) || r.Service == service)).Select(r => r.Name).ToArray();
        if (paused.Length == 0)
        {
            Error = Strings.ComposeProject_NothingIsPaused;
            return Task.CompletedTask;
        }

        return _page.RunCommandAsync(this, Strings.ComposeProject_Resuming, ["unpause", .. paused], engine: true);
    }

    /// <summary>Stops the containers at once, without letting them shut down.</summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task KillAsync(string? service) => _page.RunCommandAsync(this, Strings.ComposeProject_Killing, ["kill", .. Only(service)]);

    /// <summary>Stops and removes the containers; Start makes them again.</summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task RemoveContainerAsync(string? service) => _page.RunCommandAsync(this, Strings.ComposeProject_Removing, ["rm", "--stop", "--force", .. Only(service)]);

    [RelayCommand(CanExecute = nameof(CanRun))]
    private void DownWithVolumes() => IsConfirmingDownWithVolumes = true;

    [RelayCommand]
    private void CancelDownWithVolumes() => IsConfirmingDownWithVolumes = false;

    /// <summary>As Down, and removes the volumes too, such as the broker's development certificates.</summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task ConfirmDownWithVolumesAsync()
    {
        IsConfirmingDownWithVolumes = false;
        return _page.RunCommandAsync(this, Strings.ComposeProject_TakingDown, ["down", "--volumes"]);
    }

    /// <summary>Shows only this service's logs, starting them if they are not shown.</summary>
    [RelayCommand]
    private void ShowServiceLogs(string? service) => _page.ShowLogs(this, service);

    /// <summary>Opens a terminal with a shell inside the service's container.</summary>
    [RelayCommand]
    private async Task OpenShellAsync(string? service)
    {
        var container = Services.FirstOrDefault(r => r.Service == service)?.Container;
        if (container is not { IsRunning: true } || await _page.ToolAsync(File) is not { } tool)
            return;

        try
        {
            Terminal.Open(tool.Executable, ["exec", "-it", container.Name, "sh"]);
            Error = null;
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or PlatformNotSupportedException)
        {
            Error = string.Format(Strings.ComposeProject_CouldNotOpenATerminal, e.Message);
        }
    }

    /// <summary>Updates the commands when the page starts or finishes running one.</summary>
    public void BusyChanged()
    {
        RemoveCommand.NotifyCanExecuteChanged();
        UpCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        RestartCommand.NotifyCanExecuteChanged();
        DownCommand.NotifyCanExecuteChanged();
        RecreateCommand.NotifyCanExecuteChanged();
        RebuildCommand.NotifyCanExecuteChanged();
        PullCommand.NotifyCanExecuteChanged();
        BuildCommand.NotifyCanExecuteChanged();
        PauseCommand.NotifyCanExecuteChanged();
        UnpauseCommand.NotifyCanExecuteChanged();
        KillCommand.NotifyCanExecuteChanged();
        RemoveContainerCommand.NotifyCanExecuteChanged();
        DownWithVolumesCommand.NotifyCanExecuteChanged();
        ConfirmDownWithVolumesCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Forgets what was read, for when the engine changes.</summary>
    public void Reset()
    {
        _nameRead = false;
        Services.Clear();
        Summary = "";
    }

    /// <summary>Reads the project's name again on the next check, after the file may have changed.</summary>
    public void ForgetName() => _nameRead = false;

    public async Task RefreshStatusAsync()
    {
        // A check already under way may have started before a command changed the containers.
        if (_refreshing)
        {
            _refreshAgain = true;
            return;
        }

        _refreshing = true;
        do
        {
            _refreshAgain = false;
            await ReadStatusAsync();
        }
        while (_refreshAgain);

        _refreshing = false;
        _page.ProjectRead();
    }

    private async Task ReadStatusAsync()
    {
        try
        {
            if (!System.IO.File.Exists(File))
            {
                Services.Clear();
                Summary = "";
                ShowStatusError(string.Format(Strings.ComposeProject_DoesNotExist, File));
                return;
            }

            if (await _page.ToolAsync(File) is not { } tool)
            {
                Services.Clear();
                Summary = "";
                return;
            }

            var services = ComposeTool.InFileOrder(tool.ComposeFile, (await tool.ReadAsync(["config", "--services"]))
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            var containers = ComposeContainer.Parse(await tool.ReadAsync(["ps", "--all", "--format", "json"]));
            await ReadNameAsync(tool);

            // In the file's order, then containers of services no longer in the file by name, so
            // the rows stay put from one check to the next.
            var rows = services.Select(s => new ComposeServiceRow(s, containers.FirstOrDefault(c => c.Service == s), this))
                .Concat(containers.Where(c => !services.Contains(c.Service)).OrderBy(c => c.Service, StringComparer.Ordinal)
                    .ThenBy(c => c.Name, StringComparer.Ordinal).Select(c => new ComposeServiceRow(c.Service, c, this)))
                .ToList();
            if (!rows.SequenceEqual(Services))
            {
                Services.Clear();
                foreach (var row in rows)
                    Services.Add(row);
            }

            var running = rows.Count(r => r.IsRunning);
            Summary = rows.Count == 0 ? Strings.ComposeProject_TheFileHasNoServices : string.Format(Strings.ComposeProject_OfRunning, running, rows.Count);

            // A failed command's message stays until the next command; only a status problem clears.
            if (Error is not null && Error == _statusError)
                Error = null;
            _statusError = null;
        }
        catch (Exception e) when (e is InvalidOperationException or JsonException or System.ComponentModel.Win32Exception)
        {
            ShowStatusError(string.Format(Strings.ComposeProject_CouldNotReadTheContainers, e.Message));
        }
    }

    private async Task ReadNameAsync(ComposeTool tool)
    {
        if (_nameRead)
            return;
        _nameRead = true;
        try
        {
            using var config = JsonDocument.Parse(await tool.ReadAsync(["config", "--format", "json"]));
            if (config.RootElement.TryGetProperty("name", out var name) && name.GetString() is { Length: > 0 } text)
                Name = text;
        }
        catch (Exception e) when (e is InvalidOperationException or JsonException or System.ComponentModel.Win32Exception)
        {
            // A provider without this format keeps the folder's name, which is usually the same.
        }
    }

    private void ShowStatusError(string message) => Error = _statusError = message;

    private static string[] Only(string? service) => string.IsNullOrEmpty(service) ? [] : [service];
}

/// <summary>What the logs show: a file's services, or one of them.</summary>
public sealed record ComposeLogChoice(ComposeProjectViewModel Project, string? Service)
{
    public string ServiceText => Service ?? ContainersViewModel.AllServices;
}
