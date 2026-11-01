using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Joktan.Agent.Mcp;
using Joktan.Containers;
using Joktan.Containers.ViewModels;
using static Joktan.Agent.Tools.AgentTools;

namespace Joktan.Agent.Tools;

public static class ContainerTools
{
    private static readonly ToolParameter Project = P.Text("project", "The project's name or compose file, from containers_list.");

    private static readonly string[] Actions = ["up", "stop", "restart", "down", "down_with_volumes", "recreate", "rebuild", "pull", "build", "pause", "unpause", "kill", "remove"];

    public static IEnumerable<McpTool> Create(ContainersViewModel page) =>
    [
        ReadAsync("containers_list", "List the compose files on the Containers page and the state of their services.", [], async _ =>
        {
            await Task.WhenAll(page.Projects.Select(p => p.RefreshStatusAsync()));
            return new
            {
                Engine = page.SelectedEngine.ToString(), page.EngineText, page.Summary, page.Error,
                Projects = page.Projects.Select(p => new
                {
                    p.Name, p.File, p.Summary, p.Error,
                    Services = p.Services.Select(s => new { s.Service, Container = s.Name, s.Status, s.Ports, s.Image }).ToList(),
                }).ToList(),
            };
        }),
        Change("containers_add_file", "Add a compose file to the Containers page. It starts nothing.", [P.Text("path", "The file's path.")], args =>
        {
            page.NewComposeFile = args.Text("path");
            page.AddTypedCommand.Execute(null);
            Check(page.Error);
            return "Added.";
        }),
        Change("containers_remove_file", "Stop showing a compose file. Its containers are left as they are.", [Project], args =>
        {
            page.RemoveProject(Find(page, args));
            return "Removed.";
        }),
        Change("containers_engine", "Choose the container engine.", [P.Choice("engine", "Which.", Enum.GetNames<ContainerEngine>())], args =>
        {
            page.SelectedEngine = Enum.Parse<ContainerEngine>(args.Text("engine"), ignoreCase: true);
            return "Chosen.";
        }),
        RiskyAsync("containers_run", "Run a compose action on a project or one service. down_with_volumes also deletes what its volumes hold.",
            [Project, P.Choice("action", "What to do.", Actions), P.OptText("service", "One service; all of them otherwise.")], async args =>
            {
                var project = Find(page, args);
                var service = args.OptText("service");
                if (page.IsBusy)
                    throw new ToolFailure("Another compose command is still running.");

                page.Output.Clear();
                await (args.Text("action") switch
                {
                    "up" => project.UpCommand.ExecuteAsync(service),
                    "stop" => project.StopCommand.ExecuteAsync(service),
                    "restart" => project.RestartCommand.ExecuteAsync(service),
                    "down" => project.DownCommand.ExecuteAsync(null),
                    "down_with_volumes" => project.ConfirmDownWithVolumesCommand.ExecuteAsync(null),
                    "recreate" => project.RecreateCommand.ExecuteAsync(service),
                    "rebuild" => project.RebuildCommand.ExecuteAsync(service),
                    "pull" => project.PullCommand.ExecuteAsync(service),
                    "build" => project.BuildCommand.ExecuteAsync(service),
                    "pause" => project.PauseCommand.ExecuteAsync(service),
                    "unpause" => project.UnpauseCommand.ExecuteAsync(service),
                    "kill" => project.KillCommand.ExecuteAsync(service),
                    "remove" => project.RemoveContainerCommand.ExecuteAsync(service),
                    _ => throw new ToolFailure($"The action must be one of {string.Join(", ", Actions)}."),
                });

                await project.RefreshStatusAsync();

                // The page takes in its output on a timer.
                await Task.Delay(TimeSpan.FromMilliseconds(500));
                return new { project.Summary, Problem = project.Error ?? page.Error, Output = page.Output.TakeLast(100).ToList() };
            }),
        Risky("containers_open_shell", "Open a terminal window on the user's screen, with a shell inside a running service's container. Nothing is typed into it.", [Project, P.Text("service", "The service.")], args =>
        {
            Find(page, args).OpenShellCommand.Execute(args.Text("service"));
            return "Opened.";
        }),
        ReadAsync("containers_logs", "Read the latest logs of a project or one service.", [Project, P.OptText("service", "One service; all of them otherwise."), P.OptNumber("lines", "How many lines; 200 unless given.")], async args =>
        {
            var project = Find(page, args);
            var lines = args.Whole("lines") ?? 200;
            page.TailLines = lines;
            page.ShowLogs(project, args.OptText("service"));

            // They arrive as they are read, so give them a moment.
            await Task.Delay(TimeSpan.FromSeconds(2));
            return page.LogLines.TakeLast(lines).ToList();
        }),
    ];

    private static ComposeProjectViewModel Find(ContainersViewModel page, ToolArgs args)
    {
        var name = args.Text("project").Trim();
        var found = page.Projects.Where(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase) || p.File == name).ToList();
        return found.Count switch
        {
            0 => throw new ToolFailure($"No project is called {name}. containers_list gives them."),
            1 => found[0],
            _ => throw new ToolFailure($"{found.Count} projects are called {name}. Give its file instead."),
        };
    }
}
