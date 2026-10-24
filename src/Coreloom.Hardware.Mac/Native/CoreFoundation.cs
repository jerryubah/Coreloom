using System.Runtime.InteropServices;
using System.Text;

namespace Coreloom.Hardware.Mac.Native;

/// <summary>Framework and library paths (dlopen'd by the runtime; nothing here is bundled).</summary>
internal static class Libraries
{
    public const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    public const string IOKit = "/System/Library/Frameworks/IOKit.framework/IOKit";
    public const string System = "/usr/lib/libSystem.dylib";
    public const string ObjC = "/usr/lib/libobjc.A.dylib";
    public const string IOReport = "/usr/lib/libIOReport.dylib";
    public const string SystemConfiguration = "/System/Library/Frameworks/SystemConfiguration.framework/SystemConfiguration";
    public const string Metal = "/System/Library/Frameworks/Metal.framework/Metal";
}

/// <summary>
/// The parts of CoreFoundation Coreloom needs: creating and releasing objects, and reading the dictionaries, arrays,
/// strings, numbers and data that IOKit hands back (converted to plain .NET values by <see cref="ToObject"/>).
/// </summary>
internal static partial class CF
{
    public const int NumberSInt32Type = 3;
    public const int NumberSInt64Type = 4;
    public const int NumberFloat64Type = 6;
    public const int NumberDoubleType = 13;
    private const uint StringEncodingUtf8 = 0x08000100;

    private static readonly Lazy<nint> StringTypeId = new(CFStringGetTypeID);
    private static readonly Lazy<nint> NumberTypeId = new(CFNumberGetTypeID);
    private static readonly Lazy<nint> BooleanTypeId = new(CFBooleanGetTypeID);
    private static readonly Lazy<nint> DictionaryTypeId = new(CFDictionaryGetTypeID);
    private static readonly Lazy<nint> ArrayTypeId = new(CFArrayGetTypeID);
    private static readonly Lazy<nint> DataTypeId = new(CFDataGetTypeID);

    [LibraryImport(Libraries.CoreFoundation)]
    public static partial void CFRelease(nint obj);

    [LibraryImport(Libraries.CoreFoundation)]
    public static partial nint CFRetain(nint obj);

    [LibraryImport(Libraries.CoreFoundation)]
    public static partial nint CFGetTypeID(nint obj);

    [LibraryImport(Libraries.CoreFoundation)]
    private static partial nint CFStringGetTypeID();

    [LibraryImport(Libraries.CoreFoundation)]
    private static partial nint CFNumberGetTypeID();

    [LibraryImport(Libraries.CoreFoundation)]
    private static partial nint CFBooleanGetTypeID();

    [LibraryImport(Libraries.CoreFoundation)]
    private static partial nint CFDictionaryGetTypeID();

    [LibraryImport(Libraries.CoreFoundation)]
    private static partial nint CFArrayGetTypeID();

    [LibraryImport(Libraries.CoreFoundation)]
    private static partial nint CFDataGetTypeID();

    [LibraryImport(Libraries.CoreFoundation, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint CFStringCreateWithCString(nint allocator, string value, uint encoding);

    [LibraryImport(Libraries.CoreFoundation)]
    private static partial nint CFStringGetLength(nint value);

    [LibraryImport(Libraries.CoreFoundation)]
    private static partial nint CFStringGetMaximumSizeForEncoding(nint length, uint encoding);

    [LibraryImport(Libraries.CoreFoundation)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static unsafe partial bool CFStringGetCString(nint value, byte* buffer, nint size, uint encoding);

    [LibraryImport(Libraries.CoreFoundation)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static unsafe partial bool CFNumberGetValue(nint number, int type, void* value);

    [LibraryImport(Libraries.CoreFoundation)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool CFNumberIsFloatType(nint number);

    [LibraryImport(Libraries.CoreFoundation)]
    public static unsafe partial nint CFNumberCreate(nint allocator, int type, void* value);

    [LibraryImport(Libraries.CoreFoundation)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool CFBooleanGetValue(nint boolean);

    [LibraryImport(Libraries.CoreFoundation)]
    public static partial nint CFDictionaryGetValue(nint dictionary, nint key);

    [LibraryImport(Libraries.CoreFoundation)]
    public static partial nint CFDictionaryGetCount(nint dictionary);

    [LibraryImport(Libraries.CoreFoundation)]
    private static unsafe partial void CFDictionaryGetKeysAndValues(nint dictionary, nint* keys, nint* values);

    [LibraryImport(Libraries.CoreFoundation)]
    public static partial nint CFDictionaryCreateMutable(nint allocator, nint capacity, nint keyCallbacks, nint valueCallbacks);

    [LibraryImport(Libraries.CoreFoundation)]
    public static partial void CFDictionarySetValue(nint dictionary, nint key, nint value);

    [LibraryImport(Libraries.CoreFoundation)]
    public static partial nint CFArrayGetCount(nint array);

    [LibraryImport(Libraries.CoreFoundation)]
    public static partial nint CFArrayGetValueAtIndex(nint array, nint index);

    [LibraryImport(Libraries.CoreFoundation)]
    private static partial nint CFDataGetLength(nint data);

    [LibraryImport(Libraries.CoreFoundation)]
    private static partial nint CFDataGetBytePtr(nint data);

    /// <summary>kCFTypeDictionaryKeyCallBacks and kCFTypeDictionaryValueCallBacks (exported data symbols).</summary>
    public static nint TypeDictionaryKeyCallBacks => Symbol("kCFTypeDictionaryKeyCallBacks");

    public static nint TypeDictionaryValueCallBacks => Symbol("kCFTypeDictionaryValueCallBacks");

    private static nint Symbol(string name)
    {
        nint library = NativeLibrary.Load(Libraries.CoreFoundation);
        return NativeLibrary.GetExport(library, name);
    }

    /// <summary>A new CFString (release it).</summary>
    public static nint String(string value) => CFStringCreateWithCString(0, value, StringEncodingUtf8);

    /// <summary>A new CFNumber holding a 32-bit integer (release it).</summary>
    public static unsafe nint Number(int value) => CFNumberCreate(0, NumberSInt32Type, &value);

    /// <summary>A new mutable dictionary with CF type callbacks (release it).</summary>
    public static nint MutableDictionary() => CFDictionaryCreateMutable(0, 0, TypeDictionaryKeyCallBacks, TypeDictionaryValueCallBacks);

    /// <summary>Sets a value under a string key; the dictionary retains both.</summary>
    public static void Set(nint dictionary, string key, nint value)
    {
        nint k = String(key);
        try
        {
            CFDictionarySetValue(dictionary, k, value);
        }
        finally
        {
            CFRelease(k);
        }
    }

    /// <summary>The value under a string key (not retained), or 0.</summary>
    public static nint Get(nint dictionary, string key)
    {
        if (dictionary == 0)
        {
            return 0;
        }

        nint k = String(key);
        try
        {
            return CFDictionaryGetValue(dictionary, k);
        }
        finally
        {
            CFRelease(k);
        }
    }

    public static bool IsString(nint obj) => obj != 0 && CFGetTypeID(obj) == StringTypeId.Value;

    public static bool IsNumber(nint obj) => obj != 0 && CFGetTypeID(obj) == NumberTypeId.Value;

    public static bool IsDictionary(nint obj) => obj != 0 && CFGetTypeID(obj) == DictionaryTypeId.Value;

    public static bool IsArray(nint obj) => obj != 0 && CFGetTypeID(obj) == ArrayTypeId.Value;

    public static bool IsData(nint obj) => obj != 0 && CFGetTypeID(obj) == DataTypeId.Value;

    public static unsafe string? ToStringValue(nint value)
    {
        if (!IsString(value))
        {
            return null;
        }

        nint length = CFStringGetLength(value);
        nint size = CFStringGetMaximumSizeForEncoding(length, StringEncodingUtf8) + 1;
        if (size <= 0 || size > 1 << 20)
        {
            return null;
        }

        var buffer = new byte[size];
        fixed (byte* p = buffer)
        {
            if (!CFStringGetCString(value, p, size, StringEncodingUtf8))
            {
                return null;
            }

            return Marshal.PtrToStringUTF8((nint)p);
        }
    }

    public static unsafe long? ToLong(nint value)
    {
        if (!IsNumber(value))
        {
            return null;
        }

        if (CFNumberIsFloatType(value))
        {
            double d;
            return CFNumberGetValue(value, NumberFloat64Type, &d) ? (long)d : null;
        }

        long l;
        return CFNumberGetValue(value, NumberSInt64Type, &l) ? l : null;
    }

    public static unsafe double? ToDouble(nint value)
    {
        if (!IsNumber(value))
        {
            return null;
        }

        double d;
        return CFNumberGetValue(value, NumberFloat64Type, &d) ? d : null;
    }

    public static byte[]? ToBytes(nint value)
    {
        if (!IsData(value))
        {
            return null;
        }

        int length = (int)CFDataGetLength(value);
        var bytes = new byte[length];
        if (length > 0)
        {
            Marshal.Copy(CFDataGetBytePtr(value), bytes, 0, length);
        }

        return bytes;
    }

    /// <summary>
    /// Converts a CF object to .NET: CFString → string, CFNumber → long or double, CFBoolean → bool, CFData → byte[],
    /// CFArray → List&lt;object?&gt;, CFDictionary → Dictionary&lt;string, object?&gt; (string keys only).
    /// </summary>
    public static unsafe object? ToObject(nint value, int depth = 0)
    {
        if (value == 0 || depth > 12)
        {
            return null;
        }

        nint type = CFGetTypeID(value);
        if (type == StringTypeId.Value)
        {
            return ToStringValue(value);
        }

        if (type == NumberTypeId.Value)
        {
            if (CFNumberIsFloatType(value))
            {
                return ToDouble(value);
            }

            return ToLong(value);
        }

        if (type == BooleanTypeId.Value)
        {
            return CFBooleanGetValue(value);
        }

        if (type == DataTypeId.Value)
        {
            return ToBytes(value);
        }

        if (type == ArrayTypeId.Value)
        {
            int count = (int)CFArrayGetCount(value);
            var list = new List<object?>(count);
            for (int i = 0; i < count; i++)
            {
                list.Add(ToObject(CFArrayGetValueAtIndex(value, i), depth + 1));
            }

            return list;
        }

        if (type == DictionaryTypeId.Value)
        {
            int count = (int)CFDictionaryGetCount(value);
            var keys = new nint[count];
            var values = new nint[count];
            fixed (nint* k = keys)
            fixed (nint* v = values)
            {
                CFDictionaryGetKeysAndValues(value, k, v);
            }

            var dictionary = new Dictionary<string, object?>(count, StringComparer.Ordinal);
            for (int i = 0; i < count; i++)
            {
                if (ToStringValue(keys[i]) is { } key)
                {
                    dictionary[key] = ToObject(values[i], depth + 1);
                }
            }

            return dictionary;
        }

        return null;
    }

    /// <summary>Reads a number out of a converted dictionary (long or double), or null.</summary>
    public static double? Number(IReadOnlyDictionary<string, object?>? dictionary, string key) =>
        dictionary is not null && dictionary.TryGetValue(key, out var value) ? value switch
        {
            long l => l,
            double d => d,
            int i => i,
            bool b => b ? 1 : 0,
            _ => null,
        } : null;

    public static string? Text(IReadOnlyDictionary<string, object?>? dictionary, string key) =>
        dictionary is not null && dictionary.TryGetValue(key, out var value) ? value switch
        {
            string s => s,
            byte[] bytes => Encoding.UTF8.GetString(bytes).TrimEnd('\0').Trim(),
            _ => null,
        } : null;
}
