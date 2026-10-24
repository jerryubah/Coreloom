using System.Runtime.InteropServices;
using System.Text;

namespace Coreloom.Hardware.Mac.Native;

/// <summary>Mounted file systems: statfs (for the device behind a mount and its flags) and CFURL's volume properties.</summary>
internal static unsafe partial class FileSystems
{
    public const uint MntLocal = 0x00001000;
    public const uint MntRootFs = 0x00004000;
    public const uint MntDontBrowse = 0x00100000;

    [StructLayout(LayoutKind.Sequential)]
    public struct StatFs
    {
        public uint BlockSize;
        public int IoSize;
        public ulong Blocks;
        public ulong BlocksFree;
        public ulong BlocksAvailable;
        public ulong Files;
        public ulong FilesFree;
        public ulong FsId;
        public uint Owner;
        public uint Type;
        public uint Flags;
        public uint FsSubtype;
        public fixed byte TypeName[16];
        public fixed byte MountedOn[1024];
        public fixed byte MountedFrom[1024];
        public uint FlagsExt;
        public fixed uint Reserved[7];
    }

    private static delegate* unmanaged<byte*, StatFs*, int> _statfs;

    /// <summary>statfs with the 64-bit inode layout ("statfs" on arm64, "statfs$INODE64" on x86-64).</summary>
    public static bool TryStat(string path, out StatFs result, out string fileSystem, out string device)
    {
        result = default;
        fileSystem = device = string.Empty;
        if (_statfs is null)
        {
            nint library = NativeLibrary.Load(Libraries.System);
            string name = RuntimeInformation.ProcessArchitecture == Architecture.X64 ? "statfs$INODE64" : "statfs";
            _statfs = (delegate* unmanaged<byte*, StatFs*, int>)NativeLibrary.GetExport(library, name);
        }

        byte[] bytes = Encoding.UTF8.GetBytes(path + "\0");
        StatFs stat;
        fixed (byte* p = bytes)
        {
            if (_statfs(p, &stat) != 0)
            {
                return false;
            }
        }

        result = stat;
        fileSystem = Marshal.PtrToStringUTF8((nint)stat.TypeName) ?? string.Empty;
        device = Marshal.PtrToStringUTF8((nint)stat.MountedFrom) ?? string.Empty;
        return true;
    }

    [LibraryImport(Libraries.CoreFoundation)]
    private static partial nint CFURLCreateWithFileSystemPath(nint allocator, nint path, int style, [MarshalAs(UnmanagedType.U1)] bool isDirectory);

    [LibraryImport(Libraries.CoreFoundation)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool CFURLCopyResourcePropertyForKey(nint url, nint key, out nint value, out nint error);

    private static nint Key(string name) =>
        NativeLibrary.TryGetExport(NativeLibrary.Load(Libraries.CoreFoundation), name, out nint symbol) && symbol != 0 ? *(nint*)symbol : 0;

    /// <summary>
    /// A volume's Finder name ("Macintosh HD") and capacity as Finder reports it: the free space includes purgeable
    /// space macOS frees on demand ("available for important usage"). Null when the volume can't be queried.
    /// </summary>
    public static (string? Name, long? Total, long? Available, bool? Internal, bool? Browsable)? VolumeInfo(string mountPoint)
    {
        nint path = CF.String(mountPoint);
        nint url = CFURLCreateWithFileSystemPath(0, path, 0, true);
        CF.CFRelease(path);
        if (url == 0)
        {
            return null;
        }

        try
        {
            object? Get(string key)
            {
                nint k = Key(key);
                if (k == 0)
                {
                    return null;
                }

                if (!CFURLCopyResourcePropertyForKey(url, k, out nint value, out nint error) || value == 0)
                {
                    if (error != 0)
                    {
                        CF.CFRelease(error);
                    }

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

            static long? Long(object? o) => o switch { long l => l, double d => (long)d, _ => null };
            return (
                Get("kCFURLVolumeNameKey") as string,
                Long(Get("kCFURLVolumeTotalCapacityKey")),
                Long(Get("kCFURLVolumeAvailableCapacityForImportantUsageKey")) ?? Long(Get("kCFURLVolumeAvailableCapacityKey")),
                Get("kCFURLVolumeIsInternalKey") as bool?,
                Get("kCFURLVolumeIsBrowsableKey") as bool?);
        }
        finally
        {
            CF.CFRelease(url);
        }
    }
}
