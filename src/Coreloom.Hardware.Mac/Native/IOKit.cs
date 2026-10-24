using System.Runtime.InteropServices;

namespace Coreloom.Hardware.Mac.Native;

/// <summary>IOKit's registry: finding services by class or name, and reading their properties.</summary>
internal static partial class IOKit
{
    public const int Success = 0;

    [LibraryImport(Libraries.IOKit, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint IOServiceMatching(string name);

    [LibraryImport(Libraries.IOKit, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint IOServiceNameMatching(string name);

    [LibraryImport(Libraries.IOKit, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint IOBSDNameMatching(uint mainPort, uint options, string bsdName);

    /// <summary>Consumes <paramref name="matching"/> (it must not be released afterwards).</summary>
    [LibraryImport(Libraries.IOKit)]
    public static partial int IOServiceGetMatchingServices(uint mainPort, nint matching, out uint iterator);

    [LibraryImport(Libraries.IOKit)]
    public static partial uint IOServiceGetMatchingService(uint mainPort, nint matching);

    [LibraryImport(Libraries.IOKit)]
    public static partial uint IOIteratorNext(uint iterator);

    [LibraryImport(Libraries.IOKit)]
    public static partial int IOObjectRelease(uint obj);

    [LibraryImport(Libraries.IOKit)]
    public static partial int IORegistryEntryCreateCFProperties(uint entry, out nint properties, nint allocator, uint options);

    [LibraryImport(Libraries.IOKit)]
    public static partial nint IORegistryEntryCreateCFProperty(uint entry, nint key, nint allocator, uint options);

    [LibraryImport(Libraries.IOKit)]
    public static unsafe partial int IORegistryEntryGetName(uint entry, byte* name);

    [LibraryImport(Libraries.IOKit, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int IORegistryEntryGetParentEntry(uint entry, string plane, out uint parent);

    [LibraryImport(Libraries.IOKit, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int IORegistryEntryGetChildIterator(uint entry, string plane, out uint iterator);

    [LibraryImport(Libraries.IOKit, StringMarshalling = StringMarshalling.Utf8)]
    public static partial uint IORegistryEntryFromPath(uint mainPort, string path);

    [LibraryImport(Libraries.IOKit)]
    public static unsafe partial int IOObjectGetClass(uint obj, byte* className);

    [LibraryImport(Libraries.IOKit)]
    public static partial int IORegistryEntryGetRegistryEntryID(uint entry, out ulong id);

    [LibraryImport(Libraries.IOKit)]
    public static partial int IOServiceOpen(uint service, uint owningTask, uint type, out uint connect);

    [LibraryImport(Libraries.IOKit)]
    public static partial int IOServiceClose(uint connect);

    [LibraryImport(Libraries.IOKit)]
    public static unsafe partial int IOConnectCallStructMethod(uint connection, uint selector, void* input, nint inputSize, void* output, nint* outputSize);

    private static uint _taskSelf;

    /// <summary>The task port of this process (libSystem's mach_task_self_ variable; mach_task_self() is only a C macro).</summary>
    public static unsafe uint TaskSelf()
    {
        if (_taskSelf == 0)
        {
            nint library = NativeLibrary.Load(Libraries.System);
            _taskSelf = *(uint*)NativeLibrary.GetExport(library, "mach_task_self_");
        }

        return _taskSelf;
    }

    /// <summary>Every service matching a class, with its properties converted to .NET.</summary>
    public static List<(uint Service, Dictionary<string, object?> Properties)> Services(string className, bool keepServices = false)
    {
        var result = new List<(uint, Dictionary<string, object?>)>();
        if (IOServiceGetMatchingServices(0, IOServiceMatching(className), out uint iterator) != Success)
        {
            return result;
        }

        try
        {
            uint service;
            while ((service = IOIteratorNext(iterator)) != 0)
            {
                var properties = Properties(service) ?? [];
                result.Add((service, properties));
                if (!keepServices)
                {
                    IOObjectRelease(service);
                }
            }
        }
        finally
        {
            IOObjectRelease(iterator);
        }

        return keepServices ? result : result.Select(r => (0u, r.Item2)).ToList();
    }

    /// <summary>A registry entry's properties converted to .NET, or null.</summary>
    public static Dictionary<string, object?>? Properties(uint entry)
    {
        if (IORegistryEntryCreateCFProperties(entry, out nint properties, 0, 0) != Success || properties == 0)
        {
            return null;
        }

        try
        {
            return CF.ToObject(properties) as Dictionary<string, object?>;
        }
        finally
        {
            CF.CFRelease(properties);
        }
    }

    /// <summary>One property of a registry entry converted to .NET, or null.</summary>
    public static object? Property(uint entry, string key)
    {
        nint k = CF.String(key);
        try
        {
            nint value = IORegistryEntryCreateCFProperty(entry, k, 0, 0);
            if (value == 0)
            {
                return null;
            }

            try
            {
                return CF.ToObject(value);
            }
            finally
            {
                CF.CFRelease(value);
            }
        }
        finally
        {
            CF.CFRelease(k);
        }
    }

    public static unsafe string? Name(uint entry)
    {
        byte* buffer = stackalloc byte[128];
        return IORegistryEntryGetName(entry, buffer) == Success ? Marshal.PtrToStringUTF8((nint)buffer) : null;
    }

    public static unsafe string? ClassName(uint entry)
    {
        byte* buffer = stackalloc byte[128];
        return IOObjectGetClass(entry, buffer) == Success ? Marshal.PtrToStringUTF8((nint)buffer) : null;
    }

    /// <summary>The children in the service plane (release each), or none.</summary>
    public static List<uint> Children(uint entry)
    {
        var result = new List<uint>();
        if (IORegistryEntryGetChildIterator(entry, "IOService", out uint iterator) != Success)
        {
            return result;
        }

        try
        {
            uint child;
            while ((child = IOIteratorNext(iterator)) != 0)
            {
                result.Add(child);
            }
        }
        finally
        {
            IOObjectRelease(iterator);
        }

        return result;
    }

    public static ulong EntryId(uint entry) => IORegistryEntryGetRegistryEntryID(entry, out ulong id) == Success ? id : 0;

    /// <summary>The registry entry id of the nearest ancestor of class <paramref name="className"/>, or 0.</summary>
    public static ulong AncestorId(uint entry, string className, int maxDepth = 24)
    {
        uint current = entry;
        bool owned = false;
        for (int depth = 0; depth < maxDepth && current != 0; depth++)
        {
            uint parent = Parent(current);
            if (owned)
            {
                IOObjectRelease(current);
            }

            if (parent == 0)
            {
                return 0;
            }

            current = parent;
            owned = true;
            if (ClassName(current) == className)
            {
                ulong id = EntryId(current);
                IOObjectRelease(current);
                return id;
            }
        }

        if (owned && current != 0)
        {
            IOObjectRelease(current);
        }

        return 0;
    }

    /// <summary>The parent in the service plane (release it), or 0.</summary>
    public static uint Parent(uint entry) => IORegistryEntryGetParentEntry(entry, "IOService", out uint parent) == Success ? parent : 0;

    /// <summary>Walks up the service plane until <paramref name="predicate"/> matches; returns its properties.</summary>
    public static Dictionary<string, object?>? FindAncestor(uint entry, Func<uint, bool> predicate, int maxDepth = 12)
    {
        uint current = entry;
        bool owned = false;
        for (int depth = 0; depth < maxDepth && current != 0; depth++)
        {
            uint parent = Parent(current);
            if (owned)
            {
                IOObjectRelease(current);
            }

            if (parent == 0)
            {
                return null;
            }

            current = parent;
            owned = true;
            if (predicate(current))
            {
                var properties = Properties(current);
                IOObjectRelease(current);
                return properties;
            }
        }

        if (owned && current != 0)
        {
            IOObjectRelease(current);
        }

        return null;
    }
}
