using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Joktan.Core;
using Joktan.Core.ViewModels;
using Joktan.Dashboard.Resources;
using Joktan.Dashboard.Telemetry;

namespace Joktan.Dashboard.ViewModels;

public sealed record InfoRow(string Label, string Value);

public partial class DashboardViewModel : PageViewModel
{
    private const string Missing = "–";

    private readonly MetricTileViewModel _cpu = new(Strings.Dashboard_CPUUsage, Icons.Cpu, hasMeter: true, hasTrend: true);
    private readonly MetricTileViewModel _memory = new(Strings.Dashboard_MemoryUsed, Icons.Memory, hasMeter: true, hasTrend: true);
    private readonly MetricTileViewModel _disk = new(Strings.Dashboard_DiskUsed, Icons.Disk, hasMeter: true);
    private readonly MetricTileViewModel _uptime = new(Strings.Dashboard_Uptime, Icons.Clock);

    public DashboardViewModel(MachineMonitor monitor)
        : base(Strings.Dashboard_Overview, Strings.Dashboard_LiveMetricsForThisMachine)
    {
        Tiles = [_cpu, _memory, _disk, _uptime];
        SystemInfo =
        [
            new InfoRow(Strings.Dashboard_MachineName, Environment.MachineName),
            new InfoRow(Strings.Dashboard_OperatingSystem, RuntimeInformation.OSDescription),
            new InfoRow(Strings.Dashboard_Architecture, RuntimeInformation.OSArchitecture.ToString()),
            new InfoRow(Strings.Dashboard_LogicalProcessors, Environment.ProcessorCount.ToString()),
            new InfoRow(Strings.Dashboard_NETRuntime, Environment.Version.ToString()),
        ];

        Apply(monitor.Latest);
        monitor.Updated += snapshot => Dispatcher.UIThread.Post(() => Apply(snapshot));
    }

    public IReadOnlyList<MetricTileViewModel> Tiles { get; }

    public IReadOnlyList<InfoRow> SystemInfo { get; }

    public string TelemetrySummary
        => string.Format(Strings.Dashboard_ExportedOverOTLPToFromThe, TelemetryHost.Endpoint, MachineMetrics.MeterName);

    /// <summary>The instruments exported through OpenTelemetry, with their latest values.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<InfoRow> ExportedMetrics { get; set; } = [];

    private void Apply(MachineSnapshot s)
    {
        _cpu.Update(
            FormatPercent(s.CpuUtilization),
            string.Format(Strings.Dashboard_LogicalProcessors2, Environment.ProcessorCount),
            s.CpuUtilization);
        _memory.Update(
            FormatPercent(s.MemoryUtilization),
            FormatUsage(s.MemoryUsedBytes, s.MemoryTotalBytes),
            s.MemoryUtilization);
        _disk.Update(
            FormatPercent(s.DiskUtilization),
            FormatUsage(s.DiskUsedBytes, s.DiskTotalBytes),
            s.DiskUtilization);
        _uptime.Update(FormatDuration(s.Uptime), string.Format(Strings.Dashboard_Started, DateTime.Now - s.Uptime));

        ExportedMetrics =
        [
            new InfoRow("system.cpu.utilization", FormatPercent(s.CpuUtilization)),
            new InfoRow("system.cpu.logical.count", Environment.ProcessorCount.ToString()),
            new InfoRow("system.memory.usage", FormatBytes(s.MemoryUsedBytes)),
            new InfoRow("system.memory.limit", FormatBytes(s.MemoryTotalBytes)),
            new InfoRow("system.memory.utilization", FormatPercent(s.MemoryUtilization)),
            new InfoRow("system.filesystem.usage", FormatBytes(s.DiskUsedBytes)),
            new InfoRow("system.filesystem.limit", FormatBytes(s.DiskTotalBytes)),
            new InfoRow("system.filesystem.utilization", FormatPercent(s.DiskUtilization)),
            new InfoRow("system.uptime", $"{s.Uptime.TotalSeconds:N0} s"),
            new InfoRow("process.memory.usage", FormatBytes(s.ProcessMemoryBytes)),
        ];
    }

    private static string FormatPercent(double? ratio)
        => ratio is { } r ? $"{r * 100:0}%" : Missing;

    private static string FormatUsage(long? used, long? total)
        => used is null || total is null ? Strings.Dashboard_NotAvailable : string.Format(Strings.Dashboard_Of, FormatBytes(used), FormatBytes(total));

    private static string FormatBytes(long? bytes)
    {
        if (bytes is not { } b)
            return Missing;

        const double mb = 1024 * 1024;
        const double gb = mb * 1024;
        return b >= gb ? string.Format(Strings.Dashboard_GB, b / gb) : string.Format(Strings.Dashboard_MB, b / mb);
    }

    private static string FormatDuration(TimeSpan t)
        => t.TotalDays >= 1 ? $"{(int)t.TotalDays}d {t.Hours}h"
            : t.TotalHours >= 1 ? $"{t.Hours}h {t.Minutes}m"
            : $"{t.Minutes}m";
}
