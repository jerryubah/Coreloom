using System.Windows;
using System.Windows.Controls;
using Coreloom.Core.Dashboard;

namespace Coreloom.App.Controls;

/// <summary>
/// Responsive grid: fits as many equal-width columns as possible with each at least
/// <see cref="MinColumnWidth"/> wide (capped at <see cref="MaxColumns"/>), and stretches items to fill
/// the row. Row height is the tallest item in the row (with <see cref="StretchRows"/>, rows also share any extra height
/// the grid is given, so a tile grid can fill a card next to it). With <see cref="MaxColumnWidth"/>, items stay compact:
/// columns never grow past it, so cards keep the same width however many there are and wrap onto new rows. Used for all card grids.
/// </summary>
public sealed class AdaptiveGrid : Panel
{
    public static readonly DependencyProperty MinColumnWidthProperty = DependencyProperty.Register(
        nameof(MinColumnWidth), typeof(double), typeof(AdaptiveGrid),
        new FrameworkPropertyMetadata(240.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty MaxColumnsProperty = DependencyProperty.Register(
        nameof(MaxColumns), typeof(int), typeof(AdaptiveGrid),
        new FrameworkPropertyMetadata(6, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing), typeof(double), typeof(AdaptiveGrid),
        new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty RowSpacingProperty = DependencyProperty.Register(
        nameof(RowSpacing), typeof(double), typeof(AdaptiveGrid),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty UniformRowHeightProperty = DependencyProperty.Register(
        nameof(UniformRowHeight), typeof(bool), typeof(AdaptiveGrid),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty StretchRowsProperty = DependencyProperty.Register(
        nameof(StretchRows), typeof(bool), typeof(AdaptiveGrid),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsArrange));

    public static readonly DependencyProperty MaxColumnWidthProperty = DependencyProperty.Register(
        nameof(MaxColumnWidth), typeof(double), typeof(AdaptiveGrid),
        new FrameworkPropertyMetadata(double.PositiveInfinity, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty FillHeightProperty = DependencyProperty.Register(
        nameof(FillHeight), typeof(bool), typeof(AdaptiveGrid),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty FillLastRowProperty = DependencyProperty.Register(
        nameof(FillLastRow), typeof(bool), typeof(AdaptiveGrid),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsMeasure));

    private double[] _rowHeights = [];
    private int _columns = 1;
    private double _columnWidth;

    public double MinColumnWidth
    {
        get => (double)GetValue(MinColumnWidthProperty);
        set => SetValue(MinColumnWidthProperty, value);
    }

    /// <summary>
    /// Widest a column may get (no limit by default). When set, the grid picks the column count that fills the row with
    /// columns between <see cref="MinColumnWidth"/> and this width, keeps that count even when there are fewer items,
    /// and lines items up from the left when the row can't be filled.
    /// </summary>
    public double MaxColumnWidth
    {
        get => (double)GetValue(MaxColumnWidthProperty);
        set => SetValue(MaxColumnWidthProperty, value);
    }

    public int MaxColumns
    {
        get => (int)GetValue(MaxColumnsProperty);
        set => SetValue(MaxColumnsProperty, value);
    }

    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>Gap between rows; when not set, <see cref="Spacing"/> is used both ways.</summary>
    public double RowSpacing
    {
        get => (double)GetValue(RowSpacingProperty);
        set => SetValue(RowSpacingProperty, value);
    }

    private double EffectiveRowSpacing => double.IsNaN(RowSpacing) ? Spacing : RowSpacing;

    /// <summary>When true every row uses the height of the tallest item overall.</summary>
    public bool UniformRowHeight
    {
        get => (bool)GetValue(UniformRowHeightProperty);
        set => SetValue(UniformRowHeightProperty, value);
    }

    /// <summary>When the grid is arranged taller than it asked for, the rows grow equally to fill the height.</summary>
    public bool StretchRows
    {
        get => (bool)GetValue(StretchRowsProperty);
        set => SetValue(StretchRowsProperty, value);
    }

    /// <summary>
    /// Items in a last row that isn't full share its whole width, so the grid ends on one straight line (nine cards in
    /// two columns: the ninth spans both).
    /// </summary>
    public bool FillLastRow
    {
        get => (bool)GetValue(FillLastRowProperty);
        set => SetValue(FillLastRowProperty, value);
    }

    /// <summary>
    /// Given more height than its items need (a card stretched beside a taller one), use fewer columns, down to one, as
    /// long as they still fit: the items spread out to fill the height instead of leaving it empty.
    /// </summary>
    public bool FillHeight
    {
        get => (bool)GetValue(FillHeightProperty);
        set => SetValue(FillHeightProperty, value);
    }

    /// <summary>The first item of an unfull last row (when it fills its row), else the item count.</summary>
    private static int LastRowStart(int count, int columns, bool fill) =>
        fill && columns > 1 && count % columns != 0 ? count - (count % columns) : count;

    /// <summary>Width of each item of an unfull last row that fills the row.</summary>
    private static double LastRowWidth(int count, int columns, double columnWidth, double spacing)
    {
        int items = count % columns;
        double row = (columns * columnWidth) + ((columns - 1) * spacing);
        return items == 0 ? columnWidth : (row - ((items - 1) * spacing)) / items;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var visible = InternalChildren.Cast<UIElement>().Where(c => c.Visibility != Visibility.Collapsed).ToList();
        double width = double.IsInfinity(availableSize.Width) ? (MinColumnWidth * Math.Max(1, Math.Min(MaxColumns, visible.Count))) + (Spacing * Math.Max(0, Math.Min(MaxColumns, visible.Count) - 1)) : availableSize.Width;
        double spacing = Spacing;

        var (columns, columnWidth) = CardColumns.Fit(width, spacing, MinColumnWidth, MaxColumnWidth, MaxColumns, visible.Count);
        double height = MeasureAt(visible, columns, columnWidth, spacing);
        if (FillHeight && double.IsFinite(availableSize.Height) && height < availableSize.Height && columns > 1)
        {
            int best = columns;
            for (int c = columns - 1; c >= 1; c--)
            {
                if (MeasureAt(visible, c, CardColumns.ColumnWidth(width, spacing, c, MaxColumnWidth), spacing) > availableSize.Height)
                {
                    break;
                }

                best = c;
            }

            columnWidth = CardColumns.ColumnWidth(width, spacing, best, MaxColumnWidth);
            height = MeasureAt(visible, best, columnWidth, spacing);
        }

        return new Size(width, height);
    }

    /// <summary>Measures the items in <paramref name="columns"/> columns; returns the height they take.</summary>
    private double MeasureAt(List<UIElement> visible, int columns, double columnWidth, double spacing)
    {
        _columns = columns;
        _columnWidth = columnWidth;
        int lastRow = LastRowStart(visible.Count, columns, FillLastRow);
        double lastWidth = LastRowWidth(visible.Count, columns, _columnWidth, spacing);
        int visibleIndex = 0;
        foreach (UIElement child in InternalChildren)
        {
            bool wide = child.Visibility != Visibility.Collapsed && visibleIndex++ >= lastRow;
            child.Measure(new Size(wide ? lastWidth : _columnWidth, double.PositiveInfinity));
        }

        int rows = visible.Count == 0 ? 0 : (int)Math.Ceiling(visible.Count / (double)columns);
        _rowHeights = new double[rows];
        for (int i = 0; i < visible.Count; i++)
        {
            int row = i / columns;
            _rowHeights[row] = Math.Max(_rowHeights[row], visible[i].DesiredSize.Height);
        }

        if (UniformRowHeight && rows > 0)
        {
            double max = _rowHeights.Max();
            Array.Fill(_rowHeights, max);
        }

        return _rowHeights.Sum() + (EffectiveRowSpacing * Math.Max(0, rows - 1));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double spacing = Spacing;
        double rowSpacing = EffectiveRowSpacing;
        double columnWidth = CardColumns.ColumnWidth(finalSize.Width, spacing, _columns, MaxColumnWidth);
        double extra = 0;
        if (StretchRows && _rowHeights.Length > 0)
        {
            double used = _rowHeights.Sum() + (rowSpacing * (_rowHeights.Length - 1));
            extra = Math.Max(0, (finalSize.Height - used) / _rowHeights.Length);
        }

        double y = 0;
        int index = 0;
        int count = InternalChildren.Cast<UIElement>().Count(c => c.Visibility != Visibility.Collapsed);
        int lastRow = LastRowStart(count, _columns, FillLastRow);
        double lastWidth = LastRowWidth(count, _columns, columnWidth, spacing);
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed)
            {
                child.Arrange(new Rect(0, 0, 0, 0));
                continue;
            }

            int row = index / _columns;
            int column = index % _columns;
            if (column == 0 && row > 0)
            {
                y += _rowHeights[row - 1] + extra + rowSpacing;
            }

            double height = row < _rowHeights.Length ? _rowHeights[row] + extra : child.DesiredSize.Height;
            if (index >= lastRow)
            {
                child.Arrange(new Rect(column * (lastWidth + spacing), y, lastWidth, height));
            }
            else
            {
                child.Arrange(new Rect(column * (columnWidth + spacing), y, columnWidth, height));
            }

            index++;
        }

        return finalSize;
    }
}
