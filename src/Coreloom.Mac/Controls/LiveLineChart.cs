using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Coreloom.Core.Charting;
using Coreloom.Core.History;
using Coreloom.Mac.Behaviors;
using Coreloom.Mac.Themes;
using Coreloom.ViewModels.Infrastructure;

namespace Coreloom.Mac.Controls;

/// <summary>A layer of a custom-drawn control: draws whatever its owner last prepared.</summary>
internal sealed class DrawLayer : Control
{
    public DrawLayer()
    {
        IsHitTestVisible = false;
    }

    public Action<DrawingContext>? Draw { get; set; }

    public override void Render(DrawingContext context) => Draw?.Invoke(context);
}

/// <summary>
/// Real-time line/area chart. Features: min/max downsampling for long windows, monotone curve smoothing (never
/// overshoots), gradient area fills, gap detection, nice Y axes with hysteresis, reference lines (power limit /
/// slowdown temperature), a hover crosshair and tooltip shared by every chart, freeze, and smooth scrolling between
/// samples (the plotted lines slide by a transform, composited; off for windows over 5 minutes and with reduced motion).
/// </summary>
public sealed class LiveLineChart : Control
{
    public static readonly StyledProperty<ChartViewModel?> ChartProperty = AvaloniaProperty.Register<LiveLineChart, ChartViewModel?>(nameof(Chart));

    public static readonly StyledProperty<TimelineViewModel?> TimelineProperty = AvaloniaProperty.Register<LiveLineChart, TimelineViewModel?>(nameof(Timeline));

    public static readonly StyledProperty<bool> ShowAxesProperty = AvaloniaProperty.Register<LiveLineChart, bool>(nameof(ShowAxes), true);

    public static readonly StyledProperty<double> StrokeThicknessProperty = AvaloniaProperty.Register<LiveLineChart, double>(nameof(StrokeThickness), 1.8);

    public static readonly StyledProperty<double> FillOpacityProperty = AvaloniaProperty.Register<LiveLineChart, double>(nameof(FillOpacity), 0.38);

    public static readonly StyledProperty<bool> ShowTooltipProperty = AvaloniaProperty.Register<LiveLineChart, bool>(nameof(ShowTooltip), true);

    private const double AxisFontSize = 10.5;
    private readonly DrawLayer _grid = new();
    private readonly DrawLayer _plot = new();
    private readonly Panel _plotHost = new() { IsHitTestVisible = false };
    private readonly DrawLayer _overlay = new();
    private readonly TranslateTransform _shift = new();
    private readonly List<RenderedSeries> _rendered = [];
    private readonly List<Action<DrawingContext>> _gridOps = [];
    private readonly List<Action<DrawingContext>> _plotOps = [];
    private HistoryPoint[] _buffer = new HistoryPoint[1024];
    private HistoryPoint[] _sampled = new HistoryPoint[2048];
    private ChartViewModel? _chart;
    private TimelineViewModel? _timeline;
    private Rect _plotRect;
    private long _from;
    private double _pxPerMs = 1;
    private double _yMin;
    private double _yMax = 1;
    private double _smoothedAutoMax = double.NaN;
    private Point? _mouse;
    private bool _attached;
    private Animator.Tween? _scroll;
    private bool _renderQueued;

    public LiveLineChart()
    {
        _plot.RenderTransform = _shift;
        _plotHost.Children.Add(_plot);
        VisualChildren.Add(_grid);
        VisualChildren.Add(_plotHost);
        VisualChildren.Add(_overlay);
        _grid.Draw = dc =>
        {
            foreach (var op in _gridOps)
            {
                op(dc);
            }
        };
        _plot.Draw = dc =>
        {
            foreach (var op in _plotOps)
            {
                op(dc);
            }
        };
        _overlay.Draw = DrawOverlay;
        ClipToBounds = true;
        Focusable = false;
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
    }

    public ChartViewModel? Chart
    {
        get => GetValue(ChartProperty);
        set => SetValue(ChartProperty, value);
    }

    public TimelineViewModel? Timeline
    {
        get => GetValue(TimelineProperty);
        set => SetValue(TimelineProperty, value);
    }

    public bool ShowAxes
    {
        get => GetValue(ShowAxesProperty);
        set => SetValue(ShowAxesProperty, value);
    }

    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public double FillOpacity
    {
        get => GetValue(FillOpacityProperty);
        set => SetValue(FillOpacityProperty, value);
    }

    public bool ShowTooltip
    {
        get => GetValue(ShowTooltipProperty);
        set => SetValue(ShowTooltipProperty, value);
    }

    /// <summary>A transparent surface, so the whole area takes the pointer.</summary>
    public override void Render(DrawingContext context) => context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var layer in new Control[] { _grid, _plotHost, _overlay })
        {
            layer.Measure(availableSize);
        }

        return new Size(0, 0);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var rect = new Rect(finalSize);
        _grid.Arrange(rect);
        _plotHost.Arrange(rect);
        _overlay.Arrange(rect);
        QueueRender();
        return finalSize;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ChartProperty || change.Property == TimelineProperty)
        {
            if (_attached)
            {
                Detach();
                Attach();
            }
        }
        else if (change.Property == ShowAxesProperty || change.Property == StrokeThicknessProperty || change.Property == FillOpacityProperty)
        {
            QueueRender();
        }
        else if (change.Property == IsVisibleProperty)
        {
            if (IsVisible)
            {
                QueueRender();
            }
            else
            {
                StopScrolling();
            }
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        _mouse = e.GetPosition(this);
        UpdateHoverFromMouse();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _mouse = null;
        if (_timeline is not null)
        {
            _timeline.HoverTime = null;
        }

        _overlay.InvalidateVisual();
    }

    private void Attach()
    {
        if (_attached)
        {
            return;
        }

        _attached = true;
        _chart = Chart;
        _timeline = Timeline;
        _smoothedAutoMax = double.NaN;
        if (_timeline is not null)
        {
            _timeline.PropertyChanged += OnTimelineChanged;
        }

        if (_chart is not null)
        {
            _chart.Series.CollectionChanged += OnSeriesCollectionChanged;
            foreach (var series in _chart.Series)
            {
                series.PropertyChanged += OnSeriesChanged;
            }
        }

        if (ThemeService.Instance is { } theme)
        {
            theme.ThemeChanged += OnThemeChanged;
        }

        RenderChart();
    }

    private void Detach()
    {
        if (!_attached)
        {
            return;
        }

        _attached = false;
        StopScrolling();
        if (_timeline is not null)
        {
            _timeline.PropertyChanged -= OnTimelineChanged;
        }

        if (_chart is not null)
        {
            _chart.Series.CollectionChanged -= OnSeriesCollectionChanged;
            foreach (var series in _chart.Series)
            {
                series.PropertyChanged -= OnSeriesChanged;
            }
        }

        if (ThemeService.Instance is { } theme)
        {
            theme.ThemeChanged -= OnThemeChanged;
        }

        _chart = null;
        _timeline = null;
    }

    private void OnThemeChanged(object? sender, EventArgs e) => QueueRender();

    /// <summary>Several changes in one pass (a theme fade, a new frame and a resize) draw once.</summary>
    private void QueueRender()
    {
        if (_renderQueued)
        {
            return;
        }

        _renderQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _renderQueued = false;
            RenderChart();
        }, DispatcherPriority.Render);
    }

    private void OnSeriesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (ChartSeriesViewModel s in e.OldItems)
            {
                s.PropertyChanged -= OnSeriesChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (ChartSeriesViewModel s in e.NewItems)
            {
                s.PropertyChanged += OnSeriesChanged;
            }
        }

        QueueRender();
    }

    private void OnSeriesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ChartSeriesViewModel.IsVisible) or nameof(ChartSeriesViewModel.MetricId))
        {
            _smoothedAutoMax = double.NaN;
            QueueRender();
        }
    }

    private void OnTimelineChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(TimelineViewModel.RightEdge):
            case nameof(TimelineViewModel.IsFrozen):
            case nameof(TimelineViewModel.SmoothScrolling):
            case nameof(TimelineViewModel.IntervalMilliseconds):
                RenderChart();
                break;
            case nameof(TimelineViewModel.Window):
                _smoothedAutoMax = double.NaN;
                RenderChart();
                break;
            case nameof(TimelineViewModel.HoverTime):
                _overlay.InvalidateVisual();
                break;
        }
    }

    // -------------------------------------------------------------------------------------------
    // Rendering
    // -------------------------------------------------------------------------------------------

    private void RenderChart()
    {
        var chart = _chart;
        var timeline = _timeline;
        double width = Bounds.Width;
        double height = Bounds.Height;
        if (!_attached || chart is null || timeline is null || !IsEffectivelyVisible || width < 24 || height < 24)
        {
            return;
        }

        bool axes = ShowAxes;
        double left = axes ? 44 : 0;
        double right = axes ? 6 : 0;
        double top = axes ? 10 : 4;
        double bottom = axes ? 22 : 0;
        _plotRect = new Rect(left, top, Math.Max(1, width - left - right), Math.Max(1, height - top - bottom));
        _plotHost.Clip = new RectangleGeometry(_plotRect);

        long interval = Math.Max(50, timeline.IntervalMilliseconds);
        long window = Math.Max(1000, timeline.WindowMilliseconds);
        long edge = timeline.RightEdge;
        bool smooth = timeline.SmoothScrolling && !timeline.IsFrozen && window <= 300_000 && edge > 0 && !Motion.ReducedMotion;
        long plotRight = smooth ? edge - interval : edge;
        _from = plotRight - window;
        _pxPerMs = _plotRect.Width / window;

        CollectData(chart, timeline, edge, interval, window);
        ComputeScale(chart);

        var typeface = ChartFonts.MonoRegular;
        var gridColor = ChartColors.Theme("ChartGrid");
        var axisBrush = ChartColors.Solid(ChartColors.Theme("ChartAxis"));

        _gridOps.Clear();
        var gridPen = ChartColors.Pen(gridColor, 1, round: false);
        double factor = chart.AxisFactor(_yMax);
        double step = AxisScale.NiceStep((_yMax - _yMin) / factor, axes ? 4 : 2) * factor;
        var plot = _plotRect;
        for (double v = _yMin; v <= _yMax + (step * 0.001); v += step)
        {
            double y = Math.Round(Y(v)) + 0.5;
            _gridOps.Add(dc => dc.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y)));
            if (axes)
            {
                var text = Text(chart.FormatAxis(v, _yMax), typeface, axisBrush);
                _gridOps.Add(dc => dc.DrawText(text, new Point(plot.Left - 8 - text.Width, y - (text.Height / 2))));
            }
        }

        if (axes)
        {
            AddTimeLabels(timeline, window, edge, ChartFonts.UiRegular, axisBrush);
        }

        if (chart.ReferenceValue is double reference && double.IsFinite(reference) && reference >= _yMin && reference <= _yMax)
        {
            var referenceColor = ChartColors.Theme("Critical");
            double y = Math.Round(Y(reference)) + 0.5;
            var pen = ChartColors.DashedPen(Color.FromArgb(170, referenceColor.R, referenceColor.G, referenceColor.B), 1);
            string label = chart.Series.Count > 0 ? $"{chart.ReferenceLabel} · {chart.Series[0].Format(reference)}" : chart.ReferenceLabel ?? string.Empty;
            var text = Text(label, ChartFonts.UiRegular, ChartColors.Solid(referenceColor));
            _gridOps.Add(dc =>
            {
                dc.DrawLine(pen, new Point(plot.Left, y), new Point(plot.Right, y));
                dc.DrawText(text, new Point(plot.Right - text.Width - 4, y - text.Height - 2));
            });
        }

        if (_rendered.Count == 0)
        {
            var text = Text("No readings available", ChartFonts.UiRegular, axisBrush, 12);
            _gridOps.Add(dc => dc.DrawText(text, new Point(plot.Left + ((plot.Width - text.Width) / 2), plot.Top + ((plot.Height - text.Height) / 2))));
        }

        _plotOps.Clear();
        double gapMs = Math.Max(interval * 3.5, window / Math.Max(16, _plotRect.Width / 1.5) * 2.5);
        foreach (var series in _rendered)
        {
            AddSeries(series, gapMs);
        }

        _grid.InvalidateVisual();
        _plot.InvalidateVisual();

        _scroll?.Cancel();
        if (smooth && IsEffectivelyVisible)
        {
            double distance = -interval * _pxPerMs;
            _shift.X = 0;
            _scroll = Animator.Run(TimeSpan.FromMilliseconds(interval), Ease.Linear, t => _shift.X = distance * t);
        }
        else
        {
            StopScrolling();
        }

        if (_mouse is not null && IsPointerOver)
        {
            UpdateHoverFromMouse();
        }

        _overlay.InvalidateVisual();
    }

    private void CollectData(ChartViewModel chart, TimelineViewModel timeline, long edge, long interval, long window)
    {
        _rendered.Clear();
        int buckets = Math.Max(16, (int)(_plotRect.Width / 1.5));
        foreach (var series in chart.Series)
        {
            if (!series.IsVisible)
            {
                continue;
            }

            var history = timeline.History.Get(series.MetricId);
            if (history is null)
            {
                continue;
            }

            int n = history.CopyRange(_from - (interval * 2), edge + interval, ref _buffer, includeNeighbors: true);
            if (n == 0)
            {
                continue;
            }

            if (_sampled.Length < n)
            {
                _sampled = new HistoryPoint[Math.Max(n, _sampled.Length * 2)];
            }

            int m = Downsampler.MinMax(_buffer.AsSpan(0, n), _from, edge, buckets, _sampled);
            var points = new HistoryPoint[m];
            Array.Copy(_sampled, points, m);

            double max = double.MinValue;
            double min = double.MaxValue;
            bool any = false;
            foreach (var p in points)
            {
                if (p.T >= _from && p.T <= edge)
                {
                    any = true;
                    max = Math.Max(max, p.V);
                    min = Math.Min(min, p.V);
                }
            }

            if (!any)
            {
                continue;
            }

            _rendered.Add(new RenderedSeries(series, points, ChartColors.Series(series.ColorKey), min, max));
        }
    }

    private void ComputeScale(ChartViewModel chart)
    {
        double dataMax = _rendered.Count == 0 ? 0 : _rendered.Max(r => r.Max);
        switch (chart.Scale)
        {
            case ChartScale.Percent:
                _yMin = 0;
                _yMax = 100;
                break;
            case ChartScale.Temperature:
                _yMin = 0;
                double top = Math.Max(dataMax + 5, chart.ReferenceValue is double r && double.IsFinite(r) ? r + 5 : 0);
                _yMax = Math.Max(100, AxisScale.NiceRange(0, top, 4).Max);
                break;
            default:
                _yMin = 0;
                double target = Math.Max(dataMax, chart.ReferenceValue is double reference && double.IsFinite(reference) ? reference : 0) * 1.12;
                double factor = chart.AxisFactor(target);
                double nice = target <= 0 ? 1 : AxisScale.NiceRange(0, target / factor, 4).Max * factor;
                // Hysteresis: grow immediately, shrink only when the data fits in less than half the axis.
                if (double.IsNaN(_smoothedAutoMax) || nice > _smoothedAutoMax || nice < _smoothedAutoMax * 0.5)
                {
                    _smoothedAutoMax = nice;
                }

                _yMax = _smoothedAutoMax;
                break;
        }

        if (_yMax - _yMin < 1e-9)
        {
            _yMax = _yMin + 1;
        }
    }

    private void AddSeries(RenderedSeries series, double gapMs)
    {
        var points = series.Points;
        int start = 0;
        for (int i = 1; i <= points.Length; i++)
        {
            bool split = i == points.Length || points[i].T - points[i - 1].T > gapMs;
            if (!split)
            {
                continue;
            }

            int count = i - start;
            if (count == 1)
            {
                var p = ToScreen(points[start]);
                var brush = ChartColors.Solid(series.Color);
                _plotOps.Add(dc => dc.DrawEllipse(brush, null, p, 2, 2));
            }
            else if (count > 1)
            {
                AddSegment(series, start, count);
            }

            start = i;
        }
    }

    private void AddSegment(RenderedSeries series, int start, int count)
    {
        var xs = new double[count];
        var ys = new double[count];
        for (int k = 0; k < count; k++)
        {
            var p = ToScreen(series.Points[start + k]);
            xs[k] = p.X;
            ys[k] = p.Y;
        }

        for (int k = 1; k < count; k++)
        {
            if (xs[k] <= xs[k - 1])
            {
                xs[k] = xs[k - 1] + 0.001;
            }
        }

        bool smoothCurve = (xs[count - 1] - xs[0]) / Math.Max(1, count - 1) >= 3;
        double[]? tangents = null;
        if (smoothCurve)
        {
            tangents = new double[count];
            MonotoneCurve.ComputeTangents(xs, ys, tangents);
        }

        var line = new StreamGeometry();
        using (var ctx = line.Open())
        {
            ctx.BeginFigure(new Point(xs[0], ys[0]), false);
            AppendPath(ctx, xs, ys, tangents);
            ctx.EndFigure(false);
        }

        if (series.Source.Fill && FillOpacity > 0)
        {
            double baseline = Y(_yMin);
            var area = new StreamGeometry();
            using (var ctx = area.Open())
            {
                ctx.BeginFigure(new Point(xs[0], baseline), true);
                ctx.LineTo(new Point(xs[0], ys[0]));
                AppendPath(ctx, xs, ys, tangents);
                ctx.LineTo(new Point(xs[count - 1], baseline));
                ctx.EndFigure(true);
            }

            var fill = ChartColors.AreaFill(series.Color, FillOpacity);
            _plotOps.Add(dc => dc.DrawGeometry(fill, null, area));
        }

        var pen = ChartColors.Pen(series.Color, StrokeThickness);
        _plotOps.Add(dc => dc.DrawGeometry(null, pen, line));
    }

    private static void AppendPath(StreamGeometryContext ctx, double[] xs, double[] ys, double[]? tangents)
    {
        int count = xs.Length;
        if (tangents is null)
        {
            for (int k = 1; k < count; k++)
            {
                ctx.LineTo(new Point(xs[k], ys[k]));
            }

            return;
        }

        for (int k = 0; k < count - 1; k++)
        {
            var (x1, y1, x2, y2) = MonotoneCurve.ControlPoints(xs, ys, tangents, k);
            ctx.CubicBezierTo(new Point(x1, y1), new Point(x2, y2), new Point(xs[k + 1], ys[k + 1]));
        }
    }

    private void AddTimeLabels(TimelineViewModel timeline, long window, long edge, Typeface typeface, IBrush brush)
    {
        var plot = _plotRect;
        double y = plot.Bottom + 5;
        var leftText = Text(FormatAgo(window), typeface, brush);
        var middleText = Text(FormatAgo(window / 2), typeface, brush);
        string rightLabel = timeline.IsFrozen ? timeline.ToLocalTime(edge).ToString("T", CultureInfo.CurrentCulture) : "now";
        var rightText = Text(rightLabel, typeface, brush);
        _gridOps.Add(dc =>
        {
            dc.DrawText(leftText, new Point(plot.Left, y));
            dc.DrawText(middleText, new Point(plot.Left + ((plot.Width - middleText.Width) / 2), y));
            dc.DrawText(rightText, new Point(plot.Right - rightText.Width, y));
        });
    }

    private static string FormatAgo(long milliseconds)
    {
        double seconds = milliseconds / 1000.0;
        if (seconds <= 90)
        {
            return $"{seconds:0}s";
        }

        double minutes = seconds / 60;
        return minutes % 1 == 0 ? $"{minutes:0} min" : $"{minutes:0.#} min";
    }

    private void DrawOverlay(DrawingContext dc)
    {
        var timeline = _timeline;
        if (!_attached || timeline?.HoverTime is not long hover || _rendered.Count == 0)
        {
            return;
        }

        double x = _plotRect.Left + ((hover - _from) * _pxPerMs) + _shift.X;
        if (x < _plotRect.Left || x > _plotRect.Right)
        {
            return;
        }

        var crosshair = ChartColors.Pen(ChartColors.Theme("ChartCrosshair"), 1, round: false);
        dc.DrawLine(crosshair, new Point(Math.Round(x) + 0.5, _plotRect.Top), new Point(Math.Round(x) + 0.5, _plotRect.Bottom));

        var rows = new List<(Color Color, string Label, string Value, double Y)>();
        long tolerance = Math.Max(1500, timeline.IntervalMilliseconds * 2L);
        var cardColor = ChartColors.Theme("Card");
        foreach (var series in _rendered)
        {
            var history = timeline.History.Get(series.Source.MetricId);
            if (history is null || !history.TryFindNearest(hover, out var point) || Math.Abs(point.T - hover) > tolerance)
            {
                continue;
            }

            double y = Y(point.V);
            dc.DrawEllipse(ChartColors.Solid(series.Color), ChartColors.Pen(cardColor, 2), new Point(x, y), 4, 4);
            rows.Add((series.Color, series.Source.Label, series.Source.Format(point.V), y));
        }

        if (!ShowTooltip || !IsPointerOver || _mouse is null || rows.Count == 0)
        {
            return;
        }

        var typeface = ChartFonts.UiRegular;
        var strong = ChartFonts.MonoSemiBold;
        var textBrush = ChartColors.Solid(ChartColors.Theme("Text"));
        var secondary = ChartColors.Solid(ChartColors.Theme("TextSecondary"));

        var header = Text(timeline.ToLocalTime(hover).ToString("T", CultureInfo.CurrentCulture), typeface, secondary, 11);
        var lines = rows.Select(r => (r.Color, Label: Text(r.Label, typeface, secondary, 12), Value: Text(r.Value, strong, textBrush, 12))).ToList();
        double labelWidth = lines.Max(l => l.Label.Width);
        double valueWidth = lines.Max(l => l.Value.Width);
        double lineHeight = 20;
        double boxWidth = Math.Max(header.Width, 14 + labelWidth + 16 + valueWidth) + 24;
        double boxHeight = 12 + header.Height + 6 + (lines.Count * lineHeight) + 8;

        double bx = x + 14;
        if (bx + boxWidth > Bounds.Width - 2)
        {
            bx = x - 14 - boxWidth;
        }

        double by = Math.Clamp(_mouse.Value.Y - (boxHeight / 2), 2, Math.Max(2, Bounds.Height - boxHeight - 2));
        var box = new Rect(Math.Max(2, bx), by, boxWidth, boxHeight);
        dc.DrawRectangle(ChartColors.Solid(ChartColors.Theme("Tooltip")), ChartColors.Pen(ChartColors.Theme("TooltipBorder"), 1, round: false), box, 8, 8);
        dc.DrawText(header, new Point(box.Left + 12, box.Top + 10));
        double ly = box.Top + 10 + header.Height + 6;
        foreach (var (color, label, value) in lines)
        {
            dc.DrawEllipse(ChartColors.Solid(color), null, new Point(box.Left + 16, ly + (lineHeight / 2)), 4, 4);
            dc.DrawText(label, new Point(box.Left + 26, ly + ((lineHeight - label.Height) / 2)));
            dc.DrawText(value, new Point(box.Right - 12 - value.Width, ly + ((lineHeight - value.Height) / 2)));
            ly += lineHeight;
        }
    }

    private void UpdateHoverFromMouse()
    {
        var timeline = _timeline;
        if (timeline is null || _mouse is not Point mouse)
        {
            return;
        }

        if (!_plotRect.Contains(mouse) || _pxPerMs <= 0)
        {
            timeline.HoverTime = null;
            return;
        }

        timeline.HoverTime = _from + (long)((mouse.X - _plotRect.Left - _shift.X) / _pxPerMs);
    }

    private void StopScrolling()
    {
        _scroll?.Cancel();
        _scroll = null;
        _shift.X = 0;
    }

    private Point ToScreen(HistoryPoint p) => new(_plotRect.Left + ((p.T - _from) * _pxPerMs), Y(p.V));

    private double Y(double value)
    {
        double range = _yMax - _yMin;
        double clamped = Math.Clamp(value, _yMin - (range * 0.02), _yMax + (range * 0.02));
        return _plotRect.Bottom - ((clamped - _yMin) / range * _plotRect.Height);
    }

    internal static FormattedText Text(string text, Typeface typeface, IBrush brush, double size = AxisFontSize) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, size, brush);

    private sealed record RenderedSeries(ChartSeriesViewModel Source, HistoryPoint[] Points, Color Color, double Min, double Max);
}
