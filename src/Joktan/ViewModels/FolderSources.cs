using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Joktan.Api.ViewModels;
using Joktan.Certificates;
using Joktan.Core;
using Joktan.Mqtt.ViewModels;
using Joktan.Notes.ViewModels;
using Joktan.Passwords.ViewModels;
using Joktan.Settings.Backup;
using Joktan.TopicDashboards;
using Joktan.TopicDashboards.ViewModels;

namespace Joktan.ViewModels;

/// <summary>Says what each feature keeps and where, for Settings / Folders and Backup.</summary>
internal sealed class FolderSources(
    NotesViewModel notes,
    TopicDashboardsViewModel dashboards,
    ApiClientViewModel api,
    CertificateStore certificates,
    PasswordsViewModel passwords,
    MqttViewModel mqtt,
    Action openPasswords,
    Func<Task> flush,
    Action restart)
{
    private const string PrivateKeysWarning = "This holds private keys, and a backup zip is not encrypted. Keep the zip somewhere safe.";

    // Left out of the app settings. These first say where things are on this computer, which must stay as they are
    // because a restore puts things back where they are kept now. The rest are backed up on their own rows.
    private static readonly string[] NotAppSettings =
    [
        DataFolder.LocationFileName, "notes-location.json", "api-location.json", "dashboards-location.json", "passwords-settings.json", "backup.json",
        "certificates.json", "passwords.json", "passwords.vault", "dashboards.json", "notes", "api", "certificates", "mqtt-certificates",
    ];

    public IReadOnlyList<FolderSource> All()
    {
        var sources = new List<FolderSource>
        {
            SettingsSource(),
            new()
            {
                Id = "notes",
                Name = "Notes",
                CurrentPath = () => notes.Folder,
                DefaultPath = notes.DefaultFolder,
                Change = ChangeNotes,
                Item = () => new BackupItem("notes", "Notes", [new BackupRoot("folder", notes.Folder)]),
            },
            new()
            {
                Id = "dashboards",
                Name = "MQTT Dashboards",
                CurrentPath = () => dashboards.Folder,
                DefaultPath = dashboards.DefaultFolder,
                Change = ChangeDashboards,
                Item = () => new BackupItem("dashboards", "MQTT Dashboards", [new BackupRoot("file", Path.Combine(dashboards.Folder, "dashboards.json"), IsFile: true)]),
            },
            new()
            {
                Id = "api",
                Name = "API requests",
                CurrentPath = () => api.Folder,
                DefaultPath = api.DefaultFolder,
                Change = ChangeApi,
                Item = () => new BackupItem("api", "API requests", [new BackupRoot("folder", api.Folder)]),
            },
            new()
            {
                Id = "certificates",
                Name = "Certificates",
                Hint = "Each new certificate can choose its own folder when it is made. Those are backed up with these.",
                CurrentPath = () => certificates.Folder,
                IncludedByDefault = false,
                Warning = PrivateKeysWarning,
                Item = CertificatesItem,
            },
            new()
            {
                Id = "mqtt-certificates",
                Name = "MQTT client certificates",
                Hint = "Certificates copied for MQTT clients, one folder per client.",
                CurrentPath = () => certificates.ClientFolder,
                DefaultPath = certificates.DefaultClientFolder,
                Change = ChangeClientCertificates,
                IncludedByDefault = false,
                Warning = PrivateKeysWarning,
                Item = () => new BackupItem("mqtt-certificates", "MQTT client certificates", [new BackupRoot("folder", certificates.ClientFolder)]),
            },
            new()
            {
                Id = "passwords",
                Name = "Passwords",
                Hint = passwords.IsVault
                    ? "The vault file, already encrypted with its master password. Moving it needs the vault unlocked, so it is done on the Passwords page."
                    : $"The list of entries only. The passwords themselves are in {passwords.StorageText}, and are not in a backup.",
                CurrentPath = () => passwords.StorageLocation,
                OpenPage = openPasswords,
                OpenPageText = "Open Passwords",
                Item = () => new BackupItem("passwords", "Passwords", [new BackupRoot("file", passwords.StorageLocation, IsFile: true)]),
            },
        };

        foreach (var client in mqtt.Clients)
        {
            var id = MessagesId(client.Name);
            sources.Add(new FolderSource
            {
                Id = id,
                Name = client.Name,
                Hint = "Leave it empty for no folder.",
                IsMessageFolder = true,
                CurrentPath = () => client.FolderPath ?? "",
                AllowNone = true,
                Change = path => ChangeMessages(client, path),
                Item = () => string.IsNullOrEmpty(client.FolderPath)
                    ? null
                    : new BackupItem(id, $"MQTT messages: {client.Name}", [new BackupRoot("folder", client.FolderPath)]),
            });
        }

        return sources;
    }

    /// <summary>Says when the clients change, or the folder of one does, so that the page can follow.</summary>
    /// <param name="changed">Something changed.</param>
    /// <param name="renamed">A client took a new name, with the old and new ids of its folder.</param>
    public void Watch(Action changed, Action<string, string> renamed)
    {
        var names = new Dictionary<MqttClientViewModel, string>();

        void Follow(MqttClientViewModel client)
        {
            names[client] = client.Name;
            client.PropertyChanged += OnClientChanged;
        }

        void OnClientChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not MqttClientViewModel client)
                return;

            if (e.PropertyName == nameof(MqttClientViewModel.FolderPath))
                changed();
            else if (e.PropertyName == nameof(MqttClientViewModel.Name) && names.GetValueOrDefault(client) is { } old && old != client.Name)
            {
                names[client] = client.Name;
                renamed(MessagesId(old), MessagesId(client.Name));
            }
        }

        foreach (var client in mqtt.Clients)
            Follow(client);

        mqtt.Clients.CollectionChanged += (_, _) =>
        {
            foreach (var gone in names.Keys.Where(c => !mqtt.Clients.Contains(c)).ToList())
            {
                gone.PropertyChanged -= OnClientChanged;
                names.Remove(gone);
            }

            foreach (var added in mqtt.Clients.Where(c => !names.ContainsKey(c)).ToList())
                Follow(added);

            changed();
        };
    }

    private FolderSource SettingsSource()
    {
        const string name = "App settings";
        const string id = "settings";
        if (DataFolder.IsSetByVariable)
        {
            return new FolderSource
            {
                Id = id,
                Name = name,
                Hint = $"MQTT clients, the menu, themes and other settings. {DataFolder.Variable} sets this folder for this run, so it cannot be changed here.",
                CurrentPath = () => DataFolder.Path,
                Item = SettingsItem,
            };
        }

        return new FolderSource
        {
            Id = id,
            Name = name,
            Hint = "MQTT clients, the menu, themes and other settings, and whatever else is kept in this folder. Joktan restarts to use a new one.",
            CurrentPath = () => DataFolder.Path,
            DefaultPath = DataFolder.DefaultPath,
            ChangeText = "Use folder and restart",
            StartProblem = DataFolder.MissingFolder is { } missing
                ? $"{missing} could not be found, so Joktan is using its default folder for now. Connect it and restart Joktan, or choose another folder here."
                : null,
            Change = path => DataFolderMove.ApplyAsync(path, DataFolder.Path, DataFolder.DefaultPath, flush, restart),
            Item = SettingsItem,
        };
    }

    private static string MessagesId(string clientName) => $"mqtt-messages:{clientName}";

    private BackupItem SettingsItem()
    {
        var data = DataFolder.Path;
        var exclude = NotAppSettings.Select(name => Path.Combine(data, name))
            .Concat(PendingRestore.OwnFiles.Select(name => Path.Combine(data, name)))
            .Append(notes.Folder).Append(api.Folder).Append(certificates.ClientFolder).Append(certificates.Folder)
            .Append(passwords.StorageLocation)
            .Concat(mqtt.Clients.Select(c => c.FolderPath).OfType<string>().Where(f => f.Length > 0))
            .ToList();
        return new BackupItem("settings", "App settings", [new BackupRoot("folder", data, false, exclude)]);
    }

    private BackupItem CertificatesItem()
    {
        var roots = new List<BackupRoot>
        {
            new("folder", certificates.Folder),
            new("list", Path.Combine(DataFolder.Path, "certificates.json"), IsFile: true),
        };
        var inside = certificates.Folder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var entry in certificates.Entries.Where(e => e.Location is { Length: > 0 } location && !location.StartsWith(inside, StringComparison.Ordinal)))
            roots.Add(new BackupRoot($"certificate-{entry.Id}", entry.Location!));
        return new BackupItem("certificates", "Certificates", roots);
    }

    private async Task<FolderChange> ChangeNotes(string path)
    {
        notes.FolderText = path;
        await notes.ApplyFolderCommand.ExecuteAsync(null);
        return new FolderChange(notes.FolderMessage, notes.Message);
    }

    private async Task<FolderChange> ChangeDashboards(string path)
    {
        dashboards.FolderText = path;
        await dashboards.ApplyFolderCommand.ExecuteAsync(null);
        if (dashboards.CanStartEmpty)
        {
            dashboards.IsChoosingFolder = true;
            return new FolderChange($"{dashboards.FolderMessage} That button is under Folder on the MQTT Dashboards page.");
        }

        return new FolderChange(dashboards.FolderMessage, dashboards.Message);
    }

    private Task<FolderChange> ChangeApi(string path)
    {
        api.WorkspaceText = path;
        api.ApplyWorkspaceCommand.Execute(null);
        return Task.FromResult(new FolderChange(api.WorkspaceMessage, api.Message));
    }

    private Task<FolderChange> ChangeClientCertificates(string path)
    {
        var previous = certificates.ClientFolder;
        try
        {
            var copied = BackupPlanner.CopyIfEmpty(previous, path);
            var defaults = certificates.Defaults.Clone();
            defaults.ClientFolder = path == certificates.DefaultClientFolder ? "" : path;
            certificates.SaveDefaults(defaults);
            return Task.FromResult(new FolderChange(null, copied
                ? $"The certificates were copied to {path}. The originals are still in {previous}."
                : $"Certificates for MQTT clients now go to {path}."));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(new FolderChange($"Could not use {path}: {e.Message}"));
        }
    }

    private static Task<FolderChange> ChangeMessages(MqttClientViewModel client, string path)
    {
        var previous = client.FolderPath;
        try
        {
            var copied = path.Length > 0 && BackupPlanner.CopyIfEmpty(previous, path);
            client.UseFolder(path.Length > 0 ? path : null);
            return Task.FromResult(new FolderChange(null, path.Length == 0 ? "This client no longer has a folder of messages."
                : copied ? $"The messages were copied to {path}. The originals are still in {previous}."
                : $"Opened the messages in {path}."));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(new FolderChange($"Could not use {path}: {e.Message}"));
        }
    }
}
