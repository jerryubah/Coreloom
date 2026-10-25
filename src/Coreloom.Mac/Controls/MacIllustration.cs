using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Coreloom.Core.Settings;
using Coreloom.Mac.Themes;
using Coreloom.ViewModels.Pages;

namespace Coreloom.Mac.Controls;

/// <summary>The kind of Mac drawn in the Overview header.</summary>
public enum MacForm
{
    Laptop,
    AllInOne,
    Mini,
    Studio,
    Tower,
}

/// <summary>
/// A drawing of the Mac Coreloom runs on for the Overview header: a laptop, an all-in-one, a small or a tall desktop or a
/// tower, picked from the model's name, its screen (or status light) lit in the lighting color chosen in Customize. It's
/// Coreloom's own simple drawing in the spirit of the hardware, with no company logo on it.
/// </summary>
public sealed class MacIllustration : Control
{
    public static readonly StyledProperty<string?> ModelProperty = AvaloniaProperty.Register<MacIllustration, string?>(nameof(Model));

    public static readonly StyledProperty<PcGlow> GlowProperty = AvaloniaProperty.Register<MacIllustration, PcGlow>(nameof(Glow), PcGlow.Blue);

    private const double CanvasWidth = 240;
    private const double CanvasHeight = 180;

    private static readonly Color[] Rainbow =
    [
        Color.Parse("#FF2933"), Color.Parse("#FFB01A"), Color.Parse("#1AD96B"), Color.Parse("#18C8FF"), Color.Parse("#0D6BFF"), Color.Parse("#9E4DFF"),
    ];

    static MacIllustration()
    {
        AffectsRender<MacIllustration>(ModelProperty, GlowProperty);
    }

    public MacIllustration()
    {
        if (ThemeService.Instance is { } theme)
        {
            theme.ThemeChanged += (_, _) => InvalidateVisual();
        }
    }

    /// <summary>The model's name ("MacBook Pro", "Mac Studio"…).</summary>
    public string? Model
    {
        get => GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    public PcGlow Glow
    {
        get => GetValue(GlowProperty);
        set => SetValue(GlowProperty, value);
    }

    public static MacForm FormOf(string? model)
    {
        string name = model ?? string.Empty;
        return name.Contains("MacBook", StringComparison.OrdinalIgnoreCase) ? MacForm.Laptop
            : name.Contains("iMac", StringComparison.OrdinalIgnoreCase) ? MacForm.AllInOne
            : name.Contains("Studio", StringComparison.OrdinalIgnoreCase) ? MacForm.Studio
            : name.Contains("Mac Pro", StringComparison.OrdinalIgnoreCase) ? MacForm.Tower
            : MacForm.Mini;
    }

    public override void Render(DrawingContext context)
    {
        var bounds = Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        double scale = Math.Min(bounds.Width / CanvasWidth, bounds.Height / CanvasHeight);
        var offset = new Point((bounds.Width - (CanvasWidth * scale)) / 2, (bounds.Height - (CanvasHeight * scale)) / 2);
        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offset.X, offset.Y)))
        {
            var (light, deep) = LightingColors();
            bool dark = ThemeService.Instance?.IsDark != false;
            switch (FormOf(Model))
            {
                case MacForm.Laptop:
                    DrawLaptop(context, light, deep, dark);
                    break;
                case MacForm.AllInOne:
                    DrawAllInOne(context, light, deep, dark);
                    break;
                case MacForm.Tower:
                    DrawTower(context, light, dark);
                    break;
                default:
                    DrawDesktop(context, light, dark, tall: FormOf(Model) == MacForm.Studio);
                    break;
            }
        }
    }

    /// <summary>The lighting color and a deeper shade of it (accent and rainbow included).</summary>
    private (Color Light, Color Deep) LightingColors()
    {
        Color light = Glow switch
        {
            PcGlow.Accent => ThemeService.Instance?.AccentColor ?? Color.Parse("#2D6BFF"),
            PcGlow.Rainbow => Rainbow[4],
            _ => Color.Parse(PcFans.GlowHex(Glow) ?? "#0D6BFF"),
        };
        return (light, Mix(light, Color.Parse("#05070C"), 0.72));
    }

    private IBrush Wallpaper(Color light, Color deep, Rect area)
    {
        if (Glow == PcGlow.Rainbow)
        {
            var stops = new GradientStops();
            for (int i = 0; i < Rainbow.Length; i++)
            {
                stops.Add(new GradientStop(Mix(Rainbow[i], Color.Parse("#0A0C14"), 0.25), i / (double)(Rainbow.Length - 1)));
            }

            return new LinearGradientBrush { StartPoint = new RelativePoint(0, 1, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative), GradientStops = stops };
        }

        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops =
            [
                new GradientStop(deep, 0),
                new GradientStop(Mix(light, deep, 0.35), 0.55),
                new GradientStop(Mix(light, Colors.White, 0.25), 1),
            ],
        };
    }

    /// <summary>The flowing lines across a screen, like a desktop picture.</summary>
    private static void DrawWaves(DrawingContext context, Rect area, Color light)
    {
        using var clip = context.PushClip(area);
        for (int i = 0; i < 4; i++)
        {
            double y = area.Bottom - (area.Height * (0.18 + (i * 0.17)));
            var figure = new StreamGeometry();
            using (var g = figure.Open())
            {
                g.BeginFigure(new Point(area.Left - 10, y + 18), false);
                g.CubicBezierTo(new Point(area.Left + (area.Width * 0.3), y - 30 + (i * 6)), new Point(area.Left + (area.Width * 0.6), y + 34 - (i * 4)), new Point(area.Right + 10, y - 22 + (i * 3)));
            }

            var pen = new Pen(new SolidColorBrush(Mix(light, Colors.White, 0.4), 0.10 + (i * 0.05)), 6 - i, lineCap: PenLineCap.Round);
            context.DrawGeometry(null, pen, figure);
        }

        // A soft highlight in the upper right.
        var glow = new RadialGradientBrush
        {
            Center = new RelativePoint(0.82, 0.18, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.82, 0.18, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.6, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.6, RelativeUnit.Relative),
            GradientStops = [new GradientStop(Color.FromArgb(90, 255, 255, 255), 0), new GradientStop(Color.FromArgb(0, 255, 255, 255), 1)],
        };
        context.DrawRectangle(glow, null, area);
    }

    private static IBrush Aluminium(bool dark, double top = 0, double bottom = 1) => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, top, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0, bottom, RelativeUnit.Relative),
        GradientStops = dark
            ? [new GradientStop(Color.Parse("#C7CBD2"), 0), new GradientStop(Color.Parse("#8E939C"), 0.55), new GradientStop(Color.Parse("#5E636C"), 1)]
            : [new GradientStop(Color.Parse("#E9ECF0"), 0), new GradientStop(Color.Parse("#C3C8D0"), 0.55), new GradientStop(Color.Parse("#9AA0AA"), 1)],
    };

    private static void Shadow(DrawingContext context, Rect area, double opacity)
    {
        var brush = new RadialGradientBrush
        {
            GradientStops = [new GradientStop(Color.FromArgb((byte)(255 * opacity), 0, 0, 0), 0), new GradientStop(Color.FromArgb(0, 0, 0, 0), 1)],
        };
        context.DrawEllipse(brush, null, area);
    }

    private void DrawLaptop(DrawingContext context, Color light, Color deep, bool dark)
    {
        var lid = new Rect(26, 12, 188, 124);
        Shadow(context, new Rect(4, 136, 232, 26), 0.45);

        // The screen's light falls around it.
        context.DrawRectangle(null, null, new RoundedRect(lid, 10), new BoxShadows(new BoxShadow { Blur = 46, Spread = -6, Color = Color.FromArgb(110, light.R, light.G, light.B) }));
        context.DrawRectangle(new SolidColorBrush(Color.Parse("#07080B")), new Pen(Aluminium(dark), 1.6), new RoundedRect(lid, 10));
        var screen = lid.Deflate(new Thickness(7, 7, 7, 9));
        using (context.PushClip(new RoundedRect(screen, 4)))
        {
            context.DrawRectangle(Wallpaper(light, deep, screen), null, screen);
            DrawWaves(context, screen, light);
            // The menu bar.
            context.DrawRectangle(new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)), null, new Rect(screen.X, screen.Y, screen.Width, 6));
        }

        // The camera housing at the top of the screen.
        context.DrawRectangle(new SolidColorBrush(Color.Parse("#07080B")), null, new RoundedRect(new Rect(110, 19, 20, 7), 0, 0, 3, 3));

        // The base and its lip.
        var deck = new StreamGeometry();
        using (var g = deck.Open())
        {
            g.BeginFigure(new Point(10, 137), true);
            g.LineTo(new Point(230, 137));
            g.QuadraticBezierTo(new Point(236, 137), new Point(233, 142));
            g.QuadraticBezierTo(new Point(231, 146), new Point(222, 146));
            g.LineTo(new Point(18, 146));
            g.QuadraticBezierTo(new Point(9, 146), new Point(7, 142));
            g.QuadraticBezierTo(new Point(4, 137), new Point(10, 137));
            g.EndFigure(true);
        }

        context.DrawGeometry(Aluminium(dark), null, deck);
        context.DrawRectangle(new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)), null, new RoundedRect(new Rect(98, 137, 44, 3), 0, 0, 2, 2));
    }

    private void DrawAllInOne(DrawingContext context, Color light, Color deep, bool dark)
    {
        Shadow(context, new Rect(64, 156, 112, 18), 0.4);

        // The stand.
        var stand = new StreamGeometry();
        using (var g = stand.Open())
        {
            g.BeginFigure(new Point(102, 128), true);
            g.LineTo(new Point(138, 128));
            g.LineTo(new Point(144, 160));
            g.LineTo(new Point(96, 160));
            g.EndFigure(true);
        }

        context.DrawGeometry(Aluminium(dark), null, stand);
        context.DrawRectangle(Aluminium(dark), null, new RoundedRect(new Rect(84, 159, 72, 4), 2));

        var body = new Rect(16, 6, 208, 128);
        context.DrawRectangle(null, null, new RoundedRect(body, 9), new BoxShadows(new BoxShadow { Blur = 40, Spread = -8, Color = Color.FromArgb(90, light.R, light.G, light.B) }));

        // The chin in a pastel of the lighting color, the bezel white.
        context.DrawRectangle(new SolidColorBrush(Mix(light, Colors.White, 0.55)), null, new RoundedRect(body, 9));
        var front = new Rect(body.X, body.Y, body.Width, body.Height - 18);
        context.DrawRectangle(new SolidColorBrush(Color.Parse("#F2F4F7")), null, new RoundedRect(front, 9, 9, 0, 0));
        var screen = front.Deflate(new Thickness(6, 6, 6, 4));
        using (context.PushClip(new RoundedRect(screen, 2)))
        {
            context.DrawRectangle(Wallpaper(light, deep, screen), null, screen);
            DrawWaves(context, screen, light);
        }
    }

    private void DrawDesktop(DrawingContext context, Color light, bool dark, bool tall)
    {
        double height = tall ? 74 : 40;
        double width = tall ? 132 : 150;
        var front = new Rect((CanvasWidth - width) / 2, 150 - height, width, height);
        var top = new Rect(front.X + 4, front.Y - 14, front.Width - 8, 18);

        // Light on the desk under it, and its shadow.
        context.DrawEllipse(new RadialGradientBrush
        {
            GradientStops = [new GradientStop(Color.FromArgb(120, light.R, light.G, light.B), 0), new GradientStop(Color.FromArgb(0, light.R, light.G, light.B), 1)],
        }, null, new Rect(front.X - 34, front.Bottom - 16, front.Width + 68, 34));
        Shadow(context, new Rect(front.X - 10, front.Bottom - 6, front.Width + 20, 16), 0.5);

        // The top, seen from slightly above.
        context.DrawRectangle(new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = dark
                ? [new GradientStop(Color.Parse("#B9BDC4"), 0), new GradientStop(Color.Parse("#D8DBE0"), 1)]
                : [new GradientStop(Color.Parse("#D6D9DE"), 0), new GradientStop(Color.Parse("#F1F3F5"), 1)],
        }, null, new RoundedRect(top, 12, 12, 0, 0));
        context.DrawRectangle(Aluminium(dark), null, new RoundedRect(front, 4, 4, 12, 12));

        // Ports on the front and the status light.
        double portsY = front.Y + (front.Height * 0.55);
        for (int i = 0; i < (tall ? 3 : 2); i++)
        {
            context.DrawRectangle(new SolidColorBrush(Color.Parse("#2A2E35")), null, new RoundedRect(new Rect(front.X + 18 + (i * 12), portsY - 2, 6, 4), 1.5));
        }

        if (tall)
        {
            context.DrawRectangle(new SolidColorBrush(Color.Parse("#2A2E35")), null, new RoundedRect(new Rect(front.X + 60, portsY - 1.5, 18, 3), 1.5));
        }

        var led = new Rect(front.Right - 20, portsY - 2.5, 5, 5);
        context.DrawEllipse(new SolidColorBrush(light), null, led.Center, 2.5, 2.5);
        context.DrawRectangle(null, null, new RoundedRect(led, 2.5), new BoxShadows(new BoxShadow { Blur = 10, Spread = 1, Color = light }));
    }

    private void DrawTower(DrawingContext context, Color light, bool dark)
    {
        var body = new Rect(76, 20, 88, 140);
        context.DrawEllipse(new RadialGradientBrush
        {
            GradientStops = [new GradientStop(Color.FromArgb(110, light.R, light.G, light.B), 0), new GradientStop(Color.FromArgb(0, light.R, light.G, light.B), 1)],
        }, null, new Rect(body.X - 40, body.Bottom - 14, body.Width + 80, 30));
        Shadow(context, new Rect(body.X - 8, body.Bottom - 6, body.Width + 16, 14), 0.5);
        context.DrawRectangle(Aluminium(dark, 0, 1.4), null, new RoundedRect(body, 8));

        // The handles and the lattice of round holes.
        var handle = new Pen(Aluminium(dark), 4, lineCap: PenLineCap.Round);
        context.DrawLine(handle, new Point(body.X + 10, body.Y + 2), new Point(body.X + 10, body.Y - 10));
        context.DrawLine(handle, new Point(body.Right - 10, body.Y + 2), new Point(body.Right - 10, body.Y - 10));
        var hole = new SolidColorBrush(Color.FromArgb(150, 20, 22, 26));
        for (double y = body.Y + 12; y < body.Bottom - 8; y += 9)
        {
            bool shift = ((int)((y - body.Y) / 9) % 2) == 1;
            for (double x = body.X + 10 + (shift ? 4.5 : 0); x < body.Right - 8; x += 9)
            {
                context.DrawEllipse(hole, null, new Point(x, y), 3, 3);
            }
        }

        context.DrawEllipse(new SolidColorBrush(light), null, new Point(body.Right - 12, body.Y + 8), 1.8, 1.8);
    }

    private static Color Mix(Color a, Color b, double amount) => Color.FromArgb(
        (byte)Math.Round(a.A + ((b.A - a.A) * amount)),
        (byte)Math.Round(a.R + ((b.R - a.R) * amount)),
        (byte)Math.Round(a.G + ((b.G - a.G) * amount)),
        (byte)Math.Round(a.B + ((b.B - a.B) * amount)));
}
