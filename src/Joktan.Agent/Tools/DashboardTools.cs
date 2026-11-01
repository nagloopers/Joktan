using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Joktan.Agent.Mcp;
using Joktan.TopicDashboards;
using Joktan.TopicDashboards.ViewModels;
using static Joktan.Agent.Tools.AgentTools;

namespace Joktan.Agent.Tools;

public static class DashboardTools
{
    private static readonly ToolParameter DashboardName = P.Text("dashboard", "The dashboard's name, from dashboards_list.");

    private static readonly ToolParameter[] GraphFields =
    [
        P.OptChoice("kind", "How it draws.", Enum.GetNames<GraphKind>()), P.OptText("unit", "The unit shown."), P.OptNumber("span_minutes", "How many minutes it shows."),
        P.OptNumber("decimals", "Decimal places; -1 chooses."), P.OptNumber("minimum", "The bottom of the scale."), P.OptNumber("maximum", "The top of the scale."),
        P.OptNumber("width", "How many columns wide."), P.OptNumber("height", "How many rows high."),
    ];

    public static IEnumerable<McpTool> Create(TopicDashboardsViewModel page) =>
    [
        Read("dashboards_list", "List the MQTT dashboards and their graphs.", [], _ => page.Dashboards.Select(d => new
        {
            d.Name, Tab = d.DisplayName, d.Columns, d.RowHeight, Selected = d == page.SelectedDashboard,
            Graphs = d.Graphs.Select(g => new
            {
                g.Name, g.Source, Kind = g.Profile.Kind.ToString(), g.Profile.Client, g.Profile.Unit, g.Profile.SpanMinutes,
                Values = g.Profile.Values.Select(v => new { v.Topic, v.Path, v.Label }).ToList(),
                Latest = g.HeadlineValue, g.Status,
            }).ToList(),
        }).ToList()),
        Change("dashboards_create", "Make an MQTT dashboard and show it.", [P.Text("name", "Its name.")], args => page.CreateDashboard(args.Text("name")).Name),
        Change("dashboards_select", "Show a dashboard.", [DashboardName], args =>
        {
            page.SelectedDashboard = Find(page, args);
            return "Showing it.";
        }),
        Change("dashboards_update", "Rename a dashboard or change its layout and colours. Only what is given changes.",
            [DashboardName, P.OptText("name", "Its new name."), P.OptNumber("columns", "How many columns."), P.OptNumber("row_height", "How tall a row is."), P.OptNumber("gap", "The space between graphs."), P.OptText("background", "Its background, such as #102030."), P.OptText("tab_color", "Its tab's colour.")], args =>
            {
                var dashboard = Find(page, args);
                if (args.Whole("columns") is { } columns) dashboard.Columns = columns;
                if (args.Number("row_height") is { } rowHeight) dashboard.RowHeight = rowHeight;
                if (args.Number("gap") is { } gap) dashboard.Gap = gap;
                if (args.OptText("background") is { } background) dashboard.Background = background;
                if (args.OptText("tab_color") is { } tabColor) dashboard.TabColor = tabColor;
                if (args.OptText("name") is { } name) dashboard.Name = name;
                page.Save();
                return "Saved.";
            }),
        Risky("dashboards_remove", "Remove a dashboard and its graphs.", [DashboardName], args =>
        {
            page.Remove(Find(page, args));
            return "Removed.";
        }),
        Change("dashboards_add_graph", "Add a graph of numbers on an MQTT topic. Each value is a topic and, for JSON payloads, the path of the number in it, such as sensors.temperature.",
            [DashboardName, P.Text("name", "The graph's name."), P.Text("mqtt_client", "The client whose broker it reads."), P.Text("topic", "The topic."), P.OptText("path", "The path of the number; the whole payload otherwise."), P.OptText("label", "The line's label."), .. GraphFields], args =>
            {
                var dashboard = Find(page, args);
                if (page.FindClient(args.Text("mqtt_client")) is null)
                    throw new ToolFailure($"No client is called {args.Text("mqtt_client")}.");
                var profile = new GraphProfile
                {
                    Name = args.Text("name"),
                    Client = args.Text("mqtt_client"),
                    Topic = args.Text("topic"),
                    Values = [new GraphValue { Topic = args.Text("topic"), Path = args.OptText("path") ?? "", Label = args.OptText("label") ?? "" }],
                };
                Fill(profile, args);
                page.Commit(dashboard, null, profile, new GraphData());
                return "Added.";
            }),
        Change("dashboards_update_graph", "Change a graph's name, look or scale. Only what is given changes.", [DashboardName, P.Text("graph", "The graph's name."), P.OptText("name", "Its new name."), .. GraphFields], args =>
        {
            var dashboard = Find(page, args);
            var card = FindGraph(dashboard, args);

            // A new profile, as the graph editor makes: the cards on show are matched to their profile, so one
            // changed in place would keep the old size and look.
            var profile = JsonSerializer.Deserialize<GraphProfile>(JsonSerializer.Serialize(card.Profile))!;
            if (args.OptText("name") is { } name)
                profile.Name = name;
            Fill(profile, args);
            page.Commit(dashboard, card, profile, card.Data);
            return "Saved.";
        }),
        Risky("dashboards_remove_graph", "Remove a graph from a dashboard.", [DashboardName, P.Text("graph", "The graph's name.")], args =>
        {
            var dashboard = Find(page, args);
            page.RemoveGraph(dashboard.Profile, FindGraph(dashboard, args).Profile);
            return "Removed.";
        }),
    ];

    private static void Fill(GraphProfile profile, ToolArgs args)
    {
        if (args.OptText("kind") is { } kind) profile.Kind = Enum.Parse<GraphKind>(kind, ignoreCase: true);
        if (args.OptText("unit") is { } unit) profile.Unit = unit;
        if (args.Whole("span_minutes") is { } span) profile.SpanMinutes = span;
        if (args.Whole("decimals") is { } decimals) profile.Decimals = decimals;
        if (args.Number("minimum") is { } minimum) profile.Minimum = minimum;
        if (args.Number("maximum") is { } maximum) profile.Maximum = maximum;
        if (args.Whole("width") is { } width) profile.Width = width;
        if (args.Whole("height") is { } height) profile.Height = height;
    }

    private static DashboardTabViewModel Find(TopicDashboardsViewModel page, ToolArgs args)
        => page.Dashboards.FirstOrDefault(d => d.Name.Equals(args.Text("dashboard"), StringComparison.OrdinalIgnoreCase))
           ?? throw new ToolFailure($"No dashboard is called {args.Text("dashboard")}.");

    private static GraphCardViewModel FindGraph(DashboardTabViewModel dashboard, ToolArgs args)
        => dashboard.Graphs.FirstOrDefault(g => g.Name.Equals(args.Text("graph"), StringComparison.OrdinalIgnoreCase))
           ?? throw new ToolFailure($"{dashboard.Name} has no graph called {args.Text("graph")}.");
}
