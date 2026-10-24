using System.Collections.ObjectModel;
using Coreloom.Core.Formatting;
using Coreloom.Core.Hardware;
using Coreloom.Core.Telemetry;
using Coreloom.ViewModels.Infrastructure;
using Coreloom.ViewModels.Overlay;

namespace Coreloom.ViewModels.Pages;

public sealed record GpuChoice(int Slot, string Name, bool IsIntegrated) : StaticRecord
{
    public string Display => IsIntegrated ? $"{Name} (integrated)" : Name;
}

/// <summary>Graphics page: gauges, detailed metrics, five history charts and a full sensor table.</summary>
public sealed class GpuViewModel : PageViewModel
{
    private int _slot = -1;
    private int _deviceVersion = -1;
    private GpuChoice? _selectedGpu;
    private string _gpuName = "Graphics";
    private HardwareVendor _vendor;
    private string? _clockReasons;
    private bool _suppressSelection;
    private bool _unified;

    public GpuViewModel(PageContext context)
        : base("gpu", "Graphics", "Icon.Gpu", context, MonitoringSection.Gpu)
    {
        Load = new MetricViewModel(MetricIds.GpuLoad(0), "Utilization", ValueStyle.Percent, ColorKeys.Gpu, Thresholds.Load);
        Temperature = new MetricViewModel(MetricIds.GpuTemperature(0), "Temperature", ValueStyle.Temperature, ColorKeys.Temperature, Thresholds.GpuTemperature);
        Power = new MetricViewModel(MetricIds.GpuPower(0), IsMac ? "GPU power" : "Power draw", ValueStyle.Power, ColorKeys.Power, gaugeMaximum: IsMac ? 60 : 600);
        MemoryLoad = new MetricViewModel(MetricIds.GpuMemoryLoad(0), "VRAM", ValueStyle.Percent, ColorKeys.Memory, Thresholds.Memory);

        HotSpot = new MetricViewModel(MetricIds.GpuHotSpotTemperature(0), "Hot spot", ValueStyle.Temperature, ColorKeys.Temperature, new Thresholds(90, 100, 60));
        MemoryJunction = new MetricViewModel(MetricIds.GpuMemoryJunctionTemperature(0), "Memory junction", ValueStyle.Temperature, ColorKeys.Temperature, new Thresholds(90, 100, 60));
        CoreClock = new MetricViewModel(MetricIds.GpuCoreClock(0), "Core clock", ValueStyle.ClockMegahertz, ColorKeys.Gpu);
        MemoryClock = new MetricViewModel(MetricIds.GpuMemoryClock(0), "Memory clock", ValueStyle.ClockMegahertz, ColorKeys.Memory);
        MemoryUsed = new MetricViewModel(MetricIds.GpuMemoryUsed(0), "VRAM used", ValueStyle.Bytes, ColorKeys.Memory);
        MemoryTotal = new MetricViewModel(MetricIds.GpuMemoryTotal(0), "VRAM total", ValueStyle.Bytes, ColorKeys.Memory);
        PowerLimit = new MetricViewModel(MetricIds.GpuPowerLimit(0), "Power limit", ValueStyle.Power, ColorKeys.Power);
        FanRpm = new MetricViewModel(MetricIds.GpuFanRpm(0, 1), "Fan", ValueStyle.Fan, ColorKeys.Fan);
        FanPercent = new MetricViewModel(MetricIds.GpuFanPercent(0, 1), "Fan duty", ValueStyle.Percent, ColorKeys.Fan);
        Voltage = new MetricViewModel(MetricIds.GpuVoltage(0), "Core voltage", ValueStyle.Millivolts, ColorKeys.Power);
        MemoryController = new MetricViewModel(MetricIds.GpuMemoryControllerLoad(0), "Memory controller", ValueStyle.Percent, ColorKeys.Memory);
        PerformanceState = new MetricViewModel(MetricIds.GpuPerformanceState(0), "Performance state", ValueStyle.PerformanceState, ColorKeys.Gpu);
        Encoder = new MetricViewModel(MetricIds.GpuEncoderLoad(0), "Video encoder", ValueStyle.Percent, ColorKeys.Gpu);
        Decoder = new MetricViewModel(MetricIds.GpuDecoderLoad(0), "Video decoder", ValueStyle.Percent, ColorKeys.Gpu);
        PcieGeneration = new MetricViewModel(MetricIds.GpuPcieGeneration(0), "PCIe generation", ValueStyle.Count);
        PcieWidth = new MetricViewModel(MetricIds.GpuPcieWidth(0), "PCIe width", ValueStyle.Count);

        Details =
        [
            CoreClock, MemoryClock, HotSpot, MemoryJunction, FanRpm, FanPercent, Voltage, MemoryController,
            PerformanceState, PowerLimit, Encoder, Decoder,
        ];

        UsageChart = new ChartViewModel("Utilization", ChartScale.Percent,
            new ChartSeriesViewModel(MetricIds.GpuLoad(0), "GPU", ColorKeys.Gpu, ValueStyle.Percent),
            new ChartSeriesViewModel(MetricIds.GpuMemoryControllerLoad(0), "Memory controller", ColorKeys.Memory, ValueStyle.Percent, fill: false));
        TemperatureChart = new ChartViewModel("Temperature", ChartScale.Temperature,
            new ChartSeriesViewModel(MetricIds.GpuTemperature(0), "Core", ColorKeys.Gpu, ValueStyle.Temperature, fill: false),
            new ChartSeriesViewModel(MetricIds.GpuHotSpotTemperature(0), "Hot spot", ColorKeys.Temperature, ValueStyle.Temperature, fill: false),
            new ChartSeriesViewModel(MetricIds.GpuMemoryJunctionTemperature(0), "Memory junction", ColorKeys.Memory, ValueStyle.Temperature, fill: false))
        {
            ReferenceMetricId = MetricIds.GpuSlowdownTemperature(0),
            ReferenceLabel = "Slowdown",
        };
        PowerChart = new ChartViewModel("Power draw", ChartScale.Auto,
            new ChartSeriesViewModel(MetricIds.GpuPower(0), "Board power", ColorKeys.Power, ValueStyle.Power))
        {
            ReferenceMetricId = MetricIds.GpuPowerLimit(0),
            ReferenceLabel = "Power limit",
        };
        ClockChart = new ChartViewModel("Clocks", ChartScale.Auto,
            new ChartSeriesViewModel(MetricIds.GpuCoreClock(0), "Core", ColorKeys.Gpu, ValueStyle.ClockMegahertz, fill: false),
            new ChartSeriesViewModel(MetricIds.GpuMemoryClock(0), "Memory", ColorKeys.Memory, ValueStyle.ClockMegahertz, fill: false));
        MemoryChart = new ChartViewModel("VRAM usage", ChartScale.Auto,
            new ChartSeriesViewModel(MetricIds.GpuMemoryUsed(0), "Used", ColorKeys.Memory, ValueStyle.Bytes));

        Sensors = new SensorTableViewModel(d => d.Category == MetricCategory.Gpu && d.DeviceId == MetricIds.GpuDeviceId(_slot < 0 ? 0 : _slot));
        OverlaySettings = new OverlaySettingsViewModel(Settings, context.Overlay);
    }

    /// <summary>The graphics card's health card.</summary>
    public HealthReportViewModel Health => Context.Health.Gpu;

    /// <summary>The in-game overlay: its switch next to the clock limits, and its settings card.</summary>
    public OverlaySettingsViewModel OverlaySettings { get; }

    public ObservableCollection<GpuChoice> Gpus { get; } = [];

    public GpuChoice? SelectedGpu
    {
        get => _selectedGpu;
        set
        {
            if (SetProperty(ref _selectedGpu, value) && value is not null && !_suppressSelection)
            {
                Settings.Update("Monitoring", s => s.Monitoring.PreferredGpuSlot = value.Slot);
            }
        }
    }

    public bool HasMultipleGpus => Gpus.Count > 1;

    public string GpuName
    {
        get => _gpuName;
        private set => SetProperty(ref _gpuName, value);
    }

    /// <summary>Vendor of the selected GPU (picks the brand mark next to its name).</summary>
    public HardwareVendor Vendor
    {
        get => _vendor;
        private set
        {
            if (SetProperty(ref _vendor, value))
            {
                OnPropertyChanged(nameof(VendorKey));
            }
        }
    }

    /// <summary>"nvidia", "amd" or "intel"; empty when the vendor could not be identified.</summary>
    public string VendorKey => HardwareVendors.Key(_vendor);

    public ObservableCollection<KeyValueItem> Specifications { get; } = [];

    public MetricViewModel Load { get; }

    public MetricViewModel Temperature { get; }

    public MetricViewModel Power { get; }

    public MetricViewModel MemoryLoad { get; }

    public MetricViewModel HotSpot { get; }

    public MetricViewModel MemoryJunction { get; }

    public MetricViewModel CoreClock { get; }

    public MetricViewModel MemoryClock { get; }

    public MetricViewModel MemoryUsed { get; }

    public MetricViewModel MemoryTotal { get; }

    public MetricViewModel PowerLimit { get; }

    public MetricViewModel FanRpm { get; }

    public MetricViewModel FanPercent { get; }

    public MetricViewModel Voltage { get; }

    public MetricViewModel MemoryController { get; }

    public MetricViewModel PerformanceState { get; }

    public MetricViewModel Encoder { get; }

    public MetricViewModel Decoder { get; }

    public MetricViewModel PcieGeneration { get; }

    public MetricViewModel PcieWidth { get; }

    /// <summary>Secondary metrics shown as a tile grid.</summary>
    public IReadOnlyList<MetricViewModel> Details { get; }

    public ChartViewModel UsageChart { get; }

    public ChartViewModel TemperatureChart { get; }

    public ChartViewModel PowerChart { get; }

    public ChartViewModel ClockChart { get; }

    public ChartViewModel MemoryChart { get; }

    public SensorTableViewModel Sensors { get; }

    /// <summary>Why clocks are currently limited (NVML clock event reasons).</summary>
    public string? ClockReasons
    {
        get => _clockReasons;
        private set => SetProperty(ref _clockReasons, value);
    }

    protected override void OnActivated() => _deviceVersion = -1;

    public override void OnSettingsChanged(string section)
    {
        if (section is "Monitoring" or "All")
        {
            _deviceVersion = -1;
        }
    }

    protected override void OnUpdate(TelemetryFrame frame)
    {
        int slot = Context.GpuSlot;
        if (slot != _slot)
        {
            Retarget(slot);
        }

        if (Devices.Version != _deviceVersion)
        {
            _deviceVersion = Devices.Version;
            RefreshDevices();
        }

        UpdateMetrics(frame, true, Load, Temperature, Power, MemoryLoad);
        UpdateMetrics(frame, false, HotSpot, MemoryJunction, CoreClock, MemoryClock, MemoryUsed, MemoryTotal, PowerLimit,
            FanRpm, FanPercent, Voltage, MemoryController, PerformanceState, Encoder, Decoder, PcieGeneration, PcieWidth);

        if (PowerLimit.HasValue && PowerLimit.Value > 0)
        {
            Power.GaugeMaximum = PowerLimit.Value;
            Power.Thresholds = Thresholds.ForLimit(PowerLimit.Value);
            Power.Detail = $"of {PowerLimit.Number} {PowerLimit.Unit} limit";
        }

        MemoryLoad.Detail = MemoryUsed.HasValue && MemoryTotal.HasValue
            ? $"{Formatter.BytesAs(MemoryUsed.Value, Formatter.ByteUnitIndex(MemoryTotal.Value), 1).Number} of {Formatter.Bytes(MemoryTotal.Value, 0)}"
            : null;
        Temperature.Detail = HotSpot.HasValue ? $"Hot spot {HotSpot.Number}{HotSpot.Unit}" : null;
        Load.Detail = PerformanceState.HasValue ? $"State {PerformanceState.Number}" : null;
        FanRpm.Detail = FanPercent.HasValue ? $"{FanPercent.Number}% duty" : null;
        PcieGeneration.Detail = PcieWidth.HasValue ? $"×{PcieWidth.Number}" : null;

        ClockReasons = frame.TryGetValue(MetricIds.GpuClockEventReasons(slot), out double reasons) ? GpuClockReasons.Summarize(reasons) : null;

        UpdateCharts(frame, UsageChart, TemperatureChart, PowerChart, ClockChart, MemoryChart);
        Sensors.Update(frame, Formatter, Catalog);
    }

    private void Retarget(int slot)
    {
        _slot = slot;
        Load.Retarget(MetricIds.GpuLoad(slot));
        Temperature.Retarget(MetricIds.GpuTemperature(slot));
        Power.Retarget(MetricIds.GpuPower(slot));
        MemoryLoad.Retarget(MetricIds.GpuMemoryLoad(slot));
        HotSpot.Retarget(MetricIds.GpuHotSpotTemperature(slot));
        MemoryJunction.Retarget(MetricIds.GpuMemoryJunctionTemperature(slot));
        CoreClock.Retarget(MetricIds.GpuCoreClock(slot));
        MemoryClock.Retarget(MetricIds.GpuMemoryClock(slot));
        MemoryUsed.Retarget(MetricIds.GpuMemoryUsed(slot));
        MemoryTotal.Retarget(MetricIds.GpuMemoryTotal(slot));
        PowerLimit.Retarget(MetricIds.GpuPowerLimit(slot));
        FanRpm.Retarget(MetricIds.GpuFanRpm(slot, 1));
        FanPercent.Retarget(MetricIds.GpuFanPercent(slot, 1));
        Voltage.Retarget(MetricIds.GpuVoltage(slot));
        MemoryController.Retarget(MetricIds.GpuMemoryControllerLoad(slot));
        PerformanceState.Retarget(MetricIds.GpuPerformanceState(slot));
        Encoder.Retarget(MetricIds.GpuEncoderLoad(slot));
        Decoder.Retarget(MetricIds.GpuDecoderLoad(slot));
        PcieGeneration.Retarget(MetricIds.GpuPcieGeneration(slot));
        PcieWidth.Retarget(MetricIds.GpuPcieWidth(slot));

        UsageChart.Series[0].MetricId = MetricIds.GpuLoad(slot);
        UsageChart.Series[1].MetricId = MetricIds.GpuMemoryControllerLoad(slot);
        TemperatureChart.Series[0].MetricId = MetricIds.GpuTemperature(slot);
        TemperatureChart.Series[1].MetricId = MetricIds.GpuHotSpotTemperature(slot);
        TemperatureChart.Series[2].MetricId = MetricIds.GpuMemoryJunctionTemperature(slot);
        TemperatureChart.ReferenceMetricId = MetricIds.GpuSlowdownTemperature(slot);
        PowerChart.Series[0].MetricId = MetricIds.GpuPower(slot);
        PowerChart.ReferenceMetricId = MetricIds.GpuPowerLimit(slot);
        ClockChart.Series[0].MetricId = MetricIds.GpuCoreClock(slot);
        ClockChart.Series[1].MetricId = MetricIds.GpuMemoryClock(slot);
        MemoryChart.Series[0].MetricId = MetricIds.GpuMemoryUsed(slot);
        if (_unified)
        {
            _unified = false;
            ApplyMemoryModel(true);
        }

        Sensors.Invalidate();
        _deviceVersion = -1;
    }

    /// <summary>
    /// An Apple silicon GPU shares the Mac's memory, so it has no VRAM gauge: the fourth gauge shows its renderer's load
    /// (Activity Monitor's other GPU figure) and the memory figures read "in use" rather than "VRAM".
    /// </summary>
    private void ApplyMemoryModel(bool unified)
    {
        if (unified == _unified)
        {
            return;
        }

        _unified = unified;
        string slot = (_slot < 0 ? 0 : _slot).ToString(System.Globalization.CultureInfo.InvariantCulture);
        MemoryLoad.Retarget(unified ? $"gpu/{slot}/load/renderer" : MetricIds.GpuMemoryLoad(_slot < 0 ? 0 : _slot));
        MemoryLoad.Label = unified ? "Renderer" : "VRAM";
        MemoryUsed.Label = unified ? "Memory in use" : "VRAM used";
        MemoryChart.Title = unified ? "GPU memory in use" : "VRAM usage";
        UsageChart.Series[1].MetricId = unified ? $"gpu/{slot}/load/renderer" : MetricIds.GpuMemoryControllerLoad(_slot < 0 ? 0 : _slot);
        UsageChart.Series[1].Label = unified ? "Renderer" : "Memory controller";
    }

    private void RefreshDevices()
    {
        var gpus = Monitor.Gpus().Select(g => new GpuChoice(g.Slot, g.Name, g.IsIntegrated)).ToArray();
        if (!Gpus.SequenceEqual(gpus))
        {
            _suppressSelection = true;
            Gpus.Clear();
            foreach (var g in gpus)
            {
                Gpus.Add(g);
            }

            OnPropertyChanged(nameof(HasMultipleGpus));
            _suppressSelection = false;
        }

        _suppressSelection = true;
        SelectedGpu = Gpus.FirstOrDefault(g => g.Slot == _slot);
        _suppressSelection = false;

        var device = Devices.Get(MetricIds.GpuDeviceId(_slot));
        GpuName = device?.Name ?? SelectedGpu?.Name ?? "No GPU detected";
        Vendor = HardwareVendors.Detect(device?.Vendor, GpuName);
        Subtitle = device is null ? "No supported GPU was detected"
            : IsMac ? string.Join(" · ", new[] { device.Kind, device.GetProperty("GPU cores") is { } cores ? $"{cores} GPU cores" : null, "Metal" }.Where(s => !string.IsNullOrEmpty(s)))
            : string.Join(" · ", new[] { device.Kind, device.GetProperty("Driver") is { } d ? $"Driver {d}" : null, device.GetProperty("VRAM") }.Where(s => !string.IsNullOrEmpty(s)));
        ApplyMemoryModel(IsMac && device?.Vendor == "Apple");
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
}
