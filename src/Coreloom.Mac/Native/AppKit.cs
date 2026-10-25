using System.Runtime.InteropServices;
using Coreloom.Hardware.Mac.Native;

namespace Coreloom.Mac.Native;

/// <summary>
/// The AppKit pieces Coreloom uses directly: alerts, save and open panels, the pasteboard, the screen's refresh rate and
/// the application's activation and hiding. Call on the main thread (Avalonia's UI thread on macOS).
/// </summary>
internal static class AppKit
{
    private const long AlertFirstButton = 1000;
    private const long ModalResponseOk = 1;

    static AppKit() => NativeLibrary.TryLoad("/System/Library/Frameworks/AppKit.framework/AppKit", out _);

    public static nint App => ObjC.Send(ObjC.Class("NSApplication"), ObjC.Sel("sharedApplication"));

    public static void Activate() => ObjC.SendVoidBool(App, ObjC.Sel("activateIgnoringOtherApps:"), true);

    public static void Hide() => ObjC.SendVoid(App, ObjC.Sel("hide:"), 0);

    public static void HideOthers() => ObjC.SendVoid(App, ObjC.Sel("hideOtherApplications:"), 0);

    public static void UnhideAll() => ObjC.SendVoid(App, ObjC.Sel("unhideAllApplications:"), 0);

    public static void OrderFrontAbout() => ObjC.SendVoid(App, ObjC.Sel("orderFrontStandardAboutPanel:"), 0);

    /// <summary>"Regular" (in the Dock, with a menu bar) or "accessory" (menu bar item only): NSApplicationActivationPolicy.</summary>
    public static void SetShowInDock(bool show) => ObjC.SendBool(App, ObjC.Sel("setActivationPolicy:"), show ? 0 : 1);

    /// <summary>Shows a message with an OK button (and Cancel when <paramref name="confirm"/>); true for OK.</summary>
    public static bool Alert(string title, string message, bool confirm, bool warning = false, string ok = "OK", string cancel = "Cancel")
    {
        using var pool = new ObjC.Pool();
        nint alert = ObjC.Send(ObjC.Send(ObjC.Class("NSAlert"), ObjC.Sel("alloc")), ObjC.Sel("init"));
        ObjC.SendVoid(alert, ObjC.Sel("setMessageText:"), ObjC.NSString(title));
        ObjC.SendVoid(alert, ObjC.Sel("setInformativeText:"), ObjC.NSString(message));
        ObjC.SendVoidLong(alert, ObjC.Sel("setAlertStyle:"), warning ? 0 : 1); // warning, informational
        ObjC.Send(alert, ObjC.Sel("addButtonWithTitle:"), ObjC.NSString(ok));
        if (confirm)
        {
            ObjC.Send(alert, ObjC.Sel("addButtonWithTitle:"), ObjC.NSString(cancel));
        }

        Activate();
        long response = ObjC.SendLongResult(alert, ObjC.Sel("runModal"));
        ObjC.Release(alert);
        return response == AlertFirstButton;
    }

    /// <summary>A save panel; returns the chosen path or null.</summary>
    public static string? SavePanel(string suggestedName, IReadOnlyList<string> extensions, string? directory)
    {
        using var pool = new ObjC.Pool();
        nint panel = ObjC.Send(ObjC.Class("NSSavePanel"), ObjC.Sel("savePanel"));
        ObjC.SendVoid(panel, ObjC.Sel("setNameFieldStringValue:"), ObjC.NSString(suggestedName));
        ObjC.SendVoidBool(panel, ObjC.Sel("setCanCreateDirectories:"), true);
        Configure(panel, extensions, directory);
        Activate();
        return ObjC.SendLongResult(panel, ObjC.Sel("runModal")) == ModalResponseOk ? PathOf(ObjC.Send(panel, ObjC.Sel("URL"))) : null;
    }

    /// <summary>An open panel for one file; returns the chosen path or null.</summary>
    public static string? OpenPanel(string title, IReadOnlyList<string> extensions, string? directory)
    {
        using var pool = new ObjC.Pool();
        nint panel = ObjC.Send(ObjC.Class("NSOpenPanel"), ObjC.Sel("openPanel"));
        ObjC.SendVoid(panel, ObjC.Sel("setMessage:"), ObjC.NSString(title));
        ObjC.SendVoidBool(panel, ObjC.Sel("setCanChooseFiles:"), true);
        ObjC.SendVoidBool(panel, ObjC.Sel("setCanChooseDirectories:"), false);
        ObjC.SendVoidBool(panel, ObjC.Sel("setAllowsMultipleSelection:"), false);

        // App bundles are folders: let them be picked as files.
        ObjC.SendVoidBool(panel, ObjC.Sel("setTreatsFilePackagesAsDirectories:"), false);
        Configure(panel, extensions, directory);
        Activate();
        return ObjC.SendLongResult(panel, ObjC.Sel("runModal")) == ModalResponseOk ? PathOf(ObjC.Send(panel, ObjC.Sel("URL"))) : null;
    }

    private static void Configure(nint panel, IReadOnlyList<string> extensions, string? directory)
    {
        if (extensions.Count > 0 && !extensions.Contains("*"))
        {
            nint types = ObjC.Send(ObjC.Class("NSMutableArray"), ObjC.Sel("array"));
            foreach (string extension in extensions)
            {
                ObjC.SendVoid(types, ObjC.Sel("addObject:"), ObjC.NSString(extension));
            }

            ObjC.SendVoid(panel, ObjC.Sel("setAllowedFileTypes:"), types);
        }

        if (directory is { Length: > 0 } && Directory.Exists(directory))
        {
            ObjC.SendVoid(panel, ObjC.Sel("setDirectoryURL:"), ObjC.Send(ObjC.Class("NSURL"), ObjC.Sel("fileURLWithPath:"), ObjC.NSString(directory)));
        }
    }

    private static string? PathOf(nint url) => url == 0 ? null : ObjC.ToStringValue(ObjC.Send(url, ObjC.Sel("path")));

    /// <summary>"Text (*.txt)|*.txt|All files|*.*" → ["txt", "*"].</summary>
    public static IReadOnlyList<string> Extensions(string filter)
    {
        var result = new List<string>();
        string[] parts = filter.Split('|');
        for (int i = 1; i < parts.Length; i += 2)
        {
            foreach (string pattern in parts[i].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string extension = pattern.StartsWith("*.", StringComparison.Ordinal) ? pattern[2..] : pattern;
                if (extension is "*" or "*.*")
                {
                    extension = "*";
                }

                if (extension.Length > 0 && !result.Contains(extension, StringComparer.OrdinalIgnoreCase))
                {
                    result.Add(extension);
                }
            }
        }

        return result;
    }

    public static void CopyText(string text)
    {
        using var pool = new ObjC.Pool();
        nint board = ObjC.Send(ObjC.Class("NSPasteboard"), ObjC.Sel("generalPasteboard"));
        ObjC.SendLongResult(board, ObjC.Sel("clearContents"));
        ObjC.Send(board, ObjC.Sel("setString:forType:"), ObjC.NSString(text), ObjC.NSString("public.utf8-plain-text"));
    }

    /// <summary>The main display's refresh rate in Hz (ProMotion displays report 120), or 0.</summary>
    public static int RefreshRate()
    {
        using var pool = new ObjC.Pool();
        nint screen = ObjC.Send(ObjC.Class("NSScreen"), ObjC.Sel("mainScreen"));
        return screen == 0 ? 0 : (int)ObjC.SendLongResult(screen, ObjC.Sel("maximumFramesPerSecond"));
    }

    /// <summary>The bundle path of the app in front ("/Applications/Safari.app"), or null.</summary>
    public static (string? BundlePath, int ProcessId) FrontmostApplication()
    {
        using var pool = new ObjC.Pool();
        nint workspace = ObjC.Send(ObjC.Class("NSWorkspace"), ObjC.Sel("sharedWorkspace"));
        nint app = ObjC.Send(workspace, ObjC.Sel("frontmostApplication"));
        if (app == 0)
        {
            return (null, 0);
        }

        string? path = PathOf(ObjC.Send(app, ObjC.Sel("bundleURL")));
        int pid = (int)ObjC.SendLongResult(app, ObjC.Sel("processIdentifier"));
        return (path, pid);
    }
}
