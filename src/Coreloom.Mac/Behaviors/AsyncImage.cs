using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Coreloom.Mac.Behaviors;

/// <summary>
/// Shows a picture file without holding up the window: the file is decoded at the size it is shown (<c>DecodeWidth</c>,
/// in pixels, so twice the width on a Retina display) on a background thread, kept in a small cache, and faded in. On an
/// <see cref="Image"/> it becomes the source; on a <see cref="Border"/> or <see cref="Controls.SquircleBorder"/> it fills
/// the background (cropped to fill, inside the rounded corners). Game covers in the Gaming page's library use it, so
/// opening a large library stays smooth. A missing or broken file leaves it empty.
/// </summary>
public static class AsyncImage
{
    public static readonly AttachedProperty<string?> PathProperty =
        AvaloniaProperty.RegisterAttached<Control, string?>("Path", typeof(AsyncImage));

    public static readonly AttachedProperty<int> DecodeWidthProperty =
        AvaloniaProperty.RegisterAttached<Control, int>("DecodeWidth", typeof(AsyncImage));

    private const int CacheSize = 400;
    private static readonly Dictionary<string, Bitmap?> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly LinkedList<string> Order = new();

    static AsyncImage()
    {
        PathProperty.Changed.AddClassHandler<Control>((element, e) => OnChanged(element, e.NewValue as string));
    }

    public static string? GetPath(Control element) => element.GetValue(PathProperty);

    public static void SetPath(Control element, string? value) => element.SetValue(PathProperty, value);

    public static int GetDecodeWidth(Control element) => element.GetValue(DecodeWidthProperty);

    public static void SetDecodeWidth(Control element, int value) => element.SetValue(DecodeWidthProperty, value);

    private static async void OnChanged(Control element, string? path)
    {
        if (element is not (Image or Border or Controls.SquircleBorder))
        {
            return;
        }

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
            element.Opacity = 1;
            return;
        }

        Show(element, null);
        var bitmap = await Task.Run(() => Load(path, width));
        Remember(key, bitmap);
        if (!string.Equals(GetPath(element), path, StringComparison.OrdinalIgnoreCase))
        {
            return; // the item was reused for another game meanwhile
        }

        Show(element, bitmap);
        if (bitmap is not null && !Motion.ReducedMotion)
        {
            element.Opacity = 0;
            Animator.Run(TimeSpan.FromMilliseconds(220), Ease.CubicOut, t => element.Opacity = t, () => element.Opacity = 1);
        }
    }

    private static void Show(Control element, Bitmap? bitmap)
    {
        IBrush? brush = bitmap is null ? null : new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill, AlignmentY = AlignmentY.Center };
        switch (element)
        {
            case Image image:
                image.Source = bitmap;
                break;
            case Border border:
                border.Background = brush;
                break;
            case Controls.SquircleBorder squircle:
                squircle.Background = brush;
                break;
        }
    }

    private static Bitmap? Load(string path, int width)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using var stream = File.OpenRead(path); // read now, so the file isn't kept open
            return width > 0 ? Bitmap.DecodeToWidth(stream, width, BitmapInterpolationMode.HighQuality) : new Bitmap(stream);
        }
        catch (Exception)
        {
            return null; // unreadable, or a file the decoder can't read (Skia throws its own exceptions)
        }
    }

    private static bool TryCached(string key, out Bitmap? bitmap)
    {
        lock (Cache)
        {
            return Cache.TryGetValue(key, out bitmap);
        }
    }

    private static void Remember(string key, Bitmap? bitmap)
    {
        lock (Cache)
        {
            if (Cache.ContainsKey(key))
            {
                return;
            }

            Cache[key] = bitmap;
            Order.AddLast(key);
            while (Order.Count > CacheSize)
            {
                Cache.Remove(Order.First!.Value);
                Order.RemoveFirst();
            }
        }
    }
}
