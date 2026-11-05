using Coreloom.Core.Settings;

namespace Coreloom.Core.Tests;

/// <summary>1.9.3: the gradient noise setting.</summary>
public class Release193Tests
{
    [Fact]
    public void Noise_DefaultsToTheStandardGrain()
    {
        var appearance = new AppSettings().Appearance;
        Assert.Equal(AppearanceSettings.StandardNoise, appearance.Noise);
        Assert.Equal(50, AppearanceSettings.StandardNoise);
        Assert.Equal(1, appearance.NoiseFactor, 6); // the grain as it was before 1.9.3
    }

    [Fact]
    public void Noise_ScalesTheGrain_FromNoneToTwice()
    {
        var appearance = new AppearanceSettings { Noise = 0 };
        Assert.Equal(0, appearance.NoiseFactor);
        appearance.Noise = 100;
        Assert.Equal(2, appearance.NoiseFactor, 6);
        appearance.Noise = 25;
        Assert.Equal(0.5, appearance.NoiseFactor, 6);
    }

    [Fact]
    public void Noise_FromAnOlderFile_IsTheStandard_AndOddValuesAreClamped()
    {
        string json = SettingsStore.Serialize(new AppSettings()).Replace("\"Noise\"", "\"NoiseOld\"", StringComparison.Ordinal);
        var read = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json, SettingsStore.JsonOptions)!.Normalize();
        Assert.Equal(50, read.Appearance.Noise);

        var odd = new AppSettings();
        odd.Appearance.Noise = 250;
        odd.Normalize();
        Assert.Equal(100, odd.Appearance.Noise);
        odd.Appearance.Noise = -3;
        odd.Normalize();
        Assert.Equal(0, odd.Appearance.Noise);

        var kept = new AppSettings();
        kept.Appearance.Noise = 20;
        Assert.Equal(20, SettingsStore.Clone(kept).Appearance.Noise);
    }
}
