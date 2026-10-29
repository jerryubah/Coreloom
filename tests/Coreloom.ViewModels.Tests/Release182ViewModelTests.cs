using Coreloom.Core.Appearance;
using Coreloom.Core.Settings;
using Coreloom.ViewModels.Pages;
using Coreloom.ViewModels.Services;

namespace Coreloom.ViewModels.Tests;

/// <summary>1.8.2: Settings › Appearance › Background, and System health fitting its content.</summary>
public sealed class Release182ViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "coreloom-backdrop-" + Guid.NewGuid().ToString("N"));

    private (BackdropViewModel Backdrop, SettingsService Settings, FakeShell Shell, List<string> Changes) Create(bool micaSupported = true, bool isMac = false)
    {
        var (_, settings, _) = Fixture.Create();
        var changes = new List<string>();
        settings.Changed += changes.Add;
        var shell = new FakeShell();
        var theme = new FakeTheme { IsMicaSupported = micaSupported, BackdropFolder = Path.Combine(_dir, "Backdrops") };
        return (new BackdropViewModel(settings, shell, theme, isMac), settings, shell, changes);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    [Fact]
    public void Kinds_IncludeSeeThroughOnlyWhereTheSystemHasIt_NamedForEachPlatform()
    {
        var (windows, _, _, _) = Create(micaSupported: true);
        Assert.Equal(new[] { "Gradients", "Colors", "Pictures", "Mica" }, windows.Kinds.Select(k => k.Label));
        var (mac, _, _, _) = Create(micaSupported: true, isMac: true);
        Assert.Equal("Translucent", mac.Kinds.Last().Label);
        var (old, _, _, _) = Create(micaSupported: false);
        Assert.DoesNotContain(old.Kinds, k => k.Value == BackdropKind.SeeThrough);

        old.Kind = BackdropKind.SeeThrough; // not offered: ignored
        Assert.Equal(BackdropKind.Gradient, old.Kind);
        Assert.True(old.IsGradient);
    }

    [Fact]
    public void Choosing_AColorGradientOrPicture_SwitchesToItAndSaves()
    {
        var (backdrop, settings, _, changes) = Create();
        var raised = new List<string?>();
        backdrop.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        backdrop.SelectColorCommand.Execute("#0b1020");
        Assert.Equal(BackdropKind.Color, settings.Current.Appearance.Backdrop.Kind);
        Assert.Equal("#0B1020", backdrop.Color);
        Assert.Equal("Midnight", backdrop.ColorName);
        Assert.True(backdrop.IsColor);
        Assert.Contains("Appearance", changes); // the theme applies it at once
        Assert.Contains(string.Empty, raised);

        backdrop.SelectGradientCommand.Execute("aurora");
        Assert.True(backdrop.IsGradient);
        Assert.Equal("aurora", backdrop.Gradient);
        backdrop.SelectGradientCommand.Execute("no-such");
        Assert.Equal("aurora", backdrop.Gradient);

        backdrop.SelectImageCommand.Execute("dunes");
        Assert.True(backdrop.IsImage);
        Assert.Equal("dunes", settings.Current.Appearance.Backdrop.Image);
        backdrop.SelectImageCommand.Execute(BackdropSettings.CustomImageId); // none chosen yet: nothing happens
        Assert.Equal("dunes", backdrop.Image);
    }

    [Fact]
    public void CustomColor_AppliesValidHexOnly_AndKeepsWhatWasTyped()
    {
        var (backdrop, settings, _, _) = Create();
        backdrop.CustomColor = "12";
        Assert.Equal("12", backdrop.CustomColor);
        Assert.Equal(BackdropKind.Gradient, settings.Current.Appearance.Backdrop.Kind);

        backdrop.CustomColor = "1a2b3c";
        Assert.Equal(BackdropKind.Color, settings.Current.Appearance.Backdrop.Kind);
        Assert.Equal("#1A2B3C", backdrop.Color);
        Assert.Equal("#1A2B3C", backdrop.ColorName); // not one of the suggestions: its value
    }

    [Fact]
    public void Sliders_AreClampedAndDescribed()
    {
        var (backdrop, settings, _, _) = Create();
        backdrop.ImageOpacity = 3;
        Assert.Equal(BackdropSettings.MinimumOpacity, settings.Current.Appearance.Backdrop.ImageOpacity);
        backdrop.ImageOpacity = 64.6;
        Assert.Equal(65, settings.Current.Appearance.Backdrop.ImageOpacity);
        Assert.Equal("65 %", backdrop.ImageOpacityText);

        backdrop.Blur = 0;
        Assert.Equal("Sharp", backdrop.BlurText);
        backdrop.Blur = 140;
        Assert.Equal(100, settings.Current.Appearance.Backdrop.Blur);
        Assert.Equal("100 %", backdrop.BlurText);
    }

    [Fact]
    public void ChoosingYourOwnPicture_KeepsACopy_AndRemovesThePreviousOne()
    {
        var (backdrop, settings, shell, _) = Create();
        Directory.CreateDirectory(_dir);
        string first = Path.Combine(_dir, "Holiday.JPG");
        File.WriteAllBytes(first, [1, 2, 3]);
        shell.Picked = first;

        Assert.True(backdrop.CanChooseImage);
        backdrop.ChooseImageCommand.Execute(null);
        var b = settings.Current.Appearance.Backdrop;
        Assert.Equal(BackdropKind.Image, b.Kind);
        Assert.Equal(BackdropSettings.CustomImageId, b.Image);
        Assert.NotNull(b.CustomImage);
        Assert.EndsWith(".jpg", b.CustomImage);
        Assert.True(backdrop.HasCustomImage);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(backdrop.CustomImagePath!));
        Assert.StartsWith("Pictures", shell.Filter);
        string firstCopy = backdrop.CustomImagePath!;

        // Another picture replaces it (the old copy is deleted).
        Thread.Sleep(1100); // copies are named by the second
        string second = Path.Combine(_dir, "City.png");
        File.WriteAllBytes(second, [4, 5]);
        shell.Picked = second;
        backdrop.ChooseImageCommand.Execute(null);
        Assert.NotEqual(firstCopy, backdrop.CustomImagePath);
        Assert.False(File.Exists(firstCopy));
        Assert.True(File.Exists(backdrop.CustomImagePath));

        // Cancelled: nothing changes; a missing file: a message, nothing changes.
        shell.Picked = null;
        backdrop.ChooseImageCommand.Execute(null);
        Assert.Null(backdrop.Message);
        shell.Picked = Path.Combine(_dir, "gone.jpg");
        string before = settings.Current.Appearance.Backdrop.CustomImage!;
        backdrop.ChooseImageCommand.Execute(null);
        Assert.True(backdrop.HasMessage);
        Assert.Equal(before, settings.Current.Appearance.Backdrop.CustomImage);
    }

    [Fact]
    public void WithoutAFolder_YourOwnPictureIsNotOffered()
    {
        var (settings, shell) = (Fixture.Create().Settings, new FakeShell());
        var backdrop = new BackdropViewModel(settings, shell, new FakeTheme { BackdropFolder = null }, isMac: true);
        Assert.False(backdrop.CanChooseImage);
        Assert.False(backdrop.ChooseImageCommand.CanExecute(null));
        Assert.Null(backdrop.CustomImagePath);
        Assert.Contains("Reduce transparency", backdrop.SeeThroughText);
    }

    [Fact]
    public void HealthTile_FitsItsContent_OthersDont()
    {
        var health = DashboardCatalog.All.Single(c => c.Id == DashboardCatalog.Health);
        Assert.True(DashboardCatalog.FitsContent(health.Id));
        Assert.All(DashboardCatalog.All.Where(c => c.Id != DashboardCatalog.Health), c => Assert.False(DashboardCatalog.FitsContent(c.Id)));
    }

    private sealed class FakeTheme : IThemeService
    {
        public bool IsMicaSupported { get; init; } = true;

        public string? BackdropFolder { get; init; }

        public void Apply(AppearanceSettings appearance)
        {
        }
    }

    private sealed class FakeShell : IShellService
    {
        public string? Picked { get; set; }

        public string? Filter { get; private set; }

        public void OpenUrl(string url)
        {
        }

        public void OpenFolder(string path)
        {
        }

        public void OpenFile(string path)
        {
        }

        public void CopyToClipboard(string text)
        {
        }

        public string? PickSaveFile(string suggestedName, string filter) => null;

        public string? PickOpenFile(string title, string filter, string? initialDirectory = null)
        {
            Filter = filter;
            return Picked;
        }

        public void ShowMessage(string title, string message)
        {
        }

        public bool Confirm(string title, string message) => true;
    }
}
