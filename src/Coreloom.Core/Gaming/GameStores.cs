using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace Coreloom.Core.Gaming;

/// <summary>Reads Steam's library files: the library folders, the installed-app manifests and the local artwork cache.</summary>
public static class SteamLibrary
{
    /// <summary>Steam tools that are installed like games but aren't (redistributables, runtimes, Proton, SteamVR).</summary>
    private static readonly HashSet<string> ToolIds = ["228980", "1070560", "1391110", "1628350", "1493710", "2180100", "1826330", "1161040", "250820", "1113280", "1245040", "1420170", "2348590", "3658110"];

    private static readonly string[] ToolNamePrefixes = ["Steamworks Common Redistributables", "Steam Linux Runtime", "Proton ", "Proton-", "SteamVR", "Source SDK Base"];

    /// <summary>Library folders listed in <c>steamapps\libraryfolders.vdf</c>, plus Steam's own folder, without duplicates.</summary>
    public static List<string> LibraryFolders(string steamPath, string? libraryFoldersVdf)
    {
        var folders = new List<string>();
        void Add(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            string p = path.Replace('/', '\\').TrimEnd('\\');
            if (!folders.Any(f => string.Equals(f, p, StringComparison.OrdinalIgnoreCase)))
            {
                folders.Add(p);
            }
        }

        Add(steamPath);
        var root = libraryFoldersVdf is null ? null : KeyValues.Parse(libraryFoldersVdf);
        if (root is not null)
        {
            foreach (var entry in root.Children.Where(c => c.Key.All(char.IsDigit)))
            {
                Add(entry.Value ?? entry.Get("path")); // old files: "1" "D:\\SteamLibrary"; newer: "1" { "path" "…" }
            }
        }

        return folders;
    }

    /// <summary>An installed game from its <c>appmanifest_*.acf</c>; null for tools and unfinished installs.</summary>
    public static GameEntry? FromManifest(string acf, string libraryFolder)
    {
        var state = KeyValues.Parse(acf);
        string? id = state?.Get("appid");
        string? name = state?.Get("name");
        string? installDir = state?.Get("installdir");
        if (state is null || string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name) || !id.All(char.IsDigit))
        {
            return null;
        }

        long flags = state.GetLong("StateFlags") ?? 4;
        if ((flags & 4) == 0 || IsTool(id, name))
        {
            return null; // not fully installed (or downloading for the first time), or not a game
        }

        long? lastPlayed = state.GetLong("LastPlayed");
        long? size = state.GetLong("SizeOnDisk");
        return new GameEntry("steam:" + id, name, GameStore.Steam)
        {
            InstallPath = string.IsNullOrWhiteSpace(installDir) ? null : Path.Combine(libraryFolder, "steamapps", "common", installDir),
            SizeBytes = size > 0 ? size : null,
            LastPlayed = lastPlayed > 0 ? DateTimeOffset.FromUnixTimeSeconds(lastPlayed.Value) : null,
            Launch = "steam://rungameid/" + id,
            CoverUrls = CoverUrls(id),
        };
    }

    public static bool IsTool(string appId, string name) =>
        ToolIds.Contains(appId) || ToolNamePrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase))
        || name.EndsWith("Dedicated Server", StringComparison.OrdinalIgnoreCase);

    /// <summary>Steam's public store pictures for a game (only fetched after the user asks).</summary>
    public static IReadOnlyList<string> CoverUrls(string appId) =>
    [
        $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/library_600x900_2x.jpg",
        $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/library_600x900.jpg",
    ];

    /// <summary>
    /// The pictures Steam keeps for its library in <c>appcache\librarycache</c>: older clients name them
    /// <c>{appid}_library_600x900.jpg</c>, newer ones keep a folder per game (sometimes with a hashed subfolder).
    /// </summary>
    public static GameArtwork FindArtwork(string libraryCache, string appId)
    {
        string? cover = null, banner = null, icon = null;
        try
        {
            if (!Directory.Exists(libraryCache))
            {
                return GameArtwork.None;
            }

            string Flat(string suffix) => Path.Combine(libraryCache, $"{appId}_{suffix}");
            cover = FirstExisting(Flat("library_600x900_2x.jpg"), Flat("library_600x900.jpg"));
            banner = FirstExisting(Flat("header.jpg"), Flat("library_hero.jpg"));
            icon = FirstExisting(Flat("icon.jpg"));

            string folder = Path.Combine(libraryCache, appId);
            if (Directory.Exists(folder))
            {
                var files = new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.AllDirectories)
                    .OrderByDescending(f => f.LastWriteTimeUtc) // a newer picture (a game's new art) wins
                    .ToList();
                cover ??= Pick(files, "library_600x900_2x.jpg", "library_600x900.jpg", "library_capsule.jpg");
                banner ??= Pick(files, "header.jpg", "library_header.jpg", "library_header_2x.jpg", "library_hero.jpg");
                icon ??= files.FirstOrDefault(f => f.Extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                    && Path.GetFileNameWithoutExtension(f.Name).Length == 40 && Path.GetFileNameWithoutExtension(f.Name).All(Uri.IsHexDigit))?.FullName;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // pictures are optional
        }

        return new GameArtwork(cover, banner, icon);
    }

    /// <summary>Steam's per-game shader pre-cache folder in a library.</summary>
    public static string ShaderCacheFolder(string libraryFolder) => Path.Combine(libraryFolder, "steamapps", "shadercache");

    private static string? Pick(List<FileInfo> files, params string[] names)
    {
        foreach (string name in names)
        {
            var match = files.FirstOrDefault(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && f.Length > 0);
            if (match is not null)
            {
                return match.FullName;
            }
        }

        return null;
    }

    private static string? FirstExisting(params string[] paths) => paths.FirstOrDefault(File.Exists);
}

/// <summary>Reads the Epic Games Launcher's install manifests (<c>Data\Manifests\*.item</c>) and its catalog cache.</summary>
public static class EpicLibrary
{
    /// <summary>A game from an install manifest; null for DLC, engine installs and unfinished installs.</summary>
    public static GameEntry? FromManifest(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string? name = Str(root, "DisplayName");
            string? app = Str(root, "AppName");
            string? location = Str(root, "InstallLocation");
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(app) || string.IsNullOrWhiteSpace(location))
            {
                return null;
            }

            if (Bool(root, "bIsIncompleteInstall"))
            {
                return null;
            }

            string? main = Str(root, "MainGameAppName");
            if (!string.IsNullOrEmpty(main) && !string.Equals(main, app, StringComparison.OrdinalIgnoreCase))
            {
                return null; // DLC installs a manifest of its own
            }

            if (root.TryGetProperty("AppCategories", out var categories) && categories.ValueKind == JsonValueKind.Array
                && categories.GetArrayLength() > 0
                && !categories.EnumerateArray().Any(c => string.Equals(c.GetString(), "games", StringComparison.OrdinalIgnoreCase)))
            {
                return null; // Unreal Engine, plugins…
            }

            string? ns = Str(root, "CatalogNamespace");
            string? item = Str(root, "CatalogItemId");
            string? exe = Str(root, "LaunchExecutable");
            long size = root.TryGetProperty("InstallSize", out var s) && s.ValueKind == JsonValueKind.Number && s.TryGetInt64(out long n) ? n : 0;
            string launch = !string.IsNullOrEmpty(ns) && !string.IsNullOrEmpty(item)
                ? $"com.epicgames.launcher://apps/{Uri.EscapeDataString(ns)}%3A{Uri.EscapeDataString(item)}%3A{Uri.EscapeDataString(app)}?action=launch&silent=true"
                : $"com.epicgames.launcher://apps/{Uri.EscapeDataString(app)}?action=launch&silent=true";
            return new GameEntry("epic:" + app, name, GameStore.Epic)
            {
                InstallPath = location,
                SizeBytes = size > 0 ? size : null,
                Launch = launch,
                Executable = string.IsNullOrEmpty(exe) ? null : Path.Combine(location, exe),
                CatalogId = item,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Cover picture links per catalog item from the launcher's <c>Data\Catalog\catcache.bin</c> (base64 JSON),
    /// tall box art first.
    /// </summary>
    public static Dictionary<string, IReadOnlyList<string>> CatalogCovers(string catcache)
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        string json;
        try
        {
            json = Encoding.UTF8.GetString(Convert.FromBase64String(catcache.Trim()));
        }
        catch (FormatException)
        {
            json = catcache; // plain JSON
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            string[] preferred = ["DieselGameBoxTall", "OfferImageTall", "DieselStoreFrontTall", "DieselGameBox", "OfferImageWide", "DieselStoreFrontWide", "Thumbnail"];
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                string? id = Str(item, "id");
                if (id is null || !item.TryGetProperty("keyImages", out var images) || images.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                var byType = images.EnumerateArray()
                    .Select(i => (Type: Str(i, "type") ?? string.Empty, Url: Str(i, "url")))
                    .Where(i => i.Url is not null && i.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                var urls = preferred.SelectMany(t => byType.Where(i => i.Type == t).Select(i => i.Url!)).Distinct().ToList();
                if (urls.Count > 0)
                {
                    result[id] = urls;
                }
            }
        }
        catch (JsonException)
        {
        }

        return result;
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}

/// <summary>Reads GOG's <c>goggame-*.info</c> files (the game's main program).</summary>
public static class GogLibrary
{
    /// <summary>The primary program of a GOG game (relative to its folder), or null.</summary>
    public static string? PrimaryExecutable(string infoJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(infoJson);
            if (!doc.RootElement.TryGetProperty("playTasks", out var tasks) || tasks.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            JsonElement? primary = null;
            foreach (var task in tasks.EnumerateArray())
            {
                bool isPrimary = task.TryGetProperty("isPrimary", out var p) && p.ValueKind == JsonValueKind.True;
                bool isFile = !task.TryGetProperty("type", out var t) || t.GetString() == "FileTask";
                if (isFile && task.TryGetProperty("path", out var path) && path.ValueKind == JsonValueKind.String)
                {
                    if (isPrimary)
                    {
                        return path.GetString();
                    }

                    primary ??= task;
                }
            }

            return primary?.GetProperty("path").GetString();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>What an Xbox (PC Game Pass / Microsoft Store) game's <c>MicrosoftGame.config</c> says about it.</summary>
public sealed record XboxGameConfig(string IdentityName, string Publisher, string? DisplayName, string? ExecutableId, string? Executable)
{
    public string? StoreLogo { get; init; }

    public string? Square150Logo { get; init; }

    public string? SplashScreen { get; init; }
}

public static class XboxLibrary
{
    private const string Base32 = "0123456789abcdefghjkmnpqrstvwxyz";

    public static XboxGameConfig? ParseConfig(string xml)
    {
        try
        {
            var doc = XDocument.Parse(xml);
            var root = doc.Root;
            if (root is null)
            {
                return null;
            }

            XElement? El(string name) => root.Descendants().FirstOrDefault(e => e.Name.LocalName == name);
            string? Attr(XElement? e, string name) => e?.Attributes().FirstOrDefault(a => a.Name.LocalName == name)?.Value is { Length: > 0 } v ? v : null;

            var identity = El("Identity");
            string? name = Attr(identity, "Name");
            string? publisher = Attr(identity, "Publisher");
            if (name is null || publisher is null)
            {
                return null;
            }

            var executable = root.Descendants().FirstOrDefault(e => e.Name.LocalName == "Executable" && Attr(e, "Name") is not null);
            var visuals = El("ShellVisuals");
            return new XboxGameConfig(name, publisher, Attr(visuals, "DefaultDisplayName"), Attr(executable, "Id"), Attr(executable, "Name"))
            {
                StoreLogo = Attr(visuals, "StoreLogo"),
                Square150Logo = Attr(visuals, "Square150x150Logo"),
                SplashScreen = Attr(visuals, "SplashScreenImage"),
            };
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    /// <summary>
    /// The 13-character publisher id of a package family name: the first 8 bytes of the SHA-256 of the publisher
    /// (UTF-16), in Crockford's base 32.
    /// </summary>
    public static string PublisherId(string publisher)
    {
        byte[] hash = SHA256.HashData(Encoding.Unicode.GetBytes(publisher));
        ulong bits = 0;
        for (int i = 0; i < 8; i++)
        {
            bits = (bits << 8) | hash[i];
        }

        var sb = new StringBuilder(13);
        for (int i = 0; i < 13; i++)
        {
            int shift = 59 - (i * 5); // 65 bits: the last character takes the final 4 bits and a zero
            int index = shift >= 0 ? (int)((bits >> shift) & 31) : (int)((bits << 1) & 31);
            sb.Append(Base32[index]);
        }

        return sb.ToString();
    }

    public static string FamilyName(string identityName, string publisher) => identityName + "_" + PublisherId(publisher);
}

/// <summary>A program from Windows' "installed apps" list (the Uninstall registry keys).</summary>
public sealed record UninstallEntry(string KeyName, string? DisplayName, string? Publisher, string? UninstallString, string? InstallLocation, string? DisplayIcon)
{
    public long? EstimatedSizeKb { get; init; }
}

/// <summary>Recognises games of launchers that don't keep a readable library file (Ubisoft, EA, Battle.net, Riot).</summary>
public static class LauncherGames
{
    public static GameEntry? FromUninstall(UninstallEntry entry)
    {
        string name = entry.DisplayName?.Trim() ?? string.Empty;
        string publisher = entry.Publisher ?? string.Empty;
        string uninstall = entry.UninstallString ?? string.Empty;
        if (name.Length == 0)
        {
            return null;
        }

        long? size = entry.EstimatedSizeKb > 0 ? entry.EstimatedSizeKb * 1024 : null;
        string? icon = IconPath(entry.DisplayIcon);
        string? location = string.IsNullOrWhiteSpace(entry.InstallLocation) ? null : entry.InstallLocation.Trim().Trim('"');

        // Ubisoft Connect: "Uplay Install 635"
        if (entry.KeyName.StartsWith("Uplay Install ", StringComparison.OrdinalIgnoreCase))
        {
            string id = entry.KeyName["Uplay Install ".Length..].Trim();
            return id.All(char.IsDigit) && id.Length > 0
                ? new GameEntry("ubisoft:" + id, name, GameStore.Ubisoft) { InstallPath = location, SizeBytes = size, Launch = $"uplay://launch/{id}/0", Executable = icon }
                : null;
        }

        // Riot Client: "…RiotClientServices.exe" --uninstall-product=valorant --uninstall-patchline=live
        if (uninstall.Contains("RiotClientServices.exe", StringComparison.OrdinalIgnoreCase) && uninstall.Contains("--uninstall-product=", StringComparison.OrdinalIgnoreCase))
        {
            string product = Argument(uninstall, "--uninstall-product=");
            string patchline = Argument(uninstall, "--uninstall-patchline=");
            string client = uninstall[..(uninstall.IndexOf("RiotClientServices.exe", StringComparison.OrdinalIgnoreCase) + "RiotClientServices.exe".Length)].Trim('"', ' ');
            return product.Length == 0 ? null : new GameEntry("riot:" + product, name, GameStore.Riot)
            {
                InstallPath = location,
                SizeBytes = size,
                Launch = $"\"{client}\" --launch-product={product} --launch-patchline={(patchline.Length > 0 ? patchline : "live")}",
                Executable = icon,
            };
        }

        // Battle.net: "…\Blizzard Uninstaller.exe" --lang=enUS --uid=wow
        if (uninstall.Contains("Blizzard Uninstaller", StringComparison.OrdinalIgnoreCase) && !name.Equals("Battle.net", StringComparison.OrdinalIgnoreCase))
        {
            string uid = Argument(uninstall, "--uid=");
            return new GameEntry("battlenet:" + (uid.Length > 0 ? uid : name), name, GameStore.BattleNet)
            {
                InstallPath = location,
                SizeBytes = size,
                Launch = icon,
                Executable = icon,
            };
        }

        // EA app: published by Electronic Arts and removed with the EA installer.
        if (publisher.Contains("Electronic Arts", StringComparison.OrdinalIgnoreCase)
            && (uninstall.Contains("EAInstaller", StringComparison.OrdinalIgnoreCase) || uninstall.Contains("Cleanup.exe", StringComparison.OrdinalIgnoreCase) || uninstall.Contains("Touchup.exe", StringComparison.OrdinalIgnoreCase))
            && !name.Equals("EA app", StringComparison.OrdinalIgnoreCase) && !name.StartsWith("Origin", StringComparison.OrdinalIgnoreCase))
        {
            return location is null ? null : new GameEntry("ea:" + entry.KeyName, name, GameStore.EA)
            {
                InstallPath = location,
                SizeBytes = size,
                Launch = icon,
                Executable = icon,
            };
        }

        return null;
    }

    /// <summary>"C:\Games\x.exe,0" → "C:\Games\x.exe" (null unless it names a program).</summary>
    public static string? IconPath(string? displayIcon)
    {
        if (string.IsNullOrWhiteSpace(displayIcon))
        {
            return null;
        }

        string s = displayIcon.Trim();
        int comma = s.LastIndexOf(',');
        if (comma > 0 && int.TryParse(s[(comma + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            s = s[..comma];
        }

        s = s.Trim().Trim('"');
        return s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || s.EndsWith(".ico", StringComparison.OrdinalIgnoreCase) ? s : null;
    }

    private static string Argument(string command, string prefix)
    {
        int at = command.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (at < 0)
        {
            return string.Empty;
        }

        int start = at + prefix.Length;
        int end = command.IndexOfAny([' ', '"'], start);
        return (end < 0 ? command[start..] : command[start..end]).Trim();
    }
}
