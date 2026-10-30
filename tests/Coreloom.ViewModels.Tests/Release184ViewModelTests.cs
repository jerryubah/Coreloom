using Coreloom.Core.Gaming;
using Coreloom.Core.Settings;
using Coreloom.ViewModels.Infrastructure;
using Coreloom.ViewModels.Pages;
using Coreloom.ViewModels.Services;

namespace Coreloom.ViewModels.Tests;

/// <summary>1.8.4: the Gaming page without the frame limiter, its twelve boosts, the VRR tile.</summary>
public class Release184ViewModelTests
{
    private static (GamingViewModel Page, FakeGaming Fake, SettingsService Settings) Create(Action<FakeGaming>? setup = null, Action<AppSettings>? settingsSetup = null)
    {
        var (monitor, settings, _) = Fixture.Create();
        if (settingsSetup is not null)
        {
            settings.Update("Gaming", settingsSetup);
        }

        var fake = new FakeGaming();
        setup?.Invoke(fake);
        var context = new PageContext
        {
            Monitor = monitor,
            Settings = settings,
            Timeline = new TimelineViewModel(monitor.Engine.History),
            Tools = new ToolServices { Gaming = fake },
        };
        var page = new GamingViewModel(context) { Now = () => new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero) };
        page.Activate(monitor.Engine.LatestFrame);
        return (page, fake, settings);
    }

    [Fact]
    public void TheGamingPage_HasTwelveBoosts_TheShaderCachesAmongThem_AndNoFrameLimiter()
    {
        var (page, _, settings) = Create();
        Assert.Equal(
        [
            "Hardware-accelerated GPU scheduling", "Ultimate Performance power plan", "Game Mode", "Optimizations for windowed games",
            "No background recording", "Maximum graphics card performance", "G-SYNC & adaptive refresh", "NVIDIA shader cache",
            "Keep shader caches", "DirectStorage & GPU decompression", "Intelligent VRAM management", "Intelligent CPU core scheduling",
        ], page.Boosts.Select(b => b.Title));
        Assert.All(page.Boosts, b => Assert.True(b.HasCategory));
        Assert.Equal("Loading + streaming", page.DirectStorageBoost.Category);
        Assert.Equal("AMD + NVIDIA", page.DirectStorageBoost.Tag);
        Assert.Equal("Frame stability", page.VramBoost.Category);
        Assert.Equal("AMD + NVIDIA", page.VramBoost.Tag);
        Assert.DoesNotContain(typeof(GamingViewModel).GetProperties(), p => p.Name is "FramePacer" or "Adaptive" or "FrameLimitText");
        settings.Dispose();
    }

    [Fact]
    public async Task TheNvidiaShaderCachesSize_IsChosenUnderItsBoost()
    {
        var (page, fake, settings) = Create();
        var cache = page.ShaderCache;
        Assert.Equal(["10 GB", "100 GB", "Unlimited"], cache.Choices!.Select(o => o.Label));
        Assert.Equal(ShaderCacheSize.Recommended, cache.Choice);
        Assert.Equal("Limit now: the driver's default", cache.ChoiceNote);

        cache.IsOn = true;
        await Task.Yield();
        Assert.Equal("Limit now: 10 GB", cache.ChoiceNote);
        cache.Choice = ShaderCacheSize.Large;
        await Task.Yield();
        Assert.Equal(ShaderCacheSize.Large, fake.ShaderCacheLimit);
        Assert.Equal(ShaderCacheSize.Large, settings.Current.Gaming.ShaderCacheSize);
        Assert.Equal("Limit now: 100 GB", cache.ChoiceNote);
        Assert.False(page.KeepShaderCachesBoost.HasChoices);
        settings.Dispose();
    }

    [Fact]
    public async Task KeepShaderCaches_IsASwitch_LikeTheOthers()
    {
        var (page, fake, settings) = Create();
        var keep = page.KeepShaderCachesBoost;
        Assert.True(keep.IsSwitch);
        Assert.False(keep.IsOn);
        Assert.Contains("Disk Cleanup", keep.Caveat);
        keep.IsOn = true;
        await Task.Yield();
        Assert.True(fake.Tweaks[GamingTweak.KeepShaderCaches].IsOn);
        Assert.True(keep.IsOn);
        settings.Dispose();
    }

    [Fact]
    public async Task DirectStorage_IsACheck_CountedWhenReady_AndBoostNowLeavesIt()
    {
        var (page, fake, settings) = Create();
        var ds = page.DirectStorageBoost;
        Assert.True(fake.DirectStorageChecks >= 1); // checked after the scan
        Assert.False(ds.IsSwitch);
        Assert.False(ds.CanToggle);
        Assert.Equal("Ready", ds.StatusText);
        Assert.Equal(ColorKeys.ToneCool, ds.ColorKey);
        Assert.True(ds.IsOn);
        Assert.Contains("Ready", ds.Recommendation);

        ds.IsOn = false; // a check can't be switched
        Assert.True(ds.IsOn);

        // A game on a SATA SSD: partly ready, with what's in the way; Boost now can't change that.
        fake.DirectStorage = DirectStorageCheck.Evaluate(new DirectStorageInputs(26100, "RTX 5090", 0x66,
            [new GameDrive(@"E:\", "SATA", true, new BypassIoState(true))], [new DirectStorageGame("steam:1", "Forspoken", @"E:\")]));
        await page.RescanCommand.ExecuteAsync(null); // a scan always checks again (a visit only when it's due)
        Assert.Equal("Partly ready", ds.StatusText);
        Assert.Equal(ColorKeys.ToneWarm, ds.ColorKey);
        Assert.False(ds.IsOn);
        Assert.True(ds.IsSupported);
        Assert.Contains("NVMe", ds.Message);
        Assert.Null(ds.Recommendation);

        await page.ApplyAllCommand.ExecuteAsync(null);
        Assert.False(ds.IsOn);
        Assert.Equal(page.BoostsAvailable - 1, page.BoostsOn); // everything else is on

        // Details list each part.
        ds.DetailsCommand!.Execute(null);
        Assert.True(page.Details.IsOpen);
        Assert.Equal("DirectStorage & GPU decompression", page.Details.Title);
        Assert.Contains(page.Details.Rows, r => r.Name == "Drive E:" && r.Value == "SATA SSD" && r.ColorKey == ColorKeys.ToneWarm);
        Assert.Contains(page.Details.Rows, r => r.Name == "Graphics card" && r.ColorKey == ColorKeys.ToneCool);
        Assert.True(page.Details.HasNote);
        page.Details.CloseCommand.Execute(null);
        Assert.False(page.Details.IsOpen);

        // No DirectX 12 card with Shader Model 6.0: not available.
        fake.DirectStorage = DirectStorageCheck.Evaluate(new DirectStorageInputs(19045, "Old card", 0x51, [], []));
        await page.RescanCommand.ExecuteAsync(null);
        Assert.False(ds.IsSupported);
        Assert.Equal("Not available", ds.StatusText);
        settings.Dispose();
    }

    [Fact]
    public async Task VramManagement_WatchesALibraryGame_AndKeepsARecommendation()
    {
        var (page, _, settings) = Create();
        var vram = page.Vram;
        var t0 = new DateTime(2026, 10, 30, 20, 0, 0, DateTimeKind.Utc);
        const long Gb = 1L << 30;
        string program = @"D:\Steam\steamapps\common\Cyberpunk 2077\bin\x64\Cyberpunk2077.exe";

        vram.Tick(new VramTick(4242, program, new VramSample(8 * Gb, 0, 9 * Gb, 16 * Gb)), t0);
        Assert.Null(vram.Session); // off: nothing watched
        Assert.StartsWith("Recommended: on.", page.VramBoost.Recommendation);
        Assert.Null(page.VramBoost.Message);

        page.VramBoost.IsOn = true;
        await Task.Yield();
        Assert.True(settings.Current.Gaming.VramManagement);
        Assert.Equal("Waiting for a game from your library.", page.VramBoost.Recommendation);
        Assert.Null(page.VramBoost.Message); // the warning line is only for problems

        // A program that isn't one of the games isn't watched.
        vram.Tick(new VramTick(77, @"C:\Program Files\Browser\browser.exe", new VramSample(Gb, 0, 4 * Gb, 16 * Gb)), t0);
        Assert.Null(vram.Session);

        for (int i = 0; i < 30; i++)
        {
            long inUse = (i < 20 ? 12 : 15) * Gb + (i == 25 ? Gb / 2 : 0);
            vram.Tick(new VramTick(4242, program, new VramSample(inUse - Gb, i > 22 ? 300L << 20 : 0, inUse, 16 * Gb)), t0.AddSeconds(i * 2));
        }

        Assert.Equal("Cyberpunk 2077", vram.GameName);
        Assert.StartsWith("Watching Cyberpunk 2077:", page.VramBoost.Recommendation);

        // Out of the game for a moment: the session goes on; after 30 seconds without a game it ends and is kept.
        vram.Tick(new VramTick(null, null, null), t0.AddSeconds(70));
        Assert.NotNull(vram.Session);
        vram.Tick(new VramTick(null, null, null), t0.AddSeconds(100));
        Assert.Null(vram.Session);
        var record = Assert.Single(settings.Current.Gaming.VramGames);
        Assert.Equal("steam:1091500", record.GameId);
        Assert.Equal(16 * 1024, record.TotalMb);
        Assert.Equal(15 * 1024 + 512, record.PeakInUseMb);
        Assert.Equal(VramFit.Tight, VramAdvice.Fit(record));
        Assert.StartsWith("Cyberpunk 2077: tight, 15.5 of 16 GB at most", page.VramBoost.Recommendation);

        page.VramBoost.DetailsCommand!.Execute(null);
        var row = Assert.Single(page.Details.Rows);
        Assert.Equal("Cyberpunk 2077", row.Name);
        Assert.StartsWith("Tight", row.Value);
        Assert.Equal(ColorKeys.ToneWarm, row.ColorKey);
        Assert.Contains("lower Texture quality one step", row.Detail);

        page.VramBoost.IsOn = false;
        await Task.Yield();
        Assert.False(settings.Current.Gaming.VramManagement);
        Assert.Single(settings.Current.Gaming.VramGames); // what it learned stays
        Assert.StartsWith("Recommended: on.", page.VramBoost.Recommendation);
        settings.Dispose();
    }

    [Fact]
    public async Task TheVrrTile_FollowsGSync()
    {
        var (page, _, settings) = Create();
        Assert.Equal("Off", page.VrrText);
        Assert.Equal("G-SYNC", page.VrrCaption);
        Assert.Equal(ColorKeys.Neutral, page.VrrColorKey);
        page.Boosts.Single(b => b.Tweak == GamingTweak.GSync).IsOn = true;
        await Task.Yield();
        Assert.Equal("On", page.VrrText);
        Assert.Equal(ColorKeys.ToneCool, page.VrrColorKey);
        settings.Dispose();

        var (amd, _, settings2) = Create(f =>
        {
            f.HasNvidia = false;
            f.Tweaks[GamingTweak.GSync] = TweakStatus.Unsupported("Needs an NVIDIA graphics card. On AMD graphics, FreeSync is turned on in AMD Software.");
        });
        Assert.Equal("—", amd.VrrText);
        Assert.Equal("set in AMD Software", amd.VrrCaption);
        settings2.Dispose();

        var (display, _, settings3) = Create(f => f.Tweaks[GamingTweak.GSync] = TweakStatus.Unsupported("Your display doesn't support G-SYNC or variable refresh."));
        Assert.Equal("Off", display.VrrText);
        Assert.Equal("display can't", display.VrrCaption);
        settings3.Dispose();
    }

    [Fact]
    public async Task AdaptivePacingsLimits_AreRemovedOnce_AndTheUsersOwnStay()
    {
        var (page, fake, settings) = Create(
            f =>
            {
                f.ProgramCaps["Cyberpunk2077.exe"] = 118;
                f.ProgramCaps["witcher3.exe"] = 90;
            },
            s =>
            {
                s.Gaming.AdaptivePacing = true;
                s.Gaming.GameFrameCaps.Add(new GameFrameCap { GameId = "steam:1091500", Name = "Cyberpunk 2077", Fps = 118, Executables = ["Cyberpunk2077.exe"], Adaptive = true });
                s.Gaming.GameFrameCaps.Add(new GameFrameCap { GameId = "gog:1207658924", Name = "The Witcher 3", Fps = 90, Executables = ["witcher3.exe"] });
            });
        await Task.Yield();
        Assert.False(settings.Current.Gaming.AdaptivePacing);
        var kept = Assert.Single(settings.Current.Gaming.GameFrameCaps);
        Assert.Equal("gog:1207658924", kept.GameId);
        Assert.False(fake.ProgramCaps.ContainsKey("Cyberpunk2077.exe"));
        Assert.Equal(90, fake.ProgramCaps["witcher3.exe"]);
        Assert.NotNull(page);
        settings.Dispose();
    }
}
