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
