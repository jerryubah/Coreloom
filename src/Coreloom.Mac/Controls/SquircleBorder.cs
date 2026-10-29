using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Rendering;
using Avalonia.Threading;
using Coreloom.Mac.Behaviors;
using Coreloom.Mac.Services;
using Coreloom.Mac.Themes;

namespace Coreloom.Mac.Controls;

/// <summary>
/// Smooth-cornered rectangles ("squircles"): each corner eases out of the straight edge before turning, instead of
/// meeting a circular arc at a hard tangent. This is Figma's corner smoothing (the curve of Apple's icons and cards):
/// with smoothing ξ the corner starts (1 + ξ)·r from the vertex, a shorter circular arc of radius r sits in the middle
/// and two cubic Béziers lead into it.
/// </summary>
public static class Squircle
{
    /// <summary>Apple's corner smoothing (about 60 %).</summary>
    public const double DefaultSmoothing = 0.6;

    /// <summary>The outline of <paramref name="rect"/> with smoothed corners.</summary>
    public static Geometry Create(Rect rect, CornerRadius radii, double smoothing = DefaultSmoothing)
    {
        var geometry = new StreamGeometry();
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return geometry;
        }

        double budget = Math.Min(rect.Width, rect.Height) / 2;
        smoothing = Math.Clamp(smoothing, 0, 1);
        var tr = Corner(radii.TopRight, smoothing, budget);
        var br = Corner(radii.BottomRight, smoothing, budget);
        var bl = Corner(radii.BottomLeft, smoothing, budget);
        var tl = Corner(radii.TopLeft, smoothing, budget);
        double x = rect.X, y = rect.Y, w = rect.Width, h = rect.Height;

        using (var ctx = geometry.Open())
        {
            var p = new Point(x + w - tr.P, y);
            ctx.BeginFigure(p, true);

            // Top right: along the top edge, down the right edge.
            if (tr.R > 0)
            {
                p = Curve(ctx, p, (tr.A, 0), (tr.A + tr.B, 0), (tr.A + tr.B + tr.C, tr.D));
                p = Arc(ctx, p, tr.ArcLength, tr.ArcLength, tr.R);
                p = Curve(ctx, p, (tr.D, tr.C), (tr.D, tr.B + tr.C), (tr.D, tr.A + tr.B + tr.C));
            }
            else
            {
                p = Line(ctx, new Point(x + w, y));
            }

            p = Line(ctx, new Point(x + w, y + h - br.P));
            if (br.R > 0)
            {
                p = Curve(ctx, p, (0, br.A), (0, br.A + br.B), (-br.D, br.A + br.B + br.C));
                p = Arc(ctx, p, -br.ArcLength, br.ArcLength, br.R);
                p = Curve(ctx, p, (-br.C, br.D), (-(br.B + br.C), br.D), (-(br.A + br.B + br.C), br.D));
            }
            else
            {
                p = Line(ctx, new Point(x + w, y + h));
            }

            p = Line(ctx, new Point(x + bl.P, y + h));
            if (bl.R > 0)
            {
                p = Curve(ctx, p, (-bl.A, 0), (-(bl.A + bl.B), 0), (-(bl.A + bl.B + bl.C), -bl.D));
                p = Arc(ctx, p, -bl.ArcLength, -bl.ArcLength, bl.R);
                p = Curve(ctx, p, (-bl.D, -bl.C), (-bl.D, -(bl.B + bl.C)), (-bl.D, -(bl.A + bl.B + bl.C)));
            }
            else
            {
                p = Line(ctx, new Point(x, y + h));
            }

            p = Line(ctx, new Point(x, y + tl.P));
            if (tl.R > 0)
            {
                p = Curve(ctx, p, (0, -tl.A), (0, -(tl.A + tl.B)), (tl.D, -(tl.A + tl.B + tl.C)));
                p = Arc(ctx, p, tl.ArcLength, -tl.ArcLength, tl.R);
                Curve(ctx, p, (tl.C, -tl.D), (tl.B + tl.C, -tl.D), (tl.A + tl.B + tl.C, -tl.D));
            }
            else
            {
                Line(ctx, new Point(x, y));
            }

            ctx.EndFigure(true);
        }

        return geometry;

        static Point Line(StreamGeometryContext ctx, Point to)
        {
            ctx.LineTo(to);
            return to;
        }

        static Point Curve(StreamGeometryContext ctx, Point from, (double X, double Y) c1, (double X, double Y) c2, (double X, double Y) end)
        {
            var to = new Point(from.X + end.X, from.Y + end.Y);
            ctx.CubicBezierTo(new Point(from.X + c1.X, from.Y + c1.Y), new Point(from.X + c2.X, from.Y + c2.Y), to);
            return to;
        }

        static Point Arc(StreamGeometryContext ctx, Point from, double dx, double dy, double radius)
        {
            var to = new Point(from.X + dx, from.Y + dy);
            ctx.ArcTo(to, new Size(radius, radius), 0, false, SweepDirection.Clockwise);
            return to;
        }
    }

    /// <summary>The same rectangle shrunk by <paramref name="inset"/> on every side, corners shrunk to match.</summary>
    public static Geometry Inset(Rect rect, CornerRadius radii, double inset, double smoothing = DefaultSmoothing)
    {
        var inner = new Rect(rect.X + inset, rect.Y + inset, Math.Max(0, rect.Width - (2 * inset)), Math.Max(0, rect.Height - (2 * inset)));
        return Create(inner, Shrink(radii, inset), smoothing);
    }

    public static CornerRadius Shrink(CornerRadius radii, double by) => new(
        Math.Max(0, radii.TopLeft - by), Math.Max(0, radii.TopRight - by), Math.Max(0, radii.BottomRight - by), Math.Max(0, radii.BottomLeft - by));

    public static CornerRadius Grow(CornerRadius radii, double by) => new(
        radii.TopLeft + by, radii.TopRight + by, radii.BottomRight + by, radii.BottomLeft + by);

    /// <summary>Figma's corner parameters (figma-squircle, "preserve smoothing").</summary>
    internal static CornerShape Corner(double radius, double smoothing, double budget)
    {
        double r = Math.Clamp(double.IsFinite(radius) ? radius : 0, 0, budget);
        if (r <= 0.01)
        {
            return default;
        }

        double p = (1 + smoothing) * r;
        double arcMeasure = 90 * (1 - smoothing);
        double arcLength = Math.Sin(Rad(arcMeasure / 2)) * r * Math.Sqrt(2);
        double angleAlpha = (90 - arcMeasure) / 2;
        double p3ToP4 = r * Math.Tan(Rad(angleAlpha / 2));
        double angleBeta = 45 * smoothing;
        double c = p3ToP4 * Math.Cos(Rad(angleBeta));
        double d = c * Math.Tan(Rad(angleBeta));
        double b = (p - arcLength - c - d) / 3;
        double a = 2 * b;
        if (p > budget)
        {
            // Not enough room for the full ease: keep the arc and shorten the straight lead-in.
            double p1ToP3 = budget - d - arcLength - c;
            double minA = p1ToP3 / 6;
            double maxB = p1ToP3 - minA;
            b = Math.Min(b, maxB);
            a = p1ToP3 - b;
            p = budget;
        }

        return new CornerShape(r, p, a, b, c, d, arcLength);

        static double Rad(double degrees) => degrees * Math.PI / 180;
    }

    internal readonly record struct CornerShape(double R, double P, double A, double B, double C, double D, double ArcLength);
}

/// <summary>
/// A border drawn as a squircle (<see cref="Squircle"/>), used for every card and rounded surface so corners look the
/// same everywhere. It lays out like Avalonia's Border (thickness, then padding, around one child).
/// <para>
/// A card can also glow: <see cref="GlowKey"/> names a color (a series such as "Gpu", a status such as "Success", a
/// temperature such as "Temp.Cool", or "Accent") that lights the card from the bottom and along its lower edge, like the
/// light of a lamp under frosted glass; when the key changes the light fades to the new color. With
/// <see cref="EdgeGlow"/> the pointer lights the card's rim where it is nearest, as a soft glow around the outside of the
/// edge with a bright line on it, following the pointer smoothly; the content itself never glows.
/// </para>
/// </summary>
public class SquircleBorder : Decorator, ICustomHitTest
{
    public static readonly StyledProperty<IBrush?> BackgroundProperty = Border.BackgroundProperty.AddOwner<SquircleBorder>();

    public static readonly StyledProperty<IBrush?> BorderBrushProperty = Border.BorderBrushProperty.AddOwner<SquircleBorder>();

    public static readonly StyledProperty<Thickness> BorderThicknessProperty = Border.BorderThicknessProperty.AddOwner<SquircleBorder>();

    public static readonly StyledProperty<CornerRadius> CornerRadiusProperty = Border.CornerRadiusProperty.AddOwner<SquircleBorder>();

    public static readonly StyledProperty<double> SmoothingProperty =
        AvaloniaProperty.Register<SquircleBorder, double>(nameof(Smoothing), Squircle.DefaultSmoothing);

    public static readonly StyledProperty<string?> GlowKeyProperty =
        AvaloniaProperty.Register<SquircleBorder, string?>(nameof(GlowKey));

    public static readonly StyledProperty<double> GlowStrengthProperty =
        AvaloniaProperty.Register<SquircleBorder, double>(nameof(GlowStrength), 1.0);

    public static readonly StyledProperty<bool> EdgeGlowProperty =
        AvaloniaProperty.Register<SquircleBorder, bool>(nameof(EdgeGlow));

    public static readonly StyledProperty<bool> ClipContentProperty =
        AvaloniaProperty.Register<SquircleBorder, bool>(nameof(ClipContent));

    public static readonly StyledProperty<double> ShadowProperty =
        AvaloniaProperty.Register<SquircleBorder, double>(nameof(Shadow));

    public static readonly StyledProperty<double> SheenProperty =
        AvaloniaProperty.Register<SquircleBorder, double>(nameof(Sheen));

    public static readonly StyledProperty<bool> FrostedProperty =
        AvaloniaProperty.Register<SquircleBorder, bool>(nameof(Frosted));

    /// <summary>How far the edge glow spreads outside the card.</summary>
    private const double OuterReach = 14;

    /// <summary>How long a new glow color takes to fade in.</summary>
    private const double GlowFadeSeconds = 0.45;

    // The outer glow's widths (twice how far each reaches out) and strengths, widest and faintest first.
    private static readonly (double Width, double Alpha)[] OuterLayers = [(OuterReach * 2, 0.07), (16, 0.12), (8, 0.2), (3.5, 0.34)];

    private Geometry? _outer;
    private Geometry? _stroke;
    private Geometry? _outside;
    private (Size Size, CornerRadius Radii, double Thickness, double Smoothing) _shapeKey;
    private bool _themeHooked;
    private Animator.Tween? _motion;
    private bool _hovering;
    private double _hover;
    private Point _pointer;
    private Point _target;
    private double _lastSeconds;
    private DispatcherTimer? _settle;
    private Color? _glowFrom;
    private double _glowFade = 1;
    private Color? _glowShown;
    private bool _frostHooked;

    static SquircleBorder()
    {
        AffectsRender<SquircleBorder>(BackgroundProperty, BorderBrushProperty, BorderThicknessProperty, CornerRadiusProperty, SmoothingProperty,
            GlowKeyProperty, GlowStrengthProperty, ShadowProperty, SheenProperty, FrostedProperty);
        AffectsMeasure<SquircleBorder>(BorderThicknessProperty);
    }

    public SquircleBorder()
    {
        Loaded += (_, _) =>
        {
            HookTheme(GlowKey is not null || EdgeGlow || Shadow > 0 || Sheen > 0);
            HookFrost(Frosted);
        };
        Unloaded += (_, _) =>
        {
            HookTheme(false);
            HookFrost(false);
            StopMotion();
            _hovering = false;
            _hover = 0;
            _glowFrom = null;
            _glowFade = 1;
        };
    }

    public IBrush? Background
    {
        get => GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    public IBrush? BorderBrush
    {
        get => GetValue(BorderBrushProperty);
        set => SetValue(BorderBrushProperty, value);
    }

    public Thickness BorderThickness
    {
        get => GetValue(BorderThicknessProperty);
        set => SetValue(BorderThicknessProperty, value);
    }

    public CornerRadius CornerRadius
    {
        get => GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    /// <summary>How much the corners ease into the edges, 0 (circular) to 1 (fully smoothed); 0.6 by default.</summary>
    public double Smoothing
    {
        get => GetValue(SmoothingProperty);
        set => SetValue(SmoothingProperty, value);
    }

    /// <summary>The color the card glows with ("Gpu", "Tone.Warm", "Success", "Accent" …); null for none.</summary>
    public string? GlowKey
    {
        get => GetValue(GlowKeyProperty);
        set => SetValue(GlowKeyProperty, value);
    }

    /// <summary>Scales the glow (1 for data cards, about 1.5 for a page's hero card).</summary>
    public double GlowStrength
    {
        get => GetValue(GlowStrengthProperty);
        set => SetValue(GlowStrengthProperty, value);
    }

    /// <summary>The pointer lights the card's edge (in the glow color, or the accent).</summary>
    public bool EdgeGlow
    {
        get => GetValue(EdgeGlowProperty);
        set => SetValue(EdgeGlowProperty, value);
    }

    /// <summary>Clips the content to the squircle (for pictures that reach the edges).</summary>
    public bool ClipContent
    {
        get => GetValue(ClipContentProperty);
        set => SetValue(ClipContentProperty, value);
    }

    /// <summary>Depth of a soft shadow under a floating surface (0 for none, about 1 for the sidebar).</summary>
    public double Shadow
    {
        get => GetValue(ShadowProperty);
        set => SetValue(ShadowProperty, value);
    }

    /// <summary>Strength of a specular highlight along the top rim (glass), 0 for none.</summary>
    public double Sheen
    {
        get => GetValue(SheenProperty);
        set => SetValue(SheenProperty, value);
    }

    /// <summary>
    /// Frosted glass: the card shows a blurred copy of the window's background behind it (the part right behind it,
    /// following it as the page scrolls), under its own <see cref="Background"/> tint. Off for a card inside another.
    /// </summary>
    public bool Frosted
    {
        get => GetValue(FrostedProperty);
        set => SetValue(FrostedProperty, value);
    }

    private static bool IsDark => ThemeService.Instance?.IsDark ?? true;

    /// <summary>The color for a glow key: a status, "Accent", a temperature ("Temp.Cool" … "Temp.Hot"), or a series.</summary>
    public static Color GlowColor(string key) => key switch
    {
        "Success" or "Info" or "Elevated" or "Critical" or "Accent" => ChartColors.Theme(key),
        _ when key.StartsWith("Temp.", StringComparison.Ordinal) => ChartColors.Theme(key),
        _ => ChartColors.Series(key),
    };

    /// <summary>Only the card itself takes the pointer, never its shadow or the glow around its edge.</summary>
    public bool HitTest(Point point) => new Rect(Bounds.Size).Contains(point);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SmoothingProperty || change.Property == CornerRadiusProperty || change.Property == BorderThicknessProperty)
        {
            _outer = null;
            UpdateClip();
        }
        else if (change.Property == ClipContentProperty)
        {
            UpdateClip();
        }
        else if (change.Property == FrostedProperty && IsLoaded)
        {
            HookFrost(change.NewValue is true);
        }
        else if (change.Property == GlowKeyProperty)
        {
            if (IsLoaded)
            {
                HookTheme(true);
            }

            // A new color (a temperature warming up, say) fades in over about 450 ms rather than switching at once.
            if (_glowShown is { } shown && change.NewValue is not null && IsEffectivelyVisible && !Motion.ReducedMotion)
            {
                _glowFrom = shown;
                _glowFade = 0;
                StartMotion();
            }
            else
            {
                _glowFrom = null;
                _glowFade = 1;
            }
        }
        else if (change.Property == EdgeGlowProperty)
        {
            if (change.NewValue is not true)
            {
                _hovering = false;
                _hover = 0;
                StopMotion();
                InvalidateVisual();
            }
            else if (IsLoaded)
            {
                HookTheme(true);
            }
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var inset = BorderThickness + Padding;
        if (Child is null)
        {
            return new Size(inset.Left + inset.Right, inset.Top + inset.Bottom);
        }

        Child.Measure(availableSize.Deflate(inset));
        return Child.DesiredSize.Inflate(inset);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Child?.Arrange(new Rect(finalSize).Deflate(BorderThickness + Padding));
        UpdateClip(finalSize);
        return finalSize;
    }

    private void UpdateClip(Size? size = null)
    {
        var s = size ?? Bounds.Size;
        Clip = ClipContent && s.Width > 0 && s.Height > 0 ? Shape(s).Outer : null;
    }

    private Color? CurrentGlow()
    {
        if (GlowKey is not { } key)
        {
            return null;
        }

        var target = GlowColor(key);
        if (_glowFrom is not { } from || _glowFade >= 1)
        {
            return target;
        }

        double t = 1 - Math.Pow(1 - Math.Clamp(_glowFade, 0, 1), 3); // ease out
        return Color.FromArgb(
            (byte)Math.Round(from.A + ((target.A - from.A) * t)),
            (byte)Math.Round(from.R + ((target.R - from.R) * t)),
            (byte)Math.Round(from.G + ((target.G - from.G) * t)),
            (byte)Math.Round(from.B + ((target.B - from.B) * t)));
    }

    private (Geometry Outer, Geometry Stroke) Shape(Size size)
    {
        double thickness = BorderThickness.Left;
        var key = (size, CornerRadius, thickness, Smoothing);
        if (_outer is null || _stroke is null || key != _shapeKey)
        {
            var rect = new Rect(size);
            _outer = Squircle.Create(rect, CornerRadius, Smoothing);
            _stroke = thickness > 0 ? Squircle.Inset(rect, CornerRadius, thickness / 2, Smoothing) : _outer;
            _outside = null;
            _shapeKey = key;
        }

        return (_outer, _stroke);
    }

    /// <summary>The area around the card, out to a little past the reach of the edge glow.</summary>
    private Geometry Outside(Size size, Geometry outer) =>
        _outside ??= new CombinedGeometry(GeometryCombineMode.Exclude,
            new RectangleGeometry(new Rect(-OuterReach - 4, -OuterReach - 4, size.Width + (2 * (OuterReach + 4)), size.Height + (2 * (OuterReach + 4)))),
            outer);

    public override void Render(DrawingContext dc)
    {
        var size = Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0)
        {
            return;
        }

        var (outer, stroke) = Shape(size);
        double thickness = BorderThickness.Left;
        bool dark = IsDark;

        if (Shadow > 0)
        {
            DrawShadow(dc, outer, size, dark);
        }

        if (Frosted)
        {
            DrawFrost(dc, outer, size);
        }

        if (Background is { } background)
        {
            dc.DrawGeometry(background, null, outer);
        }

        Color? glow = CurrentGlow();
        _glowShown = glow;
        if (glow is { } lamp)
        {
            DrawLamp(dc, outer, size, lamp, dark);
        }

        if (thickness > 0 && BorderBrush is { } borderBrush)
        {
            dc.DrawGeometry(null, new Pen(borderBrush, thickness, lineJoin: PenLineJoin.Round), stroke);
        }

        if (glow is { } rim)
        {
            DrawLitEdge(dc, stroke, size, Math.Max(thickness, 1), rim, dark);
        }

        if (Sheen > 0)
        {
            DrawSheen(dc, stroke, size, Math.Max(thickness, 1), dark);
        }

        if (_hover > 0.004 && EdgeGlow)
        {
            DrawPointerGlow(dc, outer, stroke, size, glow ?? ChartColors.Theme("Accent"), dark);
        }
    }

    private static RelativePoint Abs(double x, double y) => new(x, y, RelativeUnit.Absolute);

    /// <summary>The frosted part of the window's background that's behind the card (the whole frost, placed so it lines up).</summary>
    private void DrawFrost(DrawingContext dc, Geometry outer, Size size)
    {
        if (Backdrop.IsSeeThrough || Backdrop.Frost is not { } frost || Backdrop.Host is not { } host || this.TranslatePoint(default, host) is not { } at)
        {
            return;
        }

        var hostSize = host.Bounds.Size;
        using (dc.PushGeometryClip(outer))
        {
            dc.DrawImage(frost, new Rect(frost.Size), new Rect(-at.X, -at.Y, hostSize.Width, hostSize.Height));
        }
    }

    /// <summary>Draws again when the frost changes, or when the card moves over the background (scrolling, layout).</summary>
    private void HookFrost(bool hook)
    {
        if (hook == _frostHooked)
        {
            return;
        }

        _frostHooked = hook;
        if (hook)
        {
            Backdrop.Changed += OnFrostChanged;
            EffectiveViewportChanged += OnViewportChanged;
            RenderOptions.SetBitmapInterpolationMode(this, Avalonia.Media.Imaging.BitmapInterpolationMode.HighQuality);
        }
        else
        {
            Backdrop.Changed -= OnFrostChanged;
            EffectiveViewportChanged -= OnViewportChanged;
        }
    }

    private void OnFrostChanged(object? sender, EventArgs e) => InvalidateVisual();

    private void OnViewportChanged(object? sender, Avalonia.Layout.EffectiveViewportChangedEventArgs e) => InvalidateVisual();

    /// <summary>A soft light rising from below the card's bottom edge.</summary>
    private void DrawLamp(DrawingContext dc, Geometry outer, Size size, Color color, bool dark)
    {
        double strength = Math.Clamp(GlowStrength, 0, 3);
        byte Alpha(double darkAlpha, double lightAlpha) => (byte)Math.Clamp((dark ? darkAlpha : lightAlpha) * strength, 0, 255);
        var center = Abs(size.Width * 0.5, size.Height * 1.08);
        var lamp = new ImmutableRadialGradientBrush(
            [
                new ImmutableGradientStop(0, ChartColors.WithAlpha(color, Alpha(0x46, 0x2A))),
                new ImmutableGradientStop(0.45, ChartColors.WithAlpha(color, Alpha(0x1A, 0x10))),
                new ImmutableGradientStop(1, ChartColors.WithAlpha(color, 0)),
            ],
            center: center,
            gradientOrigin: center,
            radiusX: new RelativeScalar(Math.Max(size.Width * 0.78, 60), RelativeUnit.Absolute),
            radiusY: new RelativeScalar(Math.Max(size.Height * 0.95, 60), RelativeUnit.Absolute));
        using (dc.PushGeometryClip(outer))
        {
            dc.DrawRectangle(lamp, null, new Rect(size));
        }
    }

    /// <summary>The lower rim catching the light: clear along the top, the glow color brightening toward the bottom.</summary>
    private void DrawLitEdge(DrawingContext dc, Geometry stroke, Size size, double thickness, Color color, bool dark)
    {
        double strength = Math.Clamp(GlowStrength, 0, 3);
        var bright = ThemePalette.Shift(color, 0, 0.1);
        var edge = new ImmutableLinearGradientBrush(
            [
                new ImmutableGradientStop(0.45, ChartColors.WithAlpha(bright, 0)),
                new ImmutableGradientStop(1, ChartColors.WithAlpha(bright, (byte)Math.Clamp((dark ? 0x70 : 0x50) * strength, 0, 255))),
            ],
            startPoint: Abs(0, 0),
            endPoint: Abs(0, size.Height));
        dc.DrawGeometry(null, new ImmutablePen(edge, thickness, lineJoin: PenLineJoin.Round), stroke);
    }

    /// <summary>A specular highlight along the top rim, fading down the sides (glass).</summary>
    private void DrawSheen(DrawingContext dc, Geometry stroke, Size size, double thickness, bool dark)
    {
        byte top = (byte)Math.Clamp((dark ? 0x3A : 0xC0) * Sheen, 0, 255);
        var sheen = new ImmutableLinearGradientBrush(
            [
                new ImmutableGradientStop(0, Color.FromArgb(top, 255, 255, 255)),
                new ImmutableGradientStop(1, Color.FromArgb(0, 255, 255, 255)),
            ],
            startPoint: Abs(0, 0),
            endPoint: Abs(size.Width * 0.18, Math.Min(size.Height, 160)));
        dc.DrawGeometry(null, new ImmutablePen(sheen, thickness, lineJoin: PenLineJoin.Round), stroke);
    }

    /// <summary>
    /// A subtle shadow under a floating surface: many faint, widening outlines (no blur effect),
    /// nudged a little down and kept outside the shape, so it never darkens the glass itself. At most about 15 % black
    /// right at the edge in dark mode (half that in light), fading out over 15 points.
    /// </summary>
    private void DrawShadow(DrawingContext dc, Geometry outer, Size size, bool dark)
    {
        const int Layers = 8;
        double depth = Math.Clamp(Shadow, 0, 3);
        byte alpha = (byte)Math.Clamp((dark ? 0x05 : 0x03) * depth, 0, 255);
        var brush = ChartColors.Solid(Color.FromArgb(alpha, 0, 0, 0));
        using (dc.PushGeometryClip(Outside(size, outer)))
        {
            for (int i = Layers; i >= 1; i--)
            {
                double grow = i * 1.75 * Math.Min(depth, 1);
                var rect = new Rect(-grow, -grow + (i * 0.25 * Math.Min(depth, 1)), size.Width + (2 * grow), size.Height + (2 * grow));
                dc.DrawGeometry(brush, null, Squircle.Create(rect, Squircle.Grow(CornerRadius, grow), Smoothing));
            }
        }
    }

    /// <summary>
    /// The edge near the pointer lights up in the card's own glow color: a soft glow spreading outward from the edge
    /// (clipped to the outside, so it never washes over the content), a faint inner rim, and a bright line on the edge.
    /// </summary>
    private void DrawPointerGlow(DrawingContext dc, Geometry outer, Geometry stroke, Size size, Color color, bool dark)
    {
        double nearest = Math.Min(Math.Min(_pointer.X, size.Width - _pointer.X), Math.Min(_pointer.Y, size.Height - _pointer.Y));
        double reach = Math.Max(48, Math.Min(size.Width, size.Height) * 0.5);
        double intensity = _hover * (0.55 + (0.45 * (1 - Math.Clamp(nearest / reach, 0, 1)))) * (dark ? 1 : 0.8);
        double radius = Math.Clamp(Math.Max(size.Width, size.Height) * 0.5, 110, 240);
        var line = ThemePalette.Shift(color, 0, dark ? 0.14 : 0.04);

        IImmutableBrush Light(Color c, double alpha) => new ImmutableRadialGradientBrush(
            [
                new ImmutableGradientStop(0, ChartColors.WithAlpha(c, (byte)Math.Clamp(255 * alpha * intensity, 0, 255))),
                new ImmutableGradientStop(0.5, ChartColors.WithAlpha(c, (byte)Math.Clamp(110 * alpha * intensity, 0, 255))),
                new ImmutableGradientStop(1, ChartColors.WithAlpha(c, 0)),
            ],
            center: Abs(_pointer.X, _pointer.Y),
            gradientOrigin: Abs(_pointer.X, _pointer.Y),
            radiusX: new RelativeScalar(radius, RelativeUnit.Absolute),
            radiusY: new RelativeScalar(radius, RelativeUnit.Absolute));

        using (dc.PushGeometryClip(Outside(size, outer)))
        {
            foreach (var (width, alpha) in OuterLayers)
            {
                dc.DrawGeometry(null, new ImmutablePen(Light(color, alpha), width, lineJoin: PenLineJoin.Round), outer);
            }
        }

        using (dc.PushGeometryClip(outer))
        {
            dc.DrawGeometry(null, new ImmutablePen(Light(color, 0.16), 6, lineJoin: PenLineJoin.Round), stroke);
            dc.DrawGeometry(null, new ImmutablePen(Light(line, 1.0), 1.3, lineJoin: PenLineJoin.Round), stroke);
        }
    }

    // ------------------------------------------------------------------ pointer light

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        if (!EdgeGlow)
        {
            return;
        }

        _hovering = true;
        _target = e.GetPosition(this);
        if (_hover <= 0.004)
        {
            _pointer = _target; // the light appears where the pointer came in, not where it last left
        }

        Animate();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!EdgeGlow)
        {
            return;
        }

        _hovering = true;
        _target = e.GetPosition(this);
        Animate();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (!EdgeGlow)
        {
            return;
        }

        _hovering = false;
        Animate();
    }

    private void Animate()
    {
        if (Motion.ReducedMotion)
        {
            _pointer = _target;
            _hover = _hovering ? 1 : 0;
            _glowFade = 1;
            StopMotion();
            InvalidateVisual();
            return;
        }

        StartMotion();
    }

    private void StartMotion()
    {
        if (_motion is { IsCancelled: false })
        {
            return;
        }

        _lastSeconds = 0;
        _motion = Animator.Loop(OnFrame);
    }

    private void StopMotion()
    {
        _motion?.Cancel();
        _motion = null;
    }

    private void OnFrame(double seconds)
    {
        double dt = Math.Clamp(seconds - _lastSeconds, 0, 0.1);
        _lastSeconds = seconds;
        if (dt <= 0)
        {
            dt = 1 / 120.0;
        }

        double follow = 1 - Math.Exp(-dt * 16);          // the light trails the pointer a little
        double fade = 1 - Math.Exp(-dt * (_hovering ? 9 : 6));
        _pointer = new Point(_pointer.X + ((_target.X - _pointer.X) * follow), _pointer.Y + ((_target.Y - _pointer.Y) * follow));
        _hover += ((_hovering ? 1 : 0) - _hover) * fade;
        if (_glowFade < 1)
        {
            _glowFade = Math.Min(1, _glowFade + (dt / GlowFadeSeconds));
        }

        bool settled = Math.Abs(_target.X - _pointer.X) < 0.3 && Math.Abs(_target.Y - _pointer.Y) < 0.3
            && Math.Abs((_hovering ? 1 : 0) - _hover) < 0.004
            && _glowFade >= 1;
        if (settled)
        {
            _pointer = _target;
            _hover = _hovering ? 1 : 0;
            _glowFrom = null;
            StopMotion();
        }

        InvalidateVisual();
    }

    // ------------------------------------------------------------------ theme

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
            _settle?.Stop();
        }
    }

    /// <summary>Colors cross-fade for about 300 ms after a theme change, so draw again now and once they've settled.</summary>
    private void OnThemeChanged(object? sender, EventArgs e)
    {
        InvalidateVisual();
        _settle ??= new DispatcherTimer(TimeSpan.FromMilliseconds(320), DispatcherPriority.Render, (_, _) =>
        {
            _settle!.Stop();
            InvalidateVisual();
        });
        _settle.Stop();
        _settle.Start();
    }
}
