using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Coreloom.Core.Charting;
using Coreloom.Core.History;
using Coreloom.Mac.Behaviors;
using Coreloom.Mac.Themes;
using Coreloom.ViewModels.Infrastructure;

namespace Coreloom.Mac.Controls;

/// <summary>
/// Minimal area sparkline for cards and the per-thread grid. Reads directly from the history store when the timeline
/// advances; no axes, no interaction. One small geometry per frame.
/// </summary>
public sealed class Sparkline : Control
{
    public static readonly StyledProperty<string?> MetricIdProperty = AvaloniaProperty.Register<Sparkline, string?>(nameof(MetricId));

    public static readonly StyledProperty<TimelineViewModel?> TimelineProperty = AvaloniaProperty.Register<Sparkline, TimelineViewModel?>(nameof(Timeline));

    public static readonly StyledProperty<string?> ColorKeyProperty = AvaloniaProperty.Register<Sparkline, string?>(nameof(ColorKey), ColorKeys.Accent);

    public static readonly StyledProperty<double> MaximumProperty = AvaloniaProperty.Register<Sparkline, double>(nameof(Maximum), double.NaN);

    public static readonly StyledProperty<double> SecondsProperty = AvaloniaProperty.Register<Sparkline, double>(nameof(Seconds), 60.0);

    public static readonly StyledProperty<double> FillOpacityProperty = AvaloniaProperty.Register<Sparkline, double>(nameof(FillOpacity), 0.42);

    public static readonly StyledProperty<double> StrokeThicknessProperty = AvaloniaProperty.Register<Sparkline, double>(nameof(StrokeThickness), 1.5);

    private HistoryPoint[] _buffer = new HistoryPoint[256];
    private double[] _xs = new double[256];
    private double[] _ys = new double[256];
    private TimelineViewModel? _subscribed;

    static Sparkline() => AffectsRender<Sparkline>(MetricIdProperty, ColorKeyProperty, MaximumProperty, SecondsProperty, FillOpacityProperty, StrokeThicknessProperty);

    public Sparkline()
    {
        Loaded += (_, _) => Subscribe(Timeline);
        Unloaded += (_, _) => Subscribe(null);
        IsHitTestVisible = false;
    }

    public string? MetricId
    {
        get => GetValue(MetricIdProperty);
        set => SetValue(MetricIdProperty, value);
    }

    public TimelineViewModel? Timeline
    {
        get => GetValue(TimelineProperty);
        set => SetValue(TimelineProperty, value);
    }

    public string? ColorKey
    {
        get => GetValue(ColorKeyProperty);
        set => SetValue(ColorKeyProperty, value);
    }

    /// <summary>Fixed maximum (e.g. 100 for percentages); NaN scales to the data.</summary>
    public double Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public double Seconds
    {
        get => GetValue(SecondsProperty);
        set => SetValue(SecondsProperty, value);
    }

    public double FillOpacity
    {
        get => GetValue(FillOpacityProperty);
        set => SetValue(FillOpacityProperty, value);
    }

    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TimelineProperty)
        {
            if (IsLoaded)
            {
                Subscribe(change.NewValue as TimelineViewModel);
            }

            InvalidateVisual();
        }
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
        if (e.PropertyName == nameof(TimelineViewModel.RightEdge) && IsEffectivelyVisible)
        {
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext dc)
    {
        var timeline = Timeline;
        string? id = MetricId;
        double width = Bounds.Width;
        double height = Bounds.Height;
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
            ctx.BeginFigure(new Point(xs[0], ys[0]), false);
            for (int i = 1; i < n; i++)
            {
                ctx.LineTo(new Point(xs[i], ys[i]));
            }

            ctx.EndFigure(false);
        }

        using (dc.PushClip(new Rect(0, 0, width, height)))
        {
            if (FillOpacity > 0)
            {
                var area = new StreamGeometry();
                using (var ctx = area.Open())
                {
                    ctx.BeginFigure(new Point(xs[0], height), true);
                    for (int i = 0; i < n; i++)
                    {
                        ctx.LineTo(new Point(xs[i], ys[i]));
                    }

                    ctx.LineTo(new Point(xs[n - 1], height));
                    ctx.EndFigure(true);
                }

                // The fill fades out at both ends, so it has no hard left and right edges on the card.
                using (dc.PushOpacityMask(ChartColors.EdgeFade, new Rect(0, 0, width, height)))
                {
                    dc.DrawGeometry(ChartColors.AreaFill(color, FillOpacity), null, area);
                }
            }

            dc.DrawGeometry(null, ChartColors.Pen(color, stroke), line);
        }
    }
}

/// <summary>
/// Animated donut chart for <see cref="DonutSegmentViewModel"/> items. Segments sweep in on first display and ease
/// between proportions when values change.
/// </summary>
public sealed class DonutChart : Control
{
    public static readonly StyledProperty<IEnumerable?> SegmentsProperty = AvaloniaProperty.Register<DonutChart, IEnumerable?>(nameof(Segments));

    public static readonly StyledProperty<double> ThicknessProperty = AvaloniaProperty.Register<DonutChart, double>(nameof(Thickness), 14.0);

    public static readonly StyledProperty<double> GapDegreesProperty = AvaloniaProperty.Register<DonutChart, double>(nameof(GapDegrees), 3.0);

    private readonly List<DonutSegmentViewModel> _items = [];
    private double[] _from = [];
    private double[] _to = [];
    private double _progress = 1;
    private Animator.Tween? _tween;
    private INotifyCollectionChanged? _collection;
    private bool _hasShown;
    private bool _attached;

    static DonutChart() => AffectsRender<DonutChart>(ThicknessProperty, GapDegreesProperty);

    public DonutChart()
    {
        IsHitTestVisible = false;
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
    }

    public IEnumerable? Segments
    {
        get => GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    public double Thickness
    {
        get => GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    public double GapDegrees
    {
        get => GetValue(GapDegreesProperty);
        set => SetValue(GapDegreesProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SegmentsProperty && _attached)
        {
            Detach();
            Attach();
        }
    }

    private void OnThemeChanged(object? sender, EventArgs e) => InvalidateVisual();

    private void Attach()
    {
        if (_attached)
        {
            return;
        }

        _attached = true;
        if (ThemeService.Instance is { } theme)
        {
            theme.ThemeChanged += OnThemeChanged;
        }

        _collection = Segments as INotifyCollectionChanged;
        if (_collection is not null)
        {
            _collection.CollectionChanged += OnCollectionChanged;
        }

        ResubscribeItems();
        Retarget();
    }

    private void Detach()
    {
        if (!_attached)
        {
            return;
        }

        _attached = false;
        if (ThemeService.Instance is { } theme)
        {
            theme.ThemeChanged -= OnThemeChanged;
        }

        if (_collection is not null)
        {
            _collection.CollectionChanged -= OnCollectionChanged;
            _collection = null;
        }

        foreach (var item in _items)
        {
            item.PropertyChanged -= OnItemChanged;
        }

        _items.Clear();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ResubscribeItems();
        Retarget();
    }

    private void ResubscribeItems()
    {
        foreach (var item in _items)
        {
            item.PropertyChanged -= OnItemChanged;
        }

        _items.Clear();
        if (Segments is not null)
        {
            foreach (var item in Segments.OfType<DonutSegmentViewModel>())
            {
                item.PropertyChanged += OnItemChanged;
                _items.Add(item);
            }
        }
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DonutSegmentViewModel.Value))
        {
            Retarget();
        }
        else if (e.PropertyName == nameof(DonutSegmentViewModel.ColorKey))
        {
            InvalidateVisual();
        }
    }

    private void Retarget()
    {
        int count = _items.Count;
        double total = _items.Sum(i => Math.Max(0, i.Value));
        var target = new double[count];
        for (int i = 0; i < count; i++)
        {
            target[i] = total > 0 ? Math.Max(0, _items[i].Value) / total : 0;
        }

        var current = CurrentFractions(count);
        if (target.SequenceEqual(_to) && _hasShown)
        {
            return;
        }

        _from = current;
        _to = target;
        _tween?.Cancel();
        bool animate = AnimatedText.IsGloballyEnabled && IsEffectivelyVisible && total > 0;
        if (!animate)
        {
            _progress = 1;
        }
        else
        {
            _tween = Animator.Run(TimeSpan.FromMilliseconds(_hasShown ? 450 : 700), Ease.CubicOut, t =>
            {
                _progress = t;
                InvalidateVisual();
            });
        }

        _hasShown |= total > 0;
        InvalidateVisual();
    }

    private double[] CurrentFractions(int count)
    {
        double p = _progress;
        var result = new double[count];
        for (int i = 0; i < count; i++)
        {
            double from = i < _from.Length ? _from[i] : 0;
            double to = i < _to.Length ? _to[i] : 0;
            result[i] = from + ((to - from) * p);
        }

        return result;
    }

    public override void Render(DrawingContext dc)
    {
        double size = Math.Min(Bounds.Width, Bounds.Height);
        if (size < 8)
        {
            return;
        }

        double thickness = Math.Min(Thickness, size / 4);
        double radius = (size - thickness) / 2;
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var trackColor = ChartColors.Theme("Track");
        dc.DrawEllipse(null, ChartColors.Pen(trackColor, thickness, round: false), center, radius, radius);

        var fractions = CurrentFractions(_items.Count);
        int nonZero = fractions.Count(f => f > 0.0005);
        double gap = nonZero > 1 ? GapDegrees : 0;
        double angle = -90;
        for (int i = 0; i < _items.Count; i++)
        {
            double sweep = fractions[i] * 360;
            if (sweep <= 0.05)
            {
                continue;
            }

            var pen = ChartColors.Pen(ChartColors.Series(_items[i].ColorKey), thickness, round: false);
            if (sweep >= 359.9)
            {
                dc.DrawEllipse(null, pen, center, radius, radius);
            }
            else
            {
                double drawn = Math.Max(0.1, sweep - gap);
                dc.DrawGeometry(null, pen, RadialGauge.Arc(center, radius, angle + (gap / 2), drawn));
            }

            angle += sweep;
        }
    }
}
