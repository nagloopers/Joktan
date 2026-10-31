using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Certificates;
using Joktan.Core;
using Joktan.Core.ViewModels;

namespace Joktan.Mqtt.ViewModels;

/// <summary>
/// Dev Tools / Brokers / MQTT: one tab per MQTT client. Each client connects to its own
/// broker and publishes test messages, typed in by hand or read from its own folder of JSON files.
/// Specified in docs/features/dev-tools/connectivity/mqtt/spec.md.
/// </summary>
public partial class MqttViewModel : PageViewModel
{
    private readonly MqttSettings _settings;
    private readonly CredentialStore _credentials = CredentialStore.Create();

    private readonly CertificateStore? _certificates;

    /// <param name="certificates">The certificates made on the Certificates page, offered in the client settings.</param>
    /// <param name="settings">The settings, if already read; otherwise they are read here.</param>
    public MqttViewModel(CertificateStore? certificates = null, MqttSettings? settings = null)
        : base("MQTT", "Dev Tools / Brokers / MQTT")
    {
        _certificates = certificates;
        _settings = settings ?? MqttSettings.Load();
        foreach (var profile in _settings.Clients)
            Clients.Add(NewClient(profile));
        MessagingClient = NewClient(_settings.MessagingServer());
        if (_settings.Messaging.ShowInBrokers)
            Clients.Add(MessagingClient);
        UpdateRemovable();
        SelectedClient = Find(_settings.SelectedClient) ?? Clients[0];
        _settings.Save();
    }

    /// <summary>The clients, one tab each. Shared with the topic explorer.</summary>
    public ObservableCollection<MqttClientViewModel> Clients { get; } = [];

    /// <summary>The client for the alerts and messages server. It is in <see cref="Clients"/> only while <see cref="IsMessagingClientShown"/>.</summary>
    public MqttClientViewModel MessagingClient { get; }

    /// <summary>The server for alerts and messages, and how the user appears in messages.</summary>
    public MessagingSettings Messaging => _settings.Messaging;

    /// <summary>
    /// Whether the alerts and messages server has a tab on this page and in the topic explorer. Taking
    /// it away leaves it connected, but stops what only those tabs could stop.
    /// </summary>
    public bool IsMessagingClientShown
    {
        get => Clients.Contains(MessagingClient);
        set
        {
            if (value == IsMessagingClientShown)
                return;

            Messaging.ShowInBrokers = value;
            if (value)
            {
                Clients.Add(MessagingClient);
            }
            else
            {
                MessagingClient.StopSending();
                Clients.Remove(MessagingClient);
                if (SelectedClient == MessagingClient || SelectedClient is null)
                    SelectedClient = Clients[0];
            }

            SaveSettings();
            OnPropertyChanged();
        }
    }

    // Every client, with the alerts and messages server even when it has no tab.
    private IEnumerable<MqttClientViewModel> AllClients => IsMessagingClientShown ? Clients : Clients.Append(MessagingClient);

    /// <summary>
    /// Every MQTT client whose TLS settings use a file of this certificate, or a copy of one, by name.
    /// </summary>
    public List<string> ClientsUsing(StoredCertificate entry)
    {
        var folder = Path.GetFullPath(entry.Folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return AllClients
            .Select(c => c.Profile)
            .Where(c => c.CaCertificateId == entry.Id || c.ClientCertificateId == entry.Id
                        || new[] { c.CaCertificate, c.ClientCertificate, c.ClientKey }.Any(path => IsIn(path, folder)))
            .Select(c => c.Name)
            .ToList();

        static bool IsIn(string path, string folder)
        {
            if (path.Trim().Length == 0)
                return false;
            try
            {
                return Path.GetFullPath(path.Trim()).StartsWith(folder, OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return false;
            }
        }
    }

    [ObservableProperty]
    public partial MqttClientViewModel? SelectedClient { get; set; }

    /// <summary>Shows the client editor and returns whether it was saved. Set by the view.</summary>
    public Func<ClientSettingsViewModel, Task<bool>>? EditClientSettings { get; set; }

    [RelayCommand]
    private async Task AddClientAsync()
    {
        if (EditClientSettings is null)
            return;

        var profile = new ClientProfile { Name = UnusedName(), Host = "localhost", ClientId = ClientProfile.NewClientId() };
        var editor = new ClientSettingsViewModel(profile, "", "", AllClients.Select(c => c.Name).ToList(), isNew: true, _certificates);
        if (!await EditClientSettings(editor))
            return;

        var client = NewClient(editor.ToProfile());
        client.Apply(client.Profile, editor.Password, editor.KeyPassword);
        // The alerts and messages server stays the last tab.
        Clients.Insert(IsMessagingClientShown ? Clients.Count - 1 : Clients.Count, client);
        UpdateRemovable();
        SelectedClient = client;
        SaveSettings();
    }

    private async Task EditClientAsync(MqttClientViewModel client)
    {
        if (EditClientSettings is null)
            return;

        var others = AllClients.Where(c => c != client).Select(c => c.Name).ToList();
        var editor = new ClientSettingsViewModel(client.Profile.Clone(), client.Password, client.KeyPassword, others, isNew: false, _certificates);
        if (!await EditClientSettings(editor) || !client.CanEdit)
            return;

        client.Apply(editor.ToProfile(), editor.Password, editor.KeyPassword);
        SaveSettings();
    }

    private void RemoveClient(MqttClientViewModel client)
    {
        if (!client.IsRemovable || !client.CanEdit)
            return;

        var index = Clients.IndexOf(client);
        Clients.Remove(client);
        client.Dispose();
        UpdateRemovable();
        if (SelectedClient == client || SelectedClient is null)
            SelectedClient = Clients[Math.Min(index, Clients.Count - 1)];
        SaveSettings();
    }

    /// <summary>The client with this name, ignoring case, or null.</summary>
    public MqttClientViewModel? Find(string? name)
        => Clients.FirstOrDefault(c => c.Name.Equals(name?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Writes the settings to disk now. Edits are otherwise saved shortly after typing stops, so
    /// this is called on exit to keep anything typed in the last moment.
    /// </summary>
    public void SaveSettings()
    {
        foreach (var client in AllClients)
            client.StoreSettings();
        _settings.Clients = Clients.Where(c => c != MessagingClient).Select(c => c.Profile).ToList();
        Messaging.Server = MessagingClient.Profile;
        _settings.SelectedClient = SelectedClient?.Name ?? "";
        _settings.Save();
    }

    /// <summary>Saves everything and waits for message switches still being written. Called when the app exits.</summary>
    public void SaveState()
    {
        SaveSettings();
        foreach (var client in AllClients)
            client.FinishWrites();
    }

    partial void OnSelectedClientChanged(MqttClientViewModel? value)
    {
        if (value is not null && _settings.SelectedClient != value.Name)
        {
            _settings.SelectedClient = value.Name;
            _settings.Save();
        }
    }

    private MqttClientViewModel NewClient(ClientProfile profile)
        => new(profile, _credentials, SaveSettings)
        {
            EditClientRequested = EditClientAsync,
            RemoveClientRequested = RemoveClient,
        };

    // The last client cannot be removed, and the alerts and messages server neither counts as one nor can be.
    private void UpdateRemovable()
    {
        var removable = Clients.Count(c => c != MessagingClient) > 1;
        foreach (var client in Clients)
            client.IsRemovable = removable && client != MessagingClient;
    }

    private string UnusedName()
    {
        for (var i = Clients.Count + 1; ; i++)
        {
            if (!AllClients.Any(c => c.Name.Equals($"client {i}", StringComparison.OrdinalIgnoreCase)))
                return $"client {i}";
        }
    }
}
