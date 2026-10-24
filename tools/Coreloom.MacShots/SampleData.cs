using Coreloom.Core;
using Coreloom.Core.Alerts;
using Coreloom.Core.Diagnostics;
using Coreloom.Core.Hardware;
using Coreloom.Core.History;
using Coreloom.Core.Settings;
using Coreloom.Core.Telemetry;
using Coreloom.Core.Updates;
using Coreloom.ViewModels;
using Coreloom.ViewModels.Infrastructure;
using Coreloom.ViewModels.Pages;
using Coreloom.ViewModels.Services;

namespace Coreloom.MacShots;

/// <summary>A Mac like a 16-inch MacBook Pro with an M4 Max (12 performance and 4 efficiency cores, 40-core GPU, 64 GB).</summary>
internal sealed class SampleData
{
    private readonly SampleProvider _provider = new();
    private readonly SampleClock _clock = new();

    public SampleData()
    {
        Engine = new TelemetryEngine([_provider], new MetricCatalog(), new DeviceRegistry(), new HistoryStore(4096), SystemCapabilities.Unknown, clock: _clock);
        Engine.InitializeAll();
        Engine.Devices.Upsert(new DeviceInfo(MetricIds.CpuDeviceId, MetricCategory.Cpu, "Apple M4 Max")
        {
            Vendor = "Apple",
            Properties =
            [
                new("Cores", "16 (12 performance, 4 efficiency)"), new("Threads", "16"),
                new("Performance cores", "12 · up to 4.51 GHz"), new("Efficiency cores", "4 · up to 2.89 GHz"),
                new("Architecture", "ARM64 (Apple silicon)"), new("Neural Engine", "16-core"),
            ],
        });
        Engine.Devices.Upsert(new DeviceInfo(MetricIds.GpuDeviceId(0), MetricCategory.Gpu, "Apple M4 Max")
        {
            Vendor = "Apple",
            Kind = "Integrated",
            Properties = [new("GPU cores", "40"), new("Metal", "Metal 3"), new("Memory", "Unified, 64 GB shared")],
        });
        Engine.Devices.Upsert(new DeviceInfo(MetricIds.DiskDeviceId(0), MetricCategory.Storage, "APPLE SSD AP2048Z")
        {
            Kind = "Apple Fabric SSD",
            Properties = [new("Capacity", "2 TB"), new("Interface", "Apple Fabric"), new("TRIM", "Supported")],
        });
        foreach (var (key, name) in new[] { ("disk3s1", "Macintosh HD"), ("disk3s5", "Data") })
        {
            Engine.Devices.Upsert(new DeviceInfo($"vol/{key}", MetricCategory.Storage, name) { Kind = "Volume", Properties = [new("Disk number", "0")] });
        }

        Engine.PollOnce();
        Settings = new SettingsService(new SettingsStore(Path.Combine(Path.GetTempPath(), "coreloom-macshots-" + Guid.NewGuid().ToString("N"), "settings.json")), new AppSettings().Normalize());
        Monitor = new MonitorContext
        {
            Engine = Engine,
            Summary = new SystemSummary("Jerry's MacBook Pro", "macOS Sequoia", "15.7", "MacBook Pro (16-inch, 2024)", null, "Apple M4 Max", 16, 16, 64L << 30, []),
            Processes = new SampleProcesses(),
            PrimaryGpuSlot = () => 0,
            Gpus = () => [(0, "Apple M4 Max", true)],
            NetworkAdapters = () => [("en0", "Wi-Fi", true), ("en1", "Thunderbolt Ethernet", false)],
            LibreHardwareMonitorIncluded = false,
        };
    }

    public TelemetryEngine Engine { get; }

    public SettingsService Settings { get; }

    public MonitorContext Monitor { get; }

    public ToolServices Tools { get; init; } = new();

    /// <summary>A reading a second, as the app takes them.</summary>
    public void Tick()
    {
        _clock.Advance(1000);
        Engine.PollOnce();
    }

    /// <summary>Two minutes of readings, so the charts are full.</summary>
    public void Prefill()
    {
        for (int i = 0; i < 120; i++)
        {
            Tick();
        }
    }

    private sealed class SampleClock : IMonotonicClock
    {
        private readonly DateTime _start = DateTime.Now.AddMinutes(-5);

        public long NowMilliseconds { get; private set; } = 1_000_000;

        public DateTime LocalNow => _start.AddMilliseconds(NowMilliseconds - 1_000_000);

        public void Advance(long milliseconds) => NowMilliseconds += milliseconds;
    }

    public ShellViewModel Shell(Coreloom.ViewModels.Services.IThemeService theme)
    {
        var stubs = new Stubs();
        var app = new AppVersionInfo(SemanticVersion.Parse("1.6.0"), "1.6.0", "jerryubah/Coreloom", null);
        var updates = new UpdatesViewModel(app, Settings, stubs, stubs, new HttpClient(), Path.GetTempPath());
        return new ShellViewModel(new ShellDependencies
        {
            Monitor = Monitor,
            Settings = Settings,
            Alerts = new AlertEngine(TimeSpan.FromMinutes(10)),
            Theme = theme,
            Startup = stubs,
            Elevation = stubs,
            Shell = stubs,
            Lifetime = stubs,
            Diagnostics = stubs,
            Updates = updates,
            App = app,
            Tools = Tools,
            Platform = HostPlatform.MacOS,
        });
    }

    private sealed class Stubs : IStartupService, IElevationService, IShellService, IAppLifetime, IDiagnosticsService, IUpdateInstaller
    {
        public bool IsStartAtLogonRegistered => true;

        public Task<bool> SetStartAtLogonAsync(bool enabled, bool minimized) => Task.FromResult(true);

        public bool IsElevated => false;

        public void OpenUrl(string url)
        {
        }

        public void OpenFolder(string path)
        {
        }

        public void OpenFile(string path)
        {
        }

        public void CopyToClipboard(string text)
        {
        }

        public string? PickSaveFile(string suggestedName, string filter) => null;

        public void ShowMessage(string title, string message)
        {
        }

        public bool Confirm(string title, string message) => false;

        public void Restart()
        {
        }

        public void Exit()
        {
        }

        public string LogDirectory => Path.GetTempPath();

        public void SetVerboseLogging(bool enabled)
        {
        }

        public IReadOnlyList<LogEntry> RecentLog() => [];

        public bool CanInstallUpdates => true;

        public UpdatePackageKind PackageKind => UpdatePackageKind.MacArm64;

        public bool Prepare(VerifiedPackage package) => false;

        public bool LaunchAndExit(VerifiedPackage package, UpdateInstallOptions options) => false;
    }
}

internal sealed class SampleProvider : ITelemetryProvider
{
    private readonly Dictionary<string, MetricHandle> _handles = [];
    private readonly Dictionary<string, (MetricCategory Category, MetricKind Kind, double Value, string Name)> _values = [];
    private int _tick;

    public SampleProvider()
    {
        Add(MetricIds.CpuLoad, MetricCategory.Cpu, MetricKind.Load, 18, "CPU load");
        Add(MetricIds.CpuTemperature, MetricCategory.Cpu, MetricKind.Temperature, 52, "CPU temperature");
        Add(MetricIds.CpuPower, MetricCategory.Cpu, MetricKind.Power, 9.4, "CPU power");
        Add(MetricIds.CpuClockAverage, MetricCategory.Cpu, MetricKind.Clock, 3720, "Average clock");
        Add("cpu/cluster/p/clock", MetricCategory.Cpu, MetricKind.Clock, 3912, "Performance cores");
        Add("cpu/cluster/e/clock", MetricCategory.Cpu, MetricKind.Clock, 2184, "Efficiency cores");
        for (int i = 0; i < 16; i++)
        {
            Add(MetricIds.CpuThreadLoad(i), MetricCategory.Cpu, MetricKind.Load, i < 4 ? 22 + (i * 7) : 6 + (i * 5 % 31), $"Core {i + 1}");
        }

        Add(MetricIds.GpuLoad(0), MetricCategory.Gpu, MetricKind.Load, 34, "GPU load");
        Add(MetricIds.GpuTemperature(0), MetricCategory.Gpu, MetricKind.Temperature, 48, "GPU temperature");
        Add(MetricIds.GpuPower(0), MetricCategory.Gpu, MetricKind.Power, 6.1, "GPU power");
        Add(MetricIds.GpuCoreClock(0), MetricCategory.Gpu, MetricKind.Clock, 1088, "GPU clock");
        Add(MetricIds.GpuMemoryUsed(0), MetricCategory.Gpu, MetricKind.Data, 5.4 * (1L << 30), "GPU memory in use");
        Add(MetricIds.MemoryUsed, MetricCategory.Memory, MetricKind.Data, 31d * (1L << 30), "Memory used");
        Add(MetricIds.MemoryAvailable, MetricCategory.Memory, MetricKind.Data, 33d * (1L << 30), "Memory available");
        Add(MetricIds.MemoryTotal, MetricCategory.Memory, MetricKind.Data, 64d * (1L << 30), "Memory total");
        Add(MetricIds.MemoryLoad, MetricCategory.Memory, MetricKind.Load, 48.4, "Memory load");
        Add(MetricIds.MemoryCommitUsed, MetricCategory.Memory, MetricKind.Data, 2.1 * (1L << 30), "Swap used");
        Add(MetricIds.MemoryCommitLimit, MetricCategory.Memory, MetricKind.Data, 4d * (1L << 30), "Swap total");
        Add(MetricIds.MemoryCache, MetricCategory.Memory, MetricKind.Data, 14d * (1L << 30), "Cached files");
        Add(MetricIds.DiskRead(0), MetricCategory.Storage, MetricKind.Throughput, 24_000_000, "Read");
        Add(MetricIds.DiskWrite(0), MetricCategory.Storage, MetricKind.Throughput, 6_000_000, "Write");
        Add(MetricIds.DiskTotalRead, MetricCategory.Storage, MetricKind.Throughput, 24_000_000, "Read");
        Add(MetricIds.DiskTotalWrite, MetricCategory.Storage, MetricKind.Throughput, 6_000_000, "Write");
        Add(MetricIds.DiskTemperature(0), MetricCategory.Storage, MetricKind.Temperature, 39, "SSD temperature");
        foreach (var (key, total, used) in new[] { ("disk3s1", 1995d, 12d), ("disk3s5", 1995d, 1104d) })
        {
            Add(MetricIds.VolumeTotal(key), MetricCategory.Storage, MetricKind.Data, total * 1e9, "Total");
            Add(MetricIds.VolumeUsed(key), MetricCategory.Storage, MetricKind.Data, used * 1e9, "Used");
            Add(MetricIds.VolumeFree(key), MetricCategory.Storage, MetricKind.Data, (total - used) * 1e9, "Free");
            Add(MetricIds.VolumeLoad(key), MetricCategory.Storage, MetricKind.Load, used / total * 100, "Load");
        }

        Add(MetricIds.NetworkActiveDown, MetricCategory.Network, MetricKind.Throughput, 4_200_000, "Download");
        Add(MetricIds.NetworkActiveUp, MetricCategory.Network, MetricKind.Throughput, 640_000, "Upload");
        Add("board/fan/left", MetricCategory.Motherboard, MetricKind.Fan, 1630, "Left fan");
        Add("board/fan/right", MetricCategory.Motherboard, MetricKind.Fan, 1750, "Right fan");
    }

    public string Name => "Sample";

    public MonitoringSection Section => MonitoringSection.System;

    public int InitStage => 0;

    public TimeSpan MinimumPollInterval => TimeSpan.Zero;

    public ProviderInitResult Initialize(IProviderContext context)
    {
        foreach (var (id, (category, kind, _, name)) in _values)
        {
            string? device = id.StartsWith("cpu", StringComparison.Ordinal) ? MetricIds.CpuDeviceId
                : id.StartsWith("gpu/0", StringComparison.Ordinal) ? MetricIds.GpuDeviceId(0)
                : id.StartsWith("disk/0", StringComparison.Ordinal) ? MetricIds.DiskDeviceId(0)
                : id.StartsWith("vol/", StringComparison.Ordinal) ? id[..id.LastIndexOf('/')]
                : null;
            _handles[id] = context.Register(new MetricDescriptor(id, name, category, kind) { DeviceId = device }, Provenance.Measured, 10, "sample");
        }

        return ProviderInitResult.Running();
    }

    public void Poll(IMetricWriter writer)
    {
        _tick++;
        foreach (var (id, (_, kind, value, _)) in _values)
        {
            // Readings that wander like real ones: two slow waves and a quick ripple, different for each metric.
            double phase = id.Length * 1.7;
            double wobble = kind is MetricKind.Load or MetricKind.Power or MetricKind.Throughput or MetricKind.Clock
                ? 1 + (0.22 * Math.Sin((_tick * 0.11) + phase)) + (0.12 * Math.Sin((_tick * 0.43) + (phase * 2))) + (0.05 * Math.Sin(_tick * 1.9))
                : kind == MetricKind.Temperature ? 1 + (0.06 * Math.Sin((_tick * 0.07) + phase)) : 1;
            writer.Write(_handles[id], kind == MetricKind.Load ? Math.Clamp(value * wobble, 0, 100) : value * wobble);
        }
    }

    public void Shutdown()
    {
    }

    public void Dispose()
    {
    }

    private void Add(string id, MetricCategory category, MetricKind kind, double value, string name) => _values[id] = (category, kind, value, name);
}

internal sealed class SampleProcesses : IProcessSampler
{
    private static readonly (string Name, int Count, double Cpu, double Gb)[] Apps =
    [
        ("Safari", 14, 3.4, 3.1), ("Xcode", 3, 2.2, 4.6), ("Final Cut Pro", 2, 6.8, 5.2), ("Music", 1, 0.6, 0.4), ("Messages", 1, 0.2, 0.3),
        ("WindowServer", 1, 2.9, 1.1), ("Finder", 1, 0.3, 0.3), ("Coreloom", 1, 0.5, 0.14), ("Discord", 6, 0.8, 1.0), ("Slack", 5, 0.6, 0.9),
        ("Photos", 2, 0.1, 0.6), ("Spotlight", 3, 0.4, 0.2), ("Figma", 4, 0.9, 1.4), ("Mail", 1, 0.2, 0.4),
    ];

    public IReadOnlyList<ProcessUsage> Sample() =>
        [.. Apps.Select(a => new ProcessUsage(a.Name, a.Count, a.Cpu, (long)(a.Gb * (1L << 30)), (long)(a.Gb * 0.8 * (1L << 30))))];
}
