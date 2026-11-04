using Coreloom.Core.Gaming;
using Coreloom.ViewModels.Infrastructure;

namespace Coreloom.ViewModels.Tests;

/// <summary>1.9.1: the core scheduling details' colours: green when fine, blue for a note, yellow for something to fix, red for what breaks it.</summary>
public class Release191ViewModelTests
{
    [Fact]
    public void CoreSchedulingChecks_AreColoredByWhatTheyMean_NothingGrey()
    {
        var (page, _, _, settings) = CoreSchedulingViewModelTests.Create(
            cores: c => c.Amd = new AmdVCacheStatus(ServiceState.Running, ServiceState.Stopped, 1, []),
            gaming: g => g.Plan = new PowerPlan(Guid.NewGuid(), "Power saver", false));
        var checks = page.CoreScheduling.Checks;
        Assert.Equal(ColorKeys.ToneCool, checks.Single(c => c.Name == "AMD 3D V-Cache driver").ColorKey);   // running: green
        Assert.Equal(ColorKeys.ToneWarm, checks.Single(c => c.Name == "AMD 3D V-Cache service").ColorKey);  // stopped: yellow
        Assert.Equal(ColorKeys.ToneNormal, checks.Single(c => c.Name == "AMD preference").ColorKey);        // cache first: blue, a note
        Assert.Equal(ColorKeys.ToneNormal, checks.Single(c => c.Name == "Power plan").ColorKey);            // Power saver parks but isn't Balanced: blue
        Assert.DoesNotContain(checks, c => c.ColorKey == ColorKeys.Neutral);
        settings.Dispose();
    }
}
