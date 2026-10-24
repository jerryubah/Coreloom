using System.Text.RegularExpressions;

namespace Coreloom.Core.Hardware;

/// <summary>Silicon vendors Coreloom can identify (used to pick the brand mark next to a CPU or GPU name).</summary>
public enum HardwareVendor
{
    Unknown = 0,
    Nvidia,
    Amd,
    Intel,

    /// <summary>Apple silicon (M-series processors and their graphics).</summary>
    Apple,
}

/// <summary>
/// Identifies the vendor of a processor or graphics card from the vendor string a provider reported
/// ("NVIDIA", "AuthenticAMD", "GenuineIntel"...) and, when that is missing, from the device name.
/// </summary>
public static partial class HardwareVendors
{
    /// <summary>The vendor string wins; the device name is only consulted when the string says nothing.</summary>
    public static HardwareVendor Detect(string? vendor, string? deviceName)
    {
        var fromVendor = FromVendorString(vendor);
        return fromVendor != HardwareVendor.Unknown ? fromVendor : FromDeviceName(deviceName);
    }

    public static HardwareVendor FromVendorString(string? vendor)
    {
        if (string.IsNullOrWhiteSpace(vendor))
        {
            return HardwareVendor.Unknown;
        }

        if (VendorNvidia().IsMatch(vendor))
        {
            return HardwareVendor.Nvidia;
        }

        if (VendorAmd().IsMatch(vendor))
        {
            return HardwareVendor.Amd;
        }

        if (VendorIntel().IsMatch(vendor))
        {
            return HardwareVendor.Intel;
        }

        return VendorApple().IsMatch(vendor) ? HardwareVendor.Apple : HardwareVendor.Unknown;
    }

    public static HardwareVendor FromDeviceName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return HardwareVendor.Unknown;
        }

        // Intel first: "Intel(R) Arc(TM) A770" and "Intel Core Ultra" must not match the AMD/NVIDIA product words.
        if (NameIntel().IsMatch(name))
        {
            return HardwareVendor.Intel;
        }

        if (NameNvidia().IsMatch(name))
        {
            return HardwareVendor.Nvidia;
        }

        if (NameAmd().IsMatch(name))
        {
            return HardwareVendor.Amd;
        }

        return NameApple().IsMatch(name) ? HardwareVendor.Apple : HardwareVendor.Unknown;
    }

    /// <summary>File-name key for the brand mark: "nvidia", "amd", "intel" (empty when unknown).</summary>
    public static string Key(HardwareVendor vendor) => vendor switch
    {
        HardwareVendor.Nvidia => "nvidia",
        HardwareVendor.Amd => "amd",
        HardwareVendor.Intel => "intel",
        HardwareVendor.Apple => "apple",
        _ => string.Empty,
    };

    /// <summary>Plain-text vendor name, used by the text badge shown when no logo file is installed.</summary>
    public static string DisplayName(HardwareVendor vendor) => vendor switch
    {
        HardwareVendor.Nvidia => "NVIDIA",
        HardwareVendor.Amd => "AMD",
        HardwareVendor.Intel => "Intel",
        HardwareVendor.Apple => "Apple",
        _ => string.Empty,
    };

    /// <summary>Inverse of <see cref="Key"/>; unknown keys give <see cref="HardwareVendor.Unknown"/>.</summary>
    public static HardwareVendor FromKey(string? key) => key?.Trim().ToLowerInvariant() switch
    {
        "nvidia" => HardwareVendor.Nvidia,
        "amd" => HardwareVendor.Amd,
        "intel" => HardwareVendor.Intel,
        "apple" => HardwareVendor.Apple,
        _ => HardwareVendor.Unknown,
    };

    [GeneratedRegex(@"nvidia", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VendorNvidia();

    [GeneratedRegex(@"\bamd\b|authenticamd|advanced micro devices|\bati\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VendorAmd();

    [GeneratedRegex(@"intel", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VendorIntel();

    [GeneratedRegex(@"^apple\b|apple inc", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VendorApple();

    [GeneratedRegex(@"\bapple\s+m\d|\bapple\s+(?:silicon|paravirtual)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NameApple();

    [GeneratedRegex(@"\bintel\b|\bxeon\b|\bpentium\b|\bceleron\b|\barc\s*(?:\(tm\)\s*)?(?:graphics|[ab]\d{3})|\buhd graphics\b|\biris\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NameIntel();

    [GeneratedRegex(@"\bnvidia\b|\bgeforce\b|\bquadro\b|\btitan\b|\btesla\b|\b(?:rtx|gtx)\s*(?:\d|a\d|pro)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NameNvidia();

    [GeneratedRegex(@"\bamd\b|\bradeon\b|\bryzen\b|\bepyc\b|\bthreadripper\b|\bathlon\b|\binstinct\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NameAmd();
}
