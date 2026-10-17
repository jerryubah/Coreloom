using System.Globalization;

namespace Coreloom.Core.Gaming;

/// <summary>Where a game was installed from.</summary>
public enum GameStore
{
    Steam,
    Epic,
    Gog,
    Xbox,
    Ubisoft,
    EA,
    BattleNet,
    Riot,

    /// <summary>Added by hand (since 1.4.6).</summary>
    Custom,
    Other,
}

/// <summary>How the Gaming page shows the library.</summary>
public enum GameLibraryView
{
    List,
    Grid,
}

/// <summary>Order of the library.</summary>
public enum GameSort
{
    RecentlyPlayed,
    Name,
    Size,
}

/// <summary>Pictures of a game found on this PC: a portrait cover (600×900), a wide banner and a small icon.</summary>
public sealed record GameArtwork(string? Cover, string? Banner, string? Icon)
{
    public static GameArtwork None { get; } = new(null, null, null);

    public bool HasAny => Cover is not null || Banner is not null || Icon is not null;

    /// <summary>Fills missing pictures from <paramref name="other"/>.</summary>
    public GameArtwork Or(GameArtwork other) => new(Cover ?? other.Cover, Banner ?? other.Banner, Icon ?? other.Icon);
}

/// <summary>An installed game.</summary>
/// <param name="Id">Unique across stores: "steam:730", "epic:Fortnite", "gog:1207658924"…</param>
public sealed record GameEntry(string Id, string Name, GameStore Store)
{
    public string? InstallPath { get; init; }

    public long? SizeBytes { get; init; }

    public DateTimeOffset? LastPlayed { get; init; }

    /// <summary>What starts the game: a store link (steam://…), an app id (shell:AppsFolder\…) or a program path.</summary>
    public string? Launch { get; init; }

    /// <summary>The game's program, when known (for its icon).</summary>
    public string? Executable { get; init; }

    /// <summary>The store's catalog id, where pictures are looked up by it (Epic).</summary>
    public string? CatalogId { get; init; }

    public GameArtwork Artwork { get; init; } = GameArtwork.None;

    /// <summary>Where the store keeps a cover picture, best first (tried first when the game has no picture on the PC).</summary>
    public IReadOnlyList<string> CoverUrls { get; init; } = [];

    /// <summary>The store's own id (Steam app id, Epic app name…).</summary>
    public string StoreId => Id[(Id.IndexOf(':') + 1)..];
}

/// <summary>A running program: its process id and file.</summary>
public readonly record struct RunningProcess(int Id, string Path);

/// <summary>A game that is running, and the ids of its programs.</summary>
public sealed record RunningGame(GameEntry Game, IReadOnlyList<int> ProcessIds);

/// <summary>Sorting, searching and recognising running games.</summary>
public static class GameLibrary
{
    /// <summary>Launchers and helpers that live in game folders but aren't the game itself.</summary>
    private static readonly HashSet<string> NotGames = new(StringComparer.OrdinalIgnoreCase)
    {
        "steam.exe", "steamwebhelper.exe", "steamservice.exe", "steamerrorreporter.exe", "steamerrorreporter64.exe",
        "EpicGamesLauncher.exe", "EpicWebHelper.exe", "UnrealCEFSubProcess.exe", "CrashReportClient.exe",
        "GalaxyClient.exe", "GalaxyClient Helper.exe", "GOG Galaxy Notifications Renderer.exe",
        "upc.exe", "UbisoftConnect.exe", "UplayWebCore.exe", "EADesktop.exe", "EABackgroundService.exe", "Battle.net.exe",
        "Agent.exe", "RiotClientServices.exe", "RiotClientUx.exe", "RiotClientCrashHandler.exe", "vc_redist.x64.exe", "vc_redist.x86.exe",
        "DXSETUP.exe", "dotnetfx.exe", "UE4PrereqSetup_x64.exe", "UEPrereqSetup_x64.exe", "EasyAntiCheat.exe", "EasyAntiCheat_EOS.exe",
        "EasyAntiCheat_EOS_Setup.exe", "BEService.exe", "BEService_x64.exe", "unins000.exe", "QtWebEngineProcess.exe",
    };

    /// <summary>A launcher, helper or installer that lives in game folders ("unins000.exe", "EasyAntiCheat.exe").</summary>
    public static bool IsNotGame(string fileName) => NotGames.Contains(fileName);

    public static string StoreName(GameStore store) => store switch
    {
        GameStore.Steam => "Steam",
        GameStore.Epic => "Epic Games",
        GameStore.Gog => "GOG",
        GameStore.Xbox => "Xbox",
        GameStore.Ubisoft => "Ubisoft Connect",
        GameStore.EA => "EA app",
        GameStore.BattleNet => "Battle.net",
        GameStore.Riot => "Riot Games",
        GameStore.Custom => "Added by you",
        _ => "Other",
    };

    /// <summary>One entry per game id (the first wins), without empty names.</summary>
    public static List<GameEntry> Merge(IEnumerable<GameEntry> games)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<GameEntry>();
        foreach (var game in games)
        {
            if (string.IsNullOrWhiteSpace(game.Name) || !seen.Add(game.Id))
            {
                continue;
            }

            // The same folder found twice (e.g. a game registered by two launchers) is one game.
            string? path = NormalizeFolder(game.InstallPath);
            if (path is not null && !paths.Add(path))
            {
                continue;
            }

            result.Add(game with { Name = game.Name.Trim() });
        }

        return result;
    }

    public static List<GameEntry> Sort(IEnumerable<GameEntry> games, GameSort sort)
    {
        var byName = StringComparer.Create(CultureInfo.CurrentCulture, CompareOptions.IgnoreCase);
        return sort switch
        {
            GameSort.Size => games.OrderByDescending(g => g.SizeBytes ?? -1).ThenBy(g => SortName(g.Name), byName).ToList(),
            GameSort.RecentlyPlayed => games.OrderByDescending(g => g.LastPlayed ?? DateTimeOffset.MinValue).ThenBy(g => SortName(g.Name), byName).ToList(),
            _ => games.OrderBy(g => SortName(g.Name), byName).ToList(),
        };
    }

    /// <summary>"The Witcher 3" sorts under W.</summary>
    public static string SortName(string name) =>
        name.StartsWith("The ", StringComparison.OrdinalIgnoreCase) && name.Length > 4 ? name[4..] : name;

    /// <summary>Whether a game matches the search (name or store, any word order).</summary>
    public static bool Matches(GameEntry game, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return true;
        }

        string haystack = game.Name + " " + StoreName(game.Store);
        return search.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .All(word => haystack.Contains(word, StringComparison.CurrentCultureIgnoreCase));
    }

    /// <summary>"CS" for "Counter-Strike 2", "RD" for "Red Dead Redemption 2": shown where a game has no picture.</summary>
    public static string Initials(string name)
    {
        var words = name.Split([' ', '-', ':', '_', '.'], StringSplitOptions.RemoveEmptyEntries)
            .Where(w => char.IsLetterOrDigit(w[0]) && !IsMinorWord(w))
            .ToList();
        if (words.Count == 0)
        {
            return name.Length > 0 ? name[..1].ToUpperInvariant() : "?";
        }

        var letters = words.Where(w => !w.All(char.IsDigit)).Take(2).Select(w => char.ToUpperInvariant(w[0])).ToList();
        return letters.Count > 0 ? string.Concat(letters) : words[0][..1];
    }

    private static bool IsMinorWord(string w) => w.Equals("the", StringComparison.OrdinalIgnoreCase) || w.Equals("of", StringComparison.OrdinalIgnoreCase) || w.Equals("and", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The game whose programs are running (they live in the game's folder), with those programs, or null. Launchers,
    /// crash reporters and installers in game folders don't count.
    /// </summary>
    public static RunningGame? FindRunning(IReadOnlyList<GameEntry> games, IEnumerable<RunningProcess> processes)
    {
        var programs = games
            .Where(g => g.Store == GameStore.Custom && !string.IsNullOrEmpty(g.Executable))
            .ToList();
        var folders = games
            .Where(g => g.Store != GameStore.Custom || !CustomGames.IsBroadFolder(g.InstallPath)) // a game added from Desktop isn't every program there
            .Select(g => (Game: g, Folder: NormalizeFolder(g.InstallPath)))
            .Where(x => x.Folder is not null && x.Folder.Length > 3) // never a whole drive
            .OrderByDescending(x => x.Folder!.Length) // the most specific folder wins (a game inside another's folder)
            .ToList();
        GameEntry? found = null;
        var ids = new List<int>();
        foreach (var process in processes)
        {
            string path = process.Path;
            if (string.IsNullOrEmpty(path) || NotGames.Contains(path[(path.LastIndexOfAny(['\\', '/']) + 1)..]))
            {
                continue;
            }

            var exact = programs.FirstOrDefault(g => string.Equals(g.Executable, path, StringComparison.OrdinalIgnoreCase));
            if (exact is not null)
            {
                if (found is null || ReferenceEquals(found, exact))
                {
                    found = exact;
                    ids.Add(process.Id);
                }

                continue;
            }

            foreach (var (game, folder) in folders)
            {
                if (path.StartsWith(folder!, StringComparison.OrdinalIgnoreCase))
                {
                    if (found is null || ReferenceEquals(found, game))
                    {
                        found = game;
                        ids.Add(process.Id);
                    }

                    break;
                }
            }
        }

        return found is null ? null : new RunningGame(found, ids);
    }

    /// <summary>"C:\Games\Foo" → "C:\Games\Foo\" (so "C:\Games\Foo2" doesn't match it).</summary>
    public static string? NormalizeFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string p = path.Trim().Replace('/', '\\').TrimEnd('\\');
        return p.Length == 0 ? null : p + "\\";
    }

    /// <summary>"Played today", "Played 3 days ago", "Not played yet".</summary>
    public static string LastPlayedText(DateTimeOffset? lastPlayed, DateTimeOffset now)
    {
        if (lastPlayed is not { } at || at.ToUnixTimeSeconds() <= 0)
        {
            return "Not played yet";
        }

        var local = at.ToLocalTime().Date;
        int days = (now.ToLocalTime().Date - local).Days;
        return days switch
        {
            <= 0 => "Played today",
            1 => "Played yesterday",
            < 30 => $"Played {days} days ago",
            < 365 => $"Played {local.ToString("MMM d", CultureInfo.CurrentCulture)}",
            _ => $"Played {local.ToString("MMM yyyy", CultureInfo.CurrentCulture)}",
        };
    }
}
