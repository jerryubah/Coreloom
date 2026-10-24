using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Coreloom.Core.Settings;
using Coreloom.Mac.Behaviors;
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

    /// <summary>True when the sidebar lets the desktop show through (the "translucent sidebar" setting).</summary>
    public bool IsTranslucent { get; private set; }

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
        SetColor("Brush.AccentTrack", ThemePalette.WithAlpha(AccentColor, (byte)(_isDark ? 0x33 : 0x29)), animate);
        PublishAccentGradients(palette);
        byte soft = (byte)(_isDark ? 0x26 : 0x1C); // tinted backgrounds for notices and results
        SetColor("Brush.SuccessSoft", ThemePalette.WithAlpha(palette["Success"], soft), animate);
        SetColor("Brush.InfoSoft", ThemePalette.WithAlpha(palette["Info"], soft), animate);
        SetColor("Brush.ElevatedSoft", ThemePalette.WithAlpha(palette["Elevated"], soft), animate);
        SetColor("Brush.CriticalSoft", ThemePalette.WithAlpha(palette["Critical"], soft), animate);

        IsTranslucent = appearance.UseMica;
        var background = palette["Background"];
        // The pages sit on an opaque layer; the sidebar column is the backdrop the glass sidebar floats over. When it's
        // translucent, macOS's vibrancy (behind the window) shows through it, darkened to the theme.
        SetColor("Brush.WindowBackground", IsTranslucent ? Colors.Transparent : background, animate: false);
        SetColor("Brush.Sidebar", IsTranslucent ? ThemePalette.WithAlpha(background, (byte)(_isDark ? 0x8C : 0x66)) : background, animate);
        SetColor("Brush.ContentLayer", background, animate);
        SetColor("Brush.TitleBar", background, animate);

        // The Fluent theme's own controls (text boxes, menus, scroll bars) take the accent and the text colors too.
        _resources["SystemAccentColor"] = AccentColor;
        _resources["SystemAccentColorLight1"] = Blend(AccentColor, Colors.White, 0.12);
        _resources["SystemAccentColorDark1"] = Blend(AccentColor, Colors.Black, 0.12);
        PublishGlass();
        PublishAppIcon();

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
        _resources["Brush.AccentGradient"] = Linear(new RelativePoint(0, 0, RelativeUnit.Relative), new RelativePoint(1, 0.4, RelativeUnit.Relative),
            (cool, 0), (accent, 0.5), (warm, 1));
    }

    /// <summary>The floating sidebar's liquid glass: a faint fill, a bevelled rim and two soft lights behind it.</summary>
    private void PublishGlass()
    {
        var accent = AccentColor;
        var (_, warm) = AccentNeighbours(accent);
        static Color White(byte alpha) => Color.FromArgb(alpha, 255, 255, 255);
        static Color Black(byte alpha) => Color.FromArgb(alpha, 0, 0, 0);
        var start = new RelativePoint(0, 0, RelativeUnit.Relative);

        if (_isDark)
        {
            _resources["Brush.GlassFill"] = Linear(start, new RelativePoint(0.7, 1, RelativeUnit.Relative),
                (ThemePalette.WithAlpha(accent, 0x14), 0), (White(0x0B), 0.35), (White(0x07), 0.75), (White(0x0A), 1));
            _resources["Brush.GlassRim"] = Linear(start, new RelativePoint(1, 1, RelativeUnit.Relative),
                (White(0x5C), 0), (White(0x16), 0.28), (White(0x08), 0.62), (White(0x30), 1));
        }
        else
        {
            _resources["Brush.GlassFill"] = Linear(start, new RelativePoint(0.7, 1, RelativeUnit.Relative),
                (White(0xD8), 0), (White(0xB4), 0.5), (White(0xA4), 1));
            _resources["Brush.GlassRim"] = Linear(start, new RelativePoint(1, 1, RelativeUnit.Relative),
                (White(0xFF), 0), (White(0x80), 0.35), (Black(0x0C), 0.7), (Black(0x16), 1));
        }

        _resources["Brush.GlassLightA"] = Glow(accent, _isDark ? (byte)0x46 : (byte)0x30);
        _resources["Brush.GlassLightB"] = Glow(warm, _isDark ? (byte)0x30 : (byte)0x22);

        static RadialGradientBrush Glow(Color color, byte alpha) =>
            Radial((ThemePalette.WithAlpha(color, alpha), 0), (ThemePalette.WithAlpha(color, (byte)(alpha * 0.4)), 0.5), (ThemePalette.WithAlpha(color, 0), 1));
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
