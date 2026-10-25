namespace Coreloom.Core.Optimize;

/// <summary>The folders junk is looked for in on a Mac (the home folder and macOS's per-user cache and temp folders).</summary>
/// <param name="UserCache">getconf DARWIN_USER_CACHE_DIR (/var/folders/…/C/), or null.</param>
/// <param name="UserTemp">getconf DARWIN_USER_TEMP_DIR (/var/folders/…/T/), or null.</param>
public sealed record MacJunkFolders(string Home, string? UserCache, string? UserTemp);

/// <summary>
/// The junk Coreloom cleans on a Mac. Like on Windows, only what macOS and apps treat as disposable and rebuild on their
/// own: caches, logs, temporary files and the Trash, and only files nobody has written to for a while. Documents,
/// settings, mail, photos, backups and app data are never touched, and nothing outside the user's own folders is.
/// </summary>
public static class MacJunk
{
    public const string AppCaches = "app-caches";
    public const string Logs = "logs";
    public const string Temp = "temp";
    public const string Developer = "developer";
    public const string Shaders = "shaders";
    public const string Installers = "installers";
    public const string Trash = "trash";

    /// <summary>Caches and logs younger than this stay: an app may be using them.</summary>
    public static readonly TimeSpan CacheAge = TimeSpan.FromDays(3);

    public static readonly TimeSpan InstallerAge = TimeSpan.FromDays(30);

    /// <summary>Apple's own cache folders without a "com.apple." name: left alone, like every "com.apple." one.</summary>
    private static readonly HashSet<string> AppleCaches = new(StringComparer.OrdinalIgnoreCase)
    {
        "CloudKit", "FamilyCircle", "GeoServices", "PassKit", "Maps", "SiriTTS", "ColorSync", "familycircled", "storeassetd",
    };

    /// <summary>
    /// The junk categories. <paramref name="listDirectories"/> lists a folder's subfolders (the file system by default): app
    /// caches are cleaned per app, leaving Apple's own and Coreloom's alone.
    /// </summary>
    public static IReadOnlyList<JunkCategory> Categories(MacJunkFolders folders, Func<string, IEnumerable<string>>? listDirectories = null)
    {
        listDirectories ??= SafeDirectories;
        string library = Path.Combine(folders.Home, "Library");
        var caches = listDirectories(Path.Combine(library, "Caches"))
            .Where(d => IsThirdPartyFolder(Path.GetFileName(d)))
            .Select(d => new JunkLocation(d, MinAge: CacheAge))
            .ToList();
        var logs = listDirectories(Path.Combine(library, "Logs"))
            .Where(d => !string.Equals(Path.GetFileName(d), "Coreloom", StringComparison.OrdinalIgnoreCase))
            .Select(d => new JunkLocation(d, MinAge: CacheAge))
            .ToList();
        var temp = new List<JunkLocation>();
        if (folders.UserTemp is { Length: > 0 } userTemp)
        {
            temp.Add(new JunkLocation(userTemp, MinAge: CacheAge));
        }

        var shaders = new List<JunkLocation>();
        if (folders.UserCache is { Length: > 0 } userCache)
        {
            shaders.Add(new JunkLocation(Path.Combine(userCache, "com.apple.metal"), MinAge: CacheAge));
        }

        shaders.Add(new JunkLocation(Path.Combine(library, "Application Support", "Steam", "steamapps", "shadercache")));
        return
        [
            new(AppCaches, "App caches", "Cached data apps and browsers keep in your Library and rebuild when needed", "Icon.Database",
                caches, SelectedByDefault: true),
            new(Logs, "Logs and crash reports", "Apps' log files and crash reports macOS already sent or no longer needs", "Icon.Warning",
                logs, SelectedByDefault: true),
            new(Temp, "Temporary files", "Files apps left in your temporary folder", "Icon.Clock", temp, SelectedByDefault: true),
            new(Developer, "Developer build files", "Xcode's build products and old simulator caches; Xcode rebuilds them", "Icon.Layers",
                [
                    new(Path.Combine(library, "Developer", "Xcode", "DerivedData")),
                    new(Path.Combine(library, "Developer", "CoreSimulator", "Caches")),
                ], SelectedByDefault: false)
            {
                Caution = "The next build of each Xcode project takes longer",
            },
            new(Shaders, "Shader caches", "Shaders Metal and Steam compiled for your games", "Icon.Gpu", shaders, SelectedByDefault: false)
            {
                Caution = "Games compile them again, which can stutter the first time you play",
            },
            new(Installers, "Old installers in Downloads", "Disk images and installer packages in Downloads you haven't touched for a month", "Icon.Download",
                [
                    new(Path.Combine(folders.Home, "Downloads"), "*.dmg", Recursive: false, MinAge: InstallerAge),
                    new(Path.Combine(folders.Home, "Downloads"), "*.pkg", Recursive: false, MinAge: InstallerAge),
                ], SelectedByDefault: false)
            {
                Caution = "macOS may ask once whether Coreloom can use your Downloads folder",
            },
            new(Trash, "Trash", "Files you've already moved to the Trash", "Icon.Trash", [], SelectedByDefault: false, JunkKind.RecycleBin)
            {
                Caution = "Emptied for good: they can't be put back",
            },
        ];
    }

    /// <summary>A cache folder of a third-party app (not Apple's, not Coreloom's).</summary>
    public static bool IsThirdPartyFolder(string name) =>
        name.Length > 0
        && !name.StartsWith('.')
        && !name.StartsWith("com.apple.", StringComparison.OrdinalIgnoreCase)
        && !AppleCaches.Contains(name)
        && !name.Contains("Coreloom", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> SafeDirectories(string folder)
    {
        try
        {
            return Directory.Exists(folder) ? Directory.GetDirectories(folder) : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
