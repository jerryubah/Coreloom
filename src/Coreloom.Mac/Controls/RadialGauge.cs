using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Coreloom.Core.Formatting;
using Coreloom.Mac.Behaviors;
using Coreloom.Mac.Themes;

namespace Coreloom.Mac.Controls;

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
/// 270° ring gauge, drawn as a ring of fine radial ticks or a smooth arc with rounded caps. <see cref="Value"/> is a 0–1
/// fraction; changes ease over ~450 ms unless reduced motion is on. With <see cref="ToneScale"/> the arc takes its color
/// from the shared four-color scale (green idle or cool, blue normal, orange heavy, red at the limit; see
/// <see cref="GaugeTone"/>), worked out from the animated value so the color glides along with the arc.
/// </summary>
public sealed class RadialGauge : Control
{
    public static readonly StyledProperty<double> ValueProperty = AvaloniaProperty.Register<RadialGauge, double>(nameof(Value));

    public static readonly StyledProperty<double> ThicknessProperty = AvaloniaProperty.Register<RadialGauge, double>(nameof(Thickness), 8.0);

    public static readonly StyledProperty<IBrush?> TrackBrushProperty = AvaloniaProperty.Register<RadialGauge, IBrush?>(nameof(TrackBrush), Brushes.DimGray);

    public static readonly StyledProperty<IBrush?> FillProperty = AvaloniaProperty.Register<RadialGauge, IBrush?>(nameof(Fill), Brushes.DodgerBlue);

    public static readonly StyledProperty<double> SweepAngleProperty = AvaloniaProperty.Register<RadialGauge, double>(nameof(SweepAngle), 270.0);

    public static readonly StyledProperty<bool> IsUnavailableProperty = AvaloniaProperty.Register<RadialGauge, bool>(nameof(IsUnavailable));

    public static readonly StyledProperty<bool> ToneScaleProperty = AvaloniaProperty.Register<RadialGauge, bool>(nameof(ToneScale));

    public static readonly StyledProperty<bool> TemperatureScaleProperty = AvaloniaProperty.Register<RadialGauge, bool>(nameof(TemperatureScale));

    public static readonly StyledProperty<double> CoolAtProperty = AvaloniaProperty.Register<RadialGauge, double>(nameof(CoolAt), double.NaN);

    public static readonly StyledProperty<double> WarmAtProperty = AvaloniaProperty.Register<RadialGauge, double>(nameof(WarmAt), double.NaN);

    public static readonly StyledProperty<double> HotAtProperty = AvaloniaProperty.Register<RadialGauge, double>(nameof(HotAt), double.NaN);

    /// <summary>A fixed position on the color scale (0 green … 3 red) instead of one worked out from the value.</summary>
    public static readonly StyledProperty<double> ToneProperty = AvaloniaProperty.Register<RadialGauge, double>(nameof(Tone), double.NaN);

    public static readonly StyledProperty<GaugeGradient> GradientProperty = AvaloniaProperty.Register<RadialGauge, GaugeGradient>(nameof(Gradient));

    public static readonly StyledProperty<bool> FadeTailProperty = AvaloniaProperty.Register<RadialGauge, bool>(nameof(FadeTail));

    public static readonly StyledProperty<GaugeLook> LookProperty = AvaloniaProperty.Register<RadialGauge, GaugeLook>(nameof(Look));

    public static readonly StyledProperty<bool> OrbProperty = AvaloniaProperty.Register<RadialGauge, bool>(nameof(Orb));

    private bool _themeHooked;
    private double _displayValue;
    private double _displayTone = double.NaN;
    private Animator.Tween? _valueTween;
    private Animator.Tween? _toneTween;

    static RadialGauge()
    {
        AffectsRender<RadialGauge>(ThicknessProperty, TrackBrushProperty, FillProperty, SweepAngleProperty, IsUnavailableProperty, ToneScaleProperty,
            TemperatureScaleProperty, CoolAtProperty, WarmAtProperty, HotAtProperty, GradientProperty, FadeTailProperty, LookProperty, OrbProperty);
    }

    public RadialGauge()
    {
        IsHitTestVisible = false;
        Loaded += (_, _) => HookTheme(true);
        Unloaded += (_, _) => HookTheme(false);
    }

    public GaugeGradient Gradient
    {
        get => GetValue(GradientProperty);
        set => SetValue(GradientProperty, value);
    }

    public GaugeLook Look
    {
        get => GetValue(LookProperty);
        set => SetValue(LookProperty, value);
    }

    /// <summary>A soft glowing sphere inside the ring, in the gauge's color, lit from the top right.</summary>
    public bool Orb
    {
        get => GetValue(OrbProperty);
        set => SetValue(OrbProperty, value);
    }

    /// <summary>The arc fades out toward its start, like a comet's tail (for a short spinning arc).</summary>
    public bool FadeTail
    {
        get => GetValue(FadeTailProperty);
        set => SetValue(FadeTailProperty, value);
    }

    public bool ToneScale
    {
        get => GetValue(ToneScaleProperty);
        set => SetValue(ToneScaleProperty, value);
    }

    /// <summary>With <see cref="ToneScale"/>, the gauge shows a temperature and takes its colors from how warm it is.</summary>
    public bool TemperatureScale
    {
        get => GetValue(TemperatureScaleProperty);
        set => SetValue(TemperatureScaleProperty, value);
    }

    public double Tone
    {
        get => GetValue(ToneProperty);
        set => SetValue(ToneProperty, value);
    }

    public double CoolAt
    {
        get => GetValue(CoolAtProperty);
        set => SetValue(CoolAtProperty, value);
    }

    public double WarmAt
    {
        get => GetValue(WarmAtProperty);
        set => SetValue(WarmAtProperty, value);
    }

    public double HotAt
    {
        get => GetValue(HotAtProperty);
        set => SetValue(HotAtProperty, value);
    }

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Thickness
    {
        get => GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    public IBrush? TrackBrush
    {
        get => GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public double SweepAngle
    {
        get => GetValue(SweepAngleProperty);
        set => SetValue(SweepAngleProperty, value);
    }

    /// <summary>Draws only the track (value unavailable).</summary>
    public bool IsUnavailable
    {
        get => GetValue(IsUnavailableProperty);
        set => SetValue(IsUnavailableProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty)
        {
            double target = Math.Clamp(change.NewValue is double d && double.IsFinite(d) ? d : 0, 0, 1);
            _valueTween?.Cancel();
            if (!AnimatedText.IsGloballyEnabled || !IsEffectivelyVisible)
            {
                _displayValue = target;
                InvalidateVisual();
                return;
            }

            double from = _displayValue;
            _valueTween = Animator.Run(TimeSpan.FromMilliseconds(450), Ease.CubicOut, t =>
            {
                _displayValue = from + ((target - from) * t);
                InvalidateVisual();
            });
        }
        else if (change.Property == ToneProperty)
        {
            double target = change.NewValue is double d ? d : double.NaN;
            double current = _displayTone;
            _toneTween?.Cancel();
            if (!double.IsFinite(target) || !double.IsFinite(current) || !AnimatedText.IsGloballyEnabled || !IsEffectivelyVisible)
            {
                _displayTone = target;
                InvalidateVisual();
                return;
            }

            target = Math.Clamp(target, 0, 3);
            _toneTween = Animator.Run(TimeSpan.FromMilliseconds(450), Ease.CubicOut, t =>
            {
                _displayTone = current + ((target - current) * t);
                InvalidateVisual();
            });
        }
    }

    public override void Render(DrawingContext dc)
    {
        double w = Bounds.Width, h = Bounds.Height;
        double size = Math.Min(w, h);
        if (size < 8)
        {
            return;
        }

        double thickness = Math.Min(Thickness, size / 4);
        double radius = (size - thickness) / 2;
        var center = new Point(w / 2, h / 2);
        double sweep = Math.Clamp(SweepAngle, 10, 359);
        double start = 90 + ((360 - sweep) / 2); // opening centered at the bottom
        double value = _displayValue;
        double tone = _displayTone;
        bool ticks = Look == GaugeLook.Ticks || (Look == GaugeLook.Auto && size >= 40 && !FadeTail);

        if (ticks)
        {
            RenderTicks(dc, center, radius, thickness, start, sweep, IsUnavailable ? 0 : value, tone);
            return;
        }

        if (TrackBrush is { } track)
        {
            dc.DrawGeometry(null, RoundPen(track, thickness), Arc(center, radius, start, sweep));
        }

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
            IBrush brush = !ToneScale ? (Fill ?? Brushes.DodgerBlue)
                : double.IsFinite(tone) ? ChartColors.Solid(ScaleColor(tone))
                : ChartColors.Solid(ToneColor(value));
            dc.DrawGeometry(null, RoundPen(brush, thickness), Arc(center, radius, start, arcSweep));
            return;
        }

        Color color = ToneScale ? (double.IsFinite(tone) ? ScaleColor(tone) : ToneColor(value))
            : Fill is ISolidColorBrush solid ? solid.Color
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

    private Func<double, double, Color> TickColors(double value, double tone)
    {
        if (ToneScale && TemperatureScale)
        {
            var (tail, head) = TemperatureColors(double.IsFinite(tone) ? tone : TonePosition(value));
            return (_, along) => Lerp(tail, head, along);
        }

        Color color = ToneScale ? (double.IsFinite(tone) ? ScaleColor(tone) : ToneColor(value))
            : Fill is ISolidColorBrush solid ? solid.Color
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
    /// toward the head, where the last few reach a little further out and glow.
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
        double lit = Math.Clamp(value, 0, 1) * (sweep >= 358 ? count : count - 1);
        int full = value <= 0.002 ? -1 : (int)Math.Floor(lit + 1e-6);
        double partial = lit - Math.Floor(lit + 1e-6);
        var colorAt = TickColors(value, tone);
        Color head = colorAt(Math.Clamp(lit / Math.Max(count - 1, 1), 0, 1), 1);

        if (Orb)
        {
            DrawOrb(dc, center, inner - (thickness * 0.55), head);
        }

        if (TrackBrush is { } track && !(track is ISolidColorBrush s && s.Color.A == 0))
        {
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                for (int i = Math.Max(full + 1, 0); i < count; i++)
                {
                    AddTick(ctx, center, start + (i * step), inner, outer);
                }
            }

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
            double along = headIndex > 0 ? i / (double)headIndex : 1;
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
            var pen = ChartColors.Pen(ChartColors.WithAlpha(color, (byte)Math.Round(color.A * Math.Clamp(alpha, 0, 1))), width);
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                AddTick(ctx, center, start + (i * step), inner - (extend * 0.35), outer + extend);
            }

            dc.DrawGeometry(null, pen, geometry);
        }

        // The head's glow.
        double headAngle = (start + (Math.Min(lit, count - 1) * step)) * Math.PI / 180;
        var at = new Point(center.X + (radius * Math.Cos(headAngle)), center.Y + (radius * Math.Sin(headAngle)));
        double glow = Math.Max(thickness * 1.5, 7);
        var halo = new ImmutableRadialGradientBrush(
        [
            new ImmutableGradientStop(0, ChartColors.WithAlpha(ThemePalette.Shift(head, 0, 0.12), 0x70)),
            new ImmutableGradientStop(0.5, ChartColors.WithAlpha(head, 0x24)),
            new ImmutableGradientStop(1, ChartColors.WithAlpha(head, 0)),
        ], radius: 0.5);
        dc.DrawEllipse(halo, null, at, glow, glow);
    }

    private static void AddTick(StreamGeometryContext ctx, Point center, double degrees, double inner, double outer)
    {
        double a = degrees * Math.PI / 180;
        double cos = Math.Cos(a), sin = Math.Sin(a);
        ctx.BeginFigure(new Point(center.X + (inner * cos), center.Y + (inner * sin)), false);
        ctx.LineTo(new Point(center.X + (outer * cos), center.Y + (outer * sin)));
        ctx.EndFigure(false);
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
        var body = new ImmutableRadialGradientBrush(
            [
                new ImmutableGradientStop(0, ChartColors.WithAlpha(light, (byte)(dark ? 0x3C : 0x2A))),
                new ImmutableGradientStop(0.5, ChartColors.WithAlpha(color, (byte)(dark ? 0x1C : 0x12))),
                new ImmutableGradientStop(0.82, ChartColors.WithAlpha(color, (byte)(dark ? 0x0A : 0x06))),
                new ImmutableGradientStop(1, ChartColors.WithAlpha(color, 0)),
            ],
            center: new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            gradientOrigin: new RelativePoint(0.7, 0.26, RelativeUnit.Relative),
            radiusX: new RelativeScalar(0.62, RelativeUnit.Relative),
            radiusY: new RelativeScalar(0.62, RelativeUnit.Relative));
        dc.DrawEllipse(body, null, center, radius, radius);

        var rim = new ImmutableLinearGradientBrush(
            [
                new ImmutableGradientStop(0, ChartColors.WithAlpha(light, (byte)(dark ? 0x70 : 0x50))),
                new ImmutableGradientStop(0.6, ChartColors.WithAlpha(light, 0)),
            ],
            startPoint: new RelativePoint(0.85, 0.1, RelativeUnit.Relative),
            endPoint: new RelativePoint(0.25, 0.85, RelativeUnit.Relative));
        dc.DrawEllipse(null, new ImmutablePen(rim, 1), center, radius - 0.5, radius - 0.5);
    }

    private static Color Lighter(Color color) => ThemePalette.Shift(color, 0, 0.14);

    /// <summary>
    /// Draws the arc as short pieces, each in the color for its place on the ring (a sweep gradient). Neighbouring pieces
    /// overlap a hair so no seam shows; the two ends get round caps.
    /// </summary>
    private static void DrawSweep(DrawingContext dc, Point center, double radius, double thickness, double start, double fullSweep, double arcSweep, Func<double, Color> colorAt)
    {
        int pieces = Math.Clamp((int)Math.Ceiling(arcSweep / 3.0), 1, 120);
        double step = arcSweep / pieces;
        Color first = colorAt(Math.Clamp(0.5 * step / fullSweep, 0, 1));
        Color last = first;
        for (int i = 0; i < pieces; i++)
        {
            bool isLast = i == pieces - 1;
            var color = colorAt(Math.Clamp(((i + 0.5) * step) / fullSweep, 0, 1));
            last = color;
            double overlap = !isLast && color.A == 255 ? 0.6 : 0;
            dc.DrawGeometry(null, ChartColors.Pen(color, thickness, round: false), Arc(center, radius, start + (i * step), step + overlap));
        }

        // Round caps at the two ends.
        double a0 = start * Math.PI / 180, a1 = (start + arcSweep) * Math.PI / 180;
        dc.DrawEllipse(ChartColors.Solid(first), null, new Point(center.X + (radius * Math.Cos(a0)), center.Y + (radius * Math.Sin(a0))), thickness / 2, thickness / 2);
        dc.DrawEllipse(ChartColors.Solid(last), null, new Point(center.X + (radius * Math.Cos(a1)), center.Y + (radius * Math.Sin(a1))), thickness / 2, thickness / 2);
    }

    /// <summary>A short arc fading from clear at its start to <paramref name="color"/> at its head.</summary>
    private static void DrawTail(DrawingContext dc, Point center, double radius, double thickness, double start, double arcSweep, Color color)
    {
        double a0 = start * Math.PI / 180;
        double a1 = (start + arcSweep) * Math.PI / 180;
        var tail = new Point(center.X + (radius * Math.Cos(a0)), center.Y + (radius * Math.Sin(a0)));
        var head = new Point(center.X + (radius * Math.Cos(a1)), center.Y + (radius * Math.Sin(a1)));
        var brush = new ImmutableLinearGradientBrush(
            [new ImmutableGradientStop(0, ChartColors.WithAlpha(color, 0)), new ImmutableGradientStop(1, color)],
            startPoint: new RelativePoint(tail, RelativeUnit.Absolute),
            endPoint: new RelativePoint(head, RelativeUnit.Absolute));
        dc.DrawGeometry(null, new ImmutablePen(brush, thickness, lineCap: PenLineCap.Flat), Arc(center, radius, start, arcSweep));
        dc.DrawEllipse(ChartColors.Solid(color), null, head, thickness / 2, thickness / 2);
    }

    private Color ToneColor(double fraction) => ScaleColor(TonePosition(fraction));

    private double TonePosition(double fraction)
    {
        double hot = Valid(HotAt, 0.95);
        double warm = Math.Min(Valid(WarmAt, 0.80), hot);
        double cool = Math.Min(Valid(CoolAt, Math.Min(0.30, warm * 0.5)), warm);
        return GaugeTone.At(fraction, cool, warm, hot);

        static double Valid(double value, double fallback) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : fallback;
    }

    /// <summary>A temperature's gradient (tail and head colors) at a position on the scale (0 cool … 3 hot).</summary>
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

    private static IPen RoundPen(IBrush brush, double thickness) => new ImmutablePen(brush.ToImmutable(), thickness, lineCap: PenLineCap.Round);

    internal static Geometry Arc(Point center, double radius, double startDegrees, double sweepDegrees)
    {
        double a0 = startDegrees * Math.PI / 180;
        double a1 = (startDegrees + sweepDegrees) * Math.PI / 180;
        var p0 = new Point(center.X + (radius * Math.Cos(a0)), center.Y + (radius * Math.Sin(a0)));
        var p1 = new Point(center.X + (radius * Math.Cos(a1)), center.Y + (radius * Math.Sin(a1)));
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(p0, false);
            ctx.ArcTo(p1, new Size(radius, radius), 0, sweepDegrees > 180, SweepDirection.Clockwise);
            ctx.EndFigure(false);
        }

        return geometry;
    }
}
