using System.Text.Json;
using Coreloom.Core.Appearance;
using Coreloom.Core.Dashboard;
using Coreloom.Core.Settings;

namespace Coreloom.Core.Tests;

/// <summary>1.8.2: the window's background (colors, gradients, pictures, the frost the glass shows) and System health's height.</summary>
public class Release182Tests
{
    private static readonly Rgb Dark = Rgb.Parse("#0A0B0E");
    private static readonly Rgb Light = Rgb.Parse("#F3F4F6");

    [Fact]
    public void BackdropSettings_DefaultToCoreloomsGradient_AndRepairWhatIsWrong()
    {
        var fresh = new AppSettings().Normalize().Appearance.Backdrop;
        Assert.Equal(BackdropKind.Gradient, fresh.Kind);
        Assert.Equal(BackdropPresets.DefaultGradient, fresh.Gradient);
        Assert.Equal(75, fresh.ImageOpacity);
        Assert.Equal(20, fresh.Blur);

        var settings = new AppSettings();
        var b = settings.Appearance.Backdrop;
        b.Kind = (BackdropKind)42;
        b.Color = "red";
        b.Gradient = "no-such";
        b.Image = BackdropSettings.CustomImageId; // but no picture of your own
        b.CustomImage = "../../etc/passwd";
        b.ImageOpacity = 0;
        b.Blur = 400;
        settings.Normalize();

        Assert.Equal(BackdropKind.Gradient, b.Kind);
        Assert.Equal(BackdropPresets.DefaultColor, b.Color);
        Assert.Equal(BackdropPresets.DefaultGradient, b.Gradient);
        Assert.Null(b.CustomImage); // only a file name in Coreloom's own folder is kept
        Assert.Equal(BackdropPresets.DefaultImage, b.Image);
        Assert.Equal(BackdropSettings.MinimumOpacity, b.ImageOpacity);
        Assert.Equal(100, b.Blur);
        Assert.Equal(48, b.BlurPoints, 6);
        Assert.True(b.FrostPoints > b.BlurPoints / 2);

        // Your own picture is kept when it's a plain file name; a settings file from before 1.8.2 loads with the default.
        var own = new AppSettings();
        own.Appearance.Backdrop.Image = BackdropSettings.CustomImageId;
        own.Appearance.Backdrop.CustomImage = "custom-20261029.jpg";
        own.Appearance.Backdrop.Color = "#0b1020";
        own.Normalize();
        Assert.Equal(BackdropSettings.CustomImageId, own.Appearance.Backdrop.Image);
        Assert.Equal("#0B1020", own.Appearance.Backdrop.Color);

        var old = JsonSerializer.Deserialize<AppSettings>("{\"Appearance\":{\"Theme\":\"Dark\",\"UseMica\":true}}", SettingsStore.JsonOptions)!.Normalize();
        Assert.Equal(BackdropKind.Gradient, old.Appearance.Backdrop.Kind);
    }

    [Fact]
    public void BackdropSettings_RoundTripThroughTheSettingsFile()
    {
        var settings = new AppSettings();
        settings.Appearance.Backdrop.Kind = BackdropKind.Image;
        settings.Appearance.Backdrop.Image = "aurora";
        settings.Appearance.Backdrop.ImageOpacity = 55;
        settings.Appearance.Backdrop.Blur = 40;
        string json = JsonSerializer.Serialize(settings, SettingsStore.JsonOptions);
        Assert.Contains("\"kind\": \"Image\"", json);

        var loaded = JsonSerializer.Deserialize<AppSettings>(json, SettingsStore.JsonOptions)!.Normalize().Appearance.Backdrop;
        Assert.Equal(BackdropKind.Image, loaded.Kind);
        Assert.Equal("aurora", loaded.Image);
        Assert.Equal(55, loaded.ImageOpacity);
        Assert.Equal(40, loaded.Blur);
    }

    [Fact]
    public void Presets_AreDarkUniqueAndComplete()
    {
        Assert.Equal(BackdropPresets.Colors.Count, BackdropPresets.Colors.Select(c => c.Hex).Distinct().Count());
        Assert.All(BackdropPresets.Colors, c => Assert.True(Rgb.Parse(c.Hex).Luma < 0.12, c.Name));
        Assert.Equal(BackdropPresets.Gradients.Count, BackdropPresets.Gradients.Select(g => g.Id).Distinct().Count());
        Assert.All(BackdropPresets.Gradients, g => Assert.NotEmpty(g.Glows));
        Assert.True(BackdropPresets.FindGradient(BackdropPresets.DefaultGradient)!.UsesAccent);
        Assert.Equal(6, BackdropPresets.Images.Count);
        Assert.NotNull(BackdropPresets.FindImage(BackdropPresets.DefaultImage));

        // In the light theme a chosen color becomes a pale tint, so the light theme stays light.
        var tint = BackdropPresets.ForTheme(Rgb.Parse("#0A1630"), dark: false, Light);
        Assert.True(tint.Luma > 0.8);
        Assert.Equal(Rgb.Parse("#0A1630"), BackdropPresets.ForTheme(Rgb.Parse("#0A1630"), dark: true, Light));
    }

    [Fact]
    public void Gradient_IsDitheredWithinOneStep_AndLitWhereItsLightsAre()
    {
        var gradient = BackdropPresets.FindGradient("midnight")!;
        var plain = BackdropRenderer.Gradient(gradient, 160, 100, Rgb.Parse("#5B8CFF"), dark: true, Light, dither: false);
        var dithered = BackdropRenderer.Gradient(gradient, 160, 100, Rgb.Parse("#5B8CFF"), dark: true, Light, dither: true);
        int differ = 0;
        for (int i = 0; i < plain.Bgra.Length; i++)
        {
            Assert.InRange(Math.Abs(plain.Bgra[i] - dithered.Bgra[i]), 0, 1);
            differ += plain.Bgra[i] != dithered.Bgra[i] ? 1 : 0;
        }

        Assert.True(differ > plain.Bgra.Length / 10); // the noise is really there

        // Midnight's blue light sits at the top right: brighter there than at the top left.
        var topRight = plain.At(150, 2);
        var topLeft = plain.At(5, 2);
        Assert.True(topRight.B > topLeft.B + 0.05);
        Assert.All(Enumerable.Range(0, plain.Bgra.Length / 4), i => Assert.Equal(255, plain.Bgra[(i * 4) + 3]));

        // Coreloom's own gradient follows the accent color.
        var coreloom = BackdropPresets.FindGradient(BackdropPresets.DefaultGradient)!;
        var blue = BackdropRenderer.Gradient(coreloom, 80, 50, Rgb.Parse("#5B8CFF"), true, Light, false).At(8, 2);
        var green = BackdropRenderer.Gradient(coreloom, 80, 50, Rgb.Parse("#45D6A0"), true, Light, false).At(8, 2);
        Assert.True(blue.B > blue.G);
        Assert.True(green.G > green.B);

        // The light theme: pale everywhere.
        var light = BackdropRenderer.Gradient(gradient, 80, 50, Rgb.Parse("#5B8CFF"), dark: false, Light, dither: false);
        Assert.True(light.Average().Luma > 0.8);
    }

    [Fact]
    public void Cover_FillsTheFrameKeepingTheShape_AndCropsTheMiddle()
    {
        // 4 × 2: two red columns, then two blue ones. Covering a square keeps the middle two columns.
        var source = new PixelImage(4, 2);
        for (int y = 0; y < 2; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                int i = ((y * 4) + x) * 4;
                source.Bgra[i] = (byte)(x < 2 ? 0 : 255);
                source.Bgra[i + 2] = (byte)(x < 2 ? 255 : 0);
                source.Bgra[i + 3] = 255;
            }
        }

        var square = BackdropRenderer.Cover(source, 2, 2);
        Assert.True(square.At(0, 0).R > 0.9 && square.At(0, 0).B < 0.1);
        Assert.True(square.At(1, 1).B > 0.9 && square.At(1, 1).R < 0.1);

        // Shrinking a big picture a lot averages it (no pixels skipped): a fine checkerboard turns gray.
        var checker = new PixelImage(400, 400);
        for (int y = 0; y < 400; y++)
        {
            for (int x = 0; x < 400; x++)
            {
                byte v = (byte)(((x + y) % 2) == 0 ? 255 : 0);
                int i = ((y * 400) + x) * 4;
                checker.Bgra[i] = checker.Bgra[i + 1] = checker.Bgra[i + 2] = v;
                checker.Bgra[i + 3] = 255;
            }
        }

        var small = BackdropRenderer.Cover(checker, 20, 20);
        Assert.InRange(small.Average().R, 0.4, 0.6);
    }

    [Fact]
    public void Blur_SpreadsLightAndKeepsTheAverage()
    {
        var image = BackdropRenderer.Solid(new Rgb(0, 0, 0), 41, 41);
        int center = ((20 * 41) + 20) * 4;
        image.Bgra[center] = image.Bgra[center + 1] = image.Bgra[center + 2] = 255;
        BackdropRenderer.Blur(image, 3);
        Assert.True(image.Bgra[center] < 255);                  // the bright pixel spread out
        Assert.True(image.At(23, 20).R > 0);                     // to its neighbours
        Assert.Equal(0, image.At(0, 0).R, 6);                    // but not to the far corner

        var flat = BackdropRenderer.Solid(Rgb.Parse("#405060"), 30, 20);
        BackdropRenderer.Blur(flat, 5);
        Assert.Equal(Rgb.Parse("#405060"), flat.At(15, 10)); // a flat color stays exactly the same
    }

    [Fact]
    public void Render_MakesTheBackgroundAndASmallerFrostForEachKind()
    {
        var request = new BackdropRequest { Width = 400, Height = 300, Scale = 2, ThemeBase = Dark, Accent = Rgb.Parse("#5B8CFF"), FrostPoints = 30 };

        Assert.Null(BackdropRenderer.Render(request with { Kind = BackdropKind.SeeThrough }));

        var color = BackdropRenderer.Render(request with { Kind = BackdropKind.Color, Color = Rgb.Parse("#0B1020") })!;
        Assert.Equal(Rgb.Parse("#0B1020"), color.Background.At(0, 0));
        Assert.Equal(Rgb.Parse("#0B1020"), color.Frost.At(3, 3));

        var gradient = BackdropRenderer.Render(request with { Kind = BackdropKind.Gradient, Gradient = BackdropPresets.FindGradient("aurora") })!;
        Assert.Equal((400, 300), (gradient.Background.Width, gradient.Background.Height));
        Assert.True(gradient.Frost.Width < 100 && gradient.Frost.Height < 80); // small: it's stretched (and blurred) anyway

        // A white picture at half strength over the dark theme's background: half gray, plus a little of the dark.
        var white = BackdropRenderer.Solid(new Rgb(1, 1, 1), 64, 64);
        var picture = BackdropRenderer.Render(request with { Kind = BackdropKind.Image, Image = white, ImageOpacity = 0.5, BlurPoints = 0 })!;
        Assert.InRange(picture.Background.At(200, 150).R, 0.5, 0.56);
        Assert.Equal((400, 300), (picture.Background.Width, picture.Background.Height)); // sharp: full size

        // Blurred: drawn smaller (it's soft anyway), still the same color on average.
        var blurred = BackdropRenderer.Render(request with { Kind = BackdropKind.Image, Image = white, ImageOpacity = 0.5, BlurPoints = 24 })!;
        Assert.True(blurred.Background.Width < 400);
        Assert.InRange(blurred.Background.Average().R, 0.5, 0.56);

        // A missing picture: the theme's own color.
        var missing = BackdropRenderer.Render(request with { Kind = BackdropKind.Image, Image = null })!;
        Assert.Equal(Dark, missing.Background.At(0, 0));

        // Huge windows draw at most MaxBackgroundPixels.
        var (w, h) = BackdropRenderer.Fit(6000, 4000, BackdropRenderer.MaxBackgroundPixels);
        Assert.True((long)w * h <= BackdropRenderer.MaxBackgroundPixels);
        Assert.Equal(1.5, (double)w / h, 2);
    }

    [Fact]
    public void Health_FitsItsContent_OnRowsOfItsOwn()
    {
        Assert.True(DashboardCatalog.FitsContent(DashboardCatalog.Health));
        Assert.False(DashboardCatalog.FitsContent(DashboardCatalog.CpuLoad));

        // Six summary cards (1 × 2) on a 6-column grid, then System health across the width (6 × 2).
        var tiles = Enumerable.Repeat(new TileSize(1, 2), 6).Append(new TileSize(6, 2)).ToArray();
        var fits = tiles.Select((_, i) => i == 6).ToArray();
        var placements = DashboardLayout.Place(DashboardLayout.FitSizes(tiles, fits, 6), 6);
        var needs = tiles.Select((_, i) => i == 6 ? 118.0 : 260.0).ToArray();
        var rows = DashboardLayout.RowHeights(placements, fits, needs, 130, 16);

        Assert.Equal(new double[] { 130, 130, 51, 51 }, rows); // 118 over two rows and the gap between them
        Assert.Equal(118, DashboardLayout.Extent(rows, 2, 2, 16));
        Assert.Equal(130 + 16 + 130 + 16, DashboardLayout.Offset(rows, 2, 16));
        Assert.Equal(2.5, DashboardLayout.RowAt(rows, 130 + 16 + 130 + 16 + 33.5, 16, 130), 6);
    }

    [Fact]
    public void Health_BesideATallerCard_TakesItsHeight()
    {
        // On a 4-column grid: System health made 2 × 2, then a chart 2 × 3 beside it.
        var tiles = new[] { new TileSize(2, 2), new TileSize(2, 3) };
        var fits = new[] { true, false };
        var sizes = DashboardLayout.FitSizes(tiles, fits, 4);
        Assert.Equal(new TileSize(2, 3), sizes[0]); // grown down to the chart's bottom

        var placements = DashboardLayout.Place(sizes, 4);
        var rows = DashboardLayout.RowHeights(placements, fits, [80, 400], 130, 16);
        Assert.Equal(new double[] { 130, 130, 130 }, rows); // shared rows keep the dashboard's height
        Assert.Equal(DashboardLayout.Extent(rows, 0, 3, 16), DashboardLayout.Extent(3, 130, 16));

        // Without anything that fits its content, nothing changes.
        var plain = new[] { new TileSize(2, 2), new TileSize(2, 3) };
        Assert.Equal(plain, DashboardLayout.FitSizes(plain, [false, false], 4));
        Assert.Equal(Enumerable.Repeat(130.0, 3), DashboardLayout.RowHeights(DashboardLayout.Place(plain, 4), [false, false], [0, 0], 130, 16));
    }
}
