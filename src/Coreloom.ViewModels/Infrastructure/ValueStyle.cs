using Coreloom.Core.Formatting;
using Coreloom.Core.Telemetry;

namespace Coreloom.ViewModels.Infrastructure;

/// <summary>How a numeric metric is presented.</summary>
public enum ValueStyle
{
    Percent,
    PercentPrecise,
    Temperature,
    ClockMegahertz,
    ClockGigahertz,
    Power,
    Volts,
    Millivolts,
    Bytes,

    /// <summary>A memory size (binary units on a Mac, see <see cref="Coreloom.Core.Formatting.UnitFormatter.BinaryMemory"/>).</summary>
    MemoryBytes,
    DiskRate,
    NetworkRate,
    Fan,
    PerformanceState,
    Count,
    Duration,
    Auto,
}

/// <summary>Semantic color keys. The view maps them to theme brushes (e.g. "Cpu" → accent blue).</summary>
public static class ColorKeys
{
    public const string Accent = "Accent";
    public const string Cpu = "Cpu";
    public const string Gpu = "Gpu";
    public const string Memory = "Memory";
    public const string Storage = "Storage";
    public const string Temperature = "Temperature";
    public const string Power = "Power";
    public const string Fan = "Fan";
    public const string NetworkDown = "NetworkDown";
    public const string NetworkUp = "NetworkUp";
    public const string Read = "Read";
    public const string Write = "Write";
    public const string Neutral = "Neutral";

    /// <summary>The four-color scale of gauges and usage donuts: green (idle, cool), blue (normal), orange (heavy), red (at the limit).</summary>
    public const string ToneCool = "Tone.Cool";
    public const string ToneNormal = "Tone.Normal";
    public const string ToneWarm = "Tone.Warm";
    public const string ToneHot = "Tone.Hot";

    /// <summary>The scale's color for a threshold level.</summary>
    public static string ForLevel(Core.Formatting.ThresholdLevel level) => level switch
    {
        Core.Formatting.ThresholdLevel.Cool => ToneCool,
        Core.Formatting.ThresholdLevel.Elevated => ToneWarm,
        Core.Formatting.ThresholdLevel.Critical => ToneHot,
        _ => ToneNormal,
    };

    /// <summary>
    /// A temperature's colors, like a thermometer: blue (cool), teal-green (normal), amber-orange (warm), red (hot). Each is
    /// drawn as a gradient on gauges and lights the card it is on.
    /// </summary>
    public const string TempCool = "Temp.Cool";
    public const string TempNormal = "Temp.Normal";
    public const string TempWarm = "Temp.Warm";
    public const string TempHot = "Temp.Hot";

    /// <summary>The temperature color for a threshold level (unknown or no thresholds: normal).</summary>
    public static string ForTemperature(Core.Formatting.ThresholdLevel level) => level switch
    {
        Core.Formatting.ThresholdLevel.Cool => TempCool,
        Core.Formatting.ThresholdLevel.Elevated => TempWarm,
        Core.Formatting.ThresholdLevel.Critical => TempHot,
        _ => TempNormal,
    };

    /// <summary>Palette used for user-composed charts (Performance History).</summary>
    public static IReadOnlyList<string> Sequence { get; } = [Cpu, Gpu, Temperature, Memory, Storage, Power, NetworkDown, Fan];
}

public static class ValueStyles
{
    public static FormattedValue Format(ValueStyle style, double value, UnitFormatter f) => style switch
    {
        ValueStyle.Percent => f.Percent(value),
        ValueStyle.PercentPrecise => f.Percent(value, 1),
        ValueStyle.Temperature => f.Temperature(value),
        ValueStyle.ClockMegahertz => f.Clock(value),
        ValueStyle.ClockGigahertz => f.ClockGigahertz(value),
        ValueStyle.Power => f.Power(value),
        ValueStyle.Volts => f.Voltage(value),
        ValueStyle.Millivolts => f.Millivolts(value),
        ValueStyle.Bytes => f.Bytes(value),
        ValueStyle.MemoryBytes => f.MemoryBytes(value),
        ValueStyle.DiskRate => f.DiskRate(value),
        ValueStyle.NetworkRate => f.NetworkRate(value),
        ValueStyle.Fan => f.Fan(value),
        ValueStyle.PerformanceState => new FormattedValue(UnitFormatter.PerformanceState(value), string.Empty),
        ValueStyle.Count => new FormattedValue(f.Number(value, 0), string.Empty),
        ValueStyle.Duration => f.Duration(value),
        _ => FormattedValue.Unavailable,
    };

    /// <summary>Default style for a metric kind.</summary>
    public static ValueStyle ForKind(MetricKind kind) => kind switch
    {
        MetricKind.Load or MetricKind.Control or MetricKind.Level => ValueStyle.Percent,
        MetricKind.Temperature => ValueStyle.Temperature,
        MetricKind.Clock => ValueStyle.ClockMegahertz,
        MetricKind.Power => ValueStyle.Power,
        MetricKind.Voltage => ValueStyle.Volts,
        MetricKind.Fan => ValueStyle.Fan,
        MetricKind.Data => ValueStyle.Bytes,
        MetricKind.Throughput => ValueStyle.DiskRate,
        MetricKind.Duration => ValueStyle.Duration,
        MetricKind.State or MetricKind.Count => ValueStyle.Count,
        _ => ValueStyle.Auto,
    };

    /// <summary>Formats with <paramref name="style"/>, or by kind when the style is <see cref="ValueStyle.Auto"/>.</summary>
    public static FormattedValue Format(ValueStyle style, MetricKind kind, double value, UnitFormatter f) =>
        style == ValueStyle.Auto ? f.Format(kind, value) : Format(style, value, f);
}
