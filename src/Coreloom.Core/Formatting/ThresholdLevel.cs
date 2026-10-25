namespace Coreloom.Core.Formatting;

/// <summary>Visual severity used to tint values and gauges.</summary>
public enum ThresholdLevel
{
    Normal,
    Elevated,
    Critical,

    /// <summary>Below the normal range: idle, cool (green on gauges).</summary>
    Cool,
}

/// <summary>
/// Warning thresholds for colouring UI elements (not safety limits). Below <see cref="Cool"/> a reading is idle or cool,
/// up to <see cref="Elevated"/> normal, then heavy, and from <see cref="Critical"/> at its limit.
/// </summary>
public readonly record struct Thresholds(double Elevated, double Critical, double Cool = double.NaN)
{
    public static Thresholds None { get; } = new(double.PositiveInfinity, double.PositiveInfinity);

    public static Thresholds Load { get; } = new(85, 97, 30);

    /// <summary>
    /// Graphics temperatures. Macs run their chips hotter by design (Apple silicon and Intel Macs alike sit near 100 °C under
    /// sustained load, and macOS manages that itself), so on a Mac the colours turn later.
    /// </summary>
    public static Thresholds GpuTemperature { get; } = OperatingSystem.IsMacOS() ? new(90, 100, 50) : new(75, 83, 50);

    public static Thresholds CpuTemperature { get; } = OperatingSystem.IsMacOS() ? new(92, 102, 55) : new(80, 90, 55);

    public static Thresholds Memory { get; } = new(80, 92, 40);

    /// <summary>How full a drive is, in percent.</summary>
    public static Thresholds DriveSpace { get; } = new(85, 95, 50);

    /// <summary>Power draw against a limit: heavy from 90 %, at the limit from 99 %, light below 30 %.</summary>
    public static Thresholds ForLimit(double limit) => new(limit * 0.9, limit * 0.99, limit * 0.3);

    public ThresholdLevel Evaluate(double value)
    {
        if (!double.IsFinite(value))
        {
            return ThresholdLevel.Normal;
        }

        if (value >= Critical)
        {
            return ThresholdLevel.Critical;
        }

        if (value >= Elevated)
        {
            return ThresholdLevel.Elevated;
        }

        return value < Cool ? ThresholdLevel.Cool : ThresholdLevel.Normal;
    }
}

/// <summary>
/// The four-color scale every gauge uses: green (idle or cool), blue (normal), orange (heavy), red (at the limit or a
/// problem). <see cref="At"/> gives a position on it from 0 (green) to 3 (red) for a gauge fraction, pure inside each
/// band with short blends at the boundaries, so a gauge glides from one color to the next as its value moves.
/// </summary>
public static class GaugeTone
{
    /// <summary>Half the width of a blend between two colors, as a fraction of the gauge.</summary>
    public const double Blend = 0.04;

    public const double Green = 0;
    public const double Blue = 1;
    public const double Orange = 2;
    public const double Red = 3;

    /// <summary>Where the bands start on a gauge (fractions), from thresholds in the gauge's units.</summary>
    public static (double Cool, double Warm, double Hot) Stops(Thresholds thresholds, double gaugeMaximum)
    {
        double max = gaugeMaximum > 0 && double.IsFinite(gaugeMaximum) ? gaugeMaximum : 100;
        double warm = double.IsFinite(thresholds.Elevated) ? thresholds.Elevated / max : 0.80;
        double hot = double.IsFinite(thresholds.Critical) ? thresholds.Critical / max : 0.95;
        double cool = double.IsFinite(thresholds.Cool) ? thresholds.Cool / max : Math.Min(0.30, warm * 0.5);
        hot = Math.Clamp(hot, 0.02, 1);
        warm = Math.Clamp(warm, 0.01, hot);
        cool = Math.Clamp(cool, 0, warm);
        return (cool, warm, hot);
    }

    /// <summary>Position on the scale (0 = green … 3 = red) for a gauge fraction.</summary>
    public static double At(double fraction, double cool, double warm, double hot)
    {
        if (!double.IsFinite(fraction))
        {
            return Blue;
        }

        double f = Math.Clamp(fraction, 0, 1);
        if (f >= hot)
        {
            return Red;
        }

        double b1 = Math.Min(Math.Min(Blend, cool), (warm - cool) / 2); // no green band when cool is 0
        double b2 = Math.Min(Blend, Math.Min(warm - cool, hot - warm) / 2);
        double b3 = Math.Min(Blend * 2, (hot - warm) / 2);
        if (f < cool - b1)
        {
            return Green;
        }

        if (f < cool + b1)
        {
            return Green + Step(f, cool - b1, cool + b1);
        }

        if (f < warm - b2)
        {
            return Blue;
        }

        if (f < warm + b2)
        {
            return Blue + Step(f, warm - b2, warm + b2);
        }

        if (f < hot - b3)
        {
            return Orange;
        }

        return Orange + Step(f, hot - b3, hot);
    }

    private static double Step(double f, double from, double to) => to > from ? Math.Clamp((f - from) / (to - from), 0, 1) : 1;
}
