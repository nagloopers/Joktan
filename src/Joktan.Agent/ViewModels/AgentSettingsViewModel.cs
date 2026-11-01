using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Agent.Clients;
using Joktan.Agent.Speech;
using Joktan.Core;
using Joktan.Core.ViewModels;

namespace Joktan.Agent.ViewModels;

/// <summary>Settings / Agent: which command-line client the panel runs and how, and who else may use the MCP server.</summary>
public partial class AgentSettingsViewModel : PageViewModel
{
    private readonly AgentSettings _settings;
    private const string SystemVoice = "System default";

    private bool _loading;
    private bool _isShown;

    public AgentSettingsViewModel(AgentViewModel agent) : base("Agent", "Settings / Agent")
    {
        Agent = agent;
        _settings = agent.Settings;
        Agent.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AgentViewModel.SelectedClient))
                LoadClient();
        };
        LoadClient();
    }

    public AgentViewModel Agent { get; }

    [ObservableProperty]
    public partial IReadOnlyList<string> Voices { get; private set; } = [SystemVoice];

    /// <summary>Says what is missing when this computer has nothing to read with.</summary>
    [ObservableProperty]
    public partial string? SpeechProblem { get; private set; }

    public bool VoiceInput
    {
        get => _settings.VoiceInput;
        set
        {
            _settings.VoiceInput = value;
            _settings.Save();
            Agent.VoiceInputChanged();
            OnPropertyChanged();
        }
    }

    /// <summary>Says so if this build came without the speech model.</summary>
    public string? VoiceProblem => SpeechToText.IsAvailable ? null : "This copy of Joktan came without its speech model, so the microphone button is not shown.";

    public string SelectedVoice
    {
        get => _settings.SpeechVoice.Length > 0 ? _settings.SpeechVoice : SystemVoice;
        set
        {
            // A list being replaced clears its selection, which is no choice of the user's.
            if (value is null)
                return;
            _settings.SpeechVoice = value == SystemVoice ? "" : value;
            _settings.Save();
            OnPropertyChanged();
        }
    }

    public double SpeechSpeed
    {
        get => _settings.SpeechSpeed;
        set
        {
            _settings.SpeechSpeed = (int)Math.Round(value / 10) * 10;
            _settings.Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(SpeechSpeedText));
        }
    }

    public string SpeechSpeedText => $"{_settings.SpeechSpeed}%";

    /// <summary>Puts text on the clipboard. Set by the view.</summary>
    public Func<string, Task>? CopyText { get; set; }

    public bool IsCustom => Agent.SelectedClient is CustomClient;

    public string ModelHint => Agent.SelectedClient.ModelHint;

    public string PathHint => IsCustom ? "The whole path of the program." : "Empty looks for it in the usual folders.";

    [ObservableProperty]
    public partial string ProgramPath { get; set; } = "";

    [ObservableProperty]
    public partial string Model { get; set; } = "";

    [ObservableProperty]
    public partial string ExtraArguments { get; set; } = "";

    [ObservableProperty]
    public partial string? ClientStatus { get; private set; }

    [ObservableProperty]
    public partial bool ClientFound { get; private set; }

    [ObservableProperty]
    public partial string? Message { get; private set; }

    public string CustomArguments
    {
        get => _settings.CustomArguments;
        set
        {
            _settings.CustomArguments = value;
            _settings.Save();
            OnPropertyChanged();
        }
    }

    public bool AllowClientTools
    {
        get => _settings.AllowClientTools;
        set
        {
            _settings.AllowClientTools = value;
            _settings.Save();
            OnPropertyChanged();
        }
    }

    public bool OutsideEnabled
    {
        get => _settings.OutsideEnabled;
        set
        {
            _settings.OutsideEnabled = value;
            _settings.Save();
            Agent.ApplyOutside();
            OnPropertyChanged();
            OnPropertyChanged(nameof(OutsideUrl));
            Message = null;
        }
    }

    public string OutsidePort
    {
        get => _settings.OutsidePort.ToString(System.Globalization.CultureInfo.InvariantCulture);
        set
        {
            if (!int.TryParse(value, out var port) || port is < 1024 or > 65535)
                throw new ArgumentException("Give a port from 1024 to 65535.");
            if (port == _settings.OutsidePort)
                return;
            _settings.OutsidePort = port;
            _settings.Save();
            if (_settings.OutsideEnabled)
                Agent.ApplyOutside();
            OnPropertyChanged();
            OnPropertyChanged(nameof(OutsideUrl));
        }
    }

    public string OutsideUrl => $"http://localhost:{_settings.OutsidePort}/mcp";

    partial void OnProgramPathChanged(string value)
    {
        if (_loading)
            return;
        Profile.Path = value;
        _settings.Save();
        _ = CheckAsync();
    }

    partial void OnModelChanged(string value)
    {
        if (_loading)
            return;
        Profile.Model = value;
        _settings.Save();
    }

    partial void OnExtraArgumentsChanged(string value)
    {
        if (_loading)
            return;
        Profile.ExtraArguments = value;
        _settings.Save();
    }

    private AgentClientProfile Profile => _settings.ProfileOf(Agent.SelectedClient.Kind);

    private void LoadClient()
    {
        _loading = true;
        ProgramPath = Profile.Path;
        Model = Profile.Model;
        ExtraArguments = Profile.ExtraArguments;
        _loading = false;
        OnPropertyChanged(nameof(IsCustom));
        OnPropertyChanged(nameof(ModelHint));
        OnPropertyChanged(nameof(PathHint));
        if (_isShown)
            _ = CheckAsync();
    }

    /// <summary>Looks for the programs when the page is first shown, so that starting the app runs none of them.</summary>
    public async Task ShownAsync()
    {
        if (_isShown)
            return;
        _isShown = true;
        _ = CheckAsync();

        SpeechProblem = SpeechOutput.FindProgram() is null
            ? "Nothing on this computer can read aloud. On Linux, install espeak-ng or speech-dispatcher."
            : null;
        var voices = await SpeechOutput.VoicesAsync();
        Voices = [SystemVoice, .. voices];
        OnPropertyChanged(nameof(SelectedVoice));
    }

    [RelayCommand]
    private void TrySpeech() => Agent.TrySpeech();

    /// <summary>Looks for the chosen client, and asks it its version.</summary>
    [RelayCommand]
    private async Task CheckAsync()
    {
        var client = Agent.SelectedClient;
        if (AgentProgram.Find(client, Profile) is not { } program)
        {
            ClientFound = false;
            ClientStatus = $"{client.DisplayName} was not found. {client.InstallHint}";
            return;
        }

        ClientFound = true;
        ClientStatus = program;
        if (client is not CustomClient && await AgentProgram.VersionAsync(program) is { Length: > 0 } version && Agent.SelectedClient == client)
            ClientStatus = $"{version} · {program}";
    }

    /// <summary>Copies a configuration that lets another client use the server. It holds the token.</summary>
    [RelayCommand]
    private async Task CopyConfigurationAsync()
    {
        if (Agent.OutsideToken is not { } token || CopyText is null)
            return;
        var configuration = new JsonObject
        {
            ["mcpServers"] = new JsonObject
            {
                ["joktan"] = new JsonObject
                {
                    ["type"] = "http",
                    ["url"] = OutsideUrl,
                    ["headers"] = new JsonObject { ["Authorization"] = $"Bearer {token}" },
                },
            },
        };
        await CopyText(configuration.ToJsonString(JsonFormats.Indented));
        Message = "Copied. It holds the token, so keep it private.";
    }

    [RelayCommand]
    private void ReplaceToken()
    {
        Agent.ReplaceOutsideToken();
        Message = "A new token is in use. Clients holding the old one are shut out.";
    }
}
