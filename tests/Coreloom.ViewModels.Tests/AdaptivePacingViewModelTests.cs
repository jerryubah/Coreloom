using Coreloom.Core.Gaming;
using Coreloom.ViewModels.Infrastructure;
using Coreloom.ViewModels.Pages;
using Coreloom.ViewModels.Services;

namespace Coreloom.ViewModels.Tests;

public class AdaptivePacingViewModelTests
{
    private const string Cyberpunk = @"D:\Steam\steamapps\common\Cyberpunk 2077\bin\x64\Cyberpunk2077.exe";

    private static (GamingViewModel Page, FakeGaming Fake, SettingsService Settings) Create(Action<FakeGaming>? setup = null)
    {
        var (monitor, settings, _) = Fixture.Create();
        var fake = new FakeGaming();
        fake.Tweaks[GamingTweak.GSync] = new TweakStatus(true, true); // G-SYNC on: limits just under 144 Hz, in steps of 5
        setup?.Invoke(fake);
        var context = new PageContext
        {
            Monitor = monitor,
            Settings = settings,
            Timeline = new TimelineViewModel(monitor.Engine.History),
            Tools = new ToolServices { Gaming = fake },
        };
        var page = new GamingViewModel(context);
        page.Activate(monitor.Engine.LatestFrame);
        return (page, fake, settings);
    }

    private static PacingSecond Second(double fps, double low) => new(fps, 1000 / fps, 1000 / low, (int)fps, 0, 1000 / low, 80);

    private static async Task PlayAsync(AdaptivePacingController adaptive, int seconds, Func<int, PacingSecond> second, string program = Cyberpunk, int pid = 4242)
    {
        for (int i = 0; i < seconds; i++)
        {
            await adaptive.TickAsync(new PacingTick(pid, program, second(i)));
        }
    }

    [Fact]
    public async Task Off_ByDefault_AndDoesNothing()
    {
        var (page, fake, settings) = Create();

        await PlayAsync(page.Adaptive, 60, i => Second(95 + (i % 31), 80));

        Assert.False(page.FramePacer.AdaptiveOn);
        Assert.Empty(fake.ProgramCaps);
        Assert.StartsWith("Off.", page.Adaptive.StatusText);
        Assert.Equal("Frame stability · 1% lows · Smoothness", page.FramePacer.AdaptiveTags);
        Assert.Equal("An intelligent engine that monitors actual frame times and automatically finds a stable FPS target for each game.", page.FramePacer.AdaptiveDescription);
        settings.Dispose();
    }

    [Fact]
    public async Task On_MeasuresTheGameInFront_ThenSetsItsOwnLimit()
    {
        var (page, fake, settings) = Create();
        await page.Adaptive.SetEnabledAsync(true);
        Assert.True(page.FramePacer.AdaptiveOn);

        await PlayAsync(page.Adaptive, AdaptivePacingSession.MinimumSeconds - 1, i => Second(95 + (i % 31), 80));
        Assert.Equal("Cyberpunk 2077", page.Adaptive.GameName);
        Assert.Contains("Measuring", page.Adaptive.StatusText);
        Assert.Empty(fake.ProgramCaps);

        await PlayAsync(page.Adaptive, 1, i => Second(100, 80));

        Assert.Equal(90, fake.ProgramCaps["Cyberpunk2077.exe"]);
        var cap = settings.Current.Gaming.CapFor("steam:1091500")!;
        Assert.True(cap.Adaptive);
        Assert.Equal(90, cap.Fps);
        Assert.Equal(90, settings.Current.Gaming.PacingFor("steam:1091500")!.Target);
        Assert.Contains("Set Cyberpunk 2077's limit to 90 FPS", page.Adaptive.LastChangeText);
        Assert.Contains(page.FramePacer.GameCaps, c => c.LimitText == "90 FPS · adaptive");
        settings.Dispose();
    }

    [Fact]
    public async Task AChangeThatDoesntShow_IsLeftForTheNextStart()
    {
        var (page, fake, settings) = Create();
        await page.Adaptive.SetEnabledAsync(true);
        await PlayAsync(page.Adaptive, AdaptivePacingSession.MinimumSeconds, i => Second(95 + (i % 31), 80));
        Assert.Equal(90, fake.ProgramCaps["Cyberpunk2077.exe"]);

        await PlayAsync(page.Adaptive, AdaptivePacingSession.VerifySeconds + 30, i => Second(95 + (i % 31), 80)); // still uncapped

        Assert.Contains("next start", page.Adaptive.StatusText);
        Assert.Equal(90, fake.ProgramCaps["Cyberpunk2077.exe"]); // no further changes
        settings.Dispose();
    }

    [Fact]
    public async Task AGameWithALimitYouSet_IsLeftAlone()
    {
        var (page, fake, settings) = Create();
        await page.SetGameFramePacerAsync("steam:1091500", 60, 60);
        await page.Adaptive.SetEnabledAsync(true);

        await PlayAsync(page.Adaptive, 60, _ => Second(60, 45));

        Assert.Equal(60, fake.ProgramCaps["Cyberpunk2077.exe"]);
        Assert.Contains("has a limit you set (60 FPS)", page.Adaptive.StatusText);
        settings.Dispose();
    }

    [Fact]
    public async Task AProgramOutsideTheLibrary_IsExplained()
    {
        var (page, _, settings) = Create();
        await page.Adaptive.SetEnabledAsync(true);

        await PlayAsync(page.Adaptive, 3, _ => Second(60, 50), program: @"C:\Tools\emulator.exe", pid: 77);

        Assert.Contains("emulator.exe isn't in your library", page.Adaptive.StatusText);
        settings.Dispose();
    }

    [Fact]
    public async Task TurningItOff_RemovesTheLimitsItSet_ButKeepsWhatItLearned()
    {
        var (page, fake, settings) = Create();
        await page.Adaptive.SetEnabledAsync(true);
        await PlayAsync(page.Adaptive, AdaptivePacingSession.MinimumSeconds, i => Second(95 + (i % 31), 80));
        await page.Adaptive.TickAsync(new PacingTick(null, null, null)); // the game closed

        await page.Adaptive.SetEnabledAsync(false);

        Assert.Empty(fake.ProgramCaps);
        Assert.Null(settings.Current.Gaming.CapFor("steam:1091500"));
        var learned = settings.Current.Gaming.PacingFor("steam:1091500")!;
        Assert.Equal(90, learned.Target);
        Assert.Equal(1, learned.Sessions);
        Assert.Contains("Removed the limit adaptive pacing set", page.Adaptive.LastChangeText);
        settings.Dispose();
    }

    [Fact]
    public async Task WithoutAnNvidiaDriver_ItSuggestsALimit()
    {
        var (page, fake, settings) = Create(f => f.HasNvidia = false);
        await page.Adaptive.SetEnabledAsync(true);

        await PlayAsync(page.Adaptive, AdaptivePacingSession.MinimumSeconds + 5, i => Second(95 + (i % 31), 80));

        Assert.Empty(fake.ProgramCaps);
        Assert.Contains("suggested 90 FPS", page.Adaptive.StatusText);
        Assert.Contains("AMD Software", page.Adaptive.StatusText);
        Assert.Contains("suggests", page.FramePacer.AdaptiveMethodText);
        settings.Dispose();
    }

    [Fact]
    public async Task FrameGeneration_IsSetPerGame_AndShapesTheLimit()
    {
        var (page, fake, settings) = Create();
        await page.Adaptive.SetEnabledAsync(true);
        page.FramePacer.SelectedScope = "steam:1091500";
        Assert.True(page.FramePacer.ShowFrameGeneration);
        Assert.Equal("Frame generation in Cyberpunk 2077", page.FramePacer.FrameGenerationLabel);

        page.FramePacer.SelectedFrameGeneration = 2;
        await PlayAsync(page.Adaptive, AdaptivePacingSession.MinimumSeconds, i => Second(150 + (i % 31), 105));

        Assert.Equal(2, settings.Current.Gaming.PacingFor("steam:1091500")!.FrameGeneration);
        Assert.Equal(2, page.Adaptive.Session!.Limits.Factor);
        Assert.Equal(120, fake.ProgramCaps["Cyberpunk2077.exe"]); // about 124 sustained → steps of 10 with 2× frame generation
        settings.Dispose();
    }
}
