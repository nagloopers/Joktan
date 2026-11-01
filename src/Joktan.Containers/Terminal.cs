using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Joktan.Containers.Resources;

namespace Joktan.Containers;

/// <summary>Opens the system's terminal running one command, such as a shell inside a container.</summary>
public static class Terminal
{
    /// <summary>Starts a program with its arguments. Replaced in tests, so that no real terminal opens.</summary>
    public static Action<string, IEnumerable<string>> Start { get; set; } = StartProcess;

    public static void Open(string executable, IReadOnlyList<string> arguments)
    {
        if (OperatingSystem.IsMacOS())
        {
            var command = string.Join(" ", new[] { executable }.Concat(arguments).Select(ShellQuote));
            var script = $"tell application \"Terminal\"\n activate\n do script \"{command.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"\nend tell";
            Start("/usr/bin/osascript", ["-e", script]);
        }
        else if (OperatingSystem.IsWindows())
        {
            Start("cmd.exe", ["/c", "start", "", executable, .. arguments]);
        }
        else
        {
            var terminal = new[] { "x-terminal-emulator", "gnome-terminal", "konsole", "xterm" }
                .FirstOrDefault(t => (Environment.GetEnvironmentVariable("PATH") ?? "")
                    .Split(Path.PathSeparator).Any(folder => File.Exists(Path.Combine(folder, t))))
                ?? throw new PlatformNotSupportedException(Strings.Terminal_NoTerminalWasFound);
            Start(terminal, terminal == "gnome-terminal" ? ["--", executable, .. arguments] : ["-e", executable, .. arguments]);
        }
    }

    private static void StartProcess(string executable, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start);
    }

    private static string ShellQuote(string value) => "'" + value.Replace("'", "'\\''") + "'";
}
