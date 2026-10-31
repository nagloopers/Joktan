using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Joktan.Containers;
using Joktan.Core.ViewModels;
using Joktan.Dashboard.Telemetry;
using Joktan.Help;
using Joktan.Mqtt;
using Joktan.Settings.ViewModels;
using Joktan.Tests.TestSupport;
using Joktan.ViewModels;
using Xunit;

namespace Joktan.Tests.ViewModels;

// With an empty data folder nothing here has a username or a keychain entry to look up, and no
// compose file is chosen, so that neither the real keychain nor a real engine is reached.
[Collection(ProcessStateCollection.Name)]
public class MainViewModelStartTests : DataFolderTest, IDisposable
{
    private readonly MachineMonitor _monitor = new();

    private MainViewModel New()
    {
        new ComposeSettings { ComposeFiles = [] }.Save();
        return new MainViewModel(_monitor);
    }

    private static IEnumerable<NavItemViewModel> Leaves(IEnumerable<NavItemViewModel> items) =>
        items.SelectMany(i => i.HasChildren ? Leaves(i.Children) : [i]);

    private static NavItemViewModel Item(MainViewModel main, string title) => Leaves(main.MenuItems).First(i => i.Title == title);

    [Fact]
    public Task Opens_on_the_first_page_of_the_menu() => Headless.Run(() =>
    {
        var main = New();

        Assert.Equal("Overview", main.CurrentPage!.Title);
        Assert.True(main.MenuItems[0].IsExpanded);
        Assert.True(main.MenuItems[0].IsSelected);
        Assert.Equal("dashboard", main.CurrentHelpTopic);
        Assert.Equal("Help on Overview (F1)", main.HelpTip);
        Assert.NotNull(main.Help);
    });

    [Fact]
    public Task The_previewer_can_make_one_too() => Headless.Run(() =>
    {
        new ComposeSettings { ComposeFiles = [] }.Save();

        Assert.NotNull(new MainViewModel().CurrentPage);
    });

    [Fact]
    public Task Going_somewhere_marks_the_way_there_and_a_group_only_opens() => Headless.Run(() =>
    {
        var main = New();
        var group = main.MenuItems.First(i => i.Title == "Dev Tools");
        var overview = main.CurrentPage;
        Assert.False(group.IsExpanded);

        main.NavigateCommand.Execute(group);
        Assert.True(group.IsExpanded);
        Assert.Same(overview, main.CurrentPage);
        main.NavigateCommand.Execute(group);
        Assert.False(group.IsExpanded);

        var mqtt = Item(main, "MQTT");
        main.NavigateCommand.Execute(mqtt);
        Assert.True(mqtt.IsSelected);
        Assert.True(group.IsSelected);
        Assert.False(main.MenuItems[0].IsSelected);
        Assert.Equal("mqtt", main.CurrentHelpTopic);
    });

    [Fact]
    public Task An_entry_with_no_page_yet_gets_a_placeholder_and_help_on_getting_started() => Headless.Run(() =>
    {
        var main = New();

        var later = new NavItemViewModel("Later");
        main.MenuItems.Add(later);

        main.NavigateCommand.Execute(later);

        Assert.Equal("Later", main.CurrentPage!.Title);
        Assert.Equal("Later", main.CurrentPage.Subtitle);
        Assert.Equal(HelpTopic.Home, main.CurrentHelpTopic);
        Assert.Equal("Help: getting started (F1)", main.HelpTip);
    });

    [Fact]
    public Task Every_entry_on_the_menu_has_a_page_of_its_own() => Headless.Run(() =>
    {
        var main = New();

        Assert.All(Leaves(main.MenuItems), item => Assert.NotNull(item.Page));
        Assert.DoesNotContain(Leaves(main.MenuItems), item => item.Title is "Analytics" or "Reports" or "Charts" or "Tables");
    });

    [Fact]
    public Task Brokers_holds_mqtt_and_the_explorer_and_certificates_sit_beside_it() => Headless.Run(() =>
    {
        var devTools = New().MenuItems.First(i => i.Title == "Dev Tools");

        Assert.Equal(["Brokers", "Certificates", "API Client", "Containers"], devTools.Children.Select(i => i.Title));
        Assert.Equal(["MQTT", "Topic Explorer"], devTools.Children[0].Children.Select(i => i.Title));
        Assert.Equal("Dev Tools / Certificates", devTools.Children[1].Page!.Subtitle);
    });

    [Fact]
    public Task A_layout_saved_under_the_old_group_name_still_applies_to_brokers() => Headless.Run(() =>
    {
        var layout = new Joktan.Settings.MenuLayout
        {
            Entries =
            [
                new Joktan.Settings.MenuLayoutEntry
                {
                    Title = "Dev Tools",
                    Children =
                    [
                        new Joktan.Settings.MenuLayoutEntry
                        {
                            Title = "Connectivity",
                            Children = [new Joktan.Settings.MenuLayoutEntry { Title = "Topic Explorer", Shown = false }],
                        },
                    ],
                },
            ],
        };
        new ComposeSettings { ComposeFiles = [] }.Save();

        var main = new MainViewModel(_monitor, menuLayout: layout);

        Assert.False(Item(main, "Topic Explorer").IsShown);
    });

    [Fact]
    public Task A_layout_saved_under_the_old_name_still_applies_to_mqtt_dashboards() => Headless.Run(() =>
    {
        var layout = new Joktan.Settings.MenuLayout
        {
            Entries =
            [
                new Joktan.Settings.MenuLayoutEntry
                {
                    Title = "Dashboard",
                    Children = [new Joktan.Settings.MenuLayoutEntry { Title = "Topic Dashboards", Shown = false }],
                },
            ],
        };
        new ComposeSettings { ComposeFiles = [] }.Save();

        var main = new MainViewModel(_monitor, menuLayout: layout);

        Assert.False(Item(main, "MQTT Dashboards").IsShown);
        Assert.Equal("MQTT Dashboards", Item(main, "MQTT Dashboards").Page!.Title);
    });

    [Fact]
    public Task A_hidden_first_entry_is_passed_over() => Headless.Run(() =>
    {
        var layout = new Joktan.Settings.MenuLayout { Entries = [new Joktan.Settings.MenuLayoutEntry { Title = "Dashboard", Shown = false }] };
        new ComposeSettings { ComposeFiles = [] }.Save();

        var main = new MainViewModel(_monitor, menuLayout: layout);

        Assert.NotEqual("Overview", main.CurrentPage!.Title);
    });

    [Fact]
    public Task The_sidebar_can_be_folded_away() => Headless.Run(() =>
    {
        var main = New();
        Assert.True(main.IsSidebarOpen);

        main.ToggleSidebarCommand.Execute(null);

        Assert.False(main.IsSidebarOpen);
    });

    [Fact]
    public Task Saving_writes_what_each_feature_holds() => Headless.Run(() =>
    {
        var main = New();
        var mqtt = (Joktan.Mqtt.ViewModels.MqttViewModel)Item(main, "MQTT").Page!;
        mqtt.Clients[0].ManualTopic = "typed";

        main.SaveState();

        Assert.Equal("typed", MqttSettings.Load().Clients[0].ManualTopic);
    });

    private static BackupViewModel Backup(MainViewModel main) => (BackupViewModel)Item(main, "Folders and Backup").Page!;

    [Fact]
    public Task The_backup_page_lists_every_kind_of_folder_and_backs_them_up() => Headless.Run(async () =>
    {
        var main = New();
        var backup = Backup(main);

        Assert.Equal(["settings", "notes", "dashboards", "api", "certificates", "mqtt-certificates", "passwords"], backup.Rows.Select(r => r.Source.Id));
        Assert.Single(backup.MessageRows);
        Assert.Contains("vault file", backup.Rows.First(r => r.Source.Id == "passwords").Hint);

        Data.Write("mqtt-extra.json", "{}");
        backup.BackupFolderText = Data.At("backups");
        await backup.BackUpNowCommand.ExecuteAsync(null);

        Assert.NotEmpty(Directory.GetFiles(Data.At("backups"), "*.zip"));
    });

    [Fact]
    public Task Opening_the_passwords_page_from_the_backup_page_goes_there() => Headless.Run(() =>
    {
        var main = New();

        Backup(main).Rows.First(r => r.Source.Id == "passwords").OpenPageCommand.Execute(null);

        Assert.Equal("Passwords", main.CurrentPage!.Title);
    });

    private static async Task<FolderRowViewModel> Change(BackupViewModel backup, string id, string path)
    {
        var row = backup.Rows.Concat(backup.MessageRows).First(r => r.Source.Id == id);
        row.PathText = path;
        await row.ApplyCommand.ExecuteAsync(null);
        return row;
    }

    [Theory]
    [InlineData("notes")]
    [InlineData("dashboards")]
    [InlineData("api")]
    [InlineData("mqtt-certificates")]
    public Task A_feature_can_be_moved_from_the_backup_page(string id) => Headless.Run(async () =>
    {
        var main = New();
        var target = Data.At("moved", id);

        var row = await Change(Backup(main), id, target);

        Assert.False(row.HasProblem);
        Assert.Equal(target, row.Source.CurrentPath());
        Assert.True(row.HasMessage);
    });

    [Theory]
    [InlineData("notes")]
    [InlineData("dashboards")]
    [InlineData("api")]
    [InlineData("mqtt-certificates")]
    [InlineData("mqtt-messages:client 1")]
    public Task A_folder_that_cannot_be_used_says_so(string id) => Headless.Run(async () =>
    {
        var main = New();
        var file = Data.Write("a-file", "");
        var backup = Backup(main);
        id = id == "mqtt-messages:client 1" ? backup.MessageRows[0].Source.Id : id;

        var row = await Change(backup, id, Path.Combine(file, "below"));

        Assert.True(row.HasProblem);
    });

    [Fact]
    public Task A_clients_folder_can_be_chosen_copied_to_and_taken_away() => Headless.Run(async () =>
    {
        var main = New();
        var backup = Backup(main);
        var client = ((Joktan.Mqtt.ViewModels.MqttViewModel)Item(main, "MQTT").Page!).Clients[0];
        var id = backup.MessageRows[0].Source.Id;
        Assert.Null(backup.MessageRows[0].Source.Item!());

        var first = Data.Write("first/topic/m.json", "{}");
        var row = await Change(backup, id, Path.GetDirectoryName(Path.GetDirectoryName(first))!);
        Assert.Contains("Opened the messages", row.Message);
        Assert.NotNull(backup.MessageRows[0].Source.Item!());

        row = await Change(backup, id, Data.At("second"));
        Assert.Contains("copied", row.Message);
        Assert.Equal(Data.At("second"), client.FolderPath);

        row = await Change(backup, id, "");
        Assert.Contains("no longer has a folder", row.Message);
        Assert.Null(client.FolderPath);
    });

    [Fact]
    public Task Certificates_copied_to_a_new_folder_and_those_kept_elsewhere_are_backed_up() => Headless.Run(async () =>
    {
        var store = Joktan.Certificates.CertificateStore.Load();
        store.Create(new Joktan.Certificates.CertificateSpec
        {
            Kind = Joktan.Certificates.CertificateKind.RootAuthority,
            Name = "elsewhere",
            CommonName = "elsewhere",
            NotAfter = DateTimeOffset.UtcNow.AddYears(1),
            Folder = Data.At("own-folder"),
        });
        new ComposeSettings { ComposeFiles = [] }.Save();
        var main = new MainViewModel(_monitor, store);
        var backup = Backup(main);

        var item = backup.Rows.First(r => r.Source.Id == "certificates").Source.Item!();
        Assert.Contains(item!.Roots, r => r.Key.StartsWith("certificate-", StringComparison.Ordinal));

        Data.Write("mqtt-certificates/client/ca.pem", "x");
        var row = await Change(backup, "mqtt-certificates", Data.At("moved-certificates"));
        Assert.Contains("copied", row.Message);

        row = await Change(backup, "mqtt-certificates", store.DefaultClientFolder);
        Assert.False(row.HasProblem);
    });

    [Fact]
    public Task The_rows_follow_the_clients_and_their_folders() => Headless.Run(async () =>
    {
        var main = New();
        var backup = Backup(main);
        var mqtt = (Joktan.Mqtt.ViewModels.MqttViewModel)Item(main, "MQTT").Page!;
        var client = mqtt.Clients[0];
        Assert.Equal(client.Name, backup.MessageRows[0].Name);

        client.UseFolder(Data.At("elsewhere"));
        Assert.Equal(Data.At("elsewhere"), backup.MessageRows[0].PathText);

        client.Apply(new ClientProfile { Name = "renamed", Host = "h", ClientId = client.ClientId }, "", "");
        backup.Refresh();
        Assert.Equal("renamed", backup.MessageRows[0].Name);

        // The same name again is no rename.
        client.Apply(new ClientProfile { Name = "renamed", Host = "other", ClientId = client.ClientId }, "", "");
        client.ToggleBrokerCommand.Execute(null);

        mqtt.EditClientSettings = _ => Task.FromResult(true);
        await mqtt.AddClientCommand.ExecuteAsync(null);
        Assert.Equal(2, backup.MessageRows.Count);

        mqtt.Clients[0].RemoveClientCommand.Execute(null);
        Assert.Single(backup.MessageRows);
    });

    [Fact]
    public Task Dashboards_that_have_gone_point_to_where_they_can_be_started_again() => Headless.Run(async () =>
    {
        var folder = Data.At("gone");
        Joktan.TopicDashboards.DashboardStore.SaveChosenFolder(folder);
        var main = New();
        var dashboards = (Joktan.TopicDashboards.ViewModels.TopicDashboardsViewModel)main.MenuItems[0].Children[1].Page!;

        // Written another way, as the very path shown counts as no change at all.
        var row = await Change(Backup(main), "dashboards", folder + Path.DirectorySeparatorChar);

        Assert.Contains("under Folder on the MQTT Dashboards page", row.Problem);
        Assert.True(dashboards.IsChoosingFolder);
    });

    void IDisposable.Dispose()
    {
        _monitor.Dispose();
        base.Dispose();
    }
}
