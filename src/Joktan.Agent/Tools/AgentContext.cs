using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Joktan.Api.ViewModels;
using Joktan.Certificates.ViewModels;
using Joktan.Containers.ViewModels;
using Joktan.Core;
using Joktan.Core.ViewModels;
using Joktan.Dashboard.ViewModels;
using Joktan.Mqtt.ViewModels;
using Joktan.Notes.ViewModels;
using Joktan.Passwords.ViewModels;
using Joktan.Pomodoro.ViewModels;
using Joktan.Settings.Themes;
using Joktan.Settings.ViewModels;
using Joktan.TopicDashboards.ViewModels;
using Joktan.TopicExplorer.ViewModels;

namespace Joktan.Agent.Tools;

/// <summary>The app's pages, for the tools to act on. Settings / Agent is left out on purpose: an agent must not be able to widen its own access.</summary>
public sealed class AgentContext
{
    public required ObservableCollection<NavItemViewModel> Menu { get; init; }
    public required Action<NavItemViewModel> Navigate { get; init; }
    public required Func<string?> CurrentPage { get; init; }
    public required DashboardViewModel Overview { get; init; }
    public required NotesViewModel Notes { get; init; }
    public required PasswordsViewModel Passwords { get; init; }
    public required PomodoroViewModel Pomodoro { get; init; }
    public required MqttViewModel Mqtt { get; init; }
    public required InboxViewModel Inbox { get; init; }
    public required MessagingViewModel Messaging { get; init; }
    public required TopicExplorerViewModel Explorer { get; init; }
    public required TopicDashboardsViewModel Dashboards { get; init; }
    public required CertificatesViewModel Certificates { get; init; }
    public required CertificateDefaultsViewModel CertificateDefaults { get; init; }
    public required ApiClientViewModel Api { get; init; }
    public required ContainersViewModel Containers { get; init; }
    public required SearchSettingsViewModel SearchSettings { get; init; }
    public required MenuSettingsViewModel MenuSettings { get; init; }
    public required ThemeEditor Themes { get; init; }
    public required BackupViewModel Backup { get; init; }
    public required Func<IReadOnlyList<string>, IEnumerable<SearchHit>> Search { get; init; }
}
