using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Coreloom.App.Behaviors;
using Coreloom.Core.Dashboard;
using Coreloom.Core.Settings;
using Coreloom.ViewModels.Pages;

namespace Coreloom.App.Controls;

/// <summary>Marks the parts of a dashboard card that start a drag: the grip (move) and the corner (resize).</summary>
public enum DashboardHandle
{
    None,
    Move,
    Resize,
}

/// <summary>
/// The Overview's card grid. Cards cover whole cells of a grid of equal columns (6, 4, 2 or 1, by width) and short rows
/// (about half a summary card), in the order and sizes the user set (see <see cref="DashboardLayout"/>).
/// <para>
/// Drag a card to move it: the others make room as it goes, and on release it snaps into the place it is nearest to.
/// Drag its bottom-right corner to resize it in whole cells. Esc cancels either. Cards glide to their new places rather
/// than jumping, unless reduced motion is on. The result goes to <see cref="MoveCommand"/> or <see cref="ResizeCommand"/>,
/// which save it; the grid then lays the cards out from their (bound) order and size.
/// </para>
/// </summary>
public sealed class DashboardGrid : Panel
{
    public static readonly DependencyProperty MinColumnWidthProperty = DependencyProperty.Register(
        nameof(MinColumnWidth), typeof(double), typeof(DashboardGrid),
        new FrameworkPropertyMetadata(232.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing), typeof(double), typeof(DashboardGrid),
        new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty MinRowHeightProperty = DependencyProperty.Register(
        nameof(MinRowHeight), typeof(double), typeof(DashboardGrid),
        new FrameworkPropertyMetadata(40.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty MoveCommandProperty = DependencyProperty.Register(
        nameof(MoveCommand), typeof(ICommand), typeof(DashboardGrid));

    public static readonly DependencyProperty ResizeCommandProperty = DependencyProperty.Register(
        nameof(ResizeCommand), typeof(ICommand), typeof(DashboardGrid));

    public static readonly DependencyProperty OrderProperty = DependencyProperty.RegisterAttached(
        "Order", typeof(int), typeof(DashboardGrid), new FrameworkPropertyMetadata(0, OnCardLayoutChanged));

    public static readonly DependencyProperty ColumnSpanProperty = DependencyProperty.RegisterAttached(
        "ColumnSpan", typeof(int), typeof(DashboardGrid), new FrameworkPropertyMetadata(1, OnCardLayoutChanged));

    public static readonly DependencyProperty RowSpanProperty = DependencyProperty.RegisterAttached(
        "RowSpan", typeof(int), typeof(DashboardGrid), new FrameworkPropertyMetadata(2, OnCardLayoutChanged));

    public static readonly DependencyProperty MinColumnsProperty = DependencyProperty.RegisterAttached(
        "MinColumns", typeof(int), typeof(DashboardGrid), new FrameworkPropertyMetadata(1, OnCardLayoutChanged));

    public static readonly DependencyProperty MinRowsProperty = DependencyProperty.RegisterAttached(
        "MinRows", typeof(int), typeof(DashboardGrid), new FrameworkPropertyMetadata(1, OnCardLayoutChanged));

    public static readonly DependencyProperty HandleProperty = DependencyProperty.RegisterAttached(
        "Handle", typeof(DashboardHandle), typeof(DashboardGrid), new PropertyMetadata(DashboardHandle.None));

    /// <summary>A card as tall as its content (System health; see <see cref="DashboardLayout.RowHeights"/>).</summary>
    public static readonly DependencyProperty FitsContentProperty = DependencyProperty.RegisterAttached(
        "FitsContent", typeof(bool), typeof(DashboardGrid), new FrameworkPropertyMetadata(false, OnCardLayoutChanged));

    private static readonly TimeSpan GlideDuration = TimeSpan.FromMilliseconds(220);
    private const double AutoScrollEdge = 56;
    private const double AutoScrollSpeed = 16;

    private readonly Dictionary<UIElement, Rect> _slots = [];
    private List<UIElement> _ordered = [];
    private TilePlacement[] _placements = [];
    private int _columns = 1;
    private double _columnWidth;
    private double _rowHeight = 40;
    private double[] _rowHeights = [];
    private bool _animate;

    private Mode _mode;
    private UIElement? _active;
    private DashboardHandle _pressedHandle;
    private Point _pressPoint;
    private Rect _startSlot;
    private Point _floating;
    private int _startIndex = -1;
    private int _previewIndex = -1;
    private TileSize _startSize;
    private TileSize? _previewSize;
    private Rect? _outline;
    private ScrollViewer? _scroller;
    private Window? _window;
    private DispatcherTimer? _autoScroll;
    private double _autoScrollStep;

    private enum Mode
    {
        None,
        Pressed,
        Moving,
        Resizing,
    }

    public double MinColumnWidth
    {
        get => (double)GetValue(MinColumnWidthProperty);
        set => SetValue(MinColumnWidthProperty, value);
    }

    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>Rows are at least this tall; normally the cards' contents decide (see <see cref="DashboardLayout.RowHeight"/>).</summary>
    public double MinRowHeight
    {
        get => (double)GetValue(MinRowHeightProperty);
        set => SetValue(MinRowHeightProperty, value);
    }

    /// <summary>Runs with a <see cref="DashboardTileMove"/> when a card is dropped in a new place.</summary>
    public ICommand? MoveCommand
    {
        get => (ICommand?)GetValue(MoveCommandProperty);
        set => SetValue(MoveCommandProperty, value);
    }

    /// <summary>Runs with a <see cref="DashboardTileResize"/> when a card is resized.</summary>
    public ICommand? ResizeCommand
    {
        get => (ICommand?)GetValue(ResizeCommandProperty);
        set => SetValue(ResizeCommandProperty, value);
    }

    public static int GetOrder(DependencyObject d) => (int)d.GetValue(OrderProperty);

    public static void SetOrder(DependencyObject d, int value) => d.SetValue(OrderProperty, value);

    public static int GetColumnSpan(DependencyObject d) => (int)d.GetValue(ColumnSpanProperty);

    public static void SetColumnSpan(DependencyObject d, int value) => d.SetValue(ColumnSpanProperty, value);

    public static int GetRowSpan(DependencyObject d) => (int)d.GetValue(RowSpanProperty);

    public static void SetRowSpan(DependencyObject d, int value) => d.SetValue(RowSpanProperty, value);

    public static int GetMinColumns(DependencyObject d) => (int)d.GetValue(MinColumnsProperty);

    public static void SetMinColumns(DependencyObject d, int value) => d.SetValue(MinColumnsProperty, value);

    public static int GetMinRows(DependencyObject d) => (int)d.GetValue(MinRowsProperty);

    public static void SetMinRows(DependencyObject d, int value) => d.SetValue(MinRowsProperty, value);

    public static bool GetFitsContent(DependencyObject d) => (bool)d.GetValue(FitsContentProperty);

    public static void SetFitsContent(DependencyObject d, bool value) => d.SetValue(FitsContentProperty, value);

    public static DashboardHandle GetHandle(DependencyObject d) => (DashboardHandle)d.GetValue(HandleProperty);

    public static void SetHandle(DependencyObject d, DashboardHandle value) => d.SetValue(HandleProperty, value);

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

        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed)
            {
                child.Measure(default);
            }
        }

        _ordered = Ordered();
        var fits = _ordered.Select(GetFitsContent).ToArray();
        var sizes = DashboardLayout.FitSizes(Sizes(_ordered), fits, _columns);

        // First what each card needs at its width, which sets the row height (cards that fit their content have rows
        // of their own sizes, so they don't count); then each at the size it will get.
        var needs = new (double Height, int MinRows)[_ordered.Count];
        var heights = new double[_ordered.Count];
        for (int i = 0; i < _ordered.Count; i++)
        {
            var child = _ordered[i];
            child.Measure(new Size(DashboardLayout.Extent(Math.Min(sizes[i].Columns, _columns), _columnWidth, spacing), double.PositiveInfinity));
            heights[i] = child.DesiredSize.Height;
            needs[i] = fits[i] ? (0, 1) : (child.DesiredSize.Height, Math.Max(1, GetMinRows(child)));
        }

        _rowHeight = DashboardLayout.RowHeight(needs, spacing, MinRowHeight);
        _placements = DashboardLayout.Place(sizes, _columns);
        _rowHeights = DashboardLayout.RowHeights(_placements, fits, heights, _rowHeight, spacing);
        for (int i = 0; i < _ordered.Count; i++)
        {
            var p = _placements[i];
            _ordered[i].Measure(new Size(DashboardLayout.Extent(p.Columns, _columnWidth, spacing), DashboardLayout.Extent(_rowHeights, p.Row, p.Rows, spacing)));
        }

        return new Size(width, DashboardLayout.Extent(_rowHeights, 0, _rowHeights.Length, spacing));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double spacing = Spacing;
        double columnWidth = Math.Max(0, (finalSize.Width - (spacing * (_columns - 1))) / _columns);
        _columnWidth = columnWidth;
        bool glide = _animate && !Motion.ReducedMotion;
        var arranged = new HashSet<UIElement>();
        for (int i = 0; i < _ordered.Count && i < _placements.Length; i++)
        {
            var child = _ordered[i];
            var p = _placements[i];
            var slot = new Rect(
                p.Column * (columnWidth + spacing),
                DashboardLayout.Offset(_rowHeights, p.Row, spacing),
                DashboardLayout.Extent(p.Columns, columnWidth, spacing),
                DashboardLayout.Extent(_rowHeights, p.Row, p.Rows, spacing));
            child.Arrange(slot);
            arranged.Add(child);

            var shift = Shift(child);
            if (child == _active && _mode == Mode.Moving)
            {
                Hold(shift, _floating.X - slot.X, _floating.Y - slot.Y);
            }
            else if (glide && _slots.TryGetValue(child, out var old) && old.TopLeft != slot.TopLeft)
            {
                Glide(shift, old.X + shift.X - slot.X, old.Y + shift.Y - slot.Y);
            }

            _slots[child] = slot;
        }

        foreach (UIElement child in InternalChildren)
        {
            if (!arranged.Contains(child))
            {
                child.Arrange(default);
                _slots.Remove(child);
            }
        }

        _animate = false;
        if (_mode is Mode.Moving or Mode.Resizing)
        {
            InvalidateVisual(); // the drop outline follows the card's place
        }

        return finalSize;
    }

    /// <summary>The outline of where a dragged card will land, or of the size a card is being dragged to.</summary>
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        Rect? outline = _mode switch
        {
            Mode.Moving when _active is not null && _slots.TryGetValue(_active, out var slot) => slot,
            Mode.Resizing => _outline,
            _ => null,
        };
        if (outline is not { } rect || rect.IsEmpty)
        {
            return;
        }

        var accent = (TryFindResource("Brush.Accent") as SolidColorBrush)?.Color ?? Color.FromRgb(0x4C, 0x8D, 0xFF);
        var fill = new SolidColorBrush(Color.FromArgb(_mode == Mode.Moving ? (byte)28 : (byte)14, accent.R, accent.G, accent.B));
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(200, accent.R, accent.G, accent.B)), 1.5)
        {
            DashStyle = new DashStyle([4, 3], 0),
            DashCap = PenLineCap.Round,
        };
        fill.Freeze();
        pen.Freeze();
        rect.Inflate(-0.75, -0.75);
        dc.DrawRoundedRectangle(fill, pen, rect, 14, 14);
    }

    protected override void OnVisualChildrenChanged(DependencyObject visualAdded, DependencyObject visualRemoved)
    {
        base.OnVisualChildrenChanged(visualAdded, visualRemoved);
        if (visualRemoved is UIElement removed)
        {
            _slots.Remove(removed);
            if (removed == _active)
            {
                End(commit: false);
            }
        }

        _animate = _slots.Count > 0; // the others glide to make room or close the gap
    }

    private static void OnCardLayoutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UIElement child && VisualTreeHelper.GetParent(child) is DashboardGrid grid)
        {
            grid._animate = grid._slots.Count > 0;
            grid.InvalidateMeasure();
        }
    }

    /// <summary>Visible cards by their order; while a card is dragged (and <paramref name="preview"/>), with it moved to where it would land.</summary>
    private List<UIElement> Ordered(bool preview = true)
    {
        var cards = InternalChildren.Cast<UIElement>()
            .Where(c => c.Visibility != Visibility.Collapsed)
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

    private TileSize[] Sizes(IReadOnlyList<UIElement> cards) =>
        cards.Select(c => c == _active && _mode == Mode.Resizing && _previewSize is { } preview ? preview : SizeOf(c)).ToArray();

    private static TileSize SizeOf(UIElement card) =>
        new(Math.Max(GetColumnSpan(card), Math.Max(1, GetMinColumns(card))), Math.Max(GetRowSpan(card), Math.Max(1, GetMinRows(card))));

    private static TranslateTransform Shift(UIElement child)
    {
        if (child.RenderTransform is TranslateTransform { IsFrozen: false } shift)
        {
            return shift;
        }

        shift = new TranslateTransform();
        child.RenderTransform = shift;
        return shift;
    }

    /// <summary>Puts a card where it is shown right now.</summary>
    private static void Hold(TranslateTransform shift, double x, double y)
    {
        shift.BeginAnimation(TranslateTransform.XProperty, null);
        shift.BeginAnimation(TranslateTransform.YProperty, null);
        shift.X = x;
        shift.Y = y;
    }

    /// <summary>Glides a card from an offset to its place.</summary>
    private static void Glide(TranslateTransform shift, double fromX, double fromY)
    {
        shift.X = 0;
        shift.Y = 0;
        if (Motion.ReducedMotion || (Math.Abs(fromX) < 0.5 && Math.Abs(fromY) < 0.5))
        {
            shift.BeginAnimation(TranslateTransform.XProperty, null);
            shift.BeginAnimation(TranslateTransform.YProperty, null);
            return;
        }

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var x = new DoubleAnimation(fromX, 0, GlideDuration) { EasingFunction = ease };
        var y = new DoubleAnimation(fromY, 0, GlideDuration) { EasingFunction = ease };
        Timeline.SetDesiredFrameRate(x, 60);
        Timeline.SetDesiredFrameRate(y, 60);
        shift.BeginAnimation(TranslateTransform.XProperty, x);
        shift.BeginAnimation(TranslateTransform.YProperty, y);
    }

    // ------------------------------------------------------------------------------------------------
    // Dragging and resizing
    // ------------------------------------------------------------------------------------------------

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);
        if (_mode is Mode.Moving or Mode.Resizing)
        {
            return;
        }

        Reset(); // a press whose release happened outside the grid

        var (card, handle, interactive) = Find(e.OriginalSource as DependencyObject);
        if (card is null || interactive || card.DataContext is not DashboardTileViewModel)
        {
            return;
        }

        // Nothing happens until the mouse moves a little, so clicks inside cards work as before.
        _mode = Mode.Pressed;
        _active = card;
        _pressedHandle = handle;
        _pressPoint = e.GetPosition(this);
    }

    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        base.OnPreviewMouseMove(e);
        switch (_mode)
        {
            case Mode.Pressed:
                if (e.LeftButton != MouseButtonState.Pressed)
                {
                    Reset();
                    return;
                }

                var point = e.GetPosition(this);
                if (Math.Abs(point.X - _pressPoint.X) >= SystemParameters.MinimumHorizontalDragDistance
                    || Math.Abs(point.Y - _pressPoint.Y) >= SystemParameters.MinimumVerticalDragDistance)
                {
                    Begin(_pressedHandle == DashboardHandle.Resize ? Mode.Resizing : Mode.Moving);
                    e.Handled = true;
                }

                break;
            case Mode.Moving or Mode.Resizing:
                Track(e.GetPosition(this));
                e.Handled = true;
                break;
        }
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonUp(e);
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

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (_mode is Mode.Moving or Mode.Resizing)
        {
            End(commit: false); // e.g. another window took the mouse
        }
    }

    /// <summary>The card (a direct child) a press landed in, which handle it was on, and whether it was on a control.</summary>
    private (FrameworkElement? Card, DashboardHandle Handle, bool Interactive) Find(DependencyObject? source)
    {
        var handle = DashboardHandle.None;
        bool interactive = false;
        for (var d = source; d is not null && d != this; d = ParentOf(d))
        {
            if (handle == DashboardHandle.None)
            {
                handle = GetHandle(d);
                if (handle == DashboardHandle.None && d is ButtonBase or TextBoxBase or ScrollBar or Thumb or Selector or Slider or PasswordBox)
                {
                    interactive = true;
                }
            }

            if (d is FrameworkElement element && VisualTreeHelper.GetParent(element) == this)
            {
                return (element, handle, interactive && handle == DashboardHandle.None);
            }
        }

        return (null, DashboardHandle.None, false);
    }

    private static DependencyObject? ParentOf(DependencyObject d) =>
        d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);

    private void Begin(Mode mode)
    {
        if (_active is null || !_slots.TryGetValue(_active, out _startSlot) || !CaptureMouse())
        {
            Reset();
            return;
        }

        _mode = mode;
        _startIndex = Ordered(preview: false).IndexOf(_active);
        var size = SizeOf(_active);
        _startSize = new TileSize(Math.Min(size.Columns, _columns), size.Rows);
        if (mode == Mode.Moving)
        {
            _previewIndex = _startIndex;
            _floating = _startSlot.TopLeft;
            Panel.SetZIndex(_active, 1);
            _active.Effect = new DropShadowEffect { BlurRadius = 28, ShadowDepth = 8, Direction = 270, Opacity = 0.24, Color = Colors.Black };
            Mouse.OverrideCursor = Cursors.SizeAll;
        }
        else
        {
            _previewSize = _startSize;
            _outline = _startSlot;
            Mouse.OverrideCursor = Cursors.SizeNWSE;
        }

        _scroller = FindScroller();
        if (_scroller is not null)
        {
            _scroller.ScrollChanged += OnScrollChanged;
        }

        _window = Window.GetWindow(this);
        if (_window is not null)
        {
            _window.PreviewKeyDown += OnWindowKeyDown;
        }

        InvalidateVisual();
    }

    private void Track(Point point)
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
            var ordered = Ordered(preview: false);
            int index = DashboardLayout.DropIndex(DashboardLayout.FitSizes(Sizes(ordered), ordered.Select(GetFitsContent).ToArray(), _columns), _startIndex, _columns,
                _floating.X / columnPitch, DashboardLayout.RowAt(_rowHeights, _floating.Y, spacing, _rowHeight), columnPitch, rowPitch, _previewIndex);
            if (index != _previewIndex)
            {
                _previewIndex = index;
                _animate = true;
                InvalidateMeasure();
            }
            else if (_slots.TryGetValue(_active, out var slot))
            {
                Hold(Shift(_active), _floating.X - slot.X, _floating.Y - slot.Y);
            }
        }
        else if (_mode == Mode.Resizing)
        {
            double width = Math.Max(24, _startSlot.Width + point.X - _pressPoint.X);
            double height = Math.Max(24, _startSlot.Height + point.Y - _pressPoint.Y);
            var origin = _slots.TryGetValue(_active, out var slot) ? slot.TopLeft : _startSlot.TopLeft;
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

        AutoScroll();
    }

    /// <summary>Drops the card (or cancels): saves through the commands when something changed.</summary>
    private void End(bool commit)
    {
        var card = _active;
        var mode = _mode;
        var tile = (card as FrameworkElement)?.DataContext as DashboardTileViewModel;
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
            Glide(shift, shift.X, shift.Y);

            var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = GlideDuration + TimeSpan.FromMilliseconds(60) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                if (_active != card)
                {
                    Panel.SetZIndex(card, 0);
                    card.Effect = null;
                }
            };
            timer.Start();
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

        if (_window is not null)
        {
            _window.PreviewKeyDown -= OnWindowKeyDown;
            _window = null;
        }

        if (captured)
        {
            Mouse.OverrideCursor = null;
            if (IsMouseCaptured)
            {
                ReleaseMouseCapture();
            }
        }
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _mode is Mode.Moving or Mode.Resizing)
        {
            End(commit: false);
            e.Handled = true;
        }
    }

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_mode is Mode.Moving or Mode.Resizing && e.VerticalChange != 0)
        {
            Track(Mouse.GetPosition(this)); // the page moved under the pointer
        }
    }

    private ScrollViewer? FindScroller()
    {
        for (var d = VisualTreeHelper.GetParent(this); d is not null; d = VisualTreeHelper.GetParent(d))
        {
            if (d is ScrollViewer scroller)
            {
                return scroller;
            }
        }

        return null;
    }

    /// <summary>Scrolls the page while a card is held near its top or bottom edge.</summary>
    private void AutoScroll()
    {
        if (_scroller is null)
        {
            return;
        }

        double y = Mouse.GetPosition(_scroller).Y;
        double height = _scroller.ViewportHeight;
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

        if (_autoScroll is null)
        {
            _autoScroll = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(16) };
            _autoScroll.Tick += (_, _) => _scroller?.ScrollToVerticalOffset(_scroller.VerticalOffset + _autoScrollStep);
        }

        _autoScroll.Start();
    }

    private void StopAutoScroll()
    {
        _autoScroll?.Stop();
        _autoScrollStep = 0;
    }
}
