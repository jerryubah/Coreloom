using System.Runtime.InteropServices;
using Coreloom.ViewModels.Connect;

namespace Coreloom.App.Overlay;

/// <summary>
/// The frame rate of the program in front for the phone's overlay (Coreloom Connect), from the same graphics events the
/// in-game overlay uses. It has its own share of the measurement, so the phone's overlay works with the in-game overlay
/// off, and stops measuring when no phone shows its overlay any more.
/// </summary>
internal sealed partial class PhoneFrameRates(EtwFrameRateSource source) : IForegroundFrameRates
{
    private const string Owner = "phone overlay";
    private readonly int _ownProcessId = Environment.ProcessId;
    private readonly object _gate = new();
    private int _lastProgramId;
    private string? _lastProgram;

    public string? Problem => source.Problem;

    public bool Start() => source.Acquire(Owner);

    public void Stop() => source.Release(Owner);

    public ForegroundFrames Read(Span<double> frameTimes)
    {
        IntPtr foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero || GetWindowThreadProcessId(foreground, out uint pid) == 0 || pid == _ownProcessId)
        {
            return default; // nothing, or Coreloom itself, in front: no game
        }

        int processId = (int)pid;
        var stats = source.Get(processId);
        int count = 0;
        double newestAge = 0;
        if (stats is not null)
        {
            count = source.RecentFrameTimes(processId, frameTimes, out newestAge);
        }

        return new ForegroundFrames(ProgramName(processId), stats, count, newestAge);
    }

    /// <summary>"Cyberpunk2077.exe" (remembered for the last program asked about).</summary>
    private string? ProgramName(int processId)
    {
        lock (_gate)
        {
            if (processId == _lastProgramId)
            {
                return _lastProgram;
            }
        }

        string? name = null;
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(processId);
            name = process.ProcessName + ".exe";
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // gone already, or not ours to look at
        }

        lock (_gate)
        {
            _lastProgramId = processId;
            _lastProgram = name;
        }

        return name;
    }

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
}
