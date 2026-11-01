using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Data.Converters;
using Joktan.Containers.Resources;

namespace Joktan.Containers;

public enum ContainerEngine
{
    /// <summary>Docker when its engine answers, Podman otherwise.</summary>
    Auto,
    Docker,
    Podman,
}

public static class ContainerEngineText
{
    /// <summary>What a ContainerEngine is called on screen.</summary>
    public static string Text(this ContainerEngine value) => value switch
    {
        ContainerEngine.Auto => Strings.ContainerEngine_Auto,
        _ => value.ToString(),
    };

    public static readonly FuncValueConverter<ContainerEngine, string> ContainerEngineConverter = new(value => value.Text());
}

/// <summary>
/// Runs <c>docker compose</c> or <c>podman compose</c> against one compose file, from the file's
/// folder so that its <c>.env</c> is read.
/// </summary>
public sealed partial class ComposeTool
{
    // An app started from the Finder or a desktop launcher gets a short PATH, without the folders
    // that Homebrew, Docker Desktop and the Podman installer put their commands in.
    private static readonly string[] ExtraFolders =
    [
        "/opt/homebrew/bin",
        "/usr/local/bin",
        "/opt/podman/bin",
        "/Applications/Docker.app/Contents/Resources/bin",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".docker", "bin"),
        "/usr/bin",
        "/bin",
    ];

    private readonly string? _temporaryFolder;

    private ComposeTool(ContainerEngine engine, string executable, string composeFile, string? temporaryFolder)
    {
        Engine = engine;
        Executable = executable;
        ComposeFile = composeFile;
        _temporaryFolder = temporaryFolder;
    }

    /// <summary>Docker or Podman, never Auto.</summary>
    public ContainerEngine Engine { get; }

    public string Executable { get; }

    public string ComposeFile { get; }

    public string EngineName => Engine == ContainerEngine.Docker ? "docker" : "podman";

    /// <summary>The same engine and command, for another compose file.</summary>
    public ComposeTool For(string composeFile) => new(Engine, Executable, composeFile, _temporaryFolder);

    /// <summary>The command line for <paramref name="arguments"/>, to show the user.</summary>
    public string CommandLine(IEnumerable<string> arguments)
        => string.Join(" ", new[] { EngineName, "compose" }.Concat(arguments).Select(a => a.Contains(' ') ? $"\"{a}\"" : a));

    /// <summary>The full path of an engine's command, or null when it is not installed.</summary>
    public static string? Find(ContainerEngine engine)
    {
        var name = engine == ContainerEngine.Docker ? "docker" : "podman";
        if (OperatingSystem.IsWindows())
            name += ".exe";

        return SearchPath().Select(folder => Path.Combine(folder, name)).FirstOrDefault(File.Exists);
    }

    /// <summary>
    /// The tool for <paramref name="engine"/>. For Auto, Docker is used when its engine answers,
    /// then Podman when it does, then whichever is installed. Throws when neither is installed.
    /// </summary>
    public static async Task<ComposeTool> ResolveAsync(ContainerEngine engine, string composeFile, CancellationToken cancel = default)
    {
        var installed = (engine == ContainerEngine.Auto ? [ContainerEngine.Docker, ContainerEngine.Podman] : new[] { engine })
            .Select(e => (Engine: e, Path: Find(e)))
            .Where(e => e.Path is not null)
            .ToList();

        if (installed.Count == 0)
        {
            throw new InvalidOperationException(engine switch
            {
                ContainerEngine.Docker => Strings.ComposeTool_DockerIsNotInstalledOrIts,
                ContainerEngine.Podman => Strings.ComposeTool_PodmanIsNotInstalledOrIts,
                _ => Strings.ComposeTool_NeitherDockerNorPodmanIsInstalled,
            });
        }

        var chosen = installed[0];
        if (installed.Count > 1)
        {
            foreach (var candidate in installed)
            {
                if (await AnswersAsync(candidate.Path!, cancel))
                {
                    chosen = candidate;
                    break;
                }
            }
        }

        var temporaryFolder = chosen.Engine == ContainerEngine.Podman && OperatingSystem.IsMacOS()
            ? await PodmanTemporaryFolderAsync(chosen.Path!, cancel)
            : null;
        return new ComposeTool(chosen.Engine, chosen.Path!, composeFile, temporaryFolder);
    }

    /// <summary>
    /// Runs <c>compose -f file</c> followed by <paramref name="arguments"/>, passing each line it
    /// writes, from either stream, to <paramref name="line"/> on a background thread. Returns the
    /// exit code. Cancelling stops the command and everything it started.
    /// </summary>
    public Task<int> RunAsync(IEnumerable<string> arguments, Action<string> line, CancellationToken cancel = default)
        => RunAsync(Executable, ["compose", "-f", ComposeFile, .. arguments], line, cancel);

    /// <summary>Runs an engine command that is not a compose one, such as <c>podman unpause</c>, as <see cref="RunAsync"/> does.</summary>
    public Task<int> RunEngineAsync(IEnumerable<string> arguments, Action<string> line, CancellationToken cancel = default)
        => RunAsync(Executable, arguments, line, cancel);

    /// <summary>The command line for an engine command, to show the user.</summary>
    public string EngineCommandLine(IEnumerable<string> arguments)
        => string.Join(" ", new[] { EngineName }.Concat(arguments).Select(a => a.Contains(' ') ? $"\"{a}\"" : a));

    /// <summary>Runs a compose command and returns what it wrote to standard output; throws if it fails.</summary>
    public async Task<string> ReadAsync(IEnumerable<string> arguments, CancellationToken cancel = default)
    {
        var command = arguments.ToList();
        var output = new List<string>();
        var errors = new List<string>();
        var exitCode = await RunAsync(Executable, ["compose", "-f", ComposeFile, .. command], output.Add, cancel, errors.Add);
        if (exitCode != 0)
        {
            var message = errors.LastOrDefault(e => e.Length > 0) ?? string.Format(Strings.ComposeTool_FailedWithExitCode, CommandLine(command), exitCode);
            throw new InvalidOperationException(message);
        }

        return string.Join("\n", output);
    }

    private async Task<int> RunAsync(string executable, IEnumerable<string> arguments, Action<string> output,
        CancellationToken cancel, Action<string>? errors = null)
    {
        var start = StartInfo(executable, arguments);
        start.WorkingDirectory = Path.GetDirectoryName(ComposeFile) ?? "";
        if (_temporaryFolder is not null)
            start.Environment["TMPDIR"] = _temporaryFolder;

        return await Run(start, output, errors ?? output, cancel);
    }

    private static ProcessStartInfo StartInfo(string executable, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        // podman compose runs a compose provider found on PATH.
        start.Environment["PATH"] = string.Join(Path.PathSeparator, SearchPath());
        start.Environment["COMPOSE_ANSI"] = "never";
        start.Environment["COMPOSE_PROGRESS"] = "plain";
        start.Environment["BUILDKIT_PROGRESS"] = "plain";
        start.Environment["NO_COLOR"] = "1";
        // Otherwise podman compose starts with a banner naming the provider it runs.
        start.Environment["PODMAN_COMPOSE_WARNING_LOGS"] = "false";
        return start;
    }

    private static async Task<int> Run(ProcessStartInfo start, Action<string> output, Action<string> errors, CancellationToken cancel)
    {
        using var process = new Process();
        process.StartInfo = start;
        var outputDone = new TaskCompletionSource();
        var errorsDone = new TaskCompletionSource();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
                outputDone.TrySetResult();
            else
                output(Clean(e.Data));
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
                errorsDone.TrySetResult();
            else
                errors(Clean(e.Data));
        };

        process.Start();
        process.StandardInput.Close();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await using (cancel.Register(() => Kill(process)))
        {
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(outputDone.Task, errorsDone.Task);
        }

        cancel.ThrowIfCancellationRequested();
        return process.ExitCode;
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // It had already exited.
        }
    }

    private static async Task<bool> AnswersAsync(string executable, CancellationToken cancel)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            // Fails when the engine, or Podman's virtual machine, is not running.
            return await Run(StartInfo(executable, ["info"]), _ => { }, _ => { }, timeout.Token) == 0;
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            return false;
        }
    }

    /// <summary>
    /// The TMPDIR that podman compose needs on macOS. It looks for the Podman machine's socket in
    /// <c>$TMPDIR/podman/</c>, but the socket is wherever TMPDIR pointed when the machine started,
    /// which need not be this app's TMPDIR; <c>podman machine inspect</c> only reports where it
    /// would be for the current TMPDIR. The machine's gvproxy process is given the real path.
    /// </summary>
    private static async Task<string?> PodmanTemporaryFolderAsync(string podman, CancellationToken cancel)
    {
        var candidates = new List<string>();
        try
        {
            await Run(StartInfo(podman, ["machine", "inspect", "--format", "{{.ConnectionInfo.PodmanSocket.Path}}"]),
                candidates.Add, _ => { }, cancel);
            await Run(StartInfo("/bin/ps", ["-axww", "-o", "command="]), line =>
            {
                if (line.Contains("gvproxy", StringComparison.Ordinal) && GvproxySocket().Match(line) is { Success: true } match)
                    candidates.Add(match.Groups[1].Value);
            }, _ => { }, cancel);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
        }

        // .../podman/podman-machine-default-api.sock: TMPDIR is the folder above podman/.
        var socket = candidates.Select(c => c.Trim()).FirstOrDefault(c => c.EndsWith(".sock", StringComparison.Ordinal) && File.Exists(c));
        var socketFolder = Path.GetDirectoryName(socket);
        return socketFolder is not null && Path.GetFileName(socketFolder) == "podman"
            ? Path.GetDirectoryName(socketFolder) + Path.DirectorySeparatorChar
            : null;
    }

    // The path may contain spaces, so it runs to the .sock before the next option.
    /// <summary>
    /// Puts services in the order they are written in the compose file. <c>config --services</c>
    /// lists them in no fixed order. Services not found in the file, such as ones from an included
    /// file, come after the others in alphabetical order.
    /// </summary>
    public static List<string> InFileOrder(string composeFile, IEnumerable<string> services)
    {
        var positions = new Dictionary<string, int>(StringComparer.Ordinal);
        try
        {
            var lines = File.ReadAllLines(composeFile);
            var start = Array.FindIndex(lines, l => ServicesKey().IsMatch(l));
            string? indent = null;
            for (var i = start + 1; start >= 0 && i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.Trim().Length == 0 || line.TrimStart().StartsWith('#'))
                    continue;
                // The next top-level key ends the services.
                if (!char.IsWhiteSpace(line[0]))
                    break;

                // The services are the keys at the indentation of the first one.
                indent ??= line[..(line.Length - line.TrimStart().Length)];
                if (line.StartsWith(indent, StringComparison.Ordinal) && !char.IsWhiteSpace(line[indent.Length])
                    && ServiceKey().Match(line[indent.Length..]) is { Success: true } key)
                    positions.TryAdd(key.Groups[1].Value.Trim('"', '\''), positions.Count);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // An unreadable file leaves the services in alphabetical order.
        }

        return services
            .OrderBy(s => positions.TryGetValue(s, out var position) ? position : int.MaxValue)
            .ThenBy(s => s, StringComparer.Ordinal)
            .ToList();
    }

    [GeneratedRegex(@"^services:\s*(#.*)?$")]
    private static partial Regex ServicesKey();

    [GeneratedRegex(@"^(""[^""]+""|'[^']+'|[^\s:#]+)\s*:")]
    private static partial Regex ServiceKey();

    [GeneratedRegex(@"-forward-sock (.+?\.sock)(?= -|$)")]
    private static partial Regex GvproxySocket();

    private static IEnumerable<string> SearchPath()
        => (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Concat(OperatingSystem.IsWindows() ? [] : ExtraFolders)
            .Distinct();

    // Colour and cursor codes, for a compose provider that writes them anyway.
    private static string Clean(string line) => AnsiCode().Replace(line, "");

    [GeneratedRegex(@"\x1B\[[0-9;?]*[A-Za-z]")]
    private static partial Regex AnsiCode();
}
