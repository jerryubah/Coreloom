using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Coreloom.App.Behaviors;
using Coreloom.App.Themes;

namespace Coreloom.App.Controls;

/// <summary>
/// Smooth-cornered rectangles ("squircles"): each corner eases out of the straight edge before turning, instead of
/// meeting a circular arc at a hard tangent. This is Figma's corner smoothing (the same curve as Apple's icons and
/// cards): with smoothing ξ the corner starts (1 + ξ)·r from the vertex, a shorter circular arc of radius r sits in the
/// middle and two cubic Béziers lead into it.
/// </summary>
public static class Squircle
{
    /// <summary>Apple's corner smoothing (about 60 %).</summary>
    public const double DefaultSmoothing = 0.6;

    /// <summary>The outline of <paramref name="rect"/> with smoothed corners, frozen.</summary>
    public static Geometry Create(Rect rect, CornerRadius radii, double smoothing = DefaultSmoothing)
    {
        var geometry = new StreamGeometry();
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            geometry.Freeze();
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
            ctx.BeginFigure(p, true, true);

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
        }

        geometry.Freeze();
        return geometry;

        static Point Line(StreamGeometryContext ctx, Point to)
        {
            ctx.LineTo(to, true, true);
            return to;
        }

        static Point Curve(StreamGeometryContext ctx, Point from, (double X, double Y) c1, (double X, double Y) c2, (double X, double Y) end)
        {
            var to = new Point(from.X + end.X, from.Y + end.Y);
            ctx.BezierTo(new Point(from.X + c1.X, from.Y + c1.Y), new Point(from.X + c2.X, from.Y + c2.Y), to, true, true);
            return to;
        }

        static Point Arc(StreamGeometryContext ctx, Point from, double dx, double dy, double radius)
        {
            var to = new Point(from.X + dx, from.Y + dy);
            ctx.ArcTo(to, new Size(radius, radius), 0, false, SweepDirection.Clockwise, true, true);
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
/// A <see cref="Border"/> drawn as a squircle (<see cref="Squircle"/>), used for every card and rounded surface so corners
/// look the same everywhere. Styles written for <c>Border</c> (Card, Card.Inset) apply to it unchanged.
/// <para>
/// A card can also glow: <see cref="GlowKey"/> names a color (a series such as "Gpu", a status such as "Success", or
/// "Accent") that lights the card from the bottom and along its lower edge, like the light of a lamp under frosted glass.
/// With <see cref="EdgeGlow"/> the pointer lights the card's rim where it is nearest, the light following it smoothly and
/// growing as it nears the edge; only the edge glows, never the content.
/// </para>
/// </summary>
public class SquircleBorder : Border
{
    public static readonly DependencyProperty SmoothingProperty = DependencyProperty.Register(
        nameof(Smoothing), typeof(double), typeof(SquircleBorder),
        new FrameworkPropertyMetadata(Squircle.DefaultSmoothing, FrameworkPropertyMetadataOptions.AffectsRender, OnShapeChanged));

    public static readonly DependencyProperty GlowKeyProperty = DependencyProperty.Register(
        nameof(GlowKey), typeof(string), typeof(SquircleBorder),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnGlowKeyChanged));

    public static readonly DependencyProperty GlowStrengthProperty = DependencyProperty.Register(
        nameof(GlowStrength), typeof(double), typeof(SquircleBorder),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty EdgeGlowProperty = DependencyProperty.Register(
        nameof(EdgeGlow), typeof(bool), typeof(SquircleBorder), new PropertyMetadata(false, OnEdgeGlowChanged));

    public static readonly DependencyProperty ClipContentProperty = DependencyProperty.Register(
        nameof(ClipContent), typeof(bool), typeof(SquircleBorder),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsArrange));

    public static readonly DependencyProperty ShadowProperty = DependencyProperty.Register(
        nameof(Shadow), typeof(double), typeof(SquircleBorder),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SheenProperty = DependencyProperty.Register(
        nameof(Sheen), typeof(double), typeof(SquircleBorder),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    private Geometry? _outer;
    private Geometry? _stroke;
    private (Size Size, CornerRadius Radii, double Thickness, double Smoothing) _shapeKey;
    private bool _themeHooked;
    private bool _renderingHooked;
    private bool _hovering;
    private double _hover;           // 0 … 1, eases toward 1 while the pointer is over the card
    private Point _pointer;          // where the light is (eases toward _target)
    private Point _target;           // where the pointer is
    private TimeSpan _lastFrame;
    private DispatcherTimer? _settle;

    public SquircleBorder()
    {
        Loaded += (_, _) => HookTheme(GlowKey is not null || EdgeGlow);
        Unloaded += (_, _) =>
        {
            HookTheme(false);
            HookRendering(false);
            _hovering = false;
            _hover = 0;
        };
    }

    /// <summary>How much the corners ease into the edges, 0 (circular) to 1 (fully smoothed); 0.6 by default.</summary>
    public double Smoothing
    {
        get => (double)GetValue(SmoothingProperty);
        set => SetValue(SmoothingProperty, value);
    }

    /// <summary>The color the card glows with ("Gpu", "Tone.Warm", "Success", "Accent" …); null for none.</summary>
    public string? GlowKey
    {
        get => (string?)GetValue(GlowKeyProperty);
        set => SetValue(GlowKeyProperty, value);
    }

    /// <summary>Scales the glow (1 for data cards, about 1.6 for a page's hero card).</summary>
    public double GlowStrength
    {
        get => (double)GetValue(GlowStrengthProperty);
        set => SetValue(GlowStrengthProperty, value);
    }

    /// <summary>The pointer lights the card's edge (in the glow color, or the accent).</summary>
    public bool EdgeGlow
    {
        get => (bool)GetValue(EdgeGlowProperty);
        set => SetValue(EdgeGlowProperty, value);
    }

    /// <summary>Clips the content to the squircle (for pictures that reach the edges).</summary>
    public bool ClipContent
    {
        get => (bool)GetValue(ClipContentProperty);
        set => SetValue(ClipContentProperty, value);
    }

    /// <summary>Depth of a soft shadow under a floating surface (0 for none, about 1 for the sidebar).</summary>
    public double Shadow
    {
        get => (double)GetValue(ShadowProperty);
        set => SetValue(ShadowProperty, value);
    }

    /// <summary>Strength of a specular highlight along the top rim (glass), 0 for none.</summary>
    public double Sheen
    {
        get => (double)GetValue(SheenProperty);
        set => SetValue(SheenProperty, value);
    }

    private static bool IsDark => ThemeService.Instance?.IsDark ?? true;

    private static void OnShapeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((SquircleBorder)d)._outer = null;

    private static void OnGlowKeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var border = (SquircleBorder)d;
        if (border.IsLoaded)
        {
            border.HookTheme(e.NewValue is not null || border.EdgeGlow);
        }
    }

    private static void OnEdgeGlowChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var border = (SquircleBorder)d;
        if (!(bool)e.NewValue)
        {
            border._hovering = false;
            border._hover = 0;
            border.HookRendering(false);
            border.InvalidateVisual();
        }
        else if (border.IsLoaded)
        {
            border.HookTheme(true);
        }
    }

    /// <summary>The color for a glow key: a status ("Success", "Info", "Elevated", "Critical"), "Accent", or a series.</summary>
    public static Color GlowColor(string key) => key switch
    {
        "Success" or "Info" or "Elevated" or "Critical" or "Accent" => ChartColors.Theme(key),
        _ => ChartColors.Series(key),
    };

    protected override Geometry? GetLayoutClip(Size layoutSlotSize) =>
        ClipContent ? Shape(RenderSize).Outer : base.GetLayoutClip(layoutSlotSize);

    private (Geometry Outer, Geometry Stroke) Shape(Size size)
    {
        double thickness = BorderThickness.Left;
        var key = (size, CornerRadius, thickness, Smoothing);
        if (_outer is null || _stroke is null || key != _shapeKey)
        {
            var rect = new Rect(size);
            _outer = Squircle.Create(rect, CornerRadius, Smoothing);
            _stroke = thickness > 0 ? Squircle.Inset(rect, CornerRadius, thickness / 2, Smoothing) : _outer;
            _shapeKey = key;
        }

        return (_outer, _stroke);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = RenderSize;
        if (size.Width <= 0 || size.Height <= 0)
        {
            return;
        }

        var (outer, stroke) = Shape(size);
        double thickness = BorderThickness.Left;
        bool dark = IsDark;

        if (Shadow > 0)
        {
            DrawShadow(dc, size, dark);
        }

        if (Background is { } background)
        {
            dc.DrawGeometry(background, null, outer);
        }

        Color? glow = GlowKey is { } key ? GlowColor(key) : null;
        if (glow is { } lamp)
        {
            DrawLamp(dc, outer, size, lamp, dark);
        }

        if (thickness > 0 && BorderBrush is { } borderBrush)
        {
            dc.DrawGeometry(null, Pen(borderBrush, thickness), stroke);
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
            DrawPointerGlow(dc, outer, stroke, size, glow ?? ChartColors.Theme("Accent"));
        }
    }

    /// <summary>A soft light rising from below the card's bottom edge.</summary>
    private void DrawLamp(DrawingContext dc, Geometry outer, Size size, Color color, bool dark)
    {
        double strength = Math.Clamp(GlowStrength, 0, 3);
        byte Alpha(double darkAlpha, double lightAlpha) => (byte)Math.Clamp((dark ? darkAlpha : lightAlpha) * strength, 0, 255);
        var lamp = new RadialGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            Center = new Point(size.Width * 0.5, size.Height * 1.08),
            GradientOrigin = new Point(size.Width * 0.5, size.Height * 1.08),
            RadiusX = Math.Max(size.Width * 0.78, 60),
            RadiusY = Math.Max(size.Height * 0.95, 60),
            GradientStops =
            {
                new GradientStop(ThemePalette.WithAlpha(color, Alpha(0x46, 0x2A)), 0),
                new GradientStop(ThemePalette.WithAlpha(color, Alpha(0x1A, 0x10)), 0.45),
                new GradientStop(ThemePalette.WithAlpha(color, 0), 1),
            },
        };
        lamp.Freeze();
        dc.PushClip(outer);
        dc.DrawRectangle(lamp, null, new Rect(size));
        dc.Pop();
    }

    /// <summary>The lower rim catching the light: clear along the top, the glow color brightening toward the bottom.</summary>
    private void DrawLitEdge(DrawingContext dc, Geometry stroke, Size size, double thickness, Color color, bool dark)
    {
        double strength = Math.Clamp(GlowStrength, 0, 3);
        var bright = ThemePalette.Shift(color, 0, 0.1);
        var edge = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, size.Height),
            GradientStops =
            {
                new GradientStop(ThemePalette.WithAlpha(bright, 0), 0.45),
                new GradientStop(ThemePalette.WithAlpha(bright, (byte)Math.Clamp((dark ? 0x70 : 0x50) * strength, 0, 255)), 1),
            },
        };
        edge.Freeze();
        dc.DrawGeometry(null, Pen(edge, thickness), stroke);
    }

    /// <summary>A specular highlight along the top rim, fading down the sides (glass).</summary>
    private void DrawSheen(DrawingContext dc, Geometry stroke, Size size, double thickness, bool dark)
    {
        byte top = (byte)Math.Clamp((dark ? 0x3A : 0xC0) * Sheen, 0, 255);
        var sheen = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = new Point(0, 0),
            EndPoint = new Point(size.Width * 0.18, Math.Min(size.Height, 160)),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(top, 255, 255, 255), 0),
                new GradientStop(Color.FromArgb(0, 255, 255, 255), 1),
            },
        };
        sheen.Freeze();
        dc.DrawGeometry(null, Pen(sheen, thickness), stroke);
    }

    /// <summary>A shadow made of a few widening, faint outlines (no blur effect, so text stays ClearType).</summary>
    private void DrawShadow(DrawingContext dc, Size size, bool dark)
    {
        const int Layers = 6;
        double depth = Math.Clamp(Shadow, 0, 3);
        byte alpha = (byte)Math.Clamp((dark ? 0x12 : 0x07) * depth, 0, 255);
        var brush = ChartColors.Solid(Color.FromArgb(alpha, 0, 0, 0));
        for (int i = Layers; i >= 1; i--)
        {
            double grow = i * 2.2 * depth;
            var rect = new Rect(-grow, -grow + (i * 1.2 * depth), size.Width + (2 * grow), size.Height + (2 * grow));
            dc.DrawGeometry(brush, null, Squircle.Create(rect, Squircle.Grow(CornerRadius, grow), Smoothing));
        }
    }

    /// <summary>
    /// The rim near the pointer lights up in two colors (the glow color and its warmer neighbour): a crisp line on the edge
    /// and a soft halo just inside it, clipped to the card so nothing spills over the content around it.
    /// </summary>
    private void DrawPointerGlow(DrawingContext dc, Geometry outer, Geometry stroke, Size size, Color color)
    {
        double nearest = Math.Min(Math.Min(_pointer.X, size.Width - _pointer.X), Math.Min(_pointer.Y, size.Height - _pointer.Y));
        double reach = Math.Max(48, Math.Min(size.Width, size.Height) * 0.5);
        double intensity = _hover * (0.55 + (0.45 * (1 - Math.Clamp(nearest / reach, 0, 1))));
        double radius = Math.Clamp(Math.Max(size.Width, size.Height) * 0.5, 110, 240);
        var primary = ThemePalette.Shift(color, 0, 0.16);
        var secondary = ThemePalette.Shift(color, 38, 0.06);

        dc.PushClip(outer);
        foreach (var (width, alpha) in Layers)
        {
            var brush = new RadialGradientBrush
            {
                MappingMode = BrushMappingMode.Absolute,
                Center = _pointer,
                GradientOrigin = _pointer,
                RadiusX = radius,
                RadiusY = radius,
                GradientStops =
                {
                    new GradientStop(ThemePalette.WithAlpha(primary, (byte)(255 * alpha * intensity)), 0),
                    new GradientStop(ThemePalette.WithAlpha(secondary, (byte)(150 * alpha * intensity)), 0.45),
                    new GradientStop(ThemePalette.WithAlpha(secondary, 0), 1),
                },
            };
            brush.Freeze();
            dc.DrawGeometry(null, Pen(brush, width), stroke);
        }

        dc.Pop();
    }

    // Halo widths and strengths, widest first; the last one is the crisp line on the rim.
    private static readonly (double Width, double Alpha)[] Layers = [(14, 0.10), (7, 0.18), (3, 0.42), (1.3, 1.0)];

    private static Pen Pen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness) { LineJoin = PenLineJoin.Round };
        if (pen.CanFreeze)
        {
            pen.Freeze();
        }

        return pen;
    }

    // ------------------------------------------------------------------ pointer light

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
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

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!EdgeGlow)
        {
            return;
        }

        _hovering = true;
        _target = e.GetPosition(this);
        Animate();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
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
            HookRendering(false);
            InvalidateVisual();
            return;
        }

        HookRendering(true);
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = e is RenderingEventArgs args ? args.RenderingTime : TimeSpan.Zero;
        if (now == _lastFrame)
        {
            return; // Rendering can fire more than once per frame
        }

        double dt = _lastFrame == TimeSpan.Zero ? 1 / 60.0 : Math.Clamp((now - _lastFrame).TotalSeconds, 0, 0.1);
        _lastFrame = now;

        double follow = 1 - Math.Exp(-dt * 16);          // the light trails the pointer a little
        double fade = 1 - Math.Exp(-dt * (_hovering ? 9 : 6));
        _pointer = new Point(_pointer.X + ((_target.X - _pointer.X) * follow), _pointer.Y + ((_target.Y - _pointer.Y) * follow));
        _hover += ((_hovering ? 1 : 0) - _hover) * fade;

        bool settled = Math.Abs(_target.X - _pointer.X) < 0.3 && Math.Abs(_target.Y - _pointer.Y) < 0.3
            && Math.Abs((_hovering ? 1 : 0) - _hover) < 0.004;
        if (settled)
        {
            _pointer = _target;
            _hover = _hovering ? 1 : 0;
            HookRendering(false);
        }

        InvalidateVisual();
    }

    private void HookRendering(bool hook)
    {
        if (hook == _renderingHooked)
        {
            return;
        }

        _renderingHooked = hook;
        _lastFrame = TimeSpan.Zero;
        if (hook)
        {
            CompositionTarget.Rendering += OnRendering;
        }
        else
        {
            CompositionTarget.Rendering -= OnRendering;
        }
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
        }, Dispatcher);
        _settle.Stop();
        _settle.Start();
    }
}
