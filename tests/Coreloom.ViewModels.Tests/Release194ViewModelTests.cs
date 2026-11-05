using Coreloom.Core;
using Coreloom.Core.Gaming;
using Coreloom.ViewModels.Infrastructure;
using Coreloom.ViewModels.Pages;
using Coreloom.ViewModels.Services;

namespace Coreloom.ViewModels.Tests;

/// <summary>A Mac for the boosts: High Power Mode on the charger, memory getting tight, a few apps running.</summary>
internal sealed class FakeMacPerformance : IMacPerformanceService
{
    public bool HasHighPowerMode { get; set; } = true;

    public bool OnCharger { get; set; } = true;

    public int ChargerMode { get; set; }

    public bool Allowed { get; set; }

    public bool AppleSilicon { get; set; } = true;

    public List<int> ModesSet { get; } = [];

    public List<bool> Permissions { get; } = [];

    public List<string> Quit { get; } = [];

    public List<string> Reopened { get; } = [];

    public List<RunningAppInfo> Apps { get; } =
    [
        new(10, "Google Chrome", "com.google.Chrome", "/Applications/Google Chrome.app", 14, 2_500_000_000, 3, true),
        new(11, "Slack", "com.tinyspeck.slackmacgap", "/Applications/Slack.app", 2, 800_000_000, 0, true),
        new(12, "Notes", "com.apple.Notes", "/System/Applications/Notes.app", 0.1, 80_000_000, 0, true),
        new(13, "Steam", "com.valvesoftware.steam", "/Applications/Steam.app", 3, 400_000_000, 0, true),
    ];

    public bool IsSupported => true;

    public bool IsAppleSilicon => AppleSilicon;

    public double TicksToSeconds => 1e-9;

    public Task<MacPowerState> PowerAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new MacPowerState(HasHighPowerMode, OnCharger, ChargerMode, Allowed));

    public Task<TweakResult> AllowPowerSwitchingAsync(bool allow, CancellationToken cancellationToken)
    {
        Permissions.Add(allow);
        Allowed = allow;
        return Task.FromResult(new TweakResult(true, allow ? "Allowed." : "Removed."));
    }

    public Task<bool> SetChargerPowerModeAsync(int mode, CancellationToken cancellationToken)
    {
        if (!Allowed)
        {
            return Task.FromResult(false);
        }

        ModesSet.Add(mode);
        ChargerMode = mode;
        return Task.FromResult(true);
    }

    public MemorySnapshot? Memory() => new(MemoryPressure.Warning, 16L << 30, 14L << 30, 3L << 30, 1L << 30,
        [new("Cyberpunk 2077", 6L << 30), new("Google Chrome", 2_500_000_000), new("Slack", 800_000_000), new("WindowServer", 600_000_000)]);

    public ThermalPressure? ThermalState() => ThermalPressure.Nominal;

    public IReadOnlyList<CoreTimes> CoreTimes() => [];

    public Task<MetalFxFeatures> MetalFxAsync(GameEntry game, CancellationToken cancellationToken) =>
        Task.FromResult(game.Name == "Cyberpunk 2077" ? MetalFxFeatures.Linked | MetalFxFeatures.TemporalUpscaling | MetalFxFeatures.FrameInterpolation : MetalFxFeatures.None);

    public IReadOnlyList<DisplayDescription> Displays() =>
        [new("Desk monitor", false, new DisplayMode(2560, 1440, 2560, 1440, 60), 144, false, 1, 8, 2560, 1440)];

    public Task<CpuArchitecture> ArchitectureAsync(GameEntry game, CancellationToken cancellationToken) =>
        Task.FromResult(game.Name == "Alan Wake 2" ? CpuArchitecture.X86_64 : CpuArchitecture.Arm64);

    public IReadOnlyList<TranslatedProcess> TranslatedProcesses() => [];

    public IReadOnlyList<RunningAppInfo> RunningApps() => [.. Apps];

    public Task<bool> QuitAsync(RunningAppInfo app, CancellationToken cancellationToken)
    {
        Quit.Add(app.Name);
        Apps.RemoveAll(a => a.ProcessId == app.ProcessId);
        return Task.FromResult(true);
    }

    public bool Reopen(string bundlePath)
    {
        Reopened.Add(bundlePath);
        return true;
    }
}

public class Release194ViewModelTests
{
    private static readonly DateTime T0 = new(2026, 11, 5, 20, 0, 0, DateTimeKind.Utc);

    private static (GamingViewModel Page, FakeMacPerformance Mac, SettingsService Settings, FakeGaming Gaming) Create(HostPlatform platform = HostPlatform.MacOS, Action<FakeMacPerformance>? setup = null)
    {
        var (monitor, settings, _) = Fixture.Create();
        var gaming = new FakeGaming();
        gaming.Tweaks[GamingTweak.PowerMode] = new(true, false);
        gaming.Tweaks[GamingTweak.KeepAwake] = new(true, false);
        var mac = new FakeMacPerformance();
        setup?.Invoke(mac);
        var context = new PageContext
        {
            Monitor = monitor,
            Settings = settings,
            Timeline = new TimelineViewModel(monitor.Engine.History),
            Tools = new ToolServices { Gaming = gaming, MacPerformance = mac },
            Platform = platform,
        };
        var page = new GamingViewModel(context) { Now = () => new DateTimeOffset(2026, 11, 5, 20, 0, 0, TimeSpan.Zero) };
        page.Activate(monitor.Engine.LatestFrame);
        return (page, mac, settings, gaming);
    }

    private static GamingTweakViewModel Boost(GamingViewModel page, GamingTweak tweak) => page.Boosts.Single(b => b.Tweak == tweak);

    private static async Task WaitFor(Func<bool> condition)
    {
        for (int i = 0; i < 400 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), "Timed out waiting for the condition.");
    }

    [Fact]
    public async Task OnAMac_TheMetalHudMadeWayForEightBoosts_EachWithDetails()
    {
        var (page, _, settings, _) = Create();
        await WaitFor(() => Boost(page, GamingTweak.MemoryGuard).IsSupported);
        Assert.Equal(
            [GamingTweak.GameMode, GamingTweak.PowerMode, GamingTweak.HighPowerAutomation, GamingTweak.KeepAwake, GamingTweak.MemoryGuard, GamingTweak.ThermalHeadroom,
             GamingTweak.CoreWorkload, GamingTweak.MetalFx, GamingTweak.DisplayPipeline, GamingTweak.NativeApps, GamingTweak.BackgroundGovernor],
            page.Boosts.Select(b => b.Tweak));
        Assert.DoesNotContain(page.Boosts, b => b.Tweak == GamingTweak.MetalHud);
        foreach (var tweak in MacBoostsController.Tweaks)
        {
            var boost = Boost(page, tweak);
            Assert.True(boost.HasDetails, tweak.ToString());
            Assert.True(boost.IsSupported, tweak.ToString());
            Assert.False(boost.IsOn, tweak.ToString()); // off until turned on
            Assert.StartsWith("Recommended:", boost.Recommendation);
            Assert.True(boost.HasCategory);
        }

        Assert.Equal("High Power Mode automation", Boost(page, GamingTweak.HighPowerAutomation).Title);
        Assert.Equal("Visual fidelity + FPS", Boost(page, GamingTweak.MetalFx).Category);
        Assert.Equal("ARM64", Boost(page, GamingTweak.NativeApps).Tag);
        Assert.DoesNotContain("HUD", page.HeroDetail);
        settings.Dispose();
    }

    [Fact]
    public async Task HighPowerAutomation_AsksOnce_RaisesForAGame_AndPutsTheModeBack()
    {
        var (page, mac, settings, _) = Create();
        var boost = Boost(page, GamingTweak.HighPowerAutomation);
        await WaitFor(() => boost.IsSupported);

        boost.IsOn = true;
        await WaitFor(() => !boost.IsBusy && boost.IsOn);
        Assert.Equal([true], mac.Permissions); // macOS's password, once
        Assert.True(settings.Current.Gaming.HighPowerAutomation);
        Assert.Equal("Watching", boost.StatusText);

        var game = page.LibrarySnapshot().First(g => g.Name == "Cyberpunk 2077");
        await page.MacBoosts.TickAsync(new MacBoostTick(game, 42, true), T0);
        Assert.Equal([MacPowerModes.HighPower], mac.ModesSet);
        Assert.Equal(MacPowerModes.Automatic, settings.Current.Gaming.AutomationRaisedFrom);
        Assert.Equal("High Power", boost.StatusText);
        Assert.Contains("for Cyberpunk 2077", boost.Recommendation);

        // The game quits: after the quiet spell the charger's mode goes back to Automatic.
        await page.MacBoosts.TickAsync(new MacBoostTick(null, null, false), T0.AddSeconds(2));
        await page.MacBoosts.TickAsync(new MacBoostTick(null, null, false), T0.AddSeconds(14));
        Assert.Equal([MacPowerModes.HighPower], mac.ModesSet); // still in the quiet spell
        await page.MacBoosts.TickAsync(new MacBoostTick(null, null, false), T0.AddSeconds(50));
        Assert.Equal([MacPowerModes.HighPower, MacPowerModes.Automatic], mac.ModesSet);
        Assert.Null(settings.Current.Gaming.AutomationRaisedFrom);
        Assert.Contains("Back to Automatic", boost.Recommendation);

        // Turned off: the permission is removed.
        boost.IsOn = false;
        await WaitFor(() => !boost.IsBusy && !boost.IsOn);
        Assert.Equal([true, false], mac.Permissions);
        settings.Dispose();
    }

    [Fact]
    public async Task HighPowerAutomation_OnBatteryWaits_AndAMacWithoutItCantUseIt()
    {
        var (page, mac, settings, _) = Create(setup: m => m.OnCharger = false);
        var boost = Boost(page, GamingTweak.HighPowerAutomation);
        await WaitFor(() => boost.IsSupported);
        boost.IsOn = true;
        await WaitFor(() => !boost.IsBusy && boost.IsOn);
        await page.MacBoosts.TickAsync(new MacBoostTick(page.LibrarySnapshot()[0], 42, true), T0);
        Assert.Empty(mac.ModesSet);
        Assert.Contains("On battery", boost.Recommendation);
        settings.Dispose();

        var (without, _, settings2, _) = Create(setup: m => m.HasHighPowerMode = false);
        var none = Boost(without, GamingTweak.HighPowerAutomation);
        await WaitFor(() => !none.IsSupported && none.Message is not null);
        Assert.Contains("no High Power Mode", none.Message);
        Assert.False(none.CanToggle);
        settings2.Dispose();
    }

    [Fact]
    public async Task MemoryGuard_SaysWhatToQuit_AndItsDetailsListTheApps()
    {
        var (page, _, settings, _) = Create();
        var boost = Boost(page, GamingTweak.MemoryGuard);
        await WaitFor(() => boost.IsSupported);
        boost.IsOn = true;
        await WaitFor(() => !boost.IsBusy && boost.StatusText == "Elevated");
        var game = page.LibrarySnapshot().First(g => g.Name == "Cyberpunk 2077");
        await page.MacBoosts.TickAsync(new MacBoostTick(game, 42, true), T0);
        await page.MacBoosts.TickAsync(new MacBoostTick(game, 42, true), T0.AddSeconds(2));
        Assert.Contains("Quit Google Chrome", boost.Recommendation);
        Assert.Contains("Cyberpunk 2077", boost.Recommendation);
        Assert.DoesNotContain("WindowServer", boost.Recommendation);

        boost.DetailsCommand!.Execute(null);
        Assert.True(page.Details.IsOpen);
        Assert.Equal("Unified memory pressure guard", page.Details.Title);
        Assert.Equal("Memory pressure", page.Details.Rows[0].Name);
        Assert.Equal("Warning", page.Details.Rows[0].Value);
        Assert.Contains(page.Details.Rows, r => r.Name == "Google Chrome" && r.Detail == "Quitting it would give your game room.");
        Assert.Contains(page.Details.Rows, r => r.Name == "Cyberpunk 2077" && r.Detail == "Your game.");
        Assert.DoesNotContain(page.Details.Rows, r => r.ColorKey == ColorKeys.ToneWarm); // no yellow on a boost
        settings.Dispose();
    }

    [Fact]
    public async Task MetalFxDisplaysAndRosetta_ReportWhatTheyFind()
    {
        var (page, _, settings, _) = Create();
        foreach (var tweak in new[] { GamingTweak.MetalFx, GamingTweak.DisplayPipeline, GamingTweak.NativeApps })
        {
            var boost = Boost(page, tweak);
            await WaitFor(() => boost.IsSupported);
            boost.IsOn = true;
            await WaitFor(() => !boost.IsBusy && boost.IsOn);
        }

        await WaitFor(() => Boost(page, GamingTweak.MetalFx).StatusText == "1 game");
        Assert.Contains("MetalFX in Cyberpunk 2077", Boost(page, GamingTweak.MetalFx).Recommendation);
        Assert.Equal("Check", Boost(page, GamingTweak.DisplayPipeline).StatusText);
        Assert.Contains("144 Hz", Boost(page, GamingTweak.DisplayPipeline).Recommendation);
        Assert.Equal("1 on Rosetta", Boost(page, GamingTweak.NativeApps).StatusText);
        Assert.Contains("Alan Wake 2 runs through Rosetta 2", Boost(page, GamingTweak.NativeApps).Recommendation);

        Boost(page, GamingTweak.MetalFx).DetailsCommand!.Execute(null);
        var first = page.Details.Rows[0];
        Assert.Equal("Cyberpunk 2077", first.Name);
        Assert.Equal("Temporal upscaling · Frame interpolation", first.Value);
        Assert.Contains("frame generation", first.Detail);
        settings.Dispose();
    }

    [Fact]
    public async Task Governor_QuitsTickedApps_KeepsThemForAGame_AndReopensThemAfter()
    {
        var (page, mac, settings, _) = Create();
        var boost = Boost(page, GamingTweak.BackgroundGovernor);
        await WaitFor(() => boost.IsSupported);
        boost.IsOn = true;
        await WaitFor(() => !boost.IsBusy && boost.IsOn);

        var apps = page.MacBoosts.Apps;
        boost.DetailsCommand!.Execute(null);
        Assert.True(apps.IsOpen);
        await WaitFor(() => apps.HasRows);
        Assert.Equal(["Google Chrome", "Slack", "Notes"], apps.Rows.Select(r => r.Name)); // Steam is a launcher: never listed
        Assert.Equal(2, apps.HeavyCount);
        Assert.Equal("2 to review", boost.StatusText);
        Assert.NotNull(apps.ProfileGame);

        // Keep Chrome and Slack for Cyberpunk 2077.
        apps.ProfileGame = page.LibrarySnapshot().First(g => g.Name == "Cyberpunk 2077");
        apps.Rows.Single(r => r.Name == "Google Chrome").IsSelected = true;
        apps.Rows.Single(r => r.Name == "Slack").IsSelected = true;
        apps.SaveForGameCommand.Execute(null);
        Assert.Equal(["com.google.Chrome", "com.tinyspeck.slackmacgap"], settings.Current.Gaming.QuitProfileFor("steam:1091500")!.BundleIds);
        Assert.Contains("each time Cyberpunk 2077 starts", apps.ProfileText);
        apps.CloseCommand.Execute(null);

        // The game starts: they quit by themselves, to reopen when it ends.
        var game = page.LibrarySnapshot().First(g => g.Name == "Cyberpunk 2077");
        await page.MacBoosts.TickAsync(new MacBoostTick(game, 42, true), T0);
        Assert.Equal(["Google Chrome", "Slack"], mac.Quit);
        Assert.Equal(["/Applications/Google Chrome.app", "/Applications/Slack.app"], settings.Current.Gaming.AppsToReopen);
        Assert.Contains("Quit Google Chrome and Slack for Cyberpunk 2077", boost.Recommendation);

        // A game that restarts within ten seconds doesn't reopen them; one that's gone for good does.
        await page.MacBoosts.TickAsync(new MacBoostTick(null, null, false), T0.AddSeconds(2));
        await page.MacBoosts.TickAsync(new MacBoostTick(game, 42, true), T0.AddSeconds(6));
        Assert.Empty(mac.Reopened);
        await page.MacBoosts.TickAsync(new MacBoostTick(null, null, false), T0.AddSeconds(8));
        await page.MacBoosts.TickAsync(new MacBoostTick(null, null, false), T0.AddSeconds(20));
        Assert.Equal(["/Applications/Google Chrome.app", "/Applications/Slack.app"], mac.Reopened);
        Assert.Empty(settings.Current.Gaming.AppsToReopen);
        Assert.Contains("Reopened Google Chrome and Slack", boost.Recommendation);
        settings.Dispose();
    }

    [Fact]
    public async Task Governor_QuitNow_WhileAGameRuns_ReopensWhenItEnds()
    {
        var (page, mac, settings, _) = Create();
        var boost = Boost(page, GamingTweak.BackgroundGovernor);
        await WaitFor(() => boost.IsSupported);
        boost.IsOn = true;
        await WaitFor(() => !boost.IsBusy && boost.IsOn);
        var game = page.LibrarySnapshot().First(g => g.Name == "The Witcher 3");
        await page.MacBoosts.TickAsync(new MacBoostTick(game, 7, true), T0);

        var apps = page.MacBoosts.Apps;
        apps.IsOpen = true;
        await WaitFor(() => apps.HasRows);
        Assert.False(apps.QuitSelectedCommand.CanExecute(null));
        apps.Rows.Single(r => r.Name == "Slack").IsSelected = true;
        Assert.True(apps.QuitSelectedCommand.CanExecute(null));
        await apps.QuitSelectedCommand.ExecuteAsync(null);
        Assert.Equal(["Slack"], mac.Quit);
        Assert.Contains("reopens when The Witcher 3 ends", apps.Message);
        Assert.True(apps.CanReopen);
        Assert.Equal("Reopen 1 app now", apps.ReopenText);

        // Turning the governor off reopens what it quit.
        boost.IsOn = false;
        await WaitFor(() => !boost.IsBusy && !boost.IsOn);
        Assert.Equal(["/Applications/Slack.app"], mac.Reopened);
        Assert.False(apps.CanReopen);
        settings.Dispose();
    }

    [Fact]
    public async Task OnAnIntelMac_TheCoreAndRosettaBoostsDontApply()
    {
        var (page, _, settings, _) = Create(setup: m => m.AppleSilicon = false);
        await WaitFor(() => Boost(page, GamingTweak.MemoryGuard).IsSupported);
        Assert.False(Boost(page, GamingTweak.CoreWorkload).IsSupported);
        Assert.Contains("one kind of core", Boost(page, GamingTweak.CoreWorkload).Message);
        Assert.False(Boost(page, GamingTweak.NativeApps).IsSupported);
        Assert.True(Boost(page, GamingTweak.ThermalHeadroom).IsSupported);
        settings.Dispose();
    }

    [Fact]
    public void OnWindows_NothingChanges()
    {
        var (page, _, settings, _) = Create(HostPlatform.Windows);
        Assert.Equal(12, page.Boosts.Count);
        Assert.DoesNotContain(page.Boosts, b => MacBoostsController.Owns(b.Tweak));
        Assert.False(page.MacBoosts.IsSupported);
        Assert.False(page.MacBoosts.Status(GamingTweak.MemoryGuard).IsSupported);
        settings.Dispose();
    }
}
