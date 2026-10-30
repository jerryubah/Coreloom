using Coreloom.Core.Gaming;
using Coreloom.ViewModels.Infrastructure;
using Coreloom.ViewModels.Pages;
using Coreloom.ViewModels.Services;

namespace Coreloom.ViewModels.Tests;

/// <summary>1.8.5: CPU cache optimization on the Gaming page (in 1.8.4's DirectStorage check's place), and no yellow on boosts.</summary>
public class Release185ViewModelTests
{
    private const string Cyberpunk = @"D:\Steam\steamapps\common\Cyberpunk 2077\bin\x64\Cyberpunk2077.exe";
    private const ulong All = FakeCores.All;
    private const ulong FrequencyCores = 0xFFFF_0000;

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

    /// <summary>AMD's driver isn't installed: nothing parks cores, so cache optimization keeps apps off the 3D V-Cache.</summary>
    private static void NoAmdDriver(FakeCores cores) => cores.Amd = new(ServiceState.Missing, ServiceState.Missing, null, []);

    private static void Run(FakeCores cores, int pid, string program, ulong mask = All)
    {
        cores.Running.Add(new RunningProcess(pid, program));
        cores.Processes[pid] = new ProcessAffinity(mask, All);
    }

    private static double[] Loads(double first, double second) => [.. Enumerable.Range(0, 32).Select(i => i < 16 ? first : second)];

    [Fact]
    public async Task OnARyzenX3D_ItKeepsBackgroundAppsOffTheVCache_AndGivesThemBackWhenTheGameLeaves()
    {
        var (page, _, cores, settings) = Create(NoAmdDriver);
        var boost = page.CacheOptimizationBoost;
        var cache = page.CacheOptimization;
        Assert.True(boost.IsSwitch);
        Assert.True(boost.IsSupported);
        Assert.False(boost.IsOn);
        Assert.Equal("CPU cache optimization", boost.Title);
        Assert.StartsWith("Recommended: on for processors with more than one L3 cache", boost.Recommendation);
        Assert.Null(boost.Message);

        Run(cores, 100, @"C:\Program Files\Google\Chrome\Application\chrome.exe");
        Run(cores, 101, @"C:\Users\me\AppData\Local\Discord\app-1.0\Discord.exe");
        Run(cores, 102, @"C:\Program Files\Google\Chrome\Application\chrome.exe", mask: 0xFF); // cores chosen by hand
        Run(cores, 103, @"C:\Windows\notepad.exe");                                              // not a listed app
        var t0 = new DateTime(2026, 10, 30, 20, 0, 0, DateTimeKind.Utc);

        cache.Tick(new CoreTick(FakeCores.Game, Cyberpunk, Loads(60, 10)), t0);
        Assert.Empty(cores.Sets); // off: nothing moves

        boost.IsOn = true;
        await Task.Yield();
        Assert.True(settings.Current.Gaming.CacheOptimization);
        Assert.Equal("On · waiting for a game from your library.", boost.Recommendation);
        Assert.Equal(ColorKeys.ToneCool, boost.ColorKey);

        // A program that isn't a library game moves nothing.
        cache.Tick(new CoreTick(77, @"C:\Tools\render.exe", Loads(60, 10)), t0);
        Assert.Empty(cores.Sets);

        cache.Tick(new CoreTick(FakeCores.Game, Cyberpunk, Loads(60, 10)), t0);
        Assert.Equal("Cyberpunk 2077", cache.GameName);
        Assert.Equal([(100, FrequencyCores), (101, FrequencyCores)], cores.Sets);
        Assert.Equal(All, cores.Processes[FakeCores.Game].Process); // never the game
        Assert.Equal(0xFFUL, cores.Processes[102].Process);
        Assert.Equal(All, cores.Processes[103].Process);
        Assert.Equal("Keeping 2 background apps off the cache cores while Cyberpunk 2077 plays.", boost.Recommendation);
        Assert.Null(boost.Message); // what it does is never a warning

        // A new tab started from the moved browser inherits its cores: picked up, so it gets them back too.
        Run(cores, 104, @"C:\Program Files\Google\Chrome\Application\chrome.exe", mask: FrequencyCores);
        cache.Tick(new CoreTick(FakeCores.Game, Cyberpunk, Loads(60, 10)), t0.AddSeconds(2));
        Assert.Equal(2, cache.MovedApps.Count); // looked again only every 5 seconds
        cache.Tick(new CoreTick(FakeCores.Game, Cyberpunk, Loads(60, 10)), t0.AddSeconds(6));
        Assert.Equal(["chrome.exe", "Discord.exe"], cache.MovedApps);
        Assert.StartsWith("Keeping 3 background apps", boost.Recommendation);

        // The details: the caches, which the game has, what was moved.
        boost.DetailsCommand!.Execute(null);
        Assert.True(page.Details.IsOpen);
        Assert.Equal("CPU cache optimization", page.Details.Title);
        Assert.Equal(["Cache cores", "Frequency cores", "Moved now"], page.Details.Rows.Select(r => r.Name));
        Assert.Equal(ColorKeys.ToneCool, page.Details.Rows[0].ColorKey);
        Assert.Equal("8 cores · 96 MB L3", page.Details.Rows[0].Value);
        Assert.Contains("Cyberpunk 2077 has these to itself now.", page.Details.Rows[0].Detail);
        Assert.Equal("3 background apps", page.Details.Rows[2].Value);
        Assert.All(page.Details.Rows, r => Assert.NotEqual(ColorKeys.ToneWarm, r.ColorKey));
        page.Details.CloseCommand.Execute(null);

        // Out of the game for a moment: nothing changes; 30 seconds without it and every app gets its cores back.
        cache.Tick(new CoreTick(null, null, Loads(5, 5)), t0.AddSeconds(20));
        Assert.Equal(FrequencyCores, cores.Processes[100].Process);
        cache.Tick(new CoreTick(null, null, Loads(5, 5)), t0.AddSeconds(40));
        Assert.Equal(All, cores.Processes[100].Process);
        Assert.Equal(All, cores.Processes[101].Process);
        Assert.Equal(All, cores.Processes[104].Process);
        Assert.Equal(0xFFUL, cores.Processes[102].Process);
        Assert.Null(cache.GameName);
        Assert.Equal("On · waiting for a game from your library.", boost.Recommendation);
        settings.Dispose();
    }

    [Fact]
    public async Task TurningItOff_OrClosingCoreloom_GivesTheAppsTheirCoresBack()
    {
        var (page, _, cores, settings) = Create(NoAmdDriver);
        Run(cores, 100, "chrome.exe");
        var cache = page.CacheOptimization;
        var t0 = new DateTime(2026, 10, 30, 20, 0, 0, DateTimeKind.Utc);
        page.CacheOptimizationBoost.IsOn = true;
        await Task.Yield();

        cache.Tick(new CoreTick(FakeCores.Game, Cyberpunk, Loads(60, 10)), t0);
        Assert.Equal(FrequencyCores, cores.Processes[100].Process);
        page.CacheOptimizationBoost.IsOn = false;
        await Task.Yield();
        Assert.False(settings.Current.Gaming.CacheOptimization);
        Assert.Equal(All, cores.Processes[100].Process);
        Assert.StartsWith("Recommended: on", page.CacheOptimizationBoost.Recommendation);

        page.CacheOptimizationBoost.IsOn = true;
        await Task.Yield();
        cache.Tick(new CoreTick(FakeCores.Game, Cyberpunk, Loads(60, 10)), t0.AddMinutes(1));
        Assert.Equal(FrequencyCores, cores.Processes[100].Process);
        cache.RestoreAll(); // what the app does when Coreloom closes
        Assert.Equal(All, cores.Processes[100].Process);
        settings.Dispose();
    }

    [Fact]
    public async Task WhileAmdsDriverParksTheOtherCcd_ItLeavesThingsToAmd()
    {
        var (page, gaming, cores, settings) = Create(gaming: g => g.Plan = new PowerPlan(PowerPlans.Balanced, "Balanced", true));
        Run(cores, 100, "chrome.exe");
        page.CacheOptimizationBoost.IsOn = true;
        await Task.Yield();

        page.CacheOptimization.Tick(new CoreTick(FakeCores.Game, Cyberpunk, Loads(60, 10)), new DateTime(2026, 10, 30, 20, 0, 0, DateTimeKind.Utc));
        Assert.Empty(cores.Sets);
        Assert.StartsWith("AMD's driver keeps games on the cache cores here", page.CacheOptimizationBoost.Recommendation);
        Assert.Null(page.CacheOptimizationBoost.Message);
        settings.Dispose();
    }

    [Fact]
    public async Task OnTwoEqualCcds_TheGameKeepsTheCcdItRunsOn()
    {
        const long MB = 1024 * 1024;
        var (page, _, cores, settings) = Create(c =>
        {
            c.Layout = CoreLayout.Build(
                [.. Enumerable.Range(0, 16).Select(i => new CoreUnit(0, 3UL << (2 * i), 0))],
                [new CacheUnit(3, 32 * MB, 0, 0xFFFF), new CacheUnit(3, 32 * MB, 0, 0xFFFF_0000)]);
            c.Amd = AmdVCacheStatus.None;
        });
        Run(cores, 100, "spotify.exe");
        Assert.True(page.CacheOptimizationBoost.IsSupported);
        Assert.Equal("Multi-CCD", page.CacheOptimizationBoost.Tag);
        page.CacheOptimizationBoost.IsOn = true;
        await Task.Yield();

        var t0 = new DateTime(2026, 10, 30, 20, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < CorePlacement.MinimumSamples - 1; i++)
        {
            page.CacheOptimization.Tick(new CoreTick(FakeCores.Game, Cyberpunk, Loads(10, 70)), t0.AddSeconds(i));
        }

        Assert.Empty(cores.Sets); // still finding where it runs
        Assert.Equal("Finding where Cyberpunk 2077 runs…", page.CacheOptimizationBoost.Recommendation);
        page.CacheOptimization.Tick(new CoreTick(FakeCores.Game, Cyberpunk, Loads(10, 70)), t0.AddSeconds(10));
        Assert.Equal([(100, 0xFFFFUL)], cores.Sets); // off CCD 2, where the game runs
        Assert.Equal("Keeping 1 background app off CCD 2 while Cyberpunk 2077 plays.", page.CacheOptimizationBoost.Recommendation);
        settings.Dispose();
    }

    [Fact]
    public void WithOneL3Cache_ItIsNotAvailable_AndSaysWhyWithoutYellow()
    {
        var (page, _, _, settings) = Create(c => c.Layout = CoreLayout.Build(
            [.. Enumerable.Range(0, 8).Select(i => new CoreUnit(0, 3UL << (2 * i), 0))],
            [new CacheUnit(3, 32L << 20, 0, 0xFFFF)]));
        var boost = page.CacheOptimizationBoost;
        Assert.False(boost.IsSupported);
        Assert.False(boost.CanToggle);
        Assert.Equal("Not available", boost.StatusText);
        Assert.Equal("Every core here shares one L3 cache, so there's nothing to keep apart.", boost.Message);
        Assert.Equal(ColorKeys.Neutral, boost.ColorKey);
        settings.Dispose();
    }

    [Fact]
    public void NoBoostIsYellow_EvenWhileARestartIsStillToCome()
    {
        var (page, _, _, settings) = Create();
        var gpu = page.Boosts.Single(b => b.Tweak == GamingTweak.GpuScheduling);
        gpu.Apply(new TweakStatus(true, true, NeedsRestart: true));
        Assert.Equal("Restart to finish", gpu.StatusText);
        Assert.Equal(ColorKeys.ToneCool, gpu.ColorKey);
        gpu.Apply(new TweakStatus(true, false, NeedsRestart: true));
        Assert.Equal(ColorKeys.Neutral, gpu.ColorKey);
        Assert.All(page.Boosts, b => Assert.True(b.IsSwitch)); // every boost has its switch
        Assert.DoesNotContain(page.Boosts, b => b.ColorKey == ColorKeys.ToneWarm);
        Assert.DoesNotContain(typeof(GamingViewModel).GetProperties(), p => p.Name.Contains("DirectStorage", StringComparison.Ordinal));
        settings.Dispose();
    }
}
