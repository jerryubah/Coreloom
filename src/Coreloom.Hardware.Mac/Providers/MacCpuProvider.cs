using System.Globalization;
using Coreloom.Core.Formatting;
using Coreloom.Core.Telemetry;
using Coreloom.Hardware.Mac.Native;
using Coreloom.Hardware.Mac.Sensors;

namespace Coreloom.Hardware.Mac.Providers;

/// <summary>
/// The processor: per-thread load from the kernel's per-processor tick counters (host_processor_info, what Activity
/// Monitor's CPU history uses), and from the shared sensors its temperature, power and, on Apple silicon, the clock and
/// activity of each core cluster.
/// </summary>
public sealed class MacCpuProvider : ITelemetryProvider
{
    public const int Priority = 60;
    private const string Source = "macOS";
    private readonly SensorHub _sensors;
    private uint[][]? _previous;
    private MetricHandle? _total;
    private MetricHandle? _maxThread;
    private MetricHandle? _pLoad;
    private MetricHandle? _eLoad;
    private MetricHandle? _pClock;
    private MetricHandle? _eClock;
    private MetricHandle? _avgClock;
    private MetricHandle? _temperature;
    private MetricHandle? _power;
    private MetricHandle? _anePower;
    private MetricHandle[] _threads = [];
    private int _efficiencyThreads;
    private int _performanceCores;
    private int _efficiencyCores;
    private bool _acquired;

    public MacCpuProvider(SensorHub sensors) => _sensors = sensors;

    public string Name => "macOS processor counters";

    public MonitoringSection Section => MonitoringSection.Cpu;

    public int InitStage => 0;

    public TimeSpan MinimumPollInterval => TimeSpan.Zero;

    public ProviderInitResult Initialize(IProviderContext context)
    {
        Shutdown();
        string cpuName = MacSystemInfo.CpuName();
        int threads = (int)(Darwin.SysctlLong("hw.logicalcpu") ?? Environment.ProcessorCount);
        (_performanceCores, _efficiencyCores) = MacSystemInfo.CoreTypes();

        // On Apple silicon the efficiency cores are the first logical processors.
        _efficiencyThreads = MacSystemInfo.IsAppleSilicon ? (int)(Darwin.SysctlLong("hw.perflevel1.logicalcpu") ?? _efficiencyCores) : 0;
        RegisterDevice(context, cpuName, threads);

        MetricDescriptor Describe(string id, string name, MetricKind kind, string group, int order = 0) => new(id, name, MetricCategory.Cpu, kind)
        {
            DeviceId = MetricIds.CpuDeviceId,
            DeviceName = cpuName,
            Group = group,
            SortOrder = order,
        };

        _total = context.Register(Describe(MetricIds.CpuLoad, "CPU utilization", MetricKind.Load, "Load"), Provenance.Measured, Priority,
            "macOS per-processor tick counters (host_processor_info): user + system + nice time over all time, like Activity Monitor.");
        _maxThread = context.Register(Describe(MetricIds.CpuLoadMaxThread, "Busiest thread", MetricKind.Load, "Load"), Provenance.Derived, Priority,
            "Maximum of the per-logical-processor utilization values");
        _threads = new MetricHandle[threads];
        for (int i = 0; i < threads; i++)
        {
            string label = _efficiencyThreads > 0 ? (i < _efficiencyThreads ? $"E-core {i}" : $"P-core {i - _efficiencyThreads}") : $"Thread {i}";
            _threads[i] = context.Register(Describe(MetricIds.CpuThreadLoad(i), label, MetricKind.Load, "Threads", i), Provenance.Measured, Priority);
        }

        if (_efficiencyThreads > 0 && _efficiencyThreads < threads)
        {
            _pLoad = context.Register(Describe(MetricIds.CpuLoadPerformanceCores, "Performance cores", MetricKind.Load, "Load", 1), Provenance.Derived, Priority,
                "Average utilization of the performance cores' logical processors");
            _eLoad = context.Register(Describe(MetricIds.CpuLoadEfficiencyCores, "Efficiency cores", MetricKind.Load, "Load", 2), Provenance.Derived, Priority,
                "Average utilization of the efficiency cores' logical processors");
        }

        _acquired = true;
        _sensors.Acquire();
        var first = _sensors.Sample();
        if (MacSystemInfo.IsAppleSilicon)
        {
            _pClock = context.Register(Describe(MetricIds.CpuClockPerformanceCores, "Performance cores clock", MetricKind.Clock, "Clocks", 1), Provenance.Derived, Priority,
                "IOReport: time-weighted average of the performance cluster's frequency states while active (the power manager's frequency table).");
            _eClock = context.Register(Describe(MetricIds.CpuClockEfficiencyCores, "Efficiency cores clock", MetricKind.Clock, "Clocks", 2), Provenance.Derived, Priority,
                "IOReport: time-weighted average of the efficiency cluster's frequency states while active.");
            _avgClock = context.Register(Describe(MetricIds.CpuClockAverage, "Average clock", MetricKind.Clock, "Clocks"), Provenance.Derived, Priority,
                "IOReport: the clusters' active clocks averaged over their cores and the time they were active.");
            _anePower = context.Register(Describe(MetricIds.NeuralEnginePower, "Neural Engine power", MetricKind.Power, "Power", 2), Provenance.Measured, Priority,
                "IOReport energy counters (Energy Model ANE channels) over the sampling interval.");
        }
        else
        {
            const string reason = "macOS doesn't publish an Intel processor's live clock to apps (only powermetrics, run as root, can read it).";
            context.SetUnavailableReason(MetricIds.CpuClockAverage, reason);
        }

        _temperature = context.Register(Describe(MetricIds.CpuTemperature, "CPU temperature", MetricKind.Temperature, "Temperatures"), Provenance.Measured, Priority,
            MacSystemInfo.IsAppleSilicon
                ? "Average of the processor clusters' die sensors (HID temperature sensors, the ones macOS's thermal management reads)."
                : "SMC temperature key (CPU die, else CPU proximity).");
        _power = context.Register(Describe(MetricIds.CpuPower, "CPU power", MetricKind.Power, "Power"), Provenance.Measured, Priority,
            MacSystemInfo.IsAppleSilicon ? "IOReport energy counters (Energy Model CPU channels) over the sampling interval." : "SMC power key (CPU package).");

        string sensorReason = SensorHub.MissingReason;
        if (!double.IsFinite(first.CpuTemperature))
        {
            context.SetUnavailableReason(MetricIds.CpuTemperature, sensorReason);
        }

        if (MacSystemInfo.IsAppleSilicon ? !_sensors.HasEnergyCounters : !double.IsFinite(first.CpuPower))
        {
            context.SetUnavailableReason(MetricIds.CpuPower, sensorReason);
        }

        if (MacSystemInfo.IsAppleSilicon && !_sensors.HasEnergyCounters)
        {
            foreach (string id in new[] { MetricIds.CpuClockAverage, MetricIds.CpuClockPerformanceCores, MetricIds.CpuClockEfficiencyCores, MetricIds.NeuralEnginePower })
            {
                context.SetUnavailableReason(id, sensorReason);
            }
        }

        _previous = Darwin.CpuTicks();
        return ProviderInitResult.Running($"{cpuName}: {threads} threads; sensors: {_sensors.Description}");
    }

    private void RegisterDevice(IProviderContext context, string cpuName, int threads)
    {
        var f = new UnitFormatter(culture: CultureInfo.InvariantCulture);
        var props = new List<KeyValuePair<string, string>>();
        int cores = (int)(Darwin.SysctlLong("hw.physicalcpu") ?? threads);
        props.Add(new("Cores", _performanceCores > 0 ? $"{cores} ({_performanceCores}P + {_efficiencyCores}E)" : cores.ToString(CultureInfo.InvariantCulture)));
        props.Add(new("Threads", threads.ToString(CultureInfo.InvariantCulture)));
        if (Darwin.SysctlLong("hw.cpufrequency") is long hz && hz > 0)
        {
            props.Add(new("Base clock", f.ClockGigahertz(hz / 1e6).ToString()));
        }

        if (MacSystemInfo.IsAppleSilicon)
        {
            props.Add(new("Architecture", "Apple silicon (arm64)"));
            if (Darwin.SysctlLong("hw.perflevel0.l2cachesize") is long pL2 && pL2 > 0)
            {
                props.Add(new("L2 cache (P-cores)", f.Bytes(pL2, 0).ToString()));
            }

            if (Darwin.SysctlLong("hw.perflevel1.l2cachesize") is long eL2 && eL2 > 0)
            {
                props.Add(new("L2 cache (E-cores)", f.Bytes(eL2, 0).ToString()));
            }

            var (e, p, gpu) = FrequencyTables.Read();
            if (p.Count > 0)
            {
                props.Add(new("Max clock (P-cores)", f.ClockGigahertz(p.Max()).ToString()));
            }

            if (e.Count > 0)
            {
                props.Add(new("Max clock (E-cores)", f.ClockGigahertz(e.Max()).ToString()));
            }
        }
        else
        {
            props.Add(new("Architecture", "x86-64"));
            if (Darwin.SysctlLong("hw.l2cachesize") is long l2 && l2 > 0)
            {
                props.Add(new("L2 cache", f.Bytes(l2, 0).ToString()));
            }

            if (Darwin.SysctlLong("hw.l3cachesize") is long l3 && l3 > 0)
            {
                props.Add(new("L3 cache", f.Bytes(l3, 0).ToString()));
            }

            if (Darwin.SysctlString("machdep.cpu.features") is { Length: > 0 } features)
            {
                var sets = features.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Where(x => x is "AVX1.0" or "AVX2" or "AVX512F" or "SSE4.2" or "AES" or "FMA" or "SHA")
                    .Select(x => x == "AVX1.0" ? "AVX" : x)
                    .ToList();
                if (Darwin.SysctlString("machdep.cpu.leaf7_features") is { } leaf7)
                {
                    sets.AddRange(leaf7.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(x => x is "AVX2" or "AVX512F" or "SHA").Where(x => !sets.Contains(x)));
                }

                if (sets.Count > 0)
                {
                    props.Add(new("Instruction sets", string.Join(", ", sets.Distinct())));
                }
            }
        }

        context.UpsertDevice(new DeviceInfo(MetricIds.CpuDeviceId, MetricCategory.Cpu, cpuName)
        {
            Vendor = MacSystemInfo.IsAppleSilicon ? "Apple" : "Intel",
            Kind = "Processor",
            Source = Source,
            Properties = props,
        });
    }

    public void Poll(IMetricWriter writer)
    {
        var current = Darwin.CpuTicks();
        if (current is not null && _previous is not null && current.Length == _previous.Length)
        {
            var loads = CpuTickMath.Loads(_previous, current, out double total);
            if (double.IsFinite(total))
            {
                writer.Write(_total!, total);
            }

            double max = double.NaN, pSum = 0, eSum = 0;
            int pCount = 0, eCount = 0;
            for (int i = 0; i < loads.Length && i < _threads.Length; i++)
            {
                if (!double.IsFinite(loads[i]))
                {
                    continue;
                }

                writer.Write(_threads[i], loads[i]);
                max = double.IsNaN(max) ? loads[i] : Math.Max(max, loads[i]);
                if (i < _efficiencyThreads)
                {
                    eSum += loads[i];
                    eCount++;
                }
                else
                {
                    pSum += loads[i];
                    pCount++;
                }
            }

            if (double.IsFinite(max))
            {
                writer.Write(_maxThread!, max);
            }

            if (_pLoad is not null && pCount > 0)
            {
                writer.Write(_pLoad, pSum / pCount);
            }

            if (_eLoad is not null && eCount > 0)
            {
                writer.Write(_eLoad, eSum / eCount);
            }
        }

        _previous = current ?? _previous;

        var sample = _sensors.Sample();
        if (_temperature is not null)
        {
            writer.Write(_temperature, sample.CpuTemperature);
        }

        if (_power is not null)
        {
            writer.Write(_power, sample.CpuPower);
        }

        if (_anePower is not null)
        {
            writer.Write(_anePower, sample.NeuralEnginePower);
        }

        if (sample.Clusters.Count > 0)
        {
            WriteClusters(writer, sample.Clusters);
        }
    }

    private void WriteClusters(IMetricWriter writer, IReadOnlyList<ClusterReading> clusters)
    {
        double pMhz = ClusterMath.Clock(clusters, performance: true);
        double eMhz = ClusterMath.Clock(clusters, performance: false);
        if (_pClock is not null)
        {
            writer.Write(_pClock, pMhz);
        }

        if (_eClock is not null)
        {
            writer.Write(_eClock, eMhz);
        }

        if (_avgClock is not null)
        {
            writer.Write(_avgClock, ClusterMath.Average(clusters, _performanceCores, _efficiencyCores));
        }
    }

    public void Shutdown()
    {
        if (_acquired)
        {
            _acquired = false;
            _sensors.Release();
        }

        _previous = null;
        _pLoad = _eLoad = _pClock = _eClock = _avgClock = _anePower = null;
    }

    public void Dispose() => Shutdown();
}

/// <summary>Per-processor load from two readings of the kernel's tick counters (user, system, idle, nice).</summary>
internal static class CpuTickMath
{
    public static double[] Loads(uint[][] previous, uint[][] current, out double total)
    {
        var loads = new double[current.Length];
        double busyAll = 0, all = 0;
        for (int i = 0; i < current.Length; i++)
        {
            // Counters are 32-bit and wrap: unsigned subtraction gives the right difference across one wrap.
            uint user = current[i][0] - previous[i][0];
            uint system = current[i][1] - previous[i][1];
            uint idle = current[i][2] - previous[i][2];
            uint nice = current[i][3] - previous[i][3];
            double busy = (double)user + system + nice;
            double sum = busy + idle;
            loads[i] = sum > 0 ? Math.Clamp(busy * 100.0 / sum, 0, 100) : double.NaN;
            busyAll += busy;
            all += sum;
        }

        total = all > 0 ? Math.Clamp(busyAll * 100.0 / all, 0, 100) : double.NaN;
        return loads;
    }
}

/// <summary>Combining Apple silicon's cluster readings.</summary>
internal static class ClusterMath
{
    /// <summary>A cluster type's clock: the busiest cluster of that type (Pro and Max chips have two performance clusters).</summary>
    public static double Clock(IReadOnlyList<ClusterReading> clusters, bool performance)
    {
        ClusterReading? best = null;
        foreach (var cluster in clusters)
        {
            if (cluster.IsPerformance == performance && (best is null || cluster.ActiveFraction > best.ActiveFraction))
            {
                best = cluster;
            }
        }

        return best?.ClockMhz ?? double.NaN;
    }

    /// <summary>The clusters' clocks weighted by their cores and by how much of the time they were active.</summary>
    public static double Average(IReadOnlyList<ClusterReading> clusters, int performanceCores, int efficiencyCores)
    {
        int pClusters = Math.Max(1, clusters.Count(c => c.IsPerformance));
        int eClusters = Math.Max(1, clusters.Count(c => !c.IsPerformance));
        double weighted = 0, weights = 0;
        foreach (var cluster in clusters)
        {
            double cores = cluster.IsPerformance ? Math.Max(1, performanceCores) / (double)pClusters : Math.Max(1, efficiencyCores) / (double)eClusters;
            double weight = cores * Math.Max(cluster.ActiveFraction, 0.001);
            weighted += cluster.ClockMhz * weight;
            weights += weight;
        }

        return weights > 0 ? weighted / weights : double.NaN;
    }
}
