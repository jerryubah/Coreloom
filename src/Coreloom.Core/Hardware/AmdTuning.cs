using System.Globalization;

namespace Coreloom.Core.Hardware;

/// <summary>What package power over the last hour says about the power limit in effect.</summary>
public enum PowerLimitVerdict
{
    /// <summary>No stock limit to compare with (laptops, handhelds, unknown models).</summary>
    NotApplicable,

    /// <summary>No package power readings.</summary>
    NoReadings,

    /// <summary>The processor hasn't been loaded hard enough to reach the limit.</summary>
    NotEnoughLoad,

    /// <summary>Power held near the stock limit under load: stock limits (PBO off).</summary>
    HeldAtStock,

    /// <summary>Power went clearly above the stock limit several times: PBO or a raised limit.</summary>
    AboveStock,
}

public readonly record struct PowerLimitAssessment(PowerLimitVerdict Verdict, double PeakWatts, int? StockWatts);

/// <summary>How a tuning control is shown: Coreloom only reads, so none of them can be changed here.</summary>
public enum TuningState
{
    /// <summary>A value Coreloom knows (looked up or inferred), shown read-only.</summary>
    ReadOnly,

    /// <summary>Can't be read or changed from Coreloom; the explanation says why and where to change it.</summary>
    Unavailable,

    /// <summary>Inferred as active (e.g. PBO likely on).</summary>
    Detected,

    /// <summary>Inferred as not active.</summary>
    NotDetected,

    /// <summary>Not enough information yet.</summary>
    Unknown,
}

/// <summary>
/// A processor control on the Processor page: its name, a status pill, a one-line summary shown on the card, and the full
/// explanation (shown on hover).
/// </summary>
public sealed record TuningControl(string Name, string Status, TuningState State, string Summary, string Explanation)
{
    /// <summary>Always false: Coreloom doesn't change processor tuning.</summary>
    public bool CanChange => false;
}

/// <summary>
/// Reads what can be read about an AMD processor's power limits and tuning, and explains the rest. Read-only by design:
/// limits, Precision Boost Overdrive and Curve Optimizer live in the processor's SMU, and writing to it without
/// per-model validation can crash or destabilise the PC, so Coreloom shows each control as unavailable and says where
/// it can be changed instead.
/// </summary>
public static class AmdTuning
{
    /// <summary>Package power above stock PPT × this counts as above the limit.</summary>
    public const double AboveFactor = 1.08;

    /// <summary>Readings above the limit needed before PBO counts as likely on (one spike isn't enough).</summary>
    public const int AboveReadings = 3;

    /// <summary>Readings near the limit needed before the processor counts as held at stock.</summary>
    public const int NearReadings = 5;

    /// <summary>
    /// Compares package power readings with the stock PPT. <paramref name="fromPptSensor"/> means the readings are the
    /// SMU's own PPT figure; the RAPL "Package" figure reads somewhat below PPT, so "near the limit" starts lower for it.
    /// </summary>
    public static PowerLimitAssessment Assess(ReadOnlySpan<double> watts, AmdProcessorProfile profile, bool fromPptSensor = false)
    {
        int? stock = profile.StockPptWatts;
        double peak = 0;
        int above = 0, near = 0, count = 0;
        double nearFactor = fromPptSensor ? 0.92 : 0.8;
        foreach (double w in watts)
        {
            if (!double.IsFinite(w) || w <= 0)
            {
                continue;
            }

            count++;
            peak = Math.Max(peak, w);
            if (stock is int s)
            {
                if (w > s * AboveFactor)
                {
                    above++;
                }
                else if (w >= s * nearFactor)
                {
                    near++;
                }
            }
        }

        if (stock is null || profile.LimitsSetByMaker)
        {
            return new PowerLimitAssessment(PowerLimitVerdict.NotApplicable, peak, stock);
        }

        var verdict = count == 0 ? PowerLimitVerdict.NoReadings
            : above >= AboveReadings ? PowerLimitVerdict.AboveStock
            : near + above >= NearReadings ? PowerLimitVerdict.HeldAtStock
            : PowerLimitVerdict.NotEnoughLoad;
        return new PowerLimitAssessment(verdict, peak, stock);
    }

    /// <summary>The controls shown for an AMD processor, each read-only or unavailable with its reason.</summary>
    public static IReadOnlyList<TuningControl> Controls(AmdProcessorProfile profile, PowerLimitAssessment power, bool sensorsAccessible)
    {
        string Watts(double w) => w.ToString("0", CultureInfo.InvariantCulture) + " W";
        bool laptop = profile.LimitsSetByMaker;
        bool desktop = profile.Segment is ProcessorSegment.Desktop or ProcessorSegment.DesktopWithGraphics;
        const string Limits = "Power limits";
        const string Pbo = "Precision Boost Overdrive";
        var controls = new List<TuningControl>();

        // Power limits (PPT, TDC, EDC).
        if (laptop)
        {
            controls.Add(new(Limits, "Set by the maker", TuningState.Unavailable, "Set in the laptop's firmware",
                "PPT, TDC and EDC: the laptop's maker sets the sustained and boost power limits in firmware and changes them with its "
                + "power modes. Windows doesn't report them, so Coreloom shows what the processor actually draws (package power) instead."));
        }
        else if (profile.StockPptWatts is int stock && profile.TdpWatts is int tdp)
        {
            controls.Add(new(Limits, $"Stock PPT {stock} W", TuningState.ReadOnly, $"AMD's default for a {tdp} W TDP",
                $"AMD's stock socket power limit (PPT) for a {tdp} W processor is 1.35 × TDP. The limits in effect are held by the processor's "
                + "SMU, which Windows doesn't report, so this is the stock value, not a reading. TDC and EDC limits differ per model and aren't shown."));
        }
        else
        {
            controls.Add(new(Limits, "Not reported", TuningState.Unavailable, "Held by the SMU, not reported",
                "PPT, TDC and EDC: the limits in effect are held by the processor's SMU, which Windows doesn't report, and Coreloom doesn't "
                + "know this model's stock values to show instead."));
        }

        // Precision Boost Overdrive.
        controls.Add(power.Verdict switch
        {
            PowerLimitVerdict.AboveStock => new(Pbo, "Likely on", TuningState.Detected,
                $"Reached {Watts(power.PeakWatts)}, over the {power.StockWatts} W stock limit",
                $"Package power reached {Watts(power.PeakWatts)} in the last hour, above the stock {power.StockWatts} W limit more than once. "
                + "PBO, a higher TDP mode or a motherboard power option has probably raised the limits. Windows can't read the setting itself."),
            PowerLimitVerdict.HeldAtStock => new(Pbo, "Looks off", TuningState.NotDetected,
                $"Held near the {power.StockWatts} W stock limit under load",
                $"Under heavy load package power held near the stock {power.StockWatts} W limit (peak {Watts(power.PeakWatts)}), which is what stock limits do. "
                + "Windows can't read the setting itself, so this is an inference from power."),
            PowerLimitVerdict.NotEnoughLoad => new(Pbo, "Not known yet", TuningState.Unknown,
                $"Peak {Watts(power.PeakWatts)} of {power.StockWatts} W · needs a heavy load",
                $"Package power has peaked at {Watts(power.PeakWatts)} of the stock {power.StockWatts} W limit in the last hour. Put it under a heavy "
                + "all-core load (a render, a compile or a demanding game) for a minute and look again."),
            PowerLimitVerdict.NoReadings => new(Pbo, "Not known", TuningState.Unknown,
                sensorsAccessible ? "Package power isn't reported" : "Needs administrator rights and PawnIO",
                sensorsAccessible
                    ? "Package power isn't reported for this processor, so Coreloom can't compare it with the stock limit."
                    : "Coreloom needs package power to tell, which needs administrator rights and the PawnIO driver."),
            _ when laptop => new(Pbo, "Not offered", TuningState.Unavailable, "A desktop feature",
                "PBO is a desktop feature. Laptop processors boost within the limits the maker set."),
            _ => new(Pbo, "Not known", TuningState.Unknown, "Stock limit unknown for this model",
                "Coreloom doesn't know this model's stock power limit, so it can't tell whether power goes above it."),
        });

        // Curve Optimizer.
        bool zen3OrLater = profile.Architecture is "Zen 3" or "Zen 3+" or "Zen 4" or "Zen 4c" or "Zen 5";
        controls.Add(laptop
            ? new("Curve Optimizer", "Not offered", TuningState.Unavailable, "Not offered on laptops",
                "AMD's tools don't offer per-core voltage offsets on laptop processors.")
            : !zen3OrLater && profile.Architecture is not null
                ? new("Curve Optimizer", "Not on this processor", TuningState.Unavailable, "Arrived with Ryzen 5000 (Zen 3)",
                    "Curve Optimizer arrived with Ryzen 5000 (Zen 3); this processor's architecture doesn't have it.")
                : new("Curve Optimizer", "Unavailable", TuningState.Unavailable, "Change it in the BIOS or Ryzen Master",
                    "Per-core voltage offsets live in the SMU. An offset that's too low can crash the PC at light load, which looks like a hardware fault, "
                    + "so Coreloom doesn't read or set them. Use the BIOS (PBO › Curve Optimizer) or AMD Ryzen Master."));

        // Eco mode.
        controls.Add(laptop
            ? new("Eco mode", "Use power modes", TuningState.Unavailable, "Use Windows' power mode",
                "Use Windows' power mode or the laptop maker's app to trade speed for battery life and fan noise.")
            : new("Eco mode", "Unavailable", TuningState.Unavailable, desktop ? "Change it in the BIOS or Ryzen Master" : "Change it in the BIOS",
                desktop
                    ? "Lowers the power limits to a smaller TDP class (for example 105 W → 65 W) for cooler, quieter running. Set it in the BIOS or AMD Ryzen Master."
                    : "Set power limits in the BIOS or AMD's tools for this platform."));

        // Manual clocks and voltages.
        bool locked = profile.HasVCache && profile.Architecture is not "Zen 5";
        string manual = "Coreloom never changes clocks, multipliers or voltages. A fixed all-core overclock turns off Precision Boost and can make the PC unstable.";
        if (locked)
        {
            manual += " AMD also locks the multiplier on this X3D model.";
        }

        controls.Add(new("Manual tuning", "Unavailable", TuningState.Unavailable,
            locked ? "Multiplier locked on this X3D model" : "Coreloom never changes clocks or voltages", manual));
        return controls;
    }
}
