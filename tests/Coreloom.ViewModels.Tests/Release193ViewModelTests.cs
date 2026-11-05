using Coreloom.Core.Settings;

namespace Coreloom.ViewModels.Tests;

/// <summary>1.9.3: Settings › Appearance › Gradient noise.</summary>
public class Release193ViewModelTests
{
    [Fact]
    public void Noise_StartsAtTheStandard_AndEachChangeIsSavedAndApplied()
    {
        var (shell, _, settings, _) = ShellViewModelTests.Create();
        var page = shell.Settings;
        var changes = new List<string>();
        settings.Changed += changes.Add;
        Assert.Equal(50, page.Noise);
        Assert.Equal("Standard", page.NoiseText);
        Assert.True(page.IsStandardNoise);

        var raised = new List<string?>();
        page.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        page.Noise = 0;
        Assert.Equal(0, settings.Current.Appearance.Noise);
        Assert.Contains("Appearance", changes); // the theme is applied again, with the grain off
        Assert.Equal("Off", page.NoiseText);
        Assert.False(page.IsStandardNoise);
        Assert.Contains(nameof(page.NoiseText), raised);
        Assert.Contains(nameof(page.IsStandardNoise), raised);

        page.Noise = 79.6;
        Assert.Equal(80, settings.Current.Appearance.Noise);
        Assert.Equal("80 %", page.NoiseText);
        page.Noise = 400;
        Assert.Equal(100, settings.Current.Appearance.Noise);

        page.ResetNoiseCommand.Execute(null);
        Assert.Equal(AppearanceSettings.StandardNoise, settings.Current.Appearance.Noise);
        Assert.True(page.IsStandardNoise);
        settings.Dispose();
    }
}
