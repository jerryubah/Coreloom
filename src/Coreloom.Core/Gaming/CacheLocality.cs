using System.Globalization;

namespace Coreloom.Core.Gaming;

/// <summary>Which cores' L3 cache the game in front gets to itself, or why there's nothing to do right now.</summary>
/// <param name="Group">The game's cores (background apps go to the others), or null.</param>
/// <param name="Text">Why these cores, or why none ("AMD's driver keeps games on the cache cores here…").</param>
public sealed record CacheTarget(CoreGroup? Group, string Text);

/// <summary>
/// CPU cache optimization (since 1.8.5). On a processor with more than one L3 cache (a two-CCD Ryzen such as the
/// 9950X3D or 7950X), threads on one CCD can't use the other's L3, so background apps running next to a game share — and
/// evict — the game's cache. While a game is in front this moves known background apps (browsers, chat, music, cloud
/// sync and launchers' web helpers) to the cores outside the game's L3, the way Process Lasso's CPU sets or Task
/// Manager's <em>Set affinity</em> do, and gives them their cores back when the game leaves. It never moves the game, a
/// system process, or an app whose cores were already chosen by hand; and it leaves a Ryzen X3D alone while AMD's 3D
/// V-Cache driver is parking the other CCD (moving apps there would wake those cores and invite the game onto them).
/// </summary>
public static class CacheLocality
{
    /// <summary>Background apps it moves (program file names): heavy, not latency-critical, and not part of a game.</summary>
    public static readonly IReadOnlyList<string> BackgroundApps =
    [
        // browsers
        "chrome.exe", "msedge.exe", "msedgewebview2.exe", "firefox.exe", "brave.exe", "opera.exe", "vivaldi.exe", "arc.exe",
        // chat and calls
        "discord.exe", "slack.exe", "ms-teams.exe", "teams.exe", "whatsapp.exe", "telegram.exe", "signal.exe", "zoom.exe",
        // music
        "spotify.exe", "applemusic.exe", "itunes.exe",
        // cloud sync
        "onedrive.exe", "dropbox.exe", "googledrivefs.exe",
        // launchers' web helpers and stores (the games themselves run separately)
        "steamwebhelper.exe", "epicwebhelper.exe", "epicgameslauncher.exe", "eadesktop.exe", "battle.net.exe", "galaxyclient.exe", "upc.exe",
        // live wallpapers
        "wallpaper32.exe", "wallpaper64.exe",
    ];

    /// <summary>The kinds of apps it moves, for the boost's details.</summary>
    public const string AppKinds = "browsers, chat and calls, music, cloud sync, launchers' web helpers and live wallpapers";

    /// <summary>A program (path or file name) it moves.</summary>
    public static bool IsBackgroundApp(string? program) =>
        GameExecutables.FileName(program) is { } name && BackgroundApps.Contains(name, StringComparer.OrdinalIgnoreCase);

    /// <summary>The processor has more than one L3 cache and Coreloom can choose cores with one mask.</summary>
    public static bool Applies(CoreLayout layout) => layout.CanSetAffinity && layout.L3Caches >= 2 && layout.Groups.Count >= 2;

    /// <summary>Why it can't be used here.</summary>
    public static string UnsupportedText(CoreLayout layout) => !layout.IsKnown
        ? "The processor's caches couldn't be read here."
        : layout.L3Caches <= 1
            ? "Every core here shares one L3 cache, so there's nothing to keep apart."
            : "This processor's cores span more than one processor group, so Coreloom can't choose them.";

    /// <summary>
    /// The game's cores: the cores its core profile keeps it on (when core scheduling applies one); else, with AMD's
    /// driver parking the other CCD, none (AMD's way); else the cache cores of a Ryzen X3D; else, once enough readings are
    /// in, the cores carrying most of the load while the game is in front.
    /// </summary>
    /// <param name="pinned">The cores the game's profile has it on, or null.</param>
    /// <param name="shares">Each group's share of the load while the game is in front, or null until enough readings.</param>
    /// <param name="amdParks">A Ryzen X3D whose 3D V-Cache driver runs with a plan that lets it park cores.</param>
    public static CacheTarget Target(CoreLayout layout, CoreGroup? pinned, IReadOnlyList<double>? shares, bool amdParks, string game)
    {
        if (pinned is not null)
        {
            return new CacheTarget(pinned, $"{game} runs on {Name(pinned)} (its core profile).");
        }

        if (amdParks)
        {
            return new CacheTarget(null, "AMD's driver keeps games on the cache cores here and parks the others, so background apps are left to Windows.");
        }

        if (layout.IsVCache && layout.Group(CoreKind.Cache) is { } cache)
        {
            return new CacheTarget(cache, $"{game} gets the 3D V-Cache to itself.");
        }

        if (shares is null || shares.Count != layout.Groups.Count)
        {
            return new CacheTarget(null, $"Finding where {game} runs…");
        }

        int busiest = 0;
        for (int i = 1; i < shares.Count; i++)
        {
            if (shares[i] > shares[busiest])
            {
                busiest = i;
            }
        }

        var group = layout.Groups[busiest];
        return new CacheTarget(group, $"{game} runs mostly on {Name(group)} ({CoreScheduling.Percent(shares[busiest])} of the load).");
    }

    /// <summary>A core group's name in a sentence: "the cache cores", "CCD 2".</summary>
    public static string Name(CoreGroup group) => group.Kind == CoreKind.Standard ? group.Name : "the " + group.Name.ToLowerInvariant();

    /// <summary>The cores background apps get: every core outside the game's.</summary>
    public static ulong BackgroundMask(CoreLayout layout, CoreGroup target) => layout.AllMask & ~target.Mask;

    /// <summary>
    /// The background apps to move now: running, on the list, not the game, and still on every core (an app whose cores
    /// were chosen by hand is left alone). A process already on <paramref name="backgroundMask"/> is picked too: it
    /// started from one Coreloom moved (a browser's new tab inherits its parent's cores), so it must get every core back
    /// with the others. <paramref name="affinity"/> reads a process's cores (null when it can't).
    /// </summary>
    public static IReadOnlyList<RunningProcess> ToMove(IEnumerable<RunningProcess> running, IReadOnlyCollection<int> gamePids, Func<int, ProcessAffinity?> affinity, IReadOnlyCollection<int> alreadyMoved, ulong backgroundMask = 0)
    {
        var picked = new List<RunningProcess>();
        foreach (var process in running)
        {
            if (gamePids.Contains(process.Id) || alreadyMoved.Contains(process.Id) || !IsBackgroundApp(process.Path))
            {
                continue;
            }

            if (affinity(process.Id) is { } cores && (!cores.IsCustom || (backgroundMask != 0 && cores.Process == backgroundMask)))
            {
                picked.Add(process);
            }
        }

        return picked;
    }

    /// <summary>"6 background apps" / "1 background app".</summary>
    public static string Apps(int count) => $"{count.ToString(CultureInfo.CurrentCulture)} background {(count == 1 ? "app" : "apps")}";
}
