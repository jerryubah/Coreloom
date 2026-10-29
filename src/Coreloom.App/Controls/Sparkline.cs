using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using Coreloom.Core.Charting;
using Coreloom.Core.History;
using Coreloom.ViewModels.Infrastructure;

namespace Coreloom.App.Controls;

/// <summary>
/// Minimal area sparkline for cards and the per-thread grid. Reads directly from the history store when the
/// timeline advances; no axes, no interaction. Very cheap: one small geometry per frame.
/// </summary>
public sealed class Sparkline : FrameworkElement
{
    public static readonly DependencyProperty MetricIdProperty = DependencyProperty.Register(
        nameof(MetricId), typeof(string), typeof(Sparkline), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TimelineProperty = DependencyProperty.Register(
        nameof(Timeline), typeof(TimelineViewModel), typeof(Sparkline), new PropertyMetadata(null, OnTimelineChanged));

    public static readonly DependencyProperty ColorKeyProperty = DependencyProperty.Register(
        nameof(ColorKey), typeof(string), typeof(Sparkline), new FrameworkPropertyMetadata(ColorKeys.Accent, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(Sparkline), new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SecondsProperty = DependencyProperty.Register(
        nameof(Seconds), typeof(double), typeof(Sparkline), new FrameworkPropertyMetadata(60.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillOpacityProperty = DependencyProperty.Register(
        nameof(FillOpacity), typeof(double), typeof(Sparkline), new FrameworkPropertyMetadata(0.36, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness), typeof(double), typeof(Sparkline), new FrameworkPropertyMetadata(1.5, FrameworkPropertyMetadataOptions.AffectsRender));

    private HistoryPoint[] _buffer = new HistoryPoint[256];
    private double[] _xs = new double[256];
    private double[] _ys = new double[256];
    private TimelineViewModel? _subscribed;

    public Sparkline()
    {
        Loaded += (_, _) => Subscribe(Timeline);
        Unloaded += (_, _) => Subscribe(null);
        IsHitTestVisible = false;
    }

    public string? MetricId
    {
        get => (string?)GetValue(MetricIdProperty);
        set => SetValue(MetricIdProperty, value);
    }

    public TimelineViewModel? Timeline
    {
        get => (TimelineViewModel?)GetValue(TimelineProperty);
        set => SetValue(TimelineProperty, value);
    }

    public string? ColorKey
    {
        get => (string?)GetValue(ColorKeyProperty);
        set => SetValue(ColorKeyProperty, value);
    }

    /// <summary>Fixed maximum (e.g. 100 for percentages); NaN scales to the data.</summary>
    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public double Seconds
    {
        get => (double)GetValue(SecondsProperty);
        set => SetValue(SecondsProperty, value);
    }

    public double FillOpacity
    {
        get => (double)GetValue(FillOpacityProperty);
        set => SetValue(FillOpacityProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    private static void OnTimelineChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var spark = (Sparkline)d;
        if (spark.IsLoaded)
        {
            spark.Subscribe(e.NewValue as TimelineViewModel);
        }

        spark.InvalidateVisual();
    }

    private void Subscribe(TimelineViewModel? timeline)
    {
        if (_subscribed is not null)
        {
            _subscribed.PropertyChanged -= OnTimelinePropertyChanged;
        }

        _subscribed = timeline;
        if (_subscribed is not null)
        {
            _subscribed.PropertyChanged += OnTimelinePropertyChanged;
        }
    }

    private void OnTimelinePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TimelineViewModel.RightEdge) && IsVisible)
        {
            InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        var timeline = Timeline;
        string? id = MetricId;
        double width = ActualWidth;
        double height = ActualHeight;
        if (timeline is null || string.IsNullOrEmpty(id) || width < 4 || height < 4)
        {
            return;
        }

        var series = timeline.History.Get(id);
        if (series is null)
        {
            return;
        }

        long to = timeline.RightEdge;
        long window = (long)(Seconds * 1000);
        long from = to - window;
        int n = series.CopyRange(from, to, ref _buffer, includeNeighbors: true);
        if (n < 2)
        {
            return;
        }

        double max = Maximum;
        if (double.IsNaN(max))
        {
            max = 0;
            for (int i = 0; i < n; i++)
            {
                max = Math.Max(max, _buffer[i].V);
            }

            max = max <= 0 ? 1 : AxisScale.NiceCeiling(max * 1.1);
        }

        double stroke = StrokeThickness;
        double top = stroke;
        double usable = Math.Max(1, height - stroke - 1);
        if (_xs.Length < n)
        {
            _xs = new double[_buffer.Length];
            _ys = new double[_buffer.Length];
        }

        var xs = _xs;
        var ys = _ys;
        for (int i = 0; i < n; i++)
        {
            xs[i] = (_buffer[i].T - from) * width / window;
            ys[i] = top + usable - (Math.Clamp(_buffer[i].V / max, 0, 1) * usable);
        }

        var color = ChartColors.Series(ColorKey);
        var line = new StreamGeometry();
        using (var ctx = line.Open())
        {
            ctx.BeginFigure(new Point(xs[0], ys[0]), false, false);
            for (int i = 1; i < n; i++)
            {
                ctx.LineTo(new Point(xs[i], ys[i]), true, true);
            }
        }

        line.Freeze();

        if (FillOpacity > 0)
        {
            var area = new StreamGeometry();
            using (var ctx = area.Open())
            {
                ctx.BeginFigure(new Point(xs[0], height), true, true);
                for (int i = 0; i < n; i++)
                {
                    ctx.LineTo(new Point(xs[i], ys[i]), false, true);
                }

                ctx.LineTo(new Point(xs[n - 1], height), false, false);
            }

            area.Freeze();
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, width, height)));
            // The fill fades out at both ends, so it has no hard left and right edges on the card.
            dc.PushOpacityMask(ChartColors.EdgeFade);
            dc.DrawGeometry(ChartColors.AreaFill(color, FillOpacity), null, area);
            dc.Pop();
            dc.DrawGeometry(null, ChartColors.Pen(color, stroke), line);
            dc.Pop();
        }
        else
        {
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, width, height)));
            dc.DrawGeometry(null, ChartColors.Pen(color, stroke), line);
            dc.Pop();
        }
    }
}
