using System.Diagnostics;
using System.IO;
using System.Windows;
using Coreloom.Core;
using Coreloom.Core.Diagnostics;
using Coreloom.Core.Updates;
using Coreloom.ViewModels.Services;

namespace Coreloom.App.Services;

/// <summary>Shell integration: URLs, folders, files, clipboard and simple dialogs.</summary>
public sealed class ShellService : IShellService
{
    public void OpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return;
        }

        Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
    }

    public void OpenFolder(string path)
    {
        if (Directory.Exists(path))
        {
            Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
    }

    public void OpenFile(string path)
    {
        if (File.Exists(path))
        {
            Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
    }

    public void CopyToClipboard(string text)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                Thread.Sleep(50); // clipboard briefly locked by another app
            }
        }
    }

    public string? PickSaveFile(string suggestedName, string filter)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = suggestedName,
            Filter = filter,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            AddExtension = true,
            OverwritePrompt = true,
        };
        return dialog.ShowDialog(Application.Current?.MainWindow) == true ? dialog.FileName : null;
    }

    public string? PickOpenFile(string title, string filter, string? initialDirectory = null)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = title,
            Filter = filter,
            CheckFileExists = true,
            Multiselect = false,
            InitialDirectory = initialDirectory is { Length: > 0 } dir && System.IO.Directory.Exists(dir) ? dir : string.Empty,
        };
        return dialog.ShowDialog(Application.Current?.MainWindow) == true ? dialog.FileName : null;
    }

    public void ShowMessage(string title, string message)
    {
        var owner = Application.Current?.MainWindow;
        if (owner is { IsVisible: true })
        {
            MessageBox.Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    public bool Confirm(string title, string message)
    {
        var owner = Application.Current?.MainWindow;
        var result = owner is { IsVisible: true }
            ? MessageBox.Show(owner, message, title, MessageBoxButton.OKCancel, MessageBoxImage.Question)
            : MessageBox.Show(message, title, MessageBoxButton.OKCancel, MessageBoxImage.Question);
        return result == MessageBoxResult.OK;
    }

    private static void Start(ProcessStartInfo info)
    {
        try
        {
            using var _ = Process.Start(info);
        }
        catch (Exception ex)
        {
            Log.Warn("Shell", $"Could not open {info.FileName}.", ex);
        }
    }
}

/// <summary>Restart / exit / elevation state.</summary>
public sealed class LifetimeService : IAppLifetime, IElevationService
{
    private readonly Action _exit;

    public LifetimeService(Action exit)
    {
        _exit = exit;
    }

    public bool IsElevated => Environment.IsPrivilegedProcess;

    public static string ExecutablePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Coreloom.exe");

    /// <summary>
    /// Starts a new instance and exits this one. The new process is launched from an elevated parent, so Windows
    /// elevates it without asking again; <c>--wait-for</c> makes it wait until this process has released the
    /// single-instance lock.
    /// </summary>
    public void Restart()
    {
        if (Launch())
        {
            _exit();
        }
    }

    public void Exit() => _exit();

    private static bool Launch()
    {
        try
        {
            var info = new ProcessStartInfo(ExecutablePath, $"--wait-for {Environment.ProcessId}")
            {
                UseShellExecute = true,
                WorkingDirectory = AppContext.BaseDirectory,
            };

            using var process = Process.Start(info);
            return process is not null;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // The user cancelled a UAC prompt (only possible if the process was somehow not elevated).
            return false;
        }
        catch (Exception ex)
        {
            Log.Warn("Lifetime", "Relaunching Coreloom failed.", ex);
            return false;
        }
    }
}

/// <summary>
/// Installs a verified update and exits so the files can be replaced. Copies installed with the installer run the
/// release's installer. Every other copy (portable, or simply unzipped) is updated in place from the release's portable
/// zip: it is unpacked into <c>.coreloom-update\new</c> next to the app, and the new version's executable copies itself
/// over this folder once Coreloom has exited (see <see cref="FolderUpdate"/> and <c>App.RunUpdateHelper</c>).
/// </summary>
public sealed class UpdateInstaller : IUpdateInstaller
{
    private const int ErrorSharingViolation = 32;

    private readonly AppPaths _paths;
    private readonly Action _exit;
    private Task _cleanup = Task.CompletedTask;

    public UpdateInstaller(AppPaths paths, Action exit)
    {
        _paths = paths;
        _exit = exit;
    }

    public bool CanInstallUpdates => true;

    public UpdatePackageKind PackageKind => _paths.IsInstalled && !_paths.IsPortable ? UpdatePackageKind.Installer : UpdatePackageKind.PortableZip;

    /// <summary>
    /// Removes what the last in-place update left next to the app (its unpacked files and the backup of the old
    /// version), off the UI thread. Preparing a new update waits for it.
    /// </summary>
    public void StartCleanup()
    {
        string work = FolderUpdate.WorkDirectory(_paths.BaseDirectory);
        if (Directory.Exists(work) || File.Exists(work))
        {
            _cleanup = Task.Run(() => FolderUpdate.Cleanup(_paths.BaseDirectory));
        }
    }

    public bool Prepare(VerifiedPackage package)
    {
        if (PackageKind == UpdatePackageKind.Installer)
        {
            return true; // the installer is run as it is
        }

        try
        {
            _cleanup.Wait();
            using var zip = package.OpenRead();
            FolderUpdate.Stage(zip, _paths.BaseDirectory);
            return true;
        }
        catch (UpdateException ex)
        {
            Log.Warn("Updates", "Unpacking the update next to Coreloom failed.", ex);
            return false;
        }
    }

    public bool LaunchAndExit(VerifiedPackage package, UpdateInstallOptions options)
    {
        var info = PackageKind == UpdatePackageKind.Installer
            ? InstallerStartInfo(package, options)
            : FolderUpdateStartInfo(options);
        if (info is null)
        {
            return false;
        }

        Process? process;
        try
        {
            process = Process.Start(info);
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == ErrorSharingViolation && PackageKind == UpdatePackageKind.Installer)
        {
            // Not expected (the file is only open for reading), but don't let the safety check block the update.
            Log.Warn("Updates", "Windows couldn't start the installer while it was held open; retrying without the hold.", ex);
            package.Release();
            process = Process.Start(info);
        }

        if (process is null)
        {
            return false;
        }

        process.Dispose();
        _exit();
        return true;
    }

    private static ProcessStartInfo InstallerStartInfo(VerifiedPackage installer, UpdateInstallOptions options)
    {
        // The arguments come from UpdateInstallOptions (see installer/Coreloom.iss): /SILENT shows a small progress
        // window, /VERYSILENT nothing; /CLOSEAPPLICATIONS lets the installer close a Coreloom that is still exiting;
        // /UPDATE=1 relaunches Coreloom when finished (or the old version with --update-failed if it couldn't finish).
        // Files are only ever replaced by the installer, never by the running app. The installer stays open in
        // `installer` while it starts, so it can't be swapped between the hash check and this call.
        return new ProcessStartInfo(installer.Path, options.ToInstallerArguments())
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(installer.Path) ?? AppContext.BaseDirectory,
        };
    }

    private ProcessStartInfo? FolderUpdateStartInfo(UpdateInstallOptions options)
    {
        // The new version, unpacked by Prepare, waits for this process to exit, copies itself over this folder and
        // starts it again. It is started from an elevated process, so Windows doesn't ask again.
        string helper = FolderUpdate.HelperPath(_paths.BaseDirectory);
        if (!File.Exists(helper))
        {
            Log.Warn("Updates", $"The unpacked update is missing ({helper}).");
            return null;
        }

        return new ProcessStartInfo(helper, FolderUpdate.HelperArguments(Environment.ProcessId, options))
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(helper) ?? AppContext.BaseDirectory,
        };
    }
}

/// <summary>Log locations and verbosity.</summary>
public sealed class DiagnosticsService : IDiagnosticsService
{
    private readonly MemoryLogSink _memory;

    public DiagnosticsService(string logDirectory, MemoryLogSink memory)
    {
        LogDirectory = logDirectory;
        _memory = memory;
    }

    public string LogDirectory { get; }

    public void SetVerboseLogging(bool enabled) => Log.MinimumLevel = enabled ? LogLevel.Debug : LogLevel.Info;

    public IReadOnlyList<LogEntry> RecentLog() => _memory.Snapshot();

    /// <summary>Writes out what is still queued, then reads the files back.</summary>
    public IReadOnlyList<LogRecord> ReadLog()
    {
        Log.Flush();
        return LogFile.Read(LogFile.Files(LogDirectory));
    }
}
