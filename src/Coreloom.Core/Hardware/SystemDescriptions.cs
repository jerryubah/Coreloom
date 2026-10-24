using System.Globalization;
using System.Text.RegularExpressions;
using Coreloom.Core.Formatting;

namespace Coreloom.Core.Hardware;

/// <summary>What kind of graphics card the PC has (the Overview picture labels only a discrete one).</summary>
public enum GpuSize
{
    /// <summary>No discrete card (integrated graphics or none detected).</summary>
    None,
    Medium,
    Large,
}

/// <summary>
/// Short, human descriptions of the machine for the Overview header: a clean board name, the memory line, the
/// model badge on the pictured graphics card and the card's class.
/// </summary>
public static partial class SystemDescriptions
{
    // Board manufacturers as SMBIOS spells them, and the name people use. Matched at the start of the board string.
    private static readonly (Regex Pattern, string Name)[] BoardVendors =
    [
        (Vendor(@"ASUSTeK(\s+COMPUTER)?(\s+INC\.?)?"), "ASUS"),
        (Vendor(@"Micro-Star\s+International(\s+Co\.?)?,?(\s+Ltd\.?)?"), "MSI"),
        (Vendor(@"Gigabyte\s+Technology(\s+Co\.?)?,?(\s+Ltd\.?)?"), "Gigabyte"),
        (Vendor(@"ASRock(\s+Incorporation)?"), "ASRock"),
        (Vendor(@"BIOSTAR(\s+Group)?"), "Biostar"),
        (Vendor(@"EVGA(\s+Corp(oration|\.)?)?"), "EVGA"),
        (Vendor(@"NZXT(\s+Inc\.?)?"), "NZXT"),
        (Vendor(@"Super\s*Micro(\s+Computer)?(,?\s+Inc\.?)?"), "Supermicro"),
        (Vendor(@"Dell(\s+Inc\.?)?"), "Dell"),
        (Vendor(@"LENOVO"), "Lenovo"),
        (Vendor(@"(HP|Hewlett-Packard)(\s+Development\s+Company(,?\s+L\.?P\.?)?)?"), "HP"),
        (Vendor(@"Intel(\s+Corporation)?"), "Intel"),
        (Vendor(@"Microsoft(\s+Corporation)?"), "Microsoft"),
        (Vendor(@"Acer(\s+Inc\.?)?"), "Acer"),
        (Vendor(@"Razer(\s+USA(\s+Ltd\.?)?)?"), "Razer"),
    ];

    // Values firmware vendors leave in unfilled SMBIOS fields.
    private static readonly string[] Placeholders =
    [
        "Default string", "To be filled by O.E.M.", "To Be Filled By O.E.M.", "System Product Name", "Base Board Product Name",
        "Not Applicable", "Not Specified", "None", "Unknown", "OEM",
    ];

    /// <summary>
    /// "ASUSTeK COMPUTER INC. ROG STRIX X870E-E GAMING WIFI" → "ASUS ROG STRIX X870E-E GAMING WIFI". Null when the
    /// firmware does not name the board.
    /// </summary>
    public static string? BoardName(string? motherboard)
    {
        if (string.IsNullOrWhiteSpace(motherboard))
        {
            return null;
        }

        string board = Collapse(motherboard);
        if (Placeholders.Any(p => board.EndsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        foreach (var (pattern, name) in BoardVendors)
        {
            var match = pattern.Match(board);
            if (match.Success)
            {
                string rest = board[match.Length..].TrimStart(' ', ',', '-');
                if (rest.Length == 0)
                {
                    return null; // only the manufacturer: not a board name
                }

                // Products sometimes repeat the brand ("ASUS ASUS PRIME B650-PLUS").
                board = rest.StartsWith(name + " ", StringComparison.OrdinalIgnoreCase) ? rest : $"{name} {rest}";
                break;
            }
        }

        // MSI appends its internal board code: "MAG X870E TOMAHAWK WIFI (MS-7E59)".
        return BoardCode().Replace(board, string.Empty);
    }

    /// <summary>"NVIDIA GeForce RTX 5090" → "RTX 5090"; "AMD Radeon RX 9070 XT" → "RX 9070 XT"; "Intel(R) Arc(TM) B580 Graphics" → "Arc B580".</summary>
    public static string GpuBadge(string? gpuName)
    {
        if (string.IsNullOrWhiteSpace(gpuName))
        {
            return "GPU";
        }

        string s = Trademarks().Replace(gpuName, " ");
        s = BadgeNoise().Replace(s, " ");
        s = Collapse(s);
        if (s.Length == 0)
        {
            return "GPU";
        }

        if (s.Length <= 12)
        {
            return s;
        }

        // Too long for the badge: keep the model number, a short prefix and as much of the suffix as fits
        // ("RTX 4070 Ti SUPER" → "RTX 4070 Ti").
        var words = s.Split(' ');
        int model = Array.FindIndex(words, w => w.Any(char.IsDigit));
        if (model < 0)
        {
            return s[..12].TrimEnd();
        }

        string badge = words[model];
        if (model > 0 && words[model - 1].Length <= 4 && words[model - 1].Length + 1 + badge.Length <= 12)
        {
            badge = $"{words[model - 1]} {badge}";
        }

        for (int i = model + 1; i < words.Length && badge.Length + 1 + words[i].Length <= 12; i++)
        {
            badge += " " + words[i];
        }

        return badge;
    }

    /// <summary>
    /// Large for the top tiers (xx80/xx90, TITAN, Radeon x800/x900 and RX 9070), medium for other discrete cards, none for
    /// integrated graphics.
    /// </summary>
    public static GpuSize GpuSizeFor(string? gpuName, bool isIntegrated)
    {
        if (isIntegrated || string.IsNullOrWhiteSpace(gpuName))
        {
            return GpuSize.None;
        }

        if (gpuName.Contains("TITAN", StringComparison.OrdinalIgnoreCase))
        {
            return GpuSize.Large;
        }

        var nvidia = NvidiaModel().Match(gpuName);
        if (nvidia.Success)
        {
            int tier = int.Parse(nvidia.Groups["tier"].Value, CultureInfo.InvariantCulture);
            return tier >= 80 ? GpuSize.Large : GpuSize.Medium;
        }

        var amd = AmdModel().Match(gpuName);
        if (amd.Success)
        {
            string digits = amd.Groups["model"].Value;
            bool large = digits[0] == '9' && digits[1] == '0'
                ? digits[2] >= '7' // RX 9070 (XT) vs RX 9060
                : digits[1] >= '8'; // RX 7900, 7800, 6900, 6800
            return large ? GpuSize.Large : GpuSize.Medium;
        }

        return GpuSize.Medium;
    }

    /// <summary>"64 GB DDR5-6000 · 2 × 32 GB" (falls back to "64 GB" when the modules are not reported).</summary>
    public static string MemoryLine(long installedBytes, IReadOnlyList<MemoryModuleInfo> modules, UnitFormatter formatter)
    {
        var populated = modules.Where(m => m.SizeBytes > 0).ToArray();
        long total = installedBytes > 0 ? installedBytes : populated.Sum(m => m.SizeBytes);
        if (total <= 0)
        {
            return "—";
        }

        string size = formatter.Bytes(total, 0).ToString();
        if (populated.Length == 0)
        {
            return size;
        }

        string? type = populated.Select(m => m.MemoryType)
            .Where(t => !string.IsNullOrWhiteSpace(t) && !t.Equals("Unknown", StringComparison.OrdinalIgnoreCase) && !t.Equals("Other", StringComparison.OrdinalIgnoreCase))
            .GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault();
        int speed = populated.Select(m => m.ConfiguredSpeedMtps > 0 ? m.ConfiguredSpeedMtps : m.SpeedMtps).DefaultIfEmpty(0).Max();
        string kind = (type, speed) switch
        {
            (not null, > 0) => $" {type}-{speed.ToString(CultureInfo.InvariantCulture)}",
            (not null, _) => $" {type}",
            (null, > 0) => $" {speed.ToString(CultureInfo.InvariantCulture)} MT/s",
            _ => string.Empty,
        };

        string layout = populated.Select(m => m.SizeBytes).Distinct().Count() == 1
            ? $"{populated.Length.ToString(CultureInfo.InvariantCulture)} × {formatter.Bytes(populated[0].SizeBytes, 0)}"
            : $"{populated.Length.ToString(CultureInfo.InvariantCulture)} modules";
        return $"{size}{kind} · {layout}";
    }

    /// <summary>"16 cores · 32 threads" (or just the thread count when the core count is unknown).</summary>
    public static string CoresLine(int physicalCores, int logicalProcessors)
    {
        string threads = $"{logicalProcessors.ToString(CultureInfo.InvariantCulture)} {(logicalProcessors == 1 ? "thread" : "threads")}";
        return physicalCores > 0
            ? $"{physicalCores.ToString(CultureInfo.InvariantCulture)} {(physicalCores == 1 ? "core" : "cores")} · {threads}"
            : threads;
    }

    /// <summary>"MacBook Pro (14-inch, 2024) (Mac16,8)" → "MacBook Pro (14-inch, 2024)": the model without its identifier.</summary>
    public static string? MacModelName(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return null;
        }

        string name = Collapse(model);
        int open = name.LastIndexOf(" (", StringComparison.Ordinal);
        if (open > 0 && name.EndsWith(')') && name[(open + 2)..^1].Contains(',', StringComparison.Ordinal))
        {
            name = name[..open];
        }

        return name;
    }

    /// <summary>"Windows 11 Pro" + "24H2 (build 26100.4061)" → "Windows 11 Pro 24H2".</summary>
    public static string WindowsLine(string operatingSystem, string? osVersion)
    {
        if (string.IsNullOrWhiteSpace(osVersion) || osVersion.StartsWith("Build", StringComparison.OrdinalIgnoreCase))
        {
            return operatingSystem;
        }

        int paren = osVersion.IndexOf(" (", StringComparison.Ordinal);
        string release = (paren > 0 ? osVersion[..paren] : osVersion).Trim();
        return release.Length == 0 ? operatingSystem : $"{operatingSystem} {release}";
    }

    private static Regex Vendor(string pattern) =>
        new(@"^" + pattern + @"(?=\s|,|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string Collapse(string value) => string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    [GeneratedRegex(@"\s*\(MS-[0-9A-Z]+\)$", RegexOptions.IgnoreCase)]
    private static partial Regex BoardCode();

    [GeneratedRegex(@"\((R|TM|C)\)|®|™", RegexOptions.IgnoreCase)]
    private static partial Regex Trademarks();

    [GeneratedRegex(@"\b(NVIDIA|GeForce|AMD|ATI|Radeon(?!\s+(Pro|VII)\b)|Intel|Graphics|Laptop|GPU|Series|Founders\s+Edition)\b", RegexOptions.IgnoreCase)]
    private static partial Regex BadgeNoise();

    [GeneratedRegex(@"\b(RTX|GTX)\s*(PRO\s*)?\d{1,2}(?<tier>\d0)\b", RegexOptions.IgnoreCase)]
    private static partial Regex NvidiaModel();

    [GeneratedRegex(@"\bRX\s*(?<model>\d{4})\b", RegexOptions.IgnoreCase)]
    private static partial Regex AmdModel();
}
