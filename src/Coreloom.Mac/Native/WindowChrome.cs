using System.Runtime.InteropServices;
using Coreloom.Hardware.Mac.Native;

namespace Coreloom.Mac.Native;

/// <summary>
/// A look at the window's title bar from AppKit's side: where macOS put the close, minimize and zoom buttons, and which
/// view a click at a given point would go to (Coreloom's own view, or the title bar's). Logged once after the window
/// opens, so a Mac's log shows the buttons sit inside the sidebar's glass and that the controls near the top still get
/// their clicks.
/// </summary>
internal static partial class WindowChrome
{
    private const string ObjCLibrary = "/usr/lib/libobjc.A.dylib";

    [StructLayout(LayoutKind.Sequential)]
    private struct NSPoint
    {
        public double X;
        public double Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NSRect
    {
        public double X;
        public double Y;
        public double Width;
        public double Height;
    }

    /// <summary>The window buttons' frames (top-left origin, points) and what each point would hit: "name (x, y) → class".</summary>
    public static (string Report, bool AllReachCoreloom) Describe(nint window, IEnumerable<(string Name, double X, double Y, bool MustReach)> points)
    {
        if (window == 0)
        {
            return ("no window", true);
        }

        using var pool = new ObjC.Pool();
        var frame = Rect(window, "frame");
        nint content = ObjC.Send(window, ObjC.Sel("contentView"));
        nint theme = content == 0 ? 0 : ObjC.Send(content, ObjC.Sel("superview"));
        var parts = new List<string>();
        string[] names = ["close", "minimize", "zoom"];
        for (int i = 0; i < 3; i++)
        {
            nint button = ObjC.SendLong(window, ObjC.Sel("standardWindowButton:"), i);
            if (button == 0)
            {
                continue;
            }

            var bounds = Rect(button, "bounds");
            var bottomLeft = Convert(button, new NSPoint { X = 0, Y = 0 });
            var topRight = Convert(button, new NSPoint { X = bounds.Width, Y = bounds.Height });
            double left = Math.Min(bottomLeft.X, topRight.X);
            double top = frame.Height - Math.Max(bottomLeft.Y, topRight.Y);
            parts.Add(FormattableString.Invariant($"{names[i]} {left:0.#},{top:0.#} {bounds.Width:0.#}×{bounds.Height:0.#}"));
        }

        bool reach = true;
        if (theme != 0)
        {
            foreach (var (name, x, y, mustReach) in points)
            {
                nint hit = SendHit(theme, ObjC.Sel("hitTest:"), new NSPoint { X = x, Y = frame.Height - y });
                string cls = hit == 0 ? "nothing" : ObjC.ToStringValue(NSStringFromClass(ObjC.Send(hit, ObjC.Sel("class")))) ?? "?";
                bool ours = cls.StartsWith("Avn", StringComparison.Ordinal) || cls.Contains("Avalonia", StringComparison.Ordinal);
                reach &= ours || !mustReach;
                parts.Add(FormattableString.Invariant($"{name} ({x:0},{y:0}) → {cls}"));
            }
        }

        return (string.Join("; ", parts), reach);
    }

    private static NSPoint Convert(nint view, NSPoint point) => SendConvert(view, ObjC.Sel("convertPoint:toView:"), point, 0);

    // A struct of four doubles comes back in registers on Apple silicon; on Intel it is written through a hidden pointer.
    private static NSRect Rect(nint receiver, string selector) =>
        RuntimeInformation.ProcessArchitecture == Architecture.Arm64
            ? SendRect(receiver, ObjC.Sel(selector))
            : SendRectStret(receiver, ObjC.Sel(selector));

    [LibraryImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    private static partial NSRect SendRect(nint receiver, nint selector);

    [LibraryImport(ObjCLibrary, EntryPoint = "objc_msgSend_stret")]
    private static partial NSRect SendRectStret(nint receiver, nint selector);

    [LibraryImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    private static partial NSPoint SendConvert(nint receiver, nint selector, NSPoint point, nint view);

    [LibraryImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    private static partial nint SendHit(nint receiver, nint selector, NSPoint point);

    [LibraryImport("/System/Library/Frameworks/Foundation.framework/Foundation")]
    private static partial nint NSStringFromClass(nint cls);
}
