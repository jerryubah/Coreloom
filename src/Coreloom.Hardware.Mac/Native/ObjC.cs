using System.Runtime.InteropServices;

namespace Coreloom.Hardware.Mac.Native;

/// <summary>
/// Calling Objective-C from C#: classes, selectors and objc_msgSend with the argument shapes Coreloom uses. Every call
/// that can create autoreleased objects runs inside <see cref="Pool"/>.
/// </summary>
internal static unsafe partial class ObjC
{
    private static readonly Dictionary<string, nint> Selectors = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, nint> Classes = new(StringComparer.Ordinal);

    [LibraryImport(Libraries.ObjC, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint objc_getClass(string name);

    [LibraryImport(Libraries.ObjC, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint sel_registerName(string name);

    [LibraryImport(Libraries.ObjC)]
    public static partial nint objc_autoreleasePoolPush();

    [LibraryImport(Libraries.ObjC)]
    public static partial void objc_autoreleasePoolPop(nint pool);

    [LibraryImport(Libraries.ObjC, EntryPoint = "objc_msgSend")]
    public static partial nint Send(nint receiver, nint selector);

    [LibraryImport(Libraries.ObjC, EntryPoint = "objc_msgSend")]
    public static partial nint Send(nint receiver, nint selector, nint a);

    [LibraryImport(Libraries.ObjC, EntryPoint = "objc_msgSend")]
    public static partial nint Send(nint receiver, nint selector, nint a, nint b);

    [LibraryImport(Libraries.ObjC, EntryPoint = "objc_msgSend")]
    public static partial nint Send(nint receiver, nint selector, nint a, nint b, nint c);

    [LibraryImport(Libraries.ObjC, EntryPoint = "objc_msgSend")]
    public static partial nint Send(nint receiver, nint selector, nint a, nint b, nint c, nint d);

    [LibraryImport(Libraries.ObjC, EntryPoint = "objc_msgSend")]
    public static partial nint SendLong(nint receiver, nint selector, long a);

    [LibraryImport(Libraries.ObjC, EntryPoint = "objc_msgSend")]
    public static partial nint SendULongULong(nint receiver, nint selector, ulong a, ulong b);

    [LibraryImport(Libraries.ObjC, EntryPoint = "objc_msgSend")]
    public static partial nint SendPtrULongULong(nint receiver, nint selector, nint a, ulong b, ulong c);

    [LibraryImport(Libraries.ObjC, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool SendBool(nint receiver, nint selector);

    [LibraryImport(Libraries.ObjC, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool SendBool(nint receiver, nint selector, nint a);

    [LibraryImport(Libraries.ObjC, EntryPoint = "objc_msgSend")]
    public static partial void SendVoid(nint receiver, nint selector);

    [LibraryImport(Libraries.ObjC, EntryPoint = "objc_msgSend")]
    public static partial void SendVoid(nint receiver, nint selector, nint a);

    [LibraryImport(Libraries.ObjC, EntryPoint = "objc_msgSend")]
    public static partial void SendVoid(nint receiver, nint selector, nint a, nint b);

    [LibraryImport(Libraries.ObjC, EntryPoint = "objc_msgSend")]
    public static partial void SendVoid(nint receiver, nint selector, nint a, nint b, nint c);

    [LibraryImport(Libraries.ObjC, EntryPoint = "objc_msgSend")]
    public static partial void SendVoidBool(nint receiver, nint selector, [MarshalAs(UnmanagedType.U1)] bool a);

    [LibraryImport(Libraries.ObjC, EntryPoint = "objc_msgSend")]
    public static partial void SendVoidLong(nint receiver, nint selector, long a);

    [LibraryImport(Libraries.ObjC, EntryPoint = "objc_msgSend")]
    public static partial void SendVoidPtrULongULong(nint receiver, nint selector, nint a, ulong b, ulong c);

    [LibraryImport(Libraries.ObjC, EntryPoint = "objc_msgSend")]
    public static partial void SendVoidPtrULong(nint receiver, nint selector, nint a, ulong b);

    [LibraryImport(Libraries.ObjC, EntryPoint = "objc_msgSend")]
    public static partial void SendVoidPtrPtrULong(nint receiver, nint selector, nint a, nint b, ulong c);

    [LibraryImport(Libraries.ObjC, EntryPoint = "objc_msgSend")]
    public static partial void SendVoidPtrULongPtrULong(nint receiver, nint selector, nint a, ulong b, nint c, ulong d);

    [LibraryImport(Libraries.ObjC, EntryPoint = "objc_msgSend")]
    public static partial ulong SendULong(nint receiver, nint selector);

    [LibraryImport(Libraries.ObjC, EntryPoint = "objc_msgSend")]
    public static partial long SendLongResult(nint receiver, nint selector);

    [LibraryImport(Libraries.ObjC, EntryPoint = "objc_msgSend")]
    public static partial double SendDouble(nint receiver, nint selector);

    public static nint Class(string name)
    {
        lock (Classes)
        {
            if (!Classes.TryGetValue(name, out nint cls))
            {
                cls = objc_getClass(name);
                Classes[name] = cls;
            }

            return cls;
        }
    }

    public static nint Sel(string name)
    {
        lock (Selectors)
        {
            if (!Selectors.TryGetValue(name, out nint sel))
            {
                sel = sel_registerName(name);
                Selectors[name] = sel;
            }

            return sel;
        }
    }

    /// <summary>An autoreleased NSString.</summary>
    public static nint NSString(string value)
    {
        nint utf8 = Marshal.StringToCoTaskMemUTF8(value);
        try
        {
            return Send(Class("NSString"), Sel("stringWithUTF8String:"), utf8);
        }
        finally
        {
            Marshal.FreeCoTaskMem(utf8);
        }
    }

    public static string? ToStringValue(nint nsString) =>
        nsString == 0 ? null : Marshal.PtrToStringUTF8(Send(nsString, Sel("UTF8String")));

    public static void Release(nint obj)
    {
        if (obj != 0)
        {
            SendVoid(obj, Sel("release"));
        }
    }

    /// <summary>An autorelease pool for the length of a using block.</summary>
    public readonly struct Pool : IDisposable
    {
        private readonly nint _pool;

        public Pool() => _pool = objc_autoreleasePoolPush();

        public void Dispose() => objc_autoreleasePoolPop(_pool);
    }

    /// <summary>
    /// An Objective-C block (a heap-free global block) that calls a C# function pointer. Used for completion handlers;
    /// the block and its descriptor live for the life of the process.
    /// </summary>
    public static nint GlobalBlock(nint invoke)
    {
        // struct Block_literal { void* isa; int flags; int reserved; void* invoke; Block_descriptor* descriptor; }
        // struct Block_descriptor { unsigned long reserved; unsigned long size; }
        nint library = NativeLibrary.Load(Libraries.System);
        nint isa = NativeLibrary.GetExport(library, "_NSConcreteGlobalBlock");
        nint descriptor = Marshal.AllocHGlobal(16);
        *(ulong*)descriptor = 0;
        *(ulong*)(descriptor + 8) = 32;
        nint block = Marshal.AllocHGlobal(32);
        *(nint*)block = isa;
        *(int*)(block + 8) = 1 << 28; // BLOCK_IS_GLOBAL
        *(int*)(block + 12) = 0;
        *(nint*)(block + 16) = invoke;
        *(nint*)(block + 24) = descriptor;
        return block;
    }
}
