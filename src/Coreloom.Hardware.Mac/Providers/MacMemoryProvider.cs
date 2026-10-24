using System.Globalization;
using Coreloom.Core.Formatting;
using Coreloom.Core.Telemetry;
using Coreloom.Hardware.Mac.Native;
using Coreloom.Hardware.Mac.Sensors;

namespace Coreloom.Hardware.Mac.Providers;

/// <summary>
/// Memory as Activity Monitor counts it: "Memory Used" is app memory + wired + compressed, "Cached Files" is file-backed
/// and purgeable memory, and swap and memory pressure come from the kernel. On Apple silicon the memory is unified
/// (shared by the processor and the GPU), and its power comes from IOReport.
/// </summary>
public sealed class MacMemoryProvider : ITelemetryProvider
{
    private readonly SensorHub _sensors;
    private long _pageSize;
    private long _total;
    private MetricHandle? _totalMetric;
    private MetricHandle? _used;
    private MetricHandle? _available;
    private MetricHandle? _load;
    private MetricHandle? _cache;
    private MetricHandle? _app;
    private MetricHandle? _wired;
    private MetricHandle? _compressed;
    private MetricHandle? _swapUsed;
    private MetricHandle? _swapTotal;
    private MetricHandle? _pressure;
    private MetricHandle? _power;
    private bool _acquired;

    public MacMemoryProvider(SensorHub sensors) => _sensors = sensors;

    public string Name => "macOS memory";

    public MonitoringSection Section => MonitoringSection.Memory;

    public int InitStage => 0;

    public TimeSpan MinimumPollInterval => TimeSpan.Zero;

    public ProviderInitResult Initialize(IProviderContext context)
    {
        Shutdown();
        _pageSize = Darwin.PageSize();
        _total = Darwin.SysctlLong("hw.memsize") ?? 0;
        if (_total <= 0 || !Darwin.VmStatistics(out _))
        {
            throw new InvalidOperationException("host_statistics64 / hw.memsize are unavailable.");
        }

        MetricHandle Reg(string id, string name, MetricKind kind, bool track = true, Provenance provenance = Provenance.Measured, string? note = null, int order = 0) =>
            context.Register(new MetricDescriptor(id, name, MetricCategory.Memory, kind)
            {
                DeviceId = MetricIds.MemoryDeviceId,
                DeviceName = "Memory",
                Group = "Memory",
                TrackHistory = track,
                SortOrder = order,
            }, provenance, 50, note);

        const string vm = "macOS virtual memory statistics (host_statistics64), counted like Activity Monitor";
        _totalMetric = Reg(MetricIds.MemoryTotal, "Physical memory", MetricKind.Data, track: false, note: "hw.memsize");
        _used = Reg(MetricIds.MemoryUsed, "Memory used", MetricKind.Data, provenance: Provenance.Derived, note: "App memory + wired + compressed (Activity Monitor's \"Memory Used\")", order: 1);
        _available = Reg(MetricIds.MemoryAvailable, "Available", MetricKind.Data, provenance: Provenance.Derived, note: "Physical memory − memory used", order: 2);
        _load = Reg(MetricIds.MemoryLoad, "Memory utilization", MetricKind.Load, provenance: Provenance.Derived, note: "Memory used ÷ physical memory");
        _app = Reg(MetricIds.MemoryApp, "App memory", MetricKind.Data, note: vm + ": internal − purgeable pages", order: 3);
        _wired = Reg(MetricIds.MemoryWired, "Wired memory", MetricKind.Data, note: vm, order: 4);
        _compressed = Reg(MetricIds.MemoryCompressed, "Compressed", MetricKind.Data, note: vm + ": pages held by the compressor", order: 5);
        _cache = Reg(MetricIds.MemoryCache, "Cached files", MetricKind.Data, note: vm + ": file-backed + purgeable pages", order: 6);
        _swapUsed = Reg(MetricIds.MemorySwapUsed, "Swap used", MetricKind.Data, note: "vm.swapusage", order: 7);
        _swapTotal = Reg(MetricIds.MemorySwapTotal, "Swap size", MetricKind.Data, track: false, note: "vm.swapusage", order: 8);
        if (Darwin.SysctlLong("kern.memorystatus_vm_pressure_level") is not null)
        {
            _pressure = Reg(MetricIds.MemoryPressure, "Memory pressure", MetricKind.State, note: "kern.memorystatus_vm_pressure_level: 1 normal, 2 warning, 4 critical", order: 9);
        }

        _acquired = true;
        _sensors.Acquire();
        if (MacSystemInfo.IsAppleSilicon)
        {
            _power = Reg(MetricIds.MemoryPower, "Memory power", MetricKind.Power, note: "IOReport energy counters (Energy Model DRAM channels)", order: 10);
            if (!_sensors.HasEnergyCounters)
            {
                context.SetUnavailableReason(MetricIds.MemoryPower, SensorHub.MissingReason);
            }
        }

        var f = new UnitFormatter(culture: CultureInfo.InvariantCulture);
        var props = new List<KeyValuePair<string, string>>
        {
            new("Installed", f.Bytes(_total, 0).ToString()),
            new("Architecture", MacSystemInfo.IsAppleSilicon ? "Unified memory (shared by the processor, GPU and Neural Engine)" : "Separate system memory"),
            new("Page size", f.Bytes(_pageSize, 0).ToString()),
        };
        if (Darwin.SysctlStruct("vm.swapusage", out Darwin.XswUsage swap))
        {
            props.Add(new("Swap", swap.Encrypted != 0 ? "Dynamic, encrypted" : "Dynamic"));
        }

        context.UpsertDevice(new DeviceInfo(MetricIds.MemoryDeviceId, MetricCategory.Memory, MacSystemInfo.IsAppleSilicon ? "Unified memory" : "Memory")
        {
            Kind = MacSystemInfo.IsAppleSilicon ? "Unified memory" : "System memory",
            Vendor = MacSystemInfo.IsAppleSilicon ? "Apple" : null,
            Source = "macOS",
            Properties = props,
        });
        return ProviderInitResult.Running($"{f.Bytes(_total, 0)} physical memory");
    }

    public void Poll(IMetricWriter writer)
    {
        if (!Darwin.VmStatistics(out var vm))
        {
            return;
        }

        var memory = MemoryMath.FromStatistics(vm, _pageSize, _total);
        writer.Write(_totalMetric!, _total);
        writer.Write(_used!, memory.Used);
        writer.Write(_available!, memory.Available);
        writer.Write(_load!, memory.LoadPercent);
        writer.Write(_app!, memory.App);
        writer.Write(_wired!, memory.Wired);
        writer.Write(_compressed!, memory.Compressed);
        writer.Write(_cache!, memory.Cached);

        if (Darwin.SysctlStruct("vm.swapusage", out Darwin.XswUsage swap))
        {
            writer.Write(_swapUsed!, swap.Used);
            writer.Write(_swapTotal!, swap.Total);
        }

        if (_pressure is not null && Darwin.SysctlLong("kern.memorystatus_vm_pressure_level") is long level)
        {
            writer.Write(_pressure, level);
        }

        if (_power is not null)
        {
            writer.Write(_power, _sensors.Sample().MemoryPower);
        }
    }

    public void Shutdown()
    {
        if (_acquired)
        {
            _acquired = false;
            _sensors.Release();
        }

        _power = null;
        _pressure = null;
    }

    public void Dispose() => Shutdown();
}

/// <summary>Activity Monitor's memory figures from the kernel's page counts.</summary>
internal readonly record struct MacMemory(double Used, double Available, double LoadPercent, double App, double Wired, double Compressed, double Cached)
{
}

internal static class MemoryMath
{
    public static MacMemory FromStatistics(in Darwin.VmStatistics64 vm, long pageSize, long total)
    {
        double page = pageSize;
        double app = Math.Max(0, (double)vm.InternalPageCount - vm.PurgeableCount) * page;
        double wired = vm.WireCount * page;
        double compressed = vm.CompressorPageCount * page;
        double cached = ((double)vm.ExternalPageCount + vm.PurgeableCount) * page;
        double used = Math.Min(total, app + wired + compressed);
        double available = Math.Max(0, total - used);
        return new MacMemory(used, available, total > 0 ? Math.Clamp(used * 100.0 / total, 0, 100) : double.NaN, app, wired, compressed, cached);
    }

    /// <summary>"Normal", "Warning" or "Critical" for kern.memorystatus_vm_pressure_level.</summary>
    public static string PressureText(double level) => level switch
    {
        >= 4 => "Critical",
        >= 2 => "Warning",
        _ => "Normal",
    };
}
