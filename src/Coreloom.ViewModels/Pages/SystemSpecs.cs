using Coreloom.Core.Formatting;
using Coreloom.Core.Hardware;
using Coreloom.Core.Telemetry;
using Coreloom.ViewModels.Infrastructure;
using Coreloom.ViewModels.Services;

namespace Coreloom.ViewModels.Pages;

/// <summary>The computer as the Overview's header describes it: its name, graphics, key specs and volumes.</summary>
public sealed record SystemSpecsInfo(
    string Title,
    string Subtitle,
    GpuSize GpuSize,
    string GpuLabel,
    IReadOnlyList<KeyValueItem> Left,
    IReadOnlyList<KeyValueItem> Right,
    int DriveCount,
    IReadOnlyList<(string Letter, string Name)> Volumes);

/// <summary>
/// Builds the Overview header's description of the computer, shared by the Overview and Coreloom Connect's phone app
/// so both say the same thing.
/// </summary>
public static class SystemSpecs
{
    public static SystemSpecsInfo Describe(MonitorContext monitor, int gpuSlot, bool isMac, UnitFormatter formatter)
    {
        var devices = monitor.Engine.Devices;
        var summary = monitor.Summary;
        var cpu = devices.Get(MetricIds.CpuDeviceId);
        string cpuName = PageViewModel.ShortCpuName(cpu?.Name ?? summary.CpuName);
        var gpu = monitor.Gpus().FirstOrDefault(g => g.Slot == gpuSlot);
        var gpuDevice = devices.Get(MetricIds.GpuDeviceId(gpuSlot));
        string? gpuName = Clean(gpuDevice?.Name ?? gpu.Name);
        bool integrated = gpuDevice?.IsIntegrated ?? gpu.IsIntegrated;

        string title = (isMac ? SystemDescriptions.MacModelName(summary.Motherboard) : SystemDescriptions.BoardName(summary.Motherboard)) ?? summary.ComputerName;
        IReadOnlyList<KeyValueItem> left;
        IReadOnlyList<KeyValueItem> right;
        if (isMac)
        {
            // Apple silicon: one chip carries the GPU and the memory is unified. An Intel Mac has a processor and its own
            // graphics memory (a virtual Mac's Apple-made display adapter doesn't make it Apple silicon).
            bool appleSilicon = cpuName?.StartsWith("Apple", StringComparison.Ordinal) == true;
            bool unified = appleSilicon && gpuDevice?.Vendor == "Apple";
            left = Rows(
                (appleSilicon ? "Chip" : "Processor", cpuName),
                ("Cores", cpu?.GetProperty("Cores") is { } cores ? $"{cores} · {summary.LogicalProcessors} threads" : SystemDescriptions.CoresLine(summary.PhysicalCores, summary.LogicalProcessors)),
                ("Memory", SystemDescriptions.MemoryLine(summary.InstalledMemoryBytes, summary.MemoryModules, formatter) + (unified ? " unified" : string.Empty)));
            right = Rows(
                ("GPU cores", gpuDevice?.GetProperty("GPU cores")),
                ("Graphics memory", unified ? null : gpuDevice?.GetProperty("Memory")),
                ("System", SystemDescriptions.WindowsLine(summary.OperatingSystem, summary.OsVersion)),
                ("Firmware", summary.Bios));
        }
        else
        {
            left = Rows(
                ("Processor", cpuName),
                ("Cores", summary.LogicalProcessors > 0 ? SystemDescriptions.CoresLine(summary.PhysicalCores, summary.LogicalProcessors) : null),
                ("Memory", SystemDescriptions.MemoryLine(summary.InstalledMemoryBytes, summary.MemoryModules, formatter)));
            right = Rows(
                ("Graphics memory", gpuDevice?.GetProperty("VRAM")),
                ("Driver", gpuDevice?.GetProperty("Driver")),
                ("System", SystemDescriptions.WindowsLine(summary.OperatingSystem, summary.OsVersion)),
                ("BIOS", summary.Bios));
        }

        int driveCount = devices.GetByCategory(MetricCategory.Storage)
            .Count(d => d.Id.StartsWith("disk/", StringComparison.Ordinal) && !string.Equals(d.GetProperty("Removable"), "Yes", StringComparison.OrdinalIgnoreCase));

        // Volumes, the system's first.
        string systemLetter = isMac
            ? devices.GetByCategory(MetricCategory.Storage).FirstOrDefault(d => d.Kind == "Volume" && d.GetProperty("Mount point") == "/")?.Id[4..] ?? string.Empty
            : Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\', ':', '/') is { Length: 1 } l ? l.ToUpperInvariant() : "C";
        var volumes = devices.GetByCategory(MetricCategory.Storage)
            .Where(d => d.Kind == "Volume" && d.Id.StartsWith("vol/", StringComparison.Ordinal))
            .Select(d => (Letter: d.Id[4..], d.Name))
            .OrderBy(v => v.Letter == systemLetter ? 0 : 1)
            .ThenBy(v => v.Letter, StringComparer.Ordinal)
            .ToArray();

        return new SystemSpecsInfo(
            title,
            gpuName ?? cpuName ?? string.Empty,
            SystemDescriptions.GpuSizeFor(gpuName, integrated),
            SystemDescriptions.GpuBadge(gpuName),
            left,
            right,
            driveCount,
            volumes);
    }

    /// <summary>"2 drives · 3.8 TB" from the volumes' sizes (a Mac's volumes sharing a disk are counted once).</summary>
    public static string? StorageLine(SystemSpecsInfo info, TelemetryFrame frame, DeviceRegistry devices, bool isMac, UnitFormatter f)
    {
        double total = 0;
        var disks = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (letter, _) in info.Volumes)
        {
            string disk = isMac ? devices.Get($"vol/{letter}")?.GetProperty("Disk number") ?? letter : letter;
            if (disks.Add(disk) && frame.TryGetValue(MetricIds.VolumeTotal(letter), out double size) && size > 0)
            {
                total += size;
            }
        }

        return (info.DriveCount, total) switch
        {
            (0, <= 0) => null,
            (0, _) => f.Bytes(total, 1).ToString(),
            (_, <= 0) => DriveCount(info.DriveCount),
            _ => $"{DriveCount(info.DriveCount)} · {f.Bytes(total, 1)}",
        };
    }

    private static string DriveCount(int count) => count == 1 ? "1 drive" : $"{count} drives";

    private static KeyValueItem[] Rows(params (string Key, string? Value)[] rows) =>
        rows.Where(r => !string.IsNullOrWhiteSpace(r.Value) && r.Value != "—").Select(r => new KeyValueItem(r.Key, r.Value!.Trim())).ToArray();

    private static string? Clean(string? name) =>
        string.IsNullOrWhiteSpace(name)
            ? null
            : string.Join(' ', name.Replace("(R)", string.Empty, StringComparison.OrdinalIgnoreCase).Replace("(TM)", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
