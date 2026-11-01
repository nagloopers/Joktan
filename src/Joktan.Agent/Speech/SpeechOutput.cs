using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Joktan.Agent.Clients;
using Markdig;

namespace Joktan.Agent.Speech;

/// <summary>
/// Reads text aloud through what the operating system already has: <c>say</c> on macOS, Windows' speech
/// engine through PowerShell, and espeak or Speech Dispatcher on Linux. One thing is read at a time, in order.
/// The text goes in on standard input, so that no reply can be taken for an argument.
/// </summary>
public sealed partial class SpeechOutput : IDisposable
{
    /// <summary>The usual speed, in words a minute, that a speed of 100% stands for.</summary>
    private const int NormalWordsPerMinute = 175;

    private const string WindowsScript =
        "Add-Type -AssemblyName System.Speech; [Console]::InputEncoding = [Text.Encoding]::UTF8; " +
        "$s = New-Object System.Speech.Synthesis.SpeechSynthesizer; " +
        "if ($env:JOKTAN_VOICE) { $s.SelectVoice($env:JOKTAN_VOICE) }; " +
        "$s.Rate = [int]$env:JOKTAN_RATE; $s.Speak([Console]::In.ReadToEnd())";

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UsePipeTables().UseEmphasisExtras().UseAutoLinks().Build();

    private readonly object _gate = new();
    private readonly Queue<(string Text, string Voice, int Speed)> _waiting = new();
    private Process? _current;
    private bool _running;

    /// <summary>Raised on a background thread when reading starts or ends.</summary>
    public event Action<bool>? SpeakingChanged;

    /// <summary>The program that would do the reading, or null when this computer has none.</summary>
    public static string? FindProgram()
    {
        if (OperatingSystem.IsWindows())
            return AgentProgram.FindOnPath("powershell.exe");
        if (OperatingSystem.IsMacOS())
            return AgentProgram.FindOnPath("say");
        return new[] { "espeak-ng", "espeak", "spd-say" }.Select(AgentProgram.FindOnPath).FirstOrDefault(p => p is not null);
    }

    public void Say(string text, string voice, int speed)
    {
        if (text.Trim().Length == 0)
            return;

        lock (_gate)
        {
            _waiting.Enqueue((text, voice, speed));
            if (_running)
                return;
            _running = true;
        }

        SpeakingChanged?.Invoke(true);
        _ = Task.Run(ReadAllAsync);
    }

    /// <summary>Stops reading, and forgets what was waiting.</summary>
    public void Stop()
    {
        Process? current;
        lock (_gate)
        {
            _waiting.Clear();
            current = _current;
        }

        Kill(current);
    }

    public void Dispose() => Stop();

    private async Task ReadAllAsync()
    {
        while (true)
        {
            (string Text, string Voice, int Speed) next;
            lock (_gate)
            {
                if (!_waiting.TryDequeue(out next))
                {
                    _running = false;
                    break;
                }
            }

            try
            {
                await ReadAsync(next.Text, next.Voice, next.Speed);
            }
            catch (Exception e) when (e is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Replies are on the screen anyway, so speech that fails does not interrupt anything.
            }
        }

        SpeakingChanged?.Invoke(false);
    }

    private async Task ReadAsync(string text, string voice, int speed)
    {
        if (FindProgram() is not { } program)
            return;

        var start = new ProcessStartInfo(program)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
        };
        AddArguments(start, program, voice, speed);

        using var process = new Process { StartInfo = start };
        process.Start();
        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived += (_, _) => { };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        lock (_gate)
            _current = process;

        try
        {
            await process.StandardInput.WriteAsync(text);
            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // Stopped before it had read everything.
        }

        await process.WaitForExitAsync();
        lock (_gate)
            _current = null;
    }

    private static void AddArguments(ProcessStartInfo start, string program, string voice, int speed)
    {
        var words = NormalWordsPerMinute * speed / 100;
        var name = Path.GetFileNameWithoutExtension(program);
        var arguments = start.ArgumentList;
        switch (name)
        {
            case "powershell":
                start.Environment["JOKTAN_VOICE"] = voice;
                start.Environment["JOKTAN_RATE"] = Math.Clamp((speed - 100) / 10, -10, 10).ToString(System.Globalization.CultureInfo.InvariantCulture);
                arguments.Add("-NoProfile");
                arguments.Add("-NonInteractive");
                arguments.Add("-Command");
                arguments.Add(WindowsScript);
                break;
            case "spd-say":
                arguments.Add("-w");
                arguments.Add("-e");
                arguments.Add("-r");
                arguments.Add(Math.Clamp(speed - 100, -100, 100).ToString(System.Globalization.CultureInfo.InvariantCulture));
                break;
            case "espeak" or "espeak-ng":
                arguments.Add("--stdin");
                arguments.Add("-s");
                arguments.Add(words.ToString(System.Globalization.CultureInfo.InvariantCulture));
                if (voice.Length > 0)
                {
                    arguments.Add("-v");
                    arguments.Add(voice);
                }

                break;
            default:
                arguments.Add("-r");
                arguments.Add(words.ToString(System.Globalization.CultureInfo.InvariantCulture));
                if (voice.Length > 0)
                {
                    arguments.Add("-v");
                    arguments.Add(voice);
                }

                break;
        }
    }

    /// <summary>The voices to choose from. Empty where the system gives no simple list.</summary>
    public static async Task<IReadOnlyList<string>> VoicesAsync()
    {
        if (FindProgram() is not { } program)
            return [];

        var start = new ProcessStartInfo(program) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        if (OperatingSystem.IsMacOS())
        {
            start.ArgumentList.Add("-v");
            start.ArgumentList.Add("?");
        }
        else if (OperatingSystem.IsWindows())
        {
            start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-Command");
            start.ArgumentList.Add("Add-Type -AssemblyName System.Speech; (New-Object System.Speech.Synthesis.SpeechSynthesizer).GetInstalledVoices() | ForEach-Object { $_.VoiceInfo.Name }");
        }
        else
        {
            return [];
        }

        try
        {
            using var process = Process.Start(start);
            if (process is null)
                return [];
            process.ErrorDataReceived += (_, _) => { };
            process.BeginErrorReadLine();
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            var lines = output.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            return OperatingSystem.IsMacOS()
                ? lines.Select(MacVoiceName).Where(n => n.Length > 0).Distinct().Order().ToList()
                : lines.Order().ToList();
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return [];
        }
    }

    // A line reads "Eddy (German (Germany))  de_DE    # Hallo!": the name ends where the language code starts.
    private static string MacVoiceName(string line)
    {
        var match = MacVoice().Match(line);
        return match.Success ? match.Groups[1].Value.Trim() : "";
    }

    /// <summary>What to read of a reply: its words without the Markdown, and code only named, as it does not read well aloud.</summary>
    public static string SpokenText(string markdown)
    {
        var text = FencedCode().Replace(markdown, "\n\nCode left out.\n\n");
        return Whitespace().Replace(Markdown.ToPlainText(text, Pipeline), " ").Trim();
    }

    private static void Kill(Process? process)
    {
        try
        {
            process?.Kill(entireProcessTree: true);
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            // It has already ended.
        }
    }

    [GeneratedRegex(@"^(.+?)\s+[a-z]{2,3}[_-][A-Za-z0-9_-]+\s+#")]
    private static partial Regex MacVoice();

    [GeneratedRegex(@"^(```|~~~).*?(^\1\s*$|\z)", RegexOptions.Multiline | RegexOptions.Singleline)]
    private static partial Regex FencedCode();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
