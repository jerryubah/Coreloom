using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Coreloom.App.Behaviors;
using Coreloom.App.Themes;

namespace Coreloom.App.Controls;

/// <summary>
/// The dial in the Gaming and Smart Optimize banners (since 1.8.3): a thick arc of three quarters, open at the bottom,
/// filled in one color from a deeper shade to a lighter one with a soft glow under it; a fine scale of ticks inside it
/// lit up to the value; the numbers at the two ends of the arc; and the dial's light rising from the bottom of the tile
/// it sits in. It fills the space it's given and centres itself in it, so its tile's left and right walls are always the
/// same distance away. While <see cref="IsSpinning"/> a bright comet runs along the arc. (The Mac app draws the same.)
/// </summary>
public sealed class HeroDial : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(HeroDial), new PropertyMetadata(0.0, OnValueChanged));

    /// <summary>The color on the green … red scale (0 … 3), or NaN for the accent.</summary>
    public static readonly DependencyProperty ToneProperty = DependencyProperty.Register(
        nameof(Tone), typeof(double), typeof(HeroDial), new PropertyMetadata(double.NaN, OnToneChanged));

    public static readonly DependencyProperty DialSizeProperty = DependencyProperty.Register(
        nameof(DialSize), typeof(double), typeof(HeroDial), new FrameworkPropertyMetadata(220.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(
        nameof(Thickness), typeof(double), typeof(HeroDial), new FrameworkPropertyMetadata(12.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StartLabelProperty = DependencyProperty.Register(
        nameof(StartLabel), typeof(string), typeof(HeroDial), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty EndLabelProperty = DependencyProperty.Register(
        nameof(EndLabel), typeof(string), typeof(HeroDial), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsSpinningProperty = DependencyProperty.Register(
        nameof(IsSpinning), typeof(bool), typeof(HeroDial), new PropertyMetadata(false, (d, _) => ((HeroDial)d).UpdateSpin()));

    private static readonly DependencyProperty DisplayValueProperty = DependencyProperty.Register(
        "DisplayValue", typeof(double), typeof(HeroDial), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly DependencyProperty DisplayToneProperty = DependencyProperty.Register(
        "DisplayTone", typeof(double), typeof(HeroDial), new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly DependencyProperty PhaseProperty = DependencyProperty.Register(
        "Phase", typeof(double), typeof(HeroDial), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    private const double Start = 135;   // degrees, clockwise from the right: bottom left
    private const double Sweep = 270;   // to the bottom right
    private const int Ticks = 46;

    /// <summary>Wider, fainter copies of the arc under it: a soft glow without a blur effect.</summary>
    private static readonly (double Widen, double Alpha)[] GlowLayers =
        [(3.6, 0x06), (3.2, 0x07), (2.8, 0x08), (2.45, 0x09), (2.1, 0x0A), (1.8, 0x0B), (1.5, 0x0C), (1.25, 0x0D)];

    private bool _themeHooked;

    public HeroDial()
    {
        Loaded += (_, _) =>
        {
            HookTheme(true);
            UpdateSpin();
        };
        Unloaded += (_, _) =>
        {
            HookTheme(false);
            BeginAnimation(PhaseProperty, null);
        };
    }

    /// <summary>0 … 1: how much of the arc is lit.</summary>
    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Tone
    {
        get => (double)GetValue(ToneProperty);
        set => SetValue(ToneProperty, value);
    }

    /// <summary>The dial's diameter at most (it shrinks to fit a smaller space).</summary>
    public double DialSize
    {
        get => (double)GetValue(DialSizeProperty);
        set => SetValue(DialSizeProperty, value);
    }

    public double Thickness
    {
        get => (double)GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    /// <summary>The number under the start of the arc (bottom left), such as "0".</summary>
    public string? StartLabel
    {
        get => (string?)GetValue(StartLabelProperty);
        set => SetValue(StartLabelProperty, value);
    }

    /// <summary>The number under the end of the arc (bottom right), such as "9".</summary>
    public string? EndLabel
    {
        get => (string?)GetValue(EndLabelProperty);
        set => SetValue(EndLabelProperty, value);
    }

    public bool IsSpinning
    {
        get => (bool)GetValue(IsSpinningProperty);
        set => SetValue(IsSpinningProperty, value);
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var dial = (HeroDial)d;
        double target = Math.Clamp(double.IsFinite((double)e.NewValue) ? (double)e.NewValue : 0, 0, 1);
        if (!AnimatedText.IsGloballyEnabled || !dial.IsVisible)
        {
            dial.BeginAnimation(DisplayValueProperty, null);
            dial.SetValue(DisplayValueProperty, target);
            return;
        }

        dial.BeginAnimation(DisplayValueProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(650))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd,
        });
    }

    private static void OnToneChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var dial = (HeroDial)d;
        double target = (double)e.NewValue;
        double current = (double)dial.GetValue(DisplayToneProperty);
        if (!double.IsFinite(target) || !double.IsFinite(current) || !AnimatedText.IsGloballyEnabled || !dial.IsVisible)
        {
            dial.BeginAnimation(DisplayToneProperty, null);
            dial.SetValue(DisplayToneProperty, target);
            return;
        }

        dial.BeginAnimation(DisplayToneProperty, new DoubleAnimation(Math.Clamp(target, 0, 3), TimeSpan.FromMilliseconds(450))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd,
        });
    }

    private void UpdateSpin()
    {
        if (IsSpinning && !Motion.ReducedMotion && IsLoaded)
        {
            BeginAnimation(PhaseProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(1.6)) { RepeatBehavior = RepeatBehavior.Forever });
        }
        else
        {
            BeginAnimation(PhaseProperty, null);
            InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        double size = Math.Min(Math.Min(w, h) - 8, DialSize);
        if (size < 40)
        {
            return;
        }

        bool dark = ThemeService.Instance?.IsDark ?? true;
        double thickness = Math.Clamp(Thickness, 4, size / 8);
        var center = new Point(w / 2, h / 2);
        double radius = (size - thickness) / 2;
        double value = Math.Clamp((double)GetValue(DisplayValueProperty), 0, 1);
        var (deep, main, light) = Colors3();

        DrawLight(dc, w, h, main, dark);

        // The face: a faint disc, lighter at the top, so the dial reads as one object on the glass.
        double face = radius - thickness - 6;
        var faceBrush = new RadialGradientBrush(Color.FromArgb((byte)(dark ? 0x10 : 0x30), 255, 255, 255), Color.FromArgb(0, 255, 255, 255))
        {
            Center = new Point(0.5, 0.15),
            GradientOrigin = new Point(0.5, 0.15),
            RadiusX = 0.9,
            RadiusY = 0.9,
        };
        faceBrush.Freeze();
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
                dc.DrawGeometry(null, ChartColors.Pen(ThemePalette.WithAlpha(main, (byte)(alpha * (dark ? 1 : 0.6))), thickness * widen),
                    RadialGauge.Arc(center, radius, Start, arc));
            }

            DrawGradientArc(dc, center, radius, thickness, arc, deep, light);
            var head = PointAt(center, radius, Start + arc);
            var halo = new RadialGradientBrush(ThemePalette.WithAlpha(light, 0xB0), ThemePalette.WithAlpha(light, 0));
            halo.Freeze();
            dc.DrawEllipse(halo, null, head, thickness * 1.5, thickness * 1.5);
            dc.DrawEllipse(ChartColors.Solid(Color.FromArgb(0xE6, 255, 255, 255)), null, head, thickness * 0.2, thickness * 0.2);
        }

        if (IsSpinning && !Motion.ReducedMotion)
        {
            DrawComet(dc, center, radius, thickness, light, (double)GetValue(PhaseProperty));
        }

        DrawLabels(dc, center, radius, thickness);
    }

    /// <summary>The deeper shade the arc starts in, the color itself, and the lighter shade it ends in.</summary>
    private (Color Deep, Color Main, Color Light) Colors3()
    {
        double tone = (double)GetValue(DisplayToneProperty);
        var main = double.IsFinite(tone) ? RadialGauge.ScaleColor(tone) : ChartColors.Theme("Accent");
        return (ThemePalette.Shift(main, 0, -0.12), main, RadialGauge.Lerp(main, Colors.White, 0.38));
    }

    /// <summary>The dial's light, rising from the bottom of the space it fills (the tile it sits in).</summary>
    private static void DrawLight(DrawingContext dc, double w, double h, Color color, bool dark)
    {
        var light = new RadialGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            Center = new Point(w / 2, h * 1.02),
            GradientOrigin = new Point(w / 2, h * 1.02),
            RadiusX = Math.Max(w * 0.62, 140),
            RadiusY = Math.Max(h * 0.55, 110),
        };
        light.GradientStops.Add(new GradientStop(ThemePalette.WithAlpha(color, (byte)(dark ? 0x5C : 0x38)), 0));
        light.GradientStops.Add(new GradientStop(ThemePalette.WithAlpha(color, (byte)(dark ? 0x22 : 0x14)), 0.45));
        light.GradientStops.Add(new GradientStop(ThemePalette.WithAlpha(color, 0), 1));
        light.Freeze();
        dc.DrawRectangle(light, null, new Rect(0, 0, w, h));
    }

    private static void DrawScale(DrawingContext dc, Point center, double outer, double value, Color lit, bool dark)
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
            var color = f <= value + 0.0001 && value > 0 ? ThemePalette.WithAlpha(lit, (byte)(major ? 0xF0 : 0xB4)) : unlit;
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
            double t = (i + 0.5) / pieces;
            var color = RadialGauge.Lerp(deep, light, -(Math.Cos(Math.PI * t) - 1) / 2);
            double overlap = i < pieces - 1 ? 0.6 : 0;
            dc.DrawGeometry(null, ChartColors.Pen(color, thickness, round: false), RadialGauge.Arc(center, radius, Start + (i * step), step + overlap));
        }

        dc.DrawEllipse(ChartColors.Solid(deep), null, PointAt(center, radius, Start), thickness / 2, thickness / 2);
        dc.DrawEllipse(ChartColors.Solid(light), null, PointAt(center, radius, Start + arc), thickness / 2, thickness / 2);
    }

    /// <summary>A bright comet with a fading tail, running along the arc while something is being worked on.</summary>
    private static void DrawComet(DrawingContext dc, Point center, double radius, double thickness, Color color, double phase)
    {
        const double Tail = 64;
        double headAngle = Start + (Sweep * phase);
        double tailStart = Math.Max(Start, headAngle - Tail);
        double length = headAngle - tailStart;
        if (length < 1)
        {
            return;
        }

        const int Pieces = 24;
        double step = length / Pieces;
        for (int i = 0; i < Pieces; i++)
        {
            double f = (i + 1) / (double)Pieces;
            var piece = ThemePalette.WithAlpha(RadialGauge.Lerp(color, Colors.White, 0.5), (byte)(255 * f * f * 0.85));
            dc.DrawGeometry(null, ChartColors.Pen(piece, thickness * 0.55, round: false), RadialGauge.Arc(center, radius, tailStart + (i * step), step + 0.4));
        }

        dc.DrawEllipse(ChartColors.Solid(Color.FromArgb(0xF0, 255, 255, 255)), null, PointAt(center, radius, headAngle), thickness * 0.3, thickness * 0.3);
    }

    private void DrawLabels(DrawingContext dc, Point center, double radius, double thickness)
    {
        var brush = ChartColors.Solid(ChartColors.Theme("TextTertiary"));
        var typeface = new Typeface(ChartFonts.Ui, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        foreach (var (text, angle) in new[] { (StartLabel, Start), (EndLabel, Start + Sweep) })
        {
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 12, brush, pixelsPerDip);
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
