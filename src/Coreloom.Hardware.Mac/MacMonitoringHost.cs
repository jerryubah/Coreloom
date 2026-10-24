using Coreloom.Core.Hardware;
using Coreloom.Core.History;
using Coreloom.Core.Telemetry;
using Coreloom.Hardware.Mac.Native;
using Coreloom.Hardware.Mac.Providers;
using Coreloom.Hardware.Mac.Sensors;

namespace Coreloom.Hardware.Mac;

public sealed record MacHardwareOptions
{
    public TimeSpan PollingInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Returns the preferred network interface (or null for automatic). Evaluated on every poll.</summary>
    public Func<string?>? PreferredNetworkAdapter { get; init; }
}

/// <summary>
/// The macOS monitoring back-end: reads what the Mac is, creates the providers (which share one set of sensors) and
/// the telemetry engine. The Mac counterpart of the Windows MonitoringHost; the pages read the same metrics from both.
/// </summary>
public sealed class MacMonitoringHost : IDisposable
{
    private MacMonitoringHost(TelemetryEngine engine, SystemSummary summary, SystemCapabilities capabilities, SensorHub sensors, IReadOnlyList<ITelemetryProvider> providers)
    {
        Engine = engine;
        Summary = summary;
        Capabilities = capabilities;
        Sensors = sensors;
        Providers = providers;
    }

    public TelemetryEngine Engine { get; }

    public SystemSummary Summary { get; }

    public SystemCapabilities Capabilities { get; }

    public SensorHub Sensors { get; }

    public IReadOnlyList<ITelemetryProvider> Providers { get; }

    public IProcessSampler ProcessSampler { get; } = new MacProcessSampler();

    public MacGpuProvider? Gpu => Providers.OfType<MacGpuProvider>().FirstOrDefault();

    public MacNetworkProvider? Network => Providers.OfType<MacNetworkProvider>().FirstOrDefault();

    public MacStorageProvider? Storage => Providers.OfType<MacStorageProvider>().FirstOrDefault();

    public int PrimaryGpuSlot => Gpu?.PrimarySlot ?? 0;

    public static MacMonitoringHost Create(MacHardwareOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var capabilities = MacSystemInfo.DetectCapabilities();
        var summary = MacSystemInfo.ReadSummary();
        var sensors = new SensorHub();
        var providers = new List<ITelemetryProvider>
        {
            new SelfMonitorProvider(UptimeSeconds, MacProcessSampler.OwnFootprint),
            new MacSystemProvider(summary, sensors),
            new MacCpuProvider(sensors),
            new MacMemoryProvider(sensors),
            new MacStorageProvider(sensors),
            new MacNetworkProvider(options.PreferredNetworkAdapter),
            new MacGpuProvider(sensors),
        };

        var engine = new TelemetryEngine(providers, new MetricCatalog(), new DeviceRegistry(), HistoryStore.ForInterval(options.PollingInterval), capabilities)
        {
            Interval = options.PollingInterval,
        };
        return new MacMonitoringHost(engine, summary, capabilities, sensors, providers);
    }

    /// <summary>Seconds since boot, sleep included (kern.boottime).</summary>
    public static double UptimeSeconds()
    {
        if (Darwin.SysctlStruct("kern.boottime", out TimeValue boot) && boot.Seconds > 0)
        {
            return Math.Max(0, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0 - (boot.Seconds + (boot.Microseconds / 1e6)));
        }

        return Environment.TickCount64 / 1000.0;
    }

    public void Dispose()
    {
        Engine.Dispose();
        Sensors.Dispose();
    }

#pragma warning disable CS0649 // filled in by sysctl
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct TimeValue
    {
        public long Seconds;
        public int Microseconds;
        public int Padding;
    }
#pragma warning restore CS0649
}
