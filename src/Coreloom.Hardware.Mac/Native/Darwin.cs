using System.Runtime.InteropServices;
using System.Text;

namespace Coreloom.Hardware.Mac.Native;

/// <summary>Mach host statistics (memory and per-core CPU ticks), sysctl and libproc.</summary>
internal static partial class Darwin
{
    public const int HostVmInfo64 = 4;
    public const int HostVmInfo64Count = 38;
    public const int ProcessorCpuLoadInfo = 2;
    public const int ProcPidTaskInfo = 4;
    public const int RusageInfoVersion4 = 4;

    [StructLayout(LayoutKind.Sequential)]
    public struct VmStatistics64
    {
        public uint FreeCount;
        public uint ActiveCount;
        public uint InactiveCount;
        public uint WireCount;
        public ulong ZeroFillCount;
        public ulong Reactivations;
        public ulong Pageins;
        public ulong Pageouts;
        public ulong Faults;
        public ulong CowFaults;
        public ulong Lookups;
        public ulong Hits;
        public ulong Purges;
        public uint PurgeableCount;
        public uint SpeculativeCount;
        public ulong Decompressions;
        public ulong Compressions;
        public ulong Swapins;
        public ulong Swapouts;
        public uint CompressorPageCount;
        public uint ThrottledCount;
        public uint ExternalPageCount;
        public uint InternalPageCount;
        public ulong TotalUncompressedPagesInCompressor;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XswUsage
    {
        public ulong Total;
        public ulong Available;
        public ulong Used;
        public uint PageSize;
        public int Encrypted;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ProcTaskInfo
    {
        public ulong VirtualSize;
        public ulong ResidentSize;
        public ulong TotalUser;
        public ulong TotalSystem;
        public ulong ThreadsUser;
        public ulong ThreadsSystem;
        public int Policy;
        public int Faults;
        public int Pageins;
        public int CowFaults;
        public int MessagesSent;
        public int MessagesReceived;
        public int SyscallsMach;
        public int SyscallsUnix;
        public int ContextSwitches;
        public int ThreadCount;
        public int RunningThreads;
        public int Priority;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct RusageInfoV4
    {
        public fixed byte Uuid[16];
        public ulong UserTime;
        public ulong SystemTime;
        public ulong PkgIdleWakeups;
        public ulong InterruptWakeups;
        public ulong Pageins;
        public ulong WiredSize;
        public ulong ResidentSize;
        public ulong PhysFootprint;
        public ulong ProcStartAbsTime;
        public ulong ProcExitAbsTime;
        public ulong ChildUserTime;
        public ulong ChildSystemTime;
        public ulong ChildPkgIdleWakeups;
        public ulong ChildInterruptWakeups;
        public ulong ChildPageins;
        public ulong ChildElapsedAbsTime;
        public ulong DiskIoBytesRead;
        public ulong DiskIoBytesWritten;
        public ulong CpuTimeQosDefault;
        public ulong CpuTimeQosMaintenance;
        public ulong CpuTimeQosBackground;
        public ulong CpuTimeQosUtility;
        public ulong CpuTimeQosLegacy;
        public ulong CpuTimeQosUserInitiated;
        public ulong CpuTimeQosUserInteractive;
        public ulong BilledSystemTime;
        public ulong ServicedSystemTime;
        public ulong LogicalWrites;
        public ulong LifetimeMaxPhysFootprint;
        public ulong Instructions;
        public ulong Cycles;
        public ulong BilledEnergy;
        public ulong ServicedEnergy;
        public ulong IntervalMaxPhysFootprint;
        public ulong RunnableTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TimebaseInfo
    {
        public uint Numer;
        public uint Denom;
    }

    [LibraryImport(Libraries.System)]
    public static partial uint mach_host_self();

    [LibraryImport(Libraries.System)]
    public static unsafe partial int host_statistics64(uint host, int flavor, void* info, ref int count);

    [LibraryImport(Libraries.System)]
    public static unsafe partial int host_processor_info(uint host, int flavor, out int processorCount, out nint info, out int infoCount);

    [LibraryImport(Libraries.System)]
    public static partial int host_page_size(uint host, out nuint pageSize);

    [LibraryImport(Libraries.System)]
    public static partial int vm_deallocate(uint task, nuint address, nuint size);

    [LibraryImport(Libraries.System)]
    public static partial int mach_timebase_info(out TimebaseInfo info);

    [LibraryImport(Libraries.System, StringMarshalling = StringMarshalling.Utf8)]
    public static unsafe partial int sysctlbyname(string name, void* oldp, ref nuint oldlenp, void* newp, nuint newlen);

    [LibraryImport(Libraries.System)]
    public static unsafe partial int proc_listallpids(void* buffer, int bufferSize);

    [LibraryImport(Libraries.System)]
    public static unsafe partial int proc_pidinfo(int pid, int flavor, ulong arg, void* buffer, int bufferSize);

    [LibraryImport(Libraries.System)]
    public static unsafe partial int proc_pidpath(int pid, byte* buffer, uint bufferSize);

    [LibraryImport(Libraries.System)]
    public static unsafe partial int proc_name(int pid, byte* buffer, uint bufferSize);

    [LibraryImport(Libraries.System)]
    public static unsafe partial int proc_pid_rusage(int pid, int flavor, void* buffer);

    /// <summary>A sysctl as a string ("machdep.cpu.brand_string", "hw.model"), or null.</summary>
    public static unsafe string? SysctlString(string name)
    {
        nuint size = 0;
        if (sysctlbyname(name, null, ref size, null, 0) != 0 || size == 0 || size > 65536)
        {
            return null;
        }

        var buffer = new byte[(int)size];
        fixed (byte* p = buffer)
        {
            if (sysctlbyname(name, p, ref size, null, 0) != 0)
            {
                return null;
            }
        }

        return Encoding.UTF8.GetString(buffer, 0, (int)size).TrimEnd('\0').Trim();
    }

    /// <summary>A numeric sysctl (32- or 64-bit), or null.</summary>
    public static unsafe long? SysctlLong(string name)
    {
        long value = 0;
        nuint size = sizeof(long);
        if (sysctlbyname(name, &value, ref size, null, 0) != 0)
        {
            return null;
        }

        return size == 4 ? (int)value : value;
    }

    /// <summary>A sysctl that returns a struct.</summary>
    public static unsafe bool SysctlStruct<T>(string name, out T value)
        where T : unmanaged
    {
        T result = default;
        nuint size = (nuint)sizeof(T);
        bool ok = sysctlbyname(name, &result, ref size, null, 0) == 0;
        value = result;
        return ok;
    }

    public static unsafe bool VmStatistics(out VmStatistics64 stats)
    {
        VmStatistics64 result = default;
        int count = HostVmInfo64Count;
        bool ok = host_statistics64(mach_host_self(), HostVmInfo64, &result, ref count) == 0;
        stats = result;
        return ok;
    }

    public static long PageSize()
    {
        return host_page_size(mach_host_self(), out nuint size) == 0 && size > 0 ? (long)size : SysctlLong("hw.pagesize") ?? 4096;
    }

    /// <summary>Per-core CPU ticks (user, system, idle, nice), or null.</summary>
    public static unsafe uint[][]? CpuTicks()
    {
        if (host_processor_info(mach_host_self(), ProcessorCpuLoadInfo, out int processors, out nint info, out int infoCount) != 0 || info == 0)
        {
            return null;
        }

        try
        {
            var result = new uint[processors][];
            uint* p = (uint*)info;
            for (int i = 0; i < processors; i++)
            {
                result[i] = [p[(i * 4) + 0], p[(i * 4) + 1], p[(i * 4) + 2], p[(i * 4) + 3]];
            }

            return result;
        }
        finally
        {
            vm_deallocate(IOKit.TaskSelf(), (nuint)info, (nuint)(infoCount * sizeof(int)));
        }
    }

    private static double? _ticksToNs;

    /// <summary>Nanoseconds per Mach absolute time unit (1 on Intel, 125/3 on Apple silicon).</summary>
    public static double TicksToNanoseconds
    {
        get
        {
            if (_ticksToNs is null)
            {
                _ticksToNs = mach_timebase_info(out var info) == 0 && info.Denom != 0 ? (double)info.Numer / info.Denom : 1;
            }

            return _ticksToNs.Value;
        }
    }

    public static unsafe int[] AllPids()
    {
        int count = proc_listallpids(null, 0);
        if (count <= 0)
        {
            return [];
        }

        var pids = new int[count + 64];
        fixed (int* p = pids)
        {
            int got = proc_listallpids(p, pids.Length * sizeof(int));
            return got <= 0 ? [] : pids[..Math.Min(got, pids.Length)];
        }
    }

    public static unsafe string? ProcessPath(int pid)
    {
        byte* buffer = stackalloc byte[4096];
        int length = proc_pidpath(pid, buffer, 4096);
        return length > 0 ? Encoding.UTF8.GetString(buffer, length) : null;
    }

    public static unsafe string? ProcessName(int pid)
    {
        byte* buffer = stackalloc byte[256];
        int length = proc_name(pid, buffer, 256);
        return length > 0 ? Encoding.UTF8.GetString(buffer, length) : null;
    }
}
