using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Joktan.Agent.Mcp;

namespace Joktan.Agent.Tools;

/// <summary>Every tool of the app's MCP server. Each drives the same commands as the page it belongs to, so its checks apply and the page shows the result.</summary>
public static class AgentTools
{
    public static IReadOnlyList<McpTool> All(AgentContext app) =>
    [
        .. AppTools.Create(app),
        .. NotesTools.Create(app.Notes),
        .. PasswordTools.Create(app.Passwords),
        .. PomodoroTools.Create(app.Pomodoro),
        .. MqttTools.Create(app.Mqtt),
        .. InboxTools.Create(app.Inbox, app.Messaging),
        .. ExplorerTools.Create(app.Explorer),
        .. DashboardTools.Create(app.Dashboards),
        .. CertificateTools.Create(app.Certificates, app.CertificateDefaults),
        .. ApiTools.Create(app.Api),
        .. ContainerTools.Create(app.Containers),
        .. SettingsTools.Create(app),
    ];

    public static McpTool Read(string name, string description, ToolParameter[] parameters, Func<ToolArgs, object?> run)
        => McpTool.Sync(name, description, ToolRisk.Read, parameters, run);

    public static McpTool ReadAsync(string name, string description, ToolParameter[] parameters, Func<ToolArgs, Task<object?>> run)
        => new(name, description, ToolRisk.Read, parameters, run);

    public static McpTool Change(string name, string description, ToolParameter[] parameters, Func<ToolArgs, object?> run)
        => McpTool.Sync(name, description, ToolRisk.Change, parameters, run);

    public static McpTool ChangeAsync(string name, string description, ToolParameter[] parameters, Func<ToolArgs, Task<object?>> run)
        => new(name, description, ToolRisk.Change, parameters, run);

    public static McpTool Risky(string name, string description, ToolParameter[] parameters, Func<ToolArgs, object?> run)
        => McpTool.Sync(name, description, ToolRisk.Risky, parameters, run);

    public static McpTool RiskyAsync(string name, string description, ToolParameter[] parameters, Func<ToolArgs, Task<object?>> run)
        => new(name, description, ToolRisk.Risky, parameters, run);

    /// <summary>Stops with a reason when something a page reports is set.</summary>
    public static void Check(string? problem)
    {
        if (!string.IsNullOrEmpty(problem))
            throw new ToolFailure(problem);
    }
}
