using System.Runtime.InteropServices;
using Coreloom.Hardware.Mac.Native;

namespace Coreloom.Mac.Overlay;

/// <summary>The few AppKit calls the readings window needs: floating over every Space (full-screen games included),
/// letting clicks through, and its place on the screen in front.</summary>
internal static partial class OverlayNative
{
    private const string ObjCLibrary = "/usr/lib/libobjc.A.dylib";

    // NSWindowCollectionBehavior: CanJoinAllSpaces | Stationary | IgnoresCycle | FullScreenAuxiliary.
    private const ulong CollectionBehavior = 1 | 16 | 64 | 256;

    /// <summary>NSStatusWindowLevel: above apps (and full-screen games), below the system's own alerts.</summary>
    private const long StatusWindowLevel = 25;

    [StructLayout(LayoutKind.Sequential)]
    public struct NSPoint
    {
        public double X;
        public double Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NSRect
    {
        public double X;
        public double Y;
        public double Width;
        public double Height;
    }

    /// <summary>Makes the window float over every Space without a shadow, out of the window cycle, and lets clicks through.</summary>
    public static void Configure(nint window, bool clickThrough)
    {
        if (window == 0)
        {
            return;
        }

        using var pool = new ObjC.Pool();
        ObjC.SendVoidLong(window, ObjC.Sel("setCollectionBehavior:"), (long)CollectionBehavior);
        if (ObjC.SendLongResult(window, ObjC.Sel("level")) != StatusWindowLevel)
        {
            ObjC.SendVoidLong(window, ObjC.Sel("setLevel:"), StatusWindowLevel);
        }

        ObjC.SendVoidBool(window, ObjC.Sel("setHasShadow:"), false);
        ObjC.SendVoidBool(window, ObjC.Sel("setIgnoresMouseEvents:"), clickThrough);
    }

    /// <summary>Puts the level back if something lowered it (it is checked every tick, so it costs one call).</summary>
    public static void KeepOnTop(nint window)
    {
        if (window != 0 && ObjC.SendLongResult(window, ObjC.Sel("level")) != StatusWindowLevel)
        {
            ObjC.SendVoidLong(window, ObjC.Sel("setLevel:"), StatusWindowLevel);
        }
    }

    public static void SetClickThrough(nint window, bool clickThrough)
    {
        if (window != 0)
        {
            ObjC.SendVoidBool(window, ObjC.Sel("setIgnoresMouseEvents:"), clickThrough);
        }
    }

    /// <summary>The usable part (no menu bar or Dock) of the screen with the app in front, in points, Cocoa's bottom-left origin.</summary>
    public static NSRect? FrontScreen()
    {
        using var pool = new ObjC.Pool();
        nint screen = ObjC.Send(ObjC.Class("NSScreen"), ObjC.Sel("mainScreen"));
        return screen == 0 ? null : Rect(screen, "visibleFrame");
    }

    /// <summary>The screen the window is on (mostly), or null.</summary>
    public static NSRect? ScreenOf(nint window)
    {
        if (window == 0)
        {
            return null;
        }

        using var pool = new ObjC.Pool();
        nint screen = ObjC.Send(window, ObjC.Sel("screen"));
        return screen == 0 ? null : Rect(screen, "visibleFrame");
    }

    public static NSRect Frame(nint window) => Rect(window, "frame");

    public static void SetOrigin(nint window, double x, double y)
    {
        if (window != 0)
        {
            SendPoint(window, ObjC.Sel("setFrameOrigin:"), new NSPoint { X = x, Y = y });
        }
    }

    // A struct of four doubles comes back in registers on Apple silicon; on Intel it is written through a hidden pointer,
    // which objc_msgSend_stret expects.
    private static NSRect Rect(nint receiver, string selector) =>
        RuntimeInformation.ProcessArchitecture == Architecture.Arm64
            ? SendRect(receiver, ObjC.Sel(selector))
            : SendRectStret(receiver, ObjC.Sel(selector));

    [LibraryImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    private static partial NSRect SendRect(nint receiver, nint selector);

    [LibraryImport(ObjCLibrary, EntryPoint = "objc_msgSend_stret")]
    private static partial NSRect SendRectStret(nint receiver, nint selector);

    [LibraryImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    private static partial void SendPoint(nint receiver, nint selector, NSPoint point);
}
