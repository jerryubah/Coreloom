using System.Globalization;
using System.Text.RegularExpressions;

namespace Coreloom.Core.Hardware;

/// <summary>Where a processor is meant to go, which decides who sets its power limits.</summary>
public enum ProcessorSegment
{
    Unknown,
    Desktop,
    DesktopWithGraphics,
    Laptop,
    Handheld,
    Workstation,
    Server,
}

/// <summary>
/// An AMD processor as Coreloom understands it: architecture and codename (from the CPUID family and model), where it
/// goes (from the name's suffix), and AMD's stock power limit for desktop parts whose TDP is known. Everything here is
/// looked up, not read from the processor, and the page says so.
/// </summary>
public sealed record AmdProcessorProfile
{
    public required string Brand { get; init; }

    /// <summary>"5800X3D", "4600H", "HX 370" — the model as AMD prints it; null when the name has none.</summary>
    public string? ModelNumber { get; init; }

    /// <summary>"Zen 2", "Zen 3+", "Zen 5".</summary>
    public string? Architecture { get; init; }

    /// <summary>AMD's codename for the die, e.g. "Renoir".</summary>
    public string? Codename { get; init; }

    public ProcessorSegment Segment { get; init; }

    /// <summary>A 3D V-Cache (X3D) model.</summary>
    public bool HasVCache { get; init; }

    /// <summary>AMD's rated TDP for desktop models Coreloom knows (watts).</summary>
    public int? TdpWatts { get; init; }

    /// <summary>AMD's stock socket power limit (PPT) for that TDP: 1.35 × TDP on AM4 and AM5.</summary>
    public int? StockPptWatts => TdpWatts is int tdp ? AmdProcessors.StockPpt(tdp) : null;

    /// <summary>Laptops and handhelds: the maker sets the limits, so there's no stock value to compare with.</summary>
    public bool LimitsSetByMaker => Segment is ProcessorSegment.Laptop or ProcessorSegment.Handheld;

    /// <summary>"Zen 2 · Renoir".</summary>
    public string? ArchitectureText => (Architecture, Codename) switch
    {
        (null, null) => null,
        ({ } a, null) => a,
        (null, { } c) => c,
        ({ } a, { } c) => $"{a} · {c}",
    };

    public string SegmentText => AmdProcessors.Describe(Segment);
}

/// <summary>Looks up AMD processors by CPUID family/model and by name. Platform-neutral and side-effect free.</summary>
public static partial class AmdProcessors
{
    // Family 17h (Zen, Zen+, Zen 2), 19h (Zen 3, Zen 3+, Zen 4) and 1Ah (Zen 5): model ranges → architecture and codename.
    private static readonly (int Family, int From, int To, string Architecture, string Codename, ProcessorSegment Segment)[] Dies =
    [
        (0x17, 0x01, 0x01, "Zen", "Summit Ridge", ProcessorSegment.Desktop),
        (0x17, 0x08, 0x08, "Zen+", "Pinnacle Ridge", ProcessorSegment.Desktop),
        (0x17, 0x11, 0x11, "Zen", "Raven Ridge", ProcessorSegment.Laptop),
        (0x17, 0x18, 0x18, "Zen+", "Picasso", ProcessorSegment.Laptop),
        (0x17, 0x20, 0x20, "Zen", "Dalí", ProcessorSegment.Laptop),
        (0x17, 0x31, 0x31, "Zen 2", "Castle Peak / Rome", ProcessorSegment.Workstation),
        (0x17, 0x60, 0x60, "Zen 2", "Renoir", ProcessorSegment.Laptop),
        (0x17, 0x68, 0x68, "Zen 2", "Lucienne", ProcessorSegment.Laptop),
        (0x17, 0x71, 0x71, "Zen 2", "Matisse", ProcessorSegment.Desktop),
        (0x17, 0x90, 0x91, "Zen 2", "Van Gogh", ProcessorSegment.Handheld),
        (0x17, 0xA0, 0xA0, "Zen 2", "Mendocino", ProcessorSegment.Laptop),
        (0x19, 0x00, 0x01, "Zen 3", "Milan", ProcessorSegment.Server),
        (0x19, 0x08, 0x08, "Zen 3", "Chagall", ProcessorSegment.Workstation),
        (0x19, 0x10, 0x11, "Zen 4", "Genoa", ProcessorSegment.Server),
        (0x19, 0x18, 0x18, "Zen 4", "Storm Peak", ProcessorSegment.Workstation),
        (0x19, 0x21, 0x21, "Zen 3", "Vermeer", ProcessorSegment.Desktop),
        (0x19, 0x40, 0x44, "Zen 3+", "Rembrandt", ProcessorSegment.Laptop),
        (0x19, 0x50, 0x50, "Zen 3", "Cezanne", ProcessorSegment.Laptop),
        (0x19, 0x61, 0x61, "Zen 4", "Raphael", ProcessorSegment.Desktop),
        (0x19, 0x74, 0x75, "Zen 4", "Phoenix", ProcessorSegment.Laptop),
        (0x19, 0x78, 0x78, "Zen 4", "Phoenix 2", ProcessorSegment.Laptop),
        (0x19, 0xA0, 0xAF, "Zen 4c", "Bergamo / Siena", ProcessorSegment.Server),
        (0x1A, 0x00, 0x1F, "Zen 5", "Turin", ProcessorSegment.Server),
        (0x1A, 0x20, 0x2F, "Zen 5", "Strix Point", ProcessorSegment.Laptop),
        (0x1A, 0x40, 0x4F, "Zen 5", "Granite Ridge", ProcessorSegment.Desktop),
        (0x1A, 0x60, 0x6F, "Zen 5", "Krackan Point", ProcessorSegment.Laptop),
        (0x1A, 0x70, 0x7F, "Zen 5", "Strix Halo", ProcessorSegment.Laptop),
    ];

    // AMD's rated TDP (watts) for desktop Ryzen models, from AMD's product pages.
    private static readonly Dictionary<string, int> DesktopTdp = new(StringComparer.OrdinalIgnoreCase)
    {
        ["1200"] = 65, ["1300X"] = 65, ["1400"] = 65, ["1500X"] = 65, ["1600"] = 65, ["1600X"] = 95, ["1700"] = 65, ["1700X"] = 95, ["1800X"] = 95,
        ["2200G"] = 65, ["2400G"] = 65, ["2600"] = 65, ["2600X"] = 95, ["2700"] = 65, ["2700X"] = 105,
        ["3100"] = 65, ["3200G"] = 65, ["3300X"] = 65, ["3400G"] = 65, ["3500X"] = 65, ["3600"] = 65, ["3600X"] = 95, ["3600XT"] = 95,
        ["3700X"] = 65, ["3800X"] = 105, ["3800XT"] = 105, ["3900X"] = 105, ["3900XT"] = 105, ["3950X"] = 105,
        ["4100"] = 65, ["4300G"] = 65, ["4500"] = 65, ["4600G"] = 65, ["4700G"] = 65, ["4350GE"] = 35, ["4600GE"] = 35, ["4700GE"] = 35,
        ["5300G"] = 65, ["5500"] = 65, ["5500GT"] = 65, ["5600"] = 65, ["5600G"] = 65, ["5600GT"] = 65, ["5600T"] = 65, ["5600X"] = 65,
        ["5600XT"] = 65, ["5600X3D"] = 105, ["5700"] = 65, ["5700G"] = 65, ["5700X"] = 65, ["5700X3D"] = 105, ["5800"] = 65,
        ["5800X"] = 105, ["5800XT"] = 105, ["5800X3D"] = 105, ["5900X"] = 105, ["5900XT"] = 105, ["5950X"] = 105,
        ["5600GE"] = 35, ["5700GE"] = 35,
        ["7500F"] = 65, ["7600"] = 65, ["7600X"] = 105, ["7600X3D"] = 65, ["7700"] = 65, ["7700X"] = 105, ["7800X3D"] = 120,
        ["7900"] = 65, ["7900X"] = 170, ["7900X3D"] = 120, ["7950X"] = 170, ["7950X3D"] = 120,
        ["8300G"] = 65, ["8400F"] = 65, ["8500G"] = 65, ["8600G"] = 65, ["8700F"] = 65, ["8700G"] = 65,
        ["9600"] = 65, ["9600X"] = 65, ["9700X"] = 65, ["9800X3D"] = 120, ["9900X"] = 120, ["9900X3D"] = 120, ["9950X"] = 170, ["9950X3D"] = 170,
    };

    /// <summary>AMD's stock PPT for a desktop TDP: 1.35 × TDP (65 W → 88 W, 105 W → 142 W, 120 W → 162 W, 170 W → 230 W).</summary>
    public static int StockPpt(int tdpWatts) => (int)Math.Round(tdpWatts * 1.35, MidpointRounding.AwayFromZero);

    /// <summary>Builds the profile from the processor's name and, when known, its CPUID family and model.</summary>
    public static AmdProcessorProfile Create(string brand, int? family = null, int? model = null)
    {
        brand = (brand ?? string.Empty).Trim();
        string? number = ModelNumber(brand);
        var die = family is int f && model is int m ? Die(f, m) : null;
        var segment = SegmentOf(brand, number, die?.Segment ?? ProcessorSegment.Unknown);
        string? architecture = die?.Architecture ?? (family is int fam ? FamilyArchitecture(fam, model ?? 0) : null);
        string? codename = die?.Codename;
        if (codename is "Raphael" && segment == ProcessorSegment.Laptop)
        {
            codename = "Dragon Range";
        }
        else if (codename is "Granite Ridge" && segment == ProcessorSegment.Laptop)
        {
            codename = "Fire Range";
        }
        else if (codename is "Castle Peak / Rome")
        {
            codename = segment == ProcessorSegment.Server ? "Rome" : "Castle Peak";
        }
        else if (codename is "Phoenix" && number is not null && number.StartsWith('8') && segment == ProcessorSegment.Laptop)
        {
            codename = "Hawk Point";
        }

        int? tdp = segment is ProcessorSegment.Desktop or ProcessorSegment.DesktopWithGraphics && number is not null && DesktopTdp.TryGetValue(number, out int t) ? t : null;
        return new AmdProcessorProfile
        {
            Brand = brand,
            ModelNumber = number,
            Architecture = architecture,
            Codename = codename,
            Segment = segment,
            HasVCache = number?.Contains("X3D", StringComparison.OrdinalIgnoreCase) == true || brand.Contains("X3D", StringComparison.OrdinalIgnoreCase),
            TdpWatts = tdp,
        };
    }

    /// <summary>"5800X3D" from "AMD Ryzen 7 5800X3D 8-Core Processor"; "HX 370" from "AMD Ryzen AI 9 HX 370 w/ Radeon 890M".</summary>
    public static string? ModelNumber(string brand)
    {
        if (RyzenAiPattern().Match(brand) is { Success: true } ai)
        {
            return ai.Groups[1].Success ? $"{ai.Groups[1].Value} {ai.Groups[2].Value}" : ai.Groups[2].Value;
        }

        if (ZPattern().Match(brand) is { Success: true } z)
        {
            return z.Groups[1].Value.Trim();
        }

        return ModelPattern().Match(brand) is { Success: true } m ? m.Groups[1].Value.ToUpperInvariant() : null;
    }

    public static string Describe(ProcessorSegment segment) => segment switch
    {
        ProcessorSegment.Desktop => "Desktop processor",
        ProcessorSegment.DesktopWithGraphics => "Desktop processor with graphics",
        ProcessorSegment.Laptop => "Laptop processor",
        ProcessorSegment.Handheld => "Handheld processor",
        ProcessorSegment.Workstation => "Workstation processor (Threadripper)",
        ProcessorSegment.Server => "Server processor (EPYC)",
        _ => "Processor",
    };

    private static (string Architecture, string Codename, ProcessorSegment Segment)? Die(int family, int model)
    {
        foreach (var die in Dies)
        {
            if (die.Family == family && model >= die.From && model <= die.To)
            {
                return (die.Architecture, die.Codename, die.Segment);
            }
        }

        return null;
    }

    private static string? FamilyArchitecture(int family, int model) => family switch
    {
        0x17 => model < 0x30 ? "Zen / Zen+" : "Zen 2",
        0x19 => model is >= 0x10 and < 0x20 or >= 0x60 ? "Zen 4" : "Zen 3",
        0x1A => "Zen 5",
        0x15 => "Bulldozer family",
        0x16 => "Jaguar family",
        _ => null,
    };

    private static ProcessorSegment SegmentOf(string brand, string? number, ProcessorSegment fromDie)
    {
        if (brand.Contains("EPYC", StringComparison.OrdinalIgnoreCase))
        {
            return ProcessorSegment.Server;
        }

        if (brand.Contains("Threadripper", StringComparison.OrdinalIgnoreCase))
        {
            return ProcessorSegment.Workstation;
        }

        if (ZPattern().IsMatch(brand) || brand.Contains("Custom APU", StringComparison.OrdinalIgnoreCase))
        {
            return ProcessorSegment.Handheld;
        }

        if (RyzenAiPattern().IsMatch(brand))
        {
            return ProcessorSegment.Laptop;
        }

        if (number is not null && SuffixPattern().Match(number) is { Success: true } suffix)
        {
            string s = suffix.Groups[1].Value.ToUpperInvariant();
            if (s is "U" or "H" or "HS" or "HX" or "C")
            {
                return ProcessorSegment.Laptop;
            }

            if (s is "G" or "GE" or "GT")
            {
                return ProcessorSegment.DesktopWithGraphics;
            }

            if (s.Length == 0 || s is "X" or "XT" or "X3D" or "F" or "T")
            {
                return fromDie == ProcessorSegment.Handheld ? fromDie : ProcessorSegment.Desktop;
            }
        }

        return fromDie;
    }

    [GeneratedRegex(@"Ryzen\s+AI\s+(?:Max\+?\s+(?:PRO\s+)?)?(?:\d\s+)?(?:PRO\s+)?(HX|H|U)?\s*(\d{3})", RegexOptions.IgnoreCase)]
    private static partial Regex RyzenAiPattern();

    [GeneratedRegex(@"Ryzen\s+(Z\d\s*(?:Extreme|Ultra)?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ZPattern();

    [GeneratedRegex(@"\b(\d{4}[A-Z0-9]*)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ModelPattern();

    [GeneratedRegex(@"^\d{4}([A-Z]*?)(?:3D)?$", RegexOptions.IgnoreCase)]
    private static partial Regex SuffixPattern();
}
