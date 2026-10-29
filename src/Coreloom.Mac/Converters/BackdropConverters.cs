using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Coreloom.Core.Appearance;
using Coreloom.Mac.Services;
using Coreloom.Mac.Themes;

namespace Coreloom.Mac.Converters;

/// <summary>A gradient's swatch for Settings › Appearance › Background: the gradient itself, drawn small (dark, as it shows).</summary>
public sealed class BackdropGradientPreviewConverter : IValueConverter
{
    private static readonly Dictionary<string, Bitmap> Cache = [];

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not BackdropGradient gradient)
        {
            return null;
        }

        var accent = ThemeService.Instance?.AccentColor ?? Colors.CornflowerBlue;
        string key = gradient.Id + (gradient.UsesAccent ? accent.ToString() : string.Empty);
        if (!Cache.TryGetValue(key, out var bitmap))
        {
            // A little brighter than in the window, where the lights spread over far more room: so the small swatches
            // can be told apart.
            var swatch = gradient with { Glows = gradient.Glows.Select(g => g with { Strength = g.Strength * 1.7 }).ToArray() };
            var pixels = BackdropRenderer.Gradient(swatch, 240, 150, new Rgb(accent.R / 255f, accent.G / 255f, accent.B / 255f),
                dark: true, Rgb.Parse("#F3F4F6"), dither: true);
            bitmap = Backdrop.ToBitmap(pixels);
            Cache[key] = bitmap;
        }

        return bitmap;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => BindingOperations.DoNothing;
}

/// <summary>A picture's swatch: Coreloom's own thumbnails by id, or your own picture (a full path) shrunk.</summary>
public sealed class BackdropThumbnailConverter : IValueConverter
{
    private static readonly Dictionary<string, Bitmap?> Cache = [];

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string key || key.Length == 0)
        {
            return null;
        }

        if (Cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        Bitmap? bitmap = null;
        try
        {
            if (Path.IsPathRooted(key))
            {
                if (File.Exists(key))
                {
                    using var stream = File.OpenRead(key);
                    bitmap = Bitmap.DecodeToWidth(stream, 336, BitmapInterpolationMode.HighQuality);
                }
                else if (File.Exists(key + ".jpg"))
                {
                    using var stream = File.OpenRead(key + ".jpg"); // a HEIC picture, converted by sips
                    bitmap = Bitmap.DecodeToWidth(stream, 336, BitmapInterpolationMode.HighQuality);
                }
            }
            else
            {
                using var stream = AssetLoader.Open(new Uri($"avares://Coreloom/Assets/Backdrops/{key}-thumb.jpg"));
                bitmap = new Bitmap(stream);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            bitmap = null; // shows as an empty tile
        }

        Cache[key] = bitmap;
        return bitmap;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => BindingOperations.DoNothing;
}
