using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using Coreloom.App.Behaviors;
using Coreloom.App.Interop;
using Coreloom.Core.Settings;
using Coreloom.ViewModels.Services;

namespace Coreloom.App.Themes;

/// <summary>
/// Owns all theme brushes ("Brush.*" and "Series.*.Brush" resources) and applies reduced motion, the Mica
/// backdrop and the immersive dark title bar.
/// <para>
/// Each color exists as two brushes:
/// <list type="bullet">
/// <item>a <b>resource brush</b> in Application.Resources for XAML <c>DynamicResource</c> references. WPF
/// freezes any Freezable added to an application-level ResourceDictionary, so resource brushes are never
/// modified: a theme or accent change publishes a new brush. To cross-fade, the new brush starts at the old
/// color and is already animating when it is inserted (an animating Freezable cannot be frozen).</item>
/// <item>a <b>live brush</b>, never placed in a ResourceDictionary, returned by <see cref="GetBrush"/> and
/// <see cref="GetSeriesBrush"/> for converters and custom-drawn controls. It is updated in place, so
/// values produced by converters follow theme changes without re-binding.</item>
/// </list>
/// </para>
/// </summary>
public sealed class ThemeService : IThemeService
{
    private static readonly string[] SurfaceKeys = ["WindowBackground", "Sidebar", "ContentLayer", "TitleBar"];
    private readonly ResourceDictionary _resources;
    private readonly Dictionary<string, SolidColorBrush> _live = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Color> _published = new(StringComparer.Ordinal);
    private AppearanceSettings? _current;
    private bool _isDark = true;
    private bool? _iconIsDark;
    private bool _initialized;

    public ThemeService(ResourceDictionary resources)
    {
        _resources = resources;
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.General && _current?.Theme == ThemePreference.System)
            {
                var current = _current;
                Application.Current?.Dispatcher.InvokeAsync(() => Apply(current));
            }
        };
    }

    public static ThemeService? Instance { get; private set; }

    public event EventHandler? ThemeChanged;

    /// <summary>Raised when the app icon changes between the light and the dark-mode icon.</summary>
    public event EventHandler? AppIconChanged;

    public bool IsDark => _isDark;

    public bool IsMicaSupported => NativeWindow.IsMicaSupported;

    /// <summary>True when Mica is active for the main window (transparent surfaces).</summary>
    public bool IsMicaActive { get; private set; }

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

        var palette = _isDark ? ThemePalette.Dark : ThemePalette.Light;
        var duration = TimeSpan.FromMilliseconds(animate ? 280 : 0);
        foreach (var (key, color) in palette)
        {
            string resourceKey = key.StartsWith("Series.", StringComparison.Ordinal) ? key + ".Brush" : "Brush." + key;
            SetColor(resourceKey, color, duration);
        }

        AccentColor = ThemePalette.Hex(AccentPalette.IsValidHex(appearance.Accent) ? appearance.Accent : AccentPalette.Default);
        SetColor("Brush.Accent", AccentColor, duration);
        SetColor("Series.Accent.Brush", AccentColor, duration);
        SetColor("Brush.AccentSoft", ThemePalette.WithAlpha(AccentColor, (byte)(_isDark ? 0x2E : 0x24)), duration);
        SetColor("Brush.AccentHover", Blend(AccentColor, _isDark ? Colors.White : Colors.Black, 0.12), duration);
        SetColor("Brush.OnAccent", ThemePalette.ContrastingText(AccentColor), duration);
        SetColor("Brush.AccentTrack", ThemePalette.WithAlpha(AccentColor, (byte)(_isDark ? 0x33 : 0x29)), duration);
        PublishAccentGradients(palette);
        byte soft = (byte)(_isDark ? 0x26 : 0x1C); // tinted backgrounds for notices and results
        SetColor("Brush.SuccessSoft", ThemePalette.WithAlpha(palette["Success"], soft), duration);
        SetColor("Brush.InfoSoft", ThemePalette.WithAlpha(palette["Info"], soft), duration);
        SetColor("Brush.ElevatedSoft", ThemePalette.WithAlpha(palette["Elevated"], soft), duration);
        SetColor("Brush.CriticalSoft", ThemePalette.WithAlpha(palette["Critical"], soft), duration);

        IsMicaActive = appearance.UseMica && IsMicaSupported;
        var background = palette["Background"];
        // The pages sit on an opaque layer (crisp ClearType text, and the darkest surface stays dark). The sidebar column is
        // the backdrop the glass sidebar floats over: with Mica it lets the desktop's tint through, darkened to the theme.
        SetColor("Brush.WindowBackground", IsMicaActive ? Colors.Transparent : background, TimeSpan.Zero);
        SetColor("Brush.Sidebar", IsMicaActive ? ThemePalette.WithAlpha(background, (byte)(_isDark ? 0x8C : 0x66)) : background, duration);
        SetColor("Brush.ContentLayer", background, duration);
        SetColor("Brush.TitleBar", background, duration);
        PublishGlass();
        PublishAppIcon();

        AnimatedText.IsGloballyEnabled = appearance.AnimateValues && !appearance.ReducedMotion;
        Motion.ReducedMotion = appearance.ReducedMotion;

        if (Application.Current is { } app)
        {
            foreach (Window window in app.Windows)
            {
                NativeWindow.ApplyBackdrop(window, _isDark, IsMicaActive);
            }
        }

        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The accent's cooler, lighter neighbour (sky for a blue) and its warmer one (violet for a blue).</summary>
    public static (Color Cool, Color Warm) AccentNeighbours(Color accent) =>
        (ThemePalette.Shift(accent, -24, 0.06), ThemePalette.Shift(accent, 42, 0));

    /// <summary>
    /// Gradient brushes made from the accent, for highlighted surfaces such as the Smart Optimize banner: a wash for the
    /// background, a bloom behind a ring, a lit edge for the card and a gradient for its main button. They're rebuilt
    /// (not cross-faded) when the theme or accent changes.
    /// </summary>
    private void PublishAccentGradients(IReadOnlyDictionary<string, Color> palette)
    {
        var accent = AccentColor;
        var (cool, warm) = AccentNeighbours(accent);
        Color A(Color c, int dark, int light) => ThemePalette.WithAlpha(c, (byte)(_isDark ? dark : light));

        Publish("Brush.AccentWash", Linear(new Point(0, 0), new Point(1, 1),
            (A(accent, 0x4C, 0x30), 0), (A(warm, 0x24, 0x16), 0.5), (A(warm, 0, 0), 0.95)));
        Publish("Brush.AccentBloom", new RadialGradientBrush(new GradientStopCollection
        {
            new(A(accent, 0x52, 0x34), 0),
            new(A(accent, 0x2A, 0x1C), 0.4),
            new(A(warm, 0x10, 0x0A), 0.72),
            new(A(warm, 0, 0), 1),
        }));
        Publish("Brush.AccentEdge", Linear(new Point(0, 0), new Point(1, 0.7),
            (A(cool, 0xA0, 0x90), 0), (A(accent, 0x50, 0x40), 0.3), (palette["CardBorder"], 0.7)));
        Publish("Brush.AccentGradient", Linear(new Point(0, 0), new Point(1, 0.4),
            (cool, 0), (accent, 0.5), (warm, 1)));

        static LinearGradientBrush Linear(Point start, Point end, params (Color Color, double Offset)[] stops)
        {
            var brush = new LinearGradientBrush { StartPoint = start, EndPoint = end };
            foreach (var (color, offset) in stops)
            {
                brush.GradientStops.Add(new GradientStop(color, offset));
            }

            return brush;
        }
    }

    /// <summary>
    /// The floating sidebar's liquid glass: a faint fill tinted by the accent where light enters (top left), a rim that is
    /// bright along the top-left and bottom-right edges like the bevel of thick glass, and two soft lights behind it.
    /// </summary>
    private void PublishGlass()
    {
        var accent = AccentColor;
        var (_, warm) = AccentNeighbours(accent);
        Color White(byte alpha) => Color.FromArgb(alpha, 255, 255, 255);
        Color Black(byte alpha) => Color.FromArgb(alpha, 0, 0, 0);

        if (_isDark)
        {
            Publish("Brush.GlassFill", Gradient(new Point(0, 0), new Point(0.7, 1),
                (ThemePalette.WithAlpha(accent, 0x14), 0), (White(0x0B), 0.35), (White(0x07), 0.75), (White(0x0A), 1)));
            Publish("Brush.GlassRim", Gradient(new Point(0, 0), new Point(1, 1),
                (White(0x5C), 0), (White(0x16), 0.28), (White(0x08), 0.62), (White(0x30), 1)));
        }
        else
        {
            Publish("Brush.GlassFill", Gradient(new Point(0, 0), new Point(0.7, 1),
                (White(0xD8), 0), (White(0xB4), 0.5), (White(0xA4), 1)));
            Publish("Brush.GlassRim", Gradient(new Point(0, 0), new Point(1, 1),
                (White(0xFF), 0), (White(0x80), 0.35), (Black(0x0C), 0.7), (Black(0x16), 1)));
        }

        Publish("Brush.GlassLightA", Glow(accent, _isDark ? (byte)0x46 : (byte)0x30));
        Publish("Brush.GlassLightB", Glow(warm, _isDark ? (byte)0x30 : (byte)0x22));

        static RadialGradientBrush Glow(Color color, byte alpha) => new(new GradientStopCollection
        {
            new(ThemePalette.WithAlpha(color, alpha), 0),
            new(ThemePalette.WithAlpha(color, (byte)(alpha * 0.4)), 0.5),
            new(ThemePalette.WithAlpha(color, 0), 1),
        });
    }

    /// <summary>
    /// The app icon for the theme as "Image.AppIcon": the dark-mode icon (a dark glass tile) in the dark theme, the white tile
    /// otherwise. The window and tray icons follow through <see cref="AppIconChanged"/>.
    /// </summary>
    private void PublishAppIcon()
    {
        if (_resources.Contains("Image.AppIcon") && _iconIsDark == _isDark)
        {
            return;
        }

        _iconIsDark = _isDark;
        try
        {
            _resources["Image.AppIcon"] = Picture(AppIcons.Png(_isDark, 64));
            _resources["Image.AppIconLarge"] = Picture(AppIcons.Png(_isDark, 128));
        }
        catch (Exception)
        {
            // A missing resource must never break a theme change; the pictures simply stay as they were.
        }

        AppIconChanged?.Invoke(this, EventArgs.Empty);
    }

    private static System.Windows.Media.Imaging.BitmapImage Picture(string uri)
    {
        var image = new System.Windows.Media.Imaging.BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri(uri, UriKind.Absolute);
        image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static LinearGradientBrush Gradient(Point start, Point end, params (Color Color, double Offset)[] stops)
    {
        var brush = new LinearGradientBrush { StartPoint = start, EndPoint = end };
        foreach (var (color, offset) in stops)
        {
            brush.GradientStops.Add(new GradientStop(color, offset));
        }

        return brush;
    }

    private void Publish(string key, Brush brush)
    {
        brush.Freeze();
        _resources[key] = brush;
    }

    /// <summary>Applies DWM attributes to a newly created window.</summary>
    public void ApplyToWindow(Window window) => NativeWindow.ApplyBackdrop(window, _isDark, IsMicaActive);

    /// <summary>Live (in-place updated) brush for a resource key such as "Brush.Text".</summary>
    public Brush GetBrush(string resourceKey) =>
        _live.TryGetValue(resourceKey, out var brush) ? brush : Brushes.Gray;

    /// <summary>Live brush for a semantic series color key such as "Cpu" or "Gpu".</summary>
    public Brush GetSeriesBrush(string? colorKey) =>
        ToneBrushKey(colorKey) is { } tone && _live.TryGetValue(tone, out var toneBrush) ? toneBrush
        : _live.TryGetValue($"Series.{colorKey ?? "Accent"}.Brush", out var brush) ? brush
        : _live.TryGetValue("Series.Accent.Brush", out var accent) ? accent : Brushes.SteelBlue;

    /// <summary>The gauge scale's colors ("Tone.Cool" … "Tone.Hot") are the status brushes.</summary>
    private static string? ToneBrushKey(string? colorKey) => colorKey switch
    {
        "Tone.Cool" => "Brush.Success",
        "Tone.Normal" => "Brush.Info",
        "Tone.Warm" => "Brush.Elevated",
        "Tone.Hot" => "Brush.Critical",
        _ => null,
    };

    private void SetColor(string key, Color color, TimeSpan duration)
    {
        // Live brush: private to this service, so it is never frozen and can be animated in place.
        if (!_live.TryGetValue(key, out var live))
        {
            _live[key] = new SolidColorBrush(color);
        }
        else
        {
            AnimateTo(live, color, duration);
        }

        // Resource brush: publish a new instance (the previous one may be frozen by the ResourceDictionary).
        bool hadPrevious = _published.TryGetValue(key, out var previous);
        if (hadPrevious && previous == color && _resources.Contains(key))
        {
            return;
        }

        var published = new SolidColorBrush(hadPrevious ? previous : color);
        if (hadPrevious && duration > TimeSpan.Zero)
        {
            // Start the animation before inserting: an animating brush can't be frozen by the dictionary.
            AnimateTo(published, color, duration);
        }
        else
        {
            published.Color = color;
        }

        _resources[key] = published;
        _published[key] = color;
    }

    private static void AnimateTo(SolidColorBrush brush, Color color, TimeSpan duration)
    {
        if (brush.IsFrozen)
        {
            return; // defensive: never throw from a theme change
        }

        if (duration <= TimeSpan.Zero || brush.Color == color)
        {
            brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
            brush.Color = color;
            return;
        }

        var animation = new ColorAnimation(color, duration) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }, FillBehavior = FillBehavior.HoldEnd };
        animation.Completed += (_, _) =>
        {
            if (!brush.IsFrozen)
            {
                brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
                brush.Color = color;
            }
        };
        brush.BeginAnimation(SolidColorBrush.ColorProperty, animation);
    }

    private static Color Blend(Color a, Color b, double t) => Color.FromArgb(
        a.A,
        (byte)(a.R + ((b.R - a.R) * t)),
        (byte)(a.G + ((b.G - a.G) * t)),
        (byte)(a.B + ((b.B - a.B) * t)));

    /// <summary>Whether a theme preference shows the dark theme (System follows Windows' app mode).</summary>
    public static bool ResolvesToDark(ThemePreference theme) => theme switch
    {
        ThemePreference.Light => false,
        ThemePreference.System => !SystemUsesLightTheme(),
        _ => true,
    };

    private static bool SystemUsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 1;
        }
        catch
        {
            return false;
        }
    }
}
