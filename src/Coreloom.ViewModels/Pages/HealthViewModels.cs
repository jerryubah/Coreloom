using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Coreloom.Core.Formatting;
using Coreloom.Core.Health;
using Coreloom.Core.Settings;
using Coreloom.Core.Telemetry;

namespace Coreloom.ViewModels.Pages;

/// <summary>One line of a health card, updated in place so the card doesn't redraw every second.</summary>
public sealed class HealthCheckViewModel : ObservableObject
{
    private CheckState _state;
    private string _title = string.Empty;
    private string _detail = string.Empty;

    public HealthCheckViewModel(string id)
    {
        Id = id;
    }

    public string Id { get; }

    public CheckState State
    {
        get => _state;
        set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(StateKey));
            }
        }
    }

    /// <summary>"Ok", "Notice", "Problem" or "Unknown" (templates pick the icon and color from it).</summary>
    public string StateKey => _state.ToString();

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public string Detail
    {
        get => _detail;
        set => SetProperty(ref _detail, value);
    }
}

/// <summary>The health of one part of the PC as a card: status ("Excellent", "Good"…), summary and checks.</summary>
public sealed class HealthReportViewModel : ObservableObject
{
    private HealthStatus _status;
    private string _summary = HealthReport.Checking.Summary;

    public HealthReportViewModel(string name, string iconKey)
    {
        Name = name;
        IconKey = iconKey;
    }

    /// <summary>"Graphics card", "Processor", "Memory", "Storage".</summary>
    public string Name { get; }

    public string IconKey { get; }

    public HealthStatus Status
    {
        get => _status;
        private set
        {
            if (SetProperty(ref _status, value))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StatusKey));
            }
        }
    }

    public string StatusText => StatusLabel(_status);

    /// <summary>"Checking", "Excellent", "Good" or "Attention" (templates pick colors from it).</summary>
    public string StatusKey => _status.ToString();

    public string Summary
    {
        get => _summary;
        private set => SetProperty(ref _summary, value);
    }

    public ObservableCollection<HealthCheckViewModel> Checks { get; } = [];

    public static string StatusLabel(HealthStatus status) => status switch
    {
        HealthStatus.Excellent => "Excellent",
        HealthStatus.Good => "Good",
        HealthStatus.Attention => "Needs attention",
        _ => "Checking…",
    };

    /// <summary>Takes a new report; checks with the same id are updated rather than replaced. Problems come first.</summary>
    public void Apply(HealthReport report)
    {
        Status = report.Status;
        Summary = report.Summary;
        var ordered = report.Checks.OrderBy(c => c.State switch
        {
            CheckState.Problem => 0,
            CheckState.Notice => 1,
            CheckState.Unknown => 2,
            _ => 3,
        }).ToList();

        if (!Checks.Select(c => c.Id).SequenceEqual(ordered.Select(c => c.Id)))
        {
            var existing = Checks.ToDictionary(c => c.Id, StringComparer.Ordinal);
            Checks.Clear();
            foreach (var check in ordered)
            {
                Checks.Add(existing.GetValueOrDefault(check.Id) ?? new HealthCheckViewModel(check.Id));
            }
        }

        for (int i = 0; i < ordered.Count; i++)
        {
            Checks[i].State = ordered[i].State;
            Checks[i].Title = ordered[i].Title;
            Checks[i].Detail = ordered[i].Detail;
        }
    }
}

/// <summary>
/// Health of the graphics card, the processor, memory and storage, from the last minute of readings. One shared
/// instance (see <see cref="PageContext.Health"/>) is fed every frame by the shell, also while the window is hidden, so
/// the cards are current the moment a page opens.
/// </summary>
public sealed class SystemHealthViewModel : ObservableObject
{
    private readonly PageContext _context;
    private readonly HealthMonitor _monitor = new();
    private int _fanCatalogVersion = -1;
    private int _gpuSlot = int.MinValue;
    private IReadOnlyList<PcFanSensor> _cpuFans = [];
    private HealthStatus _overall;
    private (int Rated, int Configured)? _moduleSpeeds;

    public SystemHealthViewModel(PageContext context)
    {
        _context = context;
    }

    public HealthReportViewModel Gpu { get; } = new("Graphics card", "Icon.Gpu");

    public HealthReportViewModel Cpu { get; } = new("Processor", "Icon.Cpu");

    public HealthReportViewModel Memory { get; } = new("Memory", "Icon.Memory");

    public HealthReportViewModel Storage { get; } = new("Storage", "Icon.Storage");

    /// <summary>The four reports, for the Overview's health card.</summary>
    public IReadOnlyList<HealthReportViewModel> Parts => [Gpu, Cpu, Memory, Storage];

    /// <summary>The worst of the four.</summary>
    public HealthStatus Overall
    {
        get => _overall;
        private set
        {
            if (SetProperty(ref _overall, value))
            {
                OnPropertyChanged(nameof(OverallText));
                OnPropertyChanged(nameof(OverallKey));
                OnPropertyChanged(nameof(OverallSummary));
            }
        }
    }

    public string OverallText => HealthReportViewModel.StatusLabel(_overall);

    public string OverallKey => _overall.ToString();

    public string OverallSummary => _overall switch
    {
        HealthStatus.Excellent => "Everything looks great",
        HealthStatus.Good => "Running well, with something to keep an eye on",
        HealthStatus.Attention => "Something needs your attention",
        _ => $"Checking your {Coreloom.Core.HostPlatforms.Computer(_context.Platform)}…",
    };

    public void Update(TelemetryFrame frame)
    {
        var settings = _context.Settings;
        int slot = _context.GpuSlot;
        if (slot != _gpuSlot)
        {
            _gpuSlot = slot;
            _monitor.Clear(); // another card: start over
        }

        _monitor.Record(frame, slot, CpuFanRpm(frame));
        var f = settings.Formatter;
        var catalog = _context.Monitor.Engine.Catalog;
        string? cpuTempReason = catalog.Get(MetricIds.CpuTemperature)?.UnavailableReason ?? catalog.GetUnavailableReason(MetricIds.CpuTemperature);

        Gpu.Apply(_monitor.EvaluateGpu(f, AlertThresholds(settings.Current, "gpu-temp", Thresholds.GpuTemperature, 8)));
        Cpu.Apply(_monitor.EvaluateCpu(f, AlertThresholds(settings.Current, "cpu-temp", Thresholds.CpuTemperature, 10), cpuTempReason));
        var (rated, configured) = ModuleSpeeds();
        Memory.Apply(_monitor.EvaluateMemory(f, rated, configured));
        Storage.Apply(_monitor.EvaluateStorage(f));
        Overall = HealthReport.Worst(Parts.Select(p => new HealthReport(p.Status, string.Empty, [])));
    }

    /// <summary>Warning levels follow the user's temperature alerts (as on the Overview), or the defaults.</summary>
    internal static Thresholds AlertThresholds(AppSettings settings, string ruleId, Thresholds fallback, double elevatedOffset)
    {
        var rule = settings.Alerts.Rules.FirstOrDefault(r => r.Id == ruleId);
        return rule is { Enabled: true } ? new Thresholds(rule.Threshold - elevatedOffset, rule.Threshold, fallback.Cool) : fallback;
    }

    /// <summary>The slowest module's rated and configured speed (MT/s), or zeros when the modules aren't known.</summary>
    private (int Rated, int Configured) ModuleSpeeds()
    {
        if (_moduleSpeeds is { } known)
        {
            return known; // the modules don't change while the app runs
        }

        var modules = _context.Monitor.Summary.MemoryModules;
        int configured = modules.Select(m => m.ConfiguredSpeedMtps).Where(s => s > 0).DefaultIfEmpty(0).Min();
        int rated = modules.Select(m => m.SpeedMtps).Where(s => s > 0).DefaultIfEmpty(0).Min();
        _moduleSpeeds = (rated, configured);
        return (rated, configured);
    }

    /// <summary>The processor fan's speed (the fastest fan the board names as CPU, AIO or radiator), when there is one.</summary>
    private double? CpuFanRpm(TelemetryFrame frame)
    {
        var catalog = _context.Monitor.Engine.Catalog;
        if (catalog.Version != _fanCatalogVersion)
        {
            _fanCatalogVersion = catalog.Version;
            _cpuFans = PcFans.Classify(catalog
                .Where(d => d.Kind == MetricKind.Fan && d.Id.StartsWith("board/fan/", StringComparison.Ordinal))
                .Select(d => new PcFanSensor(d.Id, d.Name))).Cpu;
        }

        double? rpm = null;
        foreach (var fan in _cpuFans)
        {
            if (frame.TryGetValue(fan.Id, out double value))
            {
                rpm = Math.Max(rpm ?? 0, value);
            }
        }

        return rpm;
    }
}

/// <summary>The Overview's "System health" card (a dashboard tile): the three reports side by side.</summary>
public sealed class HealthPanelViewModel : DashboardPanelViewModel
{
    public HealthPanelViewModel(SystemHealthViewModel health)
        : base(DashboardCatalog.Health, "System health")
    {
        Health = health;
    }

    public SystemHealthViewModel Health { get; }
}
