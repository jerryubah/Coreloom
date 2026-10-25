using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using Coreloom.Core.Formatting;
using Coreloom.Core.History;
using Coreloom.Core.Telemetry;
using Coreloom.ViewModels.Infrastructure;
using Coreloom.ViewModels.Services;

namespace Coreloom.ViewModels.Pages;

/// <summary>
/// Base class for navigation pages. Only the active page receives frame updates, so hidden pages cost
/// nothing; history keeps accumulating in the background and charts are complete when a page is opened.
/// </summary>
public abstract partial class PageViewModel : ObservableObject
{
    private bool _isActive;
    private bool _isEnabled = true;
    private string? _subtitle;

    protected PageViewModel(string key, string title, string iconKey, PageContext context, MonitoringSection? section = null)
    {
        Key = key;
        Title = title;
        IconKey = iconKey;
        Context = context;
        Section = section;
    }

    public string Key { get; }

    public string Title { get; }

    /// <summary>Icon resource key used by the navigation pane (e.g. "Icon.Gpu").</summary>
    public string IconKey { get; }

    public MonitoringSection? Section { get; }

    public string? Subtitle
    {
        get => _subtitle;
        protected set => SetProperty(ref _subtitle, value);
    }

    public bool IsActive
    {
        get => _isActive;
        private set => SetProperty(ref _isActive, value);
    }

    /// <summary>False when the page's monitoring section is disabled in Settings (hidden from navigation).</summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    public TimelineViewModel Timeline => Context.Timeline;

    protected PageContext Context { get; }

    /// <summary>The system Coreloom runs on: wording and what the page offers follow it.</summary>
    public Coreloom.Core.HostPlatform Platform => Context.Platform;

    /// <summary>True in the macOS app.</summary>
    public bool IsMac => Context.IsMac;

    /// <summary>"PC" or "Mac": how the page refers to the computer.</summary>
    public string Computer => Coreloom.Core.HostPlatforms.Computer(Context.Platform);

    protected MonitorContext Monitor => Context.Monitor;

    protected SettingsService Settings => Context.Settings;

    protected UnitFormatter Formatter => Context.Settings.Formatter;

    protected MetricCatalog Catalog => Context.Monitor.Engine.Catalog;

    protected DeviceRegistry Devices => Context.Monitor.Engine.Devices;

    protected HistoryStore History => Context.Monitor.Engine.History;

    protected long WindowMilliseconds => Context.Timeline.WindowMilliseconds;

    protected bool ShowStatistics => Context.Settings.Current.Dashboard.ShowStatistics;

    public void Activate(TelemetryFrame frame)
    {
        IsActive = true;
        OnActivated();
        if (frame.Sequence > 0)
        {
            OnUpdate(frame);
        }
    }

    public void Deactivate()
    {
        IsActive = false;
        OnDeactivated();
    }

    public void Update(TelemetryFrame frame)
    {
        if (frame.Sequence > 0)
        {
            OnUpdate(frame);
        }
    }

    protected abstract void OnUpdate(TelemetryFrame frame);

    protected virtual void OnActivated()
    {
    }

    protected virtual void OnDeactivated()
    {
    }

    /// <summary>Called when settings change (units, sections, dashboard layout...).</summary>
    public virtual void OnSettingsChanged(string section)
    {
    }

    protected void UpdateMetric(MetricViewModel metric, TelemetryFrame frame, bool withStats = false) =>
        metric.Update(frame, Formatter, Catalog, withStats && ShowStatistics ? History : null, WindowMilliseconds);

    protected void UpdateMetrics(TelemetryFrame frame, bool withStats, params MetricViewModel[] metrics)
    {
        foreach (var metric in metrics)
        {
            UpdateMetric(metric, frame, withStats);
        }
    }

    protected void UpdateCharts(TelemetryFrame frame, params ChartViewModel[] charts)
    {
        foreach (var chart in charts)
        {
            chart.Update(frame, Formatter);
        }
    }

    protected static IReadOnlyList<KeyValueItem> ToItems(DeviceInfo? device) =>
        device is null ? [] : device.Properties.Select(p => new KeyValueItem(p.Key, p.Value)).ToArray();

    /// <summary>"AMD Ryzen 9 9950X3D 16-Core Processor" → "AMD Ryzen 9 9950X3D".</summary>
    public static string ShortCpuName(string name)
    {
        string s = name.Replace("(R)", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("(TM)", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(" CPU", string.Empty, StringComparison.Ordinal);
        s = CoreSuffix().Replace(s, string.Empty);
        int at = s.IndexOf(" @ ", StringComparison.Ordinal);
        if (at > 0)
        {
            s = s[..at];
        }

        return string.Join(' ', s.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>"NVIDIA GeForce RTX 5090" → "GeForce RTX 5090".</summary>
    public static string ShortGpuName(string name)
    {
        string s = name.Replace("(R)", string.Empty, StringComparison.OrdinalIgnoreCase).Replace("(TM)", string.Empty, StringComparison.OrdinalIgnoreCase);
        if (s.StartsWith("NVIDIA ", StringComparison.OrdinalIgnoreCase) && s.Length > 7)
        {
            s = s[7..];
        }

        return string.Join(' ', s.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    [GeneratedRegex(@"\s+\d+-Core Processor\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex CoreSuffix();
}

/// <summary>Services shared by all pages.</summary>
public sealed class PageContext
{
    private SystemHealthViewModel? _health;
    private Overlay.OverlayViewModel? _overlay;

    public required MonitorContext Monitor { get; init; }

    public required SettingsService Settings { get; init; }

    public required TimelineViewModel Timeline { get; init; }

    /// <summary>Drive benchmark, internet speed test and Smart Optimize's Windows services.</summary>
    public ToolServices Tools { get; init; } = new();

    /// <summary>The system Coreloom runs on (Windows unless the Mac app says otherwise).</summary>
    public Coreloom.Core.HostPlatform Platform { get; init; } = Coreloom.Core.HostPlatform.Windows;

    /// <summary>True in the macOS app.</summary>
    public bool IsMac => Platform == Coreloom.Core.HostPlatform.MacOS;

    /// <summary>GPU slot to show on Overview/GPU pages (user preference or automatic).</summary>
    public int GpuSlot => Settings.Current.Monitoring.PreferredGpuSlot is int slot && Monitor.Gpus().Any(g => g.Slot == slot)
        ? slot
        : Monitor.PrimaryGpuSlot();

    /// <summary>Health of the graphics card, processor, memory and storage (shared by the Overview and the hardware pages).</summary>
    public SystemHealthViewModel Health => _health ??= new SystemHealthViewModel(this);

    /// <summary>The in-game overlay's content and look (shared by the overlay window and its settings).</summary>
    public Overlay.OverlayViewModel Overlay => _overlay ??= new Overlay.OverlayViewModel(this);

    /// <summary>Opens Settings › Logs filtered to a category and search (set by the shell; null in tests).</summary>
    public Action<string?, string?>? OpenLogs { get; set; }
}
