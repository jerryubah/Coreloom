using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Coreloom.Core.Formatting;
using Coreloom.Core.Hardware;
using Coreloom.Core.Telemetry;

namespace Coreloom.ViewModels.Infrastructure;

/// <summary>A label/value pair for specification lists.</summary>
public sealed record KeyValueItem(string Key, string Value) : StaticRecord;

/// <summary>A segment of a donut chart.</summary>
public sealed class DonutSegmentViewModel : ObservableObject
{
    private double _value;
    private string _valueText = string.Empty;

    private string _colorKey;

    public DonutSegmentViewModel(string label, string colorKey)
    {
        Label = label;
        _colorKey = colorKey;
    }

    public string Label { get; }

    /// <summary>Series color; usage segments follow the gauge scale (see <see cref="ColorKeys.ForLevel"/>).</summary>
    public string ColorKey
    {
        get => _colorKey;
        set => SetProperty(ref _colorKey, value);
    }

    public double Value
    {
        get => _value;
        set
        {
            if (!_value.Equals(value))
            {
                _value = value;
                OnPropertyChanged();
            }
        }
    }

    public string ValueText
    {
        get => _valueText;
        set => SetProperty(ref _valueText, value);
    }
}

/// <summary>One row of a sensor table.</summary>
public sealed class SensorRowViewModel : ObservableObject
{
    private string _value = "—";
    private bool _hasValue;
    private string? _source;
    private bool _isDerived;
    private string _toolTip = string.Empty;

    public SensorRowViewModel(MetricDescriptor descriptor)
    {
        Descriptor = descriptor;
    }

    public MetricDescriptor Descriptor { get; }

    public string Id => Descriptor.Id;

    public string Name => Descriptor.Name;

    public string Group => Descriptor.Group ?? Descriptor.Kind.ToString();

    public string Device => Descriptor.DeviceName ?? string.Empty;

    public string Value
    {
        get => _value;
        private set => SetProperty(ref _value, value);
    }

    public bool HasValue
    {
        get => _hasValue;
        private set => SetProperty(ref _hasValue, value);
    }

    public string? Source
    {
        get => _source;
        private set => SetProperty(ref _source, value);
    }

    public bool IsDerived
    {
        get => _isDerived;
        private set => SetProperty(ref _isDerived, value);
    }

    public string ToolTip
    {
        get => _toolTip;
        private set => SetProperty(ref _toolTip, value);
    }

    public void Update(TelemetryFrame frame, UnitFormatter formatter)
    {
        if (frame.TryGetSample(Descriptor.Id, out var sample))
        {
            HasValue = true;
            Value = Descriptor.Kind == MetricKind.Voltage && sample.Value < 2.5 && Descriptor.Category == MetricCategory.Gpu
                ? formatter.Millivolts(sample.Value).ToString()
                : formatter.Format(Descriptor.Kind, sample.Value).ToString();
            Source = sample.Source.Provider;
            IsDerived = sample.Source.Provenance == Provenance.Derived;
            ToolTip = $"{Descriptor.Id}\n{sample.Source.Provider} · {sample.Source.Provenance}{(sample.Source.Note is { Length: > 0 } note ? "\n" + note : string.Empty)}";
        }
        else
        {
            HasValue = false;
            Value = "—";
            Source = null;
            IsDerived = false;
            ToolTip = $"{Descriptor.Id}\nUnavailable: {Descriptor.UnavailableReason ?? "no reading in the latest sample"}";
        }
    }
}

/// <summary>A named group of sensor rows (e.g. "Temperatures").</summary>
public sealed class SensorGroupViewModel : StaticItem
{
    public SensorGroupViewModel(string name, IReadOnlyList<SensorRowViewModel> rows)
    {
        Name = name;
        Rows = rows;
    }

    public string Name { get; }

    public IReadOnlyList<SensorRowViewModel> Rows { get; }
}

/// <summary>
/// A filterable, self-refreshing table of sensors (canonical and raw). Rows are rebuilt only when the
/// catalog changes; values are refreshed every frame while the owning page is visible.
/// </summary>
public sealed class SensorTableViewModel : ObservableObject
{
    private readonly Func<MetricDescriptor, bool> _filter;
    private int _catalogVersion = -1;
    private bool _showRaw;

    public SensorTableViewModel(Func<MetricDescriptor, bool> filter, bool showRaw = true)
    {
        _filter = filter;
        _showRaw = showRaw;
    }

    public ObservableCollection<SensorRowViewModel> Rows { get; } = [];

    /// <summary>Rows grouped by <see cref="SensorRowViewModel.Group"/>, canonical metrics first.</summary>
    public ObservableCollection<SensorGroupViewModel> Groups { get; } = [];

    /// <summary>The groups split into two columns of similar height (see <see cref="SensorColumns"/>).</summary>
    public ObservableCollection<SensorGroupViewModel> FirstColumn { get; } = [];

    public ObservableCollection<SensorGroupViewModel> SecondColumn { get; } = [];

    public bool HasSecondColumn => SecondColumn.Count > 0;

    public bool IsEmpty => Rows.Count == 0;

    public bool ShowRaw
    {
        get => _showRaw;
        set
        {
            if (SetProperty(ref _showRaw, value))
            {
                _catalogVersion = -1;
            }
        }
    }

    public int Count => Rows.Count;

    public void Invalidate() => _catalogVersion = -1;

    public void Update(TelemetryFrame frame, UnitFormatter formatter, MetricCatalog catalog)
    {
        if (catalog.Version != _catalogVersion)
        {
            _catalogVersion = catalog.Version;
            var wanted = catalog.Where(d => _filter(d) && (_showRaw || !d.IsRaw))
                .OrderBy(d => d.IsRaw)
                .ThenBy(d => d.Group, StringComparer.OrdinalIgnoreCase)
                .ThenBy(d => d.DeviceName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(d => d.SortOrder)
                .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (wanted.Length != Rows.Count || wanted.Where((d, i) => !ReferenceEquals(d, Rows[i].Descriptor)).Any())
            {
                Rows.Clear();
                foreach (var d in wanted)
                {
                    Rows.Add(new SensorRowViewModel(d));
                }

                Groups.Clear();
                foreach (var group in Rows.GroupBy(r => r.Descriptor.IsRaw ? $"{r.Group} · raw" : r.Group))
                {
                    Groups.Add(new SensorGroupViewModel(group.Key, group.ToArray()));
                }

                var (first, second) = SensorColumns.Split(Groups);
                FirstColumn.Clear();
                SecondColumn.Clear();
                foreach (var group in first)
                {
                    FirstColumn.Add(group);
                }

                foreach (var group in second)
                {
                    SecondColumn.Add(group);
                }

                OnPropertyChanged(nameof(Count));
                OnPropertyChanged(nameof(IsEmpty));
                OnPropertyChanged(nameof(HasSecondColumn));
            }
        }

        foreach (var row in Rows)
        {
            row.Update(frame, formatter);
        }
    }
}

/// <summary>A row in a "top processes" list.</summary>
public sealed class ProcessRowViewModel : ObservableObject
{
    private string _name = string.Empty;
    private string _primary = string.Empty;
    private string _secondary = string.Empty;
    private double _fraction;

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string Primary
    {
        get => _primary;
        set => SetProperty(ref _primary, value);
    }

    public string Secondary
    {
        get => _secondary;
        set => SetProperty(ref _secondary, value);
    }

    public double Fraction
    {
        get => _fraction;
        set
        {
            if (!_fraction.Equals(value))
            {
                _fraction = value;
                OnPropertyChanged();
            }
        }
    }
}

public enum ProcessSort
{
    Cpu,
    Memory,
}

/// <summary>Top-N processes, sampled on demand at a slow cadence while visible.</summary>
public sealed class ProcessListViewModel : ObservableObject
{
    private readonly IProcessSampler _sampler;
    private readonly ProcessSort _sort;
    private readonly int _count;
    private DateTime _lastSample = DateTime.MinValue;

    /// <param name="count">Rows always shown.</param>
    /// <param name="capacity">Rows kept up to date, shown when the card is taller (at least <paramref name="count"/>).</param>
    public ProcessListViewModel(IProcessSampler sampler, ProcessSort sort, int count = 6, int capacity = 0)
    {
        _sampler = sampler;
        _sort = sort;
        MinRows = count;
        _count = Math.Max(count, capacity);
        for (int i = 0; i < _count; i++)
        {
            Rows.Add(new ProcessRowViewModel());
        }
    }

    public ObservableCollection<ProcessRowViewModel> Rows { get; }  = [];

    /// <summary>Rows always shown; a card made taller to line up with its neighbour shows more of <see cref="Rows"/>.</summary>
    public int MinRows { get; }

    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(2);

    public void Reset() => _lastSample = DateTime.MinValue;

    public void Update(UnitFormatter formatter, double totalMemoryBytes)
    {
        var now = DateTime.UtcNow;
        if (now - _lastSample < Interval)
        {
            return;
        }

        _lastSample = now;
        IReadOnlyList<ProcessUsage> sample;
        try
        {
            sample = _sampler.Sample();
        }
        catch
        {
            return;
        }

        var ordered = (_sort == ProcessSort.Cpu
                ? sample.Where(p => !p.Name.Equals("System", StringComparison.OrdinalIgnoreCase) || p.CpuPercent > 0).OrderByDescending(p => p.CpuPercent).ThenByDescending(p => p.WorkingSetBytes)
                : sample.OrderByDescending(p => p.WorkingSetBytes))
            .Take(_count)
            .ToArray();

        for (int i = 0; i < _count; i++)
        {
            var row = Rows[i];
            if (i >= ordered.Length)
            {
                row.Name = string.Empty;
                row.Primary = string.Empty;
                row.Secondary = string.Empty;
                row.Fraction = 0;
                continue;
            }

            var p = ordered[i];
            row.Name = p.InstanceCount > 1 ? $"{TrimExe(p.Name)}  ×{p.InstanceCount}" : TrimExe(p.Name);
            if (_sort == ProcessSort.Cpu)
            {
                row.Primary = formatter.Percent(p.CpuPercent, 1).ToString();
                row.Secondary = formatter.Bytes(p.WorkingSetBytes).ToString();
                row.Fraction = Math.Clamp(p.CpuPercent / 100.0, 0, 1);
            }
            else
            {
                row.Primary = formatter.Bytes(p.WorkingSetBytes).ToString();
                row.Secondary = formatter.Percent(p.CpuPercent, 1).ToString() + " CPU";
                row.Fraction = totalMemoryBytes > 0 ? Math.Clamp(p.WorkingSetBytes / totalMemoryBytes, 0, 1) : 0;
            }
        }
    }

    private static string TrimExe(string name) => name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
}

/// <summary>
/// Splits sensor groups into two columns of similar height so long sensor lists sit side by side instead of making
/// one very tall card. Groups keep their order (the first column gets the first groups); a long group may continue
/// at the top of the second column ("Load, continued") when that balances the columns much better than moving the
/// whole group. Short lists stay in one column.
/// </summary>
public static class SensorColumns
{
    /// <summary>A group's title counts as this many rows of height.</summary>
    public const double HeaderWeight = 1.6;

    /// <summary>Lists with at most this many rows stay in one column.</summary>
    public const int SingleColumnMaxRows = 8;

    /// <summary>Both parts of a split group keep at least this many rows.</summary>
    public const int MinRowsPerPart = 3;

    public static (IReadOnlyList<SensorGroupViewModel> First, IReadOnlyList<SensorGroupViewModel> Second) Split(IReadOnlyList<SensorGroupViewModel> groups)
    {
        int totalRows = groups.Sum(g => g.Rows.Count);
        if (groups.Count == 0 || totalRows <= SingleColumnMaxRows)
        {
            return (groups.ToArray(), []);
        }

        double total = groups.Sum(Weight);

        // Best split between whole groups.
        int bestBoundary = -1;
        double bestBoundaryGap = double.MaxValue;
        double prefix = 0;
        for (int k = 1; k < groups.Count; k++)
        {
            prefix += Weight(groups[k - 1]);
            double gap = Math.Abs(prefix - (total - prefix));
            if (gap < bestBoundaryGap)
            {
                bestBoundaryGap = gap;
                bestBoundary = k;
            }
        }

        // Best split inside a group (the continued part gets its own title).
        int bestGroup = -1;
        int bestRow = 0;
        double bestInnerGap = double.MaxValue;
        prefix = 0;
        for (int g = 0; g < groups.Count; g++)
        {
            int rows = groups[g].Rows.Count;
            for (int r = MinRowsPerPart; r <= rows - MinRowsPerPart; r++)
            {
                double left = prefix + HeaderWeight + r;
                double right = total - left + HeaderWeight;
                double gap = Math.Abs(left - right);
                if (gap < bestInnerGap)
                {
                    bestInnerGap = gap;
                    bestGroup = g;
                    bestRow = r;
                }
            }

            prefix += Weight(groups[g]);
        }

        // Moving whole groups reads better; only split a group when that is clearly more even.
        if (bestGroup >= 0 && bestInnerGap + 4 < bestBoundaryGap)
        {
            var group = groups[bestGroup];
            var first = groups.Take(bestGroup).Append(new SensorGroupViewModel(group.Name, group.Rows.Take(bestRow).ToArray())).ToArray();
            var second = new[] { new SensorGroupViewModel($"{group.Name}, continued", group.Rows.Skip(bestRow).ToArray()) }
                .Concat(groups.Skip(bestGroup + 1)).ToArray();
            return (first, second);
        }

        return bestBoundary > 0
            ? (groups.Take(bestBoundary).ToArray(), groups.Skip(bestBoundary).ToArray())
            : (groups.ToArray(), []);
    }

    private static double Weight(SensorGroupViewModel group) => HeaderWeight + group.Rows.Count;
}
