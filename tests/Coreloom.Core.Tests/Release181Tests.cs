using Coreloom.Core.Benchmarks;
using Coreloom.Core.Settings;

namespace Coreloom.Core.Tests;

/// <summary>1.8.1: Bluetooth names kept in Coreloom, the Mac's menu bar modules and the Mac's GPU compute scores.</summary>
public class Release181Tests
{
    [Fact]
    public void BluetoothNames_AreTrimmedCappedAndOnlyKeptWhenGiven()
    {
        Assert.Null(DevicesSettings.Clean(null));
        Assert.Null(DevicesSettings.Clean("   "));
        Assert.Equal("Desk trackpad", DevicesSettings.Clean("  Desk trackpad "));
        string longName = new('x', 60);
        Assert.Equal(DevicesSettings.MaximumNameLength, DevicesSettings.Clean(longName)!.Length);

        var devices = new DevicesSettings();
        devices.BluetoothNames["pad"] = "Desk trackpad";
        Assert.Equal("Desk trackpad", devices.NameFor("pad", "Magic Trackpad"));
        Assert.Equal("Magic Keyboard", devices.NameFor("keys", "Magic Keyboard"));
    }

    [Fact]
    public void Settings_DropEmptyBluetoothNames_AndKeepAtMostTwoHundred()
    {
        var settings = new AppSettings();
        settings.Devices.BluetoothNames["a"] = "  Kitchen speaker  ";
        settings.Devices.BluetoothNames["b"] = " ";
        for (int i = 0; i < 300; i++)
        {
            settings.Devices.BluetoothNames[$"d{i}"] = $"Device {i}";
        }

        settings.Normalize();

        Assert.Equal("Kitchen speaker", settings.Devices.BluetoothNames["a"]);
        Assert.False(settings.Devices.BluetoothNames.ContainsKey("b"));
        Assert.Equal(200, settings.Devices.BluetoothNames.Count);

        var missing = new AppSettings { Devices = null!, MenuBar = null! }.Normalize();
        Assert.Empty(missing.Devices.BluetoothNames);
        Assert.Equal(MenuBarModules.All.Count, missing.MenuBar.Modules.Count);
    }

    [Fact]
    public void MenuBar_ListsEveryModuleOnce_InOrder_NoneShownAtFirst()
    {
        var menuBar = new MenuBarSettings();
        Assert.Equal(MenuBarModules.All.Select(m => m.Id), menuBar.Modules.Select(m => m.Id));
        Assert.All(menuBar.Modules, m => Assert.False(m.Shown));
        Assert.All(menuBar.Modules, m => Assert.True(m.ShowName && m.ShowGauge));

        // Saved by a later version, edited by hand: unknown and repeated modules go, missing ones come back (off).
        var settings = new AppSettings();
        settings.MenuBar.Modules =
        [
            new MenuBarModuleSetting { Id = "gpu", Shown = true, ShowGauge = false },
            new MenuBarModuleSetting { Id = "gpu", Shown = false },
            new MenuBarModuleSetting { Id = "weather", Shown = true },
            null!,
        ];
        settings.Normalize();

        Assert.Equal(MenuBarModules.All.Select(m => m.Id), settings.MenuBar.Modules.Select(m => m.Id));
        var gpu = settings.MenuBar.For("gpu");
        Assert.True(gpu.Shown);
        Assert.False(gpu.ShowGauge);
        Assert.Single(settings.MenuBar.Modules, m => m.Shown);
        Assert.Null(MenuBarModules.Find("weather"));
        Assert.False(MenuBarModules.Find("fans")!.HasGauge);
    }

    [Fact]
    public void MacComputeScores_AreTenTimesHigher_AndOlderResultsAreConverted()
    {
        Assert.Equal(10, GpuTests.FactorFor("Compute", GpuTests.MetalComputeFactor));
        Assert.Equal(1, GpuTests.FactorFor("Graphics", GpuTests.MetalComputeFactor));
        Assert.Equal(100000, GpuTests.Score(10, 10, GpuTests.MetalComputeFactor), 6);

        // A Mac's result saved by 1.8.0: graphics 40,000, compute 4,000 on today's scale.
        var old = new BenchmarkResult
        {
            Id = "mac",
            Kind = BenchmarkKind.Gpu,
            PointsAtReference = BenchmarkScale.Gpu,
            Score = Math.Sqrt(40000 * 4000.0),
            Subscores = [new("Graphics", 40000), new("Compute", 4000)],
            Tests = [new("lighting", "Lighting", "Graphics", "fps", 300, 40000), new("compute", "Compute", "Compute", "GFLOPS", 900, 4000)],
        };

        var mac = BenchmarkScale.Upgrade(old, GpuTests.MetalComputeFactor);
        Assert.Equal(GpuTests.MetalComputeFactor, mac.ComputeFactor);
        Assert.Equal(new double[] { 40000, 40000 }, mac.Tests.Select(t => t.Score));
        Assert.Equal(new double[] { 300, 900 }, mac.Tests.Select(t => t.Rate)); // what was measured stays
        Assert.Equal(new double[] { 40000, 40000 }, mac.Subscores.Select(s => Math.Round(s.Score, 6)));
        Assert.Equal(40000, mac.Score, 6);
        Assert.Same(mac, BenchmarkScale.Upgrade(mac, GpuTests.MetalComputeFactor)); // once only

        // Windows keeps its scores.
        var windows = BenchmarkScale.Upgrade(old, 1);
        Assert.Equal(old.Score, windows.Score);
        Assert.Same(windows, BenchmarkScale.Upgrade(windows, 1));

        // A Mac's history compares old and new results on one scale.
        var history = new BenchmarkHistory([old], GpuTests.MetalComputeFactor);
        Assert.Equal(40000, history.Results[0].Score, 6);
    }
}
