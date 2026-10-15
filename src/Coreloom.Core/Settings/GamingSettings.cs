using Coreloom.Core.Gaming;

namespace Coreloom.Core.Settings;

/// <summary>The Gaming page: how the library is shown, the frame pacer, and what Coreloom remembers to undo its changes.</summary>
public sealed class GamingSettings
{
    public GameLibraryView LibraryView { get; set; } = GameLibraryView.List;

    public GameSort LibrarySort { get; set; } = GameSort.RecentlyPlayed;

    /// <summary>The frame pacer's limit (0 = automatic, a little under the refresh rate).</summary>
    public int FramePacerTarget { get; set; }

    /// <summary>The power plan that was active before Ultimate Performance was turned on (restored when it's turned off).</summary>
    public string? PreviousPowerPlan { get; set; }

    /// <summary>The Ultimate Performance plan Coreloom created.</summary>
    public string? UltimatePowerPlan { get; set; }

    /// <summary>The user allowed missing cover pictures to be downloaded from Steam's and Epic's servers.</summary>
    public bool DownloadArtwork { get; set; }

    /// <summary>The graphics driver version seen last (a new one means shader caches start over).</summary>
    public string? LastDriverVersion { get; set; }

    /// <summary>The driver's limit for all games as last seen (0 = none); the overlay shows when it is pacing a game.</summary>
    public int GlobalFrameCap { get; set; }

    /// <summary>Games with a limit of their own (set in the driver for their programs only).</summary>
    public List<GameFrameCap> GameFrameCaps { get; set; } = [];

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

    public void Normalize()
    {
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
