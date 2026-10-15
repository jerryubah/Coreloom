using Coreloom.Core.Startup;

namespace Coreloom.Core.Tests;

public class SplashPolicyTests
{
    [Theory]
    [InlineData(false, false, false, false)]  // double-click: window (and splash)
    [InlineData(false, false, true, false)]   // "Start minimized" only applies to the sign-in task
    [InlineData(false, true, false, false)]   // sign-in task, window wanted
    [InlineData(false, true, true, true)]     // sign-in task with "Start minimized"
    [InlineData(true, false, false, true)]    // --minimized always starts in the tray
    [InlineData(true, true, false, true)]
    public void StartsHidden_FollowsTheLaunchArgumentsAndSetting(bool minimized, bool fromStartup, bool launchMinimized, bool expected) =>
        Assert.Equal(expected, SplashPolicy.StartsHidden(minimized, fromStartup, launchMinimized));

    [Theory]
    [InlineData(0, 850)]
    [InlineData(300, 550)]
    [InlineData(849, 1)]
    [InlineData(850, 0)]
    [InlineData(5000, 0)]
    [InlineData(-200, 850)]
    public void RemainingBeforeHandoff_OnlyPadsVeryFastStarts(int visibleMs, int expectedMs) =>
        Assert.Equal(TimeSpan.FromMilliseconds(expectedMs), SplashPolicy.RemainingBeforeHandoff(TimeSpan.FromMilliseconds(visibleMs)));

    [Theory]
    [InlineData(false, true, false)]
    [InlineData(true, true, true)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    public void ReducedMotion_HonoursCoreloomAndWindows(bool appSetting, bool windowsAnimations, bool expected) =>
        Assert.Equal(expected, SplashPolicy.UseReducedMotion(appSetting, windowsAnimations));

    [Fact]
    public void Timings_MatchTheApprovedMotionSpec()
    {
        // Brief: soft ~0.6 s entrance with a 6–12 px rise, 0.36 s handoff fade, ~850 ms minimum on instant starts.
        Assert.InRange(SplashPolicy.EntranceDuration.TotalMilliseconds, 550, 700);
        Assert.InRange(SplashPolicy.EntranceRise, 6, 12);
        Assert.Equal(360, SplashPolicy.HandoffDuration.TotalMilliseconds);
        Assert.Equal(850, SplashPolicy.MinimumVisible.TotalMilliseconds);

        // The safety net must be far longer than any normal first paint, so it never shortens a slow start.
        Assert.True(SplashPolicy.RevealWatchdog >= TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Progress_MovesOnWithEachStage_CreepsWithinOne_AndNeverReachesTheEnd()
    {
        double start = SplashPolicy.ProgressTarget(0, TimeSpan.Zero);
        Assert.InRange(start, 0.1, 0.2);

        // Each reported stage moves the bar on; within a stage it creeps forward but stays below the next stage.
        for (int stage = 0; stage < 3; stage++)
        {
            double now = SplashPolicy.ProgressTarget(stage, TimeSpan.Zero);
            double later = SplashPolicy.ProgressTarget(stage, TimeSpan.FromSeconds(30));
            double next = SplashPolicy.ProgressTarget(stage + 1, TimeSpan.Zero);
            Assert.True(later > now);
            Assert.True(next > now);
        }

        // However long startup takes, the bar leaves room for the handoff to fill.
        Assert.Equal(SplashPolicy.ProgressCeiling, SplashPolicy.ProgressTarget(10, TimeSpan.FromMinutes(5)));
        Assert.True(SplashPolicy.ProgressCeiling < 1);
    }

    [Fact]
    public void ProgressStep_EasesTowardTheTarget_AndNeverGoesBack()
    {
        double shown = 0.2;
        double next = SplashPolicy.ProgressStep(shown, 0.6, TimeSpan.FromMilliseconds(16));
        Assert.InRange(next, 0.2001, 0.6);
        Assert.Equal(0.5, SplashPolicy.ProgressStep(0.5, 0.3, TimeSpan.FromMilliseconds(16)));

        for (int i = 0; i < 400; i++)
        {
            shown = SplashPolicy.ProgressStep(shown, 0.6, TimeSpan.FromMilliseconds(16));
        }

        Assert.Equal(0.6, shown);
    }
}
