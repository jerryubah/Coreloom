using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Coreloom.Core.Formatting;
using Coreloom.Core.Telemetry;

namespace Coreloom.ViewModels.Infrastructure;

/// <summary>Y-axis behaviour of a chart.</summary>
public enum ChartScale
{
    /// <summary>Fixed 0–100 (utilization).</summary>
    Percent,
    /// <summary>0 to max(100 °C, data) — temperatures stay comparable over time.</summary>
    Temperature,
    /// <summary>0 to a "nice" ceiling above the data (power, clocks, throughput, bytes).</summary>
    Auto,
}

/// <summary>One line in a chart.</summary>
public sealed class ChartSeriesViewModel : ObservableObject
{
    private string _metricId;
    private string _currentText = "—";
    private bool _hasData;
    private bool _isVisible = true;
    private UnitFormatter _formatter = UnitFormatter.Default;

    public ChartSeriesViewModel(string metricId, string label, string colorKey, ValueStyle style, bool fill = true)
    {
        _metricId = metricId;
        Label = label;
        ColorKey = colorKey;
        Style = style;
        Fill = fill;
    }

    public string MetricId
    {
        get => _metricId;
        set => SetProperty(ref _metricId, value);
    }

    public string Label { get; set; }

    public string ColorKey { get; }

    public ValueStyle Style { get; }

    /// <summary>Draw a gradient area under the line.</summary>
    public bool Fill { get; }

    public string CurrentText
    {
        get => _currentText;
        private set => SetProperty(ref _currentText, value);
    }

    public bool HasData
    {
        get => _hasData;
        private set => SetProperty(ref _hasData, value);
    }

    /// <summary>Legend toggle.</summary>
    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }

    /// <summary>Formats a value (with unit) for tooltips and legends.</summary>
    public string Format(double value) => ValueStyles.Format(Style, value, _formatter).ToString();

    /// <summary>Formats a value for axis labels (number only for compact axes).</summary>
    public string FormatAxis(double value) => ValueStyles.Format(Style, value, _formatter).Number;

    public void Update(TelemetryFrame frame, UnitFormatter formatter)
    {
        _formatter = formatter;
        if (frame.TryGetValue(_metricId, out double v))
        {
            HasData = true;
            CurrentText = Format(v);
        }
        else
        {
            HasData = false;
            CurrentText = "—";
        }
    }
}

/// <summary>A live chart composed of one or more series plus an optional reference line (e.g. power limit).</summary>
public sealed class ChartViewModel : ObservableObject
{
    private string _title;
    private string? _subtitle;
    private double? _referenceValue;
    private string? _referenceLabel;
    private bool _hasData;
    private UnitFormatter _formatter = UnitFormatter.Default;

    public ChartViewModel(string title, ChartScale scale, params ChartSeriesViewModel[] series)
    {
        _title = title;
        Scale = scale;
        Series = new ObservableCollection<ChartSeriesViewModel>(series);
    }

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public string? Subtitle
    {
        get => _subtitle;
        set => SetProperty(ref _subtitle, value);
    }

    public ChartScale Scale { get; }

    public ObservableCollection<ChartSeriesViewModel> Series { get; }

    /// <summary>Optional horizontal reference line in base units (power limit, slowdown temperature).</summary>
    public double? ReferenceValue
    {
        get => _referenceValue;
        set
        {
            if (!Nullable.Equals(_referenceValue, value))
            {
                _referenceValue = value;
                OnPropertyChanged();
            }
        }
    }

    public string? ReferenceLabel
    {
        get => _referenceLabel;
        set => SetProperty(ref _referenceLabel, value);
    }

    /// <summary>Optional metric whose current value becomes the reference line each frame.</summary>
    public string? ReferenceMetricId { get; set; }

    /// <summary>When true, a temperature reference is converted for display by the formatter.</summary>
    public bool HasData
    {
        get => _hasData;
        private set => SetProperty(ref _hasData, value);
    }

    /// <summary>Formats an axis tick using the first series' style.</summary>
    public string FormatAxis(double value) => Series.Count > 0 ? Series[0].FormatAxis(value) : value.ToString("0", _formatter.Culture);

    /// <summary>
    /// The size of the unit a byte axis counts in for this top value (1 GB, or 1 GiB for memory on a Mac), so its ticks fall
    /// on round numbers of that unit; 1 for every other axis.
    /// </summary>
    public double AxisFactor(double max)
    {
        if (Series.Count == 0 || !double.IsFinite(max) || max <= 0)
        {
            return 1;
        }

        return Series[0].Style switch
        {
            ValueStyle.Bytes => Math.Pow(_formatter.ByteBase, _formatter.ByteUnitIndex(max)),
            ValueStyle.MemoryBytes => Math.Pow(_formatter.MemoryByteBase, _formatter.MemoryUnitIndex(max)),
            _ => 1,
        };
    }

    /// <summary>An axis tick: byte axes count every tick in the unit of the top one ("0, 2, 4, 6" GB), the rest as <see cref="FormatAxis(double)"/>.</summary>
    public string FormatAxis(double value, double max)
    {
        if (Series.Count == 0 || Series[0].Style is not (ValueStyle.Bytes or ValueStyle.MemoryBytes) || !double.IsFinite(max) || max <= 0)
        {
            return FormatAxis(value);
        }

        double factor = AxisFactor(max);
        double units = value / factor;
        int decimals = Math.Abs(units - Math.Round(units)) < 1e-6 ? 0 : 1;
        return Series[0].Style == ValueStyle.MemoryBytes
            ? _formatter.MemoryBytesAs(value, _formatter.MemoryUnitIndex(max), decimals).Number
            : _formatter.BytesAs(value, _formatter.ByteUnitIndex(max), decimals).Number;
    }

    /// <summary>Unit shown on the axis (e.g. "%", "°C", "W").</summary>
    public string AxisUnit => Series.Count > 0 ? ValueStyles.Format(Series[0].Style, 1, _formatter).Unit : string.Empty;

    public void Update(TelemetryFrame frame, UnitFormatter formatter)
    {
        bool unitChanged = !ReferenceEquals(_formatter, formatter);
        _formatter = formatter;
        bool any = false;
        foreach (var series in Series)
        {
            series.Update(frame, formatter);
            any |= series.HasData;
        }

        HasData = any;
        if (ReferenceMetricId is not null)
        {
            ReferenceValue = frame.GetValue(ReferenceMetricId);
        }

        if (unitChanged)
        {
            OnPropertyChanged(nameof(AxisUnit));
        }
    }
}
