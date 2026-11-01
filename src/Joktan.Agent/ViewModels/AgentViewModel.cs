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
using Joktan.Agent.Resources;
using Joktan.Agent.Speech;
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
    private readonly SpeechOutput _speech = new();
    private readonly SpeechToText _speechToText = new();
    private IMicrophone? _microphone;
    private bool _sendAfterTurn;

    // The loudest sample below which a recording counts as room noise, on a scale of 0 to 1.
    private const float QuietestSpeech = 0.02f;

    public AgentViewModel(AgentSettings settings, Func<IReadOnlyList<McpTool>> tools)
    {
        Settings = settings;
        _tools = tools;
        SelectedClient = AgentClient.Of(settings.Client);
        IsOpen = settings.PanelOpen;
        IsFloating = settings.PanelFloating;
        IsReadingAloud = settings.ReadAloud;
        _speech.SpeakingChanged += speaking => Dispatcher.UIThread.Post(() => IsSpeaking = speaking);
        Items.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
        Prompts.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoPrompts));
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

    /// <summary>The prompts offered in the chat, added to and removed from on Settings / Agent.</summary>
    public ObservableCollection<AgentPrompt> Prompts => Settings.Prompts;

    public bool HasNoPrompts => Prompts.Count == 0;

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

    /// <summary>Whether replies are read aloud.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReadAloudTip))]
    public partial bool IsReadingAloud { get; set; }

    /// <summary>Whether something is being read now.</summary>
    [ObservableProperty]
    public partial bool IsSpeaking { get; private set; }

    /// <summary>Whether the microphone button shows.</summary>
    public bool CanTalk => Settings.VoiceInput && SpeechToText.IsAvailable;

    /// <summary>Whether the microphone is on.</summary>
    [ObservableProperty]
    public partial bool IsListening { get; private set; }

    /// <summary>Whether what was said is being turned into text.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPressTalk))]
    public partial bool IsTranscribing { get; private set; }

    public bool CanPressTalk => !IsTranscribing;

    // In the box and not a tooltip, as an open tooltip can take the press meant for the button below it.
    public string DraftHint => CanTalk ? Strings.Agent_AskTheAgentOrHoldThe : Strings.Agent_AskTheAgentShiftEnterFor;

    /// <summary>What is happening with the microphone, or what went wrong; gone once anything is typed.</summary>
    [ObservableProperty]
    public partial string? TalkMessage { get; private set; }

    /// <summary>Asks the view to put the cursor in the message box, after words have arrived there.</summary>
    public event Action? DraftFocusRequested;

    /// <summary>Takes in a change of the voice input switch.</summary>
    public void VoiceInputChanged()
    {
        if (!Settings.VoiceInput && IsListening)
            CancelListening();
        TalkMessage = null;
        OnPropertyChanged(nameof(CanTalk));
        OnPropertyChanged(nameof(DraftHint));
    }

    public string ReadAloudTip => IsReadingAloud ? Strings.Agent_StopReadingRepliesAloud : Strings.Agent_ReadRepliesAloud;

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

    partial void OnIsReadingAloudChanged(bool value)
    {
        Settings.ReadAloud = value;
        Settings.Save();
        if (!value)
            _speech.Stop();
    }

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

    /// <summary>Starts listening, for as long as the microphone button is held.</summary>
    public void StartListening()
    {
        // Otherwise the microphone would hear the replies being read.
        _speech.Stop();
        TalkMessage = null;
        var microphone = Microphone.Create();
        try
        {
            microphone.Start();
        }
        catch (InvalidOperationException e)
        {
            microphone.Dispose();
            TalkMessage = string.Format(Strings.Agent_CheckThatOneIsConnectedAnd, e.Message);
            return;
        }

        microphone.LimitReached += () => Dispatcher.UIThread.Post(() => _ = FinishListeningAsync());
        _microphone = microphone;
        IsListening = true;
        TalkMessage = Strings.Agent_ListeningLetGoToSend;
        _ = _speechToText.LoadAsync();
    }

    /// <summary>Turns what was said into text and sends it, when the microphone button is let go.</summary>
    public async Task FinishListeningAsync()
    {
        if (_microphone is not { } microphone)
            return;

        _microphone = null;
        IsListening = false;
        IsTranscribing = true;
        TalkMessage = Strings.Agent_WorkingOutWhatYouSaid;
        try
        {
            var samples = await microphone.StopAsync();
            var peak = samples.Length == 0 ? 0 : samples.Max(Math.Abs);
            if (peak == 0)
            {
                TalkMessage = Strings.Agent_NoSoundReachedTheAppCheck;
                return;
            }

            // Whisper makes up words from room noise, and a made-up "send" would act on them.
            if (peak < QuietestSpeech)
            {
                TalkMessage = Strings.Agent_ThatWasTooQuietToBe;
                return;
            }

            var text = await _speechToText.TranscribeAsync(samples);
            TalkMessage = null;
            if (text.Length == 0)
                TalkMessage = Strings.Agent_NoWordsWereHeardTryAgain;
            else
                TakeSpeech(text);
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or UnauthorizedAccessException or Whisper.net.Wave.CorruptedWaveException or DllNotFoundException)
        {
            TalkMessage = string.Format(Strings.Agent_WhatYouSaidCouldNotBe, e.Message);
        }
        finally
        {
            microphone.Dispose();
            IsTranscribing = false;
        }
    }

    private void TakeSpeech(string text)
    {
        var (kind, message) = VoiceCommand.Read(text);
        switch (kind)
        {
            case VoiceCommandKind.Stop:
                Stop();
                return;
            case VoiceCommandKind.ClearChat:
                Draft = "";
                Clear();
                return;
        }

        Draft = Draft.Trim().Length > 0 ? $"{Draft.TrimEnd()} {message}" : message;
        DraftFocusRequested?.Invoke();

        if (IsBusy)
        {
            _sendAfterTurn = true;
            TalkMessage = Strings.Agent_TheAgentIsStillWorkingThis;
        }
        else if (SendCommand.CanExecute(null))
        {
            SendCommand.Execute(null);
        }
    }

    // After the turn has wholly ended, as the send command counts as running until then.
    partial void OnIsBusyChanged(bool value)
    {
        if (value || !_sendAfterTurn)
            return;

        _sendAfterTurn = false;
        Dispatcher.UIThread.Post(() =>
        {
            TalkMessage = null;
            if (SendCommand.CanExecute(null))
                SendCommand.Execute(null);
        }, DispatcherPriority.Background);
    }

    private void CancelListening()
    {
        _microphone?.Dispose();
        _microphone = null;
        IsListening = false;
    }

    [RelayCommand]
    private void ToggleReadAloud() => IsReadingAloud = !IsReadingAloud;

    /// <summary>Stops what is being read. Later replies are still read.</summary>
    [RelayCommand]
    private void StopSpeaking() => _speech.Stop();

    /// <summary>Reads a sample in the chosen voice, for the settings page.</summary>
    public void TrySpeech()
    {
        _speech.Stop();
        _speech.Say(Strings.Agent_ThisIsHowRepliesWillSound, Settings.SpeechVoice, Settings.SpeechSpeed);
    }

    private void Speak(string text)
    {
        if (IsReadingAloud)
            _speech.Say(text, Settings.SpeechVoice, Settings.SpeechSpeed);
    }

    // A reply is read once it is whole, since Markdown cannot be read well in pieces.
    private void FinishReply()
    {
        if (_replying is { } reply)
            Speak(SpeechOutput.SpokenText(reply.Text));
        _replying = null;
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
        _speech.Stop();
        Items.Clear();
        _allowed.Clear();
        _sessionId = null;
        _replying = null;
    }

    [RelayCommand]
    private void Stop()
    {
        _cancel?.Cancel();
        _speech.Stop();
        _replying = null;
        _sendAfterTurn = false;
        foreach (var approval in Items.OfType<ApprovalViewModel>())
            approval.Withdraw(Strings.Agent_Stopped);
    }

    // Into the box and not sent, so it can be finished off first, as the agent acts on what it is told.
    [RelayCommand]
    private void UsePrompt(AgentPrompt prompt)
    {
        Draft = Draft.Trim().Length > 0 ? $"{Draft.TrimEnd()} {prompt.Text}" : prompt.Text;
        DraftFocusRequested?.Invoke();
    }

    private bool CanSend => !IsBusy && Draft.Trim().Length > 0;

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        var text = Draft.Trim();
        var earlier = Transcript();
        Draft = "";
        _speech.Stop();
        Add(new UserMessageViewModel(text));
        _replying = null;

        var client = SelectedClient;
        var profile = Settings.ProfileOf(client.Kind);
        if (AgentProgram.Find(client, profile) is not { } program)
        {
            Add(new AgentProblemViewModel(string.Format(Strings.Agent_WasNotFoundYouCanGive, client.DisplayName, client.InstallHint)));
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
                Add(new AgentProblemViewModel(string.Format(Strings.Agent_Failed, client.DisplayName, problem)));
            else if (!output.HadProblem && !output.HadText && !cancel.IsCancellationRequested)
                Add(new AgentProblemViewModel(string.Format(Strings.Agent_FinishedWithoutSayingAnything, client.DisplayName)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException or HttpListenerException or SocketException)
        {
            Add(new AgentProblemViewModel(string.Format(Strings.Agent_CouldNotBeStarted, client.DisplayName, e.Message)));
        }
        finally
        {
            FinishReply();
            _cancel = null;
            _output = null;
            IsBusy = false;
            foreach (var approval in Items.OfType<ApprovalViewModel>())
                approval.Withdraw(Strings.Agent_TheTurnEnded);
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
        if (item is AgentProblemViewModel)
        {
            FinishReply();
            Speak(Strings.Agent_SomethingWentWrongSeeThePanel);
        }

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
                    ServerProblem = Strings.Agent_WaitForTheAgentToFinish;
                    return;
                }

                _server.Stop();
            }

            EnsureServer();
            _server.OutsideToken = OutsideTokenFromStore();
        }
        catch (Exception e) when (e is HttpListenerException or SocketException or InvalidOperationException or IOException)
        {
            ServerProblem = string.Format(Strings.Agent_CouldNotListenOnPort, Settings.OutsidePort, e.Message);
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
        FinishReply();
        Speak(Strings.Agent_TheAgentIsAskingForYour);
        Add(approval);
        IsOpen = true;
        AttentionRequested?.Invoke();

        using var timeout = new CancellationTokenSource(ApprovalTimeout);
        await using var withdraw = timeout.Token.Register(() => Dispatcher.UIThread.Post(() => approval.Withdraw(Strings.Agent_NobodyAnsweredInTime)));
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
        FinishReply();
        Add(row);
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
        CancelListening();
        _speechToText.Dispose();
        _cancel?.Cancel();
        _server?.Dispose();
        _speech.Dispose();

        GC.SuppressFinalize(this);
    }
}
