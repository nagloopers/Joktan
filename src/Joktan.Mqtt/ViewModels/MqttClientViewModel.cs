using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Avalonia.Threading;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Core;
using Joktan.Core.ViewModels;
using Joktan.Mqtt.Controls;

namespace Joktan.Mqtt.ViewModels;

/// <summary>
/// One tab of the MQTT page: a client with its own connection to a broker, its own manual message,
/// its own folder of messages, and its own activity log. Several clients can be connected at once.
/// </summary>
public partial class MqttClientViewModel : ViewModelBase, IDisposable
{
    private const int MaxLogEntries = 500;

    private readonly CredentialStore _credentials;
    private readonly Action _save;
    private readonly FolderPublisher _publisher;
    private readonly SettingsWriter _writer;
    private readonly DispatcherTimer _runTimer;
    private readonly DispatcherTimer _saveTimer;
    private readonly DispatcherTimer _rescanTimer;
    private readonly HashSet<string> _collapsedGroups = new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher? _watcher;
    private ConnectionState _loggedState;
    private bool _ticking;
    private bool _loading = true;
    private int _folderReads;
    private int _switches;

    /// <param name="save">Saves every client's settings; called shortly after this one's change.</param>
    public MqttClientViewModel(ClientProfile profile, CredentialStore credentials, Action save)
    {
        _credentials = credentials;
        _save = save;
        Profile = profile;
        Connection.Name = profile.Name;
        Password = ReadSecret(PasswordAccount) ?? "";
        KeyPassword = ReadSecret(KeyAccount) ?? "";

        _publisher = new FolderPublisher(Connection, Log);
        _writer = new SettingsWriter((path, e) => Dispatcher.UIThread.Post(() =>
            Log(new ActivityEntry(DateTime.Now, "Failed", "", Path.GetFileName(path), $"Settings not saved: {e.Message}"))));
        _runTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, OnRunTick);
        _saveTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, OnSaveTick);
        _rescanTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(300), DispatcherPriority.Background, OnRescanTick);
        _runTimer.Stop();
        _saveTimer.Stop();
        _rescanTimer.Stop();

        ManualTopic = profile.ManualTopic;
        ManualBody = profile.ManualBody;
        ManualQos = profile.ManualQos;
        ManualRetain = profile.ManualRetain;
        ManualGzip = profile.ManualGzip;
        FolderPath = profile.Folder;
        IsBrokerCollapsed = profile.BrokerCollapsed;
        IsManualCollapsed = profile.ManualCollapsed;
        IsFolderCollapsed = profile.FolderCollapsed;
        IsActivityCollapsed = profile.ActivityCollapsed;
        _loading = false;

        Connection.StateChanged += () => Dispatcher.UIThread.Post(OnConnectionChanged);
        _ = LoadFolderAsync();
        Watch();
    }

    public ClientProfile Profile { get; private set; }

    public MqttConnection Connection { get; } = new();

    /// <summary>The broker password; saved in the credential store after a successful connection.</summary>
    public string Password { get; private set; }

    /// <summary>The password of the client certificate or its private key, saved the same way.</summary>
    public string KeyPassword { get; private set; }

    /// <summary>Opens the client's settings. Set by the MQTT page.</summary>
    public Func<MqttClientViewModel, Task>? EditClientRequested { get; set; }

    /// <summary>Removes the client. Set by the MQTT page.</summary>
    public Action<MqttClientViewModel>? RemoveClientRequested { get; set; }

    /// <summary>Asks the user for a folder. Set by the view, which owns the system dialog.</summary>
    public Func<Task<string?>>? PickFolder { get; set; }

    /// <summary>Shows the settings editor and returns whether it was saved. Set by the view.</summary>
    public Func<MessageSettingsViewModel, Task<bool>>? EditSettings { get; set; }

    /// <summary>Every message in the folder, in list order.</summary>
    public ObservableCollection<FolderMessageRowViewModel> Rows { get; } = [];

    /// <summary>The same messages, arranged under their groups.</summary>
    public ObservableCollection<MessageGroupViewModel> Groups { get; } = [];

    /// <summary>
    /// What the message list shows: the visible groups, each followed by its rows while it is
    /// expanded. One flat list lets the view build only the rows that are on screen.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<object> Items { get; private set; } = [];

    public ObservableCollection<ActivityEntry> Entries { get; } = [];

    // Client

    public string Name => Profile.Name;

    public string Address => $"{Profile.Host.Trim()}:{Profile.Port.Trim()}";

    /// <summary>The text colour of the client's tabs, or null for the theme's.</summary>
    public IBrush? TabTextBrush => TabColors.TextBrush(Profile.TabText, Profile.TabFill);

    /// <summary>The fill of the client's tabs, or unset for the theme's.</summary>
    public object TabFillBrush => TabColors.BrushOrUnset(Profile.TabFill);

    /// <summary>The colour behind the cards on the client's tabs, or unset for the theme's.</summary>
    public object PageBrush => TabColors.BrushOrUnset(Profile.PageBackground);

    public string ClientId => Profile.ClientId;

    public string Login => Profile.Username.Trim() is { Length: > 0 } username ? username : "Anonymous";

    public string Security => !Profile.UseTls ? "No TLS"
        : Profile.UsesClientCertificate ? "Mutual TLS"
        : "TLS";

    public string SecurityDetail => !Profile.UseTls ? "Not encrypted"
        : (Profile.UsesClientCertificate ? $"Client certificate {Path.GetFileName(Profile.ClientCertificate.Trim())}. " : "")
          + (Profile.AcceptAnyServerCertificate ? "The broker's certificate is not checked."
              : Profile.CaCertificate.Trim().Length > 0 ? $"The broker's certificate must be signed by {Path.GetFileName(Profile.CaCertificate.Trim())}."
              : "The broker's certificate must be trusted by this computer.");

    /// <summary>Whether this client may be removed; the last one may not. Set by the MQTT page.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveClientCommand))]
    public partial bool IsRemovable { get; set; }

    public ConnectionState State => Connection.State;

    /// <summary>The state in a word; a failure's reason is shown on a line of its own.</summary>
    public string StateText => State.ToString();

    public string? FailureReason => IsFailed ? Connection.FailureReason : null;

    public bool IsConnected => State == ConnectionState.Connected;

    public bool IsPending => State is ConnectionState.Connecting or ConnectionState.Reconnecting;

    public bool IsFailed => State == ConnectionState.Failed;

    /// <summary>Settings can be changed, and the client removed, only while it is not connected.</summary>
    public bool CanEdit => State is ConnectionState.Disconnected or ConnectionState.Failed;

    public bool ShowDisconnect => !CanEdit;

    public string DisconnectText => State == ConnectionState.Connecting ? "Cancel" : "Disconnect";

    private int? ParsedPort => ClientSettingsViewModel.ParsePort(Profile.Port);

    private bool CanConnect => CanEdit && ClientSettingsViewModel.IsValidHost(Profile.Host) && ParsedPort is not null;

    private bool CanRemoveClient => CanEdit && IsRemovable;

    // Manual message

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ManualTopicError))]
    public partial string ManualTopic { get; set; } = "";

    [ObservableProperty]
    public partial string ManualBody { get; set; } = "";

    [ObservableProperty]
    public partial int ManualQos { get; set; }

    [ObservableProperty]
    public partial bool ManualRetain { get; set; }

    /// <summary>Whether the body is gzipped before it is sent.</summary>
    [ObservableProperty]
    public partial bool ManualGzip { get; set; }

    /// <summary>Shown under the topic box; an empty topic is not reported as an error.</summary>
    public string? ManualTopicError => ManualTopic.Length == 0 ? null : TopicRules.Validate(ManualTopic);

    private bool CanSendManual => IsConnected && TopicRules.Validate(ManualTopic) is null;

    // Folder messages

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FolderText))]
    public partial string? FolderPath { get; set; }

    [ObservableProperty]
    public partial string? FolderError { get; set; }

    [ObservableProperty]
    public partial string FolderSummary { get; set; } = "";

    /// <summary>Whether the folder is being read in the background.</summary>
    [ObservableProperty]
    public partial bool IsReadingFolder { get; set; }

    [ObservableProperty]
    public partial bool IsRunning { get; set; }

    /// <summary>The group names in use, offered when a message is edited.</summary>
    public IReadOnlyList<string> GroupNames { get; private set; } = [];

    public string FolderText => string.IsNullOrEmpty(FolderPath) ? "No folder chosen" : FolderPath;

    // Activity

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedBody), nameof(HasSelectedBody))]
    public partial ActivityEntry? SelectedEntry { get; set; }

    public string? SelectedBody => SelectedEntry?.Body;

    public bool HasSelectedBody => SelectedEntry?.Body is not null;

    // Cards that are collapsed

    [ObservableProperty]
    public partial bool IsBrokerCollapsed { get; set; }

    [ObservableProperty]
    public partial bool IsManualCollapsed { get; set; }

    [ObservableProperty]
    public partial bool IsFolderCollapsed { get; set; }

    [ObservableProperty]
    public partial bool IsActivityCollapsed { get; set; }

    [RelayCommand]
    private void ToggleBroker() => IsBrokerCollapsed = !IsBrokerCollapsed;

    [RelayCommand]
    private void ToggleManual() => IsManualCollapsed = !IsManualCollapsed;

    [RelayCommand]
    private void ToggleFolder() => IsFolderCollapsed = !IsFolderCollapsed;

    [RelayCommand]
    private void ToggleActivity() => IsActivityCollapsed = !IsActivityCollapsed;

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (_loading)
            return;

        switch (e.PropertyName)
        {
            case nameof(ManualTopic) or nameof(ManualBody) or nameof(ManualQos) or nameof(ManualRetain) or nameof(ManualGzip):
                _saveTimer.Stop();
                _saveTimer.Start();
                UpdateCommands();
                break;
            case nameof(IsBrokerCollapsed) or nameof(IsManualCollapsed) or nameof(IsFolderCollapsed) or nameof(IsActivityCollapsed):
                _saveTimer.Stop();
                _saveTimer.Start();
                break;
        }
    }

    /// <summary>Takes the settings saved in the client editor.</summary>
    public void Apply(ClientProfile profile, string password, string keyPassword)
    {
        StoreSettings();
        profile.Folder = Profile.Folder;
        profile.ManualTopic = Profile.ManualTopic;
        profile.ManualBody = Profile.ManualBody;
        profile.ManualQos = Profile.ManualQos;
        profile.ManualRetain = Profile.ManualRetain;
        profile.ManualGzip = Profile.ManualGzip;
        profile.BrokerCollapsed = Profile.BrokerCollapsed;
        profile.ManualCollapsed = Profile.ManualCollapsed;
        profile.FolderCollapsed = Profile.FolderCollapsed;
        profile.ActivityCollapsed = Profile.ActivityCollapsed;
        profile.ExplorerFilters = Profile.ExplorerFilters;
        profile.ExplorerSubscribed = Profile.ExplorerSubscribed;
        profile.ExplorerCharts = Profile.ExplorerCharts;

        Profile = profile;
        Connection.Name = profile.Name;
        Password = password;
        KeyPassword = keyPassword;
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Address));
        OnPropertyChanged(nameof(ClientId));
        OnPropertyChanged(nameof(Login));
        OnPropertyChanged(nameof(Security));
        OnPropertyChanged(nameof(SecurityDetail));
        OnPropertyChanged(nameof(TabTextBrush));
        OnPropertyChanged(nameof(TabFillBrush));
        OnPropertyChanged(nameof(PageBrush));
        ConnectCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Copies what the tab shows into <see cref="Profile"/>, ready to be saved.</summary>
    public void StoreSettings()
    {
        _saveTimer.Stop();
        Profile.Folder = FolderPath;
        Profile.ManualTopic = ManualTopic;
        Profile.ManualBody = ManualBody;
        Profile.ManualQos = ManualQos;
        Profile.ManualRetain = ManualRetain;
        Profile.ManualGzip = ManualGzip;
        Profile.BrokerCollapsed = IsBrokerCollapsed;
        Profile.ManualCollapsed = IsManualCollapsed;
        Profile.FolderCollapsed = IsFolderCollapsed;
        Profile.ActivityCollapsed = IsActivityCollapsed;
    }

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync()
    {
        if (ParsedPort is not { } port)
            return;

        var profile = Profile;
        var username = profile.Username.Trim();
        var tls = !profile.UseTls ? null : new TlsEndpoint(
            NullIfEmpty(profile.CaCertificate),
            NullIfEmpty(profile.ClientCertificate),
            NullIfEmpty(profile.ClientKey),
            profile.UsesClientCertificate && KeyPassword.Length > 0 ? KeyPassword : null,
            profile.AcceptAnyServerCertificate);

        await Connection.ConnectAsync(new BrokerEndpoint(
            profile.Host.Trim(), port, profile.ClientId,
            username.Length > 0 ? username : null,
            username.Length > 0 ? Password : null,
            tls));

        if (Connection.IsConnected)
            RememberSecrets();
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        if (IsRunning)
            Stop();
        await Connection.DisconnectAsync();
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private Task EditClientAsync() => EditClientRequested?.Invoke(this) ?? Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanRemoveClient))]
    private void RemoveClient() => RemoveClientRequested?.Invoke(this);

    [RelayCommand(CanExecute = nameof(CanSendManual))]
    private async Task SendManualAsync()
    {
        var now = DateTime.Now;
        var body = Encoding.UTF8.GetBytes(ManualBody);
        var payload = ManualGzip ? Gzip.Compress(body) : body;
        var (ok, error) = await Connection.PublishAsync(ManualTopic, payload, ManualQos, ManualRetain);

        if (ok)
        {
            MqttMetrics.RecordSent(manual: true);
            Log(new ActivityEntry(now, "Sent", ManualTopic, "Manual", ActivityEntry.SizeDetail(body.Length, payload.Length, ManualGzip), ManualBody));
        }
        else
        {
            MqttMetrics.RecordFailed();
            Log(new ActivityEntry(now, "Failed", ManualTopic, "Manual", error ?? "Unknown error"));
        }
    }

    /// <summary>Lays the manual body out as indented JSON, if it is JSON.</summary>
    public void FormatManualBody()
    {
        try
        {
            if (JsonNode.Parse(ManualBody) is { } node)
                ManualBody = node.ToJsonString(JsonFormats.Indented);
        }
        catch (JsonException)
        {
            // Not JSON: the body is sent as typed.
        }
    }

    [RelayCommand]
    private async Task ChooseFolderAsync()
    {
        if (PickFolder is not null && await PickFolder() is { } path)
            UseFolder(path);
    }

    /// <summary>Sends the messages in this folder from now on; null for none.</summary>
    public void UseFolder(string? path)
    {
        FolderPath = path;
        StoreSettings();
        _save();

        // The old folder's messages are not shown while the new one is read.
        Rows.Clear();
        Groups.Clear();
        Items = [];
        _ = LoadFolderAsync();
        Watch();
    }

    [RelayCommand]
    private Task RescanAsync() => LoadFolderAsync();

    // Schedules follow the connection: they start when it is made and stop when it is given up.
    private void Start()
    {
        if (IsRunning)
            return;

        _publisher.Start(DateTime.Now);
        IsRunning = true;
        _runTimer.Start();
        RefreshRows();
    }

    /// <summary>Stops sending the folder's messages on their schedules, if they are being sent.</summary>
    public void StopSending() => Stop();

    private void Stop()
    {
        if (!IsRunning)
            return;

        _runTimer.Stop();
        _publisher.Stop();
        IsRunning = false;
        RefreshRows();
    }

    [RelayCommand]
    private void ToggleGroup(MessageGroupViewModel? group)
    {
        if (group is null)
            return;

        group.IsExpanded = !group.IsExpanded;
        if (group.IsExpanded)
            _collapsedGroups.Remove(group.Path);
        else
            _collapsedGroups.Add(group.Path);
        group.UpdateVisibility();
        UpdateItems();
    }

    // A switch changes the message in memory at once; its settings file is written in the background.
    private void SetEnabled(FolderMessageRowViewModel row, bool enabled)
    {
        Switch(row, enabled, DateTime.Now);
        SwitchesChanged();
    }

    private void SetGroupEnabled(MessageGroupViewModel group, bool enabled)
    {
        var now = DateTime.Now;
        foreach (var row in group.AllRows.Where(r => r.CanSwitch && r.IsOn != enabled).ToList())
            Switch(row, enabled, now);
        SwitchesChanged();
    }

    // The switch is the message's "enabled" setting, so it is kept in the settings file.
    private void Switch(FolderMessageRowViewModel row, bool enabled, DateTime now)
    {
        var message = row.Message;
        _switches++;
        _publisher.SetEnabled(message, enabled, now);
        _writer.Write(MessageSettings.PathFor(message.FilePath), message.Settings.ToJson());
        row.Refresh();
    }

    private void SwitchesChanged()
    {
        foreach (var group in Groups)
            group.Refresh();
        UpdateFolderSummary();
    }

    private bool CanSendNow(FolderMessageRowViewModel? row) => IsConnected && row is { IsInvalid: false };

    [RelayCommand(CanExecute = nameof(CanSendNow))]
    private async Task SendNowAsync(FolderMessageRowViewModel? row)
    {
        if (row is null)
            return;

        await _publisher.SendNowAsync(row.Message, DateTime.Now);
        row.Refresh();
    }

    private static bool CanEditMessage(FolderMessageRowViewModel? row) => row?.Message.Body is not null;

    [RelayCommand(CanExecute = nameof(CanEditMessage))]
    private async Task EditMessageAsync(FolderMessageRowViewModel? row)
    {
        if (row is null || EditSettings is null)
            return;

        // The editor saves the whole settings file, so a switch still being written must land first.
        await _writer.FlushAsync();
        if (await EditSettings(new MessageSettingsViewModel(row.Message, GroupNames)))
            await LoadFolderAsync();
    }

    [RelayCommand]
    private void ClearLog()
    {
        SelectedEntry = null;
        Entries.Clear();
    }

    // The files are read on a background thread so that a large folder does not hold up the
    // window. Only the latest read is shown. Reading before the switches are written would undo
    // them, and a switch made during the read may have been missed, so the read waits for the
    // writes and starts again after a switch.
    private async Task LoadFolderAsync()
    {
        var read = ++_folderReads;
        IsReadingFolder = true;
        UpdateFolderSummary();
        try
        {
            FolderScan scan;
            int switches;
            do
            {
                await _writer.FlushAsync();
                switches = _switches;
                var folder = FolderPath;
                scan = await Task.Run(() => FolderPublisher.Read(folder));
                if (read != _folderReads)
                    return;
            } while (switches != _switches);

            ShowFolder(scan);
        }
        finally
        {
            if (read == _folderReads)
            {
                IsReadingFolder = false;
                UpdateFolderSummary();
            }
        }
    }

    private void ShowFolder(FolderScan scan)
    {
        _publisher.Apply(scan, DateTime.Now);
        FolderError = _publisher.FolderError;

        Rows.Clear();
        foreach (var message in _publisher.Messages)
            Rows.Add(new FolderMessageRowViewModel(message, SetEnabled));

        // Named groups and their sub-groups in alphabetical order, then the messages that have no group.
        Groups.Clear();
        foreach (var group in MessageGroupViewModel.Build(Rows, path => !_collapsedGroups.Contains(path), SetGroupEnabled))
            Groups.Add(group);

        GroupNames = Groups.Where(g => g.Path.Length > 0).Select(g => g.Path).ToList();
        UpdateItems();
    }

    private void UpdateItems()
    {
        var items = new List<object>();
        foreach (var group in Groups.Where(g => g.IsVisible))
        {
            items.Add(group);
            if (group.ShowRows)
                items.AddRange(group.Rows);
        }

        Items = items;
    }

    private void UpdateFolderSummary()
    {
        var invalid = Rows.Count(r => r.IsInvalid);
        var on = Rows.Count(r => r.IsOn);
        FolderSummary = string.IsNullOrEmpty(FolderPath) ? ""
            : IsReadingFolder && Rows.Count == 0 ? "Reading the folder…"
            : FolderError is not null ? ""
            : Rows.Count == 0 ? "No .json files found in this folder."
            : $"{Rows.Count} messages, {on} switched on" + (invalid == 0 ? "" : $", {invalid} invalid");
        UpdateCommands();
    }

    private void Watch()
    {
        _watcher?.Dispose();
        _watcher = null;
        if (string.IsNullOrEmpty(FolderPath) || !Directory.Exists(FolderPath))
            return;

        try
        {
            var watcher = new FileSystemWatcher(FolderPath)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
            };
            watcher.Created += OnFolderChanged;
            watcher.Changed += OnFolderChanged;
            watcher.Deleted += OnFolderChanged;
            watcher.Renamed += OnFolderChanged;
            watcher.EnableRaisingEvents = true;
            _watcher = watcher;
        }
        catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException)
        {
            // Without a watcher the folder is still read on Rescan.
        }
    }

    // Changes arrive in bursts on a background thread; wait for them to settle, then scan once.
    // The settings files written for the switches are already known, so they are not read back.
    private void OnFolderChanged(object sender, FileSystemEventArgs e)
    {
        if (_writer.WroteRecently(e.FullPath))
            return;

        Dispatcher.UIThread.Post(() =>
        {
            _rescanTimer.Stop();
            _rescanTimer.Start();
        });
    }

    private void OnRescanTick(object? sender, EventArgs e)
    {
        _rescanTimer.Stop();
        _ = LoadFolderAsync();
    }

    private async void OnRunTick(object? sender, EventArgs e)
    {
        if (_ticking)
            return;

        _ticking = true;
        try
        {
            if (await _publisher.TickAsync(DateTime.Now))
                RefreshRows();
        }
        finally
        {
            _ticking = false;
        }
    }

    private void OnSaveTick(object? sender, EventArgs e)
    {
        StoreSettings();
        _save();
    }

    private void OnConnectionChanged()
    {
        var state = Connection.State;
        if (state != _loggedState)
        {
            _loggedState = state;
            var now = DateTime.Now;
            switch (state)
            {
                case ConnectionState.Connected:
                    Log(new ActivityEntry(now, "Connected", Detail: $"{Address} ({Security})"));
                    break;
                case ConnectionState.Reconnecting:
                    Log(new ActivityEntry(now, "Reconnecting", Detail: "The connection was lost"));
                    break;
                case ConnectionState.Disconnected:
                    Log(new ActivityEntry(now, "Disconnected"));
                    break;
                case ConnectionState.Failed:
                    Log(new ActivityEntry(now, "Failed", Detail: Connection.FailureReason ?? ""));
                    break;
            }
        }

        if (state == ConnectionState.Connected)
            Start();
        else if (state is ConnectionState.Disconnected or ConnectionState.Failed)
            Stop();

        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(FailureReason));
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(IsPending));
        OnPropertyChanged(nameof(IsFailed));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(ShowDisconnect));
        OnPropertyChanged(nameof(DisconnectText));
        UpdateCommands();
    }

    private string? PasswordAccount => Profile.Username.Trim() is { Length: > 0 } username
        ? CredentialStore.AccountFor(username, Profile.Host.Trim(), Profile.Port.Trim())
        : null;

    private string? KeyAccount => Profile.UsesClientCertificate
        ? CredentialStore.AccountForKey(Profile.ClientKey.Trim().Length > 0 && !CertificateLoader.IsPkcs12(Profile.ClientCertificate)
            ? Profile.ClientKey
            : Profile.ClientCertificate)
        : null;

    private string? ReadSecret(string? account) => account is null ? null : _credentials.Read(account);

    // Passwords go to the operating system's credential store, never to the settings file.
    private void RememberSecrets()
    {
        Remember(PasswordAccount, Password);
        Remember(KeyAccount, KeyPassword);

        void Remember(string? account, string secret)
        {
            if (account is null)
                return;
            if (secret.Length == 0)
                _credentials.Delete(account);
            else
                _credentials.Write(account, secret);
        }
    }

    private void Log(ActivityEntry entry)
    {
        Entries.Insert(0, entry);
        while (Entries.Count > MaxLogEntries)
            Entries.RemoveAt(Entries.Count - 1);
    }

    private void RefreshRows()
    {
        foreach (var row in Rows)
            row.Refresh();
    }

    /// <summary>For the message a row shows, after the folder has been read again.</summary>
    public FolderMessageRowViewModel? RowFor(string fileName)
        => Rows.FirstOrDefault(r => r.File == fileName);

    private void UpdateCommands()
    {
        ConnectCommand.NotifyCanExecuteChanged();
        EditClientCommand.NotifyCanExecuteChanged();
        RemoveClientCommand.NotifyCanExecuteChanged();
        SendManualCommand.NotifyCanExecuteChanged();
        SendNowCommand.NotifyCanExecuteChanged();
        EditMessageCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsRunningChanged(bool value) => UpdateCommands();

    /// <summary>Waits, for a short while at most, until the switches have been written to disk. Called on exit.</summary>
    public void FinishWrites() => _writer.FlushAsync().Wait(TimeSpan.FromSeconds(5));

    /// <summary>Stops the schedules, the folder watcher and the connection, when the client is removed.</summary>
    public void Dispose()
    {
        _runTimer.Stop();
        _saveTimer.Stop();
        _rescanTimer.Stop();
        _watcher?.Dispose();
        _watcher = null;
        _ = Connection.DisconnectAsync();
    }

    private static string? NullIfEmpty(string value) => value.Trim() is { Length: > 0 } trimmed ? trimmed : null;
}
