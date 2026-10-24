using System.Globalization;
using Coreloom.Core.Formatting;
using Coreloom.Core.Hardware;
using Coreloom.Core.Telemetry;
using Coreloom.Hardware.Mac.Native;
using Coreloom.Hardware.Mac.Sensors;

namespace Coreloom.Hardware.Mac.Providers;

/// <summary>A GPU the Mac has, in the order the pages list them.</summary>
public sealed record MacGpu(int Slot, string Name, HardwareVendor Vendor, bool IsIntegrated);

/// <summary>
/// The GPUs: utilization and memory from each graphics driver's own statistics (the IOAccelerator service's
/// "PerformanceStatistics", which Activity Monitor's GPU history reads too), with temperature, clock and power from
/// the shared sensors on Apple silicon, from AMD's driver statistics on Intel Macs with Radeon graphics, and from the
/// SMC otherwise.
/// </summary>
public sealed class MacGpuProvider : ITelemetryProvider
{
    public const int Priority = 60;
    private readonly SensorHub _sensors;
    private readonly List<Gpu> _gpus = [];
    private bool _acquired;

    public MacGpuProvider(SensorHub sensors) => _sensors = sensors;

    public string Name => "macOS graphics drivers";

    public MonitoringSection Section => MonitoringSection.Gpu;

    public int InitStage => 0;

    public TimeSpan MinimumPollInterval => TimeSpan.Zero;

    /// <summary>The GPUs found by the last initialization (slot order).</summary>
    public IReadOnlyList<MacGpu> Gpus { get; private set; } = [];

    /// <summary>The first discrete GPU, else the first GPU.</summary>
    public int PrimarySlot => Gpus.FirstOrDefault(g => !g.IsIntegrated)?.Slot ?? 0;

    public ProviderInitResult Initialize(IProviderContext context)
    {
        Shutdown();
        var found = Discover();
        if (found.Count == 0)
        {
            return ProviderInitResult.Unavailable("No graphics accelerator was found in the I/O registry.");
        }

        _acquired = true;
        _sensors.Acquire();
        var sample = _sensors.Sample();

        // Discrete GPUs first, so slot 0 is what games use on Intel Macs with two GPUs.
        int slot = 0;
        foreach (var gpu in found.OrderBy(g => g.IsIntegrated ? 1 : 0))
        {
            gpu.Slot = slot++;
            Register(context, gpu, sample);
            _gpus.Add(gpu);
        }

        Gpus = _gpus.Select(g => new MacGpu(g.Slot, g.Name, g.Vendor, g.IsIntegrated)).ToArray();
        return ProviderInitResult.Running(string.Join(", ", _gpus.Select(g => g.Name)));
    }

    private static List<Gpu> Discover()
    {
        var result = new List<Gpu>();
        foreach (var (service, properties) in IOKit.Services("IOAccelerator", keepServices: true))
        {
            string className = IOKit.ClassName(service) ?? string.Empty;
            var pci = IOKit.FindAncestor(service, entry => IOKit.ClassName(entry) is "IOPCIDevice" or "AppleARMIODevice" || IOKit.Property(entry, "model") is not null, maxDepth: 6);
            string? model = CF.Text(properties, "model") ?? CF.Text(pci, "model");
            var vendor = VendorOf(className, model);
            string name = Clean(model) ?? (vendor == HardwareVendor.Apple ? "Apple GPU" : className);
            var gpu = new Gpu(service, name, vendor, vendor is HardwareVendor.Apple or HardwareVendor.Intel)
            {
                Cores = CF.Number(properties, "gpu-core-count") is double cores && cores > 0 ? (int)cores : null,
                VramBytes = VramOf(pci),
                ClassName = className,
            };
            result.Add(gpu);
        }

        return result;
    }

    internal static HardwareVendor VendorOf(string className, string? model)
    {
        if (className.StartsWith("AGX", StringComparison.Ordinal) || className.Contains("Paravirt", StringComparison.OrdinalIgnoreCase))
        {
            return HardwareVendor.Apple;
        }

        if (className.StartsWith("AMD", StringComparison.Ordinal) || className.Contains("Radeon", StringComparison.OrdinalIgnoreCase))
        {
            return HardwareVendor.Amd;
        }

        if (className.StartsWith("Intel", StringComparison.Ordinal))
        {
            return HardwareVendor.Intel;
        }

        if (className.StartsWith("nv", StringComparison.OrdinalIgnoreCase) || className.StartsWith("GeForce", StringComparison.Ordinal))
        {
            return HardwareVendor.Nvidia;
        }

        return HardwareVendors.Detect(null, model);
    }

    private static string? Clean(string? model) => string.IsNullOrWhiteSpace(model) ? null : model.Replace("\0", string.Empty, StringComparison.Ordinal).Trim();

    private static long? VramOf(Dictionary<string, object?>? pci)
    {
        if (CF.Number(pci, "VRAM,totalMB") is double mb && mb > 0)
        {
            return (long)(mb * 1024 * 1024);
        }

        if (CF.Number(pci, "VRAM,totalsize") is double bytes && bytes > 0)
        {
            return (long)bytes;
        }

        return pci is not null && pci.TryGetValue("ATY,memsize", out var value) && value is byte[] { Length: >= 4 } raw
            ? BitConverter.ToUInt32(raw, 0) is uint size && size > 0 ? size : null
            : null;
    }

    private void Register(IProviderContext context, Gpu gpu, SensorSample sample)
    {
        string deviceId = MetricIds.GpuDeviceId(gpu.Slot);
        MetricHandle Reg(string id, string label, MetricKind kind, string group, string note, bool track = true, Provenance provenance = Provenance.Measured) =>
            context.Register(new MetricDescriptor(id, label, MetricCategory.Gpu, kind)
            {
                DeviceId = deviceId,
                DeviceName = gpu.Name,
                Group = group,
                TrackHistory = track,
            }, provenance, Priority, note);

        var stats = gpu.Statistics();
        const string driverNote = "The graphics driver's own statistics (IOAccelerator PerformanceStatistics)";
        if (Find(stats, "Device Utilization %", "GPU Activity(%)", "GPU Core Utilization(%)") is not null)
        {
            gpu.Load = Reg(MetricIds.GpuLoad(gpu.Slot), "GPU utilization", MetricKind.Load, "Load", driverNote);
        }

        if (Find(stats, "Renderer Utilization %") is not null)
        {
            gpu.Renderer = Reg($"gpu/{gpu.Slot.ToString(CultureInfo.InvariantCulture)}/load/renderer", "Renderer", MetricKind.Load, "Load", driverNote);
        }

        if (Find(stats, "Tiler Utilization %") is not null)
        {
            gpu.Tiler = Reg($"gpu/{gpu.Slot.ToString(CultureInfo.InvariantCulture)}/load/tiler", "Tiler", MetricKind.Load, "Load", driverNote);
        }

        bool unified = gpu.Vendor == HardwareVendor.Apple;
        if (Find(stats, "In use system memory", "vramUsedBytes", "inUseVidMemoryBytes") is not null)
        {
            gpu.MemoryUsed = Reg(MetricIds.GpuMemoryUsed(gpu.Slot), unified ? "Memory in use (unified)" : "VRAM used", MetricKind.Data, "Memory", driverNote);
        }

        if (!unified && gpu.VramBytes is long vram && vram > 0)
        {
            gpu.MemoryTotal = Reg(MetricIds.GpuMemoryTotal(gpu.Slot), "VRAM total", MetricKind.Data, "Memory", "From the GPU's PCI device properties", track: false);
            if (gpu.MemoryUsed is not null)
            {
                gpu.MemoryLoad = Reg(MetricIds.GpuMemoryLoad(gpu.Slot), "VRAM utilization", MetricKind.Load, "Memory", "VRAM used ÷ total", provenance: Provenance.Derived);
            }
        }

        bool apple = gpu.Vendor == HardwareVendor.Apple;
        if (Find(stats, "Temperature(C)") is not null)
        {
            gpu.DriverTemperature = Reg(MetricIds.GpuTemperature(gpu.Slot), "GPU temperature", MetricKind.Temperature, "Temperatures", driverNote);
        }
        else if (apple || (!gpu.IsIntegrated && _gpus.All(g => g.SensorTemperature is null)))
        {
            gpu.SensorTemperature = Reg(MetricIds.GpuTemperature(gpu.Slot), "GPU temperature", MetricKind.Temperature, "Temperatures",
                apple ? "Average of the GPU's die sensors (HID temperature sensors)" : "SMC temperature key (GPU die, else proximity)");
            if (!double.IsFinite(sample.GpuTemperature))
            {
                context.SetUnavailableReason(MetricIds.GpuTemperature(gpu.Slot), SensorHub.MissingReason);
            }
        }

        if (Find(stats, "Core Clock(MHz)") is not null)
        {
            gpu.DriverClock = Reg(MetricIds.GpuCoreClock(gpu.Slot), "Core clock", MetricKind.Clock, "Clocks", driverNote);
        }
        else if (apple)
        {
            gpu.SensorClock = Reg(MetricIds.GpuCoreClock(gpu.Slot), "GPU clock", MetricKind.Clock, "Clocks",
                "IOReport: time-weighted average of the GPU's frequency states while active", provenance: Provenance.Derived);
            if (!_sensors.HasEnergyCounters)
            {
                context.SetUnavailableReason(MetricIds.GpuCoreClock(gpu.Slot), SensorHub.MissingReason);
            }
        }

        if (Find(stats, "Memory Clock(MHz)") is not null)
        {
            gpu.MemoryClock = Reg(MetricIds.GpuMemoryClock(gpu.Slot), "Memory clock", MetricKind.Clock, "Clocks", driverNote);
        }

        if (Find(stats, "Total Power(W)") is not null)
        {
            gpu.DriverPower = Reg(MetricIds.GpuPower(gpu.Slot), "GPU power", MetricKind.Power, "Power", driverNote);
        }
        else if (apple || (gpu.Vendor == HardwareVendor.Intel && gpu.IsIntegrated))
        {
            gpu.SensorPower = Reg(MetricIds.GpuPower(gpu.Slot), "GPU power", MetricKind.Power, "Power",
                apple ? "IOReport energy counters (Energy Model GPU channel) over the sampling interval" : "SMC power key (processor package graphics)");
            if (apple ? !_sensors.HasEnergyCounters : !double.IsFinite(sample.GpuPower))
            {
                context.SetUnavailableReason(MetricIds.GpuPower(gpu.Slot), SensorHub.MissingReason);
            }
        }

        if (Find(stats, "Fan Speed(RPM)") is not null)
        {
            gpu.FanRpm = Reg(MetricIds.GpuFanRpm(gpu.Slot, 1), "Fan speed", MetricKind.Fan, "Cooling", driverNote);
        }

        if (Find(stats, "Fan Speed(%)") is not null)
        {
            gpu.FanPercent = Reg(MetricIds.GpuFanPercent(gpu.Slot, 1), "Fan duty", MetricKind.Control, "Cooling", driverNote);
        }

        var f = new UnitFormatter(culture: CultureInfo.InvariantCulture);
        var props = new List<KeyValuePair<string, string>>();
        if (gpu.Cores is int cores)
        {
            props.Add(new("GPU cores", cores.ToString(CultureInfo.InvariantCulture)));
        }

        props.Add(new("Memory", unified ? "Unified (shared with the processor)" : gpu.VramBytes is long v && v > 0 ? f.Bytes(v, 0).ToString() : "Dedicated"));
        props.Add(new("Graphics API", "Metal"));
        props.Add(new("Driver", gpu.ClassName));
        if (apple && FrequencyTables.Read().Gpu is { Count: > 0 } table)
        {
            props.Add(new("Max clock", f.ClockGigahertz(table.Max()).ToString()));
        }

        context.UpsertDevice(new DeviceInfo(deviceId, MetricCategory.Gpu, gpu.Name)
        {
            Vendor = HardwareVendors.DisplayName(gpu.Vendor),
            Kind = gpu.IsIntegrated ? (unified ? "Integrated GPU (Apple silicon)" : "Integrated GPU") : "Discrete GPU",
            IsIntegrated = gpu.IsIntegrated,
            Order = gpu.Slot,
            Source = "macOS",
            Properties = props,
        });
    }

    private static double? Find(Dictionary<string, object?>? stats, params string[] keys)
    {
        foreach (string key in keys)
        {
            if (CF.Number(stats, key) is double value && double.IsFinite(value))
            {
                return value;
            }
        }

        return null;
    }

    public void Poll(IMetricWriter writer)
    {
        SensorSample? sample = null;
        foreach (var gpu in _gpus)
        {
            var stats = gpu.Statistics();
            if (stats is not null)
            {
                Write(writer, gpu.Load, Find(stats, "Device Utilization %", "GPU Activity(%)", "GPU Core Utilization(%)"), clampPercent: true);
                Write(writer, gpu.Renderer, Find(stats, "Renderer Utilization %"), clampPercent: true);
                Write(writer, gpu.Tiler, Find(stats, "Tiler Utilization %"), clampPercent: true);
                double? used = Find(stats, "In use system memory", "vramUsedBytes", "inUseVidMemoryBytes");
                Write(writer, gpu.MemoryUsed, used);
                if (gpu.MemoryTotal is not null && gpu.VramBytes is long total)
                {
                    writer.Write(gpu.MemoryTotal, total);
                    if (gpu.MemoryLoad is not null && used is double u)
                    {
                        writer.Write(gpu.MemoryLoad, Math.Clamp(u * 100.0 / total, 0, 100));
                    }
                }

                Write(writer, gpu.DriverTemperature, Find(stats, "Temperature(C)"));
                Write(writer, gpu.DriverClock, Find(stats, "Core Clock(MHz)"));
                Write(writer, gpu.MemoryClock, Find(stats, "Memory Clock(MHz)"));
                Write(writer, gpu.DriverPower, Find(stats, "Total Power(W)"));
                Write(writer, gpu.FanRpm, Find(stats, "Fan Speed(RPM)"));
                Write(writer, gpu.FanPercent, Find(stats, "Fan Speed(%)"), clampPercent: true);
            }

            if (gpu.SensorTemperature is not null || gpu.SensorClock is not null || gpu.SensorPower is not null)
            {
                sample ??= _sensors.Sample();
                Write(writer, gpu.SensorTemperature, sample.GpuTemperature);
                Write(writer, gpu.SensorClock, sample.GpuClockMhz);
                Write(writer, gpu.SensorPower, sample.GpuPower);
            }
        }
    }

    private static void Write(IMetricWriter writer, MetricHandle? handle, double? value, bool clampPercent = false)
    {
        if (handle is not null && value is double v && double.IsFinite(v))
        {
            writer.Write(handle, clampPercent ? Math.Clamp(v, 0, 100) : v);
        }
    }

    public void Shutdown()
    {
        foreach (var gpu in _gpus)
        {
            gpu.Release();
        }

        _gpus.Clear();
        if (_acquired)
        {
            _acquired = false;
            _sensors.Release();
        }
    }

    public void Dispose() => Shutdown();

    private sealed class Gpu(uint service, string name, HardwareVendor vendor, bool isIntegrated)
    {
        private uint _service = service;

        public string Name { get; } = name;

        public HardwareVendor Vendor { get; } = vendor;

        public bool IsIntegrated { get; } = isIntegrated;

        public int Slot { get; set; }

        public int? Cores { get; init; }

        public long? VramBytes { get; init; }

        public string ClassName { get; init; } = string.Empty;

        public MetricHandle? Load { get; set; }

        public MetricHandle? Renderer { get; set; }

        public MetricHandle? Tiler { get; set; }

        public MetricHandle? MemoryUsed { get; set; }

        public MetricHandle? MemoryTotal { get; set; }

        public MetricHandle? MemoryLoad { get; set; }

        public MetricHandle? DriverTemperature { get; set; }

        public MetricHandle? SensorTemperature { get; set; }

        public MetricHandle? DriverClock { get; set; }

        public MetricHandle? SensorClock { get; set; }

        public MetricHandle? MemoryClock { get; set; }

        public MetricHandle? DriverPower { get; set; }

        public MetricHandle? SensorPower { get; set; }

        public MetricHandle? FanRpm { get; set; }

        public MetricHandle? FanPercent { get; set; }

        public Dictionary<string, object?>? Statistics() =>
            _service == 0 ? null : IOKit.Property(_service, "PerformanceStatistics") as Dictionary<string, object?>;

        public void Release()
        {
            if (_service != 0)
            {
                IOKit.IOObjectRelease(_service);
                _service = 0;
            }
        }
    }
}
