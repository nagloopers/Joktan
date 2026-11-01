using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Joktan.Agent.ViewModels;
using Joktan.Agent.Views;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Agent.Tests.Views;

[Collection(ProcessStateCollection.Name)]
public class AgentPanelTests : DataFolderTest
{
    // A client that says nothing and takes its time, until it is stopped.
    private static AgentSettings SlowClient()
    {
        var windows = OperatingSystem.IsWindows();
        var settings = new AgentSettings
        {
            Client = AgentClientKind.Custom,
            CustomArguments = windows ? "-n 60 127.0.0.1" : "60",
        };
        settings.ProfileOf(AgentClientKind.Custom).Path = windows ? Path.Combine(Environment.SystemDirectory, "PING.EXE") : "/bin/sleep";
        return settings;
    }

    [Fact]
    public Task Shows_that_it_is_working_for_as_long_as_a_turn_runs() => Headless.Run(async () =>
    {
        using var vm = new AgentViewModel(SlowClient(), () => []);
        var window = Headless.Show(new AgentPanel(), vm);
        var status = Headless.Find<StackPanel>(window, s => s.Name == "WorkingStatus");
        Assert.False(status.IsVisible);

        vm.Draft = "Hello";
        vm.SendCommand.Execute(null);
        Headless.Settle();
        Assert.True(vm.IsBusy);
        Assert.True(status.IsEffectivelyVisible);

        vm.StopCommand.Execute(null);
        await vm.SendCommand.ExecutionTask!;
        Headless.Settle();
        Assert.False(status.IsVisible);
        window.Close();
    });
}
