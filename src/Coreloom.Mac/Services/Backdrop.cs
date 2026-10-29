using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Coreloom.Core.Appearance;
using Coreloom.Core.Diagnostics;
using Coreloom.Core.Settings;
using Coreloom.Mac.Native;

namespace Coreloom.Mac.Services;

/// <summary>
/// The window's background (Settings › Appearance › Background) and the frosted copy of it the glass sidebar and cards
/// show. Settings, the theme and the window's size go in; <see cref="BackdropRenderer"/> draws off the UI thread; two
/// bitmaps come out (<see cref="Background"/> for the window, <see cref="Frost"/> for the glass) and <see cref="Changed"/>
/// tells everything showing them. Pictures are decoded once (macOS's sips converts HEIC first) and kept while chosen.
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
    private static string? _logged;

    /// <summary>The window's background, stretched to the window (null until drawn, or when see-through).</summary>
    public static Bitmap? Background { get; private set; }

    /// <summary>The frosted copy: stretched to the window too, each glass panel draws the part behind it.</summary>
    public static Bitmap? Frost { get; private set; }

    /// <summary>The desktop shows through the window (macOS vibrancy); the glass only tints.</summary>
    public static bool IsSeeThrough { get; private set; }

    /// <summary>The control filling the window behind everything; the glass measures its place against it.</summary>
    public static Control? Host { get; private set; }

    /// <summary>Where a copy of your own picture is kept.</summary>
    public static string? Folder { get; private set; }

    /// <summary>Drawing is scheduled or under way (MacShots waits for it before taking a picture).</summary>
    public static bool IsBusy => _rendering || _pending || (_timer?.IsEnabled ?? false);

    /// <summary>Raised on the UI thread when the background or the frost changed.</summary>
    public static event EventHandler? Changed;

    public static void Initialize(string folder) => Folder = folder;

    /// <summary>The settings or theme changed (from ThemeService.Apply).</summary>
    public static void Apply(BackdropSettings settings, bool dark, Color themeBase, Color accent)
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
        _dark = dark;
        _themeBase = new Rgb(themeBase.R / 255f, themeBase.G / 255f, themeBase.B / 255f);
        _accent = new Rgb(accent.R / 255f, accent.G / 255f, accent.B / 255f);
        bool seeThrough = settings.Kind == BackdropKind.SeeThrough;
        if (seeThrough != IsSeeThrough)
        {
            IsSeeThrough = seeThrough;
            Changed?.Invoke(null, EventArgs.Empty);
        }

        Schedule(SettingsDelay);
    }

    /// <summary>The window's backdrop control (one per window; the main window's).</summary>
    public static void Attach(Control host)
    {
        Host = host;
        Schedule(TimeSpan.Zero);
    }

    public static void Detach(Control host)
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
        _timer ??= new DispatcherTimer(DispatcherPriority.Background);
        _timer.Stop();
        _timer.Interval = delay <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : delay;
        _timer.Tick -= OnTick;
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private static void OnTick(object? sender, EventArgs e)
    {
        _timer?.Stop();
        _ = RenderAsync();
    }

    /// <summary>Device pixels per point where the backdrop is drawn (the screen's, times any zoom such as MacShots' 2×).</summary>
    private static double ScaleOf(Control host)
    {
        var top = TopLevel.GetTopLevel(host);
        double scale = top?.RenderScaling ?? 1;
        if (top is not null && host.TransformToVisual(top) is { } matrix)
        {
            scale *= Math.Max(0.25, Math.Abs(matrix.M11));
        }

        return Math.Clamp(scale, 0.5, 4);
    }

    private static async Task RenderAsync()
    {
        if (Host is not { } host || host.Bounds.Width < 2 || host.Bounds.Height < 2)
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
            var gradient = BackdropPresets.FindGradient(settings.Gradient);
            string? picture = settings.Kind == BackdropKind.Image ? PicturePath(settings) : null;
            var request = new BackdropRequest
            {
                Kind = settings.Kind,
                Width = (int)Math.Ceiling(host.Bounds.Width * scale),
                Height = (int)Math.Ceiling(host.Bounds.Height * scale),
                Scale = scale,
                Dark = _dark,
                ThemeBase = _themeBase,
                Accent = _accent,
                Color = Rgb.Parse(settings.Color),
                Gradient = gradient,
                ImageOpacity = settings.ImageOpacity / 100.0,
                BlurPoints = settings.BlurPoints,
                FrostPoints = settings.FrostPoints,
            };

            string key = $"{request.Kind}|{request.Width}x{request.Height}|{scale:0.##}|{_dark}|{_themeBase}|{_accent}|{settings.Color}|{settings.Gradient}|{picture}|{settings.ImageOpacity}|{settings.Blur}";
            if (key == _lastKey && (Background is not null || request.Kind == BackdropKind.SeeThrough))
            {
                return;
            }

            var timer = System.Diagnostics.Stopwatch.StartNew();
            var layers = await Task.Run(async () =>
            {
                if (picture is not null)
                {
                    request = request with { Image = await LoadPictureAsync(picture).ConfigureAwait(false) };
                }

                return BackdropRenderer.Render(request);
            }).ConfigureAwait(true);

            if (generation != _generation)
            {
                return; // something newer is on its way
            }

            var oldBackground = Background;
            var oldFrost = Frost;
            Background = layers is null ? null : ToBitmap(layers.Background);
            Frost = layers is null ? null : ToBitmap(layers.Frost);
            _lastKey = key;
            string what = request.Kind switch
            {
                BackdropKind.Color => $"Color {settings.Color}",
                BackdropKind.Gradient => $"Gradient {settings.Gradient}",
                BackdropKind.Image => $"Image {(settings.Image == BackdropSettings.CustomImageId ? "of your own" : settings.Image)}{(request.Image is null ? " (unreadable)" : string.Empty)}",
                _ => "See-through",
            };
            if (what != _logged)
            {
                _logged = what;
                Log.Info("Appearance", Layers(layers) is { } size
                    ? $"Background: {what} drawn at {size} in {timer.ElapsedMilliseconds} ms."
                    : $"Background: {what}.");
            }

            Changed?.Invoke(null, EventArgs.Empty);
            oldBackground?.Dispose();
            oldFrost?.Dispose();
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

    private static string? Layers(BackdropLayers? layers) =>
        layers is null ? null : $"{layers.Background.Width}×{layers.Background.Height} (frost {layers.Frost.Width}×{layers.Frost.Height})";

    /// <summary>The chosen picture: Coreloom's own (inside the app), or your copy in <see cref="Folder"/>.</summary>
    private static string PicturePath(BackdropSettings settings) =>
        settings.Image == BackdropSettings.CustomImageId && settings.CustomImage is { } name && Folder is { } folder
            ? Path.Combine(folder, name)
            : $"avares://Coreloom/Assets/Backdrops/{settings.Image}.jpg";

    private static async Task<PixelImage?> LoadPictureAsync(string path)
    {
        if (_picture is { } cached && cached.Key == path)
        {
            return cached.Image;
        }

        try
        {
            Bitmap? bitmap = null;
            if (path.StartsWith("avares://", StringComparison.Ordinal))
            {
                await using var stream = AssetLoader.Open(new Uri(path));
                bitmap = new Bitmap(stream);
            }
            else if (File.Exists(path))
            {
                bitmap = TryDecode(path) ?? (OperatingSystem.IsMacOS() ? await ConvertAsync(path).ConfigureAwait(false) : null);
            }

            if (bitmap is null)
            {
                Log.Warn("Appearance", "The background picture couldn't be read; the theme's own color shows instead.");
                return null;
            }

            using (bitmap)
            {
                var image = Pixels(bitmap);
                _picture = (path, image);
                return image;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            Log.Warn("Appearance", "The background picture couldn't be read.", e);
            return null;
        }
    }

    private static Bitmap? TryDecode(string path)
    {
        try
        {
            return new Bitmap(path);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or NotSupportedException or IOException)
        {
            return null;
        }
    }

    /// <summary>A picture Skia can't read (HEIC): macOS's sips converts it to a JPEG next to it, once.</summary>
    private static async Task<Bitmap?> ConvertAsync(string path)
    {
        string jpeg = path + ".jpg";
        if (!File.Exists(jpeg))
        {
            var result = await Commands.RunAsync("/usr/bin/sips", ["-s", "format", "jpeg", "-Z", MaxPictureWidth.ToString(System.Globalization.CultureInfo.InvariantCulture), path, "--out", jpeg], TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            if (!result.Succeeded || !File.Exists(jpeg))
            {
                return null;
            }
        }

        return TryDecode(jpeg);
    }

    /// <summary>The bitmap's pixels as BGRA (at most <see cref="MaxPictureWidth"/> wide), opaque.</summary>
    private static PixelImage Pixels(Bitmap bitmap)
    {
        Bitmap source = bitmap;
        if (bitmap.PixelSize.Width > MaxPictureWidth)
        {
            int height = Math.Max(1, (int)((long)bitmap.PixelSize.Height * MaxPictureWidth / bitmap.PixelSize.Width));
            source = bitmap.CreateScaledBitmap(new PixelSize(MaxPictureWidth, height), BitmapInterpolationMode.HighQuality);
        }

        try
        {
            int w = source.PixelSize.Width, h = source.PixelSize.Height;
            var bytes = new byte[w * h * 4];
            var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                source.CopyPixels(new PixelRect(0, 0, w, h), handle.AddrOfPinnedObject(), bytes.Length, w * 4);
            }
            finally
            {
                handle.Free();
            }

            bool rgba = source.Format is { } format && format == PixelFormats.Rgba8888;
            for (int i = 0; i < bytes.Length; i += 4)
            {
                if (rgba)
                {
                    (bytes[i], bytes[i + 2]) = (bytes[i + 2], bytes[i]);
                }

                bytes[i + 3] = 255;
            }

            return new PixelImage(w, h, bytes);
        }
        finally
        {
            if (!ReferenceEquals(source, bitmap))
            {
                source.Dispose();
            }
        }
    }

    /// <summary>Pixels into a bitmap Avalonia can draw.</summary>
    public static WriteableBitmap ToBitmap(PixelImage image)
    {
        var bitmap = new WriteableBitmap(new PixelSize(image.Width, image.Height), new Vector(96, 96), PixelFormats.Bgra8888, AlphaFormat.Opaque);
        using var buffer = bitmap.Lock();
        int stride = image.Stride;
        for (int y = 0; y < image.Height; y++)
        {
            Marshal.Copy(image.Bgra, y * stride, buffer.Address + (y * buffer.RowBytes), stride);
        }

        return bitmap;
    }
}
