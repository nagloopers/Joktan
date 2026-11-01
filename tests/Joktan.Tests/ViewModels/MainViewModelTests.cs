using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Joktan.Api.ViewModels;
using Joktan.Certificates.ViewModels;
using Joktan.Containers.ViewModels;
using Joktan.Core.ViewModels;
using Joktan.Dashboard.ViewModels;
using Joktan.Help;
using Joktan.Mqtt.ViewModels;
using Joktan.Notes.ViewModels;
using Joktan.Passwords.ViewModels;
using Joktan.Pomodoro.ViewModels;
using Joktan.Settings.ViewModels;
using Joktan.Tests.TestSupport;
using Joktan.TopicDashboards.ViewModels;
using Joktan.TopicExplorer.ViewModels;
using Xunit;

namespace Joktan.Tests.ViewModels;

// The menu is written in the constructor, which needs a running Avalonia platform and the system
// keychain, so what the menu relies on is checked against the source and the assemblies instead.
public class MainViewModelTests
{
    private static readonly Assembly[] PageAssemblies =
    [
        typeof(PageViewModel).Assembly,
        typeof(ApiClientViewModel).Assembly,
        typeof(CertificatesViewModel).Assembly,
        typeof(ContainersViewModel).Assembly,
        typeof(DashboardViewModel).Assembly,
        typeof(MqttViewModel).Assembly,
        typeof(NotesViewModel).Assembly,
        typeof(PasswordsViewModel).Assembly,
        typeof(PomodoroViewModel).Assembly,
        typeof(MenuSettingsViewModel).Assembly,
        typeof(TopicDashboardsViewModel).Assembly,
        typeof(TopicExplorerViewModel).Assembly,
    ];

    public static TheoryData<string> Pages()
    {
        var pages = new TheoryData<string>();
        foreach (var type in PageAssemblies.SelectMany(a => a.GetTypes()).Where(t => typeof(PageViewModel).IsAssignableFrom(t) && !t.IsAbstract))
            pages.Add(type.AssemblyQualifiedName!);
        return pages;
    }

    [Theory]
    [MemberData(nameof(Pages))]
    public void Every_page_has_a_view_the_locator_will_find(string page)
    {
        var type = Type.GetType(page)!;
        var view = type.FullName!.Replace("ViewModel", "View", StringComparison.Ordinal);

        Assert.NotNull(type.Assembly.GetType(view));
    }

    [Fact]
    public void Every_help_topic_the_menu_names_exists()
    {
        var source = File.ReadAllText(RepositoryFiles.At("src", "Joktan", "ViewModels", "MainViewModel.cs"));

        var named = Regex.Matches(source, "HelpTopic = \"([^\"]+)\"").Select(m => m.Groups[1].Value).Distinct().ToList();

        Assert.NotEmpty(named);
        Assert.All(named, topic => Assert.Contains(topic, HelpTopic.All.Select(t => t.Id)));
    }
}
