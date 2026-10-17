using Coreloom.Core.Gaming;
using Coreloom.Core.Settings;

namespace Coreloom.Core.Tests;

public class PacingSecondTests
{
    [Fact]
    public void SumsUpTheNewestSecondOfFrames()
    {
        // Two seconds of 10 ms frames, then one 40 ms hitch: only the newest ~1000 ms count.
        var frames = Enumerable.Repeat(10.0, 200).Append(40).ToArray();

        var second = PacingSecond.From(frames, gpuLoad: 87)!.Value;

        Assert.Equal(97, second.Frames); // 40 ms + 96 × 10 ms covers the second
        Assert.Equal(97 * 1000 / 1000.0, second.Fps, 6);
        Assert.Equal(10, second.MedianMs);
        Assert.Equal(40, second.LongestMs);
        Assert.Equal(1, second.Spikes);
        Assert.Equal(87, second.GpuLoad);
        Assert.Equal(25, second.LowFps, 6); // the 99th percentile frame is the 40 ms one
    }

    [Fact]
    public void TooFewFrames_GiveNothing()
    {
        Assert.Null(PacingSecond.From([16.7]));
        Assert.Null(PacingSecond.From([]));
    }
}

public class PacingLimitsTests
{
    [Fact]
    public void VariableRefresh_StaysJustUnderTheRefreshRate_InStepsOfFive()
    {
        var limits = new PacingLimits(144, VariableRefresh: true);

        Assert.Equal(138, limits.Ceiling);
        Assert.Equal(30, limits.Floor);
        Assert.Equal(95, limits.SnapDown(97.4));
        Assert.Equal(138, limits.SnapDown(180));
        Assert.Equal(30, limits.SnapDown(12));
    }

    [Fact]
    public void FixedRefresh_UsesWholeFractionsOfTheRefreshRate()
    {
        var limits = new PacingLimits(144, VariableRefresh: false);

        Assert.Equal(144, limits.Ceiling);
        Assert.Equal(72, limits.SnapDown(100));
        Assert.Equal(48, limits.SnapDown(60));
        Assert.Equal(36, limits.SnapDown(20)); // the lowest fraction still above 30 FPS
        Assert.Equal(60, new PacingLimits(60, false).SnapDown(61));
        Assert.Equal(30, new PacingLimits(60, false).SnapDown(59.9));
    }

    [Fact]
    public void FrameGeneration_KeepsWholeRenderedFrames_AndARenderedFloorOfForty()
    {
        var two = new PacingLimits(165, VariableRefresh: true, FrameGeneration: 2);
        var four = new PacingLimits(240, VariableRefresh: true, FrameGeneration: 4);

        Assert.Equal(156, two.Ceiling); // 157 → an even number
        Assert.Equal(80, two.Floor);
        Assert.Equal(110, two.SnapDown(117));
        Assert.Equal(224, four.Ceiling);
        Assert.Equal(160, four.Floor);
        Assert.Equal(180, four.SnapDown(199));
    }
}

public class AdaptivePacingSessionTests
{
    private static readonly PacingLimits Vrr144 = new(144, VariableRefresh: true);

    private static PacingSecond Second(double fps, double lowFps, double gpu = double.NaN, double longest = 0) =>
        new(fps, 1000 / fps, 1000 / lowFps, (int)fps, 0, longest > 0 ? longest : 1000 / lowFps, gpu);

    private static void Play(AdaptivePacingSession session, int seconds, Func<int, PacingSecond> second)
    {
        for (int i = 0; i < seconds; i++)
        {
            session.Add(second(i));
        }
    }

    [Fact]
    public void NothingIsSuggested_UntilEnoughPlayIsMeasured()
    {
        var session = new AdaptivePacingSession(0, Vrr144);
        Play(session, AdaptivePacingSession.MinimumSeconds - 1, _ => Second(110, 90));

        var advice = session.Advise();

        Assert.Equal(PacingAction.None, advice.Action);
        Assert.Contains("44 of 45 seconds", advice.Reason);
    }

    [Fact]
    public void LoadingScreensAndPauses_DontCount()
    {
        var session = new AdaptivePacingSession(0, Vrr144);

        Assert.False(session.Add(Second(5, 4)));
        Assert.False(session.Add(Second(60, 50, longest: 800)));
        Assert.True(session.Add(Second(100, 85)));
        Assert.Equal(1, session.Seconds);
        Assert.Equal(2, session.Skipped);
    }

    [Fact]
    public void AnUncappedGame_GetsTheFrameRateItSustainsWithSteadyLows()
    {
        // 95–125 FPS with 1% lows around 80: lows limit it to about 94 → 90 FPS.
        var session = new AdaptivePacingSession(0, Vrr144);
        Play(session, 60, i => Second(95 + (i % 31), 80));

        var advice = session.Advise();

        Assert.Equal(PacingAction.Set, advice.Action);
        Assert.Equal(90, advice.Target);
        Assert.Equal(94, advice.SustainableFps);
    }

    [Fact]
    public void AFastGame_IsLimitedJustUnderTheRefreshRate()
    {
        var session = new AdaptivePacingSession(0, Vrr144);
        Play(session, 60, _ => Second(220, 190));

        Assert.Equal(138, session.Advise().Target);
    }

    [Fact]
    public void MissingItsLimit_LowersItAStepAtATime()
    {
        // Limited to 138 but mostly running 70–90: the fall is held to 15 % (138 → 115), not straight to 70.
        var session = new AdaptivePacingSession(138, Vrr144);
        Play(session, 60, i => Second(70 + (i % 21), 62));

        var advice = session.Advise();

        Assert.Equal(PacingAction.Lower, advice.Action);
        Assert.Equal(115, advice.Target);
        Assert.Contains("Missed the 138 FPS limit", advice.Reason);
    }

    [Fact]
    public void HoldingTheLimit_WithAGraphicsCardToSpare_RaisesIt_ButNotToALimitItFailedBefore()
    {
        var session = new AdaptivePacingSession(90, Vrr144);
        Play(session, 60, _ => Second(90, 86, gpu: 55));
        Assert.Equal((PacingAction.Raise, 95), (session.Advise().Action, session.Advise().Target));

        var remembers = new AdaptivePacingSession(90, Vrr144, failedAt: 95);
        Play(remembers, 60, _ => Second(90, 86, gpu: 55));
        Assert.Equal(PacingAction.None, remembers.Advise().Action); // it couldn't hold 95 before
    }

    [Fact]
    public void HoldingTheLimit_WithABusyGraphicsCard_KeepsIt()
    {
        var session = new AdaptivePacingSession(90, Vrr144);
        Play(session, 60, _ => Second(90, 85, gpu: 97));

        var advice = session.Advise();

        Assert.Equal(PacingAction.None, advice.Action);
        Assert.Contains("Steady at 90 FPS", advice.Reason);
    }

    [Fact]
    public void SmallDipsUnderTheLimit_DontChangeIt()
    {
        var session = new AdaptivePacingSession(100, Vrr144);
        Play(session, 60, i => Second(i % 2 == 0 ? 97 : 100, 88));

        Assert.Equal(PacingAction.None, session.Advise().Action);
    }

    [Fact]
    public void ALimitAboveTheDisplay_ComesDownToJustUnderTheRefreshRate()
    {
        // An all-games limit of 240 on a 144 Hz G-SYNC display: the game sits at 144 behind V-Sync.
        var session = new AdaptivePacingSession(240, Vrr144);
        Play(session, 60, _ => Second(144, 138));

        var advice = session.Advise();

        Assert.Equal(PacingAction.Lower, advice.Action);
        Assert.Equal(138, advice.Target);
    }

    [Fact]
    public void AChangeThatShowsInTheFrames_IsTakenUp_AndMeasuringStartsAgain()
    {
        var session = new AdaptivePacingSession(138, Vrr144);
        Play(session, 60, i => Second(70 + (i % 21), 62));
        session.Request(115);
        Assert.Equal(138, session.FailedAt);

        Play(session, AdaptivePacingSession.VerifySeconds, _ => Second(85, 75));

        Assert.Equal(115, session.Cap);
        Assert.Equal(0, session.Requested);
        Assert.Equal(0, session.Seconds);
        Assert.False(session.WaitingForRestart);
    }

    [Fact]
    public void AChangeThatDoesntShow_WaitsForTheNextStart()
    {
        var session = new AdaptivePacingSession(0, Vrr144);
        Play(session, 60, i => Second(95 + (i % 31), 80));
        session.Request(90);

        Play(session, AdaptivePacingSession.VerifySeconds, i => Second(95 + (i % 31), 80)); // still over 90: the driver kept the old setting

        Assert.True(session.WaitingForRestart);
        Assert.Equal(PacingAction.None, session.Advise().Action);
        Assert.Contains("next start", session.Advise().Reason);
    }

    [Fact]
    public void ARaise_ThatTheFramesDontShow_AlsoWaits()
    {
        var session = new AdaptivePacingSession(90, Vrr144);
        Play(session, 60, _ => Second(90, 86, gpu: 55));
        session.Request(95);

        Play(session, AdaptivePacingSession.VerifySeconds, _ => Second(90, 86, gpu: 55));

        Assert.True(session.WaitingForRestart);
    }
}

public class AdaptivePacingSettingsTests
{
    [Fact]
    public void Normalize_KeepsOneEntryPerGame_WithinRange()
    {
        var settings = new GamingSettings
        {
            AdaptivePacingGames =
            [
                new AdaptivePacingGame { GameId = "steam:1", Name = "A", FrameGeneration = 9, Target = 5000 },
                new AdaptivePacingGame { GameId = "STEAM:1", Name = "A again" },
                new AdaptivePacingGame { GameId = " " },
            ],
        };

        settings.Normalize();

        var game = Assert.Single(settings.AdaptivePacingGames);
        Assert.Equal(4, game.FrameGeneration);
        Assert.Equal(FrameCap.Maximum, game.Target);
        Assert.Same(game, settings.PacingFor("steam:1"));
        Assert.False(settings.AdaptivePacing);
    }
}
