using Coreloom.Core.Gaming;

namespace Coreloom.Core.Settings;

/// <summary>
/// The Gaming page: how the library is shown, what Coreloom remembers to undo its changes, and what it learned per game.
/// The frame limiter's fields (removed from the page in 1.8.4) are kept so older settings files still read and so the
/// limits it left in the driver can be found.
/// </summary>
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

    /// <summary>
    /// Intelligent CPU core scheduling (since 1.4.7): watches which cores the game in front runs on, checks AMD's own
    /// 3D V-Cache scheduling, and applies the per-game core profiles the user kept after a test. Off until turned on.
    /// </summary>
    public bool CoreScheduling { get; set; }

    /// <summary>Games kept on chosen cores (each after an A/B test showed a gain and the user kept it).</summary>
    public List<CoreProfile> CoreProfiles { get; set; } = [];

    /// <summary>macOS (since 1.6.0): keep the display awake while a library game runs. Off until turned on.</summary>
    public bool KeepAwake { get; set; }

    /// <summary>macOS: the charger's power mode before "Full power on the charger" was turned on (restored when it's turned off).</summary>
    public int? PreviousPowerMode { get; set; }

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

    /// <summary>
    /// Intelligent VRAM management (since 1.8.4): Coreloom watches the graphics memory of the game in front and keeps a
    /// recommendation per game. Off until turned on.
    /// </summary>
    public bool VramManagement { get; set; }

    /// <summary>
    /// CPU cache optimization (since 1.8.5): background apps kept off the game's L3 cache while it's in front. Off until
    /// turned on.
    /// </summary>
    public bool CacheOptimization { get; set; }

    /// <summary>What VRAM management learned per game (newest first, at most <see cref="VramRecord.MaxGames"/>).</summary>
    public List<VramRecord> VramGames { get; set; } = [];

    /// <summary>The game's graphics-memory record, or null.</summary>
    public VramRecord? VramFor(string gameId) =>
        VramGames.FirstOrDefault(r => string.Equals(r.GameId, gameId, StringComparison.OrdinalIgnoreCase));

    /// <summary>Keeps a game's record (replacing the old one), newest first.</summary>
    public void KeepVram(VramRecord record)
    {
        VramGames.RemoveAll(r => string.Equals(r.GameId, record.GameId, StringComparison.OrdinalIgnoreCase));
        VramGames.Insert(0, record);
        VramGames = VramRecord.Normalize(VramGames);
    }

    public void Normalize()
    {
        ShaderCacheSize = Gaming.ShaderCacheSize.Normalize(ShaderCacheSize);
        CustomGames = Gaming.CustomGames.Normalize(CustomGames);
        AdaptivePacingGames = AdaptivePacingGame.Normalize(AdaptivePacingGames);
        CoreProfiles = CoreProfile.Normalize(CoreProfiles);
        VramGames = VramRecord.Normalize(VramGames);
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
