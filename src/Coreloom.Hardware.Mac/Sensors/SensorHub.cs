using System.Diagnostics;
using Coreloom.Core.Diagnostics;
using Coreloom.Hardware.Mac.Native;

namespace Coreloom.Hardware.Mac.Sensors;

/// <summary>A temperature sensor's reading.</summary>
public sealed record TemperatureReading(string Name, double Celsius, string Source);

/// <summary>A fan's speed, and its range when the SMC reports one.</summary>
public sealed record FanReading(int Index, string Name, double Rpm, double? MinRpm, double? MaxRpm);

/// <summary>A processor cluster on Apple silicon: its average clock while active and how busy it was.</summary>
public sealed record ClusterReading(string Name, bool IsPerformance, double ClockMhz, double ActiveFraction);

/// <summary>Everything the sensors said in one sample.</summary>
public sealed class SensorSample
{
    public IReadOnlyList<TemperatureReading> Temperatures { get; init; } = [];

    public IReadOnlyList<FanReading> Fans { get; init; } = [];

    public IReadOnlyList<ClusterReading> Clusters { get; init; } = [];

    /// <summary>Watts, or NaN when unknown.</summary>
    public double CpuPower { get; init; } = double.NaN;

    public double GpuPower { get; init; } = double.NaN;

    public double MemoryPower { get; init; } = double.NaN;

    public double NeuralEnginePower { get; init; } = double.NaN;

    public double SystemPower { get; init; } = double.NaN;

    public double GpuClockMhz { get; init; } = double.NaN;

    public double GpuActiveFraction { get; init; } = double.NaN;

    public double CpuTemperature { get; init; } = double.NaN;

    public double GpuTemperature { get; init; } = double.NaN;

    public double SsdTemperature { get; init; } = double.NaN;
}

/// <summary>
/// Reads the Mac's sensors once per frame for every provider that needs them: on Apple silicon the HID temperature
/// sensors and IOReport (energy and frequency residencies), on Intel Macs the SMC's temperature and power keys; fans
/// from the SMC on both. Every source is optional: a source that isn't there (as in a virtual machine) is skipped and
/// the readings it would give are simply missing. Nothing here needs administrator rights, and nothing is written.
/// </summary>
public sealed class SensorHub : IDisposable
{
    private const string LogCategory = "Sensors";
    private static readonly TimeSpan Reuse = TimeSpan.FromMilliseconds(250);
    private readonly object _gate = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private HidThermals? _hid;
    private IOReportSampler? _ioReport;
    private Smc.Connection? _smc;
    private SmcKeys? _smcKeys;
    private SensorSample _last = new();
    private TimeSpan? _lastAt;
    private bool _opened;
    private int _users;

    public static bool IsAppleSilicon => System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.Arm64;

    /// <summary>True in a virtual machine (kern.hv_vmm_present), where macOS exposes no hardware sensors.</summary>
    public static bool IsVirtualMachine => Darwin.SysctlLong("kern.hv_vmm_present") == 1;

    /// <summary>True when IOReport's energy and frequency counters opened (Apple silicon).</summary>
    public bool HasEnergyCounters => _ioReport is not null;

    public bool HasTemperatureSensors => _hid is { Count: > 0 } || _smcKeys is { Temperatures.Count: > 0 } || _smcKeys?.CpuTemperatureKey is not null;

    public bool HasSmc => _smc is not null;

    /// <summary>Why a sensor reading is missing, for the "not available" notes.</summary>
    public static string MissingReason => IsVirtualMachine
        ? "Not available in a virtual machine: macOS gives virtual Macs no hardware sensors."
        : "This Mac doesn't report it to apps.";

    /// <summary>What was found when the sources were opened (for the log and diagnostics).</summary>
    public string Description { get; private set; } = "not opened";

    /// <summary>Opens the sources (once, for the first provider that asks).</summary>
    public void Acquire()
    {
        lock (_gate)
        {
            _users++;
            if (_opened)
            {
                return;
            }

            _opened = true;
            var found = new List<string>();
            if (IsAppleSilicon)
            {
                _hid = Try(() => HidThermals.Open(), "HID temperature sensors");
                if (_hid is { Count: > 0 })
                {
                    found.Add($"{_hid.Count} HID temperature sensors");
                }

                _ioReport = Try(() => IOReportSampler.Open(), "IOReport");
                if (_ioReport is not null)
                {
                    found.Add("IOReport " + _ioReport.Description);
                }
            }

            _smc = Try(Smc.Connection.Open, "SMC");
            if (_smc is not null)
            {
                _smcKeys = Try(() => SmcKeys.Discover(_smc), "SMC keys");
                found.Add("SMC" + (_smcKeys is null ? string.Empty : $" ({_smcKeys.Description})"));
            }

            Description = found.Count == 0 ? (IsVirtualMachine ? "no hardware sensors (virtual machine)" : "no hardware sensors") : string.Join(", ", found);
            Log.Info(LogCategory, $"Sensors: {Description}.");
        }
    }

    public void Release()
    {
        lock (_gate)
        {
            if (--_users > 0)
            {
                return;
            }

            CloseSources();
        }
    }

    /// <summary>The latest sample, read now unless another provider read it in the last quarter second.</summary>
    public SensorSample Sample()
    {
        lock (_gate)
        {
            var now = _clock.Elapsed;
            if (_lastAt is { } last && now - last < Reuse)
            {
                return _last;
            }

            _lastAt = now;
            _last = Read();
            return _last;
        }
    }

    private SensorSample Read()
    {
        var temperatures = new List<TemperatureReading>();
        var fans = new List<FanReading>();
        double cpuTemp = double.NaN, gpuTemp = double.NaN, ssdTemp = double.NaN;
        double cpuPower = double.NaN, gpuPower = double.NaN, memoryPower = double.NaN, anePower = double.NaN, systemPower = double.NaN;
        double gpuClock = double.NaN, gpuActive = double.NaN;
        IReadOnlyList<ClusterReading> clusters = [];

        if (_hid is not null)
        {
            try
            {
                var readings = _hid.Read();
                temperatures.AddRange(readings);
                cpuTemp = HidThermals.CpuTemperature(readings);
                gpuTemp = HidThermals.GpuTemperature(readings);
                ssdTemp = HidThermals.Average(readings, r => r.Name.Contains("NAND", StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                Log.WarnThrottled("hid-read", TimeSpan.FromMinutes(10), LogCategory, "Reading the HID temperature sensors failed.", ex);
            }
        }

        if (_ioReport is not null)
        {
            try
            {
                if (_ioReport.Sample() is { } report)
                {
                    cpuPower = report.CpuPower;
                    gpuPower = report.GpuPower;
                    memoryPower = report.MemoryPower;
                    anePower = report.NeuralEnginePower;
                    gpuClock = report.GpuClockMhz;
                    gpuActive = report.GpuActiveFraction;
                    clusters = report.Clusters;
                }
            }
            catch (Exception ex)
            {
                Log.WarnThrottled("ioreport-read", TimeSpan.FromMinutes(10), LogCategory, "Reading IOReport failed.", ex);
            }
        }

        if (_smc is not null && _smcKeys is not null)
        {
            try
            {
                fans.AddRange(_smcKeys.Fans(_smc));
                systemPower = _smcKeys.SystemPower(_smc);
                if (!IsAppleSilicon)
                {
                    foreach (var (name, key) in _smcKeys.Temperatures)
                    {
                        if (_smc.Number(key) is double t && t is > 0 and < 150)
                        {
                            temperatures.Add(new TemperatureReading(name, t, "SMC " + key));
                        }
                    }

                    cpuTemp = _smcKeys.CpuTemperature(_smc);
                    gpuTemp = _smcKeys.GpuTemperature(_smc);
                    cpuPower = _smcKeys.CpuPower(_smc);
                    gpuPower = _smcKeys.GpuPower(_smc);
                }
            }
            catch (Exception ex)
            {
                Log.WarnThrottled("smc-read", TimeSpan.FromMinutes(10), LogCategory, "Reading the SMC failed.", ex);
            }
        }

        return new SensorSample
        {
            Temperatures = temperatures,
            Fans = fans,
            Clusters = clusters,
            CpuPower = cpuPower,
            GpuPower = gpuPower,
            MemoryPower = memoryPower,
            NeuralEnginePower = anePower,
            SystemPower = systemPower,
            GpuClockMhz = gpuClock,
            GpuActiveFraction = gpuActive,
            CpuTemperature = cpuTemp,
            GpuTemperature = gpuTemp,
            SsdTemperature = ssdTemp,
        };
    }

    private static T? Try<T>(Func<T?> open, string what)
        where T : class
    {
        try
        {
            return open();
        }
        catch (Exception ex)
        {
            Log.Warn(LogCategory, $"{what} unavailable.", ex);
            return null;
        }
    }

    private void CloseSources()
    {
        _hid?.Dispose();
        _hid = null;
        _ioReport?.Dispose();
        _ioReport = null;
        _smc?.Dispose();
        _smc = null;
        _smcKeys = null;
        _opened = false;
        _lastAt = null;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            CloseSources();
        }
    }
}
