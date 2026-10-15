using System.Text;
using Coreloom.Core.Formatting;
using Coreloom.Core.Gaming;
using Coreloom.Core.Settings;

namespace Coreloom.Core.Tests;

public sealed class KeyValuesTests
{
    [Fact]
    public void Parses_nested_values_escapes_comments_and_conditions()
    {
        const string text = """
            // written by Steam
            "libraryfolders"
            {
                "0"
                {
                    "path"      "C:\\Program Files (x86)\\Steam"
                    "label"     ""
                    "apps"
                    {
                        "730"   "34000000000"
                    }
                }
                "1"   "D:\\SteamLibrary"   [$WIN32]
                "Quote"  "say \"hi\""
            }
            """;

        var root = KeyValues.Parse(text)!;
        Assert.Equal("libraryfolders", root.Key);
        Assert.Equal(@"C:\Program Files (x86)\Steam", root["0"]!.Get("PATH"));
        Assert.Equal(string.Empty, root["0"]!.Get("label"));
        Assert.Equal(34000000000, root["0"]!["apps"]!.GetLong("730"));
        Assert.Equal(@"D:\SteamLibrary", root.Get("1"));
        Assert.Equal("say \"hi\"", root.Get("quote"));
        Assert.Null(root.Get("missing"));
    }

    [Fact]
    public void Empty_or_broken_text_gives_nothing_or_what_it_could_read()
    {
        Assert.Null(KeyValues.Parse(string.Empty));
        var partial = KeyValues.Parse("\"AppState\" { \"appid\" \"10\" ");
        Assert.Equal("10", partial!.Get("appid"));
    }
}

public sealed class GameStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "coreloom-games-" + Guid.NewGuid().ToString("N"));

    public GameStoreTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static string Manifest(string id, string name, string flags = "4", string lastPlayed = "1760000000", string size = "1048576") => $$"""
        "AppState"
        {
            "appid"        "{{id}}"
            "name"         "{{name}}"
            "StateFlags"   "{{flags}}"
            "installdir"   "{{name}}"
            "LastPlayed"   "{{lastPlayed}}"
            "SizeOnDisk"   "{{size}}"
        }
        """;

    [Fact]
    public void Steam_library_folders_include_steam_itself_once_in_old_and_new_formats()
    {
        const string vdf = """
            "libraryfolders"
            {
                "contentstatsid" "123"
                "0" { "path" "C:\\Program Files (x86)\\Steam" }
                "1" { "path" "E:\\Games\\SteamLibrary" }
                "2" "F:/Steam"
            }
            """;

        var folders = SteamLibrary.LibraryFolders("c:/program files (x86)/steam", vdf);
        Assert.Equal([@"c:\program files (x86)\steam", @"E:\Games\SteamLibrary", @"F:\Steam"], folders);
        Assert.Single(SteamLibrary.LibraryFolders(@"C:\Steam", null));
    }

    [Fact]
    public void Steam_manifest_becomes_a_game_with_launch_link_size_and_last_played()
    {
        var game = SteamLibrary.FromManifest(Manifest("1091500", "Cyberpunk 2077"), @"D:\SteamLibrary")!;
        Assert.Equal("steam:1091500", game.Id);
        Assert.Equal("1091500", game.StoreId);
        Assert.Equal(GameStore.Steam, game.Store);
        Assert.Equal("steam://rungameid/1091500", game.Launch);
        Assert.Equal(1048576, game.SizeBytes);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1760000000), game.LastPlayed);
        Assert.EndsWith("Cyberpunk 2077", game.InstallPath);
        Assert.Contains("steamapps", game.InstallPath);
        Assert.Contains(game.CoverUrls, u => u.Contains("/1091500/library_600x900"));
    }

    [Fact]
    public void Steam_tools_and_unfinished_installs_are_left_out()
    {
        Assert.Null(SteamLibrary.FromManifest(Manifest("228980", "Steamworks Common Redistributables"), "C:"));
        Assert.Null(SteamLibrary.FromManifest(Manifest("2805730", "Proton 9.0"), "C:"));
        Assert.Null(SteamLibrary.FromManifest(Manifest("1", "Valheim Dedicated Server"), "C:"));
        Assert.Null(SteamLibrary.FromManifest(Manifest("570", "Dota 2", flags: "1026"), "C:")); // being installed
        Assert.Null(SteamLibrary.FromManifest("garbage", "C:"));
        var neverPlayed = SteamLibrary.FromManifest(Manifest("570", "Dota 2", lastPlayed: "0", size: "0"), "C:")!;
        Assert.Null(neverPlayed.LastPlayed);
        Assert.Null(neverPlayed.SizeBytes);
    }

    [Fact]
    public void Steam_artwork_is_found_in_old_and_new_cache_layouts()
    {
        string cache = Path.Combine(_root, "librarycache");
        Directory.CreateDirectory(Path.Combine(cache, "730", "f00d"));
        File.WriteAllBytes(Path.Combine(cache, "440_library_600x900.jpg"), [1]);
        File.WriteAllBytes(Path.Combine(cache, "440_header.jpg"), [1]);
        File.WriteAllBytes(Path.Combine(cache, "730", "f00d", "library_600x900.jpg"), [1]);
        File.WriteAllBytes(Path.Combine(cache, "730", "header.jpg"), [1]);
        File.WriteAllBytes(Path.Combine(cache, "730", new string('a', 40) + ".jpg"), [1]);

        var old = SteamLibrary.FindArtwork(cache, "440");
        Assert.EndsWith("440_library_600x900.jpg", old.Cover);
        Assert.EndsWith("440_header.jpg", old.Banner);

        var current = SteamLibrary.FindArtwork(cache, "730");
        Assert.EndsWith("library_600x900.jpg", current.Cover);
        Assert.EndsWith("header.jpg", current.Banner);
        Assert.EndsWith(new string('a', 40) + ".jpg", current.Icon);

        Assert.False(SteamLibrary.FindArtwork(cache, "999").HasAny);
        Assert.False(SteamLibrary.FindArtwork(Path.Combine(_root, "missing"), "730").HasAny);
    }

    [Fact]
    public void Epic_manifest_becomes_a_game_and_dlc_engines_and_unfinished_installs_dont()
    {
        const string game = """
            { "DisplayName": "Alan Wake 2", "AppName": "Dodo", "InstallLocation": "D:\\Epic\\AlanWake2", "LaunchExecutable": "AlanWake2.exe",
              "CatalogNamespace": "dc9d2e595d0e4650b35d659f90d41059", "CatalogItemId": "4b8a5d1ccd4f4b1f8b0f9a0b2c9f3d1e", "InstallSize": 90000000000,
              "AppCategories": ["public", "games", "applications"], "MainGameAppName": "Dodo", "bIsIncompleteInstall": false }
            """;
        var entry = EpicLibrary.FromManifest(game)!;
        Assert.Equal("epic:Dodo", entry.Id);
        Assert.Equal("Alan Wake 2", entry.Name);
        Assert.Equal(90000000000, entry.SizeBytes);
        Assert.Equal("com.epicgames.launcher://apps/dc9d2e595d0e4650b35d659f90d41059%3A4b8a5d1ccd4f4b1f8b0f9a0b2c9f3d1e%3ADodo?action=launch&silent=true", entry.Launch);
        Assert.EndsWith("AlanWake2.exe", entry.Executable);
        Assert.Equal("4b8a5d1ccd4f4b1f8b0f9a0b2c9f3d1e", entry.CatalogId);

        Assert.Null(EpicLibrary.FromManifest(game.Replace("\"MainGameAppName\": \"Dodo\"", "\"MainGameAppName\": \"Other\"")));
        Assert.Null(EpicLibrary.FromManifest(game.Replace("\"games\", ", string.Empty)));
        Assert.Null(EpicLibrary.FromManifest(game.Replace("\"bIsIncompleteInstall\": false", "\"bIsIncompleteInstall\": true")));
        Assert.Null(EpicLibrary.FromManifest("{ not json"));
    }

    [Fact]
    public void Epic_catalog_cache_gives_tall_covers_first()
    {
        const string json = """
            [ { "id": "abc", "keyImages": [ { "type": "DieselGameBox", "url": "https://cdn1.epicgames.com/wide.jpg" },
                                            { "type": "DieselGameBoxTall", "url": "https://cdn1.epicgames.com/tall.jpg" },
                                            { "type": "Thumbnail", "url": "http://insecure.example/thumb.jpg" } ] },
              { "id": "none" } ]
            """;
        var covers = EpicLibrary.CatalogCovers(Convert.ToBase64String(Encoding.UTF8.GetBytes(json)));
        Assert.Equal(["https://cdn1.epicgames.com/tall.jpg", "https://cdn1.epicgames.com/wide.jpg"], covers["ABC"]);
        Assert.False(covers.ContainsKey("none"));
        Assert.Empty(EpicLibrary.CatalogCovers("not base64 and not json"));
    }

    [Fact]
    public void Gog_primary_play_task_is_the_game_program()
    {
        const string info = """
            { "gameId": "1207658924", "name": "The Witcher 3",
              "playTasks": [ { "category": "tool", "path": "redkit.exe", "type": "FileTask" },
                             { "isPrimary": true, "path": "bin\\x64\\witcher3.exe", "type": "FileTask" } ] }
            """;
        Assert.Equal(@"bin\x64\witcher3.exe", GogLibrary.PrimaryExecutable(info));
        Assert.Equal("game.exe", GogLibrary.PrimaryExecutable("""{ "playTasks": [ { "path": "game.exe", "type": "FileTask" } ] }"""));
        Assert.Null(GogLibrary.PrimaryExecutable("{}"));
    }

    [Fact]
    public void Xbox_config_and_package_family_name()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <Game configVersion="1">
              <Identity Name="Microsoft.624F8B84B80" Publisher="CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US" Version="1.0.0.0" />
              <ExecutableList><Executable Name="ForzaHorizon5.exe" Id="Game" TargetDeviceFamily="PC" /></ExecutableList>
              <ShellVisuals DefaultDisplayName="Forza Horizon 5" StoreLogo="StoreLogo.png" Square150x150Logo="Logo.png" SplashScreenImage="SplashScreen.png" />
            </Game>
            """;
        var config = XboxLibrary.ParseConfig(xml)!;
        Assert.Equal("Forza Horizon 5", config.DisplayName);
        Assert.Equal("Game", config.ExecutableId);
        Assert.Equal("ForzaHorizon5.exe", config.Executable);
        Assert.Equal("SplashScreen.png", config.SplashScreen);
        Assert.Equal("Logo.png", config.Square150Logo);
        Assert.Equal("8wekyb3d8bbwe", XboxLibrary.PublisherId(config.Publisher)); // Microsoft's well-known publisher id
        Assert.Equal("Microsoft.624F8B84B80_8wekyb3d8bbwe", XboxLibrary.FamilyName(config.IdentityName, config.Publisher));
        Assert.Null(XboxLibrary.ParseConfig("<Game/>"));
        Assert.Null(XboxLibrary.ParseConfig("<broken"));
    }

    [Fact]
    public void Launcher_games_are_recognised_from_installed_programs()
    {
        var ubisoft = LauncherGames.FromUninstall(new UninstallEntry("Uplay Install 635", "Assassin's Creed Odyssey", "Ubisoft", "\"C:\\Ubisoft\\Uninstall.exe\" uplay://uninstall/635", @"D:\Ubisoft\ACOdyssey\", "D:\\Ubisoft\\ACOdyssey\\ACOdyssey.exe,0"))!;
        Assert.Equal("ubisoft:635", ubisoft.Id);
        Assert.Equal("uplay://launch/635/0", ubisoft.Launch);
        Assert.Equal(@"D:\Ubisoft\ACOdyssey\ACOdyssey.exe", ubisoft.Executable);

        var riot = LauncherGames.FromUninstall(new UninstallEntry("Riot Game valorant.live", "VALORANT", "Riot Games, Inc",
            "\"C:\\Riot Games\\Riot Client\\RiotClientServices.exe\" --uninstall-product=valorant --uninstall-patchline=live", @"C:\Riot Games\VALORANT\live", null))!;
        Assert.Equal(GameStore.Riot, riot.Store);
        Assert.Equal("\"C:\\Riot Games\\Riot Client\\RiotClientServices.exe\" --launch-product=valorant --launch-patchline=live", riot.Launch);

        var blizzard = LauncherGames.FromUninstall(new UninstallEntry("Overwatch", "Overwatch", "Blizzard Entertainment",
            "\"C:\\ProgramData\\Battle.net\\Agent\\Blizzard Uninstaller.exe\" --lang=enUS --uid=prometheus", @"C:\Games\Overwatch", "C:\\Games\\Overwatch\\Overwatch Launcher.exe") { EstimatedSizeKb = 1024 })!;
        Assert.Equal("battlenet:prometheus", blizzard.Id);
        Assert.Equal(1024 * 1024, blizzard.SizeBytes);
        Assert.Null(LauncherGames.FromUninstall(new UninstallEntry("Battle.net", "Battle.net", "Blizzard Entertainment", "\"C:\\Blizzard Uninstaller.exe\" --uid=battle.net", null, null)));

        var ea = LauncherGames.FromUninstall(new UninstallEntry("{1234}", "Battlefield 2042", "Electronic Arts", "\"C:\\Program Files\\Common Files\\EAInstaller\\Battlefield 2042\\Cleanup.exe\" uninstall_game", @"D:\EA Games\Battlefield 2042", "D:\\EA Games\\Battlefield 2042\\BF2042.exe"))!;
        Assert.Equal(GameStore.EA, ea.Store);
        Assert.Null(LauncherGames.FromUninstall(new UninstallEntry("EA app", "EA app", "Electronic Arts", "EAInstaller", @"C:\EA", null)));

        Assert.Null(LauncherGames.FromUninstall(new UninstallEntry("7-Zip", "7-Zip", "Igor Pavlov", "uninstall.exe", @"C:\7-Zip", null)));
        Assert.Null(LauncherGames.IconPath("C:\\x.dll,3"));
        Assert.Equal(@"C:\Game\icon.ico", LauncherGames.IconPath("\"C:\\Game\\icon.ico\""));
    }
}

public sealed class GameLibraryTests
{
    private static GameEntry Game(string id, string name, string? path = null, long? size = null, int? playedDaysAgo = null) =>
        new(id, name, GameStore.Steam)
        {
            InstallPath = path,
            SizeBytes = size,
            LastPlayed = playedDaysAgo is int d ? new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero).AddDays(-d) : null,
        };

    [Fact]
    public void Merge_drops_duplicates_by_id_and_folder_and_empty_names()
    {
        var merged = GameLibrary.Merge([
            Game("steam:1", " Hades ", @"C:\Games\Hades"),
            Game("steam:1", "Hades again"),
            Game("gog:2", "Hades (GOG)", @"c:\games\hades\"),
            Game("epic:3", "  "),
            Game("epic:4", "Celeste", @"D:\Celeste"),
        ]);
        Assert.Equal(["Hades", "Celeste"], merged.Select(g => g.Name));
    }

    [Fact]
    public void Sorting_by_recent_name_and_size()
    {
        var games = new[] { Game("a", "The Witcher 3", size: 50, playedDaysAgo: 3), Game("b", "Alan Wake 2", size: 90), Game("c", "Hades", size: 10, playedDaysAgo: 1) };
        Assert.Equal(["Hades", "The Witcher 3", "Alan Wake 2"], GameLibrary.Sort(games, GameSort.RecentlyPlayed).Select(g => g.Name));
        Assert.Equal(["Alan Wake 2", "Hades", "The Witcher 3"], GameLibrary.Sort(games, GameSort.Name).Select(g => g.Name));
        Assert.Equal(["Alan Wake 2", "The Witcher 3", "Hades"], GameLibrary.Sort(games, GameSort.Size).Select(g => g.Name));
    }

    [Fact]
    public void Search_matches_words_in_any_order_and_the_store()
    {
        var game = Game("steam:1", "Red Dead Redemption 2");
        Assert.True(GameLibrary.Matches(game, "redemption red"));
        Assert.True(GameLibrary.Matches(game, "steam"));
        Assert.True(GameLibrary.Matches(game, "  "));
        Assert.False(GameLibrary.Matches(game, "red epic"));
    }

    [Theory]
    [InlineData("Counter-Strike 2", "CS")]
    [InlineData("Red Dead Redemption 2", "RD")]
    [InlineData("The Witcher 3: Wild Hunt", "WW")]
    [InlineData("DOOM", "D")]
    [InlineData("1000xRESIST", "1")]
    [InlineData("", "?")]
    public void Initials_for_games_without_pictures(string name, string expected) => Assert.Equal(expected, GameLibrary.Initials(name));

    [Fact]
    public void Running_game_is_found_by_its_folder_but_not_by_launchers_or_similar_folders()
    {
        var games = new[] { Game("steam:1", "Foo", @"C:\Games\Foo"), Game("steam:2", "Foo DLC", @"C:\Games\Foo\Expansion"), Game("steam:3", "Bar", @"C:\") };
        Assert.Null(GameLibrary.FindRunning(games, [new(1, @"C:\Games\Foo2\foo2.exe"), new(2, @"C:\Games\Foo\EasyAntiCheat.exe"), new(3, @"C:\Windows\explorer.exe")]));
        var foo = GameLibrary.FindRunning(games, [new(7, @"c:\games\foo\bin\foo.exe"), new(8, @"C:\Games\Foo\Expansion\x.exe"), new(9, @"C:\Games\Foo\helper.exe")])!;
        Assert.Equal("steam:1", foo.Game.Id);
        Assert.Equal([7, 9], foo.ProcessIds);
        Assert.Equal("steam:2", GameLibrary.FindRunning(games, [new(4, @"C:\Games\Foo\Expansion\x.exe")])!.Game.Id);
    }

    [Fact]
    public void Last_played_texts()
    {
        var now = new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal("Not played yet", GameLibrary.LastPlayedText(null, now));
        Assert.Equal("Played today", GameLibrary.LastPlayedText(now.AddHours(-1), now));
        Assert.Equal("Played yesterday", GameLibrary.LastPlayedText(now.AddDays(-1), now));
        Assert.Equal("Played 5 days ago", GameLibrary.LastPlayedText(now.AddDays(-5), now));
        Assert.StartsWith("Played ", GameLibrary.LastPlayedText(now.AddDays(-400), now));
    }
}

public sealed class GamingTweakTests
{
    [Fact]
    public void Power_plans_are_read_from_powercfg_in_any_language()
    {
        const string english = """
            Existing Power Schemes (* Active)
            -----------------------------------
            Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Balanced)
            Power Scheme GUID: 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c  (High performance) *
            Power Scheme GUID: 4f8a1b2c-0000-4000-8000-123456789abc  (Ultimate Performance)
            """;
        var plans = PowerPlans.Parse(english.Replace("\n", "\r\n"));
        Assert.Equal(3, plans.Count);
        Assert.Equal(PowerPlans.HighPerformance, plans.Single(p => p.IsActive).Id);
        Assert.Equal("High performance", plans[1].Name);
        Assert.Equal(new Guid("4f8a1b2c-0000-4000-8000-123456789abc"), PowerPlans.FindUltimate(plans, null)!.Id);

        const string german = "GUID des Energieschemas: 381b4222-f694-41f0-9685-ff5bb260df2e  (Ausbalanciert) *";
        Assert.True(PowerPlans.Parse(german).Single().IsActive);

        var remembered = new Guid("11111111-2222-3333-4444-555555555555");
        var withOwn = new List<PowerPlan>(plans) { new(remembered, "Ultimative Leistung", false) };
        Assert.Equal(remembered, PowerPlans.FindUltimate(withOwn, remembered)!.Id);
        Assert.Null(PowerPlans.FindUltimate(PowerPlans.Parse(german), null));

        Assert.Equal(new Guid("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"), PowerPlans.ParseCreated("Power Scheme GUID: aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee  (Ultimate Performance)"));
        Assert.Null(PowerPlans.ParseCreated("Invalid Parameters"));
    }

    [Fact]
    public void Gpu_scheduling_follows_the_registry_and_notices_a_pending_restart()
    {
        Assert.False(GpuScheduling.Status(false, false, null, false).IsSupported);
        var pending = GpuScheduling.Status(true, enabledNow: false, registryValue: GpuScheduling.On, enabledByDefault: false);
        Assert.True(pending.IsOn);
        Assert.True(pending.NeedsRestart);
        var byDefault = GpuScheduling.Status(true, enabledNow: true, registryValue: null, enabledByDefault: true);
        Assert.True(byDefault.IsOn);
        Assert.False(byDefault.NeedsRestart);
        Assert.False(GpuScheduling.Status(true, true, GpuScheduling.Off, true).IsOn);
    }

    [Fact]
    public void DirectX_user_settings_keep_other_entries()
    {
        const string current = "VRROptimizeEnable=0;AutoHDREnable=1;";
        Assert.False(DirectXUserSettings.IsOn(current, DirectXUserSettings.SwapEffectUpgrade));
        string on = DirectXUserSettings.Set(current, DirectXUserSettings.SwapEffectUpgrade, true);
        Assert.Equal("VRROptimizeEnable=0;AutoHDREnable=1;SwapEffectUpgradeEnable=1;", on);
        Assert.True(DirectXUserSettings.IsOn(on, DirectXUserSettings.SwapEffectUpgrade));
        Assert.Equal("VRROptimizeEnable=0;AutoHDREnable=1;SwapEffectUpgradeEnable=0;", DirectXUserSettings.Set(on, "swapeffectupgradeenable", false));
        Assert.Equal("SwapEffectUpgradeEnable=1;", DirectXUserSettings.Set(null, DirectXUserSettings.SwapEffectUpgrade, true));
    }

    [Theory]
    [InlineData(60, 59)]
    [InlineData(120, 116)]
    [InlineData(144, 138)]
    [InlineData(165, 157)]
    [InlineData(240, 224)]
    [InlineData(360, 324)]
    [InlineData(0, 0)]
    public void Automatic_frame_cap_stays_inside_the_variable_refresh_range(int refresh, int expected) =>
        Assert.Equal(expected, FrameCap.AutoTarget(refresh));

    [Fact]
    public void Frame_cap_targets_and_presets()
    {
        Assert.Equal(138, FrameCap.Resolve(0, 144));
        Assert.Equal(120, FrameCap.Resolve(120, 144));
        Assert.Equal(FrameCap.Maximum, FrameCap.Resolve(5000, 144));
        Assert.Equal(FrameCap.Minimum, FrameCap.Clamp(5));
        Assert.Equal(0, FrameCap.Clamp(0));
        var presets = FrameCap.Presets(165);
        Assert.Contains(60, presets);
        Assert.Contains(165, presets);
        Assert.DoesNotContain(240, presets);
        Assert.Contains(175, FrameCap.Presets(175));
        Assert.Equal("—", FrameCap.FrameTimeText(0));
    }

    [Fact]
    public void Frame_pacing_grades_from_the_one_percent_low()
    {
        Assert.Equal(PacingGrade.Smooth, FramePacing.Grade(138, 125));
        Assert.Equal(PacingGrade.Uneven, FramePacing.Grade(138, 95));
        Assert.Equal(PacingGrade.Stuttering, FramePacing.Grade(138, 60));
        Assert.Equal(PacingGrade.Unknown, FramePacing.Grade(0, 0));
        Assert.Equal(91, FramePacing.Consistency(138, 125));
        Assert.Equal(ThresholdLevel.Cool, FramePacing.Level(PacingGrade.Smooth));
        Assert.Equal(ThresholdLevel.Critical, FramePacing.Level(PacingGrade.Stuttering));
    }

    [Fact]
    public void Shader_caches_and_driver_changes()
    {
        var locations = ShaderCaches.Locations("L", "P", ["S1"]);
        Assert.Contains(locations, l => l.Name == "NVIDIA DirectX" && l.Path.EndsWith("DXCache"));
        Assert.Contains(locations, l => l.Name == "Steam shader pre-cache" && l.Path.Contains("shadercache"));
        Assert.Null(ShaderCaches.DriverChangeText(null, "581.57"));
        Assert.Null(ShaderCaches.DriverChangeText("581.57", "581.57"));
        Assert.Contains("from 576.02 to 581.57", ShaderCaches.DriverChangeText("576.02", "581.57"));
    }

    [Fact]
    public void Gaming_settings_are_repaired()
    {
        var settings = new AppSettings { Gaming = null! };
        settings.Normalize();
        Assert.Equal(GameLibraryView.List, settings.Gaming.LibraryView);
        settings.Gaming.FramePacerTarget = 5;
        settings.Gaming.PreviousPowerPlan = "not a guid";
        settings.Gaming.LibraryView = (GameLibraryView)42;
        settings.Normalize();
        Assert.Equal(FrameCap.Minimum, settings.Gaming.FramePacerTarget);
        Assert.Null(settings.Gaming.PreviousPowerPlan);
        Assert.Equal(GameLibraryView.List, settings.Gaming.LibraryView);
    }
}
