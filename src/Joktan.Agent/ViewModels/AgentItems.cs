using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Agent.Mcp;
using Joktan.Core.ViewModels;

namespace Joktan.Agent.ViewModels;

/// <summary>Something the user said.</summary>
public sealed class UserMessageViewModel(string text) : ViewModelBase
{
    public string Text { get; } = text;
}

/// <summary>Something the agent said, which grows as it arrives.</summary>
public sealed partial class AgentMessageViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial string Text { get; set; } = "";
}

/// <summary>Something that went wrong with the turn.</summary>
public sealed class AgentProblemViewModel(string text) : ViewModelBase
{
    public string Text { get; } = text;
}

/// <summary>A tool being called, whichever client called it.</summary>
public sealed partial class ToolCallViewModel(McpTool tool, ToolCaller caller) : ViewModelBase
{
    public string Name { get; } = tool.Name;

    public string Description { get; } = tool.Description;

    public string CallerText { get; } = caller == ToolCaller.Outside ? "outside client" : "";

    public bool IsFromOutside { get; } = caller == ToolCaller.Outside;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRunning))]
    [NotifyPropertyChangedFor(nameof(IsDone))]
    public partial bool IsFinished { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDone))]
    public partial bool IsFailed { get; set; }

    [ObservableProperty]
    public partial string? Problem { get; set; }

    public bool IsRunning => !IsFinished;

    public bool IsDone => IsFinished && !IsFailed;
}

/// <summary>A tool waiting for the user's answer.</summary>
public sealed partial class ApprovalViewModel(McpTool tool, string details, ToolCaller caller) : ViewModelBase
{
    private readonly TaskCompletionSource<bool> _answer = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public string Name { get; } = tool.Name;

    public string Question { get; } = tool.Risk == ToolRisk.Secret
        ? "Show this secret to the agent? It goes to the client's AI provider."
        : caller == ToolCaller.Outside ? "A client outside the app asks to do this:" : "The agent asks to do this:";

    public string Description { get; } = tool.Description;

    public string Details { get; } = details;

    public bool IsSecret { get; } = tool.Risk == ToolRisk.Secret;

    /// <summary>A secret is asked about every time.</summary>
    public bool CanRemember { get; } = tool.Risk != ToolRisk.Secret;

    public Task<bool> Answer => _answer.Task;

    /// <summary>Whether it was allowed for the rest of the conversation.</summary>
    public bool Remembered { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWaiting))]
    public partial string? Outcome { get; private set; }

    public bool IsWaiting => Outcome is null;

    [RelayCommand]
    private void Allow() => Finish(true, "Allowed");

    [RelayCommand]
    private void AllowAlways()
    {
        Remembered = true;
        Finish(true, "Allowed for this conversation");
    }

    [RelayCommand]
    private void Deny() => Finish(false, "Declined");

    /// <summary>Takes the question back, when nobody answered or the turn was stopped.</summary>
    public void Withdraw(string outcome) => Finish(false, outcome);

    private void Finish(bool allowed, string outcome)
    {
        if (Outcome is not null)
            return;
        Outcome = outcome;
        _answer.TrySetResult(allowed);
    }
}
