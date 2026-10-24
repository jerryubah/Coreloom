using System.Globalization;
using System.Runtime.InteropServices;
using Coreloom.Core.Diagnostics;
using Coreloom.Core.Hardware;
using Coreloom.Core.Telemetry;
using Coreloom.Hardware.Mac.Native;

namespace Coreloom.Hardware.Mac;

/// <summary>What the Mac is: model, chip, macOS version and memory. Everything comes from sysctl and the I/O registry.</summary>
public static partial class MacSystemInfo
{
    private static readonly Lazy<MacModel> ModelCache = new(ReadModel);

    [LibraryImport(Libraries.SystemConfiguration)]
    private static partial nint SCDynamicStoreCopyComputerName(nint store, nint encoding);

    [LibraryImport(Libraries.System)]
    private static partial uint geteuid();

    public static bool IsAppleSilicon => RuntimeInformation.ProcessArchitecture == Architecture.Arm64;

    /// <summary>The model ("MacBook Pro (14-inch, 2024)" on Apple silicon, "Mac mini" on Intel), its identifier and firmware.</summary>
    public static MacModel Model => ModelCache.Value;

    public static SystemCapabilities DetectCapabilities() => new()
    {
        IsElevated = SafeEuid() == 0,
        PawnIoInstalled = false,
        OsBuild = Environment.OSVersion.Version.Major,
    };

    private static uint SafeEuid()
    {
        try
        {
            return geteuid();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return uint.MaxValue;
        }
    }

    public static SystemSummary ReadSummary()
    {
        try
        {
            var model = Model;
            return new SystemSummary(
                ComputerName(),
                ReleaseName(Darwin.SysctlString("kern.osproductversion")) is { } release ? $"macOS {release}" : "macOS",
                OsVersionText(),
                model.DisplayName,
                model.Firmware,
                CpuName(),
                (int)(Darwin.SysctlLong("hw.physicalcpu") ?? Environment.ProcessorCount),
                (int)(Darwin.SysctlLong("hw.logicalcpu") ?? Environment.ProcessorCount),
                Darwin.SysctlLong("hw.memsize") ?? 0,
                []);
        }
        catch (Exception ex)
        {
            Log.Warn("SystemInfo", "Reading the system summary failed.", ex);
            return new SystemSummary(Environment.MachineName, "macOS", Environment.OSVersion.VersionString, null, null, "Processor", 0, Environment.ProcessorCount, 0, []);
        }
    }

    /// <summary>The name the user gave the Mac in Sharing settings ("Jerry's MacBook Pro").</summary>
    public static string ComputerName()
    {
        try
        {
            nint name = SCDynamicStoreCopyComputerName(0, 0);
            if (name != 0)
            {
                string? text = CF.ToStringValue(name);
                CF.CFRelease(name);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text.Trim();
                }
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
        }

        return Environment.MachineName;
    }

    public static string CpuName() =>
        Darwin.SysctlString("machdep.cpu.brand_string") is { Length: > 0 } brand ? CleanCpuName(brand) : IsAppleSilicon ? "Apple silicon" : "Intel processor";

    /// <summary>"Intel(R) Core(TM) i7-8700B CPU @ 3.20GHz" → "Intel Core i7-8700B"; Apple's names are already clean.</summary>
    internal static string CleanCpuName(string brand)
    {
        string name = brand.Replace("(R)", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("(TM)", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(" CPU", string.Empty, StringComparison.Ordinal);
        int at = name.IndexOf(" @", StringComparison.Ordinal);
        if (at > 0)
        {
            name = name[..at];
        }

        return string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>"15.1 (24B83)".</summary>
    public static string OsVersionText()
    {
        string version = Darwin.SysctlString("kern.osproductversion") ?? Environment.OSVersion.Version.ToString(2);
        string? build = Darwin.SysctlString("kern.osversion");
        return build is null ? version : $"{version} ({build})";
    }

    /// <summary>The macOS release's name ("Sequoia"), or null for a version this build doesn't know.</summary>
    public static string? ReleaseName(string? version)
    {
        if (version is null || !int.TryParse(version.Split('.')[0], NumberStyles.None, CultureInfo.InvariantCulture, out int major))
        {
            return null;
        }

        return major switch
        {
            11 => "Big Sur",
            12 => "Monterey",
            13 => "Ventura",
            14 => "Sonoma",
            15 => "Sequoia",
            26 => "Tahoe",
            _ => null,
        };
    }

    /// <summary>"macOS Sequoia 15.1".</summary>
    public static string OsDisplayName()
    {
        string? version = Darwin.SysctlString("kern.osproductversion");
        return ReleaseName(version) is { } name ? $"macOS {name} {version}" : $"macOS {version}";
    }

    private static MacModel ReadModel()
    {
        string identifier = Darwin.SysctlString("hw.model") ?? "Mac";
        string? productName = null;
        string? firmware = null;
        try
        {
            productName = RegistryText("IODeviceTree:/product", "product-name");
            firmware = RegistryText("IODeviceTree:/chosen", "system-firmware-version") ?? RegistryText("IODeviceTree:/rom", "version");
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
        }

        return new MacModel(identifier, productName ?? FamilyName(identifier), firmware);
    }

    private static string? RegistryText(string path, string key)
    {
        uint entry = IOKit.IORegistryEntryFromPath(0, path);
        if (entry == 0)
        {
            return null;
        }

        try
        {
            return IOKit.Property(entry, key) switch
            {
                string s => s.Trim(),
                byte[] bytes => System.Text.Encoding.UTF8.GetString(bytes).TrimEnd('\0').Trim(),
                _ => null,
            } is { Length: > 0 } text ? text : null;
        }
        finally
        {
            IOKit.IOObjectRelease(entry);
        }
    }

    /// <summary>"MacBookPro16,1" → "MacBook Pro"; Apple silicon identifiers ("Mac15,3") give "Mac".</summary>
    internal static string FamilyName(string identifier)
    {
        string family = new(identifier.TakeWhile(c => !char.IsDigit(c)).ToArray());
        return family switch
        {
            "MacBookPro" => "MacBook Pro",
            "MacBookAir" => "MacBook Air",
            "MacBook" => "MacBook",
            "Macmini" => "Mac mini",
            "MacPro" => "Mac Pro",
            "iMacPro" => "iMac Pro",
            "iMac" => "iMac",
            "VirtualMac" => "Virtual Mac",
            _ => "Mac",
        };
    }

    /// <summary>Performance and efficiency core counts (Apple silicon and hybrid chips), or (0, 0).</summary>
    public static (int Performance, int Efficiency) CoreTypes()
    {
        long levels = Darwin.SysctlLong("hw.nperflevels") ?? 1;
        if (levels < 2)
        {
            return (0, 0);
        }

        return ((int)(Darwin.SysctlLong("hw.perflevel0.physicalcpu") ?? 0), (int)(Darwin.SysctlLong("hw.perflevel1.physicalcpu") ?? 0));
    }
}

/// <summary>The Mac's model identifier ("Mac15,8"), its display name and its firmware version.</summary>
public sealed record MacModel(string Identifier, string Name, string? Firmware)
{
    public string DisplayName => Name == Identifier ? Name : $"{Name} ({Identifier})";
}
