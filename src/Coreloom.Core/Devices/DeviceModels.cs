using System.Globalization;

namespace Coreloom.Core.Devices;

public enum BluetoothTransport
{
    /// <summary>Bluetooth Classic (BR/EDR): headphones, speakers, older controllers and keyboards.</summary>
    Classic,

    /// <summary>Bluetooth Low Energy: most newer mice, keyboards, pens and wearables.</summary>
    LowEnergy,
}

/// <summary>A paired Bluetooth device as Windows knows it.</summary>
public sealed record BluetoothDeviceInfo(string Id, string Name, ulong Address, BluetoothTransport Transport, DeviceKind Kind, bool IsConnected)
{
    /// <summary>The physical device it belongs to; a device paired over both Classic and LE shares one.</summary>
    public Guid? ContainerId { get; init; }

    /// <summary>Battery charge in percent, when the device reports it.</summary>
    public int? Battery { get; init; }

    /// <summary>When it was last connected (Classic devices), in local time.</summary>
    public DateTime? LastConnected { get; init; }

    public bool IsAudio => DeviceKinds.IsAudio(Kind);
}

/// <summary>The PC's Bluetooth adapter.</summary>
public sealed record BluetoothRadioInfo(bool IsPresent, bool IsOn, bool CanToggle)
{
    public static BluetoothRadioInfo None { get; } = new(false, false, false);

    /// <summary>macOS: Coreloom hasn't been allowed to use Bluetooth yet; macOS asks when the person clicks Allow.</summary>
    public static BluetoothRadioInfo PermissionNeeded { get; } = new(false, false, false) { NeedsPermission = true };

    /// <summary>The system asks the person before an app may use Bluetooth, and hasn't asked yet.</summary>
    public bool NeedsPermission { get; init; }
}

/// <summary>How a connect, disconnect or eject went, in words for the user.</summary>
public sealed record DeviceActionResult(bool Succeeded, string Message);

/// <summary>Merges what Windows lists separately for one device (dual-mode devices are paired over Classic and LE).</summary>
public static class BluetoothDeviceList
{
    public static IReadOnlyList<BluetoothDeviceInfo> Merge(IEnumerable<BluetoothDeviceInfo> devices)
    {
        var merged = new List<BluetoothDeviceInfo>();
        foreach (var group in devices.GroupBy(d => d.ContainerId is { } c && c != Guid.Empty ? c.ToString() : "addr:" + d.Address.ToString(CultureInfo.InvariantCulture)))
        {
            var items = group.OrderBy(d => d.Transport == BluetoothTransport.Classic ? 0 : 1).ToList();
            var main = items[0];
            var kind = items.Select(d => d.Kind).FirstOrDefault(k => k != DeviceKind.Unknown, DeviceKind.Unknown);
            merged.Add(main with
            {
                Kind = kind,
                IsConnected = items.Any(d => d.IsConnected),
                Battery = items.Select(d => d.Battery).FirstOrDefault(b => b is not null),
                LastConnected = items.Select(d => d.LastConnected).Where(t => t is not null).DefaultIfEmpty(null).Max(),
            });
        }

        return merged
            .OrderByDescending(d => d.IsConnected)
            .ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}

/// <summary>One device node from Windows' device tree (what Device Manager lists).</summary>
public sealed record DevNode(string InstanceId, string? ParentId, Guid? ContainerId, string? ClassName)
{
    public string? FriendlyName { get; init; }

    public string? Description { get; init; }

    /// <summary>The product name the device itself reports (USB string descriptor).</summary>
    public string? BusDescription { get; init; }

    public string? Manufacturer { get; init; }

    public int Problem { get; init; }

    /// <summary>Windows can eject it (DN_REMOVABLE).</summary>
    public bool IsRemovable { get; init; }

    /// <summary>"USB", "HID", "DISPLAY"…: the part of the instance id before the first backslash.</summary>
    public string Enumerator => InstanceId.Split('\\')[0].ToUpperInvariant();
}

/// <summary>A physical device plugged into the PC (a USB keyboard, a monitor, a flash drive…).</summary>
public sealed record WiredDevice(string Id, string Name, DeviceKind Kind, string Connection)
{
    public string? Manufacturer { get; init; }

    /// <summary>Device Manager's problem code (0 = working).</summary>
    public int Problem { get; init; }

    /// <summary>The device node to eject for "Safely remove", for removable storage.</summary>
    public string? EjectInstanceId { get; init; }

    public bool CanEject => EjectInstanceId is not null;

    public DeviceGroup Group => DeviceKinds.GroupOf(Kind);

    /// <summary>"Disabled", "Driver not installed"… or null when it works.</summary>
    public string? ProblemText => Problem switch
    {
        0 => null,
        22 => "Disabled in Device Manager",
        28 => "Driver not installed",
        10 => "Can't start",
        43 => "Stopped after reporting a problem",
        _ => $"Device problem (code {Problem.ToString(CultureInfo.InvariantCulture)})",
    };
}

/// <summary>
/// Turns Windows' device tree into the physical devices plugged into the PC. Every device node belongs to a container:
/// everything built into the PC shares one, each external device has its own. A container counts when it has a node
/// from a wired bus (USB, a display connection, Thunderbolt/PCIe) and none from Bluetooth (those have their own list).
/// </summary>
public static class WiredDevices
{
    /// <summary>The container of everything inside the PC (GUID_CONTAINERID_INTERNALLY_CONNECTED_DEVICE).</summary>
    public static readonly Guid InternalContainer = new("00000000-0000-0000-ffff-ffffffffffff");

    private static readonly HashSet<string> WiredBuses = new(StringComparer.OrdinalIgnoreCase) { "USB", "USBSTOR", "HID", "DISPLAY", "SCSI", "PCI", "USBPRINT" };

    private static readonly string[] GenericNames =
    [
        "usb composite device", "usb input device", "usb mass storage device", "generic pnp monitor", "generic monitor", "generic usb hub",
        "usb root hub", "hid-compliant", "hid keyboard device", "usb attached scsi", "usb audio device", "usb device", "usb video device",
        "disk drive", "generic superspeed usb hub", "usb hub",
    ];

    private static readonly string[] GenericMakers = ["(standard", "standard", "microsoft", "generic", "(generic", "compatible", "usb"];

    public static IReadOnlyList<WiredDevice> Build(IEnumerable<DevNode> nodes)
    {
        var devices = new List<WiredDevice>();
        foreach (var group in nodes.Where(n => n.ContainerId is { } c && c != Guid.Empty && c != InternalContainer).GroupBy(n => n.ContainerId!.Value))
        {
            var members = group.ToList();
            if (members.Any(n => n.Enumerator.StartsWith("BTH", StringComparison.Ordinal)) || !members.Any(n => WiredBuses.Contains(n.Enumerator)))
            {
                continue; // Bluetooth, or not plugged in by wire (network printers, software devices)
            }

            var ids = members.Select(n => n.InstanceId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var root = members
                .Where(n => n.ParentId is null || !ids.Contains(n.ParentId))
                .OrderBy(n => WiredBuses.Contains(n.Enumerator) ? 0 : 1)
                .First();

            var (kind, primary) = Classify(members, root);
            if (kind == DeviceKind.Hub)
            {
                continue; // a hub or a USB controller on its own isn't something you use; what's plugged into it is listed
            }

            string name = Name(members, root, primary, kind);
            var refined = DeviceKinds.Refine(kind, name);
            if (refined != DeviceKind.Controller || kind == DeviceKind.Controller || IsGamepad(members, name))
            {
                kind = refined; // "LED Controller" or "MSI Gaming Controller" doesn't become a game controller by its name alone
            }
            string? maker = new[] { root.Manufacturer, primary.Manufacturer }.FirstOrDefault(m => !string.IsNullOrWhiteSpace(m) && !GenericMakers.Any(g => m!.StartsWith(g, StringComparison.OrdinalIgnoreCase)));
            int problem = root.Problem != 0 ? root.Problem : members.Select(n => n.Problem).FirstOrDefault(p => p != 0);
            string? eject = kind is DeviceKind.Storage or DeviceKind.CardReader or DeviceKind.Phone
                ? (root.IsRemovable ? root : members.FirstOrDefault(n => n.IsRemovable && WiredBuses.Contains(n.Enumerator)))?.InstanceId
                : null;

            devices.Add(new WiredDevice(group.Key.ToString(), name, kind, Connection(root))
            {
                Manufacturer = maker?.Trim(),
                Problem = problem,
                EjectInstanceId = kind == DeviceKind.Phone ? null : eject,
            });
        }

        return devices
            .OrderBy(d => d.Group)
            .ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>The kind from the device classes in the container, and the node that decided it.</summary>
    private static (DeviceKind Kind, DevNode Primary) Classify(List<DevNode> members, DevNode root)
    {
        DevNode? Find(params string[] classes) => members.FirstOrDefault(n => n.ClassName is { } c && classes.Contains(c, StringComparer.OrdinalIgnoreCase));

        string? rootName = root.BusDescription ?? root.FriendlyName;
        var named = DeviceKinds.FromName(rootName);
        if (named == DeviceKind.Controller && !IsGamepad(members, rootName))
        {
            named = DeviceKind.Unknown; // the word "controller" alone: an LED, USB or monitor controller, decided below
        }

        if (named is DeviceKind.Mouse or DeviceKind.Keyboard or DeviceKind.Controller or DeviceKind.Receiver or DeviceKind.Headset or DeviceKind.Headphones or DeviceKind.Speaker or DeviceKind.Phone)
        {
            return (named, root);
        }

        if (Find("WPD") is { } portable && Find("DiskDrive") is null)
        {
            return (DeviceKinds.FromName(portable.FriendlyName) is DeviceKind.Phone or DeviceKind.Tablet ? DeviceKind.Phone : DeviceKind.Storage, portable);
        }

        if (Find("DiskDrive", "CDROM", "WPD") is { } disk)
        {
            return (DeviceKind.Storage, disk);
        }

        if (Find("Camera", "Image") is { } camera)
        {
            return (DeviceKinds.FromName(camera.FriendlyName) == DeviceKind.Printer ? DeviceKind.Printer : DeviceKind.Camera, camera);
        }

        if (Find("Monitor") is { } monitor)
        {
            return (DeviceKind.Display, monitor);
        }

        if (Find("XnaComposite", "XboxComposite") is { } pad)
        {
            return (DeviceKind.Controller, pad);
        }

        if (Find("AudioEndpoint", "MEDIA") is { } audio)
        {
            return (DeviceKind.Audio, audio);
        }

        var keyboard = Find("Keyboard");
        var mouse = Find("Mouse");
        if (keyboard is not null && mouse is not null)
        {
            return (named == DeviceKind.Unknown ? DeviceKind.KeyboardAndMouse : named, root);
        }

        if (keyboard is not null)
        {
            return (DeviceKind.Keyboard, keyboard);
        }

        if (mouse is not null)
        {
            return (DeviceKind.Mouse, mouse);
        }

        if (Find("Printer", "PrintQueue") is { } printer)
        {
            return (DeviceKind.Printer, printer);
        }

        if (Find("Net") is { } network)
        {
            return (DeviceKind.Network, network);
        }

        if (Find("SmartCardReader") is { } reader)
        {
            return (DeviceKind.CardReader, reader);
        }

        if (Find("HIDClass") is { } hid)
        {
            // Not a keyboard or a mouse: a light, a stream deck, a UPS, a monitor's control link…
            return (named == DeviceKind.Unknown ? DeviceKind.Accessory : named, hid);
        }

        bool onlyInfrastructure = members.Where(n => WiredBuses.Contains(n.Enumerator)).All(IsInfrastructure);
        return (onlyInfrastructure ? DeviceKind.Hub : named, root);
    }

    /// <summary>A game controller: named like one, or Windows set it up as one (an Xbox driver or a HID game controller).</summary>
    private static bool IsGamepad(List<DevNode> members, string? name) =>
        DeviceKinds.IsGamepadName(name)
        || members.Any(n => n.ClassName is "XnaComposite" or "XboxComposite"
            || DeviceKinds.IsGamepadName(n.FriendlyName) || DeviceKinds.IsGamepadName(n.Description));

    private static readonly string[] InfrastructureWords =
    [
        "hub", "host controller", "universal serial bus", "usb controller", "usb4", "root port", "pci express", "pci-to-pci", "pci bridge",
        "thunderbolt controller", "upstream port", "downstream port",
    ];

    /// <summary>Hubs, USB host controllers and PCIe bridges: plumbing that devices plug into, not devices.</summary>
    private static bool IsInfrastructure(DevNode node)
    {
        string text = $"{node.FriendlyName} {node.Description} {node.BusDescription}";
        return InfrastructureWords.Any(w => text.Contains(w, StringComparison.OrdinalIgnoreCase))
            || (node.Enumerator == "PCI" && node.ClassName is "USB" or "System" or null);
    }

    private static string Name(List<DevNode> members, DevNode root, DevNode primary, DeviceKind kind)
    {
        // Drives name themselves best on the disk ("SanDisk Ultra USB Device" → "SanDisk Ultra").
        string? disk = kind == DeviceKind.Storage && primary.FriendlyName is { } friendly
            ? TrimSuffix(TrimSuffix(friendly, " USB Device"), " SCSI Disk Device")
            : null;
        var candidates = new[]
        {
            disk, root.BusDescription, primary.FriendlyName, root.FriendlyName, primary.BusDescription, primary.Description, root.Description,
        }.Concat(members.Select(n => n.BusDescription)).Concat(members.Select(n => n.FriendlyName))
         .Where(n => !string.IsNullOrWhiteSpace(n))
         .Select(n => n!.Trim())
         .Where(IsReadable)
         .ToList();

        // Generic names last, and the device's own ("USB Input Device") before its parts' ("HID-compliant …").
        return candidates.FirstOrDefault(n => !IsGeneric(n))
            ?? candidates.FirstOrDefault(n => !n.StartsWith("hid-", StringComparison.OrdinalIgnoreCase))
            ?? candidates.FirstOrDefault()
            ?? DeviceKinds.Text(kind) switch
        {
            "Device" => "USB device",
            var text => "USB " + text.ToLowerInvariant(),
        };
    }

    /// <summary>
    /// A name worth showing: at least two letters or digits. Some devices report a broken product string (a stray
    /// character, a run of symbols); the next best name is used instead.
    /// </summary>
    public static bool IsReadable(string name) => name.Count(char.IsLetterOrDigit) >= 2;

    private static string TrimSuffix(string text, string suffix) =>
        text.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ? text[..^suffix.Length].TrimEnd() : text;

    public static bool IsGeneric(string name) =>
        GenericNames.Any(g => name.StartsWith(g, StringComparison.OrdinalIgnoreCase));

    private static string Connection(DevNode root) => root.Enumerator switch
    {
        "DISPLAY" => "Display cable",
        "PCI" => "Thunderbolt or PCIe",
        _ => "USB",
    };
}
