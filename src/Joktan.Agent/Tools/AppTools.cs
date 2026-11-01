using System;
using System.Collections.Generic;
using System.Linq;
using Joktan.Agent.Mcp;
using Joktan.Core.ViewModels;
using Joktan.Help;
using static Joktan.Agent.Tools.AgentTools;

namespace Joktan.Agent.Tools;

public static class AppTools
{
    public static IEnumerable<McpTool> Create(AgentContext app) =>
    [
        Read("app_pages", "List the app's menu: every page and group, with whether it is shown.", [], _ => app.Menu.Select(Describe).ToList()),
        Read("app_current_page", "Say which page is being shown.", [], _ => app.CurrentPage()),
        Change("app_open_page", "Show a page, so the user can see it. Name it as app_pages does, such as \"Notes\" or \"Topic Explorer\".",
            [P.Text("page", "The page's title.")], args =>
            {
                var page = Find(app.Menu, args.Text("page")) ?? throw new ToolFailure($"No page is called {args.Text("page")}.");
                if (page.HasChildren)
                    throw new ToolFailure($"{page.Title} is a group; open one of its pages.");
                for (var group = page.Parent; group is not null; group = group.Parent)
                    group.IsExpanded = true;
                app.Navigate(page);
                return $"Showing {page.Title}.";
            }),
        Read("app_search", "Search what the search box searches: notes, help, messages and alerts. Passwords are never searched.",
            [P.Text("words", "The words to find, in any order.")], args =>
                app.Search(args.Text("words").Split(' ', StringSplitOptions.RemoveEmptyEntries)).Select(h => new { h.Area, h.Title, h.Text }).ToList()),
        Read("help_topics", "List the topics of the app's built-in help.", [], _ => HelpTopic.All.Select(t => new { t.Id, t.Title, t.Parent }).ToList()),
        Read("help_read", "Read a help topic, in Markdown.", [P.Text("id", "The topic's id, from help_topics.")], args =>
            (HelpTopic.All.FirstOrDefault(t => t.Id == args.Text("id")) ?? throw new ToolFailure("No help topic has that id.")).ReadMarkdown()),
        Read("dashboard_overview", "Read the Overview dashboard: this machine's metrics, its details and what telemetry exports.", [], _ => new
        {
            Tiles = app.Overview.Tiles.Select(t => new { t.Label, t.Value, t.Detail, t.Percent }).ToList(),
            System = app.Overview.SystemInfo.Select(r => new { r.Label, r.Value }).ToList(),
            app.Overview.TelemetrySummary,
            Exported = app.Overview.ExportedMetrics.Select(r => new { r.Label, r.Value }).ToList(),
        }),
    ];

    private static object Describe(NavItemViewModel item) => new
    {
        item.Title,
        Shown = item.IsShown,
        Hideable = item.CanHide,
        Children = item.Children.Select(Describe).ToList(),
    };

    public static NavItemViewModel? Find(IEnumerable<NavItemViewModel> items, string title)
    {
        foreach (var item in items)
        {
            if (item.Title.Equals(title.Trim(), StringComparison.OrdinalIgnoreCase))
                return item;
            if (Find(item.Children, title) is { } found)
                return found;
        }

        return null;
    }
}
