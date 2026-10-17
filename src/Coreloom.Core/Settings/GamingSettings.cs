using Coreloom.Core.Gaming;

namespace Coreloom.Core.Settings;

/// <summary>The Gaming page: how the library is shown, the frame limiter, and what Coreloom remembers to undo its changes.</summary>
public sealed class GamingSettings
{
    public GameLibraryView LibraryView { get; set; } = GameLibraryView.List;

    public GameSort LibrarySort { get; set; } = GameSort.RecentlyPlayed;

    /// <summary>The frame limiter's limit (0 = automatic, a little under the refresh rate).</summary>
    public int FramePacerTarget { get; set; }

    /// <summary>The power plan that was active before Ultimate Performance was turned on (restored when it's turned off).</summary>
    public string? PreviousPowerPlan { get; set; }

    /// <summary>The Ultimate Performance plan Coreloom created.</summary>
    public string? UltimatePowerPlan { get; set; }

    /// <summary>
    /// The user allowed missing cover pictures to be downloaded (1.4.0–1.4.3 asked first). Since 1.4.4 pictures are
    /// always downloaded for games without one; kept so older settings files still read.
    /// </summary>
    public bool DownloadArtwork { get; set; }

    /// <summary>The graphics driver version seen last (a new one means shader caches start over).</summary>
    public string? LastDriverVersion { get; set; }

    /// <summary>The driver's limit for all games as last seen (0 = none); the overlay shows when it is pacing a game.</summary>
    public int GlobalFrameCap { get; set; }

    /// <summary>Games with a limit of their own (set in the driver for their programs only).</summary>
    public List<GameFrameCap> GameFrameCaps { get; set; } = [];

    /// <summary>Games added by hand (since 1.4.6).</summary>
    public List<CustomGame> CustomGames { get; set; } = [];

    /// <summary>
    /// Adaptive frame pacing (since 1.4.6): while a library game plays, Coreloom measures its frame times and sets the
    /// game's own limit to a frame rate it sustains steadily. Off until the user turns it on.
    /// </summary>
    public bool AdaptivePacing { get; set; }

    /// <summary>What adaptive pacing learned per game (its limit, frame generation, a limit it couldn't hold).</summary>
    public List<AdaptivePacingGame> AdaptivePacingGames { get; set; } = [];

    /// <summary>What adaptive pacing knows about a game, or null.</summary>
    public AdaptivePacingGame? PacingFor(string gameId) =>
        AdaptivePacingGames.FirstOrDefault(g => string.Equals(g.GameId, gameId, StringComparison.OrdinalIgnoreCase));

    /// <summary>The game's own limit, or null.</summary>
    public GameFrameCap? CapFor(string gameId) =>
        GameFrameCaps.FirstOrDefault(c => string.Equals(c.GameId, gameId, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The limit pacing a program now: its game's own limit when it has one, otherwise the limit for all games (0 = not
    /// paced). <paramref name="executable"/> is a file name or path.
    /// </summary>
    public int ActiveFrameCap(string? executable)
    {
        string? name = GameExecutables.FileName(executable);
        if (name is not null)
        {
            var own = GameFrameCaps.FirstOrDefault(c => c.Executables.Contains(name, StringComparer.OrdinalIgnoreCase));
            if (own is not null)
            {
                return own.Fps;
            }
        }

        return GlobalFrameCap;
    }

    /// <summary>The shader cache size the boost sets (MB): 10 GB, 100 GB or unlimited.</summary>
    public uint ShaderCacheSize { get; set; } = Gaming.ShaderCacheSize.Recommended;

    public void Normalize()
    {
        ShaderCacheSize = Gaming.ShaderCacheSize.Normalize(ShaderCacheSize);
        CustomGames = Gaming.CustomGames.Normalize(CustomGames);
        AdaptivePacingGames = AdaptivePacingGame.Normalize(AdaptivePacingGames);
        if (!Enum.IsDefined(LibraryView))
        {
            LibraryView = GameLibraryView.List;
        }

        if (!Enum.IsDefined(LibrarySort))
        {
            LibrarySort = GameSort.RecentlyPlayed;
        }

        FramePacerTarget = FramePacerTarget <= 0 ? 0 : FrameCap.Clamp(FramePacerTarget);
        GlobalFrameCap = FrameCap.Clamp(GlobalFrameCap);
        GameFrameCaps = (GameFrameCaps ?? [])
            .Where(c => c is not null && !string.IsNullOrWhiteSpace(c.GameId))
            .Select(c =>
            {
                c.Name ??= string.Empty;
                c.Choice = c.Choice <= 0 ? 0 : FrameCap.Clamp(c.Choice);
                c.Fps = FrameCap.Clamp(c.Fps);
                c.Executables = (c.Executables ?? [])
                    .Select(GameExecutables.FileName)
                    .OfType<string>()
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(GameExecutables.Max)
                    .ToList();
                return c;
            })
            .Where(c => c.Fps > 0 && c.Executables.Count > 0)
            .DistinctBy(c => c.GameId, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (PreviousPowerPlan is not null && !Guid.TryParse(PreviousPowerPlan, out _))
        {
            PreviousPowerPlan = null;
        }

        if (UltimatePowerPlan is not null && !Guid.TryParse(UltimatePowerPlan, out _))
        {
            UltimatePowerPlan = null;
        }
    }
}
