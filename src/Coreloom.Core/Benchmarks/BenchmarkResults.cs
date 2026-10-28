using System.Text.Json;
using System.Text.Json.Serialization;
using Coreloom.Core.Diagnostics;
using Coreloom.Core.Telemetry;

namespace Coreloom.Core.Benchmarks;

/// <summary>What a saved result measured.</summary>
public enum BenchmarkKind
{
    /// <summary>The CPU benchmark (single- and multi-core scores).</summary>
    Cpu,

    /// <summary>The GPU benchmark (graphics, compute, memory and physics tests).</summary>
    Gpu,

    /// <summary>A stress test of the processor, the graphics card or both.</summary>
    Stress,
}

/// <summary>One test of a benchmark: its rate and points (the single-core ones for the CPU tests too).</summary>
public sealed record BenchmarkTestResult(
    string Id,
    string Name,
    string Category,
    string Unit,
    double Rate,
    double Score,
    double? SingleRate = null,
    double? SingleScore = null);

/// <summary>A part of a benchmark's score ("Single-core", "Graphics"…).</summary>
public sealed record BenchmarkSubscore(string Name, double Score);

/// <summary>A sensor's lowest, average and highest value while the benchmark ran.</summary>
public sealed record BenchmarkReading(string Group, string Name, MetricKind Kind, double Min, double Average, double Max);

/// <summary>A fact about how the hardware was set up (overclocking, power limits, boost) when the benchmark ran.</summary>
/// <param name="Name">What it is ("Precision Boost Overdrive", "GPU power limit"…).</param>
/// <param name="Value">Its value ("On", "600 W", "+4.3 %"…).</param>
/// <param name="Detail">What it means, in a sentence.</param>
/// <param name="Raised">True when it's set beyond stock (overclocked or raised).</param>
public sealed record BenchmarkTuningNote(string Group, string Name, string Value, string Detail, bool Raised = false);

/// <summary>The PC a result was measured on.</summary>
public sealed record BenchmarkSystem
{
    public string Processor { get; init; } = string.Empty;

    public int Cores { get; init; }

    public int Threads { get; init; }

    public string GraphicsCard { get; init; } = string.Empty;

    public string GraphicsDriver { get; init; } = string.Empty;

    /// <summary>Graphics memory in bytes (0 when unknown).</summary>
    public double GraphicsMemory { get; init; }

    /// <summary>Installed memory in bytes (0 when unknown).</summary>
    public double Memory { get; init; }

    public string MemorySpeed { get; init; } = string.Empty;

    public string Motherboard { get; init; } = string.Empty;

    public string Bios { get; init; } = string.Empty;

    public string OperatingSystem { get; init; } = string.Empty;
}

/// <summary>A saved benchmark or stress test result.</summary>
public sealed record BenchmarkResult
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public BenchmarkKind Kind { get; init; }

    public DateTimeOffset Finished { get; init; }

    public string AppVersion { get; init; } = string.Empty;

    /// <summary>
    /// Points a test scores at its reference rate: 1,000 for the CPU benchmark, 10,000 for the GPU benchmark since 1.5.1.
    /// Missing (0) in results saved by 1.5.0, whose GPU scores were on the 1,000 scale; <see cref="BenchmarkScale"/>
    /// brings those up to date.
    /// </summary>
    public int PointsAtReference { get; init; }

    /// <summary>
    /// GPU results: what the compute tests' points were multiplied by (<see cref="GpuTests.MetalComputeFactor"/> on a Mac
    /// since 1.8.1, else 1). Missing (0) in results saved earlier, where it was 1.
    /// </summary>
    public double ComputeFactor { get; init; }

    /// <summary>
    /// The headline number: the multi-core score for the CPU benchmark, the overall score for the GPU benchmark, the
    /// stability (percent) for a stress test.
    /// </summary>
    public double Score { get; init; }

    /// <summary>
    /// The parts of the score: single-core and multi-core for the CPU benchmark, graphics and compute for the GPU
    /// benchmark.
    /// </summary>
    public List<BenchmarkSubscore> Subscores { get; init; } = [];

    /// <summary>The device the benchmark ran on ("AMD Ryzen 9 9950X3D", "NVIDIA GeForce RTX 5090").</summary>
    public string Device { get; init; } = string.Empty;

    /// <summary>For a stress test: "Processor", "Graphics card" or "Processor and graphics card".</summary>
    public string Target { get; init; } = string.Empty;

    public List<BenchmarkTestResult> Tests { get; init; } = [];

    public List<BenchmarkReading> Readings { get; init; } = [];

    public List<BenchmarkTuningNote> Tuning { get; init; } = [];

    public BenchmarkSystem System { get; init; } = new();

    /// <summary>How long it ran, in seconds.</summary>
    public double Seconds { get; init; }

    /// <summary>Threads the multi-core part (or the processor stress test) used.</summary>
    public int Threads { get; init; }

    /// <summary>Memory copy bandwidth in GB/s (CPU benchmark; 0 when not measured).</summary>
    public double MemoryBandwidth { get; init; }

    /// <summary>Memory latency in nanoseconds (CPU benchmark; 0 when not measured).</summary>
    public double MemoryLatency { get; init; }

    /// <summary>Stress test: work done in each loop, relative to the best loop (percent).</summary>
    public List<double> Loops { get; init; } = [];

    /// <summary>Stress test: calculation errors found (any error means the hardware isn't stable).</summary>
    public int Errors { get; init; }

    /// <summary>Stress test: whether it passed.</summary>
    public bool Passed { get; init; }

    /// <summary>Stress test: "Passed", "Failed" or "Stopped", with why ("Stopped: the processor reached 100 °C").</summary>
    public string Verdict { get; init; } = string.Empty;

    /// <summary>Anything worth knowing about the run (a test that couldn't run, an error's message).</summary>
    public string Note { get; init; } = string.Empty;

    [JsonIgnore]
    public bool IsStress => Kind == BenchmarkKind.Stress;
}

/// <summary>
/// The points scales. The CPU benchmark scores 1,000 points per test at its reference rate. The GPU benchmark scores
/// 10,000 (since 1.5.1; it was 1,000 in 1.5.0), so a high-end graphics card lands in the hundreds of thousands, the
/// range Geekbench's GPU (OpenCL) scores are in.
/// </summary>
public static class BenchmarkScale
{
    public const int Cpu = 1_000;

    public const int Gpu = 10_000;

    /// <summary>The points per reference rate for a kind (stress tests aren't scored in points: 0).</summary>
    public static int For(BenchmarkKind kind) => kind switch
    {
        BenchmarkKind.Cpu => Cpu,
        BenchmarkKind.Gpu => Gpu,
        _ => 0,
    };

    /// <summary>
    /// A result on today's scale: a GPU result saved by 1.5.0 (on the 1,000-point scale) has its score, parts and test
    /// points multiplied by ten; anything else is returned as it is (with its scale filled in).
    /// </summary>
    public static BenchmarkResult Upgrade(BenchmarkResult result) => Upgrade(result, 1);

    /// <summary>
    /// As <see cref="Upgrade(BenchmarkResult)"/>, and a GPU result's compute tests brought to
    /// <paramref name="computeFactor"/> (a Mac's results saved before 1.8.1 have theirs multiplied by ten, with the compute
    /// part and the overall score worked out again).
    /// </summary>
    public static BenchmarkResult Upgrade(BenchmarkResult result, double computeFactor)
    {
        result = UpgradeScale(result);
        if (result.Kind != BenchmarkKind.Gpu || computeFactor <= 0)
        {
            return result;
        }

        double was = result.ComputeFactor > 0 ? result.ComputeFactor : 1;
        if (result.Tests.Count == 0)
        {
            return result; // nothing to work the compute part out from
        }

        if (Math.Abs(was - computeFactor) < 1e-9)
        {
            return result.ComputeFactor == computeFactor ? result : result with { ComputeFactor = computeFactor };
        }

        double change = computeFactor / was;
        var tests = result.Tests.Select(t => t.Category == "Compute" ? t with { Score = t.Score * change, SingleScore = t.SingleScore * change } : t).ToList();
        var (overall, graphics, compute) = GpuTests.Scores(tests);
        return result with
        {
            ComputeFactor = computeFactor,
            Tests = tests,
            Score = overall,
            Subscores = result.Subscores.Select(s => s.Name switch
            {
                "Graphics" => s with { Score = graphics },
                "Compute" => s with { Score = compute },
                _ => s,
            }).ToList(),
        };
    }

    private static BenchmarkResult UpgradeScale(BenchmarkResult result)
    {
        int current = For(result.Kind);
        if (result.PointsAtReference == current)
        {
            return result;
        }

        int was = result.PointsAtReference > 0 ? result.PointsAtReference : Cpu; // 1.5.0 saved no scale: 1,000 for both
        if (current == 0 || was == current)
        {
            return result with { PointsAtReference = current };
        }

        double factor = (double)current / was;
        return result with
        {
            PointsAtReference = current,
            Score = result.Score * factor,
            Subscores = result.Subscores.Select(s => s with { Score = s.Score * factor }).ToList(),
            Tests = result.Tests.Select(t => t with { Score = t.Score * factor, SingleScore = t.SingleScore * factor }).ToList(),
        };
    }
}

/// <summary>
/// The saved results, newest first, and which one is best of its kind (the highest CPU score, the highest GPU score).
/// Stress tests pass or fail and are never "best".
/// </summary>
public sealed class BenchmarkHistory
{
    /// <summary>Results kept; the oldest go first, except each kind's best.</summary>
    public const int Capacity = 200;

    private readonly List<BenchmarkResult> _results = [];
    private readonly double _computeFactor;

    /// <param name="computeFactor">What this computer's GPU compute tests are multiplied by (see <see cref="GpuTests.MetalComputeFactor"/>).</param>
    public BenchmarkHistory(IEnumerable<BenchmarkResult>? results = null, double computeFactor = 1)
    {
        _computeFactor = computeFactor;
        if (results is not null)
        {
            _results.AddRange(results.Where(r => r is not null && !string.IsNullOrEmpty(r.Id)).DistinctBy(r => r.Id).Select(r => BenchmarkScale.Upgrade(r, _computeFactor)));
            Sort();
            Trim();
        }
    }

    public IReadOnlyList<BenchmarkResult> Results => _results;

    public void Add(BenchmarkResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        _results.RemoveAll(r => r.Id == result.Id);
        _results.Add(BenchmarkScale.Upgrade(result, _computeFactor));
        Sort();
        Trim();
    }

    public bool Remove(string id) => _results.RemoveAll(r => r.Id == id) > 0;

    /// <summary>The best result of a kind (the highest score; the earliest wins a tie), or null.</summary>
    public BenchmarkResult? Best(BenchmarkKind kind) => kind == BenchmarkKind.Stress
        ? null
        : _results.Where(r => r.Kind == kind && r.Score > 0)
            .OrderByDescending(r => r.Score)
            .ThenBy(r => r.Finished)
            .FirstOrDefault();

    public bool IsBest(BenchmarkResult result) => Best(result.Kind)?.Id == result.Id;

    /// <summary>The result's score against the best of its kind (1 for the best itself), for comparison bars.</summary>
    public double FractionOfBest(BenchmarkResult result) =>
        Best(result.Kind) is { Score: > 0 } best ? Math.Clamp(result.Score / best.Score, 0, 1) : 0;

    /// <summary>The latest result of a kind, or null.</summary>
    public BenchmarkResult? Latest(BenchmarkKind kind) => _results.FirstOrDefault(r => r.Kind == kind);

    private void Sort() => _results.Sort((a, b) => b.Finished.CompareTo(a.Finished));

    private void Trim()
    {
        while (_results.Count > Capacity)
        {
            var keep = new HashSet<string?>([Best(BenchmarkKind.Cpu)?.Id, Best(BenchmarkKind.Gpu)?.Id]);
            int oldest = _results.FindLastIndex(r => !keep.Contains(r.Id));
            if (oldest < 0)
            {
                break;
            }

            _results.RemoveAt(oldest);
        }
    }
}

/// <summary>
/// Saves results as <c>benchmarks.json</c> in Coreloom's settings folder. Writes are atomic (a temporary file, then a
/// replace), and an unreadable file is kept as <c>benchmarks.json.bad</c> so nothing is lost silently.
/// </summary>
public sealed class BenchmarkStore(string filePath)
{
    public const string FileName = "benchmarks.json";

    private readonly object _gate = new();

    public string FilePath { get; } = filePath;

    public static string Serialize(IEnumerable<BenchmarkResult> results) =>
        JsonSerializer.Serialize(new StoredResults(1, results.ToList()), Settings.SettingsStore.JsonOptions);

    public static List<BenchmarkResult> Deserialize(string json) =>
        JsonSerializer.Deserialize<StoredResults>(json, Settings.SettingsStore.JsonOptions)?.Results?.Where(r => r is not null).ToList() ?? [];

    public List<BenchmarkResult> Load()
    {
        lock (_gate)
        {
            try
            {
                return File.Exists(FilePath) ? Deserialize(File.ReadAllText(FilePath)) : [];
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException)
            {
                Log.Warn("Benchmarks", "The saved benchmark results couldn't be read; a copy was kept as benchmarks.json.bad.", ex);
                try
                {
                    File.Copy(FilePath, FilePath + ".bad", overwrite: true);
                }
                catch (Exception copy) when (copy is IOException or UnauthorizedAccessException)
                {
                }

                return [];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warn("Benchmarks", "The saved benchmark results couldn't be read.", ex);
                return [];
            }
        }
    }

    public void Save(IEnumerable<BenchmarkResult> results)
    {
        string json = Serialize(results);
        lock (_gate)
        {
            string? directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, json);
            if (File.Exists(FilePath))
            {
                File.Replace(temp, FilePath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temp, FilePath);
            }
        }
    }

    private sealed record StoredResults(int Version, List<BenchmarkResult> Results);
}

/// <summary>Collects sensor values while a benchmark runs and sums them up as min / average / max readings.</summary>
public sealed class ReadingRecorder
{
    private readonly Dictionary<(string Group, string Name), Accumulator> _values = [];
    private readonly List<(string Group, string Name)> _order = [];

    public void Add(string group, string name, MetricKind kind, double value)
    {
        if (!double.IsFinite(value))
        {
            return;
        }

        var key = (group, name);
        if (!_values.TryGetValue(key, out var accumulator))
        {
            accumulator = new Accumulator(kind);
            _values[key] = accumulator;
            _order.Add(key);
        }

        accumulator.Add(value);
    }

    public bool Has(string group, string name) => _values.ContainsKey((group, name));

    public double Max(string group, string name) => _values.TryGetValue((group, name), out var a) ? a.Max : double.NaN;

    public double Average(string group, string name) => _values.TryGetValue((group, name), out var a) ? a.Sum / a.Count : double.NaN;

    public List<BenchmarkReading> Readings() =>
        _order.Select(key => _values[key] is var a ? new BenchmarkReading(key.Group, key.Name, a.Kind, a.Min, a.Sum / a.Count, a.Max) : null!).ToList();

    private sealed class Accumulator(MetricKind kind)
    {
        public MetricKind Kind { get; } = kind;

        public double Min { get; private set; } = double.MaxValue;

        public double Max { get; private set; } = double.MinValue;

        public double Sum { get; private set; }

        public int Count { get; private set; }

        public void Add(double value)
        {
            Min = Math.Min(Min, value);
            Max = Math.Max(Max, value);
            Sum += value;
            Count++;
        }
    }
}
