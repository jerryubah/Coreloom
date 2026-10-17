using System.Collections.ObjectModel;
using Coreloom.Core.Formatting;
using Coreloom.Core.Telemetry;
using Coreloom.ViewModels.Infrastructure;

namespace Coreloom.ViewModels.Pages;

public sealed record MemoryModuleItem(string Slot, string Size, string Type, string Speed, string Part) : StaticRecord;

/// <summary>Memory page: donut, usage history, commit charge, modules and top processes.</summary>
public sealed class MemoryViewModel : PageViewModel
{
    private string _centerText = "—";
    private string _centerCaption = string.Empty;
    private int _deviceVersion = -1;

    public MemoryViewModel(PageContext context)
        : base("memory", "Memory", "Icon.Memory", context, MonitoringSection.Memory)
    {
        Load = new MetricViewModel(MetricIds.MemoryLoad, "Utilization", ValueStyle.Percent, ColorKeys.Memory, Thresholds.Memory);
        Used = new MetricViewModel(MetricIds.MemoryUsed, "In use", ValueStyle.Bytes, ColorKeys.Memory);
        Available = new MetricViewModel(MetricIds.MemoryAvailable, "Available", ValueStyle.Bytes, ColorKeys.Neutral);
        Total = new MetricViewModel(MetricIds.MemoryTotal, "Usable", ValueStyle.Bytes, ColorKeys.Neutral);
        Committed = new MetricViewModel(MetricIds.MemoryCommitUsed, "Committed", ValueStyle.Bytes, ColorKeys.Memory);
        CommitLimit = new MetricViewModel(MetricIds.MemoryCommitLimit, "Commit limit", ValueStyle.Bytes, ColorKeys.Neutral);
        Cache = new MetricViewModel(MetricIds.MemoryCache, "Cached", ValueStyle.Bytes, ColorKeys.Neutral);
        Details = [Used, Available, Committed, Cache];

        InUseSegment = new DonutSegmentViewModel("In use", ColorKeys.Memory);
        AvailableSegment = new DonutSegmentViewModel("Available", ColorKeys.Neutral);
        Segments = [InUseSegment, AvailableSegment];

        UsageChart = new ChartViewModel("Memory utilization", ChartScale.Percent,
            new ChartSeriesViewModel(MetricIds.MemoryLoad, "In use", ColorKeys.Memory, ValueStyle.Percent));
        CommitChart = new ChartViewModel("Committed memory", ChartScale.Auto,
            new ChartSeriesViewModel(MetricIds.MemoryCommitUsed, "Committed", ColorKeys.Memory, ValueStyle.Bytes, fill: false),
            new ChartSeriesViewModel(MetricIds.MemoryUsed, "In use", ColorKeys.Cpu, ValueStyle.Bytes, fill: false));

        Processes = new ProcessListViewModel(Monitor.Processes, ProcessSort.Memory, 6, 16);
        ModuleSensors = new SensorTableViewModel(d => d.Category == MetricCategory.Memory && d.IsRaw && d.Kind is MetricKind.Temperature or MetricKind.Voltage);

        var modules = Monitor.Summary.MemoryModules;
        var labels = Coreloom.Core.Hardware.MemoryModuleNames.Labels(modules);
        for (int i = 0; i < modules.Count; i++)
        {
            var m = modules[i];
            Modules.Add(new MemoryModuleItem(
                labels[i],
                Formatter.Bytes(m.SizeBytes, 0).ToString(),
                m.MemoryType,
                (m.ConfiguredSpeedMtps > 0 ? m.ConfiguredSpeedMtps : m.SpeedMtps) is int s and > 0 ? $"{s} MT/s" : "—",
                m.PartNumber ?? m.Manufacturer ?? "—"));
        }
    }

    public MetricViewModel Load { get; }

    public MetricViewModel Used { get; }

    public MetricViewModel Available { get; }

    public MetricViewModel Total { get; }

    public MetricViewModel Committed { get; }

    public MetricViewModel CommitLimit { get; }

    public MetricViewModel Cache { get; }

    public IReadOnlyList<MetricViewModel> Details { get; }

    public DonutSegmentViewModel InUseSegment { get; }

    public DonutSegmentViewModel AvailableSegment { get; }

    public ObservableCollection<DonutSegmentViewModel> Segments { get; }

    public string CenterText
    {
        get => _centerText;
        private set => SetProperty(ref _centerText, value);
    }

    public string CenterCaption
    {
        get => _centerCaption;
        private set => SetProperty(ref _centerCaption, value);
    }

    public ChartViewModel UsageChart { get; }

    public ChartViewModel CommitChart { get; }

    public ObservableCollection<KeyValueItem> Specifications { get; } = [];

    public ObservableCollection<MemoryModuleItem> Modules { get; } = [];

    public bool HasModules => Modules.Count > 0;

    public ProcessListViewModel Processes { get; }

    /// <summary>The memory's health card (shared, kept current by the shell).</summary>
    public HealthReportViewModel Health => Context.Health.Memory;

    public SensorTableViewModel ModuleSensors { get; }

    protected override void OnActivated()
    {
        _deviceVersion = -1;
        Processes.Reset();
    }

    protected override void OnUpdate(TelemetryFrame frame)
    {
        if (Devices.Version != _deviceVersion)
        {
            _deviceVersion = Devices.Version;
            var device = Devices.Get(MetricIds.MemoryDeviceId);
            Subtitle = device is null ? null : string.Join(" · ", new[] { device.GetProperty("Installed"), device.GetProperty("Modules"), device.GetProperty("Speed") }.Where(s => s is not null));
            var items = ToItems(device);
            if (!Specifications.SequenceEqual(items))
            {
                Specifications.Clear();
                foreach (var item in items)
                {
                    Specifications.Add(item);
                }
            }
        }

        UpdateMetrics(frame, true, Load, Used);
        UpdateMetrics(frame, false, Available, Total, Committed, CommitLimit, Cache);
        var f = Formatter;
        if (Used.HasValue && Available.HasValue)
        {
            InUseSegment.Value = Used.Value;
            InUseSegment.ColorKey = ColorKeys.ForLevel(Load.HasValue ? Load.Level : Thresholds.Memory.Evaluate(Used.Value * 100 / Math.Max(1, Used.Value + Available.Value)));
            AvailableSegment.Value = Available.Value;
            InUseSegment.ValueText = Used.FormatWithUnit(Used.Value);
            AvailableSegment.ValueText = Available.FormatWithUnit(Available.Value);
            CenterText = Load.HasValue ? $"{Load.Number}%" : "—";
            CenterCaption = Total.HasValue ? $"of {f.Bytes(Total.Value, 1)}" : string.Empty;
        }

        Committed.Detail = CommitLimit.HasValue ? $"of {CommitLimit.Number} {CommitLimit.Unit}" : null;
        Load.Detail = Used.HasValue && Total.HasValue ? $"{f.BytesAs(Used.Value, f.ByteUnitIndex(Total.Value), 1).Number} of {f.Bytes(Total.Value, 1)}" : null;

        UpdateCharts(frame, UsageChart, CommitChart);
        Processes.Update(f, Total.HasValue ? Total.Value : 0);
        ModuleSensors.Update(frame, f, Catalog);
    }
}
