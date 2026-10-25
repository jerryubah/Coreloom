using Coreloom.Core.Formatting;
using Coreloom.Core.Settings;
using Coreloom.Core.Telemetry;

namespace Coreloom.Hardware.Mac.Tests;

/// <summary>A Mac's first-launch settings and the readings window's hotkey on a Mac keyboard.</summary>
public sealed class MacSettingsTests
{
    [Theory]
    [InlineData(true, 105, 100)]
    [InlineData(false, 97, 100)]
    public void Defaults_FitAMac(bool appleSilicon, double cpu, double gpu)
    {
        var settings = MacDefaults.Apply(new AppSettings().Normalize(), appleSilicon);
        Assert.Equal(DataUnitStyle.Decimal, settings.Monitoring.DataUnits);
        Assert.Equal(ThemePreference.System, settings.Appearance.Theme);
        Assert.Equal(cpu, settings.Alerts.Rules.Single(r => r.MetricId == MetricIds.CpuTemperature).Threshold);
        Assert.Equal(gpu, settings.Alerts.Rules.Single(r => r.MetricId == MetricIds.GpuTemperature(0)).Threshold);
        Assert.DoesNotContain(settings.Overlay.Items, OverlayCatalog.IsFrameRate);
        Assert.Contains(OverlayCatalog.GpuLoad, settings.Overlay.Items);
        Assert.All(settings.Overlay.Items, id => Assert.True(OverlayCatalog.OnMac(id), id));
        Assert.DoesNotContain(OverlayCatalog.GpuFan, settings.Overlay.Items);
        Assert.Equal("⌥⌘O", Hotkeys.Describe(settings.Overlay.Hotkey.Key, settings.Overlay.Hotkey.Modifiers, mac: true));
    }

    [Fact]
    public void Units_StorageLikeFinderMemoryLikeAboutThisMac()
    {
        var f = new UnitFormatter(dataUnits: DataUnitStyle.Decimal, culture: System.Globalization.CultureInfo.InvariantCulture) { BinaryMemory = true };
        Assert.Equal("64 GB", f.MemoryBytes(64L << 30, 0).ToString());
        Assert.Equal("2 TB", f.Bytes(2_000_000_000_000, 0).ToString());
        Assert.Equal(3, f.MemoryUnitIndex(64L << 30));
        Assert.Equal("16.0", f.MemoryBytesAs(16L << 30, 3).Number);

        var windows = new UnitFormatter(dataUnits: DataUnitStyle.Decimal, culture: System.Globalization.CultureInfo.InvariantCulture) { BinaryMemory = false };
        Assert.Equal("69 GB", windows.MemoryBytes(64L << 30, 0).ToString()); // decimal everywhere, as asked
    }

    [Theory]
    [InlineData(0x4F, HotkeyModifiers.Alt | HotkeyModifiers.Windows, "⌥⌘O")]
    [InlineData(0x4F, HotkeyModifiers.Control | HotkeyModifiers.Shift | HotkeyModifiers.Alt | HotkeyModifiers.Windows, "⌃⌥⇧⌘O")]
    [InlineData(0x23, HotkeyModifiers.None, "↘")]
    [InlineData(0x7B, HotkeyModifiers.Control, "⌃F12")]
    [InlineData(0x20, HotkeyModifiers.Windows, "⌘Space")]
    public void Hotkeys_ReadTheMacWay(int key, HotkeyModifiers modifiers, string expected)
    {
        Assert.Equal(expected, Hotkeys.Describe(key, modifiers, mac: true));
    }

    [Fact]
    public void Hotkeys_KeepTheWindowsNamesOnWindows()
    {
        Assert.Equal("Alt + Win + O", Hotkeys.Describe(0x4F, HotkeyModifiers.Alt | HotkeyModifiers.Windows, mac: false));
    }

    [Theory]
    [InlineData(0x41, 0x00)] // A
    [InlineData(0x4F, 0x1F)] // O
    [InlineData(0x5A, 0x06)] // Z
    [InlineData(0x30, 0x1D)] // 0
    [InlineData(0x35, 0x17)] // 5
    [InlineData(0x70, 0x7A)] // F1
    [InlineData(0x7B, 0x6F)] // F12
    [InlineData(0x83, 0x5A)] // F20
    [InlineData(0x23, 0x77)] // End
    [InlineData(0x26, 0x7E)] // Up
    [InlineData(0x60, 0x52)] // Keypad 0
    [InlineData(0x2C, -1)] // Print Screen: not on a Mac keyboard
    [InlineData(0x91, -1)] // Scroll Lock
    public void Hotkeys_MapToMacKeyCodes(int key, int expected)
    {
        Assert.Equal(expected, Hotkeys.MacKeyCode(key));
    }

    [Fact]
    public void Hotkeys_EveryLetterAndDigitHasItsOwnMacKey()
    {
        var codes = Enumerable.Range(0x41, 26).Concat(Enumerable.Range(0x30, 10)).Select(Hotkeys.MacKeyCode).ToList();
        Assert.All(codes, c => Assert.InRange(c, 0, 0x7F));
        Assert.Equal(codes.Count, codes.Distinct().Count());
    }

    [Fact]
    public void Hotkeys_ModifiersUseCarbonsFlags()
    {
        Assert.Equal(0x0100u | 0x0800u, Hotkeys.MacModifiers(HotkeyModifiers.Windows | HotkeyModifiers.Alt));
        Assert.Equal(0x0200u | 0x1000u, Hotkeys.MacModifiers(HotkeyModifiers.Shift | HotkeyModifiers.Control));
        Assert.Equal(0u, Hotkeys.MacModifiers(HotkeyModifiers.None));
    }
}
