using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Joktan.Agent.Resources;

namespace Joktan.Agent.Clients;

/// <summary>Finds a client's program and runs it for one turn.</summary>
public static class AgentProgram
{
    // An app started from the Finder or a desktop launcher gets a short PATH, without the folders that
    // Homebrew and the usual installers put these commands in.
    private static IEnumerable<string> ExtraFolders()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return
        [
            "/opt/homebrew/bin",
            "/usr/local/bin",
            Path.Combine(home, ".local", "bin"),
            Path.Combine(home, ".claude", "local"),
            Path.Combine(home, ".opencode", "bin"),
            Path.Combine(home, ".npm-global", "bin"),
            Path.Combine(home, ".bun", "bin"),
            "/usr/bin",
            "/bin",
        ];
    }

    private static IEnumerable<string> SearchPath()
        => (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Concat(ExtraFolders())
            .Distinct();

    /// <summary>The whole path of a program found by name in the usual folders, or null.</summary>
    public static string? FindOnPath(string name)
        => SearchPath().Select(folder => Path.Combine(folder, name)).FirstOrDefault(File.Exists);

    /// <summary>The whole path of a client's program, or null when it is not there.</summary>
    public static string? Find(AgentClient client, AgentClientProfile profile)
    {
        var chosen = profile.Path.Trim();
        if (chosen.StartsWith('~'))
            chosen = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), chosen[1..].TrimStart('/', '\\'));
        if (chosen.Length > 0)
            return File.Exists(chosen) ? Path.GetFullPath(chosen) : null;
        if (client.ProgramName.Length == 0)
            return null;

        // Only a real program on Windows: a .cmd goes through cmd.exe, whose quoting a prompt could break out of.
        var name = OperatingSystem.IsWindows() ? client.ProgramName + ".exe" : client.ProgramName;
        return SearchPath().Select(folder => Path.Combine(folder, name)).FirstOrDefault(File.Exists);
    }

    /// <summary>What the program says its version is, or null when it would not say.</summary>
    public static async Task<string?> VersionAsync(string program)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            using var process = Process.Start(Start(program));
            if (process is null)
                return null;
            process.StandardInput.Close();
            var output = await process.StandardOutput.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            return process.ExitCode == 0 ? output.Trim().Split('\n')[0] : null;
        }
        catch (Exception e) when (e is OperationCanceledException or System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            return null;
        }

        static ProcessStartInfo Start(string program)
        {
            var start = NewStart(program, Environment.CurrentDirectory);
            start.ArgumentList.Add("--version");
            return start;
        }
    }

    /// <summary>Runs the program until it ends or is cancelled, and gives what it printed as errors when it failed.</summary>
    public static async Task<string?> RunAsync(string program, AgentLaunch launch, string workingFolder, Action<string> line, CancellationToken cancel)
    {
        Directory.CreateDirectory(workingFolder);
        var start = NewStart(program, workingFolder);
        foreach (var argument in launch.Arguments)
            start.ArgumentList.Add(argument);
        foreach (var (name, value) in launch.Environment)
            start.Environment[name] = value;

        using var process = new Process { StartInfo = start };
        process.Start();

        var errors = new Queue<string>();
        await using var kill = cancel.Register(() => Kill(process));

        // Always closed, so that a program reading its input does not wait for a person.
        try
        {
            if (launch.Input is { } input)
                await process.StandardInput.WriteAsync(input);
            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // It ended before reading; how shows below.
        }

        var reading = Task.Run(async () =>
        {
            while (await process.StandardError.ReadLineAsync() is { } text)
            {
                lock (errors)
                {
                    errors.Enqueue(text);
                    if (errors.Count > 20)
                        errors.Dequeue();
                }
            }
        }, CancellationToken.None);

        try
        {
            while (await process.StandardOutput.ReadLineAsync(CancellationToken.None) is { } text)
                line(text);

            await process.WaitForExitAsync(CancellationToken.None);
            await reading;
        }
        catch
        {
            Kill(process);
            // Observe both cleanup operations without replacing the original failure.
            try { await process.WaitForExitAsync(CancellationToken.None); }
            catch (Exception) { }
            try { await reading; }
            catch (Exception) { }
            throw;
        }
        if (cancel.IsCancellationRequested || process.ExitCode == 0)
            return null;

        lock (errors)
            return errors.Count > 0 ? string.Join("\n", errors) : string.Format(Strings.AgentProgram_ItStoppedWithExitCode, process.ExitCode);
    }

    private static ProcessStartInfo NewStart(string program, string workingFolder)
    {
        var start = new ProcessStartInfo(program)
        {
            WorkingDirectory = workingFolder,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
            StandardInputEncoding = new System.Text.UTF8Encoding(false),
        };

        // These programs often start through a script that looks for node on the PATH.
        start.Environment["PATH"] = string.Join(Path.PathSeparator, SearchPath());
        return start;
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            // It has already ended.
        }
    }
}
