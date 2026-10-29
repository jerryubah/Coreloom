using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Coreloom.App.Behaviors;
using Coreloom.App.Themes;
using Coreloom.Core.Formatting;

namespace Coreloom.App.Controls;

/// <summary>How a <see cref="RadialGauge"/>'s arc is colored along its length.</summary>
public enum GaugeGradient
{
    /// <summary>One color (<see cref="RadialGauge.Fill"/> or the scale's).</summary>
    None,

    /// <summary>The accent sweeping from its cooler, lighter neighbour through the accent to its warmer one.</summary>
    Accent,

    /// <summary>The arc's color, starting a shade lighter.</summary>
    Shade,
}

/// <summary>How a <see cref="RadialGauge"/> is drawn.</summary>
public enum GaugeLook
{
    /// <summary>Ticks for gauges (40 px and up), a smooth arc for small spinners and comet tails.</summary>
    Auto,

    /// <summary>A ring of fine radial ticks: the lit ones brighten toward the head, which glows.</summary>
    Ticks,

    /// <summary>A smooth arc with rounded caps.</summary>
    Arc,
}

/// <summary>
/// 270° ring gauge, drawn as a ring of fine radial ticks (<see cref="GaugeLook"/>) or a smooth arc with rounded caps. <see cref="Value"/> is a 0–1 fraction; changes ease over ~450 ms
/// at the display refresh rate unless reduced motion is on. With <see cref="ToneScale"/> the arc takes its color from the
/// shared four-color scale (green idle or cool, blue normal, orange heavy, red at the limit; see <see cref="GaugeTone"/>),
/// worked out from the animated value so the color glides along with the arc; otherwise it is drawn with <see cref="Fill"/>.
/// </summary>
public sealed class RadialGauge : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(RadialGauge), new PropertyMetadata(0.0, OnValueChanged));

    private static readonly DependencyProperty DisplayValueProperty = DependencyProperty.Register(
        "DisplayValue", typeof(double), typeof(RadialGauge), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(
        nameof(Thickness), typeof(double), typeof(RadialGauge), new FrameworkPropertyMetadata(8.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush), typeof(Brush), typeof(RadialGauge), new FrameworkPropertyMetadata(Brushes.DimGray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(RadialGauge), new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SweepAngleProperty = DependencyProperty.Register(
        nameof(SweepAngle), typeof(double), typeof(RadialGauge), new FrameworkPropertyMetadata(270.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsUnavailableProperty = DependencyProperty.Register(
        nameof(IsUnavailable), typeof(bool), typeof(RadialGauge), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ToneScaleProperty = DependencyProperty.Register(
        nameof(ToneScale), typeof(bool), typeof(RadialGauge), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TemperatureScaleProperty = DependencyProperty.Register(
        nameof(TemperatureScale), typeof(bool), typeof(RadialGauge), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CoolAtProperty = DependencyProperty.Register(
        nameof(CoolAt), typeof(double), typeof(RadialGauge), new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty WarmAtProperty = DependencyProperty.Register(
        nameof(WarmAt), typeof(double), typeof(RadialGauge), new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty HotAtProperty = DependencyProperty.Register(
        nameof(HotAt), typeof(double), typeof(RadialGauge), new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>A fixed position on the color scale (0 green … 3 red) instead of one worked out from the value.</summary>
    public static readonly DependencyProperty ToneProperty = DependencyProperty.Register(
        nameof(Tone), typeof(double), typeof(RadialGauge), new PropertyMetadata(double.NaN, OnToneChanged));

    private static readonly DependencyProperty DisplayToneProperty = DependencyProperty.Register(
        "DisplayTone", typeof(double), typeof(RadialGauge), new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GradientProperty = DependencyProperty.Register(
        nameof(Gradient), typeof(GaugeGradient), typeof(RadialGauge), new FrameworkPropertyMetadata(GaugeGradient.None, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FadeTailProperty = DependencyProperty.Register(
        nameof(FadeTail), typeof(bool), typeof(RadialGauge), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LookProperty = DependencyProperty.Register(
        nameof(Look), typeof(GaugeLook), typeof(RadialGauge), new FrameworkPropertyMetadata(GaugeLook.Auto, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty OrbProperty = DependencyProperty.Register(
        nameof(Orb), typeof(bool), typeof(RadialGauge), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    private bool _themeHooked;

    public RadialGauge()
    {
        IsHitTestVisible = false;
        Loaded += (_, _) => HookTheme(true);
        Unloaded += (_, _) => HookTheme(false);
    }

    /// <summary>
    /// Colors the arc along its length. The colors are fixed to positions on the ring (not to the arc's end), so the
    /// gradient stays put while the arc grows.
    /// </summary>
    public GaugeGradient Gradient
    {
        get => (GaugeGradient)GetValue(GradientProperty);
        set => SetValue(GradientProperty, value);
    }

    /// <summary>Ticks or a smooth arc (<see cref="GaugeLook.Auto"/>: ticks unless the gauge is small or a comet).</summary>
    public GaugeLook Look
    {
        get => (GaugeLook)GetValue(LookProperty);
        set => SetValue(LookProperty, value);
    }

    /// <summary>A soft glowing sphere inside the ring, in the gauge's color, lit from the top right.</summary>
    public bool Orb
    {
        get => (bool)GetValue(OrbProperty);
        set => SetValue(OrbProperty, value);
    }

    /// <summary>The arc fades out toward its start, like a comet's tail (for a short spinning arc).</summary>
    public bool FadeTail
    {
        get => (bool)GetValue(FadeTailProperty);
        set => SetValue(FadeTailProperty, value);
    }

    /// <summary>Color the arc from the four-color scale instead of <see cref="Fill"/>.</summary>
    public bool ToneScale
    {
        get => (bool)GetValue(ToneScaleProperty);
        set => SetValue(ToneScaleProperty, value);
    }

    /// <summary>
    /// With <see cref="ToneScale"/>, the gauge shows a temperature and takes its colors from how warm it is: blue when
    /// cool, teal-green when normal, amber-orange when warm, red when hot. Each is a gradient, deeper at the tail and
    /// brighter at the head, and it glides from one to the next as the temperature moves.
    /// </summary>
    public bool TemperatureScale
    {
        get => (bool)GetValue(TemperatureScaleProperty);
        set => SetValue(TemperatureScaleProperty, value);
    }

    /// <summary>
    /// Sets the color directly (for ratings where more is better, like a speed): 0 green, 1 blue, 2 orange, 3 red.
    /// Changes glide like the value does. NaN (the default) colors by the value and the stops instead.
    /// </summary>
    public double Tone
    {
        get => (double)GetValue(ToneProperty);
        set => SetValue(ToneProperty, value);
    }

    /// <summary>Gauge fraction where green turns blue (default 0.30).</summary>
    public double CoolAt
    {
        get => (double)GetValue(CoolAtProperty);
        set => SetValue(CoolAtProperty, value);
    }

    /// <summary>Gauge fraction where blue turns orange (default 0.80).</summary>
    public double WarmAt
    {
        get => (double)GetValue(WarmAtProperty);
        set => SetValue(WarmAtProperty, value);
    }

    /// <summary>Gauge fraction where the arc is fully red (default 0.95).</summary>
    public double HotAt
    {
        get => (double)GetValue(HotAtProperty);
        set => SetValue(HotAtProperty, value);
    }

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Thickness
    {
        get => (double)GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    public Brush TrackBrush
    {
        get => (Brush)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public Brush Fill
    {
        get => (Brush)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public double SweepAngle
    {
        get => (double)GetValue(SweepAngleProperty);
        set => SetValue(SweepAngleProperty, value);
    }

    /// <summary>Draws only the track (value unavailable).</summary>
    public bool IsUnavailable
    {
        get => (bool)GetValue(IsUnavailableProperty);
        set => SetValue(IsUnavailableProperty, value);
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var gauge = (RadialGauge)d;
        double target = Math.Clamp(double.IsFinite((double)e.NewValue) ? (double)e.NewValue : 0, 0, 1);
        if (!AnimatedText.IsGloballyEnabled || !gauge.IsVisible)
        {
            gauge.BeginAnimation(DisplayValueProperty, null);
            gauge.SetValue(DisplayValueProperty, target);
            return;
        }

        var animation = new DoubleAnimation(target, TimeSpan.FromMilliseconds(450))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd,
        };
        gauge.BeginAnimation(DisplayValueProperty, animation);
    }

    private static void OnToneChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var gauge = (RadialGauge)d;
        double target = (double)e.NewValue;
        double current = (double)gauge.GetValue(DisplayToneProperty);
        if (!double.IsFinite(target) || !double.IsFinite(current) || !AnimatedText.IsGloballyEnabled || !gauge.IsVisible)
        {
            gauge.BeginAnimation(DisplayToneProperty, null);
            gauge.SetValue(DisplayToneProperty, target);
            return;
        }

        gauge.BeginAnimation(DisplayToneProperty, new DoubleAnimation(Math.Clamp(target, 0, 3), TimeSpan.FromMilliseconds(450))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd,
        });
    }

    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size < 8)
        {
            return;
        }

        double thickness = Math.Min(Thickness, size / 4);
        double radius = (size - thickness) / 2;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        double sweep = Math.Clamp(SweepAngle, 10, 359);
        double start = 90 + ((360 - sweep) / 2); // opening centered at the bottom
        double value = (double)GetValue(DisplayValueProperty);
        double tone = (double)GetValue(DisplayToneProperty);
        bool ticks = Look == GaugeLook.Ticks || (Look == GaugeLook.Auto && size >= 40 && !FadeTail);

        if (ticks)
        {
            RenderTicks(dc, center, radius, thickness, start, sweep, IsUnavailable ? 0 : value, tone);
            return;
        }

        dc.DrawGeometry(null, RoundPen(TrackBrush, thickness), Arc(center, radius, start, sweep));

        if (IsUnavailable || value <= 0.002)
        {
            return;
        }

        double arcSweep = sweep * Math.Clamp(value, 0, 1);
        if (ToneScale && TemperatureScale && !FadeTail)
        {
            var (tail, head) = TemperatureColors(double.IsFinite(tone) ? tone : TonePosition(value));
            DrawSweep(dc, center, radius, thickness, start, sweep, arcSweep, t => Lerp(tail, head, Math.Clamp(t * sweep / arcSweep, 0, 1)));
            return;
        }

        var gradient = Gradient;
        if (gradient == GaugeGradient.None && !FadeTail)
        {
            var brush = !ToneScale ? Fill
                : double.IsFinite(tone) ? ChartColors.Solid(ScaleColor(tone))
                : ChartColors.Solid(ToneColor(value));
            dc.DrawGeometry(null, RoundPen(brush, thickness), Arc(center, radius, start, arcSweep));
            return;
        }

        Color color = ToneScale ? (double.IsFinite(tone) ? ScaleColor(tone) : ToneColor(value))
            : Fill is SolidColorBrush solid ? solid.Color
            : Colors.DodgerBlue;
        if (FadeTail)
        {
            DrawTail(dc, center, radius, thickness, start, arcSweep, gradient == GaugeGradient.None ? color : Lighter(color));
            return;
        }

        Func<double, Color> colorAt;
        if (gradient == GaugeGradient.Accent)
        {
            var accent = ThemeService.Instance?.AccentColor ?? color;
            var (cool, warm) = ThemeService.AccentNeighbours(accent);
            colorAt = t => t < 0.5 ? Lerp(cool, accent, t * 2) : Lerp(accent, warm, (t - 0.5) * 2);
        }
        else
        {
            var light = Lighter(color);
            colorAt = t => Lerp(light, color, t);
        }

        DrawSweep(dc, center, radius, thickness, start, sweep, arcSweep, colorAt);
    }

    /// <summary>
    /// The color of a lit tick from its place on the ring (0 at the start … 1 at the end of the sweep) and along the lit
    /// part (0 at the tail … 1 at the head).
    /// </summary>
    private Func<double, double, Color> TickColors(double value, double tone)
    {
        if (ToneScale && TemperatureScale)
        {
            var (tail, head) = TemperatureColors(double.IsFinite(tone) ? tone : TonePosition(value));
            return (_, along) => Lerp(tail, head, along);
        }

        Color color = ToneScale ? (double.IsFinite(tone) ? ScaleColor(tone) : ToneColor(value))
            : Fill is SolidColorBrush solid ? solid.Color
            : Colors.DodgerBlue;
        if (Gradient == GaugeGradient.Accent)
        {
            var accent = ThemeService.Instance?.AccentColor ?? color;
            var (cool, warm) = ThemeService.AccentNeighbours(accent);
            return (t, _) => t < 0.5 ? Lerp(cool, accent, t * 2) : Lerp(accent, warm, (t - 0.5) * 2);
        }

        return (_, _) => color;
    }

    /// <summary>
    /// The tick ring: fine radial ticks around the sweep. Unlit ticks are the track; lit ones start dim and grow brighter
    /// toward the head, where the last few reach a little further out and glow. The head tick lights partway as the value
    /// moves, so the ring glides instead of stepping.
    /// </summary>
    private void RenderTicks(DrawingContext dc, Point center, double radius, double thickness, double start, double sweep, double value, double tone)
    {
        double size = (2 * radius) + thickness;
        double pitch = Math.Clamp(size * 0.058, 3.3, 6.4);
        int count = Math.Max(8, (int)Math.Round(radius * sweep * Math.PI / 180 / pitch)) + (sweep >= 358 ? 0 : 1);
        double step = sweep / (sweep >= 358 ? count : count - 1);
        double width = Math.Clamp(pitch * 0.44, 1.2, 2.9);
        double inner = radius - (thickness / 2) + (width / 2);
        double outer = radius + (thickness / 2) - (width / 2);
        double lit = Math.Clamp(value, 0, 1) * (sweep >= 358 ? count : count - 1);   // in ticks
        int full = value <= 0.002 ? -1 : (int)Math.Floor(lit + 1e-6);               // last fully lit tick
        double partial = lit - Math.Floor(lit + 1e-6);
        var colorAt = TickColors(value, tone);
        Color head = colorAt(Math.Clamp(lit / Math.Max(count - 1, 1), 0, 1), 1);

        if (Orb)
        {
            DrawOrb(dc, center, inner - (thickness * 0.55), head);
        }

        // Unlit ticks (one geometry, one pen).
        if (TrackBrush is { } track && !(track is SolidColorBrush s && s.Color.A == 0))
        {
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                for (int i = Math.Max(full + 1, 0); i < count; i++)
                {
                    AddTick(ctx, center, start + (i * step), inner, outer);
                }
            }

            geometry.Freeze();
            dc.DrawGeometry(null, RoundPen(track, width), geometry);
        }

        if (full < 0 && partial <= 0.01)
        {
            return;
        }

        int headIndex = partial > 0.01 ? full + 1 : full;
        double reach = thickness * 0.26;
        for (int i = 0; i <= headIndex && i < count; i++)
        {
            double t = count > 1 ? i / (double)(count - 1) : 1;
            double along = headIndex > 0 ? i / (double)headIndex : 1;     // 0 at the tail … 1 at the head
            var color = colorAt(t, along);
            int fromHead = headIndex - i;
            if (fromHead <= 2)
            {
                color = Lerp(color, ThemePalette.Shift(color, 0, 0.16), 1 - (fromHead / 3.0));
            }

            double alpha = 0.5 + (0.5 * Math.Pow(along, 1.5));
            if (i == headIndex && partial > 0.01)
            {
                alpha *= partial;
            }

            double extend = fromHead <= 1 ? reach : fromHead == 2 ? reach * 0.45 : 0;
            var pen = ChartColors.Pen(ThemePalette.WithAlpha(color, (byte)Math.Round(color.A * Math.Clamp(alpha, 0, 1))), width);
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                AddTick(ctx, center, start + (i * step), inner - (extend * 0.35), outer + extend);
            }

            geometry.Freeze();
            dc.DrawGeometry(null, pen, geometry);
        }

        // The head's glow.
        double headAngle = (start + (Math.Min(lit, count - 1) * step)) * Math.PI / 180;
        var at = new Point(center.X + (radius * Math.Cos(headAngle)), center.Y + (radius * Math.Sin(headAngle)));
        double glow = Math.Max(thickness * 1.5, 7);
        var halo = new RadialGradientBrush
        {
            GradientStops =
            {
                new GradientStop(ThemePalette.WithAlpha(ThemePalette.Shift(head, 0, 0.12), 0x70), 0),
                new GradientStop(ThemePalette.WithAlpha(head, 0x24), 0.5),
                new GradientStop(ThemePalette.WithAlpha(head, 0), 1),
            },
        };
        halo.Freeze();
        dc.DrawEllipse(halo, null, at, glow, glow);
    }

    private static void AddTick(StreamGeometryContext ctx, Point center, double degrees, double inner, double outer)
    {
        double a = degrees * Math.PI / 180;
        double cos = Math.Cos(a), sin = Math.Sin(a);
        ctx.BeginFigure(new Point(center.X + (inner * cos), center.Y + (inner * sin)), false, false);
        ctx.LineTo(new Point(center.X + (outer * cos), center.Y + (outer * sin)), true, false);
    }

    /// <summary>A sphere of soft light inside the ring: brightest toward the top right, its rim catching the light there.</summary>
    private static void DrawOrb(DrawingContext dc, Point center, double radius, Color color)
    {
        if (radius < 6)
        {
            return;
        }

        bool dark = ThemeService.Instance?.IsDark ?? true;
        var light = ThemePalette.Shift(color, 0, 0.2);
        var body = new RadialGradientBrush
        {
            Center = new Point(0.5, 0.5),
            GradientOrigin = new Point(0.7, 0.26),
            RadiusX = 0.62,
            RadiusY = 0.62,
            GradientStops =
            {
                new GradientStop(ThemePalette.WithAlpha(light, (byte)(dark ? 0x3C : 0x2A)), 0),
                new GradientStop(ThemePalette.WithAlpha(color, (byte)(dark ? 0x1C : 0x12)), 0.5),
                new GradientStop(ThemePalette.WithAlpha(color, (byte)(dark ? 0x0A : 0x06)), 0.82),
                new GradientStop(ThemePalette.WithAlpha(color, 0), 1),
            },
        };
        body.Freeze();
        dc.DrawEllipse(body, null, center, radius, radius);

        var rim = new LinearGradientBrush
        {
            StartPoint = new Point(0.85, 0.1),
            EndPoint = new Point(0.25, 0.85),
            GradientStops =
            {
                new GradientStop(ThemePalette.WithAlpha(light, (byte)(dark ? 0x70 : 0x50)), 0),
                new GradientStop(ThemePalette.WithAlpha(light, 0), 0.6),
            },
        };
        rim.Freeze();
        var pen = new Pen(rim, 1);
        pen.Freeze();
        dc.DrawEllipse(null, pen, center, radius - 0.5, radius - 0.5);
    }

    private static Color Lighter(Color color) => ThemePalette.Shift(color, 0, 0.14);

    /// <summary>
    /// Draws the arc as short pieces, each in the color for its place on the ring (a sweep gradient, which WPF doesn't
    /// have). Neighbouring pieces overlap a hair so no seam shows; only the two ends are rounded.
    /// </summary>
    private static void DrawSweep(DrawingContext dc, Point center, double radius, double thickness, double start, double fullSweep, double arcSweep, Func<double, Color> colorAt)
    {
        int pieces = Math.Clamp((int)Math.Ceiling(arcSweep / 3.0), 1, 120);
        double step = arcSweep / pieces;
        for (int i = 0; i < pieces; i++)
        {
            bool last = i == pieces - 1;
            var color = colorAt(Math.Clamp(((i + 0.5) * step) / fullSweep, 0, 1));
            var pen = new Pen(new SolidColorBrush(color), thickness)
            {
                StartLineCap = i == 0 ? PenLineCap.Round : PenLineCap.Flat,
                EndLineCap = last ? PenLineCap.Round : PenLineCap.Flat,
            };
            pen.Freeze();
            double overlap = !last && color.A == 255 ? 0.6 : 0;
            dc.DrawGeometry(null, pen, Arc(center, radius, start + (i * step), step + overlap));
        }
    }

    /// <summary>A short arc fading from clear at its start to <paramref name="color"/> at its head, in one piece.</summary>
    private static void DrawTail(DrawingContext dc, Point center, double radius, double thickness, double start, double arcSweep, Color color)
    {
        double a0 = start * Math.PI / 180;
        double a1 = (start + arcSweep) * Math.PI / 180;
        var tail = new Point(center.X + (radius * Math.Cos(a0)), center.Y + (radius * Math.Sin(a0)));
        var head = new Point(center.X + (radius * Math.Cos(a1)), center.Y + (radius * Math.Sin(a1)));
        var brush = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = tail,
            EndPoint = head,
            GradientStops = { new GradientStop(ThemePalette.WithAlpha(color, 0), 0), new GradientStop(color, 1) },
        };
        var pen = new Pen(brush, thickness) { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Round };
        pen.Freeze();
        dc.DrawGeometry(null, pen, Arc(center, radius, start, arcSweep));
    }

    /// <summary>The scale's color for a gauge fraction, blended between the theme's success, info, elevated and critical colors.</summary>
    private Color ToneColor(double fraction) => ScaleColor(TonePosition(fraction));

    /// <summary>Position on the scale (0 … 3) for a gauge fraction, from the stops.</summary>
    private double TonePosition(double fraction)
    {
        double hot = Valid(HotAt, 0.95);
        double warm = Math.Min(Valid(WarmAt, 0.80), hot);
        double cool = Math.Min(Valid(CoolAt, Math.Min(0.30, warm * 0.5)), warm);
        return GaugeTone.At(fraction, cool, warm, hot);

        static double Valid(double value, double fallback) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : fallback;
    }

    /// <summary>
    /// A temperature's gradient (tail and head colors) at a position on the scale: 0 cool (blue), 1 normal (teal-green),
    /// 2 warm (amber-orange), 3 hot (red), blended between neighbours.
    /// </summary>
    internal static (Color Tail, Color Head) TemperatureColors(double position)
    {
        position = double.IsFinite(position) ? Math.Clamp(position, 0, 3) : 1;
        int band = Math.Clamp((int)Math.Floor(position), 0, 2);
        double f = Math.Clamp(position - band, 0, 1);
        return (Lerp(Temperature(band, "Tail"), Temperature(band + 1, "Tail"), f),
                Lerp(Temperature(band, "Head"), Temperature(band + 1, "Head"), f));

        static Color Temperature(int band, string part) => ChartColors.Theme($"Temp.{band switch { 0 => "Cool", 1 => "Normal", 2 => "Warm", _ => "Hot" }}.{part}");
    }

    /// <summary>The color at a position on the scale (0 green … 3 red), blended between neighbours.</summary>
    internal static Color ScaleColor(double position)
    {
        position = Math.Clamp(position, 0, 3);
        int band = Math.Clamp((int)Math.Floor(position), 0, 2);
        return Lerp(Stop(band), Stop(band + 1), Math.Clamp(position - band, 0, 1));
    }

    private static Color Stop(int index) => ChartColors.Theme(index switch
    {
        0 => "Success",
        1 => "Info",
        2 => "Elevated",
        _ => "Critical",
    });

    internal static Color Lerp(Color a, Color b, double t)
    {
        if (t <= 0)
        {
            return a;
        }

        if (t >= 1)
        {
            return b;
        }

        return Color.FromArgb(
            (byte)Math.Round(a.A + ((b.A - a.A) * t)),
            (byte)Math.Round(a.R + ((b.R - a.R) * t)),
            (byte)Math.Round(a.G + ((b.G - a.G) * t)),
            (byte)Math.Round(a.B + ((b.B - a.B) * t)));
    }

    /// <summary>Redraws with the new palette when the theme changes; subscribed only while the gauge is on screen.</summary>
    private void HookTheme(bool hook)
    {
        if (hook == _themeHooked || ThemeService.Instance is not { } theme)
        {
            return;
        }

        _themeHooked = hook;
        if (hook)
        {
            theme.ThemeChanged += OnThemeChanged;
        }
        else
        {
            theme.ThemeChanged -= OnThemeChanged;
        }
    }

    private void OnThemeChanged(object? sender, EventArgs e) => InvalidateVisual();

    /// <summary>
    /// A round-capped pen, frozen when its brush allows it. Frozen pens are cheaper to render and do not register
    /// change handlers on the (shared, theme-owned) brush.
    /// </summary>
    private static Pen RoundPen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (pen.CanFreeze)
        {
            pen.Freeze();
        }

        return pen;
    }

    internal static Geometry Arc(Point center, double radius, double startDegrees, double sweepDegrees)
    {
        double a0 = startDegrees * Math.PI / 180;
        double a1 = (startDegrees + sweepDegrees) * Math.PI / 180;
        var p0 = new Point(center.X + (radius * Math.Cos(a0)), center.Y + (radius * Math.Sin(a0)));
        var p1 = new Point(center.X + (radius * Math.Cos(a1)), center.Y + (radius * Math.Sin(a1)));
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(p0, false, false);
            ctx.ArcTo(p1, new Size(radius, radius), 0, sweepDegrees > 180, SweepDirection.Clockwise, true, false);
        }

        geometry.Freeze();
        return geometry;
    }
}
