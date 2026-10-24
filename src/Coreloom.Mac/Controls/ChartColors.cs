using Avalonia.Media;
using Avalonia.Media.Immutable;
using Coreloom.Mac.Themes;

namespace Coreloom.Mac.Controls;

/// <summary>Font families for custom-drawn text (the embedded Inter and JetBrains Mono).</summary>
internal static class ChartFonts
{
    public static FontFamily Ui { get; } = new("avares://Coreloom/Assets/Fonts#Inter");

    public static FontFamily Mono { get; } = new("avares://Coreloom/Assets/Fonts#JetBrains Mono");

    public static Typeface UiRegular { get; } = new(Ui, FontStyle.Normal, FontWeight.Normal);

    public static Typeface UiSemiBold { get; } = new(Ui, FontStyle.Normal, FontWeight.SemiBold);

    public static Typeface MonoRegular { get; } = new(Mono, FontStyle.Normal, FontWeight.Normal);

    public static Typeface MonoSemiBold { get; } = new(Mono, FontStyle.Normal, FontWeight.SemiBold);
}

/// <summary>Resolves theme colors for custom-drawn controls and caches immutable pens and brushes per color.</summary>
internal static class ChartColors
{
    private static readonly Dictionary<(uint Color, double Thickness), IPen> Pens = [];
    private static readonly Dictionary<(uint Color, int Opacity), IBrush> Fills = [];
    private static readonly Dictionary<uint, IImmutableSolidColorBrush> Solids = [];

    public static Color Series(string? colorKey) => ColorOf(ThemeService.Instance?.GetSeriesBrush(colorKey));

    public static Color Theme(string key) => ColorOf(ThemeService.Instance?.GetBrush("Brush." + key));

    public static Color ColorOf(IBrush? brush) => brush is ISolidColorBrush solid ? solid.Color : Colors.Gray;

    public static IImmutableSolidColorBrush Solid(Color color)
    {
        uint key = color.ToUInt32();
        if (!Solids.TryGetValue(key, out var brush))
        {
            if (Solids.Count > 256)
            {
                Solids.Clear();
            }

            brush = new ImmutableSolidColorBrush(color);
            Solids[key] = brush;
        }

        return brush;
    }

    public static IPen Pen(Color color, double thickness, bool round = true)
    {
        var key = (color.ToUInt32(), thickness + (round ? 0 : 1000));
        if (!Pens.TryGetValue(key, out var pen))
        {
            if (Pens.Count > 256)
            {
                Pens.Clear();
            }

            pen = new ImmutablePen(Solid(color), thickness, null, round ? PenLineCap.Round : PenLineCap.Flat, PenLineJoin.Round);
            Pens[key] = pen;
        }

        return pen;
    }

    public static IPen Pen(IBrush brush, double thickness) => new ImmutablePen(brush.ToImmutable(), thickness, null, PenLineCap.Flat, PenLineJoin.Round);

    public static IPen DashedPen(Color color, double thickness) =>
        new ImmutablePen(Solid(color), thickness, new ImmutableDashStyle([4, 4], 0), PenLineCap.Flat);

    /// <summary>Vertical gradient from <paramref name="opacity"/> at the top to transparent at the bottom.</summary>
    public static IBrush AreaFill(Color color, double opacity)
    {
        var key = (color.ToUInt32(), (int)(opacity * 100));
        if (!Fills.TryGetValue(key, out var brush))
        {
            if (Fills.Count > 256)
            {
                Fills.Clear();
            }

            var top = Color.FromArgb((byte)Math.Clamp(opacity * 255, 0, 255), color.R, color.G, color.B);
            var bottom = Color.FromArgb(0, color.R, color.G, color.B);
            brush = new ImmutableLinearGradientBrush(
                [new ImmutableGradientStop(0, top), new ImmutableGradientStop(1, bottom)],
                startPoint: new Avalonia.RelativePoint(0, 0, Avalonia.RelativeUnit.Relative),
                endPoint: new Avalonia.RelativePoint(0, 1, Avalonia.RelativeUnit.Relative));
            Fills[key] = brush;
        }

        return brush;
    }

    public static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);
}
