using System;
using System.Threading.Tasks;

namespace Joktan.Agent.Mcp;

/// <summary>Who is calling a tool: the agent in the panel, or a client from outside the app.</summary>
public enum ToolCaller
{
    Panel,
    Outside,
}

/// <summary>Asks the user whether a tool may run. The answer comes from the agent panel.</summary>
public interface IApprovalGate
{
    /// <param name="tool">The tool wanting to run.</param>
    /// <param name="arguments">What it was called with, as JSON, to show the user.</param>
    /// <param name="caller">Who is calling.</param>
    Task<bool> ApproveAsync(McpTool tool, string arguments, ToolCaller caller);
}

/// <summary>A tool call seen by the server, for the panel to show as it happens.</summary>
public interface IToolObserver
{
    /// <summary>Called on the UI thread; returns what to pass to <see cref="ToolFinished"/>.</summary>
    object? ToolStarted(McpTool tool, ToolCaller caller);

    /// <summary>Called on the UI thread.</summary>
    void ToolFinished(object? started, bool succeeded, string? problem);
}
