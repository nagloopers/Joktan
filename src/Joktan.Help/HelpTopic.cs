using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Avalonia.Platform;
using Joktan.Help.Resources;

namespace Joktan.Help;

/// <summary>One page of help: <c>Topics/&lt;id&gt;.md</c>, built into this assembly.</summary>
/// <param name="Parent">The topic this one sits below, or null for one at the top.</param>
public sealed record HelpTopic(string Id, string Title, string? Parent = null)
{
    public const string Home = "getting-started";

    /// <summary>The topics in the order they are listed, which follows the main menu.</summary>
    public static IReadOnlyList<HelpTopic> All { get; } =
    [
        new(Home, Strings.HelpTopic_GettingStarted),
        new("settings-and-data", Strings.HelpTopic_SettingsAndData),
        new("themes", Strings.HelpTopic_Themes, Strings.HelpTopic_SettingsAndData),
        new("search", Strings.HelpTopic_Search, Strings.HelpTopic_SettingsAndData),
        new("alerts-and-messages", Strings.HelpTopic_AlertsAndMessages, Strings.HelpTopic_SettingsAndData),
        new("folders-and-backup", Strings.HelpTopic_FoldersAndBackup, Strings.HelpTopic_SettingsAndData),
        new("agent", Strings.HelpTopic_Agent, Strings.HelpTopic_SettingsAndData),
        new("dashboard", Strings.HelpTopic_Dashboard),
        new("topic-dashboards", Strings.HelpTopic_MQTTDashboards),
        new("notes", Strings.HelpTopic_Notes),
        new("passwords", Strings.HelpTopic_Passwords),
        new("pomodoro", Strings.HelpTopic_Pomodoro),
        new("mqtt", Strings.HelpTopic_MQTT),
        new("mqtt-clients", Strings.HelpTopic_ClientsAndBrokers, Strings.HelpTopic_MQTT),
        new("mqtt-tls", Strings.HelpTopic_TLSAndMutualTLS, Strings.HelpTopic_MQTT),
        new("mqtt-folder-messages", Strings.HelpTopic_FolderMessages, Strings.HelpTopic_MQTT),
        new("mqtt-message-settings", Strings.HelpTopic_MessageSettings, Strings.HelpTopic_MQTT),
        new("mqtt-varying-fields", Strings.HelpTopic_VaryingFields, Strings.HelpTopic_MQTT),
        new("topic-explorer", Strings.HelpTopic_TopicExplorer),
        new("certificates", Strings.HelpTopic_Certificates),
        new("api-client", Strings.HelpTopic_APIClient),
        new("api-client-grpc", Strings.HelpTopic_GRPCRequests, Strings.HelpTopic_APIClient),
        new("api-client-variables", Strings.HelpTopic_EnvironmentsAndVariables, Strings.HelpTopic_APIClient),
        new("containers", Strings.HelpTopic_Containers),
    ];

    public string ReadMarkdown()
    {
        using var stream = AssetLoader.Open(MarkdownUri());
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>The topic in the language showing, from <c>Topics/&lt;language&gt;/&lt;id&gt;.md</c>, or else in English.</summary>
    private Uri MarkdownUri()
    {
        for (var culture = CultureInfo.CurrentUICulture; culture.Name.Length > 0; culture = culture.Parent)
        {
            var translated = new Uri($"avares://Joktan.Help/Topics/{culture.Name}/{Id}.md");
            if (AssetLoader.Exists(translated))
                return translated;
        }

        return new Uri($"avares://Joktan.Help/Topics/{Id}.md");
    }
}
