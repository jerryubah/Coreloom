using System.Globalization;
using Coreloom.Core.Formatting;
using Coreloom.Core.Telemetry;

namespace Coreloom.Core.Health;

/// <summary>Overall state of a part of the PC.</summary>
public enum HealthStatus
{
    /// <summary>Not enough readings yet.</summary>
    Checking,

    /// <summary>Everything checked is fine.</summary>
    Excellent,

    /// <summary>Something to keep an eye on, or something that couldn't be checked.</summary>
    Good,

    /// <summary>A real problem: running too hot, slowing down to protect itself, a fan that stopped…</summary>
    Attention,
}

public enum CheckState
{
    Ok,

    /// <summary>Worth knowing, not a problem yet.</summary>
    Notice,

    /// <summary>Needs attention.</summary>
    Problem,

    /// <summary>Couldn't be checked (no reading).</summary>
    Unknown,
}

/// <summary>One check: e.g. "Temperature normal · 54 °C average".</summary>
public sealed record HealthCheck(string Id, CheckState State, string Title, string Detail);

/// <summary>The result for one part of the PC: its status, a one-line summary and the checks behind it.</summary>
public sealed record HealthReport(HealthStatus Status, string Summary, IReadOnlyList<HealthCheck> Checks)
{
    public static HealthReport Checking { get; } = new(HealthStatus.Checking, "Checking…", []);

    public static HealthReport From(IReadOnlyList<HealthCheck> checks)
    {
        if (checks.Count == 0)
        {
            return Checking;
        }

        int problems = checks.Count(c => c.State == CheckState.Problem);
        int notices = checks.Count(c => c.State == CheckState.Notice);
        int unknown = checks.Count(c => c.State == CheckState.Unknown);
        if (problems > 0)
        {
            return new HealthReport(HealthStatus.Attention, problems == 1 ? "1 problem found" : $"{problems} problems found", checks);
        }

        if (notices > 0)
        {
            return new HealthReport(HealthStatus.Good, notices == 1 ? "1 thing to keep an eye on" : $"{notices} things to keep an eye on", checks);
        }

        return unknown > 0
            ? new HealthReport(HealthStatus.Good, "No issues found · some readings aren't available", checks)
            : new HealthReport(HealthStatus.Excellent, "No issues found", checks);
    }

    /// <summary>The worst of several reports (for the whole PC).</summary>
    public static HealthStatus Worst(IEnumerable<HealthReport> reports)
    {
        var statuses = reports.Select(r => r.Status).ToList();
        if (statuses.Count == 0 || statuses.All(s => s == HealthStatus.Checking))
        {
            return HealthStatus.Checking;
        }

        return statuses.Contains(HealthStatus.Attention) ? HealthStatus.Attention
            : statuses.Contains(HealthStatus.Good) ? HealthStatus.Good
            : HealthStatus.Excellent;
    }
}

/// <summary>
/// Watches the readings that tell whether the graphics card, the processor, memory and drives are healthy, over the
/// last minute (so a brief spike doesn't change anything), and turns them into reports. Fed one frame at a time.
/// </summary>
public sealed class HealthMonitor
{
    /// <summary>Readings kept per input: a minute at the default 1 s polling.</summary>
    public const int Window = 60;

    /// <summary>Reports need this many frames first.</summary>
    public const int MinimumSamples = 3;

    // NVML clock event reasons.
    private const ulong ThermalLimit = 0x20;
    private const ulong HardwareSlowdowns = 0x8 | 0x40 | 0x80;

    private readonly Dictionary<string, Series> _series = new(StringComparer.Ordinal);
    private int _frames;
    private string? _fullestVolume;

    /// <summary>Frames seen (capped at the window).</summary>
    public int Samples => _frames;

    /// <summary>
    /// A volume's name for its key ("DISK3S5" → "Data"), where volumes have names rather than drive letters (a Mac); null
    /// keeps "C:".
    /// </summary>
    public Func<string, string?>? VolumeName { get; set; }

    /// <summary>Records the readings of one frame. <paramref name="cpuFanRpm"/> is the processor fan, when known.</summary>
    public void Record(TelemetryFrame frame, int gpuSlot, double? cpuFanRpm = null)
    {
        _frames = Math.Min(_frames + 1, Window);
        Add("gpu.temp", frame.GetValue(MetricIds.GpuTemperature(gpuSlot)));
        Add("gpu.hotspot", frame.GetValue(MetricIds.GpuHotSpotTemperature(gpuSlot)));
        Add("gpu.memjunction", frame.GetValue(MetricIds.GpuMemoryJunctionTemperature(gpuSlot)));
        Add("gpu.slowdown", frame.GetValue(MetricIds.GpuSlowdownTemperature(gpuSlot)));
        Add("gpu.reasons", frame.GetValue(MetricIds.GpuClockEventReasons(gpuSlot)));
        Add("gpu.fan", frame.GetValue(MetricIds.GpuFanRpm(gpuSlot, 1)));
        Add("cpu.temp", frame.GetValue(MetricIds.CpuTemperature));
        Add("cpu.fan", cpuFanRpm);
        Add("mem.load", frame.GetValue(MetricIds.MemoryLoad));
        Add("mem.pressure", frame.GetValue(MetricIds.MemoryPressure));
        Add("mem.commit", frame.TryGetValue(MetricIds.MemoryCommitUsed, out double committed)
            && frame.TryGetValue(MetricIds.MemoryCommitLimit, out double limit) && limit > 0 ? committed * 100 / limit : null);

        double? fullest = null;
        double? lifeUsed = null;
        double? spare = null;
        double? diskTemp = null;
        foreach (var (id, sample) in frame.Samples)
        {
            if (id.StartsWith("vol/", StringComparison.Ordinal) && id.EndsWith("/total", StringComparison.Ordinal) && sample.Value > 0)
            {
                string letter = id[4..^6];
                if (frame.TryGetValue(MetricIds.VolumeFree(letter), out double free))
                {
                    double load = Math.Clamp((sample.Value - free) * 100 / sample.Value, 0, 100);
                    if (fullest is null || load > fullest)
                    {
                        fullest = load;
                        _fullestVolume = letter.ToUpperInvariant();
                    }
                }
            }
            else if (id.StartsWith("disk/", StringComparison.Ordinal))
            {
                if (id.EndsWith("/health/used", StringComparison.Ordinal))
                {
                    lifeUsed = Math.Max(lifeUsed ?? double.MinValue, sample.Value);
                }
                else if (id.EndsWith("/health/spare", StringComparison.Ordinal))
                {
                    spare = Math.Min(spare ?? double.MaxValue, sample.Value);
                }
                else if (id.EndsWith("/temp", StringComparison.Ordinal))
                {
                    diskTemp = Math.Max(diskTemp ?? double.MinValue, sample.Value);
                }
            }
        }

        Add("vol.fullest", fullest);
        Add("disk.life", lifeUsed);
        Add("disk.spare", spare);
        Add("disk.temp", diskTemp);
    }

    public void Clear()
    {
        _series.Clear();
        _frames = 0;
    }

    /// <summary>
    /// The graphics card: temperature against <paramref name="thresholds"/> (and the driver's slowdown temperature),
    /// slowing down from heat or power problems, the hot spot and memory temperatures, and its fans.
    /// </summary>
    public HealthReport EvaluateGpu(UnitFormatter f, Thresholds thresholds)
    {
        if (_frames < MinimumSamples)
        {
            return HealthReport.Checking;
        }

        var checks = new List<HealthCheck>();
        double? temp = Average("gpu.temp");
        if (temp is double t)
        {
            double? slowdown = Latest("gpu.slowdown");
            bool nearSlowdown = slowdown is > 0 && t >= slowdown - 2;
            checks.Add(t >= thresholds.Critical || nearSlowdown
                ? new HealthCheck("gpu-temp", CheckState.Problem, "Running hot", $"{f.Temperature(t)} on average over the last minute")
                : t >= thresholds.Elevated
                    ? new HealthCheck("gpu-temp", CheckState.Notice, "Running warm", $"{f.Temperature(t)} on average over the last minute")
                    : new HealthCheck("gpu-temp", CheckState.Ok, "Temperature normal", $"{f.Temperature(t)} on average"));
        }
        else
        {
            checks.Add(new HealthCheck("gpu-temp", CheckState.Unknown, "Temperature not available", "The driver doesn't report it"));
        }

        if (Get("gpu.reasons") is { Count: > 0 } reasons)
        {
            double hardware = reasons.Fraction(v => ((ulong)v & HardwareSlowdowns) != 0);
            double thermal = reasons.Fraction(v => ((ulong)v & ThermalLimit) != 0);
            if (hardware >= 0.05)
            {
                checks.Add(new HealthCheck("gpu-slowdown", CheckState.Problem, "Hardware slowdown",
                    "The card slowed itself down to protect against heat or a power problem"));
            }
            else if (thermal >= 0.75)
            {
                checks.Add(new HealthCheck("gpu-slowdown", CheckState.Problem, "Slowing down from heat", $"{Percent(thermal)} of the last minute"));
            }
            else if (thermal >= 0.25 || hardware > 0)
            {
                checks.Add(new HealthCheck("gpu-slowdown", CheckState.Notice, "Slowing down from heat at times",
                    hardware > 0 ? "A brief hardware slowdown in the last minute" : $"{Percent(thermal)} of the last minute"));
            }
            else
            {
                checks.Add(new HealthCheck("gpu-slowdown", CheckState.Ok, "No thermal slowdown", "Clocks aren't held back by heat"));
            }
        }

        if (Average("gpu.hotspot") is double hotSpot && temp is double core)
        {
            double gap = hotSpot - core;
            string detail = $"{f.Temperature(hotSpot)}, {Delta(f, gap)} above the core";
            checks.Add(hotSpot >= 105 || gap >= 35
                ? new HealthCheck("gpu-hotspot", CheckState.Problem, "Hot spot very hot", detail)
                : hotSpot >= 95 || gap >= 25
                    ? new HealthCheck("gpu-hotspot", CheckState.Notice, "Hot spot running hot", detail)
                    : new HealthCheck("gpu-hotspot", CheckState.Ok, "Hot spot normal", detail));
        }

        if (Average("gpu.memjunction") is double memory)
        {
            checks.Add(memory >= 105
                ? new HealthCheck("gpu-memory", CheckState.Problem, "Memory running hot", $"{f.Temperature(memory)} on average")
                : memory >= 95
                    ? new HealthCheck("gpu-memory", CheckState.Notice, "Memory warm", $"{f.Temperature(memory)} on average")
                    : new HealthCheck("gpu-memory", CheckState.Ok, "Memory temperature normal", $"{f.Temperature(memory)} on average"));
        }

        if (Get("gpu.fan") is { Count: > 0 } fan)
        {
            checks.Add(FanCheck("gpu-fan", fan, temp, 70, "Fans", "the card"));
        }

        return HealthReport.From(checks);
    }

    /// <summary>The processor: temperature against <paramref name="thresholds"/>, and its fan when it can be told apart.</summary>
    public HealthReport EvaluateCpu(UnitFormatter f, Thresholds thresholds, string? temperatureUnavailableReason = null)
    {
        if (_frames < MinimumSamples)
        {
            return HealthReport.Checking;
        }

        var checks = new List<HealthCheck>();
        double? temp = Average("cpu.temp");
        if (temp is double t)
        {
            double peak = Get("cpu.temp")!.Max;
            checks.Add(t >= thresholds.Critical
                ? new HealthCheck("cpu-temp", CheckState.Problem, "Running hot", $"{f.Temperature(t)} on average over the last minute")
                : t >= thresholds.Elevated
                    ? new HealthCheck("cpu-temp", CheckState.Notice, "Running warm", $"{f.Temperature(t)} on average over the last minute")
                    : peak >= thresholds.Critical
                        ? new HealthCheck("cpu-temp", CheckState.Notice, "Brief temperature spikes", $"Up to {f.Temperature(peak)}")
                        : new HealthCheck("cpu-temp", CheckState.Ok, "Temperature normal", $"{f.Temperature(t)} on average"));
        }
        else
        {
            checks.Add(new HealthCheck("cpu-temp", CheckState.Unknown, "Temperature not available", temperatureUnavailableReason ?? "Not reported on this PC"));
        }

        if (Get("cpu.fan") is { Count: > 0 } fan)
        {
            checks.Add(FanCheck("cpu-fan", fan, temp, 60, "CPU fan", "the processor"));
        }

        return HealthReport.From(checks);
    }

    /// <summary>
    /// Memory: how much is in use and how close the commit charge (RAM plus page file) is to its limit, and, when the
    /// modules are known, the speed they run at against their rated speed (for information).
    /// </summary>
    public HealthReport EvaluateMemory(UnitFormatter f, int ratedMtps = 0, int configuredMtps = 0)
    {
        if (_frames < MinimumSamples)
        {
            return HealthReport.Checking;
        }

        var checks = new List<HealthCheck>();

        // macOS: memory pressure (the kernel's own judgement) says whether memory is short; how full it is doesn't, as macOS
        // keeps memory in use on purpose and compresses it before it swaps.
        double? pressure = Latest("mem.pressure");
        double? average = Average("mem.load");
        if (average is double used && pressure is not null)
        {
            checks.Add(new HealthCheck("memory", CheckState.Ok, "Memory in use", $"{f.Percent(used)} on average"));
        }
        else if (average is double memory)
        {
            string detail = $"{f.Percent(memory)} in use on average";
            checks.Add(memory >= 95
                ? new HealthCheck("memory", CheckState.Problem, "Memory full", detail)
                : memory >= 90
                    ? new HealthCheck("memory", CheckState.Notice, "Memory almost full", detail)
                    : new HealthCheck("memory", CheckState.Ok, "Memory use normal", detail));
        }

        if (pressure is double level)
        {
            checks.Add(level >= 4
                ? new HealthCheck("pressure", CheckState.Problem, "Memory pressure critical", "macOS is short of memory: quit apps you aren't using")
                : level >= 2
                    ? new HealthCheck("pressure", CheckState.Notice, "Memory pressure high", "macOS is compressing memory and using swap")
                    : new HealthCheck("pressure", CheckState.Ok, "Memory pressure normal", "Enough memory for what's running"));
        }

        if (Latest("mem.commit") is double commit)
        {
            string detail = $"{f.Percent(commit)} of the commit limit used";
            checks.Add(commit >= 95
                ? new HealthCheck("commit", CheckState.Problem, "Running out of virtual memory", detail)
                : commit >= 85
                    ? new HealthCheck("commit", CheckState.Notice, "Virtual memory running low", detail)
                    : new HealthCheck("commit", CheckState.Ok, "Virtual memory normal", detail));
        }

        if (configuredMtps > 0)
        {
            string speed = configuredMtps.ToString("N0", CultureInfo.CurrentCulture);
            checks.Add(ratedMtps > configuredMtps
                ? new HealthCheck("speed", CheckState.Ok, "Running below rated speed", $"{speed} of {ratedMtps.ToString("N0", CultureInfo.CurrentCulture)} MT/s (set by the board or BIOS)")
                : new HealthCheck("speed", CheckState.Ok, "Running at rated speed", $"{speed} MT/s"));
        }

        return HealthReport.From(checks);
    }

    /// <summary>Drives: free space on the fullest drive, wear (rated life used, spare blocks) and temperature.</summary>
    public HealthReport EvaluateStorage(UnitFormatter f)
    {
        if (_frames < MinimumSamples)
        {
            return HealthReport.Checking;
        }

        var checks = new List<HealthCheck>();
        if (Latest("vol.fullest") is double fullest)
        {
            string drive = _fullestVolume is not { } letter ? "A drive" : VolumeName?.Invoke(letter) is { Length: > 0 } name ? name : $"{letter}:";
            string detail = $"{drive} {f.Percent(fullest)} full";
            checks.Add(fullest >= 98
                ? new HealthCheck("space", CheckState.Problem, $"{drive} is full", detail)
                : fullest >= 92
                    ? new HealthCheck("space", CheckState.Notice, $"{drive} almost full", detail)
                    : new HealthCheck("space", CheckState.Ok, "Enough free space", $"Fullest drive: {detail}"));
        }

        double? life = Latest("disk.life");
        double? spare = Latest("disk.spare");
        if (life is not null || spare is not null)
        {
            string detail = life is double used ? $"{f.Percent(used)} of its rated life used" : $"{f.Percent(spare!.Value)} spare left";
            checks.Add(life >= 100
                ? new HealthCheck("wear", CheckState.Problem, "A drive is worn out", detail)
                : spare < 10
                    ? new HealthCheck("wear", CheckState.Problem, "A drive is running out of spare blocks", $"{f.Percent(spare.Value)} spare left")
                    : life >= 90
                        ? new HealthCheck("wear", CheckState.Notice, "A drive is near the end of its rated life", detail)
                        : new HealthCheck("wear", CheckState.Ok, "Drive wear low", life is null ? detail : $"Most worn: {detail}"));
        }

        if (Average("disk.temp") is double temperature)
        {
            string detail = $"{f.Temperature(temperature)} on average (warmest drive)";
            checks.Add(temperature >= 80
                ? new HealthCheck("drive-temp", CheckState.Problem, "A drive is too hot", detail)
                : temperature >= 70
                    ? new HealthCheck("drive-temp", CheckState.Notice, "A drive is running hot", detail)
                    : new HealthCheck("drive-temp", CheckState.Ok, "Drive temperature normal", detail));
        }

        if (checks.Count == 0)
        {
            checks.Add(new HealthCheck("space", CheckState.Unknown, "No drive readings", "Storage monitoring is off, or no drives were found"));
        }
        else if (life is null && spare is null && Average("disk.temp") is null)
        {
            checks.Add(new HealthCheck("wear", CheckState.Unknown, "Drive wear not reported", "These drives don't share their health readings"));
        }

        return HealthReport.From(checks);
    }

    private static HealthCheck FanCheck(string id, Series fan, double? temperature, double hot, string name, string part)
    {
        double rpm = fan.Average;
        if (fan.Max <= 0 && temperature >= hot)
        {
            return new HealthCheck(id, CheckState.Problem, $"{name} not spinning", $"Stopped while {part} is warm");
        }

        return fan.Max <= 0
            ? new HealthCheck(id, CheckState.Ok, $"{name} resting", "Stopped while cool (zero-RPM mode)")
            : new HealthCheck(id, CheckState.Ok, $"{name} spinning", $"{rpm.ToString("N0", CultureInfo.CurrentCulture)} RPM on average");
    }

    private static string Percent(double fraction) => $"{Math.Round(fraction * 100).ToString(CultureInfo.CurrentCulture)}%";

    private static string Delta(UnitFormatter f, double celsius) =>
        $"{Math.Round(f.ConvertTemperatureDelta(celsius)).ToString(CultureInfo.CurrentCulture)} {f.Temperature(0).Unit}";

    private void Add(string key, double? value)
    {
        if (value is not double v || !double.IsFinite(v))
        {
            return;
        }

        if (!_series.TryGetValue(key, out var series))
        {
            series = new Series();
            _series[key] = series;
        }

        series.Add(v);
    }

    private Series? Get(string key) => _series.GetValueOrDefault(key);

    private double? Average(string key) => Get(key) is { Count: > 0 } s ? s.Average : null;

    private double? Latest(string key) => Get(key) is { Count: > 0 } s ? s.Latest : null;

    /// <summary>The last <see cref="Window"/> readings of one input.</summary>
    private sealed class Series
    {
        private readonly double[] _values = new double[Window];
        private int _next;

        public int Count { get; private set; }

        public double Latest => _values[(_next + Window - 1) % Window];

        public double Average
        {
            get
            {
                double sum = 0;
                for (int i = 0; i < Count; i++)
                {
                    sum += _values[i];
                }

                return sum / Count;
            }
        }

        public double Max
        {
            get
            {
                double max = double.MinValue;
                for (int i = 0; i < Count; i++)
                {
                    max = Math.Max(max, _values[i]);
                }

                return max;
            }
        }

        public void Add(double value)
        {
            _values[_next] = value;
            _next = (_next + 1) % Window;
            Count = Math.Min(Count + 1, Window);
        }

        public double Fraction(Func<double, bool> predicate)
        {
            int hits = 0;
            for (int i = 0; i < Count; i++)
            {
                if (predicate(_values[i]))
                {
                    hits++;
                }
            }

            return Count == 0 ? 0 : hits / (double)Count;
        }
    }
}
