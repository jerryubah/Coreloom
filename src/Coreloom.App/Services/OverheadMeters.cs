using System.Diagnostics;
using System.Runtime.InteropServices;
using Coreloom.ViewModels.Services;

namespace Coreloom.App.Services;

/// <summary>
/// What Coreloom's own threads cost: each part registers its thread (sensor reading, the window, the overlay's graph,
/// the frame counter) and <see cref="Measure"/> reports each one's share of the whole machine's processor time since
/// the last measurement. This is how the overlay's own cost can be told apart from the rest of Coreloom.
/// </summary>
internal sealed partial class OverheadMeters : IOverheadService
{
    private const uint ThreadQueryLimitedInformation = 0x0800;

    private readonly object _gate = new();
    private readonly Dictionary<string, Meter> _meters = new(StringComparer.Ordinal);
    private long _lastMeasured = Stopwatch.GetTimestamp();

    public static OverheadMeters Shared { get; } = new();

    /// <summary>Registers the calling thread under <paramref name="name"/> (replacing an earlier one of that name).</summary>
    public void RegisterCurrentThread(string name)
    {
        lock (_gate)
        {
            _meters[name] = new Meter(GetCurrentThreadId(), ThreadTime(GetCurrentThreadId()) ?? 0);
        }
    }

    public void Unregister(string name)
    {
        lock (_gate)
        {
            _meters.Remove(name);
        }
    }

    public IReadOnlyList<OverheadPart> Measure()
    {
        lock (_gate)
        {
            long now = Stopwatch.GetTimestamp();
            double seconds = Math.Max(0.001, (now - _lastMeasured) / (double)Stopwatch.Frequency);
            _lastMeasured = now;
            var parts = new List<OverheadPart>();
            foreach (var (name, meter) in _meters.ToList())
            {
                if (ThreadTime(meter.ThreadId) is not { } time)
                {
                    _meters.Remove(name); // the thread ended
                    continue;
                }

                double cpuSeconds = Math.Max(0, (time - meter.LastTime) / 1e7);
                _meters[name] = meter with { LastTime = time };
                parts.Add(new OverheadPart(name, cpuSeconds / seconds / Environment.ProcessorCount * 100));
            }

            return parts;
        }
    }

    /// <summary>The thread's processor time so far (100 ns units), or null when it's gone.</summary>
    private static long? ThreadTime(uint threadId)
    {
        IntPtr handle = OpenThread(ThreadQueryLimitedInformation, false, threadId);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return GetThreadTimes(handle, out _, out _, out long kernel, out long user) ? kernel + user : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private readonly record struct Meter(uint ThreadId, long LastTime);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr OpenThread(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint threadId);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetThreadTimes(IntPtr thread, out long creation, out long exit, out long kernel, out long user);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr handle);
}
