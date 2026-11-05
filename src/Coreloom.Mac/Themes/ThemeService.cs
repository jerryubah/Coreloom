using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Coreloom.Core.Settings;
using Coreloom.Mac.Behaviors;
using Coreloom.Mac.Services;
using Coreloom.ViewModels.Services;

namespace Coreloom.Mac.Themes;

/// <summary>
/// Owns every theme brush ("Brush.*" and "Series.*.Brush" resources) and the theme variant the built-in controls follow.
/// <para>
/// Avalonia's brushes are mutable, so each color is one <see cref="SolidColorBrush"/> placed in the application's
/// resources once and recolored in place on a theme or accent change (cross-fading over about 280 ms, like the Windows
/// app): everything using it, through a DynamicResource or <see cref="GetBrush"/>, follows without re-binding. Gradients
/// are rebuilt and republished. "System" follows macOS's appearance (Light, Dark or Auto) as it changes.
/// </para>
/// </summary>
public sealed class ThemeService : IThemeService
{
    private static readonly TimeSpan FadeDuration = TimeSpan.FromMilliseconds(280);
    private readonly IResourceDictionary _resources;
    private readonly Dictionary<string, SolidColorBrush> _brushes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (Color From, Color To)> _fading = new(StringComparer.Ordinal);
    private DispatcherTimer? _fadeTimer;
    private DateTime _fadeStart;
    private AppearanceSettings? _current;
    private bool _isDark = true;
    private bool? _iconIsDark;
    private bool _initialized;

    public ThemeService(IResourceDictionary resources)
    {
        _resources = resources;
        if (Application.Current?.PlatformSettings is { } platform)
        {
            platform.ColorValuesChanged += (_, _) =>
            {
                if (_current is { Theme: ThemePreference.System } current)
                {
                    Dispatcher.UIThread.Post(() => Apply(current));
                }
            };
        }
    }

    public static ThemeService? Instance { get; private set; }

    public event EventHandler? ThemeChanged;

    /// <summary>Raised when the app icon changes between the light and the dark-mode icon.</summary>
    public event EventHandler? AppIconChanged;

    public bool IsDark => _isDark;

    /// <summary>The translucent sidebar (macOS vibrancy) is always available.</summary>
    public bool IsMicaSupported => true;

    /// <summary>True when the desktop shows through the whole window (Background › Translucent).</summary>
    public bool IsTranslucent { get; private set; }

    /// <summary>Where a copy of your own background picture is kept.</summary>
    public string? BackdropFolder => Backdrop.Folder;

    public Color AccentColor { get; private set; } = ThemePalette.Hex(AccentPalette.Default);

    /// <summary>Creates every brush for the saved appearance. Call once, before any window is created.</summary>
    public void Initialize(AppearanceSettings appearance)
    {
        Instance = this;
        Apply(appearance, animate: false);
        _initialized = true;
    }

    public void Apply(AppearanceSettings appearance) => Apply(appearance, animate: _initialized && !appearance.ReducedMotion);

    private void Apply(AppearanceSettings appearance, bool animate)
    {
        _current = appearance;
        _isDark = ResolvesToDark(appearance.Theme);
        if (Application.Current is { } app)
        {
            app.RequestedThemeVariant = _isDark ? ThemeVariant.Dark : ThemeVariant.Light;
        }

        var palette = _isDark ? ThemePalette.Dark : ThemePalette.Light;
        foreach (var (key, color) in palette)
        {
            string resourceKey = key.StartsWith("Series.", StringComparison.Ordinal) ? key + ".Brush" : "Brush." + key;
            SetColor(resourceKey, color, animate);
        }

        AccentColor = ThemePalette.Hex(AccentPalette.IsValidHex(appearance.Accent) ? appearance.Accent : AccentPalette.Default);
        SetColor("Brush.Accent", AccentColor, animate);
        SetColor("Series.Accent.Brush", AccentColor, animate);
        SetColor("Brush.AccentSoft", ThemePalette.WithAlpha(AccentColor, (byte)(_isDark ? 0x2E : 0x24)), animate);
        SetColor("Brush.AccentHover", Blend(AccentColor, _isDark ? Colors.White : Colors.Black, 0.12), animate);
        SetColor("Brush.OnAccent", ThemePalette.ContrastingText(AccentColor), animate);
        SetColor("Brush.OnAccentSoft", ThemePalette.WithAlpha(ThemePalette.ContrastingText(AccentColor), 0x3A), animate); // a count on the accent pill (1.9.0)
        SetColor("Brush.AccentTrack", ThemePalette.WithAlpha(AccentColor, (byte)(_isDark ? 0x33 : 0x29)), animate);
        PublishAccentGradients(palette);
        byte soft = (byte)(_isDark ? 0x2C : 0x1E); // tinted backgrounds for notices, results and status pills
        SetColor("Brush.SuccessSoft", ThemePalette.WithAlpha(palette["Success"], soft), animate);
        SetColor("Brush.InfoSoft", ThemePalette.WithAlpha(palette["Info"], soft), animate);
        SetColor("Brush.ElevatedSoft", ThemePalette.WithAlpha(palette["Elevated"], soft), animate);
        SetColor("Brush.CriticalSoft", ThemePalette.WithAlpha(palette["Critical"], soft), animate);

        var backdrop = appearance.Backdrop ?? new BackdropSettings();
        IsTranslucent = backdrop.Kind == BackdropKind.SeeThrough;
        var background = palette["Background"];
        // One background spans the window (Settings › Appearance › Background, drawn by Backdrop); until it's drawn, and
        // see-through, the theme's color shows (mostly clear over the desktop's vibrancy when see-through).
        var tint = IsTranslucent ? ThemePalette.WithAlpha(background, (byte)(_isDark ? 0x80 : 0x5C)) : background;
        SetColor("Brush.WindowBackground", IsTranslucent ? Colors.Transparent : background, animate: false);
        SetColor("Brush.Sidebar", tint, animate);
        SetColor("Brush.ContentLayer", tint, animate);
        SetColor("Brush.TitleBar", Colors.Transparent, animate: false);

        // The Fluent theme's own controls (text boxes, menus, scroll bars) take the accent and the text colors too.
        _resources["SystemAccentColor"] = AccentColor;
        _resources["SystemAccentColorLight1"] = Blend(AccentColor, Colors.White, 0.12);
        _resources["SystemAccentColorDark1"] = Blend(AccentColor, Colors.Black, 0.12);
        PublishGlass();
        var sidebar = appearance.Sidebar ?? new SidebarSettings();
        PublishSidebar(sidebar, palette);
        // The grain over the gradients (Settings › Appearance › Gradient noise, since 1.9.3): 4.5 % over the banners'
        // light at the standard strength, 3 % over the whole window on a Mac; none at 0, twice at 100.
        _resources["Grain.Opacity"] = 0.045 * appearance.NoiseFactor;
        _resources["Grain.WindowOpacity"] = 0.03 * appearance.NoiseFactor;
        PublishAppIcon();
        Backdrop.Apply(backdrop, sidebar, _isDark, background, AccentColor);

        AnimatedText.IsGloballyEnabled = appearance.AnimateValues && !appearance.ReducedMotion;
        Motion.ReducedMotion = appearance.ReducedMotion;
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The accent's cooler, lighter neighbour (sky for a blue) and its warmer one (violet for a blue).</summary>
    public static (Color Cool, Color Warm) AccentNeighbours(Color accent) =>
        (ThemePalette.Shift(accent, -24, 0.06), ThemePalette.Shift(accent, 42, 0));

    /// <summary>Gradient brushes made from the accent (the Smart Optimize banner: a wash, a bloom, a lit edge, a button).</summary>
    private void PublishAccentGradients(IReadOnlyDictionary<string, Color> palette)
    {
        var accent = AccentColor;
        var (cool, warm) = AccentNeighbours(accent);
        Color A(Color c, int dark, int light) => ThemePalette.WithAlpha(c, (byte)(_isDark ? dark : light));

        _resources["Brush.AccentWash"] = Linear(new RelativePoint(0, 0, RelativeUnit.Relative), new RelativePoint(1, 1, RelativeUnit.Relative),
            (A(accent, 0x4C, 0x30), 0), (A(warm, 0x24, 0x16), 0.5), (A(warm, 0, 0), 0.95));
        _resources["Brush.AccentBloom"] = Radial((A(accent, 0x52, 0x34), 0), (A(accent, 0x2A, 0x1C), 0.4), (A(warm, 0x10, 0x0A), 0.72), (A(warm, 0, 0), 1));
        _resources["Brush.AccentEdge"] = Linear(new RelativePoint(0, 0, RelativeUnit.Relative), new RelativePoint(1, 0.7, RelativeUnit.Relative),
            (A(cool, 0xA0, 0x90), 0), (A(accent, 0x50, 0x40), 0.3), (palette["CardBorder"], 0.7));
        // Since 1.8.3 one hue, a lighter shade to a deeper one (no longer sky to violet, which white text got lost in).
        var deep = ThemePalette.Shift(accent, 0, -0.10);
        var lighter = Blend(accent, Colors.White, 0.32);
        _resources["Brush.AccentGradient"] = Linear(new RelativePoint(0, 0, RelativeUnit.Relative), new RelativePoint(1, 0.4, RelativeUnit.Relative),
            (lighter, 0), (accent, 0.55), (deep, 1));
        // The version number in What's new (since 1.9.2): on the dark theme a light, clear blue of the accent (pale to
        // bright) that stands off the dark card instead of sinking into it; on the light theme the accent to its deeper shade.
        var versionStart = new RelativePoint(0, 0, RelativeUnit.Relative);
        var versionEnd = new RelativePoint(1, 0.4, RelativeUnit.Relative);
        _resources["Brush.VersionGradient"] = _isDark
            ? Linear(versionStart, versionEnd, (Blend(accent, Colors.White, 0.66), 0), (Blend(accent, Colors.White, 0.42), 0.55), (Blend(accent, Colors.White, 0.24), 1))
            : Linear(versionStart, versionEnd, (accent, 0), (deep, 1));
        PublishHeroLooks(accent, deep, lighter);
    }

    /// <summary>
    /// The banners' look (since 1.8.3): a light from the top edge in the accent that fades out well before the content,
    /// a bright line where it meets the edge, a dark tile for the dial, and the dark glass buttons with a soft light in
    /// one corner (white text on charcoal, not on the accent).
    /// </summary>
    private void PublishHeroLooks(Color accent, Color deep, Color lighter)
    {
        Color A(Color c, int dark, int light) => ThemePalette.WithAlpha(c, (byte)(_isDark ? dark : light));
        static Color Rgb(byte a, byte r, byte g, byte b) => Color.FromArgb(a, r, g, b);
        var top = new RelativePoint(0.5, 0, RelativeUnit.Relative);

        _resources["Brush.HeroGlow"] = new RadialGradientBrush
        {
            Center = top,
            GradientOrigin = top,
            RadiusX = new RelativeScalar(0.6, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.9, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(A(lighter, 0x80, 0x52), 0),
                new GradientStop(A(accent, 0x46, 0x2C), 0.25),
                new GradientStop(A(deep, 0x18, 0x10), 0.55),
                new GradientStop(A(deep, 0, 0), 1),
            },
        };
        _resources["Brush.HeroLine"] = Linear(new RelativePoint(0, 0, RelativeUnit.Relative), new RelativePoint(1, 0, RelativeUnit.Relative),
            (A(lighter, 0, 0), 0.12), (A(Blend(lighter, Colors.White, 0.5), 0xB0, 0x90), 0.5), (A(lighter, 0, 0), 0.88));
        _resources["Brush.DialTile"] = Linear(new RelativePoint(0, 0, RelativeUnit.Relative), new RelativePoint(0, 1, RelativeUnit.Relative),
            (_isDark ? Rgb(0x30, 0, 0, 0) : Rgb(0x8C, 255, 255, 255), 0), (_isDark ? Rgb(0x58, 0, 0, 0) : Rgb(0xB8, 255, 255, 255), 1));
        _resources["Brush.DialTileRim"] = Linear(new RelativePoint(0, 0, RelativeUnit.Relative), new RelativePoint(0, 1, RelativeUnit.Relative),
            (_isDark ? Rgb(0x2C, 255, 255, 255) : Rgb(0x20, 0, 0, 0), 0), (_isDark ? Rgb(0x0C, 255, 255, 255) : Rgb(0x0C, 0, 0, 0), 0.6), (A(lighter, 0x40, 0x30), 1));

        // The buttons: charcoal glass whichever the theme, a rim lit along the top and toward the light, and the light.
        _resources["Brush.GlowButtonFill"] = Linear(new RelativePoint(0, 0, RelativeUnit.Relative), new RelativePoint(0, 1, RelativeUnit.Relative),
            (Rgb(0xF2, 0x34, 0x36, 0x3E), 0), (Rgb(0xF6, 0x1C, 0x1D, 0x22), 1));
        _resources["Brush.GlowButtonRim"] = Linear(new RelativePoint(0, 0, RelativeUnit.Relative), new RelativePoint(1, 1, RelativeUnit.Relative),
            (Rgb(0x5C, 255, 255, 255), 0), (Rgb(0x16, 255, 255, 255), 0.42), (ThemePalette.WithAlpha(lighter, 0xA8), 1));
        _resources["Brush.GlowButtonLight"] = new RadialGradientBrush
        {
            Center = new RelativePoint(0.92, 1.05, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.92, 1.05, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.75, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(1.5, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(ThemePalette.WithAlpha(lighter, 0xA0), 0),
                new GradientStop(ThemePalette.WithAlpha(accent, 0x38), 0.45),
                new GradientStop(ThemePalette.WithAlpha(accent, 0), 1),
            },
        };
        _resources["Brush.OnGlow"] = new SolidColorBrush(Rgb(0xFF, 0xF5, 0xF7, 0xFB));
    }

    /// <summary>
    /// The glass every card and the sidebar are made of (since 1.8.2): over the frosted background behind it, a faint
    /// fill (a little of the accent where the light enters, top left), a bevelled rim bright along the top-left and
    /// bottom-right edges, and two soft lights for the see-through window.
    /// </summary>
    private void PublishGlass()
    {
        var accent = AccentColor;
        var (_, warm) = AccentNeighbours(accent);
        static Color White(byte alpha) => Color.FromArgb(alpha, 255, 255, 255);
        static Color Black(byte alpha) => Color.FromArgb(alpha, 0, 0, 0);
        var start = new RelativePoint(0, 0, RelativeUnit.Relative);

        static Color Ink(byte alpha) => Color.FromArgb(alpha, 0x0B, 0x10, 0x17);
        if (_isDark)
        {
            // Since 1.9.0 a dark, nearly opaque, neutral fill (it was a faint tint lit by the accent), so what's behind
            // shows only as soft colour, and a hairline rim a shade lighter than the fill.
            _resources["Brush.GlassFill"] = Linear(start, new RelativePoint(0.6, 1, RelativeUnit.Relative),
                (Ink(0xD2), 0), (Ink(0xD8), 0.5), (Ink(0xDC), 1));
            _resources["Brush.GlassRim"] = Linear(start, new RelativePoint(0.3, 1, RelativeUnit.Relative),
                (White(0x22), 0), (White(0x14), 0.4), (White(0x10), 1));
        }
        else
        {
            _resources["Brush.GlassFill"] = Linear(start, new RelativePoint(0.8, 1, RelativeUnit.Relative),
                (White(0xD4), 0), (White(0xC2), 0.5), (White(0xB8), 1));
            _resources["Brush.GlassRim"] = Linear(start, new RelativePoint(1, 1, RelativeUnit.Relative),
                (White(0xFF), 0), (White(0x90), 0.35), (Black(0x10), 0.7), (Black(0x18), 1));
        }

        _resources["Brush.GlassLightA"] = Glow(accent, _isDark ? (byte)0x16 : (byte)0x1E);
        _resources["Brush.GlassLightB"] = Glow(warm, _isDark ? (byte)0x10 : (byte)0x16);

        static RadialGradientBrush Glow(Color color, byte alpha) =>
            Radial((ThemePalette.WithAlpha(color, alpha), 0), (ThemePalette.WithAlpha(color, (byte)(alpha * 0.4)), 0.5), (ThemePalette.WithAlpha(color, 0), 1));
    }

    /// <summary>
    /// The sidebar's surface (Settings › Appearance › Sidebar, since 1.9.2) as "Brush.SidebarFill", "Brush.SidebarRim",
    /// "Brush.SidebarHighlight" and the numbers the sidebar's shape takes ("Sidebar.Sheen", "Sidebar.Shadow",
    /// "Sidebar.Frosted"). Frosted glass is the card glass (the sidebar before 1.9.2); liquid glass a thin tint over a
    /// clearer frost with a bright rim catching the light at two corners and a highlight down from the top; translucent a
    /// see-through tint with nothing blurred; solid the cards' own colour.
    /// </summary>
    private void PublishSidebar(SidebarSettings sidebar, IReadOnlyDictionary<string, Color> palette)
    {
        static Color White(byte alpha) => Color.FromArgb(alpha, 255, 255, 255);
        static Color Black(byte alpha) => Color.FromArgb(alpha, 0, 0, 0);
        static Color Ink(byte alpha) => Color.FromArgb(alpha, 0x0B, 0x10, 0x17);
        static RelativePoint At(double x, double y) => new(x, y, RelativeUnit.Relative);
        double sheen = 1;
        IBrush highlight = Brushes.Transparent;
        switch (sidebar.Surface)
        {
            case SidebarSurface.Solid:
                _resources["Brush.SidebarFill"] = new SolidColorBrush(palette["Card"]);
                _resources["Brush.SidebarRim"] = new SolidColorBrush(palette["CardBorder"]);
                sheen = 0.4;
                break;

            case SidebarSurface.LiquidGlass:
                _resources["Brush.SidebarFill"] = _isDark
                    ? Linear(At(0, 0), At(0.6, 1), (Ink(0x30), 0), (Ink(0x44), 0.55), (Ink(0x58), 1))
                    : Linear(At(0, 0), At(0.8, 1), (White(0x7A), 0), (White(0x62), 0.55), (White(0x56), 1));
                _resources["Brush.SidebarRim"] = _isDark
                    ? Linear(At(0, 0), At(1, 1), (White(0x66), 0), (White(0x1C), 0.3), (White(0x10), 0.65), (White(0x3C), 1))
                    : Linear(At(0, 0), At(1, 1), (White(0xFF), 0), (White(0x9A), 0.3), (Black(0x10), 0.65), (White(0xC8), 1));
                highlight = Linear(At(0.5, 0), At(0.5, 0.42), (White((byte)(_isDark ? 0x24 : 0x48)), 0), (White(0), 1));
                sheen = 2.2;
                break;

            case SidebarSurface.Translucent:
                _resources["Brush.SidebarFill"] = new SolidColorBrush(_isDark ? Ink(0x5A) : White(0x80));
                _resources["Brush.SidebarRim"] = _isDark
                    ? Linear(At(0, 0), At(0.3, 1), (White(0x24), 0), (White(0x12), 0.4), (White(0x0E), 1))
                    : Linear(At(0, 0), At(1, 1), (White(0xE0), 0), (Black(0x10), 0.7), (Black(0x16), 1));
                sheen = 0.7;
                break;

            default:
                _resources["Brush.SidebarFill"] = _resources["Brush.GlassFill"];
                _resources["Brush.SidebarRim"] = _resources["Brush.GlassRim"];
                break;
        }

        _resources["Brush.SidebarHighlight"] = highlight;
        _resources["Sidebar.Sheen"] = sheen;
        _resources["Sidebar.Shadow"] = sidebar.ShadowDepth;
        _resources["Sidebar.Frosted"] = sidebar.Blurs;
    }

    /// <summary>The app icon for the theme as "Image.AppIcon" (the dark glass tile in the dark theme, the white tile otherwise).</summary>
    private void PublishAppIcon()
    {
        if (_iconIsDark == _isDark)
        {
            return;
        }

        _iconIsDark = _isDark;
        try
        {
            _resources["Image.AppIcon"] = AppIcons.Load(_isDark, 64);
            _resources["Image.AppIconLarge"] = AppIcons.Load(_isDark, 128);
        }
        catch (Exception)
        {
            // A missing resource must never break a theme change; the pictures simply stay as they were.
        }

        AppIconChanged?.Invoke(this, EventArgs.Empty);
    }

    private static LinearGradientBrush Linear(RelativePoint start, RelativePoint end, params (Color Color, double Offset)[] stops)
    {
        var brush = new LinearGradientBrush { StartPoint = start, EndPoint = end };
        foreach (var (color, offset) in stops)
        {
            brush.GradientStops.Add(new GradientStop(color, offset));
        }

        return brush;
    }

    private static RadialGradientBrush Radial(params (Color Color, double Offset)[] stops)
    {
        var brush = new RadialGradientBrush();
        foreach (var (color, offset) in stops)
        {
            brush.GradientStops.Add(new GradientStop(color, offset));
        }

        return brush;
    }

    /// <summary>The brush for a resource key such as "Brush.Text" (the same instance the resources hold, recolored in place).</summary>
    public IBrush GetBrush(string resourceKey) =>
        _brushes.TryGetValue(resourceKey, out var brush) ? brush : Brushes.Gray;

    /// <summary>The brush for a semantic series color key such as "Cpu" or "Gpu".</summary>
    public IBrush GetSeriesBrush(string? colorKey) =>
        ToneBrushKey(colorKey) is { } tone && _brushes.TryGetValue(tone, out var toneBrush) ? toneBrush
        : _brushes.TryGetValue($"Series.{colorKey ?? "Accent"}.Brush", out var brush) ? brush
        : _brushes.TryGetValue("Series.Accent.Brush", out var accent) ? accent : Brushes.SteelBlue;

    /// <summary>The gauge scale's colors are the status brushes; a temperature's are its own.</summary>
    private static string? ToneBrushKey(string? colorKey) => colorKey switch
    {
        "Tone.Cool" => "Brush.Success",
        "Tone.Normal" => "Brush.Info",
        "Tone.Warm" => "Brush.Elevated",
        "Tone.Hot" => "Brush.Critical",
        "Temp.Cool" or "Temp.Normal" or "Temp.Warm" or "Temp.Hot" => "Brush." + colorKey,
        _ => null,
    };

    private void SetColor(string key, Color color, bool animate)
    {
        if (!_brushes.TryGetValue(key, out var brush))
        {
            brush = new SolidColorBrush(color);
            _brushes[key] = brush;
            _resources[key] = brush;
            return;
        }

        if (!animate || brush.Color == color)
        {
            _fading.Remove(key);
            brush.Color = color;
            return;
        }

        _fading[key] = (brush.Color, color);
        _fadeStart = DateTime.UtcNow;
        _fadeTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => StepFade());
        _fadeTimer.Start();
    }

    private void StepFade()
    {
        double t = Math.Clamp((DateTime.UtcNow - _fadeStart).TotalMilliseconds / FadeDuration.TotalMilliseconds, 0, 1);
        double eased = t < 0.5 ? 2 * t * t : 1 - (Math.Pow((-2 * t) + 2, 2) / 2); // quadratic ease in-out
        foreach (var (key, (from, to)) in _fading)
        {
            _brushes[key].Color = Lerp(from, to, eased);
        }

        if (t >= 1)
        {
            _fading.Clear();
            _fadeTimer?.Stop();
        }

        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    private static Color Lerp(Color a, Color b, double t) => Color.FromArgb(
        (byte)Math.Round(a.A + ((b.A - a.A) * t)),
        (byte)Math.Round(a.R + ((b.R - a.R) * t)),
        (byte)Math.Round(a.G + ((b.G - a.G) * t)),
        (byte)Math.Round(a.B + ((b.B - a.B) * t)));

    private static Color Blend(Color a, Color b, double t) => Color.FromArgb(
        a.A,
        (byte)(a.R + ((b.R - a.R) * t)),
        (byte)(a.G + ((b.G - a.G) * t)),
        (byte)(a.B + ((b.B - a.B) * t)));

    /// <summary>Whether a theme preference shows the dark theme (System follows macOS's appearance).</summary>
    public static bool ResolvesToDark(ThemePreference theme) => theme switch
    {
        ThemePreference.Light => false,
        ThemePreference.System => SystemIsDark(),
        _ => true,
    };

    private static bool SystemIsDark()
    {
        try
        {
            return Application.Current?.PlatformSettings?.GetColorValues().ThemeVariant != PlatformThemeVariant.Light;
        }
        catch
        {
            return true;
        }
    }
}

/// <summary>The app icon's pictures: the blue mark on a white tile, or on a dark glass tile while the dark theme is on.</summary>
internal static class AppIcons
{
    public static Uri Uri(bool dark, int size) => new($"avares://Coreloom/Assets/coreloom{(dark ? "-dark" : string.Empty)}-{size}.png");

    public static Bitmap Load(bool dark, int size)
    {
        using var stream = AssetLoader.Open(Uri(dark, size));
        return new Bitmap(stream);
    }
}
