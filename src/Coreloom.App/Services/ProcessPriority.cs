using System.ComponentModel;
using System.Diagnostics;
using Coreloom.Core.Diagnostics;

namespace Coreloom.App.Services;

/// <summary>
/// Coreloom's process priority: below normal while anything asks it to give way to a game (the in-game overlay with
/// another program in front, or lighter monitoring with a game in front), normal otherwise.
/// </summary>
internal static class ProcessPriority
{
    private static readonly object Gate = new();
    private static readonly HashSet<string> Reasons = new(StringComparer.Ordinal);
    private static bool _lowered;

    public static bool IsLowered
    {
        get
        {
            lock (Gate)
            {
                return _lowered;
            }
        }
    }

    /// <summary>Asks (or stops asking) for below-normal priority for <paramref name="reason"/>.</summary>
    public static void GiveWay(string reason, bool giveWay)
    {
        lock (Gate)
        {
            if (giveWay)
            {
                Reasons.Add(reason);
            }
            else
            {
                Reasons.Remove(reason);
            }

            bool lower = Reasons.Count > 0;
            if (lower == _lowered)
            {
                return;
            }

            try
            {
                using var process = Process.GetCurrentProcess();
                process.PriorityClass = lower ? ProcessPriorityClass.BelowNormal : ProcessPriorityClass.Normal;
                _lowered = lower;
                Log.Info("App", lower ? $"Coreloom gives way to the game in front (below-normal priority: {string.Join(", ", Reasons)})." : "Coreloom back to normal priority.");
            }
            catch (Exception e) when (e is Win32Exception or InvalidOperationException or NotSupportedException)
            {
                Log.WarnThrottled("process-priority", TimeSpan.FromMinutes(10), "App", "Changing Coreloom's priority failed.", e);
            }
        }
    }
}
