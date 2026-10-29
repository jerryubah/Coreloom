using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Coreloom.App.Services;
using Coreloom.App.Themes;
using Coreloom.Core.Appearance;

namespace Coreloom.App.Converters;

/// <summary>A gradient's swatch for Settings › Appearance › Background: the gradient itself, drawn small (dark, as it shows).</summary>
public sealed class BackdropGradientPreviewConverter : IValueConverter
{
    private static readonly Dictionary<string, BitmapSource> Cache = [];

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

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>A picture's swatch: Coreloom's own thumbnails by id, or your own picture (a full path) shrunk.</summary>
public sealed class BackdropThumbnailConverter : IValueConverter
{
    private static readonly Dictionary<string, BitmapSource?> Cache = [];

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

        BitmapSource? bitmap = null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 336;
            if (Path.IsPathRooted(key))
            {
                if (File.Exists(key))
                {
                    image.StreamSource = new MemoryStream(File.ReadAllBytes(key));
                }
            }
            else
            {
                image.UriSource = new Uri($"pack://application:,,,/Assets/Backdrops/{key}-thumb.jpg", UriKind.Absolute);
            }

            if (image.StreamSource is not null || image.UriSource is not null)
            {
                image.EndInit();
                image.Freeze();
                bitmap = image;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or InvalidOperationException or FileFormatException)
        {
            bitmap = null; // shows as an empty tile
        }

        Cache[key] = bitmap;
        return bitmap;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Two values equal (strings, ignoring case) → true; for a swatch's selected ring.</summary>
public sealed class MultiEqualsToBoolConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Length >= 2 && string.Equals(values[0]?.ToString(), values[1]?.ToString(), StringComparison.OrdinalIgnoreCase);

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
