using System.Diagnostics;
using Coreloom.Core.Hardware;
using Coreloom.Hardware.Mac.Native;

namespace Coreloom.Hardware.Mac;

/// <summary>
/// Per-app CPU and memory, grouped the way Activity Monitor's hierarchy does: every process inside an app bundle
/// (helpers, renderers, plug-ins) counts toward that app ("Google Chrome ×14"), other processes by name. Memory is the
/// footprint Activity Monitor shows in its Memory column where macOS shares it (the user's own processes), else the
/// resident size. No privileges are needed; other users' processes only report what macOS allows.
/// </summary>
public sealed unsafe class MacProcessSampler : IProcessSampler
{
    private readonly object _gate = new();
    private readonly Dictionary<int, (string Name, ulong StartTime)> _names = [];
    private Dictionary<int, ulong> _previousCpu = [];
    private long _previousTimestamp;

    public IReadOnlyList<ProcessUsage> Sample()
    {
        lock (_gate)
        {
            int[] pids = Darwin.AllPids();
            long now = Stopwatch.GetTimestamp();
            double elapsedNs = _previousTimestamp == 0 ? 0 : Stopwatch.GetElapsedTime(_previousTimestamp, now).TotalMilliseconds * 1e6;
            double capacityNs = elapsedNs * Environment.ProcessorCount;
            double ticksToNs = Darwin.TicksToNanoseconds;
            var currentCpu = new Dictionary<int, ulong>(pids.Length);
            var groups = new Dictionary<string, (int Count, double Cpu, long Memory)>(StringComparer.Ordinal);
            var alive = new HashSet<int>();
            foreach (int pid in pids)
            {
                if (pid <= 0)
                {
                    continue;
                }

                Darwin.ProcTaskInfo task;
                if (Darwin.proc_pidinfo(pid, Darwin.ProcPidTaskInfo, 0, &task, sizeof(Darwin.ProcTaskInfo)) != sizeof(Darwin.ProcTaskInfo))
                {
                    continue;
                }

                alive.Add(pid);
                ulong cpu = task.TotalUser + task.TotalSystem;
                long memory = (long)task.ResidentSize;
                Darwin.RusageInfoV4 usage;
                if (Darwin.proc_pid_rusage(pid, Darwin.RusageInfoVersion4, &usage) == 0)
                {
                    memory = (long)usage.PhysFootprint;
                }

                currentCpu[pid] = cpu;
                double percent = 0;
                if (capacityNs > 0 && _previousCpu.TryGetValue(pid, out ulong previous) && cpu >= previous)
                {
                    percent = (cpu - previous) * ticksToNs * 100.0 / capacityNs;
                }

                string name = NameOf(pid);
                groups.TryGetValue(name, out var group);
                groups[name] = (group.Count + 1, group.Cpu + percent, group.Memory + memory);
            }

            foreach (int gone in _names.Keys.Where(p => !alive.Contains(p)).ToArray())
            {
                _names.Remove(gone);
            }

            _previousCpu = currentCpu;
            _previousTimestamp = now;
            return groups.Select(g => new ProcessUsage(g.Key, g.Value.Count, Math.Clamp(g.Value.Cpu, 0, 100), g.Value.Memory, g.Value.Memory)).ToArray();
        }
    }

    private string NameOf(int pid)
    {
        if (_names.TryGetValue(pid, out var cached))
        {
            return cached.Name;
        }

        string? path = Darwin.ProcessPath(pid);
        string name = AppName(path) ?? Darwin.ProcessName(pid) ?? (path is null ? $"Process {pid}" : Path.GetFileName(path));
        _names[pid] = (name, 0);
        return name;
    }

    /// <summary>"/Applications/Google Chrome.app/Contents/Frameworks/…/Google Chrome Helper.app/…" → "Google Chrome".</summary>
    internal static string? AppName(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        int app = path.IndexOf(".app/", StringComparison.Ordinal);
        if (app < 0)
        {
            return null;
        }

        int start = path.LastIndexOf('/', app) + 1;
        string name = path[start..app];
        return name.Length > 0 ? name : null;
    }

    /// <summary>This process's footprint (what Activity Monitor shows for Coreloom), or NaN.</summary>
    public static double OwnFootprint()
    {
        Darwin.RusageInfoV4 usage;
        return Darwin.proc_pid_rusage(Environment.ProcessId, Darwin.RusageInfoVersion4, &usage) == 0 ? usage.PhysFootprint : double.NaN;
    }
}
