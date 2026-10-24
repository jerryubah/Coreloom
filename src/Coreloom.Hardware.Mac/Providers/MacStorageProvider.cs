using System.Diagnostics;
using System.Globalization;
using Coreloom.Core.Diagnostics;
using Coreloom.Core.Formatting;
using Coreloom.Core.Telemetry;
using Coreloom.Hardware.Mac.Native;
using Coreloom.Hardware.Mac.Sensors;

namespace Coreloom.Hardware.Mac.Providers;

/// <summary>A disk the Mac has: its BSD unit ("disk0" → 0), its model and how it's connected.</summary>
public sealed record MacDisk(int Number, string Model, string Interconnect, bool IsInternal, bool IsSolidState, long SizeBytes);

/// <summary>
/// Disks and volumes. Throughput and active time come from each disk's block-storage driver statistics (the counters
/// Activity Monitor's Disk tab reads); volumes are the mounted, browsable local file systems with Finder's capacity
/// figures (free space includes purgeable space macOS frees on demand). The internal SSD's temperature comes from its
/// NAND sensors on Apple silicon.
/// </summary>
public sealed class MacStorageProvider : ITelemetryProvider
{
    public const int Priority = 50;
    private const string LogCategory = "Storage";
    private static readonly TimeSpan RescanInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan VolumeInterval = TimeSpan.FromSeconds(5);
    private readonly SensorHub _sensors;
    private readonly Dictionary<int, Disk> _disks = [];
    private readonly Dictionary<string, Volume> _volumes = new(StringComparer.Ordinal);
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private IProviderContext? _context;
    private MetricHandle? _totalRead;
    private MetricHandle? _totalWrite;
    private TimeSpan _lastRescan;
    private TimeSpan? _lastVolumes;
    private bool _acquired;

    public MacStorageProvider(SensorHub sensors) => _sensors = sensors;

    public string Name => "macOS storage";

    public MonitoringSection Section => MonitoringSection.Storage;

    public int InitStage => 0;

    public TimeSpan MinimumPollInterval => TimeSpan.Zero;

    public IReadOnlyList<MacDisk> Disks => _disks.Values.Select(d => d.Info).OrderBy(d => d.Number).ToArray();

    public ProviderInitResult Initialize(IProviderContext context)
    {
        Shutdown();
        _context = context;
        _acquired = true;
        _sensors.Acquire();
        _totalRead = context.Register(new MetricDescriptor(MetricIds.DiskTotalRead, "Total disk read", MetricCategory.Storage, MetricKind.Throughput) { Group = "Throughput" }, Provenance.Measured, Priority);
        _totalWrite = context.Register(new MetricDescriptor(MetricIds.DiskTotalWrite, "Total disk write", MetricCategory.Storage, MetricKind.Throughput) { Group = "Throughput" }, Provenance.Measured, Priority);
        DiscoverDisks();
        DiscoverVolumes();
        _lastRescan = _clock.Elapsed;
        return _disks.Count == 0
            ? ProviderInitResult.Degraded("No disks with block-storage statistics were found.")
            : ProviderInitResult.Running($"{_disks.Count} disk(s), {_volumes.Count} volume(s)");
    }

    private void DiscoverDisks()
    {
        var context = _context!;
        var seen = new HashSet<int>();
        foreach (var (driver, _) in IOKit.Services("IOBlockStorageDriver", keepServices: true))
        {
            try
            {
                var info = Describe(driver);
                if (info is null)
                {
                    IOKit.IOObjectRelease(driver);
                    continue;
                }

                seen.Add(info.Number);
                if (_disks.TryGetValue(info.Number, out var existing))
                {
                    existing.ReplaceService(driver);
                    continue;
                }

                var disk = new Disk(driver, info, IOKit.EntryId(driver));
                Register(context, disk);
                _disks[info.Number] = disk;
            }
            catch (Exception ex)
            {
                IOKit.IOObjectRelease(driver);
                Log.WarnThrottled("mac-disk", TimeSpan.FromMinutes(10), LogCategory, "Reading a disk failed.", ex);
            }
        }

        foreach (int gone in _disks.Keys.Where(n => !seen.Contains(n)).ToArray())
        {
            _disks[gone].Release();
            _disks.Remove(gone);
            context.RemoveDevice(MetricIds.DiskDeviceId(gone));
        }
    }

    /// <summary>The disk behind a block-storage driver, or null for disk images and other virtual disks.</summary>
    private static MacDisk? Describe(uint driver)
    {
        Dictionary<string, object?>? media = null;
        foreach (uint child in IOKit.Children(driver))
        {
            if (media is null && IOKit.ClassName(child) is { } cls && cls.EndsWith("Media", StringComparison.Ordinal))
            {
                media = IOKit.Properties(child);
            }

            IOKit.IOObjectRelease(child);
        }

        if (media is null || CF.Number(media, "BSD Unit") is not double unit)
        {
            return null;
        }

        uint parent = IOKit.Parent(driver);
        var device = parent != 0 ? IOKit.Properties(parent) : null;
        if (parent != 0)
        {
            IOKit.IOObjectRelease(parent);
        }

        var characteristics = device?.GetValueOrDefault("Device Characteristics") as Dictionary<string, object?>;
        var protocol = device?.GetValueOrDefault("Protocol Characteristics") as Dictionary<string, object?>;
        string interconnect = CF.Text(protocol, "Physical Interconnect") ?? "Unknown";
        if (interconnect.Contains("Virtual", StringComparison.OrdinalIgnoreCase) || interconnect.Contains("Disk Image", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        bool isInternal = string.Equals(CF.Text(protocol, "Physical Interconnect Location"), "Internal", StringComparison.OrdinalIgnoreCase);
        string? medium = CF.Text(characteristics, "Medium Type");
        string model = CF.Text(characteristics, "Product Name") ?? CF.Text(media, "IOMediaContent") ?? $"disk{unit}";
        string vendor = CF.Text(characteristics, "Vendor Name") ?? string.Empty;
        if (vendor.Length > 0 && !model.StartsWith(vendor, StringComparison.OrdinalIgnoreCase))
        {
            model = $"{vendor} {model}";
        }

        bool ssd = medium is null
            ? interconnect is "Apple Fabric" or "PCI-Express" or "PCI" || model.Contains("SSD", StringComparison.OrdinalIgnoreCase)
            : medium.Contains("Solid", StringComparison.OrdinalIgnoreCase);
        long size = (long)(CF.Number(media, "Size") ?? 0);
        return new MacDisk((int)unit, model.Trim(), interconnect, isInternal, ssd, size);
    }

    private void Register(IProviderContext context, Disk disk)
    {
        var info = disk.Info;
        string deviceId = MetricIds.DiskDeviceId(info.Number);
        MetricHandle Reg(string id, string name, MetricKind kind, string group, string? note = null, Provenance provenance = Provenance.Measured) =>
            context.Register(new MetricDescriptor(id, name, MetricCategory.Storage, kind)
            {
                DeviceId = deviceId,
                DeviceName = info.Model,
                Group = group,
            }, provenance, Priority, note);

        const string stats = "The disk's block-storage driver statistics (bytes and time spent reading and writing)";
        disk.Read = Reg(MetricIds.DiskRead(info.Number), "Read speed", MetricKind.Throughput, "Throughput", stats);
        disk.Write = Reg(MetricIds.DiskWrite(info.Number), "Write speed", MetricKind.Throughput, "Throughput", stats);
        disk.Activity = Reg(MetricIds.DiskActivity(info.Number), "Active time", MetricKind.Load, "Activity",
            "Time spent reading and writing ÷ elapsed time (the driver's Total Time counters)", Provenance.Derived);
        bool apple = MacSystemInfo.IsAppleSilicon && info.IsInternal && info.Interconnect == "Apple Fabric";
        if (apple)
        {
            disk.Temperature = Reg(MetricIds.DiskTemperature(info.Number), "Temperature", MetricKind.Temperature, "Temperatures", "Average of the SSD's NAND sensors (HID temperature sensors)");
        }
        else
        {
            context.SetUnavailableReason(MetricIds.DiskTemperature(info.Number), "macOS doesn't share this drive's temperature with apps.");
        }

        const string health = "macOS only shares an SSD's wear readings with privileged tools.";
        context.SetUnavailableReason(MetricIds.DiskLifeUsed(info.Number), health);
        context.SetUnavailableReason(MetricIds.DiskAvailableSpare(info.Number), health);

        var f = new UnitFormatter(culture: CultureInfo.InvariantCulture);
        string connection = info.Interconnect switch
        {
            "Apple Fabric" => "Apple Fabric (internal)",
            "PCI-Express" or "PCI" => "PCI Express (NVMe)",
            var other => other,
        };
        string kind = info.Interconnect switch
        {
            "Apple Fabric" => "Apple SSD",
            "PCI-Express" or "PCI" => "NVMe SSD",
            "USB" => info.IsSolidState ? "USB SSD" : "USB drive",
            "Thunderbolt" => "Thunderbolt drive",
            "SATA" => info.IsSolidState ? "SATA SSD" : "SATA HDD",
            _ => info.IsSolidState ? "SSD" : "Drive",
        };
        var props = new List<KeyValuePair<string, string>>
        {
            new("Capacity", info.SizeBytes > 0 ? f.Bytes(info.SizeBytes, 0).ToString() : "Unknown"),
            new("Interface", connection),
            new("Type", info.IsSolidState ? "Solid state" : "Rotational"),
            new("Location", info.IsInternal ? "Internal" : "External"),
            new("Disk number", info.Number.ToString(CultureInfo.InvariantCulture)),
            new("BSD name", $"disk{info.Number.ToString(CultureInfo.InvariantCulture)}"),
        };
        context.UpsertDevice(new DeviceInfo(deviceId, MetricCategory.Storage, info.Model)
        {
            Kind = kind,
            Order = info.Number,
            Source = "macOS",
            Properties = props,
        });
    }

    private void DiscoverVolumes()
    {
        var context = _context!;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string mount in MountPoints())
        {
            try
            {
                if (!FileSystems.TryStat(mount, out var stat, out string fs, out string device)
                    || (stat.Flags & FileSystems.MntLocal) == 0
                    || (stat.Flags & FileSystems.MntDontBrowse) != 0
                    || !device.StartsWith("/dev/disk", StringComparison.Ordinal))
                {
                    continue;
                }

                var volumeInfo = FileSystems.VolumeInfo(mount);
                if (volumeInfo?.Browsable == false)
                {
                    continue;
                }

                string bsd = device["/dev/".Length..];
                int? diskNumber = DiskOf(bsd);
                if (diskNumber is null)
                {
                    continue; // a disk image or a disk Coreloom doesn't list
                }

                string key = VolumeKey(bsd);
                if (!seen.Add(key) || _volumes.ContainsKey(key))
                {
                    continue;
                }

                string name = volumeInfo?.Name is { Length: > 0 } n ? n : mount == "/" ? "Macintosh HD" : Path.GetFileName(mount);
                string deviceId = $"vol/{key}";
                MetricHandle Reg(string id, string label, MetricKind kind) =>
                    context.Register(new MetricDescriptor(id, label, MetricCategory.Storage, kind)
                    {
                        DeviceId = deviceId,
                        DeviceName = name,
                        Group = "Volumes",
                        TrackHistory = false,
                    }, Provenance.Measured, Priority);

                _volumes[key] = new Volume(mount)
                {
                    Used = Reg(MetricIds.VolumeUsed(key), $"{name}: used", MetricKind.Data),
                    Free = Reg(MetricIds.VolumeFree(key), $"{name}: available", MetricKind.Data),
                    Total = Reg(MetricIds.VolumeTotal(key), $"{name}: capacity", MetricKind.Data),
                    Load = Reg(MetricIds.VolumeLoad(key), $"{name}: used %", MetricKind.Load),
                };
                context.UpsertDevice(new DeviceInfo(deviceId, MetricCategory.Storage, name)
                {
                    Kind = "Volume",
                    Order = mount == "/" ? 100 : 101 + _volumes.Count,
                    Source = "macOS",
                    Properties =
                    [
                        new("Mount point", mount),
                        new("File system", FileSystemName(fs)),
                        new("Device", bsd),
                        new("Disk number", diskNumber.Value.ToString(CultureInfo.InvariantCulture)),
                    ],
                });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The volume went away while it was being read.
            }
        }

        foreach (string gone in _volumes.Keys.Where(k => !seen.Contains(k)).ToArray())
        {
            _volumes.Remove(gone);
            context.RemoveDevice($"vol/{gone}");
        }
    }

    /// <summary>"/" and the volumes under /Volumes (the ones Finder shows).</summary>
    private static IEnumerable<string> MountPoints()
    {
        yield return "/";
        string[] others;
        try
        {
            others = Directory.Exists("/Volumes") ? Directory.GetDirectories("/Volumes") : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            others = [];
        }

        foreach (string mount in others.Order(StringComparer.Ordinal))
        {
            // /Volumes/Macintosh HD is a link back to "/".
            if (new DirectoryInfo(mount).LinkTarget is null)
            {
                yield return mount;
            }
        }
    }

    /// <summary>"disk3s1s1" → "DISK3S1" (a sealed system snapshot counts as its volume).</summary>
    internal static string VolumeKey(string bsd)
    {
        string name = bsd.StartsWith("disk", StringComparison.OrdinalIgnoreCase) ? bsd[4..] : bsd;
        string[] parts = name.Split('s');
        string key = parts.Length >= 2 ? $"disk{parts[0]}s{parts[1]}" : bsd;
        return key.ToUpperInvariant();
    }

    internal static string FileSystemName(string fs) => fs switch
    {
        "apfs" => "APFS",
        "hfs" => "Mac OS Extended",
        "exfat" => "ExFAT",
        "msdos" => "FAT",
        "ntfs" => "NTFS",
        _ => fs.ToUpperInvariant(),
    };

    /// <summary>The disk a volume lives on: the first block-storage driver above its media in the I/O registry.</summary>
    private int? DiskOf(string bsd)
    {
        uint media = IOKit.IOServiceGetMatchingService(0, IOKit.IOBSDNameMatching(0, 0, bsd));
        if (media == 0)
        {
            return null;
        }

        try
        {
            ulong driver = IOKit.AncestorId(media, "IOBlockStorageDriver");
            foreach (var disk in _disks.Values)
            {
                if (disk.DriverId == driver && driver != 0)
                {
                    return disk.Info.Number;
                }
            }

            return null;
        }
        finally
        {
            IOKit.IOObjectRelease(media);
        }
    }

    public void Poll(IMetricWriter writer)
    {
        var now = _clock.Elapsed;
        if (now - _lastRescan >= RescanInterval)
        {
            _lastRescan = now;
            DiscoverDisks();
            DiscoverVolumes();
        }

        double totalRead = 0, totalWrite = 0;
        bool any = false;
        var sample = _disks.Values.Any(d => d.Temperature is not null) ? _sensors.Sample() : null;
        foreach (var disk in _disks.Values)
        {
            if (disk.Sample(now) is not { } rates)
            {
                continue;
            }

            any = true;
            writer.Write(disk.Read!, rates.Read);
            writer.Write(disk.Write!, rates.Write);
            writer.Write(disk.Activity!, rates.Active);
            totalRead += rates.Read;
            totalWrite += rates.Write;
            if (disk.Temperature is not null && sample is not null)
            {
                writer.Write(disk.Temperature, sample.SsdTemperature);
            }
        }

        if (any)
        {
            writer.Write(_totalRead!, totalRead);
            writer.Write(_totalWrite!, totalWrite);
        }

        if (_lastVolumes is not { } lastVolumes || now - lastVolumes >= VolumeInterval)
        {
            _lastVolumes = now;
            foreach (var volume in _volumes.Values)
            {
                volume.Refresh();
            }
        }

        foreach (var volume in _volumes.Values.Where(v => v.Valid))
        {
            writer.Write(volume.Total!, volume.TotalBytes);
            writer.Write(volume.Free!, volume.FreeBytes);
            writer.Write(volume.Used!, volume.TotalBytes - volume.FreeBytes);
            if (volume.TotalBytes > 0)
            {
                writer.Write(volume.Load!, Math.Clamp((volume.TotalBytes - volume.FreeBytes) * 100.0 / volume.TotalBytes, 0, 100));
            }
        }
    }

    public void Shutdown()
    {
        foreach (var disk in _disks.Values)
        {
            disk.Release();
        }

        _disks.Clear();
        _volumes.Clear();
        if (_acquired)
        {
            _acquired = false;
            _sensors.Release();
        }
    }

    public void Dispose() => Shutdown();

    private sealed class Disk(uint service, MacDisk info, ulong driverId)
    {
        private uint _service = service;
        private (double Read, double Write, double ReadTime, double WriteTime, TimeSpan At)? _previous;

        public MacDisk Info { get; } = info;

        public ulong DriverId { get; private set; } = driverId;

        public MetricHandle? Read { get; set; }

        public MetricHandle? Write { get; set; }

        public MetricHandle? Activity { get; set; }

        public MetricHandle? Temperature { get; set; }

        public void ReplaceService(uint service)
        {
            Release();
            _service = service;
            DriverId = IOKit.EntryId(service);
        }

        public (double Read, double Write, double Active)? Sample(TimeSpan now)
        {
            if (_service == 0 || IOKit.Property(_service, "Statistics") is not Dictionary<string, object?> stats)
            {
                return null;
            }

            double read = CF.Number(stats, "Bytes (Read)") ?? double.NaN;
            double write = CF.Number(stats, "Bytes (Write)") ?? double.NaN;
            double readTime = CF.Number(stats, "Total Time (Read)") ?? 0;
            double writeTime = CF.Number(stats, "Total Time (Write)") ?? 0;
            var previous = _previous;
            _previous = (read, write, readTime, writeTime, now);
            if (previous is not { } p || !double.IsFinite(read) || !double.IsFinite(write))
            {
                return null;
            }

            double seconds = (now - p.At).TotalSeconds;
            if (seconds <= 0.05 || read < p.Read || write < p.Write)
            {
                return null;
            }

            double busyNs = Math.Max(0, readTime - p.ReadTime) + Math.Max(0, writeTime - p.WriteTime);
            double active = Math.Clamp(busyNs / (seconds * 1e9) * 100.0, 0, 100);
            return ((read - p.Read) / seconds, (write - p.Write) / seconds, active);
        }

        public void Release()
        {
            if (_service != 0)
            {
                IOKit.IOObjectRelease(_service);
                _service = 0;
            }
        }
    }

    private sealed class Volume(string mountPoint)
    {
        public string MountPoint { get; } = mountPoint;

        public MetricHandle? Used { get; init; }

        public MetricHandle? Free { get; init; }

        public MetricHandle? Total { get; init; }

        public MetricHandle? Load { get; init; }

        public bool Valid { get; private set; }

        public long TotalBytes { get; private set; }

        public long FreeBytes { get; private set; }

        public void Refresh()
        {
            try
            {
                var info = FileSystems.VolumeInfo(MountPoint);
                if (info is { Total: long total, Available: long available } && total > 0)
                {
                    TotalBytes = total;
                    FreeBytes = Math.Clamp(available, 0, total);
                    Valid = true;
                    return;
                }

                var drive = new DriveInfo(MountPoint);
                TotalBytes = drive.TotalSize;
                FreeBytes = drive.AvailableFreeSpace;
                Valid = TotalBytes > 0;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Valid = false;
            }
        }
    }
}
