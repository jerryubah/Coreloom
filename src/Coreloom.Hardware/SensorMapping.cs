using System.Globalization;
using System.Text.RegularExpressions;
using Coreloom.Core.Telemetry;

namespace Coreloom.Hardware;

/// <summary>Library-independent view of a LibreHardwareMonitor hardware type.</summary>
public enum SensorHardwareKind
{
    Cpu,
    GpuNvidia,
    GpuAmd,
    GpuIntel,
    Memory,
    Storage,
    Motherboard,
    SuperIO,
    Cooler,
    EmbeddedController,
    Network,
    Other,
}

/// <summary>Library-independent view of a LibreHardwareMonitor sensor type (same names as LHM's SensorType).</summary>
public enum SensorValueKind
{
    Voltage,
    Current,
    Power,
    Clock,
    Temperature,
    Load,
    Frequency,
    Fan,
    Flow,
    Control,
    Level,
    Factor,
    Data,
    SmallData,
    Throughput,
    TimeSpan,
    Timing,
    Energy,
    Noise,
    Conductivity,
    Humidity,
    Unknown,
}

/// <summary>Where a raw sensor should also be published as a canonical metric.</summary>
public sealed record CanonicalMapping(string Id, string Name, MetricKind Kind, int Priority, string Group, MetricCategory Category, bool Track = true);

/// <summary>
/// Maps LibreHardwareMonitor sensor names to Coreloom's canonical metric ids. Kept free of LHM types so the
/// rules can be unit tested on any platform. Names follow LibreHardwareMonitorLib 0.9.x conventions.
/// </summary>
public static partial class SensorMapping
{
    public const int LhmGpuPriority = 50;
    public const int LhmCpuPriority = 40;
    public const int LhmStoragePriority = 40;

    public static MetricKind ToKind(SensorValueKind kind) => kind switch
    {
        SensorValueKind.Voltage => MetricKind.Voltage,
        SensorValueKind.Current => MetricKind.Current,
        SensorValueKind.Power => MetricKind.Power,
        SensorValueKind.Clock => MetricKind.Clock,
        SensorValueKind.Temperature => MetricKind.Temperature,
        SensorValueKind.Load => MetricKind.Load,
        SensorValueKind.Frequency => MetricKind.Frequency,
        SensorValueKind.Fan => MetricKind.Fan,
        SensorValueKind.Control => MetricKind.Control,
        SensorValueKind.Level => MetricKind.Level,
        SensorValueKind.Factor => MetricKind.Factor,
        SensorValueKind.Data or SensorValueKind.SmallData => MetricKind.Data,
        SensorValueKind.Throughput => MetricKind.Throughput,
        SensorValueKind.TimeSpan => MetricKind.Duration,
        _ => MetricKind.Factor,
    };

    /// <summary>Multiplier from the LHM unit to Coreloom's base unit (bytes for data sizes).</summary>
    public static double ScaleFor(SensorValueKind kind) => kind switch
    {
        SensorValueKind.Data => 1024.0 * 1024 * 1024,   // GB (2^30) -> bytes
        SensorValueKind.SmallData => 1024.0 * 1024,     // MB (2^20) -> bytes
        _ => 1.0,
    };

    public static MetricCategory CategoryOf(SensorHardwareKind kind) => kind switch
    {
        SensorHardwareKind.Cpu => MetricCategory.Cpu,
        SensorHardwareKind.GpuNvidia or SensorHardwareKind.GpuAmd or SensorHardwareKind.GpuIntel => MetricCategory.Gpu,
        SensorHardwareKind.Memory => MetricCategory.Memory,
        SensorHardwareKind.Storage => MetricCategory.Storage,
        SensorHardwareKind.Motherboard or SensorHardwareKind.SuperIO or SensorHardwareKind.Cooler or SensorHardwareKind.EmbeddedController => MetricCategory.Motherboard,
        SensorHardwareKind.Network => MetricCategory.Network,
        _ => MetricCategory.System,
    };

    public static string GroupName(SensorValueKind kind) => kind switch
    {
        SensorValueKind.Temperature => "Temperatures",
        SensorValueKind.Load => "Load",
        SensorValueKind.Clock => "Clocks",
        SensorValueKind.Power => "Power",
        SensorValueKind.Voltage => "Voltages",
        SensorValueKind.Current => "Currents",
        SensorValueKind.Fan => "Fans",
        SensorValueKind.Control => "Fan control",
        SensorValueKind.Data or SensorValueKind.SmallData => "Data",
        SensorValueKind.Throughput => "Throughput",
        SensorValueKind.Level => "Levels",
        SensorValueKind.Factor => "Factors",
        _ => kind.ToString(),
    };

    public static CanonicalMapping? MapCpu(string name, SensorValueKind kind)
    {
        const MetricCategory cat = MetricCategory.Cpu;
        switch (kind)
        {
            case SensorValueKind.Temperature:
                return name switch
                {
                    "Core (Tctl/Tdie)" => new(MetricIds.CpuTemperature, "CPU temperature", MetricKind.Temperature, LhmCpuPriority, "Temperatures", cat),
                    "Core (Tdie)" => new(MetricIds.CpuTemperature, "CPU temperature", MetricKind.Temperature, LhmCpuPriority - 1, "Temperatures", cat),
                    "Core (Tctl)" => new(MetricIds.CpuTemperature, "CPU temperature", MetricKind.Temperature, LhmCpuPriority - 2, "Temperatures", cat),
                    "CPU Package" => new(MetricIds.CpuTemperature, "CPU temperature", MetricKind.Temperature, LhmCpuPriority, "Temperatures", cat),
                    "CCDs Max (Tdie)" => new(MetricIds.CpuTemperature, "CPU temperature", MetricKind.Temperature, LhmCpuPriority - 10, "Temperatures", cat),
                    "Core Max" => new(MetricIds.CpuTemperature, "CPU temperature", MetricKind.Temperature, LhmCpuPriority - 15, "Temperatures", cat),
                    "SoC" => new(MetricIds.CpuSocTemperature, "SoC temperature", MetricKind.Temperature, LhmCpuPriority, "Temperatures", cat),
                    "IOD Hotspot" => new(MetricIds.CpuIodTemperature, "I/O die hot spot", MetricKind.Temperature, LhmCpuPriority, "Temperatures", cat),
                    _ when CcdPattern().Match(name) is { Success: true } m =>
                        new(MetricIds.CpuCcdTemperature(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)), $"CCD{m.Groups[1].Value} temperature", MetricKind.Temperature, LhmCpuPriority, "Temperatures", cat),
                    _ when IntelCorePattern().Match(name) is { Success: true } m =>
                        new(MetricIds.CpuCoreTemperature(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)), $"Core {m.Groups[1].Value} temperature", MetricKind.Temperature, LhmCpuPriority, "Temperatures", cat),
                    _ => null,
                };

            case SensorValueKind.Power:
                if (name is "Package" or "CPU Package")
                {
                    return new(MetricIds.CpuPower, "CPU package power", MetricKind.Power, LhmCpuPriority, "Power", cat);
                }

                switch (name)
                {
                    case "CPU PPT":
                        return new(MetricIds.CpuPpt, "PPT (socket power)", MetricKind.Power, LhmCpuPriority, "Power", cat);
                    case "Core Power":
                        return new(MetricIds.CpuCoresPower, "Cores power", MetricKind.Power, LhmCpuPriority, "Power", cat, Track: false);
                    case "SOC Power" or "SoC":
                        return new(MetricIds.CpuSocPower, "SoC power", MetricKind.Power, LhmCpuPriority, "Power", cat, Track: false);
                    case "Misc Power":
                        return new(MetricIds.CpuMiscPower, "Misc power", MetricKind.Power, LhmCpuPriority, "Power", cat, Track: false);
                }

                if (CoreSmuPowerPattern().Match(name) is { Success: true } power)
                {
                    return new(MetricIds.CpuCorePower(int.Parse(power.Groups[1].Value, CultureInfo.InvariantCulture)), $"Core {power.Groups[1].Value} power", MetricKind.Power, LhmCpuPriority, "Power", cat, Track: false);
                }

                return null;

            case SensorValueKind.Clock:
                if (name == "Cores (Average)")
                {
                    return new(MetricIds.CpuClockAverage, "Average core clock", MetricKind.Clock, LhmCpuPriority, "Clocks", cat);
                }

                if (name == "Cores (Average Effective)")
                {
                    return new(MetricIds.CpuClockEffectiveAverage, "Average effective clock", MetricKind.Clock, LhmCpuPriority, "Clocks", cat);
                }

                switch (name)
                {
                    case "Fabric":
                        return new(MetricIds.CpuFabricClock, "Fabric clock (FCLK)", MetricKind.Clock, LhmCpuPriority, "Clocks", cat, Track: false);
                    case "Uncore":
                        return new(MetricIds.CpuUncoreClock, "Memory controller clock (UCLK)", MetricKind.Clock, LhmCpuPriority, "Clocks", cat, Track: false);
                    case "Memory":
                        return new(MetricIds.CpuMemoryClock, "Memory clock (MCLK)", MetricKind.Clock, LhmCpuPriority, "Clocks", cat, Track: false);
                }

                if (CoreClockPattern().Match(name) is { Success: true } clock)
                {
                    int core = int.Parse(clock.Groups[1].Value, CultureInfo.InvariantCulture);
                    bool effective = clock.Groups[2].Success;
                    return effective
                        ? new(MetricIds.CpuCoreEffectiveClock(core), $"Core {core} effective clock", MetricKind.Clock, LhmCpuPriority, "Clocks", cat)
                        : new(MetricIds.CpuCoreClock(core), $"Core {core} clock", MetricKind.Clock, LhmCpuPriority, "Clocks", cat);
                }

                return null;

            case SensorValueKind.Voltage:
                if (name.StartsWith("Core (SVI", StringComparison.Ordinal) || name is "CPU Core" or "Core")
                {
                    return new(MetricIds.CpuVoltage, "Core voltage", MetricKind.Voltage, LhmCpuPriority, "Voltages", cat);
                }

                if (name == "VDDCR")
                {
                    return new(MetricIds.CpuVoltage, "Core voltage", MetricKind.Voltage, LhmCpuPriority - 1, "Voltages", cat);
                }

                return name.StartsWith("SoC (SVI", StringComparison.Ordinal) || name == "VDDCR SoC"
                    ? new(MetricIds.CpuSocVoltage, "SoC voltage", MetricKind.Voltage, LhmCpuPriority, "Voltages", cat, Track: false)
                    : null;

            case SensorValueKind.Current:
                return name switch
                {
                    "TDC" => new(MetricIds.CpuTdc, "TDC (sustained current)", MetricKind.Current, LhmCpuPriority, "Currents", cat),
                    "EDC" => new(MetricIds.CpuEdc, "EDC (peak current)", MetricKind.Current, LhmCpuPriority, "Currents", cat),
                    "SoC" => new(MetricIds.CpuSocCurrent, "SoC current", MetricKind.Current, LhmCpuPriority, "Currents", cat, Track: false),
                    _ => null,
                };

            case SensorValueKind.Load:
                return name switch
                {
                    "CPU Total" => new(MetricIds.CpuLoad, "CPU utilization", MetricKind.Load, 10, "Load", cat),
                    "CPU Core Max" => new(MetricIds.CpuLoadMaxThread, "Busiest thread", MetricKind.Load, 10, "Load", cat),
                    _ => null,
                };

            default:
                return null;
        }
    }

    public static CanonicalMapping? MapGpu(int slot, string name, SensorValueKind kind)
    {
        const MetricCategory cat = MetricCategory.Gpu;
        int p = LhmGpuPriority;
        switch (kind)
        {
            case SensorValueKind.Temperature:
                return name switch
                {
                    "GPU Core" => new(MetricIds.GpuTemperature(slot), "GPU temperature", MetricKind.Temperature, p, "Temperatures", cat),
                    "GPU Hot Spot" => new(MetricIds.GpuHotSpotTemperature(slot), "Hot spot temperature", MetricKind.Temperature, p, "Temperatures", cat),
                    "GPU Memory Junction" or "GPU Memory" => new(MetricIds.GpuMemoryJunctionTemperature(slot), "Memory junction temperature", MetricKind.Temperature, p, "Temperatures", cat),
                    _ => null,
                };

            case SensorValueKind.Load:
                return name switch
                {
                    "GPU Core" => new(MetricIds.GpuLoad(slot), "GPU utilization", MetricKind.Load, p, "Load", cat),
                    "GPU Memory Controller" => new(MetricIds.GpuMemoryControllerLoad(slot), "Memory controller load", MetricKind.Load, p, "Load", cat),
                    "GPU Video Engine" => new(MetricIds.GpuVideoEngineLoad(slot), "Video engine load", MetricKind.Load, p, "Load", cat),
                    "GPU Bus" => new(MetricIds.GpuBusLoad(slot), "Bus load", MetricKind.Load, p, "Load", cat),
                    "GPU Memory" => new(MetricIds.GpuMemoryLoad(slot), "VRAM utilization", MetricKind.Load, p, "Memory", cat),
                    _ => null,
                };

            case SensorValueKind.Clock:
                return name switch
                {
                    "GPU Core" => new(MetricIds.GpuCoreClock(slot), "Core clock", MetricKind.Clock, p, "Clocks", cat),
                    "GPU Memory" => new(MetricIds.GpuMemoryClock(slot), "Memory clock", MetricKind.Clock, p, "Clocks", cat),
                    "GPU Shader" => new(MetricIds.GpuShaderClock(slot), "Shader clock", MetricKind.Clock, p, "Clocks", cat),
                    "GPU Video" => new(MetricIds.GpuVideoClock(slot), "Video clock", MetricKind.Clock, p, "Clocks", cat),
                    _ => null,
                };

            case SensorValueKind.Power:
                return name is "GPU Package" or "GPU Power"
                    ? new(MetricIds.GpuPower(slot), "Board power draw", MetricKind.Power, p, "Power", cat)
                    : null;

            case SensorValueKind.Voltage:
                return name is "GPU Core Voltage" or "GPU Core"
                    ? new(MetricIds.GpuVoltage(slot), "Core voltage", MetricKind.Voltage, p, "Voltages", cat)
                    : null;

            case SensorValueKind.Fan when GpuFanPattern().Match(name) is { Success: true } fan:
            {
                int n = fan.Groups[2].Success ? int.Parse(fan.Groups[2].Value, CultureInfo.InvariantCulture) : 1;
                return new(MetricIds.GpuFanRpm(slot, n), $"Fan {n} speed", MetricKind.Fan, p, "Cooling", cat);
            }

            case SensorValueKind.Control when GpuFanPattern().Match(name) is { Success: true } control:
            {
                int n = control.Groups[2].Success ? int.Parse(control.Groups[2].Value, CultureInfo.InvariantCulture) : 1;
                return new(MetricIds.GpuFanPercent(slot, n), $"Fan {n} duty", MetricKind.Control, p, "Cooling", cat);
            }

            case SensorValueKind.SmallData:
                return name switch
                {
                    "GPU Memory Used" => new(MetricIds.GpuMemoryUsed(slot), "VRAM used", MetricKind.Data, p, "Memory", cat),
                    "GPU Memory Total" => new(MetricIds.GpuMemoryTotal(slot), "VRAM total", MetricKind.Data, p, "Memory", cat, Track: false),
                    _ => null,
                };

            case SensorValueKind.Throughput:
                return name switch
                {
                    "GPU PCIe Rx" => new(MetricIds.GpuPcieRx(slot), "PCIe receive", MetricKind.Throughput, p, "Bus", cat),
                    "GPU PCIe Tx" => new(MetricIds.GpuPcieTx(slot), "PCIe transmit", MetricKind.Throughput, p, "Bus", cat),
                    _ => null,
                };

            default:
                return null;
        }
    }

    public static CanonicalMapping? MapStorage(int diskNumber, string name, SensorValueKind kind)
    {
        const MetricCategory cat = MetricCategory.Storage;
        return kind switch
        {
            SensorValueKind.Temperature when name.Contains("Composite", StringComparison.OrdinalIgnoreCase) || name is "Temperature" or "Temperature 1" or "Temperature #1"
                => new(MetricIds.DiskTemperature(diskNumber), "Temperature", MetricKind.Temperature, LhmStoragePriority, "Temperatures", cat),
            SensorValueKind.Level when name == "Percentage Used"
                => new(MetricIds.DiskLifeUsed(diskNumber), "Endurance used", MetricKind.Level, LhmStoragePriority, "Health", cat, Track: false),
            SensorValueKind.Level when name == "Available Spare"
                => new(MetricIds.DiskAvailableSpare(diskNumber), "Available spare", MetricKind.Level, LhmStoragePriority, "Health", cat, Track: false),
            _ => null,
        };
    }

    public static CanonicalMapping? MapBoard(string name, SensorValueKind kind)
    {
        const MetricCategory cat = MetricCategory.Motherboard;
        string slug = Slug(name);
        return kind switch
        {
            SensorValueKind.Fan => new($"board/fan/{slug}", name, MetricKind.Fan, LhmCpuPriority, "Fans", cat),
            SensorValueKind.Temperature => new($"board/temp/{slug}", name, MetricKind.Temperature, LhmCpuPriority, "Temperatures", cat),
            _ => null,
        };
    }

    public static string Slug(string name)
    {
        var chars = name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
        string slug = string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
        return slug.Length == 0 ? "sensor" : slug;
    }

    [GeneratedRegex(@"^CCD\s?(\d+)\s*\(Tdie\)$")]
    private static partial Regex CcdPattern();

    [GeneratedRegex(@"^CPU Core #(\d+)$")]
    private static partial Regex IntelCorePattern();

    [GeneratedRegex(@"^(?:CPU )?Core #(\d+)( \(Effective\))?$")]
    private static partial Regex CoreClockPattern();

    [GeneratedRegex(@"^Core #(\d+) \(SMU\)$")]
    private static partial Regex CoreSmuPowerPattern();

    [GeneratedRegex(@"^GPU( Fan)?(?: (\d+))?$")]
    private static partial Regex GpuFanPattern();
}
