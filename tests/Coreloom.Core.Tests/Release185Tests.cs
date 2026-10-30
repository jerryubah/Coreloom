using Coreloom.Core.Gaming;

namespace Coreloom.Core.Tests;

/// <summary>1.8.5: CPU cache optimization's rules (which processors it's for, the game's cores, which apps it moves).</summary>
public class Release185Tests
{
    private const long MB = 1024 * 1024;

    private static CoreLayout Cores(params (ulong Mask, long L3)[] caches)
    {
        var cores = new List<CoreUnit>();
        foreach (var (mask, _) in caches)
        {
            for (int bit = 0; bit < 64; bit += 2)
            {
                if ((mask & (3UL << bit)) != 0)
                {
                    cores.Add(new CoreUnit(0, 3UL << bit, 0));
                }
            }
        }

        return CoreLayout.Build(cores, [.. caches.Select(c => new CacheUnit(3, c.L3, 0, c.Mask))]);
    }

    private static CoreLayout X3D => Cores((0xFFFF, 96 * MB), (0xFFFF_0000, 32 * MB));

    private static CoreLayout TwoCcds => Cores((0xFFFF, 32 * MB), (0xFFFF_0000, 32 * MB));

    private static CoreLayout OneCcd => Cores((0xFFFF, 32 * MB));

    [Fact]
    public void TheLayout_CountsItsL3Caches_AndCacheOptimizationIsForMoreThanOne()
    {
        Assert.Equal(2, X3D.L3Caches);
        Assert.Equal(2, TwoCcds.L3Caches);
        Assert.Equal(1, OneCcd.L3Caches);
        Assert.Equal(2, CoreLayout.Parse(Topology.Ryzen9950X3D()).L3Caches);

        Assert.True(CacheLocality.Applies(X3D));
        Assert.True(CacheLocality.Applies(TwoCcds));
        Assert.False(CacheLocality.Applies(OneCcd));
        Assert.False(CacheLocality.Applies(CoreLayout.Unknown));
        Assert.Equal("Every core here shares one L3 cache, so there's nothing to keep apart.", CacheLocality.UnsupportedText(OneCcd));
        Assert.Equal("The processor's caches couldn't be read here.", CacheLocality.UnsupportedText(CoreLayout.Unknown));

        // A hybrid Intel whose cores share one L3: P and E cores, but nothing to keep apart.
        var hybrid = CoreLayout.Build(
            [new CoreUnit(0, 0b11, 1), new CoreUnit(0, 0b1100, 1), new CoreUnit(0, 0b1_0000, 0), new CoreUnit(0, 0b10_0000, 0)],
            [new CacheUnit(3, 36 * MB, 0, 0b11_1111)]);
        Assert.True(hybrid.IsHybrid);
        Assert.False(CacheLocality.Applies(hybrid));
    }

    [Fact]
    public void TheGamesCores_ComeFromItsProfile_ThenTheVCache_ThenWhereItRuns()
    {
        var x3d = X3D;
        var cache = x3d.Group(CoreKind.Cache)!;
        var frequency = x3d.Group(CoreKind.Frequency)!;

        // A core profile wins (core scheduling applies it).
        var pinned = CacheLocality.Target(x3d, frequency, null, amdParks: false, "Cyberpunk 2077");
        Assert.Same(frequency, pinned.Group);
        Assert.Contains("core profile", pinned.Text);

        // AMD's driver parking the other CCD: left to AMD.
        var amd = CacheLocality.Target(x3d, null, null, amdParks: true, "Cyberpunk 2077");
        Assert.Null(amd.Group);
        Assert.StartsWith("AMD's driver keeps games on the cache cores", amd.Text);

        // Otherwise a Ryzen X3D's game gets the 3D V-Cache to itself.
        var vcache = CacheLocality.Target(x3d, null, null, amdParks: false, "Cyberpunk 2077");
        Assert.Same(cache, vcache.Group);
        Assert.Equal("Cyberpunk 2077 gets the 3D V-Cache to itself.", vcache.Text);
        Assert.Equal(0xFFFF_0000UL, CacheLocality.BackgroundMask(x3d, cache));

        // Two equal CCDs: wait for readings, then the busiest one.
        var two = TwoCcds;
        var waiting = CacheLocality.Target(two, null, null, amdParks: false, "Elden Ring");
        Assert.Null(waiting.Group);
        Assert.Equal("Finding where Elden Ring runs…", waiting.Text);
        var busiest = CacheLocality.Target(two, null, [0.3, 0.7], amdParks: false, "Elden Ring");
        Assert.Same(two.Groups[1], busiest.Group);
        Assert.Equal("CCD 2", busiest.Group!.Name);
        Assert.StartsWith("Elden Ring runs mostly on CCD 2 (70", busiest.Text);
        Assert.Equal(0xFFFFUL, CacheLocality.BackgroundMask(two, busiest.Group));
    }

    [Fact]
    public void ItMovesListedBackgroundApps_NeverTheGame_OrAppsWhoseCoresWereChosen()
    {
        Assert.True(CacheLocality.IsBackgroundApp(@"C:\Program Files\Google\Chrome\Application\chrome.exe"));
        Assert.True(CacheLocality.IsBackgroundApp("Discord.exe"));
        Assert.True(CacheLocality.IsBackgroundApp("steamwebhelper.exe"));
        Assert.False(CacheLocality.IsBackgroundApp(@"C:\Windows\System32\svchost.exe"));
        Assert.False(CacheLocality.IsBackgroundApp(@"D:\Steam\steamapps\common\Cyberpunk 2077\bin\x64\Cyberpunk2077.exe"));
        Assert.False(CacheLocality.IsBackgroundApp(null));
        Assert.All(CacheLocality.BackgroundApps, app => Assert.EndsWith(".exe", app));

        const ulong all = 0xFFFF_FFFF;
        const ulong background = 0xFFFF_0000;
        var affinity = new Dictionary<int, ProcessAffinity>
        {
            [1] = new(all, all),               // Chrome on every core: moved
            [2] = new(all, all),               // Discord: moved
            [3] = new(0xFF, all),              // Chrome whose cores were chosen by hand: left alone
            [4] = new(background, all),        // a new Chrome tab that inherited the moved cores: picked, so it gets them back
            [5] = new(all, all),               // Notepad: not on the list
            [6] = new(all, all),               // Spotify, already moved
            [7] = new(all, all),               // the game, even if it were named like a listed app
        };
        RunningProcess[] running =
        [
            new(1, @"C:\Chrome\chrome.exe"), new(2, "discord.exe"), new(3, @"C:\Chrome\chrome.exe"), new(4, @"C:\Chrome\chrome.exe"),
            new(5, "notepad.exe"), new(6, "Spotify.exe"), new(7, "chrome.exe"), new(8, "slack.exe"), // 8 ended: no affinity
        ];
        var picked = CacheLocality.ToMove(running, [7], id => affinity.TryGetValue(id, out var a) ? a : null, [6], background);
        Assert.Equal([1, 2, 4], picked.Select(p => p.Id));

        // Without the background mask, an inherited one looks chosen by hand.
        Assert.Equal([1, 2], CacheLocality.ToMove(running, [7], id => affinity.TryGetValue(id, out var a) ? a : null, [6]).Select(p => p.Id));
        Assert.Equal("1 background app", CacheLocality.Apps(1));
        Assert.Equal("3 background apps", CacheLocality.Apps(3));
    }

    [Fact]
    public void TheNewBoostsSetting_IsKeptInTheSettingsFile()
    {
        var settings = new Coreloom.Core.Settings.GamingSettings();
        Assert.False(settings.CacheOptimization); // off until the user turns it on
        settings.CacheOptimization = true;
        var back = System.Text.Json.JsonSerializer.Deserialize<Coreloom.Core.Settings.GamingSettings>(System.Text.Json.JsonSerializer.Serialize(settings))!;
        Assert.True(back.CacheOptimization);
    }
}
