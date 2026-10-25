using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Coreloom.Core.Telemetry;
using Coreloom.Hardware.Mac;
using Coreloom.Hardware.Mac.Native;
using Coreloom.Hardware.Mac.Sensors;

namespace Coreloom.Hardware.Mac.Tests;

/// <summary>
/// Reads the real Mac through the same providers the app uses. Runs on the macOS CI runners (Apple silicon and Intel
/// virtual machines, which have no temperature sensors but real processor, memory, disk, network and GPU counters) and
/// skips elsewhere. Set CORELOOM_MAC_REPORT to a file path to get every reading written out.
/// </summary>
public sealed class MacHardwareTests
{
    private const string MacOnly = "Reads the real Mac; runs on the macOS runners.";

    [Fact]
    public void Host_ReadsTheMac()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), MacOnly);
        using var host = MacMonitoringHost.Create(new MacHardwareOptions());
        host.Engine.InitializeAll();
        host.Engine.PollOnce();
        Thread.Sleep(1100);
        host.Engine.PollOnce();
        Thread.Sleep(1100);
        var frame = host.Engine.PollOnce();
        WriteReport(host, frame);

        foreach (var status in host.Engine.ProviderStatuses)
        {
            Assert.True(status.State is ProviderState.Running or ProviderState.Degraded, $"{status.Name}: {status.State} {status.Message} {status.LastError}");
        }

        // Processor: total and every logical processor.
        Assert.InRange(frame.GetValue(MetricIds.CpuLoad) ?? -1, 0, 100);
        int threads = (int)(Darwin.SysctlLong("hw.logicalcpu") ?? 0);
        Assert.True(threads > 0);
        for (int i = 0; i < threads; i++)
        {
            Assert.InRange(frame.GetValue(MetricIds.CpuThreadLoad(i)) ?? -1, 0, 100);
        }

        Assert.NotNull(host.Engine.Devices.Get(MetricIds.CpuDeviceId));
        Assert.False(string.IsNullOrWhiteSpace(host.Summary.CpuName));

        // Memory adds up the way Activity Monitor's does.
        double total = frame.GetValue(MetricIds.MemoryTotal) ?? 0;
        Assert.Equal(Darwin.SysctlLong("hw.memsize") ?? -1, (long)total);
        double used = frame.GetValue(MetricIds.MemoryUsed) ?? -1;
        Assert.InRange(used, 1, total);
        Assert.Equal(total - used, frame.GetValue(MetricIds.MemoryAvailable) ?? -1, 0);
        double app = frame.GetValue(MetricIds.MemoryApp) ?? -1;
        double wired = frame.GetValue(MetricIds.MemoryWired) ?? -1;
        double compressed = frame.GetValue(MetricIds.MemoryCompressed) ?? -1;
        Assert.True(app > 0 && wired > 0 && compressed >= 0);
        Assert.Equal(used, app + wired + compressed, 0);
        Assert.InRange(frame.GetValue(MetricIds.MemoryPressure) ?? -1, 1, 4);
        Assert.True(frame.GetValue(MetricIds.MemorySwapTotal) >= 0);

        // At least one disk with live counters, and the startup volume.
        var disks = host.Storage!.Disks;
        Assert.NotEmpty(disks);
        foreach (var disk in disks)
        {
            Assert.True(frame.GetValue(MetricIds.DiskRead(disk.Number)) >= 0, $"disk{disk.Number} read");
            Assert.True(frame.GetValue(MetricIds.DiskWrite(disk.Number)) >= 0, $"disk{disk.Number} write");
            Assert.InRange(frame.GetValue(MetricIds.DiskActivity(disk.Number)) ?? -1, 0, 100);
        }

        var volumes = host.Engine.Devices.GetByCategory(MetricCategory.Storage).Where(d => d.Kind == "Volume").ToArray();
        var root = Assert.Single(volumes, v => v.GetProperty("Mount point") == "/");
        string key = root.Id[4..];
        double capacity = frame.GetValue(MetricIds.VolumeTotal(key)) ?? 0;
        Assert.True(capacity > 1e9, $"startup volume capacity {capacity}");
        Assert.InRange(frame.GetValue(MetricIds.VolumeFree(key)) ?? -1, 0, capacity);

        // Network: the runner's Ethernet, with the active connection's counters.
        Assert.NotEmpty(host.Network!.GetAdapters());
        Assert.True(frame.GetValue(MetricIds.NetworkActiveDown) >= 0);

        // The GPU and its memory.
        var gpu = Assert.Single(host.Gpu!.Gpus.Take(1));
        Assert.False(string.IsNullOrWhiteSpace(gpu.Name));
        Assert.True(frame.GetValue(MetricIds.GpuMemoryUsed(gpu.Slot)) > 0, "GPU memory in use");

        // The system and its uptime.
        Assert.NotNull(host.Engine.Devices.Get(MetricIds.SystemDeviceId));
        double uptime = frame.GetValue(MetricIds.SystemUptime) ?? 0;
        Assert.True(uptime > 10 && uptime < 365 * 86400, $"uptime {uptime}");
    }

    [Fact]
    public void ProcessSampler_MeasuresThisProcess()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), MacOnly);
        var sampler = new MacProcessSampler();
        string name = MacProcessSampler.AppName(Darwin.ProcessPath(Environment.ProcessId)) ?? Darwin.ProcessName(Environment.ProcessId)!;
        Assert.False(string.IsNullOrEmpty(name));
        using var process = Process.GetCurrentProcess();
        sampler.Sample();
        var cpuBefore = process.TotalProcessorTime;
        var wall = Stopwatch.StartNew();
        var spin = Stopwatch.StartNew();
        double sink = 0;
        while (spin.ElapsedMilliseconds < 1500)
        {
            sink += Math.Sqrt(spin.ElapsedTicks);
        }

        var samples = sampler.Sample();
        process.Refresh();
        double expected = (process.TotalProcessorTime - cpuBefore).TotalMilliseconds / (wall.Elapsed.TotalMilliseconds * Environment.ProcessorCount) * 100.0;
        var mine = Assert.Single(samples, s => s.Name == name);
        Assert.True(sink > 0);

        // The kernel reports CPU time in Mach ticks on Apple silicon; a wrong conversion is off by a factor of ~42.
        Assert.InRange(mine.CpuPercent, expected * 0.6, Math.Min(100, (expected * 1.5) + 2));
        Assert.True(mine.WorkingSetBytes > 10 * 1024 * 1024, $"footprint {mine.WorkingSetBytes}");
        Assert.True(samples.Count > 20);
        Assert.InRange(MacProcessSampler.OwnFootprint(), 10e6, 8e9);
    }

    [Fact]
    public void Sensors_OpenAndCloseCleanly()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), MacOnly);
        for (int round = 0; round < 2; round++)
        {
            using var hub = new SensorHub();
            hub.Acquire();
            var first = hub.Sample();
            Thread.Sleep(300);
            var second = hub.Sample();
            Assert.NotNull(first);
            Assert.NotNull(second);
            foreach (var t in second.Temperatures)
            {
                Assert.InRange(t.Celsius, 0, 150);
            }

            foreach (var fan in second.Fans)
            {
                Assert.InRange(fan.Rpm, 0, 20000);
            }

            hub.Release();
        }
    }

    [Fact]
    public async Task MetalBenchmark_RunsEveryTestAndScoresIt()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), MacOnly);
        var adapters = Coreloom.Hardware.Mac.Gpu.MetalBenchmark.Adapters();
        Assert.NotEmpty(adapters);
        var scores = await Coreloom.Hardware.Mac.Gpu.MetalBenchmark.RunAsync(new Coreloom.Core.Benchmarks.GpuBenchmarkOptions(Seconds: 0.4, Small: true), adapters[0], null, CancellationToken.None);
        Assert.Equal(5, scores.Tests.Count);
        foreach (var test in scores.Tests)
        {
            Assert.True(test.Rate > 0 && double.IsFinite(test.Rate), $"{test.Name}: {test.Rate}");
            Assert.True(test.Score > 0, $"{test.Name} score {test.Score}");
        }

        Assert.True(scores.Score > 0);
        string? report = Environment.GetEnvironmentVariable("CORELOOM_MAC_REPORT");
        if (!string.IsNullOrWhiteSpace(report))
        {
            File.AppendAllText(Path.ChangeExtension(report, ".gpu.md"), $"{scores.Adapter.Name}: score {scores.Score:0} (graphics {scores.Graphics:0}, compute {scores.Compute:0})\n"
                + string.Join("\n", scores.Tests.Select(t => $"- {t.Name}: {t.Rate:0.###} {t.Unit} → {t.Score:0}")) + "\n");
        }
    }

    /// <summary>The real benchmark (full sizes, short runs) for the verification report; only where a report is asked for.</summary>
    [Fact]
    public async Task MetalBenchmark_FullSizeForTheReport()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), MacOnly);
        string? report = Environment.GetEnvironmentVariable("CORELOOM_MAC_REPORT");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(report), "Only for the verification report.");
        var adapters = Coreloom.Hardware.Mac.Gpu.MetalBenchmark.Adapters();
        var scores = await Coreloom.Hardware.Mac.Gpu.MetalBenchmark.RunAsync(new Coreloom.Core.Benchmarks.GpuBenchmarkOptions(Seconds: 1.5), adapters[0], null, CancellationToken.None);
        Assert.All(scores.Tests, t => Assert.True(t.Rate > 0, t.Name));
        File.AppendAllText(Path.ChangeExtension(report!, ".gpu.md"), $"\nFull size: {scores.Adapter.Name}: score {scores.Score:0} (graphics {scores.Graphics:0}, compute {scores.Compute:0})\n"
            + string.Join("\n", scores.Tests.Select(t => $"- {t.Name}: {t.Rate:0.###} {t.Unit} → {t.Score:0}")) + "\n");
    }

    [Fact]
    public void MetalStress_RunsWithoutErrors()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), MacOnly);
        using var load = new Coreloom.Hardware.Mac.Gpu.MetalStressLoad(null, small: true) { VerifySeconds = 0.5 };
        load.Start();
        Thread.Sleep(2500);
        load.Stop();
        Assert.Null(load.Failure);
        Assert.Equal(0, load.Errors);
        Assert.True(load.Work > 0);
    }

    [Fact]
    public void Smc_ReadsTheIntelRunnersKeys()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture == Architecture.X64, "Intel Macs only.");
        using var smc = Smc.Connection.Open();
        Assert.SkipWhen(smc is null, "This Mac has no SMC service.");
        Assert.True(smc!.KeyCount() > 0);
        var keys = SmcKeys.Discover(smc);
        Assert.InRange(keys.FanCount, 0, 8);
        if (keys.SystemPowerKey is not null)
        {
            Assert.InRange(keys.SystemPower(smc), 0, 2000);
        }

        foreach (var (_, key) in keys.Temperatures)
        {
            Assert.InRange(smc.Number(key) ?? -1, 0, 150);
        }
    }

    [Fact]
    public void SystemInfo_DescribesTheMac()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), MacOnly);
        var summary = MacSystemInfo.ReadSummary();
        Assert.StartsWith("macOS", summary.OperatingSystem);
        Assert.Matches(@"^\d+\.\d+", summary.OsVersion);
        Assert.True(summary.InstalledMemoryBytes > 1L << 30);
        Assert.True(summary.LogicalProcessors >= summary.PhysicalCores && summary.PhysicalCores > 0);
        Assert.False(string.IsNullOrWhiteSpace(MacSystemInfo.ComputerName()));
        Assert.False(string.IsNullOrWhiteSpace(MacSystemInfo.Model.Identifier));
        Assert.True(MacMonitoringHost.UptimeSeconds() > 10);
    }

    private static void WriteReport(MacMonitoringHost host, TelemetryFrame frame)
    {
        string? path = Environment.GetEnvironmentVariable("CORELOOM_MAC_REPORT");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var sb = new StringBuilder();
        var inv = CultureInfo.InvariantCulture;
        sb.AppendLine(inv, $"# Coreloom macOS readings — {RuntimeInformation.ProcessArchitecture}, {MacSystemInfo.OsDisplayName()}");
        sb.AppendLine();
        sb.AppendLine(inv, $"Model: {MacSystemInfo.Model.DisplayName}; firmware {MacSystemInfo.Model.Firmware ?? "—"}; virtual machine: {SensorHub.IsVirtualMachine}");
        sb.AppendLine(inv, $"Sensors: {host.Sensors.Description}");
        sb.AppendLine();
        sb.AppendLine("## Providers");
        foreach (var status in host.Engine.ProviderStatuses)
        {
            sb.AppendLine(inv, $"- {status.Name}: {status.State} — {status.Message} ({status.LastPollMilliseconds:0.0} ms)");
        }

        sb.AppendLine();
        sb.AppendLine("## Devices");
        foreach (var device in host.Engine.Devices.GetAll())
        {
            sb.AppendLine(inv, $"- **{device.Id}** {device.Name} ({device.Kind}, {device.Vendor}): {string.Join("; ", device.Properties.Select(p => $"{p.Key} = {p.Value}"))}");
        }

        sb.AppendLine();
        sb.AppendLine("## Readings");
        foreach (var descriptor in host.Engine.Catalog.Snapshot().OrderBy(d => d.Id, StringComparer.Ordinal))
        {
            string value = frame.TryGetValue(descriptor.Id, out double v) ? v.ToString("0.###", inv) : $"— {descriptor.UnavailableReason}";
            sb.AppendLine(inv, $"- `{descriptor.Id}` {descriptor.Name} [{descriptor.Kind}]: {value}");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, sb.ToString());
    }
}
