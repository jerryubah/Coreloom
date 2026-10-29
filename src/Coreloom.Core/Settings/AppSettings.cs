using System.Text.Json.Serialization;
using Coreloom.Core.Alerts;
using Coreloom.Core.Formatting;
using Coreloom.Core.History;
using Coreloom.Core.Telemetry;

namespace Coreloom.Core.Settings;

public enum ThemePreference
{
    Dark,
    Light,
    System,
}

public enum UpdateCheckFrequency
{
    Never,
    OnStartup,
    Daily,
    Weekly,
}

/// <summary>All persisted user settings. Serialized as JSON; unknown fields are ignored and missing
/// fields take defaults, so files from older/newer versions load safely.</summary>
public sealed class AppSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public AppearanceSettings Appearance { get; set; } = new();

    public MonitoringSettings Monitoring { get; set; } = new();

    public DashboardSettings Dashboard { get; set; } = new();

    public OverlaySettings Overlay { get; set; } = new();

    public GamingSettings Gaming { get; set; } = new();

    public DesktopSettings Desktop { get; set; } = new();

    public AlertSettings Alerts { get; set; } = new();

    public UpdateSettings Updates { get; set; } = new();

    public DiagnosticsSettings Diagnostics { get; set; } = new();

    public ConnectSettings Connect { get; set; } = new();

    public DevicesSettings Devices { get; set; } = new();

    /// <summary>macOS: the readings shown in the menu bar itself, one item per module.</summary>
    public MenuBarSettings MenuBar { get; set; } = new();

    public WindowPlacement? Window { get; set; }

    /// <summary>Repairs out-of-range or missing values. Returns the same instance for chaining.</summary>
    public AppSettings Normalize()
    {
        SchemaVersion = CurrentSchemaVersion;
        Appearance ??= new AppearanceSettings();
        Monitoring ??= new MonitoringSettings();
        Dashboard ??= new DashboardSettings();
        Overlay ??= new OverlaySettings();
        Gaming ??= new GamingSettings();
        Desktop ??= new DesktopSettings();
        Alerts ??= new AlertSettings();
        Updates ??= new UpdateSettings();
        Diagnostics ??= new DiagnosticsSettings();
        Connect ??= new ConnectSettings();
        Devices ??= new DevicesSettings();
        Devices.Normalize();
        MenuBar ??= new MenuBarSettings();
        MenuBar.Normalize();
        if (Connect.Port is < 1024 or > 65535)
        {
            Connect.Port = ConnectSettings.DefaultPort;
        }

        if (!AccentPalette.IsValidHex(Appearance.Accent))
        {
            Appearance.Accent = AccentPalette.Default;
        }

        Appearance.Backdrop ??= new BackdropSettings();
        Appearance.Backdrop.Normalize();

        Monitoring.PollingIntervalMs = Math.Clamp(Monitoring.PollingIntervalMs, (int)TelemetryEngine.MinimumInterval.TotalMilliseconds, (int)TelemetryEngine.MaximumInterval.TotalMilliseconds);
        Monitoring.GamingIntervalMs = AdaptiveMonitoring.NormalizeGamingInterval(Monitoring.GamingIntervalMs);
        if (!Enum.IsDefined(Monitoring.DefaultWindow))
        {
            Monitoring.DefaultWindow = HistoryWindow.Seconds60;
        }

        Dashboard.Cards = DashboardCatalog.Normalize(Dashboard.Cards);
        if (!Enum.IsDefined(Dashboard.PcGlow))
        {
            Dashboard.PcGlow = PcGlow.Blue;
        }

        Overlay.Normalize();
        Gaming.Normalize();
        Alerts.CooldownMinutes = Math.Clamp(Alerts.CooldownMinutes, 1, 24 * 60);
        Alerts.Rules ??= AlertSettings.DefaultRules();
        foreach (var rule in Alerts.Rules)
        {
            rule.SustainSeconds = Math.Clamp(rule.SustainSeconds, 0, 3600);
            rule.Hysteresis = Math.Clamp(rule.Hysteresis, 0, 1000);
            if (string.IsNullOrWhiteSpace(rule.Id))
            {
                rule.Id = Guid.NewGuid().ToString("N");
            }
        }

        if (Window is { } w && (w.Width < 640 || w.Height < 420 || !double.IsFinite(w.Left) || !double.IsFinite(w.Top)))
        {
            Window = null;
        }

        return this;
    }
}

public sealed class AppearanceSettings
{
    public ThemePreference Theme { get; set; } = ThemePreference.Dark;

    /// <summary>Accent color as #RRGGBB.</summary>
    public string Accent { get; set; } = AccentPalette.Default;

    /// <summary>Disables value tweens, chart scrolling and transitions.</summary>
    public bool ReducedMotion { get; set; }

    /// <summary>Continuously scroll charts between samples (GPU-composited).</summary>
    public bool SmoothChartScrolling { get; set; } = true;

    /// <summary>Animate numbers, gauges and donuts when values change.</summary>
    public bool AnimateValues { get; set; } = true;

    /// <summary>
    /// Before 1.8.2: Mica (Windows 11) or vibrancy (macOS) in the sidebar. Kept so older files load; the window's
    /// background is now <see cref="Backdrop"/> (see-through is <see cref="BackdropKind.SeeThrough"/>).
    /// </summary>
    public bool UseMica { get; set; } = true;

    public bool SidebarCollapsed { get; set; }

    /// <summary>The window's background behind the glass (since 1.8.2).</summary>
    public BackdropSettings Backdrop { get; set; } = new();
}

public sealed class MonitoringSettings
{
    public int PollingIntervalMs { get; set; } = 1000;

    /// <summary>Read sensors less often (and at lower priority) while a game is in front (since 1.4.6).</summary>
    public bool LighterWhileGaming { get; set; } = true;

    /// <summary>How often sensors are read while a game is in front (milliseconds).</summary>
    public int GamingIntervalMs { get; set; } = AdaptiveMonitoring.DefaultGamingIntervalMs;

    public HistoryWindow DefaultWindow { get; set; } = HistoryWindow.Seconds60;

    public TemperatureUnit TemperatureUnit { get; set; } = TemperatureUnit.Celsius;

    public DataUnitStyle DataUnits { get; set; } = DataUnitStyle.Binary;

    public NetworkUnitStyle NetworkUnits { get; set; } = NetworkUnitStyle.BitsPerSecond;

    public bool GpuEnabled { get; set; } = true;

    public bool CpuEnabled { get; set; } = true;

    public bool MemoryEnabled { get; set; } = true;

    public bool StorageEnabled { get; set; } = true;

    public bool NetworkEnabled { get; set; } = true;

    /// <summary>Use LibreHardwareMonitor for CPU temperature/power/clocks, GPU hot spot, board and drive sensors.</summary>
    public bool UseLibreHardwareMonitor { get; set; } = true;

    /// <summary>Scan USB/HID cooling controllers (Corsair, NZXT, Aquacomputer...). Off by default for faster startup.</summary>
    public bool ScanUsbControllers { get; set; }

    public bool ShowPerThreadGraphs { get; set; } = true;

    /// <summary>Preferred GPU slot for the Overview page; null = automatic (first discrete GPU).</summary>
    public int? PreferredGpuSlot { get; set; }

    /// <summary>Preferred network adapter id; null = automatic (adapter with the default route).</summary>
    public string? PreferredNetworkAdapter { get; set; }

    public bool IsSectionEnabled(MonitoringSection section) => section switch
    {
        MonitoringSection.Gpu => GpuEnabled,
        MonitoringSection.Cpu => CpuEnabled,
        MonitoringSection.Memory => MemoryEnabled,
        MonitoringSection.Storage => StorageEnabled,
        MonitoringSection.Network => NetworkEnabled,
        _ => true,
    };
}

public sealed class DashboardSettings
{
    /// <summary>Card order and visibility for the Overview page.</summary>
    public List<DashboardCardSetting> Cards { get; set; } = DashboardCatalog.Defaults();

    public bool ShowStatistics { get; set; } = true;

    /// <summary>The picture of the PC on the left of the Overview header.</summary>
    public bool ShowPcPicture { get; set; } = true;

    /// <summary>Lighting color of the PC picture in the Overview header.</summary>
    public PcGlow PcGlow { get; set; } = PcGlow.Blue;

    /// <summary>macOS: the MacBook in the Overview header shows the Mac's own desktop picture on its screen.</summary>
    public bool ShowDesktopPicture { get; set; } = true;

    /// <summary>
    /// The person turned <see cref="ShowDesktopPicture"/> on themselves. A desktop picture kept in a folder macOS guards
    /// (Desktop, Documents, Downloads, iCloud Drive) is read only then, so macOS's "wants to access" question never comes
    /// out of the blue.
    /// </summary>
    public bool DesktopPictureChosen { get; set; }
}

/// <summary>Lighting colors for the PC picture in the Overview header.</summary>
public enum PcGlow
{
    Blue,
    Purple,
    Red,
    Green,

    /// <summary>Slowly cycles through the colors.</summary>
    Rainbow,

    /// <summary>Follows the app's accent color.</summary>
    Accent,
}

public sealed class DashboardCardSetting
{
    public string Id { get; set; } = string.Empty;

    public bool Visible { get; set; } = true;

    /// <summary>Width in dashboard columns; not set means the card's default (see <see cref="DashboardCatalog.SizeOf"/>).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Columns { get; set; }

    /// <summary>Height in dashboard rows; not set means the card's default.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Rows { get; set; }
}

public sealed class DesktopSettings
{
    /// <summary>Start at sign-in through an elevated Task Scheduler task (the app always runs as administrator).</summary>
    public bool StartWithWindows { get; set; }

    public bool LaunchMinimized { get; set; }

    public bool MinimizeToTray { get; set; } = true;

    public bool CloseToTray { get; set; } = true;

    public bool ShowTrayTooltipStats { get; set; } = true;

    /// <summary>The one-time "Coreloom is still running in the tray" notification was shown.</summary>
    public bool TrayHintShown { get; set; }

    /// <summary>macOS: the user said "Not now" to moving Coreloom into the Applications folder (asked once).</summary>
    public bool MoveToApplicationsDeclined { get; set; }

    /// <summary>macOS: while the window is closed and Coreloom runs in the menu bar, its Dock icon is hidden too.</summary>
    public bool HideDockIconWhenClosed { get; set; } = true;
}

public sealed class AlertSettings
{
    public bool Enabled { get; set; } = true;

    public int CooldownMinutes { get; set; } = 10;

    public List<AlertRule> Rules { get; set; } = DefaultRules();

    public static List<AlertRule> DefaultRules() =>
    [
        new AlertRule { Id = "gpu-temp", Name = "GPU temperature", MetricId = MetricIds.GpuTemperature(0), Threshold = 83, SustainSeconds = 10, Hysteresis = 3, Enabled = true },
        new AlertRule { Id = "cpu-temp", Name = "CPU temperature", MetricId = MetricIds.CpuTemperature, Threshold = 90, SustainSeconds = 10, Hysteresis = 3, Enabled = true },
        new AlertRule { Id = "ram-load", Name = "Memory usage", MetricId = MetricIds.MemoryLoad, Threshold = 90, SustainSeconds = 30, Hysteresis = 5, Enabled = false },
        new AlertRule { Id = "vram-load", Name = "VRAM usage", MetricId = MetricIds.GpuMemoryLoad(0), Threshold = 95, SustainSeconds = 30, Hysteresis = 5, Enabled = false },
    ];
}

public sealed class UpdateSettings
{
    public UpdateCheckFrequency Frequency { get; set; } = UpdateCheckFrequency.Daily;

    /// <summary>
    /// Download new versions in the background and install them when Coreloom starts or while the PC is idle
    /// (installed copies only; see <see cref="Updates.UpdateInstallPolicy"/>).
    /// </summary>
    public bool AutoInstall { get; set; } = true;

    public bool IncludePrereleases { get; set; }

    public string? SkippedVersion { get; set; }

    public DateTime? LastCheckUtc { get; set; }

    /// <summary>Overrides the repository compiled into the build ("owner/name").</summary>
    public string? RepositoryOverride { get; set; }

    /// <summary>The version that ran last time; a higher running version means Coreloom was just updated.</summary>
    public string? LastRunVersion { get; set; }

    /// <summary>Set to the new version after an update until its "Updated to …" banner is dismissed.</summary>
    public string? WhatsNewVersion { get; set; }

    /// <summary>
    /// Records that <paramref name="current"/> is running. Returns true when it is newer than the version that ran
    /// before (the first launch after an update), in which case <see cref="WhatsNewVersion"/> is set so the app can
    /// show what changed. The very first launch, and launching an older build, return false.
    /// </summary>
    /// <param name="ranBefore">
    /// Coreloom ran here before (e.g. it has checked for updates) even though no version was recorded: versions up to
    /// 1.0.0 didn't record themselves, so their first launch after an update counts as an update.
    /// </param>
    public bool RecordLaunch(Updates.SemanticVersion current, bool ranBefore = false)
    {
        ArgumentNullException.ThrowIfNull(current);
        bool upgraded = Updates.SemanticVersion.TryParse(LastRunVersion, out var previous) ? current > previous : ranBefore;
        LastRunVersion = current.ToString();
        if (upgraded)
        {
            WhatsNewVersion = current.ToString();
        }
        else if (WhatsNewVersion is not null && (!Updates.SemanticVersion.TryParse(WhatsNewVersion, out var announced) || !announced.Equals(current)))
        {
            WhatsNewVersion = null; // a banner for some other version is no longer relevant
        }

        return upgraded;
    }

    public bool IsCheckDue(DateTime utcNow, bool isStartup)
    {
        return Frequency switch
        {
            UpdateCheckFrequency.Never => false,
            UpdateCheckFrequency.OnStartup => isStartup,
            UpdateCheckFrequency.Daily => LastCheckUtc is null || utcNow - LastCheckUtc.Value >= TimeSpan.FromDays(1),
            UpdateCheckFrequency.Weekly => LastCheckUtc is null || utcNow - LastCheckUtc.Value >= TimeSpan.FromDays(7),
            _ => false,
        };
    }
}

public sealed class DiagnosticsSettings
{
    public bool VerboseLogging { get; set; }
}

/// <summary>Coreloom Connect: phones on the same network see this computer's readings (Settings › Connect).</summary>
public sealed class ConnectSettings
{
    public const int DefaultPort = 47101;

    /// <summary>Off until turned on: then Coreloom answers phones on the local network (only paired ones see readings).</summary>
    public bool Enabled { get; set; }

    /// <summary>The TCP port phones connect to.</summary>
    public int Port { get; set; } = DefaultPort;
}

public sealed class DevicesSettings
{
    public const int MaximumNameLength = 48;

    /// <summary>
    /// Names given to Bluetooth devices in Coreloom, by device id. Coreloom shows these instead of the name the device
    /// reports; Windows' and macOS's own Bluetooth settings keep theirs.
    /// </summary>
    public Dictionary<string, string> BluetoothNames { get; set; } = new(StringComparer.Ordinal);

    /// <summary>The name to show for a device: the one given in Coreloom, or the device's own.</summary>
    public string NameFor(string id, string reported) =>
        BluetoothNames.TryGetValue(id, out string? name) && !string.IsNullOrWhiteSpace(name) ? name : reported;

    /// <summary>A name as it is kept: trimmed and at most <see cref="MaximumNameLength"/> characters; null when empty.</summary>
    public static string? Clean(string? name)
    {
        string trimmed = (name ?? string.Empty).Trim();
        return trimmed.Length == 0 ? null : trimmed.Length > MaximumNameLength ? trimmed[..MaximumNameLength].TrimEnd() : trimmed;
    }

    internal void Normalize()
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (id, name) in BluetoothNames ?? [])
        {
            if (!string.IsNullOrWhiteSpace(id) && Clean(name) is { } clean && names.Count < 200)
            {
                names[id] = clean;
            }
        }

        BluetoothNames = names;
    }
}

public sealed class WindowPlacement
{
    public double Left { get; set; }

    public double Top { get; set; }

    public double Width { get; set; } = 1360;

    public double Height { get; set; } = 880;

    public bool Maximized { get; set; }
}

/// <summary>Accent colors offered in Settings (any valid #RRGGBB is accepted).</summary>
public static class AccentPalette
{
    public const string Default = "#5B8CFF";

    public static IReadOnlyList<(string Name, string Hex)> Presets { get; } =
    [
        ("Blue", "#5B8CFF"),
        ("Green", "#45D6A0"),
        ("Orange", "#FFB454"),
        ("Purple", "#AA86FF"),
        ("Pink", "#FF7AB6"),
        ("Teal", "#3CC8D8"),
        ("Coral", "#FF7A6B"),
        ("Graphite", "#A3ABBA"),
    ];

    public static bool IsValidHex(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length != 7 || value[0] != '#')
        {
            return false;
        }

        for (int i = 1; i < 7; i++)
        {
            if (!Uri.IsHexDigit(value[i]))
            {
                return false;
            }
        }

        return true;
    }
}
