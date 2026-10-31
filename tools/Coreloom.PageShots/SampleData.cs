using System.IO;
using Coreloom.Core.Apps;
using Coreloom.Core.Benchmarks;
using Coreloom.Core.Devices;
using Coreloom.Core.Drivers;
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

    private readonly SampleClock? _clock;

    /// <param name="sampleClock">Readings a second apart however fast they're taken (so a few minutes of charts take no time).</param>
    public SampleData(bool sampleClock = false)
    {
        _clock = sampleClock ? new SampleClock() : null;
        Engine = new TelemetryEngine([_provider], new MetricCatalog(), new DeviceRegistry(), new HistoryStore(256), SystemCapabilities.Unknown, clock: _clock);
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
            s.Gaming.VramManagement = true;
            s.Gaming.CacheOptimization = true;
            s.Gaming.ShaderCacheSize = ShaderCacheSize.Unlimited; // what the sample driver has
            s.Gaming.KeepVram(new VramRecord { GameId = "epic:Dodo", Name = "Alan Wake 2", TotalMb = 32607, PeakGameMb = 17100, PeakInUseMb = 19400, PeakSharedMb = 310, Minutes = 95, Sessions = 2, LastPlayed = DateTime.UtcNow.AddDays(-2) });
            s.Gaming.KeepVram(new VramRecord { GameId = "steam:1091500", Name = "Cyberpunk 2077", TotalMb = 32607, PeakGameMb = 13900, PeakInUseMb = 15800, PeakSharedMb = 240, Minutes = 140, Sessions = 3, LastPlayed = DateTime.UtcNow.AddHours(-3) });
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

    /// <summary>The sample tools (games, core scheduling, clean-up, benchmarks).</summary>
    public ToolServices Tools => new() { Gaming = new SampleGaming(), CoreScheduling = new SampleCores(), Optimizer = new SampleOptimize(), Benchmarks = new SampleBenchmarks(), Devices = new SampleDevices(), Drivers = new SampleDrivers(), Apps = new SampleApps() };

    /// <summary>Takes a new reading (the sample values drift a little each time), a second after the last with the sample clock.</summary>
    public TelemetryFrame Poll()
    {
        _clock?.Advance(1000);
        return Engine.PollOnce();
    }

    private sealed class SampleClock : IMonotonicClock
    {
        private readonly DateTime _start = DateTime.Now.AddMinutes(-5);

        public long NowMilliseconds { get; private set; } = 1_000_000;

        public DateTime LocalNow => _start.AddMilliseconds(NowMilliseconds - 1_000_000);

        public void Advance(long milliseconds) => NowMilliseconds += milliseconds;
    }

    private PageContext Context() => new()
    {
        Monitor = Monitor,
        Settings = Settings,
        Timeline = new TimelineViewModel(Engine.History),
        Tools = Tools,
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
        [GamingTweak.KeepShaderCaches] = new(true, true),
    };

    public bool IsSupported => true;

    public Task<IReadOnlyList<GameEntry>> ScanLibraryAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<GameEntry>>(
    [
        new("steam:1091500", "Cyberpunk 2077", GameStore.Steam) { InstallPath = @"D:\Steam\steamapps\common\Cyberpunk 2077", SizeBytes = 92L << 30, LastPlayed = DateTimeOffset.Now.AddHours(-3) },
        new("epic:Dodo", "Alan Wake 2", GameStore.Epic) { InstallPath = @"D:\Epic\AlanWake2", SizeBytes = 90L << 30, LastPlayed = DateTimeOffset.Now.AddDays(-2) },
        new("steam:1245620", "Elden Ring", GameStore.Steam) { InstallPath = @"D:\Steam\steamapps\common\ELDEN RING", SizeBytes = 60L << 30, LastPlayed = DateTimeOffset.Now.AddDays(-6) },
        new("steam:1895880", "Ratchet & Clank: Rift Apart", GameStore.Steam) { InstallPath = @"D:\Steam\steamapps\common\Ratchet & Clank - Rift Apart", SizeBytes = 75L << 30, LastPlayed = DateTimeOffset.Now.AddDays(-9) },
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

/// <summary>The sample PC's drivers and the updates on offer, for the Drivers page (since 1.8.6).</summary>
internal sealed class SampleDrivers : IDriverService
{
    private static InstalledDriver D(string name, string cls, string provider, string version, int year, int month, int day) =>
        new($@"SAMPLE\{name}", name, cls, provider, version, new DateTime(year, month, day));

    public bool IsSupported => true;

    public Task<IReadOnlyList<InstalledDriver>> InstalledAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<InstalledDriver>>(
    [
        D("NVIDIA GeForce RTX 5090", "Display", "NVIDIA", "32.0.15.8157", 2026, 9, 30),
        D("AMD Radeon(TM) Graphics", "Display", "Advanced Micro Devices, Inc.", "32.0.21013.1000", 2026, 7, 2),
        D("NVIDIA High Definition Audio", "MEDIA", "NVIDIA", "1.4.3.2", 2025, 6, 11),
        D("Realtek USB Audio", "MEDIA", "Realtek", "6.3.9600.2393", 2025, 11, 4),
        D("Focusrite USB Audio", "MEDIA", "Focusrite Audio Engineering Ltd.", "4.143.0.261", 2025, 9, 18),
        D("Realtek Gaming 5GbE Family Controller", "Net", "Realtek", "10.72.0.0", 2026, 4, 9),
        D("Qualcomm FastConnect 7800 Wi-Fi 7", "Net", "Qualcomm Technologies, Inc.", "3.1.0.1415", 2026, 2, 27),
        D("Qualcomm FastConnect 7800 Bluetooth Adapter", "Bluetooth", "Qualcomm Technologies, Inc.", "10.0.0.1250", 2025, 12, 3),
        D("Samsung SSD 990 PRO 4TB", "DiskDrive", "Microsoft", "10.0.26100.1", 2006, 6, 21),
        D("Standard NVM Express Controller", "SCSIAdapter", "Microsoft", "10.0.26100.4202", 2006, 6, 21),
        D("AMD SATA Controller", "HDC", "Advanced Micro Devices, Inc.", "9.3.2.158", 2024, 3, 18),
        D("AMD USB 3.20 eXtensible Host Controller", "USB", "Advanced Micro Devices, Inc.", "1.0.0.183", 2025, 1, 22),
        D("USB Root Hub (USB 3.0)", "USB", "(Standard USB HUBs)", "10.0.26100.1", 2006, 6, 21),
        D("USB Root Hub (USB 3.0)", "USB", "(Standard USB HUBs)", "10.0.26100.1", 2006, 6, 21),
        D("Logitech G Pro X Superlight 2", "HIDClass", "Logitech", "1.10.82.0", 2025, 8, 5),
        D("HID Keyboard Device", "Keyboard", "Microsoft", "10.0.26100.1", 2006, 6, 21),
        D("Xbox Wireless Controller", "XnaComposite", "Microsoft", "10.0.26100.2033", 2006, 6, 21),
        D("AMD GPIO Controller", "System", "Advanced Micro Devices, Inc", "2.2.0.130", 2024, 1, 2),
        D("AMD PSP 11.0 Device", "SecurityDevices", "Advanced Micro Devices Inc.", "5.39.0.0", 2025, 3, 3),
        D("AMD 3D V-Cache Performance Optimizer", "System", "Advanced Micro Devices, Inc.", "1.0.0.11", 2025, 10, 9),
        D("AMD SMBus", "System", "Advanced Micro Devices, Inc", "5.12.0.44", 2023, 12, 11),
        D("Odyssey OLED G8", "Monitor", "Samsung", "1.0.0.0", 2024, 9, 1),
        D("Elgato Facecam MK.2", "Camera", "Elgato", "1.0.0.22", 2025, 5, 14),
        D("System Firmware", "Firmware", "ASRock", "3.30.0.0", 2026, 8, 12),
    ]);

    public Task<DriverScan> CheckAsync(IReadOnlyList<InstalledDriver> installed, IProgress<string>? progress, CancellationToken cancellationToken) => Task.FromResult(new DriverScan(
    [
        new DriverUpdate("nvidia", "NVIDIA GeForce RTX 5090", DriverSource.Nvidia, DriverCategory.Graphics)
        {
            Provider = "NVIDIA", CurrentVersion = "581.57", NewVersion = "621.01", Date = new DateTime(2026, 10, 21), SizeText = "1.04 GB",
            DetailsUrl = "https://www.nvidia.com/en-us/drivers/details/282412/",
        },
        new DriverUpdate("u1", "Qualcomm FastConnect 7800 Bluetooth Adapter", DriverSource.WindowsUpdate, DriverCategory.Bluetooth)
        {
            Provider = "Qualcomm Technologies, Inc.", CurrentVersion = "10.0.0.1250", NewVersion = "10.0.0.1301", Date = new DateTime(2026, 9, 8), SizeBytes = 21_400_000,
        },
        new DriverUpdate("u2", "Realtek Gaming 5GbE Family Controller", DriverSource.WindowsUpdate, DriverCategory.Network)
        {
            Provider = "Realtek", CurrentVersion = "10.72.0.0", NewVersion = "10.76.0.0", Date = new DateTime(2026, 8, 26), SizeBytes = 2_900_000,
        },
        DirectXRuntime.AsUpdate(["XInput 1.3 (controllers)", "XAudio 2.7 (sound)"])!,
    ], []));

    public Task<DriverInstallResult> InstallAsync(DriverUpdate update, IProgress<string>? progress, CancellationToken cancellationToken) =>
        Task.FromResult(new DriverInstallResult(true, "Installed."));

    public bool OpenNvidiaApp() => true;

    public void OpenUrl(string url)
    {
    }
}

/// <summary>The sample PC's apps, for the Applications page (since 1.8.6).</summary>
internal sealed class SampleApps : IAppService
{
    private static InstalledApp A(string name, string publisher, string version, double gb, int year, int month, AppKind kind = AppKind.Desktop) =>
        new((kind == AppKind.Store ? "store:" : "hklm:") + name, name, kind)
        {
            Publisher = publisher,
            Version = version,
            SizeBytes = (long)(gb * (1L << 30)),
            Installed = new DateTime(year, month, 3),
            Uninstall = "uninstall.exe",
            InstallLocation = $@"C:\Program Files\{name}",
        };

    public bool IsSupported => true;

    public Task<IReadOnlyList<InstalledApp>> InstalledAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<InstalledApp>>(
    [
        A("Adobe Photoshop 2026", "Adobe Inc.", "27.4.0", 4.8, 2026, 9),
        A("Microsoft Visual Studio Code", "Microsoft Corporation", "1.105.1", 0.42, 2026, 10),
        A("Steam", "Valve Corporation", "2.10.91.91", 1.1, 2024, 5),
        A("Discord", "Discord Inc.", "1.0.9200", 0.36, 2026, 9),
        A("Spotify", "Spotify AB", "1.2.74.477", 0.41, 2026, 6, AppKind.Store),
        A("Microsoft OneDrive", "Microsoft Corporation", "25.180.0914.0002", 0.29, 2026, 9),
        A("Microsoft Teams", "Microsoft Corporation", "25290.205.3964.4258", 0.47, 2026, 10, AppKind.Store),
        A("NVIDIA Broadcast", "NVIDIA Corporation", "2.0.2.0", 2.6, 2025, 11),
        A("Blender", "Blender Foundation", "4.5.3", 1.3, 2026, 8),
        A("OBS Studio", "OBS Project", "32.0.1", 0.52, 2026, 9),
        A("Figma", "Figma, Inc.", "125.8.6", 0.33, 2026, 10),
        A("7-Zip 25.01 (x64)", "Igor Pavlov", "25.01", 0.006, 2025, 8),
        A("Epic Games Launcher", "Epic Games, Inc.", "1.3.143.0", 0.85, 2024, 12),
        A("Xbox", "Microsoft Corporation", "2510.1001.21.0", 0.18, 2026, 10, AppKind.Store),
    ]);

    public Task<IReadOnlyDictionary<string, long>> MeasureAsync(IReadOnlyList<InstalledApp> apps, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<string, long>>(new Dictionary<string, long>());

    public string? IconFor(InstalledApp app) => null;

    public Task<AppUninstallResult> UninstallAsync(InstalledApp app, CancellationToken cancellationToken) => Task.FromResult(new AppUninstallResult(true, "Uninstalled."));

    public void OpenFolder(string path)
    {
    }
}

/// <summary>A PC's Bluetooth devices and what's plugged in, for the Devices page (since 1.8.5: Connect is the accent button again).</summary>
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
        new("1", "WH-1000XM5", 0xAC_DE_48_00_11_22, BluetoothTransport.Classic, DeviceKind.Headphones, true) { Battery = 82 },
        new("2", "MX Keys S", 0xAC_DE_48_00_11_23, BluetoothTransport.LowEnergy, DeviceKind.Keyboard, true) { Battery = 64 },
        new("3", "Xbox Wireless Controller", 0xAC_DE_48_00_11_25, BluetoothTransport.LowEnergy, DeviceKind.Controller, false),
        new("4", "Living Room Speaker", 0xAC_DE_48_00_11_26, BluetoothTransport.Classic, DeviceKind.Speaker, false),
    ]);

    public Task<DeviceActionResult> ConnectAsync(BluetoothDeviceInfo device, CancellationToken cancellationToken) => Task.FromResult(new DeviceActionResult(true, "Connected."));

    public Task<DeviceActionResult> DisconnectAsync(BluetoothDeviceInfo device, CancellationToken cancellationToken) => Task.FromResult(new DeviceActionResult(true, "Disconnected."));

    public Task<IReadOnlyList<WiredDevice>> WiredDevicesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<WiredDevice>>(
    [
        new("d1", "Odyssey OLED G8", DeviceKind.Display, "DisplayPort") { Manufacturer = "Samsung" },
        new("s1", "Samsung T7 Shield", DeviceKind.Storage, "USB 3.2 Gen 2") { EjectInstanceId = @"USBSTOR\Disk" },
        new("a1", "Focusrite Scarlett 2i2", DeviceKind.Audio, "USB"),
    ]);

    public Task<DeviceActionResult> EjectAsync(WiredDevice device, CancellationToken cancellationToken) => Task.FromResult(new DeviceActionResult(true, "Ejected. It's safe to unplug."));

    public void OpenBluetoothSettings()
    {
    }
}

internal sealed class SampleCores : ICoreSchedulingService
{
    public bool IsSupported => true;

    public CoreLayout ReadLayout() => CoreLayout.Build(
        [.. Enumerable.Range(0, 16).Select(i => new CoreUnit(0, 3UL << (2 * i), 0))],
        [new CacheUnit(3, 96L << 20, 0, 0xFFFF), new CacheUnit(3, 32L << 20, 0, 0xFFFF_0000)]);

    public AmdVCacheStatus ReadAmdStatus() => new(ServiceState.Running, ServiceState.Running, 0, []);

    /// <summary>Background apps on every core (CPU cache optimization's details move them); the game isn't among them.</summary>
    private readonly Dictionary<int, ProcessAffinity> _apps = new()
    {
        [5101] = new(0xFFFF_FFFF, 0xFFFF_FFFF),
        [5102] = new(0xFFFF_FFFF, 0xFFFF_FFFF),
        [5103] = new(0xFFFF_FFFF, 0xFFFF_FFFF),
        [5104] = new(0xFFFF_FFFF, 0xFFFF_FFFF),
    };

    public ProcessAffinity? GetAffinity(int processId) => _apps.TryGetValue(processId, out var cores) ? cores : null;

    public bool SetAffinity(int processId, ulong mask)
    {
        if (!_apps.TryGetValue(processId, out var cores))
        {
            return false;
        }

        _apps[processId] = cores with { Process = mask };
        return true;
    }

    public IReadOnlyList<RunningProcess> RunningPrograms() =>
    [
        new(5101, @"C:\Program Files\Google\Chrome\Application\chrome.exe"),
        new(5102, @"C:\Users\Jerry\AppData\Local\Discord\app-1.0.9200\Discord.exe"),
        new(5103, @"C:\Users\Jerry\AppData\Roaming\Spotify\Spotify.exe"),
        new(5104, @"C:\Program Files (x86)\Steam\bin\cef\cef.win7x64\steamwebhelper.exe"),
    ];
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
            AppVersion = "1.5.1",
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
        double[] scores = [352000, 338000, 455000, 248000, 452000];
        var tests = GpuTests.All.Select((t, i) => new BenchmarkTestResult(t.Id, t.Name, t.Category, t.Unit, scores[i] * scale * t.Reference / BenchmarkScale.Gpu, scores[i] * scale)).ToList();
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
            AppVersion = "1.5.1",
            PointsAtReference = BenchmarkScale.Gpu,
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
            AppVersion = "1.5.1",
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

        public long Work => (long)((_clock.Elapsed.TotalSeconds * 1000) - (60 * Math.Sin(_clock.Elapsed.TotalSeconds / 10))); // ±0.6 % per loop

        public int Errors => 0;

        public string? Failure => null;

        public void Start() => _clock.Start();

        public void Stop() => _clock.Stop();

        public void Dispose() => Stop();
    }
}
