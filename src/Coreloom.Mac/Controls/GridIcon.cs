using Avalonia;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

namespace Coreloom.Mac.Controls;

/// <summary>
/// An icon from the app's 24-point grid (Icons.axaml), drawn so every icon has the same scale and sits in the middle of
/// its box (since 1.9.4). A Path with <c>Stretch="Uniform"</c> fitted each icon's own outline to the box and pinned it to
/// the top left: a narrow icon such as the thermometer sat left of centre, and every icon came out at its own scale. Here
/// 18 points of the grid fill the box (most icons are drawn 16–18 points across, so they keep about the size they had;
/// the widest, 19, reach a little past it), and the icon's outline is centred, so icons with feet or a bulb look balanced
/// too. The stroke keeps its width. A glyph that's a control of its own (a close button's cross, a chevron, plus, play and
/// pause) sets <see cref="Fit"/> and fills its box instead, centred.
/// </summary>
public class GridIcon : Avalonia.Controls.Shapes.Path
{
    /// <summary>How many points of the 24-point grid an icon's box shows across.</summary>
    public const double DrawnArea = 18;

    /// <summary>Fills the box with the icon's own outline (for glyphs that are a control of their own), still centred.</summary>
    public static readonly StyledProperty<bool> FitProperty = AvaloniaProperty.Register<GridIcon, bool>(nameof(Fit));

    static GridIcon()
    {
        AffectsGeometry<GridIcon>(WidthProperty, HeightProperty, FitProperty);
    }

    public bool Fit
    {
        get => GetValue(FitProperty);
        set => SetValue(FitProperty, value);
    }

    /// <summary>The scale an icon is drawn at in a box of this size (the same for every icon in boxes of that size).</summary>
    public static double ScaleFor(double width, double height) => Math.Min(width, height) / DrawnArea;

    /// <summary>
    /// Where a geometry with these bounds goes in a box: scaled by <see cref="ScaleFor"/> (or, to fit, so its outline fills
    /// the box), its middle in the box's middle.
    /// </summary>
    public static Matrix Placement(Rect bounds, double width, double height, bool fit = false)
    {
        double scale = fit && bounds.Width > 0 && bounds.Height > 0
            ? Math.Min(width / bounds.Width, height / bounds.Height)
            : ScaleFor(width, height);
        return Matrix.CreateTranslation(-bounds.Center.X, -bounds.Center.Y)
            * Matrix.CreateScale(scale, scale)
            * Matrix.CreateTranslation(width / 2, height / 2);
    }

    protected override Geometry? CreateDefiningGeometry()
    {
        if (Data is not { } data)
        {
            return null;
        }

        double width = double.IsFinite(Width) ? Width : 18;
        double height = double.IsFinite(Height) ? Height : width;
        var bounds = data.Bounds;
        if (bounds.Width <= 0 && bounds.Height <= 0)
        {
            return data;
        }

        var placed = data.Clone();
        placed.Transform = new MatrixTransform(Placement(bounds, width, height, Fit));
        return placed;
    }
}
