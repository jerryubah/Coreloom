using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Coreloom.Core.Dashboard;

namespace Coreloom.Mac.Controls;

/// <summary>
/// Responsive grid: fits as many equal-width columns as possible with each at least <see cref="MinColumnWidth"/> wide
/// (capped at <see cref="MaxColumns"/>), and stretches items to fill the row. Row height is the tallest item in the row
/// (with <see cref="StretchRows"/>, rows also share any extra height the grid is given). With
/// <see cref="MaxColumnWidth"/>, columns never grow past it. Used for all card grids.
/// </summary>
public sealed class AdaptiveGrid : Panel
{
    public static readonly StyledProperty<double> MinColumnWidthProperty = AvaloniaProperty.Register<AdaptiveGrid, double>(nameof(MinColumnWidth), 240.0);

    public static readonly StyledProperty<int> MaxColumnsProperty = AvaloniaProperty.Register<AdaptiveGrid, int>(nameof(MaxColumns), 6);

    public static readonly StyledProperty<double> SpacingProperty = AvaloniaProperty.Register<AdaptiveGrid, double>(nameof(Spacing), 16.0);

    public static readonly StyledProperty<double> RowSpacingProperty = AvaloniaProperty.Register<AdaptiveGrid, double>(nameof(RowSpacing), double.NaN);

    public static readonly StyledProperty<bool> UniformRowHeightProperty = AvaloniaProperty.Register<AdaptiveGrid, bool>(nameof(UniformRowHeight));

    public static readonly StyledProperty<bool> StretchRowsProperty = AvaloniaProperty.Register<AdaptiveGrid, bool>(nameof(StretchRows));

    public static readonly StyledProperty<double> MaxColumnWidthProperty = AvaloniaProperty.Register<AdaptiveGrid, double>(nameof(MaxColumnWidth), double.PositiveInfinity);

    public static readonly StyledProperty<bool> FillLastRowProperty = AvaloniaProperty.Register<AdaptiveGrid, bool>(nameof(FillLastRow));

    private double[] _rowHeights = [];
    private int _columns = 1;
    private double _columnWidth;

    static AdaptiveGrid()
    {
        AffectsMeasure<AdaptiveGrid>(MinColumnWidthProperty, MaxColumnsProperty, SpacingProperty, RowSpacingProperty, UniformRowHeightProperty, MaxColumnWidthProperty, FillLastRowProperty);
        AffectsArrange<AdaptiveGrid>(StretchRowsProperty);
    }

    public double MinColumnWidth
    {
        get => GetValue(MinColumnWidthProperty);
        set => SetValue(MinColumnWidthProperty, value);
    }

    public double MaxColumnWidth
    {
        get => GetValue(MaxColumnWidthProperty);
        set => SetValue(MaxColumnWidthProperty, value);
    }

    public int MaxColumns
    {
        get => GetValue(MaxColumnsProperty);
        set => SetValue(MaxColumnsProperty, value);
    }

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>Gap between rows; when not set, <see cref="Spacing"/> is used both ways.</summary>
    public double RowSpacing
    {
        get => GetValue(RowSpacingProperty);
        set => SetValue(RowSpacingProperty, value);
    }

    private double EffectiveRowSpacing => double.IsNaN(RowSpacing) ? Spacing : RowSpacing;

    public bool UniformRowHeight
    {
        get => GetValue(UniformRowHeightProperty);
        set => SetValue(UniformRowHeightProperty, value);
    }

    public bool StretchRows
    {
        get => GetValue(StretchRowsProperty);
        set => SetValue(StretchRowsProperty, value);
    }

    /// <summary>Items in a last row that isn't full share its whole width, so the grid ends on one straight line.</summary>
    public bool FillLastRow
    {
        get => GetValue(FillLastRowProperty);
        set => SetValue(FillLastRowProperty, value);
    }

    private static int LastRowStart(int count, int columns, bool fill) =>
        fill && columns > 1 && count % columns != 0 ? count - (count % columns) : count;

    private static double LastRowWidth(int count, int columns, double columnWidth, double spacing)
    {
        int items = count % columns;
        double row = (columns * columnWidth) + ((columns - 1) * spacing);
        return items == 0 ? columnWidth : (row - ((items - 1) * spacing)) / items;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var visible = Children.Where(c => c.IsVisible).ToList();
        double width = double.IsInfinity(availableSize.Width) ? (MinColumnWidth * Math.Max(1, Math.Min(MaxColumns, visible.Count))) + (Spacing * Math.Max(0, Math.Min(MaxColumns, visible.Count) - 1)) : availableSize.Width;
        double spacing = Spacing;

        var (columns, columnWidth) = CardColumns.Fit(width, spacing, MinColumnWidth, MaxColumnWidth, MaxColumns, visible.Count);
        _columns = columns;
        _columnWidth = columnWidth;

        int lastRow = LastRowStart(visible.Count, columns, FillLastRow);
        double lastWidth = LastRowWidth(visible.Count, columns, _columnWidth, spacing);
        int visibleIndex = 0;
        foreach (var child in Children)
        {
            bool wide = child.IsVisible && visibleIndex++ >= lastRow;
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

        double height = _rowHeights.Sum() + (EffectiveRowSpacing * Math.Max(0, rows - 1));
        return new Size(double.IsInfinity(availableSize.Width) ? width : Math.Min(width, availableSize.Width), height);
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
        int count = Children.Count(c => c.IsVisible);
        int lastRow = LastRowStart(count, _columns, FillLastRow);
        double lastWidth = LastRowWidth(count, _columns, columnWidth, spacing);
        foreach (var child in Children)
        {
            if (!child.IsVisible)
            {
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

/// <summary>
/// Columns of cards that stack: items of different heights go under whichever column is shortest so far, so they pack
/// tightly. Columns are worked out exactly like <see cref="AdaptiveGrid"/>'s, so the two line up on one grid.
/// </summary>
public sealed class StackedColumns : Panel
{
    public static readonly StyledProperty<double> MinColumnWidthProperty = AvaloniaProperty.Register<StackedColumns, double>(nameof(MinColumnWidth), 240.0);

    public static readonly StyledProperty<double> MaxColumnWidthProperty = AvaloniaProperty.Register<StackedColumns, double>(nameof(MaxColumnWidth), double.PositiveInfinity);

    public static readonly StyledProperty<int> MaxColumnsProperty = AvaloniaProperty.Register<StackedColumns, int>(nameof(MaxColumns), 12);

    public static readonly StyledProperty<double> SpacingProperty = AvaloniaProperty.Register<StackedColumns, double>(nameof(Spacing), 16.0);

    public static readonly StyledProperty<double> RowSpacingProperty = AvaloniaProperty.Register<StackedColumns, double>(nameof(RowSpacing), 20.0);

    public static readonly StyledProperty<bool> InOrderProperty = AvaloniaProperty.Register<StackedColumns, bool>(nameof(InOrder));

    public static readonly StyledProperty<bool> FillColumnsProperty = AvaloniaProperty.Register<StackedColumns, bool>(nameof(FillColumns));

    /// <summary>Marks a card whose content can use extra height (a list that shows more rows when taller).</summary>
    public static readonly AttachedProperty<bool> CanFillProperty = AvaloniaProperty.RegisterAttached<StackedColumns, Control, bool>("CanFill");

    private IReadOnlyList<StackPlacement> _placements = [];
    private double[] _heights = [];
    private double _totalHeight;
    private int _columns = 1;

    static StackedColumns()
    {
        AffectsMeasure<StackedColumns>(MinColumnWidthProperty, MaxColumnWidthProperty, MaxColumnsProperty, SpacingProperty, RowSpacingProperty, InOrderProperty);
        AffectsArrange<StackedColumns>(FillColumnsProperty);
        AffectsParentArrange<StackedColumns>(CanFillProperty);
    }

    public double MinColumnWidth
    {
        get => GetValue(MinColumnWidthProperty);
        set => SetValue(MinColumnWidthProperty, value);
    }

    public double MaxColumnWidth
    {
        get => GetValue(MaxColumnWidthProperty);
        set => SetValue(MaxColumnWidthProperty, value);
    }

    public int MaxColumns
    {
        get => GetValue(MaxColumnsProperty);
        set => SetValue(MaxColumnsProperty, value);
    }

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    public double RowSpacing
    {
        get => GetValue(RowSpacingProperty);
        set => SetValue(RowSpacingProperty, value);
    }

    public bool InOrder
    {
        get => GetValue(InOrderProperty);
        set => SetValue(InOrderProperty, value);
    }

    public bool FillColumns
    {
        get => GetValue(FillColumnsProperty);
        set => SetValue(FillColumnsProperty, value);
    }

    public static bool GetCanFill(Control element) => element.GetValue(CanFillProperty);

    public static void SetCanFill(Control element, bool value) => element.SetValue(CanFillProperty, value);

    protected override Size MeasureOverride(Size availableSize)
    {
        var visible = Children.Where(c => c.IsVisible).ToList();
        double width = double.IsInfinity(availableSize.Width)
            ? (MinColumnWidth * Math.Max(1, Math.Min(MaxColumns, visible.Count))) + (Spacing * Math.Max(0, Math.Min(MaxColumns, visible.Count) - 1))
            : availableSize.Width;

        var (columns, columnWidth) = CardColumns.Fit(width, Spacing, MinColumnWidth, MaxColumnWidth, MaxColumns, visible.Count);
        _columns = columns;
        foreach (var child in Children)
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
        foreach (var child in Children)
        {
            if (!child.IsVisible || index >= _placements.Count)
            {
                continue;
            }

            var place = _placements[index];
            double height = index < heights.Length && (GetCanFill(child) || !FillColumns) ? heights[index] : child.DesiredSize.Height;
            index++;
            child.Arrange(new Rect(place.Column * (columnWidth + Spacing), place.Top, columnWidth, height));
        }

        return finalSize;
    }
}

/// <summary>
/// Stacks rows and shows as many as fit: it asks for the height of the first <see cref="MinRows"/> rows, and when its
/// card is made taller it shows more rows instead of leaving the space empty. Rows that don't fit whole are hidden.
/// </summary>
public sealed class FitRows : Panel
{
    public static readonly StyledProperty<int> MinRowsProperty = AvaloniaProperty.Register<FitRows, int>(nameof(MinRows), 6);

    static FitRows() => AffectsMeasure<FitRows>(MinRowsProperty);

    public FitRows() => ClipToBounds = true; // rows that don't fit are moved out of sight

    public int MinRows
    {
        get => GetValue(MinRowsProperty);
        set => SetValue(MinRowsProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = 0, height = 0;
        int counted = 0;
        foreach (var child in Children)
        {
            child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
            if (!child.IsVisible)
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

        return new Size(double.IsInfinity(availableSize.Width) ? width : Math.Min(width, availableSize.Width), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double y = 0;
        bool full = false;
        foreach (var child in Children)
        {
            if (!child.IsVisible)
            {
                continue;
            }

            double height = child.DesiredSize.Height;
            full |= y + height > finalSize.Height + 0.5;
            if (full)
            {
                child.Arrange(new Rect(0, -10000, finalSize.Width, height)); // hidden: it doesn't fit whole
                continue;
            }

            child.Arrange(new Rect(0, y, finalSize.Width, height));
            y += height;
        }

        return finalSize;
    }
}

/// <summary>
/// Asks for at most <see cref="Cap"/> height when its container has no limit, but fills whatever height it is given.
/// </summary>
public sealed class HeightCap : Decorator
{
    public static readonly StyledProperty<double> CapProperty = AvaloniaProperty.Register<HeightCap, double>(nameof(Cap), 250.0);

    static HeightCap() => AffectsMeasure<HeightCap>(CapProperty);

    public double Cap
    {
        get => GetValue(CapProperty);
        set => SetValue(CapProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Child is null)
        {
            return default;
        }

        double height = double.IsPositiveInfinity(availableSize.Height) ? Cap : availableSize.Height;
        Child.Measure(new Size(availableSize.Width, height));
        return Child.DesiredSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Child?.Arrange(new Rect(finalSize));
        return finalSize;
    }
}

/// <summary>
/// Hosts the current page view and keeps one view per page view model, so navigating back to a page is instant (no
/// re-creation of dozens of charts). Views come from the data template registered for the view model's type.
/// </summary>
public sealed class PageHost : ContentControl
{
    public static readonly StyledProperty<object?> PageProperty = AvaloniaProperty.Register<PageHost, object?>(nameof(Page));

    private readonly Dictionary<object, Control> _views = new(ReferenceEqualityComparer.Instance);

    public PageHost()
    {
        Focusable = false;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
    }

    public object? Page
    {
        get => GetValue(PageProperty);
        set => SetValue(PageProperty, value);
    }

    /// <summary>The view made for a page, when it has been shown.</summary>
    public Control? ViewFor(object page) => _views.TryGetValue(page, out var view) ? view : null;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != PageProperty)
        {
            return;
        }

        object? page = change.NewValue;
        if (page is null)
        {
            Content = null;
            return;
        }

        if (!_views.TryGetValue(page, out var view))
        {
            var template = this.FindDataTemplate(page);
            view = template?.Build(page);
            if (view is null)
            {
                Content = page;
                return;
            }

            view.DataContext = page;
            _views[page] = view;
        }

        Content = view;
    }
}
