using System.Runtime.InteropServices;
using Coreloom.Core.Diagnostics;
using Coreloom.Core.Settings;

namespace Coreloom.Mac.Overlay;

/// <summary>
/// The readings window's hotkey, also inside full-screen games: registered with Carbon's RegisterEventHotKey, which needs
/// no Accessibility or Input Monitoring permission because macOS only tells Coreloom when that exact key combination is
/// pressed (Coreloom never sees any other key). The hotkey shows or hides the window; Shift + the hotkey lets it be moved.
/// </summary>
internal sealed unsafe partial class GlobalHotkey : IDisposable
{
    private const string Carbon = "/System/Library/Frameworks/Carbon.framework/Carbon";
    private const uint Signature = 0x436F726C; // 'Corl'
    private const uint ClassKeyboard = 0x6B657962; // 'keyb'
    private const uint HotKeyPressed = 5;
    private const uint ParamDirectObject = 0x2D2D2D2D; // '----'
    private const uint TypeHotKeyId = 0x686B6964; // 'hkid'
    private const uint ToggleId = 1;
    private const uint MoveId = 2;

    private static GlobalHotkey? _current;
    private static nint _handler;

    private readonly Action _toggle;
    private readonly Action _move;
    private nint _toggleRef;
    private nint _moveRef;
    private (int Key, HotkeyModifiers Modifiers) _registered;

    public GlobalHotkey(Action toggle, Action move)
    {
        _toggle = toggle;
        _move = move;
        _current = this;
    }

    /// <summary>While the settings wait for a new hotkey, pressing the old one mustn't switch the window.</summary>
    public bool Paused { get; set; }

    /// <summary>Registers the hotkey (a Windows virtual-key code and modifiers, as saved in the settings); 0 removes it.</summary>
    public void Set(int key, HotkeyModifiers modifiers)
    {
        if ((key, modifiers) == _registered)
        {
            return;
        }

        Unregister();
        _registered = (key, modifiers);
        int code = key == 0 ? -1 : Hotkeys.MacKeyCode(key);
        if (code < 0)
        {
            if (key != 0)
            {
                Log.Warn("Overlay", $"{Hotkeys.Describe(key, modifiers)} isn't a key on a Mac keyboard; the readings window has no hotkey.");
            }

            return;
        }

        try
        {
            if (!EnsureHandler())
            {
                return;
            }

            nint target = GetApplicationEventTarget();
            _toggleRef = Register((uint)code, Hotkeys.MacModifiers(modifiers), ToggleId, target);
            if (!modifiers.HasFlag(HotkeyModifiers.Shift))
            {
                _moveRef = Register((uint)code, Hotkeys.MacModifiers(modifiers | HotkeyModifiers.Shift), MoveId, target);
            }

            Log.Info("Overlay", _toggleRef != 0 ? $"Hotkey {Hotkeys.Describe(key, modifiers)} registered." : $"Hotkey {Hotkeys.Describe(key, modifiers)} is taken by another app.");
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            Log.Warn("Overlay", "Global hotkeys aren't available.", e);
        }
    }

    public void Dispose()
    {
        Unregister();
        if (ReferenceEquals(_current, this))
        {
            _current = null;
        }
    }

    private static nint Register(uint code, uint modifiers, uint id, nint target)
    {
        nint reference;
        int status = RegisterEventHotKey(code, modifiers, new HotKeyId { Signature = Signature, Id = id }, target, 0, &reference);
        return status == 0 ? reference : 0;
    }

    private void Unregister()
    {
        try
        {
            if (_toggleRef != 0)
            {
                _ = UnregisterEventHotKey(_toggleRef);
            }

            if (_moveRef != 0)
            {
                _ = UnregisterEventHotKey(_moveRef);
            }
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            // nothing was registered
        }

        _toggleRef = 0;
        _moveRef = 0;
        _registered = default;
    }

    private static bool EnsureHandler()
    {
        if (_handler != 0)
        {
            return true;
        }

        var spec = new EventTypeSpec { EventClass = ClassKeyboard, EventKind = HotKeyPressed };
        nint handler;
        int status = InstallEventHandler(GetApplicationEventTarget(), (nint)(delegate* unmanaged<nint, nint, nint, int>)&OnHotKey, 1, &spec, 0, &handler);
        if (status != 0)
        {
            Log.Warn("Overlay", $"Installing the hotkey handler failed ({status}).");
            return false;
        }

        _handler = handler;
        return true;
    }

    [UnmanagedCallersOnly]
    private static int OnHotKey(nint callRef, nint eventRef, nint userData)
    {
        try
        {
            HotKeyId id;
            if (GetEventParameter(eventRef, ParamDirectObject, TypeHotKeyId, null, (nuint)sizeof(HotKeyId), null, &id) != 0 || id.Signature != Signature)
            {
                return -9874; // eventNotHandledErr
            }

            var current = _current;
            if (current is null || current.Paused)
            {
                return 0;
            }

            (id.Id == MoveId ? current._move : current._toggle)();
            return 0;
        }
        catch (Exception e)
        {
            Log.Error("Overlay", "Handling the hotkey failed.", e);
            return 0;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HotKeyId
    {
        public uint Signature;
        public uint Id;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EventTypeSpec
    {
        public uint EventClass;
        public uint EventKind;
    }

    [LibraryImport(Carbon)]
    private static partial nint GetApplicationEventTarget();

    [LibraryImport(Carbon)]
    private static partial int RegisterEventHotKey(uint keyCode, uint modifiers, HotKeyId id, nint target, uint options, nint* reference);

    [LibraryImport(Carbon)]
    private static partial int UnregisterEventHotKey(nint reference);

    [LibraryImport(Carbon)]
    private static partial int InstallEventHandler(nint target, nint handler, nuint typeCount, EventTypeSpec* types, nint userData, nint* reference);

    [LibraryImport(Carbon)]
    private static partial int GetEventParameter(nint eventRef, uint name, uint desiredType, uint* actualType, nuint bufferSize, nuint* actualSize, void* data);
}
