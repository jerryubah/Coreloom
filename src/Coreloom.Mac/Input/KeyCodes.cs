using Avalonia.Input;
using Coreloom.Core.Settings;

namespace Coreloom.Mac.Input;

/// <summary>Avalonia's keys as the Windows virtual-key codes hotkeys are saved with (the settings are shared with Windows).</summary>
public static class KeyCodes
{
    public static int VirtualKey(Key key) => key switch
    {
        >= Key.A and <= Key.Z => 0x41 + (key - Key.A),
        >= Key.D0 and <= Key.D9 => 0x30 + (key - Key.D0),
        >= Key.NumPad0 and <= Key.NumPad9 => 0x60 + (key - Key.NumPad0),
        >= Key.F1 and <= Key.F24 => 0x70 + (key - Key.F1),
        Key.Back => 0x08,
        Key.Tab => 0x09,
        Key.Enter => 0x0D,
        Key.Escape => 0x1B,
        Key.Space => 0x20,
        Key.PageUp => 0x21,
        Key.PageDown => 0x22,
        Key.End => 0x23,
        Key.Home => 0x24,
        Key.Left => 0x25,
        Key.Up => 0x26,
        Key.Right => 0x27,
        Key.Down => 0x28,
        Key.Insert or Key.Help => 0x2D,
        Key.Delete => 0x2E,
        Key.Multiply => 0x6A,
        Key.Add => 0x6B,
        Key.Subtract => 0x6D,
        Key.Decimal => 0x6E,
        Key.Divide => 0x6F,
        Key.OemSemicolon => 0xBA,
        Key.OemPlus => 0xBB,
        Key.OemComma => 0xBC,
        Key.OemMinus => 0xBD,
        Key.OemPeriod => 0xBE,
        Key.OemQuestion => 0xBF,
        Key.OemTilde => 0xC0,
        Key.OemOpenBrackets => 0xDB,
        Key.OemPipe => 0xDC,
        Key.OemCloseBrackets => 0xDD,
        Key.OemQuotes => 0xDE,
        Key.LeftShift or Key.RightShift => 0x10,
        Key.LeftCtrl or Key.RightCtrl => 0x11,
        Key.LeftAlt or Key.RightAlt => 0x12,
        Key.LWin or Key.RWin => 0x5B,
        _ => 0,
    };

    /// <summary>⌘ is saved as the Windows key, ⌥ as Alt.</summary>
    public static HotkeyModifiers Modifiers(KeyModifiers modifiers) =>
        (modifiers.HasFlag(KeyModifiers.Control) ? HotkeyModifiers.Control : HotkeyModifiers.None)
        | (modifiers.HasFlag(KeyModifiers.Alt) ? HotkeyModifiers.Alt : HotkeyModifiers.None)
        | (modifiers.HasFlag(KeyModifiers.Shift) ? HotkeyModifiers.Shift : HotkeyModifiers.None)
        | (modifiers.HasFlag(KeyModifiers.Meta) ? HotkeyModifiers.Windows : HotkeyModifiers.None);
}
