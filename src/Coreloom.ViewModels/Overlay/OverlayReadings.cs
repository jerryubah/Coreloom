using Coreloom.Core.Formatting;
using Coreloom.Core.Settings;
using Coreloom.Core.Telemetry;
using Coreloom.ViewModels.Infrastructure;
using Coreloom.ViewModels.Pages;

namespace Coreloom.ViewModels.Overlay;

/// <summary>
/// What each overlay item reads on this computer: which reading it is, whether the computer reports it, and its warning
/// levels. Shared by the in-game overlay and the phone's overlay (Coreloom Connect), so both show the same thing.
/// </summary>
public sealed class OverlayReadings
{
    private readonly PageContext _context;
    private int _fanCatalogVersion = -1;
    private IReadOnlyList<string> _cpuFanIds = [];

    public OverlayReadings(PageContext context)
    {
        _context = context;
    }

    /// <summary>Whether this computer reports the item (frame-rate items aren't readings and aren't covered).</summary>
    public bool IsAvailable(string id, TelemetryFrame frame, int slot) => id switch
    {
        OverlayCatalog.CpuFan => CpuFanRpm(frame) is not null,
        OverlayCatalog.GpuFan => frame.Contains(MetricIds.GpuFanRpm(slot, 1)) || frame.Contains(MetricIds.GpuFanPercent(slot, 1)),
        OverlayCatalog.CpuClock => frame.Contains(MetricIds.CpuClockEffectiveAverage) || frame.Contains(MetricIds.CpuClockDerived),
        _ => MetricIdFor(id, slot) is { } metricId && frame.Contains(metricId),
    };

    /// <summary>The reading behind an item, for the items that have one plain reading.</summary>
    public static string? MetricIdFor(string id, int slot) => id switch
    {
        OverlayCatalog.GpuLoad => MetricIds.GpuLoad(slot),
        OverlayCatalog.GpuTemperature => MetricIds.GpuTemperature(slot),
        OverlayCatalog.GpuHotSpot => MetricIds.GpuHotSpotTemperature(slot),
        OverlayCatalog.GpuPower => MetricIds.GpuPower(slot),
        OverlayCatalog.GpuClock => MetricIds.GpuCoreClock(slot),
        OverlayCatalog.GpuMemoryClock => MetricIds.GpuMemoryClock(slot),
        OverlayCatalog.GpuMemory => MetricIds.GpuMemoryUsed(slot),
        OverlayCatalog.GpuVoltage => MetricIds.GpuVoltage(slot),
        OverlayCatalog.CpuLoad => MetricIds.CpuLoad,
        OverlayCatalog.CpuTemperature => MetricIds.CpuTemperature,
        OverlayCatalog.CpuPower => MetricIds.CpuPower,
        OverlayCatalog.Ram => MetricIds.MemoryUsed,
        _ => null,
    };

    /// <summary>A view model for an item's reading with its warning levels (null for frame rates and the CPU fan).</summary>
    public MetricViewModel? MetricFor(string id, int slot, TelemetryFrame? frame = null)
    {
        var settings = _context.Settings.Current;
        return id switch
        {
            OverlayCatalog.GpuLoad => new(MetricIds.GpuLoad(slot), "GPU", ValueStyle.Percent, thresholds: Thresholds.Load),
            OverlayCatalog.GpuTemperature => new(MetricIds.GpuTemperature(slot), "GPU temperature", ValueStyle.Temperature,
                thresholds: SystemHealthViewModel.AlertThresholds(settings, "gpu-temp", Thresholds.GpuTemperature, 8)),
            OverlayCatalog.GpuHotSpot => new(MetricIds.GpuHotSpotTemperature(slot), "Hot spot", ValueStyle.Temperature, thresholds: new Thresholds(90, 100, 60)),
            OverlayCatalog.GpuPower => new(MetricIds.GpuPower(slot), "GPU power", ValueStyle.Power),
            OverlayCatalog.GpuClock => new(MetricIds.GpuCoreClock(slot), "GPU clock", ValueStyle.ClockMegahertz),
            OverlayCatalog.GpuMemoryClock => new(MetricIds.GpuMemoryClock(slot), "Memory clock", ValueStyle.ClockMegahertz),
            OverlayCatalog.GpuMemory => new(MetricIds.GpuMemoryUsed(slot), "VRAM", ValueStyle.MemoryBytes),
            OverlayCatalog.GpuFan => frame is null || frame.Contains(MetricIds.GpuFanRpm(slot, 1))
                ? new(MetricIds.GpuFanRpm(slot, 1), "GPU fan", ValueStyle.Fan)
                : new(MetricIds.GpuFanPercent(slot, 1), "GPU fan", ValueStyle.Percent),
            OverlayCatalog.GpuVoltage => new(MetricIds.GpuVoltage(slot), "GPU voltage", ValueStyle.Millivolts),
            OverlayCatalog.CpuLoad => new(MetricIds.CpuLoad, "CPU", ValueStyle.Percent, thresholds: Thresholds.Load),
            OverlayCatalog.CpuTemperature => new(MetricIds.CpuTemperature, "CPU temperature", ValueStyle.Temperature,
                thresholds: SystemHealthViewModel.AlertThresholds(settings, "cpu-temp", Thresholds.CpuTemperature, 10)),
            OverlayCatalog.CpuPower => new(MetricIds.CpuPower, "CPU power", ValueStyle.Power),
            OverlayCatalog.CpuClock => frame is null || frame.Contains(MetricIds.CpuClockEffectiveAverage)
                ? new(MetricIds.CpuClockEffectiveAverage, "CPU clock", ValueStyle.ClockGigahertz)
                : new(MetricIds.CpuClockDerived, "CPU clock", ValueStyle.ClockGigahertz),
            OverlayCatalog.Ram => new(MetricIds.MemoryUsed, "RAM", ValueStyle.MemoryBytes),
            _ => null,
        };
    }

    /// <summary>The processor fan's speed: the fastest of the board fans named for the CPU, AIO or radiator.</summary>
    public double? CpuFanRpm(TelemetryFrame frame)
    {
        var catalog = _context.Monitor.Engine.Catalog;
        if (catalog.Version != _fanCatalogVersion)
        {
            _fanCatalogVersion = catalog.Version;
            _cpuFanIds = PcFans.Classify(catalog
                .Where(d => d.Kind == MetricKind.Fan && d.Id.StartsWith("board/fan/", StringComparison.Ordinal))
                .Select(d => new PcFanSensor(d.Id, d.Name))).Cpu.Select(s => s.Id).ToArray();
        }

        double? rpm = null;
        foreach (string id in _cpuFanIds)
        {
            if (frame.TryGetValue(id, out double value))
            {
                rpm = Math.Max(rpm ?? 0, value);
            }
        }

        return rpm;
    }

    /// <summary>The CPU fan's speed formatted as the overlay shows it.</summary>
    public FormattedValue CpuFan(TelemetryFrame frame) =>
        CpuFanRpm(frame) is double rpm ? ValueStyles.Format(ValueStyle.Fan, rpm, _context.Settings.Formatter) : FormattedValue.Unavailable;
}
