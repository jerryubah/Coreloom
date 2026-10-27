using Coreloom.Core.Telemetry;

namespace Coreloom.ConnectDemo;

/// <summary>Sample readings that wander like real ones: a gaming PC (Ryzen 9 9950X3D, RTX 5090) or a MacBook Pro (M4 Max).</summary>
internal sealed class DemoProvider : ITelemetryProvider
{
    private readonly Dictionary<string, MetricHandle> _handles = [];
    private readonly Dictionary<string, (MetricCategory Category, MetricKind Kind, double Value, string Name)> _values = [];
    private readonly bool _mac;
    private int _tick;

    public DemoProvider(bool mac)
    {
        _mac = mac;
        if (mac)
        {
            AddMac();
        }
        else
        {
            AddPc();
        }
    }

    public string Name => "Sample";

    public MonitoringSection Section => MonitoringSection.System;

    public int InitStage => 0;

    public TimeSpan MinimumPollInterval => TimeSpan.Zero;

    public void AddDevices(DeviceRegistry devices)
    {
        if (_mac)
        {
            devices.Upsert(new DeviceInfo(MetricIds.CpuDeviceId, MetricCategory.Cpu, "Apple M4 Max") { Vendor = "Apple", Properties = [new("Cores", "16 (12 performance, 4 efficiency)")] });
            devices.Upsert(new DeviceInfo(MetricIds.GpuDeviceId(0), MetricCategory.Gpu, "Apple M4 Max") { Vendor = "Apple", Kind = "Integrated", Properties = [new("GPU cores", "40")] });
            devices.Upsert(new DeviceInfo(MetricIds.DiskDeviceId(0), MetricCategory.Storage, "APPLE SSD AP2048Z") { Kind = "Apple Fabric SSD" });
            foreach (var (key, name) in new[] { ("disk3s1", "Macintosh HD"), ("disk3s5", "Data") })
            {
                devices.Upsert(new DeviceInfo($"vol/{key}", MetricCategory.Storage, name) { Kind = "Volume", Properties = [new("Disk number", "0"), new("Mount point", key == "disk3s1" ? "/" : "/System/Volumes/Data")] });
            }

            return;
        }

        devices.Upsert(new DeviceInfo(MetricIds.CpuDeviceId, MetricCategory.Cpu, "AMD Ryzen 9 9950X3D 16-Core Processor") { Vendor = "AMD" });
        devices.Upsert(new DeviceInfo(MetricIds.GpuDeviceId(0), MetricCategory.Gpu, "NVIDIA GeForce RTX 5090") { Vendor = "NVIDIA", Properties = [new("VRAM", "32 GB GDDR7"), new("Driver", "581.57")] });
        devices.Upsert(new DeviceInfo(MetricIds.DiskDeviceId(0), MetricCategory.Storage, "Samsung SSD 9100 PRO 4TB") { Kind = "NVMe" });
        devices.Upsert(new DeviceInfo(MetricIds.DiskDeviceId(1), MetricCategory.Storage, "WD_BLACK SN850X 2TB") { Kind = "NVMe" });
        devices.Upsert(new DeviceInfo("vol/C", MetricCategory.Storage, "Windows") { Kind = "Volume" });
        devices.Upsert(new DeviceInfo("vol/D", MetricCategory.Storage, "Games") { Kind = "Volume" });
    }

    private void AddPc()
    {
        Add(MetricIds.CpuLoad, MetricCategory.Cpu, MetricKind.Load, 23, "CPU utilization");
        Add(MetricIds.CpuTemperature, MetricCategory.Cpu, MetricKind.Temperature, 66, "CPU temperature");
        Add(MetricIds.CpuPower, MetricCategory.Cpu, MetricKind.Power, 96, "Package power");
        Add(MetricIds.CpuClockEffectiveAverage, MetricCategory.Cpu, MetricKind.Clock, 4710, "Effective clock");
        Add(MetricIds.CpuLoadMaxThread, MetricCategory.Cpu, MetricKind.Load, 74, "Busiest thread");
        Add(MetricIds.CpuCcdTemperature(1), MetricCategory.Cpu, MetricKind.Temperature, 64, "CCD1");
        Add(MetricIds.CpuCcdTemperature(2), MetricCategory.Cpu, MetricKind.Temperature, 58, "CCD2");
        Add(MetricIds.CpuVoltage, MetricCategory.Cpu, MetricKind.Voltage, 1.18, "Core voltage");
        for (int i = 0; i < 32; i++)
        {
            Add(MetricIds.CpuThreadLoad(i), MetricCategory.Cpu, MetricKind.Load, i < 16 ? 18 + (i * 11 % 47) : 4 + (i * 7 % 19), $"Thread {i}");
        }

        Add(MetricIds.GpuLoad(0), MetricCategory.Gpu, MetricKind.Load, 71, "GPU utilization");
        Add(MetricIds.GpuTemperature(0), MetricCategory.Gpu, MetricKind.Temperature, 63, "GPU temperature");
        Add(MetricIds.GpuHotSpotTemperature(0), MetricCategory.Gpu, MetricKind.Temperature, 74, "Hot spot");
        Add(MetricIds.GpuMemoryJunctionTemperature(0), MetricCategory.Gpu, MetricKind.Temperature, 70, "Memory junction");
        Add(MetricIds.GpuPower(0), MetricCategory.Gpu, MetricKind.Power, 388, "Board power");
        Add(MetricIds.GpuPowerLimit(0), MetricCategory.Gpu, MetricKind.Power, 575, "Power limit");
        Add(MetricIds.GpuCoreClock(0), MetricCategory.Gpu, MetricKind.Clock, 2610, "Core clock");
        Add(MetricIds.GpuMemoryClock(0), MetricCategory.Gpu, MetricKind.Clock, 14001, "Memory clock");
        Add(MetricIds.GpuMemoryUsed(0), MetricCategory.Gpu, MetricKind.Data, 11.4 * (1L << 30), "VRAM used");
        Add(MetricIds.GpuMemoryTotal(0), MetricCategory.Gpu, MetricKind.Data, 32d * (1L << 30), "VRAM total");
        Add(MetricIds.GpuMemoryLoad(0), MetricCategory.Gpu, MetricKind.Load, 35.6, "VRAM");
        Add(MetricIds.GpuFanRpm(0, 1), MetricCategory.Gpu, MetricKind.Fan, 1460, "Fan 1");
        Add(MetricIds.GpuFanPercent(0, 1), MetricCategory.Gpu, MetricKind.Load, 46, "Fan 1 duty");
        Add(MetricIds.GpuVoltage(0), MetricCategory.Gpu, MetricKind.Voltage, 925, "Core voltage");
        Add(MetricIds.GpuEncoderLoad(0), MetricCategory.Gpu, MetricKind.Load, 6, "Encoder");
        Add(MetricIds.GpuDecoderLoad(0), MetricCategory.Gpu, MetricKind.Load, 0, "Decoder");

        Add(MetricIds.MemoryUsed, MetricCategory.Memory, MetricKind.Data, 24.6 * (1L << 30), "In use");
        Add(MetricIds.MemoryAvailable, MetricCategory.Memory, MetricKind.Data, 38.9 * (1L << 30), "Available");
        Add(MetricIds.MemoryTotal, MetricCategory.Memory, MetricKind.Data, 63.5 * (1L << 30), "Usable");
        Add(MetricIds.MemoryLoad, MetricCategory.Memory, MetricKind.Load, 38.7, "Memory utilization");
        Add(MetricIds.MemoryCommitUsed, MetricCategory.Memory, MetricKind.Data, 31.2 * (1L << 30), "Committed");
        Add(MetricIds.MemoryCommitLimit, MetricCategory.Memory, MetricKind.Data, 72d * (1L << 30), "Commit limit");
        Add(MetricIds.MemoryCache, MetricCategory.Memory, MetricKind.Data, 18.3 * (1L << 30), "Cached");

        Add(MetricIds.DiskTotalRead, MetricCategory.Storage, MetricKind.Throughput, 41_000_000, "Read");
        Add(MetricIds.DiskTotalWrite, MetricCategory.Storage, MetricKind.Throughput, 9_000_000, "Write");
        foreach (var (letter, total, used) in new[] { ("C", 3996d, 1240d), ("D", 1999d, 1611d) })
        {
            Add(MetricIds.VolumeTotal(letter), MetricCategory.Storage, MetricKind.Data, total * 1e9, "Total");
            Add(MetricIds.VolumeUsed(letter), MetricCategory.Storage, MetricKind.Data, used * 1e9, "Used");
            Add(MetricIds.VolumeFree(letter), MetricCategory.Storage, MetricKind.Data, (total - used) * 1e9, "Free");
        }

        Add(MetricIds.NetworkActiveDown, MetricCategory.Network, MetricKind.Throughput, 6_800_000, "Download");
        Add(MetricIds.NetworkActiveUp, MetricCategory.Network, MetricKind.Throughput, 910_000, "Upload");
        Add(MetricIds.NetworkActiveReceivedSession, MetricCategory.Network, MetricKind.Data, 3.4e9, "Received");
        Add(MetricIds.NetworkActiveSentSession, MetricCategory.Network, MetricKind.Data, 0.42e9, "Sent");
    }

    private void AddMac()
    {
        Add(MetricIds.CpuLoad, MetricCategory.Cpu, MetricKind.Load, 18, "CPU utilization");
        Add(MetricIds.CpuTemperature, MetricCategory.Cpu, MetricKind.Temperature, 52, "CPU temperature");
        Add(MetricIds.CpuPower, MetricCategory.Cpu, MetricKind.Power, 9.4, "CPU power");
        Add(MetricIds.CpuClockAverage, MetricCategory.Cpu, MetricKind.Clock, 3720, "Average clock");
        Add(MetricIds.CpuClockPerformanceCores, MetricCategory.Cpu, MetricKind.Clock, 3912, "Performance cores clock");
        Add(MetricIds.CpuClockEfficiencyCores, MetricCategory.Cpu, MetricKind.Clock, 2184, "Efficiency cores clock");
        Add(MetricIds.CpuLoadPerformanceCores, MetricCategory.Cpu, MetricKind.Load, 14, "Performance cores");
        Add(MetricIds.CpuLoadEfficiencyCores, MetricCategory.Cpu, MetricKind.Load, 38, "Efficiency cores");
        Add(MetricIds.CpuLoadMaxThread, MetricCategory.Cpu, MetricKind.Load, 61, "Busiest core");
        Add(MetricIds.NeuralEnginePower, MetricCategory.Cpu, MetricKind.Power, 0.4, "Neural Engine power");
        Add(MetricIds.SystemPower, MetricCategory.System, MetricKind.Power, 24.5, "System power");
        for (int i = 0; i < 16; i++)
        {
            Add(MetricIds.CpuThreadLoad(i), MetricCategory.Cpu, MetricKind.Load, i < 4 ? 22 + (i * 7) : 6 + (i * 5 % 31), $"Core {i}");
        }

        Add(MetricIds.GpuLoad(0), MetricCategory.Gpu, MetricKind.Load, 34, "GPU utilization");
        Add("gpu/0/load/renderer", MetricCategory.Gpu, MetricKind.Load, 31, "Renderer");
        Add("gpu/0/load/tiler", MetricCategory.Gpu, MetricKind.Load, 12, "Tiler");
        Add(MetricIds.GpuTemperature(0), MetricCategory.Gpu, MetricKind.Temperature, 48, "GPU temperature");
        Add(MetricIds.GpuPower(0), MetricCategory.Gpu, MetricKind.Power, 6.1, "GPU power");
        Add(MetricIds.GpuCoreClock(0), MetricCategory.Gpu, MetricKind.Clock, 1088, "GPU clock");
        Add(MetricIds.GpuMemoryUsed(0), MetricCategory.Gpu, MetricKind.Data, 5.4 * (1L << 30), "Memory in use");
        Add(MetricIds.MemoryUsed, MetricCategory.Memory, MetricKind.Data, 31d * (1L << 30), "Memory used");
        Add(MetricIds.MemoryAvailable, MetricCategory.Memory, MetricKind.Data, 33d * (1L << 30), "Available");
        Add(MetricIds.MemoryTotal, MetricCategory.Memory, MetricKind.Data, 64d * (1L << 30), "Physical memory");
        Add(MetricIds.MemoryLoad, MetricCategory.Memory, MetricKind.Load, 48.4, "Memory utilization"); // 31 of 64 GB
        Add(MetricIds.MemoryApp, MetricCategory.Memory, MetricKind.Data, 21.3 * (1L << 30), "App memory");
        Add(MetricIds.MemoryWired, MetricCategory.Memory, MetricKind.Data, 4.1 * (1L << 30), "Wired memory");
        Add(MetricIds.MemoryCompressed, MetricCategory.Memory, MetricKind.Data, 5.6 * (1L << 30), "Compressed");
        Add(MetricIds.MemoryCache, MetricCategory.Memory, MetricKind.Data, 14d * (1L << 30), "Cached files");
        Add(MetricIds.MemorySwapUsed, MetricCategory.Memory, MetricKind.Data, 0.9 * (1L << 30), "Swap used");
        Add(MetricIds.BatteryCharge, MetricCategory.System, MetricKind.Level, 86, "Battery charge");
        Add(MetricIds.BatteryHealth, MetricCategory.System, MetricKind.Level, 97, "Battery health");
        Add(MetricIds.BatteryCycles, MetricCategory.System, MetricKind.Count, 112, "Charge cycles");
        Add(MetricIds.BatteryPower, MetricCategory.System, MetricKind.Power, 14.2, "Battery power");
        Add(MetricIds.DiskTotalRead, MetricCategory.Storage, MetricKind.Throughput, 24_000_000, "Read");
        Add(MetricIds.DiskTotalWrite, MetricCategory.Storage, MetricKind.Throughput, 6_000_000, "Write");
        foreach (var (key, total, used) in new[] { ("disk3s1", 1995d, 12d), ("disk3s5", 1995d, 1104d) })
        {
            Add(MetricIds.VolumeTotal(key), MetricCategory.Storage, MetricKind.Data, total * 1e9, "Total");
            Add(MetricIds.VolumeUsed(key), MetricCategory.Storage, MetricKind.Data, used * 1e9, "Used");
            Add(MetricIds.VolumeFree(key), MetricCategory.Storage, MetricKind.Data, (total - used) * 1e9, "Free");
        }

        Add(MetricIds.NetworkActiveDown, MetricCategory.Network, MetricKind.Throughput, 4_200_000, "Download");
        Add(MetricIds.NetworkActiveUp, MetricCategory.Network, MetricKind.Throughput, 640_000, "Upload");
    }

    public ProviderInitResult Initialize(IProviderContext context)
    {
        foreach (var (id, (category, kind, _, name)) in _values)
        {
            string? device = id.StartsWith("cpu", StringComparison.Ordinal) ? MetricIds.CpuDeviceId
                : id.StartsWith("gpu/0", StringComparison.Ordinal) ? MetricIds.GpuDeviceId(0)
                : id.StartsWith("vol/", StringComparison.Ordinal) ? id[..id.LastIndexOf('/')]
                : id.StartsWith("mem/", StringComparison.Ordinal) ? MetricIds.MemoryDeviceId
                : id.StartsWith("battery/", StringComparison.Ordinal) ? MetricIds.BatteryDeviceId
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
            double phase = id.Length * 1.7;
            double wobble = kind is MetricKind.Load or MetricKind.Power or MetricKind.Throughput or MetricKind.Clock or MetricKind.Fan
                ? 1 + (0.22 * Math.Sin((_tick * 0.11) + phase)) + (0.12 * Math.Sin((_tick * 0.43) + (phase * 2))) + (0.05 * Math.Sin(_tick * 1.9))
                : kind == MetricKind.Temperature ? 1 + (0.06 * Math.Sin((_tick * 0.07) + phase)) : 1;
            if (id.Contains("limit", StringComparison.Ordinal) || id.EndsWith("/total", StringComparison.Ordinal) || id.Contains("vol/", StringComparison.Ordinal)
                || id == MetricIds.MemoryLoad) // stays in step with memory in use / usable, as the real reading does
            {
                wobble = 1;
            }

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
