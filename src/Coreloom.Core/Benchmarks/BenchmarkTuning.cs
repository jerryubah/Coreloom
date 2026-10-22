using System.Globalization;
using System.Text.RegularExpressions;
using Coreloom.Core.Formatting;
using Coreloom.Core.Hardware;
using Coreloom.Core.Telemetry;

namespace Coreloom.Core.Benchmarks;

/// <summary>Reading names a benchmark records (the group is "Processor" or "Graphics card").</summary>
public static class BenchmarkReadings
{
    public const string Processor = "Processor";
    public const string Graphics = "Graphics card";

    public const string Clock = "Clock";
    public const string FastestCore = "Fastest core";
    public const string MemoryClock = "Memory clock";
    public const string Temperature = "Temperature";
    public const string HotSpot = "Hot spot";
    public const string Power = "Power";
    public const string PowerLimit = "Power limit";
    public const string Voltage = "Voltage";
    public const string Load = "Utilization";
    public const string Performance = "Performance";
    public const string PerformanceLimit = "Performance limit";
    public const string Fan = "Fan";

    /// <summary>
    /// Records a frame's processor readings and returns its package power for judging power limits: the SMU's PPT figure
    /// when there is one, otherwise package power (NaN when there's neither).
    /// </summary>
    public static (double Watts, bool FromPpt) RecordProcessor(ReadingRecorder recorder, TelemetryFrame frame)
    {
        void Add(string name, MetricKind kind, double value) => recorder.Add(Processor, name, kind, value);

        if (frame.TryGetValue(MetricIds.CpuClockEffectiveAverage, out double clock) || frame.TryGetValue(MetricIds.CpuClockAverage, out clock))
        {
            Add(Clock, MetricKind.Clock, clock);
        }

        double fastest = double.NaN;
        foreach (var (id, sample) in frame.Samples)
        {
            if (id.StartsWith("cpu/core/", StringComparison.Ordinal) && id.EndsWith("/clock", StringComparison.Ordinal) && double.IsFinite(sample.Value))
            {
                fastest = double.IsNaN(fastest) ? sample.Value : Math.Max(fastest, sample.Value);
            }
        }

        Add(FastestCore, MetricKind.Clock, fastest);
        Add(Temperature, MetricKind.Temperature, frame.GetValue(MetricIds.CpuTemperature) ?? double.NaN);
        Add(Power, MetricKind.Power, frame.GetValue(MetricIds.CpuPower) ?? double.NaN);
        Add(Voltage, MetricKind.Voltage, frame.GetValue(MetricIds.CpuVoltage) ?? double.NaN);
        Add(Load, MetricKind.Load, frame.GetValue(MetricIds.CpuLoad) ?? double.NaN);
        Add(Performance, MetricKind.Level, frame.GetValue(MetricIds.CpuPerformance) ?? double.NaN);
        Add(PerformanceLimit, MetricKind.Level, frame.GetValue(MetricIds.CpuPerformanceLimit) ?? double.NaN);
        return frame.GetValue(MetricIds.CpuPpt) is double ppt ? (ppt, true) : (frame.GetValue(MetricIds.CpuPower) ?? double.NaN, false);
    }

    /// <summary>Records a frame's readings for the graphics card in <paramref name="slot"/> (and returns its clock-limit reasons).</summary>
    public static ulong RecordGraphics(ReadingRecorder recorder, TelemetryFrame frame, int slot)
    {
        void Add(string name, MetricKind kind, string id)
        {
            if (frame.TryGetValue(id, out double value))
            {
                recorder.Add(Graphics, name, kind, value);
            }
        }

        Add(Clock, MetricKind.Clock, MetricIds.GpuCoreClock(slot));
        Add(MemoryClock, MetricKind.Clock, MetricIds.GpuMemoryClock(slot));
        Add(Temperature, MetricKind.Temperature, MetricIds.GpuTemperature(slot));
        Add(HotSpot, MetricKind.Temperature, MetricIds.GpuHotSpotTemperature(slot));
        Add(Power, MetricKind.Power, MetricIds.GpuPower(slot));
        Add(PowerLimit, MetricKind.Power, MetricIds.GpuPowerLimit(slot));
        Add(Voltage, MetricKind.Voltage, MetricIds.GpuVoltage(slot));
        Add(Load, MetricKind.Load, MetricIds.GpuLoad(slot));
        Add(Fan, MetricKind.Control, MetricIds.GpuFanPercent(slot, 1));
        return frame.TryGetValue(MetricIds.GpuClockEventReasons(slot), out double reasons) && reasons >= 0 && double.IsFinite(reasons)
            ? (ulong)reasons
            : 0;
    }
}

/// <summary>
/// Works out, from what the sensors showed during a run, how the hardware was set up: how high it clocked and boosted,
/// its voltages, whether power limits were raised (Precision Boost Overdrive, a raised graphics power limit) and what
/// held it back. Coreloom reads these; it never changes clocks, voltages or limits.
/// </summary>
public static partial class BenchmarkTuning
{
    public static List<BenchmarkTuningNote> Processor(ReadingRecorder readings, AmdProcessorProfile? amd, IReadOnlyList<double> packageWatts, bool wattsFromPpt, UnitFormatter f)
    {
        const string G = BenchmarkReadings.Processor;
        var notes = new List<BenchmarkTuningNote>();
        double Max(string name) => readings.Max(G, name);
        double Average(string name) => readings.Average(G, name);

        if (Max(BenchmarkReadings.FastestCore) is var fastest && double.IsFinite(fastest))
        {
            notes.Add(new(G, "Fastest core clock", f.ClockGigahertz(fastest).ToString(), "The highest clock any core reached during the run."));
        }

        if (Average(BenchmarkReadings.Performance) is var performance && double.IsFinite(performance))
        {
            notes.Add(new(G, "Boost", $"{Whole(performance)} % of base clock",
                "How fast the processor ran on average compared with its base clock, as Windows measures it (over 100 % means it boosted)."));
        }

        double peakWatts = packageWatts.Where(double.IsFinite).DefaultIfEmpty(double.NaN).Max();
        if (amd is { } profile && !profile.LimitsSetByMaker && profile.StockPptWatts is int stock)
        {
            var assessment = AmdTuning.Assess(packageWatts.ToArray(), profile, wattsFromPpt);
            string peak = double.IsFinite(peakWatts) ? f.Power(peakWatts).ToString() : "—";
            notes.Add(assessment.Verdict switch
            {
                PowerLimitVerdict.AboveStock => new(G, "Precision Boost Overdrive", "Likely on",
                    $"Power reached {peak}, above AMD's {stock} W stock limit (PPT): Precision Boost Overdrive or a raised power limit is on.", Raised: true),
                PowerLimitVerdict.HeldAtStock => new(G, "Precision Boost Overdrive", "Off (stock limits)",
                    $"Power held near AMD's {stock} W stock limit (PPT) under full load, as it does with stock settings."),
                PowerLimitVerdict.NoReadings => new(G, "Precision Boost Overdrive", "Unknown",
                    "There were no package power readings to compare with the stock limit."),
                _ => new(G, "Precision Boost Overdrive", "Not reached",
                    $"Power peaked at {peak}, below AMD's {stock} W stock limit, so it couldn't tell (temperature or current limits may have held it back first)."),
            });
        }

        if (double.IsFinite(peakWatts))
        {
            string detail = amd?.StockPptWatts is int ppt && !amd.LimitsSetByMaker
                ? $"Average {f.Power(Average(BenchmarkReadings.Power))}. AMD's stock limit for this processor is {ppt} W."
                : $"Average {f.Power(Average(BenchmarkReadings.Power))} for the whole processor.";
            notes.Add(new(G, "Peak power", f.Power(peakWatts).ToString(), detail, Raised: amd?.StockPptWatts is int s && peakWatts > s * AmdTuning.AboveFactor));
        }

        if (Max(BenchmarkReadings.Voltage) is var volts && double.IsFinite(volts))
        {
            notes.Add(new(G, "Core voltage", $"{f.Voltage(volts)} peak",
                $"Average {f.Voltage(Average(BenchmarkReadings.Voltage))}. What the processor ran at (or asked for); undervolting or a voltage offset shows here as lower or higher values."));
        }

        if (readings.Has(G, BenchmarkReadings.PerformanceLimit))
        {
            double lowest = readings.Readings().First(r => r.Group == G && r.Name == BenchmarkReadings.PerformanceLimit).Min;
            notes.Add(lowest < 99
                ? new(G, "Held back", $"Down to {Whole(lowest)} %", "Windows reported the processor being limited at times (by power, temperature or current limits).")
                : new(G, "Held back", "No", "Windows didn't report the processor being limited during the run."));
        }

        if (Max(BenchmarkReadings.Temperature) is var hottest && double.IsFinite(hottest))
        {
            notes.Add(new(G, "Hottest", f.Temperature(hottest).ToString(), $"Average {f.Temperature(Average(BenchmarkReadings.Temperature))} during the run."));
        }

        return notes;
    }

    public static List<BenchmarkTuningNote> Graphics(ReadingRecorder readings, DeviceInfo? device, ulong clockReasons, UnitFormatter f)
    {
        const string G = BenchmarkReadings.Graphics;
        var notes = new List<BenchmarkTuningNote>();
        double Max(string name) => readings.Max(G, name);
        double Average(string name) => readings.Average(G, name);

        if (Max(BenchmarkReadings.Clock) is var clock && double.IsFinite(clock))
        {
            double rated = Number(device?.GetProperty("Max core clock"));
            bool above = double.IsFinite(rated) && clock > rated + 15;
            notes.Add(new(G, "Peak core clock", f.Clock(clock).ToString(),
                double.IsFinite(rated)
                    ? above ? $"Above the card's rated maximum of {f.Clock(rated)}: the core is overclocked." : $"The card's rated maximum is {f.Clock(rated)}. Average during the run: {f.Clock(Average(BenchmarkReadings.Clock))}."
                    : $"Average during the run: {f.Clock(Average(BenchmarkReadings.Clock))}.",
                Raised: above));
        }

        if (Max(BenchmarkReadings.MemoryClock) is var memory && double.IsFinite(memory))
        {
            double rated = Number(device?.GetProperty("Max memory clock"));
            bool above = double.IsFinite(rated) && memory > rated + 15;
            notes.Add(new(G, "Memory clock", f.Clock(memory).ToString(),
                double.IsFinite(rated)
                    ? above ? $"Above the card's rated {f.Clock(rated)}: the memory is overclocked." : $"The card's rated memory clock is {f.Clock(rated)}."
                    : "The graphics memory's clock during the run.",
                Raised: above));
        }

        if (Average(BenchmarkReadings.PowerLimit) is var limit && double.IsFinite(limit))
        {
            double stock = Number(device?.GetProperty("Default power limit"));
            string value = f.Power(limit).ToString();
            notes.Add(!double.IsFinite(stock) || stock <= 0
                ? new(G, "Power limit", value, "The power the card was allowed to draw.")
                : limit > stock * 1.01
                    ? new(G, "Power limit", value, $"Raised from the {f.Power(stock)} default (+{Whole((limit / stock - 1) * 100)} %).", Raised: true)
                    : limit < stock * 0.99
                        ? new(G, "Power limit", value, $"Lowered from the {f.Power(stock)} default ({Whole((limit / stock - 1) * 100)} %), which saves power and lowers performance.")
                        : new(G, "Power limit", value, "The card's default."));
        }

        if (Max(BenchmarkReadings.Power) is var power && double.IsFinite(power))
        {
            notes.Add(new(G, "Peak power", f.Power(power).ToString(), $"Average {f.Power(Average(BenchmarkReadings.Power))} during the run."));
        }

        if (Max(BenchmarkReadings.Voltage) is var volts && double.IsFinite(volts))
        {
            notes.Add(new(G, "Core voltage", $"{f.Voltage(volts)} peak",
                $"Average {f.Voltage(Average(BenchmarkReadings.Voltage))}. An undervolt (a lower voltage curve) shows here as lower values."));
        }

        var reasons = GpuClockReasons.Describe(clockReasons).Where(r => r != "Idle").ToList();
        if (readings.Has(G, BenchmarkReadings.Clock))
        {
            notes.Add(reasons.Count > 0
                ? new(G, "Held back by", string.Join(", ", reasons), "What the driver reported limiting the clocks at some point during the run.")
                : new(G, "Held back by", "Nothing reported", "The driver didn't report anything limiting the clocks (or doesn't report it for this card)."));
        }

        if (Max(BenchmarkReadings.Temperature) is var hottest && double.IsFinite(hottest))
        {
            string hotSpot = Max(BenchmarkReadings.HotSpot) is var spot && double.IsFinite(spot) ? $" Hot spot: {f.Temperature(spot)}." : string.Empty;
            notes.Add(new(G, "Hottest", f.Temperature(hottest).ToString(), $"Average {f.Temperature(Average(BenchmarkReadings.Temperature))}.{hotSpot}"));
        }

        return notes;
    }

    /// <summary>The first number in a property such as "575 W" or "3,090 MHz" (NaN when there's none).</summary>
    public static double Number(string? text)
    {
        if (text is null || FirstNumber().Match(text) is not { Success: true } match)
        {
            return double.NaN;
        }

        return double.TryParse(match.Value.Replace(",", string.Empty, StringComparison.Ordinal), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? value
            : double.NaN;
    }

    private static string Whole(double value) => Math.Round(value).ToString("0", CultureInfo.CurrentCulture);

    [GeneratedRegex(@"\d[\d,]*(\.\d+)?")]
    private static partial Regex FirstNumber();
}
