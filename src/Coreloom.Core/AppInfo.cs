using System.Reflection;
using Coreloom.Core.Updates;

namespace Coreloom.Core;

/// <summary>Version and build metadata of the running application.</summary>
public sealed record AppVersionInfo(SemanticVersion Version, string InformationalVersion, string? Repository, string? CommitHash)
{
    public const string ProductName = "Coreloom";

    public string DisplayVersion => Version.ToString();

    public static AppVersionInfo FromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        string informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString(3)
            ?? "0.0.0";

        // The SDK appends "+<commit sha>" to the informational version when SourceLink data is available.
        string? commit = null;
        string versionText = informational;
        int plus = informational.IndexOf('+');
        if (plus >= 0)
        {
            commit = informational[(plus + 1)..];
            versionText = informational[..plus];
        }

        if (!SemanticVersion.TryParse(versionText, out var version))
        {
            version = new SemanticVersion(0, 0, 0);
        }

        string? repository = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "UpdateRepository")?.Value;

        return new AppVersionInfo(version, informational, repository, commit);
    }
}

/// <summary>
/// File-system locations used by the app. A file named <c>portable.txt</c> next to the executable switches
/// to portable mode, where settings and logs are stored in a <c>Data</c> folder beside the app.
/// </summary>
public sealed class AppPaths
{
    public const string PortableMarkerFile = "portable.txt";

    /// <summary>The product's name before it was renamed; its settings folder is imported once.</summary>
    public const string PreviousProductName = "Vigil"; // legacy-name

    private readonly string? _previousSettingsFile;
    private readonly string? _logDirectory;

    private AppPaths(bool isPortable, string baseDirectory, string settingsDirectory, string localDirectory, string? previousSettingsFile = null, string? logDirectory = null)
    {
        IsPortable = isPortable;
        BaseDirectory = baseDirectory;
        SettingsDirectory = settingsDirectory;
        LocalDirectory = localDirectory;
        _previousSettingsFile = previousSettingsFile;
        _logDirectory = logDirectory;
    }

    public bool IsPortable { get; }

    /// <summary>Folder containing the executable.</summary>
    public string BaseDirectory { get; }

    /// <summary>Roaming settings folder (%AppData%\Coreloom, or Data\ in portable mode).</summary>
    public string SettingsDirectory { get; }

    /// <summary>Machine-local folder for logs and downloads (%LocalAppData%\Coreloom, or Data\ in portable mode).</summary>
    public string LocalDirectory { get; }

    public string SettingsFile => Path.Combine(SettingsDirectory, "settings.json");

    /// <summary>Optional folder where the user can place vendor logo files (nvidia.png, amd.png, intel.png).</summary>
    public string VendorLogoDirectory => Path.Combine(SettingsDirectory, "Vendors");

    public string LogDirectory => _logDirectory ?? Path.Combine(LocalDirectory, "Logs");

    public string DownloadDirectory => Path.Combine(LocalDirectory, "Updates");

    /// <summary>True when the app was installed by the Inno Setup installer (an uninstaller sits next to it).</summary>
    public bool IsInstalled => File.Exists(Path.Combine(BaseDirectory, "unins000.exe"));

    public static AppPaths Resolve(string baseDirectory, string? appDataRoot = null, string? localAppDataRoot = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        if (File.Exists(Path.Combine(baseDirectory, PortableMarkerFile)))
        {
            string data = Path.Combine(baseDirectory, "Data");
            return new AppPaths(true, baseDirectory, data, data);
        }

        string roaming = appDataRoot ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string local = localAppDataRoot ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new AppPaths(
            false,
            baseDirectory,
            Path.Combine(roaming, AppVersionInfo.ProductName),
            Path.Combine(local, AppVersionInfo.ProductName),
            Path.Combine(roaming, PreviousProductName, "settings.json"));
    }

    /// <summary>
    /// The Mac's locations, as macOS apps keep them: settings, downloads and the like in
    /// <c>~/Library/Application Support/Coreloom</c>, logs in <c>~/Library/Logs/Coreloom</c> (where Console finds them).
    /// <paramref name="baseDirectory"/> is the app bundle's <c>Contents/MacOS</c> folder.
    /// </summary>
    public static AppPaths ForMac(string baseDirectory, string? home = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        home ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string support = Path.Combine(home, "Library", "Application Support", AppVersionInfo.ProductName);
        return new AppPaths(false, baseDirectory, support, support, logDirectory: Path.Combine(home, "Library", "Logs", AppVersionInfo.ProductName));
    }

    /// <summary>
    /// Copies <c>settings.json</c> from the previous product's folder (<c>%AppData%\Vigil</c>) the first time the
    /// renamed app starts, so appearance, alerts and the dashboard layout carry over. Never overwrites existing
    /// settings, never touches the old folder, and does nothing in portable mode. Returns true when it copied.
    /// </summary>
    public bool MigratePreviousSettings()
    {
        if (_previousSettingsFile is null || File.Exists(SettingsFile) || !File.Exists(_previousSettingsFile))
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            File.Copy(_previousSettingsFile, SettingsFile, overwrite: false);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(SettingsDirectory);
        Directory.CreateDirectory(LocalDirectory);
        Directory.CreateDirectory(LogDirectory);
    }
}
