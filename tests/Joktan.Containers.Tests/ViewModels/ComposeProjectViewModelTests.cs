using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Joktan.Containers.ViewModels;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Containers.Tests.ViewModels;

public class ComposeServiceRowTests
{
    private static ComposeServiceRow Row(string state, string status = "s", string health = "") =>
        new("svc", new ComposeContainer("svc", "name", state, status, "1→1", "img", health), null!);

    [Fact]
    public void A_service_without_a_container_can_be_started()
    {
        var row = new ComposeServiceRow("svc", null, null!);

        Assert.Equal("Not created", row.Status);
        Assert.Equal("", row.Name);
        Assert.Equal("", row.Ports);
        Assert.Equal("", row.Image);
        Assert.False(row.HasContainer);
        Assert.False(row.IsRunning);
        Assert.False(row.IsPaused);
        Assert.False(row.IsStopped);
        Assert.True(row.CanStart);
    }

    [Theory]
    [InlineData("running", true, false, false, false)]
    [InlineData("paused", false, true, false, false)]
    [InlineData("exited", false, false, true, true)]
    public void Knows_the_state_of_its_container(string state, bool running, bool paused, bool stopped, bool canStart)
    {
        var row = Row(state);

        Assert.True(row.HasContainer);
        Assert.Equal(running, row.IsRunning);
        Assert.Equal(paused, row.IsPaused);
        Assert.Equal(stopped, row.IsStopped);
        Assert.Equal(canStart, row.CanStart);
        Assert.Equal("name", row.Name);
        Assert.Equal("1→1", row.Ports);
        Assert.Equal("img", row.Image);
    }

    [Theory]
    [InlineData("Up 2 minutes", "", "Up 2 minutes")]
    [InlineData("Up 2 minutes", "healthy", "Up 2 minutes (healthy)")]
    [InlineData("Up 2 minutes (HEALTHY)", "healthy", "Up 2 minutes (HEALTHY)")]
    public void Adds_the_health_unless_the_status_gives_it(string status, string health, string expected)
    {
        Assert.Equal(expected, Row("running", status, health).Status);
    }
}

public class ComposeProjectViewModelTests : FakeEngineTest
{
    [Fact]
    public Task Is_named_after_its_folder_until_the_file_says_otherwise() => Headless.Run(async () =>
    {
        var page = Page();
        var project = page.Projects[0];
        Assert.Equal("project", project.Name);
        Assert.Equal(ComposeFile, project.File);
        Assert.Equal(ComposeFile, project.Details);

        await project.RefreshStatusAsync();

        Assert.Equal("proj", project.Name);
        Assert.Contains("proj", project.DownWithVolumesText);
        Assert.Equal($"1 of 2 running · {ComposeFile}", project.Details);
    });

    [Fact]
    public Task Lists_the_services_in_file_order_then_others_by_name() => Headless.Run(async () =>
    {
        Answer("ps", string.Join("\n",
            """{"Service":"old","Name":"p-old-2","State":"exited","Status":"Exited"}""",
            """{"Service":"broker","Name":"p-broker-1","State":"running","Status":"Up"}""",
            """{"Service":"another","Name":"p-another-1","State":"exited","Status":"Exited"}""",
            """{"Service":"old","Name":"p-old-1","State":"exited","Status":"Exited"}"""));
        var project = await Read(Page());

        Assert.Equal(["broker", "web", "another", "old", "old"], project.Services.Select(s => s.Service));
        Assert.Equal(["p-old-1", "p-old-2"], project.Services.Skip(3).Select(s => s.Name));
        Assert.Null(project.Error);
    });

    [Fact]
    public Task The_same_rows_are_not_replaced() => Headless.Run(async () =>
    {
        var project = await Read(Page());
        var first = project.Services[0];

        await project.RefreshStatusAsync();

        Assert.Same(first, project.Services[0]);
    });

    [Fact]
    public Task A_file_with_no_services_says_so() => Headless.Run(async () =>
    {
        Answer("services");
        Answer("ps");
        var project = await Read(Page());

        Assert.Empty(project.Services);
        Assert.Equal("The file has no services.", project.Summary);
    });

    [Fact]
    public Task A_missing_file_is_reported_and_clears_when_it_returns() => Headless.Run(async () =>
    {
        var project = await Read(Page());
        Assert.NotEmpty(project.Services);
        File.Delete(ComposeFile);

        await project.RefreshStatusAsync();
        Assert.Equal($"{ComposeFile} does not exist.", project.Error);
        Assert.Empty(project.Services);
        Assert.Equal("", project.Summary);

        File.WriteAllText(ComposeFile, "services:\n");
        await project.RefreshStatusAsync();
        Assert.Null(project.Error);
    });

    [Fact]
    public Task A_status_that_cannot_be_read_is_reported_but_a_commands_error_stays() => Headless.Run(async () =>
    {
        Answer("status-fails");
        var project = await Read(Page());
        Assert.Equal("Could not read the containers: status went wrong", project.Error);

        NoAnswer("status-fails");
        project.Error = "A command failed";
        await project.RefreshStatusAsync();
        Assert.Equal("A command failed", project.Error);
    });

    [Fact]
    public Task Damaged_status_is_reported() => Headless.Run(async () =>
    {
        Answer("ps", "{ not json");
        var project = await Read(Page());

        Assert.StartsWith("Could not read the containers: ", project.Error);
    });

    [Fact]
    public Task A_provider_without_the_config_format_keeps_the_folders_name() => Headless.Run(async () =>
    {
        Answer("name-fails");
        Assert.Equal("project", (await Read(Page())).Name);

        NoAnswer("name-fails");
        Answer("config", "{ not json");
        Assert.Equal("project", (await Read(Page())).Name);

        Answer("config", """{"other":"x"}""");
        Assert.Equal("project", (await Read(Page())).Name);

        Answer("config", """{"name":""}""");
        Assert.Equal("project", (await Read(Page())).Name);
    });

    [Fact]
    public Task The_name_is_read_once_until_forgotten() => Headless.Run(async () =>
    {
        var project = await Read(Page());
        Answer("config", """{"name":"changed"}""");

        await project.RefreshStatusAsync();
        Assert.Equal("proj", project.Name);

        project.ForgetName();
        await project.RefreshStatusAsync();
        Assert.Equal("changed", project.Name);
    });

    [Fact]
    public Task Resetting_forgets_what_was_read() => Headless.Run(async () =>
    {
        var project = await Read(Page());

        project.Reset();

        Assert.Empty(project.Services);
        Assert.Equal("", project.Summary);
    });

    [Fact]
    public Task A_check_during_a_check_runs_another_afterwards() => Headless.Run(async () =>
    {
        var project = Page().Projects[0];

        var first = project.RefreshStatusAsync();
        var second = project.RefreshStatusAsync();
        await second;
        await first;

        Assert.Equal(4, Calls.Split('\n').Count(l => l.Contains("config --services") || l.Contains("ps --all")));
    });

    [Fact]
    public Task Expanding_toggles() => Headless.Run(() =>
    {
        var project = Page().Projects[0];
        Assert.Equal("▾", project.ExpandText);

        project.ToggleExpandedCommand.Execute(null);

        Assert.False(project.IsExpanded);
        Assert.Equal("▸", project.ExpandText);
    });

    public static TheoryData<string, string?, string> Commands => new()
    {
        { "Up", "broker", "up -d broker" },
        { "Up", null, "up -d" },
        { "Stop", "broker", "stop broker" },
        { "Restart", "broker", "restart broker" },
        { "Recreate", "broker", "up -d --force-recreate broker" },
        { "Rebuild", "", "up -d --build" },
        { "Pull", "broker", "pull --ignore-buildable broker" },
        { "Build", "broker", "build broker" },
        { "Pause", "broker", "pause broker" },
        { "Kill", "broker", "kill broker" },
        { "RemoveContainer", "broker", "rm --stop --force broker" },
    };

    [Theory]
    [MemberData(nameof(Commands))]
    public Task Each_command_runs_compose_as_expected(string command, string? service, string expected) => Headless.Run(async () =>
    {
        var project = await Read(Page());
        var relay = (CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)typeof(ComposeProjectViewModel).GetProperty(command + "Command")!.GetValue(project)!;

        await relay.ExecuteAsync(service);

        Assert.Contains($"docker compose -f {ComposeFile} {expected}\n", Calls);
    });

    [Fact]
    public Task Down_runs_and_down_with_volumes_asks_first() => Headless.Run(async () =>
    {
        var project = await Read(Page());

        await project.DownCommand.ExecuteAsync(null);
        Assert.Contains($"-f {ComposeFile} down\n", Calls);

        project.DownWithVolumesCommand.Execute(null);
        Assert.True(project.IsConfirmingDownWithVolumes);
        project.CancelDownWithVolumesCommand.Execute(null);
        Assert.False(project.IsConfirmingDownWithVolumes);
        Assert.DoesNotContain("--volumes", Calls);

        project.DownWithVolumesCommand.Execute(null);
        await project.ConfirmDownWithVolumesCommand.ExecuteAsync(null);
        Assert.False(project.IsConfirmingDownWithVolumes);
        Assert.Contains("down --volumes\n", Calls);
    });

    [Fact]
    public Task Resuming_goes_through_the_engine_by_container_name() => Headless.Run(async () =>
    {
        Answer("ps", """{"Service":"broker","Name":"p-broker-1","State":"paused","Status":"Paused"}""");
        var project = await Read(Page());

        await project.UnpauseCommand.ExecuteAsync("web");
        Assert.Equal("Nothing is paused.", project.Error);

        await project.UnpauseCommand.ExecuteAsync("broker");
        Assert.Contains("docker unpause p-broker-1\n", Calls);

        await project.UnpauseCommand.ExecuteAsync(null);
        Assert.Null(project.Error);
    });

    [Fact]
    public Task Commands_wait_for_the_one_running() => Headless.Run(async () =>
    {
        Answer("command-hangs");
        var page = Page();
        var project = await Read(page);
        Assert.True(project.UpCommand.CanExecute(null));
        Assert.False(page.CancelCommand.CanExecute(null));

        var running = project.StopCommand.ExecuteAsync(null);
        await Headless.Until(() => page.IsBusy && Calls.Contains("stop"));

        Assert.False(project.UpCommand.CanExecute(null));
        Assert.False(project.RemoveCommand.CanExecute(null));
        Assert.StartsWith("Stopping", page.Activity);
        Assert.True(page.CancelCommand.CanExecute(null));

        page.CancelCommand.Execute(null);
        await running;

        Assert.False(page.IsBusy);
        Assert.Null(page.Activity);
        Assert.True(project.UpCommand.CanExecute(null));
        await Headless.Until(() => page.Output.Contains("Cancelled."));
    });

    [Fact]
    public Task Removing_leaves_the_file_off_the_page() => Headless.Run(() =>
    {
        var page = Page();

        page.Projects[0].RemoveCommand.Execute(null);

        Assert.False(page.HasProjects);
    });

    [Fact]
    public Task A_shell_opens_only_in_a_running_container() => Headless.Run(async () =>
    {
        var started = new List<(string Program, string[] Arguments)>();
        Terminal.Start = (p, a) => started.Add((p, a.ToArray()));
        var project = await Read(Page());

        await project.OpenShellCommand.ExecuteAsync("web");
        await project.OpenShellCommand.ExecuteAsync("nothing");
        Assert.Empty(started);

        project.Error = "old";
        await project.OpenShellCommand.ExecuteAsync("broker");

        var (program, arguments) = Assert.Single(started);
        Assert.Null(project.Error);
        Assert.Contains(Folder.At("bin", "docker"), string.Join(" ", arguments));
        Assert.Contains("exec", string.Join(" ", arguments));
        Assert.Contains("p-broker-1", string.Join(" ", arguments));
        Assert.NotEmpty(program);
    });

    [Fact]
    public Task A_terminal_that_will_not_open_is_reported() => Headless.Run(async () =>
    {
        Terminal.Start = (_, _) => throw new InvalidOperationException("no terminal");
        var project = await Read(Page());

        await project.OpenShellCommand.ExecuteAsync("broker");

        Assert.Equal("Could not open a terminal: no terminal", project.Error);
    });

    [Fact]
    public Task A_log_choice_names_its_service_or_all() => Headless.Run(() =>
    {
        var project = Page().Projects[0];

        Assert.Equal("web", new ComposeLogChoice(project, "web").ServiceText);
        Assert.Equal(ContainersViewModel.AllServices, new ComposeLogChoice(project, null).ServiceText);
    });
}
