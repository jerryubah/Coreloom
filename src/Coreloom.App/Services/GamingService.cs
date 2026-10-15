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
/// does). Nothing is ever added to games. Cover pictures are downloaded only after the user asks.
/// </summary>
internal sealed class WindowsGamingService : IGamingService
{
    private const string LogCategory = "Gaming";
    private const string GameBarKey = @"Software\Microsoft\GameBar";
    private const string GameDvrKey = @"Software\Microsoft\Windows\CurrentVersion\GameDVR";
    private const string FrameRateOwner = "gaming";

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

    /// <summary>Adds pictures downloaded earlier and an icon (as a picture file) for games that have nothing better.</summary>
    private GameEntry WithLocalArtwork(GameEntry game)
    {
        var art = game.Artwork;
        if (art.Cover is null && File.Exists(ArtworkFile(game)))
        {
            art = art with { Cover = ArtworkFile(game) };
        }

        if (art.Cover is null && art.Banner is null && art.Icon is null && game.Executable is { } exe && File.Exists(exe))
        {
            art = art with { Icon = IconFile(exe) };
        }

        return game with { Artwork = art };
    }

    private string ArtworkFile(GameEntry game) => Path.Combine(_artworkDirectory, $"{game.Store.ToString().ToLowerInvariant()}-{Hash(game.StoreId)}.jpg");

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

    public async Task<IReadOnlyList<GameEntry>> DownloadArtworkAsync(IReadOnlyList<GameEntry> games, CancellationToken cancellationToken)
    {
        var http = Http();
        var result = new List<GameEntry>();
        Directory.CreateDirectory(_artworkDirectory);
        foreach (var game in games)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string target = ArtworkFile(game);
            if (File.Exists(target))
            {
                result.Add(game with { Artwork = game.Artwork with { Cover = target } });
                continue;
            }

            foreach (string url in game.CoverUrls)
            {
                try
                {
                    using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    string? type = response.Content.Headers.ContentType?.MediaType;
                    if (!response.IsSuccessStatusCode || type is null || !type.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    byte[] bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                    if (bytes.Length < 1024 || bytes.Length > 16 << 20)
                    {
                        continue;
                    }

                    string temp = target + ".part";
                    await File.WriteAllBytesAsync(temp, bytes, cancellationToken);
                    File.Move(temp, target, overwrite: true);
                    result.Add(game with { Artwork = game.Artwork with { Cover = target } });
                    break;
                }
                catch (Exception e) when (e is HttpRequestException or IOException or UnauthorizedAccessException || (e is TaskCanceledException && !cancellationToken.IsCancellationRequested))
                {
                    Log.Debug(LogCategory, $"Cover for {game.Name} not downloaded from {new Uri(url).Host}: {e.Message}");
                }
            }
        }

        Log.Info(LogCategory, $"Downloaded {result.Count} of {games.Count} missing covers.");
        return result;
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
        var tweaks = new Dictionary<GamingTweak, TweakStatus>
        {
            [GamingTweak.GpuScheduling] = GpuSchedulingStatus(),
            [GamingTweak.UltimatePower] = await UltimatePowerStatusAsync(settings, cancellationToken),
            [GamingTweak.GameMode] = new(true, ReadDword(Registry.CurrentUser, GameBarKey, "AutoGameModeEnabled") != 0),
            [GamingTweak.WindowedOptimizations] = Environment.OSVersion.Version.Build >= 22000
                ? new(true, DirectXUserSettings.IsOn(Registry.CurrentUser.OpenSubKey(DirectXUserSettings.Key)?.GetValue(DirectXUserSettings.Value) as string, DirectXUserSettings.SwapEffectUpgrade))
                : TweakStatus.Unsupported("Needs Windows 11."),
            [GamingTweak.NoBackgroundRecording] = new(true, (ReadDwordOrNull(Registry.CurrentUser, GameDvrKey, "HistoricalCaptureEnabled") ?? 0) != 1), // off unless turned on
        };

        bool nvidia = NvApi.IsAvailable;
        int frameCap = 0;
        string? driver = null;
        if (nvidia)
        {
            driver = NvApi.DriverVersion();
            uint? pstate = NvApi.GetGlobal(NvApi.PowerManagementMode, 5);
            uint? cache = NvApi.GetGlobal(NvApi.ShaderCacheMaxSize, 0x4000);
            frameCap = (int)(NvApi.GetGlobal(NvApi.FrameRateLimiter, 0) ?? 0);
            tweaks[GamingTweak.MaxGpuPerformance] = pstate is null ? TweakStatus.Unsupported("The NVIDIA driver didn't answer.") : new(true, pstate == NvApi.PreferMaximumPerformance);
            tweaks[GamingTweak.UnlimitedShaderCache] = cache is null ? TweakStatus.Unsupported("The NVIDIA driver didn't answer.") : new(true, cache == NvApi.ShaderCacheUnlimited);
        }
        else
        {
            tweaks[GamingTweak.MaxGpuPerformance] = TweakStatus.Unsupported("Needs an NVIDIA graphics card. AMD Software sets this per game.");
            tweaks[GamingTweak.UnlimitedShaderCache] = TweakStatus.Unsupported("Needs an NVIDIA graphics card. AMD Software sets the shader cache in its graphics settings.");
        }

        return new GamingStatus
        {
            Tweaks = tweaks,
            HasNvidiaDriver = nvidia,
            FrameCap = frameCap > FrameCap.Maximum ? FrameCap.Maximum : frameCap,
            RefreshRate = DisplayInfo.MaxRefreshRate(),
            DriverVersion = driver,
            ShaderCaches = ShaderCacheSizes(),
        };
    }, cancellationToken);

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

    private static async Task<TweakStatus> UltimatePowerStatusAsync(GamingSettings settings, CancellationToken cancellationToken)
    {
        var plans = await PowerPlansAsync(cancellationToken);
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
                GamingTweak.UnlimitedShaderCache => SetNvidia(on ? NvApi.ShaderCacheUnlimited : null, NvApi.ShaderCacheMaxSize, "Shader cache size"),
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

    private static TweakResult SetWindowedOptimizations(bool on)
    {
        using var key = Registry.CurrentUser.CreateSubKey(DirectXUserSettings.Key, writable: true);
        string value = DirectXUserSettings.Set(key.GetValue(DirectXUserSettings.Value) as string, DirectXUserSettings.SwapEffectUpgrade, on);
        key.SetValue(DirectXUserSettings.Value, value, RegistryValueKind.String);
        return new TweakResult(true, "Applies to games you start next.");
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
            return new TweakResult(false, "The frame pacer needs an NVIDIA graphics driver.");
        }

        int limit = FrameCap.Clamp(fps);
        bool ok = limit > 0 ? NvApi.SetGlobal(NvApi.FrameRateLimiter, (uint)limit) : NvApi.RestoreGlobal(NvApi.FrameRateLimiter);
        Log.Info(LogCategory, limit > 0 ? $"Frame limit set to {limit} FPS: {(ok ? "done" : "failed")}." : $"Frame limit removed: {(ok ? "done" : "failed")}.");
        return ok
            ? new TweakResult(true, limit > 0 ? $"Games you start now are limited to {limit} FPS." : "The frame limit is off.")
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
