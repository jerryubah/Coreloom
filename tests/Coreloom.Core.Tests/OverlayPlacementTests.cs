using Coreloom.Core.Settings;

namespace Coreloom.Core.Tests;

public class OverlayPlacementTests
{
    // A 2560 × 1440 screen to the right of a 1920-wide one; a 300 × 120 overlay, 16 px from the edges.
    private const int Left = 1920, Top = 0, Width = 2560, Height = 1440, W = 300, H = 120, Margin = 16;

    [Theory]
    [InlineData(OverlayPosition.TopLeft, 1936, 16)]
    [InlineData(OverlayPosition.TopCenter, 3050, 16)]
    [InlineData(OverlayPosition.TopRight, 4164, 16)]
    [InlineData(OverlayPosition.MiddleLeft, 1936, 660)]
    [InlineData(OverlayPosition.Center, 3050, 660)]
    [InlineData(OverlayPosition.MiddleRight, 4164, 660)]
    [InlineData(OverlayPosition.BottomLeft, 1936, 1304)]
    [InlineData(OverlayPosition.BottomCenter, 3050, 1304)]
    [InlineData(OverlayPosition.BottomRight, 4164, 1304)]
    public void Locate_PutsTheOverlayInItsPlace(OverlayPosition position, int x, int y) =>
        Assert.Equal((x, y), OverlayPlacements.Locate(Left, Top, Width, Height, W, H, position, Margin, 0, 0));

    [Fact]
    public void Locate_AddsTheOffset_ButKeepsTheOverlayOnTheScreen()
    {
        Assert.Equal((1986, 46), OverlayPlacements.Locate(Left, Top, Width, Height, W, H, OverlayPosition.TopLeft, Margin, 50, 30));
        Assert.Equal((Left, Top), OverlayPlacements.Locate(Left, Top, Width, Height, W, H, OverlayPosition.TopLeft, Margin, -500, -500));
        Assert.Equal((Left + Width - W, Top + Height - H), OverlayPlacements.Locate(Left, Top, Width, Height, W, H, OverlayPosition.BottomRight, Margin, 900, 900));
    }

    [Theory]
    [InlineData(2000, 40, OverlayPosition.TopLeft)]
    [InlineData(3100, 600, OverlayPosition.Center)]
    [InlineData(4100, 1250, OverlayPosition.BottomRight)]
    [InlineData(4150, 700, OverlayPosition.MiddleRight)]
    public void FromDrop_TakesTheNearestPlace_AndStaysExactlyWhereItWasDropped(int x, int y, OverlayPosition expected)
    {
        var (position, dx, dy) = OverlayPlacements.FromDrop(Left, Top, Width, Height, x, y, W, H, Margin);

        Assert.Equal(expected, position);
        Assert.Equal((x, y), OverlayPlacements.Locate(Left, Top, Width, Height, W, H, position, Margin, dx, dy));
    }

    [Theory]
    [InlineData(1080, 1.0)]
    [InlineData(1440, 1.35)]
    [InlineData(1200, 1.1)]
    [InlineData(864, 0.85)]
    [InlineData(2160, 1.35)]
    [InlineData(0, 1.0)]
    public void ScreenScale_FollowsTheScreensHeight(double heightDip, double expected) =>
        Assert.Equal(expected, OverlayPlacements.ScreenScale(heightDip), 6);

    [Fact]
    public void PlacesPerGame_AreUsedOnlyWhenKeptPerGame()
    {
        var settings = new OverlaySettings
        {
            Position = OverlayPosition.TopLeft,
            OffsetX = 10,
            GamePlacements = [new OverlayPlacement { Program = "Cyberpunk2077.exe", Position = OverlayPosition.BottomRight, OffsetY = -40 }],
        }.Normalize();

        Assert.Equal((OverlayPosition.TopLeft, 10, 0), settings.PlacementFor("Cyberpunk2077.exe"));
        settings.PerGamePosition = true;
        Assert.Equal((OverlayPosition.BottomRight, 0, -40), settings.PlacementFor(@"D:\Games\cyberpunk2077.EXE"));
        Assert.Equal((OverlayPosition.TopLeft, 10, 0), settings.PlacementFor("witcher3.exe"));
        Assert.Equal((OverlayPosition.TopLeft, 10, 0), settings.PlacementFor(null));
    }

    [Fact]
    public void Normalize_KeepsTheNewestPlacePerGame_AndSensibleSizes()
    {
        var settings = new OverlaySettings
        {
            OffsetX = 99999,
            Scale = 900,
            Size = (OverlaySize)42,
            GamePlacements =
            [
                new OverlayPlacement { Program = @"C:\Games\a.exe", Position = OverlayPosition.TopRight },
                new OverlayPlacement { Program = "A.EXE", Position = OverlayPosition.Center },
                new OverlayPlacement { Program = " ", Position = OverlayPosition.Center },
            ],
            GroupSizes = new() { ["FrameRate"] = OverlaySize.Expanded, ["Graphics"] = OverlaySize.Standard, ["Bogus"] = OverlaySize.Compact },
        }.Normalize();

        Assert.Equal(OverlayPlacements.MaxOffset, settings.OffsetX);
        Assert.Equal(OverlayPlacements.MaxScale, settings.Scale);
        Assert.Equal(OverlaySize.Standard, settings.Size);
        var only = Assert.Single(settings.GamePlacements);
        Assert.Equal(("A.EXE", OverlayPosition.Center), (only.Program, only.Position));
        Assert.Equal(["FrameRate"], settings.GroupSizes.Keys);
        Assert.Equal(1.25, settings.GroupFactor(OverlayGroup.FrameRate));
        Assert.Equal(1, settings.GroupFactor(OverlayGroup.Graphics));
    }

    [Fact]
    public void SizeFactor_IsThePresetOrTheOwnScale()
    {
        Assert.Equal(0.85, new OverlaySettings { Size = OverlaySize.Compact }.SizeFactor);
        Assert.Equal(1, new OverlaySettings().SizeFactor);
        Assert.Equal(1.25, new OverlaySettings { Size = OverlaySize.Expanded }.SizeFactor);
        Assert.Equal(1.6, new OverlaySettings { Size = OverlaySize.Custom, Scale = 160 }.SizeFactor);
        Assert.True(new OverlaySettings().AutoScale);
    }
}
