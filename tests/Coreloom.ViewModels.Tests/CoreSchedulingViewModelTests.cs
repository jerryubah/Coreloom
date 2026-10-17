using Coreloom.Core.Gaming;
using Coreloom.ViewModels.Infrastructure;
using Coreloom.ViewModels.Pages;
using Coreloom.ViewModels.Services;

namespace Coreloom.ViewModels.Tests;

/// <summary>A Ryzen 9 9950X3D's cores, AMD's driver, and a game's cores, as the tests set them.</summary>
internal sealed class FakeCores : ICoreSchedulingService
{
    public const int Game = 4242;
    public const ulong All = 0xFFFF_FFFF;
    public const ulong CacheCores = 0xFFFF;

    private const long MB = 1024 * 1024;

    public static CoreLayout X3D { get; } = CoreLayout.Build(
        [.. Enumerable.Range(0, 16).Select(i => new CoreUnit(0, 3UL << (2 * i), 0))],
        [new CacheUnit(3, 96 * MB, 0, 0xFFFF), new CacheUnit(3, 32 * MB, 0, 0xFFFF_0000)]);

    public CoreLayout Layout { get; set; } = X3D;

    public AmdVCacheStatus Amd { get; set; } = new(ServiceState.Running, ServiceState.Running, 0, []);

    public Dictionary<int, ProcessAffinity> Processes { get; } = new() { [Game] = new(All, All) };

    /// <summary>Every change of a process's cores.</summary>
    public List<(int Pid, ulong Mask)> Sets { get; } = [];

    /// <summary>Windows (or the game's anti-cheat) refuses changes.</summary>
    public bool Refuse { get; set; }

    public bool IsSupported => true;

    public CoreLayout ReadLayout() => Layout;

    public AmdVCacheStatus ReadAmdStatus() => Amd;

    public ProcessAffinity? GetAffinity(int processId) => Processes.TryGetValue(processId, out var affinity) ? affinity : null;

    public bool SetAffinity(int processId, ulong mask)
    {
        if (Refuse || !Processes.TryGetValue(processId, out var affinity))
        {
            return false;
        }

        Sets.Add((processId, mask));
        Processes[processId] = affinity with { Process = mask };
        return true;
    }
}

public class CoreSchedulingViewModelTests
{
    private const string Cyberpunk = @"D:\Steam\steamapps\common\Cyberpunk 2077\bin\x64\Cyberpunk2077.exe";

    private static (GamingViewModel Page, FakeGaming Gaming, FakeCores Cores, SettingsService Settings) Create(Action<FakeCores>? cores = null, Action<FakeGaming>? gaming = null)
    {
        var (monitor, settings, _) = Fixture.Create();
        var fakeGaming = new FakeGaming();
        var fakeCores = new FakeCores();
        gaming?.Invoke(fakeGaming);
        cores?.Invoke(fakeCores);
        var context = new PageContext
        {
            Monitor = monitor,
            Settings = settings,
            Timeline = new TimelineViewModel(monitor.Engine.History),
            Tools = new ToolServices { Gaming = fakeGaming, CoreScheduling = fakeCores },
        };
        var page = new GamingViewModel(context);
        page.Activate(monitor.Engine.LatestFrame);
        return (page, fakeGaming, fakeCores, settings);
    }

    /// <summary>Load on each of the 32 logical processors: the first 16 are the cache cores.</summary>
    private static double[] Loads(double cache, double frequency) => [.. Enumerable.Range(0, 32).Select(i => i < 16 ? cache : frequency)];

    private static PacingSecond Second(double fps, double p99Ms) => new(fps, 1000 / fps, p99Ms, (int)fps, 0, p99Ms);

    private static async Task TurnOnAsync(GamingViewModel page)
    {
        page.CoreSchedulingBoost.IsOn = true;
        await Task.Yield();
        Assert.True(page.CoreScheduling.IsOn);
    }

    private static void Play(CoreSchedulingController scheduling, int seconds, double[] loads, Func<bool, PacingSecond>? frames = null)
    {
        for (int i = 0; i < seconds; i++)
        {
            bool onCandidate = scheduling.Trial?.OnCandidate ?? false;
            scheduling.Tick(new CoreTick(FakeCores.Game, Cyberpunk, loads, frames?.Invoke(onCandidate)));
        }
    }

    [Fact]
    public void AMultiCcdRyzenX3D_OffersCoreScheduling_AndChecksWhatAmdsSchedulingNeeds()
    {
        var (page, _, _, settings) = Create(gaming: g => g.Plan = new PowerPlan(PowerPlans.Balanced, "Balanced", true));
        var scheduling = page.CoreScheduling;

        Assert.True(page.CoreSchedulingBoost.IsSupported);
        Assert.False(page.CoreSchedulingBoost.IsOn);
        Assert.Equal("Ryzen X3D", scheduling.BadgeText);
        Assert.Equal("2 CCDs · 3D V-Cache on one", scheduling.LayoutText);
        Assert.Equal(["Cache cores", "Frequency cores"], scheduling.Groups.Select(g => g.Name));
        Assert.True(scheduling.Groups[0].IsPreferred);
        Assert.Equal("—", scheduling.Groups[0].ShareText);
        Assert.Equal(["AMD 3D V-Cache driver", "AMD 3D V-Cache service", "AMD preference", "Game Mode", "Power plan"], scheduling.Checks.Select(c => c.Name));
        Assert.All(scheduling.Checks, c => Assert.Equal(ColorKeys.ToneCool, c.ColorKey));
        Assert.Equal("Off", scheduling.StatusText);
        Assert.False(scheduling.CanTest);

        // Ultimate Performance stops the core parking AMD's driver needs: advised against on hover (no extra line under
        // the card), and not counted as a boost.
        var ultimate = page.Boosts.Single(b => b.Tweak == GamingTweak.UltimatePower);
        Assert.False(ultimate.CountsAsBoost);
        Assert.Contains("core parking", ultimate.Advice);
        Assert.Null(ultimate.Message);
        Assert.Equal(8, page.BoostsAvailable);

        // The details open from the boost's Details, over the page.
        Assert.True(page.CoreSchedulingBoost.HasDetails);
        Assert.False(page.Boosts.Single(b => b.Tweak == GamingTweak.GSync).HasDetails);
        page.CoreSchedulingBoost.DetailsCommand!.Execute(null);
        Assert.True(scheduling.IsDetailsOpen);
        scheduling.CloseDetailsCommand.Execute(null);
        Assert.False(scheduling.IsDetailsOpen);
        settings.Dispose();
    }

    [Fact]
    public async Task BoostNow_TurnsItOn_AndLeavesUltimatePerformanceOff()
    {
        var (page, gaming, _, settings) = Create();

        await page.ApplyAllCommand.ExecuteAsync(null);

        Assert.True(settings.Current.Gaming.CoreScheduling);
        Assert.True(page.CoreSchedulingBoost.IsOn);
        Assert.False(gaming.Tweaks[GamingTweak.UltimatePower].IsOn);
        Assert.True(page.AllBoostsOn);
        Assert.Equal("On · waiting for a game", page.CoreScheduling.StatusText);
        Assert.True(page.CoreScheduling.CanTest);
        settings.Dispose();
    }

    [Fact]
    public async Task WhileAGameIsInFront_ItShowsWhichCoresCarryTheLoad()
    {
        var (page, _, _, settings) = Create();
        await TurnOnAsync(page);
        var scheduling = page.CoreScheduling;

        Play(scheduling, 2, Loads(5, 45));
        Assert.Equal("Measuring where Cyberpunk 2077 runs…", scheduling.StatusText);

        Play(scheduling, 3, Loads(5, 45));
        Assert.Equal("Cyberpunk 2077: mostly on the frequency cores (90 % of the load)", scheduling.StatusText);
        Assert.Equal(ColorKeys.ToneHot, scheduling.StatusColorKey);
        Assert.Equal("90 %", scheduling.Groups[1].ShareText);
        Assert.Contains("Game Bar", scheduling.Hint);

        Play(scheduling, CorePlacement.WindowSamples, Loads(45, 5));
        Assert.Equal("Cyberpunk 2077: 90 % of the load on the cache cores", scheduling.StatusText);
        Assert.Equal(ColorKeys.ToneCool, scheduling.StatusColorKey);
        Assert.False(scheduling.HasHint);

        scheduling.Tick(new CoreTick(null, null, Loads(1, 1)));
        Assert.Equal("Cyberpunk 2077 is in the background", scheduling.StatusText);
        settings.Dispose();
    }

    [Fact]
    public async Task AKeptProfile_IsApplied_AfterGivingAmdsDriverAMoment_AndTurningOffGivesTheCoresBack()
    {
        var (page, _, cores, settings) = Create();
        settings.Update("Gaming", s => s.Gaming.CoreProfiles.Add(new CoreProfile { Program = "Cyberpunk2077.exe", Name = "Cyberpunk 2077", FpsGain = 0.06, LowGain = 0.04 }));
        await TurnOnAsync(page);
        var scheduling = page.CoreScheduling;
        Assert.Equal("Cyberpunk 2077", Assert.Single(scheduling.Profiles).Name);
        Assert.Equal("cache cores · +6 % FPS", scheduling.Profiles[0].Text);

        Play(scheduling, CoreSchedulingController.PlacementWait - 1, Loads(5, 45));
        Assert.Empty(cores.Sets); // AMD's driver may still move it

        Play(scheduling, 1, Loads(5, 45));
        Assert.Equal((FakeCores.Game, FakeCores.CacheCores), Assert.Single(cores.Sets));
        Assert.Equal("Kept Cyberpunk 2077 on the cache cores (its profile: +6 % FPS, +4 % 1% lows).", scheduling.LastActionText);

        page.CoreSchedulingBoost.IsOn = false;
        await Task.Yield();
        Assert.Equal((FakeCores.Game, FakeCores.All), cores.Sets[^1]);
        Assert.Equal("Gave 1 game Windows' choice of cores back.", scheduling.LastActionText);
        Assert.Equal("Off", scheduling.StatusText);
        settings.Dispose();
    }

    [Fact]
    public async Task AmdsDriver_AlreadyPlacingTheGame_OrItsOwnPreference_OrCoresChosenElsewhere_MeanTheProfileIsLeftOut()
    {
        static CoreProfile Profile() => new() { Program = "Cyberpunk2077.exe", Name = "Cyberpunk 2077", FpsGain = 0.05 };

        var (page, _, cores, settings) = Create();
        settings.Update("Gaming", s => s.Gaming.CoreProfiles.Add(Profile()));
        await TurnOnAsync(page);
        Play(page.CoreScheduling, CoreSchedulingController.PlacementWait, Loads(45, 5));
        Assert.Empty(cores.Sets);
        Assert.Equal("AMD's driver already keeps Cyberpunk 2077 on the cache cores; its profile isn't needed now.", page.CoreScheduling.LastActionText);
        settings.Dispose();

        (page, _, cores, settings) = Create(c => c.Amd = c.Amd with { Apps = [new AmdAppPreference("cp", "Cyberpunk2077.exe", CoreKind.Frequency)] });
        settings.Update("Gaming", s => s.Gaming.CoreProfiles.Add(Profile()));
        await TurnOnAsync(page);
        Play(page.CoreScheduling, 1, Loads(5, 45));
        Assert.Empty(cores.Sets);
        Assert.StartsWith("AMD's driver has its own preference for Cyberpunk 2077 (frequency cores)", page.CoreScheduling.LastActionText);
        settings.Dispose();

        (page, _, cores, settings) = Create(c =>
        {
            c.Amd = AmdVCacheStatus.None;
            c.Processes[FakeCores.Game] = new ProcessAffinity(0xFF, FakeCores.All);
        });
        settings.Update("Gaming", s => s.Gaming.CoreProfiles.Add(Profile()));
        await TurnOnAsync(page);
        Play(page.CoreScheduling, 1, Loads(5, 45));
        Assert.Empty(cores.Sets);
        Assert.StartsWith("Cyberpunk 2077 already runs on cores chosen elsewhere", page.CoreScheduling.LastActionText);
        Assert.Contains(page.CoreScheduling.Checks, c => c.Name == "AMD 3D V-Cache driver" && c.Status == "Not installed");
        settings.Dispose();
    }

    [Fact]
    public async Task TheTest_AlternatesTheCores_AndAClearGainCanBeKeptAsAProfile()
    {
        var (page, _, cores, settings) = Create(c => c.Amd = AmdVCacheStatus.None);
        await TurnOnAsync(page);
        var scheduling = page.CoreScheduling;

        scheduling.TestCommand.Execute(null);
        Assert.True(scheduling.IsTesting);
        Assert.True(scheduling.WantsFrames);
        Assert.StartsWith("Ready: switch to your game", scheduling.TestText);

        Assert.Equal("Test ready: play your game normally for about 2 minutes.", page.CoreSchedulingBoost.Message);

        Play(scheduling, 1, Loads(30, 30), onCache => onCache ? Second(108, 11.8) : Second(100, 12.5));
        Assert.Equal("Testing Cyberpunk 2077 · Round 1 of 4 · Windows' choice · 29 s left", scheduling.TestText);
        Assert.Equal("Testing Cyberpunk 2077: round 1 of 4.", page.CoreSchedulingBoost.Message); // under the boost too

        Play(scheduling, AffinityTrial.TotalSeconds - 1, Loads(30, 30), onCache => onCache ? Second(108, 11.8) : Second(100, 12.5));

        Assert.False(scheduling.IsTesting);
        Assert.Equal([FakeCores.CacheCores, FakeCores.All, FakeCores.CacheCores, FakeCores.All], cores.Sets.Select(s => s.Mask));
        Assert.True(scheduling.HasPending);
        Assert.Equal("Cyberpunk 2077 on the cache cores: +8 % FPS, +6 % 1% lows. Keep this as its profile?", scheduling.PendingText);
        Assert.Empty(settings.Current.Gaming.CoreProfiles); // only kept when the user says so
        Assert.Equal("Cyberpunk 2077: +8 % FPS on the cache cores. Open Details to keep it.", page.CoreSchedulingBoost.Message);

        scheduling.KeepCommand.Execute(null);
        Assert.Null(page.CoreSchedulingBoost.Message);
        var profile = Assert.Single(settings.Current.Gaming.CoreProfiles);
        Assert.Equal("Cyberpunk2077.exe", profile.Program);
        Assert.Equal(CoreKind.Cache, profile.Cores);
        Assert.Equal(0.08, profile.FpsGain, 6);

        Play(scheduling, 1, Loads(30, 30)); // still running: the profile applies now
        Assert.Equal((FakeCores.Game, FakeCores.CacheCores), cores.Sets[^1]);
        settings.Dispose();
    }

    [Fact]
    public async Task ATestWithoutAClearGain_LeavesWindowsChoice_AndDropsAnOldProfile()
    {
        var (page, _, cores, settings) = Create(c => c.Amd = AmdVCacheStatus.None);
        settings.Update("Gaming", s => s.Gaming.CoreProfiles.Add(new CoreProfile { Program = "Cyberpunk2077.exe", Name = "Cyberpunk 2077", FpsGain = 0.05 }));
        await TurnOnAsync(page);
        var scheduling = page.CoreScheduling;
        scheduling.TestCommand.Execute(null);

        Play(scheduling, AffinityTrial.TotalSeconds, Loads(30, 30), _ => Second(100, 12));

        Assert.False(scheduling.HasPending);
        Assert.Equal("Cyberpunk 2077: no clear gain on the cache cores (±0 % FPS, ±0 % 1% lows), so Windows' choice stays and its old profile was removed.", scheduling.LastActionText);
        Assert.Equal(FakeCores.All, cores.Processes[FakeCores.Game].Process);
        Assert.Empty(settings.Current.Gaming.CoreProfiles);
        Assert.False(scheduling.HasProfiles);
        settings.Dispose();
    }

    [Fact]
    public async Task WhenWindowsRefusesToChangeTheCores_TheTestStops()
    {
        var (page, _, cores, settings) = Create(c => c.Amd = AmdVCacheStatus.None);
        await TurnOnAsync(page);
        var scheduling = page.CoreScheduling;
        scheduling.TestCommand.Execute(null);
        cores.Refuse = true;

        Play(scheduling, AffinityTrial.RoundSeconds, Loads(30, 30), _ => Second(100, 12));

        Assert.False(scheduling.IsTesting);
        Assert.StartsWith("Windows refused to change Cyberpunk 2077's cores", scheduling.LastActionText);
        Assert.True(scheduling.CanTest);
        settings.Dispose();
    }

    [Fact]
    public async Task AGameAmdHasAPreferenceFor_IsntTested()
    {
        var (page, _, cores, settings) = Create(c => c.Amd = c.Amd with { Apps = [new AmdAppPreference("cp", "Cyberpunk2077.exe", CoreKind.Cache)] });
        await TurnOnAsync(page);
        page.CoreScheduling.TestCommand.Execute(null);

        Play(page.CoreScheduling, 1, Loads(30, 30), _ => Second(100, 12));

        Assert.False(page.CoreScheduling.IsTesting);
        Assert.Empty(cores.Sets);
        Assert.Equal("AMD's driver has its own preference for Cyberpunk 2077 (cache cores); Coreloom doesn't test over it.", page.CoreScheduling.LastActionText);
        settings.Dispose();
    }

    [Fact]
    public void OneCcdWithTheVCacheOnEveryCore_HasNothingToSchedule()
    {
        var (page, _, _, settings) = Create(c => c.Layout = CoreLayout.Build(
            [.. Enumerable.Range(0, 8).Select(i => new CoreUnit(0, 3UL << (2 * i), 0))],
            [new CacheUnit(3, 96L << 20, 0, 0xFFFF)]));

        Assert.False(page.CoreSchedulingBoost.IsSupported);
        Assert.Equal("Every core on this processor has the 3D V-Cache, so there's nothing to choose between.", page.CoreSchedulingBoost.Message);
        Assert.Null(page.Boosts.Single(b => b.Tweak == GamingTweak.UltimatePower).Advice);
        Assert.Equal(8, page.BoostsAvailable); // Ultimate Performance counts again; core scheduling doesn't
        settings.Dispose();
    }

    [Fact]
    public void AHybridIntel_ChecksOnlyGameMode_AndLeavesUltimatePerformanceAlone()
    {
        var (page, _, _, settings) = Create(c =>
        {
            c.Layout = CoreLayout.Build(
                [.. Enumerable.Range(0, 8).Select(i => new CoreUnit(0, 3UL << (2 * i), 1)), .. Enumerable.Range(16, 16).Select(i => new CoreUnit(0, 1UL << i, 0))],
                [new CacheUnit(3, 36L << 20, 0, 0xFFFF_FFFF)]);
            c.Amd = AmdVCacheStatus.None;
        });

        Assert.Equal("Hybrid", page.CoreScheduling.BadgeText);
        Assert.Equal("Game Mode", Assert.Single(page.CoreScheduling.Checks).Name);
        Assert.Null(page.Boosts.Single(b => b.Tweak == GamingTweak.UltimatePower).Advice);
        Assert.Equal(["Performance cores", "Efficiency cores"], page.CoreScheduling.Groups.Select(g => g.Name));
        settings.Dispose();
    }
}
