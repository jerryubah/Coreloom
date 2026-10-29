using Coreloom.Core.Hardware;
using Coreloom.Core.Settings;
using Coreloom.Core.Telemetry;

namespace Coreloom.ViewModels.Services;

/// <summary>Everything the view models need from the monitoring back-end.</summary>
public sealed class MonitorContext
{
    public required TelemetryEngine Engine { get; init; }

    public required SystemSummary Summary { get; init; }

    public required IProcessSampler Processes { get; init; }

    /// <summary>Returns the automatically selected primary GPU slot.</summary>
    public required Func<int> PrimaryGpuSlot { get; init; }

    /// <summary>Lists GPU slots (slot, name, integrated).</summary>
    public required Func<IReadOnlyList<(int Slot, string Name, bool IsIntegrated)>> Gpus { get; init; }

    /// <summary>Lists network adapters (key, name, isActive).</summary>
    public required Func<IReadOnlyList<(string Key, string Name, bool IsActive)>> NetworkAdapters { get; init; }

    public required bool LibreHardwareMonitorIncluded { get; init; }
}

/// <summary>Applies appearance settings (theme, accent, motion, backdrop) to the UI.</summary>
public interface IThemeService
{
    void Apply(AppearanceSettings appearance);

    /// <summary>Whether the window can show the desktop through it (macOS vibrancy, Windows 11 Mica).</summary>
    bool IsMicaSupported { get; }

    /// <summary>Where a copy of your own background picture is kept; null where backgrounds can't be changed.</summary>
    string? BackdropFolder => null;
}

/// <summary>
/// Registers the app to start at sign-in. The app always runs as administrator, so this is a Task Scheduler
/// logon task with highest privileges: it starts the app elevated without a UAC prompt each time.
/// </summary>
public interface IStartupService
{
    /// <summary>True when the sign-in task exists.</summary>
    bool IsStartAtLogonRegistered { get; }

    /// <summary>Creates (or refreshes) the sign-in task, or removes it. Returns false when the change failed.</summary>
    Task<bool> SetStartAtLogonAsync(bool enabled, bool minimized);
}

public interface IElevationService
{
    /// <summary>True when the process holds an elevated administrator token (always the case once started).</summary>
    bool IsElevated { get; }
}

/// <summary>Shell interactions: links, folders, files, clipboard and simple dialogs.</summary>
public interface IShellService
{
    void OpenUrl(string url);

    void OpenFolder(string path);

    void OpenFile(string path);

    void CopyToClipboard(string text);

    /// <summary>Shows a save dialog. Returns the chosen path or null if cancelled.</summary>
    string? PickSaveFile(string suggestedName, string filter);

    /// <summary>Shows an open dialog (filter like "Programs (*.exe)|*.exe"). Returns the chosen path or null if cancelled.</summary>
    string? PickOpenFile(string title, string filter, string? initialDirectory = null) => null;

    void ShowMessage(string title, string message);

    bool Confirm(string title, string message);
}

/// <summary>Hands a verified installer to Windows and exits the app so files can be replaced safely.</summary>
public interface IUpdateInstaller
{
    /// <summary>True when this copy can update itself (installed and portable copies alike).</summary>
    bool CanInstallUpdates { get; }

    /// <summary>
    /// What this copy installs from: the release's installer (copies installed with it) or its portable zip (every
    /// other copy, which is updated in place).
    /// </summary>
    Coreloom.Core.Updates.UpdatePackageKind PackageKind { get; }

    /// <summary>
    /// Gets a verified package ready to install. For a portable zip this unpacks it next to the app, which takes a few
    /// seconds, so it may run on any thread. Returns false when it couldn't (the reason is logged).
    /// </summary>
    bool Prepare(Coreloom.Core.Updates.VerifiedPackage package);

    /// <summary>
    /// Starts the installer (still held open by <paramref name="package"/>, so it can't change before it runs) or the
    /// unpacked new version that copies itself over this one, with the arguments for <paramref name="options"/>, then
    /// exits Coreloom. Both start Coreloom again when they're done. Returns false when nothing could be started.
    /// </summary>
    bool LaunchAndExit(Coreloom.Core.Updates.VerifiedPackage package, Coreloom.Core.Updates.UpdateInstallOptions options);
}

/// <summary>Application lifetime operations.</summary>
public interface IAppLifetime
{
    /// <summary>Restarts Coreloom (same privilege level).</summary>
    void Restart();

    void Exit();
}

/// <summary>A part of Coreloom and its share of the whole machine's processor time (percent).</summary>
public sealed record OverheadPart(string Name, double CpuPercent);

/// <summary>What Coreloom's own parts cost (Settings › Diagnostics), so the overlay's cost can be told apart.</summary>
public interface IOverheadService
{
    /// <summary>Each registered part's processor share since the last call.</summary>
    IReadOnlyList<OverheadPart> Measure();
}

/// <summary>Logging level and log locations controlled from Settings.</summary>
public interface IDiagnosticsService
{
    string LogDirectory { get; }

    void SetVerboseLogging(bool enabled);

    IReadOnlyList<Coreloom.Core.Diagnostics.LogEntry> RecentLog();

    /// <summary>Every entry in the log files (oldest first; at most the newest 20,000), for the log viewer.</summary>
    IReadOnlyList<Coreloom.Core.Diagnostics.LogRecord> ReadLog() =>
        Coreloom.Core.Diagnostics.LogFile.Read(Coreloom.Core.Diagnostics.LogFile.Files(LogDirectory));
}
