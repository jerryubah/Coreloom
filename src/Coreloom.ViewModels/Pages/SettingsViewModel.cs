using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Coreloom.Core;
using Coreloom.Core.Alerts;
using Coreloom.Core.Diagnostics;
using Coreloom.Core.Formatting;
using Coreloom.Core.History;
using Coreloom.Core.Settings;
using Coreloom.Core.Telemetry;
using Coreloom.ViewModels.Infrastructure;
using Coreloom.ViewModels.Services;

namespace Coreloom.ViewModels.Pages;

public sealed record Option<T>(T Value, string Label) : StaticRecord;

public sealed record AccentOption(string Name, string Hex) : StaticRecord;

/// <summary>Editable alert rule. Thresholds are shown in display units (°F when selected) and stored in base units.</summary>
public sealed class AlertRuleViewModel : ObservableObject
{
    private readonly SettingsViewModel _owner;
    private readonly AlertRule _rule;

    public AlertRuleViewModel(SettingsViewModel owner, AlertRule rule, MetricKind kind, string metricName)
    {
        _owner = owner;
        _rule = rule;
        Kind = kind;
        MetricName = metricName;
        RemoveCommand = new RelayCommand(() => _owner.RemoveRule(this));
    }

    public AlertRule Rule => _rule;

    public MetricKind Kind { get; }

    public string MetricName { get; }

    public string Name
    {
        get => _rule.Name;
        set
        {
            if (_rule.Name != value)
            {
                _rule.Name = value;
                OnPropertyChanged();
                _owner.SaveAlerts();
            }
        }
    }

    public bool Enabled
    {
        get => _rule.Enabled;
        set
        {
            if (_rule.Enabled != value)
            {
                _rule.Enabled = value;
                OnPropertyChanged();
                _owner.SaveAlerts();
            }
        }
    }

    public bool IsAbove
    {
        get => _rule.Comparison == AlertComparison.Above;
        set
        {
            var comparison = value ? AlertComparison.Above : AlertComparison.Below;
            if (_rule.Comparison != comparison)
            {
                _rule.Comparison = comparison;
                OnPropertyChanged();
                _owner.SaveAlerts();
            }
        }
    }

    /// <summary>0 = notify when the value rises above the threshold, 1 = when it falls below.</summary>
    public int ComparisonIndex
    {
        get => IsAbove ? 0 : 1;
        set
        {
            IsAbove = value == 0;
            OnPropertyChanged();
        }
    }

    /// <summary>Threshold in display units.</summary>
    public double Threshold
    {
        get => Kind == MetricKind.Temperature ? Math.Round(_owner.CurrentFormatter.ConvertTemperature(_rule.Threshold), 1) : _rule.Threshold;
        set
        {
            double stored = Kind == MetricKind.Temperature ? Math.Round(_owner.CurrentFormatter.ToCelsius(value), 2) : value;
            if (Math.Abs(_rule.Threshold - stored) > 1e-6)
            {
                _rule.Threshold = stored;
                OnPropertyChanged();
                _owner.SaveAlerts();
            }
        }
    }

    public string Unit => Kind switch
    {
        MetricKind.Temperature => _owner.CurrentFormatter.TemperatureSymbol,
        MetricKind.Load or MetricKind.Level or MetricKind.Control => "%",
        MetricKind.Power => "W",
        MetricKind.Clock => "MHz",
        MetricKind.Fan => "RPM",
        _ => string.Empty,
    };

    public int SustainSeconds
    {
        get => _rule.SustainSeconds;
        set
        {
            int clamped = Math.Clamp(value, 0, 3600);
            if (_rule.SustainSeconds != clamped)
            {
                _rule.SustainSeconds = clamped;
                OnPropertyChanged();
                _owner.SaveAlerts();
            }
        }
    }

    public IRelayCommand RemoveCommand { get; }

    public void RefreshUnits()
    {
        OnPropertyChanged(nameof(Threshold));
        OnPropertyChanged(nameof(Unit));
    }
}

public sealed record AlertMetricOption(string Id, string Name, MetricKind Kind) : StaticRecord;

public sealed class ProviderStatusItem : ObservableObject
{
    private string _state = string.Empty;
    private string? _message;
    private string _timing = string.Empty;
    private bool _isHealthy;

    public ProviderStatusItem(string name)
    {
        Name = name;
    }

    public string Name { get; }

    public string State
    {
        get => _state;
        set => SetProperty(ref _state, value);
    }

    public string? Message
    {
        get => _message;
        set => SetProperty(ref _message, value);
    }

    public string Timing
    {
        get => _timing;
        set => SetProperty(ref _timing, value);
    }

    public bool IsHealthy
    {
        get => _isHealthy;
        set => SetProperty(ref _isHealthy, value);
    }
}

public sealed class SettingsDependencies
{
    public required IThemeService Theme { get; init; }

    public required IStartupService Startup { get; init; }

    public required IElevationService Elevation { get; init; }

    public required IShellService Shell { get; init; }

    public required IAppLifetime Lifetime { get; init; }

    public required IDiagnosticsService Diagnostics { get; init; }

    public required UpdatesViewModel Updates { get; init; }

    public required AppVersionInfo App { get; init; }

    public IOverheadService? Overhead { get; init; }

    /// <summary>Coreloom Connect, when this app has it (Settings › Connect).</summary>
    public Connect.ConnectHost? Connect { get; init; }

    /// <summary>macOS: the readings shown in the menu bar itself (Settings › Menu bar).</summary>
    public MenuBar.MenuBarModulesViewModel? MenuBar { get; init; }
}

/// <summary>All user settings, grouped into sections. Every change is applied immediately and persisted.</summary>
public sealed class SettingsViewModel : PageViewModel
{
    private const string PawnIoUrl = "https://pawnio.eu";
    private readonly SettingsDependencies _deps;
    private bool _restartRequired;
    private string? _startupMessage;
    private readonly Dismissal _startupDismissal = new();
    private bool _isApplyingStartup;
    private string _overheadText = "—";
    private DateTime _lastProviderRefresh = DateTime.MinValue;
    private string? _selectedSection = "Appearance";
    private string? _customAccent;

    public SettingsViewModel(PageContext context, SettingsDependencies deps)
        : base("settings", "Settings", "Icon.Settings", context)
    {
        _deps = deps;
        Logs = new LogViewerViewModel(deps.Diagnostics, deps.Shell);
        Connect = deps.Connect is { } connect ? new Connect.ConnectViewModel(connect, context.Settings, deps.Shell, IsMac) : null;
        Subtitle = Connect is null ? "Appearance, monitoring, dashboard, alerts and updates" : "Appearance, monitoring, alerts, your phone and updates";
        MenuBar = deps.MenuBar;
        var sections = new List<string> { "Appearance", "Monitoring", "Desktop" };
        if (MenuBar is not null)
        {
            sections.Add("Menu bar");
        }

        sections.Add("Alerts");
        if (Connect is not null)
        {
            sections.Add("Connect");
        }

        sections.AddRange(["Diagnostics", "Logs", "About", "Updates"]);
        Sections = sections;

        ThemeOptions = [new(ThemePreference.Dark, "Dark"), new(ThemePreference.Light, "Light"), new(ThemePreference.System, IsMac ? "Use macOS setting" : "Use Windows setting")];
        AccentOptions = AccentPalette.Presets.Select(p => new AccentOption(p.Name, p.Hex)).ToArray();
        PollingOptions = [new(250, "0.25 s"), new(500, "0.5 s"), new(1000, "1 s (recommended)"), new(2000, "2 s (lighter)"), new(5000, "5 s (lightest)")];
        WindowOptions = Enum.GetValues<HistoryWindow>().Select(w => new Option<HistoryWindow>(w, w.ToLongLabel())).ToArray();
        TemperatureOptions = [new(TemperatureUnit.Celsius, "Celsius (°C)"), new(TemperatureUnit.Fahrenheit, "Fahrenheit (°F)")];
        DataUnitOptions = IsMac
            ? [new(DataUnitStyle.Decimal, "Like macOS — storage in decimal units, memory in binary units"), new(DataUnitStyle.Binary, "Binary — 1 GB = 1024 MB everywhere")]
            : [new(DataUnitStyle.Binary, "Binary — 1 GB = 1024 MB (Windows)"), new(DataUnitStyle.Decimal, "Decimal — 1 GB = 1000 MB")];
        NetworkUnitOptions = [new(NetworkUnitStyle.BitsPerSecond, "Bits per second (Mbps)"), new(NetworkUnitStyle.BytesPerSecond, "Bytes per second (MB/s)")];
        UpdateFrequencyOptions = [new(UpdateCheckFrequency.OnStartup, "Every launch"), new(UpdateCheckFrequency.Daily, "Daily"), new(UpdateCheckFrequency.Weekly, "Weekly"), new(UpdateCheckFrequency.Never, "Never (manual only)")];
        CooldownOptions = [new(5, "5 minutes"), new(10, "10 minutes"), new(30, "30 minutes"), new(60, "1 hour")];

        SetAccentCommand = new RelayCommand<string>(hex =>
        {
            if (AccentPalette.IsValidHex(hex))
            {
                Change("Appearance", s => s.Appearance.Accent = hex!);
                _customAccent = null;
                OnPropertyChanged(nameof(Accent));
                OnPropertyChanged(nameof(CustomAccent));
            }
        });
        AddRuleCommand = new RelayCommand<AlertMetricOption>(AddRule);
        ExportReportCommand = new RelayCommand(ExportReport);
        OpenLogsCommand = new RelayCommand(() => _deps.Shell.OpenFolder(_deps.Diagnostics.LogDirectory));
        ViewLogsCommand = new RelayCommand(() => ShowLogs());
        RescanCommand = new RelayCommand(() => Monitor.Engine.RequestReinitialize());
        OpenPawnIoCommand = new RelayCommand(() => _deps.Shell.OpenUrl(PawnIoUrl));
        RestartCommand = new RelayCommand(() => _deps.Lifetime.Restart());
        OpenRepositoryCommand = new RelayCommand(() => _deps.Shell.OpenUrl(_deps.Updates.RepositoryUrl ?? "https://github.com"), () => _deps.Updates.RepositoryUrl is not null);
        SelectSectionCommand = new RelayCommand<string>(s => SelectedSection = s);
        OpenNoticesCommand = new RelayCommand(() => _deps.Shell.OpenFile(NoticesFile(AppContext.BaseDirectory)));

        LoadRules();
        deps.Updates.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(UpdatesViewModel.ShowBadge))
            {
                OnPropertyChanged(nameof(HasNotice));
            }
        };
    }

    /// <summary>A new version of Coreloom is available, downloading or ready: a red dot on Settings in the sidebar.</summary>
    public override bool HasNotice => _deps.Updates.ShowBadge;

    // ---- Navigation within settings ----------------------------------------------------------

    public IReadOnlyList<string> Sections { get; }

    public string? SelectedSection
    {
        get => _selectedSection;
        set
        {
            if (SetProperty(ref _selectedSection, value))
            {
                if (value == "Logs")
                {
                    _ = Logs.LoadAsync(); // read fresh every time the viewer is opened
                }

                UpdateConnectVisibility();
            }
        }
    }

    /// <summary>Settings › Connect, when this app has Coreloom Connect.</summary>
    public Connect.ConnectViewModel? Connect { get; }

    /// <summary>macOS: Settings › Menu bar, the modules that can show their readings in the menu bar.</summary>
    public MenuBar.MenuBarModulesViewModel? MenuBar { get; }

    /// <summary>Pairing is open only while Settings › Connect is on screen.</summary>
    private void UpdateConnectVisibility() => Connect?.SetVisible(IsActive && SelectedSection == "Connect");

    /// <summary>The log viewer (Settings › Logs).</summary>
    public LogViewerViewModel Logs { get; }

    /// <summary>Opens the log viewer, optionally filtered (from another page).</summary>
    public void ShowLogs(string? category = null, string? search = null)
    {
        Logs.Apply(category, search);
        if (SelectedSection == "Logs")
        {
            _ = Logs.LoadAsync();
        }
        else
        {
            SelectedSection = "Logs";
        }
    }

    public IRelayCommand<string> SelectSectionCommand { get; }

    internal UnitFormatter CurrentFormatter => Formatter;

    public UpdatesViewModel Updates => _deps.Updates;

    // ---- Appearance ------------------------------------------------------------------------------

    public IReadOnlyList<Option<ThemePreference>> ThemeOptions { get; }

    public ThemePreference Theme
    {
        get => Settings.Current.Appearance.Theme;
        set => Change("Appearance", s => s.Appearance.Theme = value);
    }

    public IReadOnlyList<AccentOption> AccentOptions { get; }

    public string Accent => Settings.Current.Appearance.Accent;

    public IRelayCommand<string> SetAccentCommand { get; }

    /// <summary>Free-form accent entry (#RRGGBB). Invalid input is kept in the box but not applied.</summary>
    public string CustomAccent
    {
        get => _customAccent ?? Accent;
        set
        {
            string text = (value ?? string.Empty).Trim();
            if (text.Length == 6 && !text.StartsWith('#'))
            {
                text = "#" + text;
            }

            _customAccent = text;
            OnPropertyChanged();
            if (AccentPalette.IsValidHex(text))
            {
                SetAccentCommand.Execute(text.ToUpperInvariant());
            }
        }
    }

    public bool ReducedMotion
    {
        get => Settings.Current.Appearance.ReducedMotion;
        set => Change("Appearance", s => s.Appearance.ReducedMotion = value);
    }

    public bool SmoothChartScrolling
    {
        get => Settings.Current.Appearance.SmoothChartScrolling;
        set => Change("Appearance", s => s.Appearance.SmoothChartScrolling = value);
    }

    public bool AnimateValues
    {
        get => Settings.Current.Appearance.AnimateValues;
        set => Change("Appearance", s => s.Appearance.AnimateValues = value);
    }

    public bool UseMica
    {
        get => Settings.Current.Appearance.UseMica;
        set => Change("Appearance", s => s.Appearance.UseMica = value);
    }

    public bool IsMicaSupported => _deps.Theme.IsMicaSupported;

    // ---- Monitoring ------------------------------------------------------------------------------

    public IReadOnlyList<Option<int>> PollingOptions { get; }

    public int PollingIntervalMs
    {
        get => Settings.Current.Monitoring.PollingIntervalMs;
        set => Change("Monitoring", s => s.Monitoring.PollingIntervalMs = value);
    }

    /// <summary>Read sensors less often (and at lower priority) while a game is in front.</summary>
    public bool LighterWhileGaming
    {
        get => Settings.Current.Monitoring.LighterWhileGaming;
        set => Change("Monitoring", s => s.Monitoring.LighterWhileGaming = value);
    }

    public IReadOnlyList<Option<int>> GamingPollingOptions { get; } = [new(1000, "Every 1 s"), new(2000, "Every 2 s (recommended)"), new(5000, "Every 5 s")];

    public int GamingIntervalMs
    {
        get => Settings.Current.Monitoring.GamingIntervalMs;
        set => Change("Monitoring", s => s.Monitoring.GamingIntervalMs = value);
    }

    public IReadOnlyList<Option<HistoryWindow>> WindowOptions { get; }

    public HistoryWindow DefaultWindow
    {
        get => Settings.Current.Monitoring.DefaultWindow;
        set => Change("Monitoring", s => s.Monitoring.DefaultWindow = value);
    }

    public IReadOnlyList<Option<TemperatureUnit>> TemperatureOptions { get; }

    public TemperatureUnit TemperatureUnit
    {
        get => Settings.Current.Monitoring.TemperatureUnit;
        set
        {
            Change("Monitoring", s => s.Monitoring.TemperatureUnit = value);
            foreach (var rule in Rules)
            {
                rule.RefreshUnits();
            }
        }
    }

    public IReadOnlyList<Option<DataUnitStyle>> DataUnitOptions { get; }

    public DataUnitStyle DataUnits
    {
        get => Settings.Current.Monitoring.DataUnits;
        set => Change("Monitoring", s => s.Monitoring.DataUnits = value);
    }

    public IReadOnlyList<Option<NetworkUnitStyle>> NetworkUnitOptions { get; }

    public NetworkUnitStyle NetworkUnits
    {
        get => Settings.Current.Monitoring.NetworkUnits;
        set => Change("Monitoring", s => s.Monitoring.NetworkUnits = value);
    }

    public bool GpuEnabled
    {
        get => Settings.Current.Monitoring.GpuEnabled;
        set => Change("Sections", s => s.Monitoring.GpuEnabled = value);
    }

    public bool CpuEnabled
    {
        get => Settings.Current.Monitoring.CpuEnabled;
        set => Change("Sections", s => s.Monitoring.CpuEnabled = value);
    }

    public bool MemoryEnabled
    {
        get => Settings.Current.Monitoring.MemoryEnabled;
        set => Change("Sections", s => s.Monitoring.MemoryEnabled = value);
    }

    public bool StorageEnabled
    {
        get => Settings.Current.Monitoring.StorageEnabled;
        set => Change("Sections", s => s.Monitoring.StorageEnabled = value);
    }

    public bool NetworkEnabled
    {
        get => Settings.Current.Monitoring.NetworkEnabled;
        set => Change("Sections", s => s.Monitoring.NetworkEnabled = value);
    }

    public bool UseLibreHardwareMonitor
    {
        get => Settings.Current.Monitoring.UseLibreHardwareMonitor;
        set
        {
            Change("Monitoring", s => s.Monitoring.UseLibreHardwareMonitor = value);
            RestartRequired = true;
        }
    }

    public bool ScanUsbControllers
    {
        get => Settings.Current.Monitoring.ScanUsbControllers;
        set
        {
            Change("Monitoring", s => s.Monitoring.ScanUsbControllers = value);
            RestartRequired = true;
        }
    }

    public bool ShowPerThreadGraphs
    {
        get => Settings.Current.Monitoring.ShowPerThreadGraphs;
        set => Change("Monitoring", s => s.Monitoring.ShowPerThreadGraphs = value);
    }

    public bool LibreHardwareMonitorIncluded => Monitor.LibreHardwareMonitorIncluded;

    public bool RestartRequired
    {
        get => _restartRequired;
        private set => SetProperty(ref _restartRequired, value);
    }

    public IRelayCommand RestartCommand { get; }

    // ---- Desktop ---------------------------------------------------------------------------------

    public bool StartWithWindows
    {
        get => Settings.Current.Desktop.StartWithWindows;
        set
        {
            Change("Desktop", s => s.Desktop.StartWithWindows = value);
            _ = ApplyStartupAsync();
        }
    }

    public bool LaunchMinimized
    {
        get => Settings.Current.Desktop.LaunchMinimized;
        set
        {
            Change("Desktop", s => s.Desktop.LaunchMinimized = value);
            _ = ApplyStartupAsync();
        }
    }

    public bool MinimizeToTray
    {
        get => Settings.Current.Desktop.MinimizeToTray;
        set => Change("Desktop", s => s.Desktop.MinimizeToTray = value);
    }

    public bool CloseToTray
    {
        get => Settings.Current.Desktop.CloseToTray;
        set => Change("Desktop", s => s.Desktop.CloseToTray = value);
    }

    /// <summary>The live readings in the tray icon's tooltip (Windows) or at the top of the menu bar item's menu (macOS).</summary>
    public bool ShowTrayReadings
    {
        get => Settings.Current.Desktop.ShowTrayTooltipStats;
        set => Change("Desktop", s => s.Desktop.ShowTrayTooltipStats = value);
    }

    /// <summary>macOS: while the window is closed and Coreloom runs in the menu bar, its Dock icon is hidden too.</summary>
    public bool HideDockIconWhenClosed
    {
        get => Settings.Current.Desktop.HideDockIconWhenClosed;
        set => Change("Desktop", s => s.Desktop.HideDockIconWhenClosed = value);
    }

    /// <summary>How Coreloom starts with the computer, or (for five seconds) why that couldn't be set up.</summary>
    public string? StartupMessage
    {
        get => _startupMessage;
        private set => SetProperty(ref _startupMessage, value);
    }

    private string? StartupExplanation() => !Settings.Current.Desktop.StartWithWindows ? null
        : IsMac ? "Opens at login (listed in System Settings › General › Login Items)."
        : "Starts at sign-in with administrator rights through Task Scheduler, so there is no UAC prompt.";

    private void ShowStartupProblem(string problem)
    {
        StartupMessage = problem;
        _startupDismissal.Update(true, () =>
        {
            if (StartupMessage == problem)
            {
                StartupMessage = StartupExplanation();
            }
        });
    }

    /// <summary>
    /// Synchronizes the sign-in task with the settings. The app is always elevated, so this never shows a prompt
    /// and is safe to run at launch (it also refreshes the task if the executable moved).
    /// </summary>
    public async Task ApplyStartupAsync()
    {
        if (_isApplyingStartup)
        {
            return;
        }

        _isApplyingStartup = true;
        try
        {
            var desktop = Settings.Current.Desktop;
            bool ok = await _deps.Startup.SetStartAtLogonAsync(desktop.StartWithWindows, desktop.LaunchMinimized);
            if (!ok)
            {
                ShowStartupProblem(IsMac
                    ? "Could not update the login item. Check System Settings › General › Login Items, or the log in Settings › Diagnostics."
                    : "Could not update the sign-in task. Check Task Scheduler or the log in Settings › Diagnostics.");
            }
            else
            {
                _startupDismissal.Cancel();
                StartupMessage = StartupExplanation();
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Startup", "Updating startup registration failed.", ex);
            ShowStartupProblem($"Could not update startup registration: {ex.Message}");
        }
        finally
        {
            _isApplyingStartup = false;
        }
    }

    // ---- Alerts ----------------------------------------------------------------------------------

    public bool AlertsEnabled
    {
        get => Settings.Current.Alerts.Enabled;
        set => Change("Alerts", s => s.Alerts.Enabled = value);
    }

    public IReadOnlyList<Option<int>> CooldownOptions { get; }

    public int CooldownMinutes
    {
        get => Settings.Current.Alerts.CooldownMinutes;
        set => Change("Alerts", s => s.Alerts.CooldownMinutes = value);
    }

    public ObservableCollection<AlertRuleViewModel> Rules { get; } = [];

    public ObservableCollection<AlertMetricOption> AlertMetricOptions { get; } = [];

    public IRelayCommand<AlertMetricOption> AddRuleCommand { get; }

    internal void SaveAlerts() => Settings.Update("Alerts");

    internal void RemoveRule(AlertRuleViewModel rule)
    {
        Rules.Remove(rule);
        Settings.Update("Alerts", s => s.Alerts.Rules.Remove(rule.Rule));
    }

    private void AddRule(AlertMetricOption? option)
    {
        if (option is null)
        {
            return;
        }

        double threshold = option.Kind switch
        {
            MetricKind.Temperature => 85,
            MetricKind.Load or MetricKind.Level => 90,
            MetricKind.Power => 400,
            _ => 100,
        };
        var rule = new AlertRule { Name = option.Name, MetricId = option.Id, Threshold = threshold, SustainSeconds = 10, Hysteresis = option.Kind == MetricKind.Temperature ? 3 : 5 };
        Settings.Update("Alerts", s => s.Alerts.Rules.Add(rule));
        Rules.Add(new AlertRuleViewModel(this, rule, option.Kind, option.Name));
    }

    private void LoadRules()
    {
        Rules.Clear();
        foreach (var rule in Settings.Current.Alerts.Rules)
        {
            var descriptor = Catalog.Get(rule.MetricId);
            var kind = descriptor?.Kind ?? GuessKind(rule.MetricId);
            Rules.Add(new AlertRuleViewModel(this, rule, kind, descriptor?.Name ?? rule.Name));
        }
    }

    private static MetricKind GuessKind(string id) =>
        id.Contains("/temp", StringComparison.Ordinal) ? MetricKind.Temperature
        : id.EndsWith("/power", StringComparison.Ordinal) ? MetricKind.Power
        : MetricKind.Load;

    private void RefreshAlertMetricOptions()
    {
        var options = Catalog.Where(d => !d.IsRaw && d.TrackHistory && d.Kind is MetricKind.Temperature or MetricKind.Load or MetricKind.Power)
            .OrderBy(d => d.Category)
            .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .Select(d => new AlertMetricOption(d.Id, d.DeviceName is { Length: > 0 } dev ? $"{d.Name} ({dev})" : d.Name, d.Kind))
            .ToArray();
        if (!AlertMetricOptions.SequenceEqual(options))
        {
            AlertMetricOptions.Clear();
            foreach (var o in options)
            {
                AlertMetricOptions.Add(o);
            }
        }
    }

    // ---- Updates ---------------------------------------------------------------------------------

    public IReadOnlyList<Option<UpdateCheckFrequency>> UpdateFrequencyOptions { get; }

    public UpdateCheckFrequency UpdateFrequency
    {
        get => Settings.Current.Updates.Frequency;
        set => Change("Updates", s => s.Updates.Frequency = value);
    }

    public bool IncludePrereleases
    {
        get => Settings.Current.Updates.IncludePrereleases;
        set => Change("Updates", s => s.Updates.IncludePrereleases = value);
    }

    // ---- Diagnostics -----------------------------------------------------------------------------

    public bool IsElevated => _deps.Elevation.IsElevated;

    public string PrivilegeText => IsMac
        ? "Runs as you, with no administrator rights, helper or kernel extension"
        : IsElevated ? "Running as administrator" : "Running as a standard user";

    public string PawnIoText => Monitor.Engine.Capabilities.PawnIoInstalled
        ? $"PawnIO driver installed (v{Monitor.Engine.Capabilities.PawnIoVersion})"
        : "PawnIO driver not installed";

    public bool LowLevelSensorsAccessible => Monitor.Engine.Capabilities.LowLevelSensorsAccessible;

    public string LowLevelExplanation => IsMac
        ? "Coreloom reads the Mac's sensors through macOS itself: the temperature sensors, the energy and clock counters (IOReport) and the System Management Controller, the same sources macOS's own tools use. A virtual Mac has none of them."
        : LowLevelSensorsAccessible
        ? "CPU temperature, package power, per-core clocks and motherboard sensors are available."
        : "CPU temperature, package power, per-core clocks and motherboard sensors are read by LibreHardwareMonitor through the PawnIO driver. Coreloom already runs as administrator; what is missing is the PawnIO driver, which Coreloom never installs. Everything else works without it.";

    public ObservableCollection<ProviderStatusItem> Providers { get; } = [];

    public string OverheadText
    {
        get => _overheadText;
        private set => SetProperty(ref _overheadText, value);
    }

    /// <summary>Each part of Coreloom and its processor share (the overlay's own parts shown apart).</summary>
    public ObservableCollection<KeyValueItem> OverheadParts { get; } = [];

    public bool HasOverheadParts => OverheadParts.Count > 0;

    private void UpdateOverheadParts()
    {
        if (_deps.Overhead is not { } overhead || SelectedSection != "Diagnostics")
        {
            return;
        }

        var parts = overhead.Measure()
            .OrderBy(p => p.Name, StringComparer.CurrentCulture)
            .Select(p => new KeyValueItem(p.Name, p.CpuPercent < 0.01 ? "under 0.01 % CPU" : $"{p.CpuPercent.ToString("0.00", CultureInfo.CurrentCulture)} % CPU"))
            .ToList();
        if (!OverheadParts.SequenceEqual(parts))
        {
            OverheadParts.Clear();
            foreach (var part in parts)
            {
                OverheadParts.Add(part);
            }

            OnPropertyChanged(nameof(HasOverheadParts));
        }
    }

    public bool VerboseLogging
    {
        get => Settings.Current.Diagnostics.VerboseLogging;
        set
        {
            Change("Diagnostics", s => s.Diagnostics.VerboseLogging = value);
            _deps.Diagnostics.SetVerboseLogging(value);
        }
    }

    public IRelayCommand ExportReportCommand { get; }

    public IRelayCommand OpenLogsCommand { get; }

    /// <summary>Opens the log viewer.</summary>
    public IRelayCommand ViewLogsCommand { get; }


    public IRelayCommand RescanCommand { get; }

    public IRelayCommand OpenPawnIoCommand { get; }

    private void ExportReport()
    {
        string? path = _deps.Shell.PickSaveFile($"coreloom-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.md", "Markdown report (*.md)|*.md");
        if (path is null)
        {
            return;
        }

        var engine = Monitor.Engine;
        string report = DiagnosticReportBuilder.Build(new DiagnosticReportInput
        {
            App = _deps.App,
            Capabilities = engine.Capabilities,
            Providers = engine.ProviderStatuses,
            Devices = engine.Devices.GetAll(),
            Metrics = engine.Catalog.Snapshot(),
            Frame = engine.LatestFrame,
            RecentLog = _deps.Diagnostics.RecentLog(),
            ExtraSections = [new("Coreloom process", OverheadText)],
        }, new UnitFormatter(culture: CultureInfo.InvariantCulture));

        try
        {
            File.WriteAllText(path, report);
            _deps.Shell.OpenFile(path);
        }
        catch (Exception ex)
        {
            _deps.Shell.ShowMessage("Export failed", ex.Message);
        }
    }

    // ---- About -----------------------------------------------------------------------------------

    public string VersionText => $"Version {_deps.App.InformationalVersion}";

    public string? RepositoryText => _deps.Updates.Repository;

    public IRelayCommand OpenRepositoryCommand { get; }

    public IRelayCommand OpenNoticesCommand { get; }

    // ---- Lifecycle -------------------------------------------------------------------------------

    protected override void OnActivated()
    {
        _lastProviderRefresh = DateTime.MinValue;
        RefreshAlertMetricOptions();
        OnPropertyChanged(string.Empty);
        UpdateConnectVisibility();
    }

    protected override void OnDeactivated() => UpdateConnectVisibility();

    protected override void OnUpdate(TelemetryFrame frame)
    {
        Connect?.Tick();

        if (DateTime.UtcNow - _lastProviderRefresh < TimeSpan.FromSeconds(2))
        {
            return;
        }

        _lastProviderRefresh = DateTime.UtcNow;
        foreach (var status in Monitor.Engine.ProviderStatuses)
        {
            var item = Providers.FirstOrDefault(p => p.Name == status.Name);
            if (item is null)
            {
                item = new ProviderStatusItem(status.Name);
                Providers.Add(item);
            }

            item.State = status.State.ToString();
            item.Message = string.Join(" — ", new[] { status.Message, status.LastError }.Where(s => !string.IsNullOrWhiteSpace(s)));
            item.Timing = status.State is ProviderState.Running or ProviderState.Degraded
                ? $"{status.AveragePollMilliseconds:0.0} ms/poll · {status.MetricsWritten} values"
                : status.InitMilliseconds > 0 ? $"init {status.InitMilliseconds:0} ms" : string.Empty;
            item.IsHealthy = status.State is ProviderState.Running or ProviderState.Disabled;
        }

        var f = Formatter;
        string cpu = frame.TryGetValue(MetricIds.AppCpu, out double c) ? f.Percent(c, 2).ToString() : "—";
        string mem = frame.TryGetValue(MetricIds.AppWorkingSet, out double m) ? f.MemoryBytes(m).ToString() : "—";
        string heap = frame.TryGetValue(MetricIds.AppManagedBytes, out double h) ? f.MemoryBytes(h).ToString() : "—";
        string handles = frame.TryGetValue(MetricIds.AppHandles, out double hc) ? hc.ToString("0", CultureInfo.CurrentCulture) : "—";
        var interval = Monitor.Engine.EffectiveInterval;
        string reading = interval > Monitor.Engine.Interval
            ? $"sensors read every {interval.TotalSeconds.ToString("0.##", CultureInfo.CurrentCulture)} s (lighter while a game is in front)"
            : $"sensors read every {interval.TotalSeconds.ToString("0.##", CultureInfo.CurrentCulture)} s";
        OverheadText = $"CPU {cpu} of total capacity · working set {mem} · managed heap {heap} · {handles} handles · history buffers ≈{f.Bytes(History.EstimatedBytes)} · last poll {frame.PollDuration.TotalMilliseconds:0.0} ms · {reading}";
        UpdateOverheadParts();
        RefreshAlertMetricOptions();
    }

    public override void OnSettingsChanged(string section)
    {
        if (section == "All")
        {
            LoadRules();
            MenuBar?.Reload();
            OnPropertyChanged(string.Empty);
        }
    }

    /// <summary>
    /// THIRD-PARTY-NOTICES.md: next to the program on Windows; in the app bundle's Resources folder on a Mac
    /// (Contents/MacOS holds only code, so the bundle's signature stays clean).
    /// </summary>
    internal static string NoticesFile(string baseDirectory)
    {
        const string name = "THIRD-PARTY-NOTICES.md";
        string beside = Path.Combine(baseDirectory, name);
        string resources = Path.GetFullPath(Path.Combine(baseDirectory, "..", "Resources", name));
        return !File.Exists(beside) && File.Exists(resources) ? resources : beside;
    }

    private void Change(string section, Action<AppSettings> mutate, [System.Runtime.CompilerServices.CallerMemberName] string? property = null)
    {
        Settings.Update(section, mutate);
        OnPropertyChanged(property);
    }
}
