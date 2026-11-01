using System.IO;
using System.Linq;
using Joktan.Core;
using Joktan.Mqtt.Resources;

namespace Joktan.Mqtt;

/// <summary>
/// The MQTT server that alerts and messages go through, kept apart from the clients on the MQTT page,
/// and how the user appears in messages. Specified in docs/features/alerts-and-messages/spec.md.
/// </summary>
public sealed class MessagingSettings
{
    /// <summary>The name the server starts with, which is also its tab's when shown.</summary>
    public const string DefaultServerName = "messaging";

    public const int MaxDisplayNameLength = 15;

    /// <summary>The name of someone who has not chosen one. They can send to everyone, but cannot be answered.</summary>
    public const string AnonymousName = "anonymous";

    public const string DefaultTopicPrefix = "joktan";

    /// <summary>How the server is reached. Passwords are not part of it; see <see cref="CredentialStore"/>.</summary>
    public ClientProfile? Server { get; set; }

    /// <summary>Whether the server also has a tab on the MQTT page and in the topic explorer.</summary>
    public bool ShowInBrokers { get; set; }

    /// <summary>Whether Joktan connects to the server when it starts, trying a few times.</summary>
    public bool ConnectOnStart { get; set; } = true;

    /// <summary>What every topic of alerts and messages starts with. Those who share a prefix share messages.</summary>
    public string TopicPrefix { get; set; } = DefaultTopicPrefix;

    /// <summary>The name on the user's messages; empty until one is chosen.</summary>
    public string DisplayName { get; set; } = "";

    /// <summary>What is wrong with a display name, or null. It goes after an @ and in a topic.</summary>
    public static string? CheckName(string name)
    {
        if (name.Length > MaxDisplayNameLength)
            return string.Format(Strings.MessagingSettings_TheNameMustBeAtMost, MaxDisplayNameLength);
        return name.All(IsNameCharacter) ? null : Strings.MessagingSettings_TheNameMayUseLowercaseLetters;
    }

    /// <summary>Makes a name from elsewhere fit the rule, so that nobody can look unlike a name in a list.</summary>
    public static string NormalizeName(string name)
    {
        var fitting = new string(name.ToLowerInvariant().Where(IsNameCharacter).Take(MaxDisplayNameLength).ToArray());
        return fitting.Length > 0 ? fitting : AnonymousName;
    }

    private static bool IsNameCharacter(char c) => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-';

    /// <summary>The small picture on the user's messages, if there is one.</summary>
    public static string AvatarPath => Path.Combine(DataFolder.Path, "messaging-avatar.png");
}
