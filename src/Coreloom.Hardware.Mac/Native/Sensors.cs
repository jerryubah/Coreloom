using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace Coreloom.Hardware.Mac.Native;

/// <summary>
/// IOReport: the kernel's performance counters on Apple silicon (energy used by the CPU, GPU and memory, and how long
/// each core cluster and the GPU spent at each frequency). Read only; no privileges needed.
/// </summary>
internal static partial class IOReport
{
    public const int FormatSimple = 1;
    public const int FormatState = 2;

    [LibraryImport(Libraries.IOReport)]
    public static partial nint IOReportCopyChannelsInGroup(nint group, nint subgroup, ulong a, ulong b, ulong c);

    [LibraryImport(Libraries.IOReport)]
    public static partial void IOReportMergeChannels(nint target, nint source, nint unused);

    [LibraryImport(Libraries.IOReport)]
    public static partial nint IOReportCreateSubscription(nint unused, nint channels, out nint subscribedChannels, ulong channelId, nint unused2);

    [LibraryImport(Libraries.IOReport)]
    public static partial nint IOReportCreateSamples(nint subscription, nint subscribedChannels, nint unused);

    [LibraryImport(Libraries.IOReport)]
    public static partial nint IOReportCreateSamplesDelta(nint previous, nint current, nint unused);

    [LibraryImport(Libraries.IOReport)]
    public static partial nint IOReportChannelGetGroup(nint channel);

    [LibraryImport(Libraries.IOReport)]
    public static partial nint IOReportChannelGetSubGroup(nint channel);

    [LibraryImport(Libraries.IOReport)]
    public static partial nint IOReportChannelGetChannelName(nint channel);

    [LibraryImport(Libraries.IOReport)]
    public static partial nint IOReportChannelGetUnitLabel(nint channel);

    [LibraryImport(Libraries.IOReport)]
    public static partial int IOReportChannelGetFormat(nint channel);

    [LibraryImport(Libraries.IOReport)]
    public static partial long IOReportSimpleGetIntegerValue(nint channel, int index);

    [LibraryImport(Libraries.IOReport)]
    public static partial int IOReportStateGetCount(nint channel);

    [LibraryImport(Libraries.IOReport)]
    public static partial nint IOReportStateGetNameForIndex(nint channel, int index);

    [LibraryImport(Libraries.IOReport)]
    public static partial long IOReportStateGetResidency(nint channel, int index);

    [LibraryImport(Libraries.CoreFoundation)]
    public static partial nint CFDictionaryCreateMutableCopy(nint allocator, nint capacity, nint dictionary);
}

/// <summary>
/// The HID event system's temperature sensors (Apple silicon): the dies of the processor clusters, the GPU, the SSD and
/// the battery report here. IOHIDEventSystemClient is IOKit's own (exported) client API, used read only.
/// </summary>
internal static partial class Hid
{
    public const int EventTypeTemperature = 15;

    [LibraryImport(Libraries.IOKit)]
    public static partial nint IOHIDEventSystemClientCreate(nint allocator);

    [LibraryImport(Libraries.IOKit)]
    public static partial int IOHIDEventSystemClientSetMatching(nint client, nint matching);

    [LibraryImport(Libraries.IOKit)]
    public static partial nint IOHIDEventSystemClientCopyServices(nint client);

    [LibraryImport(Libraries.IOKit)]
    public static partial nint IOHIDServiceClientCopyProperty(nint service, nint key);

    [LibraryImport(Libraries.IOKit)]
    public static partial nint IOHIDServiceClientCopyEvent(nint service, long type, int options, long timestamp);

    [LibraryImport(Libraries.IOKit)]
    public static partial double IOHIDEventGetFloatValue(nint hidEvent, int field);

    public static int FieldBase(int type) => type << 16;
}

/// <summary>
/// The System Management Controller (AppleSMC): fan speeds, and on Intel Macs the processor's and graphics' temperature
/// and power. Only the read commands are used.
/// </summary>
internal static partial class Smc
{
    private const uint KernelIndexSmc = 2;
    private const byte ReadKey = 5;
    private const byte ReadIndex = 8;
    private const byte ReadKeyInfo = 9;

    [StructLayout(LayoutKind.Explicit, Size = 80)]
    public unsafe struct KeyData
    {
        [FieldOffset(0)]
        public uint Key;

        [FieldOffset(28)]
        public uint DataSize;

        [FieldOffset(32)]
        public uint DataType;

        [FieldOffset(36)]
        public byte DataAttributes;

        [FieldOffset(40)]
        public byte Result;

        [FieldOffset(41)]
        public byte Status;

        [FieldOffset(42)]
        public byte Data8;

        [FieldOffset(44)]
        public uint Data32;

        [FieldOffset(48)]
        public fixed byte Bytes[32];
    }

    /// <summary>An open connection to the SMC, or null when there is none (virtual machines, some Macs).</summary>
    public sealed class Connection : IDisposable
    {
        private readonly uint _connection;
        private readonly Dictionary<uint, (uint Size, uint Type)> _info = [];

        private Connection(uint connection) => _connection = connection;

        public static Connection? Open()
        {
            uint service = IOKit.IOServiceGetMatchingService(0, IOKit.IOServiceMatching("AppleSMC"));
            if (service == 0)
            {
                return null;
            }

            try
            {
                return IOKit.IOServiceOpen(service, IOKit.TaskSelf(), 0, out uint connection) == IOKit.Success && connection != 0 ? new Connection(connection) : null;
            }
            finally
            {
                IOKit.IOObjectRelease(service);
            }
        }

        public static uint Key(string key) => key.Length == 4 ? BinaryPrimitives.ReadUInt32BigEndian(Encoding.ASCII.GetBytes(key)) : 0;

        public static string KeyName(uint key)
        {
            Span<byte> bytes = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(bytes, key);
            return Encoding.ASCII.GetString(bytes);
        }

        private unsafe bool Call(ref KeyData input, out KeyData output)
        {
            KeyData result = default;
            nint size = sizeof(KeyData);
            fixed (KeyData* i = &input)
            {
                int kr = IOKit.IOConnectCallStructMethod(_connection, KernelIndexSmc, i, sizeof(KeyData), &result, &size);
                output = result;
                return kr == IOKit.Success && result.Result == 0;
            }
        }

        private bool Info(uint key, out uint size, out uint type)
        {
            if (_info.TryGetValue(key, out var cached))
            {
                (size, type) = cached;
                return size > 0;
            }

            var input = new KeyData { Key = key, Data8 = ReadKeyInfo };
            if (!Call(ref input, out var output))
            {
                _info[key] = (0, 0);
                size = type = 0;
                return false;
            }

            size = output.DataSize;
            type = output.DataType;
            _info[key] = (size, type);
            return size > 0;
        }

        /// <summary>A key's raw bytes and type ("flt ", "sp78", "ui8 " …), or null when the key doesn't exist.</summary>
        public unsafe (byte[] Bytes, string Type)? Read(string key)
        {
            uint k = Key(key);
            if (k == 0 || !Info(k, out uint size, out uint type) || size > 32)
            {
                return null;
            }

            var input = new KeyData { Key = k, DataSize = size, Data8 = ReadKey };
            if (!Call(ref input, out var output))
            {
                return null;
            }

            var bytes = new byte[size];
            for (int i = 0; i < size; i++)
            {
                bytes[i] = output.Bytes[i];
            }

            return (bytes, KeyName(type));
        }

        /// <summary>A key's value as a number (decoding the SMC's types), or null.</summary>
        public double? Number(string key) => Read(key) is { } value ? Decode(value.Bytes, value.Type) : null;

        /// <summary>How many keys the SMC has, and the key at an index (to discover temperature keys).</summary>
        public int KeyCount() => Number("#KEY") is double n ? (int)n : 0;

        public string? KeyAt(int index)
        {
            var input = new KeyData { Data8 = ReadIndex, Data32 = (uint)index };
            return Call(ref input, out var output) ? KeyName(output.Key) : null;
        }

        public void Dispose() => IOKit.IOServiceClose(_connection);
    }

    /// <summary>Decodes an SMC value: floats, signed and unsigned fixed point, and big-endian integers.</summary>
    public static double? Decode(ReadOnlySpan<byte> bytes, string type)
    {
        if (bytes.Length == 0)
        {
            return null;
        }

        switch (type)
        {
            case "flt " when bytes.Length >= 4:
                return BitConverter.ToSingle(bytes[..4]); // little-endian on both Apple silicon and Intel
            case "ui8 " or "flag":
                return bytes[0];
            case "ui16" when bytes.Length >= 2:
                return BinaryPrimitives.ReadUInt16BigEndian(bytes);
            case "ui32" when bytes.Length >= 4:
                return BinaryPrimitives.ReadUInt32BigEndian(bytes);
            case "si8 ":
                return (sbyte)bytes[0];
            case "si16" when bytes.Length >= 2:
                return BinaryPrimitives.ReadInt16BigEndian(bytes);
            case "fpe2" when bytes.Length >= 2:
                return BinaryPrimitives.ReadUInt16BigEndian(bytes) / 4.0;
        }

        // spXY / fpXY: signed or unsigned 16-bit fixed point with Y fraction bits (Y as a hex digit), big-endian.
        if (bytes.Length >= 2 && type.Length == 4 && (type.StartsWith("sp", StringComparison.Ordinal) || type.StartsWith("fp", StringComparison.Ordinal))
            && int.TryParse(type[3..4], System.Globalization.NumberStyles.HexNumber, null, out int fraction))
        {
            double scale = 1 << fraction;
            return type[0] == 's' ? BinaryPrimitives.ReadInt16BigEndian(bytes) / scale : BinaryPrimitives.ReadUInt16BigEndian(bytes) / scale;
        }

        return null;
    }
}

/// <summary>SystemConfiguration: the network interfaces' display names ("Wi-Fi") and the primary interface.</summary>
internal static partial class SystemConfiguration
{
    [LibraryImport(Libraries.SystemConfiguration)]
    public static partial nint SCNetworkInterfaceCopyAll();

    [LibraryImport(Libraries.SystemConfiguration)]
    public static partial nint SCNetworkInterfaceGetBSDName(nint networkInterface);

    [LibraryImport(Libraries.SystemConfiguration)]
    public static partial nint SCNetworkInterfaceGetLocalizedDisplayName(nint networkInterface);

    [LibraryImport(Libraries.SystemConfiguration)]
    public static partial nint SCNetworkInterfaceGetInterfaceType(nint networkInterface);

    [LibraryImport(Libraries.SystemConfiguration)]
    public static partial nint SCDynamicStoreCreate(nint allocator, nint name, nint callout, nint context);

    [LibraryImport(Libraries.SystemConfiguration)]
    public static partial nint SCDynamicStoreCopyValue(nint store, nint key);

    /// <summary>BSD name → (display name, type) for every interface macOS knows ("en0" → "Wi-Fi", "IEEE80211").</summary>
    public static Dictionary<string, (string Name, string? Type)> Interfaces()
    {
        var result = new Dictionary<string, (string, string?)>(StringComparer.Ordinal);
        nint all = SCNetworkInterfaceCopyAll();
        if (all == 0)
        {
            return result;
        }

        try
        {
            for (nint i = 0; i < CF.CFArrayGetCount(all); i++)
            {
                nint item = CF.CFArrayGetValueAtIndex(all, i);
                string? bsd = CF.ToStringValue(SCNetworkInterfaceGetBSDName(item));
                string? name = CF.ToStringValue(SCNetworkInterfaceGetLocalizedDisplayName(item));
                if (bsd is not null && name is not null)
                {
                    result[bsd] = (name, CF.ToStringValue(SCNetworkInterfaceGetInterfaceType(item)));
                }
            }
        }
        finally
        {
            CF.CFRelease(all);
        }

        return result;
    }

    /// <summary>The interface carrying the default IPv4 route ("en0"), or null.</summary>
    public static string? PrimaryInterface()
    {
        nint name = CF.String("Coreloom");
        nint store = SCDynamicStoreCreate(0, name, 0, 0);
        CF.CFRelease(name);
        if (store == 0)
        {
            return null;
        }

        try
        {
            foreach (string key in new[] { "State:/Network/Global/IPv4", "State:/Network/Global/IPv6" })
            {
                nint k = CF.String(key);
                nint value = SCDynamicStoreCopyValue(store, k);
                CF.CFRelease(k);
                if (value == 0)
                {
                    continue;
                }

                try
                {
                    if (CF.ToStringValue(CF.Get(value, "PrimaryInterface")) is { } primary)
                    {
                        return primary;
                    }
                }
                finally
                {
                    CF.CFRelease(value);
                }
            }

            return null;
        }
        finally
        {
            CF.CFRelease(store);
        }
    }
}
