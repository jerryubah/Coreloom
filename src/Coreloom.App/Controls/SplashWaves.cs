using System.Windows;
using System.Windows.Media;

namespace Coreloom.App.Controls;

/// <summary>
/// The splash's flowing light: ribbons of blue silk sweeping in from the left and right edges, each a lit edge over a
/// faint sheet, drifting slowly like fabric in water and fading out before they reach the logo in the middle. Drawn
/// every frame from a few sine waves (no bitmaps, no blur effects); <see cref="IsAnimated"/> false draws one still frame.
/// It only uses frozen resources of its own, so it is safe on the splash's separate UI thread.
/// </summary>
public sealed class SplashWaves : FrameworkElement
{
    public static readonly DependencyProperty IsAnimatedProperty = DependencyProperty.Register(
        nameof(IsAnimated), typeof(bool), typeof(SplashWaves), new PropertyMetadata(true, (d, _) => ((SplashWaves)d).Hook()));

    private const int Samples = 72;

    // A ribbon: which side it comes from, then its path (y = base + slope·u + amplitude·sin(2π(frequency·u + phase) + speed·t))
    // and its sheet's depth, all as fractions of the height; brightness 0…1.
    private static readonly Ribbon[] Ribbons =
    [
        new(Side.Left, 0.16, 0.32, 0.085, 0.70, 0.02, 0.42, 0.17, 1.0),
        new(Side.Left, 0.27, 0.20, 0.065, 0.95, 0.38, -0.36, 0.10, 0.65),
        new(Side.Left, 0.68, -0.12, 0.045, 0.65, 0.72, 0.30, 0.12, 0.3),
        new(Side.Right, 0.02, 0.44, 0.075, 0.62, 0.10, 0.38, 0.18, 1.0),
        new(Side.Right, 0.54, -0.20, 0.060, 0.95, 0.55, -0.44, 0.11, 0.8),
        new(Side.Right, 0.84, -0.14, 0.035, 0.90, 0.30, 0.26, 0.08, 0.3),
    ];

    private static readonly Pen[] EdgePens =
    [
        Pen(Color.FromArgb(0x12, 40, 110, 255), 40),
        Pen(Color.FromArgb(0x24, 50, 125, 255), 18),
        Pen(Color.FromArgb(0x4C, 70, 150, 255), 7),
        Pen(Color.FromArgb(0x99, 110, 185, 255), 3),
        Pen(Color.FromArgb(0xF2, 190, 225, 255), 1.4),
    ];

    private bool _hooked;
    private TimeSpan _start = TimeSpan.MinValue;
    private double _time;

    public SplashWaves()
    {
        IsHitTestVisible = false;
        Loaded += (_, _) => Hook();
        Unloaded += (_, _) => Unhook();
    }

    public bool IsAnimated
    {
        get => (bool)GetValue(IsAnimatedProperty);
        set => SetValue(IsAnimatedProperty, value);
    }

    private enum Side
    {
        Left,
        Right,
    }

    private void Hook()
    {
        if (IsAnimated && IsLoaded && !_hooked)
        {
            _hooked = true;
            CompositionTarget.Rendering += OnRendering;
        }
        else if (!IsAnimated)
        {
            Unhook();
            InvalidateVisual();
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
        if (e is not RenderingEventArgs args)
        {
            return;
        }

        if (_start == TimeSpan.MinValue)
        {
            _start = args.RenderingTime;
        }

        double time = (args.RenderingTime - _start).TotalSeconds;
        if (Math.Abs(time - _time) > 0.0005)
        {
            _time = time;
            InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 10 || h < 10)
        {
            return;
        }

        foreach (var ribbon in Ribbons)
        {
            DrawRibbon(dc, ribbon, w, h, _time);
        }

        // Where the right-hand ribbons cross, the light gathers into a bright spot that breathes.
        double sx = (w * 1.04) - (0.27 * w * 0.54);
        double sy = Y(Ribbons[3], 0.27, h, _time);
        double pulse = 0.75 + (0.25 * Math.Sin(_time * 1.3));
        var spark = new RadialGradientBrush
        {
            GradientStops =
            {
                new GradientStop(Color.FromArgb((byte)(200 * pulse), 214, 236, 255), 0),
                new GradientStop(Color.FromArgb((byte)(90 * pulse), 64, 140, 255), 0.25),
                new GradientStop(Color.FromArgb(0, 40, 100, 255), 1),
            },
        };
        spark.Freeze();
        dc.DrawEllipse(spark, null, new Point(sx, sy), w * 0.12, h * 0.07);
    }

    private static double Y(Ribbon r, double u, double h, double t) =>
        h * (r.Base + (r.Slope * u) + (r.Amplitude * Math.Sin((2 * Math.PI * ((r.Frequency * u) + r.Phase)) + (r.Speed * t))));

    private static double Depth(Ribbon r, double u, double h, double t) =>
        h * r.Depth * (0.65 + (0.35 * Math.Sin((2 * Math.PI * ((0.6 * u) + r.Phase)) - (0.5 * r.Speed * t))));

    private static void DrawRibbon(DrawingContext dc, Ribbon r, double w, double h, double t)
    {
        // u runs 0 → 1 from the window's edge toward the middle; x covers the outer 52 % of the width on that side.
        double span = w * 0.54;
        Point At(double u, double dy) => new(r.Side == Side.Left ? (-0.04 * w) + (u * span) : (w * 1.04) - (u * span), Y(r, u, h, t) + dy);

        var edge = new StreamGeometry();
        var sheet = new StreamGeometry();
        using (var ctx = edge.Open())
        {
            ctx.BeginFigure(At(0, 0), false, false);
            for (int i = 1; i <= Samples; i++)
            {
                ctx.LineTo(At(i / (double)Samples, 0), true, true);
            }
        }

        using (var ctx = sheet.Open())
        {
            ctx.BeginFigure(At(0, 0), true, true);
            for (int i = 1; i <= Samples; i++)
            {
                ctx.LineTo(At(i / (double)Samples, 0), true, true);
            }

            for (int i = Samples; i >= 0; i--)
            {
                double u = i / (double)Samples;
                ctx.LineTo(At(u, Depth(r, u, h, t)), true, true);
            }
        }

        edge.Freeze();
        sheet.Freeze();

        // Everything fades toward the middle (and in from the very edge), leaving the logo on dark.
        var fade = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = new Point(r.Side == Side.Left ? -0.04 * w : w * 1.04, 0),
            EndPoint = new Point(r.Side == Side.Left ? span - (0.04 * w) : (w * 1.04) - span, 0),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0x80, 0, 0, 0), 0),
                new GradientStop(Color.FromArgb(0xFF, 0, 0, 0), 0.28),
                new GradientStop(Color.FromArgb(0x8C, 0, 0, 0), 0.55),
                new GradientStop(Color.FromArgb(0x00, 0, 0, 0), 0.82),
            },
        };
        fade.Freeze();
        dc.PushOpacityMask(fade);
        dc.PushOpacity(r.Brightness);

        // The sheet: lit along its top edge, falling off into the dark below it.
        double top = Y(r, 0.45, h, t);
        var sheetFill = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = new Point(0, top - (h * 0.05)),
            EndPoint = new Point(0, top + (h * (r.Depth + 0.12))),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0x8C, 36, 104, 255), 0),
                new GradientStop(Color.FromArgb(0x33, 20, 66, 220), 0.4),
                new GradientStop(Color.FromArgb(0x00, 10, 30, 140), 1),
            },
        };
        sheetFill.Freeze();
        dc.DrawGeometry(sheetFill, null, sheet);

        // The edge: a wide faint glow, a narrower one, then the bright line itself.
        foreach (var pen in EdgePens)
        {
            dc.DrawGeometry(null, pen, edge);
        }

        dc.Pop();
        dc.Pop();
    }

    private static Pen Pen(Color color, double thickness)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        var pen = new Pen(brush, thickness) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze();
        return pen;
    }

    private sealed record Ribbon(Side Side, double Base, double Slope, double Amplitude, double Frequency, double Phase, double Speed, double Depth, double Brightness);
}
