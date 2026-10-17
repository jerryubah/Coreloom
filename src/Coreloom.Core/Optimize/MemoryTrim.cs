using System.Globalization;

namespace Coreloom.Core.Optimize;

/// <summary>What happened to one process when memory was freed. Coreloom never ends a process.</summary>
public enum TrimOutcome
{
    /// <summary>Its working set was trimmed (Windows writes out pages it hasn't touched lately).</summary>
    Trimmed,

    /// <summary>Left alone on purpose (Windows itself, the app you're using, Coreloom).</summary>
    Skipped,

    /// <summary>Windows refused (usually a protected process) or it exited meanwhile.</summary>
    Failed,
}

/// <summary>One process in a memory clean-up: its working set before and after (bytes) and what happened.</summary>
public sealed record MemoryTrimEntry(int ProcessId, string Name, TrimOutcome Outcome, long? Before = null, long? After = null, string? Reason = null)
{
    /// <summary>Memory taken out of the process's working set (0 unless trimmed).</summary>
    public long Reclaimed => Outcome == TrimOutcome.Trimmed && Before is { } before && After is { } after ? Math.Max(0, before - after) : 0;

    /// <summary>"chrome.exe": the program file's name ("System", "Registry" and the like have none).</summary>
    public string FileName => Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || Name.Length == 0 || Name.Contains(' ') || MemoryTrim.IsPseudoProcess(Name) ? Name : Name + ".exe";

    /// <summary>The log line: "Trimmed chrome.exe (PID 1234): 512.0 MB → 120.0 MB, 392.0 MB reclaimed".</summary>
    public string LogLine() => Outcome switch
    {
        TrimOutcome.Trimmed => $"Trimmed {FileName} (PID {ProcessId}): {MemoryTrim.Megabytes(Before)} → {MemoryTrim.Megabytes(After)}, {MemoryTrim.Megabytes(Reclaimed)} reclaimed",
        TrimOutcome.Skipped => $"Skipped {FileName} (PID {ProcessId}){(Before is { } b ? $" at {MemoryTrim.Megabytes(b)}" : string.Empty)}: {Reason ?? "left alone"}",
        _ => $"Failed {FileName} (PID {ProcessId}){(Before is { } f ? $" at {MemoryTrim.Megabytes(f)}" : string.Empty)}: {Reason ?? "Windows refused"}",
    };
}

/// <summary>
/// The rules and wording for freeing memory, shared by the Windows service (which does it) and the page (which shows
/// it): which processes are left alone and why, how a refusal is described, and the totals.
/// </summary>
public static class MemoryTrim
{
    /// <summary>Windows' own processes that must stay responsive: never trimmed.</summary>
    public static IReadOnlySet<string> KeepProcesses { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Idle", "System", "Registry", "Memory Compression", "Secure System", "smss", "csrss", "wininit", "winlogon", "services",
        "lsass", "dwm", "audiodg", "fontdrvhost", "MsMpEng", "NisSrv",
    };

    /// <summary>Windows' own entries in the process list that aren't programs ("System", "Registry", "Idle").</summary>
    public static bool IsPseudoProcess(string name) => name is "System" or "Registry" or "Idle" or "Secure System" or "Memory Compression";

    /// <summary>Why a process is left alone, or null when it may be trimmed.</summary>
    public static string? SkipReason(int processId, string name, int foregroundProcessId, int ownProcessId) =>
        processId is 0 or 4 ? "part of Windows itself"
        : processId == ownProcessId ? "Coreloom itself"
        : processId == foregroundProcessId ? "the app you're using keeps its memory"
        : KeepProcesses.Contains(name) ? "Windows needs it to stay responsive"
        : null;

    /// <summary>A Windows error code as a reason: 5 is a protected process, 87 or 1168 one that exited meanwhile.</summary>
    public static string Describe(int win32Error) => win32Error switch
    {
        5 => "access denied (a protected process)",
        87 or 1168 => "it exited meanwhile",
        299 => "only part of its memory could be read",
        0 => "Windows refused",
        _ => $"Windows error {win32Error.ToString(CultureInfo.InvariantCulture)}",
    };

    public static MemoryTrimTotals Totals(IEnumerable<MemoryTrimEntry> entries)
    {
        int trimmed = 0, skipped = 0, failed = 0;
        long reclaimed = 0;
        foreach (var entry in entries)
        {
            switch (entry.Outcome)
            {
                case TrimOutcome.Trimmed:
                    trimmed++;
                    reclaimed += entry.Reclaimed;
                    break;
                case TrimOutcome.Skipped:
                    skipped++;
                    break;
                default:
                    failed++;
                    break;
            }
        }

        return new MemoryTrimTotals(trimmed, skipped, failed, reclaimed);
    }

    /// <summary>The order to show them in: the most memory reclaimed first, then failures, then the ones left alone.</summary>
    public static IReadOnlyList<MemoryTrimEntry> Ordered(IEnumerable<MemoryTrimEntry> entries) =>
    [
        .. entries.OrderBy(e => e.Outcome switch { TrimOutcome.Trimmed => 0, TrimOutcome.Failed => 1, _ => 2 })
            .ThenByDescending(e => e.Reclaimed)
            .ThenByDescending(e => e.Before ?? 0)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase),
    ];

    /// <summary>"512.0 MB" (invariant, for the log).</summary>
    public static string Megabytes(long? bytes) => bytes is { } b ? (b / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB" : "?";
}

/// <summary>How many processes were trimmed, skipped and failed, and how much was taken out of working sets.</summary>
public readonly record struct MemoryTrimTotals(int Trimmed, int Skipped, int Failed, long Reclaimed)
{
    /// <summary>Coreloom never ends processes; shown so it's clear.</summary>
    public int Ended => 0;

    /// <summary>"Trimmed 142 · skipped 9 · failed 37 · ended 0 (Coreloom never ends processes)".</summary>
    public string Summary =>
        $"Trimmed {Trimmed.ToString(CultureInfo.InvariantCulture)} · skipped {Skipped.ToString(CultureInfo.InvariantCulture)} · failed {Failed.ToString(CultureInfo.InvariantCulture)} · ended 0 (Coreloom never ends processes)";
}
