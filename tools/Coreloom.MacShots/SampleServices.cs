using Coreloom.Core.Devices;
using Coreloom.ViewModels.Services;

namespace Coreloom.MacShots;

/// <summary>A Mac's Bluetooth accessories and what's plugged in, for the Devices page.</summary>
internal sealed class SampleDevices : IDeviceService
{
    public bool IsSupported => true;

    public event EventHandler? Changed
    {
        add { }
        remove { }
    }

    public Task<BluetoothRadioInfo> RadioAsync(CancellationToken cancellationToken) => Task.FromResult(new BluetoothRadioInfo(true, true, true));

    public Task<bool> SetRadioAsync(bool on, CancellationToken cancellationToken) => Task.FromResult(true);

    public Task<IReadOnlyList<BluetoothDeviceInfo>> BluetoothDevicesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<BluetoothDeviceInfo>>(
    [
        new("1", "Jerry's AirPods Pro", 0xAC_DE_48_00_11_22, BluetoothTransport.Classic, DeviceKind.Earbuds, true) { Battery = 82 },
        new("2", "Magic Keyboard with Touch ID", 0xAC_DE_48_00_11_23, BluetoothTransport.LowEnergy, DeviceKind.Keyboard, true) { Battery = 64 },
        new("3", "Magic Trackpad", 0xAC_DE_48_00_11_24, BluetoothTransport.LowEnergy, DeviceKind.Mouse, true) { Battery = 18 },
        new("4", "Xbox Wireless Controller", 0xAC_DE_48_00_11_25, BluetoothTransport.LowEnergy, DeviceKind.Controller, false),
        new("5", "Living Room Speaker", 0xAC_DE_48_00_11_26, BluetoothTransport.Classic, DeviceKind.Speaker, false),
    ]);

    public Task<DeviceActionResult> ConnectAsync(BluetoothDeviceInfo device, CancellationToken cancellationToken) => Task.FromResult(new DeviceActionResult(true, "Connected."));

    public Task<DeviceActionResult> DisconnectAsync(BluetoothDeviceInfo device, CancellationToken cancellationToken) => Task.FromResult(new DeviceActionResult(true, "Disconnected."));

    public Task<IReadOnlyList<WiredDevice>> WiredDevicesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<WiredDevice>>(
    [
        new("d1", "Studio Display", DeviceKind.Display, "Thunderbolt") { Manufacturer = "Apple" },
        new("c1", "Studio Display Camera", DeviceKind.Camera, "USB") { Manufacturer = "Apple" },
        new("s1", "Samsung T7 Shield", DeviceKind.Storage, "USB 3.2 Gen 2") { EjectInstanceId = "disk5" },
        new("h1", "CalDigit TS4", DeviceKind.Hub, "Thunderbolt 4"),
        new("a1", "Focusrite Scarlett 2i2", DeviceKind.Audio, "USB"),
    ]);

    public Task<DeviceActionResult> EjectAsync(WiredDevice device, CancellationToken cancellationToken) => Task.FromResult(new DeviceActionResult(true, "Ejected. It's safe to unplug."));

    public void OpenBluetoothSettings()
    {
    }
}

/// <summary>Smart Optimize on a Mac: the junk categories, macOS's protections, login items and maintenance.</summary>
internal sealed class SampleOptimizer : IOptimizeService
{
    private static readonly string Home = Path.Combine(Path.GetTempPath(), "coreloom-macshots-home");

    public bool IsSupported => true;

    public IReadOnlyList<Coreloom.Core.Optimize.JunkCategory> JunkCategories() =>
        Coreloom.Core.Optimize.MacJunk.Categories(new Coreloom.Core.Optimize.MacJunkFolders(Home, null, null), _ => []);

    public Coreloom.Core.Optimize.JunkSize RecycleBinSize() => new(1_840_000_000, 212);

    public bool EmptyRecycleBin() => true;

    public Task<Coreloom.Core.Optimize.MemoryCleanResult> FreeMemoryAsync(bool clearStandby, CancellationToken cancellationToken) =>
        Task.FromResult(new Coreloom.Core.Optimize.MemoryCleanResult(30L << 30, 33L << 30, 64L << 30, 0));

    public Task<Coreloom.Core.Optimize.ProtectionStatus> ProtectionAsync(CancellationToken cancellationToken) => Task.FromResult(new Coreloom.Core.Optimize.ProtectionStatus
    {
        RealTimeProtection = true,
        AntivirusEnabled = true,
        SignatureVersion = "XProtect 5321",
        SignaturesUpdated = DateTime.Now.AddDays(-2),
        Checks =
        [
            new("xprotect", "XProtect", true, "Apple's built-in malware protection: checks apps and downloads as they open, and removes known malware.", true),
            new("gatekeeper", "Gatekeeper", true, "Only opens apps from the App Store and identified developers unless you allow one.", true),
            new("sip", "System Integrity Protection", true, "Keeps even administrators from changing macOS's own files.", true),
            new("filevault", "FileVault", true, "Encrypts the startup disk, so your files are safe if the Mac is lost.", false),
            new("firewall", "Firewall", false, "Blocks incoming connections to apps you haven't allowed.", false),
        ],
    });

    public Task<Coreloom.Core.Optimize.ScanOutcome> QuickScanAsync(CancellationToken cancellationToken) => Task.FromResult(new Coreloom.Core.Optimize.ScanOutcome(true, 0, null));

    public Task<Coreloom.Core.Optimize.MaintenanceResult> UpdateSignaturesAsync(CancellationToken cancellationToken) => Task.FromResult(new Coreloom.Core.Optimize.MaintenanceResult(true, "Up to date"));

    public Task<Coreloom.Core.Optimize.MaintenanceResult> RemoveThreatsAsync(CancellationToken cancellationToken) => Task.FromResult(new Coreloom.Core.Optimize.MaintenanceResult(true, "None"));

    public void OpenSecurity()
    {
    }

    public IReadOnlyList<Coreloom.Core.Optimize.StartupApp> StartupApps() =>
    [
        new("Coreloom", "com.jerryubah.coreloom", "/Applications/Coreloom.app", Coreloom.Core.Optimize.StartupScope.MacUserAgent, true) { Publisher = "Jerry Ubah" },
        new("Dropbox", "com.dropbox.DropboxMacUpdate.agent", "/Applications/Dropbox.app", Coreloom.Core.Optimize.StartupScope.MacUserAgent, true) { Publisher = "Dropbox" },
        new("Google Updater", "com.google.GoogleUpdater.wake", "~/Library/Application Support/Google/GoogleUpdater", Coreloom.Core.Optimize.StartupScope.MacUserAgent, true) { Publisher = "Google" },
        new("Steam", "com.valvesoftware.steamclean", "/Applications/Steam.app", Coreloom.Core.Optimize.StartupScope.MacUserAgent, false) { Publisher = "Valve" },
        new("Adobe Creative Cloud", "com.adobe.AdobeCreativeCloud", "/Applications/Utilities/Adobe Creative Cloud", Coreloom.Core.Optimize.StartupScope.MacSystemAgent, true) { Publisher = "Adobe" },
        new("Zoom", "us.zoom.updater", "/Applications/zoom.us.app", Coreloom.Core.Optimize.StartupScope.MacSystemAgent, true) { Publisher = "Zoom" },
    ];

    public bool SetStartupEnabled(Coreloom.Core.Optimize.StartupApp app, bool enabled) => true;

    public Task<Coreloom.Core.Optimize.MaintenanceResult> FlushDnsAsync(CancellationToken cancellationToken) => Task.FromResult(new Coreloom.Core.Optimize.MaintenanceResult(true, "Done"));

    public Task<Coreloom.Core.Optimize.MaintenanceResult> OptimizeDrivesAsync(CancellationToken cancellationToken) => Task.FromResult(new Coreloom.Core.Optimize.MaintenanceResult(true, "Done"));
}

/// <summary>The Benchmarking page with a few saved runs on an M4 Max (CPU, GPU on Metal, and a stress test).</summary>
internal sealed class SampleBenchmarks : IHardwareBenchmarkService
{
    private List<Coreloom.Core.Benchmarks.BenchmarkResult> _results =
    [
        new()
        {
            Kind = Coreloom.Core.Benchmarks.BenchmarkKind.Cpu, Finished = DateTimeOffset.Now.AddHours(-3), AppVersion = "1.6.0", PointsAtReference = 1000,
            Score = 24_810, Subscores = [new("Single-core", 2_412), new("Multi-core", 24_810)], Device = "Apple M4 Max", Threads = 16, Seconds = 64,
            MemoryBandwidth = 118.4, MemoryLatency = 96,
        },
        new()
        {
            Kind = Coreloom.Core.Benchmarks.BenchmarkKind.Gpu, Finished = DateTimeOffset.Now.AddHours(-3).AddMinutes(2), AppVersion = "1.6.0", PointsAtReference = 10_000,
            Score = 61_350, Subscores = [new("Graphics", 74_120), new("Compute", 54_480)], Device = "Apple M4 Max", Seconds = 41,
        },
        new()
        {
            Kind = Coreloom.Core.Benchmarks.BenchmarkKind.Stress, Finished = DateTimeOffset.Now.AddDays(-1), AppVersion = "1.6.0",
            Score = 99.1, Device = "Apple M4 Max", Target = "Processor and graphics card", Seconds = 600, Passed = true, Verdict = "Passed",
            Loops = [100, 99.4, 99.2, 99.6, 98.9, 99.1, 99.3, 99.0, 98.8, 99.2],
        },
    ];

    public bool GpuSupported => true;

    public IReadOnlyList<Coreloom.Core.Benchmarks.GpuAdapter> GraphicsCards() => [new("Apple M4 Max", 0x106B, 0, 0, 1, false)];

    public Task<Coreloom.Core.Benchmarks.CpuBenchmarkScores> RunCpuAsync(Coreloom.Core.Benchmarks.CpuBenchmarkOptions options, IProgress<Coreloom.Core.Benchmarks.BenchmarkProgress> progress, CancellationToken cancellationToken) =>
        Task.FromException<Coreloom.Core.Benchmarks.CpuBenchmarkScores>(new NotSupportedException());

    public Task<Coreloom.Core.Benchmarks.GpuBenchmarkScores> RunGpuAsync(Coreloom.Core.Benchmarks.GpuAdapter? card, IProgress<Coreloom.Core.Benchmarks.BenchmarkProgress> progress, CancellationToken cancellationToken) =>
        Task.FromException<Coreloom.Core.Benchmarks.GpuBenchmarkScores>(new NotSupportedException());

    public Coreloom.Core.Benchmarks.IStressLoad ProcessorStress(string device) => new Coreloom.Core.Benchmarks.CpuStressLoad(device);

    public Coreloom.Core.Benchmarks.IStressLoad? GraphicsStress(Coreloom.Core.Benchmarks.GpuAdapter? card) => null;

    public IReadOnlyList<Coreloom.Core.Benchmarks.BenchmarkResult> LoadResults() => _results;

    public void SaveResults(IReadOnlyList<Coreloom.Core.Benchmarks.BenchmarkResult> results) => _results = [.. results];
}

/// <summary>
/// The Gaming page on a Mac: a small library from Steam, Epic Games and the Applications folder (invented games, with
/// abstract pictures drawn here), one of them running, and the macOS boosts.
/// </summary>
internal sealed class SampleGaming : IGamingService
{
    private static readonly string Art = Path.Combine(Path.GetTempPath(), "coreloom-macshots-art");
    private readonly List<Coreloom.Core.Gaming.GameEntry> _games;

    public SampleGaming()
    {
        Directory.CreateDirectory(Art);
        var now = DateTimeOffset.Now;
        _games =
        [
            Game("steam:1", "Ember Road", Coreloom.Core.Gaming.GameStore.Steam, 84L << 30, now.AddMinutes(-20), Picture("ember", "#FF7A45", "#5B1A3A", cover: true), Picture("ember-wide", "#FF7A45", "#5B1A3A", cover: false)),
            Game("steam:2", "Starfall Tactics", Coreloom.Core.Gaming.GameStore.Steam, 41L << 30, now.AddDays(-2), Picture("starfall", "#5B8CFF", "#141A46", cover: true), Picture("starfall-wide", "#5B8CFF", "#141A46", cover: false)),
            Game("epic:3", "Hollow Peaks", Coreloom.Core.Gaming.GameStore.Epic, 63L << 30, now.AddDays(-9), Picture("hollow", "#3CCB8F", "#0F3B33", cover: true), Picture("hollow-wide", "#3CCB8F", "#0F3B33", cover: false)),
            Game("app:4", "Tidecaller", Coreloom.Core.Gaming.GameStore.AppStore, 7L << 30, now.AddDays(-30), null, null),
            Game("steam:5", "Neon Drift", Coreloom.Core.Gaming.GameStore.Steam, 22L << 30, null, null, null),
            Game("custom:6", "Quiet Orbit", Coreloom.Core.Gaming.GameStore.Custom, 3L << 30, now.AddDays(-4), null, null) with { Executable = "/Applications/Quiet Orbit.app" },
        ];
    }

    public bool IsSupported => true;

    private static Coreloom.Core.Gaming.GameEntry Game(string id, string name, Coreloom.Core.Gaming.GameStore store, long size, DateTimeOffset? played, string? cover, string? banner) =>
        new(id, name, store)
        {
            InstallPath = $"/Applications/{name}.app",
            SizeBytes = size,
            LastPlayed = played,
            Launch = $"/Applications/{name}.app",
            Artwork = new Coreloom.Core.Gaming.GameArtwork(cover, banner, null),
        };

    /// <summary>An abstract picture: two colors, a sun and a horizon (no real game's art).</summary>
    private static string Picture(string name, string light, string dark, bool cover)
    {
        string file = Path.Combine(Art, name + ".png");
        if (File.Exists(file))
        {
            return file;
        }

        var size = cover ? new Avalonia.PixelSize(600, 900) : new Avalonia.PixelSize(920, 430);
        using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(size);
        using (var dc = bitmap.CreateDrawingContext())
        {
            var a = Avalonia.Media.Color.Parse(light);
            var b = Avalonia.Media.Color.Parse(dark);
            var rect = new Avalonia.Rect(0, 0, size.Width, size.Height);
            dc.FillRectangle(new Avalonia.Media.LinearGradientBrush
            {
                StartPoint = new Avalonia.RelativePoint(0, 0, Avalonia.RelativeUnit.Relative),
                EndPoint = new Avalonia.RelativePoint(0, 1, Avalonia.RelativeUnit.Relative),
                GradientStops = { new(b, 0), new(a, 0.75), new(b, 1) },
            }, rect);
            double sun = size.Width * 0.22;
            dc.DrawEllipse(new Avalonia.Media.SolidColorBrush(Avalonia.Media.Colors.White, 0.85), null, new Avalonia.Point(size.Width * 0.62, size.Height * 0.48), sun, sun);
            var hills = new Avalonia.Media.StreamGeometry();
            using (var g = hills.Open())
            {
                g.BeginFigure(new Avalonia.Point(0, size.Height * 0.72), true);
                g.QuadraticBezierTo(new Avalonia.Point(size.Width * 0.3, size.Height * 0.55), new Avalonia.Point(size.Width * 0.55, size.Height * 0.7));
                g.QuadraticBezierTo(new Avalonia.Point(size.Width * 0.8, size.Height * 0.82), new Avalonia.Point(size.Width, size.Height * 0.66));
                g.LineTo(new Avalonia.Point(size.Width, size.Height));
                g.LineTo(new Avalonia.Point(0, size.Height));
                g.EndFigure(true);
            }

            dc.DrawGeometry(new Avalonia.Media.SolidColorBrush(b, 0.92), null, hills);
        }

        bitmap.Save(file);
        return file;
    }

    public Task<IReadOnlyList<Coreloom.Core.Gaming.GameEntry>> ScanLibraryAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Coreloom.Core.Gaming.GameEntry>>(_games);

    public Task<Coreloom.Core.Gaming.GamingStatus> StatusAsync(Coreloom.Core.Settings.GamingSettings settings, CancellationToken cancellationToken) =>
        Task.FromResult(new Coreloom.Core.Gaming.GamingStatus
        {
            Tweaks = new Dictionary<Coreloom.Core.Gaming.GamingTweak, Coreloom.Core.Gaming.TweakStatus>
            {
                [Coreloom.Core.Gaming.GamingTweak.GameMode] = new(true, true),
                [Coreloom.Core.Gaming.GamingTweak.PowerMode] = new(true, false),
                [Coreloom.Core.Gaming.GamingTweak.KeepAwake] = new(true, true),
                [Coreloom.Core.Gaming.GamingTweak.MetalHud] = new(true, false),
            },
            RefreshRate = 120,
            DriverVersion = "Metal · macOS 15.1",
            ShaderCaches =
            [
                new("Metal: com.valvesoftware.steam", "~/Library/Caches/com.apple.metal/com.valvesoftware.steam", 1_420_000_000, 812),
                new("Metal: com.epicgames.EpicGamesLauncher", "~/Library/Caches/com.apple.metal/com.epicgames.EpicGamesLauncher", 386_000_000, 240),
                new("Metal: com.example.tidecaller", "~/Library/Caches/com.apple.metal/com.example.tidecaller", 92_000_000, 61),
                new("Metal: com.apple.Safari", "~/Library/Caches/com.apple.metal/com.apple.Safari", 18_400_000, 22),
            ],
        });

    public Task<Coreloom.Core.Gaming.TweakResult> SetTweakAsync(Coreloom.Core.Gaming.GamingTweak tweak, bool on, Coreloom.Core.Settings.GamingSettings settings, CancellationToken cancellationToken) =>
        Task.FromResult(new Coreloom.Core.Gaming.TweakResult(true, on ? "Turned on." : "Turned off."));

    public Task<Coreloom.Core.Gaming.TweakResult> SetFrameCapAsync(int fps, CancellationToken cancellationToken) =>
        Task.FromResult(new Coreloom.Core.Gaming.TweakResult(false, "Not on a Mac."));

    public Task<IReadOnlyList<string>> FindExecutablesAsync(Coreloom.Core.Gaming.GameEntry game, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>([]);

    public Task<Coreloom.Core.Gaming.TweakResult> SetGameFrameCapAsync(string gameName, IReadOnlyList<string> executables, int fps, CancellationToken cancellationToken) =>
        Task.FromResult(new Coreloom.Core.Gaming.TweakResult(false, "Not on a Mac."));

    public Task<Coreloom.Core.Gaming.RunningGame?> FindRunningAsync(IReadOnlyList<Coreloom.Core.Gaming.GameEntry> games, CancellationToken cancellationToken) =>
        Task.FromResult<Coreloom.Core.Gaming.RunningGame?>(games.FirstOrDefault(g => g.Id == "steam:1") is { } game ? new Coreloom.Core.Gaming.RunningGame(game, [4242]) : null);

    public void WatchFrameRates(bool on)
    {
    }

    public Coreloom.Core.Overlay.FrameRateStats? FrameRate(int processId) => null;

    public bool Launch(Coreloom.Core.Gaming.GameEntry game) => true;

    public void OpenFolder(string path)
    {
    }

    public Task<IReadOnlyList<Coreloom.Core.Gaming.GameEntry>> DownloadArtworkAsync(IReadOnlyList<Coreloom.Core.Gaming.GameEntry> games, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Coreloom.Core.Gaming.GameEntry>>([]);

    public bool FileExists(string path) => true;

    public string? PickProgram(string? startIn = null) => "/Applications/Lantern Keep.app";

    public Coreloom.Core.Gaming.ProgramInfo? DescribeProgram(string path) => new(path, Path.GetFileNameWithoutExtension(path));
}
