using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Coreloom.Core.Apps;
using Coreloom.Core.Diagnostics;
using Coreloom.ViewModels.Services;
using Microsoft.Win32;

namespace Coreloom.App.Services;

/// <summary>
/// The Applications page on Windows (since 1.8.6): Windows' installed-apps list (the Uninstall keys for everyone, both
/// registry views, and this account) and the Microsoft Store packages of this account, their pictures, the sizes of
/// apps that don't record one (their folders measured, a few seconds at most), and uninstalling with the app's own
/// uninstaller (Store apps through the package manager). Only an app the user confirmed is uninstalled.
/// </summary>
internal sealed class WindowsAppService : IAppService
{
    private const string LogCategory = "Applications";
    private const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    /// <summary>Measuring folders stops after this long in all (the rest keep a dash).</summary>
    private static readonly TimeSpan MeasureBudget = TimeSpan.FromSeconds(12);

    private readonly string _icons;

    public WindowsAppService(string localDirectory)
    {
        _icons = Path.Combine(localDirectory, "AppIcons");
    }

    public bool IsSupported => true;

    public Task<IReadOnlyList<InstalledApp>> InstalledAsync(CancellationToken cancellationToken) => Task.Run<IReadOnlyList<InstalledApp>>(() =>
    {
        var apps = new List<InstalledApp>();
        foreach (var (hive, view, machine) in new[]
        {
            (RegistryHive.LocalMachine, RegistryView.Registry64, true),
            (RegistryHive.LocalMachine, RegistryView.Registry32, true),
            (RegistryHive.CurrentUser, RegistryView.Registry64, false),
        })
        {
            apps.AddRange(FromRegistry(hive, view, machine));
        }

        apps.AddRange(StorePackages());
        return InstalledApps.Dedupe(apps);
    }, cancellationToken);

    public Task<IReadOnlyDictionary<string, long>> MeasureAsync(IReadOnlyList<InstalledApp> apps, CancellationToken cancellationToken) => Task.Run<IReadOnlyDictionary<string, long>>(() =>
    {
        var sizes = new Dictionary<string, long>();
        var clock = Stopwatch.StartNew();
        foreach (var app in apps)
        {
            if (clock.Elapsed > MeasureBudget || cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (app.InstallLocation is { } folder && IsAppFolder(folder) && Directory.Exists(folder))
            {
                long bytes = FolderSize(folder, clock);
                if (bytes > 0)
                {
                    sizes[app.Id] = bytes;
                }
            }
        }

        Log.Info(LogCategory, $"Measured {sizes.Count} of {apps.Count} app folders in {clock.Elapsed.TotalSeconds:0.0} s.");
        return sizes;
    }, cancellationToken);

    public string? IconFor(InstalledApp app)
    {
        string? path = app.IconPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        if (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
        {
            return path; // Store logos are pictures already
        }

        string file = Path.Combine(_icons, Hash(path.ToLowerInvariant()) + ".png");
        if (File.Exists(file))
        {
            return file;
        }

        try
        {
            using var icon = path.EndsWith(".ico", StringComparison.OrdinalIgnoreCase)
                ? new System.Drawing.Icon(path, 64, 64)
                : System.Drawing.Icon.ExtractIcon(path, 0, 64) ?? System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon is null)
            {
                return null;
            }

            Directory.CreateDirectory(_icons);
            using var bitmap = icon.ToBitmap();
            bitmap.Save(file, System.Drawing.Imaging.ImageFormat.Png);
            return file;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or System.Runtime.InteropServices.ExternalException)
        {
            return null;
        }
    }

    public async Task<AppUninstallResult> UninstallAsync(InstalledApp app, CancellationToken cancellationToken)
    {
        if (app.Kind == AppKind.Store)
        {
            return await RemovePackageAsync(app);
        }

        if (app.Uninstall is not { } command)
        {
            return new AppUninstallResult(false, "It has no uninstaller.");
        }

        var (file, arguments) = InstalledApps.SplitCommand(command);
        var started = DateTime.Now;
        try
        {
            using var process = Process.Start(new ProcessStartInfo(file, arguments) { UseShellExecute = false })
                ?? throw new InvalidOperationException("Its uninstaller didn't start.");
            Log.Info(LogCategory, $"Started {app.Name}'s uninstaller ({Path.GetFileName(file)}).");
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            Log.Warn(LogCategory, $"{app.Name}'s uninstaller couldn't start.", e);
            return new AppUninstallResult(false, "Its uninstaller couldn't start. Try Settings › Apps › Installed apps.");
        }

        // Many uninstallers hand over to a copy of themselves (in Temp) and exit at once: wait while one is still running.
        int quiet = 0;
        for (int second = 0; second < 600 && quiet < 3; second++)
        {
            if (!StillListed(app))
            {
                Log.Info(LogCategory, $"{app.Name} was uninstalled.");
                return new AppUninstallResult(true, "Uninstalled.");
            }

            quiet = UninstallerRunning(started) ? 0 : quiet + 1;
            await Task.Delay(1000, cancellationToken);
        }

        bool gone = !StillListed(app);
        Log.Info(LogCategory, gone ? $"{app.Name} was uninstalled." : $"{app.Name} is still installed after its uninstaller closed.");
        return gone
            ? new AppUninstallResult(true, "Uninstalled.")
            : new AppUninstallResult(false, "It's still installed: its uninstaller was closed before it finished.");
    }

    public void OpenFolder(string path) => new ShellService().OpenFolder(path);

    // ---- Reading --------------------------------------------------------------------------------------

    private static List<InstalledApp> FromRegistry(RegistryHive hive, RegistryView view, bool machine)
    {
        var apps = new List<InstalledApp>();
        try
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            using var list = root.OpenSubKey(UninstallPath);
            if (list is null)
            {
                return apps;
            }

            foreach (string name in list.GetSubKeyNames())
            {
                using var key = list.OpenSubKey(name);
                if (key is null)
                {
                    continue;
                }

                var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (string value in key.GetValueNames())
                {
                    values[value] = key.GetValue(value);
                }

                if (InstalledApps.FromUninstallKey(new UninstallKey(name, values, machine)) is { } app)
                {
                    apps.Add(app);
                }
            }
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            Log.Warn(LogCategory, $"Couldn't read the installed apps under {hive} ({view}).", e);
        }

        return apps;
    }

    private static List<InstalledApp> StorePackages()
    {
        var apps = new List<InstalledApp>();
        try
        {
            var manager = new global::Windows.Management.Deployment.PackageManager();
            foreach (var package in manager.FindPackagesForUser(string.Empty))
            {
                try
                {
                    if (package.IsFramework || package.IsResourcePackage || package.IsBundle
                        || package.SignatureKind == global::Windows.ApplicationModel.PackageSignatureKind.System
                        || package.GetAppListEntries().Count == 0)
                    {
                        continue; // parts of Windows and libraries other apps use
                    }

                    string name = package.DisplayName;
                    if (string.IsNullOrWhiteSpace(name) || name.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var v = package.Id.Version;
                    apps.Add(new InstalledApp("store:" + package.Id.FullName, name, AppKind.Store)
                    {
                        Publisher = package.PublisherDisplayName,
                        Version = $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}",
                        Installed = package.InstalledDate.LocalDateTime,
                        InstallLocation = package.InstalledPath,
                        Uninstall = package.Id.FullName,
                        IconPath = LogoFile(package),
                    });
                }
                catch (Exception e) when (e is System.Runtime.InteropServices.COMException or UnauthorizedAccessException or ArgumentException or FileNotFoundException)
                {
                    // a package being installed or removed right now
                }
            }
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            Log.Warn(LogCategory, "Couldn't list the Microsoft Store apps.", e);
        }

        return apps;
    }

    /// <summary>The package's logo file (the manifest names "Assets\StoreLogo.png"; the files carry scale qualifiers).</summary>
    private static string? LogoFile(global::Windows.ApplicationModel.Package package)
    {
        string? path = package.Logo?.LocalPath;
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        if (File.Exists(path))
        {
            return path;
        }

        string? folder = Path.GetDirectoryName(path);
        if (folder is null || !Directory.Exists(folder))
        {
            return null;
        }

        string stem = Path.GetFileNameWithoutExtension(path);
        return Directory.EnumerateFiles(folder, stem + ".*" + Path.GetExtension(path))
            .OrderBy(f => f.Contains("scale-200", StringComparison.OrdinalIgnoreCase) ? 0 : f.Contains("scale-100", StringComparison.OrdinalIgnoreCase) ? 1 : 2)
            .FirstOrDefault();
    }

    private static bool StillListed(InstalledApp app)
    {
        int colon = app.Id.IndexOf(':');
        string kind = app.Id[..colon];
        string name = app.Id[(colon + 1)..];
        var places = kind == "hkcu"
            ? new[] { (RegistryHive.CurrentUser, RegistryView.Registry64) }
            : new[] { (RegistryHive.LocalMachine, RegistryView.Registry64), (RegistryHive.LocalMachine, RegistryView.Registry32) };
        foreach (var (hive, view) in places)
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            using var key = root.OpenSubKey($@"{UninstallPath}\{name}");
            if (key is not null)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>An uninstaller started since the given time is still running (NSIS's Au_/Un_A copies, Inno's unins…, MSI's own window).</summary>
    private static bool UninstallerRunning(DateTime since)
    {
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                string name = process.ProcessName;
                bool looksLikeOne = name.StartsWith("Au_", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Un_", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("unins", StringComparison.OrdinalIgnoreCase) || name.Contains("uninst", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("msiexec", StringComparison.OrdinalIgnoreCase);
                if (!looksLikeOne)
                {
                    continue;
                }

                try
                {
                    if (process.StartTime >= since.AddSeconds(-2))
                    {
                        return true;
                    }
                }
                catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
                {
                    // ended, or a protected process
                }
            }
        }

        return false;
    }

    private static async Task<AppUninstallResult> RemovePackageAsync(InstalledApp app)
    {
        try
        {
            var manager = new global::Windows.Management.Deployment.PackageManager();
            var result = await manager.RemovePackageAsync(app.Uninstall!);
            if (result.ExtendedErrorCode is { } error && error.HResult != 0)
            {
                Log.Warn(LogCategory, $"Removing {app.Name} failed: {result.ErrorText}");
                return new AppUninstallResult(false, "Windows couldn't remove it: " + result.ErrorText);
            }

            Log.Info(LogCategory, $"Removed the Store app {app.Name}.");
            return new AppUninstallResult(true, "Uninstalled.");
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or UnauthorizedAccessException or ArgumentException)
        {
            Log.Warn(LogCategory, $"Removing {app.Name} failed.", e);
            return new AppUninstallResult(false, "Windows couldn't remove it: " + e.Message);
        }
    }

    /// <summary>A folder that belongs to one app (not a drive, Windows, Program Files or a user folder itself).</summary>
    private static bool IsAppFolder(string folder)
    {
        string full;
        try
        {
            full = Path.GetFullPath(folder).TrimEnd('\\');
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        if (full.Length <= 3)
        {
            return false;
        }

        foreach (var special in new[]
        {
            Environment.SpecialFolder.Windows, Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86,
            Environment.SpecialFolder.UserProfile, Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolder.CommonApplicationData, Environment.SpecialFolder.System,
        })
        {
            string path = Environment.GetFolderPath(special).TrimEnd('\\');
            if (path.Length > 0 && full.Equals(path, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return !full.Equals(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"), StringComparison.OrdinalIgnoreCase);
    }

    private static long FolderSize(string folder, Stopwatch clock)
    {
        long total = 0;
        try
        {
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
            foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*", options))
            {
                total += file.Length;
                if (clock.Elapsed > MeasureBudget)
                {
                    break;
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // what was counted so far
        }

        return total;
    }

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16].ToLowerInvariant();
}
