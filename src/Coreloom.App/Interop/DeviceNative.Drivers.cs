using Coreloom.Core.Drivers;

namespace Coreloom.App.Interop;

/// <summary>The Drivers page's read of the device tree (since 1.8.6): every present device's driver properties.</summary>
internal static unsafe partial class DeviceNative
{
    private const uint DevPropTypeFileTime = 0x10;
    private const uint DevPropTypeStringList = 0x2012;

    // devpkey.h: DEVPKEY_Device_Driver* share one GUID; HardwareIds and the problem code are the device's own.
    private static readonly Guid DriverKeys = new("a8b865dd-2e3d-4094-ad97-e593a70c75d6");
    private static readonly DevPropKey DriverDate = new(DriverKeys, 2);
    private static readonly DevPropKey DriverVersion = new(DriverKeys, 3);
    private static readonly DevPropKey DriverDesc = new(DriverKeys, 4);
    private static readonly DevPropKey DriverInfPath = new(DriverKeys, 5);
    private static readonly DevPropKey DriverProvider = new(DriverKeys, 9);
    private static readonly DevPropKey HardwareIds = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 3);

    /// <summary>Every present device with a driver: its name, setup class, the driver's provider, version, date and INF, and its hardware ids.</summary>
    public static List<InstalledDriver> InstalledDrivers()
    {
        var drivers = new List<InstalledDriver>();
        foreach (string id in DeviceIds(null, FilterPresent))
        {
            if (CM_Locate_DevNode(out uint node, id, 0) != CrSuccess)
            {
                continue;
            }

            string? version = StringProperty(node, DriverVersion);
            if (version is null)
            {
                continue; // no driver installed for it
            }

            string name = StringProperty(node, FriendlyName) ?? StringProperty(node, DeviceDesc) ?? StringProperty(node, DriverDesc) ?? id;
            CM_Get_DevNode_Status(out _, out uint problem, node, 0);
            drivers.Add(new InstalledDriver(id, name, StringProperty(node, ClassName), StringProperty(node, DriverProvider), version, FileTimeProperty(node, DriverDate))
            {
                InfName = StringProperty(node, DriverInfPath),
                Manufacturer = StringProperty(node, Manufacturer),
                HardwareIds = StringListProperty(node, HardwareIds),
                Problem = (int)problem,
            });
        }

        return drivers;
    }

    private static DateTime? FileTimeProperty(uint node, DevPropKey key)
    {
        var bytes = Property(node, key, out uint type);
        if (bytes is not { Length: >= 8 } || type != DevPropTypeFileTime)
        {
            return null;
        }

        long ticks = BitConverter.ToInt64(bytes, 0);
        return ticks <= 0 ? null : DateTime.FromFileTimeUtc(ticks).Date;
    }

    private static IReadOnlyList<string> StringListProperty(uint node, DevPropKey key)
    {
        var bytes = Property(node, key, out uint type);
        if (bytes is null || type != DevPropTypeStringList)
        {
            return [];
        }

        return System.Text.Encoding.Unicode.GetString(bytes).Split('\0', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
