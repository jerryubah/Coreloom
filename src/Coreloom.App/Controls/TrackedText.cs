using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Coreloom.App.Controls;

/// <summary>
/// Single-line text with letter spacing (CSS <c>letter-spacing</c>), which WPF's TextBlock cannot do. Used for the
/// splash wordmark and its small tracked labels. The string is shaped once as a whole (so the font's kerning is
/// kept), then each character is drawn at its shaped position plus <see cref="Tracking"/> per preceding character.
/// Characters from <see cref="AccentStart"/> onwards use <see cref="AccentForeground"/> ("Core" + "loom").
/// </summary>
public sealed class TrackedText : FrameworkElement
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(TrackedText),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FontFamilyProperty = TextElement.FontFamilyProperty.AddOwner(typeof(TrackedText),
        new FrameworkPropertyMetadata(SystemFonts.MessageFontFamily, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.Inherits));

    public static readonly DependencyProperty FontSizeProperty = TextElement.FontSizeProperty.AddOwner(typeof(TrackedText),
        new FrameworkPropertyMetadata(SystemFonts.MessageFontSize, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.Inherits));

    public static readonly DependencyProperty FontWeightProperty = TextElement.FontWeightProperty.AddOwner(typeof(TrackedText),
        new FrameworkPropertyMetadata(FontWeights.Normal, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.Inherits));

    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(typeof(TrackedText),
        new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.Inherits));

    public static readonly DependencyProperty AccentForegroundProperty = DependencyProperty.Register(
        nameof(AccentForeground), typeof(Brush), typeof(TrackedText),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AccentStartProperty = DependencyProperty.Register(
        nameof(AccentStart), typeof(int), typeof(TrackedText),
        new FrameworkPropertyMetadata(-1, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Extra space after every character, in DIPs (negative tightens), like CSS letter-spacing.</summary>
    public static readonly DependencyProperty TrackingProperty = DependencyProperty.Register(
        nameof(Tracking), typeof(double), typeof(TrackedText),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Height of the line box in DIPs (NaN: the font's own line spacing). Text is centred in it, as in CSS.</summary>
    public static readonly DependencyProperty LineHeightProperty = DependencyProperty.Register(
        nameof(LineHeight), typeof(double), typeof(TrackedText),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    private double[] _positions = [];
    private double _naturalHeight;
    private double _width;

    public TrackedText()
    {
        Focusable = false;
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public FontFamily FontFamily
    {
        get => (FontFamily)GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public FontWeight FontWeight
    {
        get => (FontWeight)GetValue(FontWeightProperty);
        set => SetValue(FontWeightProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public Brush? AccentForeground
    {
        get => (Brush?)GetValue(AccentForegroundProperty);
        set => SetValue(AccentForegroundProperty, value);
    }

    public int AccentStart
    {
        get => (int)GetValue(AccentStartProperty);
        set => SetValue(AccentStartProperty, value);
    }

    public double Tracking
    {
        get => (double)GetValue(TrackingProperty);
        set => SetValue(TrackingProperty, value);
    }

    public double LineHeight
    {
        get => (double)GetValue(LineHeightProperty);
        set => SetValue(LineHeightProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        string text = Text ?? string.Empty;
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var shaped = Format(text, Brushes.Black, pixelsPerDip);

        // Left edge of every character in the shaped (kerned) line.
        _positions = new double[text.Length];
        double running = 0;
        for (int i = 0; i < text.Length; i++)
        {
            var box = text.Length == 1 ? null : shaped.BuildHighlightGeometry(new Point(0, 0), i, 1);
            _positions[i] = box is null || box.Bounds.IsEmpty ? running : box.Bounds.Left;
            running = box is null || box.Bounds.IsEmpty ? running : box.Bounds.Right;
        }

        _naturalHeight = shaped.Height;
        _width = Math.Max(0, shaped.WidthIncludingTrailingWhitespace + (text.Length * Tracking));
        double height = double.IsNaN(LineHeight) || LineHeight <= 0 ? _naturalHeight : LineHeight;
        return new Size(_width, height);
    }

    protected override void OnRender(DrawingContext dc)
    {
        string text = Text ?? string.Empty;
        if (text.Length == 0 || _positions.Length != text.Length)
        {
            return;
        }

        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        double Snap(double value) => Math.Round(value * pixelsPerDip) / pixelsPerDip;

        // CSS half-leading: the font's line box is centred in the requested line height (it may overflow it).
        double lineBox = double.IsNaN(LineHeight) || LineHeight <= 0 ? _naturalHeight : LineHeight;
        double top = Snap((lineBox - _naturalHeight) / 2);

        var accent = AccentForeground;
        int accentStart = AccentStart;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (char.IsWhiteSpace(c) || char.IsLowSurrogate(c))
            {
                continue;
            }

            string glyph = char.IsHighSurrogate(c) && i + 1 < text.Length ? text.Substring(i, 2) : c.ToString();
            var brush = accent is not null && accentStart >= 0 && i >= accentStart ? accent : Foreground;
            dc.DrawText(Format(glyph, brush, pixelsPerDip), new Point(Snap(_positions[i] + (i * Tracking)), top));
        }
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        InvalidateMeasure();
        InvalidateVisual();
        base.OnDpiChanged(oldDpi, newDpi);
    }

    private FormattedText Format(string text, Brush brush, double pixelsPerDip) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily ?? SystemFonts.MessageFontFamily, FontStyles.Normal, FontWeight, FontStretches.Normal),
            FontSize > 0 ? FontSize : SystemFonts.MessageFontSize, brush, null,
            FontSize >= 20 ? TextFormattingMode.Ideal : TextFormattingMode.Display, pixelsPerDip); // big glyphs keep their true shapes
}
