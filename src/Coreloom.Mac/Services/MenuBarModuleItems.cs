using System.Runtime.InteropServices;
using Coreloom.Core.Diagnostics;
using Coreloom.Hardware.Mac.Native;
using Coreloom.Mac.Controls;
using Coreloom.ViewModels.MenuBar;

namespace Coreloom.Mac.Services;

/// <summary>
/// The modules turned on in Settings › Menu bar, each as its own item in the menu bar (an NSStatusItem as wide as its
/// reading, its image a template that macOS colors for light and dark menu bars). Clicking one shows a short menu: Open
/// Coreloom, Menu Bar Settings…, Quit. People can ⌘-drag the items into any order, as with any menu bar item.
/// Everything runs on the main thread (Avalonia's UI thread on a Mac).
/// </summary>
public sealed unsafe partial class MenuBarModuleItems : IDisposable
{
    private const double VariableLength = -1;
    private const long OpenTag = 1;
    private const long SettingsTag = 2;
    private const long QuitTag = 3;

    private static MenuBarModuleItems? _current;
    private readonly MenuBarModulesViewModel _modules;
    private readonly Action _open;
    private readonly Action _openSettings;
    private readonly Action _quit;
    private readonly Dictionary<string, Item> _items = new(StringComparer.Ordinal);
    private nint _target;
    private bool _failed;
    private string _shown = string.Empty;

    public MenuBarModuleItems(MenuBarModulesViewModel modules, Action open, Action openSettings, Action quit)
    {
        _modules = modules;
        _open = open;
        _openSettings = openSettings;
        _quit = quit;
        _current = this;
        modules.ReadingsChanged += (_, _) => Apply();
        Apply();
    }

    [LibraryImport(Libraries.ObjC, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint objc_allocateClassPair(nint superclass, string name, nint extraBytes);

    [LibraryImport(Libraries.ObjC)]
    private static partial void objc_registerClassPair(nint cls);

    [LibraryImport(Libraries.ObjC, StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool class_addMethod(nint cls, nint selector, nint implementation, string types);

    [LibraryImport(Libraries.ObjC, EntryPoint = "objc_msgSend")]
    private static partial nint SendDouble(nint receiver, nint selector, double value);

    [LibraryImport(Libraries.ObjC, EntryPoint = "objc_msgSend")]
    private static partial nint SendBytes(nint receiver, nint selector, byte* bytes, nuint length);

    [LibraryImport(Libraries.ObjC, EntryPoint = "objc_msgSend")]
    private static partial void SendSize(nint receiver, nint selector, NSSize size);

    [LibraryImport(Libraries.ObjC, EntryPoint = "objc_msgSend")]
    private static partial nint SendMenuItem(nint receiver, nint selector, nint title, nint action, nint keyEquivalent);

    [StructLayout(LayoutKind.Sequential)]
    private struct NSSize
    {
        public double Width;
        public double Height;
    }

    /// <summary>Makes the menu bar match the modules turned on: adds, updates and removes items.</summary>
    public void Apply()
    {
        if (_failed)
        {
            return;
        }

        try
        {
            var readings = _modules.Readings;
            string shown = string.Join(", ", readings.Select(r => r.Id));
            if (shown != _shown)
            {
                _shown = shown;
                Log.Info("MenuBar", shown.Length == 0 ? "No readings in the menu bar." : $"Readings in the menu bar: {shown}.");
            }

            foreach (string id in _items.Keys.Where(id => readings.All(r => r.Id != id)).ToList())
            {
                Remove(id);
            }

            // macOS puts a new item to the left of the others: adding them right to left keeps Settings' order.
            for (int i = readings.Count - 1; i >= 0; i--)
            {
                var reading = readings[i];
                if (!_items.TryGetValue(reading.Id, out var item))
                {
                    item = Create();
                    _items[reading.Id] = item;
                }

                if (item.Reading != reading)
                {
                    item.Reading = reading;
                    SetImage(item, reading);
                }
            }
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException or ArgumentException)
        {
            _failed = true;
            Log.Warn("MenuBar", "Showing readings in the menu bar failed.", e);
        }
    }

    public void Dispose()
    {
        foreach (string id in _items.Keys.ToList())
        {
            Remove(id);
        }

        if (_current == this)
        {
            _current = null;
        }
    }

    private Item Create()
    {
        using var pool = new ObjC.Pool();
        nint bar = ObjC.Send(ObjC.Class("NSStatusBar"), ObjC.Sel("systemStatusBar"));
        nint statusItem = ObjC.Send(SendDouble(bar, ObjC.Sel("statusItemWithLength:"), VariableLength), ObjC.Sel("retain"));
        ObjC.SendVoid(statusItem, ObjC.Sel("setMenu:"), Menu());
        return new Item(statusItem);
    }

    private static void SetImage(Item item, MenuBarReading reading)
    {
        var (png, size) = MenuBarModuleRenderer.RenderPng(reading);
        using var pool = new ObjC.Pool();
        nint data;
        fixed (byte* bytes = png)
        {
            data = SendBytes(ObjC.Class("NSData"), ObjC.Sel("dataWithBytes:length:"), bytes, (nuint)png.Length);
        }

        nint image = ObjC.Send(ObjC.Send(ObjC.Class("NSImage"), ObjC.Sel("alloc")), ObjC.Sel("initWithData:"), data);
        if (image == 0)
        {
            return;
        }

        SendSize(image, ObjC.Sel("setSize:"), new NSSize { Width = size.Width, Height = size.Height });
        ObjC.SendVoidBool(image, ObjC.Sel("setTemplate:"), true);
        nint button = ObjC.Send(item.StatusItem, ObjC.Sel("button"));
        if (button != 0)
        {
            ObjC.SendVoid(button, ObjC.Sel("setImage:"), image);
            ObjC.SendVoid(button, ObjC.Sel("setToolTip:"), ObjC.NSString(reading.SecondLine is { } second ? $"{reading.Value} · {second}" : reading.Value));
        }

        ObjC.Release(image);
    }

    private void Remove(string id)
    {
        if (!_items.Remove(id, out var item))
        {
            return;
        }

        using var pool = new ObjC.Pool();
        nint bar = ObjC.Send(ObjC.Class("NSStatusBar"), ObjC.Sel("systemStatusBar"));
        ObjC.SendVoid(bar, ObjC.Sel("removeStatusItem:"), item.StatusItem);
        ObjC.Release(item.StatusItem);
    }

    /// <summary>The menu each item shows: Open Coreloom, Menu Bar Settings…, Quit Coreloom (autoreleased).</summary>
    private nint Menu()
    {
        nint menu = ObjC.Send(ObjC.Send(ObjC.Send(ObjC.Class("NSMenu"), ObjC.Sel("alloc")), ObjC.Sel("init")), ObjC.Sel("autorelease"));
        AddItem(menu, "Open Coreloom", OpenTag, string.Empty);
        AddItem(menu, "Menu Bar Settings…", SettingsTag, string.Empty);
        ObjC.SendVoid(menu, ObjC.Sel("addItem:"), ObjC.Send(ObjC.Class("NSMenuItem"), ObjC.Sel("separatorItem")));
        AddItem(menu, "Quit Coreloom", QuitTag, "q");
        return menu;
    }

    private void AddItem(nint menu, string title, long tag, string key)
    {
        nint item = SendMenuItem(ObjC.Send(ObjC.Class("NSMenuItem"), ObjC.Sel("alloc")), ObjC.Sel("initWithTitle:action:keyEquivalent:"),
            ObjC.NSString(title), ObjC.Sel("coreloomMenuAction:"), ObjC.NSString(key));
        ObjC.SendVoid(item, ObjC.Sel("setTarget:"), Target());
        ObjC.SendVoidLong(item, ObjC.Sel("setTag:"), tag);
        ObjC.SendVoid(menu, ObjC.Sel("addItem:"), item);
        ObjC.Release(item);
    }

    /// <summary>An object made at run time whose coreloomMenuAction: runs the clicked item's action.</summary>
    private nint Target()
    {
        if (_target != 0)
        {
            return _target;
        }

        nint cls = ObjC.Class("CoreloomMenuBarTarget");
        if (cls == 0)
        {
            cls = objc_allocateClassPair(ObjC.Class("NSObject"), "CoreloomMenuBarTarget", 0);
            class_addMethod(cls, ObjC.Sel("coreloomMenuAction:"), (nint)(delegate* unmanaged<nint, nint, nint, void>)&OnMenuAction, "v@:@");
            objc_registerClassPair(cls);
        }

        _target = ObjC.Send(ObjC.Send(cls, ObjC.Sel("alloc")), ObjC.Sel("init"));
        return _target;
    }

    [UnmanagedCallersOnly]
    private static void OnMenuAction(nint self, nint selector, nint sender)
    {
        try
        {
            var items = _current;
            if (items is null)
            {
                return;
            }

            switch (ObjC.SendLongResult(sender, ObjC.Sel("tag")))
            {
                case OpenTag:
                    items._open();
                    break;
                case SettingsTag:
                    items._openSettings();
                    break;
                case QuitTag:
                    items._quit();
                    break;
            }
        }
        catch (Exception e)
        {
            Log.Error("MenuBar", "A menu bar item's menu failed.", e);
        }
    }

    private sealed class Item(nint statusItem)
    {
        public nint StatusItem { get; } = statusItem;

        public MenuBarReading? Reading { get; set; }
    }
}
