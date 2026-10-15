using Coreloom.Core.Gaming;
using Coreloom.Core.Overlay;
using Coreloom.Core.Settings;
using Coreloom.ViewModels.Infrastructure;
using Coreloom.ViewModels.Pages;
using Coreloom.ViewModels.Services;

namespace Coreloom.ViewModels.Tests;

internal sealed class FakeGaming : IGamingService
{
    public List<GameEntry> Games { get; } =
    [
        new("steam:1091500", "Cyberpunk 2077", GameStore.Steam)
        {
            InstallPath = @"D:\Steam\steamapps\common\Cyberpunk 2077",
            SizeBytes = 80L << 30,
            LastPlayed = new DateTimeOffset(2026, 10, 14, 20, 0, 0, TimeSpan.Zero),
            Launch = "steam://rungameid/1091500",
            Artwork = new GameArtwork(@"C:\cache\1091500\library_600x900.jpg", @"C:\cache\1091500\header.jpg", null),
        },
        new("epic:Dodo", "Alan Wake 2", GameStore.Epic)
        {
            InstallPath = @"D:\Epic\AlanWake2",
            SizeBytes = 90L << 30,
            Launch = "com.epicgames.launcher://apps/Dodo?action=launch&silent=true",
            CoverUrls = ["https://cdn1.epicgames.com/tall.jpg"],
        },
        new("gog:1207658924", "The Witcher 3", GameStore.Gog)
        {
            InstallPath = @"D:\GOG\The Witcher 3",
            LastPlayed = new DateTimeOffset(2026, 10, 1, 20, 0, 0, TimeSpan.Zero),
            Launch = @"D:\GOG\The Witcher 3\bin\x64\witcher3.exe",
        },
    ];

    public Dictionary<GamingTweak, TweakStatus> Tweaks { get; } = new()
    {
        [GamingTweak.GpuScheduling] = new(true, false),
        [GamingTweak.UltimatePower] = new(true, false),
        [GamingTweak.GameMode] = new(true, true),
        [GamingTweak.WindowedOptimizations] = new(true, false),
        [GamingTweak.NoBackgroundRecording] = new(true, true),
        [GamingTweak.MaxGpuPerformance] = new(true, false),
        [GamingTweak.UnlimitedShaderCache] = new(true, false),
    };

    public int FrameCap { get; set; }

    public string Driver { get; set; } = "581.57";

    public bool HasNvidia { get; set; } = true;

    public RunningGame? Running { get; set; }

    public FrameRateStats? Stats { get; set; }

    public bool Watching { get; private set; }

    public List<string> Launched { get; } = [];

    public List<string> Opened { get; } = [];

    public bool IsSupported => true;

    public Task<IReadOnlyList<GameEntry>> ScanLibraryAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<GameEntry>>(Games.ToList());

    public Task<GamingStatus> StatusAsync(GamingSettings settings, CancellationToken cancellationToken) => Task.FromResult(new GamingStatus
    {
        Tweaks = new Dictionary<GamingTweak, TweakStatus>(Tweaks),
        HasNvidiaDriver = HasNvidia,
        FrameCap = FrameCap,
        RefreshRate = 144,
        DriverVersion = Driver,
        ShaderCaches =
        [
            new ShaderCacheInfo("NVIDIA DirectX", @"C:\L\NVIDIA\DXCache", 3L << 30, 1200),
            new ShaderCacheInfo("DirectX", @"C:\L\D3DSCache", 1L << 30, 300),
            new ShaderCacheInfo("AMD Vulkan", @"C:\L\AMD\VkCache", 0, 0),
        ],
    });

    public Task<TweakResult> SetTweakAsync(GamingTweak tweak, bool on, GamingSettings settings, CancellationToken cancellationToken)
    {
        if (tweak == GamingTweak.GpuScheduling)
        {
            Tweaks[tweak] = new TweakStatus(true, on, NeedsRestart: true);
            return Task.FromResult(new TweakResult(true, "Restart to finish.", NeedsRestart: true));
        }

        if (tweak == GamingTweak.UltimatePower && on)
        {
            settings.PreviousPowerPlan = PowerPlans.Balanced.ToString();
        }

        Tweaks[tweak] = new TweakStatus(true, on);
        return Task.FromResult(new TweakResult(true, "Done."));
    }

    public Task<TweakResult> SetFrameCapAsync(int fps, CancellationToken cancellationToken)
    {
        if (!HasNvidia)
        {
            return Task.FromResult(new TweakResult(false, "No NVIDIA driver."));
        }

        FrameCap = fps;
        return Task.FromResult(new TweakResult(true, "Done."));
    }

    public Task<RunningGame?> FindRunningAsync(IReadOnlyList<GameEntry> games, CancellationToken cancellationToken) => Task.FromResult(Running);

    public void WatchFrameRates(bool on) => Watching = on;

    public FrameRateStats? FrameRate(int processId) => Running is not null && Running.ProcessIds.Contains(processId) ? Stats : null;

    public bool Launch(GameEntry game)
    {
        Launched.Add(game.Id);
        return true;
    }

    public void OpenFolder(string path) => Opened.Add(path);

    public Task<IReadOnlyList<GameEntry>> DownloadArtworkAsync(IReadOnlyList<GameEntry> games, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<GameEntry>>(games.Select(g => g with { Artwork = g.Artwork.Or(new GameArtwork(@"C:\Coreloom\Artwork\" + g.StoreId + ".jpg", null, null)) }).ToList());
}

public class GamingViewModelTests
{
    private static void Report(Action test)
    {
        try { test(); }
        catch (Exception e) { throw new InvalidOperationException("\n::error title=Gaming VM test::" + e.GetType().Name + ": " + e.Message.Replace('\n', ' ').Replace('\r', ' ') + " @ " + (e.StackTrace ?? string.Empty).Replace('\n', ' ').Replace('\r', ' ')); }
    }

    private static async Task ReportAsync(Func<Task> test)
    {
        try { await test(); }
        catch (Exception e) { throw new InvalidOperationException("\n::error title=Gaming VM test::" + e.GetType().Name + ": " + e.Message.Replace('\n', ' ').Replace('\r', ' ') + " @ " + (e.StackTrace ?? string.Empty).Replace('\n', ' ').Replace('\r', ' ')); }
    }

    private static (GamingViewModel Page, FakeGaming Fake, SettingsService Settings) Create(Action<FakeGaming>? setup = null)
    {
        var (monitor, settings, _) = Fixture.Create();
        var fake = new FakeGaming();
        setup?.Invoke(fake);
        var context = new PageContext
        {
            Monitor = monitor,
            Settings = settings,
            Timeline = new TimelineViewModel(monitor.Engine.History),
            Tools = new ToolServices { Gaming = fake },
        };
        var page = new GamingViewModel(context) { Now = () => new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero) };
        page.Activate(monitor.Engine.LatestFrame);
        return (page, fake, settings);
    }

    [Fact]
    public void Opening_ListsGames_RecentFirst_WithStoreAndSize() => Report(() =>
    {
        var (page, _, settings) = Create();
        Assert.True(page.IsLoaded);
        Assert.Equal("Gaming", page.Title);
        Assert.Equal("Icon.Controller", page.IconKey);
        Assert.Equal(["Cyberpunk 2077", "The Witcher 3", "Alan Wake 2"], page.Games.Select(g => g.Name));
        Assert.StartsWith("3 games · ", page.LibrarySummary);

        var cyberpunk = page.Games[0];
        Assert.Equal("Steam", cyberpunk.StoreName);
        Assert.StartsWith("Steam · 80", cyberpunk.Detail);
        Assert.Equal("Played yesterday", cyberpunk.LastPlayedText);
        Assert.EndsWith("library_600x900.jpg", cyberpunk.GridPicture);
        Assert.EndsWith("header.jpg", cyberpunk.ListPicture);

        var witcher = page.Games[1];
        Assert.False(witcher.HasGridPicture);
        Assert.Equal("W", witcher.Initials);
        Assert.StartsWith("#", witcher.PlaceholderColor);
        Assert.Equal("Not played yet", page.Games[2].LastPlayedText);
        settings.Dispose();
    });

    [Fact]
    public void Sorting_Search_AndViewAreRemembered() => Report(() =>
    {
        var (page, _, settings) = Create();
        Assert.True(page.IsListView);

        page.SelectedSort = GameSort.Name;
        Assert.Equal(["Alan Wake 2", "Cyberpunk 2077", "The Witcher 3"], page.Games.Select(g => g.Name));
        page.SelectedSort = GameSort.Size;
        Assert.Equal("Alan Wake 2", page.Games[0].Name);
        Assert.Equal(GameSort.Size, settings.Current.Gaming.LibrarySort);

        page.Search = "witcher gog";
        Assert.Equal("The Witcher 3", Assert.Single(page.Games).Name);
        page.Search = "halo";
        Assert.False(page.HasGames);
        Assert.Equal("No games match your search.", page.EmptyText);
        page.ClearSearchCommand.Execute(null);
        Assert.Equal(3, page.Games.Count);

        page.ShowGridCommand.Execute(null);
        Assert.True(page.IsGridView);
        Assert.Equal(GameLibraryView.Grid, settings.Current.Gaming.LibraryView);
        settings.Dispose();
    });

    [Fact]
    public void PlayAndOpenFolder_GoThroughTheService() => Report(() =>
    {
        var (page, fake, settings) = Create();
        page.Games[0].PlayCommand.Execute(null);
        page.Games[0].OpenFolderCommand.Execute(null);
        Assert.Equal(["steam:1091500"], fake.Launched);
        Assert.Equal([@"D:\Steam\steamapps\common\Cyberpunk 2077"], fake.Opened);
        settings.Dispose();
    });

    [Fact]
    public Task MissingCovers_AreOffered_AndDownloadedOnlyWhenAsked() => ReportAsync(async () =>
    {
        var (page, _, settings) = Create();
        Assert.Equal(1, page.MissingArtworkCount); // Alan Wake 2 has a link; The Witcher 3 has none
        Assert.True(page.ShowArtworkOffer);
        Assert.Contains("1 game has no cover", page.ArtworkOfferText);
        Assert.False(settings.Current.Gaming.DownloadArtwork);

        await page.DownloadArtworkCommand.ExecuteAsync(null);

        Assert.True(settings.Current.Gaming.DownloadArtwork);
        Assert.Equal(0, page.MissingArtworkCount);
        Assert.False(page.ShowArtworkOffer);
        Assert.EndsWith("Dodo.jpg", page.Games.Single(g => g.Name == "Alan Wake 2").GridPicture);
        settings.Dispose();
    });

    [Fact]
    public Task Boosts_ShowState_AndBoostNowTurnsOnTheRest() => ReportAsync(async () =>
    {
        var (page, fake, settings) = Create();
        Assert.Equal(6, page.Boosts.Count);
        Assert.Equal(7, page.BoostsAvailable);
        Assert.Equal(2, page.BoostsOn);
        Assert.Equal("2/7", page.ReadinessText);
        Assert.Contains("5 boosts can make", page.HeroTitle);
        Assert.True(page.Boosts.Single(b => b.Tweak == GamingTweak.GameMode).IsOn);

        await page.ApplyAllCommand.ExecuteAsync(null);

        Assert.All(fake.Tweaks.Values, t => Assert.True(t.IsOn));
        Assert.Equal(7, page.BoostsOn);
        Assert.True(page.AllBoostsOn);
        Assert.True(page.RestartPending);
        Assert.Equal("Restart Windows to finish.", page.Boosts.Single(b => b.Tweak == GamingTweak.GpuScheduling).Message);
        Assert.Contains("Turned on 5 boosts. Restart Windows to finish.", page.HeroMessage);
        Assert.Equal(PowerPlans.Balanced.ToString(), settings.Current.Gaming.PreviousPowerPlan); // saved to restore later
        Assert.False(page.ApplyAllCommand.CanExecute(null));
        settings.Dispose();
    });

    [Fact]
    public Task TurningABoostOff_UsesTheSwitch() => ReportAsync(async () =>
    {
        var (page, fake, settings) = Create();
        var gameMode = page.Boosts.Single(b => b.Tweak == GamingTweak.GameMode);
        gameMode.IsOn = false;
        await Task.Yield();
        Assert.False(fake.Tweaks[GamingTweak.GameMode].IsOn);
        Assert.False(gameMode.IsOn);
        Assert.Equal("Off", gameMode.StatusText);
        settings.Dispose();
    });

    [Fact]
    public Task FramePacer_UsesTheAutomaticLimit_AndFollowsTheChosenOne() => ReportAsync(async () =>
    {
        var (page, fake, settings) = Create();
        var pacer = page.FramePacer;
        Assert.True(pacer.IsSupported);
        Assert.False(pacer.IsOn);
        Assert.Equal(138, pacer.Target); // 144 Hz display
        Assert.Equal("138", pacer.LimitText);
        Assert.Equal("144 Hz display", pacer.DisplayText);
        Assert.StartsWith("Automatic · 138 FPS", pacer.Options[0].Label);
        Assert.Contains(pacer.Options, o => o.Label == "144 FPS · refresh rate");

        pacer.IsOn = true;
        await Task.Yield();
        Assert.Equal(138, fake.FrameCap);
        Assert.True(pacer.IsOn);
        Assert.Equal("138 FPS", page.FrameLimitText);

        pacer.SelectedTarget = 120;
        await Task.Yield();
        Assert.Equal(120, fake.FrameCap);
        Assert.Equal(120, settings.Current.Gaming.FramePacerTarget);
        Assert.Equal("120", pacer.LimitText);

        pacer.IsOn = false;
        await Task.Yield();
        Assert.Equal(0, fake.FrameCap);
        Assert.Equal("Off", page.FrameLimitText);
        settings.Dispose();
    });

    [Fact]
    public void FramePacer_ExplainsWhenThereIsNoNvidiaDriver() => Report(() =>
    {
        var (page, _, settings) = Create(f => f.HasNvidia = false);
        Assert.False(page.FramePacer.IsSupported);
        Assert.False(page.FramePacer.CanToggle);
        Assert.Contains("NVIDIA", page.FramePacer.UnsupportedText);
        settings.Dispose();
    });

    [Fact]
    public void RunningGame_IsMarked_AndItsPacingMeasured() => Report(() =>
    {
        var (page, fake, settings) = Create(f =>
        {
            f.Running = new RunningGame(f.Games[0], [4242]);
            f.Stats = new FrameRateStats(138, 7.2, 124, 138);
        });

        Assert.True(page.IsPlaying);
        Assert.Equal("Cyberpunk 2077", page.NowPlayingName);
        Assert.True(page.Games[0].IsRunning);
        Assert.True(fake.Watching);
        Assert.True(page.FramePacer.HasLive);
        Assert.Equal("138", page.FramePacer.LiveFps);
        Assert.Equal("124", page.FramePacer.LiveLow);
        Assert.Equal("90 %", page.FramePacer.LiveConsistency);
        Assert.Equal("Smooth", page.FramePacer.PacingText);
        Assert.Equal(ColorKeys.ToneCool, page.FramePacer.PacingColorKey);

        page.Deactivate();
        Assert.False(fake.Watching);
        Assert.False(page.FramePacer.HasLive);
        settings.Dispose();
    });

    [Fact]
    public void ShaderCaches_AreSummarised_AndADriverChangeIsPointedOut() => Report(() =>
    {
        var (page, _, settings) = Create(f => f.Driver = "581.57");
        Assert.Equal(2, page.ShaderCaches.Count); // the empty AMD folder is left out
        Assert.Equal("NVIDIA DirectX", page.ShaderCaches[0].Name);
        Assert.Contains("in 2 caches", page.ShaderCacheSummary);
        Assert.Equal("581.57", settings.Current.Gaming.LastDriverVersion); // first look: remembered silently
        Assert.False(page.HasDriverNotice);
        settings.Dispose();

        var (changed, _, settings2) = Create(f => f.Driver = "582.10");
        settings2.Current.Gaming.LastDriverVersion = "581.57";
        changed.RefreshCommand.Execute(null);
        Assert.True(changed.HasDriverNotice);
        Assert.Contains("from 581.57 to 582.10", changed.DriverNotice);
        changed.DismissDriverNoticeCommand.Execute(null);
        Assert.False(changed.HasDriverNotice);
        Assert.Equal("582.10", settings2.Current.Gaming.LastDriverVersion);
        settings2.Dispose();
    });
}
