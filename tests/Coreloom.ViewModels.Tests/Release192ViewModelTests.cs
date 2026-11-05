using Coreloom.Core.Formatting;
using Coreloom.Core.Settings;
using Coreloom.Core.Telemetry;
using Coreloom.ViewModels.Pages;

namespace Coreloom.ViewModels.Tests;

/// <summary>1.9.2: Settings › Appearance › Sidebar, and the memory and drive rings as tick dials.</summary>
public class Release192ViewModelTests
{
    [Fact]
    public void Sidebar_OffersFourSurfaces_FrostedChosen_AndShadowAndBlurAtStandard()
    {
        var (_, settings, _) = Fixture.Create();
        var sidebar = new SidebarLookViewModel(settings);
        Assert.Equal(new[] { "Solid color", "Liquid glass", "Frosted glass", "Translucent blend" }, sidebar.Surfaces.Select(s => s.Label));
        Assert.Equal(SidebarSurface.FrostedGlass, sidebar.Surface);
        Assert.Equal(50, sidebar.Shadow);
        Assert.Equal(50, sidebar.Blur);
        Assert.Equal("Standard", sidebar.ShadowText);
        Assert.Equal("Standard", sidebar.BlurText);
        Assert.True(sidebar.CanBlur);
        Assert.True(sidebar.IsStandard);
        settings.Dispose();
    }

    [Fact]
    public void Sidebar_Changes_ApplyAtOnce_AndAreSaved()
    {
        var (_, settings, _) = Fixture.Create();
        var changes = new List<string>();
        settings.Changed += changes.Add;
        var sidebar = new SidebarLookViewModel(settings);
        var raised = new List<string?>();
        sidebar.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        sidebar.Surface = SidebarSurface.LiquidGlass;
        Assert.Equal(SidebarSurface.LiquidGlass, settings.Current.Appearance.Sidebar.Surface);
        Assert.Contains("Appearance", changes);   // the theme is applied again from this
        Assert.Contains(string.Empty, raised);    // every property again (the text under the choice, whether blur applies)
        Assert.Contains("highlight", sidebar.SurfaceText);

        sidebar.Shadow = 80.4;
        sidebar.Blur = 12;
        Assert.Equal(80, settings.Current.Appearance.Sidebar.Shadow);
        Assert.Equal(12, settings.Current.Appearance.Sidebar.Blur);
        Assert.Equal("80 %", sidebar.ShadowText);
        Assert.Equal("12 %", sidebar.BlurText);
        Assert.False(sidebar.IsStandard);

        sidebar.Shadow = 0;
        Assert.Equal("None", sidebar.ShadowText);
        sidebar.Shadow = 250;                     // clamped
        Assert.Equal(100, settings.Current.Appearance.Sidebar.Shadow);

        sidebar.ResetCommand.Execute(null);
        Assert.True(sidebar.IsStandard);
        Assert.Equal(SidebarSurface.LiquidGlass, sidebar.Surface); // the surface stays
        settings.Dispose();
    }

    [Fact]
    public void Sidebar_SolidAndTranslucent_HaveNothingToBlur()
    {
        var (_, settings, _) = Fixture.Create();
        var sidebar = new SidebarLookViewModel(settings) { Surface = SidebarSurface.Solid };
        Assert.False(sidebar.CanBlur);
        Assert.Equal("No blur", sidebar.BlurText);
        sidebar.Surface = SidebarSurface.Translucent;
        Assert.False(sidebar.CanBlur);
        Assert.Contains("sharp", sidebar.SurfaceText);
        sidebar.Surface = SidebarSurface.FrostedGlass;
        Assert.True(sidebar.CanBlur);
        settings.Dispose();
    }

    [Fact]
    public void Settings_HasTheSidebarLook()
    {
        var (_, settings, _) = Fixture.Create();
        Assert.Contains(typeof(SettingsViewModel).GetProperties(), p => p.Name == "Sidebar" && p.PropertyType == typeof(SidebarLookViewModel));
        settings.Dispose();
    }

    [Fact]
    public void MemoryPanel_FillsItsDial_WithTheMemoryThresholds()
    {
        var memory = new MemoryPanelViewModel();
        Assert.False(memory.HasFraction);         // before the first reading the dial shows only its track
        memory.Fraction = 0.48;
        Assert.True(memory.HasFraction);
        var (cool, warm, hot) = GaugeTone.Stops(Thresholds.Memory, 100);
        Assert.Equal((cool, warm, hot), (memory.GaugeCoolAt, memory.GaugeWarmAt, memory.GaugeHotAt));
        Assert.Equal(GaugeTone.Blue, Math.Round(GaugeTone.At(0.48, cool, warm, hot)));   // blue, like its "In use" dot
    }

    [Fact]
    public void Volume_FillsItsDial_GreenWhileRoomy_RedWhenNearlyFull()
    {
        var volume = new VolumeViewModel("C", "System");
        MetricHandle handle = null!; // the volume reads only the values
        var frame = new TelemetryFrame(1, 0, DateTime.Now, TimeSpan.Zero, new Dictionary<string, MetricSample>
        {
            [MetricIds.VolumeTotal("C")] = new(1000, handle),
            [MetricIds.VolumeFree("C")] = new(30, handle),
        });
        volume.Update(frame, UnitFormatter.Default);
        Assert.Equal(0.97, volume.Fraction, 6);
        var (cool, warm, hot) = GaugeTone.Stops(Thresholds.DriveSpace, 100);
        Assert.Equal((cool, warm, hot), (volume.GaugeCoolAt, volume.GaugeWarmAt, volume.GaugeHotAt));
        Assert.Equal(GaugeTone.Red, GaugeTone.At(volume.Fraction, cool, warm, hot));
        Assert.Equal(GaugeTone.Green, GaugeTone.At(0.2, cool, warm, hot));
    }
}
