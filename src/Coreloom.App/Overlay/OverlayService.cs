using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Coreloom.App.Views;
using Coreloom.Core.Diagnostics;
using Coreloom.Core.Settings;
using Coreloom.ViewModels;
using Coreloom.ViewModels.Overlay;
using Coreloom.ViewModels.Services;

namespace Coreloom.App.Overlay;

/// <summary>
/// Runs the in-game overlay: shows its window over the program in front when it is on, keeps it in its corner of that
/// program's screen, measures frame rates only while a frame-rate reading is shown, and listens for the hotkey (which
/// turns the overlay on and off, also from inside games).
/// <para>
/// It stays out of the game's way: the window is drawn on the processor (never queuing work on the graphics card), the
/// frame-time graph draws from its own low-priority thread, it only claims the top of the screen again when another
/// window has actually covered it, and while a program other than Coreloom is in front Coreloom runs at below-normal
/// priority, so its own work (sensors, the overlay) always gives way to the game's threads.
/// </para>
/// <para>
/// Its place is one of nine on the screen plus a fine-tuning offset, for all games or per game; Shift + the hotkey (or
/// *Move on screen*) lets it be dragged and resized, and where it is dropped is saved. Its size follows the screen it is
/// on when auto-scaling is on.
/// </para>
/// </summary>
internal sealed partial class OverlayService : IDisposable
{
    private const string LogCategory = "Overlay";
    private const string FrameRateOwner = "overlay";
    private static readonly IntPtr HwndTopmost = new(-1);
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint MonitorDefaultToPrimary = 1;
    private const uint GwHwndPrev = 3;
    private const int GwlExStyle = -20;
    private const long WsExTopmost = 0x00000008;

    private readonly ShellViewModel _shell;
    private readonly SettingsService _settings;
    private readonly Dispatcher _dispatcher;
    private readonly OverlayViewModel _overlay;
    private readonly OverlaySettingsViewModel _card;
    private readonly EtwFrameRateSource _frameRates;
    private readonly GlobalHotkey _hotkey;
    private readonly DispatcherTimer _timer;
    private readonly int _ownProcessId = Environment.ProcessId;
    private OverlayWindow? _window;
    private int _lastProgramId;
    private string? _lastProgram;
    private bool _disposed;

    public OverlayService(ShellViewModel shell, SettingsService settings, Dispatcher dispatcher, EtwFrameRateSource frameRates)
    {
        _frameRates = frameRates;
        _shell = shell;
        _settings = settings;
        _dispatcher = dispatcher;
        _overlay = shell.Overlay;
        _card = shell.Gpu.OverlaySettings;
        _overlay.FrameRates = _frameRates;
        _hotkey = new GlobalHotkey(() => _dispatcher.BeginInvoke(Toggle), () => _dispatcher.BeginInvoke(ToggleMoving));
        _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) => Tick();

        settings.Changed += OnSettingsChanged;
        _overlay.PropertyChanged += OnOverlayChanged;
        _card.PropertyChanged += OnCardChanged;
    }

    public void Start() => Apply();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _settings.Changed -= OnSettingsChanged;
        _overlay.PropertyChanged -= OnOverlayChanged;
        _card.PropertyChanged -= OnCardChanged;
        _timer.Stop();
        Yield(false);
        _hotkey.Dispose();
        _frameRates.Release(FrameRateOwner);
        _window?.Close();
        _window = null;
    }

    private OverlaySettings Options => _settings.Current.Overlay;

    private void OnSettingsChanged(string section)
    {
        if (section is "Overlay" or "All")
        {
            Apply();
        }
    }

    private void OnOverlayChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OverlayViewModel.HasContent) or nameof(OverlayViewModel.NeedsFrameRates))
        {
            Apply();
        }
        else if (e.PropertyName is nameof(OverlayViewModel.IsMoving))
        {
            _window?.SetMovable(_overlay.IsMoving);
            Log.Info(LogCategory, _overlay.IsMoving ? "Overlay can be moved and resized." : "Overlay placed.");
            Place();
        }
    }

    /// <summary>Shift + the hotkey: lets the overlay be dragged and resized (turning it on if it's off), or finishes.</summary>
    private void ToggleMoving()
    {
        if (_disposed)
        {
            return;
        }

        if (!Options.Enabled)
        {
            _settings.Update("Overlay", s =>
            {
                s.Overlay.Enabled = true;
                s.Overlay.HasBeenEnabled = true;
            });
        }

        _overlay.IsMoving = !_overlay.IsMoving;
    }

    /// <summary>Saves where the overlay was dropped or resized: the nearest of the nine places and the offset from it.</summary>
    private void SaveAdjusted()
    {
        var window = _window;
        if (window is null || !GetWindowRect(window.Handle, out var rect))
        {
            _overlay.CommitScale();
            return;
        }

        IntPtr monitor = MonitorFromWindow(window.Handle, MonitorDefaultToPrimary);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info))
        {
            _overlay.CommitScale();
            return;
        }

        double scale = GetDpiForMonitor(monitor, 0, out uint dpiX, out _) == 0 && dpiX > 0 ? dpiX / 96.0 : 1;
        var screen = info.Monitor;
        int margin = (int)Math.Round(Options.EdgeMargin * scale);
        var (position, dx, dy) = OverlayPlacements.FromDrop(screen.Left, screen.Top, screen.Right - screen.Left, screen.Bottom - screen.Top,
            rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top, margin);
        _overlay.SavePlacement(position, (int)Math.Round(dx / scale), (int)Math.Round(dy / scale));
    }

    private void OnCardChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OverlaySettingsViewModel.IsCapturingHotkey) or "" or null)
        {
            _hotkey.Paused = _card.IsCapturingHotkey; // typing a new hotkey mustn't flip the overlay
        }
    }

    /// <summary>The hotkey: turns the overlay on or off (and saves it, so the switch on the Graphics page follows).</summary>
    private void Toggle()
    {
        if (!_disposed)
        {
            _settings.Update("Overlay", s => s.Overlay.Enabled = !s.Overlay.Enabled);
        }
    }

    private void Apply()
    {
        if (_disposed)
        {
            return;
        }

        var options = Options;
        var hotkey = options.ActiveHotkey; // none until the overlay has been turned on once
        _hotkey.Set(hotkey.Key, hotkey.Modifiers);

        bool on = options.Enabled;
        if (on && _overlay.NeedsFrameRates)
        {
            if (!_frameRates.Acquire(FrameRateOwner))
            {
                Log.Warn(LogCategory, $"Frame rates unavailable: {_frameRates.Problem}");
            }
        }
        else
        {
            _frameRates.Release(FrameRateOwner);
        }

        _card.RefreshFrameRateNote();

        if (on && _overlay.HasContent)
        {
            if (_window is null)
            {
                _window = new OverlayWindow(_overlay);
                _window.SizeChanged += (_, _) => Place();
                _window.Adjusted += (_, _) => SaveAdjusted();
                _window.SetMovable(_overlay.IsMoving);
            }

            if (!_window.IsVisible)
            {
                _window.Show();
                Log.Info(LogCategory, "Overlay shown.");
            }

            Tick();
            _timer.Start();
        }
        else
        {
            _timer.Stop();
            if (_window is { IsVisible: true })
            {
                _window.Hide();
                Log.Info(LogCategory, "Overlay hidden.");
            }

            _overlay.Tick(null);
            _overlay.IsMoving = false;
            Yield(false);
        }
    }

    private void Tick()
    {
        if (_disposed || _window is null)
        {
            return;
        }

        IntPtr foreground = GetForegroundWindow();
        int? processId = null;
        if (foreground != IntPtr.Zero && GetWindowThreadProcessId(foreground, out uint pid) != 0 && pid != _ownProcessId)
        {
            processId = (int)pid; // Coreloom itself isn't a game: no frame rate while it is in front
        }

        string? program = processId is int id ? ProgramName(id) : null;
        _overlay.Tick(processId, program);
        if (processId is not null)
        {
            // The program the overlay is over: its own place is used when places are kept per game. With Coreloom itself in
            // front the last program stays, so moving the overlay from Coreloom saves it for the game played last.
            _overlay.CurrentGame = program;
        }

        Place();
        Yield(processId is not null);

        // Other topmost windows (some games, launchers) can end up above it: claim the top again, but only then, so the
        // window order the game is composed with isn't touched for nothing.
        if (IsCovered(_window.Handle))
        {
            SetWindowPos(_window.Handle, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
        }
    }

    /// <summary>
    /// While a program other than Coreloom is in front (and the overlay is up), Coreloom runs below normal priority, so
    /// the game's threads always come first; back to normal when Coreloom is in front or the overlay is off (unless
    /// lighter monitoring still asks for it).
    /// </summary>
    private static void Yield(bool yield) => Coreloom.App.Services.ProcessPriority.GiveWay("overlay", yield);

    /// <summary>Whether a visible topmost window of another program is above the overlay and overlaps it.</summary>
    private bool IsCovered(IntPtr hwnd)
    {
        if (!GetWindowRect(hwnd, out var mine))
        {
            return false;
        }

        IntPtr above = GetWindow(hwnd, GwHwndPrev);
        for (int i = 0; above != IntPtr.Zero && i < 64; i++, above = GetWindow(above, GwHwndPrev))
        {
            if (!IsWindowVisible(above) || (GetWindowLongPtr(above, GwlExStyle) & WsExTopmost) == 0)
            {
                continue;
            }

            if (GetWindowThreadProcessId(above, out uint pid) != 0 && pid == _ownProcessId)
            {
                continue; // Coreloom's own (the graph's window sits just above the overlay)
            }

            if (GetWindowRect(above, out var other)
                && other.Left < mine.Right && mine.Left < other.Right && other.Top < mine.Bottom && mine.Top < other.Bottom)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>"Cyberpunk2077.exe" for a process (remembered for the last one asked about).</summary>
    private string? ProgramName(int processId)
    {
        if (processId == _lastProgramId)
        {
            return _lastProgram;
        }

        string? name = null;
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(processId);
            name = process.ProcessName + ".exe";
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // gone already, or not ours to look at
        }

        _lastProgramId = processId;
        _lastProgram = name;
        return name;
    }

    /// <summary>
    /// Puts the overlay in its place on the screen of the window in front: one of nine places plus the offset (for the
    /// game in front when places are kept per game), never off the screen. Also tells the overlay how big that screen
    /// is, for auto-scaling. Not while it is being dragged or resized.
    /// </summary>
    private void Place()
    {
        var window = _window;
        if (window is null || !window.IsVisible || window.IsAdjusting)
        {
            return;
        }

        IntPtr hwnd = window.Handle;
        IntPtr foreground = GetForegroundWindow();
        IntPtr monitor = MonitorFromWindow(foreground != IntPtr.Zero && !_overlay.IsMoving ? foreground : hwnd, MonitorDefaultToPrimary);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info) || !GetWindowRect(hwnd, out var rect))
        {
            return;
        }

        double scale = GetDpiForMonitor(monitor, 0, out uint dpiX, out _) == 0 && dpiX > 0 ? dpiX / 96.0 : 1;
        var screen = info.Monitor;
        _overlay.ScreenScale = OverlayPlacements.ScreenScale((screen.Bottom - screen.Top) / scale);

        var (position, offsetX, offsetY) = _overlay.Placement;
        var (x, y) = OverlayPlacements.Locate(screen.Left, screen.Top, screen.Right - screen.Left, screen.Bottom - screen.Top,
            rect.Right - rect.Left, rect.Bottom - rect.Top, position, (int)Math.Round(Options.EdgeMargin * scale),
            (int)Math.Round(offsetX * scale), (int)Math.Round(offsetY * scale));

        if (x != rect.Left || y != rect.Top)
        {
            SetWindowPos(hwnd, HwndTopmost, x, y, 0, 0, SwpNoSize | SwpNoActivate);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [LibraryImport("user32.dll")]
    private static partial IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [LibraryImport("shcore.dll")]
    private static partial int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetWindow(IntPtr hwnd, uint command);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(IntPtr hwnd);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static partial nint GetWindowLongPtr(IntPtr hwnd, int index);
}
