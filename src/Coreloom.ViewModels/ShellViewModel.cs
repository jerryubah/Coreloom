using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Coreloom.Core.Alerts;
using Coreloom.Core.Formatting;
using Coreloom.Core.History;
using Coreloom.Core.Settings;
using Coreloom.Core.Telemetry;
using Coreloom.ViewModels.Infrastructure;
using Coreloom.ViewModels.Pages;
using Coreloom.ViewModels.Services;

namespace Coreloom.ViewModels;

public sealed class ShellDependencies
{
    public required MonitorContext Monitor { get; init; }

    public required SettingsService Settings { get; init; }

    public required AlertEngine Alerts { get; init; }

    public required IThemeService Theme { get; init; }

    public required IStartupService Startup { get; init; }

    public required IElevationService Elevation { get; init; }

    public required IShellService Shell { get; init; }

    public required IAppLifetime Lifetime { get; init; }

    public required IDiagnosticsService Diagnostics { get; init; }

    public required UpdatesViewModel Updates { get; init; }

    public required Coreloom.Core.AppVersionInfo App { get; init; }

    /// <summary>Benchmarks, speed test and Smart Optimize; defaults work everywhere (Smart Optimize then explains it's unavailable).</summary>
    public ToolServices? Tools { get; init; }
}

/// <summary>
/// Root view model: navigation, the shared timeline, live status, pause controls, the limited-mode banner
/// and application of settings to the engine/alerts/theme. Receives frames on the UI thread.
/// </summary>
public sealed class ShellViewModel : ObservableObject
{
    private readonly ShellDependencies _deps;
    private readonly PageContext _context;
    private PageViewModel _currentPage;
    private bool _isLive;
    private bool _isWindowVisible = true;
    private bool _bannerDismissed;
    private string _trayText = "Coreloom";
    private long _lastFrameSequence;

    public ShellViewModel(ShellDependencies deps)
    {
        _deps = deps;
        Timeline = new TimelineViewModel(deps.Monitor.Engine.History);
        var context = new PageContext { Monitor = deps.Monitor, Settings = deps.Settings, Timeline = Timeline, Tools = deps.Tools ?? new ToolServices() };
        _context = context;

        Overview = new OverviewViewModel(context);
        Gaming = new GamingViewModel(context);
        Gpu = new GpuViewModel(context);
        Cpu = new CpuViewModel(context);
        Memory = new MemoryViewModel(context);
        Storage = new StorageViewModel(context);
        Devices = new DevicesViewModel(context);
        Network = new NetworkViewModel(context);
        History = new HistoryViewModel(context, deps.Shell);
        Optimize = new OptimizeViewModel(context);
        Settings = new SettingsViewModel(context, new SettingsDependencies
        {
            Theme = deps.Theme,
            Startup = deps.Startup,
            Elevation = deps.Elevation,
            Shell = deps.Shell,
            Lifetime = deps.Lifetime,
            Diagnostics = deps.Diagnostics,
            Updates = deps.Updates,
            App = deps.App,
        });

        Pages = [Overview, Gaming, Gpu, Cpu, Memory, Storage, Devices, Network, Optimize, History];
        _currentPage = Overview;
        NavigateCommand = new RelayCommand<PageViewModel>(page =>
        {
            if (page is not null)
            {
                Navigate(page);
            }
        });
        ToggleFreezeCommand = new RelayCommand(() => IsChartsFrozen = !IsChartsFrozen);
        TogglePauseMonitoringCommand = new RelayCommand(() => IsMonitoringPaused = !IsMonitoringPaused);
        ToggleSidebarCommand = new RelayCommand(() => IsSidebarCollapsed = !IsSidebarCollapsed);
        SetWindowCommand = new RelayCommand<HistoryWindow?>(w =>
        {
            if (w is HistoryWindow window)
            {
                SelectedWindow = window;
            }
        });
        OpenPawnIoCommand = new RelayCommand(() => _deps.Shell.OpenUrl("https://pawnio.eu"));
        DismissBannerCommand = new RelayCommand(() =>
        {
            _bannerDismissed = true;
            OnPropertyChanged(nameof(ShowLimitedModeBanner));
        });
        OpenUpdatesCommand = new RelayCommand(() =>
        {
            Settings.SelectedSection = "Updates";
            Navigate(Settings);
        });
        ShowWhatsNewCommand = new RelayCommand(() =>
        {
            OpenUpdatesCommand.Execute(null);
            deps.Updates.DismissUpdatedBannerCommand.Execute(null);
        });

        deps.Settings.Changed += OnSettingsChanged;
        deps.Updates.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(UpdatesViewModel.ShowBadge))
            {
                OnPropertyChanged(nameof(IsUpdateAvailable));
            }
        };

        ApplySettings("All");
        _currentPage.Activate(deps.Monitor.Engine.LatestFrame);
    }

    public TimelineViewModel Timeline { get; }

    public OverviewViewModel Overview { get; }

    /// <summary>Game library, boosts, frame pacer and shader caches (above Graphics).</summary>
    public GamingViewModel Gaming { get; }

    public GpuViewModel Gpu { get; }

    public CpuViewModel Cpu { get; }

    public MemoryViewModel Memory { get; }

    public StorageViewModel Storage { get; }

    public DevicesViewModel Devices { get; }

    public NetworkViewModel Network { get; }

    public HistoryViewModel History { get; }

    public OptimizeViewModel Optimize { get; }

    public SettingsViewModel Settings { get; }

    public UpdatesViewModel Updates => _deps.Updates;

    /// <summary>The in-game overlay's content (the app shows it in its own window over games).</summary>
    public Overlay.OverlayViewModel Overlay => _context.Overlay;

    /// <summary>Health of the graphics card, processor, memory and drives.</summary>
    public SystemHealthViewModel Health => _context.Health;

    /// <summary>Navigation pages (Settings is shown separately at the bottom of the sidebar).</summary>
    public ObservableCollection<PageViewModel> Pages { get; }

    public PageViewModel CurrentPage
    {
        get => _currentPage;
        private set => SetProperty(ref _currentPage, value);
    }

    public IRelayCommand<PageViewModel> NavigateCommand { get; }

    public IRelayCommand ToggleFreezeCommand { get; }

    public IRelayCommand TogglePauseMonitoringCommand { get; }

    public IRelayCommand ToggleSidebarCommand { get; }

    public IRelayCommand<HistoryWindow?> SetWindowCommand { get; }

    public IRelayCommand OpenPawnIoCommand { get; }

    public IRelayCommand DismissBannerCommand { get; }

    public IRelayCommand OpenUpdatesCommand { get; }

    /// <summary>"What's new" on the "Updated to …" banner: opens Settings › Updates and dismisses the banner.</summary>
    public IRelayCommand ShowWhatsNewCommand { get; }

    public IReadOnlyList<Option<HistoryWindow>> WindowOptions { get; } =
        Enum.GetValues<HistoryWindow>().Select(w => new Option<HistoryWindow>(w, w.ToLabel())).ToArray();

    public HistoryWindow SelectedWindow
    {
        get => Timeline.Window;
        set
        {
            if (Timeline.Window != value)
            {
                Timeline.Window = value;
                OnPropertyChanged();
                _deps.Settings.Update("Window", s => s.Monitoring.DefaultWindow = value);
                RefreshCurrentPage();
            }
        }
    }

    public bool IsChartsFrozen
    {
        get => Timeline.IsFrozen;
        set
        {
            if (Timeline.IsFrozen != value)
            {
                Timeline.IsFrozen = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    public bool IsMonitoringPaused
    {
        get => _deps.Monitor.Engine.IsPaused;
        set
        {
            if (value)
            {
                _deps.Monitor.Engine.Pause();
            }
            else
            {
                _deps.Monitor.Engine.Resume();
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(IsLive));
        }
    }

    public bool IsSidebarCollapsed
    {
        get => _deps.Settings.Current.Appearance.SidebarCollapsed;
        set
        {
            if (_deps.Settings.Current.Appearance.SidebarCollapsed != value)
            {
                _deps.Settings.Update("Sidebar", s => s.Appearance.SidebarCollapsed = value);
                OnPropertyChanged();
            }
        }
    }

    /// <summary>True while frames are arriving and monitoring is not paused.</summary>
    public bool IsLive
    {
        get => _isLive && !IsMonitoringPaused;
        private set => SetProperty(ref _isLive, value);
    }

    public string StatusText => IsMonitoringPaused
        ? "Monitoring paused"
        : IsChartsFrozen
            ? "Charts paused"
            : _isLive ? $"Live · {FormatInterval(_deps.Settings.Current.Monitoring.PollingIntervalMs)}" : "Starting…";

    public bool ShowLimitedModeBanner => !_bannerDismissed && !_deps.Monitor.Engine.Capabilities.LowLevelSensorsAccessible
        && _deps.Settings.Current.Monitoring.UseLibreHardwareMonitor && _deps.Monitor.LibreHardwareMonitorIncluded;

    /// <summary>The app always runs elevated, so the only missing piece is the optional PawnIO driver.</summary>
    public string LimitedModeText => !_deps.Monitor.Engine.Capabilities.IsElevated
        ? "CPU temperature, package power and motherboard sensors need administrator rights, which this session doesn't have."
        : "CPU temperature, package power and motherboard sensors need the PawnIO driver, which isn't installed.";

    /// <summary>A newer version is available, downloading or ready (the sidebar badge).</summary>
    public bool IsUpdateAvailable => _deps.Updates.ShowBadge;

    /// <summary>Short live summary for the tray icon tooltip.</summary>
    public string TrayText
    {
        get => _trayText;
        private set => SetProperty(ref _trayText, value);
    }

    public string AppTitle => "Coreloom";

    public void Navigate(PageViewModel page)
    {
        if (ReferenceEquals(page, _currentPage))
        {
            return;
        }

        _currentPage.Deactivate();
        CurrentPage = page;
        page.Activate(_deps.Monitor.Engine.LatestFrame);
    }

    /// <summary>Called by the app when the main window is shown/hidden/minimized. Hidden windows do no UI work.</summary>
    public void SetWindowVisible(bool visible)
    {
        if (_isWindowVisible == visible)
        {
            return;
        }

        _isWindowVisible = visible;
        if (visible)
        {
            _currentPage.Activate(_deps.Monitor.Engine.LatestFrame);
        }
        else
        {
            _currentPage.Deactivate();
        }
    }

    /// <summary>Delivers a frame on the UI thread.</summary>
    public void OnFrame(TelemetryFrame frame)
    {
        if (frame.Sequence <= _lastFrameSequence)
        {
            return;
        }

        _lastFrameSequence = frame.Sequence;
        if (!_isLive)
        {
            IsLive = true;
            OnPropertyChanged(nameof(StatusText));
        }

        UpdateTrayText(frame);

        // Health and the overlay are kept current while the window is hidden: you're in a game.
        _context.Health.Update(frame);
        _context.Overlay.OnFrame(frame);
        if (!_isWindowVisible)
        {
            return;
        }

        Timeline.Advance(frame);
        _currentPage.Update(frame);
    }

    private void UpdateTrayText(TelemetryFrame frame)
    {
        if (!_deps.Settings.Current.Desktop.ShowTrayTooltipStats)
        {
            TrayText = "Coreloom";
            return;
        }

        var f = _deps.Settings.Formatter;
        int gpu = _deps.Settings.Current.Monitoring.PreferredGpuSlot ?? _deps.Monitor.PrimaryGpuSlot();
        string Part(string label, string loadId, string tempId)
        {
            string load = frame.TryGetValue(loadId, out double l) ? f.Percent(l).ToString() : "—";
            string temp = frame.TryGetValue(tempId, out double t) ? f.Temperature(t).ToString() : string.Empty;
            return string.IsNullOrEmpty(temp) ? $"{label} {load}" : $"{label} {load} · {temp}";
        }

        string text = $"Coreloom\n{Part("CPU", MetricIds.CpuLoad, MetricIds.CpuTemperature)}\n{Part("GPU", MetricIds.GpuLoad(gpu), MetricIds.GpuTemperature(gpu))}";
        if (frame.TryGetValue(MetricIds.MemoryLoad, out double ram))
        {
            text += $"\nRAM {f.Percent(ram)}";
        }

        TrayText = text.Length > 120 ? text[..120] : text;
    }

    private void RefreshCurrentPage()
    {
        var frame = _deps.Monitor.Engine.LatestFrame;
        if (frame.Sequence > 0)
        {
            _currentPage.Update(frame);
        }
    }

    private void OnSettingsChanged(string section)
    {
        ApplySettings(section);
        foreach (var page in Pages.Append(Settings))
        {
            page.OnSettingsChanged(section);
        }

        if (section != "Overlay")
        {
            RefreshCurrentPage(); // overlay sliders change settings many times a second; pages don't show them
        }
    }

    /// <summary>Applies settings to the engine, alert engine, theme and timeline.</summary>
    public void ApplySettings(string section)
    {
        var s = _deps.Settings.Current;
        var engine = _deps.Monitor.Engine;
        bool all = section == "All";

        if (all || section == "Monitoring")
        {
            engine.Interval = TimeSpan.FromMilliseconds(s.Monitoring.PollingIntervalMs);
            Timeline.IntervalMilliseconds = s.Monitoring.PollingIntervalMs;
            if (all)
            {
                Timeline.Window = s.Monitoring.DefaultWindow;
                OnPropertyChanged(nameof(SelectedWindow));
            }

            OnPropertyChanged(nameof(StatusText));
        }

        if (all || section is "Sections" or "Monitoring")
        {
            engine.SetSectionEnabled(MonitoringSection.Gpu, s.Monitoring.GpuEnabled);
            engine.SetSectionEnabled(MonitoringSection.Cpu, s.Monitoring.CpuEnabled);
            engine.SetSectionEnabled(MonitoringSection.Memory, s.Monitoring.MemoryEnabled);
            engine.SetSectionEnabled(MonitoringSection.Storage, s.Monitoring.StorageEnabled);
            engine.SetSectionEnabled(MonitoringSection.Network, s.Monitoring.NetworkEnabled);
            foreach (var page in Pages)
            {
                page.IsEnabled = page.Section is not MonitoringSection section2 || s.Monitoring.IsSectionEnabled(section2);
            }

            if (!_currentPage.IsEnabled)
            {
                Navigate(Overview);
            }
        }

        if (all || section == "Alerts")
        {
            _deps.Alerts.Enabled = s.Alerts.Enabled;
            _deps.Alerts.Cooldown = TimeSpan.FromMinutes(s.Alerts.CooldownMinutes);
            _deps.Alerts.SetRules(s.Alerts.Rules);
        }

        if (all || section == "Appearance")
        {
            Timeline.SmoothScrolling = s.Appearance.SmoothChartScrolling && !s.Appearance.ReducedMotion;
            Timeline.AnimateValues = s.Appearance.AnimateValues && !s.Appearance.ReducedMotion;
            _deps.Theme.Apply(s.Appearance);
        }

        if (all || section == "Sidebar")
        {
            OnPropertyChanged(nameof(IsSidebarCollapsed));
        }
    }

    private static string FormatInterval(int ms) => ms >= 1000 ? $"{ms / 1000.0:0.##} s" : $"{ms} ms";
}
