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
    private int _ticks;
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
        _hotkey = new GlobalHotkey(() => _dispatcher.BeginInvoke(Toggle));
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

        _overlay.Tick(processId, processId is int id ? ProgramName(id) : null);
        Place();

        // Other topmost windows (some games, launchers) can end up above it; claim the top again now and then.
        if (++_ticks % 4 == 0)
        {
            SetWindowPos(_window.Handle, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
        }
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

    /// <summary>Puts the overlay in its place on the screen of the window in front.</summary>
    private void Place()
    {
        var window = _window;
        if (window is null || !window.IsVisible)
        {
            return;
        }

        IntPtr hwnd = window.Handle;
        IntPtr foreground = GetForegroundWindow();
        IntPtr monitor = MonitorFromWindow(foreground != IntPtr.Zero ? foreground : hwnd, MonitorDefaultToPrimary);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info) || !GetWindowRect(hwnd, out var rect))
        {
            return;
        }

        double scale = GetDpiForMonitor(monitor, 0, out uint dpiX, out _) == 0 && dpiX > 0 ? dpiX / 96.0 : 1;
        int margin = (int)Math.Round(Options.EdgeMargin * scale);
        var screen = info.Monitor;
        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;
        var position = Options.Position;

        int x = position switch
        {
            OverlayPosition.TopLeft or OverlayPosition.MiddleLeft or OverlayPosition.BottomLeft => screen.Left + margin,
            OverlayPosition.TopCenter or OverlayPosition.BottomCenter => screen.Left + ((screen.Right - screen.Left - width) / 2),
            _ => screen.Right - margin - width,
        };
        int y = position switch
        {
            OverlayPosition.TopLeft or OverlayPosition.TopCenter or OverlayPosition.TopRight => screen.Top + margin,
            OverlayPosition.MiddleLeft or OverlayPosition.MiddleRight => screen.Top + ((screen.Bottom - screen.Top - height) / 2),
            _ => screen.Bottom - margin - height,
        };

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
}
