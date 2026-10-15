using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace Coreloom.App.Behaviors;

/// <summary>
/// Shows a picture file without holding up the window: the file is decoded at the size it is shown (<c>DecodeWidth</c>)
/// on a background thread, kept in a small cache, and faded in. On an <see cref="Image"/> it becomes the source; on a
/// <see cref="Border"/> it fills the background (cropped to fill, inside the rounded corners). Game covers in the Gaming
/// page's library use it, so opening a large library stays smooth. A missing or broken file leaves it empty.
/// </summary>
public static class AsyncImage
{
    public static readonly DependencyProperty PathProperty = DependencyProperty.RegisterAttached(
        "Path", typeof(string), typeof(AsyncImage), new PropertyMetadata(null, OnChanged));

    public static readonly DependencyProperty DecodeWidthProperty = DependencyProperty.RegisterAttached(
        "DecodeWidth", typeof(int), typeof(AsyncImage), new PropertyMetadata(0));

    private const int CacheSize = 400;
    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly LinkedList<string> Order = new();

    public static string? GetPath(DependencyObject element) => (string?)element.GetValue(PathProperty);

    public static void SetPath(DependencyObject element, string? value) => element.SetValue(PathProperty, value);

    public static int GetDecodeWidth(DependencyObject element) => (int)element.GetValue(DecodeWidthProperty);

    public static void SetDecodeWidth(DependencyObject element, int value) => element.SetValue(DecodeWidthProperty, value);

    private static async void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not (Image or Border))
        {
            return;
        }

        var element = (FrameworkElement)d;
        string? path = e.NewValue as string;
        if (string.IsNullOrEmpty(path))
        {
            Show(element, null);
            return;
        }

        int width = GetDecodeWidth(element);
        string key = $"{path}|{width}";
        if (TryCached(key, out var cached))
        {
            Show(element, cached);
            element.BeginAnimation(UIElement.OpacityProperty, null);
            return;
        }

        Show(element, null);
        var source = await Task.Run(() => Load(path, width));
        Remember(key, source);
        if (!string.Equals(GetPath(element), path, StringComparison.OrdinalIgnoreCase))
        {
            return; // the item was recycled for another game meanwhile
        }

        Show(element, source);
        if (source is not null && !Motion.ReducedMotion)
        {
            element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
    }

    private static void Show(FrameworkElement element, ImageSource? source)
    {
        if (element is Image image)
        {
            image.Source = source;
        }
        else if (element is Border border)
        {
            if (source is null)
            {
                border.Background = null;
                return;
            }

            var brush = new ImageBrush(source) { Stretch = Stretch.UniformToFill, AlignmentY = AlignmentY.Center };
            brush.Freeze();
            border.Background = brush;
        }
    }

    private static ImageSource? Load(string path, int width)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad; // read now, so the file isn't kept open
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile | BitmapCreateOptions.IgnoreImageCache;
            if (width > 0)
            {
                bitmap.DecodePixelWidth = width;
            }

            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException or UriFormatException)
        {
            return null;
        }
    }

    private static bool TryCached(string key, out ImageSource? source)
    {
        lock (Cache)
        {
            return Cache.TryGetValue(key, out source);
        }
    }

    private static void Remember(string key, ImageSource? source)
    {
        lock (Cache)
        {
            if (Cache.ContainsKey(key))
            {
                return;
            }

            Cache[key] = source;
            Order.AddLast(key);
            while (Order.Count > CacheSize)
            {
                Cache.Remove(Order.First!.Value);
                Order.RemoveFirst();
            }
        }
    }
}
