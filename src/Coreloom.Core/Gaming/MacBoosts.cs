using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Coreloom.Core.Formatting;

namespace Coreloom.Core.Gaming;

// The thinking behind the Mac's Gaming boosts added in 1.9.4: when to switch High Power Mode, what memory pressure,
// heat and the cores' work mean, which games use MetalFX, what the displays are set to, which programs run under
// Rosetta 2 and which apps are worth quitting while a game plays. The Mac app measures; this decides and words it, and
// is tested on any system.

// ---------------------------------------------------------------------------------------------------- power mode

/// <summary>The charger's power mode as <c>pmset</c> names it (<c>powermode</c>: 0 automatic, 1 low power, 2 high power).</summary>
public static class MacPowerModes
{
    public const int Automatic = 0;
    public const int LowPower = 1;
    public const int HighPower = 2;

    public static string Describe(int mode) => mode switch
    {
        LowPower => "Low Power",
        HighPower => "High Power",
        _ => "Automatic",
    };
}

/// <summary>
/// Whether a demanding workload is running: a game from the library (or an app filed under Games) at once, or the GPU
/// busy (at least 60 %) for 20 seconds, such as a game Coreloom doesn't know or a render. It ends once no game runs and
/// the GPU has been under 30 % for 45 seconds, so loading screens and menus don't switch the power mode back and forth.
/// </summary>
public sealed class WorkloadDetector
{
    public const double BusyGpu = 60;
    public const double QuietGpu = 30;
    public static readonly TimeSpan GpuStart = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan QuietEnd = TimeSpan.FromSeconds(45);

    private DateTime? _busySince;
    private DateTime? _quietSince;

    public bool IsActive { get; private set; }

    /// <summary>What the workload is ("Baldur's Gate 3", "a graphics workload"), or null when there's none.</summary>
    public string? Reason { get; private set; }

    public bool Update(DateTime now, string? game, double? gpuLoad)
    {
        bool busy = gpuLoad >= BusyGpu;
        _busySince = busy ? _busySince ?? now : null;
        bool quiet = game is null && !(gpuLoad >= QuietGpu);
        _quietSince = quiet ? _quietSince ?? now : null;

        if (!IsActive && (game is not null || (_busySince is { } since && now - since >= GpuStart)))
        {
            IsActive = true;
        }
        else if (IsActive && _quietSince is { } calm && now - calm >= QuietEnd)
        {
            IsActive = false;
        }

        Reason = !IsActive ? null : game ?? "a graphics workload";
        return IsActive;
    }
}

public enum PowerStep
{
    None,
    RaiseToHigh,
    Restore,

    /// <summary>The mode was changed by hand meanwhile: nothing to restore, Coreloom just forgets what it remembered.</summary>
    Forget,
}

/// <param name="RestoreTo">The mode to put back (for <see cref="PowerStep.Restore"/>).</param>
public sealed record PowerDecision(PowerStep Step, int? RestoreTo = null);

/// <summary>When High Power Mode automation switches: up on the charger when a workload starts, back when it ends.</summary>
public static class HighPowerAutomation
{
    /// <param name="raisedFrom">The charger's mode before Coreloom raised it (null while Coreloom hasn't).</param>
    public static PowerDecision Decide(bool hasHighPowerMode, bool onCharger, bool workload, int chargerMode, int? raisedFrom)
    {
        if (!hasHighPowerMode)
        {
            return new PowerDecision(PowerStep.None);
        }

        if (raisedFrom is int previous)
        {
            if (workload)
            {
                return new PowerDecision(PowerStep.None);
            }

            return chargerMode == MacPowerModes.HighPower
                ? new PowerDecision(PowerStep.Restore, previous == MacPowerModes.HighPower ? MacPowerModes.Automatic : previous)
                : new PowerDecision(PowerStep.Forget);
        }

        return workload && onCharger && chargerMode != MacPowerModes.HighPower
            ? new PowerDecision(PowerStep.RaiseToHigh)
            : new PowerDecision(PowerStep.None);
    }

    /// <summary>
    /// A macOS user name that's safe to write into a sudoers rule (letters, digits, '.', '_' and '-' only), or null.
    /// </summary>
    public static string? SafeUserName(string? name) =>
        !string.IsNullOrEmpty(name) && name.Length <= 64 && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-') && !name.StartsWith('-')
            ? name
            : null;

    /// <summary>
    /// The one line Coreloom's sudoers file holds: this user may set the charger's power mode to each of its three values
    /// without a password, and nothing else.
    /// </summary>
    public static string SudoersRule(string user) =>
        $"{user} ALL=(root) NOPASSWD: /usr/bin/pmset -c powermode 0, /usr/bin/pmset -c powermode 1, /usr/bin/pmset -c powermode 2";

    public const string SudoersFile = "/etc/sudoers.d/coreloom-powermode";
}

// ---------------------------------------------------------------------------------------------------- memory

/// <summary>macOS's memory pressure (<c>kern.memorystatus_vm_pressure_level</c>: 1 normal, 2 warning, 4 critical).</summary>
public enum MemoryPressure
{
    Normal = 1,
    Warning = 2,
    Critical = 4,
}

/// <summary>An app's memory (its processes together, as Activity Monitor groups them).</summary>
public sealed record AppMemory(string Name, long Bytes);

/// <param name="Apps">The apps using the most memory, largest first.</param>
public sealed record MemorySnapshot(MemoryPressure Pressure, long Total, long Used, long Compressed, long SwapUsed, IReadOnlyList<AppMemory> Apps);

/// <param name="Level">"Normal", "Elevated" or "High".</param>
/// <param name="Suggested">Apps that would free the most memory if quit (never the game, Coreloom or macOS's own).</param>
public sealed record MemoryVerdict(string Level, bool Concern, string Summary, string? Advice, IReadOnlyList<AppMemory> Suggested, double SwapGrowthPerMinute);

/// <summary>
/// The Unified memory pressure guard: memory pressure, compressed memory, swap and its growth, and which apps to quit
/// before memory runs short. It recommends; it never quits anything itself.
/// </summary>
public sealed class MemoryGuard
{
    /// <summary>Swap growing this fast while a game runs (bytes a minute) is a warning even when pressure still reads normal.</summary>
    public const double SwapGrowthWarning = 64L * 1024 * 1024;

    private const long SuggestFrom = 400L * 1024 * 1024;
    private readonly Queue<(DateTime Time, long Swap)> _swap = new();

    private static readonly HashSet<string> System = new(StringComparer.OrdinalIgnoreCase)
    {
        "kernel_task", "WindowServer", "Finder", "Dock", "loginwindow", "launchd", "Coreloom", "mds", "mds_stores", "mdworker",
        "SystemUIServer", "Control Center", "ControlCenter", "Spotlight", "coreaudiod", "bluetoothd", "Notification Center",
    };

    /// <summary>macOS's own processes (and Coreloom): never suggested for quitting or counted as competing with a game.</summary>
    public static bool IsSystem(string name) => System.Contains(name);

    public MemoryVerdict Evaluate(MemorySnapshot memory, DateTime now, string? game)
    {
        _swap.Enqueue((now, memory.SwapUsed));
        while (_swap.Count > 1 && now - _swap.Peek().Time > TimeSpan.FromMinutes(2))
        {
            _swap.Dequeue();
        }

        var (firstTime, firstSwap) = _swap.Peek();
        double minutes = (now - firstTime).TotalMinutes;
        double growth = minutes >= 0.5 ? Math.Max(0, (memory.SwapUsed - firstSwap) / minutes) : 0;

        var suggested = memory.Apps
            .Where(a => a.Bytes >= SuggestFrom && !System.Contains(a.Name) && !SameName(a.Name, game))
            .OrderByDescending(a => a.Bytes)
            .Take(3)
            .ToList();
        string level = memory.Pressure switch
        {
            MemoryPressure.Critical => "High",
            MemoryPressure.Warning => "Elevated",
            _ => game is not null && growth >= SwapGrowthWarning ? "Elevated" : "Normal",
        };
        bool concern = level != "Normal";
        string summary = $"{Memory(memory.Used)} of {Memory(memory.Total)} in use · {Memory(memory.Compressed)} compressed · {Memory(memory.SwapUsed)} swap";
        string? advice = !concern ? null
            : suggested.Count == 0 ? "Memory is tight, but no single app is using much. Lower the game's texture quality to give it room."
            : $"Quit {Join(suggested.Select(a => $"{a.Name} ({Memory(a.Bytes)})"))} to give {(game is null ? "your game" : game)} room before it starts to stutter.";
        return new MemoryVerdict(level, concern, summary, advice, suggested, growth);
    }

    public static string Describe(MemoryPressure pressure) => pressure switch
    {
        MemoryPressure.Critical => "Critical",
        MemoryPressure.Warning => "Warning",
        _ => "Normal",
    };

    internal static bool SameName(string name, string? game) =>
        game is not null && (string.Equals(name, game, StringComparison.OrdinalIgnoreCase)
            || name.Contains(game, StringComparison.OrdinalIgnoreCase) || game.Contains(name, StringComparison.OrdinalIgnoreCase));

    internal static string Memory(long bytes)
    {
        var value = UnitFormatter.Default.MemoryBytes(bytes, bytes >= 1L << 30 ? 1 : 0); // "2.4 GB", "896 MB"
        return $"{value.Number} {value.Unit}";
    }

    internal static string Join(IEnumerable<string> items)
    {
        var list = items.ToList();
        return list.Count switch
        {
            0 => string.Empty,
            1 => list[0],
            _ => string.Join(", ", list.Take(list.Count - 1)) + " and " + list[^1],
        };
    }
}

// ---------------------------------------------------------------------------------------------------- heat

/// <summary>macOS's thermal state (<c>NSProcessInfo.thermalState</c>).</summary>
public enum ThermalPressure
{
    Nominal = 0,
    Fair = 1,
    Serious = 2,
    Critical = 3,
}

/// <summary>A moment of the session: thermal state, temperatures, activity and clocks (null when the Mac doesn't report it).</summary>
public sealed record ThermalSample(DateTime Time, ThermalPressure Pressure, double? CpuTemperature, double? GpuTemperature,
    double? CpuLoad, double? GpuLoad, double? CpuClock, double? GpuClock, double? FanRpm);

/// <param name="Headroom">"Plenty", "Some", "Little" or "None".</param>
/// <param name="ClockDrop">How far the busier chip's clock has fallen under its session peak while still busy (0–1), or null.</param>
public sealed record ThermalVerdict(string Headroom, ThermalPressure Pressure, bool Throttling, int Events, double? ClockDrop, string Advice);

/// <summary>
/// The Thermal headroom optimizer: follows the thermal state, temperatures, activity and clocks over the session, and
/// says how much room is left before heat slows a long game down, and what helps. macOS runs the fans and clocks; this
/// only reads and advises.
/// </summary>
public sealed class ThermalTracker
{
    /// <summary>A clock this far under its peak while the chip stays busy counts as being slowed.</summary>
    public const double ThrottleDrop = 0.15;

    private readonly List<ThermalSample> _samples = [];
    private double _cpuPeak;
    private double _gpuPeak;
    private bool _wasSlowed;

    /// <summary>Times this session macOS reported serious heat, or a busy chip's clock dropped well under its peak.</summary>
    public int Events { get; private set; }

    public IReadOnlyList<ThermalSample> Samples => _samples;

    public void Add(ThermalSample sample)
    {
        _samples.Add(sample);
        _samples.RemoveAll(s => sample.Time - s.Time > TimeSpan.FromMinutes(10));
        if (sample.CpuLoad >= 70 && sample.CpuClock is { } cpu)
        {
            _cpuPeak = Math.Max(_cpuPeak, cpu);
        }

        if (sample.GpuLoad >= 70 && sample.GpuClock is { } gpu)
        {
            _gpuPeak = Math.Max(_gpuPeak, gpu);
        }

        bool slowed = sample.Pressure >= ThermalPressure.Serious || (ClockDrop(sample) ?? 0) >= ThrottleDrop;
        if (slowed && !_wasSlowed)
        {
            Events++;
        }

        _wasSlowed = slowed;
    }

    /// <summary>How far a busy chip's clock is under its session peak (the larger of CPU and GPU), or null.</summary>
    public double? ClockDrop(ThermalSample sample)
    {
        double? Drop(double? load, double? clock, double peak) =>
            load >= 80 && clock is { } c && peak > 0 ? Math.Max(0, 1 - (c / peak)) : null;
        var cpu = Drop(sample.CpuLoad, sample.CpuClock, _cpuPeak);
        var gpu = Drop(sample.GpuLoad, sample.GpuClock, _gpuPeak);
        return cpu is null && gpu is null ? null : Math.Max(cpu ?? 0, gpu ?? 0);
    }

    public ThermalVerdict Evaluate()
    {
        if (_samples.Count == 0)
        {
            return new ThermalVerdict("Plenty", ThermalPressure.Nominal, false, 0, null, "No readings yet.");
        }

        var last = _samples[^1];
        double? drop = ClockDrop(last);
        bool throttling = last.Pressure >= ThermalPressure.Serious || drop >= ThrottleDrop;
        double hottest = Math.Max(last.CpuTemperature ?? 0, last.GpuTemperature ?? 0);
        string headroom = last.Pressure == ThermalPressure.Critical ? "None"
            : last.Pressure == ThermalPressure.Serious || throttling || hottest >= 95 ? "Little"
            : last.Pressure == ThermalPressure.Fair || hottest >= 88 ? "Some"
            : "Plenty";
        string advice = headroom switch
        {
            "None" => "Very hot: macOS is holding the chip back hard. Pause the game, let the Mac cool and check nothing covers its vents.",
            "Little" => "Heat is slowing the chip. Lower the game's graphics settings or cap its frame rate, keep the vents clear"
                + " and, on a MacBook Pro with High Power Mode, play on the charger so the fans can run faster.",
            "Some" => "Warming up, still at full speed. Keep the vents clear and the Mac on a hard, flat surface for long sessions.",
            _ => "Cool and at full speed: there's room for long sessions.",
        };
        return new ThermalVerdict(headroom, last.Pressure, throttling, Events, drop, advice);
    }

    public static string Describe(ThermalPressure pressure) => pressure switch
    {
        ThermalPressure.Critical => "Critical",
        ThermalPressure.Serious => "Serious",
        ThermalPressure.Fair => "Fair",
        _ => "Nominal",
    };
}

// ---------------------------------------------------------------------------------------------------- cores

/// <summary>
/// A process's processor time so far: <paramref name="CpuTicks"/> from its task counters (in the timebase's ticks, for how
/// much it used), and from <c>proc_pid_rusage</c> (<c>RUSAGE_INFO_V6</c>) its total, the part on performance cores and the
/// part at each quality-of-service class, used only as shares of one another (so their unit doesn't matter).
/// </summary>
public sealed record CoreTimes(int ProcessId, string Name, ulong CpuTicks, ulong Total, ulong Performance,
    ulong Interactive, ulong Initiated, ulong Default, ulong Utility, ulong Background);

/// <param name="CoresUsed">Processor time over the last interval in cores (1 = one core fully busy).</param>
/// <param name="PerformanceShare">The part of it on performance cores (0–1).</param>
/// <param name="Priority">"High", "Default", "Utility" or "Background": the QoS class most of its time ran at.</param>
/// <param name="Competes">Busy on the performance cores at a high or default priority while a game plays.</param>
public sealed record CoreUser(string Name, double CoresUsed, double PerformanceShare, string Priority, bool IsGame, bool Competes);

/// <summary>
/// The Performance core workload monitor: which programs use the performance and the efficiency cores, at which QoS
/// class, and which compete with the game for the performance cores.
/// </summary>
public sealed class CoreWorkloadMonitor
{
    /// <summary>A program using at least this much of a core on performance cores at high priority competes with the game.</summary>
    public const double CompetingCores = 0.15;

    private Dictionary<int, CoreTimes> _previous = [];
    private DateTime _previousTime;

    /// <param name="ticksToSeconds">Seconds per tick of <see cref="CoreTimes.CpuTicks"/> (Apple silicon's timebase).</param>
    public IReadOnlyList<CoreUser> Update(IReadOnlyList<CoreTimes> now, DateTime time, double ticksToSeconds, string? game)
    {
        var users = new List<CoreUser>();
        double elapsed = (time - _previousTime).TotalSeconds;
        if (_previous.Count > 0 && elapsed > 0.2)
        {
            var groups = new Dictionary<string, (double Ticks, double Total, double P, double High, double Def, double Util, double Bg)>(StringComparer.Ordinal);
            foreach (var current in now)
            {
                if (!_previous.TryGetValue(current.ProcessId, out var before) || current.CpuTicks < before.CpuTicks || current.Total < before.Total)
                {
                    continue;
                }

                double ticks = current.CpuTicks - before.CpuTicks;
                double total = current.Total - before.Total;
                if (ticks <= 0)
                {
                    continue;
                }

                groups.TryGetValue(current.Name, out var g);
                groups[current.Name] = (
                    g.Ticks + ticks,
                    g.Total + total,
                    g.P + Delta(current.Performance, before.Performance),
                    g.High + Delta(current.Interactive, before.Interactive) + Delta(current.Initiated, before.Initiated),
                    g.Def + Delta(current.Default, before.Default),
                    g.Util + Delta(current.Utility, before.Utility),
                    g.Bg + Delta(current.Background, before.Background));
            }

            foreach (var (name, g) in groups)
            {
                double cores = g.Ticks * ticksToSeconds / elapsed;
                if (cores < 0.01)
                {
                    continue;
                }

                double pShare = g.Total > 0 ? Math.Clamp(g.P / g.Total, 0, 1) : 0;
                double qos = g.High + g.Def + g.Util + g.Bg;
                string priority = qos <= 0 ? "Default"
                    : g.High >= Math.Max(g.Def, Math.Max(g.Util, g.Bg)) ? "High"
                    : g.Def >= Math.Max(g.Util, g.Bg) ? "Default"
                    : g.Util >= g.Bg ? "Utility" : "Background";
                bool isGame = MemoryGuard.SameName(name, game);
                bool competes = game is not null && !isGame && !MemoryGuard.IsSystem(name) && cores * pShare >= CompetingCores && priority is "High" or "Default";
                users.Add(new CoreUser(name, cores, pShare, priority, isGame, competes));
            }
        }

        _previous = now.GroupBy(t => t.ProcessId).ToDictionary(g => g.Key, g => g.First());
        _previousTime = time;
        return [.. users.OrderByDescending(u => u.CoresUsed)];
    }

    private static double Delta(ulong now, ulong before) => now >= before ? now - before : 0;

    public static string DescribePriority(string priority) => priority switch
    {
        "High" => "user-interactive / user-initiated",
        "Utility" => "utility",
        "Background" => "background",
        _ => "default",
    };
}

// ---------------------------------------------------------------------------------------------------- MetalFX

[Flags]
public enum MetalFxFeatures
{
    None = 0,

    /// <summary>The game links Apple's MetalFX framework.</summary>
    Linked = 1,
    SpatialUpscaling = 2,
    TemporalUpscaling = 4,
    FrameInterpolation = 8,
    Denoising = 16,
}

/// <summary>
/// The MetalFX graphics optimizer: which of the game's MetalFX features its program (and the frameworks inside it)
/// refers to — Apple's class names appear in every program that uses them — and what to turn on in the game.
/// </summary>
public static class MetalFx
{
    private static readonly (byte[] Marker, MetalFxFeatures Feature)[] Markers =
    [
        (Encoding.ASCII.GetBytes("MetalFX.framework"), MetalFxFeatures.Linked),
        (Encoding.ASCII.GetBytes("MTLFXSpatialScaler"), MetalFxFeatures.SpatialUpscaling),
        (Encoding.ASCII.GetBytes("MTLFXTemporalScaler"), MetalFxFeatures.TemporalUpscaling),
        (Encoding.ASCII.GetBytes("MTLFXFrameInterpolator"), MetalFxFeatures.FrameInterpolation),
        (Encoding.ASCII.GetBytes("MTLFXTemporalDenoisedScaler"), MetalFxFeatures.Denoising),
    ];

    private static readonly int Longest = Markers.Max(m => m.Marker.Length);

    public static MetalFxFeatures Scan(ReadOnlySpan<byte> data)
    {
        var found = MetalFxFeatures.None;
        foreach (var (marker, feature) in Markers)
        {
            if (data.IndexOf(marker) >= 0)
            {
                found |= feature;
            }
        }

        return found;
    }

    /// <summary>Reads a file in pieces (overlapping by the longest marker), up to <paramref name="maxBytes"/>.</summary>
    public static MetalFxFeatures Scan(Stream stream, long maxBytes = 1L << 30, int chunk = 8 * 1024 * 1024)
    {
        int overlap = Longest - 1;
        var found = MetalFxFeatures.None;
        byte[] buffer = new byte[chunk + overlap];
        int carry = 0;
        long read = 0;
        while (read < maxBytes)
        {
            int n = stream.Read(buffer, carry, (int)Math.Min(chunk, maxBytes - read));
            if (n <= 0)
            {
                break;
            }

            read += n;
            int length = carry + n;
            found |= Scan(buffer.AsSpan(0, length));
            int keep = Math.Min(overlap, length);
            Buffer.BlockCopy(buffer, length - keep, buffer, 0, keep);
            carry = keep;
        }

        return found;
    }

    /// <summary>
    /// A program file: in a Mach-O program, only the parts where names are kept (<see cref="MachO.NameRegions"/>), each at
    /// most 256 MB, so even a game's multi-gigabyte program is checked in a moment; nothing for other files.
    /// </summary>
    public static MetalFxFeatures ScanProgram(Stream stream)
    {
        long length = stream.Length;
        byte[] start = new byte[Math.Min(4096, (int)Math.Min(length, 4096))];
        stream.Position = 0;
        stream.ReadExactly(start);
        if (MachO.PreferredSlice(start, length) is not var (sliceOffset, sliceSize))
        {
            return MetalFxFeatures.None;
        }

        stream.Position = sliceOffset;
        byte[] first = new byte[(int)Math.Min(32, sliceSize)];
        stream.ReadExactly(first);
        int headerSize = MachO.HeaderSize(first);
        if (headerSize == 0 || headerSize > sliceSize)
        {
            return MetalFxFeatures.None;
        }

        byte[] header = new byte[headerSize];
        stream.Position = sliceOffset;
        stream.ReadExactly(header);
        var found = MetalFxFeatures.None;
        foreach (var (offset, size) in MachO.NameRegions(header))
        {
            if (offset < 0 || offset >= sliceSize)
            {
                continue;
            }

            stream.Position = sliceOffset + offset;
            found |= Scan(stream, Math.Min(Math.Min(size, sliceSize - offset), 256L * 1024 * 1024));
        }

        return found;
    }

    /// <summary>"Temporal upscaling · Frame interpolation", "Linked (no features found)" or "Not used".</summary>
    public static string Describe(MetalFxFeatures features)
    {
        var names = new List<string>();
        if (features.HasFlag(MetalFxFeatures.TemporalUpscaling))
        {
            names.Add("Temporal upscaling");
        }

        if (features.HasFlag(MetalFxFeatures.SpatialUpscaling))
        {
            names.Add("Spatial upscaling");
        }

        if (features.HasFlag(MetalFxFeatures.FrameInterpolation))
        {
            names.Add("Frame interpolation");
        }

        if (features.HasFlag(MetalFxFeatures.Denoising))
        {
            names.Add("Denoising");
        }

        return names.Count > 0 ? string.Join(" · ", names) : features.HasFlag(MetalFxFeatures.Linked) ? "MetalFX, features not named" : "Not used";
    }

    public static bool Supports(MetalFxFeatures features) => features != MetalFxFeatures.None;

    /// <summary>What to turn on in the game's settings.</summary>
    public static string Advice(MetalFxFeatures features)
    {
        if (!Supports(features))
        {
            return "No MetalFX in this game: its own resolution scale or graphics presets are the way to more frames.";
        }

        var steps = new List<string>();
        if (features.HasFlag(MetalFxFeatures.TemporalUpscaling) || features.HasFlag(MetalFxFeatures.Denoising))
        {
            steps.Add("choose MetalFX upscaling (Temporal for the sharpest image)");
        }
        else if (features.HasFlag(MetalFxFeatures.SpatialUpscaling))
        {
            steps.Add("choose MetalFX upscaling");
        }

        if (features.HasFlag(MetalFxFeatures.FrameInterpolation))
        {
            steps.Add("turn on MetalFX frame generation for a higher frame rate");
        }

        if (features.HasFlag(MetalFxFeatures.Denoising))
        {
            steps.Add("use its MetalFX denoiser with ray tracing");
        }

        return steps.Count > 0
            ? "In the game's graphics settings, " + MemoryGuard.Join(steps) + "."
            : "The game links MetalFX: look for an upscaling or MetalFX option in its graphics settings.";
    }
}

// ---------------------------------------------------------------------------------------------------- displays

/// <param name="PixelWidth">What the GPU draws (the backing pixels; more than the panel's for a "More Space" setting).</param>
/// <param name="Width">How big things look, in points.</param>
public sealed record DisplayMode(int PixelWidth, int PixelHeight, int Width, int Height, double RefreshRate);

/// <param name="MaxRefreshRate">The highest refresh rate it offers at its current size (0 = unknown).</param>
/// <param name="VariableRefresh">ProMotion or adaptive sync: the refresh follows the frames.</param>
/// <param name="EdrHeadroom">How much brighter than white HDR can go now (1 = no HDR headroom).</param>
/// <param name="NativeWidth">The panel's own pixels (0 = unknown).</param>
public sealed record DisplayDescription(string Name, bool BuiltIn, DisplayMode Mode, double MaxRefreshRate, bool VariableRefresh,
    double EdrHeadroom, int BitsPerComponent, int NativeWidth, int NativeHeight);

/// <param name="Name">The display.</param>
/// <param name="Value">Its refresh and size in short ("120 Hz ProMotion · 3024 × 1964").</param>
/// <param name="Good">Nothing to change.</param>
/// <param name="Detail">HDR, colour depth and what to change.</param>
public sealed record DisplayVerdict(string Name, string Value, bool Good, string Detail);

/// <summary>The ProMotion &amp; display pipeline optimizer: each display's refresh, HDR, resolution and colour depth, and what to change.</summary>
public static class DisplayPipeline
{
    public static IReadOnlyList<DisplayVerdict> Evaluate(IReadOnlyList<DisplayDescription> displays) => [.. displays.Select(Evaluate)];

    public static DisplayVerdict Evaluate(DisplayDescription d)
    {
        var ci = CultureInfo.InvariantCulture;
        double hz = Math.Round(d.Mode.RefreshRate);
        double max = Math.Round(d.MaxRefreshRate);
        string refresh = hz <= 0 ? "Refresh unknown" : d.VariableRefresh ? $"Up to {hz.ToString("0", ci)} Hz {(d.BuiltIn ? "ProMotion" : "adaptive")}" : $"{hz.ToString("0", ci)} Hz";
        string size = $"{d.Mode.PixelWidth.ToString(ci)} × {d.Mode.PixelHeight.ToString(ci)}";
        var notes = new List<string>();
        var advice = new List<string>();

        if (max > hz + 1 && hz > 0)
        {
            advice.Add(d.BuiltIn
                ? $"Refresh rate is set to {hz.ToString("0", ci)} Hz: choose ProMotion in System Settings › Displays for up to {max.ToString("0", ci)} Hz."
                : $"It can run at {max.ToString("0", ci)} Hz: choose that refresh rate in System Settings › Displays.");
        }

        notes.Add(d.EdrHeadroom > 1.05 ? $"HDR ready ({d.EdrHeadroom.ToString("0.#", ci)}× SDR brightness)" : "SDR");
        if (d.BitsPerComponent > 0)
        {
            notes.Add($"{d.BitsPerComponent.ToString(ci)}-bit colour");
        }

        if (d.Mode.Width > 0 && d.Mode.PixelWidth >= d.Mode.Width * 2 - 1)
        {
            notes.Add($"looks like {d.Mode.Width.ToString(ci)} × {d.Mode.Height.ToString(ci)}");
        }

        if (d.NativeWidth > 0 && d.Mode.PixelWidth > d.NativeWidth + 8)
        {
            advice.Add($"It draws {size} and scales down to the panel's {d.NativeWidth.ToString(ci)} × {d.NativeHeight.ToString(ci)} (a \"More Space\" setting): games in a window cost the GPU more. The Default size avoids that.");
        }

        string detail = string.Join(" · ", notes) + (advice.Count > 0 ? ". " + string.Join(" ", advice) : ".");
        return new DisplayVerdict(d.Name, $"{refresh} · {size}", advice.Count == 0, detail);
    }
}

// ---------------------------------------------------------------------------------------------------- Apple silicon

public enum CpuArchitecture
{
    Unknown,
    Arm64,
    X86_64,

    /// <summary>Both, so it runs natively on either kind of Mac.</summary>
    Universal,
}

/// <summary>Reads which processors a Mach-O program (thin or universal) was built for, from its first bytes.</summary>
public static class MachO
{
    public const int CpuTypeX86_64 = 0x01000007;
    public const int CpuTypeArm64 = 0x0100000C;

    /// <summary>The CPU types of its slices, or empty when it isn't a Mach-O program.</summary>
    public static IReadOnlyList<int> CpuTypes(ReadOnlySpan<byte> header)
    {
        if (header.Length < 8)
        {
            return [];
        }

        uint big = BinaryPrimitives.ReadUInt32BigEndian(header);
        if (big is 0xCAFEBABE or 0xCAFEBABF)
        {
            // Universal: big-endian header, then 20-byte (or 64-bit: 32-byte) entries. Java class files share the magic;
            // their "count" is a version number in the 40s or more, so a short list is required.
            uint count = BinaryPrimitives.ReadUInt32BigEndian(header[4..]);
            int entry = big == 0xCAFEBABF ? 32 : 20;
            if (count is 0 or > 16 || header.Length < 8 + (count * entry))
            {
                return [];
            }

            var types = new List<int>();
            for (int i = 0; i < count; i++)
            {
                types.Add(BinaryPrimitives.ReadInt32BigEndian(header[(8 + (i * entry))..]));
            }

            return types;
        }

        uint little = BinaryPrimitives.ReadUInt32LittleEndian(header);
        return little is 0xFEEDFACF or 0xFEEDFACE ? [BinaryPrimitives.ReadInt32LittleEndian(header[4..])] : [];
    }

    /// <summary>
    /// In a universal program, the slice to read (Apple silicon's first, then Intel's, then the first); a thin program is
    /// one slice from the start. Null when it isn't a Mach-O program.
    /// </summary>
    public static (long Offset, long Size)? PreferredSlice(ReadOnlySpan<byte> header, long fileLength)
    {
        if (header.Length < 8)
        {
            return null;
        }

        uint big = BinaryPrimitives.ReadUInt32BigEndian(header);
        if (big is 0xCAFEBABE or 0xCAFEBABF)
        {
            uint count = BinaryPrimitives.ReadUInt32BigEndian(header[4..]);
            bool wide = big == 0xCAFEBABF;
            int entry = wide ? 32 : 20;
            if (count is 0 or > 16 || header.Length < 8 + (count * entry))
            {
                return null;
            }

            (long, long)? first = null, intel = null;
            for (int i = 0; i < count; i++)
            {
                var e = header[(8 + (i * entry))..];
                int type = BinaryPrimitives.ReadInt32BigEndian(e);
                long offset = wide ? (long)BinaryPrimitives.ReadUInt64BigEndian(e[8..]) : BinaryPrimitives.ReadUInt32BigEndian(e[8..]);
                long size = wide ? (long)BinaryPrimitives.ReadUInt64BigEndian(e[16..]) : BinaryPrimitives.ReadUInt32BigEndian(e[12..]);
                if (offset <= 0 || size <= 0 || offset + size > fileLength)
                {
                    continue;
                }

                if (type == CpuTypeArm64)
                {
                    return (offset, size);
                }

                if (type == CpuTypeX86_64)
                {
                    intel ??= (offset, size);
                }

                first ??= (offset, size);
            }

            return intel ?? first;
        }

        return BinaryPrimitives.ReadUInt32LittleEndian(header) == 0xFEEDFACF ? (0, fileLength) : null;
    }

    /// <summary>How many bytes of a 64-bit slice's start hold its header and load commands (0 when it isn't one).</summary>
    public static int HeaderSize(ReadOnlySpan<byte> slice) =>
        slice.Length >= 32 && BinaryPrimitives.ReadUInt32LittleEndian(slice) == 0xFEEDFACF
            ? 32 + (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(slice[20..]), 4 * 1024 * 1024)
            : 0;

    /// <summary>
    /// The parts of a 64-bit slice where the names a program uses are kept: its load commands (the frameworks it links),
    /// the <c>__LINKEDIT</c> segment (the symbols it imports) and the <c>__TEXT</c> string sections (class and method
    /// names, C strings). Offsets are from the slice's start. <paramref name="header"/> holds <see cref="HeaderSize"/> bytes.
    /// </summary>
    public static IReadOnlyList<(long Offset, long Length)> NameRegions(ReadOnlySpan<byte> header)
    {
        int size = HeaderSize(header);
        if (size == 0 || header.Length < size)
        {
            return [];
        }

        var regions = new List<(long, long)> { (0, size) };
        uint commands = BinaryPrimitives.ReadUInt32LittleEndian(header[16..]);
        int at = 32;
        for (uint i = 0; i < commands && at + 8 <= size; i++)
        {
            uint cmd = BinaryPrimitives.ReadUInt32LittleEndian(header[at..]);
            int cmdSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(header[(at + 4)..]);
            if (cmdSize < 8 || at + cmdSize > size)
            {
                break;
            }

            if (cmd == 0x19 && cmdSize >= 72) // LC_SEGMENT_64
            {
                var segment = header.Slice(at, cmdSize);
                string name = Name(segment[8..24]);
                long fileOffset = (long)BinaryPrimitives.ReadUInt64LittleEndian(segment[40..]);
                long fileSize = (long)BinaryPrimitives.ReadUInt64LittleEndian(segment[48..]);
                uint sections = BinaryPrimitives.ReadUInt32LittleEndian(segment[64..]);
                if (name == "__LINKEDIT" && fileSize > 0)
                {
                    regions.Add((fileOffset, fileSize));
                }
                else if (name == "__TEXT")
                {
                    for (int s = 0; s < sections && 72 + ((s + 1) * 80) <= cmdSize; s++)
                    {
                        var section = segment[(72 + (s * 80))..];
                        string sectionName = Name(section[..16]);
                        long length = (long)BinaryPrimitives.ReadUInt64LittleEndian(section[40..]);
                        uint offset = BinaryPrimitives.ReadUInt32LittleEndian(section[48..]);
                        if (sectionName is "__cstring" or "__objc_classname" or "__objc_methname" && length > 0 && offset > 0)
                        {
                            regions.Add((offset, length));
                        }
                    }
                }
            }

            at += cmdSize;
        }

        return regions;
    }

    private static string Name(ReadOnlySpan<byte> bytes)
    {
        int end = bytes.IndexOf((byte)0);
        return Encoding.ASCII.GetString(end < 0 ? bytes : bytes[..end]);
    }

    public static CpuArchitecture Architecture(ReadOnlySpan<byte> header)
    {
        var types = CpuTypes(header);
        bool arm = types.Contains(CpuTypeArm64);
        bool intel = types.Contains(CpuTypeX86_64);
        return arm && intel ? CpuArchitecture.Universal : arm ? CpuArchitecture.Arm64 : intel ? CpuArchitecture.X86_64 : CpuArchitecture.Unknown;
    }
}

/// <summary>The Native Apple silicon check's words.</summary>
public static class NativeApps
{
    public static string Describe(CpuArchitecture architecture) => architecture switch
    {
        CpuArchitecture.Universal => "Universal (native)",
        CpuArchitecture.Arm64 => "Apple silicon (native)",
        CpuArchitecture.X86_64 => "Intel (Rosetta 2)",
        _ => "Not a Mac program",
    };

    public static bool RunsTranslated(CpuArchitecture architecture) => architecture == CpuArchitecture.X86_64;

    public static string Advice(string name, CpuArchitecture architecture) => architecture switch
    {
        CpuArchitecture.X86_64 => $"{name} is built for Intel and runs through Rosetta 2. Look for an Apple silicon or Universal version (in its store or on its website) for better frame rates and battery life.",
        CpuArchitecture.Unknown => $"{name} isn't a Mac program (a Windows game through a translation layer, or a script).",
        _ => $"{name} runs natively on Apple silicon.",
    };
}

// ---------------------------------------------------------------------------------------------------- background apps

/// <summary>An app running now (one with a Dock icon or a menu bar item).</summary>
/// <param name="Cpu">Percent of one core.</param>
/// <param name="Gpu">Percent of the GPU's time, or null when macOS doesn't say.</param>
public sealed record RunningAppInfo(int ProcessId, string Name, string? BundleId, string? BundlePath, double Cpu, long Memory, double? Gpu, bool IsRegularApp);

/// <param name="Heavy">Using enough processor, graphics or memory to be worth quitting while a game plays.</param>
/// <param name="Usage">"12 % CPU · 1.4 GB"</param>
public sealed record GovernorCandidate(RunningAppInfo App, bool Heavy, string Usage);

/// <summary>
/// The Background application governor: the apps worth reviewing while a game plays (never the game, Coreloom, game
/// launchers or macOS's own), heaviest first.
/// </summary>
public static class BackgroundGovernor
{
    public const double HeavyCpu = 5;
    public const double HeavyGpu = 3;
    public const long HeavyMemory = 600L * 1024 * 1024;

    /// <summary>Kept running whatever they use: macOS's own and the launchers games need.</summary>
    public static readonly HashSet<string> Essential = new(StringComparer.OrdinalIgnoreCase)
    {
        "com.apple.finder", "com.apple.dock", "com.apple.systemuiserver", "com.apple.controlcenter", "com.apple.notificationcenterui",
        "com.apple.loginwindow", "com.apple.WindowManager", "com.apple.Spotlight", "com.apple.TextInputMenuAgent", "com.apple.wallpaper.agent",
        "com.apple.ActivityMonitor", "com.jerryubah.coreloom",
        "com.valvesoftware.steam", "com.epicgames.EpicGamesLauncher", "com.blizzard.bnetlauncher", "com.gog.galaxy", "com.ea.app",
        "com.ubisoft.uplay", "com.riotgames.RiotGames.RiotClient", "com.apple.gamecenter",
    };

    public static bool IsEssential(RunningAppInfo app) =>
        (app.BundleId is { } id && Essential.Contains(id)) || string.Equals(app.Name, "Coreloom", StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<GovernorCandidate> Rank(IReadOnlyList<RunningAppInfo> apps, string? game, int? gameProcessId) =>
        [.. apps
            .Where(a => a.IsRegularApp && !IsEssential(a) && a.ProcessId != gameProcessId && !MemoryGuard.SameName(a.Name, game))
            .Select(a => new GovernorCandidate(a, a.Cpu >= HeavyCpu || a.Gpu >= HeavyGpu || a.Memory >= HeavyMemory, Usage(a)))
            .OrderByDescending(c => c.Heavy)
            .ThenByDescending(c => c.App.Cpu + ((c.App.Gpu ?? 0) * 2) + (c.App.Memory / (256.0 * 1024 * 1024)))];

    public static string Usage(RunningAppInfo app)
    {
        var ci = CultureInfo.InvariantCulture;
        var parts = new List<string> { $"{app.Cpu.ToString(app.Cpu < 10 ? "0.#" : "0", ci)} % CPU" };
        if (app.Gpu is { } gpu && gpu >= 0.5)
        {
            parts.Add($"{gpu.ToString("0", ci)} % GPU");
        }

        parts.Add(MemoryGuard.Memory(app.Memory));
        return string.Join(" · ", parts);
    }
}

/// <summary>A game's background apps: the ones to quit each time it starts (by bundle id).</summary>
public sealed class QuitProfile
{
    public const int MaxGames = 40;
    public const int MaxApps = 24;

    public string GameId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public List<string> BundleIds { get; set; } = [];

    public static List<QuitProfile> Normalize(List<QuitProfile>? profiles) =>
        [.. (profiles ?? [])
            .Where(p => p is not null && !string.IsNullOrWhiteSpace(p.GameId))
            .Select(p =>
            {
                p.Name ??= string.Empty;
                p.BundleIds = [.. (p.BundleIds ?? []).Where(b => !string.IsNullOrWhiteSpace(b)).Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxApps)];
                return p;
            })
            .Where(p => p.BundleIds.Count > 0)
            .DistinctBy(p => p.GameId, StringComparer.OrdinalIgnoreCase)
            .Take(MaxGames)];
}
