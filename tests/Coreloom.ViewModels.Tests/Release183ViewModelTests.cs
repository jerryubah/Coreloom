using Coreloom.Core.Formatting;
using Coreloom.ViewModels.Infrastructure;
using Coreloom.ViewModels.Pages;
using Coreloom.ViewModels.Services;

namespace Coreloom.ViewModels.Tests;

/// <summary>1.8.3: the Smart Optimize banner's dial.</summary>
public sealed class Release183ViewModelTests
{
    [Fact]
    public async Task OptimizeDial_IsFullBeforeTheFirstScan_ThenFollowsTheScanAndTakesTheResultsColor()
    {
        var (monitor, settings, _) = Fixture.Create();
        using var fake = new FakeOptimizer(false);
        var page = new OptimizeViewModel(new PageContext
        {
            Monitor = monitor,
            Settings = settings,
            Timeline = new TimelineViewModel(monitor.Engine.History),
            Tools = new ToolServices { Optimizer = fake },
        });
        page.Activate(monitor.Engine.LatestFrame);
        var changes = new List<string>();
        page.PropertyChanged += (_, e) => changes.Add(e.PropertyName ?? string.Empty);

        // Before any scan: a full dial in the accent (NaN = the accent).
        Assert.Equal(OptimizeStage.Idle, page.Stage);
        Assert.Equal(1, page.DialValue);
        Assert.True(double.IsNaN(page.DialTone));

        await page.ScanCommand.ExecuteAsync(null);

        // Found something: the scan's progress (all of it) in the result's color, and both were announced.
        Assert.Equal(OptimizeStage.Ready, page.Stage);
        Assert.Equal(page.RingProgress, page.DialValue, 6);
        Assert.Equal(page.RingTone, page.DialTone, 6);
        Assert.InRange(page.DialTone, GaugeTone.Green, GaugeTone.Red);
        Assert.Contains(nameof(OptimizeViewModel.DialValue), changes);
        Assert.Contains(nameof(OptimizeViewModel.DialTone), changes);

        await page.RunCommand.ExecuteAsync(null);
        Assert.Equal(OptimizeStage.Done, page.Stage);
        Assert.Equal(1, page.DialValue, 6);
        Assert.Equal(page.RingTone, page.DialTone, 6);
        settings.Dispose();
    }
}
