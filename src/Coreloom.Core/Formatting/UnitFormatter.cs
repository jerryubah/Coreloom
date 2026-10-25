using System.Globalization;
using Coreloom.Core.Telemetry;

namespace Coreloom.Core.Formatting;

public enum TemperatureUnit
{
    Celsius,
    Fahrenheit,
}

/// <summary>Binary (1 GB = 2³⁰ bytes, the Windows convention) or decimal (1 GB = 10⁹ bytes).</summary>
public enum DataUnitStyle
{
    Binary,
    Decimal,
}

public enum NetworkUnitStyle
{
    BitsPerSecond,
    BytesPerSecond,
}

/// <summary>A formatted number and its unit, kept separate so the UI can style them differently.</summary>
public readonly record struct FormattedValue(string Number, string Unit)
{
    public static FormattedValue Unavailable { get; } = new("—", string.Empty);

    public bool IsUnavailable => Number == "—";

    public override string ToString() => string.IsNullOrEmpty(Unit) ? Number : $"{Number} {Unit}";
}

/// <summary>
/// Formats metric values for display according to the user's unit preferences.
/// Instances are immutable; create a new one when preferences change.
/// </summary>
public sealed class UnitFormatter
{
    private static readonly string[] ByteUnits = ["B", "KB", "MB", "GB", "TB", "PB"];
    private static readonly string[] BitRateUnits = ["bps", "Kbps", "Mbps", "Gbps", "Tbps"];
    private static readonly string[] ByteRateUnits = ["B/s", "KB/s", "MB/s", "GB/s", "TB/s"];

    public UnitFormatter(
        TemperatureUnit temperatureUnit = TemperatureUnit.Celsius,
        DataUnitStyle dataUnits = DataUnitStyle.Binary,
        NetworkUnitStyle networkUnits = NetworkUnitStyle.BitsPerSecond,
        CultureInfo? culture = null)
    {
        TemperatureUnit = temperatureUnit;
        DataUnits = dataUnits;
        NetworkUnits = networkUnits;
        Culture = culture ?? CultureInfo.CurrentCulture;
    }

    public static UnitFormatter Default { get; } = new();

    /// <summary>
    /// Memory is counted in binary units even when sizes are decimal: macOS shows storage in decimal units (Finder) but
    /// memory in binary ones (About This Mac, Activity Monitor), so a 64 GB Mac shows 64 GB of memory and a 2 TB disk.
    /// On by default on a Mac.
    /// </summary>
    public static bool BinaryMemoryByDefault { get; set; } = OperatingSystem.IsMacOS();

    /// <summary>Memory sizes use binary units whatever <see cref="DataUnits"/> says (see <see cref="BinaryMemoryByDefault"/>).</summary>
    public bool BinaryMemory { get; init; } = BinaryMemoryByDefault;

    public TemperatureUnit TemperatureUnit { get; }

    public DataUnitStyle DataUnits { get; }

    public NetworkUnitStyle NetworkUnits { get; }

    public CultureInfo Culture { get; }

    public string TemperatureSymbol => TemperatureUnit == TemperatureUnit.Fahrenheit ? "°F" : "°C";

    private double DataBase => DataUnits == DataUnitStyle.Binary ? 1024.0 : 1000.0;

    private double MemoryBase => BinaryMemory ? 1024.0 : DataBase;

    /// <summary>1024 or 1000: the step between byte units for sizes.</summary>
    public double ByteBase => DataBase;

    /// <summary>1024 or 1000: the step between byte units for memory sizes.</summary>
    public double MemoryByteBase => MemoryBase;

    /// <summary>Formats a value according to its metric kind (base units: °C, MHz, W, V, bytes, B/s...).</summary>
    public FormattedValue Format(MetricKind kind, double value)
    {
        if (!double.IsFinite(value))
        {
            return FormattedValue.Unavailable;
        }

        return kind switch
        {
            MetricKind.Load or MetricKind.Control or MetricKind.Level => Percent(value),
            MetricKind.Temperature => Temperature(value),
            MetricKind.Clock => Clock(value),
            MetricKind.Power => Power(value),
            MetricKind.Voltage => Voltage(value),
            MetricKind.Current => new FormattedValue(Number(value, value < 10 ? 2 : 1), "A"),
            MetricKind.Fan => Fan(value),
            MetricKind.Data => Bytes(value),
            MetricKind.Throughput => DiskRate(value),
            MetricKind.Factor => new FormattedValue(Number(value, 2), "×"),
            MetricKind.State => new FormattedValue(Number(value, 0), string.Empty),
            MetricKind.Duration => Duration(value),
            MetricKind.Frequency => new FormattedValue(Number(value, 0), "Hz"),
            _ => new FormattedValue(Number(value, 1), string.Empty),
        };
    }

    public FormattedValue Percent(double value, int decimals = 0)
    {
        if (!double.IsFinite(value))
        {
            return FormattedValue.Unavailable;
        }

        return new FormattedValue(Number(value, decimals), "%");
    }

    /// <summary>Converts Celsius to the display unit.</summary>
    public double ConvertTemperature(double celsius) =>
        TemperatureUnit == TemperatureUnit.Fahrenheit ? (celsius * 9.0 / 5.0) + 32.0 : celsius;

    /// <summary>Converts a temperature in the display unit back to Celsius.</summary>
    public double ToCelsius(double displayValue) =>
        TemperatureUnit == TemperatureUnit.Fahrenheit ? (displayValue - 32.0) * 5.0 / 9.0 : displayValue;

    /// <summary>Converts a temperature difference (e.g. hysteresis) from Celsius to display units.</summary>
    public double ConvertTemperatureDelta(double celsiusDelta) =>
        TemperatureUnit == TemperatureUnit.Fahrenheit ? celsiusDelta * 9.0 / 5.0 : celsiusDelta;

    public FormattedValue Temperature(double celsius, int decimals = 0)
    {
        if (!double.IsFinite(celsius))
        {
            return FormattedValue.Unavailable;
        }

        return new FormattedValue(Number(ConvertTemperature(celsius), decimals), TemperatureSymbol);
    }

    /// <summary>Clock in MHz, e.g. "2527 MHz".</summary>
    public FormattedValue Clock(double mhz)
    {
        if (!double.IsFinite(mhz))
        {
            return FormattedValue.Unavailable;
        }

        return new FormattedValue(Number(mhz, 0), "MHz");
    }

    /// <summary>Clock in GHz with two decimals, e.g. "5.24 GHz".</summary>
    public FormattedValue ClockGigahertz(double mhz)
    {
        if (!double.IsFinite(mhz))
        {
            return FormattedValue.Unavailable;
        }

        return new FormattedValue(Number(mhz / 1000.0, 2), "GHz");
    }

    public FormattedValue Power(double watts)
    {
        if (!double.IsFinite(watts))
        {
            return FormattedValue.Unavailable;
        }

        return new FormattedValue(Number(watts, Math.Abs(watts) < 10 ? 1 : 0), "W");
    }

    /// <summary>Voltage in volts with three decimals, e.g. "1.250 V".</summary>
    public FormattedValue Voltage(double volts)
    {
        if (!double.IsFinite(volts))
        {
            return FormattedValue.Unavailable;
        }

        return new FormattedValue(Number(volts, 3), "V");
    }

    /// <summary>Voltage in millivolts, e.g. "950 mV".</summary>
    public FormattedValue Millivolts(double volts)
    {
        if (!double.IsFinite(volts))
        {
            return FormattedValue.Unavailable;
        }

        return new FormattedValue(Number(volts * 1000.0, 0), "mV");
    }

    public FormattedValue Fan(double rpm)
    {
        if (!double.IsFinite(rpm))
        {
            return FormattedValue.Unavailable;
        }

        return new FormattedValue(Number(rpm, 0), "RPM");
    }

    /// <summary>Formats a byte count, e.g. "23.4 GB". Decimals adapt to magnitude unless specified.</summary>
    public FormattedValue Bytes(double bytes, int? decimals = null)
    {
        if (!double.IsFinite(bytes))
        {
            return FormattedValue.Unavailable;
        }

        var (scaled, unit) = Scale(bytes, DataBase, ByteUnits);
        return new FormattedValue(Number(scaled, decimals ?? AdaptiveDecimals(scaled)), unit);
    }

    /// <summary>Formats bytes using a fixed unit index (0 = B, 3 = GB) so related values line up.</summary>
    public FormattedValue BytesAs(double bytes, int unitIndex, int decimals = 1)
    {
        if (!double.IsFinite(bytes))
        {
            return FormattedValue.Unavailable;
        }

        unitIndex = Math.Clamp(unitIndex, 0, ByteUnits.Length - 1);
        double scaled = bytes / Math.Pow(DataBase, unitIndex);
        return new FormattedValue(Number(scaled, decimals), ByteUnits[unitIndex]);
    }

    /// <summary>The unit index <see cref="Bytes(double, int?)"/> would choose for this value.</summary>
    public int ByteUnitIndex(double bytes)
    {
        int index = 0;
        double value = Math.Abs(bytes);
        while (value >= DataBase && index < ByteUnits.Length - 1)
        {
            value /= DataBase;
            index++;
        }

        return index;
    }

    /// <summary>A memory size: like <see cref="Bytes"/>, but in binary units when <see cref="BinaryMemory"/> is on.</summary>
    public FormattedValue MemoryBytes(double bytes, int? decimals = null)
    {
        if (!double.IsFinite(bytes))
        {
            return FormattedValue.Unavailable;
        }

        var (scaled, unit) = Scale(bytes, MemoryBase, ByteUnits);
        return new FormattedValue(Number(scaled, decimals ?? AdaptiveDecimals(scaled)), unit);
    }

    /// <summary>A memory size in a fixed unit (see <see cref="BytesAs"/>).</summary>
    public FormattedValue MemoryBytesAs(double bytes, int unitIndex, int decimals = 1)
    {
        if (!double.IsFinite(bytes))
        {
            return FormattedValue.Unavailable;
        }

        unitIndex = Math.Clamp(unitIndex, 0, ByteUnits.Length - 1);
        return new FormattedValue(Number(bytes / Math.Pow(MemoryBase, unitIndex), decimals), ByteUnits[unitIndex]);
    }

    /// <summary>The unit index <see cref="MemoryBytes"/> would choose for this value.</summary>
    public int MemoryUnitIndex(double bytes)
    {
        int index = 0;
        double value = Math.Abs(bytes);
        while (value >= MemoryBase && index < ByteUnits.Length - 1)
        {
            value /= MemoryBase;
            index++;
        }

        return index;
    }

    /// <summary>Network rate in the preferred style (bits or bytes per second). Input is bytes/s.</summary>
    public FormattedValue NetworkRate(double bytesPerSecond)
    {
        if (!double.IsFinite(bytesPerSecond))
        {
            return FormattedValue.Unavailable;
        }

        if (NetworkUnits == NetworkUnitStyle.BitsPerSecond)
        {
            // Network bit rates are always decimal (1 Mbps = 10⁶ bit/s).
            var (scaled, unit) = Scale(bytesPerSecond * 8.0, 1000.0, BitRateUnits);
            return new FormattedValue(Number(scaled, AdaptiveDecimals(scaled)), unit);
        }

        return DiskRate(bytesPerSecond);
    }

    /// <summary>Byte rate (always bytes per second), e.g. "512 MB/s".</summary>
    public FormattedValue DiskRate(double bytesPerSecond)
    {
        if (!double.IsFinite(bytesPerSecond))
        {
            return FormattedValue.Unavailable;
        }

        var (scaled, unit) = Scale(bytesPerSecond, DataBase, ByteRateUnits);
        return new FormattedValue(Number(scaled, AdaptiveDecimals(scaled)), unit);
    }

    public FormattedValue Duration(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0)
        {
            return FormattedValue.Unavailable;
        }

        var span = TimeSpan.FromSeconds(seconds);
        if (span.TotalDays >= 1)
        {
            return new FormattedValue($"{(int)span.TotalDays}d {span.Hours}h", string.Empty);
        }

        if (span.TotalHours >= 1)
        {
            return new FormattedValue($"{(int)span.TotalHours}h {span.Minutes}m", string.Empty);
        }

        return new FormattedValue($"{span.Minutes}m {span.Seconds}s", string.Empty);
    }

    public static string PerformanceState(double value) =>
        double.IsFinite(value) && value >= 0 && value <= 15 ? $"P{(int)value}" : "—";

    public string Number(double value, int decimals)
    {
        if (!double.IsFinite(value))
        {
            return "—";
        }

        decimals = Math.Clamp(decimals, 0, 6);
        double rounded = Math.Round(value, decimals, MidpointRounding.AwayFromZero);
        if (rounded == 0)
        {
            rounded = 0; // avoid "-0"
        }

        return rounded.ToString("N" + decimals.ToString(CultureInfo.InvariantCulture), Culture);
    }

    internal static int AdaptiveDecimals(double scaled)
    {
        double a = Math.Abs(scaled);
        if (a == 0) return 0;
        if (a < 10) return 2;
        if (a < 100) return 1;
        return 0;
    }

    private static (double Value, string Unit) Scale(double value, double factor, string[] units)
    {
        int index = 0;
        double scaled = value;
        while (Math.Abs(scaled) >= factor && index < units.Length - 1)
        {
            scaled /= factor;
            index++;
        }

        // Avoid displaying "1,024 KB" because of rounding: promote when rounding reaches the factor.
        if (index < units.Length - 1 && Math.Round(Math.Abs(scaled), AdaptiveDecimals(scaled)) >= factor)
        {
            scaled /= factor;
            index++;
        }

        return (scaled, units[index]);
    }
}
