using System.Text.RegularExpressions;

namespace Joktan.Agent.Speech;

public enum VoiceCommandKind
{
    None,
    Send,
    Stop,
    ClearChat,
}

/// <summary>
/// Reads a command in what was said. "stop" and "clear chat" count only as all that was said, so "please
/// stop the container" stays a request to the agent. Ignores case and the punctuation speech recognition adds.
/// </summary>
public static partial class VoiceCommand
{
    /// <summary>The command, and the message left without it.</summary>
    public static (VoiceCommandKind Kind, string Message) Read(string spoken)
    {
        var whole = Punctuation().Replace(spoken, "");
        if (whole.Equals("stop", System.StringComparison.OrdinalIgnoreCase))
            return (VoiceCommandKind.Stop, "");
        if (ClearChat().IsMatch(whole))
            return (VoiceCommandKind.ClearChat, "");

        var send = EndsWithSend().Match(spoken);
        return send.Success ? (VoiceCommandKind.Send, send.Groups[1].Value.Trim()) : (VoiceCommandKind.None, spoken.Trim());
    }

    [GeneratedRegex(@"^[\s\p{P}]+|[\s\p{P}]+$")]
    private static partial Regex Punctuation();

    [GeneratedRegex(@"^clear\s+(the\s+)?chat$", RegexOptions.IgnoreCase)]
    private static partial Regex ClearChat();

    [GeneratedRegex(@"^(.*?)[\s\p{P}]*\bsend[\s\p{P}]*$", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex EndsWithSend();
}
