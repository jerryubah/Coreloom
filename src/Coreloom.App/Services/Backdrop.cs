using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Coreloom.Core.Appearance;
using Coreloom.Core.Diagnostics;
using Coreloom.Core.Settings;

namespace Coreloom.App.Services;

/// <summary>
/// The window's background (Settings › Appearance › Background) and the frosted copy of it the glass sidebar and cards
/// show. Settings, the theme and the window's size go in; <see cref="BackdropRenderer"/> draws off the UI thread; two
/// frozen bitmaps come out (<see cref="Background"/> for the window, <see cref="Frost"/> for the glass) and
/// <see cref="Changed"/> tells everything showing them. Pictures are decoded once and kept while chosen.
/// </summary>
public static class Backdrop
{
    private const int MaxPictureWidth = 3200;
    private static readonly TimeSpan SettingsDelay = TimeSpan.FromMilliseconds(30);
    private static readonly TimeSpan ResizeDelay = TimeSpan.FromMilliseconds(140);

    private static DispatcherTimer? _timer;
    private static BackdropSettings _settings = new();
    private static bool _dark = true;
    private static Rgb _themeBase = Rgb.Parse("#0A0B0E");
    private static Rgb _accent = Rgb.Parse(AccentPalette.Default);
    private static int _generation;
    private static bool _rendering;
    private static bool _pending;
    private static (string Key, PixelImage Image)? _picture;
    private static string? _lastKey;

    /// <summary>The window's background, stretched to the window (null until drawn, or when see-through).</summary>
    public static BitmapSource? Background { get; private set; }

    /// <summary>The frosted copy: stretched to the window too, each glass panel draws the part behind it.</summary>
    public static BitmapSource? Frost { get; private set; }

    /// <summary>The desktop shows through the window (Mica); the glass only tints.</summary>
    public static bool IsSeeThrough { get; private set; }

    /// <summary>The element filling the window behind everything; the glass measures its place against it.</summary>
    public static FrameworkElement? Host { get; private set; }

    /// <summary>Where a copy of your own picture is kept.</summary>
    public static string? Folder { get; private set; }

    /// <summary>Drawing is scheduled or under way (the page-shots tool waits for it).</summary>
    public static bool IsBusy => _rendering || _pending || (_timer?.IsEnabled ?? false);

    /// <summary>Draws at this many device pixels per DIP whatever the screen (the page-shots tool's 2× pictures).</summary>
    public static double? ScaleOverride { get; set; }

    /// <summary>Raised on the UI thread when the background or the frost changed.</summary>
    public static event EventHandler? Changed;

    public static void Initialize(string folder) => Folder = folder;

    /// <summary>The settings or theme changed (from ThemeService.Apply). See-through only where Mica is available.</summary>
    public static void Apply(BackdropSettings settings, bool dark, Color themeBase, Color accent, bool seeThrough)
    {
        _settings = new BackdropSettings
        {
            Kind = settings.Kind,
            Color = settings.Color,
            Gradient = settings.Gradient,
            Image = settings.Image,
            CustomImage = settings.CustomImage,
            ImageOpacity = settings.ImageOpacity,
            Blur = settings.Blur,
        };
        if (_settings.Kind == BackdropKind.SeeThrough && !seeThrough)
        {
            _settings.Kind = BackdropKind.Gradient; // Mica isn't available here: Coreloom's own gradient instead
        }

        _dark = dark;
        _themeBase = new Rgb(themeBase.R / 255f, themeBase.G / 255f, themeBase.B / 255f);
        _accent = new Rgb(accent.R / 255f, accent.G / 255f, accent.B / 255f);
        if (seeThrough != IsSeeThrough)
        {
            IsSeeThrough = seeThrough;
            Changed?.Invoke(null, EventArgs.Empty);
        }

        Schedule(SettingsDelay);
    }

    /// <summary>The window's backdrop element (the main window's).</summary>
    public static void Attach(FrameworkElement host)
    {
        Host = host;
        Schedule(TimeSpan.Zero);
    }

    public static void Detach(FrameworkElement host)
    {
        if (Host == host)
        {
            Host = null;
        }
    }

    /// <summary>The window changed size: draw again for the new size once it settles.</summary>
    public static void HostResized() => Schedule(ResizeDelay);

    private static void Schedule(TimeSpan delay)
    {
        if (_timer is null)
        {
            _timer = new DispatcherTimer(DispatcherPriority.Background);
            _timer.Tick += (_, _) =>
            {
                _timer.Stop();
                _ = RenderAsync();
            };
        }

        _timer.Stop();
        _timer.Interval = delay <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : delay;
        _timer.Start();
    }

    /// <summary>Device pixels per DIP where the backdrop is drawn (the monitor's scale, times any zoom such as page shots' 2×).</summary>
    private static double ScaleOf(FrameworkElement host)
    {
        if (ScaleOverride is { } fixedScale)
        {
            return fixedScale;
        }

        double scale = 1;
        try
        {
            scale = VisualTreeHelper.GetDpi(host).DpiScaleX;
            if (PresentationSource.FromVisual(host)?.RootVisual is Visual root && host.TransformToAncestor(root) is Transform transform)
            {
                scale *= Math.Max(0.25, Math.Abs(transform.Value.M11));
            }
        }
        catch (InvalidOperationException)
        {
            // Not in a window yet.
        }

        return Math.Clamp(scale, 0.5, 4);
    }

    private static async Task RenderAsync()
    {
        if (Host is not { } host || host.ActualWidth < 2 || host.ActualHeight < 2)
        {
            return;
        }

        if (_rendering)
        {
            _pending = true;
            return;
        }

        _rendering = true;
        int generation = ++_generation;
        try
        {
            double scale = ScaleOf(host);
            var settings = _settings;
            string? picture = settings.Kind == BackdropKind.Image ? PicturePath(settings) : null;
            var request = new BackdropRequest
            {
                Kind = IsSeeThrough ? BackdropKind.SeeThrough : settings.Kind,
                Width = (int)Math.Ceiling(host.ActualWidth * scale),
                Height = (int)Math.Ceiling(host.ActualHeight * scale),
                Scale = scale,
                Dark = _dark,
                ThemeBase = _themeBase,
                Accent = _accent,
                Color = Rgb.Parse(settings.Color),
                Gradient = BackdropPresets.FindGradient(settings.Gradient),
                ImageOpacity = settings.ImageOpacity / 100.0,
                BlurPoints = settings.BlurPoints,
                FrostPoints = settings.FrostPoints,
            };

            string key = $"{request.Kind}|{request.Width}x{request.Height}|{scale:0.##}|{_dark}|{_themeBase}|{_accent}|{settings.Color}|{settings.Gradient}|{picture}|{settings.ImageOpacity}|{settings.Blur}";
            if (key == _lastKey && (Background is not null || request.Kind == BackdropKind.SeeThrough))
            {
                return;
            }

            var layers = await Task.Run(() =>
            {
                if (picture is not null)
                {
                    request = request with { Image = LoadPicture(picture) };
                }

                return BackdropRenderer.Render(request);
            }).ConfigureAwait(true);

            if (generation != _generation)
            {
                return; // something newer is on its way
            }

            Background = layers is null ? null : ToBitmap(layers.Background);
            Frost = layers is null ? null : ToBitmap(layers.Frost);
            _lastKey = key;
            Changed?.Invoke(null, EventArgs.Empty);
        }
        catch (Exception e)
        {
            Log.Warn("Appearance", "Drawing the background failed; the theme's own color shows instead.", e);
        }
        finally
        {
            _rendering = false;
            if (_pending)
            {
                _pending = false;
                Schedule(SettingsDelay);
            }
        }
    }

    /// <summary>The chosen picture: Coreloom's own (inside the app), or your copy in <see cref="Folder"/>.</summary>
    private static string PicturePath(BackdropSettings settings) =>
        settings.Image == BackdropSettings.CustomImageId && settings.CustomImage is { } name && Folder is { } folder
            ? Path.Combine(folder, name)
            : $"pack://application:,,,/Assets/Backdrops/{settings.Image}.jpg";

    /// <summary>Decodes the picture (at most <see cref="MaxPictureWidth"/> wide) into BGRA pixels; kept while it stays chosen.</summary>
    private static PixelImage? LoadPicture(string path)
    {
        if (_picture is { } cached && cached.Key == path)
        {
            return cached.Image;
        }

        try
        {
            var decoder = path.StartsWith("pack:", StringComparison.Ordinal)
                ? Decode(Application.GetResourceStream(new Uri(path, UriKind.Absolute))?.Stream)
                : File.Exists(path) ? Decode(File.OpenRead(path)) : null;
            if (decoder is null)
            {
                Log.Warn("Appearance", "The background picture couldn't be read; the theme's own color shows instead.");
                return null;
            }

            var image = Pixels(decoder);
            _picture = (path, image);
            return image;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or InvalidOperationException or FileFormatException)
        {
            Log.Warn("Appearance", "The background picture couldn't be read.", e);
            return null;
        }
    }

    private static BitmapSource? Decode(Stream? stream)
    {
        if (stream is null)
        {
            return null;
        }

        using (stream)
        {
            var frame = BitmapFrame.Create(stream, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
            BitmapSource source = frame;
            if (frame.PixelWidth > MaxPictureWidth)
            {
                double k = (double)MaxPictureWidth / frame.PixelWidth;
                source = new TransformedBitmap(frame, new ScaleTransform(k, k));
            }

            var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            converted.Freeze();
            return converted;
        }
    }

    private static PixelImage Pixels(BitmapSource source)
    {
        int w = source.PixelWidth, h = source.PixelHeight;
        var bytes = new byte[w * h * 4];
        source.CopyPixels(bytes, w * 4, 0);
        for (int i = 3; i < bytes.Length; i += 4)
        {
            bytes[i] = 255;
        }

        return new PixelImage(w, h, bytes);
    }

    /// <summary>Pixels into a frozen bitmap WPF can draw (from any thread).</summary>
    public static BitmapSource ToBitmap(PixelImage image)
    {
        var bitmap = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Bgr32, null, image.Bgra, image.Stride);
        bitmap.Freeze();
        return bitmap;
    }
}
