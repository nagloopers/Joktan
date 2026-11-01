using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Joktan.Containers.Tests.ViewModels;
using Xunit;

namespace Joktan.Containers.Tests;

public class ComposeToolEngineTests : FakeEngineTest
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Auto_uses_docker_when_its_engine_answers()
    {
        var tool = await ComposeTool.ResolveAsync(ContainerEngine.Auto, ComposeFile, Cancel);

        Assert.Equal(ContainerEngine.Docker, tool.Engine);
        Assert.Equal("docker", tool.EngineName);
    }

    [Fact]
    public async Task Auto_uses_podman_when_docker_does_not_answer()
    {
        Answer("docker-down");

        var tool = await ComposeTool.ResolveAsync(ContainerEngine.Auto, ComposeFile, Cancel);

        Assert.Equal(ContainerEngine.Podman, tool.Engine);
        Assert.Equal("podman", tool.EngineName);
    }

    [Fact]
    public async Task Auto_falls_back_on_the_first_installed_when_neither_answers()
    {
        Answer("docker-down");
        Answer("podman-down");

        var tool = await ComposeTool.ResolveAsync(ContainerEngine.Auto, ComposeFile, Cancel);

        Assert.Equal(ContainerEngine.Docker, tool.Engine);
    }

    [Fact]
    public async Task On_a_mac_podman_runs_with_the_temporary_folder_of_its_machines_socket()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "This is only done on macOS.");
        var socket = Folder.Write("machine tmp/podman/podman-machine-default-api.sock", "");
        Answer("socket", socket + "\n");
        var script = Folder.At("bin", "podman");
        File.AppendAllText(script, "");
        var tool = await ComposeTool.ResolveAsync(ContainerEngine.Podman, ComposeFile, Cancel);
        File.WriteAllText(script, "#!/bin/sh\necho \"TMPDIR=$TMPDIR\"\n");
        var lines = new List<string>();

        await tool.RunAsync(["anything"], lines.Add, Cancel);

        Assert.Equal([$"TMPDIR={Folder.At("machine tmp")}/"], lines);
    }

    [Fact]
    public async Task A_socket_elsewhere_or_not_there_leaves_the_temporary_folder_alone()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "This is only done on macOS.");
        var previous = Environment.GetEnvironmentVariable("TMPDIR");
        var socket = Folder.Write("not-the-usual-folder/api.sock", "");
        foreach (var answer in new[] { socket, Folder.At("gone/podman/api.sock") })
        {
            Answer("socket", answer + "\n");
            var script = Folder.At("bin", "podman");
            var original = File.ReadAllText(script);
            var tool = await ComposeTool.ResolveAsync(ContainerEngine.Podman, ComposeFile, Cancel);
            File.WriteAllText(script, "#!/bin/sh\necho \"TMPDIR=$TMPDIR\"\n");
            var lines = new List<string>();

            await tool.RunAsync(["anything"], lines.Add, Cancel);
            File.WriteAllText(script, original);

            // A machine's real gvproxy may still give it one, but never the folder of this socket.
            Assert.DoesNotContain(lines, l => l.Contains(Folder.Path, StringComparison.Ordinal));
        }

        Assert.Equal(previous, Environment.GetEnvironmentVariable("TMPDIR"));
    }

    [Fact]
    public void Terminal_runs_the_command_through_the_platforms_terminal()
    {
        var started = new List<(string Program, List<string> Arguments)>();
        Terminal.Start = (p, a) => started.Add((p, [.. a]));

        Terminal.Open("/bin/it's", ["exec", "a b"]);

        var (program, arguments) = Assert.Single(started);
        if (OperatingSystem.IsMacOS())
        {
            Assert.Equal("/usr/bin/osascript", program);
            Assert.Equal("-e", arguments[0]);
            Assert.Contains("""do script "'/bin/it'\\''s' 'exec' 'a b'" """.TrimEnd(), arguments[1]);
        }
        else
        {
            Assert.Contains("a b", arguments);
        }
    }
}
