using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using Coreloom.App.Controls;
using Coreloom.App.Interop;
using Coreloom.App.Services;
using Coreloom.App.Themes;
using Coreloom.App.Views;
using Coreloom.Core;
using Coreloom.Core.Alerts;
using Coreloom.Core.Diagnostics;
using Coreloom.Core.Settings;
using Coreloom.Core.Startup;
using Coreloom.Core.Telemetry;
using Coreloom.Core.Updates;
using Coreloom.Hardware;
using Coreloom.Hardware.Providers;
using Coreloom.ViewModels;
using Coreloom.ViewModels.Infrastructure;
using Coreloom.ViewModels.Services;

namespace Coreloom.App;

/// <summary>
/// Composition root. Owns the single-instance lock, logging, settings, the monitoring host, the tray icon,
/// the frame pump (telemetry thread → UI thread) and alert notifications. Exiting always goes through
/// <see cref="ExitApplication"/> so settings are flushed and the sensor libraries are closed cleanly.
/// </summary>
public partial class App : Application
{
    private static readonly TimeSpan UpdateCheckDelay = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan UpdateCheckPeriod = TimeSpan.FromHours(6);
    private static readonly TimeSpan BackgroundInstallCheckInterval = TimeSpan.FromMinutes(1);

    /// <summary>GPU load metrics checked to tell an idle PC from a busy one (frames carry up to a few GPUs).</summary>
    private static readonly string[] GpuLoadIds = [MetricIds.GpuLoad(0), MetricIds.GpuLoad(1), MetricIds.GpuLoad(2), MetricIds.GpuLoad(3)];
    private const string StartupFailedSplashMessage = "Coreloom couldn't start. The details are in the message window and in the log.";

    private SingleInstance? _instance;
    private SplashScreenHost? _splash;
    private FileLogSink? _fileLog;
    private SettingsService? _settings;
    private MonitoringHost? _host;
    private AlertEngine? _alerts;
    private ShellViewModel? _shell;
    private MainWindow? _window;
    private TrayIconService? _tray;
    private Overlay.OverlayService? _overlay;
    private WindowsDeviceService? _devices;
    private Overlay.EtwFrameRateSource? _frameRates;
    private Services.GamingModeService? _gamingMode;
    private Services.AdaptivePacingService? _adaptivePacing;
    private Services.CoreSchedulingTicker? _coreScheduling;
    private bool _telemetryThreadRegistered;
    private HttpClient? _http;
    private UpdatesViewModel? _updates;
    private DispatcherTimer? _updateTimer;
    private DispatcherTimer? _installTimer;
    private long _lastBusyTick = Environment.TickCount64;
    private int _framePending;
    private volatile bool _exiting;
    private bool _windowClosed;
    private bool _trayHintShown;
    private DateTime _lastCrashDialog = DateTime.MinValue;

    /// <summary>Animation frame rate in use (the fastest attached display, 60–240 Hz).</summary>
    public static int AnimationFrameRate { get; private set; } = 60;

    static App()
    {
        // WPF animates at 60 fps regardless of the monitor. Raise the default to the fastest attached display
        // (e.g. 240 Hz) so transforms and gauges move once per refresh. Must run before any Timeline exists.
        AnimationFrameRate = Math.Clamp(DisplayInfo.MaxRefreshRate(), 60, 240);
        Timeline.DesiredFrameRateProperty.OverrideMetadata(typeof(Timeline), new FrameworkPropertyMetadata { DefaultValue = AnimationFrameRate });
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            Start(e);
        }
        catch (Exception ex)
        {
            // A failure while starting must never leave an invisible process behind (it would also hold the
            // single-instance lock, so the next launch would silently exit).
            Log.Error("App", "Coreloom failed to start.", ex);
            Log.Flush();
            _splash?.ShowError(StartupFailedSplashMessage);
            MessageBox.Show(
                $"Coreloom could not start:\n\n{DescribeError(ex)}\n\nDetails were written to the log folder (%LOCALAPPDATA%\\Coreloom\\Logs).",
                "Coreloom", MessageBoxButton.OK, MessageBoxImage.Error);
            _exiting = true;
            try
            {
                _overlay?.Dispose();
                _tray?.Dispose();
                _host?.Dispose();
            }
            catch (Exception cleanup)
            {
                Log.Warn("App", "Cleanup after a failed start also failed.", cleanup);
            }

            ReleaseAndShutdown(1);
        }
    }

    private void Start(StartupEventArgs e)
    {
        var args = CommandLine.Parse(e.Args);

        if (args.RemoveStartupTask)
        {
            // Run by the uninstaller (which is not elevated and so cannot delete a highest-privilege task itself).
            // No window, no instance lock; the work happens on a pool thread so nothing waits on the dispatcher.
            Task.Run(async () =>
            {
                var startupService = new StartupService();
                await startupService.SetStartAtLogonAsync(enabled: false, minimized: false);
                await startupService.RemoveLegacyEntriesAsync();
            }).GetAwaiter().GetResult();
            Shutdown(0);
            return;
        }

        if (args.ApplyUpdate)
        {
            // Started by the previous version to copy this (new) version over its folder; see RunUpdateHelper.
            RunUpdateHelper(args);
            return;
        }

        // A restart (elevation, settings change, update) passes our old PID: wait for it so the instance lock is free.
        if (args.WaitForProcessId is int pid)
        {
            SingleInstance.WaitForProcessExit(pid, TimeSpan.FromSeconds(15));
        }

        _instance = new SingleInstance("Coreloom");
        if (!_instance.TryAcquire())
        {
            _instance.SignalPrimary();
            _instance.Dispose();
            _instance = null;
            Shutdown(0);
            return;
        }

        var paths = AppPaths.Resolve(AppContext.BaseDirectory);
        var memoryLog = new MemoryLogSink();
        InitializeLogging(paths, memoryLog);
        RegisterExceptionHandlers();

        bool importedSettings = paths.MigratePreviousSettings();
        if (importedSettings)
        {
            Log.Info("App", "Imported settings from the previous product folder.");
            // The old build may have registered a Run entry / scheduled task pointing at its own executable.
            _ = Task.Run(() => new StartupService().RemoveLegacyEntriesAsync());
        }

        var app = AppVersionInfo.FromAssembly(typeof(App).Assembly);
        Log.Info("App", $"Coreloom {app.InformationalVersion} starting (args: {string.Join(' ', e.Args)}; portable: {paths.IsPortable}; installed: {paths.IsInstalled}; elevated: {Environment.IsPrivilegedProcess}; OS: {Environment.OSVersion.VersionString}; animation {AnimationFrameRate} fps).");

        // ---- Settings & theme ---------------------------------------------------------------------
        var store = new SettingsStore(paths.SettingsFile);
        var settings = store.Load();
        var settingsService = new SettingsService(store, settings);
        _settings = settingsService;
        Log.MinimumLevel = settings.Diagnostics.VerboseLogging ? LogLevel.Debug : LogLevel.Info;

        // ---- Startup splash -----------------------------------------------------------------------
        // Only for launches that open a window: not --minimized or "Start minimized" at sign-in (tray only), and
        // never for a second launch or the uninstaller's task cleanup (both returned above). It runs on its own
        // thread, so starting it costs the main thread nothing and it keeps moving while the dashboard is built.
        bool startHidden = SplashPolicy.StartsHidden(args.Minimized, args.FromStartup, settings.Desktop.LaunchMinimized);
        bool reducedMotion = SplashPolicy.UseReducedMotion(settings.Appearance.ReducedMotion, SystemParameters.ClientAreaAnimation);
        if (!startHidden)
        {
            _splash = SplashScreenHost.TryStart(reducedMotion, ThemeService.ResolvesToDark(settings.Appearance.Theme));
        }

        // ---- Updates ------------------------------------------------------------------------------
        // Before anything else is built: pick up what the last session downloaded and, when automatic updates are on,
        // install it now (Coreloom exits, the update replaces the files and starts the new version). A launch that
        // follows an update records it here, so the window can say what's new.
        var shellService = new ShellService();
        var installer = new UpdateInstaller(paths, () => Dispatcher.InvokeAsync(ExitApplication));
        installer.StartCleanup();  // what the last in-place update left next to the app
        _http = new HttpClient(new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(15), AutomaticDecompression = System.Net.DecompressionMethods.All })
        {
            // Applies until response headers arrive; downloads stream afterwards and are cancellable by the user.
            Timeout = TimeSpan.FromSeconds(60),
        };
        IUpdateTrustStore trust = installer.PackageKind == UpdatePackageKind.Installer
            ? new RegistryUpdateTrustStore()
            : new RegistryUpdateTrustStore(FolderUpdate.CopyId(paths.BaseDirectory));
        var updates = new UpdatesViewModel(app, settingsService, installer, shellService, _http, new PendingUpdateStore(paths.DownloadDirectory), trust);
        _updates = updates;
        updates.InitializeFromDisk(launchedAfterFailedUpdate: args.UpdateFailed);
        if (updates.WillInstallAtStartup)
        {
            _splash?.SetStatus($"Installing Coreloom {updates.UpdateVersion}");
        }

        if (updates.TryInstallAtStartup(startHidden))
        {
            Log.Info("App", "Exiting so the update can replace Coreloom's files; the new version starts when it's done.");
            return;
        }

        // Vendor logos are optional files the user supplies (see assets/vendors/README.md); none are bundled.
        BrandLogos.Configure(paths.VendorLogoDirectory);

        // Optional render of the user's PC for the Overview header (see assets/pc/README.md).
        PcImages.Configure(paths.SettingsDirectory);

        var theme = new ThemeService(Resources);
        theme.Initialize(settings.Appearance);

        // ---- Monitoring back-end ------------------------------------------------------------------
        _splash?.SetStatus("Reading system information");
        try
        {
            _host = MonitoringHost.Create(new HardwareOptions
            {
                UseLibreHardwareMonitor = settings.Monitoring.UseLibreHardwareMonitor,
                ScanUsbControllers = settings.Monitoring.ScanUsbControllers,
                PollingInterval = TimeSpan.FromMilliseconds(settings.Monitoring.PollingIntervalMs),
                PreferredNetworkAdapter = () => _settings?.Current.Monitoring.PreferredNetworkAdapter,
            });
        }
        catch (Exception ex)
        {
            Log.Error("App", "Creating the monitoring host failed.", ex);
            Log.Flush();
            _splash?.ShowError(StartupFailedSplashMessage);
            MessageBox.Show($"Coreloom could not start its monitoring engine:\n\n{DescribeError(ex)}\n\nDetails were written to {paths.LogDirectory}.",
                "Coreloom", MessageBoxButton.OK, MessageBoxImage.Error);
            ReleaseAndShutdown(1);
            return;
        }

        var host = _host;
        var network = host.Providers.OfType<NetworkProvider>().FirstOrDefault();
        var monitor = new MonitorContext
        {
            Engine = host.Engine,
            Summary = host.Summary,
            Processes = host.ProcessSampler,
            PrimaryGpuSlot = () => host.PrimaryGpuSlot,
            Gpus = () => host.Services.Gpus.Entries.Select(g => (g.Slot, g.Name, g.IsIntegrated)).ToArray(),
            NetworkAdapters = () => network?.GetAdapters() ?? [],
            LibreHardwareMonitorIncluded = MonitoringHost.LibreHardwareMonitorIncluded,
        };

        var alerts = new AlertEngine(TimeSpan.FromMinutes(settings.Alerts.CooldownMinutes));
        _alerts = alerts;

        // ---- Services ---------------------------------------------------------------------------
        var lifetime = new LifetimeService(() => Dispatcher.InvokeAsync(ExitApplication));
        var diagnostics = new DiagnosticsService(paths.LogDirectory, memoryLog);
        var startup = new StartupService();

        _splash?.SetStatus("Opening dashboard");
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
            Diagnostics = diagnostics,
            Updates = updates,
            App = app,
            Overhead = Services.OverheadMeters.Shared,
            Tools = new ToolServices
            {
                Benchmarks = new Services.WindowsBenchmarkService(paths.SettingsDirectory),
                SpeedTest = new SpeedTestService($"Coreloom/{app.DisplayVersion}"),
                Optimizer = new WindowsOptimizeService(),
                Devices = _devices = new WindowsDeviceService(),
                Gaming = new WindowsGamingService(_frameRates = new Overlay.EtwFrameRateSource(), paths.LocalDirectory, $"Coreloom/{app.DisplayVersion}"),
                CoreScheduling = new WindowsCoreSchedulingService(),
            },
        });
        _shell = shell;

        // ---- Window & tray ----------------------------------------------------------------------
        var window = new MainWindow(shell)
        {
            DesktopSettingsProvider = () => settingsService.Current.Desktop,
        };
        window.ApplyPlacement(settings.Window);
        window.HiddenToTray += (_, _) => ShowTrayHintOnce();
        window.Closed += (_, _) =>
        {
            _windowClosed = true;
            ExitApplication();  // closing without "close to tray" exits the app
        };
        _window = window;

        void OpenUpdates()
        {
            shell.OpenUpdatesCommand.Execute(null);
            ShowMainWindow();
        }

        var tray = new TrayIconService(
            open: ShowMainWindow,
            togglePause: () => shell.IsMonitoringPaused = !shell.IsMonitoringPaused,
            openSettings: () =>
            {
                shell.Navigate(shell.Settings);
                ShowMainWindow();
            },
            checkUpdates: () =>
            {
                OpenUpdates();
                _ = updates.CheckAsync(userInitiated: true);
            },
            installUpdate: () =>
            {
                if (!updates.IsReady)
                {
                    OpenUpdates();  // it downloads first: show the progress
                }

                updates.InstallCommand.Execute(null);
            },
            exit: ExitApplication);
        _tray = tray;

        // The dark-mode icon (dark glass tile) replaces the light one in the window, taskbar and tray while the dark theme
        // is on, and on Coreloom's Start menu, desktop and pinned taskbar shortcuts (Windows draws those from the shortcut);
        // the sidebar, About page and splash take theirs from the theme's "Image.AppIcon*" resources.
        void ApplyAppIcon()
        {
            tray.SetDark(theme.IsDark);
            ShortcutIconService.Apply(theme.IsDark);
            try
            {
                window.Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri(AppIcons.Ico(theme.IsDark), UriKind.Absolute));
            }
            catch (Exception ex)
            {
                Log.Warn("App", "Switching the window icon failed.", ex);
            }
        }

        theme.AppIconChanged += (_, _) => ApplyAppIcon();
        ApplyAppIcon();

        shell.PropertyChanged += (_, pe) =>
        {
            if (pe.PropertyName == nameof(ShellViewModel.IsMonitoringPaused))
            {
                tray.SetPaused(shell.IsMonitoringPaused);
            }
        };

        // ---- Update notifications -------------------------------------------------------------------
        updates.NotificationRequested += note => Dispatcher.InvokeAsync(() =>
        {
            if (note.UserInitiated && _window is { IsVisible: true, IsActive: true })
            {
                return; // the user is looking at Settings › Updates already
            }

            _tray?.Notify(note.Title, note.Message, onClick: OpenUpdates);
        });

        void SyncTrayUpdateItem() => _tray?.SetUpdateItem(updates.ShowInstallButton ? $"Install update to {updates.UpdateVersion}" : null);
        updates.PropertyChanged += (_, pe) =>
        {
            if (pe.PropertyName is nameof(UpdatesViewModel.ShowInstallButton) or nameof(UpdatesViewModel.UpdateVersion))
            {
                SyncTrayUpdateItem();
            }
        };
        SyncTrayUpdateItem();

        if (startHidden && updates.UpdatedNotification is { } updated)
        {
            // Updated in the background and back in the tray: say so once (the window shows a banner instead).
            Dispatcher.InvokeAsync(() => _tray?.Notify(updated.Title, updated.Message, onClick: () =>
            {
                shell.ShowWhatsNewCommand.Execute(null);
                ShowMainWindow();
            }), DispatcherPriority.ApplicationIdle);
        }

        // ---- Frame pump & alerts ----------------------------------------------------------------
        alerts.AlertRaised += OnAlertRaised;
        host.Engine.FrameProduced += OnFrameProduced;

        _instance.ListenForActivation(() => Dispatcher.InvokeAsync(ShowMainWindow));
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        host.Engine.Start();

        // The in-game overlay (off until turned on; its hotkey works from the start).
        try
        {
            _overlay = new Overlay.OverlayService(shell, settingsService, Dispatcher, _frameRates!);
            _overlay.Start();
        }
        catch (Exception ex)
        {
            Log.Error("Overlay", "Starting the in-game overlay failed.", ex);
        }

        // Lighter monitoring while a game is in front, and what Coreloom's own parts cost.
        Services.OverheadMeters.Shared.RegisterCurrentThread("Window and overlay text");
        _gamingMode = new Services.GamingModeService(shell, () => _window, () => settingsService.Current.Monitoring.LighterWhileGaming, Dispatcher);
        _gamingMode.Start();

        // Adaptive frame pacing (does nothing until turned on in Gaming › Frame limiter).
        if (_frameRates is not null)
        {
            _adaptivePacing = new Services.AdaptivePacingService(shell, _gamingMode, _frameRates, () => settingsService.Current.Gaming.AdaptivePacing,
                () => host.Engine.LatestFrame.TryGetValue(MetricIds.GpuLoad(settingsService.Current.Monitoring.PreferredGpuSlot ?? host.PrimaryGpuSlot), out double load) ? load : double.NaN,
                Dispatcher);
            _adaptivePacing.Start();
        }

        // Intelligent CPU core scheduling (does nothing until turned on in Gaming › Performance boosts).
        _coreScheduling = new Services.CoreSchedulingTicker(shell, _gamingMode, _frameRates, () => settingsService.Current.Gaming.CoreScheduling, () =>
        {
            var frame = host.Engine.LatestFrame;
            int count = Math.Min(Environment.ProcessorCount, 256);
            var loads = new double[count];
            bool any = false;
            for (int i = 0; i < count; i++)
            {
                loads[i] = frame.TryGetValue(MetricIds.CpuThreadLoad(i), out double load) ? load : double.NaN;
                any |= !double.IsNaN(loads[i]);
            }

            return any ? loads : null;
        }, Dispatcher);
        _coreScheduling.Start();

        if (startHidden)
        {
            shell.SetWindowVisible(false);
            Log.Info("App", "Started hidden in the notification area.");
        }
        else
        {
            // Shown off-screen first; the splash fades into it once its first frame has been drawn.
            StartupHandoff.ShowMainWindow(window, _splash, reducedMotion);
        }

        // ---- Background housekeeping ------------------------------------------------------------
        _ = shell.Settings.ApplyStartupAsync();  // we are elevated, so refreshing the sign-in task never prompts
        ScheduleUpdateChecks(updates, isStartup: true);
        _ = updates.LoadInstalledNotesIfMissingAsync();  // "What's new" after an update that left no notes behind
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        Log.Info("App", $"Windows session ending ({e.ReasonSessionEnding}).");
        ExitApplication();
        base.OnSessionEnding(e);
    }

    // ------------------------------------------------------------------------------------------------
    // Frames: produced on the telemetry thread. Alerts are evaluated there (cheap, and works while the
    // window is hidden); UI delivery is coalesced so a slow UI never queues more than one frame.
    // ------------------------------------------------------------------------------------------------

    private void OnFrameProduced(TelemetryFrame frame)
    {
        if (!_telemetryThreadRegistered)
        {
            _telemetryThreadRegistered = true; // this runs on the telemetry thread
            Services.OverheadMeters.Shared.RegisterCurrentThread("Sensor reading");
        }

        try
        {
            _alerts?.Evaluate(frame);
        }
        catch (Exception ex)
        {
            Log.WarnThrottled("alerts", TimeSpan.FromMinutes(5), "Alerts", "Evaluating alert rules failed.", ex);
        }

        // Remember when the PC was last busy: a downloaded update is only installed in the background after the CPU
        // and GPU have been quiet for a while (see UpdateInstallPolicy).
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

        Dispatcher.InvokeAsync(DeliverLatestFrame, DispatcherPriority.Normal);
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
            _tray?.SetTooltip(_shell.TrayText);
        }
        catch (Exception ex)
        {
            // A UI bug must never stop monitoring; log it (throttled) and keep going.
            Log.WarnThrottled("frame-ui", TimeSpan.FromMinutes(1), "UI", "Updating the UI with a new frame failed.", ex);
        }
    }

    private void OnAlertRaised(AlertEvent alert)
    {
        var settings = _settings;
        if (settings is null)
        {
            return;
        }

        string message = DescribeAlert(alert, settings);
        Log.Info("Alerts", $"{alert.Rule.Name}: {message}");
        Dispatcher.InvokeAsync(() => _tray?.Notify(alert.Rule.Name, message, warning: true, onClick: ShowMainWindow));
    }

    internal static string DescribeAlert(AlertEvent alert, SettingsService settings)
    {
        var formatter = settings.Formatter;
        var kind = alert.Metric?.Kind ?? MetricKind.Load;
        var style = ValueStyles.ForKind(kind);
        string value = ValueStyles.Format(style, alert.Value, formatter).ToString();
        string threshold = ValueStyles.Format(style, alert.Rule.Threshold, formatter).ToString();
        string name = alert.Metric?.Name ?? alert.Rule.MetricId;
        string direction = alert.Rule.Comparison == AlertComparison.Above ? "above" : "below";
        string sustained = alert.Rule.SustainSeconds > 0
            ? string.Format(CultureInfo.CurrentCulture, " for {0} s", alert.Rule.SustainSeconds)
            : string.Empty;
        return $"{name} is {value} — {direction} your {threshold} threshold{sustained}.";
    }

    // ------------------------------------------------------------------------------------------------
    // Window / tray
    // ------------------------------------------------------------------------------------------------

    private void ShowMainWindow()
    {
        if (_exiting || _window is null)
        {
            return;
        }

        _window.ShowFromTray();
    }

    private void ShowTrayHintOnce()
    {
        if (_trayHintShown || _settings?.Current.Desktop.TrayHintShown == true)
        {
            return;
        }

        _trayHintShown = true;
        _settings?.Update("Desktop", s => s.Desktop.TrayHintShown = true);
        _tray?.Notify("Coreloom is still running", "Monitoring and alerts continue in the notification area. Right-click the icon to exit.");
    }

    // ------------------------------------------------------------------------------------------------
    // Updates & power events
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

        // A downloaded update also installs while Coreloom sits in the tray and nobody is using the PC.
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
            var signals = new BackgroundInstallSignals
            {
                WindowInUse = window is { IsVisible: true } && window.WindowState != WindowState.Minimized,
                UserIdle = UserIdle.SinceLastInput(),
                SystemQuiet = TimeSpan.FromMilliseconds(Math.Max(0, Environment.TickCount64 - Interlocked.Read(ref _lastBusyTick))),
                MonitoringPaused = _shell.IsMonitoringPaused,
            };
            updates.TryInstallInBackground(signals);
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

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume)
        {
            return;
        }

        // Drivers (NVML in particular) can need a moment after resume. Re-initialize providers afterwards.
        Log.Info("App", "System resumed from sleep; re-initializing sensor providers shortly.");
        _ = ReinitializeAfterResumeAsync();

        // Sleep isn't "idle": restart the quiet clock so a downloaded update never installs right as the PC wakes.
        Interlocked.Exchange(ref _lastBusyTick, Environment.TickCount64);
    }

    private async Task ReinitializeAfterResumeAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        if (!_exiting)
        {
            _host?.Engine.RequestReinitialize();  // thread-safe: sets a flag the telemetry thread picks up
        }
    }

    // ------------------------------------------------------------------------------------------------
    // In-place update helper
    // ------------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>--apply-update</c>: this process is the new version, unpacked into <c>&lt;app&gt;\.coreloom-update\new</c> by the
    /// version that is exiting. It waits for that version to exit, copies itself over the app folder (every change is
    /// undone if a file can't be replaced) and starts Coreloom again: the new version with <c>--updated</c>, or the old
    /// one with <c>--update-failed</c>. Later versions must keep accepting this switch (see <see cref="FolderUpdate"/>).
    /// </summary>
    private void RunUpdateHelper(CommandLine args)
    {
        if (!FolderUpdate.TryGetAppDirectoryFromStagingLocation(AppContext.BaseDirectory, out string appDirectory))
        {
            Shutdown(2);  // not an unpacked update next to a Coreloom folder: nothing to do
            return;
        }

        var paths = AppPaths.Resolve(appDirectory);
        InitializeLogging(paths, new MemoryLogSink());
        var app = AppVersionInfo.FromAssembly(typeof(App).Assembly);
        Log.Info("Updates", $"Updating {appDirectory} to Coreloom {app.InformationalVersion} (args: {string.Join(' ', Environment.GetCommandLineArgs().Skip(1))}).");
        if (!args.Quiet)
        {
            _splash = SplashScreenHost.TryStart(SplashPolicy.UseReducedMotion(false, SystemParameters.ClientAreaAnimation), darkIcon: true, $"Updating Coreloom to {app.DisplayVersion}");
        }

        if (args.WaitForProcessId is int pid)
        {
            SingleInstance.WaitForProcessExit(pid, TimeSpan.FromSeconds(60));
        }

        // Hold the instance lock while files change, so Coreloom can't be started halfway through. The relaunched
        // copy waits for this process to exit (--wait-for), which releases it.
        _instance = new SingleInstance("Coreloom");
        var deadline = Stopwatch.StartNew();
        bool locked;
        while (!(locked = _instance.TryAcquire()) && deadline.Elapsed < TimeSpan.FromSeconds(30))
        {
            Thread.Sleep(250);
        }

        if (!locked)
        {
            Log.Warn("Updates", "Coreloom is still running, so its files can't be replaced. The update will be offered again.");
            ReleaseAndShutdown(1);
            return;
        }

        FolderUpdateResult result;
        try
        {
            result = FolderUpdate.Apply(appDirectory);
        }
        catch (Exception ex)
        {
            Log.Error("Updates", "Updating the files failed.", ex);
            result = new FolderUpdateResult(false, 0, ex.Message);
        }

        _splash?.SetStatus(result.Succeeded ? "Starting Coreloom" : "The update couldn't finish. Starting Coreloom again");
        string relaunch = (result.Succeeded ? "--updated" : "--update-failed")
            + $" --wait-for {Environment.ProcessId.ToString(CultureInfo.InvariantCulture)}"
            + (args.Minimized ? " --minimized" : string.Empty);
        try
        {
            using var process = Process.Start(new ProcessStartInfo(Path.Combine(appDirectory, FolderUpdate.AppExecutable), relaunch)
            {
                UseShellExecute = true,
                WorkingDirectory = appDirectory,
            });
            Log.Info("Updates", result.Succeeded
                ? $"Updated {result.FilesCopied} files; starting Coreloom {app.DisplayVersion}."
                : $"The update failed ({result.Error}); starting the previous version again.");
        }
        catch (Exception ex)
        {
            Log.Error("Updates", "Starting Coreloom after the update failed.", ex);
        }

        ReleaseAndShutdown(result.Succeeded ? 0 : 1);
    }

    // ------------------------------------------------------------------------------------------------
    // Exit
    // ------------------------------------------------------------------------------------------------

    /// <summary>Saves state, stops monitoring, removes the tray icon and shuts down. Safe to call twice.</summary>
    private void ExitApplication()
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        Log.Info("App", "Exiting.");
        try
        {
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            _updateTimer?.Stop();
            _installTimer?.Stop();
            _updates?.Shutdown();  // a download in progress resumes next time

            if (_window is not null && _settings is not null)
            {
                _window.AllowClose = true;
                if (!_windowClosed)
                {
                    _window.Close();  // captures LastPlacement while closing
                }

                if (_window.LastPlacement is { } placement)
                {
                    _settings.Update("Window", s => s.Window = placement);
                }
            }

            _settings?.Flush();
            _adaptivePacing?.Dispose();
            _adaptivePacing = null;
            _coreScheduling?.Dispose();
            _coreScheduling = null;
            _gamingMode?.Dispose();
            _gamingMode = null;
            _overlay?.Dispose();
            _overlay = null;
            _frameRates?.Dispose();
            _frameRates = null;
            _devices?.Dispose();
            _devices = null;
            _tray?.Dispose();
            _tray = null;

            if (_host is not null)
            {
                _host.Engine.FrameProduced -= OnFrameProduced;
                _host.Dispose();
            }
        }
        catch (Exception ex)
        {
            Log.Error("App", "Error during shutdown.", ex);
        }
        finally
        {
            _http?.Dispose();
            _settings?.Dispose();
            ReleaseAndShutdown(0);
        }
    }

    private void ReleaseAndShutdown(int exitCode)
    {
        _splash?.Close();  // no-op once the handoff has finished
        _splash = null;
        _instance?.Dispose();
        _instance = null;
        Log.Flush();
        if (_fileLog is not null)
        {
            Log.RemoveSink(_fileLog);
            _fileLog.Dispose();
            _fileLog = null;
        }

        Shutdown(exitCode);
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
            // Logging to disk is best-effort (e.g. a read-only portable folder); the in-memory log still works.
            Log.Warn("App", "File logging is unavailable.", ex);
        }
    }

    private void RegisterExceptionHandlers()
    {
        DispatcherUnhandledException += (_, e) =>
        {
            Log.Error("Crash", "Unhandled UI exception.", e.Exception);
            Log.Flush();
            e.Handled = true;
            if (!_exiting && DateTime.UtcNow - _lastCrashDialog > TimeSpan.FromSeconds(30))
            {
                _lastCrashDialog = DateTime.UtcNow;
                MessageBox.Show($"Something went wrong in Coreloom:\n\n{DescribeError(e.Exception)}\n\nThe error was logged. Coreloom will keep running.",
                    "Coreloom", MessageBoxButton.OK, MessageBoxImage.Warning);
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

    /// <summary>
    /// Message plus the innermost exception type and the first Coreloom frame, so a screenshot of the dialog is
    /// enough to locate the problem. The full stack trace is always in the log file.
    /// </summary>
    internal static string DescribeError(Exception ex)
    {
        var inner = ex;
        while (inner.InnerException is not null && (inner is System.Reflection.TargetInvocationException || inner is System.Windows.Markup.XamlParseException || inner.Message == ex.Message))
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
/// Command-line options: --minimized, --startup, --wait-for &lt;pid&gt;, --remove-startup-task; after an in-app update
/// (from the installer or the update helper) --updated (informational) or --update-failed (the update was rolled back);
/// and for the update helper itself --apply-update and --quiet (no splash while copying).
/// </summary>
internal sealed record CommandLine(bool Minimized, bool FromStartup, int? WaitForProcessId, bool RemoveStartupTask = false, bool UpdateFailed = false,
    bool ApplyUpdate = false, bool Quiet = false)
{
    public static CommandLine Parse(IReadOnlyList<string> args)
    {
        bool minimized = false;
        bool startup = false;
        bool removeTask = false;
        bool updateFailed = false;
        bool applyUpdate = false;
        bool quiet = false;
        int? waitFor = null;
        for (int i = 0; i < args.Count; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--minimized" or "/minimized" or "-m":
                    minimized = true;
                    break;
                case "--startup":
                    startup = true;
                    break;
                case "--remove-startup-task":
                    removeTask = true;
                    break;
                case "--update-failed":
                    updateFailed = true;
                    break;
                case FolderUpdate.ApplySwitch:
                    applyUpdate = true;
                    break;
                case FolderUpdate.QuietSwitch:
                    quiet = true;
                    break;
                case "--wait-for" when i + 1 < args.Count && int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int pid):
                    waitFor = pid;
                    i++;
                    break;
            }
        }

        return new CommandLine(minimized, startup, waitFor, removeTask, updateFailed, applyUpdate, quiet);
    }
}
