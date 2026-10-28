using CommunityToolkit.Mvvm.ComponentModel;
using Coreloom.Core.Formatting;
using Coreloom.Core.Settings;
using Coreloom.Core.Telemetry;
using Coreloom.ViewModels.Pages;
using Coreloom.ViewModels.Services;

namespace Coreloom.ViewModels.MenuBar;

/// <summary>What one module shows in the menu bar right now (drawn by the Mac app as a small template image).</summary>
/// <param name="Id">The module ("cpu").</param>
/// <param name="Label">"CPU", drawn small beside the reading when <paramref name="ShowName"/>.</param>
/// <param name="Value">"21%", "1,460 rpm", "↓ 1.2 MB/s".</param>
/// <param name="SecondLine">A second, smaller line ("↑ 48 kB/s" for the network), or null.</param>
/// <param name="Fraction">How full the small bar is (0–1), or null when this module has no bar or it's turned off.</param>
public sealed record MenuBarReading(string Id, string Label, string Value, string? SecondLine, double? Fraction, bool ShowName);

/// <summary>One row of Settings › Menu bar: a module, whether it's shown and how, and its reading now.</summary>
public sealed class MenuBarModuleViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private string _current = "—";
    private bool _isAvailable;

    internal MenuBarModuleViewModel(MenuBarModule module, SettingsService settings)
    {
        Module = module;
        _settings = settings;
    }

    public MenuBarModule Module { get; }

    public string Id => Module.Id;

    public string Name => Module.Name;

    public string Label => Module.Label;

    public string Description => Module.Description;

    public bool HasGauge => Module.HasGauge;

    public string IconKey => Module.Id switch
    {
        MenuBarModules.Cpu => "Icon.Cpu",
        MenuBarModules.Gpu => "Icon.Gpu",
        MenuBarModules.Memory => "Icon.Memory",
        MenuBarModules.CpuTemperature or MenuBarModules.GpuTemperature => "Icon.Temperature",
        MenuBarModules.Fans => "Icon.Fan",
        MenuBarModules.Network => "Icon.Network",
        MenuBarModules.Disk => "Icon.Storage",
        MenuBarModules.Battery => "Icon.Battery",
        _ => "Icon.Power",
    };

    /// <summary>Shown in the menu bar.</summary>
    public bool Shown
    {
        get => Setting.Shown;
        set => Change(s => s.Shown = value);
    }

    /// <summary>The small name beside the reading.</summary>
    public bool ShowName
    {
        get => Setting.ShowName;
        set => Change(s => s.ShowName = value);
    }

    /// <summary>The small bar beside the reading.</summary>
    public bool ShowGauge
    {
        get => Setting.ShowGauge;
        set => Change(s => s.ShowGauge = value);
    }

    /// <summary>This Mac has the reading (no battery on a Mac mini, no fans on a MacBook Air).</summary>
    public bool IsAvailable
    {
        get => _isAvailable;
        internal set
        {
            if (SetProperty(ref _isAvailable, value))
            {
                OnPropertyChanged(nameof(AvailabilityText));
            }
        }
    }

    /// <summary>"Not on this Mac", or empty.</summary>
    public string AvailabilityText => _isAvailable ? string.Empty : "Not on this Mac";

    /// <summary>The reading now, as the menu bar shows it ("21%").</summary>
    public string Current
    {
        get => _current;
        internal set => SetProperty(ref _current, value);
    }

    internal MenuBarModuleSetting Setting => _settings.Current.MenuBar.For(Module.Id);

    /// <summary>Raised when a choice changed (the menu bar items are made again).</summary>
    internal event EventHandler? Changed;

    private void Change(Action<MenuBarModuleSetting> change)
    {
        _settings.Update("MenuBar", s => change(s.MenuBar.For(Module.Id)));
        OnPropertyChanged(nameof(Shown));
        OnPropertyChanged(nameof(ShowName));
        OnPropertyChanged(nameof(ShowGauge));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Settings were replaced (imported): the switches show the new choices.</summary>
    internal void Reload()
    {
        OnPropertyChanged(nameof(Shown));
        OnPropertyChanged(nameof(ShowName));
        OnPropertyChanged(nameof(ShowGauge));
    }
}

/// <summary>
/// The readings a Mac shows in its menu bar, one item per module turned on in Settings › Menu bar (macOS lets people
/// ⌘-drag the items into any order). Worked out from every reading, whether or not the window is open.
/// </summary>
public sealed class MenuBarModulesViewModel : ObservableObject
{
    private readonly PageContext _context;
    private IReadOnlyList<MenuBarReading> _readings = [];
    private int _fanCatalogVersion = -1;
    private string[] _fanIds = [];
    private string? _startupVolumeLoad;

    public MenuBarModulesViewModel(PageContext context)
    {
        _context = context;
        Modules = MenuBarModules.All.Select(m => new MenuBarModuleViewModel(m, context.Settings)).ToArray();
        foreach (var module in Modules)
        {
            module.Changed += (_, _) => Publish(force: true);
        }
    }

    public IReadOnlyList<MenuBarModuleViewModel> Modules { get; }

    /// <summary>What the menu bar shows now, in Settings' order (modules turned on and available on this Mac).</summary>
    public IReadOnlyList<MenuBarReading> Readings => _readings;

    public bool HasReadings => _readings.Count > 0;

    /// <summary>Raised when what the menu bar shows changed (a reading, or a choice in Settings).</summary>
    public event EventHandler? ReadingsChanged;

    /// <summary>Works out every module's reading from a frame (and publishes them when something changed).</summary>
    public void Update(TelemetryFrame frame)
    {
        var f = _context.Settings.Formatter;
        int slot = _context.GpuSlot;
        foreach (var module in Modules)
        {
            var (value, _, _) = Read(module.Id, frame, f, slot);
            module.IsAvailable = value is not null;
            module.Current = value ?? "—";
        }

        Publish(force: false, frame);
    }

    /// <summary>Settings were replaced: the rows and the menu bar follow.</summary>
    public void Reload()
    {
        foreach (var module in Modules)
        {
            module.Reload();
        }

        Publish(force: true);
    }

    private TelemetryFrame? _last;

    private void Publish(bool force, TelemetryFrame? frame = null)
    {
        frame ??= _last;
        _last = frame;
        if (frame is null)
        {
            if (force)
            {
                ReadingsChanged?.Invoke(this, EventArgs.Empty);
            }

            return;
        }

        var f = _context.Settings.Formatter;
        int slot = _context.GpuSlot;
        var readings = new List<MenuBarReading>();
        foreach (var module in Modules)
        {
            var setting = module.Setting;
            if (!setting.Shown)
            {
                continue;
            }

            var (value, second, fraction) = Read(module.Id, frame, f, slot);
            if (value is null)
            {
                continue;
            }

            readings.Add(new MenuBarReading(module.Id, module.Label, value, second,
                module.HasGauge && setting.ShowGauge && fraction is double share ? Math.Clamp(share, 0, 1) : null, setting.ShowName));
        }

        if (!force && readings.SequenceEqual(_readings))
        {
            return;
        }

        bool had = HasReadings;
        _readings = readings;
        if (had != HasReadings)
        {
            OnPropertyChanged(nameof(HasReadings));
        }

        OnPropertyChanged(nameof(Readings));
        ReadingsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>A module's reading: the value text, an optional second line, and the bar's fill (null: no bar).</summary>
    private (string? Value, string? Second, double? Fraction) Read(string id, TelemetryFrame frame, UnitFormatter f, int slot)
    {
        switch (id)
        {
            case MenuBarModules.Cpu when frame.TryGetValue(MetricIds.CpuLoad, out double load):
                return (Join(f.Percent(load)), null, load / 100);
            case MenuBarModules.Gpu when frame.TryGetValue(MetricIds.GpuLoad(slot), out double load):
                return (Join(f.Percent(load)), null, load / 100);
            case MenuBarModules.Memory when frame.TryGetValue(MetricIds.MemoryUsed, out double used):
                double? share = frame.TryGetValue(MetricIds.MemoryLoad, out double memoryLoad) ? memoryLoad / 100
                    : frame.TryGetValue(MetricIds.MemoryTotal, out double total) && total > 0 ? used / total : null;
                return (Join(f.MemoryBytes(used, 1)), null, share);
            case MenuBarModules.CpuTemperature when frame.TryGetValue(MetricIds.CpuTemperature, out double celsius):
                return (Join(f.Temperature(celsius)), null, celsius / 100);
            case MenuBarModules.GpuTemperature when frame.TryGetValue(MetricIds.GpuTemperature(slot), out double celsius):
                return (Join(f.Temperature(celsius)), null, celsius / 100);
            case MenuBarModules.Fans when FastestFan(frame) is double rpm:
                return (Join(f.Fan(rpm)), null, null);
            case MenuBarModules.Network when frame.TryGetValue(MetricIds.NetworkActiveDown, out double down) && frame.TryGetValue(MetricIds.NetworkActiveUp, out double up):
                return ("↓ " + Join(f.NetworkRate(down)), "↑ " + Join(f.NetworkRate(up)), null);
            case MenuBarModules.Disk when StartupVolumeLoad() is { } key && frame.TryGetValue(key, out double full):
                return (Join(f.Percent(full)), null, full / 100);
            case MenuBarModules.Battery when frame.TryGetValue(MetricIds.BatteryCharge, out double charge):
                return (Join(f.Percent(charge)), null, charge / 100);
            case MenuBarModules.Power when frame.TryGetValue(MetricIds.SystemPower, out double watts) || frame.TryGetValue(MetricIds.CpuPower, out watts):
                return (Join(f.Power(watts)), null, null);
            default:
                return (null, null, null);
        }
    }

    /// <summary>"21%", "65°C", "1,460 rpm", "46.0 GB".</summary>
    internal static string Join(FormattedValue value) =>
        value.Unit.Length == 0 ? value.Number : value.Number + (value.Unit.StartsWith('°') || value.Unit == "%" ? string.Empty : " ") + value.Unit;

    private double? FastestFan(TelemetryFrame frame)
    {
        var catalog = _context.Monitor.Engine.Catalog;
        if (catalog.Version != _fanCatalogVersion)
        {
            _fanCatalogVersion = catalog.Version;
            _fanIds = catalog.Where(d => d.Kind == MetricKind.Fan && !d.Id.EndsWith("/pct", StringComparison.Ordinal)).Select(d => d.Id).ToArray();
        }

        double? fastest = null;
        foreach (string id in _fanIds)
        {
            if (frame.TryGetValue(id, out double rpm) && double.IsFinite(rpm))
            {
                fastest = Math.Max(fastest ?? 0, rpm);
            }
        }

        return fastest;
    }

    /// <summary>The used-% reading of the startup volume (the volume listed first).</summary>
    private string? StartupVolumeLoad()
    {
        if (_startupVolumeLoad is null)
        {
            var volume = _context.Monitor.Engine.Devices.GetByCategory(MetricCategory.Storage).FirstOrDefault(d => d.Kind == "Volume");
            if (volume is not null && volume.Id.StartsWith("vol/", StringComparison.Ordinal))
            {
                _startupVolumeLoad = volume.Id + "/load";
            }
        }

        return _startupVolumeLoad;
    }
}
