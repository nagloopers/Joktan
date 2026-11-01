using System;
using System.IO;
using System.Runtime.Versioning;
using Joktan.Containers.ViewModels;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Containers.Tests.ViewModels;

/// <summary>
/// Small scripts standing in for docker and podman, found first on PATH, and compose files of their
/// own, so that no real engine or container is touched. Files beside them choose what they answer.
/// </summary>
[Collection(ProcessStateCollection.Name)]
public abstract class FakeEngineTest : IDisposable
{
    private const string Script = """
        #!/bin/sh
        dir="$(dirname "$0")/.."
        name="$(basename "$0")"
        echo "$name $*" >> "$dir/calls"
        case "$*" in
          info) [ -e "$dir/$name-down" ] && exit 1; exit 0 ;;
          "machine inspect"*) cat "$dir/socket" 2>/dev/null; exit 0 ;;
          *"config --services"*)
            [ -e "$dir/status-fails" ] && { echo "status went wrong" >&2; exit 1; }
            cat "$dir/services"; exit 0 ;;
          *"ps --all"*) cat "$dir/ps"; exit 0 ;;
          *"config --format json"*)
            [ -e "$dir/name-fails" ] && { echo "no such format" >&2; exit 1; }
            cat "$dir/config"; exit 0 ;;
          *" logs "*)
            echo "a log line"
            [ -e "$dir/logs-hang" ] && exec sleep 30
            [ -e "$dir/logs-exit" ] && exit "$(cat "$dir/logs-exit")"
            exit 0 ;;
        esac
        [ -e "$dir/command-hangs" ] && exec sleep 30
        echo "ran $*"
        [ -e "$dir/command-exit" ] && exit "$(cat "$dir/command-exit")"
        exit 0
        """;

    private readonly string? _path = Environment.GetEnvironmentVariable("PATH");
    private readonly Action<string, System.Collections.Generic.IEnumerable<string>> _start = Terminal.Start;

    [UnsupportedOSPlatform("windows")]
    protected FakeEngineTest()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The stand-ins for the engines are shell scripts.");

        foreach (var name in new[] { "docker", "podman" })
        {
            var script = Folder.Write($"bin/{name}", Script.ReplaceLineEndings("\n"));
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        Environment.SetEnvironmentVariable("PATH", Folder.At("bin") + Path.PathSeparator + _path);
        Terminal.Start = (_, _) => throw new InvalidOperationException("A test opened a terminal without saying what it expects.");
        Answer("services", "broker\nweb\n");
        Answer("ps", """{"Service":"broker","Name":"p-broker-1","State":"running","Status":"Up 2 minutes","Image":"mosquitto","Health":"healthy"}""" + "\n");
        Answer("config", """{"name":"proj"}""");
        ComposeFile = Folder.Write("project/docker-compose.yml", "services:\n  broker:\n  web:\n");
    }

    protected TempFolder Folder { get; } = new();

    protected string ComposeFile { get; }

    /// <summary>What the stand-ins have been asked to do, one command a line.</summary>
    protected string Calls => File.Exists(Folder.At("calls")) ? File.ReadAllText(Folder.At("calls")) : "";

    protected void Answer(string file, string content = "") => Folder.Write(file, content);

    protected void NoAnswer(string file) => File.Delete(Folder.At(file));

    protected string DataFolder => Folder.At("data");

    /// <summary>A page on the stand-in for docker, with these compose files, or the test's own.</summary>
    protected ContainersViewModel Page(params string[] files)
    {
        new ComposeSettings
        {
            ComposeFiles = files.Length > 0 ? [.. files] : [ComposeFile],
            Engine = ContainerEngine.Docker,
        }.Save(DataFolder);
        return new ContainersViewModel(DataFolder);
    }

    protected async System.Threading.Tasks.Task<ComposeProjectViewModel> Read(ContainersViewModel page)
    {
        var project = page.Projects[0];
        await project.RefreshStatusAsync();
        return project;
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("PATH", _path);
        Terminal.Start = _start;
        Folder.Dispose();
        GC.SuppressFinalize(this);
    }
}
