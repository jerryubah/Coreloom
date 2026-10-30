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
        [GamingTweak.GSync] = new(true, false),
        [GamingTweak.ShaderCache] = new(true, false),
        [GamingTweak.KeepShaderCaches] = new(true, false),
    };

    /// <summary>The shader cache size last set in the "driver" (MB), or null for its default.</summary>
    public uint? ShaderCacheLimit { get; set; }

    public int FrameCap { get; set; }

    /// <summary>The power plan in use.</summary>
    public PowerPlan? Plan { get; set; }

    public string Driver { get; set; } = "581.57";

    public bool HasNvidia { get; set; } = true;

    public RunningGame? Running { get; set; }

    /// <summary>A tweak Windows refuses to change.</summary>
    public GamingTweak? Refuse { get; set; }

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
        ActivePowerPlan = Plan,
        ShaderCacheLimit = ShaderCacheLimit,
        ShaderCaches =
        [
            new ShaderCacheInfo("NVIDIA DirectX", @"C:\L\NVIDIA\DXCache", 3L << 30, 1200),
            new ShaderCacheInfo("DirectX", @"C:\L\D3DSCache", 1L << 30, 300),
            new ShaderCacheInfo("AMD Vulkan", @"C:\L\AMD\VkCache", 0, 0),
        ],
    });

    public Task<TweakResult> SetTweakAsync(GamingTweak tweak, bool on, GamingSettings settings, CancellationToken cancellationToken)
    {
        if (tweak == Refuse)
        {
            return Task.FromResult(new TweakResult(false, "Windows didn't allow the change."));
        }

        if (tweak == GamingTweak.GpuScheduling)
        {
            Tweaks[tweak] = new TweakStatus(true, on, NeedsRestart: true);
            return Task.FromResult(new TweakResult(true, "Restart to finish.", NeedsRestart: true));
        }

        if (tweak == GamingTweak.UltimatePower && on)
        {
            settings.PreviousPowerPlan = PowerPlans.Balanced.ToString();
        }

        if (tweak == GamingTweak.ShaderCache)
        {
            ShaderCacheLimit = on ? settings.ShaderCacheSize : null;
        }

        Tweaks[tweak] = new TweakStatus(true, on);
        return Task.FromResult(new TweakResult(true, "Done."));
    }

    /// <summary>Per-program limits set in the "driver".</summary>
    public Dictionary<string, int> ProgramCaps { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>What the next "pick a program" dialog returns (null: cancelled).</summary>
    public string? NextProgram { get; set; }

    public string? NextPicture { get; set; }

    /// <summary>Programs that "exist", with their product names.</summary>
    public Dictionary<string, string?> Programs { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        [@"D:\Games\Celeste\Celeste.exe"] = "Celeste",
        [@"C:\Users\jerry\Desktop\tiny_game.exe"] = null,
    };

    public string? PickProgram(string? startIn = null) => NextProgram;

    public string? PickPicture() => NextPicture;

    public ProgramInfo? DescribeProgram(string path) => Programs.TryGetValue(path, out var product) ? new ProgramInfo(path, product) : null;

    public IReadOnlyList<GameEntry> AddLocalPictures(IReadOnlyList<GameEntry> games) =>
        [.. games.Select(g => g.Artwork.Cover is null ? g with { Artwork = g.Artwork with { Icon = g.Executable + ".png" } } : g)];

    public Task<TweakResult> SetGameFrameCapAsync(string gameName, IReadOnlyList<string> executables, int fps, CancellationToken cancellationToken)
    {
        if (!HasNvidia || executables.Count == 0)
        {
            return Task.FromResult(new TweakResult(false, $"Couldn't find {gameName}'s program to set its limit."));
        }

        foreach (string exe in executables)
        {
            if (fps > 0)
            {
                ProgramCaps[exe] = fps;
            }
            else
            {
                ProgramCaps.Remove(exe);
            }
        }

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

    /// <summary>The games pictures were asked for, by id.</summary>
    public List<string> ArtworkRequests { get; } = [];

    /// <summary>Holds the picture look-up until completed (null: answers at once).</summary>
    public TaskCompletionSource? ArtworkGate { get; set; }

    /// <summary>A picture is "found" for games with a store link (Alan Wake 2); none for the others (The Witcher 3).</summary>
    public async Task<IReadOnlyList<GameEntry>> DownloadArtworkAsync(IReadOnlyList<GameEntry> games, CancellationToken cancellationToken)
    {
        ArtworkRequests.AddRange(games.Select(g => g.Id));
        if (ArtworkGate is { } gate)
        {
            await gate.Task;
        }

        return games.Where(g => g.CoverUrls.Count > 0)
            .Select(g => g with { Artwork = g.Artwork.Or(new GameArtwork(@"C:\Coreloom\Artwork\" + g.StoreId + ".jpg", null, null)) })
            .ToList();
    }
}

public class GamingViewModelTests
{
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
    public void Opening_ListsGames_RecentFirst_WithStoreAndSize()
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
    }

    [Fact]
    public void SearchSuggestions_RankNamesStartingWithTheSearchFirst()
    {
        var (page, _, settings) = Create();
        page.Search = "w";
        // "The Witcher 3" (a word starting with W) and "Alan Wake 2" (a word too), alphabetical within the same rank
        Assert.Equal(["Alan Wake 2", "The Witcher 3"], page.SearchSuggestions.Select(s => s.Title));
        page.Search = "c";
        Assert.Equal("Cyberpunk 2077", page.SearchSuggestions[0].Title); // starts with it
        Assert.All(page.SearchSuggestions, s => Assert.Equal("Icon.Controller", s.IconKey));
        page.Search = string.Empty;
        Assert.Empty(page.SearchSuggestions);
        settings.Dispose();
    }

    [Fact]
    public void Sorting_Search_AndViewAreRemembered()
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
    }

    [Fact]
    public void PlayAndOpenFolder_GoThroughTheService()
    {
        var (page, fake, settings) = Create();
        page.Games[0].PlayCommand.Execute(null);
        page.Games[0].OpenFolderCommand.Execute(null);
        Assert.Equal(["steam:1091500"], fake.Launched);
        Assert.Equal([@"D:\Steam\steamapps\common\Cyberpunk 2077"], fake.Opened);
        settings.Dispose();
    }

    [Fact]
    public void MissingPictures_AreLookedUpAutomatically_ForEveryGameWithoutOne()
    {
        var (page, fake, settings) = Create();
        Assert.Equal(["epic:Dodo", "gog:1207658924"], fake.ArtworkRequests.Order()); // Cyberpunk 2077 has its own
        Assert.EndsWith("Dodo.jpg", page.Games.Single(g => g.Name == "Alan Wake 2").GridPicture);
        Assert.Equal(1, page.MissingArtworkCount); // nothing found for The Witcher 3: its initials stay
        Assert.False(page.IsDownloadingArtwork);
        Assert.False(settings.Current.Gaming.DownloadArtwork); // nothing was asked
        settings.Dispose();
    }

    [Fact]
    public async Task ASlowLookUp_ShowsItself_AndAQuickOneDoesNot()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (page, fake, settings) = Create(f => f.ArtworkGate = gate);
        Assert.False(page.IsDownloadingArtwork); // not at once: most look-ups come back right away
        await WaitFor(() => page.IsDownloadingArtwork);

        gate.SetResult();
        await WaitFor(() => !page.IsDownloadingArtwork);
        Assert.EndsWith("Dodo.jpg", page.Games.Single(g => g.Name == "Alan Wake 2").GridPicture);
        Assert.Equal(2, fake.ArtworkRequests.Count); // asked once
        settings.Dispose();
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        for (int i = 0; i < 500 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), "Timed out waiting for the condition.");
    }

    [Fact]
    public async Task Boosts_ShowState_AndBoostNowTurnsOnTheRest()
    {
        var (page, fake, settings) = Create();
        Assert.Equal(12, page.Boosts.Count);
        Assert.Equal(10, page.BoostsAvailable); // core scheduling and cache optimization have no cores to choose between on this test PC
        Assert.Equal(2, page.BoostsOn); // Game Mode and no background recording
        Assert.Equal("2/10", page.ReadinessText);
        Assert.Contains("8 boosts can make", page.HeroTitle);
        Assert.True(page.Boosts.Single(b => b.Tweak == GamingTweak.GameMode).IsOn);

        await page.ApplyAllCommand.ExecuteAsync(null);

        Assert.All(fake.Tweaks, t => Assert.True(t.Value.IsOn));
        Assert.False(page.CoreSchedulingBoost.IsOn); // not available here
        Assert.True(page.VramBoost.IsOn);
        Assert.True(settings.Current.Gaming.VramManagement);
        Assert.False(page.CacheOptimizationBoost.IsOn); // not available here either
        Assert.Equal(10, page.BoostsOn);
        Assert.True(page.AllBoostsOn);
        Assert.True(page.RestartPending);
        Assert.Equal("Restart Windows to finish.", page.Boosts.Single(b => b.Tweak == GamingTweak.GpuScheduling).Message);
        Assert.Contains("Turned on 8 boosts. Restart Windows to finish.", page.HeroMessage);
        Assert.Equal(PowerPlans.Balanced.ToString(), settings.Current.Gaming.PreviousPowerPlan); // saved to restore later
        Assert.False(page.ApplyAllCommand.CanExecute(null));
        settings.Dispose();
    }

    [Fact]
    public void NewBoosts_SayWhatTheyHelpWith_HowToUseThem_AndWhatTheyDont()
    {
        var (page, _, settings) = Create(f => f.FrameCap = 141);
        var gsync = page.Boosts.Single(b => b.Tweak == GamingTweak.GSync);
        var cache = page.Boosts.Single(b => b.Tweak == GamingTweak.ShaderCache);
        var cores = page.Boosts.Single(b => b.Tweak == GamingTweak.CoreScheduling);

        Assert.Equal("Smoothness", gsync.Category);
        Assert.True(gsync.IsSmoothness);
        Assert.Equal("Recommended: on for fullscreen and windowed games, with your 141 FPS limit for your 144 Hz display.", gsync.Recommendation);
        Assert.True(gsync.HasCaveat);
        Assert.Equal("Frame stability", cache.Category);
        Assert.False(cache.IsSmoothness);
        Assert.Contains("10 GB", cache.Recommendation);
        Assert.Equal("Frame stability", cores.Category);
        Assert.Equal("Intelligent CPU core scheduling", cores.Title);
        Assert.True(cores.CountsAsBoost);
        Assert.False(cores.IsSupported);
        Assert.Equal("The processor's cores couldn't be read here.", cores.Message);
        Assert.Contains("after a test", cores.Recommendation);
        Assert.True(cores.HasDetails);
        Assert.DoesNotContain(page.Boosts, b => b.Title.Contains("HDR", StringComparison.Ordinal));
        Assert.All(page.Boosts, b => Assert.True(b.HasCategory));
        Assert.Same(cache, page.ShaderCache); // its size is chosen under it (the Shader cache card is gone)
        Assert.True(cache.HasChoices);
        settings.Dispose();
    }

    [Fact]
    public void GSyncRecommendation_PointsToTheDriversMaxFrameRateWhenThereIsNoLimit()
    {
        var (page, _, settings) = Create();
        Assert.Equal("Recommended: on for fullscreen and windowed games, with Max Frame Rate in NVIDIA's settings at about 138 FPS for your 144 Hz display.",
            page.Boosts.Single(b => b.Tweak == GamingTweak.GSync).Recommendation);
        settings.Dispose();
    }

    [Fact]
    public async Task ShaderCache_SetsTheChosenSize_AndANewSizeAppliesAtOnce()
    {
        var (page, fake, settings) = Create();
        Assert.Equal(ShaderCacheSize.Recommended, page.ShaderCacheSize);
        Assert.Equal(["10 GB", "100 GB", "Unlimited"], page.ShaderCacheSizes.Select(o => o.Label));
        Assert.Equal("Limit now: the driver's default", page.ShaderCacheLimitText);

        page.ShaderCache.IsOn = true;
        await Task.Yield();
        Assert.Equal(ShaderCacheSize.Recommended, fake.ShaderCacheLimit);
        Assert.Equal("Limit now: 10 GB", page.ShaderCacheLimitText);

        page.ShaderCacheSize = ShaderCacheSize.Large;
        await Task.Yield();
        Assert.Equal(ShaderCacheSize.Large, settings.Current.Gaming.ShaderCacheSize);
        Assert.Equal(ShaderCacheSize.Large, fake.ShaderCacheLimit);
        Assert.Equal("Limit now: 100 GB", page.ShaderCacheLimitText);

        page.ShaderCache.IsOn = false;
        await Task.Yield();
        Assert.Null(fake.ShaderCacheLimit);
        page.ShaderCacheSize = ShaderCacheSize.Unlimited; // off: only remembered for next time
        await Task.Yield();
        Assert.Null(fake.ShaderCacheLimit);
        Assert.Equal(ShaderCacheSize.Unlimited, settings.Current.Gaming.ShaderCacheSize);
        settings.Dispose();
    }

    [Fact]
    public void ABoostsHint_IsShownWhenItCanBeUsed()
    {
        var (page, _, settings) = Create(f => f.Tweaks[GamingTweak.GSync] = new(true, false, Note: "Turn on G-SYNC for your display in the NVIDIA Control Panel too."));
        var gsync = page.Boosts.Single(b => b.Tweak == GamingTweak.GSync);
        Assert.True(gsync.CanToggle);
        Assert.StartsWith("Turn on G-SYNC", gsync.Message);
        settings.Dispose();
    }

    [Fact]
    public async Task TurningABoostOff_UsesTheSwitch()
    {
        var (page, fake, settings) = Create();
        var gameMode = page.Boosts.Single(b => b.Tweak == GamingTweak.GameMode);
        gameMode.IsOn = false;
        await Task.Yield();
        Assert.False(fake.Tweaks[GamingTweak.GameMode].IsOn);
        Assert.False(gameMode.IsOn);
        Assert.Equal("Off", gameMode.StatusText);
        settings.Dispose();
    }

    [Fact]
    public async Task ARefusedChange_FlipsTheSwitchBack_AndSaysWhy()
    {
        var (page, fake, settings) = Create(f => f.Refuse = GamingTweak.UltimatePower);
        var power = page.Boosts.Single(b => b.Tweak == GamingTweak.UltimatePower);
        power.IsOn = true;
        await Task.Yield();
        Assert.False(fake.Tweaks[GamingTweak.UltimatePower].IsOn);
        Assert.False(power.IsOn);
        Assert.False(power.IsBusy);
        Assert.Equal("Windows didn't allow the change.", power.Message);
        settings.Dispose();
    }

    [Fact]
    public async Task AGameAddedByHand_JoinsTheLibrary_AndCanBeChangedAndRemoved()
    {
        var (page, fake, settings) = Create();
        fake.NextProgram = null;
        page.AddGameCommand.Execute(null);
        Assert.False(page.Editor.IsOpen); // the pick was cancelled

        fake.NextProgram = @"D:\Games\Celeste\Celeste.exe";
        page.AddGameCommand.Execute(null);
        var editor = page.Editor;
        Assert.True(editor.IsOpen);
        Assert.True(editor.IsNew);
        Assert.Equal("Add a game", editor.Title);
        Assert.Equal("Celeste", editor.Name); // from the program's product name
        Assert.Equal(@"D:\Games\Celeste\Celeste.exe.png", editor.Preview); // the program's icon
        Assert.Null(editor.Error);

        editor.Arguments = " -windowed ";
        await editor.SaveCommand.ExecuteAsync(null);
        Assert.False(editor.IsOpen);
        var saved = Assert.Single(settings.Current.Gaming.CustomGames);
        Assert.StartsWith("custom:", saved.Id);
        Assert.Equal("-windowed", saved.Arguments);
        var game = page.Games.Single(g => g.Name == "Celeste");
        Assert.True(game.IsCustom);
        Assert.Equal("Added by you", game.StoreName);
        Assert.True(game.HasIconOnly);
        Assert.Equal("Celeste is in your games.", page.HeroMessage);

        // Play starts its program. (A limit the frame limiter set for it before 1.8.4 is removed with the game.)
        game.PlayCommand.Execute(null);
        Assert.Equal([saved.Id], fake.Launched);
        settings.Update("Gaming", s => s.Gaming.GameFrameCaps.Add(new GameFrameCap { GameId = saved.Id, Name = "Celeste", Fps = 120, Executables = ["Celeste.exe"] }));
        fake.ProgramCaps["Celeste.exe"] = 120;

        // Change it.
        game.EditCommand.Execute(null);
        Assert.True(editor.IsOpen);
        Assert.False(editor.IsNew);
        editor.Name = "Celeste (speedrun)";
        fake.NextPicture = @"C:\Pictures\celeste.png";
        editor.ChoosePictureCommand.Execute(null);
        Assert.True(editor.HasChosenPicture);
        await editor.SaveCommand.ExecuteAsync(null);
        Assert.Equal(saved.Id, Assert.Single(settings.Current.Gaming.CustomGames).Id); // same game, same id
        var renamed = page.Games.Single(g => g.IsCustom);
        Assert.Equal("Celeste (speedrun)", renamed.Name);
        Assert.Equal(@"C:\Pictures\celeste.png", renamed.GridPicture);

        // Remove it: its own limit goes too.
        renamed.EditCommand.Execute(null);
        await editor.RemoveCommand.ExecuteAsync(null);
        Assert.Empty(settings.Current.Gaming.CustomGames);
        Assert.Empty(settings.Current.Gaming.GameFrameCaps);
        Assert.Empty(fake.ProgramCaps);
        Assert.DoesNotContain(page.Games, g => g.IsCustom);
        settings.Dispose();
    }

    [Fact]
    public async Task AddingAGame_ChecksTheNameAndTheProgram()
    {
        var (page, fake, settings) = Create();
        fake.NextProgram = @"C:\Users\jerry\Desktop\tiny_game.exe";
        page.AddGameCommand.Execute(null);
        var editor = page.Editor;
        Assert.Equal("Tiny Game", editor.Name); // from the file name, when the program has no product name
        editor.Name = "  ";
        await editor.SaveCommand.ExecuteAsync(null);
        Assert.Equal("Give the game a name.", editor.Error);
        Assert.True(editor.IsOpen);

        fake.NextProgram = @"C:\Gone\missing.exe";
        editor.ChangeProgramCommand.Execute(null);
        Assert.Equal("That program can't be found. Pick it again.", editor.Error);
        Assert.Equal("Missing", editor.Name); // the name followed the program, since it was empty
        await editor.SaveCommand.ExecuteAsync(null);
        Assert.Empty(settings.Current.Gaming.CustomGames);

        editor.CancelCommand.Execute(null);
        Assert.False(editor.IsOpen);
        settings.Dispose();
    }

    [Fact]
    public void TheDriversAllGamesLimit_IsRememberedForTheOverlay()
    {
        var (_, _, settings) = Create(f => f.FrameCap = 141); // set elsewhere (the NVIDIA Control Panel)
        Assert.Equal(141, settings.Current.Gaming.GlobalFrameCap);
        Assert.Equal(141, settings.Current.Gaming.ActiveFrameCap("game.exe"));
        settings.Dispose();
    }

    [Fact]
    public void RunningGame_IsMarked_AndItsFrameRateMeasured()
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
        Assert.True(page.HasLive);
        Assert.Equal("138", page.LiveFps);

        page.Deactivate();
        Assert.False(fake.Watching);
        Assert.False(page.HasLive);
        settings.Dispose();
    }

    [Fact]
    public void ShaderCaches_AreSummarised_AndADriverChangeIsPointedOut()
    {
        var (page, _, settings) = Create(f => f.Driver = "581.57");
        Assert.Equal(2, page.ShaderCaches.Count); // the empty AMD folder is left out
        Assert.Equal("NVIDIA DirectX", page.ShaderCaches[0].Name);
        Assert.Contains("in 2 caches", page.ShaderCacheSummary);
        Assert.Contains("in 2 caches", page.KeepShaderCachesBoost.Recommendation); // the card's sizes, condensed into the boost
        Assert.Contains("NVIDIA DirectX: 3", page.KeepShaderCachesBoost.RecommendationToolTip);
        Assert.Equal("581.57", settings.Current.Gaming.LastDriverVersion); // first look: remembered silently
        Assert.False(page.HasDriverNotice);
        settings.Dispose();

        var (changed, _, settings2) = Create(f => f.Driver = "582.10");
        settings2.Current.Gaming.LastDriverVersion = "581.57";
        changed.RefreshCommand.Execute(null);
        Assert.True(changed.HasDriverNotice);
        Assert.Contains("from 581.57 to 582.10", changed.DriverNotice);
        Assert.Contains("from 581.57 to 582.10", changed.KeepShaderCachesBoost.Message); // shown under the boost
        changed.DismissDriverNoticeCommand.Execute(null);
        Assert.False(changed.HasDriverNotice);
        Assert.Null(changed.KeepShaderCachesBoost.Message);
        Assert.Equal("582.10", settings2.Current.Gaming.LastDriverVersion);
        settings2.Dispose();
    }

    [Fact]
    public async Task WhatASwitchDid_LeavesAfterFiveSeconds_AndItsStatusStays()
    {
        using var timer = new ManualDismissals();
        var (page, fake, settings) = Create(f => f.Refuse = GamingTweak.UltimatePower);
        var power = page.Boosts.Single(b => b.Tweak == GamingTweak.UltimatePower);
        string? note = power.Message;
        power.IsOn = true;
        await Task.Yield();
        Assert.Equal("Windows didn't allow the change.", power.Message);
        timer.Elapse();
        Assert.Equal(note, power.Message);

        await page.ApplyAllCommand.ExecuteAsync(null);
        var gpu = page.Boosts.Single(b => b.Tweak == GamingTweak.GpuScheduling);
        Assert.Equal("Restart Windows to finish.", gpu.Message); // still there after the other switches' refreshes
        Assert.NotNull(page.HeroMessage);
        timer.Elapse();
        Assert.Null(page.HeroMessage);
        Assert.Null(gpu.Message);
        Assert.Equal("Restart to finish", gpu.StatusText); // the state stays
        Assert.True(page.RestartPending);
        settings.Dispose();
    }

}
