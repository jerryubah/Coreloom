using System.Collections.Specialized;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Coreloom.Core.Dashboard;
using Coreloom.Core.Settings;
using Coreloom.Mac.Behaviors;
using Coreloom.Mac.Themes;
using Coreloom.ViewModels.Pages;

namespace Coreloom.Mac.Controls;

/// <summary>Marks the parts of a dashboard card that start a drag: the grip (move) and the corner (resize).</summary>
public enum DashboardHandle
{
    None,
    Move,
    Resize,
}

/// <summary>
/// The Overview's card grid. Cards cover whole cells of a grid of equal columns (6, 4, 2 or 1, by width) and short rows,
/// in the order and sizes the user set (see <see cref="DashboardLayout"/>).
/// <para>
/// Drag a card to move it: the others make room as it goes, and on release it snaps into the place it is nearest to.
/// Drag its bottom-right corner to resize it in whole cells. Esc cancels either. Cards glide to their new places unless
/// reduced motion is on. The result goes to <see cref="MoveCommand"/> or <see cref="ResizeCommand"/>, which save it.
/// </para>
/// </summary>
public sealed class DashboardGrid : Panel
{
    public static readonly StyledProperty<double> MinColumnWidthProperty = AvaloniaProperty.Register<DashboardGrid, double>(nameof(MinColumnWidth), 232.0);

    public static readonly StyledProperty<double> SpacingProperty = AvaloniaProperty.Register<DashboardGrid, double>(nameof(Spacing), 16.0);

    public static readonly StyledProperty<double> MinRowHeightProperty = AvaloniaProperty.Register<DashboardGrid, double>(nameof(MinRowHeight), 40.0);

    public static readonly StyledProperty<ICommand?> MoveCommandProperty = AvaloniaProperty.Register<DashboardGrid, ICommand?>(nameof(MoveCommand));

    public static readonly StyledProperty<ICommand?> ResizeCommandProperty = AvaloniaProperty.Register<DashboardGrid, ICommand?>(nameof(ResizeCommand));

    public static readonly AttachedProperty<int> OrderProperty = AvaloniaProperty.RegisterAttached<DashboardGrid, Control, int>("Order");

    public static readonly AttachedProperty<int> ColumnSpanProperty = AvaloniaProperty.RegisterAttached<DashboardGrid, Control, int>("ColumnSpan", 1);

    public static readonly AttachedProperty<int> RowSpanProperty = AvaloniaProperty.RegisterAttached<DashboardGrid, Control, int>("RowSpan", 2);

    public static readonly AttachedProperty<int> MinColumnsProperty = AvaloniaProperty.RegisterAttached<DashboardGrid, Control, int>("MinColumns", 1);

    public static readonly AttachedProperty<int> MinRowsProperty = AvaloniaProperty.RegisterAttached<DashboardGrid, Control, int>("MinRows", 1);

    public static readonly AttachedProperty<DashboardHandle> HandleProperty = AvaloniaProperty.RegisterAttached<DashboardGrid, Control, DashboardHandle>("Handle");

    private static readonly TimeSpan GlideDuration = TimeSpan.FromMilliseconds(220);
    private const double AutoScrollEdge = 56;
    private const double AutoScrollSpeed = 16;
    private const double DragThreshold = 4;

    private readonly Dictionary<Control, Rect> _slots = [];
    private readonly Dictionary<Control, Animator.Tween> _glides = [];
    private List<Control> _ordered = [];
    private TilePlacement[] _placements = [];
    private int _columns = 1;
    private double _columnWidth;
    private double _rowHeight = 40;
    private bool _animate;

    private Mode _mode;
    private Control? _active;
    private DashboardHandle _pressedHandle;
    private Point _pressPoint;
    private Point _lastPoint;
    private Rect _startSlot;
    private Point _floating;
    private int _startIndex = -1;
    private int _previewIndex = -1;
    private TileSize _startSize;
    private TileSize? _previewSize;
    private Rect? _outline;
    private IPointer? _pointer;
    private ScrollViewer? _scroller;
    private TopLevel? _topLevel;
    private DispatcherTimer? _autoScroll;
    private double _autoScrollStep;

    static DashboardGrid()
    {
        AffectsMeasure<DashboardGrid>(MinColumnWidthProperty, SpacingProperty, MinRowHeightProperty);
        foreach (var property in new[] { OrderProperty, ColumnSpanProperty, RowSpanProperty, MinColumnsProperty, MinRowsProperty })
        {
            property.Changed.AddClassHandler<Control>(OnCardLayoutChanged);
        }
    }

    public DashboardGrid()
    {
        AddHandler(PointerPressedEvent, OnPreviewPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnPreviewPointerMoved, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnPreviewPointerReleased, RoutingStrategies.Tunnel);
        AddHandler(PointerCaptureLostEvent, (_, _) =>
        {
            if (_mode is Mode.Moving or Mode.Resizing)
            {
                End(commit: false); // e.g. another window took the pointer
            }
        });
    }

    private enum Mode
    {
        None,
        Pressed,
        Moving,
        Resizing,
    }

    public double MinColumnWidth
    {
        get => GetValue(MinColumnWidthProperty);
        set => SetValue(MinColumnWidthProperty, value);
    }

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    public double MinRowHeight
    {
        get => GetValue(MinRowHeightProperty);
        set => SetValue(MinRowHeightProperty, value);
    }

    /// <summary>Runs with a <see cref="DashboardTileMove"/> when a card is dropped in a new place.</summary>
    public ICommand? MoveCommand
    {
        get => GetValue(MoveCommandProperty);
        set => SetValue(MoveCommandProperty, value);
    }

    /// <summary>Runs with a <see cref="DashboardTileResize"/> when a card is resized.</summary>
    public ICommand? ResizeCommand
    {
        get => GetValue(ResizeCommandProperty);
        set => SetValue(ResizeCommandProperty, value);
    }

    public static int GetOrder(Control d) => d.GetValue(OrderProperty);

    public static void SetOrder(Control d, int value) => d.SetValue(OrderProperty, value);

    public static int GetColumnSpan(Control d) => d.GetValue(ColumnSpanProperty);

    public static void SetColumnSpan(Control d, int value) => d.SetValue(ColumnSpanProperty, value);

    public static int GetRowSpan(Control d) => d.GetValue(RowSpanProperty);

    public static void SetRowSpan(Control d, int value) => d.SetValue(RowSpanProperty, value);

    public static int GetMinColumns(Control d) => d.GetValue(MinColumnsProperty);

    public static void SetMinColumns(Control d, int value) => d.SetValue(MinColumnsProperty, value);

    public static int GetMinRows(Control d) => d.GetValue(MinRowsProperty);

    public static void SetMinRows(Control d, int value) => d.SetValue(MinRowsProperty, value);

    public static DashboardHandle GetHandle(Control d) => d.GetValue(HandleProperty);

    public static void SetHandle(Control d, DashboardHandle value) => d.SetValue(HandleProperty, value);

    // ------------------------------------------------------------------------------------------------
    // Layout
    // ------------------------------------------------------------------------------------------------

    protected override Size MeasureOverride(Size availableSize)
    {
        double spacing = Spacing;
        double width = double.IsInfinity(availableSize.Width)
            ? DashboardLayout.Extent(DashboardCatalog.MaxColumns, MinColumnWidth, spacing)
            : availableSize.Width;
        _columns = DashboardLayout.ColumnsFor(width, MinColumnWidth, spacing, DashboardCatalog.MaxColumns);
        _columnWidth = Math.Max(0, (width - (spacing * (_columns - 1))) / _columns);

        _ordered = Ordered();
        var sizes = Sizes(_ordered);

        // First what each card needs at its width, which sets the row height; then each at the size it will get.
        var needs = new (double Height, int MinRows)[_ordered.Count];
        for (int i = 0; i < _ordered.Count; i++)
        {
            var child = _ordered[i];
            child.Measure(new Size(DashboardLayout.Extent(Math.Min(sizes[i].Columns, _columns), _columnWidth, spacing), double.PositiveInfinity));
            needs[i] = (child.DesiredSize.Height, Math.Max(1, GetMinRows(child)));
        }

        _rowHeight = DashboardLayout.RowHeight(needs, spacing, MinRowHeight);
        _placements = DashboardLayout.Place(sizes, _columns);
        for (int i = 0; i < _ordered.Count; i++)
        {
            var p = _placements[i];
            _ordered[i].Measure(new Size(DashboardLayout.Extent(p.Columns, _columnWidth, spacing), DashboardLayout.Extent(p.Rows, _rowHeight, spacing)));
        }

        return new Size(width, DashboardLayout.Extent(DashboardLayout.RowCount(_placements), _rowHeight, spacing));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double spacing = Spacing;
        double columnWidth = Math.Max(0, (finalSize.Width - (spacing * (_columns - 1))) / _columns);
        _columnWidth = columnWidth;
        bool glide = _animate && !Motion.ReducedMotion;
        for (int i = 0; i < _ordered.Count && i < _placements.Length; i++)
        {
            var child = _ordered[i];
            var p = _placements[i];
            var slot = new Rect(
                p.Column * (columnWidth + spacing),
                p.Row * (_rowHeight + spacing),
                DashboardLayout.Extent(p.Columns, columnWidth, spacing),
                DashboardLayout.Extent(p.Rows, _rowHeight, spacing));
            child.Arrange(slot);

            var shift = Shift(child);
            if (child == _active && _mode == Mode.Moving)
            {
                Hold(child, shift, _floating.X - slot.X, _floating.Y - slot.Y);
            }
            else if (glide && _slots.TryGetValue(child, out var old) && old.Position != slot.Position)
            {
                Glide(child, shift, old.X + shift.X - slot.X, old.Y + shift.Y - slot.Y);
            }

            _slots[child] = slot;
        }

        _animate = false;
        if (_mode is Mode.Moving or Mode.Resizing)
        {
            InvalidateVisual(); // the drop outline follows the card's place
        }

        return finalSize;
    }

    /// <summary>The outline of where a dragged card will land, or of the size a card is being dragged to (drawn above the cards).</summary>
    private Rect? CurrentOutline() => _mode switch
    {
        Mode.Moving when _active is not null && _slots.TryGetValue(_active, out var slot) => slot,
        Mode.Resizing => _outline,
        _ => null,
    };

    private OutlineAdorner? _adorner;

    private new void InvalidateVisual()
    {
        base.InvalidateVisual();
        if (_mode is Mode.Moving or Mode.Resizing)
        {
            if (_adorner is null && AdornerLayer.GetAdornerLayer(this) is { } layer)
            {
                _adorner = new OutlineAdorner(this);
                AdornerLayer.SetAdornedElement(_adorner, this);
                layer.Children.Add(_adorner);
            }

            _adorner?.InvalidateVisual();
        }
        else if (_adorner is not null)
        {
            (_adorner.Parent as AdornerLayer ?? _adorner.GetVisualParent() as AdornerLayer)?.Children.Remove(_adorner);
            _adorner = null;
        }
    }

    private sealed class OutlineAdorner(DashboardGrid grid) : Control
    {
        public override void Render(DrawingContext dc)
        {
            if (grid.CurrentOutline() is not { } rect || rect.Width <= 0 || rect.Height <= 0)
            {
                return;
            }

            var accent = ThemeService.Instance?.AccentColor ?? Color.FromRgb(0x4C, 0x8D, 0xFF);
            var fill = new ImmutableSolidColorBrush(Color.FromArgb(grid._mode == Mode.Moving ? (byte)28 : (byte)14, accent.R, accent.G, accent.B));
            var pen = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(200, accent.R, accent.G, accent.B)), 1.5, new ImmutableDashStyle([4, 3], 0), PenLineCap.Round);
            dc.DrawRectangle(fill, pen, rect.Deflate(0.75), 14, 14);
        }
    }

    protected override void ChildrenChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        base.ChildrenChanged(sender, e);
        if (e.OldItems is { } removed)
        {
            foreach (var item in removed.OfType<Control>())
            {
                _slots.Remove(item);
                if (item == _active)
                {
                    End(commit: false);
                }
            }
        }

        _animate = _slots.Count > 0; // the others glide to make room or close the gap
    }

    private static void OnCardLayoutChanged(Control child, AvaloniaPropertyChangedEventArgs e)
    {
        if (child.GetVisualParent() is DashboardGrid grid)
        {
            grid._animate = grid._slots.Count > 0;
            grid.InvalidateMeasure();
        }
    }

    /// <summary>Visible cards by their order; while a card is dragged (and <paramref name="preview"/>), with it moved to where it would land.</summary>
    private List<Control> Ordered(bool preview = true)
    {
        var cards = Children
            .Where(c => c.IsVisible)
            .Select((c, i) => (Card: c, Index: i))
            .OrderBy(c => GetOrder(c.Card))
            .ThenBy(c => c.Index)
            .Select(c => c.Card)
            .ToList();
        if (preview && _mode == Mode.Moving && _active is not null && _previewIndex >= 0)
        {
            int from = cards.IndexOf(_active);
            if (from >= 0)
            {
                cards = DashboardLayout.Move(cards, from, _previewIndex);
            }
        }

        return cards;
    }

    private TileSize[] Sizes(IReadOnlyList<Control> cards) =>
        cards.Select(c => c == _active && _mode == Mode.Resizing && _previewSize is { } preview ? preview : SizeOf(c)).ToArray();

    private static TileSize SizeOf(Control card) =>
        new(Math.Max(GetColumnSpan(card), Math.Max(1, GetMinColumns(card))), Math.Max(GetRowSpan(card), Math.Max(1, GetMinRows(card))));

    private static TranslateTransform Shift(Control child)
    {
        if (child.RenderTransform is TranslateTransform shift)
        {
            return shift;
        }

        shift = new TranslateTransform();
        child.RenderTransform = shift;
        return shift;
    }

    /// <summary>Puts a card where it is shown right now.</summary>
    private void Hold(Control child, TranslateTransform shift, double x, double y)
    {
        if (_glides.Remove(child, out var tween))
        {
            tween.Cancel();
        }

        shift.X = x;
        shift.Y = y;
    }

    /// <summary>Glides a card from an offset to its place.</summary>
    private void Glide(Control child, TranslateTransform shift, double fromX, double fromY)
    {
        if (_glides.Remove(child, out var running))
        {
            running.Cancel();
        }

        shift.X = 0;
        shift.Y = 0;
        if (Motion.ReducedMotion || (Math.Abs(fromX) < 0.5 && Math.Abs(fromY) < 0.5))
        {
            return;
        }

        _glides[child] = Animator.Run(GlideDuration, Ease.CubicOut, t =>
        {
            shift.X = fromX * (1 - t);
            shift.Y = fromY * (1 - t);
        }, () => _glides.Remove(child));
    }

    // ------------------------------------------------------------------------------------------------
    // Dragging and resizing
    // ------------------------------------------------------------------------------------------------

    private void OnPreviewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_mode is Mode.Moving or Mode.Resizing || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        Reset(); // a press whose release happened outside the grid

        var (card, handle, interactive) = Find(e.Source as Visual);
        if (card is null || interactive || card.DataContext is not DashboardTileViewModel)
        {
            return;
        }

        // Nothing happens until the pointer moves a little, so clicks inside cards work as before.
        _mode = Mode.Pressed;
        _active = card;
        _pressedHandle = handle;
        _pressPoint = e.GetPosition(this);
        _pointer = e.Pointer;
    }

    private void OnPreviewPointerMoved(object? sender, PointerEventArgs e)
    {
        switch (_mode)
        {
            case Mode.Pressed:
                if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                {
                    Reset();
                    return;
                }

                var point = e.GetPosition(this);
                if (Math.Abs(point.X - _pressPoint.X) >= DragThreshold || Math.Abs(point.Y - _pressPoint.Y) >= DragThreshold)
                {
                    _pointer = e.Pointer;
                    Begin(_pressedHandle == DashboardHandle.Resize ? Mode.Resizing : Mode.Moving);
                    e.Handled = true;
                }

                break;
            case Mode.Moving or Mode.Resizing:
                _lastPoint = e.GetPosition(this);
                Track(_lastPoint, e.GetPosition(_scroller));
                e.Handled = true;
                break;
        }
    }

    private void OnPreviewPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_mode == Mode.Pressed)
        {
            Reset();
        }
        else if (_mode is Mode.Moving or Mode.Resizing)
        {
            End(commit: true);
            e.Handled = true;
        }
    }

    /// <summary>The card (a direct child) a press landed in, which handle it was on, and whether it was on a control.</summary>
    private (Control? Card, DashboardHandle Handle, bool Interactive) Find(Visual? source)
    {
        var handle = DashboardHandle.None;
        bool interactive = false;
        for (var d = source; d is not null && d != this; d = d.GetVisualParent())
        {
            if (handle == DashboardHandle.None && d is Control control)
            {
                handle = GetHandle(control);
                if (handle == DashboardHandle.None && d is Button or TextBox or ScrollBar or Thumb or SelectingItemsControl or Slider or ToggleSwitch)
                {
                    interactive = true;
                }
            }

            if (d is Control element && element.GetVisualParent() == this)
            {
                return (element, handle, interactive && handle == DashboardHandle.None);
            }
        }

        return (null, DashboardHandle.None, false);
    }

    private void Begin(Mode mode)
    {
        if (_active is null || _pointer is null || !_slots.TryGetValue(_active, out _startSlot))
        {
            Reset();
            return;
        }

        _pointer.Capture(this);
        _mode = mode;
        _startIndex = Ordered(preview: false).IndexOf(_active);
        var size = SizeOf(_active);
        _startSize = new TileSize(Math.Min(size.Columns, _columns), size.Rows);
        if (mode == Mode.Moving)
        {
            _previewIndex = _startIndex;
            _floating = _startSlot.Position;
            _active.ZIndex = 1;
            _active.Effect = new DropShadowEffect { BlurRadius = 30, OffsetX = 0, OffsetY = 10, Opacity = 0.38, Color = Colors.Black };
            Cursor = new Cursor(StandardCursorType.SizeAll);
        }
        else
        {
            _previewSize = _startSize;
            _outline = _startSlot;
            Cursor = new Cursor(StandardCursorType.BottomRightCorner);
        }

        _scroller = this.FindAncestorOfType<ScrollViewer>();
        if (_scroller is not null)
        {
            _scroller.ScrollChanged += OnScrollChanged;
        }

        _topLevel = TopLevel.GetTopLevel(this);
        _topLevel?.AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        InvalidateVisual();
    }

    private void Track(Point point, Point inScroller)
    {
        if (_active is null)
        {
            return;
        }

        double spacing = Spacing;
        double columnPitch = _columnWidth + spacing;
        double rowPitch = _rowHeight + spacing;
        if (_mode == Mode.Moving)
        {
            _floating = new Point(_startSlot.X + point.X - _pressPoint.X, _startSlot.Y + point.Y - _pressPoint.Y);
            int index = DashboardLayout.DropIndex(Sizes(Ordered(preview: false)), _startIndex, _columns,
                _floating.X / columnPitch, _floating.Y / rowPitch, columnPitch, rowPitch, _previewIndex);
            if (index != _previewIndex)
            {
                _previewIndex = index;
                _animate = true;
                InvalidateMeasure();
            }
            else if (_slots.TryGetValue(_active, out var slot))
            {
                Hold(_active, Shift(_active), _floating.X - slot.X, _floating.Y - slot.Y);
            }
        }
        else if (_mode == Mode.Resizing)
        {
            double width = Math.Max(24, _startSlot.Width + point.X - _pressPoint.X);
            double height = Math.Max(24, _startSlot.Height + point.Y - _pressPoint.Y);
            var origin = _slots.TryGetValue(_active, out var slot) ? slot.Position : _startSlot.Position;
            _outline = new Rect(origin, new Size(width, height));
            var size = new TileSize(
                DashboardLayout.Snap(width, _columnWidth, spacing, Math.Min(Math.Max(1, GetMinColumns(_active)), _columns), _columns),
                DashboardLayout.Snap(height, _rowHeight, spacing, Math.Max(1, GetMinRows(_active)), DashboardCatalog.MaxRows));
            if (size != _previewSize)
            {
                _previewSize = size;
                _animate = true;
                InvalidateMeasure();
            }

            InvalidateVisual();
        }

        AutoScroll(inScroller.Y);
    }

    /// <summary>Drops the card (or cancels): saves through the commands when something changed.</summary>
    private void End(bool commit)
    {
        var card = _active;
        var mode = _mode;
        var tile = card?.DataContext as DashboardTileViewModel;
        if (commit && tile is not null)
        {
            if (mode == Mode.Moving && _previewIndex >= 0 && _previewIndex != _startIndex)
            {
                Run(MoveCommand, new DashboardTileMove(tile, _previewIndex));
            }
            else if (mode == Mode.Resizing && card is not null && _previewSize is { } size && size != _startSize)
            {
                // Only the height changed: keep a width wider than this window has columns for.
                int columns = size.Columns == _startSize.Columns ? SizeOf(card).Columns : size.Columns;
                Run(ResizeCommand, new DashboardTileResize(tile, columns, size.Rows));
            }
        }

        Reset();
        if (card is not null && mode == Mode.Moving)
        {
            // Settle from where it was let go into its place (if the place changes too, the next layout carries on from here).
            var shift = Shift(card);
            Glide(card, shift, shift.X, shift.Y);
            DispatcherTimer.RunOnce(() =>
            {
                if (_active != card)
                {
                    card.ZIndex = 0;
                    card.Effect = null;
                }
            }, GlideDuration + TimeSpan.FromMilliseconds(60));
        }

        _animate = true;
        InvalidateMeasure();
        InvalidateVisual();
    }

    private static void Run(ICommand? command, object parameter)
    {
        if (command?.CanExecute(parameter) == true)
        {
            command.Execute(parameter);
        }
    }

    private void Reset()
    {
        bool captured = _mode is Mode.Moving or Mode.Resizing;
        _mode = Mode.None;
        _active = null;
        _previewIndex = -1;
        _startIndex = -1;
        _previewSize = null;
        _outline = null;
        StopAutoScroll();
        if (_scroller is not null)
        {
            _scroller.ScrollChanged -= OnScrollChanged;
            _scroller = null;
        }

        _topLevel?.RemoveHandler(KeyDownEvent, OnWindowKeyDown);
        _topLevel = null;
        if (captured)
        {
            Cursor = null;
            if (_pointer?.Captured == this)
            {
                _pointer.Capture(null);
            }
        }

        _pointer = null;
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _mode is Mode.Moving or Mode.Resizing)
        {
            End(commit: false);
            e.Handled = true;
        }
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_mode is Mode.Moving or Mode.Resizing && e.OffsetDelta.Y != 0)
        {
            // The page moved under the pointer: the pointer is now that much further down the grid.
            _pressPoint = new Point(_pressPoint.X, _pressPoint.Y - e.OffsetDelta.Y);
            Track(_lastPoint, new Point(0, double.NaN));
        }
    }

    /// <summary>Scrolls the page while a card is held near its top or bottom edge.</summary>
    private void AutoScroll(double y)
    {
        if (_scroller is null || double.IsNaN(y))
        {
            return;
        }

        double height = _scroller.Viewport.Height;
        _autoScrollStep = y < AutoScrollEdge
            ? -AutoScrollSpeed * Math.Min(1, (AutoScrollEdge - y) / AutoScrollEdge)
            : y > height - AutoScrollEdge
                ? AutoScrollSpeed * Math.Min(1, (y - (height - AutoScrollEdge)) / AutoScrollEdge)
                : 0;
        if (_autoScrollStep == 0)
        {
            StopAutoScroll();
            return;
        }

        _autoScroll ??= new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Input, (_, _) =>
        {
            if (_scroller is { } scroller)
            {
                scroller.Offset = new Vector(scroller.Offset.X, Math.Clamp(scroller.Offset.Y + _autoScrollStep, 0, Math.Max(0, scroller.Extent.Height - scroller.Viewport.Height)));
            }
        });
        _autoScroll.Start();
    }

    private void StopAutoScroll()
    {
        _autoScroll?.Stop();
        _autoScrollStep = 0;
    }
}
