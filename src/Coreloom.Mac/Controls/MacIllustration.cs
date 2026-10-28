using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Coreloom.Core.Diagnostics;
using Coreloom.Core.Settings;
using Coreloom.Mac.Services;
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
/// A picture of the Mac Coreloom runs on for the Overview header, picked from the model's name: a MacBook (a photo-like
/// picture whose screen shows the Mac's own desktop picture, or Coreloom's in the lighting color, and whose keyboard deck
/// catches the screen's light), or a drawing of an all-in-one, a small or a tall desktop or a tower, lit in the lighting
/// color chosen in Customize. No company logo is on any of them.
/// </summary>
public sealed class MacIllustration : Control
{
    public static readonly StyledProperty<string?> ModelProperty = AvaloniaProperty.Register<MacIllustration, string?>(nameof(Model));

    public static readonly StyledProperty<PcGlow> GlowProperty = AvaloniaProperty.Register<MacIllustration, PcGlow>(nameof(Glow), PcGlow.Blue);

    /// <summary>The screen shows the Mac's own desktop picture (Customize).</summary>
    public static readonly StyledProperty<bool> UseDesktopPictureProperty = AvaloniaProperty.Register<MacIllustration, bool>(nameof(UseDesktopPicture));

    /// <summary>The person turned the desktop picture on themselves (a picture in a folder macOS guards is read only then).</summary>
    public static readonly StyledProperty<bool> DesktopPictureChosenProperty = AvaloniaProperty.Register<MacIllustration, bool>(nameof(DesktopPictureChosen));

    /// <summary>Whether a model has a screen to show a desktop picture on (for Customize).</summary>
    public static readonly FuncValueConverter<string?, bool> HasScreen = new(model => FormOf(model) is MacForm.Laptop or MacForm.AllInOne);

    private const double CanvasWidth = 240;
    private const double CanvasHeight = 180;

    // The MacBook picture (Assets/MacBook, made by build/mac/make-macbook.py): where its screen and keyboard deck are, in
    // the picture's pixels, and the width it's shown at in points.
    private static readonly Rect PictureScreen = new(80.93, 14.38, 511.05 - 80.93, 291.95 - 14.38);
    private static readonly Rect PictureDeck = new(14.5, 297.5, 577.5 - 14.5, 349.0 - 297.5);
    private const double PictureScreenRadius = 3;
    private const double PictureNotchHeight = 8.25;
    private const double MacBookWidth = 204;

    private static readonly Lazy<Bitmap?> MacBookBody = new(() => LoadPicture("body.png"));
    private static readonly Lazy<Bitmap?> MacBookDeck = new(() => LoadPicture("deck.png"));

    private DispatcherTimer? _refresh;
    private RenderTargetBitmap? _wallpaper;
    private (PcGlow Glow, Color Light, bool Dark)? _wallpaperKey;
    private RenderTargetBitmap? _reflection;
    private IImage? _reflectionSource;

    private static readonly Color[] Rainbow =
    [
        Color.Parse("#FF2933"), Color.Parse("#FFB01A"), Color.Parse("#1AD96B"), Color.Parse("#18C8FF"), Color.Parse("#0D6BFF"), Color.Parse("#9E4DFF"),
    ];

    static MacIllustration()
    {
        AffectsRender<MacIllustration>(ModelProperty, GlowProperty, UseDesktopPictureProperty);
        UseDesktopPictureProperty.Changed.AddClassHandler<MacIllustration>((picture, _) => picture.RefreshDesktopPicture());
        DesktopPictureChosenProperty.Changed.AddClassHandler<MacIllustration>((picture, _) => picture.RefreshDesktopPicture());
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

    public bool UseDesktopPicture
    {
        get => GetValue(UseDesktopPictureProperty);
        set => SetValue(UseDesktopPictureProperty, value);
    }

    public bool DesktopPictureChosen
    {
        get => GetValue(DesktopPictureChosenProperty);
        set => SetValue(DesktopPictureChosenProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        DesktopPicture.Changed += OnDesktopPictureChanged;

        // A new desktop picture shows within half a minute (the check is quick when nothing changed).
        _refresh = new DispatcherTimer(TimeSpan.FromSeconds(30), DispatcherPriority.Background, (_, _) => RefreshDesktopPicture());
        _refresh.Start();
        RefreshDesktopPicture();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        DesktopPicture.Changed -= OnDesktopPictureChanged;
        _refresh?.Stop();
        _refresh = null;
    }

    private void OnDesktopPictureChanged(object? sender, EventArgs e) => InvalidateVisual();

    private void RefreshDesktopPicture()
    {
        if (UseDesktopPicture && IsVisible && FormOf(Model) is MacForm.Laptop or MacForm.AllInOne && _refresh is not null)
        {
            DesktopPicture.Refresh(DesktopPictureChosen);
        }
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

        if (FormOf(Model) == MacForm.Laptop && MacBookBody.Value is { } body)
        {
            DrawMacBook(context, body, new Rect(bounds.Size));
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

    /// <summary>
    /// The MacBook picture, <see cref="MacBookWidth"/> points wide and centered: the lighting color's glow behind it, the
    /// desktop picture (or Coreloom's) on its screen under a faint glare, and the screen's light on the keyboard deck.
    /// </summary>
    private void DrawMacBook(DrawingContext context, Bitmap body, Rect bounds)
    {
        var size = body.Size;
        double scale = Math.Min(Math.Min(MacBookWidth, bounds.Width) / size.Width, bounds.Height * 0.94 / size.Height);
        var area = new Rect((bounds.Width - (size.Width * scale)) / 2, (bounds.Height - (size.Height * scale)) / 2, size.Width * scale, size.Height * scale);
        Rect Place(Rect r) => new(area.X + (r.X * scale), area.Y + (r.Y * scale), r.Width * scale, r.Height * scale);
        var screen = Place(PictureScreen);
        var (light, deep) = LightingColors();

        DrawHalo(context, screen.Inflate(new Thickness(screen.Width * 0.2, screen.Height * 0.24, screen.Width * 0.2, screen.Height * 0.32)), light);

        var (picture, glow) = ScreenPicture(light, deep);
        using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality }))
        {
            double radius = PictureScreenRadius * scale;
            using (context.PushClip(new RoundedRect(screen, radius, radius, 0, 0)))
            {
                DrawFilled(context, picture, screen);

                // The menu bar, as deep as the camera's notch, and a faint glare across the glass.
                context.DrawRectangle(new SolidColorBrush(Color.FromArgb(52, 0, 0, 0)), null, new Rect(screen.X, screen.Y, screen.Width, PictureNotchHeight * scale));
                context.DrawRectangle(Glare, null, screen);
            }

            context.DrawImage(body, area);
            if (Reflection(glow, body) is { } reflection)
            {
                // Added as light (screen blending): bright colors show on the aluminium and keys, dark ones leave them be.
                using (context.PushRenderOptions(new RenderOptions { BitmapBlendingMode = BitmapBlendingMode.Screen }))
                {
                    context.DrawImage(reflection, area);
                }
            }
        }
    }

    private static readonly IBrush Glare = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        GradientStops =
        [
            new GradientStop(Color.FromArgb(34, 255, 255, 255), 0),
            new GradientStop(Color.FromArgb(7, 255, 255, 255), 0.42),
            new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.43),
            new GradientStop(Color.FromArgb(0, 255, 255, 255), 1),
        ],
    };

    /// <summary>The light the screen casts around the Mac, in the lighting color (all its colors for the rainbow).</summary>
    private void DrawHalo(DrawingContext context, Rect area, Color light)
    {
        var falloff = new RadialGradientBrush
        {
            GradientStops = [new GradientStop(Color.FromArgb(150, 255, 255, 255), 0), new GradientStop(Color.FromArgb(60, 255, 255, 255), 0.5), new GradientStop(Color.FromArgb(0, 255, 255, 255), 1)],
        };
        using (context.PushOpacityMask(falloff, area))
        {
            context.DrawRectangle(Glow == PcGlow.Rainbow ? RainbowBrush(horizontal: true) : new SolidColorBrush(light), null, area);
        }
    }

    /// <summary>The rainbow lighting as a brush: red through violet.</summary>
    internal static LinearGradientBrush RainbowBrush(bool horizontal)
    {
        var stops = new GradientStops();
        for (int i = 0; i < Rainbow.Length; i++)
        {
            stops.Add(new GradientStop(Rainbow[i], i / (double)(Rainbow.Length - 1)));
        }

        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, horizontal ? 0.5 : 1, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, horizontal ? 0.5 : 0, RelativeUnit.Relative),
            GradientStops = stops,
        };
    }

    /// <summary>
    /// What the screen shows, and a tiny copy of it for the light it casts: the Mac's desktop picture when it's on and could
    /// be read, otherwise Coreloom's picture in the lighting color.
    /// </summary>
    private (IImage Picture, IImage Glow) ScreenPicture(Color light, Color deep)
    {
        if (UseDesktopPicture && DesktopPicture.Picture is { } picture && DesktopPicture.Glow is { } glow)
        {
            return (picture, glow);
        }

        bool dark = ThemeService.Instance?.IsDark != false;
        if (_wallpaper is null || _wallpaperKey != (Glow, light, dark))
        {
            // Drawn once per color at about the screen's size on a Retina display.
            const double width = 172;
            const double height = 111;
            const double pixels = 2.8;
            _wallpaper?.Dispose();
            _wallpaper = new RenderTargetBitmap(new PixelSize((int)(width * pixels), (int)(height * pixels)));
            using (var wallpaper = _wallpaper.CreateDrawingContext())
            using (wallpaper.PushTransform(Matrix.CreateScale(pixels, pixels)))
            {
                var area = new Rect(0, 0, width, height);
                wallpaper.DrawRectangle(Wallpaper(light, deep, area), null, area);
                DrawWaves(wallpaper, area, light);
            }

            _wallpaperKey = (Glow, light, dark);
        }

        return (_wallpaper, _wallpaper);
    }

    /// <summary>Draws an image to fill an area, cut to its shape from the middle.</summary>
    private static void DrawFilled(DrawingContext context, IImage image, Rect area)
    {
        var size = image.Size;
        if (size.Width <= 0 || size.Height <= 0)
        {
            return;
        }

        double scale = Math.Max(area.Width / size.Width, area.Height / size.Height);
        double width = area.Width / scale;
        double height = area.Height / scale;
        context.DrawImage(image, new Rect((size.Width - width) / 2, (size.Height - height) / 2, width, height), area);
    }

    /// <summary>
    /// The screen's light on the keyboard deck, as an image the size of the MacBook picture: the screen's colors, blurred
    /// and mirrored at the hinge, brightest there and fading toward the front edge (the deck mask).
    /// </summary>
    private RenderTargetBitmap? Reflection(IImage glow, Bitmap body)
    {
        if (ReferenceEquals(glow, _reflectionSource) && _reflection is not null)
        {
            return _reflection;
        }

        if (MacBookDeck.Value is not { } deck)
        {
            return null;
        }

        _reflection?.Dispose();
        _reflection = new RenderTargetBitmap(body.PixelSize);
        _reflectionSource = glow;
        var full = new Rect(body.Size);
        double hinge = PictureDeck.Y;
        double depth = PictureDeck.Height * 1.15;
        using (var context = _reflection.CreateDrawingContext())
        using (context.PushOpacityMask(new ImageBrush(deck) { Stretch = Stretch.Fill }, full))
        using (context.PushOpacity(0.62))
        using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality }))
        using (context.PushTransform(Matrix.CreateScale(1, -1) * Matrix.CreateTranslation(0, 2 * hinge)))
        {
            // Wider than the screen: the light spreads across the deck.
            context.DrawImage(glow, new Rect(PictureScreen.X - 56, hinge - depth, PictureScreen.Width + 112, depth));
        }

        return _reflection;
    }

    private static Bitmap? LoadPicture(string name)
    {
        try
        {
            return new Bitmap(AssetLoader.Open(new Uri($"avares://Coreloom/Assets/MacBook/{name}")));
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or ArgumentException or UriFormatException)
        {
            Log.Warn("Overview", $"The MacBook picture ({name}) couldn't be loaded; a drawing shows instead.", e);
            return null;
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
        DrawHalo(context, body.Inflate(new Thickness(30, 24)), light);

        // The chin in a pastel of the lighting color, the bezel white.
        context.DrawRectangle(new SolidColorBrush(Mix(light, Colors.White, 0.55)), null, new RoundedRect(body, 9));
        var front = new Rect(body.X, body.Y, body.Width, body.Height - 18);
        context.DrawRectangle(new SolidColorBrush(Color.Parse("#F2F4F7")), null, new RoundedRect(front, 9, 9, 0, 0));
        var screen = front.Deflate(new Thickness(6, 6, 6, 4));
        using (context.PushClip(new RoundedRect(screen, 2)))
        {
            if (UseDesktopPicture && DesktopPicture.Picture is { } picture)
            {
                using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality }))
                {
                    DrawFilled(context, picture, screen);
                }
            }
            else
            {
                context.DrawRectangle(Wallpaper(light, deep, screen), null, screen);
                DrawWaves(context, screen, light);
            }
        }
    }

    private void DrawDesktop(DrawingContext context, Color light, bool dark, bool tall)
    {
        double height = tall ? 74 : 40;
        double width = tall ? 132 : 150;
        var front = new Rect((CanvasWidth - width) / 2, 150 - height, width, height);
        var top = new Rect(front.X + 4, front.Y - 14, front.Width - 8, 18);

        // Light on the desk under it, and its shadow.
        DrawHalo(context, new Rect(front.X - 34, front.Bottom - 16, front.Width + 68, 34), light);
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
        DrawHalo(context, new Rect(body.X - 40, body.Bottom - 14, body.Width + 80, 30), light);
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
