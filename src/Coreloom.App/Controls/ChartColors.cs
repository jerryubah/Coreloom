using System.Windows;
using System.Windows.Media;
using Coreloom.App.Themes;

namespace Coreloom.App.Controls;

/// <summary>Font families for custom-drawn text, resolved once from the embedded font files.</summary>
internal static class ChartFonts
{
    private static readonly Uri BaseUri = new("pack://application:,,,/");

    /// <summary>Inter: axis captions and tooltip labels.</summary>
    public static FontFamily Ui { get; } = new(BaseUri, "./Fonts/#Inter");

    /// <summary>JetBrains Mono: numbers (axis values, tooltip readings).</summary>
    public static FontFamily Mono { get; } = new(BaseUri, "./Fonts/#JetBrains Mono");

    public static Typeface UiRegular { get; } = new(Ui, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    public static Typeface MonoRegular { get; } = new(Mono, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    public static Typeface MonoSemiBold { get; } = new(Mono, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
}

/// <summary>Resolves theme colors for custom-drawn controls and caches frozen pens/brushes per color.</summary>
internal static class ChartColors
{
    private static readonly Dictionary<(uint Color, double Thickness), Pen> Pens = [];
    private static readonly Dictionary<(uint Color, int Opacity), Brush> Fills = [];
    private static readonly Dictionary<uint, SolidColorBrush> Solids = [];

    public static Color Series(string? colorKey) => ColorOf(ThemeService.Instance?.GetSeriesBrush(colorKey));

    public static Color Theme(string key) => ColorOf(ThemeService.Instance?.GetBrush("Brush." + key));

    public static Color ColorOf(Brush? brush) => brush is SolidColorBrush solid ? solid.Color : Colors.Gray;

    public static SolidColorBrush Solid(Color color)
    {
        uint key = Pack(color);
        if (!Solids.TryGetValue(key, out var brush))
        {
            if (Solids.Count > 256)
            {
                Solids.Clear();
            }

            brush = new SolidColorBrush(color);
            brush.Freeze();
            Solids[key] = brush;
        }

        return brush;
    }

    public static Pen Pen(Color color, double thickness, bool round = true)
    {
        var key = (Pack(color), thickness + (round ? 0 : 1000));
        if (!Pens.TryGetValue(key, out var pen))
        {
            if (Pens.Count > 256)
            {
                Pens.Clear();
            }

            pen = new Pen(Solid(color), thickness)
            {
                LineJoin = PenLineJoin.Round,
                StartLineCap = round ? PenLineCap.Round : PenLineCap.Flat,
                EndLineCap = round ? PenLineCap.Round : PenLineCap.Flat,
            };
            pen.Freeze();
            Pens[key] = pen;
        }

        return pen;
    }

    public static Pen DashedPen(Color color, double thickness)
    {
        var pen = new Pen(Solid(color), thickness) { DashStyle = new DashStyle([4, 4], 0), DashCap = PenLineCap.Flat };
        pen.Freeze();
        return pen;
    }

    /// <summary>
    /// The fill under a line: <paramref name="opacity"/> at the line, easing away to nothing at the bottom (since 1.8.3
    /// along a curve rather than a straight ramp, so it reads deep near the line and ends without a visible edge).
    /// </summary>
    public static Brush AreaFill(Color color, double opacity)
    {
        var key = (Pack(color), (int)(opacity * 100));
        if (!Fills.TryGetValue(key, out var brush))
        {
            if (Fills.Count > 256)
            {
                Fills.Clear();
            }

            var gradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            foreach (var (offset, share) in FillCurve)
            {
                gradient.GradientStops.Add(new GradientStop(Color.FromArgb((byte)Math.Clamp(opacity * share * 255, 0, 255), color.R, color.G, color.B), offset));
            }

            gradient.Freeze();
            Fills[key] = gradient;
            brush = gradient;
        }

        return brush;
    }

    /// <summary>How much of the fill's strength is left down the area (an ease-out curve ending at nothing).</summary>
    internal static readonly (double Offset, double Share)[] FillCurve =
        [(0, 1), (0.18, 0.72), (0.38, 0.45), (0.58, 0.24), (0.78, 0.09), (0.92, 0.025), (1, 0)];

    /// <summary>
    /// A mask that fades a sparkline's fill in over the first few percent and out over the last few, so the fill has no
    /// hard left and right edges (the line itself isn't masked).
    /// </summary>
    public static Brush EdgeFade { get; } = CreateEdgeFade();

    private static Brush CreateEdgeFade()
    {
        var mask = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        mask.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0));
        mask.GradientStops.Add(new GradientStop(Color.FromArgb(255, 0, 0, 0), 0.07));
        mask.GradientStops.Add(new GradientStop(Color.FromArgb(255, 0, 0, 0), 0.93));
        mask.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 1));
        mask.Freeze();
        return mask;
    }

    private static uint Pack(Color c) => ((uint)c.A << 24) | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;
}
