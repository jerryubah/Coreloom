using Coreloom.Core.Hardware;
using Coreloom.Core.Telemetry;
using Coreloom.ViewModels.Pages;

namespace Coreloom.ViewModels.Tests;

public class AmdProcessorViewModelTests
{
    [Fact]
    public void AnAmdProcessor_ShowsWhatItIs_WhatItReports_AndItsReadOnlyTuning()
    {
        var (monitor, settings, provider) = Fixture.Create(extra: p => p
            .Define(MetricIds.CpuPower, MetricCategory.Cpu, MetricKind.Power, 60)
            .Define(MetricIds.CpuTdc, MetricCategory.Cpu, MetricKind.Current, 72.5)
            .Define(MetricIds.CpuFabricClock, MetricCategory.Cpu, MetricKind.Clock, 2000)
            .Define(MetricIds.CpuPerformanceLimit, MetricCategory.Cpu, MetricKind.Level, 100));
        monitor.Engine.Devices.Upsert(new DeviceInfo(MetricIds.CpuDeviceId, MetricCategory.Cpu, "AMD Ryzen 9 9950X3D 16-Core Processor")
        {
            Vendor = "AMD",
            Properties =
            [
                new("CPUID", "Family 1Ah · model 44h · stepping 0"),
                new("Instruction sets", "AVX2, AVX-512, SHA"),
                new("Features", "Core Performance Boost, CPPC preferred cores"),
            ],
        });
        var cpu = new CpuViewModel(Fixture.Context(monitor, settings));

        cpu.Activate(monitor.Engine.LatestFrame);

        Assert.True(cpu.IsAmd);
        var amd = cpu.Amd;
        Assert.Equal("Zen 5 · Granite Ridge", amd.Architecture);
        Assert.Equal("Desktop processor · 3D V-Cache · Family 1Ah · model 44h · stepping 0", amd.Caption);
        Assert.Equal(["AVX2", "AVX-512", "SHA", "Core Performance Boost", "CPPC preferred cores"], amd.Capabilities);
        Assert.Contains(amd.Readings, r => r.MetricId == MetricIds.CpuTdc && r.Number == "72.5" && r.Unit == "A");
        Assert.Contains(amd.Readings, r => r.MetricId == MetricIds.CpuFabricClock);
        Assert.Equal("Stock limit 230 W", amd.Package.Detail);
        Assert.True(amd.HasMissing);
        Assert.Contains(amd.Missing, m => m.Names.Contains("EDC", StringComparison.Ordinal));
        Assert.DoesNotContain(amd.Missing, m => m.Names.Contains("TDC", StringComparison.Ordinal));
        Assert.Equal(5, amd.Controls.Count);
        Assert.All(amd.Controls, c => Assert.False(c.CanChange));
        Assert.Equal("Stock PPT 230 W", amd.Controls[0].Status);
        Assert.Equal("Not known yet", amd.Controls[1].Status);
        Assert.Equal("Not held back", cpu.PerformanceLimit.Detail);
        settings.Dispose();
    }

    [Fact]
    public void PackagePowerAboveTheStockLimit_ForAWhile_ShowsPboAsLikelyOn()
    {
        var (monitor, settings, provider) = Fixture.Create(extra: p => p.Define(MetricIds.CpuPower, MetricCategory.Cpu, MetricKind.Power, 20));
        var cpu = new CpuViewModel(Fixture.Context(monitor, settings));
        cpu.Activate(monitor.Engine.LatestFrame);
        Assert.Equal(TuningState.Unknown, cpu.Amd.Controls[1].State);

        provider.Values[MetricIds.CpuPower] = 260; // Ryzen 9 9950X3D: stock PPT 230 W
        for (int i = 0; i < 4; i++)
        {
            monitor.Engine.PollOnce();
        }

        cpu.Deactivate();
        cpu.Activate(monitor.Engine.LatestFrame);

        Assert.Equal("Likely on", cpu.Amd.Controls[1].Status);
        Assert.Equal("detected", cpu.Amd.Controls[1].StateKey);
        settings.Dispose();
    }

    [Fact]
    public void AnIntelProcessor_HasNoAmdCard()
    {
        var (monitor, settings, _) = Fixture.Create();
        monitor.Engine.Devices.Upsert(new DeviceInfo(MetricIds.CpuDeviceId, MetricCategory.Cpu, "Intel(R) Core(TM) i7-12700K") { Vendor = "Intel" });
        var cpu = new CpuViewModel(Fixture.Context(monitor, settings));

        cpu.Activate(monitor.Engine.LatestFrame);

        Assert.False(cpu.IsAmd);
        Assert.Empty(cpu.Amd.Readings);
        settings.Dispose();
    }
}
