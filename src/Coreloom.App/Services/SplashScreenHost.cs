using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Coreloom.App.Interop;
using Coreloom.App.Views;
using Coreloom.Core.Diagnostics;

namespace Coreloom.App.Services;

/// <summary>
/// Runs the startup splash on its own UI thread. WPF animations tick on the thread that owns them, and the main
/// thread is busy building the dashboard during startup, so a splash on the main thread would freeze exactly when
/// it is needed. This thread is a background thread: it can never keep the process alive, and its window is
/// destroyed with it. Every member is safe to call from any thread and does nothing once the splash has closed.
/// </summary>
internal sealed class SplashScreenHost
{
    private static readonly TimeSpan CrossThreadTimeout = TimeSpan.FromMilliseconds(250);

    private readonly object _gate = new();
    private readonly bool _reducedMotion;
    private readonly bool _darkIcon;
    private Dispatcher? _dispatcher;
    private SplashWindow? _window;
    private string _status;
    private long _shownAt;
    private bool _closeRequested;
    private bool _ended;

    private SplashScreenHost(bool reducedMotion, string status, bool darkIcon)
    {
        _reducedMotion = reducedMotion;
        _status = status;
        _darkIcon = darkIcon;
    }

    /// <summary>False once the splash has closed, failed or been asked to close.</summary>
    public bool IsActive
    {
        get
        {
            lock (_gate)
            {
                return !_ended && !_closeRequested;
            }
        }
    }

    /// <summary>True once the splash has drawn its first frame on screen.</summary>
    public bool HasAppeared => Interlocked.Read(ref _shownAt) != 0;

    /// <summary>How long the splash has been on screen (zero until its first frame).</summary>
    public TimeSpan VisibleFor
    {
        get
        {
            long shown = Interlocked.Read(ref _shownAt);
            return shown == 0 ? TimeSpan.Zero : Stopwatch.GetElapsedTime(shown);
        }
    }

    /// <summary>
    /// Starts the splash thread and returns at once; the window appears as soon as that thread has built it.
    /// <paramref name="darkIcon"/> shows the dark-mode icon (the dark theme is on).
    /// </summary>
    public static SplashScreenHost? TryStart(bool reducedMotion, bool darkIcon, string status = "Starting Coreloom")
    {
        try
        {
            var host = new SplashScreenHost(reducedMotion, status, darkIcon);
            var thread = new Thread(host.Run)
            {
                IsBackground = true,
                Name = "Coreloom splash",
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return host;
        }
        catch (Exception ex)
        {
            Log.Warn("Startup", "The splash screen could not be started; continuing without it.", ex);
            return null;
        }
    }

    /// <summary>Time since Windows started this process (for the startup timings in the log).</summary>
    public static TimeSpan SinceProcessStart()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            return DateTime.Now - process.StartTime;
        }
        catch (Exception)
        {
            return TimeSpan.Zero;
        }
    }

    public void SetStatus(string status)
    {
        lock (_gate)
        {
            _status = status;
        }

        Post(window => window.SetStatus(status));
    }

    /// <summary>
    /// Switches the splash to its error look; the app then shows the details in a dialog and closes the splash.
    /// A splash that isn't on screen yet is cancelled instead, so it can never appear on top of that dialog.
    /// </summary>
    public void ShowError(string message)
    {
        if (!HasAppeared)
        {
            Close();
            return;
        }

        Post(window => window.ShowError(message));
    }

    /// <summary>
    /// Puts the splash just above the main window for the crossfade, without activating it. Done on the splash's
    /// own thread with a short time limit, so a stalled splash can never hold up the main window.
    /// </summary>
    public bool BringToFront()
    {
        Dispatcher? dispatcher;
        SplashWindow? window;
        lock (_gate)
        {
            if (_ended)
            {
                return false;
            }

            dispatcher = _dispatcher;
            window = _window;
        }

        if (dispatcher is null || window is null)
        {
            return false;
        }

        try
        {
            var operation = dispatcher.InvokeAsync(() => NativeWindow.BringToTopWithoutActivating(window.SplashHandle));
            return operation.Wait(CrossThreadTimeout) == DispatcherOperationStatus.Completed && operation.Result;
        }
        catch (Exception ex) when (ex is InvalidOperationException or TaskCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// Fades the splash out and closes it; its thread then ends. A splash that never reached the screen is just
    /// cancelled, so it can't pop up over the app after the handoff.
    /// </summary>
    public void FadeOutAndClose()
    {
        if (!HasAppeared)
        {
            Close();
            return;
        }

        lock (_gate)
        {
            _closeRequested = true;
        }

        Post(window => window.FadeOutAndClose(), ignoreCloseRequest: true);
    }

    /// <summary>Closes the splash immediately (exit paths). Safe to call more than once.</summary>
    public void Close()
    {
        lock (_gate)
        {
            _closeRequested = true;
        }

        Post(window => window.CloseOnce(), ignoreCloseRequest: true);
    }

    private void Post(Action<SplashWindow> action, bool ignoreCloseRequest = false)
    {
        Dispatcher? dispatcher;
        SplashWindow? window;
        lock (_gate)
        {
            if (_ended || (_closeRequested && !ignoreCloseRequest))
            {
                return;
            }

            dispatcher = _dispatcher;
            window = _window;
        }

        if (dispatcher is null || window is null)
        {
            return;  // not built yet: the thread picks up the latest status and any close request itself
        }

        try
        {
            dispatcher.InvokeAsync(() =>
            {
                try
                {
                    action(window);
                }
                catch (Exception ex)
                {
                    Log.Warn("Startup", "A splash screen update failed; closing the splash.", ex);
                    window.CloseOnce();
                }
            });
        }
        catch (Exception ex) when (ex is InvalidOperationException or TaskCanceledException)
        {
            // The splash thread is shutting down.
        }
    }

    private void Run()
    {
        try
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.UnhandledException += (_, e) =>
            {
                // A splash problem must never take the app down or leave a frozen window: log it and end the splash.
                Log.Warn("Startup", "The splash screen failed and was closed.", e.Exception);
                e.Handled = true;
                lock (_gate)
                {
                    _closeRequested = true;
                }

                dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
            };

            string status;
            lock (_gate)
            {
                status = _status;
            }

            var window = new SplashWindow(_reducedMotion, status, _darkIcon);
            window.ContentRendered += (_, _) =>
            {
                Interlocked.CompareExchange(ref _shownAt, Stopwatch.GetTimestamp(), 0);
                Log.Info("Startup", $"Splash on screen {SinceProcessStart().TotalMilliseconds:0} ms after the process started (reduced motion: {_reducedMotion}).");
            };
            window.Closed += (_, _) =>
            {
                lock (_gate)
                {
                    _closeRequested = true;
                    _window = null;  // nothing more is posted to a closed window
                }

                dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
            };

            lock (_gate)
            {
                if (_closeRequested)
                {
                    return;  // startup finished (or failed) before the splash was even built
                }

                _dispatcher = dispatcher;
                _window = window;
                if (!string.Equals(_status, status, StringComparison.Ordinal))
                {
                    window.SetStatus(_status);
                }
            }

            window.Show();
            Dispatcher.Run();
        }
        catch (Exception ex)
        {
            Log.Warn("Startup", "The splash screen could not be shown; continuing without it.", ex);
        }
        finally
        {
            lock (_gate)
            {
                _ended = true;
                _window = null;
                _dispatcher = null;
            }
        }
    }
}
