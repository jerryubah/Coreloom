using System.IO;
using Coreloom.Core.Benchmarks;
using Coreloom.Core.Gaming;
using Coreloom.Core.Hardware;
using Coreloom.Core.History;
using Coreloom.Core.Optimize;
using Coreloom.Core.Overlay;
using Coreloom.Core.Settings;
using Coreloom.Core.Telemetry;
using Coreloom.ViewModels.Infrastructure;
using Coreloom.ViewModels.Pages;
using Coreloom.ViewModels.Services;

namespace Coreloom.PageShots;

/// <summary>A PC like the owner's (Ryzen 9 9950X3D, RTX 5090, 64 GB in two modules, two NVMe drives) with sample readings.</summary>
internal sealed class SampleData
{
    private readonly SampleProvider _provider = new();

    public SampleData()
    {
        Engine = new TelemetryEngine([_provider], new MetricCatalog(), new DeviceRegistry(), new HistoryStore(256), SystemCapabilities.Unknown);
        Engine.InitializeAll();
        Engine.Devices.Upsert(new DeviceInfo(MetricIds.CpuDeviceId, MetricCategory.Cpu, "AMD Ryzen 9 9950X3D 16-Core Processor")
        {
            Vendor = "AMD",
            Properties =
            [
                new("Cores", "16"), new("Threads", "32"), new("L3 cache", "128 MB"),
                new("CPUID", "Family 1Ah · model 44h · stepping 0"),
                new("Instruction sets", "SSE4.2, AVX, AVX2, AVX-512, FMA3, AES, SHA, BMI, VAES"),
                new("Features", "Core Performance Boost, CPPC preferred cores, invariant TSC"),
            ],
        });
        Engine.Devices.Upsert(new DeviceInfo(MetricIds.GpuDeviceId(0), MetricCategory.Gpu, "NVIDIA GeForce RTX 5090") { Vendor = "NVIDIA" });
        for (int disk = 0; disk < 2; disk++)
        {
            Engine.Devices.Upsert(new DeviceInfo(MetricIds.DiskDeviceId(disk), MetricCategory.Storage, disk == 0 ? "Samsung SSD 990 PRO 2TB" : "WD_BLACK SN850X 4TB")
            {
                Kind = "NVMe SSD",
                Properties = [new("Capacity", disk == 0 ? "1.82 TB" : "3.64 TB")],
            });
        }

        foreach (var (letter, name, disk) in new[] { ("c", "Windows", 0), ("d", "Games", 1), ("e", "Media", 1), ("f", "Projects", 1) })
        {
            Engine.Devices.Upsert(new DeviceInfo($"vol/{letter}", MetricCategory.Storage, name) { Kind = "Volume", Properties = [new("Disk number", disk.ToString())] });
        }

        Engine.PollOnce();
        Settings = new SettingsService(new SettingsStore(Path.Combine(Path.GetTempPath(), "coreloom-shots-" + Guid.NewGuid().ToString("N"), "settings.json")), new AppSettings().Normalize());
        Settings.Update("Gaming", s =>
        {
            s.Gaming.CoreScheduling = true;
            s.Gaming.FramePacerTarget = 237;
            s.Gaming.AdaptivePacing = true;
            s.Gaming.CoreProfiles.Add(new CoreProfile { Program = "Cyberpunk2077.exe", Name = "Cyberpunk 2077", FpsGain = 0.07, LowGain = 0.05 });
        });
        Monitor = new MonitorContext
        {
            Engine = Engine,
            Summary = new SystemSummary("JERRY-PC", "Windows 11 Pro", "24H2", "ASRock X870E Nova WiFi", null, "AMD Ryzen 9 9950X3D 16-Core Processor", 16, 32, 64L << 30,
            [
                new MemoryModuleInfo("DIMM 1", "P0 CHANNEL A", 32L << 30, "DDR5", 6000, 6000, "G.Skill", "F5-6000J3036G32G"),
                new MemoryModuleInfo("DIMM 1", "P0 CHANNEL B", 32L << 30, "DDR5", 6000, 6000, "G.Skill", "F5-6000J3036G32G"),
            ]),
            Processes = new SampleProcesses(),
            PrimaryGpuSlot = () => 0,
            Gpus = () => [(0, "NVIDIA GeForce RTX 5090", false)],
            NetworkAdapters = () => [],
            LibreHardwareMonitorIncluded = true,
        };
    }

    public TelemetryEngine Engine { get; }

    public SettingsService Settings { get; }

    public MonitorContext Monitor { get; }

    public T Page<T>(Func<PageContext, T> create)
        where T : PageViewModel
    {
        var page = create(Context());
        page.Activate(Engine.LatestFrame);
        return page;
    }

    public GamingViewModel Gaming() => Page(c => new GamingViewModel(c));

    private PageContext Context() => new()
    {
        Monitor = Monitor,
        Settings = Settings,
        Timeline = new TimelineViewModel(Engine.History),
        Tools = new ToolServices { Gaming = new SampleGaming(), CoreScheduling = new SampleCores(), Optimizer = new SampleOptimize(), Benchmarks = new SampleBenchmarks() },
    };
}

internal sealed class SampleProvider : ITelemetryProvider
{
    private readonly Dictionary<string, MetricHandle> _handles = [];
    private readonly Dictionary<string, (MetricCategory Category, MetricKind Kind, double Value)> _values = [];
    private int _tick;

    public SampleProvider()
    {
        Add(MetricIds.CpuLoad, MetricCategory.Cpu, MetricKind.Load, 23);
        Add(MetricIds.CpuTemperature, MetricCategory.Cpu, MetricKind.Temperature, 61);
        Add(MetricIds.CpuPower, MetricCategory.Cpu, MetricKind.Power, 67);
        Add(MetricIds.CpuClockAverage, MetricCategory.Cpu, MetricKind.Clock, 5250);
        Add(MetricIds.CpuClockEffectiveAverage, MetricCategory.Cpu, MetricKind.Clock, 3900);
        Add(MetricIds.CpuPerformance, MetricCategory.Cpu, MetricKind.Level, 121);
        Add(MetricIds.CpuPerformanceLimit, MetricCategory.Cpu, MetricKind.Level, 100);
        Add(MetricIds.CpuTdc, MetricCategory.Cpu, MetricKind.Current, 52);
        Add(MetricIds.CpuFabricClock, MetricCategory.Cpu, MetricKind.Clock, 2000);
        Add(MetricIds.CpuMemoryClock, MetricCategory.Cpu, MetricKind.Clock, 3000);
        for (int i = 0; i < 32; i++)
        {
            Add(MetricIds.CpuThreadLoad(i), MetricCategory.Cpu, MetricKind.Load, i < 16 ? 30 + (i * 3 % 40) : 4 + (i % 5));
        }

        Add(MetricIds.GpuLoad(0), MetricCategory.Gpu, MetricKind.Load, 87);
        Add(MetricIds.GpuTemperature(0), MetricCategory.Gpu, MetricKind.Temperature, 64);
        Add(MetricIds.GpuPower(0), MetricCategory.Gpu, MetricKind.Power, 410);
        Add(MetricIds.GpuPowerLimit(0), MetricCategory.Gpu, MetricKind.Power, 575);
        Add(MetricIds.GpuCoreClock(0), MetricCategory.Gpu, MetricKind.Clock, 2655);
        Add(MetricIds.GpuMemoryClock(0), MetricCategory.Gpu, MetricKind.Clock, 14001);
        Add(MetricIds.GpuMemoryUsed(0), MetricCategory.Gpu, MetricKind.Data, 11.2 * (1L << 30));
        Add(MetricIds.GpuMemoryTotal(0), MetricCategory.Gpu, MetricKind.Data, 32d * (1L << 30));
        Add(MetricIds.GpuMemoryLoad(0), MetricCategory.Gpu, MetricKind.Load, 35);
        Add(MetricIds.GpuFanPercent(0, 1), MetricCategory.Gpu, MetricKind.Level, 48);
        Add(MetricIds.MemoryUsed, MetricCategory.Memory, MetricKind.Data, 24d * (1L << 30));
        Add(MetricIds.MemoryAvailable, MetricCategory.Memory, MetricKind.Data, 40d * (1L << 30));
        Add(MetricIds.MemoryTotal, MetricCategory.Memory, MetricKind.Data, 64d * (1L << 30));
        Add(MetricIds.MemoryLoad, MetricCategory.Memory, MetricKind.Load, 37.5);
        Add(MetricIds.MemoryCommitUsed, MetricCategory.Memory, MetricKind.Data, 30d * (1L << 30));
        Add(MetricIds.MemoryCommitLimit, MetricCategory.Memory, MetricKind.Data, 72d * (1L << 30));
        Add(MetricIds.MemoryCache, MetricCategory.Memory, MetricKind.Data, 18d * (1L << 30));
        for (int disk = 0; disk < 2; disk++)
        {
            Add(MetricIds.DiskRead(disk), MetricCategory.Storage, MetricKind.Throughput, 12_000_000 * (disk + 1));
            Add(MetricIds.DiskWrite(disk), MetricCategory.Storage, MetricKind.Throughput, 3_000_000);
            Add(MetricIds.DiskActivity(disk), MetricCategory.Storage, MetricKind.Load, 4 + disk);
            Add(MetricIds.DiskTemperature(disk), MetricCategory.Storage, MetricKind.Temperature, 41 + disk);
        }

        foreach (var (letter, total, used) in new[] { ("c", 1990d, 820d), ("d", 2000d, 1610d), ("e", 1000d, 420d), ("f", 1000d, 230d) })
        {
            Add(MetricIds.VolumeTotal(letter), MetricCategory.Storage, MetricKind.Data, total * 1e9);
            Add(MetricIds.VolumeUsed(letter), MetricCategory.Storage, MetricKind.Data, used * 1e9);
            Add(MetricIds.VolumeFree(letter), MetricCategory.Storage, MetricKind.Data, (total - used) * 1e9);
            Add(MetricIds.VolumeLoad(letter), MetricCategory.Storage, MetricKind.Load, used / total * 100);
        }
    }

    public string Name => "Sample";

    public MonitoringSection Section => MonitoringSection.System;

    public int InitStage => 0;

    public TimeSpan MinimumPollInterval => TimeSpan.Zero;

    public ProviderInitResult Initialize(IProviderContext context)
    {
        foreach (var (id, (category, kind, _)) in _values)
        {
            string? device = id.StartsWith("cpu", StringComparison.Ordinal) ? MetricIds.CpuDeviceId
                : id.StartsWith("gpu/0", StringComparison.Ordinal) ? MetricIds.GpuDeviceId(0)
                : id.StartsWith("disk/", StringComparison.Ordinal) ? id[..6]
                : id.StartsWith("vol/", StringComparison.Ordinal) ? id[..5]
                : null;
            _handles[id] = context.Register(new MetricDescriptor(id, id, category, kind) { DeviceId = device }, Provenance.Measured, 10, "sample");
        }

        return ProviderInitResult.Running();
    }

    public void Poll(IMetricWriter writer)
    {
        _tick++;
        foreach (var (id, (_, kind, value)) in _values)
        {
            double wobble = kind is MetricKind.Load or MetricKind.Power or MetricKind.Throughput ? 1 + (0.06 * Math.Sin((_tick + id.Length) * 0.7)) : 1;
            writer.Write(_handles[id], kind == MetricKind.Load ? Math.Clamp(value * wobble, 0, 100) : value * wobble);
        }
    }

    public void Shutdown()
    {
    }

    public void Dispose()
    {
    }

    private void Add(string id, MetricCategory category, MetricKind kind, double value) => _values[id] = (category, kind, value);
}

internal sealed class SampleProcesses : IProcessSampler
{
    private static readonly (string Name, int Count, double Cpu, double Gb)[] Apps =
    [
        ("Cyberpunk2077.exe", 1, 18.4, 9.6), ("chrome.exe", 24, 3.1, 4.2), ("Code.exe", 11, 1.2, 1.9), ("Discord.exe", 7, 0.9, 1.1),
        ("Photoshop.exe", 1, 0.6, 3.4), ("steamwebhelper.exe", 9, 0.4, 0.9), ("explorer.exe", 1, 0.3, 0.4), ("Coreloom.exe", 1, 0.4, 0.12),
        ("MsMpEng.exe", 1, 0.2, 0.35), ("dwm.exe", 1, 1.6, 0.21), ("Spotify.exe", 6, 0.5, 0.62), ("NVIDIA Overlay.exe", 2, 0.1, 0.25),
        ("OneDrive.exe", 1, 0.1, 0.18), ("svchost.exe", 88, 0.8, 1.4), ("Teams.exe", 5, 0.4, 0.95), ("Figma.exe", 4, 0.7, 1.3),
    ];

    public IReadOnlyList<ProcessUsage> Sample() =>
        [.. Apps.Select(a => new ProcessUsage(a.Name, a.Count, a.Cpu, (long)(a.Gb * (1L << 30)), (long)(a.Gb * 0.8 * (1L << 30))))];
}

internal sealed class SampleGaming : IGamingService
{
    private readonly Dictionary<GamingTweak, TweakStatus> _tweaks = new()
    {
        [GamingTweak.GpuScheduling] = new(true, true),
        [GamingTweak.UltimatePower] = new(true, true),
        [GamingTweak.GameMode] = new(true, true),
        [GamingTweak.WindowedOptimizations] = new(true, true),
        [GamingTweak.NoBackgroundRecording] = new(true, true),
        [GamingTweak.MaxGpuPerformance] = new(true, true),
        [GamingTweak.GSync] = new(true, true),
        [GamingTweak.ShaderCache] = new(true, true),
    };

    public bool IsSupported => true;

    public Task<IReadOnlyList<GameEntry>> ScanLibraryAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<GameEntry>>(
    [
        new("steam:1091500", "Cyberpunk 2077", GameStore.Steam) { InstallPath = @"D:\Steam\steamapps\common\Cyberpunk 2077", SizeBytes = 92L << 30, LastPlayed = DateTimeOffset.Now.AddHours(-3) },
        new("epic:Dodo", "Alan Wake 2", GameStore.Epic) { InstallPath = @"D:\Epic\AlanWake2", SizeBytes = 90L << 30, LastPlayed = DateTimeOffset.Now.AddDays(-2) },
        new("steam:1245620", "Elden Ring", GameStore.Steam) { InstallPath = @"D:\Steam\steamapps\common\ELDEN RING", SizeBytes = 60L << 30, LastPlayed = DateTimeOffset.Now.AddDays(-6) },
    ]);

    public Task<GamingStatus> StatusAsync(GamingSettings settings, CancellationToken cancellationToken) => Task.FromResult(new GamingStatus
    {
        Tweaks = new Dictionary<GamingTweak, TweakStatus>(_tweaks),
        HasNvidiaDriver = true,
        FrameCap = 237,
        RefreshRate = 240,
        DriverVersion = "581.57",
        ActivePowerPlan = new PowerPlan(Guid.NewGuid(), "Ultimate Performance", true),
        ShaderCacheLimit = uint.MaxValue,
        ShaderCaches =
        [
            new("DirectX", @"C:\L\D3DSCache", 2_470_000, 40), new("NVIDIA DirectX", @"C:\L\NVIDIA\DXCache", 68_100_000_000, 1200),
            new("NVIDIA OpenGL and Vulkan", @"C:\L\NVIDIA\GLCache", 256, 1), new("AMD DirectX", @"C:\L\AMD\DxCache", 897_000, 3),
            new("AMD DirectX 12", @"C:\L\AMD\DxcCache", 1_130_000, 4), new("AMD Vulkan", @"C:\L\AMD\VkCache", 90_100, 2),
            new("Steam shader pre-cache", @"D:\Steam\steamapps\shadercache", 525_000, 3),
        ],
    });

    public Task<TweakResult> SetTweakAsync(GamingTweak tweak, bool on, GamingSettings settings, CancellationToken cancellationToken)
    {
        _tweaks[tweak] = new TweakStatus(true, on);
        return Task.FromResult(new TweakResult(true, "Done."));
    }

    public Task<TweakResult> SetFrameCapAsync(int fps, CancellationToken cancellationToken) => Task.FromResult(new TweakResult(true, "Done."));

    public Task<IReadOnlyList<string>> FindExecutablesAsync(GameEntry game, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>([]);

    public Task<TweakResult> SetGameFrameCapAsync(string gameName, IReadOnlyList<string> executables, int fps, CancellationToken cancellationToken) => Task.FromResult(new TweakResult(true, "Done."));

    public Task<RunningGame?> FindRunningAsync(IReadOnlyList<GameEntry> games, CancellationToken cancellationToken) => Task.FromResult<RunningGame?>(null);

    public void WatchFrameRates(bool on)
    {
    }

    public FrameRateStats? FrameRate(int processId) => null;

    public bool Launch(GameEntry game) => false;

    public void OpenFolder(string path)
    {
    }

    public Task<IReadOnlyList<GameEntry>> DownloadArtworkAsync(IReadOnlyList<GameEntry> games, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<GameEntry>>([]);
}

internal sealed class SampleCores : ICoreSchedulingService
{
    public bool IsSupported => true;

    public CoreLayout ReadLayout() => CoreLayout.Build(
        [.. Enumerable.Range(0, 16).Select(i => new CoreUnit(0, 3UL << (2 * i), 0))],
        [new CacheUnit(3, 96L << 20, 0, 0xFFFF), new CacheUnit(3, 32L << 20, 0, 0xFFFF_0000)]);

    public AmdVCacheStatus ReadAmdStatus() => new(ServiceState.Running, ServiceState.Running, 0, []);

    public ProcessAffinity? GetAffinity(int processId) => null;

    public bool SetAffinity(int processId, ulong mask) => false;
}

internal sealed class SampleOptimize : IOptimizeService
{
    public bool IsSupported => true;

    public IReadOnlyList<JunkCategory> JunkCategories() => WindowsJunk.Categories(new JunkFolders(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Path.GetTempPath(),
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)));

    public JunkSize RecycleBinSize() => new(1_400_000_000, 212);

    public bool EmptyRecycleBin() => true;

    public Task<MemoryCleanResult> FreeMemoryAsync(bool clearStandby, CancellationToken cancellationToken) => Task.FromResult(new MemoryCleanResult(40L << 30, 44L << 30, 64L << 30, 41));

    public Task<ProtectionStatus> ProtectionAsync(CancellationToken cancellationToken) => Task.FromResult(new ProtectionStatus
    {
        DefenderAvailable = true,
        RealTimeProtection = true,
        AntivirusEnabled = true,
        Mode = "Normal",
        SignaturesUpdated = DateTime.Now.AddHours(-5),
        SignatureVersion = "1.421.112.0",
        LastQuickScan = DateTime.Now.AddDays(-1),
        Products = ["Microsoft Defender Antivirus"],
    });

    public Task<ScanOutcome> QuickScanAsync(CancellationToken cancellationToken) => Task.FromResult(new ScanOutcome(true, 0, null));

    public Task<MaintenanceResult> UpdateSignaturesAsync(CancellationToken cancellationToken) => Task.FromResult(new MaintenanceResult(true, "Up to date."));

    public Task<MaintenanceResult> RemoveThreatsAsync(CancellationToken cancellationToken) => Task.FromResult(new MaintenanceResult(true, "Done."));

    public void OpenSecurity()
    {
    }

    public IReadOnlyList<StartupApp> StartupApps() =>
    [
        new("Discord", "Discord", "Update.exe --processStart Discord.exe", StartupScope.User, true),
        new("Steam", "Steam", "steam.exe -silent", StartupScope.User, true),
        new("Spotify", "Spotify", "Spotify.exe /minimized", StartupScope.User, false),
        new("OneDrive", "OneDrive", "OneDrive.exe /background", StartupScope.User, true),
        new("Microsoft Teams", "Teams", "ms-teams.exe", StartupScope.User, false),
        new("NVIDIA Broadcast", "NvBroadcast", "NVIDIA Broadcast.exe", StartupScope.Machine, true),
    ];

    public bool SetStartupEnabled(StartupApp app, bool enabled) => true;

    public Task<MaintenanceResult> FlushDnsAsync(CancellationToken cancellationToken) => Task.FromResult(new MaintenanceResult(true, "Done."));

    public Task<MaintenanceResult> OptimizeDrivesAsync(CancellationToken cancellationToken) => Task.FromResult(new MaintenanceResult(true, "Done."));
}

/// <summary>
/// Saved results for the Benchmarking page (a 9950X3D and an RTX 5090, some with PBO and a raised power limit), and
/// benchmarks that wait until cancelled, so the running state can be drawn.
/// </summary>
internal sealed class SampleBenchmarks : IHardwareBenchmarkService
{
    private static readonly Coreloom.Core.Formatting.UnitFormatter F = new();

    public bool GpuSupported => true;

    public IReadOnlyList<GpuAdapter> GraphicsCards() => [new("NVIDIA GeForce RTX 5090", 0x10DE, 0x2B85, 32d * (1L << 30), 1, false)];

    public async Task<CpuBenchmarkScores> RunCpuAsync(CpuBenchmarkOptions options, IProgress<BenchmarkProgress> progress, CancellationToken cancellationToken)
    {
        progress.Report(new BenchmarkProgress("Ray tracing", 0.56, "Multi-core · 32 threads"));
        await Task.Delay(Timeout.Infinite, cancellationToken);
        throw new OperationCanceledException();
    }

    public async Task<GpuBenchmarkScores> RunGpuAsync(GpuAdapter? card, IProgress<BenchmarkProgress> progress, CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        throw new OperationCanceledException();
    }

    public IStressLoad ProcessorStress(string device) => new SampleLoad("Processor", device);

    public IStressLoad? GraphicsStress(GpuAdapter? card) => new SampleLoad("Graphics card", card?.Name ?? "GPU");

    public IReadOnlyList<BenchmarkResult> LoadResults()
    {
        var now = DateTimeOffset.Now;
        return
        [
            Stress(now.AddMinutes(-40), passed: false),
            Gpu(now.AddHours(-2), 1.0, raisedLimit: true),
            Cpu(now.AddHours(-3), 1.0, pbo: true),
            Stress(now.AddDays(-1), passed: true),
            Cpu(now.AddDays(-2), 0.93, pbo: false),
            Gpu(now.AddDays(-3), 0.95, raisedLimit: false),
            Cpu(now.AddDays(-9), 0.91, pbo: false),
        ];
    }

    public void SaveResults(IReadOnlyList<BenchmarkResult> results)
    {
    }

    private static BenchmarkSystem System => new()
    {
        Processor = "AMD Ryzen 9 9950X3D", Cores = 16, Threads = 32, GraphicsCard = "NVIDIA GeForce RTX 5090", GraphicsDriver = "581.57",
        GraphicsMemory = 32d * (1L << 30), Memory = 64d * (1L << 30), MemorySpeed = "6,000 MT/s", Motherboard = "ASRock X870E Nova WiFi",
        Bios = "3.25", OperatingSystem = "Windows 11 Pro 24H2",
    };

    private static BenchmarkResult Cpu(DateTimeOffset when, double scale, bool pbo)
    {
        double[] single = [2410, 2290, 2620, 2180, 2350, 2470, 2550, 2400];
        double[] multi = [34800, 39500, 41200, 30100, 38900, 47800, 36400, 44100];
        var tests = CpuTests.All().Select((t, i) => new BenchmarkTestResult(t.Id, t.Name, t.Category, t.Unit,
            multi[i] * scale * t.Reference / 1000, multi[i] * scale, single[i] * scale * t.Reference / 1000, single[i] * scale)).ToList();
        var readings = new ReadingRecorder();
        foreach (double v in new[] { 5050, 5120, 5180 })
        {
            readings.Add(BenchmarkReadings.Processor, BenchmarkReadings.Clock, MetricKind.Clock, v * (pbo ? 1.02 : 1));
        }

        foreach (var (name, kind, values) in new (string, MetricKind, double[])[]
        {
            (BenchmarkReadings.FastestCore, MetricKind.Clock, [5640, 5720, pbo ? 5810 : 5745]),
            (BenchmarkReadings.Temperature, MetricKind.Temperature, [62, 78, pbo ? 89 : 84]),
            (BenchmarkReadings.Power, MetricKind.Power, [96, pbo ? 238 : 199, pbo ? 246 : 201]),
            (BenchmarkReadings.Voltage, MetricKind.Voltage, [1.12, 1.24, pbo ? 1.36 : 1.33]),
            (BenchmarkReadings.Load, MetricKind.Load, [6, 100, 100]),
            (BenchmarkReadings.Performance, MetricKind.Level, [125, 118, 121]),
            (BenchmarkReadings.PerformanceLimit, MetricKind.Level, [100, 100, pbo ? 100 : 96]),
        })
        {
            foreach (double v in values)
            {
                readings.Add(BenchmarkReadings.Processor, name, kind, v);
            }
        }

        double[] watts = pbo ? [180, 246, 248, 251, 250, 252, 249] : [180, 228, 229, 230, 229, 230, 229];
        return new BenchmarkResult
        {
            Kind = BenchmarkKind.Cpu,
            Finished = when,
            AppVersion = "1.5.0",
            Score = CpuBenchmark.GeometricMean(tests.Select(t => t.Score)),
            Subscores = [new("Single-core", CpuBenchmark.GeometricMean(tests.Select(t => t.SingleScore!.Value))), new("Multi-core", CpuBenchmark.GeometricMean(tests.Select(t => t.Score)))],
            Device = "AMD Ryzen 9 9950X3D",
            Tests = tests,
            Readings = readings.Readings(),
            Tuning = BenchmarkTuning.Processor(readings, AmdProcessors.Create("AMD Ryzen 9 9950X3D 16-Core Processor"), watts, true, F),
            System = System,
            Seconds = 61,
            Threads = 32,
            MemoryBandwidth = 71.4,
            MemoryLatency = 74.8,
        };
    }

    private static BenchmarkResult Gpu(DateTimeOffset when, double scale, bool raisedLimit)
    {
        double[] scores = [24800, 27100, 38400, 14600, 31200];
        var tests = GpuTests.All.Select((t, i) => new BenchmarkTestResult(t.Id, t.Name, t.Category, t.Unit, scores[i] * scale * t.Reference / 1000, scores[i] * scale)).ToList();
        var readings = new ReadingRecorder();
        foreach (var (name, kind, values) in new (string, MetricKind, double[])[]
        {
            (BenchmarkReadings.Clock, MetricKind.Clock, [2602, 2745, raisedLimit ? 2880 : 2805]),
            (BenchmarkReadings.MemoryClock, MetricKind.Clock, [14001, 14001, 14001]),
            (BenchmarkReadings.Temperature, MetricKind.Temperature, [48, 66, 71]),
            (BenchmarkReadings.HotSpot, MetricKind.Temperature, [58, 77, 83]),
            (BenchmarkReadings.Power, MetricKind.Power, [120, 512, raisedLimit ? 598 : 571]),
            (BenchmarkReadings.PowerLimit, MetricKind.Power, [raisedLimit ? 600 : 575]),
            (BenchmarkReadings.Voltage, MetricKind.Voltage, [0.89, 0.98, 1.05]),
            (BenchmarkReadings.Load, MetricKind.Load, [96, 99, 100]),
            (BenchmarkReadings.Fan, MetricKind.Control, [42, 55, 61]),
        })
        {
            foreach (double v in values)
            {
                readings.Add(BenchmarkReadings.Graphics, name, kind, v);
            }
        }

        var card = new DeviceInfo(MetricIds.GpuDeviceId(0), MetricCategory.Gpu, "NVIDIA GeForce RTX 5090")
        {
            Properties = [new("Max core clock", "3,090 MHz"), new("Max memory clock", "14,001 MHz"), new("Default power limit", "575 W")],
        };
        var (overall, graphics, compute) = GpuTests.Scores(tests);
        return new BenchmarkResult
        {
            Kind = BenchmarkKind.Gpu,
            Finished = when,
            AppVersion = "1.5.0",
            Score = overall,
            Subscores = [new("Graphics", graphics), new("Compute", compute)],
            Device = "NVIDIA GeForce RTX 5090",
            Tests = tests,
            Readings = readings.Readings(),
            Tuning = BenchmarkTuning.Graphics(readings, card, 0x4, F),
            System = System,
            Seconds = 24,
        };
    }

    private static BenchmarkResult Stress(DateTimeOffset when, bool passed)
    {
        var loops = Enumerable.Range(0, 30).Select(i => passed ? 100 - (i % 7 == 3 ? 1.4 : (i % 5) * 0.3) : Math.Max(88, 100 - (i * 0.45))).ToList();
        var readings = new ReadingRecorder();
        readings.Add(BenchmarkReadings.Processor, BenchmarkReadings.Temperature, MetricKind.Temperature, 71);
        readings.Add(BenchmarkReadings.Processor, BenchmarkReadings.Temperature, MetricKind.Temperature, passed ? 86 : 95);
        readings.Add(BenchmarkReadings.Processor, BenchmarkReadings.Clock, MetricKind.Clock, passed ? 5150 : 4720);
        readings.Add(BenchmarkReadings.Processor, BenchmarkReadings.Clock, MetricKind.Clock, 5230);
        return new BenchmarkResult
        {
            Kind = BenchmarkKind.Stress,
            Finished = when,
            AppVersion = "1.5.0",
            Score = Math.Round(StressScoring.Stability(loops), 1),
            Device = "AMD Ryzen 9 9950X3D",
            Target = "Processor",
            Readings = readings.Readings(),
            System = System,
            Seconds = 300,
            Threads = 32,
            Loops = StressScoring.Relative(loops),
            Passed = passed,
            Verdict = passed ? "Passed" : $"Failed: stability {StressScoring.Stability(loops):0.0} % is under 97 % (the hardware slowed down as it heated up)",
        };
    }

    /// <summary>Work at a slightly wavering rate, like real hardware.</summary>
    private sealed class SampleLoad(string name, string device) : IStressLoad
    {
        private readonly System.Diagnostics.Stopwatch _clock = new();

        public string Name { get; } = name;

        public string Device { get; } = device;

        public long Work => (long)(_clock.Elapsed.TotalSeconds * 1000 * (1 - (0.006 * Math.Sin(_clock.Elapsed.TotalSeconds))));

        public int Errors => 0;

        public string? Failure => null;

        public void Start() => _clock.Start();

        public void Stop() => _clock.Stop();

        public void Dispose() => Stop();
    }
}
