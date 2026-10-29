using Avalonia.Media;

namespace Coreloom.Mac.Themes;

/// <summary>
/// Color tokens for the dark and light themes. Brushes are created once (unfrozen) and their colors are
/// updated in place on theme/accent changes, so every element using them updates without re-binding.
/// </summary>
internal static class ThemePalette
{
    public static IReadOnlyDictionary<string, Color> Dark { get; } = new Dictionary<string, Color>
    {
        ["Background"] = Hex("#0A0B0E"),
        // Since 1.8.2 the cards are glass over the window's background, so what sits inside them (insets, inputs,
        // tracks) is see-through too and takes on whatever the card shows. "Card" stays solid for menus and pop-ups.
        ["Surface"] = Hex("#0FFFFFFF"),
        ["Card"] = Hex("#15171C"),
        ["CardHover"] = Hex("#1B1D23"),
        ["CardBorder"] = Hex("#12FFFFFF"),
        ["Divider"] = Hex("#15FFFFFF"),
        ["Text"] = Hex("#F4F5F7"),
        ["TextSecondary"] = Hex("#9CA2AE"),
        ["TextTertiary"] = Hex("#6E7480"),
        ["Track"] = Hex("#17FFFFFF"),
        ["ChartGrid"] = Hex("#0FFFFFFF"),
        ["ChartAxis"] = Hex("#6E7480"),
        ["ChartCrosshair"] = Hex("#40FFFFFF"),
        ["Tooltip"] = Hex("#F51B1D23"),
        ["TooltipBorder"] = Hex("#22FFFFFF"),
        ["Input"] = Hex("#12FFFFFF"),
        ["InputBorder"] = Hex("#1CFFFFFF"),
        ["InputHover"] = Hex("#1AFFFFFF"),
        ["ControlHover"] = Hex("#12FFFFFF"),
        ["ControlPressed"] = Hex("#0CFFFFFF"),
        ["NavSelected"] = Hex("#17FFFFFF"),
        ["Elevated"] = Hex("#FFB454"),
        ["Critical"] = Hex("#FF5C5C"),
        ["Success"] = Hex("#45D6A0"),
        ["Info"] = Hex("#5B8CFF"),
        ["BannerBackground"] = Hex("#1FFFB454"),
        ["BannerBorder"] = Hex("#40FFB454"),
        ["Scrim"] = Hex("#B3040506"),
        ["Series.Cpu"] = Hex("#5B8CFF"),
        ["Series.Gpu"] = Hex("#45D6A0"),
        ["Series.Memory"] = Hex("#AA86FF"),
        ["Series.Storage"] = Hex("#3CC8D8"),
        ["Series.Temperature"] = Hex("#FFB454"),
        ["Series.Power"] = Hex("#FF8A5B"),
        ["Series.Fan"] = Hex("#7FD1FF"),
        ["Series.NetworkDown"] = Hex("#5B8CFF"),
        ["Series.NetworkUp"] = Hex("#AA86FF"),
        ["Series.Read"] = Hex("#3CC8D8"),
        ["Series.Write"] = Hex("#FFB454"),
        ["Series.Neutral"] = Hex("#2C3038"),

        // Temperatures: each band is a gradient (Tail → Head, cooler to warmer) and one color for its card's glow.
        ["Temp.Cool"] = Hex("#4C9DFF"),
        ["Temp.Cool.Tail"] = Hex("#2E5FF2"),
        ["Temp.Cool.Head"] = Hex("#6FCBFF"),
        ["Temp.Normal"] = Hex("#34D3A0"),
        ["Temp.Normal.Tail"] = Hex("#1FAFC4"),
        ["Temp.Normal.Head"] = Hex("#4FE08C"),
        ["Temp.Warm"] = Hex("#FFAA3A"),
        ["Temp.Warm.Tail"] = Hex("#FFD24A"),
        ["Temp.Warm.Head"] = Hex("#FF8A2E"),
        ["Temp.Hot"] = Hex("#FF5A4A"),
        ["Temp.Hot.Tail"] = Hex("#FF7F3A"),
        ["Temp.Hot.Head"] = Hex("#FF3B5C"),
    };

    public static IReadOnlyDictionary<string, Color> Light { get; } = new Dictionary<string, Color>
    {
        ["Background"] = Hex("#F3F4F6"),
        ["Surface"] = Hex("#0B000000"),
        ["Card"] = Hex("#FFFFFF"),
        ["CardHover"] = Hex("#F8F9FA"),
        ["CardBorder"] = Hex("#14000000"),
        ["Divider"] = Hex("#14000000"),
        ["Text"] = Hex("#15171C"),
        ["TextSecondary"] = Hex("#5D6370"),
        ["TextTertiary"] = Hex("#8A909C"),
        ["Track"] = Hex("#14000000"),
        ["ChartGrid"] = Hex("#12000000"),
        ["ChartAxis"] = Hex("#8A909C"),
        ["ChartCrosshair"] = Hex("#40000000"),
        ["Tooltip"] = Hex("#F7FFFFFF"),
        ["TooltipBorder"] = Hex("#1F000000"),
        ["Input"] = Hex("#B8FFFFFF"),
        ["InputBorder"] = Hex("#26000000"),
        ["InputHover"] = Hex("#E0FFFFFF"),
        ["ControlHover"] = Hex("#0F000000"),
        ["ControlPressed"] = Hex("#08000000"),
        ["NavSelected"] = Hex("#FFFFFF"),
        ["Elevated"] = Hex("#D9861A"),
        ["Critical"] = Hex("#E5484D"),
        ["Success"] = Hex("#169F70"),
        ["Info"] = Hex("#3D6FF2"),
        ["BannerBackground"] = Hex("#1FD9861A"),
        ["BannerBorder"] = Hex("#40D9861A"),
        ["Scrim"] = Hex("#66101216"),
        ["Series.Cpu"] = Hex("#3D6FF2"),
        ["Series.Gpu"] = Hex("#169F70"),
        ["Series.Memory"] = Hex("#8B63F0"),
        ["Series.Storage"] = Hex("#1BA3B5"),
        ["Series.Temperature"] = Hex("#E08A16"),
        ["Series.Power"] = Hex("#EE6A3A"),
        ["Series.Fan"] = Hex("#2E9BD6"),
        ["Series.NetworkDown"] = Hex("#3D6FF2"),
        ["Series.NetworkUp"] = Hex("#8B63F0"),
        ["Series.Read"] = Hex("#1BA3B5"),
        ["Series.Write"] = Hex("#E08A16"),
        ["Series.Neutral"] = Hex("#DADDE3"),

        ["Temp.Cool"] = Hex("#2F7CF2"),
        ["Temp.Cool.Tail"] = Hex("#2450D6"),
        ["Temp.Cool.Head"] = Hex("#27A2E8"),
        ["Temp.Normal"] = Hex("#169F78"),
        ["Temp.Normal.Tail"] = Hex("#138EA8"),
        ["Temp.Normal.Head"] = Hex("#1DAA5C"),
        ["Temp.Warm"] = Hex("#DE8612"),
        ["Temp.Warm.Tail"] = Hex("#E0A810"),
        ["Temp.Warm.Head"] = Hex("#E0680F"),
        ["Temp.Hot"] = Hex("#E3463F"),
        ["Temp.Hot.Tail"] = Hex("#E8692A"),
        ["Temp.Hot.Head"] = Hex("#D92A4A"),
    };

    public static Color Hex(string hex) => Color.Parse(hex);

    public static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

    /// <summary>Picks black or white text for good contrast on the given background.</summary>
    public static Color ContrastingText(Color background)
    {
        double luminance = (0.2126 * background.R) + (0.7152 * background.G) + (0.0722 * background.B);
        return luminance > 150 ? Hex("#15171C") : Colors.White;
    }

    /// <summary>
    /// The color with its hue turned by <paramref name="degrees"/> (positive toward violet for a blue) and its lightness
    /// moved by <paramref name="lighten"/> (−1 … 1); alpha is kept. Used to build gradients from the user's accent.
    /// </summary>
    public static Color Shift(Color color, double degrees, double lighten = 0)
    {
        double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double l = (max + min) / 2, h = 0, s = 0;
        double d = max - min;
        if (d > 1e-9)
        {
            s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            h = max == r ? ((g - b) / d) + (g < b ? 6 : 0) : max == g ? ((b - r) / d) + 2 : ((r - g) / d) + 4;
            h *= 60;
        }

        h = (((h + degrees) % 360) + 360) % 360;
        l = Math.Clamp(l + lighten, 0, 1);
        double q = l < 0.5 ? l * (1 + s) : l + s - (l * s);
        double p = (2 * l) - q;
        return Color.FromArgb(color.A, Channel(p, q, h + 120), Channel(p, q, h), Channel(p, q, h - 120));

        static byte Channel(double p, double q, double hue)
        {
            hue = ((hue % 360) + 360) % 360;
            double v = hue < 60 ? p + ((q - p) * hue / 60)
                : hue < 180 ? q
                : hue < 240 ? p + ((q - p) * (240 - hue) / 60)
                : p;
            return (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);
        }
    }
}
