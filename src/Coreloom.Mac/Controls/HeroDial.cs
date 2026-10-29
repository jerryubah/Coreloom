using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Coreloom.Mac.Behaviors;
using Coreloom.Mac.Themes;

namespace Coreloom.Mac.Controls;

/// <summary>
/// The dial in the Gaming and Smart Optimize banners (since 1.8.3): a thick arc of three quarters, open at the bottom,
/// filled in one color from a deeper shade to a lighter one with a soft glow under it; a fine scale of ticks inside it
/// lit up to the value; the numbers at the two ends of the arc; and the dial's light rising from the bottom of the tile
/// it sits in. It fills the space it's given and centres itself in it, so its tile's left and right walls are always the
/// same distance away. While <see cref="IsSpinning"/> a bright comet runs along the arc.
/// </summary>
public sealed class HeroDial : Control
{
    public static readonly StyledProperty<double> ValueProperty = AvaloniaProperty.Register<HeroDial, double>(nameof(Value));

    /// <summary>The color on the green … red scale (0 … 3), or NaN for the accent.</summary>
    public static readonly StyledProperty<double> ToneProperty = AvaloniaProperty.Register<HeroDial, double>(nameof(Tone), double.NaN);

    public static readonly StyledProperty<double> DialSizeProperty = AvaloniaProperty.Register<HeroDial, double>(nameof(DialSize), 220.0);

    public static readonly StyledProperty<double> ThicknessProperty = AvaloniaProperty.Register<HeroDial, double>(nameof(Thickness), 12.0);

    public static readonly StyledProperty<string?> StartLabelProperty = AvaloniaProperty.Register<HeroDial, string?>(nameof(StartLabel));

    public static readonly StyledProperty<string?> EndLabelProperty = AvaloniaProperty.Register<HeroDial, string?>(nameof(EndLabel));

    public static readonly StyledProperty<bool> IsSpinningProperty = AvaloniaProperty.Register<HeroDial, bool>(nameof(IsSpinning));

    private const double Start = 135;   // degrees, clockwise from the right: bottom left
    private const double Sweep = 270;   // to the bottom right
    private const int Ticks = 46;

    private double _displayValue;
    private double _displayTone = double.NaN;
    private double _phase;
    private Animator.Tween? _valueTween;
    private Animator.Tween? _toneTween;
    private Animator.Tween? _spin;
    private bool _themeHooked;

    static HeroDial()
    {
        AffectsRender<HeroDial>(DialSizeProperty, ThicknessProperty, StartLabelProperty, EndLabelProperty);
    }

    public HeroDial()
    {
        AttachedToVisualTree += (_, _) =>
        {
            HookTheme(true);
            UpdateSpin();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            HookTheme(false);
            _spin?.Cancel();
            _spin = null;
        };
    }

    /// <summary>0 … 1: how much of the arc is lit.</summary>
    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Tone
    {
        get => GetValue(ToneProperty);
        set => SetValue(ToneProperty, value);
    }

    /// <summary>The dial's diameter at most (it shrinks to fit a smaller space).</summary>
    public double DialSize
    {
        get => GetValue(DialSizeProperty);
        set => SetValue(DialSizeProperty, value);
    }

    public double Thickness
    {
        get => GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    /// <summary>The number under the start of the arc (bottom left), such as "0".</summary>
    public string? StartLabel
    {
        get => GetValue(StartLabelProperty);
        set => SetValue(StartLabelProperty, value);
    }

    /// <summary>The number under the end of the arc (bottom right), such as "9".</summary>
    public string? EndLabel
    {
        get => GetValue(EndLabelProperty);
        set => SetValue(EndLabelProperty, value);
    }

    public bool IsSpinning
    {
        get => GetValue(IsSpinningProperty);
        set => SetValue(IsSpinningProperty, value);
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
            _valueTween = Animator.Run(TimeSpan.FromMilliseconds(650), Ease.CubicOut, t =>
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
        else if (change.Property == IsSpinningProperty)
        {
            UpdateSpin();
        }
    }

    private void UpdateSpin()
    {
        _spin?.Cancel();
        _spin = null;
        if (IsSpinning && !Motion.ReducedMotion && VisualRoot is not null)
        {
            _spin = Animator.Loop(seconds =>
            {
                _phase = (seconds / 1.6) % 1;
                InvalidateVisual();
            });
        }
        else
        {
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext dc)
    {
        double w = Bounds.Width, h = Bounds.Height;
        double size = Math.Min(Math.Min(w, h) - 8, DialSize);
        if (size < 40)
        {
            return;
        }

        bool dark = ThemeService.Instance?.IsDark ?? true;
        double thickness = Math.Clamp(Thickness, 4, size / 8);
        var center = new Point(w / 2, h / 2);
        double radius = (size - thickness) / 2;
        double value = Math.Clamp(_displayValue, 0, 1);
        var (deep, main, light) = Colors3();

        DrawLight(dc, w, h, main, dark);

        // The face: a faint disc, lighter at the top, so the dial reads as one object on the glass.
        double face = radius - thickness - 6;
        var faceBrush = new ImmutableRadialGradientBrush(
            [
                new ImmutableGradientStop(0, Color.FromArgb((byte)(dark ? 0x10 : 0x30), 255, 255, 255)),
                new ImmutableGradientStop(1, Color.FromArgb(0, 255, 255, 255)),
            ],
            center: new RelativePoint(0.5, 0.15, RelativeUnit.Relative),
            gradientOrigin: new RelativePoint(0.5, 0.15, RelativeUnit.Relative),
            radiusX: new RelativeScalar(0.9, RelativeUnit.Relative),
            radiusY: new RelativeScalar(0.9, RelativeUnit.Relative));
        dc.DrawEllipse(faceBrush, null, center, face, face);

        // The track.
        var track = dark ? Color.FromArgb(0x1C, 255, 255, 255) : Color.FromArgb(0x16, 0, 0, 0);
        dc.DrawGeometry(null, ChartColors.Pen(track, thickness), RadialGauge.Arc(center, radius, Start, Sweep));

        // The scale inside the arc, lit up to the value.
        DrawScale(dc, center, radius - (thickness / 2) - 9, value, main, dark);

        // The value: a glow under it, then the arc from the deeper shade to the lighter one.
        double arc = Sweep * value;
        if (arc > 0.5)
        {
            foreach (var (widen, alpha) in GlowLayers)
            {
                dc.DrawGeometry(null, ChartColors.Pen(ChartColors.WithAlpha(main, (byte)(alpha * (dark ? 1 : 0.6))), thickness * widen),
                    RadialGauge.Arc(center, radius, Start, arc));
            }

            DrawGradientArc(dc, center, radius, thickness, arc, deep, light);
            var head = PointAt(center, radius, Start + arc);
            var halo = new ImmutableRadialGradientBrush(
                [new ImmutableGradientStop(0, ChartColors.WithAlpha(light, 0xB0)), new ImmutableGradientStop(1, ChartColors.WithAlpha(light, 0))],
                radiusX: new RelativeScalar(0.5, RelativeUnit.Relative), radiusY: new RelativeScalar(0.5, RelativeUnit.Relative));
            dc.DrawEllipse(halo, null, head, thickness * 1.5, thickness * 1.5);
            dc.DrawEllipse(ChartColors.Solid(Color.FromArgb(0xE6, 255, 255, 255)), null, head, thickness * 0.2, thickness * 0.2);
        }

        if (IsSpinning && !Motion.ReducedMotion)
        {
            DrawComet(dc, center, radius, thickness, light);
        }

        DrawLabels(dc, center, radius, thickness, dark);
    }

    /// <summary>Wider, fainter copies of the arc under it: a soft glow without a blur effect.</summary>
    private static readonly (double Widen, double Alpha)[] GlowLayers =
        [(3.6, 0x06), (3.2, 0x07), (2.8, 0x08), (2.45, 0x09), (2.1, 0x0A), (1.8, 0x0B), (1.5, 0x0C), (1.25, 0x0D)];

    /// <summary>The deeper shade the arc starts in, the color itself, and the lighter shade it ends in.</summary>
    private (Color Deep, Color Main, Color Light) Colors3()
    {
        var main = double.IsFinite(_displayTone) ? RadialGauge.ScaleColor(_displayTone) : ChartColors.Theme("Accent");
        return (ThemePalette.Shift(main, 0, -0.12), main, RadialGauge.Lerp(main, Colors.White, 0.38));
    }

    /// <summary>The dial's light, rising from the bottom of the space it fills (the tile it sits in).</summary>
    private static void DrawLight(DrawingContext dc, double w, double h, Color color, bool dark)
    {
        var light = new ImmutableRadialGradientBrush(
            [
                new ImmutableGradientStop(0, ChartColors.WithAlpha(color, (byte)(dark ? 0x5C : 0x38))),
                new ImmutableGradientStop(0.45, ChartColors.WithAlpha(color, (byte)(dark ? 0x22 : 0x14))),
                new ImmutableGradientStop(1, ChartColors.WithAlpha(color, 0)),
            ],
            center: new RelativePoint(w / 2, h * 1.02, RelativeUnit.Absolute),
            gradientOrigin: new RelativePoint(w / 2, h * 1.02, RelativeUnit.Absolute),
            radiusX: new RelativeScalar(Math.Max(w * 0.62, 140), RelativeUnit.Absolute),
            radiusY: new RelativeScalar(Math.Max(h * 0.55, 110), RelativeUnit.Absolute));
        dc.DrawRectangle(light, null, new Rect(0, 0, w, h));
    }

    private void DrawScale(DrawingContext dc, Point center, double outer, double value, Color lit, bool dark)
    {
        var unlit = dark ? Color.FromArgb(0x2E, 255, 255, 255) : Color.FromArgb(0x30, 0, 0, 0);
        for (int i = 0; i < Ticks; i++)
        {
            double f = i / (double)(Ticks - 1);
            bool major = i % 5 == 0 || i == Ticks - 1;
            double length = major ? 7 : 4;
            double angle = (Start + (Sweep * f)) * Math.PI / 180;
            var a = new Point(center.X + (outer * Math.Cos(angle)), center.Y + (outer * Math.Sin(angle)));
            var b = new Point(center.X + ((outer - length) * Math.Cos(angle)), center.Y + ((outer - length) * Math.Sin(angle)));
            var color = f <= value + 0.0001 && value > 0 ? ChartColors.WithAlpha(lit, (byte)(major ? 0xF0 : 0xB4)) : unlit;
            dc.DrawLine(ChartColors.Pen(color, major ? 1.6 : 1.2), a, b);
        }
    }

    /// <summary>The arc as short pieces, each a step further from the deep shade to the light one, with round ends.</summary>
    private static void DrawGradientArc(DrawingContext dc, Point center, double radius, double thickness, double arc, Color deep, Color light)
    {
        int pieces = Math.Clamp((int)Math.Ceiling(arc / 2.5), 1, 160);
        double step = arc / pieces;
        for (int i = 0; i < pieces; i++)
        {
            var color = RadialGauge.Lerp(deep, light, Ease.SineInOut((i + 0.5) / pieces));
            double overlap = i < pieces - 1 ? 0.6 : 0;
            dc.DrawGeometry(null, ChartColors.Pen(color, thickness, round: false), RadialGauge.Arc(center, radius, Start + (i * step), step + overlap));
        }

        dc.DrawEllipse(ChartColors.Solid(deep), null, PointAt(center, radius, Start), thickness / 2, thickness / 2);
        dc.DrawEllipse(ChartColors.Solid(light), null, PointAt(center, radius, Start + arc), thickness / 2, thickness / 2);
    }

    /// <summary>A bright comet with a fading tail, running along the arc while something is being worked on.</summary>
    private void DrawComet(DrawingContext dc, Point center, double radius, double thickness, Color color)
    {
        const double Tail = 64;
        double headAngle = Start + (Sweep * _phase);
        double tailStart = Math.Max(Start, headAngle - Tail);
        double length = headAngle - tailStart;
        if (length < 1)
        {
            return;
        }

        int pieces = 24;
        double step = length / pieces;
        for (int i = 0; i < pieces; i++)
        {
            double f = (i + 1) / (double)pieces;
            var piece = ChartColors.WithAlpha(RadialGauge.Lerp(color, Colors.White, 0.5), (byte)(255 * f * f * 0.85));
            dc.DrawGeometry(null, ChartColors.Pen(piece, thickness * 0.55, round: false), RadialGauge.Arc(center, radius, tailStart + (i * step), step + 0.4));
        }

        dc.DrawEllipse(ChartColors.Solid(Color.FromArgb(0xF0, 255, 255, 255)), null, PointAt(center, radius, headAngle), thickness * 0.3, thickness * 0.3);
    }

    private void DrawLabels(DrawingContext dc, Point center, double radius, double thickness, bool dark)
    {
        var brush = ChartColors.Solid(ChartColors.Theme("TextTertiary"));
        var typeface = new Typeface(ChartFonts.Ui, FontStyle.Normal, FontWeight.SemiBold);
        foreach (var (text, angle) in new[] { (StartLabel, Start), (EndLabel, Start + Sweep) })
        {
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 12, brush);
            var end = PointAt(center, radius, angle);
            dc.DrawText(formatted, new Point(end.X - (formatted.Width / 2), end.Y + (thickness / 2) + 6));
        }
    }

    private static Point PointAt(Point center, double radius, double degrees)
    {
        double a = degrees * Math.PI / 180;
        return new Point(center.X + (radius * Math.Cos(a)), center.Y + (radius * Math.Sin(a)));
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
}
