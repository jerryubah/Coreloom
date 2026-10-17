namespace Coreloom.Core.Telemetry;

/// <summary>
/// Lighter monitoring while a game is in front: sensors are read less often (and below normal priority), and Coreloom's
/// windows that the game covers stop redrawing, so Coreloom takes as little as possible from the game.
/// </summary>
public static class AdaptiveMonitoring
{
    /// <summary>How often sensors are read while gaming, unless chosen otherwise (2 s).</summary>
    public const int DefaultGamingIntervalMs = 2000;

    /// <summary>The choices offered for gaming (milliseconds).</summary>
    public static IReadOnlyList<int> GamingIntervals { get; } = [1000, 2000, 5000];

    /// <summary>
    /// The interval in effect: the chosen one; while a game is in front (and lighter monitoring is on) the gaming one, but
    /// never faster than the chosen one, and no slower than 1 s while the in-game overlay shows sensor readings (they
    /// would look stuck otherwise).
    /// </summary>
    public static int IntervalMs(int chosenMs, bool gameInFront, bool lighterWhileGaming, int gamingMs, bool overlayShowsReadings)
    {
        if (!gameInFront || !lighterWhileGaming)
        {
            return chosenMs;
        }

        int interval = Math.Max(chosenMs, gamingMs);
        return overlayShowsReadings ? Math.Min(interval, Math.Max(chosenMs, 1000)) : interval;
    }

    /// <summary>A gaming interval that is one of the choices (else the default).</summary>
    public static int NormalizeGamingInterval(int ms) => GamingIntervals.Contains(ms) ? ms : DefaultGamingIntervalMs;

    /// <summary>
    /// Whether a game is in front: a program other than Coreloom whose window fills its screen (fullscreen or
    /// borderless), or one of the games in the library.
    /// </summary>
    public static bool IsGameInFront(bool otherProgramInFront, bool fillsScreen, bool isLibraryGame, bool isDesktop) =>
        otherProgramInFront && !isDesktop && (fillsScreen || isLibraryGame);
}
