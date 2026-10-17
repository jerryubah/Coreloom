using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using Coreloom.Core.Diagnostics;
using Coreloom.Core.Overlay;
using Coreloom.Hardware.Native;
using Coreloom.ViewModels.Overlay;

namespace Coreloom.App.Overlay;

/// <summary>
/// The in-game overlay's frame-time graph, drawn by a thread of its own into a see-through window that sits exactly over
/// the graph's place in the overlay (<see cref="LayeredSurface"/>). A WPF window drawn without the graphics card only
/// redraws on the system's 15.6 ms timer tick (at most about 64 times a second, unevenly), which made the graph step;
/// this thread wakes on a precise timer at an even fraction of the display's refresh rate (120 times a second on a 240 Hz
/// display) while a game draws, and four times a second otherwise, however busy the app's window thread is.
/// <para>
/// It costs the game nothing it would notice: the thread runs below normal priority, draws on the processor into a
/// few kilobytes of pixels (<see cref="FrameGraphRaster"/>), allocates nothing per frame, and never waits on the app's
/// window thread or the graphics card; Windows only blends the pixels over the game.
/// </para>
/// </summary>
internal sealed class FrameGraphLayer : IDisposable
{
    private const string LogCategory = "Overlay";
    private static readonly double IdleSeconds = 0.25;

    private readonly LayeredSurface _surface;
    private readonly FrameTimeReader _read;
    private readonly IFrameTimeFeed _feed;
    private readonly ManualResetEvent _stop = new(false);
    private readonly AutoResetEvent _wake = new(false);
    private readonly object _gate = new();
    private readonly Thread _thread;
    private uint _color = 0xFFFFFF;
    private double _opacity = 1;
    private double _scale = 1;
    private int _rate = 60;
    private bool _visible;
    private bool _disposed;

    private FrameGraphLayer(LayeredSurface surface, IFrameTimeFeed feed)
    {
        _surface = surface;
        _feed = feed;
        _read = feed.Read; // one delegate, not one per frame
        _thread = new Thread(Run) { IsBackground = true, Name = "Coreloom frame graph", Priority = ThreadPriority.BelowNormal };
        _thread.Start();
    }

    /// <summary>A layer over <paramref name="window"/> (created on its thread), or null when Windows won't make one.</summary>
    public static FrameGraphLayer? TryCreate(Window window, IFrameTimeFeed feed)
    {
        IntPtr owner = new WindowInteropHelper(window).Handle;
        if (owner == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return new FrameGraphLayer(LayeredSurface.Create(owner), feed);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or DllNotFoundException or EntryPointNotFoundException)
        {
            Log.Warn(LogCategory, "The frame-time graph's own window couldn't be created; it is drawn with the overlay instead.", e);
            return null;
        }
    }

    /// <summary>Where the graph is (screen pixels) and the display's scale and refresh rate. On the window's thread.</summary>
    public void Place(int x, int y, int width, int height, double scale, int refreshHz)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _scale = scale;
            _rate = FrameGraphModel.RedrawRate(refreshHz);
        }

        _surface.Place(x, y, width, height);
        _wake.Set();
    }

    /// <summary>The line's color (0xRRGGBB) and how opaque the graph is.</summary>
    public void SetLook(uint color, double opacity)
    {
        lock (_gate)
        {
            _color = color;
            _opacity = opacity;
        }

        _wake.Set();
    }

    /// <summary>Shows or hides the graph (on the window's thread).</summary>
    public void SetVisible(bool visible)
    {
        lock (_gate)
        {
            if (_disposed || _visible == visible)
            {
                return;
            }

            _visible = visible;
        }

        _surface.SetVisible(visible);
        _wake.Set();
    }

    /// <summary>Stops the thread and destroys the window (on the window's thread).</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _stop.Set();
        _thread.Join(TimeSpan.FromSeconds(2)); // it never waits on this thread, so this is quick
        _surface.Dispose();
        _stop.Dispose();
        _wake.Dispose();
    }

    private void Run()
    {
        Coreloom.App.Services.OverheadMeters.Shared.RegisterCurrentThread("Overlay graph");
        try
        {
            using var timer = new PreciseTimer(_stop, _wake);
            var model = new FrameGraphModel();
            var raster = new FrameGraphRaster();
            uint[] pixels = [];
            long last = Stopwatch.GetTimestamp();
            long next = last;
            bool active = false;
            bool blankShown = false;
            bool fineTimerLogged = timer.UsesFineSystemTimer;
            (int Width, int Height) shownSize = default;
            Log.Info(LogCategory, $"Frame-time graph drawing on its own thread ({(timer.IsHighResolution ? "precise" : "standard")} timer).");

            while (true)
            {
                long now = Stopwatch.GetTimestamp();
                int rate;
                lock (_gate)
                {
                    rate = _rate;
                }

                long interval = (long)(Stopwatch.Frequency * (active ? 1.0 / rate : IdleSeconds));
                next = next + interval > now ? next + interval : now + interval; // keep an even beat; skip missed ones
                int woke = timer.WaitUntil(next);
                if (woke == 0)
                {
                    return; // stop
                }

                if (!fineTimerLogged && timer.UsesFineSystemTimer)
                {
                    fineTimerLogged = true;
                    Log.Info(LogCategory, "The precise timer woke late on this PC; the frame-time graph uses a 1 ms system timer while it draws.");
                }

                if (woke == 1)
                {
                    blankShown = false; // moved, resized or restyled: draw now
                    next = Stopwatch.GetTimestamp();
                }

                now = Stopwatch.GetTimestamp();
                double dt = (now - last) / (double)Stopwatch.Frequency;
                last = now;

                uint color;
                double opacity, scale;
                bool visible;
                lock (_gate)
                {
                    (color, opacity, scale, visible) = (_color, _opacity, _scale, _visible);
                }

                var (width, height) = _surface.Size;
                if (!visible || width < 2 || height < 2)
                {
                    active = false;
                    model.Clear();
                    blankShown = false;
                    continue;
                }

                active = model.Update(_read, _feed.TargetFrameTime, dt);
                if (!active && blankShown && shownSize == (width, height))
                {
                    continue; // nothing drawing: the empty graph is already up
                }

                if (pixels.Length < width * height)
                {
                    pixels = new uint[width * height];
                }

                raster.Draw(pixels, width, height, model, new FrameGraphLook(color, opacity, scale));
                if (_surface.Present(pixels, width, height))
                {
                    shownSize = (width, height);
                    blankShown = !active;
                }
            }
        }
        catch (Exception e)
        {
            Log.Warn(LogCategory, "The frame-time graph's thread stopped.", e);
        }
    }
}
