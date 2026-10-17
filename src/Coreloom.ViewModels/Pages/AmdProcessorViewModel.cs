using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Coreloom.Core.Formatting;
using Coreloom.Core.Hardware;
using Coreloom.Core.History;
using Coreloom.Core.Telemetry;
using Coreloom.ViewModels.Infrastructure;

namespace Coreloom.ViewModels.Pages;

/// <summary>A processor control shown read-only: its name, status pill and explanation.</summary>
public sealed class TuningControlViewModel : ObservableObject
{
    private string _status;
    private TuningState _state;
    private string _explanation;
    private string _summary;

    public TuningControlViewModel(TuningControl control)
    {
        Name = control.Name;
        _status = control.Status;
        _state = control.State;
        _explanation = control.Explanation;
        _summary = control.Summary;
    }

    public string Name { get; }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public TuningState State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(StateKey));
            }
        }
    }

    /// <summary>"readonly", "unavailable", "detected", "off" or "unknown", for the pill's colour.</summary>
    public string StateKey => State switch
    {
        TuningState.ReadOnly => "readonly",
        TuningState.Detected => "detected",
        TuningState.NotDetected => "off",
        TuningState.Unknown => "unknown",
        _ => "unavailable",
    };

    /// <summary>The full explanation (shown on hover).</summary>
    public string Explanation
    {
        get => _explanation;
        private set => SetProperty(ref _explanation, value);
    }

    /// <summary>One short line under the name.</summary>
    public string Summary
    {
        get => _summary;
        private set => SetProperty(ref _summary, value);
    }

    /// <summary>Always false: nothing here can be changed from Coreloom.</summary>
    public bool CanChange => false;

    public void Update(TuningControl control)
    {
        Status = control.Status;
        State = control.State;
        Explanation = control.Explanation;
        Summary = control.Summary;
    }
}

/// <summary>Readings the processor doesn't report, grouped by why.</summary>
public sealed record MissingReadings(string Names, string Reason) : StaticRecord;

/// <summary>
/// The AMD card on the Processor page: what the processor is (architecture, codename, type), the AMD-specific readings
/// it reports (PPT, TDC, EDC, SoC power and voltage, fabric and memory clocks, effective clock), the ones it doesn't and
/// why, and the tuning controls — all read-only, each with what Coreloom can tell and where it can be changed instead.
/// </summary>
public sealed class AmdProcessorViewModel : ObservableObject
{
    /// <summary>How far back package power is checked for the PBO inference.</summary>
    public const long PowerWindowMilliseconds = 60 * 60 * 1000;

    private readonly MetricViewModel[] _all;
    private HistoryPoint[] _buffer = new HistoryPoint[256];
    private double[] _watts = new double[256];
    private AmdProcessorProfile _profile = AmdProcessors.Create(string.Empty);
    private string _available = string.Empty;
    private string _missingKey = string.Empty;
    private long _assessedAt = long.MinValue;
    private string? _architecture;
    private string _segment = "Processor";
    private string? _signature;
    private bool _hasMissing;
    private string? _missingText;
    private string? _missingToolTip;

    public AmdProcessorViewModel()
    {
        Package = new MetricViewModel(MetricIds.CpuPower, "Package power", ValueStyle.Power, ColorKeys.Power);
        Ppt = new MetricViewModel(MetricIds.CpuPpt, "PPT", ValueStyle.Power, ColorKeys.Power) { Detail = "Socket power" };
        Tdc = new MetricViewModel(MetricIds.CpuTdc, "TDC", ValueStyle.Auto, ColorKeys.Power) { Detail = "Sustained current" };
        Edc = new MetricViewModel(MetricIds.CpuEdc, "EDC", ValueStyle.Auto, ColorKeys.Power) { Detail = "Peak current" };
        CoresPower = new MetricViewModel(MetricIds.CpuCoresPower, "Cores power", ValueStyle.Power, ColorKeys.Power) { Detail = "All cores together" };
        SocPower = new MetricViewModel(MetricIds.CpuSocPower, "SoC power", ValueStyle.Power, ColorKeys.Power) { Detail = "I/O, memory, graphics" };
        CoreVoltage = new MetricViewModel(MetricIds.CpuVoltage, "Core voltage", ValueStyle.Volts, ColorKeys.Power) { Detail = "VDDCR" };
        SocVoltage = new MetricViewModel(MetricIds.CpuSocVoltage, "SoC voltage", ValueStyle.Volts, ColorKeys.Power) { Detail = "VDDCR SoC" };
        EffectiveClock = new MetricViewModel(MetricIds.CpuClockEffectiveAverage, "Effective clock", ValueStyle.ClockGigahertz, ColorKeys.Cpu) { Detail = "Sleep included" };
        FabricClock = new MetricViewModel(MetricIds.CpuFabricClock, "Fabric clock", ValueStyle.ClockMegahertz, ColorKeys.Cpu) { Detail = "FCLK" };
        UncoreClock = new MetricViewModel(MetricIds.CpuUncoreClock, "Memory controller", ValueStyle.ClockMegahertz, ColorKeys.Cpu) { Detail = "UCLK" };
        MemoryClock = new MetricViewModel(MetricIds.CpuMemoryClock, "Memory clock", ValueStyle.ClockMegahertz, ColorKeys.Memory) { Detail = "MCLK" };
        SocTemperature = new MetricViewModel(MetricIds.CpuSocTemperature, "SoC temperature", ValueStyle.Temperature, ColorKeys.Temperature, Thresholds.CpuTemperature);
        IodTemperature = new MetricViewModel(MetricIds.CpuIodTemperature, "I/O die hot spot", ValueStyle.Temperature, ColorKeys.Temperature, Thresholds.CpuTemperature);
        _all = [Package, Ppt, Tdc, Edc, CoresPower, SocPower, CoreVoltage, SocVoltage, EffectiveClock, FabricClock, UncoreClock, MemoryClock, SocTemperature, IodTemperature];
        foreach (var control in AmdTuning.Controls(_profile, new PowerLimitAssessment(PowerLimitVerdict.NotApplicable, 0, null), sensorsAccessible: false))
        {
            Controls.Add(new TuningControlViewModel(control));
        }
    }

    public MetricViewModel Package { get; }

    public MetricViewModel Ppt { get; }

    public MetricViewModel Tdc { get; }

    public MetricViewModel Edc { get; }

    public MetricViewModel CoresPower { get; }

    public MetricViewModel SocPower { get; }

    public MetricViewModel CoreVoltage { get; }

    public MetricViewModel SocVoltage { get; }

    public MetricViewModel EffectiveClock { get; }

    public MetricViewModel FabricClock { get; }

    public MetricViewModel UncoreClock { get; }

    public MetricViewModel MemoryClock { get; }

    public MetricViewModel SocTemperature { get; }

    public MetricViewModel IodTemperature { get; }

    public AmdProcessorProfile Profile => _profile;

    /// <summary>"Zen 2 · Renoir".</summary>
    public string? Architecture
    {
        get => _architecture;
        private set => SetProperty(ref _architecture, value);
    }

    /// <summary>"Laptop processor".</summary>
    public string Segment
    {
        get => _segment;
        private set
        {
            if (SetProperty(ref _segment, value))
            {
                OnPropertyChanged(nameof(Caption));
            }
        }
    }

    /// <summary>"Family 17h · model 60h · stepping 1".</summary>
    public string? Signature
    {
        get => _signature;
        private set
        {
            if (SetProperty(ref _signature, value))
            {
                OnPropertyChanged(nameof(Caption));
            }
        }
    }

    /// <summary>"Desktop processor · 3D V-Cache" (CPUID details are under Specifications).</summary>
    public string Caption => Segment;

    /// <summary>One line naming what isn't reported ("Not reported here: PPT, TDC, EDC and 9 more"); why is in <see cref="MissingToolTip"/>.</summary>
    public string? MissingText
    {
        get => _missingText;
        private set => SetProperty(ref _missingText, value);
    }

    /// <summary>Each group of missing readings with why.</summary>
    public string? MissingToolTip
    {
        get => _missingToolTip;
        private set => SetProperty(ref _missingToolTip, value);
    }

    /// <summary>The readings this processor reports.</summary>
    public ObservableCollection<MetricViewModel> Readings { get; } = [];

    /// <summary>The readings it doesn't, with why.</summary>
    public ObservableCollection<MissingReadings> Missing { get; } = [];

    public bool HasMissing
    {
        get => _hasMissing;
        private set => SetProperty(ref _hasMissing, value);
    }

    /// <summary>Power limits, PBO, Curve Optimizer, Eco mode, manual tuning: each read-only or unavailable, with why.</summary>
    public ObservableCollection<TuningControlViewModel> Controls { get; } = [];

    public string ReadOnlyNote => "Read-only: Coreloom shows what the processor reports and never changes its limits, clocks or voltages.";

    /// <summary>Takes the processor's name and CPUID details from the device (or the name alone before it arrives).</summary>
    public void SetDevice(DeviceInfo? device, string name)
    {
        int? family = null, model = null;
        if (CpuSignature.TryParse(device?.GetProperty("CPUID"), out int f, out int m, out _))
        {
            family = f;
            model = m;
        }

        _profile = AmdProcessors.Create(device?.Name ?? name, family, model);
        Architecture = _profile.ArchitectureText;
        Segment = _profile.HasVCache ? $"{_profile.SegmentText} · 3D V-Cache" : _profile.SegmentText;
        Signature = device?.GetProperty("CPUID");
        Package.Detail = _profile.StockPptWatts is int stock ? $"Of {stock} W stock" : "Whole processor";
        Ppt.Detail = _profile.StockPptWatts is int ppt ? $"Of {ppt} W stock" : "Socket power";

        _assessedAt = long.MinValue;
    }

    public void Update(TelemetryFrame frame, UnitFormatter formatter, MetricCatalog catalog, HistoryStore history, bool sensorsAccessible)
    {
        foreach (var metric in _all)
        {
            metric.Update(frame, formatter, catalog);
        }

        string available = string.Join('|', _all.Where(m => m.HasValue).Select(m => m.MetricId));
        if (available != _available)
        {
            _available = available;
            Readings.Clear();
            foreach (var metric in _all.Where(m => m.HasValue))
            {
                Readings.Add(metric);
            }
        }

        var missing = _all.Where(m => !m.HasValue)
            .GroupBy(m => catalog.Get(m.MetricId)?.UnavailableReason ?? catalog.GetUnavailableReason(m.MetricId) ?? "Not reported by this processor.")
            .Select(g => new MissingReadings(string.Join(", ", g.Select(m => m.Label)), g.Key))
            .ToList();
        string missingKey = string.Join('|', missing.Select(g => g.Names + "=" + g.Reason));
        if (missingKey != _missingKey)
        {
            _missingKey = missingKey;
            Missing.Clear();
            foreach (var group in missing)
            {
                Missing.Add(group);
            }

            HasMissing = Missing.Count > 0;
            var names = missing.SelectMany(g => g.Names.Split(", ")).ToList();
            MissingText = names.Count == 0 ? null
                : names.Count <= 4 ? $"Not reported here: {string.Join(", ", names)}"
                : $"Not reported here: {string.Join(", ", names.Take(3))} and {names.Count - 3} more";
            MissingToolTip = missing.Count == 0 ? null : string.Join("\n\n", missing.Select(g => $"{g.Names}: {g.Reason}"));
        }

        if (frame.Timestamp - _assessedAt >= 10_000 || _assessedAt == long.MinValue)
        {
            _assessedAt = frame.Timestamp;
            var assessment = AssessPower(history, frame.Timestamp);
            var controls = AmdTuning.Controls(_profile, assessment, sensorsAccessible);
            for (int i = 0; i < controls.Count; i++)
            {
                if (i < Controls.Count && Controls[i].Name == controls[i].Name)
                {
                    Controls[i].Update(controls[i]);
                }
                else
                {
                    Controls.Clear();
                    foreach (var control in controls)
                    {
                        Controls.Add(new TuningControlViewModel(control));
                    }

                    break;
                }
            }
        }
    }

    /// <summary>Package power over the last hour against the stock limit (the SMU's PPT figure when there is one).</summary>
    private PowerLimitAssessment AssessPower(HistoryStore history, long now)
    {
        bool fromPpt = history.Get(MetricIds.CpuPpt) is { Count: > 0 };
        var series = fromPpt ? history.Get(MetricIds.CpuPpt) : history.Get(MetricIds.CpuPower);
        int n = series?.CopyRange(now - PowerWindowMilliseconds, now, ref _buffer) ?? 0;
        if (_watts.Length < n)
        {
            _watts = new double[Math.Max(n, _watts.Length * 2)];
        }

        for (int i = 0; i < n; i++)
        {
            _watts[i] = _buffer[i].V;
        }

        return AmdTuning.Assess(_watts.AsSpan(0, n), _profile, fromPpt);
    }
}
