using Coreloom.ViewModels.Pages;

namespace Coreloom.ViewModels.Tests;

/// <summary>1.9.0: the sidebar's count beside a page (Drivers' updates), and the pages where they were.</summary>
public class Release190ViewModelTests
{
    [Fact]
    public async Task Drivers_ShowsItsUpdatesAsACountInTheSidebar_UntilTheyAreInstalled()
    {
        var (shell, _, _, stubs) = ShellViewModelTests.Create();
        Assert.Null(shell.Overview.Badge);
        Assert.Null(shell.Settings.Badge); // Settings keeps its red dot for an update of Coreloom itself
        Assert.Equal(["Overview", "Gaming", "Benchmarking", "Graphics", "Processor", "Memory", "Storage", "Devices", "Drivers", "Network", "Applications", "Smart Optimize", "Performance History"], shell.Pages.Select(p => p.Title));

        var (context, _, fake, _, _) = Release186ViewModelTests.DriversContext();
        var page = new DriversViewModel(context) { Now = () => new DateTime(2026, 11, 4, 19, 40, 0) };
        Assert.Null(page.Badge);
        page.Activate(context.Monitor.Engine.LatestFrame);
        for (int i = 0; i < 500 && (fake.Checks < 1 || page.IsChecking); i++)
        {
            await Task.Delay(10);
        }

        Assert.Equal("3", page.Badge);
        _ = stubs;
    }
}
