using System.ComponentModel;
using Avalonia.Threading;
using Coreloom.Core.Diagnostics;
using Coreloom.Core.Overlay;
using Coreloom.Core.Settings;
using Coreloom.Mac.Native;
using Coreloom.ViewModels;
using Coreloom.ViewModels.Overlay;
using Coreloom.ViewModels.Services;

namespace Coreloom.Mac.Overlay;

/// <summary>
/// Runs the readings window (the in-game overlay on a Mac): shows it over the app in front when it is on, keeps it in its
/// corner of that app's screen (one of nine places plus a fine-tuning offset, for all games or per game) and listens for
/// the hotkey. Frame rates aren't among its readings: macOS doesn't let one app measure another's frames (Apple's Metal
/// Performance HUD, a switch on the Gaming page, shows them inside Metal games).
/// </summary>
internal sealed class MacOverlayService : IDisposable
{
    private const string LogCategory = "Overlay";

    private readonly SettingsService _settings;
    private readonly OverlayViewModel _overlay;
    private readonly OverlaySettingsViewModel _card;
    private readonly GlobalHotkey _hotkey;
    private readonly DispatcherTimer _timer;
    private readonly int _ownProcessId = Environment.ProcessId;
    private OverlayWindow? _window;
    private Avalonia.Size _grown; // the window's size while it's shown: it only grows (see Place)
    private bool _disposed;

    public MacOverlayService(ShellViewModel shell, SettingsService settings)
    {
        _settings = settings;
        _overlay = shell.Overlay;
        _card = shell.Gpu.OverlaySettings;
        _overlay.FrameRates = NoFrameRates.Instance;
        _card.Preview.FrameRates = NoFrameRates.Instance;
        _hotkey = new GlobalHotkey(() => Dispatcher.UIThread.Post(Toggle), () => Dispatcher.UIThread.Post(ToggleMoving));
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, (_, _) => Tick());
        _timer.Stop();

        settings.Changed += OnSettingsChanged;
        _overlay.PropertyChanged += OnOverlayChanged;
        _card.PropertyChanged += OnCardChanged;
    }

    private OverlaySettings Options => _settings.Current.Overlay;

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
        _window?.Close();
        _window = null;
    }

    private void OnSettingsChanged(string section)
    {
        if (section is "Overlay" or "All")
        {
            Apply();
        }
    }

    private void OnOverlayChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OverlayViewModel.HasContent))
        {
            Apply();
        }
        else if (e.PropertyName is nameof(OverlayViewModel.IsMoving))
        {
            _window?.SetMovable(_overlay.IsMoving);
            Log.Info(LogCategory, _overlay.IsMoving ? "Readings window can be moved and resized." : "Readings window placed.");
            _grown = default;
            Place();
        }
    }

    private void OnCardChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OverlaySettingsViewModel.IsCapturingHotkey) or "" or null)
        {
            _hotkey.Paused = _card.IsCapturingHotkey; // typing a new hotkey mustn't flip the window
        }
    }

    /// <summary>The hotkey: turns the window on or off (and saves it, so the switch on the Graphics page follows).</summary>
    private void Toggle()
    {
        if (!_disposed)
        {
            _settings.Update("Overlay", s => s.Overlay.Enabled = !s.Overlay.Enabled);
        }
    }

    /// <summary>Shift + the hotkey: lets the window be dragged and resized (turning it on if it's off), or finishes.</summary>
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

    private void Apply()
    {
        if (_disposed)
        {
            return;
        }

        var options = Options;
        var hotkey = options.ActiveHotkey; // none until the window has been turned on once
        _hotkey.Set(hotkey.Key, hotkey.Modifiers);
        _card.RefreshFrameRateNote();

        if (options.Enabled && _overlay.HasContent)
        {
            if (_window is null)
            {
                _window = new OverlayWindow(_overlay);
                _window.ContentSizeChanged += (_, _) => Place();
                _window.Adjusted += (_, _) => SaveAdjusted();
                _window.SetMovable(_overlay.IsMoving);
            }

            if (!_window.IsVisible)
            {
                _grown = default;
                _window.Show();
                Log.Info(LogCategory, "Readings window shown.");
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
                Log.Info(LogCategory, "Readings window hidden.");
            }

            _overlay.Tick(null);
            _overlay.IsMoving = false;
        }
    }

    private void Tick()
    {
        if (_disposed || _window is null)
        {
            return;
        }

        var (bundle, pid) = AppKit.FrontmostApplication();
        int? processId = pid != 0 && pid != _ownProcessId ? pid : null;
        string? program = processId is not null && bundle is not null ? Path.GetFileName(bundle) : null;
        _overlay.Tick(processId, program);
        if (program is not null)
        {
            // With Coreloom itself in front the last app stays, so moving the window from Coreloom saves it for the game played last.
            _overlay.CurrentGame = program;
        }

        Place();
        OverlayNative.KeepOnTop(_window.NativeHandle);
    }

    /// <summary>
    /// Puts the readings in their place on the screen of the app in front (on its own screen while they're being moved),
    /// never off the screen, and tells them how big that screen is (for auto-scaling). Not while being dragged or resized.
    /// <para>
    /// The readings sit at the window's top left. While they're being moved the window is exactly their size (it takes
    /// the clicks); otherwise it is at least their size, in steps of 64 points, and only grows while it's shown. So
    /// turning readings on and off, or a value getting wider, redraws the readings in a window that stays the same size:
    /// macOS showed the last picture stretched or squeezed while a resized window caught up (1.8.3). When it does grow,
    /// it is moved and sized in one step.
    /// </para>
    /// </summary>
    private void Place()
    {
        var window = _window;
        if (window is null || !window.IsVisible || window.IsAdjusting)
        {
            return;
        }

        nint handle = window.NativeHandle;
        var content = window.ContentSize;
        if (content.Width < 1 || content.Height < 1
            || (_overlay.IsMoving ? OverlayNative.ScreenOf(handle) : OverlayNative.FrontScreen()) is not { } screen)
        {
            return;
        }

        _overlay.ScreenScale = OverlayPlacements.ScreenScale(screen.Height);
        double contentWidth = Math.Ceiling(content.Width), contentHeight = Math.Ceiling(content.Height);
        var (position, offsetX, offsetY) = _overlay.Placement;
        var (x, y) = OverlayPlacements.Locate(0, 0, (int)screen.Width, (int)screen.Height, (int)contentWidth, (int)contentHeight,
            position, Options.EdgeMargin, offsetX, offsetY);

        double width, height;
        if (_overlay.IsMoving)
        {
            (width, height) = (contentWidth, contentHeight);
        }
        else
        {
            width = Math.Max(_grown.Width, Step(contentWidth));
            height = Math.Max(_grown.Height, Step(contentHeight));
            _grown = new Avalonia.Size(width, height);
        }

        // Cocoa measures from the bottom left of the screen; the readings' top left is the window's.
        double left = screen.X + x;
        double bottom = screen.Y + screen.Height - y - height;
        var frame = OverlayNative.Frame(handle);
        if (Math.Abs(width - frame.Width) > 0.5 || Math.Abs(height - frame.Height) > 0.5)
        {
            OverlayNative.SetFrame(handle, left, bottom, width, height);
        }
        else if (Math.Abs(left - frame.X) > 0.5 || Math.Abs(bottom - frame.Y) > 0.5)
        {
            OverlayNative.SetOrigin(handle, left, bottom);
        }

        static double Step(double size) => Math.Ceiling((size + 16) / 64) * 64;
    }

    /// <summary>Saves where the window was dropped or resized: the nearest of the nine places and the offset from it.</summary>
    private void SaveAdjusted()
    {
        var window = _window;
        if (window is null || OverlayNative.ScreenOf(window.NativeHandle) is not { } screen)
        {
            _overlay.CommitScale();
            return;
        }

        var frame = OverlayNative.Frame(window.NativeHandle);
        int top = (int)Math.Round(screen.Y + screen.Height - frame.Y - frame.Height);
        var (position, dx, dy) = OverlayPlacements.FromDrop(0, 0, (int)screen.Width, (int)screen.Height,
            (int)Math.Round(frame.X - screen.X), top, (int)Math.Round(frame.Width), (int)Math.Round(frame.Height), Options.EdgeMargin);
        _overlay.SavePlacement(position, dx, dy);
    }

    /// <summary>macOS gives no way to read another app's frame rate (without injecting code into it, which Coreloom never does).</summary>
    private sealed class NoFrameRates : IFrameRateSource
    {
        public static readonly NoFrameRates Instance = new();

        public bool IsAvailable => false;

        public string? Problem => "macOS doesn't let other apps measure a game's frame rate. Turn on Metal Performance HUD on the Gaming page to see it inside Metal games.";

        public FrameRateStats? Get(int processId) => null;

        public int RecentFrameTimes(int processId, Span<double> buffer, out double newestAgeMilliseconds)
        {
            newestAgeMilliseconds = 0;
            return 0;
        }
    }
}
