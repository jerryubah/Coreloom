using Coreloom.Core.Optimize;
using Coreloom.ViewModels.Infrastructure;
using Coreloom.ViewModels.Pages;
using Coreloom.ViewModels.Services;

namespace Coreloom.ViewModels.Tests;

internal sealed class FakeOptimizer : IOptimizeService, IDisposable
{
    private readonly List<ThreatInfo> _threats = [];

    public FakeOptimizer(bool withThreat = false)
    {
        Directory.CreateDirectory(Junk);
        var old = DateTime.UtcNow - TimeSpan.FromDays(3);
        for (int i = 0; i < 3; i++)
        {
            string path = Path.Combine(Junk, $"junk{i}.tmp");
            File.WriteAllBytes(path, new byte[1000]);
            File.SetLastWriteTimeUtc(path, old);
        }

        if (withThreat)
        {
            _threats.Add(new ThreatInfo("Trojan:Win32/Test", 5, @"C:\Users\me\Downloads\bad.exe", true));
        }
    }

    public string Junk { get; } = Path.Combine(Path.GetTempPath(), "coreloom-vm-junk-" + Guid.NewGuid().ToString("N"));

    public long RecycleBinBytes { get; set; } = 5000;

    public bool StartupWritable { get; set; } = true;

    public int FlushCount { get; private set; }

    public bool IsSupported => true;

    public IReadOnlyList<JunkCategory> JunkCategories() =>
    [
        new("temp", "Temporary files", "Test junk", "Icon.Clock", [new JunkLocation(Junk, MinAge: TimeSpan.FromHours(24))], true),
        new("recycle-bin", "Recycle Bin", "Deleted files", "Icon.Trash", [], false, JunkKind.RecycleBin) { Caution = "Can't be undone" },
    ];

    public JunkSize RecycleBinSize() => new(RecycleBinBytes, RecycleBinBytes > 0 ? 2 : 0);

    public bool EmptyRecycleBin()
    {
        RecycleBinBytes = 0;
        return true;
    }

    public Task<MemoryCleanResult> FreeMemoryAsync(bool clearStandby, CancellationToken cancellationToken) =>
        Task.FromResult(new MemoryCleanResult(8L << 30, 9L << 30, 32L << 30, 2)
        {
            Processes = MemoryTrim.Ordered(
            [
                new(4, "System", TrimOutcome.Skipped, Reason: "part of Windows itself"),
                new(1234, "chrome", TrimOutcome.Trimmed, 512L << 20, 120L << 20),
                new(1220, "svchost", TrimOutcome.Failed, Reason: "access denied (a protected process)"),
                new(77, "discord", TrimOutcome.Trimmed, 300L << 20, 100L << 20),
            ]),
        });

    public Task<ProtectionStatus> ProtectionAsync(CancellationToken cancellationToken) => Task.FromResult(new ProtectionStatus
    {
        DefenderAvailable = true,
        RealTimeProtection = true,
        AntivirusEnabled = true,
        Mode = "Normal",
        SignatureVersion = "1.419.12.0",
        SignaturesUpdated = DateTime.Now.AddHours(-5),
        LastQuickScan = DateTime.Now.AddDays(-1),
        Threats = _threats.ToList(),
        Products = ["Windows Defender"],
    });

    public Task<ScanOutcome> QuickScanAsync(CancellationToken cancellationToken) => Task.FromResult(new ScanOutcome(true, 0, null));

    public Task<MaintenanceResult> UpdateSignaturesAsync(CancellationToken cancellationToken) => Task.FromResult(new MaintenanceResult(true, "Up to date."));

    public Task<MaintenanceResult> RemoveThreatsAsync(CancellationToken cancellationToken)
    {
        _threats.Clear();
        return Task.FromResult(new MaintenanceResult(true, "Threats removed."));
    }

    public void OpenSecurity()
    {
    }

    public IReadOnlyList<StartupApp> StartupApps() =>
    [
        new("Steam", "Steam", "\"C:\\Steam\\steam.exe\" -silent", StartupScope.User, true) { Publisher = "Valve" },
        new("Updater", "Updater", "C:\\Updater\\up.exe", StartupScope.Machine, false),
    ];

    public bool SetStartupEnabled(StartupApp app, bool enabled) => StartupWritable;

    public Task<MaintenanceResult> FlushDnsAsync(CancellationToken cancellationToken)
    {
        FlushCount++;
        return Task.FromResult(new MaintenanceResult(true, "DNS cache cleared."));
    }

    public Task<MaintenanceResult> OptimizeDrivesAsync(CancellationToken cancellationToken) => Task.FromResult(new MaintenanceResult(true, "Drives optimized."));

    public void Dispose()
    {
        if (Directory.Exists(Junk))
        {
            Directory.Delete(Junk, recursive: true);
        }
    }
}

public class OptimizeViewModelTests
{
    private static (OptimizeViewModel Page, FakeOptimizer Fake, SettingsService Settings) Create(bool withThreat = false)
    {
        var (monitor, settings, _) = Fixture.Create();
        var fake = new FakeOptimizer(withThreat);
        var context = new PageContext
        {
            Monitor = monitor,
            Settings = settings,
            Timeline = new TimelineViewModel(monitor.Engine.History),
            Tools = new ToolServices { Optimizer = fake },
        };
        var page = new OptimizeViewModel(context);
        page.Activate(monitor.Engine.LatestFrame);
        return (page, fake, settings);
    }

    [Fact]
    public async Task FreeingMemory_ShowsEveryProcess_AndOpensTheLogs()
    {
        var (monitor, settings, _) = Fixture.Create();
        (string? Category, string? Search)? opened = null;
        var context = new PageContext
        {
            Monitor = monitor,
            Settings = settings,
            Timeline = new TimelineViewModel(monitor.Engine.History),
            Tools = new ToolServices { Optimizer = new FakeOptimizer() },
            OpenLogs = (category, search) => opened = (category, search),
        };
        var page = new OptimizeViewModel(context);
        page.Activate(monitor.Engine.LatestFrame);
        Assert.False(page.Memory.HasResults);
        Assert.False(page.Memory.ToggleDetailsCommand.CanExecute(null));

        await page.Memory.FreeUpCommand.ExecuteAsync(null);

        var memory = page.Memory;
        Assert.True(memory.HasResults);
        Assert.StartsWith("Trimmed 2 · skipped 1 · failed 1 · ended 0 (Coreloom never ends processes)", memory.TotalsText);
        Assert.Equal(["All 4", "Trimmed 2", "Skipped 1", "Failed 1"], memory.ResultFilters.Select(f => f.Label));
        Assert.Equal(["chrome.exe", "discord.exe", "svchost.exe", "System"], memory.Results.Select(r => r.Name));
        var chrome = memory.Results[0];
        Assert.Equal("PID 1234", chrome.PidText);
        Assert.Equal("Trimmed", chrome.OutcomeText);
        Assert.Equal("—", memory.Results[2].ReclaimedText);
        Assert.Equal("access denied (a protected process)", memory.Results[2].Reason);

        memory.ResultFilter = "Failed";
        Assert.Equal(["svchost.exe"], memory.Results.Select(r => r.Name));
        memory.ResultFilter = "Skipped";
        Assert.Equal(["System"], memory.Results.Select(r => r.Name));

        memory.ToggleDetailsCommand.Execute(null);
        Assert.True(memory.ShowDetails);
        memory.OpenLogsCommand.Execute(null);
        Assert.Equal(("Optimize", "Memory:"), opened);
        settings.Dispose();
    }

    [Fact]
    public async Task SmartScan_FindsJunk_ThenCleansUp()
    {
        var (page, fake, settings) = Create();
        Assert.Equal("Smart Optimize", page.Title);
        Assert.Equal(OptimizeStage.Idle, page.Stage);
        Assert.Equal(2, page.Cleanup.Categories.Count);
        Assert.Equal(2, page.Speed.StartupApps.Count);
        Assert.InRange(page.Memory.Load, 37, 38); // 24 of 64 GB in use

        await page.ScanCommand.ExecuteAsync(null);

        Assert.Equal(OptimizeStage.Ready, page.Stage);
        Assert.Equal(1, page.RingProgress, 6);
        Assert.Equal(3000, page.Cleanup.SelectedBytes); // the Recycle Bin isn't selected by default
        Assert.Equal(8000, page.Cleanup.TotalBytes);
        Assert.Equal(settings.Formatter.Bytes(3000).ToString(), page.Headline);
        Assert.Equal("Protected", page.ProtectionModule.Status);
        Assert.False(page.ProtectionModule.CanInclude);
        Assert.True(page.CleanupModule.IsIncluded);
        Assert.Equal("1 of 2 apps start with Windows", page.SpeedModule.Status);
        Assert.True(page.RunCommand.CanExecute(null));

        await page.RunCommand.ExecuteAsync(null);

        Assert.Equal(OptimizeStage.Done, page.Stage);
        Assert.Equal("All done", page.Headline);
        Assert.Empty(Directory.EnumerateFiles(fake.Junk));
        Assert.Equal(5000, fake.RecycleBinBytes); // left alone
        Assert.Equal(1, fake.FlushCount);
        Assert.Contains($"Freed {settings.Formatter.Bytes(1L << 30)} of memory", page.ResultText);
        Assert.Contains("Refreshed the network (DNS) cache", page.ResultText);
        Assert.All(page.Modules, m => Assert.True(m.IsDone, m.Id));
        Assert.Equal("Scan again", page.ScanText);
        fake.Dispose();
        settings.Dispose();
    }

    [Fact]
    public async Task Threats_AreFlagged_AndRemovedByTheRun()
    {
        var (page, fake, settings) = Create(withThreat: true);
        await page.ScanCommand.ExecuteAsync(null);

        Assert.Equal("Threats found", page.Protection.Title);
        Assert.Equal(ColorKeys.ToneHot, page.ProtectionModule.ColorKey);
        Assert.True(page.ProtectionModule.IsIncluded);
        Assert.Single(page.Protection.Threats);

        await page.RunCommand.ExecuteAsync(null);
        Assert.Empty(page.Protection.Threats);
        Assert.Equal("Protected", page.Protection.Title);
        Assert.Contains("Removed 1 threat", page.ResultText);
        fake.Dispose();
        settings.Dispose();
    }

    [Fact]
    public async Task Cleanup_CanIncludeTheRecycleBin()
    {
        var (page, fake, settings) = Create();
        await page.Cleanup.ScanCommand.ExecuteAsync(null);
        var bin = page.Cleanup.Categories.Single(c => c.Category.Kind == JunkKind.RecycleBin);
        Assert.Equal("Can't be undone", bin.Caution);
        bin.IsSelected = true;
        Assert.Equal(8000, page.Cleanup.SelectedBytes);

        await page.Cleanup.CleanCommand.ExecuteAsync(null);
        Assert.Equal(0, fake.RecycleBinBytes);
        Assert.Equal("Nothing to clean", bin.SizeText);
        Assert.StartsWith("Freed", bin.Result);
        Assert.False(page.Cleanup.CleanCommand.CanExecute(null));
        fake.Dispose();
        settings.Dispose();
    }

    [Fact]
    public void StartupSwitches_GoBackWhenWindowsRefuses()
    {
        var (page, fake, settings) = Create();
        var steam = page.Speed.StartupApps.Single(a => a.Name == "Steam");
        Assert.Equal("Valve", steam.Publisher);
        steam.IsEnabled = false;
        Assert.False(steam.IsEnabled);
        Assert.Equal("0 of 2 apps start with Windows", page.Speed.StartupSummary);

        fake.StartupWritable = false;
        steam.IsEnabled = true;
        Assert.False(steam.IsEnabled);
        Assert.NotNull(steam.Problem);
        fake.Dispose();
        settings.Dispose();
    }

    [Fact]
    public void WithoutWindowsFeatures_ThePageExplains()
    {
        var (monitor, settings, _) = Fixture.Create();
        var page = new OptimizeViewModel(Fixture.Context(monitor, settings));
        page.Activate(monitor.Engine.LatestFrame);
        Assert.False(page.IsSupported);
        Assert.False(page.ScanCommand.CanExecute(null));
        Assert.Empty(page.Cleanup.Categories);
        settings.Dispose();
    }
}
