using System.Globalization;
using Coreloom.Core.Diagnostics;
using Coreloom.Core.Formatting;
using Coreloom.Core.Hardware;
using Coreloom.Core.Telemetry;
using Coreloom.Hardware.Native;
using Coreloom.Hardware.SystemInfo;

namespace Coreloom.Hardware.Providers;

/// <summary>
/// CPU utilization from Windows performance counters (no administrator rights needed).
/// Uses "% Processor Utility" — the counter Task Manager uses on Windows 8+ — for total and
/// per-logical-processor load, falling back to "% Processor Time" and finally to
/// NtQuerySystemInformation if PDH is unavailable. Also derives the Task-Manager-style
/// "speed" (% Processor Performance × nominal frequency), clearly labelled as derived.
/// Temperatures, package power and true per-core clocks need ring-0 access and come from
/// LibreHardwareMonitor.
/// </summary>
public sealed class CpuProvider : ITelemetryProvider
{
    public const int Priority = 50;
    private const string LogCategory = "CPU";

    private readonly List<KeyValuePair<string, double>> _scratch = [];
    private PdhQuery? _query;
    private PdhCounter? _perThread;
    private PdhCounter? _performance;
    private PdhCounter? _frequency;
    private PdhCounter? _limit;
    private bool _usingUtility;
    private Dictionary<string, int>? _instanceToIndex;
    private MetricHandle? _total;
    private MetricHandle? _maxThread;
    private MetricHandle? _derivedClock;
    private MetricHandle? _performanceMetric;
    private MetricHandle? _limitMetric;
    private MetricHandle[] _threads = [];
    private CpuTimesSampler? _fallback;
    private int _baseMhz;
    private string _mode = string.Empty;

    public string Name => "Windows CPU counters";

    public MonitoringSection Section => MonitoringSection.Cpu;

    public int InitStage => 0;

    public TimeSpan MinimumPollInterval => TimeSpan.Zero;

    public ProviderInitResult Initialize(IProviderContext context)
    {
        Shutdown();
        string cpuName = SystemInfoReader.ReadCpuName();
        _baseMhz = SystemInfoReader.ReadCpuBaseMhz();
        var (cores, l3, pCores, eCores) = SystemInfoReader.SafeTopology();
        int threads = Environment.ProcessorCount;
        var cpuid = CpuIdNative.Read();

        RegisterDevice(context, cpuName, cores, threads, l3, pCores, eCores, cpuid);

        _total = context.Register(new MetricDescriptor(MetricIds.CpuLoad, "CPU utilization", MetricCategory.Cpu, MetricKind.Load)
        {
            DeviceId = MetricIds.CpuDeviceId,
            DeviceName = cpuName,
            Group = "Load",
        }, Provenance.Measured, Priority, "Windows performance counter \\Processor Information(_Total)\\% Processor Utility (Task Manager's metric)");

        _maxThread = context.Register(new MetricDescriptor(MetricIds.CpuLoadMaxThread, "Busiest thread", MetricCategory.Cpu, MetricKind.Load)
        {
            DeviceId = MetricIds.CpuDeviceId,
            DeviceName = cpuName,
            Group = "Load",
        }, Provenance.Derived, Priority, "Maximum of the per-logical-processor utilization values");

        _threads = new MetricHandle[threads];
        for (int i = 0; i < threads; i++)
        {
            _threads[i] = context.Register(new MetricDescriptor(MetricIds.CpuThreadLoad(i), $"Thread {i}", MetricCategory.Cpu, MetricKind.Load)
            {
                DeviceId = MetricIds.CpuDeviceId,
                DeviceName = cpuName,
                Group = "Threads",
                SortOrder = i,
            }, Provenance.Measured, Priority);
        }

        try
        {
            _query = PdhQuery.Open();
            _perThread = _query.TryAddCounter(@"\Processor Information(*)\% Processor Utility", out _);
            _usingUtility = _perThread is not null;
            _perThread ??= _query.TryAddCounter(@"\Processor Information(*)\% Processor Time", out _)
                ?? _query.TryAddCounter(@"\Processor(*)\% Processor Time", out _);
            _performance = _query.TryAddCounter(@"\Processor Information(_Total)\% Processor Performance", out _);
            _frequency = _query.TryAddCounter(@"\Processor Information(_Total)\Processor Frequency", out _);
            _limit = _query.TryAddCounter(@"\Processor Information(_Total)\% Performance Limit", out _);
            if (_perThread is null)
            {
                throw new PdhException("No CPU utilization counter could be added", 0);
            }

            _query.Collect(); // prime rate counters
            _mode = _usingUtility ? "% Processor Utility" : "% Processor Time";
        }
        catch (Exception ex)
        {
            Log.Warn(LogCategory, "PDH CPU counters unavailable; falling back to NtQuerySystemInformation.", ex);
            _query?.Dispose();
            _query = null;
            _perThread = null;
            _performance = null;
            _frequency = null;
            _limit = null;
            _fallback = new CpuTimesSampler(threads);
            _fallback.Sample(out _, out _);
            _mode = "kernel CPU times";
        }

        if (_performance is not null && (_frequency is not null || _baseMhz > 0))
        {
            _derivedClock = context.Register(new MetricDescriptor(MetricIds.CpuClockDerived, "Processor speed (estimated)", MetricCategory.Cpu, MetricKind.Clock)
            {
                DeviceId = MetricIds.CpuDeviceId,
                DeviceName = cpuName,
                Group = "Clocks",
            }, Provenance.Derived, Priority, "% Processor Performance × nominal frequency — the same estimate Task Manager shows as \"Speed\". Not a sensor reading.");
        }

        if (_performance is not null)
        {
            _performanceMetric = context.Register(new MetricDescriptor(MetricIds.CpuPerformance, "Boost (of base clock)", MetricCategory.Cpu, MetricKind.Level)
            {
                DeviceId = MetricIds.CpuDeviceId,
                DeviceName = cpuName,
                Group = "Clocks",
            }, Provenance.Measured, Priority, "Windows performance counter \\Processor Information(_Total)\\% Processor Performance: the clock relative to base, over 100 % while boosting.");
        }

        if (_limit is not null)
        {
            _limitMetric = context.Register(new MetricDescriptor(MetricIds.CpuPerformanceLimit, "Performance limit", MetricCategory.Cpu, MetricKind.Level)
            {
                DeviceId = MetricIds.CpuDeviceId,
                DeviceName = cpuName,
                Group = "Throttling",
            }, Provenance.Measured, Priority, "Windows performance counter \\Processor Information(_Total)\\% Performance Limit: the performance the platform allows, relative to base. Under 100 % means power, heat or policy is holding the processor back.");
        }

        string lowLevelReason = context.Capabilities.LowLevelSensorsAccessible
            ? "Not reported by LibreHardwareMonitor for this processor."
            : "Requires running Coreloom as administrator with the PawnIO driver installed (read via LibreHardwareMonitor).";
        context.SetUnavailableReason(MetricIds.CpuTemperature, lowLevelReason);
        context.SetUnavailableReason(MetricIds.CpuPower, lowLevelReason);
        context.SetUnavailableReason(MetricIds.CpuClockEffectiveAverage, lowLevelReason);
        context.SetUnavailableReason(MetricIds.CpuClockAverage, lowLevelReason);
        context.SetUnavailableReason(MetricIds.CpuVoltage, lowLevelReason);
        if (cpuid.IsAmd || vendorIsAmd(cpuName))
        {
            string smu = context.Capabilities.LowLevelSensorsAccessible
                ? "Not in this processor's SMU power table as LibreHardwareMonitor reads it (it knows the tables of some Ryzen 3000 and 5000 desktop models and of Ryzen 7000 and 9000)."
                : lowLevelReason;
            foreach (string id in new[] { MetricIds.CpuPpt, MetricIds.CpuTdc, MetricIds.CpuEdc, MetricIds.CpuCoresPower, MetricIds.CpuSocPower, MetricIds.CpuMiscPower,
                MetricIds.CpuSocCurrent, MetricIds.CpuFabricClock, MetricIds.CpuUncoreClock, MetricIds.CpuMemoryClock, MetricIds.CpuSocTemperature, MetricIds.CpuIodTemperature })
            {
                context.SetUnavailableReason(id, smu);
            }

            context.SetUnavailableReason(MetricIds.CpuSocVoltage, context.Capabilities.LowLevelSensorsAccessible
                ? "LibreHardwareMonitor reads SoC voltage from SVI2 telemetry (Ryzen 1000 to 5000) or the SMU table (Ryzen 7000 and 9000); this processor reports neither."
                : lowLevelReason);
        }

        static bool vendorIsAmd(string name) => name.Contains("AMD", StringComparison.OrdinalIgnoreCase);

        return ProviderInitResult.Running($"{cpuName}: {threads} threads via {_mode}");
    }

    private void RegisterDevice(IProviderContext context, string cpuName, int cores, int threads, long l3, int pCores, int eCores, CpuIdInfo cpuid)
    {
        var f = new UnitFormatter(culture: CultureInfo.InvariantCulture);
        var props = new List<KeyValuePair<string, string>>();
        if (cores > 0)
        {
            props.Add(new("Cores", pCores > 0 ? $"{cores} ({pCores}P + {eCores}E)" : cores.ToString(CultureInfo.InvariantCulture)));
        }

        props.Add(new("Threads", threads.ToString(CultureInfo.InvariantCulture)));
        if (_baseMhz > 0)
        {
            props.Add(new("Base clock", f.ClockGigahertz(_baseMhz).ToString()));
        }

        if (l3 > 0)
        {
            props.Add(new("L3 cache", f.Bytes(l3, 0).ToString()));
        }

        bool amd = cpuid.IsAmd || SystemInfoReader.ReadCpuVendor() == "AuthenticAMD";
        if (amd)
        {
            var profile = AmdProcessors.Create(cpuName, cpuid.IsKnown ? cpuid.Family : null, cpuid.IsKnown ? cpuid.Model : null);
            if (profile.ArchitectureText is { } architecture)
            {
                props.Add(new("Architecture", architecture));
            }

            props.Add(new("Type", profile.SegmentText));
            if (profile.TdpWatts is int tdp)
            {
                props.Add(new("TDP", $"{tdp} W (stock PPT {profile.StockPptWatts} W)"));
            }

            if (profile.HasVCache)
            {
                props.Add(new("3D V-Cache", "Yes"));
            }
        }

        if (cpuid.IsKnown)
        {
            props.Add(new("CPUID", cpuid.Signature));
            if (cpuid.InstructionSets.Count > 0)
            {
                props.Add(new("Instruction sets", string.Join(", ", cpuid.InstructionSets.Where(i => i is not ("SSE2" or "SSE3" or "SSSE3" or "SSE4.1")))));
            }

            var features = CpuIdInfo.Describe(cpuid.Features, amd);
            if (features.Count > 0)
            {
                props.Add(new("Features", string.Join(", ", features)));
            }
        }

        string? vendor = SystemInfoReader.ReadCpuVendor() switch
        {
            "AuthenticAMD" => "AMD",
            "GenuineIntel" => "Intel",
            var other => other,
        };

        context.UpsertDevice(new DeviceInfo(MetricIds.CpuDeviceId, MetricCategory.Cpu, cpuName)
        {
            Vendor = vendor,
            Kind = "Processor",
            Source = "Windows",
            Properties = props,
        });
    }

    public void Poll(IMetricWriter writer)
    {
        if (_fallback is not null)
        {
            if (_fallback.Sample(out double total, out double[] perCpu))
            {
                writer.Write(_total!, total);
                WriteThreads(writer, perCpu);
            }

            return;
        }

        var query = _query;
        if (query is null || _perThread is null)
        {
            return;
        }

        uint status = query.Collect();
        if (status != PdhNative.ErrorSuccess)
        {
            throw new PdhException("PdhCollectQueryData failed", status);
        }

        if (_perThread.TryGetArray(_scratch))
        {
            _instanceToIndex ??= BuildInstanceMap(_scratch);
            var values = new double[_threads.Length];
            Array.Fill(values, double.NaN);
            foreach (var (name, value) in _scratch)
            {
                if (name.EndsWith("_Total", StringComparison.OrdinalIgnoreCase))
                {
                    if (name == "_Total" || !_scratch.Any(p => p.Key == "_Total"))
                    {
                        writer.Write(_total!, Math.Clamp(value, 0, 100));
                    }

                    continue;
                }

                if (_instanceToIndex.TryGetValue(name, out int index) && index < values.Length)
                {
                    values[index] = Math.Clamp(value, 0, 100);
                }
            }

            WriteThreads(writer, values);
        }

        if (_limitMetric is not null && _limit!.TryGetValue(out double limit, noCap100: true) && limit > 0)
        {
            writer.Write(_limitMetric, limit);
        }

        if (_performanceMetric is not null && _performance!.TryGetValue(out double boost, noCap100: true) && boost > 0)
        {
            writer.Write(_performanceMetric, boost);
        }

        if (_derivedClock is not null && _performance!.TryGetValue(out double performance, noCap100: true))
        {
            double nominal = _frequency is not null && _frequency.TryGetValue(out double freq) && freq > 0 ? freq : _baseMhz;
            if (nominal > 0 && performance > 0)
            {
                writer.Write(_derivedClock, nominal * performance / 100.0);
            }
        }
    }

    private void WriteThreads(IMetricWriter writer, double[] values)
    {
        double max = double.NaN;
        for (int i = 0; i < values.Length && i < _threads.Length; i++)
        {
            if (double.IsFinite(values[i]))
            {
                writer.Write(_threads[i], values[i]);
                max = double.IsNaN(max) ? values[i] : Math.Max(max, values[i]);
            }
        }

        if (double.IsFinite(max))
        {
            writer.Write(_maxThread!, max);
        }
    }

    /// <summary>Maps PDH instance names ("0,0", "0,1"... or "0", "1"...) to logical processor indices.</summary>
    internal static Dictionary<string, int> BuildInstanceMap(IEnumerable<KeyValuePair<string, double>> instances)
    {
        var parsed = new List<(string Name, int Group, int Index)>();
        foreach (var (name, _) in instances)
        {
            if (name.Contains("_Total", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string[] parts = name.Split(',');
            if (parts.Length == 2
                && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int group)
                && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int index))
            {
                parsed.Add((name, group, index));
            }
            else if (int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out int single))
            {
                parsed.Add((name, 0, single));
            }
        }

        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        int logical = 0;
        foreach (var item in parsed.OrderBy(p => p.Group).ThenBy(p => p.Index))
        {
            map[item.Name] = logical++;
        }

        return map;
    }

    public void Shutdown()
    {
        _query?.Dispose();
        _query = null;
        _perThread = null;
        _performance = null;
        _frequency = null;
        _limit = null;
        _instanceToIndex = null;
        _fallback = null;
        _derivedClock = null;
        _performanceMetric = null;
        _limitMetric = null;
    }

    public void Dispose() => Shutdown();
}

/// <summary>Per-processor load from kernel idle/kernel/user times (fallback when PDH is broken).</summary>
internal sealed unsafe class CpuTimesSampler
{
    private readonly int _count;
    private SystemProcessorPerformanceInformation[] _previous;

    public CpuTimesSampler(int count)
    {
        _count = Math.Clamp(count, 1, 64);
        _previous = new SystemProcessorPerformanceInformation[_count];
    }

    public bool Sample(out double total, out double[] perCpu)
    {
        var current = new SystemProcessorPerformanceInformation[_count];
        uint size = (uint)(sizeof(SystemProcessorPerformanceInformation) * _count);
        fixed (SystemProcessorPerformanceInformation* p = current)
        {
            int status = Win32.NtQuerySystemInformation(Win32.SystemProcessorPerformanceInformationClass, p, size, out _);
            if (status != 0)
            {
                total = double.NaN;
                perCpu = [];
                return false;
            }
        }

        perCpu = new double[_count];
        double busySum = 0, totalSum = 0;
        for (int i = 0; i < _count; i++)
        {
            long idle = current[i].IdleTime - _previous[i].IdleTime;
            long all = (current[i].KernelTime - _previous[i].KernelTime) + (current[i].UserTime - _previous[i].UserTime); // kernel time includes idle
            perCpu[i] = all > 0 ? Math.Clamp((all - idle) * 100.0 / all, 0, 100) : 0;
            busySum += Math.Max(0, all - idle);
            totalSum += Math.Max(0, all);
        }

        total = totalSum > 0 ? Math.Clamp(busySum * 100.0 / totalSum, 0, 100) : 0;
        _previous = current;
        return true;
    }
}
