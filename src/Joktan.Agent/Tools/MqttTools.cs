using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Joktan.Agent.Mcp;
using Joktan.Mqtt;
using Joktan.Mqtt.ViewModels;
using static Joktan.Agent.Tools.AgentTools;

namespace Joktan.Agent.Tools;

public static class MqttTools
{
    private static readonly ToolParameter ClientName = P.Text("client", "The client's name, from mqtt_clients.");

    private static readonly ToolParameter[] ClientFields =
    [
        P.OptText("host", "The broker's host."), P.OptText("port", "The broker's port."), P.OptText("username", "The user to log in as."),
        P.OptText("password", "The password to log in with."), P.OptText("client_id", "The MQTT client id."), P.OptFlag("use_tls", "Whether to use TLS."),
        P.OptText("ca_certificate", "The path of the CA certificate."), P.OptText("client_certificate", "The path of the client certificate."),
        P.OptText("client_key", "The path of the client's private key."), P.OptText("key_password", "The private key's password."),
        P.OptFlag("accept_any_server_certificate", "Accept any certificate from the broker. Only for testing."),
    ];

    public static IEnumerable<McpTool> Create(MqttViewModel mqtt) =>
    [
        Read("mqtt_clients", "List the MQTT clients on the MQTT page, with their brokers and state. Passwords are not given.", [], _ => mqtt.Clients.Select(c => new
        {
            c.Name, c.Address, c.ClientId, c.Login, c.Security, State = c.StateText, c.FailureReason, c.IsRunning, Folder = c.FolderPath,
        }).ToList()),
        ChangeAsync("mqtt_client_add", "Add an MQTT client.", [P.Text("name", "Its name."), .. ClientFields], async args =>
        {
            string? problem = null;
            await WithEditor(mqtt, editor => problem = Apply(editor, args), () => mqtt.AddClientCommand.ExecuteAsync(null));
            Check(problem);
            return "Added.";
        }),
        ChangeAsync("mqtt_client_update", "Change an MQTT client that is not connected. Only what is given changes.", [ClientName, P.OptText("name", "Its new name."), .. ClientFields], async args =>
        {
            var client = Find(mqtt, args);
            if (!client.CanEdit)
                throw new ToolFailure("Disconnect the client first.");
            string? problem = null;
            await WithEditor(mqtt, editor => problem = Apply(editor, args), () => client.EditClientCommand.ExecuteAsync(null));
            Check(problem);
            return "Saved.";
        }),
        Risky("mqtt_client_remove", "Remove an MQTT client that is not connected.", [ClientName], args =>
        {
            var client = Find(mqtt, args);
            if (!client.RemoveClientCommand.CanExecute(null))
                throw new ToolFailure("It cannot be removed: disconnect it first, and the last client has to stay.");
            client.RemoveClientCommand.Execute(null);
            return "Removed.";
        }),
        RiskyAsync("mqtt_connect", "Connect a client to its broker.", [ClientName], async args =>
        {
            var client = Find(mqtt, args);
            if (!client.ConnectCommand.CanExecute(null))
                throw new ToolFailure($"It cannot connect now: it is {client.StateText}, or its settings are not complete.");
            await client.ConnectCommand.ExecuteAsync(null);
            return new { State = client.StateText, client.FailureReason };
        }),
        ChangeAsync("mqtt_disconnect", "Disconnect a client from its broker.", [ClientName], async args =>
        {
            var client = Find(mqtt, args);
            await client.DisconnectCommand.ExecuteAsync(null);
            return new { State = client.StateText };
        }),
        RiskyAsync("mqtt_publish", "Publish a message from a connected client, as the manual message on the MQTT page does.",
            [ClientName, P.Text("topic", "The topic."), P.OptText("body", "The message."), P.OptNumber("qos", "0, 1 or 2."), P.OptFlag("retain", "Whether the broker keeps it."), P.OptFlag("gzip", "Whether to compress it.")], async args =>
            {
                var client = Find(mqtt, args);

                // Through the manual form, for its checks and its line in the log; what the user typed there is put back.
                var (topic, body, qos, retain, gzip) = (client.ManualTopic, client.ManualBody, client.ManualQos, client.ManualRetain, client.ManualGzip);
                try
                {
                    client.ManualTopic = args.Text("topic");
                    client.ManualBody = args.OptText("body") ?? "";
                    client.ManualQos = args.Whole("qos") ?? 0;
                    client.ManualRetain = args.Flag("retain") ?? false;
                    client.ManualGzip = args.Flag("gzip") ?? false;
                    if (!client.SendManualCommand.CanExecute(null))
                        throw new ToolFailure(client.ManualTopicError ?? (client.IsConnected ? "It cannot be sent." : "The client is not connected."));
                    await client.SendManualCommand.ExecuteAsync(null);
                }
                finally
                {
                    (client.ManualTopic, client.ManualBody, client.ManualQos, client.ManualRetain, client.ManualGzip) = (topic, body, qos, retain, gzip);
                }

                var last = client.Entries.FirstOrDefault();
                if (last?.Event == ActivityEntry.FailedEvent)
                    throw new ToolFailure(last.Detail);
                return "Published.";
            }),
        Read("mqtt_activity", "Read a client's activity log, newest first.", [ClientName, P.OptNumber("limit", "How many lines; 50 unless given.")], args =>
            Find(mqtt, args).Entries.Take(args.Whole("limit") ?? 50).Select(e => new { Time = e.TimeText, e.Event, e.Topic, e.Source, e.Detail, e.Body }).ToList()),
        Change("mqtt_activity_clear", "Clear a client's activity log.", [ClientName], args =>
        {
            Find(mqtt, args).ClearLogCommand.Execute(null);
            return "Cleared.";
        }),
        Change("mqtt_folder_use", "Choose the folder of message files a client sends from.", [ClientName, P.Text("path", "The folder's whole path.")], args =>
        {
            var client = Find(mqtt, args);
            client.UseFolder(args.Text("path"));
            Check(client.FolderError);
            return new { client.FolderSummary };
        }),
        Read("mqtt_folder_messages", "List the messages in a client's folder, with their schedules and progress.", [ClientName], args =>
        {
            var client = Find(mqtt, args);
            return new { Folder = client.FolderPath, client.FolderSummary, client.FolderError, Messages = client.Rows.Select(Describe).ToList() };
        }),
        ChangeAsync("mqtt_folder_rescan", "Read a client's folder again.", [ClientName], async args =>
        {
            var client = Find(mqtt, args);
            await client.RescanCommand.ExecuteAsync(null);
            return new { client.FolderSummary };
        }),
        Risky("mqtt_message_switch", "Switch a folder message's schedule on or off. While a client is connected, one switched on starts being sent.", [ClientName, P.Text("file", "The message's file."), P.Flag("on", "On or off.")], args =>
        {
            var row = FindRow(Find(mqtt, args), args);
            if (!row.CanSwitch)
                throw new ToolFailure(row.SwitchTip);
            row.IsOn = args.Flag("on")!.Value;
            return Describe(row);
        }),
        RiskyAsync("mqtt_message_send", "Send a folder message now.", [ClientName, P.Text("file", "The message's file.")], async args =>
        {
            var client = Find(mqtt, args);
            var row = FindRow(client, args);
            if (!client.SendNowCommand.CanExecute(row))
                throw new ToolFailure("It cannot be sent now; the client may not be connected.");
            await client.SendNowCommand.ExecuteAsync(row);
            return Describe(row);
        }),
        ChangeAsync("mqtt_message_settings", "Change a folder message's settings, as its settings dialog does. Only what is given changes.",
            [
                ClientName, P.Text("file", "The message's file."), P.OptFlag("enabled", "Whether its schedule is on."), P.OptText("group", "Its group."), P.OptText("topic", "Its topic."),
                P.OptNumber("qos", "0, 1 or 2."), P.OptFlag("retain", "Whether the broker keeps it."), P.OptFlag("gzip", "Whether to compress it."),
                P.OptChoice("schedule", "How it repeats.", "None", "Once", "Interval", "Daily"), P.OptText("once_at", "When, for Once."), P.OptText("every", "How often, for Interval."),
                P.OptChoice("unit", "The unit of every.", "Seconds", "Minutes", "Hours", "Days"), P.OptText("max_sends", "The most times to send it."), P.OptText("daily_times", "The times of day, for Daily."),
            ], async args =>
            {
                var client = Find(mqtt, args);
                var row = FindRow(client, args);
                var previous = client.EditSettings;
                string? problem = null;
                client.EditSettings = editor =>
                {
                    if (args.Flag("enabled") is { } enabled) editor.Enabled = enabled;
                    if (args.OptText("group") is { } group) editor.Group = group;
                    if (args.OptText("topic") is { } topic) editor.Topic = topic;
                    if (args.Whole("qos") is { } qos) editor.QosIndex = qos;
                    if (args.Flag("retain") is { } retain) editor.Retain = retain;
                    if (args.Flag("gzip") is { } gzip) editor.Gzip = gzip;
                    if (args.OptText("schedule") is { } schedule) editor.ScheduleTypeIndex = (int)Enum.Parse<Joktan.Mqtt.ScheduleType>(schedule);
                    if (args.OptText("once_at") is { } onceAt) editor.OnceAt = onceAt;
                    if (args.OptText("every") is { } every) editor.Every = every;
                    if (args.OptText("unit") is { } unit) editor.UnitIndex = Array.IndexOf(new[] { "Seconds", "Minutes", "Hours", "Days" }, unit);
                    if (args.OptText("max_sends") is { } maxSends) editor.MaxSends = maxSends;
                    if (args.OptText("daily_times") is { } dailyTimes) editor.DailyTimes = dailyTimes;

                    var saved = false;
                    editor.CloseRequested += ok => saved = ok;
                    if (editor.SaveCommand.CanExecute(null))
                        editor.SaveCommand.Execute(null);
                    if (!saved)
                        problem = editor.Error ?? "The settings are not valid.";
                    return Task.FromResult(saved);
                };
                try
                {
                    await client.EditMessageCommand.ExecuteAsync(row);
                }
                finally
                {
                    client.EditSettings = previous;
                }

                Check(problem);

                // Saving reads the message again, which makes a new row.
                return Describe(FindRow(client, args));
            }),
    ];

    private static object Describe(FolderMessageRowViewModel row) => new
    {
        row.File, row.Topic, row.Schedule, row.NextSend, row.LastSent, row.Sent, row.State, row.IsOn,
    };

    private static MqttClientViewModel Find(MqttViewModel mqtt, ToolArgs args)
        => mqtt.Find(args.Text("client")) ?? throw new ToolFailure($"No client is called {args.Text("client")}. mqtt_clients lists them.");

    private static FolderMessageRowViewModel FindRow(MqttClientViewModel client, ToolArgs args)
        => client.RowFor(args.Text("file")) ?? throw new ToolFailure($"No message comes from the file {args.Text("file")}.");

    // The page would show its dialog here; a tool fills it in and saves it instead, so the dialog's checks still apply.
    private static async Task WithEditor(MqttViewModel mqtt, Func<ClientSettingsViewModel, string?> fill, Func<Task> run)
    {
        var previous = mqtt.EditClientSettings;
        mqtt.EditClientSettings = editor =>
        {
            if (fill(editor) is not null)
                return Task.FromResult(false);
            var saved = false;
            editor.CloseRequested += ok => saved = ok;
            if (editor.SaveCommand.CanExecute(null))
                editor.SaveCommand.Execute(null);
            return Task.FromResult(saved);
        };
        try
        {
            await run();
        }
        finally
        {
            mqtt.EditClientSettings = previous;
        }
    }

    private static string? Apply(ClientSettingsViewModel editor, ToolArgs args)
    {
        if (args.OptText("name") is { } name) editor.Name = name;
        if (args.OptText("host") is { } host) editor.Host = host;
        if (args.OptText("port") is { } port) editor.Port = port;
        if (args.OptText("username") is { } username) editor.Username = username;
        if (args.OptText("password") is { } password) editor.Password = password;
        if (args.OptText("client_id") is { } clientId) editor.ClientId = clientId;
        if (args.Flag("use_tls") is { } useTls) editor.UseTls = useTls;
        if (args.OptText("ca_certificate") is { } ca) editor.CaCertificate = ca;
        if (args.OptText("client_certificate") is { } certificate) editor.ClientCertificate = certificate;
        if (args.OptText("client_key") is { } key) editor.ClientKey = key;
        if (args.OptText("key_password") is { } keyPassword) editor.KeyPassword = keyPassword;
        if (args.Flag("accept_any_server_certificate") is { } any) editor.AcceptAnyServerCertificate = any;

        return editor.SaveCommand.CanExecute(null) ? null : editor.Error ?? "The settings are not valid.";
    }
}
