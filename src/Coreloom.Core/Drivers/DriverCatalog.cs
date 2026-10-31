using System.Globalization;

namespace Coreloom.Core.Drivers;

/// <summary>What a driver is for, as the Drivers page groups them (since 1.8.6).</summary>
public enum DriverCategory
{
    Graphics,
    Displays,
    Audio,
    Network,
    Bluetooth,
    Storage,
    Usb,
    Input,
    Cameras,
    Printers,
    System,
    Firmware,
    Software,
    Other,
}

/// <summary>
/// A device's driver as Windows' device tree reports it (since 1.8.6): the device, its setup class, and the driver's
/// provider, version, date and INF file.
/// </summary>
public sealed record InstalledDriver(string InstanceId, string DeviceName, string? ClassName, string? Provider, string? Version, DateTime? Date)
{
    public string? InfName { get; init; }

    public string? Manufacturer { get; init; }

    /// <summary>The device's hardware ids (most specific first), for matching Windows Update's driver offers.</summary>
    public IReadOnlyList<string> HardwareIds { get; init; } = [];

    /// <summary>Device Manager's problem code (0 when the device works).</summary>
    public int Problem { get; init; }

    public DriverCategory Category => DriverCatalog.Categorize(ClassName);
}

/// <summary>Installed drivers that look the same (several USB hubs on one driver, say), shown as one line.</summary>
public sealed record DriverGroup(string Name, DriverCategory Category, string? Provider, string? Version, DateTime? Date, int Count, bool HasProblem)
{
    public bool IsMicrosoft => DriverCatalog.IsMicrosoft(Provider);
}

/// <summary>Where an update comes from.</summary>
public enum DriverSource
{
    WindowsUpdate,
    Nvidia,
    DirectX,
}

/// <summary>
/// An update the Drivers page offers: a driver from Windows Update, a newer NVIDIA driver from NVIDIA, or Microsoft's
/// DirectX runtime for older games. <see cref="Id"/> is Windows Update's update id, "nvidia" or "directx".
/// </summary>
public sealed record DriverUpdate(string Id, string Name, DriverSource Source, DriverCategory Category)
{
    public string? Provider { get; init; }

    /// <summary>The version installed now, when it's known.</summary>
    public string? CurrentVersion { get; init; }

    public string? NewVersion { get; init; }

    public DateTime? Date { get; init; }

    public long? SizeBytes { get; init; }

    /// <summary>A size as the source writes it ("1.04 GB") when there's no byte count.</summary>
    public string? SizeText { get; init; }

    /// <summary>NVIDIA's download, or the DirectX runtime's installer.</summary>
    public string? DownloadUrl { get; init; }

    /// <summary>The release notes page.</summary>
    public string? DetailsUrl { get; init; }

    /// <summary>Installing it restarts the display for a moment (graphics drivers).</summary>
    public bool BlanksScreen => Category == DriverCategory.Graphics;
}

/// <summary>How installing one update went.</summary>
public sealed record DriverInstallResult(bool Success, string Message, bool RestartNeeded = false);

/// <summary>Grouping, labels and version rules for the Drivers page (since 1.8.6).</summary>
public static class DriverCatalog
{
    /// <summary>Setup classes that are endpoints, copies or queues of another device, not a driver of their own.</summary>
    private static readonly HashSet<string> Noise = new(StringComparer.OrdinalIgnoreCase)
    {
        "AudioEndpoint", "Volume", "VolumeSnapshot", "PrintQueue", "LegacyDriver", "WPD", "SmartCardFilter", "Infrared",
    };

    /// <summary>The group a setup class belongs to (Device Manager's class names).</summary>
    public static DriverCategory Categorize(string? className) => (className ?? string.Empty).ToUpperInvariant() switch
    {
        "DISPLAY" => DriverCategory.Graphics,
        "MONITOR" => DriverCategory.Displays,
        "MEDIA" or "AUDIOPROCESSINGOBJECT" or "SOUND" => DriverCategory.Audio,
        "NET" or "NETCLIENT" or "NETSERVICE" or "NETTRANS" or "MODEM" => DriverCategory.Network,
        "BLUETOOTH" => DriverCategory.Bluetooth,
        "DISKDRIVE" or "SCSIADAPTER" or "HDC" or "CDROM" or "STORAGEVOLUME" or "SDHOST" or "MTD" => DriverCategory.Storage,
        "USB" or "USBDEVICE" or "USBFUNCTIONCONTROLLER" => DriverCategory.Usb,
        "KEYBOARD" or "MOUSE" or "HIDCLASS" or "BIOMETRIC" or "XNACOMPOSITE" or "XBOXCOMPOSITE" => DriverCategory.Input,
        "CAMERA" or "IMAGE" => DriverCategory.Cameras,
        "PRINTER" => DriverCategory.Printers,
        "SYSTEM" or "PROCESSOR" or "COMPUTER" or "SECURITYDEVICES" or "BATTERY" or "SENSOR" or "MULTIFUNCTION" or "PCMCIA" => DriverCategory.System,
        "FIRMWARE" => DriverCategory.Firmware,
        "SOFTWARECOMPONENT" or "EXTENSION" or "SOFTWAREDEVICE" => DriverCategory.Software,
        _ => DriverCategory.Other,
    };

    /// <summary>The group's heading.</summary>
    public static string Label(DriverCategory category) => category switch
    {
        DriverCategory.Graphics => "Graphics",
        DriverCategory.Displays => "Displays",
        DriverCategory.Audio => "Audio",
        DriverCategory.Network => "Network",
        DriverCategory.Bluetooth => "Bluetooth",
        DriverCategory.Storage => "Storage",
        DriverCategory.Usb => "USB",
        DriverCategory.Input => "Keyboards, mice and controllers",
        DriverCategory.Cameras => "Cameras",
        DriverCategory.Printers => "Printers",
        DriverCategory.System => "Chipset and system",
        DriverCategory.Firmware => "Firmware",
        DriverCategory.Software => "Software components",
        _ => "Other devices",
    };

    /// <summary>The group's icon.</summary>
    public static string IconKey(DriverCategory category) => category switch
    {
        DriverCategory.Graphics => "Icon.Gpu",
        DriverCategory.Displays => "Icon.Display",
        DriverCategory.Audio => "Icon.Speaker",
        DriverCategory.Network => "Icon.Network",
        DriverCategory.Bluetooth => "Icon.Wireless",
        DriverCategory.Storage => "Icon.Storage",
        DriverCategory.Usb => "Icon.Plug",
        DriverCategory.Input => "Icon.Keyboard",
        DriverCategory.Cameras => "Icon.Camera",
        DriverCategory.Printers => "Icon.Printer",
        DriverCategory.System => "Icon.Board",
        DriverCategory.Firmware => "Icon.Shield",
        DriverCategory.Software => "Icon.Layers",
        _ => "Icon.Devices",
    };

    /// <summary>Drivers Windows itself provides (the inbox drivers), which come and go with Windows' own updates.</summary>
    public static bool IsMicrosoft(string? provider) =>
        provider is not null && (provider.Equals("Microsoft", StringComparison.OrdinalIgnoreCase)
            || provider.StartsWith("Microsoft ", StringComparison.OrdinalIgnoreCase)
            || provider.Equals("(Standard system devices)", StringComparison.OrdinalIgnoreCase)
            || provider.Equals("(Standard USB Host Controller)", StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether a device is worth listing: it has a driver, and it isn't another device's endpoint or copy.</summary>
    public static bool IsListed(InstalledDriver driver) =>
        !string.IsNullOrWhiteSpace(driver.Version) && !string.IsNullOrWhiteSpace(driver.DeviceName)
        && !(driver.ClassName is { } name && Noise.Contains(name));

    /// <summary>
    /// The drivers as the page lists them: listed devices only, alike ones (same name, group, provider and version) as one
    /// line with a count, by group and then name.
    /// </summary>
    public static IReadOnlyList<DriverGroup> Group(IEnumerable<InstalledDriver> drivers) =>
    [
        .. drivers
            .Where(IsListed)
            .GroupBy(d => (Name: d.DeviceName.Trim(), d.Category, Provider: d.Provider?.Trim(), Version: d.Version?.Trim()), new GroupKeyComparer())
            .Select(g => new DriverGroup(g.Key.Name, g.Key.Category, g.Key.Provider, g.Key.Version, g.Max(d => d.Date), g.Count(), g.Any(d => d.Problem != 0)))
            .OrderBy(g => g.Category)
            .ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase),
    ];

    /// <summary>
    /// NVIDIA's own version number from Windows' driver version: the last five digits of the third and fourth parts
    /// ("32.0.15.8157" is 581.57). Null when it isn't in that form.
    /// </summary>
    public static string? NvidiaVersion(string? windowsVersion)
    {
        if (windowsVersion is null)
        {
            return null;
        }

        var parts = windowsVersion.Trim().Split('.');
        if (parts.Length != 4 || !parts.All(p => p.Length > 0 && p.All(char.IsAsciiDigit)))
        {
            return null;
        }

        string digits = parts[2] + parts[3].PadLeft(4, '0');
        if (digits.Length < 5)
        {
            return null;
        }

        digits = digits[^5..];
        return $"{int.Parse(digits[..3], CultureInfo.InvariantCulture)}.{digits[3..]}";
    }

    /// <summary>Compares dotted version numbers part by part (missing parts are 0); text that isn't numeric compares as text.</summary>
    public static int CompareVersions(string? a, string? b)
    {
        if (a is null || b is null)
        {
            return a is null ? (b is null ? 0 : -1) : 1;
        }

        var x = a.Trim().Split('.');
        var y = b.Trim().Split('.');
        for (int i = 0; i < Math.Max(x.Length, y.Length); i++)
        {
            string p = i < x.Length ? x[i] : "0";
            string q = i < y.Length ? y[i] : "0";
            if (long.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out long m) && long.TryParse(q, NumberStyles.None, CultureInfo.InvariantCulture, out long n))
            {
                if (m != n)
                {
                    return m.CompareTo(n);
                }
            }
            else if (string.Compare(p, q, StringComparison.OrdinalIgnoreCase) is int c && c != 0)
            {
                return c;
            }
        }

        return 0;
    }

    /// <summary>
    /// An update Windows Update offers for a device: its title ("Intel Corporation - Bluetooth - 22.240.0.2") gives the
    /// provider and version; the installed driver is the device whose hardware id it names, else the one whose name it
    /// carries.
    /// </summary>
    public static DriverUpdate FromWindowsUpdate(WindowsUpdateDriver offer, IReadOnlyList<InstalledDriver> installed)
    {
        var parts = offer.Title.Split(" - ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        string? version = parts.Length >= 2 && LooksLikeVersion(parts[^1]) ? parts[^1] : null;
        string? provider = offer.Provider ?? (parts.Length >= 3 ? parts[0] : null);
        var device = Match(offer, installed);
        var category = device?.Category ?? Categorize(offer.DriverClass);
        string name = device?.DeviceName ?? offer.Model ?? (parts.Length >= 3 ? string.Join(" - ", parts[1..^1]) : offer.Title);
        return new DriverUpdate(offer.UpdateId, name, DriverSource.WindowsUpdate, category)
        {
            Provider = provider,
            CurrentVersion = device?.Version,
            NewVersion = version,
            Date = offer.Date,
            SizeBytes = offer.SizeBytes > 0 ? offer.SizeBytes : null,
        };
    }

    /// <summary>The installed device a Windows Update offer is for.</summary>
    public static InstalledDriver? Match(WindowsUpdateDriver offer, IReadOnlyList<InstalledDriver> installed)
    {
        if (!string.IsNullOrWhiteSpace(offer.HardwareId))
        {
            var byId = installed.FirstOrDefault(d => d.HardwareIds.Any(h => h.Equals(offer.HardwareId, StringComparison.OrdinalIgnoreCase)));
            if (byId is not null)
            {
                return byId;
            }
        }

        return string.IsNullOrWhiteSpace(offer.Model)
            ? null
            : installed.FirstOrDefault(d => d.DeviceName.Equals(offer.Model, StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksLikeVersion(string text) => text.Length > 0 && char.IsAsciiDigit(text[0]) && text.All(c => char.IsAsciiDigit(c) || c == '.');

    /// <summary>"3 updates" / "1 update".</summary>
    public static string Updates(int count) => $"{count.ToString(CultureInfo.CurrentCulture)} {(count == 1 ? "update" : "updates")}";

    private sealed class GroupKeyComparer : IEqualityComparer<(string Name, DriverCategory Category, string? Provider, string? Version)>
    {
        public bool Equals((string Name, DriverCategory Category, string? Provider, string? Version) x, (string Name, DriverCategory Category, string? Provider, string? Version) y) =>
            x.Category == y.Category && string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Provider, y.Provider, StringComparison.OrdinalIgnoreCase) && string.Equals(x.Version, y.Version, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Name, DriverCategory Category, string? Provider, string? Version) key) =>
            HashCode.Combine(key.Category, key.Name.ToUpperInvariant(), key.Provider?.ToUpperInvariant(), key.Version?.ToUpperInvariant());
    }
}

/// <summary>A driver Windows Update offers (the fields of its IUpdate and IWindowsDriverUpdate).</summary>
public sealed record WindowsUpdateDriver(string UpdateId, string Title)
{
    public string? DriverClass { get; init; }

    public string? Provider { get; init; }

    public string? Model { get; init; }

    public string? HardwareId { get; init; }

    public DateTime? Date { get; init; }

    public long SizeBytes { get; init; }
}
