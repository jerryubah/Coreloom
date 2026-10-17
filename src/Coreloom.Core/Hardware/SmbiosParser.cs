using System.Text;
using System.Text.RegularExpressions;

namespace Coreloom.Core.Hardware;

public sealed record MemoryModuleInfo(
    string Locator,
    string? BankLocator,
    long SizeBytes,
    string MemoryType,
    int SpeedMtps,
    int ConfiguredSpeedMtps,
    string? Manufacturer,
    string? PartNumber);

/// <summary>
/// Names for memory modules as shown on the Memory page: the slot the firmware reports, with the channel added when two
/// modules report the same slot (many AMD boards call a slot in each channel "DIMM 1"), and a number as a last resort.
/// </summary>
public static partial class MemoryModuleNames
{
    public static IReadOnlyList<string> Labels(IReadOnlyList<MemoryModuleInfo> modules)
    {
        var labels = modules.Select(m => string.IsNullOrWhiteSpace(m.Locator) ? "Module" : m.Locator.Trim()).ToList();
        foreach (var repeated in Repeated(labels))
        {
            for (int i = 0; i < labels.Count; i++)
            {
                if (string.Equals(labels[i], repeated, StringComparison.OrdinalIgnoreCase) && Bank(modules[i].BankLocator) is { } bank)
                {
                    labels[i] = $"{labels[i]} · {bank}";
                }
            }
        }

        foreach (var repeated in Repeated(labels))
        {
            int n = 0;
            for (int i = 0; i < labels.Count; i++)
            {
                if (string.Equals(labels[i], repeated, StringComparison.OrdinalIgnoreCase))
                {
                    labels[i] = $"{labels[i]} #{++n}";
                }
            }
        }

        return labels;
    }

    /// <summary>"Channel A" from "P0 CHANNEL A", else the bank as written.</summary>
    private static string? Bank(string? bankLocator)
    {
        if (string.IsNullOrWhiteSpace(bankLocator))
        {
            return null;
        }

        var channel = ChannelPattern().Match(bankLocator);
        return channel.Success ? $"Channel {channel.Groups[1].Value.ToUpperInvariant()}" : bankLocator.Trim();
    }

    private static List<string> Repeated(List<string> labels) =>
        [.. labels.GroupBy(l => l, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key)];

    [GeneratedRegex(@"CHANNEL\s*([A-Z0-9]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ChannelPattern();
}

public sealed record SmbiosInfo(
    string? BiosVendor,
    string? BiosVersion,
    string? BiosDate,
    string? SystemManufacturer,
    string? SystemProduct,
    string? BoardManufacturer,
    string? BoardProduct,
    IReadOnlyList<MemoryModuleInfo> MemoryModules);

/// <summary>
/// Parses the raw SMBIOS table returned by <c>GetSystemFirmwareTable('RSMB')</c> (no administrator rights
/// needed). Extracts BIOS, system, baseboard and memory-module (type 17) information.
/// </summary>
public static class SmbiosParser
{
    private const int RawHeaderLength = 8; // RawSMBIOSData: 4 bytes of version info + DWORD length

    public static SmbiosInfo Parse(ReadOnlySpan<byte> rawSmbiosData)
    {
        if (rawSmbiosData.Length < RawHeaderLength)
        {
            return Empty;
        }

        int declared = BitConverter.ToInt32(rawSmbiosData.Slice(4, 4));
        var table = rawSmbiosData[RawHeaderLength..];
        if (declared > 0 && declared < table.Length)
        {
            table = table[..declared];
        }

        return ParseTable(table);
    }

    public static SmbiosInfo ParseTable(ReadOnlySpan<byte> table)
    {
        string? biosVendor = null, biosVersion = null, biosDate = null;
        string? sysManufacturer = null, sysProduct = null, boardManufacturer = null, boardProduct = null;
        var modules = new List<MemoryModuleInfo>();

        int offset = 0;
        while (offset + 4 <= table.Length)
        {
            byte type = table[offset];
            byte length = table[offset + 1];
            if (length < 4 || offset + length > table.Length)
            {
                break;
            }

            var formatted = table.Slice(offset, length);

            // The string set follows the formatted area and ends with a double NUL.
            int stringsStart = offset + length;
            int end = stringsStart;
            while (end + 1 < table.Length && !(table[end] == 0 && table[end + 1] == 0))
            {
                end++;
            }

            var strings = ReadStrings(table[stringsStart..Math.Min(end + 1, table.Length)]);

            switch (type)
            {
                case 0: // BIOS information
                    biosVendor = GetString(strings, Byte(formatted, 0x04));
                    biosVersion = GetString(strings, Byte(formatted, 0x05));
                    biosDate = GetString(strings, Byte(formatted, 0x08));
                    break;
                case 1: // System information
                    sysManufacturer = GetString(strings, Byte(formatted, 0x04));
                    sysProduct = GetString(strings, Byte(formatted, 0x05));
                    break;
                case 2: // Baseboard
                    boardManufacturer = GetString(strings, Byte(formatted, 0x04));
                    boardProduct = GetString(strings, Byte(formatted, 0x05));
                    break;
                case 17: // Memory device
                    var module = ParseMemoryDevice(formatted, strings);
                    if (module is not null)
                    {
                        modules.Add(module);
                    }

                    break;
                case 127: // End of table
                    return new SmbiosInfo(biosVendor, biosVersion, biosDate, sysManufacturer, sysProduct, boardManufacturer, boardProduct, modules);
            }

            offset = end + 2;
        }

        return new SmbiosInfo(biosVendor, biosVersion, biosDate, sysManufacturer, sysProduct, boardManufacturer, boardProduct, modules);
    }

    public static SmbiosInfo Empty { get; } = new(null, null, null, null, null, null, null, []);

    private static MemoryModuleInfo? ParseMemoryDevice(ReadOnlySpan<byte> f, IReadOnlyList<string> strings)
    {
        if (f.Length < 0x15)
        {
            return null;
        }

        int size = Word(f, 0x0C);
        if (size == 0 || size == 0xFFFF)
        {
            return null; // empty slot or unknown size
        }

        long bytes;
        if (size == 0x7FFF && f.Length >= 0x20)
        {
            bytes = (long)(BitConverter.ToUInt32(f.Slice(0x1C, 4)) & 0x7FFFFFFF) * 1024 * 1024; // extended size in MB
        }
        else if ((size & 0x8000) != 0)
        {
            bytes = (long)(size & 0x7FFF) * 1024; // granularity KB
        }
        else
        {
            bytes = (long)size * 1024 * 1024; // granularity MB
        }

        string locator = GetString(strings, Byte(f, 0x10)) ?? "DIMM";
        string? bank = GetString(strings, Byte(f, 0x11));
        string type = MemoryTypeName(Byte(f, 0x12));
        int speed = f.Length >= 0x17 ? Word(f, 0x15) : 0;
        string? manufacturer = f.Length > 0x17 ? GetString(strings, Byte(f, 0x17)) : null;
        string? part = f.Length > 0x1A ? GetString(strings, Byte(f, 0x1A)) : null;
        int configured = f.Length >= 0x22 ? Word(f, 0x20) : 0;
        if (speed == 0xFFFF && f.Length >= 0x58)
        {
            speed = (int)BitConverter.ToUInt32(f.Slice(0x54, 4));
        }

        return new MemoryModuleInfo(locator, bank, bytes, type, speed, configured, Clean(manufacturer), Clean(part));
    }

    public static string MemoryTypeName(byte code) => code switch
    {
        0x12 => "DDR",
        0x13 => "DDR2",
        0x18 => "DDR3",
        0x1A => "DDR4",
        0x1B => "LPDDR",
        0x1C => "LPDDR2",
        0x1D => "LPDDR3",
        0x1E => "LPDDR4",
        0x22 => "DDR5",
        0x23 => "LPDDR5",
        _ => "RAM",
    };

    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string trimmed = value.Trim();
        return trimmed is "Unknown" or "Not Specified" or "Undefined" or "0000" or "00000000" ? null : trimmed;
    }

    private static byte Byte(ReadOnlySpan<byte> f, int offset) => offset < f.Length ? f[offset] : (byte)0;

    private static int Word(ReadOnlySpan<byte> f, int offset) => offset + 1 < f.Length ? f[offset] | (f[offset + 1] << 8) : 0;

    private static string? GetString(IReadOnlyList<string> strings, byte index) =>
        index == 0 || index > strings.Count ? null : strings[index - 1];

    private static List<string> ReadStrings(ReadOnlySpan<byte> area)
    {
        var list = new List<string>();
        int start = 0;
        for (int i = 0; i < area.Length; i++)
        {
            if (area[i] == 0)
            {
                if (i > start)
                {
                    list.Add(Encoding.ASCII.GetString(area[start..i]).Trim());
                }

                start = i + 1;
                if (i + 1 < area.Length && area[i + 1] == 0)
                {
                    break;
                }
            }
        }

        return list;
    }
}
