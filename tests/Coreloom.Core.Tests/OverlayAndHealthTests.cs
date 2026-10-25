using System.Globalization;
using Coreloom.Core.Formatting;
using Coreloom.Core.Health;
using Coreloom.Core.Overlay;
using Coreloom.Core.Settings;
using Coreloom.Core.Telemetry;

namespace Coreloom.Core.Tests;

public class OverlaySettingsTests
{
    [Fact]
    public void FrameGraph_IsOfferedOnceToSetupsThatShowTheFrameRate()
    {
        var old = new OverlaySettings { Items = [OverlayCatalog.Fps, OverlayCatalog.GpuLoad], Revision = 0 }.Normalize();
        Assert.Equal([OverlayCatalog.Fps, OverlayCatalog.FrameGraph, OverlayCatalog.GpuLoad], old.Items);
        Assert.Equal(OverlaySettings.CurrentRevision, old.Revision);

        // Turned off afterwards, it stays off; a setup without FPS doesn't get it.
        old.Items.Remove(OverlayCatalog.FrameGraph);
        Assert.DoesNotContain(OverlayCatalog.FrameGraph, old.Normalize().Items);
        Assert.Equal([OverlayCatalog.Ram], new OverlaySettings { Items = [OverlayCatalog.Ram] }.Normalize().Items);

        Assert.Contains(OverlayCatalog.FrameGraph, new AppSettings().Normalize().Overlay.Items);
        Assert.True(OverlayCatalog.IsFrameRate(OverlayCatalog.FrameGraph));
        Assert.False(OverlayCatalog.IsValue(OverlayCatalog.FrameGraph));
    }

    [Fact]
    public void RecentFrameTimes_AreTheGapsBetweenTheLatestFrames()
    {
        var tracker = new FrameRateTracker(1000); // milliseconds
        foreach (long t in new long[] { 0, 7, 14, 30, 37 })
        {
            tracker.OnPresent(42, t);
        }

        var buffer = new double[3];
        Assert.Equal(3, tracker.RecentFrameTimes(42, 40, buffer));
        Assert.Equal([7.0, 16.0, 7.0], buffer);
        Assert.Equal(0, tracker.RecentFrameTimes(42, 5000, buffer)); // stale
        Assert.Equal(0, tracker.RecentFrameTimes(7, 40, buffer));
    }

    [Fact]
    public void RecentFrameTimes_SayHowLongAgoTheNewestFrameWasShown()
    {
        var tracker = new FrameRateTracker(1000); // milliseconds
        foreach (long t in new long[] { 0, 7, 14 })
        {
            tracker.OnPresent(42, t);
        }

        var buffer = new double[8];
        Assert.Equal(2, tracker.RecentFrameTimes(42, 20, buffer, out double age));
        Assert.Equal(6, age);
        Assert.Equal(0, tracker.RecentFrameTimes(42, 5000, buffer, out age)); // stale
        Assert.Equal(0, age);
    }

    [Fact]
    public void Defaults_AreOffWithEndAsHotkey()
    {
        var overlay = new AppSettings().Normalize().Overlay;
        Assert.False(overlay.Enabled);
        Assert.Equal(0x23, overlay.Hotkey.Key);
        Assert.Equal(HotkeyModifiers.None, overlay.Hotkey.Modifiers);
        Assert.Equal("End", overlay.Hotkey.ToString());
        Assert.Equal(OverlayPosition.TopLeft, overlay.Position);
        Assert.Contains(OverlayCatalog.Fps, overlay.Items);
        Assert.Contains(OverlayCatalog.GpuPower, overlay.Items);
        Assert.Contains(OverlayCatalog.CpuTemperature, overlay.Items);
        Assert.Contains(OverlayCatalog.GpuFan, overlay.Items);
    }

    [Fact]
    public void Normalize_RepairsOutOfRangeValues()
    {
        var overlay = new OverlaySettings
        {
            Hotkey = new OverlayHotkey { Key = 0x10 }, // Shift alone
            Items = ["bogus", OverlayCatalog.Ram, OverlayCatalog.Fps, OverlayCatalog.Fps],
            FontSize = 99,
            TextColor = "red",
            BackgroundColor = "#00ff00",
            TextOpacity = 0,
            BackgroundOpacity = 140,
            CornerRadius = -3,
            EdgeMargin = 999,
            Position = (OverlayPosition)42,
            Revision = OverlaySettings.CurrentRevision, // (an older revision would also be offered the frame-time graph)
        }.Normalize();

        Assert.Equal(OverlayHotkey.DefaultKey, overlay.Hotkey.Key);
        Assert.Equal<string>(new[] { OverlayCatalog.Fps, OverlayCatalog.Ram }, overlay.Items); // catalog order, once each
        Assert.Equal(OverlaySettings.MaxFontSize, overlay.FontSize);
        Assert.Equal(OverlaySettings.DefaultTextColor, overlay.TextColor);
        Assert.Equal("#00FF00", overlay.BackgroundColor);
        Assert.Equal(20, overlay.TextOpacity);
        Assert.Equal(100, overlay.BackgroundOpacity);
        Assert.Equal(0, overlay.CornerRadius);
        Assert.Equal(160, overlay.EdgeMargin);
        Assert.Equal(OverlayPosition.TopLeft, overlay.Position);
    }

    [Fact]
    public void Settings_RoundTripThroughJson()
    {
        var settings = new AppSettings();
        settings.Overlay.Enabled = true;
        settings.Overlay.Hotkey = new OverlayHotkey { Key = 0x4F, Modifiers = HotkeyModifiers.Control | HotkeyModifiers.Shift };
        settings.Overlay.Position = OverlayPosition.BottomRight;

        string json = SettingsStore.Serialize(settings);
        Assert.Contains("\"position\": \"BottomRight\"", json);
        Assert.DoesNotContain("activeHotkey", json);
        Assert.DoesNotContain("isNone", json);
        var read = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json, SettingsStore.JsonOptions)!.Normalize();

        Assert.True(read.Overlay.Enabled);
        Assert.Equal("Ctrl + Shift + O", read.Overlay.Hotkey.ToString());
        Assert.Equal(OverlayPosition.BottomRight, read.Overlay.Position);
    }

    [Theory]
    [InlineData(0x23, HotkeyModifiers.None, "End")]
    [InlineData(0x52, HotkeyModifiers.Alt, "Alt + R")]
    [InlineData(0x7B, HotkeyModifiers.Control | HotkeyModifiers.Alt, "Ctrl + Alt + F12")]
    [InlineData(0x61, HotkeyModifiers.Windows, "Win + Num 1")]
    [InlineData(0, HotkeyModifiers.Control, "None")]
    [InlineData(0xC0, HotkeyModifiers.Shift, "Shift + `")]
    public void Hotkeys_HaveReadableNames(int key, HotkeyModifiers modifiers, string expected)
    {
        Assert.Equal(expected, Hotkeys.Describe(key, modifiers));
    }

    [Fact]
    public void ModifierKeys_CantBeHotkeysOnTheirOwn()
    {
        Assert.True(Hotkeys.IsModifierKey(0x11));
        Assert.True(Hotkeys.IsModifierKey(0xA4));
        Assert.True(Hotkeys.IsModifierKey(0x5B));
        Assert.False(Hotkeys.IsModifierKey(0x23));
    }
}

public class FrameRateTrackerTests
{
    private const long Second = 1_000_000;

    [Fact]
    public void SteadyFrames_GiveTheirRate()
    {
        var tracker = new FrameRateTracker(Second);
        long t = 0;
        for (int i = 0; i < 600; i++)
        {
            t = i * (Second / 120); // 120 FPS for five seconds
            tracker.OnPresent(42, t);
        }

        var stats = tracker.Get(42, t)!.Value;
        Assert.InRange(stats.Fps, 119.5, 120.5);
        Assert.InRange(stats.FrameTimeMilliseconds, 8.3, 8.4);
        Assert.InRange(stats.LowFps, 119.5, 120.5);
        Assert.Null(tracker.Get(7, t)); // another process: nothing shown
    }

    [Fact]
    public void Stutters_PullTheOnePercentLowDown()
    {
        var tracker = new FrameRateTracker(Second);
        long t = 0;
        for (int i = 1; i <= 1000; i++)
        {
            t += i % 100 == 0 ? Second / 20 : Second / 144; // a 50 ms hitch every 100 frames
            tracker.OnPresent(1, t);
        }

        var stats = tracker.Get(1, t)!.Value;
        Assert.InRange(stats.LowFps, 19, 21);
        Assert.True(stats.Fps > 100);
    }

    [Fact]
    public void FramesArrivingOutOfOrder_AreSorted()
    {
        var tracker = new FrameRateTracker(Second);
        long step = Second / 100;
        for (int i = 0; i < 6000; i += 2)
        {
            tracker.OnPresent(3, (i + 1) * step); // each pair arrives swapped, and more than the ring holds
            tracker.OnPresent(3, i * step);
        }

        var stats = tracker.Get(3, 5999 * step)!.Value;
        Assert.InRange(stats.Fps, 99.5, 100.5);
        Assert.InRange(stats.LowFps, 99.5, 100.5); // no negative or doubled gaps
    }

    [Fact]
    public void AProcessThatStopsDrawing_HasNoFrameRate()
    {
        var tracker = new FrameRateTracker(Second);
        for (int i = 0; i < 60; i++)
        {
            tracker.OnPresent(5, i * (Second / 60));
        }

        Assert.NotNull(tracker.Get(5, Second));
        Assert.Null(tracker.Get(5, 3 * Second));
    }
}

public class HealthMonitorTests
{
    private static readonly UnitFormatter F = new(culture: CultureInfo.InvariantCulture);
    private readonly MetricCatalog _catalog = new();

    private TelemetryFrame Frame(params (string Id, double Value)[] values)
    {
        var samples = values.ToDictionary(
            v => v.Id,
            v => new MetricSample(v.Value, _catalog.Register(new MetricDescriptor(v.Id, v.Id, MetricCategory.Gpu, MetricKind.Temperature), "test")),
            StringComparer.Ordinal);
        return new TelemetryFrame(1, 1, DateTime.Now, TimeSpan.Zero, samples);
    }

    private static void Feed(HealthMonitor monitor, TelemetryFrame frame, int times, double? cpuFan = null)
    {
        for (int i = 0; i < times; i++)
        {
            monitor.Record(frame, 0, cpuFan);
        }
    }

    [Fact]
    public void ACoolGpuWithSpinningFans_IsExcellent()
    {
        var monitor = new HealthMonitor();
        Assert.Equal(HealthStatus.Checking, monitor.EvaluateGpu(F, Thresholds.GpuTemperature).Status);

        Feed(monitor, Frame((MetricIds.GpuTemperature(0), 54), (MetricIds.GpuClockEventReasons(0), 0x1), (MetricIds.GpuFanRpm(0, 1), 1200)), 10);
        var report = monitor.EvaluateGpu(F, Thresholds.GpuTemperature);

        Assert.Equal(HealthStatus.Excellent, report.Status);
        Assert.Equal("No issues found", report.Summary);
        Assert.Contains(report.Checks, c => c is { Id: "gpu-temp", Title: "Temperature normal", Detail: "54 °C on average" });
        Assert.Contains(report.Checks, c => c is { Id: "gpu-slowdown", State: CheckState.Ok });
        Assert.Contains(report.Checks, c => c is { Id: "gpu-fan", Title: "Fans spinning" });
    }

    [Fact]
    public void AWarmGpu_IsGood_AndAHotOneNeedsAttention()
    {
        var warm = new HealthMonitor();
        Feed(warm, Frame((MetricIds.GpuTemperature(0), 78)), 10);
        var report = warm.EvaluateGpu(F, Thresholds.GpuTemperature);
        Assert.Equal(HealthStatus.Good, report.Status);
        Assert.Equal("1 thing to keep an eye on", report.Summary);

        var hot = new HealthMonitor();
        Feed(hot, Frame((MetricIds.GpuTemperature(0), 86)), 10);
        Assert.Equal(HealthStatus.Attention, hot.EvaluateGpu(F, Thresholds.GpuTemperature).Status);

        // Within 2° of the driver's own slowdown temperature counts as hot too.
        var nearLimit = new HealthMonitor();
        Feed(nearLimit, Frame((MetricIds.GpuTemperature(0), 80), (MetricIds.GpuSlowdownTemperature(0), 81)), 10);
        Assert.Equal(HealthStatus.Attention, nearLimit.EvaluateGpu(F, new Thresholds(85, 95)).Status);
    }

    [Fact]
    public void BriefSpikes_DontChangeTheStatus()
    {
        var monitor = new HealthMonitor();
        Feed(monitor, Frame((MetricIds.GpuTemperature(0), 60)), 55);
        Feed(monitor, Frame((MetricIds.GpuTemperature(0), 90)), 3);
        Assert.Equal(HealthStatus.Excellent, monitor.EvaluateGpu(F, Thresholds.GpuTemperature).Status);
    }

    [Fact]
    public void ThermalAndHardwareSlowdowns_AreReported()
    {
        var thermal = new HealthMonitor();
        Feed(thermal, Frame((MetricIds.GpuTemperature(0), 70), (MetricIds.GpuClockEventReasons(0), 0x20 | 0x4)), 10);
        Assert.Contains(thermal.EvaluateGpu(F, Thresholds.GpuTemperature).Checks, c => c is { Id: "gpu-slowdown", State: CheckState.Problem });

        var powerOnly = new HealthMonitor();
        Feed(powerOnly, Frame((MetricIds.GpuTemperature(0), 70), (MetricIds.GpuClockEventReasons(0), 0x4)), 10);
        Assert.Contains(powerOnly.EvaluateGpu(F, Thresholds.GpuTemperature).Checks, c => c is { Id: "gpu-slowdown", State: CheckState.Ok });

        var hardware = new HealthMonitor();
        Feed(hardware, Frame((MetricIds.GpuTemperature(0), 70), (MetricIds.GpuClockEventReasons(0), 0x0)), 18);
        Feed(hardware, Frame((MetricIds.GpuTemperature(0), 70), (MetricIds.GpuClockEventReasons(0), 0x80)), 2);
        var report = hardware.EvaluateGpu(F, Thresholds.GpuTemperature);
        Assert.Equal(HealthStatus.Attention, report.Status);
        Assert.Contains(report.Checks, c => c is { Id: "gpu-slowdown", Title: "Hardware slowdown" });
    }

    [Fact]
    public void StoppedFans_AreAProblemOnlyWhenWarm()
    {
        var resting = new HealthMonitor();
        Feed(resting, Frame((MetricIds.GpuTemperature(0), 45), (MetricIds.GpuFanRpm(0, 1), 0)), 10);
        var report = resting.EvaluateGpu(F, Thresholds.GpuTemperature);
        Assert.Equal(HealthStatus.Excellent, report.Status);
        Assert.Contains(report.Checks, c => c.Title == "Fans resting");

        var stopped = new HealthMonitor();
        Feed(stopped, Frame((MetricIds.GpuTemperature(0), 74), (MetricIds.GpuFanRpm(0, 1), 0)), 10);
        Assert.Contains(stopped.EvaluateGpu(F, Thresholds.GpuTemperature).Checks, c => c is { Title: "Fans not spinning", State: CheckState.Problem });
    }

    [Fact]
    public void HotSpotFarAboveTheCore_IsANotice()
    {
        var monitor = new HealthMonitor();
        Feed(monitor, Frame((MetricIds.GpuTemperature(0), 65), (MetricIds.GpuHotSpotTemperature(0), 92)), 10);
        var report = monitor.EvaluateGpu(F, Thresholds.GpuTemperature);
        Assert.Equal(HealthStatus.Good, report.Status);
        Assert.Contains(report.Checks, c => c is { Id: "gpu-hotspot", Detail: "92 °C, 27 °C above the core" });
    }

    [Fact]
    public void CpuWithoutTemperature_IsGood_AndSaysWhy()
    {
        var monitor = new HealthMonitor();
        Feed(monitor, Frame((MetricIds.CpuLoad, 20)), 5);
        var report = monitor.EvaluateCpu(F, Thresholds.CpuTemperature, "Needs the PawnIO driver");
        Assert.Equal(HealthStatus.Good, report.Status);
        Assert.Contains(report.Checks, c => c is { State: CheckState.Unknown, Detail: "Needs the PawnIO driver" });

        var cool = new HealthMonitor();
        Feed(cool, Frame((MetricIds.CpuTemperature, 58)), 5, cpuFan: 900);
        var coolReport = cool.EvaluateCpu(F, Thresholds.CpuTemperature);
        Assert.Equal(HealthStatus.Excellent, coolReport.Status);
        Assert.Contains(coolReport.Checks, c => c.Title == "CPU fan spinning");

        var stalled = new HealthMonitor();
        Feed(stalled, Frame((MetricIds.CpuTemperature, 72)), 5, cpuFan: 0);
        Assert.Equal(HealthStatus.Attention, stalled.EvaluateCpu(F, Thresholds.CpuTemperature).Status);
    }

    [Fact]
    public void Memory_ChecksUseAndCommitCharge()
    {
        long gb = 1_000_000_000;
        var healthy = new HealthMonitor();
        Feed(healthy, Frame((MetricIds.MemoryLoad, 40), (MetricIds.MemoryCommitUsed, 20 * gb), (MetricIds.MemoryCommitLimit, 40 * gb)), 5);
        var report = healthy.EvaluateMemory(F, ratedMtps: 3600, configuredMtps: 3600);
        Assert.Equal(HealthStatus.Excellent, report.Status);
        Assert.Contains(report.Checks, c => c is { Id: "commit", Title: "Virtual memory normal", Detail: "50 % of the commit limit used" });
        Assert.Contains(report.Checks, c => c is { Id: "speed", Title: "Running at rated speed" });

        var slow = healthy.EvaluateMemory(F, ratedMtps: 3600, configuredMtps: 2133);
        Assert.Equal(HealthStatus.Excellent, slow.Status); // for information only: the board or BIOS decides
        Assert.Contains(slow.Checks, c => c.Title == "Running below rated speed");

        var tight = new HealthMonitor();
        Feed(tight, Frame((MetricIds.MemoryLoad, 92), (MetricIds.MemoryCommitUsed, 39 * gb), (MetricIds.MemoryCommitLimit, 40 * gb)), 5);
        var tightReport = tight.EvaluateMemory(F);
        Assert.Equal(HealthStatus.Attention, tightReport.Status);
        Assert.Equal("commit", tightReport.Checks.First(c => c.State == CheckState.Problem).Id);
        Assert.Contains(tightReport.Checks, c => c is { Title: "Memory almost full", State: CheckState.Notice });
    }

    [Fact]
    public void Memory_OnAMacGoesByPressure()
    {
        var calm = new HealthMonitor();
        Feed(calm, Frame((MetricIds.MemoryLoad, 93), (MetricIds.MemoryPressure, 1)), 5);
        var calmReport = calm.EvaluateMemory(F);
        Assert.Equal(HealthStatus.Excellent, calmReport.Status); // a full Mac with normal pressure is fine
        Assert.Contains(calmReport.Checks, c => c is { Id: "memory", Title: "Memory in use", State: CheckState.Ok });
        Assert.Contains(calmReport.Checks, c => c is { Id: "pressure", Title: "Memory pressure normal" });
        Assert.DoesNotContain(calmReport.Checks, c => c.Id == "commit");

        var squeezed = new HealthMonitor();
        Feed(squeezed, Frame((MetricIds.MemoryLoad, 70), (MetricIds.MemoryPressure, 2)), 5);
        Assert.Contains(squeezed.EvaluateMemory(F).Checks, c => c is { Id: "pressure", State: CheckState.Notice });

        var critical = new HealthMonitor();
        Feed(critical, Frame((MetricIds.MemoryLoad, 98), (MetricIds.MemoryPressure, 4)), 5);
        var criticalReport = critical.EvaluateMemory(F);
        Assert.Equal(HealthStatus.Attention, criticalReport.Status);
        Assert.Equal("pressure", criticalReport.Checks.First(c => c.State == CheckState.Problem).Id);
    }

    [Fact]
    public void Storage_ChecksSpaceWearAndTemperature()
    {
        long tb = 1_000_000_000_000;
        var healthy = new HealthMonitor();
        Feed(healthy, Frame((MetricIds.VolumeTotal("C"), tb), (MetricIds.VolumeFree("C"), tb / 2),
            (MetricIds.DiskLifeUsed(0), 3), (MetricIds.DiskTemperature(0), 41)), 5);
        var report = healthy.EvaluateStorage(F);
        Assert.Equal(HealthStatus.Excellent, report.Status);
        Assert.Contains(report.Checks, c => c is { Id: "space", Detail: "Fullest drive: C: 50 % full" });
        Assert.Contains(report.Checks, c => c is { Id: "wear", Title: "Drive wear low" });
        Assert.Contains(report.Checks, c => c is { Id: "drive-temp", Title: "Drive temperature normal" });

        var full = new HealthMonitor();
        Feed(full, Frame((MetricIds.VolumeTotal("D"), tb), (MetricIds.VolumeFree("D"), tb / 20)), 5);
        var fullReport = full.EvaluateStorage(F);
        Assert.Contains(fullReport.Checks, c => c is { Title: "D: almost full", State: CheckState.Notice });
        Assert.Contains(fullReport.Checks, c => c is { Id: "wear", State: CheckState.Unknown }); // no SMART readings

        var worn = new HealthMonitor();
        Feed(worn, Frame((MetricIds.DiskLifeUsed(1), 100)), 5);
        Assert.Equal(HealthStatus.Attention, worn.EvaluateStorage(F).Status);

        var hot = new HealthMonitor();
        Feed(hot, Frame((MetricIds.DiskTemperature(0), 74)), 5);
        Assert.Contains(hot.EvaluateStorage(F).Checks, c => c is { Title: "A drive is running hot", State: CheckState.Notice });
    }

    [Fact]
    public void TheWorstReport_DecidesTheWhole()
    {
        var excellent = new HealthReport(HealthStatus.Excellent, "", []);
        var good = new HealthReport(HealthStatus.Good, "", []);
        var attention = new HealthReport(HealthStatus.Attention, "", []);
        Assert.Equal(HealthStatus.Excellent, HealthReport.Worst([excellent, HealthReport.Checking]));
        Assert.Equal(HealthStatus.Good, HealthReport.Worst([excellent, good]));
        Assert.Equal(HealthStatus.Attention, HealthReport.Worst([good, attention, excellent]));
        Assert.Equal(HealthStatus.Checking, HealthReport.Worst([HealthReport.Checking]));
    }
}

public class DashboardHealthCardTests
{
    [Fact]
    public void TheHealthCard_FollowsTheSummaryCards_AndSpansTheDashboard()
    {
        var ids = DashboardCatalog.All.Select(c => c.Id).ToList();
        Assert.Equal(ids.IndexOf(DashboardCatalog.GpuPower) + 1, ids.IndexOf(DashboardCatalog.Health));
        Assert.Equal(new DashboardCardSize(2, 2, 6, 2), DashboardCatalog.SizeOf(DashboardCatalog.Health));
    }

    [Fact]
    public void ANewCard_LandsAfterTheCardBeforeItInTheCatalog()
    {
        // A dashboard saved before the health card existed, rearranged by the user.
        var saved = DashboardCatalog.All.Where(c => c.Id != DashboardCatalog.Health)
            .Select(c => new DashboardCardSetting { Id = c.Id })
            .Reverse()
            .ToList();

        var normalized = DashboardCatalog.Normalize(saved);

        int power = normalized.FindIndex(c => c.Id == DashboardCatalog.GpuPower);
        Assert.Equal(DashboardCatalog.Health, normalized[power + 1].Id);
        Assert.True(normalized[power + 1].Visible);
        Assert.Equal(DashboardCatalog.Network, normalized[0].Id); // the rest keeps the user's order
    }
}
