using System.Buffers.Binary;
using System.Globalization;

namespace Coreloom.Core.Gaming;

/// <summary>What a group of cores is best at.</summary>
public enum CoreKind
{
    /// <summary>Cores alike in what they offer (one CCD, or CCDs with the same cache).</summary>
    Standard,

    /// <summary>AMD Ryzen X3D: the CCD with the stacked 3D V-Cache (best for most games).</summary>
    Cache,

    /// <summary>AMD Ryzen X3D: the CCD without it, which clocks higher.</summary>
    Frequency,

    /// <summary>Hybrid processors (Intel P-cores, AMD Zen 5 next to Zen 5c): the fast cores.</summary>
    Performance,

    /// <summary>Hybrid processors: the small, efficient cores.</summary>
    Efficiency,
}

/// <summary>A processor core as Windows reports it: the logical processors it runs (one, or two with SMT) and its efficiency class.</summary>
/// <param name="Group">The processor group (64 logical processors each).</param>
/// <param name="Mask">Its logical processors within the group.</param>
/// <param name="EfficiencyClass">Higher is faster on hybrid processors; all equal elsewhere.</param>
public readonly record struct CoreUnit(int Group, ulong Mask, byte EfficiencyClass);

/// <summary>A cache as Windows reports it: its level, size and the logical processors sharing it.</summary>
public readonly record struct CacheUnit(int Level, long Bytes, int Group, ulong Mask);

/// <summary>Cores that belong together: a CCD (cores sharing one L3 cache) or one kind of core on a hybrid processor.</summary>
/// <param name="Index">Position in the layout (0 first).</param>
/// <param name="Kind">What the cores are best at.</param>
/// <param name="Name">"Cache cores", "Frequency cores", "Performance cores", "CCD 2", "All cores".</param>
/// <param name="Processors">Their logical processors (indexes as Windows' per-processor counters number them).</param>
/// <param name="Cores">How many physical cores.</param>
/// <param name="L3Bytes">The L3 cache one of its CCDs has (0 when unknown).</param>
/// <param name="Mask">Their logical processors as an affinity mask (processor group 0 only; 0 when they're in another group).</param>
public sealed record CoreGroup(int Index, CoreKind Kind, string Name, IReadOnlyList<int> Processors, int Cores, long L3Bytes, ulong Mask)
{
    /// <summary>"8 cores · 96 MB L3", "8 cores · 16 threads".</summary>
    public string Description
    {
        get
        {
            string cores = $"{Cores.ToString(CultureInfo.CurrentCulture)} {(Cores == 1 ? "core" : "cores")}";
            if (L3Bytes > 0 && Kind is CoreKind.Cache or CoreKind.Frequency or CoreKind.Standard)
            {
                return $"{cores} · {CoreLayout.Megabytes(L3Bytes)} L3";
            }

            return Processors.Count > Cores ? $"{cores} · {Processors.Count.ToString(CultureInfo.CurrentCulture)} threads" : cores;
        }
    }
}

/// <summary>
/// The processor's cores grouped by what they're good at, worked out from what Windows reports about each core and
/// cache (<c>GetLogicalProcessorInformationEx</c>): on a multi-CCD Ryzen X3D the CCD with the larger L3 cache holds the
/// 3D V-Cache, on a hybrid processor the highest efficiency class marks the performance cores, and CCDs with the same
/// cache are listed as they are.
/// </summary>
public sealed record CoreLayout(IReadOnlyList<CoreGroup> Groups, int LogicalProcessors)
{
    /// <summary>One CCD is "cache" when its L3 is at least this much larger than the smallest one.</summary>
    public const double CacheRatio = 1.5;

    /// <summary>An L3 this large on every CCD means every core has 3D V-Cache (5800X3D, 9800X3D).</summary>
    public const long VCacheBytes = 64L * 1024 * 1024;

    public static CoreLayout Unknown { get; } = new([], 0);

    public bool IsKnown => Groups.Count > 0;

    /// <summary>The cores differ (or sit on separate CCDs), so where a game runs can matter.</summary>
    public bool HasChoice => Groups.Count > 1;

    /// <summary>A multi-CCD Ryzen X3D: cache cores and frequency cores.</summary>
    public bool IsVCache => Groups.Any(g => g.Kind == CoreKind.Cache) && Groups.Any(g => g.Kind == CoreKind.Frequency);

    /// <summary>Performance and efficiency cores.</summary>
    public bool IsHybrid => Groups.Any(g => g.Kind == CoreKind.Performance) && Groups.Any(g => g.Kind == CoreKind.Efficiency);

    /// <summary>One CCD, and its L3 is 3D V-Cache sized: every core has it.</summary>
    public bool HasVCacheEverywhere => Groups.Count == 1 && Groups[0].L3Bytes >= VCacheBytes;

    /// <summary>Every logical processor is in processor group 0, so a process's cores can be chosen with one mask.</summary>
    public bool CanSetAffinity => IsKnown && LogicalProcessors <= 64 && Groups.All(g => g.Mask != 0 && BitCount(g.Mask) == g.Processors.Count);

    /// <summary>Every logical processor.</summary>
    public ulong AllMask => Groups.Aggregate(0UL, (mask, g) => mask | g.Mask);

    /// <summary>
    /// How many separate L3 caches the processor has (since 1.8.5): 2 on a two-CCD Ryzen, 1 on a one-CCD Ryzen or an
    /// Intel processor (whose L3 every core shares), 0 when unknown.
    /// </summary>
    public int L3Caches { get; init; }

    /// <summary>
    /// The cores games are best kept on: the cache cores, the performance cores, or the first CCD when CCDs are alike
    /// (which keeps a game's threads on one L3 cache). Null when there's nothing to choose.
    /// </summary>
    public CoreGroup? Preferred => !HasChoice
        ? null
        : Groups.FirstOrDefault(g => g.Kind == CoreKind.Cache)
            ?? Groups.FirstOrDefault(g => g.Kind == CoreKind.Performance)
            ?? Groups[0];

    /// <summary>The other cores (where a game shouldn't be pushed).</summary>
    public IEnumerable<CoreGroup> Others => Groups.Where(g => g != Preferred);

    public CoreGroup? Group(CoreKind kind) => Groups.FirstOrDefault(g => g.Kind == kind);

    /// <summary>"2 CCDs: 3D V-Cache on one", "Hybrid: performance and efficiency cores", "2 CCDs", "One CCD".</summary>
    public string Summary => !IsKnown
        ? "Core layout unknown"
        : IsVCache
            ? $"{Groups.Count.ToString(CultureInfo.CurrentCulture)} CCDs · 3D V-Cache on one"
            : IsHybrid
                ? "Hybrid · performance and efficiency cores"
                : Groups.Count > 1
                    ? $"{Groups.Count.ToString(CultureInfo.CurrentCulture)} CCDs · the same cache on each"
                    : HasVCacheEverywhere ? "One CCD · 3D V-Cache on every core" : "One group of identical cores";

    /// <summary>Which group each logical processor belongs to (−1 for one in no group).</summary>
    public int[] GroupOfProcessors()
    {
        var map = new int[LogicalProcessors];
        Array.Fill(map, -1);
        foreach (var group in Groups)
        {
            foreach (int p in group.Processors)
            {
                if (p >= 0 && p < map.Length)
                {
                    map[p] = group.Index;
                }
            }
        }

        return map;
    }

    internal static string Megabytes(long bytes) => $"{(bytes / (1024.0 * 1024)).ToString("0.#", CultureInfo.CurrentCulture)} MB";

    private static int BitCount(ulong mask) => System.Numerics.BitOperations.PopCount(mask);

    // ---- Reading Windows' description ------------------------------------------------------------------------

    private const int RelationProcessorCore = 0;
    private const int RelationCache = 2;

    /// <summary>
    /// Reads the buffer <c>GetLogicalProcessorInformationEx(RelationAll)</c> fills (a run of
    /// SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX records, 64-bit layout). Unknown when nothing useful is in it.
    /// </summary>
    public static CoreLayout Parse(ReadOnlySpan<byte> buffer)
    {
        var cores = new List<CoreUnit>();
        var caches = new List<CacheUnit>();
        int offset = 0;
        while (offset + 8 <= buffer.Length)
        {
            int relationship = BinaryPrimitives.ReadInt32LittleEndian(buffer[offset..]);
            int size = (int)BinaryPrimitives.ReadUInt32LittleEndian(buffer[(offset + 4)..]);
            if (size < 8 || offset + size > buffer.Length)
            {
                break;
            }

            var record = buffer.Slice(offset, size);
            if (relationship == RelationProcessorCore && size >= 48)
            {
                // PROCESSOR_RELATIONSHIP: Flags, EfficiencyClass, Reserved[20], GroupCount, GROUP_AFFINITY[GroupCount]
                byte efficiency = record[9];
                int count = Math.Max(1, (int)BinaryPrimitives.ReadUInt16LittleEndian(record[30..]));
                for (int i = 0; i < count && 32 + (i * 16) + 16 <= size; i++)
                {
                    var affinity = record[(32 + (i * 16))..];
                    cores.Add(new CoreUnit(BinaryPrimitives.ReadUInt16LittleEndian(affinity[8..]), BinaryPrimitives.ReadUInt64LittleEndian(affinity), efficiency));
                }
            }
            else if (relationship == RelationCache && size >= 56)
            {
                // CACHE_RELATIONSHIP: Level, Associativity, LineSize, CacheSize, Type, Reserved[18], GroupCount, GROUP_AFFINITY[]
                byte level = record[8];
                long bytes = BinaryPrimitives.ReadUInt32LittleEndian(record[12..]);
                int count = Math.Max(1, (int)BinaryPrimitives.ReadUInt16LittleEndian(record[38..]));
                for (int i = 0; i < count && 40 + (i * 16) + 16 <= size; i++)
                {
                    var affinity = record[(40 + (i * 16))..];
                    caches.Add(new CacheUnit(level, bytes, BinaryPrimitives.ReadUInt16LittleEndian(affinity[8..]), BinaryPrimitives.ReadUInt64LittleEndian(affinity)));
                }
            }

            offset += size;
        }

        return Build(cores, caches);
    }

    /// <summary>Groups the cores (see the type's summary).</summary>
    public static CoreLayout Build(IReadOnlyList<CoreUnit> cores, IReadOnlyList<CacheUnit> caches)
    {
        if (cores.Count == 0)
        {
            return Unknown;
        }

        // Number the logical processors as Windows' per-processor counters do: by group, then by number.
        var all = cores
            .SelectMany(c => Bits(c.Mask).Select(bit => (c.Group, Bit: bit)))
            .Distinct()
            .OrderBy(p => p.Group)
            .ThenBy(p => p.Bit)
            .ToList();
        var index = new Dictionary<(int Group, int Bit), int>();
        for (int i = 0; i < all.Count; i++)
        {
            index[all[i]] = i;
        }

        // Each core's L3: the cache whose processors include the core's first one.
        var l3 = caches.Where(c => c.Level == 3 && c.Mask != 0).Distinct().ToList();
        var domains = new List<(CacheUnit? Cache, List<CoreUnit> Cores)>();
        foreach (var core in cores.Where(c => c.Mask != 0))
        {
            int first = Bits(core.Mask).First();
            int at = l3.FindIndex(c => c.Group == core.Group && (c.Mask & (1UL << first)) != 0);
            CacheUnit? cache = at >= 0 ? l3[at] : null;
            int domain = domains.FindIndex(d => Nullable.Equals(d.Cache, cache));
            if (domain < 0)
            {
                domains.Add((cache, []));
                domain = domains.Count - 1;
            }

            domains[domain].Cores.Add(core);
        }

        var classes = cores.Select(c => c.EfficiencyClass).Distinct().ToList();
        var groups = new List<(CoreKind Kind, List<CoreUnit> Cores, long L3)>();
        long Size((CacheUnit? Cache, List<CoreUnit> Cores) d) => d.Cache?.Bytes ?? 0;

        if (domains.Count >= 2 && domains.All(d => Size(d) > 0) && domains.Max(Size) >= domains.Min(Size) * CacheRatio)
        {
            long smallest = domains.Min(Size);
            var large = domains.Where(d => Size(d) >= smallest * CacheRatio).ToList();
            var small = domains.Except(large).ToList();
            byte largeClass = large.SelectMany(d => d.Cores).Max(c => c.EfficiencyClass);
            byte smallClass = small.SelectMany(d => d.Cores).Max(c => c.EfficiencyClass);

            // A larger L3 on faster cores is a hybrid design (Zen 5 next to Zen 5c); on equal cores it's 3D V-Cache.
            if (classes.Count < 2 || largeClass <= smallClass)
            {
                groups.Add((CoreKind.Cache, [.. large.SelectMany(d => d.Cores)], large.Max(Size)));
                groups.Add((CoreKind.Frequency, [.. small.SelectMany(d => d.Cores)], small.Max(Size)));
            }
        }

        if (groups.Count == 0 && classes.Count > 1)
        {
            byte fastest = classes.Max();
            groups.Add((CoreKind.Performance, [.. cores.Where(c => c.EfficiencyClass == fastest)], 0));
            groups.Add((CoreKind.Efficiency, [.. cores.Where(c => c.EfficiencyClass != fastest)], 0));
        }

        if (groups.Count == 0)
        {
            groups.AddRange(domains.Select(d => (CoreKind.Standard, d.Cores, Size(d))));
        }

        var built = new List<CoreGroup>();
        var ordered = groups
            .Where(g => g.Cores.Count > 0)
            .Select(g => (g.Kind, g.Cores, g.L3, Processors: g.Cores.SelectMany(c => Bits(c.Mask).Select(bit => index[(c.Group, bit)])).Distinct().Order().ToList()))
            .OrderBy(g => g.Kind is CoreKind.Standard ? 0 : g.Kind is CoreKind.Cache or CoreKind.Performance ? 0 : 1)
            .ThenBy(g => g.Processors[0])
            .ToList();
        foreach (var g in ordered)
        {
            ulong mask = g.Cores.Where(c => c.Group == 0).Aggregate(0UL, (m, c) => m | c.Mask);
            string name = g.Kind switch
            {
                CoreKind.Cache => "Cache cores",
                CoreKind.Frequency => "Frequency cores",
                CoreKind.Performance => "Performance cores",
                CoreKind.Efficiency => "Efficiency cores",
                _ => ordered.Count > 1 ? $"CCD {(built.Count + 1).ToString(CultureInfo.CurrentCulture)}" : "All cores",
            };
            built.Add(new CoreGroup(built.Count, g.Kind, name, g.Processors, g.Cores.Count, g.L3, mask));
        }

        return new CoreLayout(built, all.Count) { L3Caches = domains.Count(d => d.Cache is not null) };
    }

    private static IEnumerable<int> Bits(ulong mask)
    {
        for (int bit = 0; bit < 64; bit++)
        {
            if ((mask & (1UL << bit)) != 0)
            {
                yield return bit;
            }
        }
    }
}

/// <summary>
/// Where the load is while a game is in front: each core group's share of the processor's load over the last
/// <see cref="WindowSamples"/> readings (per-logical-processor utilization, the counters Task Manager uses). It covers
/// everything running, not only the game, so it's read as "mostly on" rather than exactly.
/// </summary>
public sealed class CorePlacement
{
    /// <summary>Readings kept (about one a second).</summary>
    public const int WindowSamples = 20;

    /// <summary>Readings before a verdict.</summary>
    public const int MinimumSamples = 5;

    private readonly CoreLayout _layout;
    private readonly int[] _groupOf;
    private readonly Queue<(double[] Groups, double Average)> _samples = new();

    public CorePlacement(CoreLayout layout)
    {
        _layout = layout;
        _groupOf = layout.GroupOfProcessors();
    }

    public int Samples => _samples.Count;

    /// <summary>Average load of a logical processor over the window (0–100).</summary>
    public double AverageLoad => _samples.Count == 0 ? 0 : _samples.Average(s => s.Average);

    /// <summary>Adds one reading (load per logical processor, 0–100; NaN where unknown). Returns whether it counted.</summary>
    public bool Add(IReadOnlyList<double> threadLoads)
    {
        if (_layout.Groups.Count == 0 || threadLoads.Count == 0)
        {
            return false;
        }

        var sums = new double[_layout.Groups.Count];
        double total = 0;
        int known = 0;
        for (int i = 0; i < threadLoads.Count && i < _groupOf.Length; i++)
        {
            double load = threadLoads[i];
            if (!double.IsFinite(load) || _groupOf[i] < 0)
            {
                continue;
            }

            load = Math.Clamp(load, 0, 100);
            sums[_groupOf[i]] += load;
            total += load;
            known++;
        }

        if (known == 0)
        {
            return false;
        }

        _samples.Enqueue((sums, total / known));
        while (_samples.Count > WindowSamples)
        {
            _samples.Dequeue();
        }

        return true;
    }

    public void Reset() => _samples.Clear();

    /// <summary>Each group's share of the load (0–1, adding up to 1), or zeros when there's no load.</summary>
    public IReadOnlyList<double> Shares
    {
        get
        {
            var sums = new double[_layout.Groups.Count];
            foreach (var (groups, _) in _samples)
            {
                for (int i = 0; i < sums.Length; i++)
                {
                    sums[i] += groups[i];
                }
            }

            double total = sums.Sum();
            return total <= 0 ? sums : [.. sums.Select(s => s / total)];
        }
    }

    public double Share(CoreGroup group) => group.Index < Shares.Count ? Shares[group.Index] : 0;
}

/// <summary>How well a game's load sits on the cores that suit it.</summary>
public enum PlacementState
{
    /// <summary>Not enough readings yet.</summary>
    Measuring,

    /// <summary>The processor is hardly busy (a menu, a pause), so there's nothing to judge.</summary>
    Idle,

    /// <summary>Mostly on the preferred cores (or on one CCD).</summary>
    Good,

    /// <summary>Split between the groups.</summary>
    Mixed,

    /// <summary>Mostly on the other cores.</summary>
    Off,
}

/// <summary>A placement verdict and its sentence.</summary>
public readonly record struct PlacementVerdict(PlacementState State, string Text, double PreferredShare);

/// <summary>Judges where a game runs, checks what the scheduling depends on, and words it.</summary>
public static class CoreScheduling
{
    /// <summary>At least this share on the preferred cores is "on the right cores".</summary>
    public const double GoodShare = 0.7;

    /// <summary>Under this share the game is "mostly on the other cores".</summary>
    public const double OffShare = 0.45;

    /// <summary>A profile isn't needed when AMD's driver already keeps this much of the game on the cache cores.</summary>
    public const double AlreadyPlacedShare = 0.8;

    /// <summary>Below this average load per logical processor (%) the game is too idle to judge.</summary>
    public const double IdleLoad = 3;

    public static PlacementVerdict Judge(CoreLayout layout, CorePlacement placement, string game)
    {
        if (layout.Preferred is not { } preferred)
        {
            return new(PlacementState.Idle, string.Empty, 0);
        }

        if (placement.Samples < CorePlacement.MinimumSamples)
        {
            return new(PlacementState.Measuring, $"Measuring where {game} runs…", 0);
        }

        if (placement.AverageLoad < IdleLoad)
        {
            return new(PlacementState.Idle, $"{game} is barely using the processor right now.", 0);
        }

        var shares = placement.Shares;
        double share = shares[preferred.Index];
        if (layout.Groups.All(g => g.Kind == CoreKind.Standard))
        {
            // CCDs alike: one CCD keeps the game's threads on one L3 cache; spread across them costs a little latency.
            int top = Enumerable.Range(0, shares.Count).MaxBy(i => shares[i]);
            return shares[top] >= GoodShare
                ? new(PlacementState.Good, $"{game}: {Percent(shares[top])} of the load on {layout.Groups[top].Name}", share)
                : new(PlacementState.Mixed, $"{game}: spread across the CCDs ({string.Join(" / ", shares.Select(Percent))})", share);
        }

        string others = layout.Others.Count() == 1 ? layout.Others.First().Name.ToLowerInvariant() : "the other cores";
        return share >= GoodShare
            ? new(PlacementState.Good, $"{game}: {Percent(share)} of the load on the {preferred.Name.ToLowerInvariant()}", share)
            : share >= OffShare
                ? new(PlacementState.Mixed, $"{game}: split, {Percent(share)} on the {preferred.Name.ToLowerInvariant()} and {Percent(1 - share)} on the {others}", share)
                : new(PlacementState.Off, $"{game}: mostly on the {others} ({Percent(1 - share)} of the load)", share);
    }

    public static string Percent(double share) => $"{Math.Round(share * 100).ToString(CultureInfo.CurrentCulture)} %";

    /// <summary>A signed percentage: "+6 %", "−2 %".</summary>
    public static string Gain(double gain)
    {
        double rounded = Math.Round(gain * 100);
        string number = Math.Abs(rounded).ToString(CultureInfo.CurrentCulture);
        return rounded > 0 ? $"+{number} %" : rounded < 0 ? $"−{number} %" : "±0 %";
    }

    /// <summary>
    /// What core scheduling depends on, as checks: for a multi-CCD Ryzen X3D, AMD's 3D V-Cache driver and service
    /// (from the chipset driver), its default preference, Game Mode (which it relies on to spot games) and a power plan
    /// that lets cores park (which it uses to keep games off the frequency cores); elsewhere Game Mode.
    /// </summary>
    public static IReadOnlyList<CoreCheck> Checks(CoreLayout layout, AmdVCacheStatus amd, bool gameModeOn, PowerPlan? plan)
    {
        var checks = new List<CoreCheck>();
        if (layout.IsVCache)
        {
            checks.Add(amd.Driver switch
            {
                ServiceState.Running => new CoreCheck("AMD 3D V-Cache driver", "Running", CheckState.Good, "AMD's chipset driver moves games to the cache cores by parking the frequency cores while one plays."),
                ServiceState.Stopped => new CoreCheck("AMD 3D V-Cache driver", "Not running", CheckState.Bad, "Installed but stopped. Restart Windows, or reinstall AMD's chipset driver from amd.com."),
                _ => new CoreCheck("AMD 3D V-Cache driver", "Not installed", CheckState.Bad, "Install AMD's chipset driver from amd.com: without it Windows doesn't know which cores have the 3D V-Cache."),
            });
            checks.Add(amd.Service switch
            {
                ServiceState.Running => new CoreCheck("AMD 3D V-Cache service", "Running", CheckState.Good, "Reads AMD's preferences, including any per-game ones."),
                ServiceState.Stopped => new CoreCheck("AMD 3D V-Cache service", "Not running", CheckState.Warn, "The amd3dvcacheSvc service is stopped. Restart Windows, or reinstall AMD's chipset driver."),
                _ => new CoreCheck("AMD 3D V-Cache service", "Not installed", CheckState.Warn, "Recent AMD chipset drivers add this service; updating the chipset driver brings it."),
            });
            if (amd.Driver != ServiceState.Missing)
            {
                checks.Add(amd.DefaultType == 1
                    ? new CoreCheck("AMD preference", "Cache first", CheckState.Info, "Set to prefer the cache cores for everything, not only games.")
                    : new CoreCheck("AMD preference", "Frequency first", CheckState.Good, "AMD's default: the frequency cores for everyday work, the cache cores once a game is detected."));
            }
        }

        checks.Add(gameModeOn
            ? new CoreCheck("Game Mode", "On", CheckState.Good, layout.IsVCache ? "AMD's driver relies on it to spot games." : "Windows gives the game in front priority.")
            : new CoreCheck("Game Mode", "Off", layout.IsVCache ? CheckState.Bad : CheckState.Warn, layout.IsVCache ? "Turn on Game Mode above: AMD's driver relies on it to spot games." : "Turn on Game Mode above so Windows gives games priority."));

        if (layout.IsVCache && plan is not null)
        {
            bool parks = plan.Id != PowerPlans.HighPerformance && plan.Id != PowerPlans.UltimateTemplate && !plan.Name.Contains("Ultimate", StringComparison.OrdinalIgnoreCase);
            checks.Add(parks
                ? new CoreCheck("Power plan", plan.Name, plan.Id == PowerPlans.Balanced ? CheckState.Good : CheckState.Info, plan.Id == PowerPlans.Balanced ? "Balanced lets cores park, which AMD's driver needs." : "AMD recommends Balanced on Ryzen X3D processors.")
                : new CoreCheck("Power plan", plan.Name, CheckState.Info, "This plan stops core parking, which AMD's driver uses to keep games on the cache cores. Balanced lets it park them."));
        }

        return checks;
    }

    /// <summary>True when a plan stops core parking (High or Ultimate Performance).</summary>
    public static bool StopsParking(PowerPlan? plan) =>
        plan is not null && (plan.Id == PowerPlans.HighPerformance || plan.Id == PowerPlans.UltimateTemplate || plan.Name.Contains("Ultimate", StringComparison.OrdinalIgnoreCase));
}

/// <summary>How a check came out.</summary>
public enum CheckState
{
    Good,
    Info,
    Warn,
    Bad,
}

/// <summary>One thing core scheduling depends on, how it is, and what to do about it.</summary>
public sealed record CoreCheck(string Name, string Status, CheckState State, string Detail);

/// <summary>Whether a Windows service is there and running.</summary>
public enum ServiceState
{
    Missing,
    Stopped,
    Running,
}

/// <summary>A per-program preference set in AMD's 3D V-Cache driver.</summary>
/// <param name="Name">The key's name.</param>
/// <param name="EndsWith">The end of the program's path it matches ("game.exe").</param>
/// <param name="Prefers">Cache or Frequency.</param>
public sealed record AmdAppPreference(string Name, string EndsWith, CoreKind Prefers);

/// <summary>AMD's 3D V-Cache Performance Optimizer, as installed by the chipset driver (read-only to Coreloom).</summary>
public sealed record AmdVCacheStatus(ServiceState Driver, ServiceState Service, int? DefaultType, IReadOnlyList<AmdAppPreference> Apps)
{
    /// <summary>The kernel driver's service name.</summary>
    public const string DriverName = "amd3dvcache";

    /// <summary>The user-mode service that comes with recent chipset drivers.</summary>
    public const string ServiceName = "amd3dvcacheSvc";

    /// <summary>Under HKEY_LOCAL_MACHINE: <c>DefaultType</c> (0 frequency, 1 cache).</summary>
    public const string PreferencesKey = @"SYSTEM\CurrentControlSet\Services\amd3dvcache\Preferences";

    /// <summary>Per-program preferences: a key each, with <c>EndsWith</c> (text) and <c>Type</c> (0 frequency, 1 cache).</summary>
    public const string AppsKey = PreferencesKey + @"\App";

    public static AmdVCacheStatus None { get; } = new(ServiceState.Missing, ServiceState.Missing, null, []);

    public bool IsActive => Driver == ServiceState.Running;

    /// <summary>AMD's per-program preference that matches a program (file name or path), or null.</summary>
    public AmdAppPreference? For(string? program)
    {
        if (string.IsNullOrWhiteSpace(program))
        {
            return null;
        }

        return Apps.FirstOrDefault(a => a.EndsWith.Length > 0 && program.EndsWith(a.EndsWith, StringComparison.OrdinalIgnoreCase))
            ?? Apps.FirstOrDefault(a => GameExecutables.FileName(a.EndsWith) is { } name && string.Equals(name, GameExecutables.FileName(program), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A registry Type value as a kind of core.</summary>
    public static CoreKind? Kind(int? type) => type switch
    {
        0 => CoreKind.Frequency,
        1 => CoreKind.Cache,
        _ => null,
    };
}

/// <summary>A process's cores (its affinity) and every core Windows lets it use.</summary>
public readonly record struct ProcessAffinity(ulong Process, ulong System)
{
    /// <summary>The process runs on a chosen subset of the cores (set by someone, or by the game itself).</summary>
    public bool IsCustom => Process != 0 && Process != System;
}

/// <summary>
/// An A/B test of a game on the preferred cores against Windows' own choice: four rounds of
/// <see cref="RoundSeconds"/> seconds of play — Windows, preferred, Windows, preferred — with the first
/// <see cref="SettleSeconds"/> of each round left out. Only gameplay counts (as for adaptive pacing). The preferred
/// cores are proven when both their rounds beat the Windows rounds before them and the gain is clear: at least 3 %
/// more FPS with 1% lows no worse, or at least 5 % better 1% lows with FPS no worse.
/// </summary>
public sealed class AffinityTrial
{
    public const int Rounds = 4;
    public const int RoundSeconds = 30;
    public const int SettleSeconds = 3;
    public const double FpsGainNeeded = 0.03;
    public const double LowGainNeeded = 0.05;
    public const double Tolerance = -0.01;

    private readonly List<(double Fps, double P99Ms)>[] _rounds = [.. Enumerable.Range(0, Rounds).Select(_ => new List<(double, double)>())];

    public AffinityTrial(CoreGroup candidate)
    {
        Candidate = candidate;
    }

    public CoreGroup Candidate { get; }

    /// <summary>0–3 while testing, 4 when done.</summary>
    public int Round { get; private set; }

    /// <summary>Seconds of play counted in this round (including the settling ones).</summary>
    public int Seconds { get; private set; }

    /// <summary>Seconds that weren't gameplay.</summary>
    public int Skipped { get; private set; }

    public bool IsDone => Round >= Rounds;

    /// <summary>This round runs on the candidate cores (else on Windows' choice).</summary>
    public bool OnCandidate => Round % 2 == 1;

    /// <summary>Total gameplay the test needs.</summary>
    public static int TotalSeconds => Rounds * RoundSeconds;

    /// <summary>"Round 2 of 4 · cache cores · 18 s left".</summary>
    public string ProgressText => IsDone
        ? "Done"
        : $"Round {(Round + 1).ToString(CultureInfo.CurrentCulture)} of {Rounds.ToString(CultureInfo.CurrentCulture)} · {(OnCandidate ? Candidate.Name.ToLowerInvariant() : "Windows' choice")} · {(RoundSeconds - Seconds).ToString(CultureInfo.CurrentCulture)} s left";

    /// <summary>Adds a second of the game's frames. Returns true when a new round starts (the game's cores change).</summary>
    public bool Add(PacingSecond second)
    {
        if (IsDone)
        {
            return false;
        }

        if (!(second.Fps >= 10) || second.LongestMs > 500 || second.Frames < 2 || !(second.P99Ms > 0))
        {
            Skipped++;
            return false;
        }

        Seconds++;
        if (Seconds > SettleSeconds)
        {
            _rounds[Round].Add((second.Fps, second.P99Ms));
        }

        if (Seconds >= RoundSeconds)
        {
            Round++;
            Seconds = 0;
            return !IsDone;
        }

        return false;
    }

    public AffinityTrialResult Result()
    {
        var fps = _rounds.Select(r => r.Count == 0 ? 0 : r.Average(s => s.Fps)).ToArray();
        var lows = _rounds.Select(r => r.Count == 0 ? 0 : 1000 / r.Average(s => s.P99Ms)).ToArray();
        if (!IsDone || fps.Any(f => f <= 0) || lows.Any(l => l <= 0))
        {
            return new AffinityTrialResult(false, 0, 0, 0, 0, 0, 0);
        }

        double fpsA = (fps[0] + fps[2]) / 2, fpsB = (fps[1] + fps[3]) / 2;
        double lowA = (lows[0] + lows[2]) / 2, lowB = (lows[1] + lows[3]) / 2;
        double fpsGain = (fpsB / fpsA) - 1;
        double lowGain = (lowB / lowA) - 1;
        bool fpsBoth = fps[1] > fps[0] && fps[3] > fps[2];
        bool lowsBoth = lows[1] > lows[0] && lows[3] > lows[2];
        bool proven = (fpsBoth && fpsGain >= FpsGainNeeded && lowGain >= Tolerance)
            || (lowsBoth && lowGain >= LowGainNeeded && fpsGain >= Tolerance);
        return new AffinityTrialResult(proven, fpsGain, lowGain, fpsA, fpsB, lowA, lowB);
    }
}

/// <summary>What an A/B test found (gains are fractions: 0.06 = 6 % better on the candidate cores).</summary>
public sealed record AffinityTrialResult(bool Proven, double FpsGain, double LowGain, double FpsWindows, double FpsCandidate, double LowWindows, double LowCandidate)
{
    /// <summary>"+6 % FPS, +4 % 1% lows".</summary>
    public string GainText => $"{CoreScheduling.Gain(FpsGain)} FPS, {CoreScheduling.Gain(LowGain)} 1% lows";
}

/// <summary>A game kept on chosen cores, after a test showed it gains from them.</summary>
public sealed class CoreProfile
{
    public const int MaxProfiles = 200;

    /// <summary>The game's program file name ("Cyberpunk2077.exe").</summary>
    public string Program { get; set; } = string.Empty;

    /// <summary>The game's name as shown.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The cores it runs on: Cache, Performance, or Standard (the first CCD). Never the frequency or efficiency cores.</summary>
    public CoreKind Cores { get; set; } = CoreKind.Cache;

    public double FpsGain { get; set; }

    public double LowGain { get; set; }

    public DateTime Tested { get; set; }

    /// <summary>The profile for a program (file name or path), or null.</summary>
    public static CoreProfile? For(IEnumerable<CoreProfile> profiles, string? program)
    {
        string? name = GameExecutables.FileName(program);
        return name is null ? null : profiles.FirstOrDefault(p => string.Equals(p.Program, name, StringComparison.OrdinalIgnoreCase));
    }

    public static List<CoreProfile> Normalize(List<CoreProfile>? profiles) =>
        (profiles ?? [])
            .Where(p => p is not null && GameExecutables.FileName(p.Program) is not null && p.Cores is CoreKind.Cache or CoreKind.Performance or CoreKind.Standard)
            .Select(p =>
            {
                p.Program = GameExecutables.FileName(p.Program)!;
                p.Name = string.IsNullOrWhiteSpace(p.Name) ? p.Program : p.Name.Trim();
                p.FpsGain = double.IsFinite(p.FpsGain) ? Math.Clamp(p.FpsGain, -1, 10) : 0;
                p.LowGain = double.IsFinite(p.LowGain) ? Math.Clamp(p.LowGain, -1, 10) : 0;
                return p;
            })
            .Reverse()
            .DistinctBy(p => p.Program, StringComparer.OrdinalIgnoreCase)
            .Reverse()
            .TakeLast(MaxProfiles)
            .ToList();
}
