using System.Globalization;
using System.Runtime.Versioning;
using Coreloom.Hardware.Native;

namespace Coreloom.Hardware.Gaming;

/// <summary>
/// A process's graphics memory from Windows' "GPU Process Memory" counters (what Task Manager shows as dedicated and
/// shared GPU memory), read for VRAM management (since 1.8.4). The counters' instances are named
/// <c>pid_1234_luid_0x00000000_0x0000C3A1_phys_0</c>: one per process and graphics card; values are bytes.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class GpuProcessMemory : IDisposable
{
    private readonly PdhQuery _query;
    private readonly PdhCounter _dedicated;
    private readonly PdhCounter _shared;
    private readonly List<KeyValuePair<string, double>> _values = [];

    private GpuProcessMemory(PdhQuery query, PdhCounter dedicated, PdhCounter shared)
    {
        _query = query;
        _dedicated = dedicated;
        _shared = shared;
    }

    /// <summary>Opens the counters, or explains why they aren't there (older Windows, counters turned off).</summary>
    public static GpuProcessMemory? Open(out string? problem)
    {
        PdhQuery? query = null;
        try
        {
            query = PdhQuery.Open();
            var dedicated = query.TryAddCounter(@"\GPU Process Memory(*)\Dedicated Usage", out uint status);
            var shared = dedicated is null ? null : query.TryAddCounter(@"\GPU Process Memory(*)\Shared Usage", out status);
            if (dedicated is null || shared is null)
            {
                problem = $"Windows' GPU Process Memory counters aren't available (PDH status 0x{status:X8}).";
                query.Dispose();
                return null;
            }

            problem = null;
            return new GpuProcessMemory(query, dedicated, shared);
        }
        catch (Exception e) when (e is PdhException or DllNotFoundException or EntryPointNotFoundException)
        {
            query?.Dispose();
            problem = e.Message;
            return null;
        }
    }

    /// <summary>
    /// The dedicated and shared graphics memory (bytes) of these processes together, on the graphics card with this
    /// LUID (any card when null). Null when the counters couldn't be read this time.
    /// </summary>
    public (long Dedicated, long Shared)? Read(IReadOnlyCollection<int> processIds, long? luid = null)
    {
        if (_query.Collect() != PdhNative.ErrorSuccess)
        {
            return null;
        }

        long dedicated = Sum(_dedicated, processIds, luid, out bool readDedicated);
        long shared = Sum(_shared, processIds, luid, out bool readShared);
        return readDedicated || readShared ? (dedicated, shared) : null;
    }

    public void Dispose() => _query.Dispose();

    private long Sum(PdhCounter counter, IReadOnlyCollection<int> processIds, long? luid, out bool read)
    {
        read = counter.TryGetArray(_values, noCap100: true);
        if (!read)
        {
            return 0;
        }

        long total = 0;
        foreach (var (name, value) in _values)
        {
            if (Parse(name) is { } instance && processIds.Contains(instance.ProcessId) && (luid is null || instance.Luid == luid))
            {
                total += (long)Math.Max(0, value);
            }
        }

        return total;
    }

    /// <summary>The process id and adapter LUID in an instance name ("pid_1234_luid_0x00000000_0x0000C3A1_phys_0").</summary>
    public static (int ProcessId, long Luid)? Parse(string instance)
    {
        var parts = instance.Split('_');
        if (parts.Length < 5 || parts[0] != "pid" || parts[2] != "luid"
            || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int pid)
            || !TryHex(parts[3], out uint high) || !TryHex(parts[4], out uint low))
        {
            return null;
        }

        return (pid, ((long)high << 32) | low);
    }

    private static bool TryHex(string text, out uint value) =>
        uint.TryParse(text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? text[2..] : text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
}
