using System.Globalization;
using Coreloom.Core.Optimize;

namespace Coreloom.Core.Apps;

/// <summary>Where an app comes from.</summary>
public enum AppKind
{
    /// <summary>Installed with its own installer (Windows' "installed apps" list, the Uninstall registry keys).</summary>
    Desktop,

    /// <summary>A Microsoft Store (MSIX) package.</summary>
    Store,
}

/// <summary>An installed app on the Applications page (since 1.8.6).</summary>
public sealed record InstalledApp(string Id, string Name, AppKind Kind)
{
    public string? Publisher { get; init; }

    public string? Version { get; init; }

    public DateTime? Installed { get; init; }

    /// <summary>Its size on disk, when known (the installer's estimate, or the folder measured).</summary>
    public long? SizeBytes { get; init; }

    public string? InstallLocation { get; init; }

    /// <summary>The app's own uninstaller (a command line), or the Store package's full name for Store apps.</summary>
    public string? Uninstall { get; init; }

    /// <summary>A program or icon file for its picture ("…\app.exe,0" is read as the file).</summary>
    public string? IconPath { get; init; }

    /// <summary>Installed for every account (HKLM) rather than just this one.</summary>
    public bool ForEveryone { get; init; }

    public bool CanUninstall => !string.IsNullOrWhiteSpace(Uninstall);
}

/// <summary>An Uninstall key's values (one per app in Windows' installed-apps list).</summary>
public sealed record UninstallKey(string KeyName, IReadOnlyDictionary<string, object?> Values, bool Machine);

/// <summary>Reading and judging Windows' installed-apps list, uninstall commands and startup entries (since 1.8.6).</summary>
public static class InstalledApps
{
    /// <summary>
    /// The app an Uninstall key describes, as Windows' Settings › Apps lists it: it has a name and an uninstaller, and
    /// isn't a system component, an update to another app or a part of one.
    /// </summary>
    public static InstalledApp? FromUninstallKey(UninstallKey key)
    {
        string? Text(string name) => key.Values.TryGetValue(name, out var value) && value is string s && !string.IsNullOrWhiteSpace(s) ? s.Trim() : null;
        int? Number(string name) => key.Values.TryGetValue(name, out var value) ? value switch
        {
            int i => i,
            long l => (int)l,
            string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) => n,
            _ => null,
        } : null;

        string? name = Text("DisplayName");
        if (name is null || Number("SystemComponent") == 1 || Text("ParentKeyName") is not null)
        {
            return null;
        }

        if (Text("ReleaseType") is { } release && (release.Contains("Update", StringComparison.OrdinalIgnoreCase) || release.Contains("Hotfix", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        string? uninstall = Text("UninstallString");
        if (Number("WindowsInstaller") == 1 && Guid.TryParse(key.KeyName, out _))
        {
            uninstall = $"MsiExec.exe /X{key.KeyName}"; // Windows Installer packages: the product code is the key's name
        }

        if (uninstall is null)
        {
            return null; // nothing to uninstall it with (Settings doesn't list these either)
        }

        long? size = Number("EstimatedSize") is int kb && kb > 0 ? kb * 1024L : null;
        return new InstalledApp($"{(key.Machine ? "hklm" : "hkcu")}:{key.KeyName}", name, AppKind.Desktop)
        {
            Publisher = Text("Publisher"),
            Version = Text("DisplayVersion"),
            Installed = ParseInstallDate(Text("InstallDate")),
            SizeBytes = size,
            InstallLocation = Text("InstallLocation")?.Trim('"'),
            Uninstall = uninstall,
            IconPath = IconFile(Text("DisplayIcon")),
            ForEveryone = key.Machine,
        };
    }

    /// <summary>"20240115" (the usual form), else a date Windows' own culture-free parsing reads.</summary>
    public static DateTime? ParseInstallDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (DateTime.TryParseExact(text.Trim(), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out date) && date.Year > 1990 ? date : null;
    }

    /// <summary>The file in a DisplayIcon value ("\"C:\\…\\app.exe\",0" → C:\…\app.exe).</summary>
    public static string? IconFile(string? displayIcon)
    {
        if (string.IsNullOrWhiteSpace(displayIcon))
        {
            return null;
        }

        string text = displayIcon.Trim();
        if (text.StartsWith('"'))
        {
            int end = text.IndexOf('"', 1);
            return end > 1 ? text[1..end] : null;
        }

        int comma = text.LastIndexOf(',');
        if (comma > 0 && text[(comma + 1)..].Trim().TrimStart('-').All(char.IsAsciiDigit))
        {
            text = text[..comma];
        }

        return text.Trim();
    }

    /// <summary>
    /// Splits a command line into the program and its arguments: a quoted program, else the text up to ".exe" (paths with
    /// spaces are often left unquoted in the registry), else up to the first space.
    /// </summary>
    public static (string File, string Arguments) SplitCommand(string command)
    {
        string text = command.Trim();
        if (text.StartsWith('"'))
        {
            int end = text.IndexOf('"', 1);
            if (end > 0)
            {
                return (text[1..end], text[(end + 1)..].Trim());
            }
        }

        int exe = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exe > 0)
        {
            int after = exe + 4;
            return (text[..after].Trim(), text[after..].Trim());
        }

        int space = text.IndexOf(' ');
        return space > 0 ? (text[..space], text[(space + 1)..].Trim()) : (text, string.Empty);
    }

    /// <summary>
    /// One line per app: the same app listed in both the 64- and 32-bit lists (or for this account and everyone) is kept
    /// once, the entry with a size first.
    /// </summary>
    public static IReadOnlyList<InstalledApp> Dedupe(IEnumerable<InstalledApp> apps) =>
    [
        .. apps
            .GroupBy(a => (Name: a.Name.ToUpperInvariant(), Version: a.Version?.ToUpperInvariant(), a.Kind))
            .Select(g => g.OrderByDescending(a => a.SizeBytes.HasValue).ThenByDescending(a => a.ForEveryone).First()),
    ];

    /// <summary>
    /// The installed app a startup entry belongs to: the one whose folder holds the program it starts, else one whose name
    /// is the entry's name.
    /// </summary>
    public static InstalledApp? ForStartup(IReadOnlyList<InstalledApp> apps, StartupApp startup)
    {
        string? program = startup.Executable;
        if (!string.IsNullOrWhiteSpace(program))
        {
            var byFolder = apps
                .Where(a => !string.IsNullOrWhiteSpace(a.InstallLocation) && IsUnder(program, a.InstallLocation!))
                .OrderByDescending(a => a.InstallLocation!.Length)
                .FirstOrDefault();
            if (byFolder is not null)
            {
                return byFolder;
            }
        }

        return apps.FirstOrDefault(a => a.Name.Equals(startup.Name, StringComparison.OrdinalIgnoreCase))
            ?? apps.FirstOrDefault(a => startup.Name.Length >= 4 && (a.Name.StartsWith(startup.Name + " ", StringComparison.OrdinalIgnoreCase)
                || a.Name.EndsWith(" " + startup.Name, StringComparison.OrdinalIgnoreCase)));
    }

    private static bool IsUnder(string file, string folder)
    {
        string f = folder.Replace('/', '\\').TrimEnd('\\') + "\\";
        return file.Replace('/', '\\').StartsWith(f, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>"3 apps" / "1 app".</summary>
    public static string Apps(int count) => $"{count.ToString(CultureInfo.CurrentCulture)} {(count == 1 ? "app" : "apps")}";
}
