using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Coreloom.Core.Formatting;
using Coreloom.Core.Overlay;
using Coreloom.Core.Settings;
using Coreloom.Core.Telemetry;
using Coreloom.ViewModels.Infrastructure;
using Coreloom.ViewModels.Pages;

namespace Coreloom.ViewModels.Overlay;

/// <summary>Frame rates of running programs (measured by the app from Windows' graphics events).</summary>
public interface IFrameRateSource
{
    /// <summary>False when frame rates can't be measured right now; <see cref="Problem"/> says why.</summary>
    bool IsAvailable { get; }

    string? Problem { get; }

    /// <summary>The frame rate of a process, or null when it isn't drawing frames.</summary>
    FrameRateStats? Get(int processId);

    /// <summary>
    /// The process's latest frame times in milliseconds, oldest first, and how long ago the newest was shown; returns how
    /// many were written.
    /// </summary>
    int RecentFrameTimes(int processId, Span<double> buffer, out double newestAgeMilliseconds);
}

/// <summary>Frame times for the overlay's graph (milliseconds, oldest first), read each time the graph draws.</summary>
public interface IFrameTimeFeed
{
    /// <summary>
    /// Fills <paramref name="buffer"/> with the latest frame times and returns how many (0 when no game is drawing), and
    /// how long ago the newest of them was shown, in milliseconds.
    /// </summary>
    int Read(Span<double> buffer, out double newestAgeMilliseconds);

    /// <summary>The frame time the frame limiter aims for (milliseconds), or 0 when the game in front isn't limited.</summary>
    double TargetFrameTime { get; }
}

/// <summary>One value on the overlay: "72" + "°C", tinted when it reaches a warning level.</summary>
public sealed class OverlayValueViewModel : ObservableObject
{
    private string _number = "—";
    private string _unit = string.Empty;
    private string _color = OverlaySettings.DefaultTextColor;

    public OverlayValueViewModel(string id, OverlayViewModel overlay)
    {
        Id = id;
        Overlay = overlay;
    }

    public string Id { get; }

    /// <summary>The overlay it belongs to.</summary>
    public OverlayViewModel Overlay { get; }

    /// <summary>The line it is on (templates read its text sizes from it).</summary>
    public OverlayGroupViewModel? Group { get; internal set; }

    public string Number
    {
        get => _number;
        set => SetProperty(ref _number, value);
    }

    public string Unit
    {
        get => _unit;
        set
        {
            if (SetProperty(ref _unit, value))
            {
                OnPropertyChanged(nameof(HasUnit));
            }
        }
    }

    public bool HasUnit => _unit.Length > 0;

    /// <summary>#RRGGBB: the text color, or amber/red at warning levels.</summary>
    public string Color
    {
        get => _color;
        set => SetProperty(ref _color, value);
    }
}

/// <summary>
/// One line of the overlay: "GPU  97 %  72 °C  350 W". Each line has its own size (Compact, Standard or Expanded) on
/// top of the overlay's, and its text, graph and spacing follow it.
/// </summary>
public sealed class OverlayGroupViewModel : ObservableObject
{
    public OverlayGroupViewModel(string id, string label, IReadOnlyList<OverlayValueViewModel> values, OverlayViewModel overlay)
    {
        Id = id;
        Label = label;
        Values = values;
        Overlay = overlay;
        foreach (var value in values)
        {
            value.Group = this;
        }

        CycleSizeCommand = new RelayCommand(() => overlay.CycleGroupSize(this));
    }

    public string Id { get; }

    /// <summary>The overlay it belongs to (templates read labels on/off and the layout from it).</summary>
    public OverlayViewModel Overlay { get; }

    /// <summary>The FPS line: the frame-time graph goes with it.</summary>
    public bool IsFrameRate => Id == nameof(OverlayGroup.FrameRate);

    public string Label { get; }

    public IReadOnlyList<OverlayValueViewModel> Values { get; }

    /// <summary>This line's size relative to the rest of the overlay.</summary>
    public double Factor => Enum.TryParse<OverlayGroup>(Id, out var group) ? Overlay.GroupFactor(group) : 1;

    public double FontSize => Math.Round(Overlay.FontSize * Factor, 1);

    public double SmallFontSize => Math.Round(FontSize * 0.78, 1);

    public double HeadlineFontSize => Math.Round(FontSize * 1.3, 1);

    public double GraphWidth => Overlay.IsHorizontal ? Math.Round(FontSize * 6.5) : Math.Round(FontSize * 11.5);

    public double GraphHeight => Overlay.IsHorizontal ? Math.Round(FontSize * 1.3) : Math.Round(FontSize * 2.6);

    public string ValueSpacing => $"0,0,{Math.Round(FontSize * 0.6)},0";

    public string LabelSpacing => $"0,0,{Math.Round(FontSize * 0.7)},0";

    /// <summary>"S", "M" or "L" on the line's size button while the overlay is being moved.</summary>
    public string SizeLabel => Factor < 0.95 ? "S" : Factor > 1.05 ? "L" : "M";

    public string SizeToolTip => $"{Label} line: {(Factor < 0.95 ? "compact" : Factor > 1.05 ? "expanded" : "standard")}. Click to change its size.";

    /// <summary>Compact → Standard → Expanded → Compact.</summary>
    public IRelayCommand CycleSizeCommand { get; }

    /// <summary>The sizes changed (the overlay's scale, the screen or this line's size).</summary>
    public void Refresh() => OnPropertyChanged(string.Empty);
}

/// <summary>
/// What the in-game overlay shows and how it looks. Sensor values follow the telemetry frames; frame rates come from
/// <see cref="FrameRates"/> for the program in front, refreshed by <see cref="Tick"/>. Readings a PC doesn't have are
/// left out rather than shown as blanks (the FPS line stays, so it doesn't jump when a game starts).
/// The same instance drives the overlay window and the preview in its settings.
/// </summary>
public sealed class OverlayViewModel : ObservableObject
{
    public const string WarningColor = "#FFB547";
    public const string CriticalColor = "#FF5C5C";

    private readonly PageContext _context;
    private readonly Dictionary<string, OverlayValueViewModel> _values = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MetricViewModel> _metrics = new(StringComparer.Ordinal);
    private FrameRateStats? _frameRate;
    private int? _processId;
    private int _pacingCap;
    private string _layoutKey = string.Empty;
    private int _fanCatalogVersion = -1;
    private IReadOnlyList<string> _cpuFanIds = [];
    private double _screenScale = 1;
    private double? _liveScale;
    private bool _isMoving;
    private string? _currentGame;

    public OverlayViewModel(PageContext context)
    {
        _context = context;
        FrameTimes = new Feed(this);
        FinishMovingCommand = new RelayCommand(() => IsMoving = false);
        context.Settings.Changed += OnSettingsChanged;
        Rebuild(context.Monitor.Engine.LatestFrame);
    }

    // ---- Size --------------------------------------------------------------------------------------------------

    /// <summary>The screen's own scale for the overlay (1 on a 1080-line screen; see <see cref="OverlayPlacements.ScreenScale"/>), set by the app.</summary>
    public double ScreenScale
    {
        get => _screenScale;
        set
        {
            value = double.IsFinite(value) && value > 0 ? value : 1;
            if (Math.Abs(value - _screenScale) > 0.001)
            {
                _screenScale = value;
                OnPropertyChanged();
                SizesChanged();
            }
        }
    }

    /// <summary>The scale while the overlay is being resized by its corner (saved when the drag ends), else null.</summary>
    public double? LiveScale
    {
        get => _liveScale;
        set
        {
            value = value is double v && double.IsFinite(v) ? Math.Clamp(v, OverlayPlacements.MinScale / 100.0, OverlayPlacements.MaxScale / 100.0) : null;
            if (value != _liveScale)
            {
                _liveScale = value;
                SizesChanged();
            }
        }
    }

    /// <summary>The overlay's scale: its size (or the one being dragged), times the screen's when it follows the screen.</summary>
    public double Factor => (_liveScale ?? Options.SizeFactor) * (Options.AutoScale ? _screenScale : 1);

    /// <summary>The overlay's own scale (its size preset or custom scale), without the screen's.</summary>
    public double OwnScale => Options.SizeFactor;

    /// <summary>"100 %": the overlay's own size (without the screen's scale).</summary>
    public string ScaleText => $"{Math.Round((_liveScale ?? Options.SizeFactor) * 100).ToString(CultureInfo.CurrentCulture)} %";

    public double GroupFactor(OverlayGroup group) => Options.GroupFactor(group);

    /// <summary>Compact → Standard → Expanded → Compact for one line.</summary>
    internal void CycleGroupSize(OverlayGroupViewModel group)
    {
        var next = group.Factor < 0.95 ? OverlaySize.Standard : group.Factor > 1.05 ? OverlaySize.Compact : OverlaySize.Expanded;
        _context.Settings.Update("Overlay", s =>
        {
            s.Overlay.GroupSizes[group.Id] = next;
            s.Overlay.Normalize();
        });
    }

    /// <summary>Saves the scale reached by dragging the corner (as the overlay's own size).</summary>
    public void CommitScale()
    {
        if (_liveScale is not double scale)
        {
            return;
        }

        int percent = (int)Math.Round(scale * 100);
        _liveScale = null;
        _context.Settings.Update("Overlay", s =>
        {
            s.Overlay.Size = OverlaySize.Custom;
            s.Overlay.Scale = percent;
            s.Overlay.Normalize();
        });
    }

    private void SizesChanged()
    {
        OnPropertyChanged(nameof(Factor));
        OnPropertyChanged(nameof(ScaleText));
        OnPropertyChanged(nameof(FontSize));
        OnPropertyChanged(nameof(SmallFontSize));
        OnPropertyChanged(nameof(HeadlineFontSize));
        OnPropertyChanged(nameof(GraphWidth));
        OnPropertyChanged(nameof(GraphHeight));
        OnPropertyChanged(nameof(Padding));
        OnPropertyChanged(nameof(ValueSpacing));
        OnPropertyChanged(nameof(GroupSpacing));
        OnPropertyChanged(nameof(LabelSpacing));
        OnPropertyChanged(nameof(CornerRadius));
        foreach (var group in Groups)
        {
            group.Refresh();
        }
    }

    // ---- Moving --------------------------------------------------------------------------------------------------

    /// <summary>
    /// The overlay can be dragged to a new place and resized by its corner (Shift + the hotkey, or *Move on screen* in its
    /// settings); clicks reach it instead of passing through to the game.
    /// </summary>
    public bool IsMoving
    {
        get => _isMoving;
        set
        {
            if (SetProperty(ref _isMoving, value))
            {
                if (!value)
                {
                    CommitScale();
                }

                OnPropertyChanged(nameof(MovingText));
            }
        }
    }

    public IRelayCommand FinishMovingCommand { get; }

    /// <summary>The game the overlay was last over ("Cyberpunk2077.exe"), set by the app; places are kept for it when they're kept per game.</summary>
    public string? CurrentGame
    {
        get => _currentGame;
        set
        {
            if (SetProperty(ref _currentGame, OverlayPlacements.FileName(value)))
            {
                OnPropertyChanged(nameof(MovingText));
            }
        }
    }

    /// <summary>Where a move is saved: "Saved for Cyberpunk2077.exe" or "Saved for all games".</summary>
    public string MovingText => Options.PerGamePosition && _currentGame is { } game ? $"Saved for {game}" : "Saved for all games";

    /// <summary>The place to use now: the current game's own place when places are kept per game, else the one for all.</summary>
    public (OverlayPosition Position, int OffsetX, int OffsetY) Placement => Options.PlacementFor(_currentGame);

    /// <summary>Saves where the overlay was dropped (and any new scale): for the current game when places are kept per game, else for all.</summary>
    public void SavePlacement(OverlayPosition position, int offsetX, int offsetY)
    {
        string? game = Options.PerGamePosition ? _currentGame : null;
        double? scale = _liveScale;
        _liveScale = null;
        _context.Settings.Update("Overlay", s =>
        {
            var o = s.Overlay;
            if (game is not null)
            {
                o.GamePlacements.RemoveAll(p => string.Equals(p.Program, game, StringComparison.OrdinalIgnoreCase));
                o.GamePlacements.Add(new OverlayPlacement { Program = game, Position = position, OffsetX = offsetX, OffsetY = offsetY });
            }
            else
            {
                o.Position = position;
                o.OffsetX = offsetX;
                o.OffsetY = offsetY;
            }

            if (scale is double value)
            {
                o.Size = OverlaySize.Custom;
                o.Scale = (int)Math.Round(value * 100);
            }

            o.Normalize();
        });
    }

    /// <summary>The frame times of the program in front, for the graph.</summary>
    public IFrameTimeFeed FrameTimes { get; }

    /// <summary>The frame-time graph is shown (under the FPS line).</summary>
    public bool ShowGraph => Options.Items.Contains(OverlayCatalog.FrameGraph);

    /// <summary>The frame limiter limits the game in front (its own limit or the one for all games).</summary>
    public bool IsPacing => _pacingCap > 0;

    /// <summary>The limiter's mark next to the FPS: the limit ("116").</summary>
    public string PacingText => _pacingCap > 0 ? _pacingCap.ToString(CultureInfo.CurrentCulture) : string.Empty;

    public string PacingToolTip => _pacingCap > 0 ? $"Frame limiter on: limited to {_pacingCap.ToString(CultureInfo.CurrentCulture)} FPS" : string.Empty;

    /// <summary>The FPS number is a little bigger than the other values.</summary>
    public double HeadlineFontSize => Math.Round(FontSize * 1.3, 1);

    public double GraphWidth => IsHorizontal ? Math.Round(FontSize * 6.5) : Math.Round(FontSize * 11.5);

    public double GraphHeight => IsHorizontal ? Math.Round(FontSize * 1.3) : Math.Round(FontSize * 2.6);

    /// <summary>Set by the app once frame rates can be measured.</summary>
    public IFrameRateSource? FrameRates { get; set; }

    private OverlaySettings Options => _context.Settings.Current.Overlay;

    public ObservableCollection<OverlayGroupViewModel> Groups { get; } = [];

    public bool IsEnabled => Options.Enabled;

    public bool HasContent => Groups.Count > 0;

    /// <summary>Whether the overlay needs frame rates (an FPS item is shown).</summary>
    public bool NeedsFrameRates => Options.Items.Any(OverlayCatalog.IsFrameRate);

    /// <summary>The text size: the chosen size times the overlay's scale.</summary>
    public double FontSize => Math.Round(Options.FontSize * Factor, 1);

    /// <summary>Units, labels and the 1% low tag are a little smaller.</summary>
    public double SmallFontSize => Math.Round(FontSize * 0.78, 1);

    public string FontWeight => Options.FontWeight switch
    {
        OverlayFontWeight.Regular => "Normal",
        OverlayFontWeight.Medium => "Medium",
        OverlayFontWeight.Bold => "Bold",
        _ => "SemiBold",
    };

    public string TextColor => Options.TextColor;

    public double TextOpacity => Options.TextOpacity / 100.0;

    public string BackgroundColor => Options.BackgroundColor;

    public double BackgroundOpacity => Options.BackgroundOpacity / 100.0;

    public double CornerRadius => Math.Round(Options.CornerRadius * Factor);

    /// <summary>Padding inside the background, in proportion to the text.</summary>
    public string Padding => $"{Math.Round(FontSize * 0.85)},{Math.Round(FontSize * 0.55)}";

    /// <summary>Space after each value.</summary>
    public string ValueSpacing => $"0,0,{Math.Round(FontSize * 0.6)},0";

    /// <summary>Space after each line: between lines when stacked, between groups on one line.</summary>
    public string GroupSpacing => IsHorizontal ? $"0,0,{Math.Round(FontSize * 0.7)},0" : "0,1,0,1";

    /// <summary>Space between a line's label and its values.</summary>
    public string LabelSpacing => $"0,0,{Math.Round(FontSize * 0.7)},0";

    public bool IsHorizontal => Options.Layout == OverlayLayout.Horizontal;

    public bool ShowLabels => Options.ShowLabels;

    public bool TextShadow => Options.TextShadow;

    public OverlayPosition Position => Options.Position;

    public double EdgeMargin => Options.EdgeMargin;

    /// <summary>Refreshes sensor values from a frame.</summary>
    public void OnFrame(TelemetryFrame frame)
    {
        if (LayoutKey(frame) != _layoutKey)
        {
            Rebuild(frame);
        }

        var f = _context.Settings.Formatter;
        foreach (var (id, metric) in _metrics)
        {
            metric.Update(frame, f, _context.Monitor.Engine.Catalog);
            if (_values.TryGetValue(id, out var value))
            {
                Show(value, metric);
            }
        }

        if (_values.TryGetValue(OverlayCatalog.CpuFan, out var cpuFan))
        {
            var formatted = CpuFanRpm(frame) is double rpm ? ValueStyles.Format(ValueStyle.Fan, rpm, f) : FormattedValue.Unavailable;
            cpuFan.Number = formatted.Number;
            cpuFan.Unit = formatted.Unit;
            cpuFan.Color = Options.TextColor;
        }
    }

    /// <summary>
    /// Refreshes the frame rate of the program in front (about twice a second). <paramref name="executable"/> (its
    /// file name or path) tells whether the frame limiter limits it.
    /// </summary>
    public void Tick(int? foregroundProcessId, string? executable = null)
    {
        _processId = foregroundProcessId;
        _frameRate = foregroundProcessId is int pid ? FrameRates?.Get(pid) : null;
        ShowFrameRate();

        int cap = _frameRate is not null ? _context.Settings.Current.Gaming.ActiveFrameCap(executable) : 0;
        if (cap != _pacingCap)
        {
            _pacingCap = cap;
            OnPropertyChanged(nameof(IsPacing));
            OnPropertyChanged(nameof(PacingText));
            OnPropertyChanged(nameof(PacingToolTip));
        }
    }

    private void OnSettingsChanged(string section)
    {
        if (section is not ("Overlay" or "All" or "Monitoring" or "Alerts"))
        {
            return;
        }

        Rebuild(_context.Monitor.Engine.LatestFrame);
        OnPropertyChanged(string.Empty); // every appearance property
        foreach (var group in Groups)
        {
            group.Refresh();
        }
    }

    /// <summary>The items shown and which of them this PC reports: when it changes, the lines are rebuilt.</summary>
    private string LayoutKey(TelemetryFrame frame)
    {
        int slot = _context.GpuSlot;
        var shown = Options.Items.Where(id => OverlayCatalog.IsFrameRate(id) || IsAvailable(id, frame, slot));
        return $"{slot}|{Options.ShowLabels}|{string.Join(',', shown)}";
    }

    private bool IsAvailable(string id, TelemetryFrame frame, int slot) => id switch
    {
        OverlayCatalog.CpuFan => CpuFanRpm(frame) is not null,
        OverlayCatalog.GpuFan => frame.Contains(MetricIds.GpuFanRpm(slot, 1)) || frame.Contains(MetricIds.GpuFanPercent(slot, 1)),
        OverlayCatalog.CpuClock => frame.Contains(MetricIds.CpuClockEffectiveAverage) || frame.Contains(MetricIds.CpuClockDerived),
        _ => MetricIdFor(id, slot) is { } metricId && frame.Contains(metricId),
    };

    private static string? MetricIdFor(string id, int slot) => id switch
    {
        OverlayCatalog.GpuLoad => MetricIds.GpuLoad(slot),
        OverlayCatalog.GpuTemperature => MetricIds.GpuTemperature(slot),
        OverlayCatalog.GpuHotSpot => MetricIds.GpuHotSpotTemperature(slot),
        OverlayCatalog.GpuPower => MetricIds.GpuPower(slot),
        OverlayCatalog.GpuClock => MetricIds.GpuCoreClock(slot),
        OverlayCatalog.GpuMemoryClock => MetricIds.GpuMemoryClock(slot),
        OverlayCatalog.GpuMemory => MetricIds.GpuMemoryUsed(slot),
        OverlayCatalog.GpuVoltage => MetricIds.GpuVoltage(slot),
        OverlayCatalog.CpuLoad => MetricIds.CpuLoad,
        OverlayCatalog.CpuTemperature => MetricIds.CpuTemperature,
        OverlayCatalog.CpuPower => MetricIds.CpuPower,
        OverlayCatalog.Ram => MetricIds.MemoryUsed,
        _ => null,
    };

    private void Rebuild(TelemetryFrame frame)
    {
        int slot = _context.GpuSlot;
        _layoutKey = LayoutKey(frame);
        _values.Clear();
        _metrics.Clear();
        Groups.Clear();

        var shown = Options.Items.Where(id => OverlayCatalog.IsValue(id) && (OverlayCatalog.IsFrameRate(id) || IsAvailable(id, frame, slot))).ToList();
        if (ShowGraph && !shown.Any(OverlayCatalog.IsFrameRate))
        {
            shown.Insert(0, OverlayCatalog.Fps); // the graph sits on the FPS line
        }

        foreach (var group in Enum.GetValues<OverlayGroup>())
        {
            var values = new List<OverlayValueViewModel>();
            foreach (string id in shown.Where(i => OverlayCatalog.GroupOf(i) == group))
            {
                var value = new OverlayValueViewModel(id, this) { Color = Options.TextColor };
                values.Add(value);
                _values[id] = value;
                if (MetricFor(id, slot, frame) is { } metric)
                {
                    _metrics[id] = metric;
                }
            }

            if (values.Count > 0)
            {
                Groups.Add(new OverlayGroupViewModel(group.ToString(), GroupLabel(group), values, this));
            }
        }

        OnFrameValues(frame);
        ShowFrameRate();
        OnPropertyChanged(nameof(HasContent));
        OnPropertyChanged(nameof(NeedsFrameRates));
    }

    private void OnFrameValues(TelemetryFrame frame)
    {
        var f = _context.Settings.Formatter;
        foreach (var (id, metric) in _metrics)
        {
            metric.Update(frame, f, _context.Monitor.Engine.Catalog);
            Show(_values[id], metric);
        }
    }

    private void Show(OverlayValueViewModel value, MetricViewModel metric)
    {
        value.Number = metric.Number;
        value.Unit = metric.Unit;
        value.Color = !Options.ColorWarnings ? Options.TextColor : metric.Level switch
        {
            ThresholdLevel.Critical => CriticalColor,
            ThresholdLevel.Elevated => WarningColor,
            _ => Options.TextColor,
        };
    }

    private void ShowFrameRate()
    {
        var stats = _frameRate;
        bool labels = Options.ShowLabels;
        if (_values.TryGetValue(OverlayCatalog.Fps, out var fps))
        {
            fps.Number = stats is { } s ? Math.Round(s.Fps).ToString("0", CultureInfo.CurrentCulture) : "—";
            fps.Unit = labels ? string.Empty : "FPS";
            fps.Color = Options.TextColor;
        }

        if (_values.TryGetValue(OverlayCatalog.FrameTime, out var frameTime))
        {
            frameTime.Number = stats is { FrameTimeMilliseconds: > 0 } s ? s.FrameTimeMilliseconds.ToString("0.0", CultureInfo.CurrentCulture) : "—";
            frameTime.Unit = "ms";
            frameTime.Color = Options.TextColor;
        }

        if (_values.TryGetValue(OverlayCatalog.LowFps, out var low))
        {
            low.Number = stats is { } s ? Math.Round(s.LowFps).ToString("0", CultureInfo.CurrentCulture) : "—";
            low.Unit = "1% low";
            low.Color = Options.TextColor;
        }
    }

    private static string GroupLabel(OverlayGroup group) => group switch
    {
        OverlayGroup.FrameRate => "FPS",
        OverlayGroup.Graphics => "GPU",
        OverlayGroup.Processor => "CPU",
        _ => "RAM",
    };

    private MetricViewModel? MetricFor(string id, int slot, TelemetryFrame? frame = null)
    {
        var settings = _context.Settings.Current;
        return id switch
        {
            OverlayCatalog.GpuLoad => new(MetricIds.GpuLoad(slot), "GPU", ValueStyle.Percent, thresholds: Thresholds.Load),
            OverlayCatalog.GpuTemperature => new(MetricIds.GpuTemperature(slot), "GPU temperature", ValueStyle.Temperature,
                thresholds: SystemHealthViewModel.AlertThresholds(settings, "gpu-temp", Thresholds.GpuTemperature, 8)),
            OverlayCatalog.GpuHotSpot => new(MetricIds.GpuHotSpotTemperature(slot), "Hot spot", ValueStyle.Temperature, thresholds: new Thresholds(90, 100, 60)),
            OverlayCatalog.GpuPower => new(MetricIds.GpuPower(slot), "GPU power", ValueStyle.Power),
            OverlayCatalog.GpuClock => new(MetricIds.GpuCoreClock(slot), "GPU clock", ValueStyle.ClockMegahertz),
            OverlayCatalog.GpuMemoryClock => new(MetricIds.GpuMemoryClock(slot), "Memory clock", ValueStyle.ClockMegahertz),
            OverlayCatalog.GpuMemory => new(MetricIds.GpuMemoryUsed(slot), "VRAM", ValueStyle.MemoryBytes),
            OverlayCatalog.GpuFan => frame is null || frame.Contains(MetricIds.GpuFanRpm(slot, 1))
                ? new(MetricIds.GpuFanRpm(slot, 1), "GPU fan", ValueStyle.Fan)
                : new(MetricIds.GpuFanPercent(slot, 1), "GPU fan", ValueStyle.Percent),
            OverlayCatalog.GpuVoltage => new(MetricIds.GpuVoltage(slot), "GPU voltage", ValueStyle.Millivolts),
            OverlayCatalog.CpuLoad => new(MetricIds.CpuLoad, "CPU", ValueStyle.Percent, thresholds: Thresholds.Load),
            OverlayCatalog.CpuTemperature => new(MetricIds.CpuTemperature, "CPU temperature", ValueStyle.Temperature,
                thresholds: SystemHealthViewModel.AlertThresholds(settings, "cpu-temp", Thresholds.CpuTemperature, 10)),
            OverlayCatalog.CpuPower => new(MetricIds.CpuPower, "CPU power", ValueStyle.Power),
            OverlayCatalog.CpuClock => frame is null || frame.Contains(MetricIds.CpuClockEffectiveAverage)
                ? new(MetricIds.CpuClockEffectiveAverage, "CPU clock", ValueStyle.ClockGigahertz)
                : new(MetricIds.CpuClockDerived, "CPU clock", ValueStyle.ClockGigahertz),
            OverlayCatalog.Ram => new(MetricIds.MemoryUsed, "RAM", ValueStyle.MemoryBytes),
            _ => null,
        };
    }

    private double? CpuFanRpm(TelemetryFrame frame)
    {
        var catalog = _context.Monitor.Engine.Catalog;
        if (catalog.Version != _fanCatalogVersion)
        {
            _fanCatalogVersion = catalog.Version;
            _cpuFanIds = PcFans.Classify(catalog
                .Where(d => d.Kind == MetricKind.Fan && d.Id.StartsWith("board/fan/", StringComparison.Ordinal))
                .Select(d => new PcFanSensor(d.Id, d.Name))).Cpu.Select(s => s.Id).ToArray();
        }

        double? rpm = null;
        foreach (string id in _cpuFanIds)
        {
            if (frame.TryGetValue(id, out double value))
            {
                rpm = Math.Max(rpm ?? 0, value);
            }
        }

        return rpm;
    }

    /// <summary>The graph's view of the frame-rate source: the program in front, and its pacing target.</summary>
    private sealed class Feed(OverlayViewModel overlay) : IFrameTimeFeed
    {
        public double TargetFrameTime => overlay._pacingCap > 0 ? 1000.0 / overlay._pacingCap : 0;

        public int Read(Span<double> buffer, out double newestAgeMilliseconds)
        {
            newestAgeMilliseconds = 0;
            return overlay._processId is int pid && overlay.FrameRates is { } source ? source.RecentFrameTimes(pid, buffer, out newestAgeMilliseconds) : 0;
        }
    }
}
