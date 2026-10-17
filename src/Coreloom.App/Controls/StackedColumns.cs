using System.Windows;
using System.Windows.Controls;
using Coreloom.Core.Dashboard;

namespace Coreloom.App.Controls;

/// <summary>
/// Columns of cards that stack: items of different heights (such as groups of devices) go under whichever column is
/// shortest so far, so they pack tightly instead of each starting a new row. Columns are worked out exactly like
/// <see cref="AdaptiveGrid"/>'s, so with the same widths and spacing the two line up on one grid.
/// </summary>
public sealed class StackedColumns : Panel
{
    public static readonly DependencyProperty MinColumnWidthProperty = DependencyProperty.Register(
        nameof(MinColumnWidth), typeof(double), typeof(StackedColumns),
        new FrameworkPropertyMetadata(240.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty MaxColumnWidthProperty = DependencyProperty.Register(
        nameof(MaxColumnWidth), typeof(double), typeof(StackedColumns),
        new FrameworkPropertyMetadata(double.PositiveInfinity, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty MaxColumnsProperty = DependencyProperty.Register(
        nameof(MaxColumns), typeof(int), typeof(StackedColumns),
        new FrameworkPropertyMetadata(12, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing), typeof(double), typeof(StackedColumns),
        new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty RowSpacingProperty = DependencyProperty.Register(
        nameof(RowSpacing), typeof(double), typeof(StackedColumns),
        new FrameworkPropertyMetadata(20.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty InOrderProperty = DependencyProperty.Register(
        nameof(InOrder), typeof(bool), typeof(StackedColumns),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty FillColumnsProperty = DependencyProperty.Register(
        nameof(FillColumns), typeof(bool), typeof(StackedColumns),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsArrange));

    /// <summary>
    /// Marks a card whose content can use extra height (such as a list that shows more rows when taller). With
    /// <see cref="FillColumns"/>, only such cards grow to line up the column bottoms; every other card keeps its own height.
    /// </summary>
    public static readonly DependencyProperty CanFillProperty = DependencyProperty.RegisterAttached(
        "CanFill", typeof(bool), typeof(StackedColumns),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsParentArrange));

    private IReadOnlyList<StackPlacement> _placements = [];
    private double[] _heights = [];
    private double _totalHeight;
    private int _columns = 1;

    public double MinColumnWidth
    {
        get => (double)GetValue(MinColumnWidthProperty);
        set => SetValue(MinColumnWidthProperty, value);
    }

    /// <summary>Widest a column may get; columns keep this width (or less) and line up from the left.</summary>
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

    /// <summary>Gap between columns.</summary>
    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>Gap between items stacked in a column.</summary>
    public double RowSpacing
    {
        get => (double)GetValue(RowSpacingProperty);
        set => SetValue(RowSpacingProperty, value);
    }

    /// <summary>
    /// Items go into the columns in turn (first, second, first, second…) instead of under the shortest one, so they keep
    /// their places while their heights change.
    /// </summary>
    public bool InOrder
    {
        get => (bool)GetValue(InOrderProperty);
        set => SetValue(InOrderProperty, value);
    }

    public static bool GetCanFill(DependencyObject element) => (bool)element.GetValue(CanFillProperty);

    public static void SetCanFill(DependencyObject element, bool value) => element.SetValue(CanFillProperty, value);

    /// <summary>
    /// The last item in each column grows so all columns end at the same height, when it is marked <c>CanFill</c> (its
    /// content can use the room); cards that can't keep their own height.
    /// </summary>
    public bool FillColumns
    {
        get => (bool)GetValue(FillColumnsProperty);
        set => SetValue(FillColumnsProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var visible = Visible();
        double width = double.IsInfinity(availableSize.Width)
            ? (MinColumnWidth * Math.Max(1, Math.Min(MaxColumns, visible.Count))) + (Spacing * Math.Max(0, Math.Min(MaxColumns, visible.Count) - 1))
            : availableSize.Width;

        var (columns, columnWidth) = CardColumns.Fit(width, Spacing, MinColumnWidth, MaxColumnWidth, MaxColumns, visible.Count);
        _columns = columns;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(columnWidth, double.PositiveInfinity));
        }

        _heights = visible.Select(c => c.DesiredSize.Height).ToArray();
        _placements = CardColumns.Stack(_heights, columns, RowSpacing, out double height, InOrder);
        _totalHeight = height;
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double columnWidth = CardColumns.ColumnWidth(finalSize.Width, Spacing, _columns, MaxColumnWidth);
        var heights = FillColumns ? CardColumns.FillColumns(_placements, _heights, Math.Max(_totalHeight, finalSize.Height)) : _heights;
        int index = 0;
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed || index >= _placements.Count)
            {
                child.Arrange(new Rect(0, 0, 0, 0));
                continue;
            }

            var place = _placements[index];
            double height = index < heights.Length && (GetCanFill(child) || !FillColumns) ? heights[index] : child.DesiredSize.Height;
            index++;
            child.Arrange(new Rect(place.Column * (columnWidth + Spacing), place.Top, columnWidth, height));
        }

        return finalSize;
    }

    private List<UIElement> Visible() => InternalChildren.Cast<UIElement>().Where(c => c.Visibility != Visibility.Collapsed).ToList();
}
