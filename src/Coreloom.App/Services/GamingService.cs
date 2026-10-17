using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Coreloom.App.Interop;
using Coreloom.App.Overlay;
using Coreloom.Core.Diagnostics;
using Coreloom.Core.Gaming;
using Coreloom.Core.Overlay;
using Coreloom.Core.Settings;
using Coreloom.ViewModels.Services;
using Microsoft.Win32;

namespace Coreloom.App.Services;

/// <summary>
/// The Gaming page on Windows. Reads launchers' own library files (Steam, Epic, GOG, Xbox) and Windows' installed-apps
/// list (Ubisoft, EA, Battle.net, Riot); changes only documented Windows settings (the same registry values and
/// powercfg commands Windows' Settings app uses) and NVIDIA driver settings through NVAPI (as the NVIDIA Control Panel
/// does). Nothing is ever added to games. Games without a picture on the PC get one downloaded automatically (since
/// 1.4.4) from Epic's catalog pictures or Steam's public store (<see cref="SteamStore"/>), sending only the game's name or
/// Steam app id.
/// </summary>
internal sealed class WindowsGamingService : IGamingService
{
    private const string LogCategory = "Gaming";
    private const string GameBarKey = @"Software\Microsoft\GameBar";
    private const string GameDvrKey = @"Software\Microsoft\Windows\CurrentVersion\GameDVR";
    private const string FrameRateOwner = "gaming";
    private static readonly TimeSpan RetryMissingPictureAfter = TimeSpan.FromDays(7);

    private readonly EtwFrameRateSource _frameRates;
    private readonly string _artworkDirectory;
    private readonly string _userAgent;
    private readonly object _gate = new();
    private HttpClient? _http;
    private List<ShaderCacheInfo> _shaderCaches = [];
    private DateTime _shaderCachesMeasured = DateTime.MinValue;

    public WindowsGamingService(EtwFrameRateSource frameRates, string localDirectory, string userAgent)
    {
        _frameRates = frameRates;
        _artworkDirectory = Path.Combine(localDirectory, "Artwork");
        _userAgent = userAgent;
    }

    public bool IsSupported => OperatingSystem.IsWindows();

    private static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    private static string ProgramData => Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

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
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException
                or ArgumentException or FormatException or System.Runtime.InteropServices.COMException or System.Xml.XmlException)
            {
                Log.Warn(LogCategory, $"Reading the {store} library failed.", e);
            }
        }

        Add("Steam", SteamGames);
        Add("Epic Games", EpicGames);
        Add("GOG", GogGames);
        Add("Xbox", XboxGames);
        Add("launcher", LauncherGamesFromRegistry);

        var merged = GameLibrary.Merge(games).Select(WithLocalArtwork).ToList();
        Log.Info(LogCategory, $"Found {merged.Count} installed games in {watch.ElapsedMilliseconds} ms.");
        return merged;
    }, cancellationToken);

    private static string? SteamPath()
    {
        string? path = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")?.GetValue("SteamPath") as string
            ?? Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam")?.GetValue("InstallPath") as string
            ?? Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Valve\Steam")?.GetValue("InstallPath") as string;
        path = path?.Replace('/', '\\');
        return path is not null && Directory.Exists(path) ? path : null;
    }

    private static List<string> SteamLibraries()
    {
        string? steam = SteamPath();
        if (steam is null)
        {
            return [];
        }

        string vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        return SteamLibrary.LibraryFolders(steam, File.Exists(vdf) ? File.ReadAllText(vdf) : null)
            .Where(Directory.Exists)
            .ToList();
    }

    private static IEnumerable<GameEntry> SteamGames()
    {
        string? steam = SteamPath();
        if (steam is null)
        {
            yield break;
        }

        string cache = Path.Combine(steam, "appcache", "librarycache");
        foreach (string library in SteamLibraries())
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
                    continue; // being written by Steam
                }

                if (game is not null)
                {
                    yield return game with { Artwork = SteamLibrary.FindArtwork(cache, game.StoreId) };
                }
            }
        }
    }

    private static IEnumerable<GameEntry> EpicGames()
    {
        string data = Path.Combine(ProgramData, "Epic", "EpicGamesLauncher", "Data");
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

            yield return game.CatalogId is { } id && covers.TryGetValue(id, out var urls) ? game with { CoverUrls = urls } : game;
        }
    }

    private static IEnumerable<GameEntry> GogGames()
    {
        foreach (var hive in new[] { @"SOFTWARE\WOW6432Node\GOG.com\Games", @"SOFTWARE\GOG.com\Games" })
        {
            using var root = Registry.LocalMachine.OpenSubKey(hive);
            if (root is null)
            {
                continue;
            }

            foreach (string id in root.GetSubKeyNames())
            {
                using var key = root.OpenSubKey(id);
                string? name = key?.GetValue("gameName") as string;
                string? path = key?.GetValue("path") as string;
                if (key is null || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                {
                    continue;
                }

                string? exe = key.GetValue("exe") as string;
                string info = Path.Combine(path, $"goggame-{id}.info");
                if (File.Exists(info) && GogLibrary.PrimaryExecutable(File.ReadAllText(info)) is { } primary)
                {
                    exe = Path.Combine(path, primary);
                }

                string icon = Path.Combine(path, $"goggame-{id}.ico");
                yield return new GameEntry("gog:" + id, name, GameStore.Gog)
                {
                    InstallPath = path,
                    Launch = exe is not null && File.Exists(exe) ? exe : null,
                    Executable = File.Exists(icon) ? icon : exe,
                };
            }
        }
    }

    private static IEnumerable<GameEntry> XboxGames()
    {
        var manager = new global::Windows.Management.Deployment.PackageManager();
        foreach (var package in manager.FindPackagesForUser(string.Empty))
        {
            string? folder;
            try
            {
                if (package.IsFramework || package.IsResourcePackage)
                {
                    continue;
                }

                folder = package.InstalledPath;
            }
            catch (Exception e) when (e is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
            {
                continue;
            }

            string config = string.IsNullOrEmpty(folder) ? string.Empty : Path.Combine(folder, "MicrosoftGame.config");
            if (config.Length == 0 || !File.Exists(config))
            {
                continue; // apps, not games
            }

            var game = XboxLibrary.ParseConfig(File.ReadAllText(config));
            string family = package.Id.FamilyName;
            string? name = game?.DisplayName is { } display && !display.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase)
                ? display
                : package.DisplayName;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            string appId = game?.ExecutableId ?? "Game";
            string? Art(string? file) => file is null ? null : ExistingScaled(Path.Combine(folder!, file));
            yield return new GameEntry("xbox:" + family, name, GameStore.Xbox)
            {
                InstallPath = folder,
                Launch = $@"shell:AppsFolder\{family}!{appId}",
                Executable = game?.Executable is { } exe ? Path.Combine(folder!, exe) : null,
                Artwork = new GameArtwork(null, Art(game?.SplashScreen), Art(game?.Square150Logo) ?? Art(game?.StoreLogo)),
            };
        }
    }

    /// <summary>Package pictures are often stored as "Logo.scale-200.png" next to the "Logo.png" the config names.</summary>
    private static string? ExistingScaled(string path)
    {
        if (File.Exists(path))
        {
            return path;
        }

        string? dir = Path.GetDirectoryName(path);
        if (dir is null || !Directory.Exists(dir))
        {
            return null;
        }

        string stem = Path.GetFileNameWithoutExtension(path);
        return Directory.EnumerateFiles(dir, stem + ".scale-*" + Path.GetExtension(path))
            .OrderByDescending(f => f.Length)
            .ThenByDescending(f => f, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static IEnumerable<GameEntry> LauncherGamesFromRegistry()
    {
        foreach (var (hive, path) in new[]
        {
            (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Uninstall"),
        })
        {
            using var root = hive.OpenSubKey(path);
            if (root is null)
            {
                continue;
            }

            foreach (string name in root.GetSubKeyNames())
            {
                using var key = root.OpenSubKey(name);
                if (key is null)
                {
                    continue;
                }

                var entry = new UninstallEntry(name, key.GetValue("DisplayName") as string, key.GetValue("Publisher") as string,
                    key.GetValue("UninstallString") as string, key.GetValue("InstallLocation") as string, key.GetValue("DisplayIcon") as string)
                {
                    EstimatedSizeKb = key.GetValue("EstimatedSize") is int kb ? kb : null,
                };
                if (LauncherGames.FromUninstall(entry) is { } game && (game.InstallPath is null || Directory.Exists(game.InstallPath)))
                {
                    yield return game;
                }
            }
        }
    }

    public IReadOnlyList<GameEntry> AddLocalPictures(IReadOnlyList<GameEntry> games) => [.. games.Select(WithLocalArtwork)];

    public ProgramInfo? DescribeProgram(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var info = FileVersionInfo.GetVersionInfo(path);
            return new ProgramInfo(path, info.ProductName, info.FileDescription);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return File.Exists(path) ? new ProgramInfo(path) : null;
        }
    }

    public string? PickProgram(string? startIn = null)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Pick the game's program",
            Filter = "Programs (*.exe)|*.exe",
            CheckFileExists = true,
            InitialDirectory = startIn is { Length: > 0 } && Directory.Exists(startIn) ? startIn : string.Empty,
        };
        string? picked = dialog.ShowDialog(System.Windows.Application.Current?.MainWindow) == true ? dialog.FileName : null;
        if (picked is not null)
        {
            Log.Info(LogCategory, $"Program picked for a game added by hand: {picked}");
        }

        return picked;
    }

    public string? PickPicture()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Pick a picture for the game",
            Filter = "Pictures (*.png;*.jpg;*.jpeg;*.bmp;*.ico)|*.png;*.jpg;*.jpeg;*.bmp;*.ico",
            CheckFileExists = true,
        };
        return dialog.ShowDialog(System.Windows.Application.Current?.MainWindow) == true ? dialog.FileName : null;
    }

    /// <summary>Adds pictures downloaded earlier and an icon (as a picture file) for games that have nothing better.</summary>
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

        if (art.Cover is null && art.Banner is null && art.Icon is null && game.Executable is { } exe && File.Exists(exe))
        {
            art = art with { Icon = IconFile(exe) };
        }

        return game with { Artwork = art };
    }

    private string ArtworkFile(GameEntry game) => Path.Combine(_artworkDirectory, $"{ArtworkName(game)}.jpg");

    /// <summary>A wide picture (Steam's store header), kept when no tall cover was found.</summary>
    private string BannerFile(GameEntry game) => Path.Combine(_artworkDirectory, $"{ArtworkName(game)}-wide.jpg");

    /// <summary>Marks a game no picture was found for, so it is looked up again only after a week.</summary>
    private string NotFoundFile(GameEntry game) => Path.Combine(_artworkDirectory, $"{ArtworkName(game)}.none");

    private static string ArtworkName(GameEntry game) => $"{game.Store.ToString().ToLowerInvariant()}-{Hash(game.StoreId)}";

    /// <summary>The program's icon saved as a 256 px picture (once), or null.</summary>
    private string? IconFile(string executable)
    {
        string file = Path.Combine(_artworkDirectory, "Icons", Hash(executable.ToLowerInvariant()) + ".png");
        if (File.Exists(file))
        {
            return file;
        }

        try
        {
            using var icon = System.Drawing.Icon.ExtractIcon(executable, 0, 256) ?? System.Drawing.Icon.ExtractAssociatedIcon(executable);
            if (icon is null)
            {
                return null;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            using var bitmap = icon.ToBitmap();
            bitmap.Save(file, System.Drawing.Imaging.ImageFormat.Png);
            return file;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or System.Runtime.InteropServices.ExternalException)
        {
            return null;
        }
    }

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16].ToLowerInvariant();

    /// <summary>
    /// Finds and downloads a picture for each game that has none: the store's own catalog pictures (Epic), else Steam's
    /// (the game's own app, or the Steam app with the same name). A game nothing is found for is tried again after a
    /// week; when the internet can't be reached, it stops and tries again next time.
    /// </summary>
    public async Task<IReadOnlyList<GameEntry>> DownloadArtworkAsync(IReadOnlyList<GameEntry> games, CancellationToken cancellationToken)
    {
        var http = Http();
        var result = new List<GameEntry>();
        int notFound = 0, offline = 0;
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
                    notFound++;
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

        Log.Info(LogCategory, $"Game pictures: found {result.Count} of {games.Count} missing ({notFound} not on Steam or Epic).");
        return result;
    }

    /// <summary>
    /// The game with a downloaded cover (or, failing that, a wide picture), or null when the stores answered but had
    /// none. Throws when no store could be reached at all, so the game isn't marked as having no picture while offline.
    /// </summary>
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
                failure = e; // one host may be blocked: try the next address
                return false;
            }
        }

        async Task<string?> Text(string url)
        {
            try
            {
                string? text = await GetTextAsync(http, url, cancellationToken);
                reached = true;
                return text;
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

        // Steam games by their own app; others by the Steam app with the same name (the search sends only the name).
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
                    Log.Debug(LogCategory, $"Picture for {game.Name} from Steam app {appId}.");
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

    /// <summary>A text answer, or null for an error status (network failures throw).</summary>
    private static async Task<string?> GetTextAsync(HttpClient http, string url, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(url, cancellationToken);
        return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync(cancellationToken) : null;
    }

    /// <summary>Saves the picture at <paramref name="url"/> as <paramref name="target"/>; false when it isn't a picture.</summary>
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
            // stays; harmless
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

        // "C:\Riot Client\RiotClientServices.exe" --launch-product=… : a program with arguments.
        if (target.StartsWith('"'))
        {
            int end = target.IndexOf('"', 1);
            if (end > 1)
            {
                string file = target[1..end];
                return File.Exists(file) && ShellLaunch.Start(file, target[(end + 1)..].Trim(), Path.GetDirectoryName(file));
            }
        }

        if (target.Contains("://", StringComparison.Ordinal) || target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
        {
            Log.Info(LogCategory, $"Starting {game.Name} through {GameLibrary.StoreName(game.Store)}.");
            return ShellLaunch.Start(target);
        }

        if (!File.Exists(target))
        {
            return false;
        }

        Log.Info(LogCategory, $"Starting {game.Name}.");
        return ShellLaunch.Start(target, null, Path.GetDirectoryName(target));
    }

    public void OpenFolder(string path)
    {
        if (Directory.Exists(path))
        {
            ShellLaunch.Start(path);
        }
    }

    // ---- Running game and frame rates ----------------------------------------------------------------------

    public Task<RunningGame?> FindRunningAsync(IReadOnlyList<GameEntry> games, CancellationToken cancellationToken) => Task.Run(() =>
        GameLibrary.FindRunning(games, GamingNative.RunningPrograms().Select(p => new RunningProcess(p.Id, p.Path))), cancellationToken);

    public void WatchFrameRates(bool on)
    {
        if (on)
        {
            if (!_frameRates.Acquire(FrameRateOwner))
            {
                Log.Warn(LogCategory, $"Frame rates unavailable: {_frameRates.Problem}");
            }
        }
        else
        {
            _frameRates.Release(FrameRateOwner);
        }
    }

    public FrameRateStats? FrameRate(int processId) => _frameRates.Get(processId);

    // ---- Status --------------------------------------------------------------------------------------------

    public Task<GamingStatus> StatusAsync(GamingSettings settings, CancellationToken cancellationToken) => Task.Run(async () =>
    {
        var plans = await PowerPlansAsync(cancellationToken);
        var tweaks = new Dictionary<GamingTweak, TweakStatus>
        {
            [GamingTweak.GpuScheduling] = GpuSchedulingStatus(),
            [GamingTweak.UltimatePower] = UltimatePowerStatus(plans, settings),
            [GamingTweak.GameMode] = new(true, ReadDword(Registry.CurrentUser, GameBarKey, "AutoGameModeEnabled") != 0),
            [GamingTweak.WindowedOptimizations] = Environment.OSVersion.Version.Build >= 22000
                ? new(true, DirectXUserSettings.IsOn(Registry.CurrentUser.OpenSubKey(DirectXUserSettings.Key)?.GetValue(DirectXUserSettings.Value) as string, DirectXUserSettings.SwapEffectUpgrade))
                : TweakStatus.Unsupported("Needs Windows 11."),
            [GamingTweak.NoBackgroundRecording] = new(true, (ReadDwordOrNull(Registry.CurrentUser, GameDvrKey, "HistoricalCaptureEnabled") ?? 0) != 1), // off unless turned on
        };

        bool nvidia = NvApi.IsAvailable;
        int frameCap = 0;
        string? driver = null;
        uint? shaderLimit = null;
        if (nvidia)
        {
            driver = NvApi.DriverVersion();
            uint? pstate = NvApi.GetGlobal(NvApi.PowerManagementMode, 5);
            var cache = NvApi.GetGlobalSetting(NvApi.ShaderCacheMaxSize);
            shaderLimit = cache is { IsSet: true } set ? set.Value : null;
            frameCap = (int)(NvApi.GetGlobal(NvApi.FrameRateLimiter, 0) ?? 0);
            tweaks[GamingTweak.MaxGpuPerformance] = pstate is null ? TweakStatus.Unsupported("The NVIDIA driver didn't answer.") : new(true, pstate == NvApi.PreferMaximumPerformance);
            tweaks[GamingTweak.ShaderCache] = cache is null ? TweakStatus.Unsupported("The NVIDIA driver didn't answer.") : new(true, ShaderCacheSize.IsOn(shaderLimit));
            tweaks[GamingTweak.GSync] = GSyncStatus();
        }
        else
        {
            tweaks[GamingTweak.MaxGpuPerformance] = TweakStatus.Unsupported("Needs an NVIDIA graphics card. AMD Software sets this per game.");
            tweaks[GamingTweak.ShaderCache] = TweakStatus.Unsupported("Needs an NVIDIA graphics card. AMD Software sets the shader cache in its graphics settings.");
            tweaks[GamingTweak.GSync] = TweakStatus.Unsupported("Needs an NVIDIA graphics card. On AMD graphics, FreeSync is turned on in AMD Software.");
        }

        return new GamingStatus
        {
            Tweaks = tweaks,
            HasNvidiaDriver = nvidia,
            ActivePowerPlan = plans.FirstOrDefault(p => p.IsActive),
            FrameCap = frameCap > FrameCap.Maximum ? FrameCap.Maximum : frameCap,
            RefreshRate = DisplayInfo.MaxRefreshRate(),
            DriverVersion = driver,
            ShaderCacheLimit = shaderLimit,
            ShaderCaches = ShaderCacheSizes(),
        };
    }, cancellationToken);

    /// <summary>
    /// G-SYNC: on when the driver's global profile has it for fullscreen and windowed games. Not offered when the gaming
    /// display (the fastest one) can't do variable refresh. (1.4.3 also warned when the display didn't report G-SYNC as
    /// requested, but that per-display flag read off on a PC with G-SYNC switched on, so the warning is gone.)
    /// </summary>
    private static TweakStatus GSyncStatus()
    {
        uint? feature = NvApi.GetGlobal(NvApi.VrrFeature, GSyncSetup.FullscreenOnly);
        uint? requested = NvApi.GetGlobal(NvApi.VrrRequestedState, GSyncSetup.FullscreenOnly);
        uint? mode = NvApi.GetGlobal(NvApi.VrrMode, GSyncSetup.FullscreenOnly);
        if (feature is null || requested is null || mode is null)
        {
            return TweakStatus.Unsupported("The NVIDIA driver didn't answer.");
        }

        var display = DisplayInfo.FastestDisplayName() is { } name ? NvApi.DisplayVrr(name) : null;
        if (display is { Possible: false })
        {
            return TweakStatus.Unsupported("Your display doesn't support G-SYNC or variable refresh.");
        }

        return new TweakStatus(true, GSyncSetup.IsOn(feature.Value, requested.Value, mode.Value));
    }

    private static TweakStatus GpuSchedulingStatus()
    {
        var caps = GamingNative.GpuScheduling();
        if (caps is null)
        {
            return TweakStatus.Unsupported("Needs Windows 10 version 2004 or later and a recent graphics driver.");
        }

        int? value = ReadDwordOrNull(Registry.LocalMachine, GpuScheduling.Key, GpuScheduling.Value);
        return GpuScheduling.Status(caps.Value.Supported, caps.Value.Enabled, value, caps.Value.EnabledByDefault);
    }

    private static TweakStatus UltimatePowerStatus(IReadOnlyList<PowerPlan> plans, GamingSettings settings)
    {
        if (plans.Count == 0)
        {
            return TweakStatus.Unsupported("Windows' power plans couldn't be read.");
        }

        var ultimate = PowerPlans.FindUltimate(plans, Guid.TryParse(settings.UltimatePowerPlan, out var id) ? id : null);
        return new TweakStatus(true, ultimate is { IsActive: true });
    }

    /// <summary>Shader cache sizes, measured at most every two minutes (Steam's pre-cache can hold many thousands of files).</summary>
    private List<ShaderCacheInfo> ShaderCacheSizes()
    {
        lock (_gate)
        {
            if (DateTime.UtcNow - _shaderCachesMeasured < TimeSpan.FromMinutes(2))
            {
                return _shaderCaches;
            }
        }

        var sizes = MeasureShaderCaches();
        lock (_gate)
        {
            _shaderCaches = sizes;
            _shaderCachesMeasured = DateTime.UtcNow;
        }

        return sizes;
    }

    private static List<ShaderCacheInfo> MeasureShaderCaches()
    {
        var list = new List<ShaderCacheInfo>();
        foreach (var (name, path) in ShaderCaches.Locations(LocalAppData, ProgramData, SteamLibraries()))
        {
            if (!Directory.Exists(path))
            {
                continue;
            }

            long bytes = 0;
            int files = 0;
            try
            {
                foreach (var file in new DirectoryInfo(path).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }))
                {
                    bytes += file.Length;
                    files++;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }

            list.Add(new ShaderCacheInfo(name, path, bytes, files));
        }

        return list;
    }

    // ---- Changes -------------------------------------------------------------------------------------------

    public Task<TweakResult> SetTweakAsync(GamingTweak tweak, bool on, GamingSettings settings, CancellationToken cancellationToken) => Task.Run(async () =>
    {
        try
        {
            var result = tweak switch
            {
                GamingTweak.GpuScheduling => SetGpuScheduling(on),
                GamingTweak.UltimatePower => await SetUltimatePowerAsync(on, settings, cancellationToken),
                GamingTweak.GameMode => SetGameMode(on),
                GamingTweak.WindowedOptimizations => SetWindowedOptimizations(on),
                GamingTweak.NoBackgroundRecording => SetBackgroundRecording(!on),
                GamingTweak.MaxGpuPerformance => SetNvidia(on ? NvApi.PreferMaximumPerformance : null, NvApi.PowerManagementMode, "Power management mode"),
                GamingTweak.ShaderCache => SetNvidia(on ? ShaderCacheSize.Normalize(settings.ShaderCacheSize) : null, NvApi.ShaderCacheMaxSize, "Shader cache size"),
                GamingTweak.GSync => SetGSync(on),
                _ => new TweakResult(false, "Unknown setting."),
            };
            Log.Info(LogCategory, $"{tweak} {(on ? "on" : "off")}: {(result.Success ? "done" : "failed")} ({result.Message})");
            return result;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException or InvalidOperationException or TimeoutException or System.ComponentModel.Win32Exception)
        {
            Log.Warn(LogCategory, $"Changing {tweak} failed.", e);
            return new TweakResult(false, "Windows didn't allow the change. Coreloom needs to run as administrator.");
        }
    }, cancellationToken);

    private static TweakResult SetGpuScheduling(bool on)
    {
        using var key = Registry.LocalMachine.CreateSubKey(GpuScheduling.Key, writable: true);
        key.SetValue(GpuScheduling.Value, on ? GpuScheduling.On : GpuScheduling.Off, RegistryValueKind.DWord);
        return new TweakResult(true, "Restart Windows to finish.", NeedsRestart: true);
    }

    private static async Task<TweakResult> SetUltimatePowerAsync(bool on, GamingSettings settings, CancellationToken cancellationToken)
    {
        var plans = await PowerPlansAsync(cancellationToken);
        var remembered = Guid.TryParse(settings.UltimatePowerPlan, out var id) ? id : (Guid?)null;
        var ultimate = PowerPlans.FindUltimate(plans, remembered);
        var active = plans.FirstOrDefault(p => p.IsActive);
        if (on)
        {
            Guid target;
            if (ultimate is null)
            {
                var (exit, output, _) = await WindowsOptimizeService.RunAsync(PowerCfg, $"-duplicatescheme {PowerPlans.UltimateTemplate}", TimeSpan.FromSeconds(20), cancellationToken);
                if (exit != 0 || PowerPlans.ParseCreated(output) is not { } created)
                {
                    return new TweakResult(false, "Windows didn't create the Ultimate Performance plan. PCs with Modern Standby (many laptops) only offer Balanced.");
                }

                target = created;
                settings.UltimatePowerPlan = created.ToString();
            }
            else
            {
                target = ultimate.Id;
            }

            if (active is not null && active.Id != target)
            {
                settings.PreviousPowerPlan = active.Id.ToString();
            }

            var (code, _, _) = await WindowsOptimizeService.RunAsync(PowerCfg, $"/setactive {target}", TimeSpan.FromSeconds(20), cancellationToken);
            return code == 0 ? new TweakResult(true, "Ultimate Performance is active.") : new TweakResult(false, "Windows didn't switch the power plan.");
        }

        var previous = Guid.TryParse(settings.PreviousPowerPlan, out var p) && plans.Any(x => x.Id == p) ? p
            : plans.Any(x => x.Id == PowerPlans.Balanced) ? PowerPlans.Balanced
            : plans.FirstOrDefault(x => x.Id != ultimate?.Id)?.Id;
        if (previous is not { } back)
        {
            return new TweakResult(false, "There's no other power plan to switch to.");
        }

        var (status, _, _) = await WindowsOptimizeService.RunAsync(PowerCfg, $"/setactive {back}", TimeSpan.FromSeconds(20), cancellationToken);
        string name = plans.FirstOrDefault(x => x.Id == back)?.Name ?? "your previous plan";
        return status == 0 ? new TweakResult(true, $"Back on {name}.") : new TweakResult(false, "Windows didn't switch the power plan.");
    }

    private static string PowerCfg => Path.Combine(Environment.SystemDirectory, "powercfg.exe");

    private static async Task<List<PowerPlan>> PowerPlansAsync(CancellationToken cancellationToken)
    {
        try
        {
            var (_, output, _) = await WindowsOptimizeService.RunAsync(PowerCfg, "/list", TimeSpan.FromSeconds(15), cancellationToken);
            return PowerPlans.Parse(output);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or TimeoutException or InvalidOperationException)
        {
            Log.Warn(LogCategory, "Reading the power plans failed.", e);
            return [];
        }
    }

    private static TweakResult SetGameMode(bool on)
    {
        using var key = Registry.CurrentUser.CreateSubKey(GameBarKey, writable: true);
        key.SetValue("AutoGameModeEnabled", on ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue("AllowAutoGameMode", on ? 1 : 0, RegistryValueKind.DWord);
        return new TweakResult(true, on ? "Game Mode is on." : "Game Mode is off.");
    }

    private static TweakResult SetWindowedOptimizations(bool on) =>
        SetDirectX(DirectXUserSettings.SwapEffectUpgrade, on, "Applies to games you start next.");

    /// <summary>One of Windows 11's DirectX settings for games (the others stay as they were).</summary>
    private static TweakResult SetDirectX(string setting, bool on, string message)
    {
        using var key = Registry.CurrentUser.CreateSubKey(DirectXUserSettings.Key, writable: true);
        string value = DirectXUserSettings.Set(key.GetValue(DirectXUserSettings.Value) as string, setting, on);
        key.SetValue(DirectXUserSettings.Value, value, RegistryValueKind.String);
        return new TweakResult(true, message);
    }

    /// <summary>
    /// G-SYNC for fullscreen and windowed games: the driver's variable-refresh feature on, requested and set for both
    /// (NVIDIA Control Panel's "Enable for windowed and full screen mode"), plus Windows 11's variable refresh for games
    /// that don't ask for it themselves. Off puts the driver's own settings (fullscreen only) back.
    /// </summary>
    private static TweakResult SetGSync(bool on)
    {
        if (!NvApi.IsAvailable)
        {
            return new TweakResult(false, "Needs an NVIDIA graphics card and driver.");
        }

        if (!on)
        {
            return NvApi.RestoreGlobals(NvApi.VrrRequestedState, NvApi.VrrMode)
                ? new TweakResult(true, "G-SYNC is back to the driver's default (fullscreen games only).")
                : new TweakResult(false, "The NVIDIA driver didn't accept the change.");
        }

        if (!NvApi.SetGlobals((NvApi.VrrFeature, 1), (NvApi.VrrRequestedState, GSyncSetup.FullscreenAndWindowed), (NvApi.VrrMode, GSyncSetup.FullscreenAndWindowed)))
        {
            return new TweakResult(false, "The NVIDIA driver didn't accept the change.");
        }

        if (Environment.OSVersion.Version.Build >= 22000)
        {
            SetDirectX(DirectXUserSettings.VariableRefreshRate, true, string.Empty);
        }

        return new TweakResult(true, "G-SYNC is on for fullscreen and windowed games you start next.");
    }

    private static TweakResult SetBackgroundRecording(bool record)
    {
        using var key = Registry.CurrentUser.CreateSubKey(GameDvrKey, writable: true);
        key.SetValue("HistoricalCaptureEnabled", record ? 1 : 0, RegistryValueKind.DWord);
        return new TweakResult(true, record ? "Game Bar records in the background again." : "Background recording is off.");
    }

    private static TweakResult SetNvidia(uint? value, uint setting, string name)
    {
        if (!NvApi.IsAvailable)
        {
            return new TweakResult(false, "Needs an NVIDIA graphics card and driver.");
        }

        bool ok = value is { } v ? NvApi.SetGlobal(setting, v) : NvApi.RestoreGlobal(setting);
        return ok ? new TweakResult(true, $"{name} changed in the NVIDIA driver.") : new TweakResult(false, "The NVIDIA driver didn't accept the change.");
    }

    public Task<TweakResult> SetFrameCapAsync(int fps, CancellationToken cancellationToken) => Task.Run(() =>
    {
        if (!NvApi.IsAvailable)
        {
            return new TweakResult(false, "The frame limiter needs an NVIDIA graphics driver.");
        }

        int limit = FrameCap.Clamp(fps);
        bool ok = limit > 0 ? NvApi.SetGlobal(NvApi.FrameRateLimiter, (uint)limit) : NvApi.RestoreGlobal(NvApi.FrameRateLimiter);
        Log.Info(LogCategory, limit > 0 ? $"Frame limit set to {limit} FPS: {(ok ? "done" : "failed")}." : $"Frame limit removed: {(ok ? "done" : "failed")}.");
        return ok
            ? new TweakResult(true, limit > 0 ? $"Games you start now are limited to {limit} FPS." : "The frame limit is off.")
            : new TweakResult(false, "The NVIDIA driver didn't accept the frame limit.");
    }, cancellationToken);

    public Task<IReadOnlyList<string>> FindExecutablesAsync(GameEntry game, CancellationToken cancellationToken) => Task.Run<IReadOnlyList<string>>(() =>
    {
        string? folder = GameLibrary.NormalizeFolder(game.InstallPath);
        if (game.Store == GameStore.Custom && CustomGames.IsBroadFolder(folder))
        {
            folder = null; // a game added from Desktop or Downloads: only its own program, not everything beside it
        }

        var files = new List<(string Path, long Size)>();
        if (folder is not null && folder.Length > 3 && Directory.Exists(folder))
        {
            try
            {
                var options = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 4, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
                foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*.exe", options).Take(600))
                {
                    files.Add((file.FullName, file.Length));
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                Log.Warn(LogCategory, $"Couldn't look through {folder}: {e.Message}");
            }
        }

        // The game's program running now is the surest answer.
        string? running = folder is null ? null : GamingNative.RunningPrograms()
            .Where(p => p.Path.StartsWith(folder.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase) && !GameLibrary.IsNotGame(Path.GetFileName(p.Path)))
            .Select(p => p.Path)
            .FirstOrDefault();
        var picked = GameExecutables.Pick(game, files, running);
        Log.Info(LogCategory, $"Programs of {game.Name}: {(picked.Count == 0 ? "none found" : string.Join(", ", picked))}.");
        return picked;
    }, cancellationToken);

    public Task<TweakResult> SetGameFrameCapAsync(string gameName, IReadOnlyList<string> executables, int fps, CancellationToken cancellationToken) => Task.Run(() =>
    {
        if (!NvApi.IsAvailable)
        {
            return new TweakResult(false, "The frame limiter needs an NVIDIA graphics driver.");
        }

        if (executables.Count == 0)
        {
            return new TweakResult(false, $"Couldn't find {gameName}'s program to set its limit.");
        }

        int limit = FrameCap.Clamp(fps);
        int done = 0;
        foreach (string exe in executables)
        {
            bool ok = limit > 0
                ? NvApi.SetForApplication(exe, gameName, NvApi.FrameRateLimiter, (uint)limit)
                : NvApi.RemoveForApplication(exe, NvApi.FrameRateLimiter);
            done += ok ? 1 : 0;
        }

        Log.Info(LogCategory, limit > 0
            ? $"Frame limit for {gameName} ({string.Join(", ", executables)}) set to {limit} FPS: {done} of {executables.Count}."
            : $"Frame limit for {gameName} removed: {done} of {executables.Count}.");
        return done > 0
            ? new TweakResult(true, limit > 0 ? $"{gameName} is limited to {limit} FPS from its next start." : $"{gameName} follows the all-games setting again.")
            : new TweakResult(false, "The NVIDIA driver didn't accept the frame limit.");
    }, cancellationToken);

    // ---- Registry helpers ----------------------------------------------------------------------------------

    /// <summary>A DWORD value, or 1 ("on", Windows' default for these switches) when it isn't set.</summary>
    private static int ReadDword(RegistryKey hive, string path, string name) => ReadDwordOrNull(hive, path, name) ?? 1;

    private static int? ReadDwordOrNull(RegistryKey hive, string path, string name)
    {
        try
        {
            using var key = hive.OpenSubKey(path);
            return key?.GetValue(name) is int value ? value : null;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }
}
