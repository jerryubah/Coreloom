using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Coreloom.Core.Diagnostics;
using Coreloom.Core.Gaming;
using Coreloom.Core.Overlay;
using Coreloom.Core.Settings;
using Coreloom.Hardware.Mac;
using Coreloom.Hardware.Mac.Native;
using Coreloom.Mac.Native;
using Coreloom.ViewModels.Services;

namespace Coreloom.Mac.Services;

/// <summary>
/// The Gaming page on a Mac. The library comes from Steam's and Epic's own library files and from the games in the
/// Applications folders (apps whose Info.plist files them under Games, App Store and Apple Arcade games included). The
/// switches are macOS's own: Low Power and High Power Mode (pmset, after macOS asks for the user's password), Apple's
/// Metal Performance HUD (a user default) and a sleep assertion while a game runs. Game Mode is macOS's to switch, so it
/// is reported, not changed. Nothing is ever added to a game; pictures missing on the Mac are fetched as on Windows
/// (Epic's catalog or Steam's public store, sending only the game's name or Steam app id).
/// </summary>
public sealed class MacGamingService : IGamingService
{
    private const string LogCategory = "Gaming";
    private static readonly TimeSpan RetryMissingPictureAfter = TimeSpan.FromDays(7);
    private readonly string _artworkDirectory;
    private readonly string _userAgent;
    private readonly Func<int> _refreshRate;
    private readonly object _gate = new();
    private HttpClient? _http;
    private List<ShaderCacheInfo> _shaderCaches = [];
    private DateTime _shaderCachesMeasured = DateTime.MinValue;

    public MacGamingService(string localDirectory, string userAgent, Func<int> refreshRate)
    {
        _artworkDirectory = Path.Combine(localDirectory, "Artwork");
        _userAgent = userAgent;
        _refreshRate = refreshRate;
    }

    public bool IsSupported => OperatingSystem.IsMacOS();

    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static string Support => Path.Combine(Home, "Library", "Application Support");

    // ---- Library ------------------------------------------------------------------------------------------

    public Task<IReadOnlyList<GameEntry>> ScanLibraryAsync(CancellationToken cancellationToken) => Task.Run<IReadOnlyList<GameEntry>>(() =>
    {
        var watch = Stopwatch.StartNew();
        var games = new List<GameEntry>();
        void Add(string store, Func<IEnumerable<GameEntry>> scan)
        {
            try
            {
                games.AddRange(scan());
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or FormatException)
            {
                Log.Warn(LogCategory, $"Reading the {store} library failed.", e);
            }
        }

        Add("Steam", SteamGames);
        Add("Epic Games", EpicGames);
        Add("Applications", ApplicationGames);
        var merged = GameLibrary.Merge(games).Select(WithLocalArtwork).ToList();
        Log.Info(LogCategory, $"Found {merged.Count} installed games in {watch.ElapsedMilliseconds} ms.");
        return merged;
    }, cancellationToken);

    private static string SteamPath => Path.Combine(Support, "Steam");

    private static IEnumerable<GameEntry> SteamGames()
    {
        string steam = SteamPath;
        if (!Directory.Exists(steam))
        {
            yield break;
        }

        string vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        var libraries = SteamLibrary.LibraryFolders(steam, File.Exists(vdf) ? File.ReadAllText(vdf) : null).Where(Directory.Exists).ToList();
        string cache = Path.Combine(steam, "appcache", "librarycache");
        foreach (string library in libraries)
        {
            string apps = Path.Combine(library, "steamapps");
            if (!Directory.Exists(apps))
            {
                continue;
            }

            foreach (string manifest in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
            {
                GameEntry? game;
                try
                {
                    game = SteamLibrary.FromManifest(File.ReadAllText(manifest), library);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                if (game is not null)
                {
                    yield return game with { Artwork = SteamLibrary.FindArtwork(cache, game.StoreId), Executable = AppInside(game.InstallPath) };
                }
            }
        }
    }

    /// <summary>The first .app bundle inside a game's folder (its icon), or null.</summary>
    private static string? AppInside(string? folder)
    {
        try
        {
            return folder is not null && Directory.Exists(folder) ? Directory.EnumerateDirectories(folder, "*.app").FirstOrDefault() : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static IEnumerable<GameEntry> EpicGames()
    {
        string data = Path.Combine(Support, "Epic", "EpicGamesLauncher", "Data");
        string manifests = Path.Combine(data, "Manifests");
        if (!Directory.Exists(manifests))
        {
            yield break;
        }

        Dictionary<string, IReadOnlyList<string>> covers = [];
        string catalog = Path.Combine(data, "Catalog", "catcache.bin");
        if (File.Exists(catalog))
        {
            try
            {
                covers = EpicLibrary.CatalogCovers(File.ReadAllText(catalog));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }

        foreach (string file in Directory.EnumerateFiles(manifests, "*.item"))
        {
            string json;
            try
            {
                json = File.ReadAllText(file);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            var game = EpicLibrary.FromManifest(json);
            if (game is null || !Directory.Exists(game.InstallPath))
            {
                continue;
            }

            game = game with { Executable = game.Executable is { } exe && exe.Contains(".app", StringComparison.Ordinal) ? exe[..(exe.IndexOf(".app", StringComparison.Ordinal) + 4)] : AppInside(game.InstallPath) };
            yield return game.CatalogId is { } id && covers.TryGetValue(id, out var urls) ? game with { CoverUrls = urls } : game;
        }
    }

    /// <summary>
    /// Games in /Applications and ~/Applications (one folder deep too, for "Blizzard/…"-style folders): apps whose
    /// Info.plist says they're games. App Store apps (they carry a receipt) count as App Store games.
    /// </summary>
    private static IEnumerable<GameEntry> ApplicationGames()
    {
        var bundles = new List<string>();
        foreach (string root in new[] { "/Applications", Path.Combine(Home, "Applications") })
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (string entry in SafeDirectories(root))
            {
                if (entry.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
                {
                    bundles.Add(entry);
                }
                else if (!Path.GetFileName(entry).StartsWith('.'))
                {
                    bundles.AddRange(SafeDirectories(entry).Where(d => d.EndsWith(".app", StringComparison.OrdinalIgnoreCase)));
                }
            }
        }

        foreach (string bundle in bundles)
        {
            if (DescribeApp(bundle) is not { } app || !app.IsGame)
            {
                continue;
            }

            bool appStore = File.Exists(Path.Combine(bundle, "Contents", "_MASReceipt", "receipt"));
            string id = (appStore ? "appstore:" : "app:") + (app.BundleId ?? bundle);
            yield return new GameEntry(id, app.Name, appStore ? GameStore.AppStore : GameStore.Other)
            {
                InstallPath = bundle,
                Executable = bundle,
                Launch = bundle,
                LastPlayed = app.LastUsed,
            };
        }
    }

    private static IEnumerable<string> SafeDirectories(string folder)
    {
        try
        {
            return Directory.GetDirectories(folder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    internal sealed record AppInfo(string Name, string? BundleId, string? Category, string? IconFile, DateTimeOffset? LastUsed)
    {
        public bool IsGame => Category is not null && (Category.Contains("games", StringComparison.OrdinalIgnoreCase) || Category.EndsWith("-games", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>An app bundle's name, identifier, category and icon file from its Info.plist, or null.</summary>
    internal static AppInfo? DescribeApp(string bundle)
    {
        if (CF.ReadPropertyList(Path.Combine(bundle, "Contents", "Info.plist")) is not Dictionary<string, object?> plist)
        {
            return null;
        }

        string name = CF.Text(plist, "CFBundleDisplayName") is { Length: > 0 } display ? display
            : CF.Text(plist, "CFBundleName") is { Length: > 0 } bundleName ? bundleName
            : Path.GetFileNameWithoutExtension(bundle);
        string? icon = CF.Text(plist, "CFBundleIconFile");
        if (icon is not null && !icon.EndsWith(".icns", StringComparison.OrdinalIgnoreCase))
        {
            icon += ".icns";
        }

        icon ??= CF.Text(plist, "CFBundleIconName") is { } iconName ? iconName + ".icns" : null;
        DateTimeOffset? lastUsed = null;
        if (CF.Text(plist, "CFBundleExecutable") is { } executable)
        {
            try
            {
                string program = Path.Combine(bundle, "Contents", "MacOS", executable);
                lastUsed = File.Exists(program) ? File.GetLastAccessTimeUtc(program) : null;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }

        return new AppInfo(name, CF.Text(plist, "CFBundleIdentifier"), CF.Text(plist, "LSApplicationCategoryType"), icon, lastUsed);
    }

    public IReadOnlyList<GameEntry> AddLocalPictures(IReadOnlyList<GameEntry> games) => [.. games.Select(WithLocalArtwork)];

    public ProgramInfo? DescribeProgram(string path)
    {
        if (!Directory.Exists(path) && !File.Exists(path))
        {
            return null;
        }

        return DescribeApp(path) is { } app ? new ProgramInfo(path, app.Name, null) : new ProgramInfo(path);
    }

    public string? PickProgram(string? startIn = null)
    {
        string? picked = AppKit.OpenPanel("Pick the game", ["app"], startIn is { Length: > 0 } && Directory.Exists(startIn) ? startIn : "/Applications");
        if (picked is not null)
        {
            Log.Info(LogCategory, $"Game picked to add by hand: {picked}");
        }

        return picked;
    }

    public string? PickPicture() => AppKit.OpenPanel("Pick a picture for the game", ["png", "jpg", "jpeg", "bmp"], Path.Combine(Home, "Pictures"));

    public bool FileExists(string path) => File.Exists(path) || (CustomGames.IsAppBundle(path) && Directory.Exists(path));

    private GameEntry WithLocalArtwork(GameEntry game)
    {
        var art = game.Artwork;
        if (art.Cover is null && File.Exists(ArtworkFile(game)))
        {
            art = art with { Cover = ArtworkFile(game) };
        }

        if (art.Banner is null && File.Exists(BannerFile(game)))
        {
            art = art with { Banner = BannerFile(game) };
        }

        if (art.Cover is null && art.Banner is null && art.Icon is null && (game.Executable ?? game.InstallPath) is { } bundle && CustomGames.IsAppBundle(bundle))
        {
            art = art with { Icon = IconFile(bundle) };
        }

        return game with { Artwork = art };
    }

    private string ArtworkFile(GameEntry game) => Path.Combine(_artworkDirectory, $"{ArtworkName(game)}.jpg");

    private string BannerFile(GameEntry game) => Path.Combine(_artworkDirectory, $"{ArtworkName(game)}-wide.jpg");

    private string NotFoundFile(GameEntry game) => Path.Combine(_artworkDirectory, $"{ArtworkName(game)}.none");

    private static string ArtworkName(GameEntry game) => $"{game.Store.ToString().ToLowerInvariant()}-{Hash(game.StoreId)}";

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16].ToLowerInvariant();

    /// <summary>The app's icon (the best PNG in its .icns) saved once as a picture, or null.</summary>
    private string? IconFile(string bundle)
    {
        string file = Path.Combine(_artworkDirectory, "Icons", Hash(bundle.ToLowerInvariant()) + ".png");
        if (File.Exists(file))
        {
            return file;
        }

        try
        {
            if (DescribeApp(bundle)?.IconFile is not { } icon)
            {
                return null;
            }

            string icns = Path.Combine(bundle, "Contents", "Resources", icon);
            if (!File.Exists(icns) || Icns.BestPng(File.ReadAllBytes(icns), 256) is not { } png)
            {
                return null;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllBytes(file, png);
            return file;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<GameEntry>> DownloadArtworkAsync(IReadOnlyList<GameEntry> games, CancellationToken cancellationToken)
    {
        var http = Http();
        var result = new List<GameEntry>();
        int offline = 0;
        Directory.CreateDirectory(_artworkDirectory);
        foreach (var game in games)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(ArtworkFile(game)) || File.Exists(BannerFile(game)))
            {
                result.Add(WithLocalArtwork(game));
                continue;
            }

            string marker = NotFoundFile(game);
            if (File.Exists(marker) && DateTime.UtcNow - File.GetLastWriteTimeUtc(marker) < RetryMissingPictureAfter)
            {
                continue;
            }

            try
            {
                if (await FindPictureAsync(http, game, cancellationToken) is { } found)
                {
                    result.Add(found);
                    offline = 0;
                    TryDelete(marker);
                }
                else
                {
                    offline = 0;
                    await File.WriteAllTextAsync(marker, game.Name, cancellationToken);
                }
            }
            catch (Exception e) when (e is HttpRequestException || (e is TaskCanceledException && !cancellationToken.IsCancellationRequested))
            {
                Log.Debug(LogCategory, $"Looking up a picture for {game.Name} failed: {e.Message}");
                if (++offline >= 3)
                {
                    Log.Info(LogCategory, "Game pictures: the stores can't be reached; trying again on the next scan.");
                    break;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Debug(LogCategory, $"Saving a picture for {game.Name} failed: {e.Message}");
            }
        }

        Log.Info(LogCategory, $"Game pictures: found {result.Count} of {games.Count} missing.");
        return result;
    }

    private async Task<GameEntry?> FindPictureAsync(HttpClient http, GameEntry game, CancellationToken cancellationToken)
    {
        bool reached = false;
        Exception? failure = null;
        var tried = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        async Task<bool> Download(string url, string target)
        {
            if (!tried.Add(url))
            {
                return false;
            }

            try
            {
                bool saved = await TryDownloadAsync(http, url, target, cancellationToken);
                reached = true;
                return saved;
            }
            catch (Exception e) when (e is HttpRequestException || (e is TaskCanceledException && !cancellationToken.IsCancellationRequested))
            {
                failure = e;
                return false;
            }
        }

        async Task<string?> Text(string url)
        {
            try
            {
                using var response = await http.GetAsync(url, cancellationToken);
                reached = true;
                return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync(cancellationToken) : null;
            }
            catch (Exception e) when (e is HttpRequestException || (e is TaskCanceledException && !cancellationToken.IsCancellationRequested))
            {
                failure = e;
                return null;
            }
        }

        foreach (string url in game.CoverUrls)
        {
            if (await Download(url, ArtworkFile(game)))
            {
                return game with { Artwork = game.Artwork with { Cover = ArtworkFile(game) } };
            }
        }

        string? appId = game.Store == GameStore.Steam ? game.StoreId : null;
        if (appId is null && SteamStore.Normalize(game.Name).Length > 0 && await Text(SteamStore.SearchUrl(game.Name)) is { } results)
        {
            appId = SteamStore.BestMatch(game.Name, SteamStore.ParseSearch(results));
        }

        if (appId is not null)
        {
            var assets = await Text(SteamStore.ItemUrl(appId)) is { } item ? SteamStore.ParseAssets(item) : null;
            foreach (var source in SteamStore.PictureUrls(appId, assets))
            {
                string target = source.IsCover ? ArtworkFile(game) : BannerFile(game);
                if (await Download(source.Url, target))
                {
                    return game with { Artwork = source.IsCover ? game.Artwork with { Cover = target } : game.Artwork with { Banner = target } };
                }
            }
        }

        if (!reached && failure is not null)
        {
            throw new HttpRequestException("No picture store could be reached.", failure);
        }

        return null;
    }

    private static async Task<bool> TryDownloadAsync(HttpClient http, string url, string target, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        string? type = response.Content.Headers.ContentType?.MediaType;
        if (!response.IsSuccessStatusCode || type is null || !type.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        byte[] bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (bytes.Length < 1024 || bytes.Length > 16 << 20)
        {
            return false;
        }

        string temp = target + ".part";
        await File.WriteAllBytesAsync(temp, bytes, cancellationToken);
        File.Move(temp, target, overwrite: true);
        return true;
    }

    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private HttpClient Http()
    {
        lock (_gate)
        {
            if (_http is null)
            {
                _http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
                _http.DefaultRequestHeaders.UserAgent.ParseAdd(_userAgent);
            }

            return _http;
        }
    }

    public bool Launch(GameEntry game)
    {
        string? target = game.Launch;
        if (string.IsNullOrWhiteSpace(target))
        {
            return false;
        }

        if (target.Contains("://", StringComparison.Ordinal))
        {
            Log.Info(LogCategory, $"Starting {game.Name} through {GameLibrary.StoreName(game.Store)}.");
            return Commands.Start("/usr/bin/open", target);
        }

        // A custom game's "path" args: an app bundle with launch options, or just the bundle.
        string bundle = target;
        string? arguments = null;
        if (target.StartsWith('"') && target.IndexOf('"', 1) is int end and > 1)
        {
            bundle = target[1..end];
            arguments = target[(end + 1)..].Trim();
        }

        if (CustomGames.IsAppBundle(bundle) && Directory.Exists(bundle))
        {
            Log.Info(LogCategory, $"Starting {game.Name}.");
            return string.IsNullOrEmpty(arguments)
                ? Commands.Start("/usr/bin/open", "-a", bundle)
                : Commands.Start("/usr/bin/open", ["-a", bundle, "--args", .. arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries)]);
        }

        return File.Exists(bundle) && Commands.Start(bundle);
    }

    public void OpenFolder(string path)
    {
        if (Directory.Exists(path))
        {
            // Show an app in its folder rather than opening it.
            if (path.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
            {
                MacShellService.Reveal(path);
            }
            else
            {
                Commands.Start("/usr/bin/open", path);
            }
        }
    }

    // ---- Running game and frame rates ----------------------------------------------------------------------

    public Task<RunningGame?> FindRunningAsync(IReadOnlyList<GameEntry> games, CancellationToken cancellationToken) =>
        Task.Run(() => GameLibrary.FindRunning(games, RunningPrograms()), cancellationToken);

    public static IEnumerable<RunningProcess> RunningPrograms()
    {
        foreach (int pid in Darwin.AllPids())
        {
            if (pid > 0 && Darwin.ProcessPath(pid) is { } path)
            {
                yield return new RunningProcess(pid, path);
            }
        }
    }

    /// <summary>Frame rates aren't measured on a Mac (that would mean adding code to games); Apple's Metal HUD shows them.</summary>
    public void WatchFrameRates(bool on)
    {
    }

    public FrameRateStats? FrameRate(int processId) => null;

    // ---- Status --------------------------------------------------------------------------------------------

    public Task<GamingStatus> StatusAsync(GamingSettings settings, CancellationToken cancellationToken) => Task.Run(async () =>
    {
        int macOS = Environment.OSVersion.Version.Major;
        var tweaks = new Dictionary<GamingTweak, TweakStatus>
        {
            [GamingTweak.GameMode] = macOS >= 14
                ? new TweakStatus(true, true) // the card's caveat says macOS turns it on by itself
                : TweakStatus.Unsupported("Game Mode comes with macOS Sonoma (14) and later."),
            [GamingTweak.KeepAwake] = new TweakStatus(true, settings.KeepAwake),
            [GamingTweak.MetalHud] = await MetalHudStatusAsync(cancellationToken),
            [GamingTweak.PowerMode] = PowerModeStatus(await PowerSettingsAsync(cancellationToken)),
        };

        if (DateTime.UtcNow - _shaderCachesMeasured > TimeSpan.FromMinutes(5))
        {
            _shaderCaches = MeasureShaderCaches();
            _shaderCachesMeasured = DateTime.UtcNow;
        }

        return new GamingStatus
        {
            Tweaks = tweaks,
            HasNvidiaDriver = false,
            RefreshRate = _refreshRate(),
            DriverVersion = $"Metal · macOS {Darwin.SysctlString("kern.osproductversion")}",
            ShaderCaches = _shaderCaches,
        };
    }, cancellationToken);

    private static async Task<TweakStatus> MetalHudStatusAsync(CancellationToken cancellationToken)
    {
        if (Environment.OSVersion.Version.Major < 13)
        {
            return TweakStatus.Unsupported("The Metal Performance HUD comes with macOS Ventura (13) and later.");
        }

        var read = await Commands.RunAsync("/usr/bin/defaults", ["read", "-g", "MetalForceHudEnabled"], cancellationToken: cancellationToken);
        return new TweakStatus(true, read.Succeeded && read.Output.Trim() is "1" or "true" or "YES");
    }

    /// <summary>The charger's power settings ("AC Power" in pmset -g custom): its keys and values.</summary>
    private static async Task<Dictionary<string, int>> PowerSettingsAsync(CancellationToken cancellationToken)
    {
        var result = await Commands.RunAsync("/usr/bin/pmset", ["-g", "custom"], cancellationToken: cancellationToken);
        return result.Succeeded ? PowerSettings.ChargerSection(result.Output) : [];
    }

    private static TweakStatus PowerModeStatus(Dictionary<string, int> charger)
    {
        bool hasLowPower = charger.TryGetValue("lowpowermode", out int lowPower);
        bool hasPowerMode = charger.TryGetValue("powermode", out int powerMode);
        if (!hasLowPower && !hasPowerMode)
        {
            return TweakStatus.Unsupported("This Mac has no Low Power or High Power Mode.");
        }

        bool on = (!hasLowPower || lowPower == 0) && (!hasPowerMode || powerMode == 2);
        return new TweakStatus(true, on); // the card's description says what "on" sets, with and without High Power Mode
    }

    /// <summary>Metal's shader caches in the user's cache folder (one per app), largest first.</summary>
    private static List<ShaderCacheInfo> MeasureShaderCaches()
    {
        var caches = new List<ShaderCacheInfo>();
        if (Darwin.UserCacheDirectory() is not { } cache)
        {
            return caches;
        }

        string metal = Path.Combine(cache, "com.apple.metal");
        if (!Directory.Exists(metal))
        {
            return caches;
        }

        foreach (string folder in SafeDirectories(metal))
        {
            long bytes = 0;
            int files = 0;
            try
            {
                foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
                {
                    bytes += file.Length;
                    files++;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            if (files > 0)
            {
                caches.Add(new ShaderCacheInfo($"Metal: {Path.GetFileName(folder)}", folder, bytes, files));
            }
        }

        return caches.OrderByDescending(c => c.Bytes).Take(30).ToList();
    }

    // ---- Switches ------------------------------------------------------------------------------------------

    public Task<TweakResult> SetTweakAsync(GamingTweak tweak, bool on, GamingSettings settings, CancellationToken cancellationToken) => tweak switch
    {
        GamingTweak.GameMode => Task.FromResult(new TweakResult(false, "macOS turns Game Mode on by itself when a game goes full screen; apps can't switch it.")),
        GamingTweak.KeepAwake => Task.FromResult(SetKeepAwake(on, settings)),
        GamingTweak.MetalHud => SetMetalHudAsync(on, cancellationToken),
        GamingTweak.PowerMode => SetPowerModeAsync(on, settings, cancellationToken),
        _ => Task.FromResult(new TweakResult(false, "Not available on a Mac.")),
    };

    private static TweakResult SetKeepAwake(bool on, GamingSettings settings)
    {
        settings.KeepAwake = on;
        return new TweakResult(true, on ? "The Mac stays awake while a library game runs." : "The Mac sleeps as usual while you play.");
    }

    private static async Task<TweakResult> SetMetalHudAsync(bool on, CancellationToken cancellationToken)
    {
        var result = await Commands.RunAsync("/usr/bin/defaults", ["write", "-g", "MetalForceHudEnabled", "-bool", on ? "true" : "false"], cancellationToken: cancellationToken);
        if (!result.Succeeded)
        {
            return new TweakResult(false, $"macOS didn't accept the change: {result.Error}");
        }

        Log.Info(LogCategory, $"Metal Performance HUD {(on ? "on" : "off")}.");
        return new TweakResult(true, on ? "Games you open from now on show Apple's Metal Performance HUD." : "Games you open from now on don't show the HUD.");
    }

    private static async Task<TweakResult> SetPowerModeAsync(bool on, GamingSettings settings, CancellationToken cancellationToken)
    {
        var charger = await PowerSettingsAsync(cancellationToken);
        bool hasPowerMode = charger.ContainsKey("powermode");
        string command;
        if (on)
        {
            settings.PreviousPowerMode = PowerSettings.Remember(charger);
            command = "/usr/bin/pmset -c lowpowermode 0" + (hasPowerMode ? "; /usr/bin/pmset -c powermode 2" : string.Empty);
        }
        else
        {
            var (powerMode, lowPower) = PowerSettings.Restore(settings.PreviousPowerMode);
            command = $"/usr/bin/pmset -c lowpowermode {lowPower.ToString(CultureInfo.InvariantCulture)}"
                + (hasPowerMode ? $"; /usr/bin/pmset -c powermode {(powerMode == 2 ? 0 : powerMode).ToString(CultureInfo.InvariantCulture)}" : string.Empty);
            settings.PreviousPowerMode = null;
        }

        var result = await Commands.RunAsAdministratorAsync(command, "Coreloom wants to change your Mac's power mode while it's plugged in.", cancellationToken);
        if (Commands.WasCancelled(result))
        {
            return new TweakResult(false, "Nothing changed: the password prompt was cancelled.");
        }

        if (!result.Succeeded)
        {
            return new TweakResult(false, $"macOS didn't accept the change: {result.Error}");
        }

        Log.Info(LogCategory, $"Charger power mode set: {command}");
        return new TweakResult(true, on ? "Full power while plugged in." : "Power mode back to how it was.");
    }

    // ---- Frame limits (Windows drivers only) ---------------------------------------------------------------

    public Task<TweakResult> SetGameFrameCapAsync(string gameName, IReadOnlyList<string> executables, int fps, CancellationToken cancellationToken) =>
        Task.FromResult(new TweakResult(false, "macOS has no per-game frame-rate limit; set one in the game's own settings."));
}

/// <summary>Reading `pmset -g custom` and remembering the charger's power settings.</summary>
public static class PowerSettings
{
    /// <summary>The "AC Power:" section's settings (key → number). Desktop Macs list only that section.</summary>
    public static Dictionary<string, int> ChargerSection(string output)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        bool inCharger = !output.Contains("AC Power:", StringComparison.Ordinal);
        foreach (string raw in output.Split('\n'))
        {
            string line = raw.Trim();
            if (line.EndsWith(':'))
            {
                inCharger = line.StartsWith("AC Power", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inCharger)
            {
                continue;
            }

            string[] parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            {
                result[parts[0]] = value;
            }
        }

        return result;
    }

    /// <summary>The power mode and Low Power Mode before a change, in one number (power mode × 10 + low power).</summary>
    public static int Remember(IReadOnlyDictionary<string, int> charger) =>
        (charger.TryGetValue("powermode", out int mode) ? mode : 0) * 10 + (charger.TryGetValue("lowpowermode", out int low) ? low : 0);

    public static (int PowerMode, int LowPower) Restore(int? remembered) => remembered is int value ? (value / 10, value % 10) : (0, 0);
}
