using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using Joktan.Containers.ViewModels;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Containers.Tests.ViewModels;

public class ContainersPageTests : FakeEngineTest
{
    [Fact]
    public Task Starts_with_the_files_chosen_and_the_engine() => Headless.Run(() =>
    {
        var page = Page();

        Assert.Equal([ComposeFile], page.Projects.Select(p => p.File));
        Assert.True(page.HasProjects);
        Assert.Equal(ContainerEngine.Docker, page.SelectedEngine);
        Assert.Equal(Enum.GetValues<ContainerEngine>(), page.Engines);
        Assert.Same(page.Projects[0], page.SelectedLog!.Project);
        Assert.Null(page.SelectedLog.Service);
        Assert.Equal("Show logs", page.LogsButtonText);
    });

    [Fact]
    public Task Without_any_choice_the_repositorys_file_is_offered() => Headless.Run(() =>
    {
        var page = new ContainersViewModel(Folder.At("nothing-saved"));

        // Only looked at: nothing here runs a command against it.
        Assert.Equal(page.DefaultComposeFile is null ? 0 : 1, page.Projects.Count);
        Assert.Empty(Calls);
    });

    [Fact]
    public Task Adding_a_typed_file_keeps_it_and_clears_the_box() => Headless.Run(async () =>
    {
        var page = Page();
        var other = Folder.Write("second/compose.yml", "services:\n");
        page.NewComposeFile = "  " + other + " ";

        page.AddTypedCommand.Execute(null);

        Assert.Equal("", page.NewComposeFile);
        Assert.Null(page.Error);
        Assert.Equal(other, page.Projects[1].File);
        Assert.Equal([ComposeFile, other], ComposeSettings.Load(DataFolder).ComposeFiles);
        await Headless.Until(() => page.Projects[1].Summary.Length > 0);
    });

    [Fact]
    public Task Adding_nothing_a_missing_file_or_one_already_there_is_refused() => Headless.Run(() =>
    {
        var page = Page();

        page.NewComposeFile = "   ";
        page.AddTypedCommand.Execute(null);
        Assert.Null(page.Error);

        page.NewComposeFile = Folder.At("missing.yml");
        page.AddTypedCommand.Execute(null);
        Assert.Contains("does not exist", page.Error);
        Assert.Equal(Folder.At("missing.yml"), page.NewComposeFile);

        page.NewComposeFile = ComposeFile;
        page.AddTypedCommand.Execute(null);
        Assert.Contains("already on the list", page.Error);
        Assert.Single(page.Projects);
    });

    [Fact]
    public Task A_tilde_means_the_home_folder() => Headless.Run(() =>
    {
        var page = Page();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        page.NewComposeFile = "~/" + Path.GetRelativePath(home, ComposeFile);

        page.AddTypedCommand.Execute(null);

        Assert.Contains("already on the list", page.Error);
    });

    [Fact]
    public Task Browsing_adds_each_file_picked() => Headless.Run(async () =>
    {
        var page = Page();
        await page.BrowseCommand.ExecuteAsync(null);
        Assert.Single(page.Projects);

        var a = Folder.Write("a/compose.yml", "services:\n");
        var b = Folder.Write("b/compose.yml", "services:\n");
        page.PickComposeFiles = () => Task.FromResult<IReadOnlyList<string>>([a, b]);
        await page.BrowseCommand.ExecuteAsync(null);

        Assert.Equal(3, page.Projects.Count);
    });

    [Fact]
    public Task The_first_file_added_is_what_the_logs_show() => Headless.Run(() =>
    {
        var page = Page();
        page.RemoveProject(page.Projects[0]);
        Assert.Null(page.SelectedLog);
        Assert.Empty(page.LogChoices);

        page.NewComposeFile = ComposeFile;
        page.AddTypedCommand.Execute(null);

        Assert.Same(page.Projects[0], page.SelectedLog!.Project);
    });

    [Fact]
    public Task Removing_the_file_the_logs_show_moves_them_on() => Headless.Run(() =>
    {
        var other = Folder.Write("second/compose.yml", "services:\n");
        var page = Page(ComposeFile, other);
        var first = page.Projects[0];
        page.SelectedLog = page.LogChoices.First(c => c.Project == first);

        page.RemoveProject(first);

        Assert.Same(page.Projects[0], page.SelectedLog!.Project);
        Assert.Equal([other], ComposeSettings.Load(DataFolder).ComposeFiles);
        Assert.False(page.IsFollowingLogs);
    });

    [Fact]
    public Task Removing_another_file_leaves_the_logs_alone() => Headless.Run(() =>
    {
        var other = Folder.Write("second/compose.yml", "services:\n");
        var page = Page(ComposeFile, other);
        var shown = page.SelectedLog;

        page.RemoveProject(page.Projects[1]);

        Assert.Same(shown, page.SelectedLog);
    });

    [Fact]
    public Task Reading_brings_the_summary_and_the_log_choices_up_to_date() => Headless.Run(async () =>
    {
        var page = Page();
        Assert.Equal("", page.Summary);

        var project = await Read(page);

        Assert.Equal("1 of 2 running", page.Summary);
        Assert.Equal([null, "broker", "web"], page.LogChoices.Select(c => c.Service));
        Assert.Contains("docker compose, with ", page.EngineText);
        Assert.Null(project.NameWarning);
    });

    [Fact]
    public Task A_service_being_shown_stays_a_choice_after_it_has_gone() => Headless.Run(async () =>
    {
        var page = Page();
        var project = await Read(page);
        page.SelectedLog = page.LogChoices.First(c => c.Service == "web");

        Answer("services", "broker\n");
        await project.RefreshStatusAsync();

        Assert.Contains(page.LogChoices, c => c.Service == "web");

        page.SelectedLog = page.LogChoices[0];
        page.ProjectRead();
        Assert.DoesNotContain(page.LogChoices, c => c.Service == "web");
    });

    [Fact]
    public Task A_new_service_takes_its_place_in_the_choices() => Headless.Run(async () =>
    {
        Answer("services", "web\n");
        Answer("ps");
        var page = Page();
        var project = await Read(page);
        Assert.Equal([null, "web"], page.LogChoices.Select(c => c.Service));

        Answer("services", "broker\nweb\n");
        await project.RefreshStatusAsync();
        Assert.Equal([null, "broker", "web"], page.LogChoices.Select(c => c.Service));

        Folder.Write("project/docker-compose.yml", "services:\n  web:\n  broker:\n");
        project.Services.Clear();
        await project.RefreshStatusAsync();
        Assert.Equal([null, "web", "broker"], page.LogChoices.Select(c => c.Service));
    });

    [Fact]
    public Task Files_with_the_same_project_name_are_warned_about() => Headless.Run(async () =>
    {
        var other = Folder.Write("second/compose.yml", "services:\n");
        var page = Page(ComposeFile, other);

        await page.RefreshCommand.ExecuteAsync(null);

        Assert.Contains(other, page.Projects[0].NameWarning);
        Assert.Contains(ComposeFile, page.Projects[1].NameWarning);
    });

    [Fact]
    public Task Refreshing_looks_for_the_engine_and_the_name_again() => Headless.Run(async () =>
    {
        var page = Page();
        var project = await Read(page);
        Answer("config", """{"name":"changed"}""");

        await page.RefreshCommand.ExecuteAsync(null);

        Assert.Equal("changed", project.Name);
    });

    [Fact]
    public Task Changing_the_engine_saves_it_and_reads_again_with_it() => Headless.Run(async () =>
    {
        var page = Page();
        var project = await Read(page);

        page.SelectedEngine = ContainerEngine.Podman;

        Assert.Equal(ContainerEngine.Podman, ComposeSettings.Load(DataFolder).Engine);
        await Headless.Until(() => Calls.Contains("podman compose") && project.Services.Count > 0);
        Assert.Contains("podman compose, with ", page.EngineText);

        page.SelectedEngine = ContainerEngine.Podman;
        page.Shutdown();
    });

    [Fact]
    public Task Settings_that_cannot_be_saved_are_reported() => Headless.Run(() =>
    {
        var page = Page();
        Directory.Delete(DataFolder, recursive: true);
        Answer("data");

        page.ToggleLogPanelCommand.Execute(null);

        Assert.StartsWith("Could not save the settings: ", page.Error);
    });

    [Fact]
    public Task Setting_the_log_panel_to_what_it_is_saves_nothing() => Headless.Run(() =>
    {
        var page = Page();
        var before = File.GetLastWriteTimeUtc(ComposeSettings.PathIn(DataFolder));
        File.Delete(ComposeSettings.PathIn(DataFolder));

        page.IsLogPanelOpen = page.IsLogPanelOpen;

        Assert.False(File.Exists(ComposeSettings.PathIn(DataFolder)));
        Assert.NotEqual(default, before);
    });

    [Fact]
    public Task Activating_checks_at_once_and_deactivating_stops_the_checks() => Headless.Run(async () =>
    {
        var page = Page();

        page.Activate();
        await Headless.Until(() => page.Projects[0].Services.Count > 0);
        page.Deactivate();

        Assert.Equal("1 of 2 running", page.Summary);
    });

    [Fact]
    public Task The_checks_repeat_while_the_page_is_shown() => Headless.Run(async () =>
    {
        var page = Page();
        page.Activate();
        await Headless.Until(() => page.Projects[0].Services.Count > 0);
        Answer("ps");

        await Headless.Until(() => page.Summary == "0 of 2 running", timeoutMs: 15000);
        page.Shutdown();
    });

    [Fact]
    public Task A_timer_refresh_failure_is_reported_without_an_unhandled_exception() => Headless.Run(async () =>
    {
        var page = Page();
        var unhandled = false;
        var notifications = new List<string?>();
        DispatcherUnhandledExceptionEventHandler handler = (_, e) =>
        {
            unhandled = true;
            e.Handled = true;
        };
        Dispatcher.UIThread.UnhandledException += handler;
        try
        {
            page.Activate();
            await Headless.Until(() => page.Summary == "1 of 2 running");
            page.PropertyChanged += (_, e) =>
            {
                notifications.Add(e.PropertyName);
                if (e.PropertyName == nameof(page.Summary))
                    throw new Exception("refresh failed");
            };
            Answer("ps");

            await Headless.Until(() => unhandled || page.Error is not null, timeoutMs: 15000);
            Assert.False(unhandled);
            Assert.Contains(nameof(page.Summary), notifications);
            Assert.Contains("refresh failed", page.Error);
        }
        finally
        {
            page.Shutdown();
            Dispatcher.UIThread.UnhandledException -= handler;
        }
    });

    [Fact]
    public Task An_engine_with_no_command_is_reported_and_found_again_later() => Headless.Run(async () =>
    {
        // Only where the real one is not in a usual folder either.
        Assert.SkipWhen(!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux(), "PATH is shaped differently.");
        File.Delete(Folder.At("bin", "docker"));
        Assert.SkipWhen(ComposeTool.Find(ContainerEngine.Docker) is not null, "Docker is installed on this machine.");
        var page = Page();
        var project = page.Projects[0];

        await project.RefreshStatusAsync();

        Assert.Contains("Docker is not installed", page.Error);
        Assert.Null(page.EngineText);
        Assert.Empty(project.Services);
    });

    [Fact]
    public Task A_command_that_fails_says_so_and_what_ran_is_in_the_output() => Headless.Run(async () =>
    {
        Answer("command-exit", "3");
        var page = Page();
        var project = await Read(page);

        await project.StopCommand.ExecuteAsync(null);

        Assert.Contains("failed with exit code 3", project.Error);
        await Headless.Until(() => page.Output.Any(l => l.StartsWith("ran ", StringComparison.Ordinal)));
        Assert.Contains(page.Output, l => l.StartsWith("proj $ docker compose stop", StringComparison.Ordinal));

        page.ClearOutputCommand.Execute(null);
        Assert.Empty(page.Output);
    });

    [Fact]
    public Task An_engine_that_has_gone_is_reported_for_commands_status_and_logs() => Headless.Run(async () =>
    {
        var page = Page();
        var project = await Read(page);
        File.Delete(Folder.At("bin", "docker"));

        await project.StopCommand.ExecuteAsync(null);
        Assert.NotNull(project.Error);

        await project.RefreshStatusAsync();
        Assert.StartsWith("Could not read the containers: ", project.Error);

        page.ToggleLogsCommand.Execute(null);
        await Headless.Until(() => page.LogLines.Count >= 2);
        Assert.StartsWith("── ", page.LogLines[1]);
        Assert.False(page.IsFollowingLogs);
    });

    [Fact]
    public Task Logs_need_a_file() => Headless.Run(() =>
    {
        var page = Page();
        page.RemoveProject(page.Projects[0]);

        page.ToggleLogsCommand.Execute(null);

        Assert.Equal("Add a compose file to see its logs.", page.Error);
        Assert.False(page.IsFollowingLogs);
    });

    [Fact]
    public Task Logs_show_what_the_command_writes_and_say_why_they_ended() => Headless.Run(async () =>
    {
        var page = Page();
        var added = 0;
        page.LogLinesAdded += () => added++;

        page.ToggleLogsCommand.Execute(null);

        await Headless.Until(() => page.LogLines.Any(l => l.Contains("no container is running")));
        Assert.StartsWith("── project $ docker compose logs --follow --no-color --tail 200", page.LogLines[0]);
        Assert.Contains("a log line", page.LogLines);
        Assert.False(page.IsFollowingLogs);
        Assert.True(added > 0);

        page.ClearLogsCommand.Execute(null);
        Assert.Empty(page.LogLines);
    });

    [Fact]
    public Task Logs_that_fail_give_the_exit_code() => Headless.Run(async () =>
    {
        Answer("logs-exit", "9");
        var page = Page();

        page.ToggleLogsCommand.Execute(null);

        await Headless.Until(() => page.LogLines.Any(l => l.Contains("exit code 9")));
    });

    [Fact]
    public Task Logs_can_be_stopped_and_changing_what_they_show_starts_them_again() => Headless.Run(async () =>
    {
        Answer("logs-hang");
        var page = Page();
        await Read(page);
        page.TailLines = -5;

        page.ToggleLogsCommand.Execute(null);
        await Headless.Until(() => page.IsFollowingLogs && page.LogLines.Contains("a log line"));
        Assert.Equal("Stop", page.LogsButtonText);
        Assert.Contains("--tail 0", page.LogLines[0]);

        page.ShowTimestamps = true;
        await Headless.Until(() => page.LogLines.Count > 0 && page.LogLines[0].Contains("--timestamps"));

        page.SelectedLog = page.LogChoices.First(c => c.Service == "web");
        await Headless.Until(() => page.LogLines.Count > 0 && page.LogLines[0].Contains("--no-log-prefix web"));

        page.ToggleLogsCommand.Execute(null);
        await Headless.Until(() => !page.IsFollowingLogs);
    });

    [Fact]
    public Task Changing_what_is_shown_does_not_start_logs_that_are_stopped() => Headless.Run(async () =>
    {
        var page = Page();
        await Read(page);

        page.ShowTimestamps = true;
        page.SelectedLog = page.LogChoices[1];

        Assert.False(page.IsFollowingLogs);
        Assert.DoesNotContain(" logs ", Calls);
    });

    [Fact]
    public Task A_cleared_selection_goes_back_to_the_first_choice() => Headless.Run(() =>
    {
        var page = Page();

        page.SelectedLog = null;

        Assert.Same(page.LogChoices[0], page.SelectedLog);
    });

    [Fact]
    public Task Showing_a_services_logs_opens_the_panel_on_them() => Headless.Run(async () =>
    {
        Answer("logs-hang");
        var page = Page();
        var project = await Read(page);
        page.IsLogPanelOpen = false;
        var shown = 0;
        page.LogsShown += () => shown++;

        project.ShowServiceLogsCommand.Execute("broker");

        Assert.True(page.IsLogPanelOpen);
        Assert.Equal(1, shown);
        Assert.Equal("broker", page.SelectedLog!.Service);
        await Headless.Until(() => page.IsFollowingLogs);

        // Already following, and asking for a service that has gone means them all.
        project.ShowServiceLogsCommand.Execute("gone");
        Assert.Null(page.SelectedLog!.Service);
        project.ShowServiceLogsCommand.Execute("");
        Assert.Null(page.SelectedLog!.Service);
        page.Shutdown();
        await Headless.Until(() => !page.IsFollowingLogs);
    });

    [Fact]
    public Task Logs_pause_for_a_command_on_their_file_and_continue_from_then() => Headless.Run(async () =>
    {
        Answer("logs-hang");
        var page = Page();
        var project = await Read(page);
        page.ToggleLogsCommand.Execute(null);
        await Headless.Until(() => page.LogLines.Contains("a log line"));

        await project.RestartCommand.ExecuteAsync(null);

        await Headless.Until(() => Calls.Contains("--since"));
        Assert.True(page.IsFollowingLogs);
        page.ToggleLogsCommand.Execute(null);
        await Headless.Until(() => !page.IsFollowingLogs);
    });

    [Fact]
    public Task Stopping_logs_paused_by_a_command_does_not_start_them_again() => Headless.Run(async () =>
    {
        Answer("logs-hang");
        Answer("command-hangs");
        var page = Page();
        var project = await Read(page);
        page.ToggleLogsCommand.Execute(null);
        await Headless.Until(() => page.LogLines.Contains("a log line"));

        var running = project.StopCommand.ExecuteAsync(null);
        await Headless.Until(() => Calls.Contains(" stop"));
        page.ToggleLogsCommand.Execute(null);
        Assert.False(page.IsFollowingLogs);

        page.CancelCommand.Execute(null);
        await running;

        Assert.DoesNotContain("--since", Calls);
    });

    [Fact]
    public Task A_command_on_another_file_leaves_the_logs_running() => Headless.Run(async () =>
    {
        Answer("logs-hang");
        var other = Folder.Write("second/compose.yml", "services:\n");
        var page = Page(ComposeFile, other);
        page.ToggleLogsCommand.Execute(null);
        await Headless.Until(() => page.LogLines.Contains("a log line"));

        await page.Projects[1].StopCommand.ExecuteAsync(null);

        Assert.True(page.IsFollowingLogs);
        Assert.DoesNotContain("--since", Calls);
        page.Shutdown();
        await Headless.Until(() => !page.IsFollowingLogs);
    });

    [Fact]
    public Task Long_output_keeps_only_the_newest_lines() => Headless.Run(async () =>
    {
        var page = Page();
        var project = await Read(page);
        var script = string.Join("\n", Enumerable.Range(0, 1100).Select(i => $"echo line{i}"));
        await File.WriteAllTextAsync(Folder.At("bin", "docker"), $"#!/bin/sh\n{script}\n");

        await project.StopCommand.ExecuteAsync(null);

        await Headless.Until(() => page.Output.Contains("line1099"));
        Assert.Equal(1000, page.Output.Count);
    });

    [Fact]
    public Task Nothing_runs_while_another_command_does() => Headless.Run(async () =>
    {
        var page = Page();
        var project = await Read(page);
        page.IsBusy = true;

        await page.RunCommandAsync(project, "Stopping", ["stop"]);

        Assert.DoesNotContain(" stop", Calls);
        page.IsBusy = false;
    });
}
