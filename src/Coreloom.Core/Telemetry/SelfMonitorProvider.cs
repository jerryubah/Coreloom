using System.Diagnostics;

namespace Coreloom.Core.Telemetry;

/// <summary>
/// Measures Coreloom's own resource usage (so overhead can be verified in the app) and system uptime.
/// </summary>
public sealed class SelfMonitorProvider : ITelemetryProvider
{
    private readonly Func<double> _uptimeSeconds;
    private readonly Func<double>? _privateBytes;
    private Process? _process;
    private MetricHandle? _cpu;
    private MetricHandle? _workingSet;
    private MetricHandle? _private;
    private MetricHandle? _managed;
    private MetricHandle? _handles;
    private MetricHandle? _uptime;
    private TimeSpan _lastCpu;
    private long _lastWallTicks;

    /// <param name="uptimeSeconds">The system's uptime including sleep (Windows' tick count by default; macOS passes its boot time).</param>
    /// <param name="privateBytes">The process's own memory where the platform measures it differently (macOS: its footprint).</param>
    public SelfMonitorProvider(Func<double>? uptimeSeconds = null, Func<double>? privateBytes = null)
    {
        _uptimeSeconds = uptimeSeconds ?? (() => Environment.TickCount64 / 1000.0);
        _privateBytes = privateBytes;
    }

    public string Name => "Coreloom process";

    public MonitoringSection Section => MonitoringSection.System;

    public int InitStage => 0;

    public TimeSpan MinimumPollInterval => TimeSpan.Zero;

    public ProviderInitResult Initialize(IProviderContext context)
    {
        Shutdown();
        _process = Process.GetCurrentProcess();
        _lastCpu = _process.TotalProcessorTime;
        _lastWallTicks = Stopwatch.GetTimestamp();

        _cpu = context.Register(new MetricDescriptor(MetricIds.AppCpu, "Coreloom CPU usage", MetricCategory.Application, MetricKind.Load)
        {
            Group = "Coreloom",
            DeviceId = MetricIds.SystemDeviceId,
        }, Provenance.Measured, 0, "Process CPU time ÷ (elapsed time × logical processors)");

        _workingSet = context.Register(new MetricDescriptor(MetricIds.AppWorkingSet, "Coreloom working set", MetricCategory.Application, MetricKind.Data)
        {
            Group = "Coreloom",
            DeviceId = MetricIds.SystemDeviceId,
        });

        _private = context.Register(new MetricDescriptor(MetricIds.AppPrivateBytes, "Coreloom private memory", MetricCategory.Application, MetricKind.Data)
        {
            Group = "Coreloom",
            DeviceId = MetricIds.SystemDeviceId,
        });

        _managed = context.Register(new MetricDescriptor(MetricIds.AppManagedBytes, "Coreloom managed heap", MetricCategory.Application, MetricKind.Data)
        {
            Group = "Coreloom",
            DeviceId = MetricIds.SystemDeviceId,
        }, Provenance.Measured, 0, "GC heap size (GC.GetTotalMemory without forcing a collection); a leak shows as steady growth that never drops back");

        if (OperatingSystem.IsWindows())
        {
            _handles = context.Register(new MetricDescriptor(MetricIds.AppHandles, "Coreloom handle count", MetricCategory.Application, MetricKind.Count)
            {
                Group = "Coreloom",
                DeviceId = MetricIds.SystemDeviceId,
            }, Provenance.Measured, 0, "Open Windows handles in this process; a leak shows as steady growth");
        }

        _uptime = context.Register(new MetricDescriptor(MetricIds.SystemUptime, "System uptime", MetricCategory.System, MetricKind.Duration)
        {
            Group = "System",
            DeviceId = MetricIds.SystemDeviceId,
            TrackHistory = false,
        });

        return ProviderInitResult.Running();
    }

    public void Poll(IMetricWriter writer)
    {
        if (_process is null)
        {
            return;
        }

        _process.Refresh();
        var cpu = _process.TotalProcessorTime;
        long now = Stopwatch.GetTimestamp();
        double wallSeconds = Stopwatch.GetElapsedTime(_lastWallTicks, now).TotalSeconds;
        if (wallSeconds > 0.05)
        {
            double cpuSeconds = (cpu - _lastCpu).TotalSeconds;
            double percent = cpuSeconds / (wallSeconds * Environment.ProcessorCount) * 100.0;
            writer.Write(_cpu!, Math.Clamp(percent, 0, 100));
            _lastCpu = cpu;
            _lastWallTicks = now;
        }

        writer.Write(_workingSet!, _process.WorkingSet64);
        writer.Write(_private!, _privateBytes?.Invoke() ?? (OperatingSystem.IsWindows() ? _process.PrivateMemorySize64 : double.NaN));

        writer.Write(_managed!, GC.GetTotalMemory(forceFullCollection: false));
        if (_handles is not null)
        {
            writer.Write(_handles, _process.HandleCount);
        }

        writer.Write(_uptime!, _uptimeSeconds());
    }

    public void Shutdown()
    {
        _process?.Dispose();
        _process = null;
    }

    public void Dispose() => Shutdown();
}
