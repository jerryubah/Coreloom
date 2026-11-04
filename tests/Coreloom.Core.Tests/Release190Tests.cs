using Coreloom.Core.Settings;

namespace Coreloom.Core.Tests;

/// <summary>1.9.0: the uplifted look's settings (the deeper blue accent, moved to once; the frost behind the glass).</summary>
public class Release190Tests
{
    [Fact]
    public void Accent_TheDeeperBlueIsTheDefault_AndTheOldOneStaysOnOffer()
    {
        Assert.Equal("#2E7CF6", AccentPalette.Default);
        Assert.Equal("#5B8CFF", AccentPalette.PreviousDefault);
        Assert.Equal(AccentPalette.Default, AccentPalette.Presets[0].Hex);
        Assert.Contains(AccentPalette.Presets, p => p.Hex == AccentPalette.PreviousDefault && p.Name == "Sky");
        Assert.Equal(AccentPalette.Default, new AppSettings().Appearance.Accent);
    }

    [Fact]
    public void Settings_FromBeforeThisVersion_GetTheNewBlue_OnlyWhenTheyKeptTheOldDefault()
    {
        // Saved by 1.8.x with the accent never changed: the new look's blue, once.
        var kept = new AppSettings { SchemaVersion = 1 };
        kept.Appearance.Accent = "#5B8CFF";
        kept.Normalize();
        Assert.Equal("#2E7CF6", kept.Appearance.Accent);
        Assert.Equal(AppSettings.CurrentSchemaVersion, kept.SchemaVersion);

        // Saved by 1.8.x with an accent chosen on purpose: kept.
        var chosen = new AppSettings { SchemaVersion = 1 };
        chosen.Appearance.Accent = "#45D6A0";
        chosen.Normalize();
        Assert.Equal("#45D6A0", chosen.Appearance.Accent);

        // Saved by this version with the old blue picked again from the palette ("Sky"): kept, too.
        var sky = new AppSettings { SchemaVersion = AppSettings.CurrentSchemaVersion };
        sky.Appearance.Accent = "#5B8CFF";
        sky.Normalize();
        Assert.Equal("#5B8CFF", sky.Appearance.Accent);
    }

    [Fact]
    public void Glass_IsFrostedDeeperThanBefore()
    {
        var backdrop = new BackdropSettings();
        Assert.True(backdrop.FrostPoints >= 44);
        Assert.True(new BackdropSettings { Blur = 100 }.FrostPoints > backdrop.FrostPoints);
    }
}
