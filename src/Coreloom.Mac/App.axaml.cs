using System.Globalization;
using System.Net.Http;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Coreloom.Core;
using Coreloom.Core.Alerts;
using Coreloom.Core.Diagnostics;
using Coreloom.Core.Settings;
using Coreloom.Core.Startup;
using Coreloom.Core.Telemetry;
using Coreloom.Core.Updates;
using Coreloom.Hardware.Mac;
using Coreloom.Hardware.Mac.Sensors;
using Coreloom.Mac.Controls;
using Coreloom.Mac.Native;
using Coreloom.Mac.Overlay;
using Coreloom.Mac.Services;
using Coreloom.Mac.Themes;
using Coreloom.Mac.Views;
using Coreloom.ViewModels;
using Coreloom.ViewModels.Infrastructure;
using Coreloom.ViewModels.Services;

namespace Coreloom.Mac;

/// <summary>
/// Composition root of the Mac app. Owns the single-instance lock, logging, settings, the monitoring host, the menu bar
/// item, the app's menus, the frame pump (telemetry thread → UI thread), alert notifications and update checks. Quitting
/// always goes through <see cref="Quit"/> so settings are saved and the sensors are closed cleanly.
/// </summary>
public partial class App : Application
{
    private static readonly TimeSpan UpdateCheckDelay = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan UpdateCheckPeriod = TimeSpan.FromHours(6);
    private static readonly TimeSpan BackgroundInstallCheckInterval = TimeSpan.FromMinutes(1);

    /// <summary>GPU load metrics checked to tell an idle Mac from a busy one.</summary>
    private static readonly string[] GpuLoadIds = [MetricIds.GpuLoad(0), MetricIds.GpuLoad(1), MetricIds.GpuLoad(2), MetricIds.GpuLoad(3)];

    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private SingleInstance? _instance;
    private FileLogSink? _fileLog;
    private SettingsService? _settings;
    private MacMonitoringHost? _host;
    private AlertEngine? _alerts;
    private ShellViewModel? _shell;
    private MainWindow? _window;
    private MenuBarService? _menuBar;
    private MacOverlayService? _overlay;
    private MacGameWatcher? _gameWatcher;
    private MacDeviceService? _devices;
    private WakeWatcher? _wake;
    private HttpClient? _http;
    private UpdatesViewModel? _updates;
    private DispatcherTimer? _updateTimer;
    private DispatcherTimer? _installTimer;
    private long _lastBusyTick = Environment.TickCount64;
    private int _framePending;
    private volatile bool _exiting;
    private bool _cleanedUp;
    private bool _trayHintShown;
    private DateTime _lastCrashDialog = DateTime.MinValue;

    /// <summary>The single-instance lock Program.Main took before the interface started.</summary>
    internal static SingleInstance? PrimaryInstance { get; set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        base.OnFrameworkInitializationCompleted();
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return; // drawn headlessly (screenshots, tests): no app to run
        }

        _desktop = desktop;
        try
        {
            Start(desktop.Args ?? []);
        }
        catch (Exception ex)
        {
            // A failure while starting must never leave an invisible process behind holding the single-instance lock.
            Log.Error("App", "Coreloom failed to start.", ex);
            Log.Flush();
            AppKit.Alert("Coreloom couldn't start", $"{DescribeError(ex)}\n\nDetails were written to ~/Library/Logs/Coreloom.", confirm: false, warning: true);
            _exiting = true;
            try
            {
                _overlay?.Dispose();
                _menuBar?.Dispose();
                _host?.Dispose();
            }
            catch (Exception cleanup)
            {
                Log.Warn("App", "Cleanup after a failed start also failed.", cleanup);
            }

            ReleaseAndShutdown(1);
        }
    }

    private void Start(string[] arguments)
    {
        var args = MacCommandLine.Parse(arguments);

        // Program.Main took the single-instance lock (a second launch never gets this far).
        var paths = AppPaths.ForMac(AppContext.BaseDirectory);
        _instance = PrimaryInstance ?? new SingleInstance(paths.SettingsDirectory);
        if (PrimaryInstance is null && !_instance.TryAcquire())
        {
            _instance.SignalPrimary(args.Page);
            _instance.Dispose();
            _instance = null;
            _desktop?.Shutdown(0);
            return;
        }

        var memoryLog = new MemoryLogSink();
        InitializeLogging(paths, memoryLog);
        RegisterExceptionHandlers();

        var app = AppVersionInfo.FromAssembly(typeof(App).Assembly);
        Log.Info("App", $"Coreloom {app.InformationalVersion} starting (args: {string.Join(' ', arguments)}; bundle: {MacLifetimeService.BundlePath ?? "none"}; "
            + $"{ProcessInfo.Describe()}; Apple silicon: {SensorHub.IsAppleSilicon}; virtual machine: {SensorHub.IsVirtualMachine}).");

        // ---- Settings & theme ---------------------------------------------------------------------
        bool firstRun = !File.Exists(paths.SettingsFile);
        var store = new SettingsStore(paths.SettingsFile);
        var settings = store.Load();
        if (firstRun)
        {
            MacDefaults.Apply(settings, SensorHub.IsAppleSilicon);
            store.Save(settings);
            Log.Info("App", "First launch: Mac defaults applied.");
        }

        var settingsService = new SettingsService(store, settings);
        _settings = settingsService;
        Log.MinimumLevel = settings.Diagnostics.VerboseLogging ? LogLevel.Debug : LogLevel.Info;

        // Open at login follows System Settings: if the user removed (or approved) Coreloom there, the switch says so.
        var startup = new MacStartupService();
        bool registered = startup.IsStartAtLogonRegistered;
        if (registered != settings.Desktop.StartWithWindows && MacLifetimeService.BundlePath is not null)
        {
            settingsService.Update("Desktop", s => s.Desktop.StartWithWindows = registered);
        }

        bool fromLogin = args.FromStartup || MacSession.LaunchedAtLogin(settings.Desktop.StartWithWindows);
        bool startHidden = SplashPolicy.StartsHidden(args.Minimized, fromLogin, settings.Desktop.LaunchMinimized);

        // ---- Updates ------------------------------------------------------------------------------
        // Before anything else is built: pick up what the last session downloaded and, when automatic updates are on,
        // install it now (Coreloom quits, the helper replaces the app and opens the new version).
        var shellService = new MacShellService();
        var installer = new MacUpdateInstaller(paths, () => Dispatcher.UIThread.Post(Quit));
        installer.StartCleanup();
        _http = new HttpClient(new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(15), AutomaticDecompression = System.Net.DecompressionMethods.All })
        {
            Timeout = TimeSpan.FromSeconds(60),
        };
        var updates = new UpdatesViewModel(app, settingsService, installer, shellService, _http, new PendingUpdateStore(paths.DownloadDirectory),
            new FileUpdateTrustStore(paths.DownloadDirectory));
        _updates = updates;
        updates.InitializeFromDisk(launchedAfterFailedUpdate: args.UpdateFailed);
        if (updates.TryInstallAtStartup(startHidden))
        {
            Log.Info("App", "Quitting so the update can replace Coreloom; the new version opens when it's done.");
            return;
        }

        BrandLogos.Configure(paths.VendorLogoDirectory);
        var theme = new ThemeService(Resources);
        theme.Initialize(settings.Appearance);

        // ---- Monitoring back-end ------------------------------------------------------------------
        try
        {
            _host = MacMonitoringHost.Create(new MacHardwareOptions
            {
                PollingInterval = TimeSpan.FromMilliseconds(settings.Monitoring.PollingIntervalMs),
                PreferredNetworkAdapter = () => _settings?.Current.Monitoring.PreferredNetworkAdapter,
            });
        }
        catch (Exception ex)
        {
            Log.Error("App", "Creating the monitoring host failed.", ex);
            Log.Flush();
            AppKit.Alert("Coreloom couldn't start", $"Its monitoring engine failed to start:\n\n{DescribeError(ex)}\n\nDetails were written to {paths.LogDirectory}.", confirm: false, warning: true);
            ReleaseAndShutdown(1);
            return;
        }

        var host = _host;
        var monitor = new MonitorContext
        {
            Engine = host.Engine,
            Summary = host.Summary,
            Processes = host.ProcessSampler,
            PrimaryGpuSlot = () => host.PrimaryGpuSlot,
            Gpus = () => host.Gpu?.Gpus.Select(g => (g.Slot, g.Name, g.IsIntegrated)).ToArray() ?? [],
            NetworkAdapters = () => host.Network?.GetAdapters() ?? [],
            LibreHardwareMonitorIncluded = false,
        };

        var alerts = new AlertEngine(TimeSpan.FromMinutes(settings.Alerts.CooldownMinutes));
        _alerts = alerts;

        // ---- Services ---------------------------------------------------------------------------
        var lifetime = new MacLifetimeService(() => Dispatcher.UIThread.Post(Quit));
        string userAgent = $"Coreloom/{app.DisplayVersion}";
        var shell = new ShellViewModel(new ShellDependencies
        {
            Monitor = monitor,
            Settings = settingsService,
            Alerts = alerts,
            Theme = theme,
            Startup = startup,
            Elevation = lifetime,
            Shell = shellService,
            Lifetime = lifetime,
            Diagnostics = new MacDiagnosticsService(paths.LogDirectory, memoryLog),
            Updates = updates,
            App = app,
            Platform = HostPlatform.MacOS,
            Connect = new Coreloom.ViewModels.Connect.ConnectEnvironment { DataDirectory = paths.SettingsDirectory },
            Tools = new ToolServices
            {
                Benchmarks = new MacBenchmarkService(paths.SettingsDirectory),
                SpeedTest = new SpeedTestService(userAgent),
                Optimizer = new MacOptimizeService(),
                Devices = _devices = new MacDeviceService(work => Dispatcher.UIThread.InvokeAsync(work).GetTask()),
                Gaming = new MacGamingService(paths.LocalDirectory, userAgent, AppKit.RefreshRate),
            },
        });
        _shell = shell;
        MacNotifications.Initialize(action => Dispatcher.UIThread.Post(action));

        // ---- Window, menus & menu bar item --------------------------------------------------------
        var window = new MainWindow(shell)
        {
            DesktopSettingsProvider = () => settingsService.Current.Desktop,
        };
        window.ApplyPlacement(settings.Window);
        window.HiddenToMenuBar += (_, _) => OnHiddenToMenuBar();
        window.Closed += (_, _) => Quit(); // closing without "keep running in the menu bar" quits
        _window = window;
        _desktop!.MainWindow = window;

        void OpenSettings()
        {
            shell.Navigate(shell.Settings);
            ShowMainWindow();
        }

        void OpenUpdates()
        {
            shell.OpenUpdatesCommand.Execute(null);
            ShowMainWindow();
        }

        void CheckForUpdates()
        {
            OpenUpdates();
            _ = updates.CheckAsync(userInitiated: true);
        }

        var menuBar = new MenuBarService(
            open: ShowMainWindow,
            togglePause: () => shell.IsMonitoringPaused = !shell.IsMonitoringPaused,
            openSettings: OpenSettings,
            checkUpdates: CheckForUpdates,
            installUpdate: () =>
            {
                if (!updates.IsReady)
                {
                    OpenUpdates(); // it downloads first: show the progress
                }

                updates.InstallCommand.Execute(null);
            },
            quit: Quit);
        _menuBar = menuBar;
        AppMenus.Install(this, window, shell, new AppMenus.Actions(ShowMainWindow, OpenSettings, CheckForUpdates, Quit));

        shell.PropertyChanged += (_, pe) =>
        {
            if (pe.PropertyName == nameof(ShellViewModel.IsMonitoringPaused))
            {
                menuBar.SetPaused(shell.IsMonitoringPaused);
            }
        };

        // ---- Update notifications -------------------------------------------------------------------
        updates.NotificationRequested += note => Dispatcher.UIThread.Post(() =>
        {
            if (note.UserInitiated && _window is { IsVisible: true, IsActive: true })
            {
                return; // the user is looking at Settings › Updates already
            }

            MacNotifications.Show(note.Title, note.Message, onClick: OpenUpdates);
        });

        void SyncUpdateItem() => _menuBar?.SetUpdateItem(updates.ShowInstallButton ? $"Install Update to {updates.UpdateVersion}" : null);
        updates.PropertyChanged += (_, pe) =>
        {
            if (pe.PropertyName is nameof(UpdatesViewModel.ShowInstallButton) or nameof(UpdatesViewModel.UpdateVersion))
            {
                SyncUpdateItem();
            }
        };
        SyncUpdateItem();

        if (startHidden && updates.UpdatedNotification is { } updated)
        {
            // Updated in the background and back in the menu bar: say so once (the window shows a banner instead).
            Dispatcher.UIThread.Post(() => MacNotifications.Show(updated.Title, updated.Message, onClick: () =>
            {
                shell.ShowWhatsNewCommand.Execute(null);
                ShowMainWindow();
            }), DispatcherPriority.ApplicationIdle);
        }

        // ---- Frame pump & alerts ----------------------------------------------------------------
        alerts.AlertRaised += OnAlertRaised;
        host.Engine.FrameProduced += OnFrameProduced;

        _instance.ListenForActivation(page => Dispatcher.UIThread.Post(() =>
        {
            ShowMainWindow();
            NavigateTo(page);
        }));
        if (TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime activatable)
        {
            // Clicking the Dock icon, or opening Coreloom again from Finder, Spotlight or Launchpad.
            activatable.Activated += (_, e) =>
            {
                if (e.Kind == ActivationKind.Reopen)
                {
                    ShowMainWindow();
                }
            };
        }

        _desktop!.ShutdownRequested += (_, _) =>
        {
            // Quit from the Dock, logging out or shutting down: save everything before macOS ends the process.
            Log.Info("App", "macOS asked Coreloom to quit.");
            Cleanup();
        };

        _wake = new WakeWatcher(OnWake);
        _wake.Start();
        host.Engine.Start();

        // The readings window (off until turned on; its hotkey works once it has been on).
        try
        {
            _overlay = new MacOverlayService(shell, settingsService);
            _overlay.Start();
        }
        catch (Exception ex)
        {
            Log.Error("Overlay", "Starting the readings window failed.", ex);
        }

        // Lighter monitoring while a game is in front, and "keep the Mac awake while playing".
        _gameWatcher = new MacGameWatcher(shell, settingsService);
        _gameWatcher.Start();

        if (startHidden)
        {
            shell.SetWindowVisible(false);
            if (settings.Desktop.HideDockIconWhenClosed)
            {
                AppKit.SetShowInDock(false);
            }

            Log.Info("App", "Started in the menu bar.");
        }
        else
        {
            window.Show();
            AppKit.Activate();
            NavigateTo(args.Page);
            Dispatcher.UIThread.Post(OfferMoveToApplications, DispatcherPriority.ApplicationIdle);
        }

        // ---- Background housekeeping ------------------------------------------------------------
        ScheduleUpdateChecks(updates, isStartup: true);
        _ = updates.LoadInstalledNotesIfMissingAsync(); // "What's new" after an update that left no notes behind
    }

    // ------------------------------------------------------------------------------------------------
    // Frames: produced on the telemetry thread. Alerts are evaluated there (cheap, and works while the window is hidden);
    // UI delivery is coalesced so a slow UI never queues more than one frame.
    // ------------------------------------------------------------------------------------------------

    private void OnFrameProduced(TelemetryFrame frame)
    {
        try
        {
            _alerts?.Evaluate(frame);
        }
        catch (Exception ex)
        {
            Log.WarnThrottled("alerts", TimeSpan.FromMinutes(5), "Alerts", "Evaluating alert rules failed.", ex);
        }

        // Remember when the Mac was last busy: a downloaded update installs in the background only after a quiet while.
        double? gpuLoad = null;
        foreach (string id in GpuLoadIds)
        {
            if (frame.TryGetValue(id, out double load) && (gpuLoad is null || load > gpuLoad))
            {
                gpuLoad = load;
            }
        }

        if (UpdateInstallPolicy.IsSystemBusy(frame.GetValue(MetricIds.CpuLoad), gpuLoad))
        {
            Interlocked.Exchange(ref _lastBusyTick, Environment.TickCount64);
        }

        if (Interlocked.Exchange(ref _framePending, 1) == 1)
        {
            return;
        }

        Dispatcher.UIThread.Post(DeliverLatestFrame, DispatcherPriority.Normal);
    }

    private void DeliverLatestFrame()
    {
        Volatile.Write(ref _framePending, 0);
        if (_exiting || _shell is null || _host is null)
        {
            return;
        }

        try
        {
            _shell.OnFrame(_host.Engine.LatestFrame);
            _menuBar?.SetReadings(_shell.TrayText);
        }
        catch (Exception ex)
        {
            // A UI bug must never stop monitoring; log it (throttled) and keep going.
            Log.WarnThrottled("frame-ui", TimeSpan.FromMinutes(1), "UI", "Updating the UI with a new frame failed.", ex);
        }
    }

    private void OnAlertRaised(AlertEvent alert)
    {
        if (_settings is not { } settings)
        {
            return;
        }

        string message = DescribeAlert(alert, settings);
        Log.Info("Alerts", $"{alert.Rule.Name}: {message}");
        Dispatcher.UIThread.Post(() => MacNotifications.Show(alert.Rule.Name, message, onClick: ShowMainWindow, sound: true));
    }

    internal static string DescribeAlert(AlertEvent alert, SettingsService settings)
    {
        var formatter = settings.Formatter;
        var style = ValueStyles.ForKind(alert.Metric?.Kind ?? MetricKind.Load);
        string value = ValueStyles.Format(style, alert.Value, formatter).ToString();
        string threshold = ValueStyles.Format(style, alert.Rule.Threshold, formatter).ToString();
        string name = alert.Metric?.Name ?? alert.Rule.MetricId;
        string direction = alert.Rule.Comparison == AlertComparison.Above ? "above" : "below";
        string sustained = alert.Rule.SustainSeconds > 0 ? string.Format(CultureInfo.CurrentCulture, " for {0} s", alert.Rule.SustainSeconds) : string.Empty;
        return $"{name} is {value} — {direction} your {threshold} threshold{sustained}.";
    }

    // ------------------------------------------------------------------------------------------------
    // Window, Dock and menu bar
    // ------------------------------------------------------------------------------------------------

    /// <summary>Opens a page by its key ("gaming", "settings", or "connect" for Settings › Connect); nothing for an unknown key or none.</summary>
    private void NavigateTo(string? key)
    {
        if (key is null || _shell is not { } shell)
        {
            return;
        }

        if (string.Equals(key, "connect", StringComparison.OrdinalIgnoreCase) && shell.Settings.Connect is not null)
        {
            shell.Navigate(shell.Settings);
            shell.Settings.SelectedSection = "Connect";
            return;
        }

        var page = shell.Pages.Append(shell.Settings).FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
        if (page is not null)
        {
            shell.Navigate(page);
        }
    }

    private void ShowMainWindow()
    {
        if (_exiting || _window is null)
        {
            return;
        }

        AppKit.SetShowInDock(true);
        _window.ShowFromMenuBar();
        AppKit.Activate();
    }

    private void OnHiddenToMenuBar()
    {
        if (_settings?.Current.Desktop.HideDockIconWhenClosed == true)
        {
            AppKit.SetShowInDock(false);
        }

        if (_trayHintShown || _settings?.Current.Desktop.TrayHintShown != false)
        {
            return;
        }

        _trayHintShown = true;
        _settings.Update("Desktop", s => s.Desktop.TrayHintShown = true);
        MacNotifications.Show("Coreloom is still running", "Monitoring and alerts continue in the menu bar. Choose Quit Coreloom from its menu to quit.");
    }

    /// <summary>The first time Coreloom runs from outside Applications (the disk image, Downloads), offer to move it there.</summary>
    private void OfferMoveToApplications()
    {
        if (_exiting || _settings is null)
        {
            return;
        }

        try
        {
            if (AppMover.OfferMove(_settings.Current.Desktop.MoveToApplicationsDeclined, () => _settings.Update("Desktop", s => s.Desktop.MoveToApplicationsDeclined = true)))
            {
                Quit(); // the moved copy is opening and waits for this one to quit
            }
        }
        catch (Exception ex)
        {
            Log.Warn("App", "Offering to move Coreloom to Applications failed.", ex);
        }
    }

    // ------------------------------------------------------------------------------------------------
    // Updates & waking
    // ------------------------------------------------------------------------------------------------

    private void ScheduleUpdateChecks(UpdatesViewModel updates, bool isStartup)
    {
        var first = new DispatcherTimer { Interval = UpdateCheckDelay };
        first.Tick += async (_, _) =>
        {
            first.Stop();
            await SafeCheckAsync(updates, isStartup);
        };
        first.Start();

        _updateTimer = new DispatcherTimer { Interval = UpdateCheckPeriod };
        _updateTimer.Tick += async (_, _) => await SafeCheckAsync(updates, isStartup: false);
        _updateTimer.Start();

        // A downloaded update also installs while Coreloom sits in the menu bar and nobody is using the Mac.
        _installTimer = new DispatcherTimer { Interval = BackgroundInstallCheckInterval };
        _installTimer.Tick += (_, _) => TryBackgroundInstall(updates);
        _installTimer.Start();
    }

    private void TryBackgroundInstall(UpdatesViewModel updates)
    {
        if (_exiting || !updates.IsReady || _shell is null)
        {
            return;
        }

        try
        {
            var window = _window;
            updates.TryInstallInBackground(new BackgroundInstallSignals
            {
                WindowInUse = window is { IsVisible: true } && window.WindowState != WindowState.Minimized,
                UserIdle = MacSession.SinceLastInput(),
                SystemQuiet = TimeSpan.FromMilliseconds(Math.Max(0, Environment.TickCount64 - Interlocked.Read(ref _lastBusyTick))),
                MonitoringPaused = _shell.IsMonitoringPaused,
            });
        }
        catch (Exception ex)
        {
            Log.Warn("Updates", "Checking whether to install the downloaded update failed.", ex);
        }
    }

    private static async Task SafeCheckAsync(UpdatesViewModel updates, bool isStartup)
    {
        try
        {
            await updates.CheckIfDueAsync(isStartup);
        }
        catch (Exception ex)
        {
            Log.Warn("Updates", "Scheduled update check failed.", ex);
        }
    }

    private void OnWake()
    {
        if (_exiting)
        {
            return;
        }

        // Sensors (the SMC, IOReport's counters) start over after sleep: re-open them once the Mac has settled.
        Log.Info("App", "The Mac woke from sleep; re-opening the sensors shortly.");
        Interlocked.Exchange(ref _lastBusyTick, Environment.TickCount64); // waking isn't "idle"
        _ = Task.Delay(TimeSpan.FromSeconds(3)).ContinueWith(_ =>
        {
            if (!_exiting)
            {
                _host?.Engine.RequestReinitialize();
            }
        }, TaskScheduler.Default);
    }

    // ------------------------------------------------------------------------------------------------
    // Quitting
    // ------------------------------------------------------------------------------------------------

    /// <summary>Saves state, stops monitoring, removes the menu bar item and quits. Safe to call twice.</summary>
    private void Quit()
    {
        if (_exiting && _cleanedUp)
        {
            return;
        }

        Cleanup();
        ReleaseAndShutdown(0);
    }

    private void Cleanup()
    {
        if (_cleanedUp)
        {
            return;
        }

        _cleanedUp = true;
        _exiting = true;
        Log.Info("App", "Quitting.");
        try
        {
            _wake?.Dispose();
            _updateTimer?.Stop();
            _installTimer?.Stop();
            _updates?.Shutdown(); // a download in progress resumes next time

            if (_window is not null && _settings is not null)
            {
                _window.AllowClose = true;
                if ((_window.LastPlacement ?? _window.CapturePlacement()) is { } placement)
                {
                    _settings.Update("Window", s => s.Window = placement);
                }
            }

            _settings?.Flush();
            if (_shell?.Connect is { } connect)
            {
                connect.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(3)); // phones see the Mac go away
            }

            _gameWatcher?.Dispose();
            _gameWatcher = null;
            _overlay?.Dispose();
            _overlay = null;
            _devices?.Dispose();
            _devices = null;
            _menuBar?.Dispose();
            _menuBar = null;

            if (_host is not null)
            {
                _host.Engine.FrameProduced -= OnFrameProduced;
                _host.Dispose();
            }
        }
        catch (Exception ex)
        {
            Log.Error("App", "Error while quitting.", ex);
        }
        finally
        {
            _http?.Dispose();
            _settings?.Dispose();
            Log.Flush();
        }
    }

    private void ReleaseAndShutdown(int exitCode)
    {
        _instance?.Dispose();
        _instance = null;
        Log.Flush();
        if (_fileLog is not null)
        {
            Log.RemoveSink(_fileLog);
            _fileLog.Dispose();
            _fileLog = null;
        }

        _desktop?.Shutdown(exitCode);
    }

    // ------------------------------------------------------------------------------------------------
    // Logging & crash handling
    // ------------------------------------------------------------------------------------------------

    private void InitializeLogging(AppPaths paths, MemoryLogSink memory)
    {
        Log.AddSink(memory);
        try
        {
            paths.EnsureCreated();
            _fileLog = new FileLogSink(paths.LogDirectory);
            Log.AddSink(_fileLog);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("App", "File logging is unavailable.", ex);
        }
    }

    private void RegisterExceptionHandlers()
    {
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            Log.Error("Crash", "Unhandled UI exception.", e.Exception);
            Log.Flush();
            e.Handled = true;
            if (!_exiting && DateTime.UtcNow - _lastCrashDialog > TimeSpan.FromSeconds(30))
            {
                _lastCrashDialog = DateTime.UtcNow;
                AppKit.Alert("Something went wrong in Coreloom", $"{DescribeError(e.Exception)}\n\nThe error was logged. Coreloom will keep running.", confirm: false, warning: true);
            }
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Log.Error("Crash", $"Unhandled exception (terminating: {e.IsTerminating}).", e.ExceptionObject as Exception);
            Log.Flush();
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Warn("Crash", "Unobserved task exception.", e.Exception);
            e.SetObserved();
        };
    }

    /// <summary>The message plus the innermost exception and the first Coreloom frame (the full trace is in the log).</summary>
    internal static string DescribeError(Exception ex)
    {
        var inner = ex;
        while (inner.InnerException is not null && (inner is System.Reflection.TargetInvocationException || inner.Message == ex.Message))
        {
            inner = inner.InnerException;
        }

        string where = (inner.StackTrace ?? ex.StackTrace ?? string.Empty)
            .Split('\n')
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.StartsWith("at Coreloom.", StringComparison.Ordinal)) ?? string.Empty;
        if (where.Length > 0)
        {
            int paren = where.IndexOf('(');
            where = "\n\n" + (paren > 0 ? where[..paren] : where);
        }

        string message = inner == ex ? ex.Message : $"{ex.Message}\n→ {inner.GetType().Name}: {inner.Message}";
        return $"{message}{where}";
    }
}

/// <summary>
/// Command-line options: --minimized and --startup (the login launch agent); --wait-for &lt;pid&gt; (after Move to
/// Applications); --updated and --update-failed (from the update helper); --page &lt;key&gt; opens a page ("gaming",
/// "settings"), also in a copy that's already running.
/// </summary>
internal sealed record MacCommandLine(bool Minimized, bool FromStartup, int? WaitForProcessId, bool Updated, bool UpdateFailed, string? Page = null)
{
    public static MacCommandLine Parse(IReadOnlyList<string> args)
    {
        bool minimized = false;
        bool startup = false;
        bool updated = false;
        bool updateFailed = false;
        int? waitFor = null;
        string? page = null;
        for (int i = 0; i < args.Count; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--minimized" or "-m":
                    minimized = true;
                    break;
                case "--startup":
                    startup = true;
                    break;
                case "--updated":
                    updated = true;
                    break;
                case "--update-failed":
                    updateFailed = true;
                    break;
                case "--wait-for" when i + 1 < args.Count && int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int pid):
                    waitFor = pid;
                    i++;
                    break;
                case "--page" when i + 1 < args.Count && SingleInstance.IsPageKey(args[i + 1].ToLowerInvariant()):
                    page = args[i + 1].ToLowerInvariant();
                    i++;
                    break;
            }
        }

        return new MacCommandLine(minimized, startup, waitFor, updated, updateFailed, page);
    }
}
