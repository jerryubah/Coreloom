using Coreloom.Core.Settings;
using Coreloom.Core.Telemetry;
using Coreloom.ViewModels.Infrastructure;
using Coreloom.ViewModels.MenuBar;
using Coreloom.ViewModels.Pages;
using Coreloom.ViewModels.Services;

namespace Coreloom.ViewModels.Tests;

/// <summary>1.8.1: renaming Bluetooth devices, the Mac's menu bar modules, the desktop picture choice and "What's new".</summary>
public class Release181ViewModelTests
{
    private static (DevicesViewModel Page, SettingsService Settings) Devices(Action<AppSettings>? configure = null)
    {
        var (monitor, settings, _) = Fixture.Create();
        if (configure is not null)
        {
            settings.Update("Devices", configure);
        }

        var context = new PageContext
        {
            Monitor = monitor,
            Settings = settings,
            Timeline = new TimelineViewModel(monitor.Engine.History),
            Tools = new ToolServices { Devices = new FakeDevices() },
        };
        var page = new DevicesViewModel(context) { Now = () => new DateTime(2026, 10, 14, 9, 0, 0) };
        page.Activate(monitor.Engine.LatestFrame);
        return (page, settings);
    }

    [Fact]
    public void Renaming_KeepsTheNameInCoreloom_AndResortsTheCards()
    {
        var (page, settings) = Devices();
        var pad = page.BluetoothDevices.Single(d => d.Id == "pad");
        Assert.False(pad.HasCustomName);
        Assert.Equal("Xbox Wireless Controller", pad.NameToolTip);

        pad.RenameCommand.Execute(null);
        Assert.True(pad.IsRenaming);
        Assert.Equal("Xbox Wireless Controller", pad.EditName);

        pad.EditName = "  Couch controller ";
        pad.SaveNameCommand.Execute(null);

        Assert.False(pad.IsRenaming);
        Assert.Equal("Couch controller", pad.Name);
        Assert.Equal("Xbox Wireless Controller", pad.ReportedName);
        Assert.True(pad.HasCustomName);
        Assert.Equal("Named in Coreloom. The device calls itself “Xbox Wireless Controller”.", pad.NameToolTip);
        Assert.Equal("Couch controller", settings.Current.Devices.BluetoothNames["pad"]);

        // Connected first, then by the name shown: "Couch controller" now comes before "MX Master 3S".
        Assert.Equal<string>(new[] { "Couch controller", "MX Master 3S", "WH-1000XM5" }, page.BluetoothDevices.Select(d => d.Name).ToArray());

        // Searching finds it by either name.
        page.Search = "xbox";
        Assert.True(pad.IsMatch);
        page.Search = "couch";
        Assert.True(pad.IsMatch);
        page.Search = string.Empty;

        // Saving again on leaving the box (a click elsewhere after Enter) changes nothing.
        pad.SaveNameCommand.Execute(null);
        Assert.Equal("Couch controller", pad.Name);
        settings.Dispose();
    }

    [Fact]
    public void AnEmptyName_OrResetName_GoesBackToTheDevicesOwn_AndEscCancels()
    {
        var (page, settings) = Devices(s => s.Devices.BluetoothNames["mouse"] = "Work mouse");
        var mouse = page.BluetoothDevices.Single(d => d.Id == "mouse");
        Assert.Equal("Work mouse", mouse.Name);
        Assert.True(mouse.ResetNameCommand.CanExecute(null));
        Assert.Equal("Work mouse: 15% battery left", page.LowBatteryText);

        mouse.RenameCommand.Execute(null);
        mouse.EditName = "Something else";
        mouse.CancelRenameCommand.Execute(null);
        Assert.False(mouse.IsRenaming);
        Assert.Equal("Work mouse", mouse.Name);

        mouse.RenameCommand.Execute(null);
        mouse.EditName = "   ";
        mouse.SaveNameCommand.Execute(null);
        Assert.Equal("MX Master 3S", mouse.Name);
        Assert.False(mouse.HasCustomName);
        Assert.False(settings.Current.Devices.BluetoothNames.ContainsKey("mouse"));
        Assert.False(mouse.ResetNameCommand.CanExecute(null));

        // Typing the device's own name keeps no name either.
        mouse.RenameCommand.Execute(null);
        mouse.EditName = "MX Master 3S";
        mouse.SaveNameCommand.Execute(null);
        Assert.False(settings.Current.Devices.BluetoothNames.ContainsKey("mouse"));

        mouse.RenameCommand.Execute(null);
        mouse.EditName = "Work mouse";
        mouse.SaveNameCommand.Execute(null);
        mouse.ResetNameCommand.Execute(null);
        Assert.Equal("MX Master 3S", mouse.Name);
        Assert.Empty(settings.Current.Devices.BluetoothNames);
        settings.Dispose();
    }

    [Fact]
    public void MenuBar_ShowsTheModulesTurnedOn_InSettingsOrder()
    {
        var (monitor, settings, provider) = Fixture.Create(extra: p => p
            .Define("fan/0", MetricCategory.Motherboard, MetricKind.Fan, 1460)
            .Define("fan/1", MetricCategory.Motherboard, MetricKind.Fan, 2210)
            .Define(MetricIds.CpuTemperature, MetricCategory.Cpu, MetricKind.Temperature, 65.4));
        var menuBar = new MenuBarModulesViewModel(Fixture.Context(monitor, settings));
        int changes = 0;
        menuBar.ReadingsChanged += (_, _) => changes++;

        menuBar.Update(monitor.Engine.LatestFrame);
        Assert.False(menuBar.HasReadings); // nothing is in the menu bar until it's turned on
        Assert.Equal("12%", menuBar.Modules.Single(m => m.Id == MenuBarModules.Cpu).Current);
        Assert.False(menuBar.Modules.Single(m => m.Id == MenuBarModules.Battery).IsAvailable);
        Assert.Equal("Not on this Mac", menuBar.Modules.Single(m => m.Id == MenuBarModules.Battery).AvailabilityText);

        menuBar.Modules.Single(m => m.Id == MenuBarModules.Fans).Shown = true;
        menuBar.Modules.Single(m => m.Id == MenuBarModules.Cpu).Shown = true;
        var memory = menuBar.Modules.Single(m => m.Id == MenuBarModules.Memory);
        memory.Shown = true;
        memory.ShowGauge = false;
        var temperature = menuBar.Modules.Single(m => m.Id == MenuBarModules.CpuTemperature);
        temperature.Shown = true;
        temperature.ShowName = false;
        menuBar.Modules.Single(m => m.Id == MenuBarModules.Battery).Shown = true; // not on this Mac: left out

        Assert.True(settings.Current.MenuBar.For(MenuBarModules.Fans).Shown);
        Assert.Equal(new[] { "cpu", "memory", "cpu-temp", "fans" }, menuBar.Readings.Select(r => r.Id));
        var cpu = menuBar.Readings[0];
        Assert.Equal("CPU", cpu.Label);
        Assert.Equal("12%", cpu.Value);
        Assert.Equal(0.124, cpu.Fraction!.Value, 6);
        Assert.True(cpu.ShowName);
        Assert.Null(menuBar.Readings[1].Fraction); // its bar is off
        Assert.Equal("65°C", menuBar.Readings[2].Value);
        Assert.False(menuBar.Readings[2].ShowName);
        Assert.Equal("2,210 RPM", menuBar.Readings[3].Value); // the fastest fan
        Assert.Null(menuBar.Readings[3].Fraction);

        // A new reading is published; the same reading again isn't.
        int before = changes;
        menuBar.Update(monitor.Engine.LatestFrame);
        Assert.Equal(before, changes);
        provider.Values[MetricIds.CpuLoad] = 55;
        menuBar.Update(monitor.Engine.PollOnce());
        Assert.Equal(before + 1, changes);
        Assert.Equal("55%", menuBar.Readings[0].Value);
        settings.Dispose();
    }

    [Fact]
    public void ShowingTheDesktopPicture_FromCustomize_CountsAsChosen()
    {
        var (monitor, settings, _) = Fixture.Create();
        var overview = new OverviewViewModel(Fixture.Context(monitor, settings));
        Assert.True(overview.ShowDesktopPicture); // on by default
        Assert.False(overview.DesktopPictureChosen); // ...but not chosen: guarded folders aren't read

        overview.Customize.AllowDesktopPictureCommand.Execute(null);
        Assert.True(settings.Current.Dashboard.DesktopPictureChosen);

        overview.Customize.ShowDesktopPicture = false;
        Assert.False(settings.Current.Dashboard.ShowDesktopPicture);
        Assert.False(settings.Current.Dashboard.DesktopPictureChosen);
        overview.Customize.ShowDesktopPicture = true;
        Assert.True(settings.Current.Dashboard.DesktopPictureChosen);
        settings.Dispose();
    }

    [Fact]
    public void WhatsNew_ShowsPlatformsAsChips_AndTheReleaseAtAGlance()
    {
        const string notes = """
            Readings in the menu bar and more.

            ### Highlights

            - **Mac: readings in the menu bar.** Pick what shows.
            - **Windows: a calmer sidebar.** Less to look at.
            - **Rename your Bluetooth devices.** Give them names you know.
            """;
        var summary = ReleaseSummaryViewModel.From("1.8.1", notes, new DateTimeOffset(2026, 10, 28, 18, 0, 0, TimeSpan.Zero));

        Assert.Equal(3, summary.ChangeCount);
        Assert.Equal("3 changes", summary.ChangesText);
        Assert.Equal("Mac", summary.Items[0].Category);
        Assert.Equal("Readings in the menu bar", summary.Items[0].Title);
        Assert.Equal("Windows", summary.Items[1].Category);
        Assert.Null(summary.Items[2].Category);
        Assert.Equal(new[] { "Mac", "Windows" }, summary.Categories);
        Assert.True(summary.HasCategories);
        Assert.StartsWith("Released October 28, 2026", summary.MetaText);
        Assert.EndsWith(" · 3 changes", summary.MetaText);

        var single = ReleaseSummaryViewModel.From("1.8.2", "- **Mac: one fix.** It works.", null);
        Assert.False(single.HasCategories); // one area only: no chips
        Assert.Equal("1 change", single.MetaText);
        Assert.Null(ReleaseSummaryViewModel.From("1.8.3", null, null).MetaText);
    }

    [Fact]
    public void OnlySettings_HasNoticeOnItsSidebarItem()
    {
        var (monitor, settings, _) = Fixture.Create();
        Assert.False(new OverviewViewModel(Fixture.Context(monitor, settings)).HasNotice);
        settings.Dispose();
    }
}
