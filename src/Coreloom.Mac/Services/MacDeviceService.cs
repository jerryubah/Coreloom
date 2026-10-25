using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using Coreloom.Core.Devices;
using Coreloom.Core.Diagnostics;
using Coreloom.Hardware.Mac.Native;
using Coreloom.Mac.Native;
using Coreloom.ViewModels.Services;

namespace Coreloom.Mac.Services;

/// <summary>
/// The Devices page on a Mac: paired Bluetooth devices through IOBluetooth (connect, disconnect and the radio switch,
/// as the Bluetooth menu does), battery levels Apple's own accessories report in the I/O registry, and what is plugged
/// in by wire — USB devices from the I/O registry, displays and Thunderbolt devices as System Information lists them —
/// with Eject for drives (diskutil). macOS asks once whether Coreloom may use Bluetooth.
/// </summary>
public sealed partial class MacDeviceService : IDeviceService, IDisposable
{
    private const string LogCategory = "Devices";
    private const string BluetoothFramework = "/System/Library/Frameworks/IOBluetooth.framework/IOBluetooth";
    private readonly Func<Func<object?>, Task<object?>> _onMain;
    private readonly object _gate = new();
    private Timer? _poll;
    private string _lastState = string.Empty;
    private bool _bluetoothLoaded;

    [LibraryImport(BluetoothFramework)]
    private static partial int IOBluetoothPreferenceGetControllerPowerState();

    [LibraryImport(BluetoothFramework)]
    private static partial void IOBluetoothPreferenceSetControllerPowerState(int state);

    /// <param name="onMain">Runs a function on the main thread (IOBluetooth belongs to it).</param>
    public MacDeviceService(Func<Func<object?>, Task<object?>> onMain) => _onMain = onMain;

    public bool IsSupported => OperatingSystem.IsMacOS();

    public event EventHandler? Changed;

    private bool LoadBluetooth()
    {
        if (!_bluetoothLoaded)
        {
            _bluetoothLoaded = NativeLibrary.TryLoad(BluetoothFramework, out _);
        }

        return _bluetoothLoaded;
    }

    private async Task<T> Main<T>(Func<T> work) => (T)(await _onMain(() => work()).ConfigureAwait(false))!;

    // ---- Bluetooth -----------------------------------------------------------------------------------------

    public async Task<BluetoothRadioInfo> RadioAsync(CancellationToken cancellationToken)
    {
        StartPolling();
        return await Main(() =>
        {
            if (!LoadBluetooth() || !HasController())
            {
                return BluetoothRadioInfo.None;
            }

            try
            {
                return new BluetoothRadioInfo(true, IOBluetoothPreferenceGetControllerPowerState() == 1, true);
            }
            catch (EntryPointNotFoundException)
            {
                return new BluetoothRadioInfo(true, true, false);
            }
        });
    }

    private static bool HasController()
    {
        using var pool = new ObjC.Pool();
        nint controller = ObjC.Send(ObjC.Class("IOBluetoothHostController"), ObjC.Sel("defaultController"));
        return controller != 0 && ObjC.Send(controller, ObjC.Sel("addressAsString")) != 0;
    }

    public async Task<bool> SetRadioAsync(bool on, CancellationToken cancellationToken)
    {
        bool done = await Main(() =>
        {
            try
            {
                IOBluetoothPreferenceSetControllerPowerState(on ? 1 : 0);
                return true;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
        });
        if (!done)
        {
            return false;
        }

        for (int i = 0; i < 20; i++)
        {
            await Task.Delay(250, cancellationToken);
            if ((await RadioAsync(cancellationToken)).IsOn == on)
            {
                Log.Info(LogCategory, $"Bluetooth turned {(on ? "on" : "off")}.");
                return true;
            }
        }

        return false;
    }

    public async Task<IReadOnlyList<BluetoothDeviceInfo>> BluetoothDevicesAsync(CancellationToken cancellationToken)
    {
        var batteries = await Task.Run(AccessoryBatteries, cancellationToken);
        return await Main<IReadOnlyList<BluetoothDeviceInfo>>(() =>
        {
            if (!LoadBluetooth())
            {
                return [];
            }

            using var pool = new ObjC.Pool();
            nint paired = ObjC.Send(ObjC.Class("IOBluetoothDevice"), ObjC.Sel("pairedDevices"));
            if (paired == 0)
            {
                return [];
            }

            var devices = new List<BluetoothDeviceInfo>();
            ulong count = ObjC.SendULong(paired, ObjC.Sel("count"));
            for (ulong i = 0; i < count; i++)
            {
                nint device = ObjC.SendLong(paired, ObjC.Sel("objectAtIndex:"), (long)i);
                string address = ObjC.ToStringValue(ObjC.Send(device, ObjC.Sel("addressString"))) ?? string.Empty;
                string name = ObjC.ToStringValue(ObjC.Send(device, ObjC.Sel("nameOrAddress"))) ?? address;
                bool connected = ObjC.SendBool(device, ObjC.Sel("isConnected"));
                uint major = (uint)ObjC.SendULong(device, ObjC.Sel("deviceClassMajor"));
                uint minor = (uint)ObjC.SendULong(device, ObjC.Sel("deviceClassMinor"));
                DateTime? last = null;
                nint date = ObjC.Send(device, ObjC.Sel("recentAccessDate"));
                if (date != 0)
                {
                    double seconds = ObjC.SendDouble(date, ObjC.Sel("timeIntervalSince1970"));
                    last = DateTimeOffset.FromUnixTimeMilliseconds((long)(seconds * 1000)).LocalDateTime;
                }

                devices.Add(new BluetoothDeviceInfo(address, name, BluetoothAddress.Parse(address), BluetoothTransport.Classic, BluetoothClass.Kind(major, minor, name), connected)
                {
                    Battery = batteries.TryGetValue(BluetoothAddress.Normalize(address), out int battery) ? battery : null,
                    LastConnected = last,
                });
            }

            return BluetoothDeviceList.Merge(devices);
        });
    }

    /// <summary>Battery levels Apple's Bluetooth accessories (Magic Mouse, Keyboard, Trackpad) report, by address.</summary>
    private static Dictionary<string, int> AccessoryBatteries()
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var (_, properties) in IOKit.Services("AppleDeviceManagementHIDEventService"))
            {
                if (CF.Number(properties, "BatteryPercent") is double percent && CF.Text(properties, "DeviceAddress") is { } address)
                {
                    result[BluetoothAddress.Normalize(address)] = (int)Math.Clamp(percent, 0, 100);
                }
            }
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
        }

        return result;
    }

    public Task<DeviceActionResult> ConnectAsync(BluetoothDeviceInfo device, CancellationToken cancellationToken) => Connect(device, connect: true, cancellationToken);

    public Task<DeviceActionResult> DisconnectAsync(BluetoothDeviceInfo device, CancellationToken cancellationToken) => Connect(device, connect: false, cancellationToken);

    private async Task<DeviceActionResult> Connect(BluetoothDeviceInfo info, bool connect, CancellationToken cancellationToken)
    {
        int status = await Main(() =>
        {
            using var pool = new ObjC.Pool();
            nint device = ObjC.Send(ObjC.Class("IOBluetoothDevice"), ObjC.Sel("deviceWithAddressString:"), ObjC.NSString(info.Id));
            return device == 0 ? -1 : (int)ObjC.SendLongResult(device, ObjC.Sel(connect ? "openConnection" : "closeConnection"));
        });

        for (int i = 0; i < 24 && status == 0; i++)
        {
            await Task.Delay(250, cancellationToken);
            bool connected = await Main(() =>
            {
                using var pool = new ObjC.Pool();
                nint device = ObjC.Send(ObjC.Class("IOBluetoothDevice"), ObjC.Sel("deviceWithAddressString:"), ObjC.NSString(info.Id));
                return device != 0 && ObjC.SendBool(device, ObjC.Sel("isConnected"));
            });
            if (connected == connect)
            {
                RaiseIfChanged();
                return new DeviceActionResult(true, connect ? $"{info.Name} is connected." : $"{info.Name} is disconnected.");
            }
        }

        return status == 0
            ? new DeviceActionResult(false, connect ? $"{info.Name} didn't connect. Make sure it's on and nearby." : $"{info.Name} is still connected.")
            : new DeviceActionResult(false, $"macOS couldn't {(connect ? "connect to" : "disconnect")} {info.Name} (error 0x{status:x8}).");
    }

    public void OpenBluetoothSettings() => Commands.Start("/usr/bin/open", "x-apple.systempreferences:com.apple.BluetoothSettings");

    private void StartPolling()
    {
        lock (_gate)
        {
            _poll ??= new Timer(_ => RaiseIfChanged(), null, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(4));
        }
    }

    /// <summary>Raises <see cref="Changed"/> when a device connected or disconnected, or the radio switched.</summary>
    private void RaiseIfChanged()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var radio = await RadioAsync(CancellationToken.None);
                var devices = await BluetoothDevicesAsync(CancellationToken.None);
                string state = radio.IsOn + "|" + string.Join(',', devices.Where(d => d.IsConnected).Select(d => d.Id));
                string previous;
                lock (_gate)
                {
                    previous = _lastState;
                    _lastState = state;
                }

                if (previous.Length > 0 && previous != state)
                {
                    Changed?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (Exception e) when (e is InvalidOperationException or TaskCanceledException)
            {
                Log.Debug(LogCategory, $"Checking Bluetooth failed: {e.Message}");
            }
        });
    }

    // ---- Wired ---------------------------------------------------------------------------------------------

    public Task<IReadOnlyList<WiredDevice>> WiredDevicesAsync(CancellationToken cancellationToken) => Task.Run<IReadOnlyList<WiredDevice>>(async () =>
    {
        var devices = new List<WiredDevice>();
        devices.AddRange(UsbDevices());
        devices.AddRange(await DisplaysAsync(cancellationToken));
        devices.AddRange(await ThunderboltAsync(cancellationToken));
        return devices
            .GroupBy(d => d.Id, StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderBy(d => d.Group)
            .ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }, cancellationToken);

    private static IEnumerable<WiredDevice> UsbDevices()
    {
        foreach (var (service, properties) in IOKit.Services("IOUSBHostDevice", keepServices: true))
        {
            try
            {
                string? name = CF.Text(properties, "USB Product Name") ?? CF.Text(properties, "kUSBProductString");
                double deviceClass = CF.Number(properties, "bDeviceClass") ?? 0;
                if (name is null || deviceClass == 9 || UsbDevice.IsInternal(name, CF.Text(properties, "USB Vendor Name")))
                {
                    continue; // hubs and the Mac's own internal devices
                }

                string? vendor = CF.Text(properties, "USB Vendor Name") ?? CF.Text(properties, "kUSBVendorString");
                string? disk = BsdDiskUnder(service);
                var kind = disk is not null ? DeviceKind.Storage : UsbDevice.Kind(name, InterfaceClasses(service));
                string id = "usb:" + (CF.Number(properties, "locationID") is double location ? ((long)location).ToString("x", CultureInfo.InvariantCulture) : name);
                yield return new WiredDevice(id, name.Trim(), kind, "USB")
                {
                    Manufacturer = vendor,
                    EjectInstanceId = disk,
                };
            }
            finally
            {
                IOKit.IOObjectRelease(service);
            }
        }
    }

    private static List<int> InterfaceClasses(uint device)
    {
        var classes = new List<int>();
        foreach (uint child in IOKit.Children(device))
        {
            if (IOKit.Property(child, "bInterfaceClass") is long c)
            {
                classes.Add((int)c);
            }

            IOKit.IOObjectRelease(child);
        }

        return classes;
    }

    [LibraryImport("/System/Library/Frameworks/IOKit.framework/IOKit")]
    private static partial nint IORegistryEntrySearchCFProperty(uint entry, [MarshalAs(UnmanagedType.LPUTF8Str)] string plane, nint key, nint allocator, uint options);

    /// <summary>The whole disk ("disk4") a USB drive provides, found anywhere below it in the registry, or null.</summary>
    private static string? BsdDiskUnder(uint device)
    {
        nint key = CF.String("BSD Name");
        try
        {
            nint value = IORegistryEntrySearchCFProperty(device, "IOService", key, 0, 1 /* kIORegistryIterateRecursively */);
            if (value == 0)
            {
                return null;
            }

            string? bsd = CF.ToStringValue(value);
            CF.CFRelease(value);
            if (bsd is null || !bsd.StartsWith("disk", StringComparison.Ordinal))
            {
                return null;
            }

            int slice = bsd.IndexOf('s', 4);
            return slice > 0 ? bsd[..slice] : bsd;
        }
        finally
        {
            CF.CFRelease(key);
        }
    }

    /// <summary>External displays, as System Information lists them.</summary>
    private static async Task<IEnumerable<WiredDevice>> DisplaysAsync(CancellationToken cancellationToken)
    {
        var result = await Commands.RunAsync("/usr/sbin/system_profiler", ["SPDisplaysDataType", "-json"], TimeSpan.FromSeconds(20), cancellationToken);
        return result.Succeeded ? SystemProfiler.Displays(result.Output) : [];
    }

    private static async Task<IEnumerable<WiredDevice>> ThunderboltAsync(CancellationToken cancellationToken)
    {
        var result = await Commands.RunAsync("/usr/sbin/system_profiler", ["SPThunderboltDataType", "-json"], TimeSpan.FromSeconds(20), cancellationToken);
        return result.Succeeded ? SystemProfiler.Thunderbolt(result.Output) : [];
    }

    public async Task<DeviceActionResult> EjectAsync(WiredDevice device, CancellationToken cancellationToken)
    {
        if (device.EjectInstanceId is not { } disk || !disk.StartsWith("disk", StringComparison.Ordinal) || !disk[4..].All(char.IsAsciiDigit))
        {
            return new DeviceActionResult(false, $"{device.Name} can't be ejected.");
        }

        var result = await Commands.RunAsync("/usr/sbin/diskutil", ["eject", disk], TimeSpan.FromMinutes(1), cancellationToken);
        Log.Info(LogCategory, $"Eject {disk}: {result.Output} {result.Error}".Trim());
        return result.Succeeded
            ? new DeviceActionResult(true, $"{device.Name} can be unplugged now.")
            : new DeviceActionResult(false, result.Error.Contains("in use", StringComparison.OrdinalIgnoreCase) || result.Error.Contains("busy", StringComparison.OrdinalIgnoreCase)
                ? $"{device.Name} is still in use. Close the files and apps using it, then try again."
                : $"{device.Name} couldn't be ejected: {result.Error}");
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _poll?.Dispose();
            _poll = null;
        }
    }
}

/// <summary>Bluetooth addresses in their two spellings ("aa-bb-cc-dd-ee-ff" and "AA:BB:…").</summary>
public static class BluetoothAddress
{
    public static string Normalize(string address) => address.Replace('-', ':').ToUpperInvariant();

    public static ulong Parse(string address)
    {
        string hex = new(address.Where(char.IsAsciiHexDigit).ToArray());
        return hex.Length == 12 && ulong.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong value) ? value : 0;
    }
}

/// <summary>The Bluetooth class of device (major and minor class) as a kind, refined by the name.</summary>
public static class BluetoothClass
{
    public static DeviceKind Kind(uint major, uint minor, string name)
    {
        var byName = DeviceKinds.FromName(name);
        if (byName != DeviceKind.Unknown)
        {
            return byName;
        }

        return major switch
        {
            0x01 => DeviceKind.Computer,
            0x02 => DeviceKind.Phone,
            0x04 => minor switch
            {
                0x01 or 0x02 => DeviceKind.Headset,
                0x04 => DeviceKind.Microphone,
                0x05 => DeviceKind.Speaker,
                0x06 => DeviceKind.Headphones,
                0x08 => DeviceKind.CarAudio,
                _ => DeviceKind.Audio,
            },
            0x05 => (minor >> 4) switch
            {
                0x1 => DeviceKind.Keyboard,
                0x2 => DeviceKind.Mouse,
                0x3 => DeviceKind.KeyboardAndMouse,
                _ => (minor & 0x0F) is 0x01 or 0x02 ? DeviceKind.Controller : DeviceKind.Input,
            },
            0x06 => DeviceKind.Camera,
            0x07 => DeviceKind.Watch,
            _ => DeviceKind.Unknown,
        };
    }
}

/// <summary>Naming USB devices: their kind from the interfaces they offer, and the Mac's own internal ones.</summary>
public static class UsbDevice
{
    /// <summary>The Mac's own devices on USB (Intel Macs' T2 chip, FaceTime camera, internal keyboard and trackpad).</summary>
    public static bool IsInternal(string name, string? vendor) =>
        (vendor?.StartsWith("Apple", StringComparison.OrdinalIgnoreCase) == true || name.StartsWith("Apple", StringComparison.OrdinalIgnoreCase))
        && (name.Contains("Internal", StringComparison.OrdinalIgnoreCase) || name.Contains("Built-in", StringComparison.OrdinalIgnoreCase)
            || name.Contains("T2", StringComparison.Ordinal) || name.Contains("Touch Bar", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Bluetooth USB Host Controller", StringComparison.OrdinalIgnoreCase) || name.Contains("Ambient Light", StringComparison.OrdinalIgnoreCase))
        || name.Contains("Virtual", StringComparison.OrdinalIgnoreCase) && vendor?.Contains("Apple", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>From the name first, then the USB interface classes (1 audio, 3 input, 7 printer, 8 storage, 14 video).</summary>
    public static DeviceKind Kind(string name, IReadOnlyCollection<int> interfaceClasses)
    {
        var byName = DeviceKinds.FromName(name);
        if (byName != DeviceKind.Unknown)
        {
            return byName;
        }

        if (interfaceClasses.Contains(14))
        {
            return DeviceKind.Camera;
        }

        if (interfaceClasses.Contains(8))
        {
            return DeviceKind.Storage;
        }

        if (interfaceClasses.Contains(7))
        {
            return DeviceKind.Printer;
        }

        if (interfaceClasses.Contains(1))
        {
            return DeviceKind.Audio;
        }

        if (interfaceClasses.Contains(3))
        {
            return DeviceKind.Input;
        }

        return interfaceClasses.Contains(2) || interfaceClasses.Contains(10) ? DeviceKind.Network : DeviceKind.Accessory;
    }
}

/// <summary>Reading `system_profiler -json` for external displays and Thunderbolt devices.</summary>
public static class SystemProfiler
{
    public static IEnumerable<WiredDevice> Displays(string json)
    {
        var result = new List<WiredDevice>();
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("SPDisplaysDataType", out var gpus))
            {
                return result;
            }

            foreach (var gpu in gpus.EnumerateArray())
            {
                if (!gpu.TryGetProperty("spdisplays_ndrvs", out var displays))
                {
                    continue;
                }

                foreach (var display in displays.EnumerateArray())
                {
                    string name = display.TryGetProperty("_name", out var n) ? n.GetString() ?? "Display" : "Display";
                    bool builtIn = display.TryGetProperty("spdisplays_connection_type", out var c) && c.GetString() == "spdisplays_internal"
                        || display.TryGetProperty("spdisplays_display_type", out var t) && (t.GetString()?.Contains("built-in", StringComparison.OrdinalIgnoreCase) ?? false)
                        || name.Contains("Color LCD", StringComparison.OrdinalIgnoreCase) || name.Contains("Built-in", StringComparison.OrdinalIgnoreCase);
                    if (builtIn || name.Contains("Virtual", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string resolution = display.TryGetProperty("_spdisplays_resolution", out var r) ? r.GetString() ?? string.Empty : string.Empty;
                    result.Add(new WiredDevice("display:" + name + resolution, name, DeviceKind.Display, "Display")
                    {
                        Manufacturer = display.TryGetProperty("_spdisplays_display-vendor-id", out var v) ? v.GetString() : null,
                    });
                }
            }
        }
        catch (JsonException)
        {
        }

        return result;
    }

    public static IEnumerable<WiredDevice> Thunderbolt(string json)
    {
        var result = new List<WiredDevice>();
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("SPThunderboltDataType", out var buses))
            {
                return result;
            }

            foreach (var bus in buses.EnumerateArray())
            {
                if (!bus.TryGetProperty("_items", out var items))
                {
                    continue;
                }

                foreach (var item in items.EnumerateArray())
                {
                    string name = item.TryGetProperty("_name", out var n) ? n.GetString() ?? "Thunderbolt device" : "Thunderbolt device";
                    string? vendor = item.TryGetProperty("vendor_name_key", out var v) ? v.GetString() : null;
                    var kind = DeviceKinds.FromName(name) is var k && k != DeviceKind.Unknown ? k : DeviceKind.Hub;
                    result.Add(new WiredDevice("tb:" + name + (item.TryGetProperty("switch_uid_key", out var uid) ? uid.GetString() : string.Empty), name, kind, "Thunderbolt")
                    {
                        Manufacturer = vendor,
                    });
                }
            }
        }
        catch (JsonException)
        {
        }

        return result;
    }
}
