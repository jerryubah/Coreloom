using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Coreloom.App.Themes;
using Coreloom.Core.Charting;
using Coreloom.Core.History;
using Coreloom.ViewModels.Infrastructure;

namespace Coreloom.App.Controls;

/// <summary>
/// Real-time line/area chart drawn with WPF's retained, GPU-composited drawing (no per-pixel CPU rasterizing).
/// Features: min/max downsampling for long windows, monotone curve smoothing (never overshoots), gradient
/// area fills, gap detection, nice Y axes with hysteresis, reference lines (power limit / slowdown temp),
/// synchronized hover crosshair + tooltip across charts, freeze, and smooth scrolling between samples via an
/// animated transform (at the display refresh rate, GPU composited; disabled for windows over 5 minutes and with reduced motion).
/// </summary>
public sealed class LiveLineChart : FrameworkElement
{
    public static readonly DependencyProperty ChartProperty = DependencyProperty.Register(
        nameof(Chart), typeof(ChartViewModel), typeof(LiveLineChart), new PropertyMetadata(null, OnSourceChanged));

    public static readonly DependencyProperty TimelineProperty = DependencyProperty.Register(
        nameof(Timeline), typeof(TimelineViewModel), typeof(LiveLineChart), new PropertyMetadata(null, OnSourceChanged));

    public static readonly DependencyProperty ShowAxesProperty = DependencyProperty.Register(
        nameof(ShowAxes), typeof(bool), typeof(LiveLineChart), new PropertyMetadata(true, OnAppearanceChanged));

    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness), typeof(double), typeof(LiveLineChart), new PropertyMetadata(1.8, OnAppearanceChanged));

    public static readonly DependencyProperty FillOpacityProperty = DependencyProperty.Register(
        nameof(FillOpacity), typeof(double), typeof(LiveLineChart), new PropertyMetadata(0.38, OnAppearanceChanged));

    public static readonly DependencyProperty ShowTooltipProperty = DependencyProperty.Register(
        nameof(ShowTooltip), typeof(bool), typeof(LiveLineChart), new PropertyMetadata(true));

    private const double AxisFontSize = 10.5;
    private readonly VisualCollection _children;
    private readonly DrawingVisual _grid = new();
    private readonly DrawingVisual _plot = new();
    private readonly DrawingVisual _overlay = new();
    private readonly ContainerVisual _plotHost = new();
    private readonly TranslateTransform _shift = new();
    private readonly RectangleGeometry _clip = new();
    private readonly List<RenderedSeries> _rendered = [];
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

    public LiveLineChart()
    {
        _children = new VisualCollection(this) { _grid, _plotHost, _overlay };
        _plotHost.Children.Add(_plot);
        _plot.Transform = _shift;
        _plotHost.Clip = _clip;
        ClipToBounds = true;
        Focusable = false;
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                Render();
            }
            else
            {
                StopScrolling();
            }
        };
    }

    public ChartViewModel? Chart
    {
        get => (ChartViewModel?)GetValue(ChartProperty);
        set => SetValue(ChartProperty, value);
    }

    public TimelineViewModel? Timeline
    {
        get => (TimelineViewModel?)GetValue(TimelineProperty);
        set => SetValue(TimelineProperty, value);
    }

    public bool ShowAxes
    {
        get => (bool)GetValue(ShowAxesProperty);
        set => SetValue(ShowAxesProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public double FillOpacity
    {
        get => (double)GetValue(FillOpacityProperty);
        set => SetValue(FillOpacityProperty, value);
    }

    public bool ShowTooltip
    {
        get => (bool)GetValue(ShowTooltipProperty);
        set => SetValue(ShowTooltipProperty, value);
    }

    protected override int VisualChildrenCount => _children.Count;

    protected override Visual GetVisualChild(int index) => _children[index];

    protected override void OnRender(DrawingContext drawingContext)
    {
        // Transparent surface so the whole area receives mouse input.
        drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        Render();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        _mouse = e.GetPosition(this);
        UpdateHoverFromMouse();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _mouse = null;
        if (_timeline is not null)
        {
            _timeline.HoverTime = null;
        }

        RenderOverlay();
    }

    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var chart = (LiveLineChart)d;
        if (chart._attached)
        {
            chart.Detach();
            chart.Attach();
        }
    }

    private static void OnAppearanceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((LiveLineChart)d).Render();

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

        Render();
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

    private void OnThemeChanged(object? sender, EventArgs e) => Dispatcher.InvokeAsync(Render);

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

        Render();
    }

    private void OnSeriesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ChartSeriesViewModel.IsVisible) or nameof(ChartSeriesViewModel.MetricId))
        {
            _smoothedAutoMax = double.NaN;
            Render();
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
                Render();
                break;
            case nameof(TimelineViewModel.Window):
                _smoothedAutoMax = double.NaN;
                Render();
                break;
            case nameof(TimelineViewModel.HoverTime):
                RenderOverlay();
                break;
        }
    }

    // -------------------------------------------------------------------------------------------
    // Rendering
    // -------------------------------------------------------------------------------------------

    private void Render()
    {
        var chart = _chart;
        var timeline = _timeline;
        if (!_attached || chart is null || timeline is null || !IsVisible || ActualWidth < 24 || ActualHeight < 24)
        {
            return;
        }

        double width = ActualWidth;
        double height = ActualHeight;
        bool axes = ShowAxes;
        double left = axes ? 44 : 0;
        double right = axes ? 6 : 0;
        double top = axes ? 10 : 4;
        double bottom = axes ? 22 : 0;
        _plotRect = new Rect(left, top, Math.Max(1, width - left - right), Math.Max(1, height - top - bottom));
        _clip.Rect = _plotRect;

        long interval = Math.Max(50, timeline.IntervalMilliseconds);
        long window = Math.Max(1000, timeline.WindowMilliseconds);
        long edge = timeline.RightEdge;
        bool smooth = timeline.SmoothScrolling && !timeline.IsFrozen && window <= 300_000 && edge > 0;
        long plotRight = smooth ? edge - interval : edge;
        _from = plotRight - window;
        _pxPerMs = _plotRect.Width / window;

        CollectData(chart, timeline, edge, interval, window);
        ComputeScale(chart);

        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var typeface = ChartFonts.MonoRegular;
        var gridColor = ChartColors.Theme("ChartGrid");
        var axisBrush = ChartColors.Solid(ChartColors.Theme("ChartAxis"));

        using (var dc = _grid.RenderOpen())
        {
            var gridPen = ChartColors.Pen(gridColor, 1, round: false);
            double step = AxisScale.NiceStep(_yMax - _yMin, axes ? 4 : 2);
            for (double v = _yMin; v <= _yMax + (step * 0.001); v += step)
            {
                double y = Math.Round(Y(v)) + 0.5;
                dc.DrawLine(gridPen, new Point(_plotRect.Left, y), new Point(_plotRect.Right, y));
                if (axes)
                {
                    var text = Text(chart.FormatAxis(v), typeface, axisBrush, pixelsPerDip);
                    dc.DrawText(text, new Point(_plotRect.Left - 8 - text.Width, y - (text.Height / 2)));
                }
            }

            if (axes)
            {
                DrawTimeLabels(dc, timeline, window, edge, ChartFonts.UiRegular, axisBrush, pixelsPerDip);
            }

            if (chart.ReferenceValue is double reference && double.IsFinite(reference) && reference >= _yMin && reference <= _yMax)
            {
                var referenceColor = ChartColors.Theme("Critical");
                double y = Math.Round(Y(reference)) + 0.5;
                dc.DrawLine(ChartColors.DashedPen(Color.FromArgb(170, referenceColor.R, referenceColor.G, referenceColor.B), 1), new Point(_plotRect.Left, y), new Point(_plotRect.Right, y));
                string label = chart.Series.Count > 0 ? $"{chart.ReferenceLabel} · {chart.Series[0].Format(reference)}" : chart.ReferenceLabel ?? string.Empty;
                var text = Text(label, ChartFonts.UiRegular, ChartColors.Solid(referenceColor), pixelsPerDip);
                dc.DrawText(text, new Point(_plotRect.Right - text.Width - 4, y - text.Height - 2));
            }

            if (_rendered.Count == 0)
            {
                var text = Text("No readings available", ChartFonts.UiRegular, axisBrush, pixelsPerDip, 12);
                dc.DrawText(text, new Point(_plotRect.Left + ((_plotRect.Width - text.Width) / 2), _plotRect.Top + ((_plotRect.Height - text.Height) / 2)));
            }
        }

        using (var dc = _plot.RenderOpen())
        {
            double gapMs = Math.Max(interval * 3.5, window / Math.Max(16, _plotRect.Width / 1.5) * 2.5);
            foreach (var series in _rendered)
            {
                DrawSeries(dc, series, gapMs);
            }
        }

        if (smooth && IsVisible)
        {
            var animation = new DoubleAnimation(0, -interval * _pxPerMs, TimeSpan.FromMilliseconds(interval));
            _shift.BeginAnimation(TranslateTransform.XProperty, animation);
        }
        else
        {
            StopScrolling();
        }

        if (_mouse is not null && IsMouseOver)
        {
            UpdateHoverFromMouse();
        }

        RenderOverlay();
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
                double nice = target <= 0 ? 1 : AxisScale.NiceRange(0, target, 4).Max;
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

    private void DrawSeries(DrawingContext dc, RenderedSeries series, double gapMs)
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
                dc.DrawEllipse(ChartColors.Solid(series.Color), null, p, 2, 2);
            }
            else if (count > 1)
            {
                DrawSegment(dc, series, start, count);
            }

            start = i;
        }
    }

    private void DrawSegment(DrawingContext dc, RenderedSeries series, int start, int count)
    {
        var xs = new double[count];
        var ys = new double[count];
        for (int k = 0; k < count; k++)
        {
            var p = ToScreen(series.Points[start + k]);
            xs[k] = p.X;
            ys[k] = p.Y;
        }

        // Ensure strictly increasing x for the curve maths.
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
            ctx.BeginFigure(new Point(xs[0], ys[0]), false, false);
            AppendPath(ctx, xs, ys, tangents);
        }

        line.Freeze();

        if (series.Source.Fill && FillOpacity > 0)
        {
            double baseline = Y(_yMin);
            var area = new StreamGeometry();
            using (var ctx = area.Open())
            {
                ctx.BeginFigure(new Point(xs[0], baseline), true, true);
                ctx.LineTo(new Point(xs[0], ys[0]), false, false);
                AppendPath(ctx, xs, ys, tangents);
                ctx.LineTo(new Point(xs[count - 1], baseline), false, false);
            }

            area.Freeze();
            dc.DrawGeometry(ChartColors.AreaFill(series.Color, FillOpacity), null, area);
        }

        dc.DrawGeometry(null, ChartColors.Pen(series.Color, StrokeThickness), line);
    }

    private static void AppendPath(StreamGeometryContext ctx, double[] xs, double[] ys, double[]? tangents)
    {
        int count = xs.Length;
        if (tangents is null)
        {
            for (int k = 1; k < count; k++)
            {
                ctx.LineTo(new Point(xs[k], ys[k]), true, true);
            }

            return;
        }

        for (int k = 0; k < count - 1; k++)
        {
            var (x1, y1, x2, y2) = MonotoneCurve.ControlPoints(xs, ys, tangents, k);
            ctx.BezierTo(new Point(x1, y1), new Point(x2, y2), new Point(xs[k + 1], ys[k + 1]), true, true);
        }
    }

    private void DrawTimeLabels(DrawingContext dc, TimelineViewModel timeline, long window, long edge, Typeface typeface, Brush brush, double pixelsPerDip)
    {
        double y = _plotRect.Bottom + 5;
        var leftText = Text(FormatAgo(window), typeface, brush, pixelsPerDip);
        dc.DrawText(leftText, new Point(_plotRect.Left, y));
        var middleText = Text(FormatAgo(window / 2), typeface, brush, pixelsPerDip);
        dc.DrawText(middleText, new Point(_plotRect.Left + ((_plotRect.Width - middleText.Width) / 2), y));
        string rightLabel = timeline.IsFrozen ? timeline.ToLocalTime(edge).ToString("T", CultureInfo.CurrentCulture) : "now";
        var rightText = Text(rightLabel, typeface, brush, pixelsPerDip);
        dc.DrawText(rightText, new Point(_plotRect.Right - rightText.Width, y));
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

    private void RenderOverlay()
    {
        using var dc = _overlay.RenderOpen();
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

        if (!ShowTooltip || !IsMouseOver || _mouse is null || rows.Count == 0)
        {
            return;
        }

        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var typeface = ChartFonts.UiRegular;
        var strong = ChartFonts.MonoSemiBold;
        var textBrush = ChartColors.Solid(ChartColors.Theme("Text"));
        var secondary = ChartColors.Solid(ChartColors.Theme("TextSecondary"));

        var header = Text(timeline.ToLocalTime(hover).ToString("T", CultureInfo.CurrentCulture), typeface, secondary, pixelsPerDip, 11);
        var lines = rows.Select(r => (r.Color, Label: Text(r.Label, typeface, secondary, pixelsPerDip, 12), Value: Text(r.Value, strong, textBrush, pixelsPerDip, 12))).ToList();
        double labelWidth = lines.Max(l => l.Label.Width);
        double valueWidth = lines.Max(l => l.Value.Width);
        double lineHeight = 20;
        double boxWidth = Math.Max(header.Width, 14 + labelWidth + 16 + valueWidth) + 24;
        double boxHeight = 12 + header.Height + 6 + (lines.Count * lineHeight) + 8;

        double bx = x + 14;
        if (bx + boxWidth > ActualWidth - 2)
        {
            bx = x - 14 - boxWidth;
        }

        double by = Math.Clamp(_mouse.Value.Y - (boxHeight / 2), 2, Math.Max(2, ActualHeight - boxHeight - 2));
        var box = new Rect(Math.Max(2, bx), by, boxWidth, boxHeight);
        dc.DrawRoundedRectangle(ChartColors.Solid(ChartColors.Theme("Tooltip")), ChartColors.Pen(ChartColors.Theme("TooltipBorder"), 1, round: false), box, 8, 8);
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
        _shift.BeginAnimation(TranslateTransform.XProperty, null);
        _shift.X = 0;
    }

    private Point ToScreen(HistoryPoint p) => new(_plotRect.Left + ((p.T - _from) * _pxPerMs), Y(p.V));

    private double Y(double value)
    {
        double range = _yMax - _yMin;
        double clamped = Math.Clamp(value, _yMin - (range * 0.02), _yMax + (range * 0.02));
        return _plotRect.Bottom - ((clamped - _yMin) / range * _plotRect.Height);
    }

    private static FormattedText Text(string text, Typeface typeface, Brush brush, double pixelsPerDip, double size = AxisFontSize) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, size, brush, null, TextFormattingMode.Display, pixelsPerDip);

    private sealed record RenderedSeries(ChartSeriesViewModel Source, HistoryPoint[] Points, Color Color, double Min, double Max);
}
