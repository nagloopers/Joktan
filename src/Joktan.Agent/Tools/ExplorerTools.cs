using System.Collections.Generic;
using System.Linq;
using Joktan.Agent.Mcp;
using Joktan.TopicExplorer.ViewModels;
using static Joktan.Agent.Tools.AgentTools;

namespace Joktan.Agent.Tools;

public static class ExplorerTools
{
    private static readonly ToolParameter ClientName = P.Text("client", "The client's name, from explorer_tabs.");

    public static IEnumerable<McpTool> Create(TopicExplorerViewModel explorer) =>
    [
        Read("explorer_tabs", "List the Topic Explorer's clients, with what they subscribe to.", [], _ =>
        {
            explorer.Activate();
            return explorer.Tabs.Select(t => new { Client = t.Client.Name, State = t.Client.StateText, t.Filters, t.SubscribedFilters, t.Summary, t.FilterError }).ToList();
        }),
        ChangeAsync("explorer_subscribe", "Subscribe a connected client to topic filters, or stop. Filters are separated by spaces or new lines, as in the box on the page.",
            [ClientName, P.OptText("filters", "The filters, such as \"sensors/#\"."), P.Flag("subscribe", "True to subscribe, false to stop.")], async args =>
            {
                var tab = Find(explorer, args);
                if (args.OptText("filters") is { } filters)
                    tab.Filters = filters;
                if (args.Flag("subscribe")!.Value != tab.IsSubscribedToBox)
                    await tab.ToggleSubscriptionCommand.ExecuteAsync(null);
                Check(tab.FilterError);
                return new { tab.SubscribedFilters, tab.Summary };
            }),
        Read("explorer_topics", "List the topics seen by a client, with their latest values.", [ClientName, P.OptText("under", "Only topics starting with this."), P.OptNumber("limit", "How many; 200 unless given.")], args =>
        {
            var under = args.OptText("under") ?? "";
            return Walk(Find(explorer, args).Roots).Where(n => n.HasValue && n.FullTopic.StartsWith(under, System.StringComparison.Ordinal))
                .Take(args.Whole("limit") ?? 200)
                .Select(n => new { Topic = n.FullTopic, n.MessageCount, n.IsRetained, Latest = n.LastMessage?.Preview })
                .ToList();
        }),
        Read("explorer_topic", "Read a topic's latest message and history.", [ClientName, P.Text("topic", "The whole topic."), P.OptNumber("history", "How many earlier messages; 10 unless given.")], args =>
        {
            var node = FindNode(Find(explorer, args), args.Text("topic"));
            return new
            {
                Topic = node.FullTopic,
                node.MessageCount,
                node.IsRetained,
                Latest = node.LastMessage?.Text,
                History = node.History.Take(args.Whole("history") ?? 10).Select(m => new { Time = m.TimeText, m.Qos, m.Retain, m.Text }).ToList(),
            };
        }),
        Change("explorer_clear", "Forget what the explorer has seen for a client. Nothing changes on the broker.", [ClientName], args =>
        {
            Find(explorer, args).ClearCommand.Execute(null);
            return "Cleared.";
        }),
        RiskyAsync("explorer_publish", "Publish a message from the explorer.",
            [ClientName, P.Text("topic", "The topic."), P.OptText("body", "The message."), P.OptNumber("qos", "0, 1 or 2."), P.OptFlag("retain", "Whether the broker keeps it."), P.OptFlag("gzip", "Whether to compress it.")], async args =>
            {
                var tab = Find(explorer, args);
                tab.PublishTopic = args.Text("topic");
                tab.PublishBody = args.OptText("body") ?? "";
                tab.PublishQos = args.Whole("qos") ?? 0;
                tab.PublishRetain = args.Flag("retain") ?? false;
                tab.PublishGzip = args.Flag("gzip") ?? false;
                if (!tab.PublishCommand.CanExecute(null))
                    throw new ToolFailure(tab.PublishTopicError ?? "It cannot be published; the client may not be connected.");
                await tab.PublishCommand.ExecuteAsync(null);
                return tab.PublishStatus;
            }),
        RiskyAsync("explorer_clear_retained", "Clear a topic's retained message on the broker.", [ClientName, P.Text("topic", "The whole topic.")], async args =>
        {
            var tab = Find(explorer, args);
            tab.SelectedNode = FindNode(tab, args.Text("topic"));
            if (!tab.ClearRetainedCommand.CanExecute(null))
                throw new ToolFailure("It has no retained message to clear, or the client is not connected.");
            await tab.ClearRetainedCommand.ExecuteAsync(null);
            return tab.PublishStatus;
        }),
    ];

    private static ClientExplorerViewModel Find(TopicExplorerViewModel explorer, ToolArgs args)
    {
        explorer.Activate();
        return explorer.Tabs.FirstOrDefault(t => t.Client.Name.Equals(args.Text("client"), System.StringComparison.OrdinalIgnoreCase))
               ?? throw new ToolFailure($"No client is called {args.Text("client")}. explorer_tabs lists them.");
    }

    private static TopicNodeViewModel FindNode(ClientExplorerViewModel tab, string topic)
        => Walk(tab.Roots).FirstOrDefault(n => n.FullTopic == topic) ?? throw new ToolFailure($"Nothing has been seen on {topic}.");

    private static IEnumerable<TopicNodeViewModel> Walk(IEnumerable<TopicNodeViewModel> nodes)
        => nodes.SelectMany(n => new[] { n }.Concat(Walk(n.Children)));
}
