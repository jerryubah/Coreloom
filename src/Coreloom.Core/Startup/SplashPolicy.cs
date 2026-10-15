namespace Coreloom.Core.Startup;

/// <summary>
/// Rules and timings for the startup splash (platform-neutral so they can be unit-tested).
/// The splash never decides by itself when to go away: the app hands off when its main window has painted, and
/// these values only prevent a flash on very fast starts and keep the motion consistent.
/// </summary>
public static class SplashPolicy
{
    /// <summary>Shortest time the splash stays up, so an instant start doesn't flash it on and off.</summary>
    public static readonly TimeSpan MinimumVisible = TimeSpan.FromMilliseconds(850);

    /// <summary>Delay before the content fades in, then the fade/rise itself.</summary>
    public static readonly TimeSpan EntranceDelay = TimeSpan.FromMilliseconds(90);

    public static readonly TimeSpan EntranceDuration = TimeSpan.FromMilliseconds(620);

    /// <summary>How far (DIPs) the content rises while it fades in.</summary>
    public const double EntranceRise = 12;

    /// <summary>Splash fade-out (and main window fade-in) during the handoff.</summary>
    public static readonly TimeSpan HandoffDuration = TimeSpan.FromMilliseconds(360);

    /// <summary>
    /// Safety net only: if the main window was shown but never reported its first paint within this time, it is
    /// revealed anyway so a rendering problem can never leave an invisible window behind a splash. This runs
    /// after all startup work has finished, so it never cuts a slow start short.
    /// </summary>
    public static readonly TimeSpan RevealWatchdog = TimeSpan.FromSeconds(10);

    /// <summary>
    /// True when this launch should go straight to the notification area (no window, so no splash):
    /// <c>--minimized</c>, or the sign-in task with "Start minimized" turned on.
    /// </summary>
    public static bool StartsHidden(bool minimizedArgument, bool fromStartupTask, bool launchMinimizedSetting) =>
        minimizedArgument || (fromStartupTask && launchMinimizedSetting);

    /// <summary>How much longer to keep the splash up once the main window is ready.</summary>
    public static TimeSpan RemainingBeforeHandoff(TimeSpan splashVisibleFor)
    {
        if (splashVisibleFor < TimeSpan.Zero)
        {
            splashVisibleFor = TimeSpan.Zero;
        }

        return splashVisibleFor >= MinimumVisible ? TimeSpan.Zero : MinimumVisible - splashVisibleFor;
    }

    /// <summary>Motion is reduced when Coreloom's own setting asks for it or Windows animation effects are off.</summary>
    public static bool UseReducedMotion(bool appReducedMotion, bool windowsAnimationsEnabled) =>
        appReducedMotion || !windowsAnimationsEnabled;

    /// <summary>Highest the loading bar goes before the app is ready (it fills the rest during the handoff).</summary>
    public const double ProgressCeiling = 0.92;

    /// <summary>How long the loading bar takes to fill the rest of the way once the app is ready.</summary>
    public static readonly TimeSpan ProgressFinish = TimeSpan.FromMilliseconds(260);

    /// <summary>
    /// Where the loading bar is heading. Startup can't be measured, so the bar moves on with each stage the app reports
    /// (reading the hardware, opening the dashboard) and creeps on slowly while a stage takes long, but never reaches the
    /// end until the app is actually ready.
    /// </summary>
    /// <param name="stagesDone">Status updates so far (0 at the start).</param>
    /// <param name="sinceStage">Time spent in the current stage.</param>
    public static double ProgressTarget(int stagesDone, TimeSpan sinceStage)
    {
        double stage = 0.14 + (0.3 * Math.Clamp(stagesDone, 0, 3) * (1 - (0.12 * Math.Clamp(stagesDone - 1, 0, 2))));
        double creep = 0.16 * (1 - Math.Exp(-Math.Max(0, sinceStage.TotalSeconds) / 2.5));
        return Math.Min(stage + creep, ProgressCeiling);
    }

    /// <summary>Moves the shown progress toward its target (eases out; never moves backwards).</summary>
    public static double ProgressStep(double shown, double target, TimeSpan frame)
    {
        if (target <= shown)
        {
            return shown;
        }

        double next = shown + ((target - shown) * (1 - Math.Exp(-5 * Math.Max(0, frame.TotalSeconds))));
        return target - next < 0.0005 ? target : next;
    }
}
