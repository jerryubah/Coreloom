using Coreloom.Core.Formatting;
using Coreloom.Core.Telemetry;

namespace Coreloom.Core.Settings;

/// <summary>
/// What a Mac starts with the first time Coreloom opens (no settings file yet), where the Windows defaults don't fit:
/// sizes in decimal units like Finder, the look following macOS's Light/Dark setting, and temperature alerts at the
/// levels Macs actually run at (Apple silicon's cores and graphics run near 100 °C under load by design).
/// </summary>
public static class MacDefaults
{
    /// <summary>Apple silicon: CPU and GPU alerts.</summary>
    public const double AppleSiliconCpuAlert = 105;

    public const double AppleSiliconGpuAlert = 100;

    /// <summary>Intel Macs: the processor throttles at 100 °C.</summary>
    public const double IntelCpuAlert = 97;

    public const double IntelGpuAlert = 100;

    /// <summary>⌥⌘O for the readings window.</summary>
    public static OverlayHotkey OverlayHotkey => new() { Key = 0x4F, Modifiers = HotkeyModifiers.Alt | HotkeyModifiers.Windows };

    public static AppSettings Apply(AppSettings settings, bool appleSilicon)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Monitoring.DataUnits = DataUnitStyle.Decimal;
        settings.Appearance.Theme = ThemePreference.System;
        settings.Monitoring.UseLibreHardwareMonitor = false;

        // The readings window: macOS doesn't let another app measure a game's frame rate (Apple's Metal HUD shows it), and
        // a Mac keyboard has no End key, so ⌥⌘O shows and hides it.
        settings.Overlay.Items = settings.Overlay.Items.Where(OverlayCatalog.OnMac).ToList();
        settings.Overlay.Hotkey = OverlayHotkey;
        foreach (var rule in settings.Alerts.Rules)
        {
            if (rule.MetricId == MetricIds.CpuTemperature)
            {
                rule.Threshold = appleSilicon ? AppleSiliconCpuAlert : IntelCpuAlert;
            }
            else if (rule.MetricId == MetricIds.GpuTemperature(0))
            {
                rule.Threshold = appleSilicon ? AppleSiliconGpuAlert : IntelGpuAlert;
            }
        }

        return settings;
    }
}
