using System;
using System.Collections.Generic;
using System.Linq;
using Joktan.Agent.Mcp;
using Joktan.Mqtt;
using Joktan.Mqtt.ViewModels;
using static Joktan.Agent.Tools.AgentTools;

namespace Joktan.Agent.Tools;

public static class InboxTools
{
    public static IEnumerable<McpTool> Create(InboxViewModel inbox, MessagingViewModel messaging) =>
    [
        Read("inbox_messages", "Read the messages in the top bar's Messages panel, newest last.", [P.OptNumber("limit", "How many; 50 unless given.")], args =>
            inbox.Messages.TakeLast(args.Whole("limit") ?? 50).Select(m => new { m.Name, m.Text, m.Time, m.IsOwn, To = m.DirectText, Reply = m.ReplyLine }).ToList()),
        Read("inbox_alerts", "Read the alerts in the top bar's Alerts panel.", [P.OptNumber("limit", "How many; 50 unless given.")], args =>
            inbox.Alerts.Take(args.Whole("limit") ?? 50).Select(a => new { a.Level, a.Title, a.Text, a.Time }).ToList()),
        RiskyAsync("inbox_send_message", "Send a message to everyone on the messaging server, or to one person by starting it with @name.", [P.Text("text", "The message.")], async args =>
        {
            inbox.Draft = args.Text("text");
            if (!inbox.SendCommand.CanExecute(null))
                throw new ToolFailure("It cannot be sent; the messaging server may not be connected.");
            await inbox.SendCommand.ExecuteAsync(null);
            Check(inbox.SendError);
            return "Sent.";
        }),
        RiskyAsync("inbox_send_alert", "Send an alert to everyone on the messaging server.", [P.Text("text", "The alert."), P.Choice("level", "How serious it is.", Enum.GetNames<AlertSeverity>())], async args =>
        {
            inbox.AlertDraft = args.Text("text");
            inbox.AlertLevel = Enum.Parse<AlertSeverity>(args.Text("level"), ignoreCase: true);
            if (!inbox.SendAlertCommand.CanExecute(null))
                throw new ToolFailure("It cannot be sent; the messaging server may not be connected.");
            await inbox.SendAlertCommand.ExecuteAsync(null);
            Check(inbox.SendError);
            return "Sent.";
        }),
        Risky("inbox_clear", "Delete the messages or alerts kept on this computer.", [P.Choice("what", "Which.", "messages", "alerts")], args =>
        {
            if (args.Text("what") == "alerts")
                inbox.ClearAlertsCommand.Execute(null);
            else
                inbox.ClearMessagesCommand.Execute(null);
            return "Cleared.";
        }),
        Read("messaging_settings", "Read the settings of alerts and messages.", [], _ => new
        {
            messaging.TopicPrefix, messaging.DisplayName, messaging.ShowInBrokers, messaging.ConnectOnStart, Server = messaging.Client.Address, State = messaging.Client.StateText,
        }),
        Change("messaging_settings_update", "Change the settings of alerts and messages. Only what is given changes.",
            [P.OptText("topic_prefix", "What the topics start with."), P.OptText("display_name", "The name shown to others."), P.OptFlag("show_in_brokers", "Whether the server has a tab on the MQTT page."), P.OptFlag("connect_on_start", "Whether to connect when the app starts.")], args =>
            {
                if (args.OptText("topic_prefix") is { } prefix)
                    messaging.TopicPrefix = prefix;
                if (args.OptText("display_name") is { } name)
                    messaging.DisplayName = name;
                if (args.Flag("show_in_brokers") is { } show)
                    messaging.ShowInBrokers = show;
                if (args.Flag("connect_on_start") is { } connect)
                    messaging.ConnectOnStart = connect;
                Check(messaging.TopicPrefixError ?? messaging.DisplayNameError);
                return "Saved.";
            }),
    ];
}
