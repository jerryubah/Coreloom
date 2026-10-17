using System.Windows;
using System.Windows.Controls;

namespace Coreloom.App.Controls;

/// <summary>
/// Stacks rows and shows as many as fit: it asks for the height of the first <see cref="MinRows"/> rows, and when its
/// card is made taller to line up with the card beside it, it shows more rows instead of leaving the space empty.
/// Rows that don't fit whole are hidden. Collapsed rows don't count.
/// </summary>
public sealed class FitRows : Panel
{
    public static readonly DependencyProperty MinRowsProperty = DependencyProperty.Register(
        nameof(MinRows), typeof(int), typeof(FitRows), new FrameworkPropertyMetadata(6, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>Rows it asks room for (fewer when there are fewer).</summary>
    public int MinRows
    {
        get => (int)GetValue(MinRowsProperty);
        set => SetValue(MinRowsProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = 0, height = 0;
        int counted = 0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
            if (child.Visibility == Visibility.Collapsed)
            {
                continue;
            }

            width = Math.Max(width, child.DesiredSize.Width);
            if (counted < Math.Max(0, MinRows))
            {
                height += child.DesiredSize.Height;
                counted++;
            }
        }

        // Given a limit smaller than the rows it wants (a short card), it asks for what it wants; the card decides.
        return new Size(double.IsInfinity(availableSize.Width) ? width : Math.Min(width, availableSize.Width), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double y = 0;
        bool full = false;
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed)
            {
                child.Arrange(new Rect(0, 0, 0, 0));
                continue;
            }

            double height = child.DesiredSize.Height;
            full |= y + height > finalSize.Height + 0.5;
            if (full)
            {
                child.Arrange(new Rect(0, 0, 0, 0)); // hidden: it doesn't fit whole
                continue;
            }

            child.Arrange(new Rect(0, y, finalSize.Width, height));
            y += height;
        }

        return finalSize;
    }
}
