using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Joktan.Agent.Clients;
using Xunit;

namespace Joktan.Agent.Tests.Clients;

public class AgentProgramTests
{
    [Fact]
    public async Task Failed_process_reports_stderr()
    {
        if (OperatingSystem.IsWindows())
            return;

        var launch = new AgentLaunch();
        launch.Arguments.Add("-c");
        launch.Arguments.Add("echo output; echo problem >&2; exit 1");
        string? output = null;

        var error = await AgentProgram.RunAsync("/bin/sh", launch, Path.GetTempPath(), text => output = text, CancellationToken.None);

        Assert.Equal("output", output);
        Assert.Equal("problem", error);
    }

    [Fact]
    public async Task Cancellation_stops_the_process_without_reporting_an_error()
    {
        if (OperatingSystem.IsWindows())
            return;

        var launch = new AgentLaunch();
        launch.Arguments.Add("-c");
        launch.Arguments.Add("echo ready; exec sleep 30");
        using var cancel = new CancellationTokenSource();

        var error = await AgentProgram.RunAsync("/bin/sh", launch, Path.GetTempPath(), _ => cancel.Cancel(), cancel.Token);

        Assert.Null(error);
    }

    [Fact]
    public async Task Callback_failure_stops_the_process_before_returning()
    {
        if (OperatingSystem.IsWindows())
            return;

        var launch = new AgentLaunch();
        launch.Arguments.Add("-c");
        launch.Arguments.Add("echo $$; exec sleep 30");
        Process? child = null;
        var failure = new InvalidOperationException("callback failed");
        try
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => AgentProgram.RunAsync(
                "/bin/sh", launch, Path.GetTempPath(), text =>
                {
                    child = Process.GetProcessById(int.Parse(text));
                    throw failure;
                }, CancellationToken.None));

            Assert.Same(failure, exception);
            Assert.NotNull(child);
            Assert.True(child.HasExited);
        }
        finally
        {
            if (child is { HasExited: false })
            {
                child.Kill(entireProcessTree: true);
                await child.WaitForExitAsync(TestContext.Current.CancellationToken);
            }
            child?.Dispose();
        }
    }
}