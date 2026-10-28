namespace Coreloom.Core.Settings;

/// <summary>What one menu bar module shows.</summary>
/// <param name="Id">"cpu", "gpu", "memory"…</param>
/// <param name="Name">"Processor", as Settings lists it.</param>
/// <param name="Label">"CPU": the small name beside the reading in the menu bar.</param>
/// <param name="Description">What it shows, for Settings.</param>
/// <param name="HasGauge">Whether it can show a small bar (a share of something: a load, a temperature's range).</param>
public sealed record MenuBarModule(string Id, string Name, string Label, string Description, bool HasGauge);

/// <summary>
/// The modules a Mac can show in its menu bar, each as its own item there (macOS lets people ⌘-drag them into the order
/// they like), in the order they appear in Settings.
/// </summary>
public static class MenuBarModules
{
    public const string Cpu = "cpu";
    public const string Gpu = "gpu";
    public const string Memory = "memory";
    public const string CpuTemperature = "cpu-temp";
    public const string GpuTemperature = "gpu-temp";
    public const string Fans = "fans";
    public const string Network = "network";
    public const string Disk = "disk";
    public const string Battery = "battery";
    public const string Power = "power";

    public static IReadOnlyList<MenuBarModule> All { get; } =
    [
        new(Cpu, "Processor", "CPU", "How busy the processor is, in percent.", true),
        new(Gpu, "Graphics", "GPU", "How busy the graphics processor is, in percent.", true),
        new(Memory, "Memory", "MEM", "Memory in use; the bar shows how full it is.", true),
        new(CpuTemperature, "Processor temperature", "CPU", "The processor's temperature; the bar fills toward 100 °C.", true),
        new(GpuTemperature, "Graphics temperature", "GPU", "The graphics processor's temperature; the bar fills toward 100 °C.", true),
        new(Fans, "Fans", "FAN", "The fastest fan's speed.", false),
        new(Network, "Network", "NET", "Download and upload speed, one above the other.", false),
        new(Disk, "Storage", "SSD", "How full the startup drive is.", true),
        new(Battery, "Battery", "BAT", "The battery's charge.", true),
        new(Power, "Power", "PWR", "How much power the Mac draws.", false),
    ];

    public static MenuBarModule? Find(string? id) => All.FirstOrDefault(m => m.Id == id);
}

/// <summary>One module's choices in Settings › Menu bar.</summary>
public sealed class MenuBarModuleSetting
{
    public string Id { get; set; } = string.Empty;

    /// <summary>Shown in the menu bar.</summary>
    public bool Shown { get; set; }

    /// <summary>The small name ("CPU") beside the reading.</summary>
    public bool ShowName { get; set; } = true;

    /// <summary>The small bar beside the reading (for modules that have one).</summary>
    public bool ShowGauge { get; set; } = true;
}

public sealed class MenuBarSettings
{
    /// <summary>Every module, in <see cref="MenuBarModules.All"/>'s order; none is shown until it's turned on.</summary>
    public List<MenuBarModuleSetting> Modules { get; set; } = Defaults();

    public MenuBarModuleSetting For(string id) => Modules.First(m => m.Id == id);

    public static List<MenuBarModuleSetting> Defaults() => MenuBarModules.All.Select(m => new MenuBarModuleSetting { Id = m.Id }).ToList();

    /// <summary>One setting per known module, in the catalog's order; unknown ids are dropped, missing ones added (off).</summary>
    internal void Normalize()
    {
        var byId = new Dictionary<string, MenuBarModuleSetting>(StringComparer.Ordinal);
        foreach (var module in Modules ?? [])
        {
            if (module is not null && MenuBarModules.Find(module.Id) is not null)
            {
                byId.TryAdd(module.Id, module);
            }
        }

        Modules = MenuBarModules.All.Select(m => byId.TryGetValue(m.Id, out var setting) ? setting : new MenuBarModuleSetting { Id = m.Id }).ToList();
    }
}
