using Coreloom.Core.Appearance;
using Coreloom.Core.Settings;

namespace Coreloom.Core.Tests;

/// <summary>1.9.2: the sidebar's look (its surface, drop shadow and blur) in Settings › Appearance.</summary>
public class Release192Tests
{
    private static readonly Rgb Dark = Rgb.Parse("#070A10");

    [Fact]
    public void Sidebar_Defaults_AreTheSidebarBeforeThisVersion()
    {
        var sidebar = new AppSettings().Appearance.Sidebar;
        Assert.Equal(SidebarSurface.FrostedGlass, sidebar.Surface);
        Assert.Equal(SidebarSettings.Standard, sidebar.Shadow);
        Assert.Equal(SidebarSettings.Standard, sidebar.Blur);
        Assert.Equal(1, sidebar.ShadowDepth, 6);                 // the shadow it always had
        Assert.True(sidebar.Blurs);
        Assert.False(sidebar.NeedsOwnFrost);                     // it shows the cards' frost, as before
        Assert.Equal(48.8, sidebar.FrostPoints(48.8), 6);
    }

    [Fact]
    public void Sidebar_FromAnOlderFile_GetsTheDefaults_AndOutOfRangeValuesAreClamped()
    {
        // Saved by 1.9.1: no "Sidebar" at all.
        string json = SettingsStore.Serialize(new AppSettings()).Replace("\"Sidebar\"", "\"SidebarOld\"", StringComparison.Ordinal);
        var read = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json, SettingsStore.JsonOptions)!.Normalize();
        Assert.Equal(SidebarSurface.FrostedGlass, read.Appearance.Sidebar.Surface);
        Assert.Equal(50, read.Appearance.Sidebar.Shadow);

        var odd = new AppSettings();
        odd.Appearance.Sidebar = new SidebarSettings { Surface = (SidebarSurface)42, Shadow = 400, Blur = -5 };
        odd.Normalize();
        Assert.Equal(SidebarSurface.FrostedGlass, odd.Appearance.Sidebar.Surface);
        Assert.Equal(100, odd.Appearance.Sidebar.Shadow);
        Assert.Equal(0, odd.Appearance.Sidebar.Blur);

        var none = new AppSettings();
        none.Appearance.Sidebar = null!;
        none.Normalize();
        Assert.NotNull(none.Appearance.Sidebar);
    }

    [Fact]
    public void Sidebar_SurvivesASaveAndLoad()
    {
        var settings = new AppSettings();
        settings.Appearance.Sidebar = new SidebarSettings { Surface = SidebarSurface.LiquidGlass, Shadow = 80, Blur = 30 };
        var read = SettingsStore.Clone(settings);
        Assert.Equal(SidebarSurface.LiquidGlass, read.Appearance.Sidebar.Surface);
        Assert.Equal(80, read.Appearance.Sidebar.Shadow);
        Assert.Equal(30, read.Appearance.Sidebar.Blur);
        Assert.Contains("\"LiquidGlass\"", SettingsStore.Serialize(settings), StringComparison.Ordinal); // by name, not number
    }

    [Fact]
    public void Sidebar_ShadowAndBlur_ScaleFromTheStandardInTheMiddle()
    {
        var sidebar = new SidebarSettings { Shadow = 0, Blur = 100 };
        Assert.Equal(0, sidebar.ShadowDepth);
        Assert.Equal(97.6, sidebar.FrostPoints(48.8), 6);       // twice the cards' blur
        Assert.True(sidebar.NeedsOwnFrost);

        sidebar.Shadow = 100;
        sidebar.Blur = 0;
        Assert.Equal(2, sidebar.ShadowDepth, 6);                 // twice the standard shadow
        Assert.Equal(4, sidebar.FrostPoints(48.8), 6);          // never quite clear: a light 4 points

        // Liquid glass is clearer than frosted glass at the same setting, so it always has its own frost.
        var liquid = new SidebarSettings { Surface = SidebarSurface.LiquidGlass };
        Assert.True(liquid.NeedsOwnFrost);
        Assert.Equal(48.8 * SidebarSettings.LiquidClarity, liquid.FrostPoints(48.8), 6);

        // Solid and translucent don't blur at all.
        foreach (var surface in new[] { SidebarSurface.Solid, SidebarSurface.Translucent })
        {
            var flat = new SidebarSettings { Surface = surface, Blur = 90 };
            Assert.False(flat.Blurs);
            Assert.False(flat.NeedsOwnFrost);
        }
    }

    [Fact]
    public void Render_DrawsTheSidebarsOwnFrost_OnlyWhenItsBlurDiffers()
    {
        var request = new BackdropRequest
        {
            Kind = BackdropKind.Gradient, Gradient = BackdropPresets.FindGradient("aurora"), Width = 400, Height = 300, Scale = 2,
            ThemeBase = Dark, Accent = Rgb.Parse("#2E7CF6"), FrostPoints = 30,
        };
        Assert.Null(BackdropRenderer.Render(request)!.SidebarFrost);
        Assert.Null(BackdropRenderer.Render(request with { SidebarFrostPoints = 30 })!.SidebarFrost);

        var layers = BackdropRenderer.Render(request with { SidebarFrostPoints = 8 })!;
        Assert.NotNull(layers.SidebarFrost);
        Assert.True(layers.SidebarFrost!.Width >= layers.Frost.Width); // a lighter blur is drawn at least as finely

        // A picture too, and its frosts come out the same size as the window's quarter or finer.
        var white = BackdropRenderer.Solid(new Rgb(1, 1, 1), 64, 64);
        var picture = BackdropRenderer.Render(request with { Kind = BackdropKind.Image, Image = white, ImageOpacity = 0.5, SidebarFrostPoints = 60 })!;
        Assert.NotNull(picture.SidebarFrost);
        Assert.InRange(picture.SidebarFrost!.Average().R, picture.Frost.Average().R - 0.02, picture.Frost.Average().R + 0.02); // same colour, more blur

        // A flat color has nothing to blur: no second frost.
        Assert.Null(BackdropRenderer.Render(request with { Kind = BackdropKind.Color, Color = Rgb.Parse("#0B1020"), SidebarFrostPoints = 8 })!.SidebarFrost);
    }
}
