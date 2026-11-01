using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Agent.Clients;
using Joktan.Agent.Mcp;
using Joktan.Core;
using Joktan.Core.ViewModels;

namespace Joktan.Agent.ViewModels;

/// <summary>
/// The conversation in the agent panel, the same whether the panel is docked or in a window of its own. It also
/// owns the MCP server and answers for it: approvals appear here and tool calls are listed here.
/// </summary>
public partial class AgentViewModel : ViewModelBase, IApprovalGate, IToolObserver, IDisposable
{
    public const string CredentialService = "Joktan Agent";
    private const string OutsideAccount = "outside-token";

    // How long a question waits for an answer before it counts as declined.
    private static readonly TimeSpan ApprovalTimeout = TimeSpan.FromMinutes(5);

    private readonly Func<IReadOnlyList<McpTool>> _tools;
    private readonly HashSet<(ToolCaller, string)> _allowed = [];
    private McpServer? _server;
    private string? _sessionId;
    private AgentMessageViewModel? _replying;
    private CancellationTokenSource? _cancel;
    private Output? _output;

    public AgentViewModel(AgentSettings settings, Func<IReadOnlyList<McpTool>> tools)
    {
        Settings = settings;
        _tools = tools;
        SelectedClient = AgentClient.Of(settings.Client);
        IsOpen = settings.PanelOpen;
        IsFloating = settings.PanelFloating;
        Items.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>Starts the server now if outside clients expect it, once the pages the tools act on exist.</summary>
    public void Start()
    {
        if (Settings.OutsideEnabled)
            StartForOutside();
    }

    public AgentSettings Settings { get; }

    public IReadOnlyList<AgentClient> Clients => AgentClient.All;

    public ObservableCollection<ViewModelBase> Items { get; } = [];

    /// <summary>The MCP server's state, for the settings page. Null when it could not start.</summary>
    [ObservableProperty]
    public partial string? ServerProblem { get; private set; }

    public string? OutsideToken => _server?.OutsideToken;

    public string? ServerUrl => _server?.Url;

    /// <summary>Asks whoever shows the panel to bring it forward, for an approval.</summary>
    public event Action? AttentionRequested;

    /// <summary>Raised as text arrives or items are added, so the view can keep the end in sight.</summary>
    public event Action? ContentGrew;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDocked))]
    [NotifyPropertyChangedFor(nameof(IsInWindow))]
    public partial bool IsOpen { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDocked))]
    [NotifyPropertyChangedFor(nameof(IsInWindow))]
    public partial bool IsFloating { get; set; }

    public bool IsDocked => IsOpen && !IsFloating;

    public bool IsInWindow => IsOpen && IsFloating;

    [ObservableProperty]
    public partial AgentClient SelectedClient { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial string Draft { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial bool IsBusy { get; private set; }

    public bool IsIdle => !IsBusy;

    public bool IsEmpty => Items.Count == 0;

    partial void OnIsOpenChanged(bool value) => SaveLayout();

    partial void OnIsFloatingChanged(bool value) => SaveLayout();

    // A session belongs to one client, so another one starts afresh, told what was said so far.
    partial void OnSelectedClientChanged(AgentClient value)
    {
        _sessionId = null;
        Settings.Client = value.Kind;
        Settings.Save();
    }

    public void SaveLayout()
    {
        Settings.PanelOpen = IsOpen;
        Settings.PanelFloating = IsFloating;
        Settings.Save();
    }

    [RelayCommand]
    private void Toggle() => IsOpen = !IsOpen;

    [RelayCommand]
    private void Close() => IsOpen = false;

    [RelayCommand]
    private void PopOut() => IsFloating = true;

    [RelayCommand]
    private void Dock() => IsFloating = false;

    /// <summary>Empties the chat and starts again: any running turn stops, the client's session is let go, and what was allowed must be asked again.</summary>
    [RelayCommand]
    private void Clear()
    {
        if (_output is not null)
            _output.IsAbandoned = true;
        Stop();
        Items.Clear();
        _allowed.Clear();
        _sessionId = null;
        _replying = null;
    }

    [RelayCommand]
    private void Stop()
    {
        _cancel?.Cancel();
        foreach (var approval in Items.OfType<ApprovalViewModel>())
            approval.Withdraw("Stopped");
    }

    private bool CanSend => !IsBusy && Draft.Trim().Length > 0;

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        var text = Draft.Trim();
        var earlier = Transcript();
        Draft = "";
        Add(new UserMessageViewModel(text));
        _replying = null;

        var client = SelectedClient;
        var profile = Settings.ProfileOf(client.Kind);
        if (AgentProgram.Find(client, profile) is not { } program)
        {
            Add(new AgentProblemViewModel($"{client.DisplayName} was not found. {client.InstallHint} You can give its path on the Settings / Agent page."));
            return;
        }

        if (client is CustomClient custom)
            custom.Arguments = Settings.CustomArguments;

        IsBusy = true;
        using var cancel = _cancel = new CancellationTokenSource();
        DirectoryInfo? folder = null;
        try
        {
            EnsureServer();
            folder = Directory.CreateTempSubdirectory("joktan-agent-");

            // A client that gave no session to carry on is told what has been said.
            var prompt = _sessionId is null && earlier.Length > 0
                ? $"The conversation so far:\n\n{earlier}\n\nThe user now says:\n\n{text}"
                : text;
            var turn = new AgentTurn(prompt, _sessionId, _server!.Url!, _server.PanelToken, folder.FullName, profile, Settings.AllowClientTools);
            var launch = client.Prepare(turn);
            var workspace = Path.Combine(DataFolder.Path, "agent-workspace");
            var output = _output = new Output(this);

            var problem = await Task.Run(() => AgentProgram.RunAsync(program, launch, workspace, line => client.Read(line, output), cancel.Token), CancellationToken.None);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

            if (output.IsAbandoned)
                return;
            if (problem is not null && !output.HadProblem)
                Add(new AgentProblemViewModel($"{client.DisplayName} failed: {problem}"));
            else if (!output.HadProblem && !output.HadText && !cancel.IsCancellationRequested)
                Add(new AgentProblemViewModel($"{client.DisplayName} finished without saying anything."));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException or HttpListenerException or SocketException)
        {
            Add(new AgentProblemViewModel($"{client.DisplayName} could not be started: {e.Message}"));
        }
        finally
        {
            _cancel = null;
            _output = null;
            IsBusy = false;
            foreach (var approval in Items.OfType<ApprovalViewModel>())
                approval.Withdraw("The turn ended");
            try
            {
                folder?.Delete(recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // The operating system clears temporary files in the end.
            }
        }
    }

    private void Add(ViewModelBase item)
    {
        Items.Add(item);
        ContentGrew?.Invoke();
    }

    private string Transcript()
    {
        var text = new StringBuilder();
        foreach (var item in Items)
        {
            if (item is UserMessageViewModel user)
                text.Append("User: ").AppendLine(user.Text);
            else if (item is AgentMessageViewModel agent)
                text.Append("Assistant: ").AppendLine(agent.Text);
        }

        return text.ToString().TrimEnd();
    }

    private sealed class Output(AgentViewModel page) : IAgentOutput
    {
        /// <summary>Set when the chat is cleared, after which what is still on its way is not shown.</summary>
        public bool IsAbandoned { get; set; }

        public bool HadText { get; private set; }

        public bool HadProblem { get; private set; }

        public void Text(string text)
        {
            HadText = true;
            Dispatcher.UIThread.Post(() =>
            {
                if (!IsAbandoned)
                    page.AddText(text);
            });
        }

        public void Session(string id) => Dispatcher.UIThread.Post(() =>
        {
            if (!IsAbandoned)
                page._sessionId = id;
        });

        public void Problem(string message)
        {
            HadProblem = true;
            Dispatcher.UIThread.Post(() =>
            {
                if (!IsAbandoned)
                    page.Add(new AgentProblemViewModel(message));
            });
        }
    }

    private void AddText(string text)
    {
        if (_replying is null)
        {
            // White space between tool calls is not a message.
            if (text.Trim().Length == 0)
                return;
            Add(_replying = new AgentMessageViewModel());
        }

        _replying.Text += text;
        ContentGrew?.Invoke();
    }

    // ---- The MCP server ----

    private void EnsureServer()
    {
        _server ??= new McpServer(_tools(), this, this);
        if (!_server.IsRunning)
        {
            _server.Start(Settings.OutsideEnabled ? Settings.OutsidePort : 0);
            OnPropertyChanged(nameof(ServerUrl));
        }
    }

    /// <summary>Lets outside clients in, or shuts them out, as the settings say. Answers with what went wrong, if anything.</summary>
    public string? ApplyOutside()
    {
        ServerProblem = null;
        if (Settings.OutsideEnabled)
            StartForOutside();
        else if (_server is not null)
            _server.OutsideToken = null;

        OnPropertyChanged(nameof(OutsideToken));
        OnPropertyChanged(nameof(ServerUrl));
        return ServerProblem;
    }

    private void StartForOutside()
    {
        try
        {
            _server ??= new McpServer(_tools(), this, this);

            // The panel's server is on any free port until then, and outside clients need the one they were told.
            if (_server.IsRunning && _server.Port != Settings.OutsidePort)
            {
                if (IsBusy)
                {
                    ServerProblem = "Wait for the agent to finish its turn, then switch this on again.";
                    return;
                }

                _server.Stop();
            }

            EnsureServer();
            _server.OutsideToken = OutsideTokenFromStore();
        }
        catch (Exception e) when (e is HttpListenerException or SocketException or InvalidOperationException or IOException)
        {
            ServerProblem = $"Could not listen on port {Settings.OutsidePort}: {e.Message}";
        }
    }

    // Kept in the credential store, so that a client's configuration stays good from one run to the next. Where
    // there is no store it lasts for this run only rather than going in a settings file.
    private static string OutsideTokenFromStore()
    {
        var store = CredentialStore.Create(CredentialService);
        if (store.Read(OutsideAccount) is { Length: > 0 } token)
            return token;
        token = McpServer.NewToken();
        store.Write(OutsideAccount, token);
        return token;
    }

    /// <summary>Makes a new token for outside clients. The old one stops working at once.</summary>
    public void ReplaceOutsideToken()
    {
        var token = McpServer.NewToken();
        CredentialStore.Create(CredentialService).Write(OutsideAccount, token);
        if (_server is not null && Settings.OutsideEnabled)
            _server.OutsideToken = token;
        OnPropertyChanged(nameof(OutsideToken));
    }

    public async Task<bool> ApproveAsync(McpTool tool, string arguments, ToolCaller caller)
        => await Dispatcher.UIThread.InvokeAsync(() => AskAsync(tool, arguments, caller));

    private async Task<bool> AskAsync(McpTool tool, string arguments, ToolCaller caller)
    {
        if (tool.Risk != ToolRisk.Secret && _allowed.Contains((caller, tool.Name)))
            return true;

        var approval = new ApprovalViewModel(tool, Pretty(arguments), caller);
        Add(approval);
        _replying = null;
        IsOpen = true;
        AttentionRequested?.Invoke();

        using var timeout = new CancellationTokenSource(ApprovalTimeout);
        await using var withdraw = timeout.Token.Register(() => Dispatcher.UIThread.Post(() => approval.Withdraw("Nobody answered in time")));
        var allowed = await approval.Answer;
        if (allowed && approval.Remembered)
            _allowed.Add((caller, tool.Name));
        return allowed;
    }

    private static string Pretty(string arguments)
    {
        try
        {
            using var document = JsonDocument.Parse(arguments);
            var text = JsonSerializer.Serialize(document.RootElement, JsonFormats.Indented);
            return text.Length <= 2000 ? text : text[..2000] + "\n…";
        }
        catch (JsonException)
        {
            return arguments;
        }
    }

    public object? ToolStarted(McpTool tool, ToolCaller caller)
    {
        var row = new ToolCallViewModel(tool, caller);
        Add(row);
        _replying = null;
        return row;
    }

    public void ToolFinished(object? started, bool succeeded, string? problem)
    {
        if (started is not ToolCallViewModel row)
            return;
        row.IsFailed = !succeeded;
        row.Problem = problem;
        row.IsFinished = true;
        ContentGrew?.Invoke();
    }

    /// <summary>Stops the running turn and the server. Called when the app exits.</summary>
    public void Dispose()
    {
        _cancel?.Cancel();
        _server?.Dispose();
        GC.SuppressFinalize(this);
    }
}
