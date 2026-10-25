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

    /// <summary>The middle of the screen (since 1.4.7).</summary>
    Center,
}

/// <summary>How big the overlay (or one of its lines) is drawn, relative to its text size.</summary>
public enum OverlaySize
{
    Compact,
    Standard,
    Expanded,

    /// <summary>The overlay's own scale (<see cref="OverlaySettings.Scale"/>), e.g. after resizing it by its corner.</summary>
    Custom,
}

/// <summary>Where the overlay sits for one game: a place on the screen and a fine-tuning offset.</summary>
public sealed class OverlayPlacement
{
    /// <summary>The game's program file ("Cyberpunk2077.exe").</summary>
    public string Program { get; set; } = string.Empty;

    public OverlayPosition Position { get; set; }

    /// <summary>Offset from that place, in pixels at 100 % scaling.</summary>
    public int OffsetX { get; set; }

    public int OffsetY { get; set; }
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

    /// <summary>Fine-tunes the place: pixels (at 100 % scaling) right of and below it; negative moves left or up.</summary>
    public int OffsetX { get; set; }

    public int OffsetY { get; set; }

    /// <summary>Remember a place for each game (when one is moved while that game is in front) instead of one for all.</summary>
    public bool PerGamePosition { get; set; }

    /// <summary>The places remembered per game.</summary>
    public List<OverlayPlacement> GamePlacements { get; set; } = [];

    /// <summary>The overlay's size: Compact, Standard, Expanded or its own scale.</summary>
    public OverlaySize Size { get; set; } = OverlaySize.Standard;

    /// <summary>The scale in percent used with <see cref="OverlaySize.Custom"/> (50–250).</summary>
    public int Scale { get; set; } = 100;

    /// <summary>Grows on big screens and shrinks on small ones (by the screen's height at its scaling).</summary>
    public bool AutoScale { get; set; } = true;

    /// <summary>The size of each line ("FrameRate", "Graphics", "Processor", "Memory"); missing means Standard.</summary>
    public Dictionary<string, OverlaySize> GroupSizes { get; set; } = [];

    /// <summary>The place for a program: its own when it has one and places are kept per game, else the one for all.</summary>
    public (OverlayPosition Position, int OffsetX, int OffsetY) PlacementFor(string? program)
    {
        if (PerGamePosition && OverlayPlacements.ForProgram(GamePlacements, program) is { } own)
        {
            return (own.Position, own.OffsetX, own.OffsetY);
        }

        return (Position, OffsetX, OffsetY);
    }

    /// <summary>The overlay's scale before the screen's: its size preset, or its own scale.</summary>
    public double SizeFactor => Size == OverlaySize.Custom ? Scale / 100.0 : OverlayPlacements.PresetFactor(Size);

    /// <summary>A line's size relative to the overlay (Compact, Standard or Expanded).</summary>
    public double GroupFactor(OverlayGroup group) =>
        GroupSizes.TryGetValue(group.ToString(), out var size) && size != OverlaySize.Custom ? OverlayPlacements.PresetFactor(size) : 1;

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
        OffsetX = Math.Clamp(OffsetX, -OverlayPlacements.MaxOffset, OverlayPlacements.MaxOffset);
        OffsetY = Math.Clamp(OffsetY, -OverlayPlacements.MaxOffset, OverlayPlacements.MaxOffset);
        GamePlacements = OverlayPlacements.Normalize(GamePlacements);
        if (!Enum.IsDefined(Size))
        {
            Size = OverlaySize.Standard;
        }

        Scale = Math.Clamp(Scale, OverlayPlacements.MinScale, OverlayPlacements.MaxScale);
        GroupSizes = (GroupSizes ?? [])
            .Where(p => Enum.TryParse<OverlayGroup>(p.Key, out _) && Enum.IsDefined(p.Value) && p.Value is not (OverlaySize.Standard or OverlaySize.Custom))
            .ToDictionary(p => p.Key, p => p.Value);
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

/// <summary>
/// Where the overlay goes on a screen and how big it is drawn: nine places (corners, edges and the middle) plus an
/// offset, the place it was dragged to, and its scale. Pixels are the screen's; offsets are kept at 100 % scaling.
/// </summary>
public static class OverlayPlacements
{
    public const int MaxOffset = 4000;
    public const int MinScale = 50;
    public const int MaxScale = 250;
    public const int MaxGames = 200;

    /// <summary>Compact 85 %, Standard 100 %, Expanded 125 %.</summary>
    public static double PresetFactor(OverlaySize size) => size switch
    {
        OverlaySize.Compact => 0.85,
        OverlaySize.Expanded => 1.25,
        _ => 1,
    };

    /// <summary>
    /// The scale for a screen <paramref name="heightDip"/> tall at its own scaling: 1 on a 1080-line screen, up to 1.35
    /// on taller ones (1440p at 100 %) and down to 0.85 on short laptop screens, in steps of 5 %.
    /// </summary>
    public static double ScreenScale(double heightDip)
    {
        if (!double.IsFinite(heightDip) || heightDip <= 0)
        {
            return 1;
        }

        return Math.Round(Math.Clamp(heightDip / 1080.0, 0.85, 1.35) * 20) / 20;
    }

    /// <summary>
    /// The overlay's top-left corner on a screen: its place (<paramref name="margin"/> from the edges) moved by the
    /// offset, kept fully on the screen. All values in the screen's pixels.
    /// </summary>
    public static (int X, int Y) Locate(int left, int top, int screenWidth, int screenHeight, int width, int height,
        OverlayPosition position, int margin, int offsetX, int offsetY)
    {
        int x = Column(position) switch
        {
            0 => left + margin,
            1 => left + ((screenWidth - width) / 2),
            _ => left + screenWidth - margin - width,
        };
        int y = Row(position) switch
        {
            0 => top + margin,
            1 => top + ((screenHeight - height) / 2),
            _ => top + screenHeight - margin - height,
        };
        x = Math.Clamp(x + offsetX, left, Math.Max(left, left + screenWidth - width));
        y = Math.Clamp(y + offsetY, top, Math.Max(top, top + screenHeight - height));
        return (x, y);
    }

    /// <summary>
    /// Where the overlay was dropped (its top-left, in the screen's pixels), as the nearest of the nine places and the
    /// offset from it, so it stays exactly there and keeps its corner on screens of other sizes.
    /// </summary>
    public static (OverlayPosition Position, int OffsetX, int OffsetY) FromDrop(int left, int top, int screenWidth, int screenHeight,
        int x, int y, int width, int height, int margin)
    {
        double centerX = x + (width / 2.0) - left;
        double centerY = y + (height / 2.0) - top;
        int column = centerX < screenWidth / 3.0 ? 0 : centerX < screenWidth * 2 / 3.0 ? 1 : 2;
        int row = centerY < screenHeight / 3.0 ? 0 : centerY < screenHeight * 2 / 3.0 ? 1 : 2;
        var position = At(row, column);
        var (baseX, baseY) = Locate(left, top, screenWidth, screenHeight, width, height, position, margin, 0, 0);
        return (position, x - baseX, y - baseY);
    }

    /// <summary>The place in a row (0 top, 1 middle, 2 bottom) and column (0 left, 1 center, 2 right).</summary>
    public static OverlayPosition At(int row, int column) => (row, column) switch
    {
        (0, 0) => OverlayPosition.TopLeft,
        (0, 1) => OverlayPosition.TopCenter,
        (0, _) => OverlayPosition.TopRight,
        (1, 0) => OverlayPosition.MiddleLeft,
        (1, 1) => OverlayPosition.Center,
        (1, _) => OverlayPosition.MiddleRight,
        (_, 0) => OverlayPosition.BottomLeft,
        (_, 1) => OverlayPosition.BottomCenter,
        _ => OverlayPosition.BottomRight,
    };

    public static int Row(OverlayPosition position) => position switch
    {
        OverlayPosition.TopLeft or OverlayPosition.TopCenter or OverlayPosition.TopRight => 0,
        OverlayPosition.MiddleLeft or OverlayPosition.Center or OverlayPosition.MiddleRight => 1,
        _ => 2,
    };

    public static int Column(OverlayPosition position) => position switch
    {
        OverlayPosition.TopLeft or OverlayPosition.MiddleLeft or OverlayPosition.BottomLeft => 0,
        OverlayPosition.TopCenter or OverlayPosition.Center or OverlayPosition.BottomCenter => 1,
        _ => 2,
    };

    /// <summary>The place kept for a program (by file name, either case), or null.</summary>
    public static OverlayPlacement? ForProgram(IEnumerable<OverlayPlacement> placements, string? program)
    {
        string? name = FileName(program);
        return name is null ? null : placements.FirstOrDefault(p => string.Equals(p.Program, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Keeps one place per program (newest wins), known places only, at most <see cref="MaxGames"/>.</summary>
    public static List<OverlayPlacement> Normalize(List<OverlayPlacement>? placements) =>
        (placements ?? [])
            .Where(p => p is not null && FileName(p.Program) is not null && Enum.IsDefined(p.Position))
            .Select(p => new OverlayPlacement
            {
                Program = FileName(p.Program)!,
                Position = p.Position,
                OffsetX = Math.Clamp(p.OffsetX, -MaxOffset, MaxOffset),
                OffsetY = Math.Clamp(p.OffsetY, -MaxOffset, MaxOffset),
            })
            .Reverse()
            .DistinctBy(p => p.Program, StringComparer.OrdinalIgnoreCase)
            .Reverse()
            .TakeLast(MaxGames)
            .ToList();

    /// <summary>"game.exe" from a name or a path (either slash).</summary>
    public static string? FileName(string? program)
    {
        if (string.IsNullOrWhiteSpace(program))
        {
            return null;
        }

        string name = program.Trim().Trim('"');
        name = name[(name.LastIndexOfAny(['\\', '/']) + 1)..];
        return name.Length == 0 ? null : name;
    }
}

/// <summary>Names and checks for hotkeys, without depending on WPF or WinForms.</summary>
public static class Hotkeys
{
    /// <summary>Hotkeys are written the Mac way (⌃⌥⇧⌘O) instead of the Windows way (Ctrl + Alt + Shift + Win + O).</summary>
    public static bool UseMacNames { get; set; } = OperatingSystem.IsMacOS();

    /// <summary>"Ctrl + Shift + O", "End", or "None"; on a Mac "⌥⌘O" (Windows ↔ Command, Alt ↔ Option).</summary>
    public static string Describe(int key, HotkeyModifiers modifiers) => Describe(key, modifiers, UseMacNames);

    public static string Describe(int key, HotkeyModifiers modifiers, bool mac)
    {
        if (key == 0)
        {
            return "None";
        }

        if (mac)
        {
            // The order Mac menus use: Control, Option, Shift, Command.
            string symbols = (modifiers.HasFlag(HotkeyModifiers.Control) ? "⌃" : string.Empty)
                + (modifiers.HasFlag(HotkeyModifiers.Alt) ? "⌥" : string.Empty)
                + (modifiers.HasFlag(HotkeyModifiers.Shift) ? "⇧" : string.Empty)
                + (modifiers.HasFlag(HotkeyModifiers.Windows) ? "⌘" : string.Empty);
            return symbols + MacKeyName(key);
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

    /// <summary>A key's name on a Mac keyboard (the Windows names for the rest).</summary>
    public static string MacKeyName(int key) => key switch
    {
        0x08 => "⌫",
        0x09 => "⇥",
        0x0D => "↩",
        0x1B => "⎋",
        0x20 => "Space",
        0x21 => "⇞",
        0x22 => "⇟",
        0x23 => "↘",
        0x24 => "↖",
        0x25 => "←",
        0x26 => "↑",
        0x27 => "→",
        0x28 => "↓",
        0x2D => "Help",
        0x2E => "⌦",
        _ => KeyName(key),
    };

    /// <summary>
    /// The Mac keyboard's key code (kVK_…) for a Windows virtual-key code, as Carbon's RegisterEventHotKey takes it; -1 for
    /// keys a Mac keyboard doesn't have (Print Screen, Scroll Lock, Pause…).
    /// </summary>
    public static int MacKeyCode(int key) => key switch
    {
        >= 0x41 and <= 0x5A => MacLetters[key - 0x41],
        >= 0x30 and <= 0x39 => MacDigits[key - 0x30],
        >= 0x60 and <= 0x69 => MacKeypadDigits[key - 0x60],
        >= 0x70 and <= 0x83 => MacFunctionKeys[key - 0x70],
        0x08 => 0x33,
        0x09 => 0x30,
        0x0D => 0x24,
        0x1B => 0x35,
        0x20 => 0x31,
        0x21 => 0x74,
        0x22 => 0x79,
        0x23 => 0x77,
        0x24 => 0x73,
        0x25 => 0x7B,
        0x26 => 0x7E,
        0x27 => 0x7C,
        0x28 => 0x7D,
        0x2D => 0x72,
        0x2E => 0x75,
        0x6A => 0x43,
        0x6B => 0x45,
        0x6D => 0x4E,
        0x6E => 0x41,
        0x6F => 0x4B,
        0xBA => 0x29,
        0xBB => 0x18,
        0xBC => 0x2B,
        0xBD => 0x1B,
        0xBE => 0x2F,
        0xBF => 0x2C,
        0xC0 => 0x32,
        0xDB => 0x21,
        0xDC => 0x2A,
        0xDD => 0x1E,
        0xDE => 0x27,
        _ => -1,
    };

    /// <summary>Carbon's modifier flags (cmdKey, shiftKey, optionKey, controlKey) for a hotkey's modifiers.</summary>
    public static uint MacModifiers(HotkeyModifiers modifiers) =>
        (modifiers.HasFlag(HotkeyModifiers.Windows) ? 0x0100u : 0)
        | (modifiers.HasFlag(HotkeyModifiers.Shift) ? 0x0200u : 0)
        | (modifiers.HasFlag(HotkeyModifiers.Alt) ? 0x0800u : 0)
        | (modifiers.HasFlag(HotkeyModifiers.Control) ? 0x1000u : 0);

    // kVK_ANSI_A … kVK_ANSI_Z, kVK_ANSI_0 … kVK_ANSI_9, kVK_ANSI_Keypad0 … 9 and kVK_F1 … kVK_F20 (Events.h).
    private static readonly int[] MacLetters =
        [0x00, 0x0B, 0x08, 0x02, 0x0E, 0x03, 0x05, 0x04, 0x22, 0x26, 0x28, 0x25, 0x2E, 0x2D, 0x1F, 0x23, 0x0C, 0x0F, 0x01, 0x11, 0x20, 0x09, 0x0D, 0x07, 0x10, 0x06];

    private static readonly int[] MacDigits = [0x1D, 0x12, 0x13, 0x14, 0x15, 0x17, 0x16, 0x1A, 0x1C, 0x19];

    private static readonly int[] MacKeypadDigits = [0x52, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58, 0x59, 0x5B, 0x5C];

    private static readonly int[] MacFunctionKeys =
        [0x7A, 0x78, 0x63, 0x76, 0x60, 0x61, 0x62, 0x64, 0x65, 0x6D, 0x67, 0x6F, 0x69, 0x6B, 0x71, 0x6A, 0x40, 0x4F, 0x50, 0x5A];
}
