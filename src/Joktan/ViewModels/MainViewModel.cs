using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Api.ViewModels;
using Joktan.Certificates;
using Joktan.Certificates.ViewModels;
using Joktan.Containers.ViewModels;
using Joktan.Core;
using Joktan.Core.ViewModels;
using Joktan.Dashboard.ViewModels;
using Joktan.Help;
using Joktan.Help.ViewModels;
using Joktan.Mqtt;
using Joktan.Mqtt.ViewModels;
using Joktan.Notes;
using Joktan.Notes.ViewModels;
using Joktan.Passwords;
using Joktan.Passwords.ViewModels;
using Joktan.Settings;
using Joktan.Settings.Backup;
using Joktan.Settings.Themes;
using Joktan.Settings.ViewModels;
using Joktan.Dashboard.Telemetry;
using Joktan.TopicDashboards;
using Joktan.TopicDashboards.ViewModels;
using Joktan.TopicExplorer.ViewModels;

namespace Joktan.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly MqttViewModel _mqtt;
    private readonly ContainersViewModel _containers = new();
    private readonly NotesViewModel _notes;
    private readonly PasswordsViewModel _passwords;
    private readonly ApiClientViewModel _apiClient = new();
    private readonly TopicDashboardsViewModel _topicDashboards;
    private readonly BackupViewModel _backup;
    private NavItemViewModel? _selectedItem;

    // Used by the XAML previewer.
    public MainViewModel() : this(new MachineMonitor())
    {
    }

    /// <param name="certificates">The certificate store, if already read; otherwise it is read here.</param>
    /// <param name="mqttSettings">The MQTT settings, if already read; otherwise they are read here.</param>
    /// <param name="notes">The notes, if already read; otherwise they are read here.</param>
    /// <param name="passwordSettings">The password manager's settings, if already read; otherwise they are read here.</param>
    /// <param name="dashboards">The MQTT dashboards, if already read; otherwise they are read here.</param>
    /// <param name="menuLayout">The order of the menu and what it shows, if already read; otherwise they are read here.</param>
    /// <param name="themes">The themes, if already read; otherwise they are read here.</param>
    public MainViewModel(MachineMonitor monitor, CertificateStore? certificates = null, MqttSettings? mqttSettings = null,
        NoteStore? notes = null, PasswordSettings? passwordSettings = null, DashboardStore? dashboards = null, MenuLayout? menuLayout = null, ThemeEditor? themes = null)
    {
        _passwords = new PasswordsViewModel(passwordSettings ?? PasswordSettings.Load(), CredentialStore.Create(CredentialStore.PasswordsService));
        certificates ??= CertificateStore.Load();
        _notes = new NotesViewModel(notes ?? NoteStore.Load());
        var mqtt = _mqtt = new MqttViewModel(certificates, mqttSettings);
        _topicDashboards = new TopicDashboardsViewModel(mqtt, dashboards);

        var passwordsItem = new NavItemViewModel("Passwords", Icons.Key) { Page = _passwords, HelpTopic = "passwords" };
        var folders = new FolderSources(_notes, _topicDashboards, _apiClient, certificates, _passwords, mqtt, () => NavigateCommand.Execute(passwordsItem));
        _backup = new BackupViewModel(folders.All, BackupSettings.Load(), FlushAsync, () => RestartRequested?.Invoke(),
            Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "");
        folders.Watch(_backup.Refresh, _backup.Renamed);

        themes ??= ThemeEditor.Load();
        var themeBar = new ThemeBarViewModel(themes);
        var menuSettings = new NavItemViewModel("Menu") { HelpTopic = "settings-and-data", CanHide = false };

        MenuItems =
        [
            new NavItemViewModel("Dashboard", Icons.Dashboard,
                new NavItemViewModel("Overview") { Page = new DashboardViewModel(monitor), HelpTopic = "dashboard" },
                new NavItemViewModel("MQTT Dashboards") { FormerTitle = "Topic Dashboards", Page = _topicDashboards, HelpTopic = "topic-dashboards" }),
            new NavItemViewModel("Notes", Icons.Notes) { Page = _notes, HelpTopic = "notes" },
            passwordsItem,
            new NavItemViewModel("Dev Tools", Icons.DevTools,
                new NavItemViewModel("Brokers", null,
                    new NavItemViewModel("MQTT") { Page = mqtt, HelpTopic = "mqtt" },
                    new NavItemViewModel("Topic Explorer") { Page = new TopicExplorerViewModel(mqtt, _topicDashboards), HelpTopic = "topic-explorer" }) { FormerTitle = "Connectivity" },
                new NavItemViewModel("Certificates") { Page = new CertificatesViewModel(certificates, mqtt.ClientsUsing), HelpTopic = "certificates" },
                new NavItemViewModel("API Client") { Page = _apiClient, HelpTopic = "api-client" },
                new NavItemViewModel("Containers") { Page = _containers, HelpTopic = "containers" }),
            new NavItemViewModel("Settings", Icons.Settings,
                menuSettings,
                new NavItemViewModel("Themes") { Page = new ThemesViewModel(themes, themeBar), HelpTopic = "themes" },
                new NavItemViewModel("Colours") { Page = new ColorsViewModel(themes, themeBar), HelpTopic = "themes" },
                new NavItemViewModel("Fonts") { Page = new FontsViewModel(themes, themeBar), HelpTopic = "themes" },
                new NavItemViewModel("Text Sizes") { Page = new TextSizesViewModel(themes, themeBar), HelpTopic = "themes" },
                new NavItemViewModel("Folders and Backup") { Page = _backup, HelpTopic = "folders-and-backup" }) { CanHide = false },
        ];

        menuSettings.Page = new MenuSettingsViewModel(MenuItems, menuLayout ?? MenuLayout.Load());

        var first = FirstInMenu(MenuItems);
        for (var group = first.Parent; group is not null; group = group.Parent)
            group.IsExpanded = true;

        Navigate(first);
    }

    public ObservableCollection<NavItemViewModel> MenuItems { get; }

    public HelpViewModel Help { get; } = new();

    /// <summary>The help topic for the page being shown; placeholder pages have none of their own.</summary>
    public string CurrentHelpTopic => _selectedItem?.HelpTopic ?? HelpTopic.Home;

    /// <summary>Says which module the help icon will open help on.</summary>
    public string HelpTip => _selectedItem is { HelpTopic: not null, Page: { } page }
        ? $"Help on {page.Title} (F1)"
        : "Help: getting started (F1)";

    [ObservableProperty]
    public partial bool IsSidebarOpen { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HelpTip))]
    public partial PageViewModel? CurrentPage { get; set; }

    /// <summary>Closes the app and starts it again, to carry out a restore. Set by the app.</summary>
    public Action? RestartRequested { get; set; }

    // Writes what features hold in memory, so that a backup has what is on screen. Unlike closing, nothing stops.
    private Task FlushAsync()
    {
        _mqtt.SaveState();
        _topicDashboards.SaveState();
        _notes.SaveState();
        _passwords.SaveState();
        _apiClient.SaveState();
        return Task.CompletedTask;
    }

    /// <summary>Writes anything not yet saved and stops the commands still running. Called when the app exits.</summary>
    public void SaveState()
    {
        _mqtt.SaveState();
        _topicDashboards.SaveState();
        _notes.SaveState();
        _passwords.SaveState();
        _apiClient.SaveState();
        _containers.Shutdown();
    }

    [RelayCommand]
    private void ToggleSidebar() => IsSidebarOpen = !IsSidebarOpen;

    [RelayCommand]
    private void Navigate(NavItemViewModel item)
    {
        if (item.HasChildren)
        {
            item.IsExpanded = !item.IsExpanded;
            return;
        }

        if (_selectedItem is not null)
            SetSelected(_selectedItem, false);

        _selectedItem = item;
        SetSelected(item, true);

        var path = string.Join(" / ", PathTo(item).Select(i => i.Title));

        using var activity = TelemetryHost.ActivitySource.StartActivity("navigate");
        activity?.SetTag("joktan.page", path);

        CurrentPage = item.Page ?? new PageViewModel(item.Title, path);
    }

    // Settings and its Menu page cannot be hidden, so there is always an entry to open.
    private static NavItemViewModel FirstInMenu(IEnumerable<NavItemViewModel> entries)
    {
        var entry = entries.First(e => e.IsInMenu);
        return entry.HasChildren ? FirstInMenu(entry.Children) : entry;
    }

    // Selecting an entry also marks the groups above it, so they stay highlighted.
    private void SetSelected(NavItemViewModel item, bool selected)
    {
        foreach (var entry in PathTo(item))
            entry.IsSelected = selected;
    }

    /// <summary>The entry and the groups above it, outermost first.</summary>
    private List<NavItemViewModel> PathTo(NavItemViewModel item)
    {
        var path = new List<NavItemViewModel>();
        Search(MenuItems);
        return path;

        bool Search(IReadOnlyList<NavItemViewModel> entries)
        {
            foreach (var entry in entries)
            {
                path.Add(entry);
                if (entry == item || Search(entry.Children))
                    return true;
                path.RemoveAt(path.Count - 1);
            }

            return false;
        }
    }
}
