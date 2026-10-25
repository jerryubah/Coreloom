using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Coreloom.Core.Diagnostics;
using Coreloom.Hardware.Mac.Native;
using Coreloom.Mac.Native;

namespace Coreloom.Mac.Services;

/// <summary>
/// Notifications through macOS's Notification Center (UNUserNotificationCenter): alerts, update news and "still running
/// in the menu bar". They also show while Coreloom is in front, and clicking one runs what it was posted with (opening
/// the window, or Settings › Updates). macOS asks the user once whether Coreloom may notify. A copy run outside its app
/// bundle has no identity for Notification Center and falls back to AppleScript's notifications.
/// </summary>
public static unsafe partial class MacNotifications
{
    private const ulong AuthorizationAlertAndSound = 4 | 2;
    private const ulong PresentBannerListSound = (1 << 4) | (1 << 3) | (1 << 1);
    private static readonly ConcurrentDictionary<string, Action> Actions = new(StringComparer.Ordinal);
    private static nint _center;
    private static nint _delegate;
    private static bool _initialized;
    private static Action<Action>? _dispatch;

    [LibraryImport(Libraries.ObjC, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint objc_allocateClassPair(nint superclass, string name, nint extraBytes);

    [LibraryImport(Libraries.ObjC)]
    private static partial void objc_registerClassPair(nint cls);

    [LibraryImport(Libraries.ObjC, StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool class_addMethod(nint cls, nint selector, nint implementation, string types);

    [LibraryImport(Libraries.ObjC, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint objc_getProtocol(string name);

    [LibraryImport(Libraries.ObjC)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool class_addProtocol(nint cls, nint protocol);

    [LibraryImport(Libraries.ObjC, EntryPoint = "objc_msgSend")]
    private static partial void SendAuthorization(nint receiver, nint selector, ulong options, nint block);

    /// <summary>Sets up Notification Center (asks for permission the first time). <paramref name="dispatch"/> runs work on the UI thread.</summary>
    public static void Initialize(Action<Action> dispatch)
    {
        _dispatch = dispatch;
        if (_initialized || MacLifetimeService.BundlePath is null)
        {
            return;
        }

        _initialized = true;
        try
        {
            NativeLibrary.Load("/System/Library/Frameworks/UserNotifications.framework/UserNotifications");
            using var pool = new ObjC.Pool();
            _center = ObjC.Send(ObjC.Send(ObjC.Class("UNUserNotificationCenter"), ObjC.Sel("currentNotificationCenter")), ObjC.Sel("retain"));
            if (_center == 0)
            {
                return;
            }

            _delegate = CreateDelegate();
            if (_delegate != 0)
            {
                ObjC.SendVoid(_center, ObjC.Sel("setDelegate:"), _delegate);
            }

            SendAuthorization(_center, ObjC.Sel("requestAuthorizationWithOptions:completionHandler:"), AuthorizationAlertAndSound,
                ObjC.GlobalBlock((nint)(delegate* unmanaged<nint, byte, nint, void>)&OnAuthorized));
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            Log.Warn("Notifications", "Notification Center isn't available.", e);
            _center = 0;
        }
    }

    /// <summary>Posts a notification; <paramref name="onClick"/> runs (on the UI thread) when the user clicks it.</summary>
    public static void Show(string title, string message, Action? onClick = null, bool sound = false)
    {
        if (_center == 0)
        {
            ShowWithAppleScript(title, message);
            return;
        }

        string id = Guid.NewGuid().ToString("N");
        if (onClick is not null)
        {
            Actions[id] = onClick;
        }

        using var pool = new ObjC.Pool();
        nint content = ObjC.Send(ObjC.Send(ObjC.Class("UNMutableNotificationContent"), ObjC.Sel("alloc")), ObjC.Sel("init"));
        ObjC.SendVoid(content, ObjC.Sel("setTitle:"), ObjC.NSString(title));
        ObjC.SendVoid(content, ObjC.Sel("setBody:"), ObjC.NSString(message));
        if (sound)
        {
            ObjC.SendVoid(content, ObjC.Sel("setSound:"), ObjC.Send(ObjC.Class("UNNotificationSound"), ObjC.Sel("defaultSound")));
        }

        nint request = ObjC.Send(ObjC.Class("UNNotificationRequest"), ObjC.Sel("requestWithIdentifier:content:trigger:"), ObjC.NSString(id), content, 0);
        ObjC.SendVoid(_center, ObjC.Sel("addNotificationRequest:withCompletionHandler:"), request, 0);
        ObjC.Release(content);
    }

    private static void ShowWithAppleScript(string title, string message) =>
        Commands.Start("/usr/bin/osascript", "-e", $"display notification {Commands.AppleScriptString(message)} with title {Commands.AppleScriptString(title)}");

    [UnmanagedCallersOnly]
    private static void OnAuthorized(nint block, byte granted, nint error)
    {
        Log.Info("Notifications", granted != 0 ? "Notifications are allowed." : "Notifications aren't allowed (System Settings › Notifications › Coreloom).");
    }

    /// <summary>A UNUserNotificationCenterDelegate made at run time: banners while Coreloom is in front, and clicks.</summary>
    private static nint CreateDelegate()
    {
        nint cls = ObjC.Class("CoreloomNotificationDelegate");
        if (cls == 0)
        {
            cls = objc_allocateClassPair(ObjC.Class("NSObject"), "CoreloomNotificationDelegate", 0);
            if (cls == 0)
            {
                return 0;
            }

            class_addMethod(cls, ObjC.Sel("userNotificationCenter:willPresentNotification:withCompletionHandler:"),
                (nint)(delegate* unmanaged<nint, nint, nint, nint, nint, void>)&WillPresent, "v@:@@@?");
            class_addMethod(cls, ObjC.Sel("userNotificationCenter:didReceiveNotificationResponse:withCompletionHandler:"),
                (nint)(delegate* unmanaged<nint, nint, nint, nint, nint, void>)&DidReceive, "v@:@@@?");
            nint protocol = objc_getProtocol("UNUserNotificationCenterDelegate");
            if (protocol != 0)
            {
                class_addProtocol(cls, protocol);
            }

            objc_registerClassPair(cls);
        }

        return ObjC.Send(ObjC.Send(cls, ObjC.Sel("alloc")), ObjC.Sel("init"));
    }

    [UnmanagedCallersOnly]
    private static void WillPresent(nint self, nint selector, nint center, nint notification, nint completion)
    {
        if (completion != 0)
        {
            ((delegate* unmanaged<nint, ulong, void>)*(nint*)(completion + 16))(completion, PresentBannerListSound);
        }
    }

    [UnmanagedCallersOnly]
    private static void DidReceive(nint self, nint selector, nint center, nint response, nint completion)
    {
        try
        {
            nint request = ObjC.Send(ObjC.Send(response, ObjC.Sel("notification")), ObjC.Sel("request"));
            string? id = ObjC.ToStringValue(ObjC.Send(request, ObjC.Sel("identifier")));
            if (id is not null && Actions.TryRemove(id, out var action))
            {
                _dispatch?.Invoke(action);
            }
        }
        catch (Exception e)
        {
            Log.Warn("Notifications", "Handling a notification click failed.", e);
        }
        finally
        {
            if (completion != 0)
            {
                ((delegate* unmanaged<nint, void>)*(nint*)(completion + 16))(completion);
            }
        }
    }
}
