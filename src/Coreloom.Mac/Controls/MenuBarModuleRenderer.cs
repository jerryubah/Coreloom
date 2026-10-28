using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Coreloom.ViewModels.MenuBar;

namespace Coreloom.Mac.Controls;

/// <summary>
/// Draws one menu bar module the way the menu bar shows it: the module's small name stacked letter over letter, the
/// reading (one line, or two small ones for the network), and a slim bar that fills from the bottom. In the menu bar it's
/// a template image, so macOS colors it for light and dark menu bars like its own icons; Settings draws the same in its
/// preview.
/// </summary>
public static class MenuBarModuleRenderer
{
    /// <summary>The menu bar's height in points.</summary>
    public const double Height = 22;

    private const double LabelSize = 6.6;
    private const double ValueSize = 12.5;
    private const double SmallSize = 9;
    private const double Gap = 3;
    private const double GaugeWidth = 4.5;
    private const double GaugeHeight = 15;

    private static readonly Typeface Regular = new(new FontFamily("avares://Coreloom/Assets/Fonts#Inter"), FontStyle.Normal, FontWeight.Medium);
    private static readonly Typeface Bold = new(new FontFamily("avares://Coreloom/Assets/Fonts#Inter"), FontStyle.Normal, FontWeight.SemiBold);

    /// <summary>The module's width in points.</summary>
    public static double Measure(MenuBarReading reading) => Layout(reading, Brushes.Black).Width;

    /// <summary>Draws the module with its top left at <paramref name="origin"/>, in <paramref name="ink"/>.</summary>
    public static void Draw(DrawingContext context, MenuBarReading reading, Point origin, IBrush ink, double inkOpacity = 1)
    {
        var layout = Layout(reading, ink);
        double x = origin.X + 1;
        double middle = origin.Y + (Height / 2);

        if (layout.Letters is { } letters)
        {
            double top = middle - (letters.Sum(l => l.Height * 0.86) / 2);
            foreach (var letter in letters)
            {
                context.DrawText(letter, new Point(x + ((layout.LabelWidth - letter.Width) / 2), top - (letter.Height * 0.07)));
                top += letter.Height * 0.86;
            }

            x += layout.LabelWidth + 2;
        }

        if (layout.Second is { } second)
        {
            double total = layout.Value.Height * 0.9 + second.Height * 0.9;
            double top = middle - (total / 2);
            context.DrawText(layout.Value, new Point(x + layout.ValueWidth - layout.Value.Width, top - (layout.Value.Height * 0.05)));
            context.DrawText(second, new Point(x + layout.ValueWidth - second.Width, top + (layout.Value.Height * 0.9) - (second.Height * 0.05)));
            x += layout.ValueWidth;
        }
        else
        {
            context.DrawText(layout.Value, new Point(x + layout.ValueWidth - layout.Value.Width, middle - (layout.Value.Height / 2)));
            x += layout.ValueWidth;
        }

        if (reading.Fraction is double fraction)
        {
            x += Gap;
            var track = new Rect(x, middle - (GaugeHeight / 2), GaugeWidth, GaugeHeight);
            var color = ink is ISolidColorBrush solid ? solid.Color : Colors.Black;
            context.DrawRectangle(null, new Pen(new SolidColorBrush(color, 0.45 * inkOpacity), 1), new RoundedRect(track.Deflate(0.5), 2));
            double fill = Math.Clamp(fraction, 0, 1) * (GaugeHeight - 3);
            if (fill > 0.4)
            {
                var bar = new Rect(track.X + 1.5, track.Bottom - 1.5 - fill, GaugeWidth - 3, fill);
                context.DrawRectangle(new SolidColorBrush(color, inkOpacity), null, new RoundedRect(bar, 0.9));
            }
        }
    }

    /// <summary>
    /// The module as a PNG for an NSStatusItem (black on clear, drawn at <paramref name="scale"/>× for Retina screens), and
    /// its size in points.
    /// </summary>
    public static (byte[] Png, Size Size) RenderPng(MenuBarReading reading, double scale = 2)
    {
        double width = Math.Ceiling(Measure(reading) + 2);
        var pixels = new PixelSize((int)Math.Ceiling(width * scale), (int)Math.Ceiling(Height * scale));
        using var bitmap = new RenderTargetBitmap(pixels, new Vector(96 * scale, 96 * scale));
        using (var context = bitmap.CreateDrawingContext())
        {
            Draw(context, reading, new Point(0, 0), Brushes.Black);
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream);
        return (stream.ToArray(), new Size(width, Height));
    }

    private sealed record Measured(IReadOnlyList<FormattedText>? Letters, double LabelWidth, FormattedText Value, FormattedText? Second, double ValueWidth, double Width);

    /// <summary>The widest reading a module usually shows, so its item keeps its width as the reading changes.</summary>
    private static string Widest(string id) => id switch
    {
        "memory" => "88.8 GB",
        "cpu-temp" or "gpu-temp" => "100°C",
        "fans" => "8,888 RPM",
        "network" => "↓ 88.8 MB/s",
        "power" => "88.8 W",
        _ => "100%",
    };

    private static Measured Layout(MenuBarReading reading, IBrush ink)
    {
        FormattedText Text(string text, Typeface face, double size) =>
            new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, size, ink);

        IReadOnlyList<FormattedText>? letters = null;
        double labelWidth = 0;
        if (reading.ShowName && reading.Label.Length > 0)
        {
            letters = reading.Label.Select(c => Text(c.ToString(), Bold, LabelSize)).ToArray();
            labelWidth = letters.Max(l => l.Width);
        }

        bool twoLines = reading.SecondLine is not null;
        var face = twoLines ? Regular : Bold;
        double size = twoLines ? SmallSize : ValueSize;
        var value = Text(reading.Value, face, size);
        var second = twoLines ? Text(reading.SecondLine!, Regular, SmallSize) : null;
        double valueWidth = Math.Max(Math.Max(value.Width, second?.Width ?? 0), Text(Widest(reading.Id), face, size).Width);
        double width = 1 + (letters is null ? 0 : labelWidth + 2) + valueWidth + (reading.Fraction is null ? 0 : Gap + GaugeWidth);
        return new Measured(letters, labelWidth, value, second, valueWidth, width);
    }
}
