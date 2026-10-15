using System.Windows;
using System.Windows.Media;
using Coreloom.ViewModels.Overlay;

namespace Coreloom.App.Controls;

/// <summary>
/// The overlay's frame-time graph: the time each of the latest frames took, newest at the right, as a line over a soft
/// fill. Even pacing is a flat line; a stutter is a spike, tinted amber or red by how far it jumps above the typical frame.
/// When the frame pacer limits the game, a dashed line marks its target. The scale follows the frames smoothly so it
/// doesn't jump. It reads <see cref="Feed"/> about 30 times a second while shown and redraws only while frames come in.
/// <para>
/// In the live overlay (<see cref="IsLiveProperty"/>, set on its window) it shows only real frames; in the preview in
/// the settings it shows a still sample trace while no game is running, so the look can be judged.
/// </para>
/// </summary>
public sealed class FrameGraph : FrameworkElement
{
    public static readonly DependencyProperty FeedProperty = DependencyProperty.Register(
        nameof(Feed), typeof(IFrameTimeFeed), typeof(FrameGraph), new PropertyMetadata(null, (d, _) => ((FrameGraph)d).Hook()));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(FrameGraph), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Inherited: true inside the live overlay window (no sample trace there).</summary>
    public static readonly DependencyProperty IsLiveProperty = DependencyProperty.RegisterAttached(
        "IsLive", typeof(bool), typeof(FrameGraph), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    private const int Frames = 160;
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(33);
    private static readonly Color Warm = Color.FromRgb(0xFF, 0xB5, 0x47);
    private static readonly Color Hot = Color.FromRgb(0xFF, 0x5C, 0x5C);
    private static readonly Color Paced = Color.FromRgb(0x45, 0xD6, 0xA0);

    private static readonly TimeSpan SampleInterval = TimeSpan.FromMilliseconds(500);

    private readonly double[] _buffer = new double[Frames];
    private readonly double[] _incoming = new double[Frames];
    private bool _showingSample;
    private readonly double[] _sorted = new double[Frames];
    private int _count;
    private double _scale = 20;   // milliseconds at the top of the graph (eases toward what the frames need)
    private double _target;
    private bool _hooked;
    private TimeSpan _lastDraw;

    public FrameGraph()
    {
        IsHitTestVisible = false;
        Loaded += (_, _) => Hook();
        Unloaded += (_, _) => Unhook();
        IsVisibleChanged += (_, _) => Hook();
    }

    public IFrameTimeFeed? Feed
    {
        get => (IFrameTimeFeed?)GetValue(FeedProperty);
        set => SetValue(FeedProperty, value);
    }

    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public static bool GetIsLive(DependencyObject element) => (bool)element.GetValue(IsLiveProperty);

    public static void SetIsLive(DependencyObject element, bool value) => element.SetValue(IsLiveProperty, value);

    private void Hook()
    {
        if (IsLoaded && IsVisible && Feed is not null)
        {
            if (!_hooked)
            {
                _hooked = true;
                CompositionTarget.Rendering += OnRendering;
            }
        }
        else
        {
            Unhook();
        }
    }

    private void Unhook()
    {
        if (_hooked)
        {
            _hooked = false;
            CompositionTarget.Rendering -= OnRendering;
        }
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = e is RenderingEventArgs args ? args.RenderingTime : TimeSpan.Zero;
        if (now - _lastDraw < (_showingSample ? SampleInterval : Interval))
        {
            return;
        }

        double elapsed = _lastDraw == TimeSpan.Zero ? Interval.TotalSeconds : Math.Min((now - _lastDraw).TotalSeconds, 0.25);
        _lastDraw = now;
        var feed = Feed;
        int read = feed?.Read(_incoming) ?? 0;
        _target = feed?.TargetFrameTime ?? 0;
        if (read == 0)
        {
            if (!GetIsLive(this))
            {
                // The preview: a still sample trace, drawn once; the feed is checked twice a second for real frames.
                if (!_showingSample)
                {
                    _showingSample = true;
                    _count = Sample(_buffer);
                    _scale = Wanted();
                    InvalidateVisual();
                }
            }
            else if (_count > 0)
            {
                _count = 0; // the game stopped drawing: back to the empty baseline (drawn once)
                InvalidateVisual();
            }

            return;
        }

        _showingSample = false;
        Array.Copy(_incoming, _buffer, read);
        _count = read;

        // The top of the graph: room above the typical frame and the target, eased so the trace doesn't jump.
        _scale += (Wanted() - _scale) * Math.Min(1, elapsed * 4);
        InvalidateVisual();
    }

    private double Wanted() => Math.Max(Math.Max(Percentile(0.5) * 2, Percentile(0.95) * 1.25), Math.Max(_target * 1.6, 8));

    /// <summary>A believable trace for the preview: ~7 ms frames with a little jitter and a stutter now and then.</summary>
    private static int Sample(double[] buffer)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            double t = i;
            double jitter = (Math.Sin(t * 0.9) * 0.35) + (Math.Sin(t * 2.3 + 1) * 0.2);
            double stutter = ((int)t % 97) == 0 ? 9 : ((int)t % 53) == 0 ? 4 : 0;
            buffer[i] = 6.9 + jitter + stutter;
        }

        return buffer.Length;
    }

    private double Percentile(double p)
    {
        if (_count == 0)
        {
            return 0;
        }

        Array.Copy(_buffer, _sorted, _count);
        Array.Sort(_sorted, 0, _count);
        return _sorted[Math.Clamp((int)(p * (_count - 1)), 0, _count - 1)];
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 8 || h < 6)
        {
            return;
        }

        var stroke = Stroke as SolidColorBrush;
        var color = stroke?.Color ?? Colors.White;

        // A faint baseline, so the graph's place shows even with nothing to draw.
        dc.DrawRectangle(Solid(WithAlpha(color, 0x10)), null, new Rect(0, h - 1, w, 1));
        if (_count < 2)
        {
            return;
        }

        double scale = Math.Max(_scale, 1);
        double step = w / (Frames - 1);
        double x0 = w - ((_count - 1) * step);
        double Y(double ms) => Math.Clamp(h - (ms / scale * (h - 1.5)), 1, h);

        var line = new StreamGeometry();
        var area = new StreamGeometry();
        using (var l = line.Open())
        using (var a = area.Open())
        {
            l.BeginFigure(new Point(x0, Y(_buffer[0])), false, false);
            a.BeginFigure(new Point(x0, h), true, true);
            a.LineTo(new Point(x0, Y(_buffer[0])), false, false);
            for (int i = 1; i < _count; i++)
            {
                var point = new Point(x0 + (i * step), Y(_buffer[i]));
                l.LineTo(point, true, true);
                a.LineTo(point, false, false);
            }

            a.LineTo(new Point(w, h), false, false);
        }

        line.Freeze();
        area.Freeze();

        var fill = new LinearGradientBrush(WithAlpha(color, 0x48), WithAlpha(color, 0x00), 90);
        fill.Freeze();
        dc.DrawGeometry(fill, null, area);
        var pen = new Pen(Solid(WithAlpha(color, 0xE6)), 1.4) { LineJoin = PenLineJoin.Round };
        pen.Freeze();
        dc.DrawGeometry(null, pen, line);

        // Stutters: frames well above the typical one, as tinted marks over the line.
        double typical = Percentile(0.5);
        for (int i = 1; i < _count; i++)
        {
            double ms = _buffer[i];
            if (ms > typical * 1.6 && ms > typical + 2)
            {
                var tint = ms > typical * 2.5 ? Hot : Warm;
                double x = x0 + (i * step);
                var mark = new Pen(Solid(tint), 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                mark.Freeze();
                dc.DrawLine(mark, new Point(x, Y(Math.Max(typical, ms - (scale * 0.25)))), new Point(x, Y(ms)));
            }
        }

        // The frame pacer's target.
        if (_target > 0)
        {
            double y = Math.Round(Y(_target)) + 0.5;
            var dash = new Pen(Solid(WithAlpha(Paced, 0xCC)), 1) { DashStyle = new DashStyle([3, 3], 0) };
            dash.Freeze();
            dc.DrawLine(dash, new Point(0, y), new Point(w, y));
        }
    }

    private static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

    private static SolidColorBrush Solid(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
