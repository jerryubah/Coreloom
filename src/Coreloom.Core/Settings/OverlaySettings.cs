using System.Globalization;
using System.Text.Json.Serialization;

namespace Coreloom.Core.Settings;

/// <summary>Where the in-game overlay sits on the screen of the window in front.</summary>
public enum OverlayPosition
{
    TopLeft,
    TopCenter,
    TopRight,
    MiddleLeft,
    MiddleRight,
    BottomLeft,
    BottomCenter,
    BottomRight,
}

/// <summary>One group per line, or everything on one line.</summary>
public enum OverlayLayout
{
    Vertical,
    Horizontal,
}

/// <summary>Text weight ("thickness") of the overlay.</summary>
public enum OverlayFontWeight
{
    Regular,
    Medium,
    SemiBold,
    Bold,
}

/// <summary>Modifier keys of a hotkey (same values as the Win32 MOD_* flags).</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Windows = 8,
}

/// <summary>A keyboard shortcut: a Windows virtual-key code (0 = none) and modifiers.</summary>
public sealed class OverlayHotkey
{
    /// <summary>The End key.</summary>
    public const int DefaultKey = 0x23;

    public int Key { get; set; } = DefaultKey;

    public HotkeyModifiers Modifiers { get; set; }

    [JsonIgnore]
    public bool IsNone => Key == 0;

    public static OverlayHotkey Default => new();

    public static OverlayHotkey None => new() { Key = 0 };

    public override string ToString() => Hotkeys.Describe(Key, Modifiers);
}

/// <summary>A reading the overlay can show.</summary>
public enum OverlayGroup
{
    FrameRate,
    Graphics,
    Processor,
    Memory,
}

/// <summary>The in-game overlay: whether it is on, what it shows, how it looks and where it sits.</summary>
public sealed class OverlaySettings
{
    public const string DefaultTextColor = "#FFFFFF";
    public const string DefaultBackgroundColor = "#0E1117";
    public const double MinFontSize = 10;
    public const double MaxFontSize = 32;

    public bool Enabled { get; set; }

    /// <summary>
    /// Set the first time the overlay is turned on. The hotkey only works from then on, so pressing End in other apps
    /// does nothing for people who never use the overlay.
    /// </summary>
    public bool HasBeenEnabled { get; set; }

    /// <summary>Shows or hides the overlay from anywhere, also in games. End by default.</summary>
    public OverlayHotkey Hotkey { get; set; } = OverlayHotkey.Default;

    /// <summary>The hotkey to listen for: none until the overlay has been used.</summary>
    [JsonIgnore]
    public OverlayHotkey ActiveHotkey => HasBeenEnabled || Enabled ? Hotkey : OverlayHotkey.None;

    /// <summary>Ids of the readings shown (see <see cref="OverlayCatalog"/>); shown in the catalog's order.</summary>
    public List<string> Items { get; set; } = OverlayCatalog.Defaults();

    public OverlayPosition Position { get; set; } = OverlayPosition.TopLeft;

    public OverlayLayout Layout { get; set; } = OverlayLayout.Vertical;

    public double FontSize { get; set; } = 14;

    public OverlayFontWeight FontWeight { get; set; } = OverlayFontWeight.SemiBold;

    public string TextColor { get; set; } = DefaultTextColor;

    /// <summary>Text opacity in percent (20–100).</summary>
    public int TextOpacity { get; set; } = 100;

    public string BackgroundColor { get; set; } = DefaultBackgroundColor;

    /// <summary>Background opacity in percent (0 = no background).</summary>
    public int BackgroundOpacity { get; set; } = 70;

    public int CornerRadius { get; set; } = 16;

    /// <summary>Distance from the edges of the screen, in pixels at 100 % scaling.</summary>
    public int EdgeMargin { get; set; } = 16;

    /// <summary>"GPU", "CPU"… in front of each line.</summary>
    public bool ShowLabels { get; set; } = true;

    /// <summary>Values turn amber or red when they reach the warning levels (temperatures, load).</summary>
    public bool ColorWarnings { get; set; } = true;

    /// <summary>A soft shadow behind the text, so it stays readable without a background.</summary>
    public bool TextShadow { get; set; } = true;

    /// <summary>Which new readings have been offered to existing setups (1: the frame-time graph, 1.4.1).</summary>
    public int Revision { get; set; }

    public const int CurrentRevision = 1;

    /// <summary>Repairs out-of-range or missing values.</summary>
    public OverlaySettings Normalize()
    {
        Hotkey ??= OverlayHotkey.Default;
        if (Hotkey.Key < 0 || Hotkey.Key > 0xFE || Hotkeys.IsModifierKey(Hotkey.Key))
        {
            Hotkey = OverlayHotkey.Default;
        }

        Hotkey.Modifiers &= HotkeyModifiers.Alt | HotkeyModifiers.Control | HotkeyModifiers.Shift | HotkeyModifiers.Windows;
        Items = OverlayCatalog.Normalize(Items);
        if (Revision < 1)
        {
            // 1.4.1 added the frame-time graph: show it to everyone who shows the frame rate.
            if (Items.Contains(OverlayCatalog.Fps) && !Items.Contains(OverlayCatalog.FrameGraph))
            {
                Items = OverlayCatalog.Normalize([.. Items, OverlayCatalog.FrameGraph]);
            }

            Revision = CurrentRevision;
        }

        if (!Enum.IsDefined(Position))
        {
            Position = OverlayPosition.TopLeft;
        }

        if (!Enum.IsDefined(Layout))
        {
            Layout = OverlayLayout.Vertical;
        }

        if (!Enum.IsDefined(FontWeight))
        {
            FontWeight = OverlayFontWeight.SemiBold;
        }

        FontSize = double.IsFinite(FontSize) ? Math.Clamp(Math.Round(FontSize), MinFontSize, MaxFontSize) : 14;
        TextColor = AccentPalette.IsValidHex(TextColor) ? TextColor.ToUpperInvariant() : DefaultTextColor;
        BackgroundColor = AccentPalette.IsValidHex(BackgroundColor) ? BackgroundColor.ToUpperInvariant() : DefaultBackgroundColor;
        TextOpacity = Math.Clamp(TextOpacity, 20, 100);
        BackgroundOpacity = Math.Clamp(BackgroundOpacity, 0, 100);
        CornerRadius = Math.Clamp(CornerRadius, 0, 24);
        EdgeMargin = Math.Clamp(EdgeMargin, 0, 160);
        return this;
    }
}

/// <summary>The readings the overlay offers, in display order, and which are shown out of the box.</summary>
public static class OverlayCatalog
{
    public const string Fps = "fps";
    public const string FrameTime = "frame-time";
    public const string LowFps = "fps-low";
    public const string GpuLoad = "gpu-load";
    public const string GpuTemperature = "gpu-temp";
    public const string GpuHotSpot = "gpu-hotspot";
    public const string GpuPower = "gpu-power";
    public const string GpuClock = "gpu-clock";
    public const string GpuMemoryClock = "gpu-mem-clock";
    public const string GpuMemory = "gpu-vram";
    public const string GpuFan = "gpu-fan";
    public const string GpuVoltage = "gpu-voltage";
    public const string CpuLoad = "cpu-load";
    public const string CpuTemperature = "cpu-temp";
    public const string CpuPower = "cpu-power";
    public const string CpuClock = "cpu-clock";
    public const string CpuFan = "cpu-fan";
    public const string Ram = "ram";

    /// <summary>A graph of the latest frame times under the FPS line (not a number).</summary>
    public const string FrameGraph = "frame-graph";

    public static IReadOnlyList<(string Id, string Name, OverlayGroup Group)> All { get; } =
    [
        (Fps, "Frames per second (FPS)", OverlayGroup.FrameRate),
        (FrameTime, "Frame time", OverlayGroup.FrameRate),
        (LowFps, "1% low FPS", OverlayGroup.FrameRate),
        (FrameGraph, "Frame time graph", OverlayGroup.FrameRate),
        (GpuLoad, "Utilization", OverlayGroup.Graphics),
        (GpuTemperature, "Temperature", OverlayGroup.Graphics),
        (GpuHotSpot, "Hot spot", OverlayGroup.Graphics),
        (GpuPower, "Power", OverlayGroup.Graphics),
        (GpuClock, "Core clock", OverlayGroup.Graphics),
        (GpuMemoryClock, "Memory clock", OverlayGroup.Graphics),
        (GpuMemory, "VRAM used", OverlayGroup.Graphics),
        (GpuFan, "Fan speed", OverlayGroup.Graphics),
        (GpuVoltage, "Voltage", OverlayGroup.Graphics),
        (CpuLoad, "Utilization", OverlayGroup.Processor),
        (CpuTemperature, "Temperature", OverlayGroup.Processor),
        (CpuPower, "Power", OverlayGroup.Processor),
        (CpuClock, "Clock", OverlayGroup.Processor),
        (CpuFan, "Fan speed", OverlayGroup.Processor),
        (Ram, "RAM used", OverlayGroup.Memory),
    ];

    /// <summary>
    /// FPS and frame time with the frame-time graph, the graphics card's load, temperature, power and fan, the processor's
    /// load and temperature, and RAM.
    /// </summary>
    public static List<string> Defaults() =>
        [Fps, FrameTime, FrameGraph, GpuLoad, GpuTemperature, GpuPower, GpuFan, CpuLoad, CpuTemperature, Ram];

    /// <summary>Readings that need frame rates measured (the FPS numbers and the graph).</summary>
    public static bool IsFrameRate(string id) => id is Fps or FrameTime or LowFps or FrameGraph;

    /// <summary>A reading shown as a number on a line (everything but the graph).</summary>
    public static bool IsValue(string id) => id != FrameGraph;

    public static OverlayGroup GroupOf(string id) => All.FirstOrDefault(i => i.Id == id).Group;

    /// <summary>Known ids only, each once, in catalog order.</summary>
    public static List<string> Normalize(IEnumerable<string>? items)
    {
        var shown = (items ?? []).Where(i => i is not null).ToHashSet(StringComparer.Ordinal);
        return All.Where(i => shown.Contains(i.Id)).Select(i => i.Id).ToList();
    }
}

/// <summary>Names and checks for hotkeys, without depending on WPF or WinForms.</summary>
public static class Hotkeys
{
    /// <summary>"Ctrl + Shift + O", "End", or "None".</summary>
    public static string Describe(int key, HotkeyModifiers modifiers)
    {
        if (key == 0)
        {
            return "None";
        }

        var parts = new List<string>(5);
        if (modifiers.HasFlag(HotkeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (modifiers.HasFlag(HotkeyModifiers.Windows))
        {
            parts.Add("Win");
        }

        parts.Add(KeyName(key));
        return string.Join(" + ", parts);
    }

    /// <summary>Shift, Ctrl, Alt and the Windows keys can't be a hotkey on their own.</summary>
    public static bool IsModifierKey(int key) => key is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or (>= 0xA0 and <= 0xA5);

    public static string KeyName(int key) => key switch
    {
        0x08 => "Backspace",
        0x09 => "Tab",
        0x0D => "Enter",
        0x13 => "Pause",
        0x14 => "Caps Lock",
        0x1B => "Esc",
        0x20 => "Space",
        0x21 => "Page Up",
        0x22 => "Page Down",
        0x23 => "End",
        0x24 => "Home",
        0x25 => "Left",
        0x26 => "Up",
        0x27 => "Right",
        0x28 => "Down",
        0x2C => "Print Screen",
        0x2D => "Insert",
        0x2E => "Delete",
        >= 0x30 and <= 0x39 => ((char)key).ToString(),
        >= 0x41 and <= 0x5A => ((char)key).ToString(),
        >= 0x60 and <= 0x69 => "Num " + (key - 0x60).ToString(CultureInfo.InvariantCulture),
        0x6A => "Num *",
        0x6B => "Num +",
        0x6D => "Num -",
        0x6E => "Num .",
        0x6F => "Num /",
        >= 0x70 and <= 0x87 => "F" + (key - 0x6F).ToString(CultureInfo.InvariantCulture),
        0x90 => "Num Lock",
        0x91 => "Scroll Lock",
        0xBA => ";",
        0xBB => "=",
        0xBC => ",",
        0xBD => "-",
        0xBE => ".",
        0xBF => "/",
        0xC0 => "`",
        0xDB => "[",
        0xDC => "\\",
        0xDD => "]",
        0xDE => "'",
        _ => "Key " + key.ToString("X2", CultureInfo.InvariantCulture),
    };
}
