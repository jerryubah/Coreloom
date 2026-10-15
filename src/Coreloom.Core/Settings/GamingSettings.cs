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
