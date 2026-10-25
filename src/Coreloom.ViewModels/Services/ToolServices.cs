using Coreloom.Core.Benchmarks;
using Coreloom.Core.Devices;
using Coreloom.Core.Gaming;
using Coreloom.Core.Optimize;
using Coreloom.Core.Overlay;
using Coreloom.Core.Settings;

namespace Coreloom.ViewModels.Services;

/// <summary>Measures drive speed (the Storage page's benchmark).</summary>
public interface IDiskBenchmarkService
{
    IReadOnlyList<BenchmarkDrive> Drives();

    Task<DiskBenchmarkResult> RunAsync(DiskBenchmarkOptions options, IProgress<DiskBenchmarkProgress> progress, CancellationToken cancellationToken);
}

/// <summary>
/// The Benchmarking page's work: the CPU benchmark, the GPU benchmark (Direct3D 11, Windows only), the stress test
/// loads, and the saved results.
/// </summary>
public interface IHardwareBenchmarkService
{
    /// <summary>False where the GPU benchmark can't run (no Direct3D: tests, other systems).</summary>
    bool GpuSupported { get; }

    /// <summary>The graphics cards the GPU benchmark can run on, best first.</summary>
    IReadOnlyList<GpuAdapter> GraphicsCards();

    Task<CpuBenchmarkScores> RunCpuAsync(CpuBenchmarkOptions options, IProgress<BenchmarkProgress> progress, CancellationToken cancellationToken);

    Task<GpuBenchmarkScores> RunGpuAsync(GpuAdapter? card, IProgress<BenchmarkProgress> progress, CancellationToken cancellationToken);

    /// <summary>A processor stress load (not started).</summary>
    IStressLoad ProcessorStress(string device);

    /// <summary>A graphics card stress load (not started), or null where the GPU benchmark can't run.</summary>
    IStressLoad? GraphicsStress(GpuAdapter? card);

    IReadOnlyList<BenchmarkResult> LoadResults();

    void SaveResults(IReadOnlyList<BenchmarkResult> results);
}

/// <summary>The CPU benchmark and stress test from Coreloom.Core, results kept in memory, and no GPU benchmark.</summary>
public sealed class PortableBenchmarkService : IHardwareBenchmarkService
{
    private List<BenchmarkResult> _saved = [];

    public bool GpuSupported => false;

    public IReadOnlyList<GpuAdapter> GraphicsCards() => [];

    public Task<CpuBenchmarkScores> RunCpuAsync(CpuBenchmarkOptions options, IProgress<BenchmarkProgress> progress, CancellationToken cancellationToken) =>
        CpuBenchmark.RunAsync(options, progress, cancellationToken);

    public Task<GpuBenchmarkScores> RunGpuAsync(GpuAdapter? card, IProgress<BenchmarkProgress> progress, CancellationToken cancellationToken) =>
        Task.FromException<GpuBenchmarkScores>(new NotSupportedException("The GPU benchmark needs Direct3D 11 on Windows."));

    public IStressLoad ProcessorStress(string device) => new CpuStressLoad(device);

    public IStressLoad? GraphicsStress(GpuAdapter? card) => null;

    public IReadOnlyList<BenchmarkResult> LoadResults() => _saved;

    public void SaveResults(IReadOnlyList<BenchmarkResult> results) => _saved = [.. results];
}

/// <summary>Measures the internet connection (the Network page's speed test).</summary>
public interface ISpeedTestService
{
    Task<SpeedTestResult> RunAsync(IProgress<SpeedTestProgress> progress, CancellationToken cancellationToken);
}

/// <summary>What Smart Optimize needs from Windows: junk locations, memory, Microsoft Defender, startup apps and maintenance.</summary>
public interface IOptimizeService
{
    /// <summary>False where Windows features are missing (tests, other systems): the page explains instead of offering actions.</summary>
    bool IsSupported { get; }

    IReadOnlyList<JunkCategory> JunkCategories();

    /// <summary>Size of the Recycle Bin on all drives.</summary>
    JunkSize RecycleBinSize();

    bool EmptyRecycleBin();

    /// <summary>Trims the memory of background apps (and, if asked, clears the standby cache).</summary>
    Task<MemoryCleanResult> FreeMemoryAsync(bool clearStandby, CancellationToken cancellationToken);

    Task<ProtectionStatus> ProtectionAsync(CancellationToken cancellationToken);

    Task<ScanOutcome> QuickScanAsync(CancellationToken cancellationToken);

    Task<MaintenanceResult> UpdateSignaturesAsync(CancellationToken cancellationToken);

    Task<MaintenanceResult> RemoveThreatsAsync(CancellationToken cancellationToken);

    /// <summary>Opens Windows Security (virus and threat protection).</summary>
    void OpenSecurity();

    IReadOnlyList<StartupApp> StartupApps();

    bool SetStartupEnabled(StartupApp app, bool enabled);

    Task<MaintenanceResult> FlushDnsAsync(CancellationToken cancellationToken);

    Task<MaintenanceResult> OptimizeDrivesAsync(CancellationToken cancellationToken);
}

/// <summary>
/// The Devices page's view of Windows: the Bluetooth adapter and paired devices (with connect and disconnect), and the
/// devices plugged in by wire (with "safely remove" for drives).
/// </summary>
public interface IDeviceService
{
    /// <summary>False where Windows' device APIs aren't available; the page explains instead.</summary>
    bool IsSupported { get; }

    /// <summary>Raised (on any thread) when a Bluetooth device connects or disconnects, or the adapter turns on or off.</summary>
    event EventHandler? Changed;

    Task<BluetoothRadioInfo> RadioAsync(CancellationToken cancellationToken);

    Task<bool> SetRadioAsync(bool on, CancellationToken cancellationToken);

    /// <summary>Paired Bluetooth devices, connected first.</summary>
    Task<IReadOnlyList<BluetoothDeviceInfo>> BluetoothDevicesAsync(CancellationToken cancellationToken);

    /// <summary>Connects (or reconnects, e.g. headphones that switched to a phone) and waits briefly for the link.</summary>
    Task<DeviceActionResult> ConnectAsync(BluetoothDeviceInfo device, CancellationToken cancellationToken);

    Task<DeviceActionResult> DisconnectAsync(BluetoothDeviceInfo device, CancellationToken cancellationToken);

    /// <summary>Devices plugged in by USB, display cables or Thunderbolt.</summary>
    Task<IReadOnlyList<WiredDevice>> WiredDevicesAsync(CancellationToken cancellationToken);

    /// <summary>"Safely remove" for drives.</summary>
    Task<DeviceActionResult> EjectAsync(WiredDevice device, CancellationToken cancellationToken);

    /// <summary>
    /// macOS: asks the system for Bluetooth access (its own "would like to use Bluetooth" prompt), on the person's click;
    /// true once the answer is in. Nothing to ask elsewhere.
    /// </summary>
    Task<bool> RequestBluetoothAccessAsync(CancellationToken cancellationToken) => Task.FromResult(true);

    /// <summary>Opens Windows' Bluetooth settings (to pair a new device).</summary>
    void OpenBluetoothSettings();
}

/// <summary>Stand-in where Windows' device APIs aren't available.</summary>
public sealed class UnsupportedDeviceService : IDeviceService
{
    public bool IsSupported => false;

    public event EventHandler? Changed
    {
        add { }
        remove { }
    }

    public Task<BluetoothRadioInfo> RadioAsync(CancellationToken cancellationToken) => Task.FromResult(BluetoothRadioInfo.None);

    public Task<bool> SetRadioAsync(bool on, CancellationToken cancellationToken) => Task.FromResult(false);

    public Task<IReadOnlyList<BluetoothDeviceInfo>> BluetoothDevicesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<BluetoothDeviceInfo>>([]);

    public Task<DeviceActionResult> ConnectAsync(BluetoothDeviceInfo device, CancellationToken cancellationToken) => Unavailable();

    public Task<DeviceActionResult> DisconnectAsync(BluetoothDeviceInfo device, CancellationToken cancellationToken) => Unavailable();

    public Task<IReadOnlyList<WiredDevice>> WiredDevicesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<WiredDevice>>([]);

    public Task<DeviceActionResult> EjectAsync(WiredDevice device, CancellationToken cancellationToken) => Unavailable();

    public void OpenBluetoothSettings()
    {
    }

    private static Task<DeviceActionResult> Unavailable() => Task.FromResult(new DeviceActionResult(false, "Not available on this system."));
}

/// <summary>
/// The Gaming page's view of Windows: installed games from every launcher, the Windows and graphics-driver settings
/// that help games, the driver's frame limiter, shader caches and the game that is running.
/// </summary>
public interface IGamingService
{
    /// <summary>False where Windows isn't available (tests, other systems); the page explains instead.</summary>
    bool IsSupported { get; }

    /// <summary>Installed games from Steam, Epic, GOG, Xbox, Ubisoft, EA, Battle.net and Riot, with the pictures found on this PC.</summary>
    Task<IReadOnlyList<GameEntry>> ScanLibraryAsync(CancellationToken cancellationToken);

    /// <summary>The state of every tweak, the frame limit, the display and the shader caches.</summary>
    Task<GamingStatus> StatusAsync(GamingSettings settings, CancellationToken cancellationToken);

    /// <summary>Turns a tweak on or off. May record what to restore later in <paramref name="settings"/> (the power plans).</summary>
    Task<TweakResult> SetTweakAsync(GamingTweak tweak, bool on, GamingSettings settings, CancellationToken cancellationToken);

    /// <summary>Sets the graphics driver's frame-rate limit for all games (0 turns it off).</summary>
    Task<TweakResult> SetFrameCapAsync(int fps, CancellationToken cancellationToken);

    /// <summary>The game's program files the driver should match (see <see cref="GameExecutables.Pick"/>).</summary>
    Task<IReadOnlyList<string>> FindExecutablesAsync(GameEntry game, CancellationToken cancellationToken);

    /// <summary>Sets the driver's frame-rate limit for these programs only (0 removes it, so the all-games limit applies again).</summary>
    Task<TweakResult> SetGameFrameCapAsync(string gameName, IReadOnlyList<string> executables, int fps, CancellationToken cancellationToken);

    /// <summary>The library game that is running now, if any.</summary>
    Task<RunningGame?> FindRunningAsync(IReadOnlyList<GameEntry> games, CancellationToken cancellationToken);

    /// <summary>Measures frame rates while true (Windows' graphics events, nothing is added to games).</summary>
    void WatchFrameRates(bool on);

    /// <summary>The frame rate of a process, when measured.</summary>
    FrameRateStats? FrameRate(int processId);

    /// <summary>Starts a game through its launcher (as the signed-in user, not as administrator).</summary>
    bool Launch(GameEntry game);

    void OpenFolder(string path);

    /// <summary>
    /// Downloads pictures for games that have none on this PC (automatically after a scan); returns the games a picture
    /// was found for.
    /// </summary>
    Task<IReadOnlyList<GameEntry>> DownloadArtworkAsync(IReadOnlyList<GameEntry> games, CancellationToken cancellationToken);

    /// <summary>Adds pictures kept on this PC (downloaded earlier, or the program's icon) to games added by hand.</summary>
    IReadOnlyList<GameEntry> AddLocalPictures(IReadOnlyList<GameEntry> games) => games;

    /// <summary>The program's version info, or null when it doesn't exist.</summary>
    ProgramInfo? DescribeProgram(string path) => File.Exists(path) ? new ProgramInfo(path) : null;

    /// <summary>Asks for a game's program (.exe); null when cancelled.</summary>
    string? PickProgram(string? startIn = null) => null;

    /// <summary>Asks for a picture (.png, .jpg, .ico); null when cancelled.</summary>
    string? PickPicture() => null;

    bool FileExists(string path) => File.Exists(path);
}

/// <summary>Stand-in where Windows isn't available: no games, nothing to switch.</summary>
public sealed class UnsupportedGamingService : IGamingService
{
    public bool IsSupported => false;

    public Task<IReadOnlyList<GameEntry>> ScanLibraryAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<GameEntry>>([]);

    public Task<GamingStatus> StatusAsync(GamingSettings settings, CancellationToken cancellationToken) => Task.FromResult(GamingStatus.Unavailable);

    public Task<TweakResult> SetTweakAsync(GamingTweak tweak, bool on, GamingSettings settings, CancellationToken cancellationToken) => Unavailable();

    public Task<TweakResult> SetFrameCapAsync(int fps, CancellationToken cancellationToken) => Unavailable();

    public Task<IReadOnlyList<string>> FindExecutablesAsync(GameEntry game, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>([]);

    public Task<TweakResult> SetGameFrameCapAsync(string gameName, IReadOnlyList<string> executables, int fps, CancellationToken cancellationToken) => Unavailable();

    public Task<RunningGame?> FindRunningAsync(IReadOnlyList<GameEntry> games, CancellationToken cancellationToken) => Task.FromResult<RunningGame?>(null);

    public void WatchFrameRates(bool on)
    {
    }

    public FrameRateStats? FrameRate(int processId) => null;

    public bool Launch(GameEntry game) => false;

    public void OpenFolder(string path)
    {
    }

    public Task<IReadOnlyList<GameEntry>> DownloadArtworkAsync(IReadOnlyList<GameEntry> games, CancellationToken cancellationToken) => Task.FromResult(games);

    private static Task<TweakResult> Unavailable() => Task.FromResult(new TweakResult(false, "Not available on this system."));
}

public sealed class DiskBenchmarkService : IDiskBenchmarkService
{
    public IReadOnlyList<BenchmarkDrive> Drives() => DiskBenchmark.Drives();

    public Task<DiskBenchmarkResult> RunAsync(DiskBenchmarkOptions options, IProgress<DiskBenchmarkProgress> progress, CancellationToken cancellationToken) =>
        Task.Run(() => DiskBenchmark.RunAsync(options, null, progress, cancellationToken), cancellationToken);
}

public sealed class SpeedTestService(string? userAgent = null) : ISpeedTestService
{
    public Task<SpeedTestResult> RunAsync(IProgress<SpeedTestProgress> progress, CancellationToken cancellationToken) =>
        Task.Run(() => new InternetSpeedTest(userAgent: userAgent).RunAsync(progress, cancellationToken), cancellationToken);
}

/// <summary>Stand-in where Smart Optimize can't work: nothing to clean, nothing changes.</summary>
public sealed class UnsupportedOptimizeService : IOptimizeService
{
    public bool IsSupported => false;

    public IReadOnlyList<JunkCategory> JunkCategories() => [];

    public JunkSize RecycleBinSize() => default;

    public bool EmptyRecycleBin() => false;

    public Task<MemoryCleanResult> FreeMemoryAsync(bool clearStandby, CancellationToken cancellationToken) => Task.FromResult(new MemoryCleanResult(0, 0, 0, 0));

    public Task<ProtectionStatus> ProtectionAsync(CancellationToken cancellationToken) => Task.FromResult(ProtectionStatus.Unavailable("Not available on this system."));

    public Task<ScanOutcome> QuickScanAsync(CancellationToken cancellationToken) => Task.FromResult(new ScanOutcome(false, 0, "Not available on this system."));

    public Task<MaintenanceResult> UpdateSignaturesAsync(CancellationToken cancellationToken) => Unavailable();

    public Task<MaintenanceResult> RemoveThreatsAsync(CancellationToken cancellationToken) => Unavailable();

    public void OpenSecurity()
    {
    }

    public IReadOnlyList<StartupApp> StartupApps() => [];

    public bool SetStartupEnabled(StartupApp app, bool enabled) => false;

    public Task<MaintenanceResult> FlushDnsAsync(CancellationToken cancellationToken) => Unavailable();

    public Task<MaintenanceResult> OptimizeDrivesAsync(CancellationToken cancellationToken) => Unavailable();

    private static Task<MaintenanceResult> Unavailable() => Task.FromResult(new MaintenanceResult(false, "Not available on this system."));
}

/// <summary>
/// What intelligent CPU core scheduling needs from Windows: the processor's core layout, AMD's 3D V-Cache driver
/// (read only), and a game's cores (its affinity, as Task Manager's "Set affinity" changes it; nothing is added to the game).
/// </summary>
public interface ICoreSchedulingService
{
    bool IsSupported { get; }

    /// <summary>The cores, grouped by what they're good at (read once; it doesn't change while Windows runs).</summary>
    CoreLayout ReadLayout();

    /// <summary>AMD's 3D V-Cache driver and service, its default preference and per-program preferences.</summary>
    AmdVCacheStatus ReadAmdStatus();

    /// <summary>A process's cores, or null when it can't be read (it ended, or its anti-cheat refuses).</summary>
    ProcessAffinity? GetAffinity(int processId);

    /// <summary>Sets a process's cores; false when Windows (or the game's anti-cheat) refuses.</summary>
    bool SetAffinity(int processId, ulong mask);
}

/// <summary>Stand-in where Windows isn't available: no layout, nothing to change.</summary>
public sealed class UnsupportedCoreSchedulingService : ICoreSchedulingService
{
    public bool IsSupported => false;

    public CoreLayout ReadLayout() => CoreLayout.Unknown;

    public AmdVCacheStatus ReadAmdStatus() => AmdVCacheStatus.None;

    public ProcessAffinity? GetAffinity(int processId) => null;

    public bool SetAffinity(int processId, ulong mask) => false;
}

/// <summary>The tools the pages offer (benchmarks, speed test, Smart Optimize, devices, gaming). Tests swap in fakes.</summary>
public sealed class ToolServices
{
    public IHardwareBenchmarkService Benchmarks { get; init; } = new PortableBenchmarkService();

    public IDiskBenchmarkService DiskBenchmark { get; init; } = new DiskBenchmarkService();

    public ISpeedTestService SpeedTest { get; init; } = new SpeedTestService();

    public IOptimizeService Optimizer { get; init; } = new UnsupportedOptimizeService();

    public IDeviceService Devices { get; init; } = new UnsupportedDeviceService();

    public IGamingService Gaming { get; init; } = new UnsupportedGamingService();

    public ICoreSchedulingService CoreScheduling { get; init; } = new UnsupportedCoreSchedulingService();
}
