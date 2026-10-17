using Coreloom.Core.Settings;
using Coreloom.ViewModels.Overlay;
using Coreloom.ViewModels.Pages;

namespace Coreloom.ViewModels.Tests;

public class OverlayPlacementViewModelTests
{
    [Fact]
    public void Sizes_FollowThePreset_TheScreen_AndEachLine()
    {
        var (monitor, settings, _) = Fixture.Create();
        var overlay = Fixture.Context(monitor, settings).Overlay;
        Assert.Equal(14, overlay.FontSize);

        settings.Update("Overlay", s => s.Overlay.Size = OverlaySize.Compact);
        Assert.Equal(11.9, overlay.FontSize);
        settings.Update("Overlay", s => s.Overlay.Size = OverlaySize.Standard);

        overlay.ScreenScale = 1.25; // a 1440p screen at 100 % (follows the screen by default)
        Assert.Equal(17.5, overlay.FontSize);
        Assert.Equal("100 %", overlay.ScaleText); // its own size is still standard
        settings.Update("Overlay", s => s.Overlay.AutoScale = false);
        Assert.Equal(14, overlay.FontSize);

        settings.Update("Overlay", s => s.Overlay.GroupSizes["FrameRate"] = OverlaySize.Expanded);
        var fps = overlay.Groups.First(g => g.IsFrameRate);
        var gpu = overlay.Groups.First(g => g.Label == "GPU");
        Assert.Equal(17.5, fps.FontSize);
        Assert.Equal(22.8, fps.HeadlineFontSize);
        Assert.Equal(Math.Round(17.5 * 11.5), fps.GraphWidth);
        Assert.Equal("L", fps.SizeLabel);
        Assert.Equal(14, gpu.FontSize);
        Assert.Same(gpu, gpu.Values[0].Group);
        settings.Dispose();
    }

    [Fact]
    public void ALineCyclesThroughItsSizes()
    {
        var (monitor, settings, _) = Fixture.Create();
        var overlay = Fixture.Context(monitor, settings).Overlay;

        overlay.Groups.First(g => g.Label == "CPU").CycleSizeCommand.Execute(null);
        Assert.Equal(OverlaySize.Expanded, settings.Current.Overlay.GroupSizes["Processor"]);
        overlay.Groups.First(g => g.Label == "CPU").CycleSizeCommand.Execute(null);
        Assert.Equal(OverlaySize.Compact, settings.Current.Overlay.GroupSizes["Processor"]);
        Assert.Equal("S", overlay.Groups.First(g => g.Label == "CPU").SizeLabel);
        overlay.Groups.First(g => g.Label == "CPU").CycleSizeCommand.Execute(null);
        Assert.False(settings.Current.Overlay.GroupSizes.ContainsKey("Processor"));
        settings.Dispose();
    }

    [Fact]
    public void ResizingByTheCorner_ShowsLive_AndIsSavedAsACustomSize()
    {
        var (monitor, settings, _) = Fixture.Create();
        var overlay = Fixture.Context(monitor, settings).Overlay;
        overlay.IsMoving = true;

        overlay.LiveScale = 1.5;
        Assert.Equal(21, overlay.FontSize);
        Assert.Equal("150 %", overlay.ScaleText);
        Assert.Equal(OverlaySize.Standard, settings.Current.Overlay.Size); // not saved while dragging

        overlay.IsMoving = false; // Done
        Assert.Null(overlay.LiveScale);
        Assert.Equal(OverlaySize.Custom, settings.Current.Overlay.Size);
        Assert.Equal(150, settings.Current.Overlay.Scale);
        Assert.Equal(21, overlay.FontSize);
        settings.Dispose();
    }

    [Fact]
    public void ADroppedOverlay_IsSavedForAllGames_OrForTheGameWhenKeptPerGame()
    {
        var (monitor, settings, _) = Fixture.Create();
        var overlay = Fixture.Context(monitor, settings).Overlay;
        overlay.CurrentGame = @"D:\Games\Cyberpunk2077.exe";
        Assert.Equal("Saved for all games", overlay.MovingText);

        overlay.SavePlacement(OverlayPosition.BottomRight, -20, -10);
        Assert.Equal((OverlayPosition.BottomRight, -20, -10), (settings.Current.Overlay.Position, settings.Current.Overlay.OffsetX, settings.Current.Overlay.OffsetY));

        settings.Update("Overlay", s => s.Overlay.PerGamePosition = true);
        Assert.Equal("Saved for Cyberpunk2077.exe", overlay.MovingText);
        overlay.LiveScale = 0.8;
        overlay.SavePlacement(OverlayPosition.Center, 0, 120);

        var own = Assert.Single(settings.Current.Overlay.GamePlacements);
        Assert.Equal(("Cyberpunk2077.exe", OverlayPosition.Center, 120), (own.Program, own.Position, own.OffsetY));
        Assert.Equal((OverlayPosition.Center, 0, 120), overlay.Placement);
        Assert.Equal(OverlayPosition.BottomRight, settings.Current.Overlay.Position); // the place for all games stays
        Assert.Equal((OverlaySize.Custom, 80), (settings.Current.Overlay.Size, settings.Current.Overlay.Scale));

        overlay.CurrentGame = "witcher3.exe";
        Assert.Equal((OverlayPosition.BottomRight, -20, -10), overlay.Placement);
        settings.Dispose();
    }

    [Fact]
    public void TheSettingsCard_OffersNinePlaces_FineTuning_PerGamePlaces_AndSizes()
    {
        var (monitor, settings, _) = Fixture.Create();
        var card = new GpuViewModel(Fixture.Context(monitor, settings)).OverlaySettings;

        Assert.Equal(9, card.Positions.Count);
        card.OffsetX = 40;
        card.OffsetY = -12;
        Assert.Equal((40, -12), (settings.Current.Overlay.OffsetX, settings.Current.Overlay.OffsetY));
        card.Positions.First(p => p.Position == OverlayPosition.Center).SelectCommand.Execute(null);
        Assert.Equal((OverlayPosition.Center, 0, 0), (settings.Current.Overlay.Position, settings.Current.Overlay.OffsetX, settings.Current.Overlay.OffsetY));

        card.SelectedSize = card.SizeOptions.First(o => o.Value == OverlaySize.Expanded);
        card.SelectedSize = card.SizeOptions.First(o => o.Value == OverlaySize.Custom);
        Assert.Equal(125, settings.Current.Overlay.Scale); // custom starts from the size it had
        card.Scale = 90;
        Assert.Equal((OverlaySize.Custom, 90), (settings.Current.Overlay.Size, settings.Current.Overlay.Scale));

        var graphics = card.ItemGroups.First(g => g.Group == OverlayGroup.Graphics);
        graphics.SelectedSize = graphics.SizeOptions[0];
        Assert.Equal(OverlaySize.Compact, settings.Current.Overlay.GroupSizes["Graphics"]);
        Assert.Equal("S", graphics.SelectedSize.Label);

        settings.Update("Overlay", s => s.Overlay.GamePlacements.Add(new OverlayPlacement { Program = "game.exe", Position = OverlayPosition.TopRight }));
        var chip = Assert.Single(card.GamePlacements);
        Assert.Equal("game.exe · top right", chip.PlaceText);
        chip.RemoveCommand.Execute(null);
        Assert.Empty(settings.Current.Overlay.GamePlacements);

        card.OpenCommand.Execute(null);
        card.MoveOnScreenCommand.Execute(null);
        Assert.True(settings.Current.Overlay.Enabled);
        Assert.True(card.Preview.IsMoving);
        Assert.False(card.IsOpen);
        Assert.Equal("In a game, Shift + End does the same.", card.MoveHint);

        card.ResetCommand.Execute(null);
        Assert.Equal((OverlaySize.Standard, 100, true), (settings.Current.Overlay.Size, settings.Current.Overlay.Scale, settings.Current.Overlay.AutoScale));
        Assert.Empty(settings.Current.Overlay.GroupSizes);
        settings.Dispose();
    }
}
