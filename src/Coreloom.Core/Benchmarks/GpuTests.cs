namespace Coreloom.Core.Benchmarks;

/// <summary>A GPU test: what it does and the rate that scores 1,000 points.</summary>
/// <param name="Category">"Graphics" (drawing frames) or "Compute" (general GPU computing).</param>
/// <param name="Reference">
/// The rate that scores 1,000 points: an estimate for an entry-level gaming graphics card of about 2.5 TFLOPS and
/// 110 GB/s (a GeForce GTX 1650 class card). It's Coreloom's own scale, worked out from each test's arithmetic rather
/// than measured on that card.
/// </param>
public sealed record GpuTestInfo(string Id, string Name, string Category, string Description, string Unit, double Reference);

/// <summary>The GPU tests, in the order they run, and how they're scored.</summary>
public static class GpuTests
{
    public static GpuTestInfo Lighting { get; } = new(
        "lighting", "Lighting", "Graphics",
        "Draws a detailed surface lit by 64 moving lights at 2560 × 1440, one frame after another.", "fps", 80);

    public static GpuTestInfo RayMarching { get; } = new(
        "raymarching", "Ray marching", "Graphics",
        "Renders a 3D scene with soft shadows and ambient occlusion by marching rays through it at 1920 × 1080.", "fps", 35);

    public static GpuTestInfo Compute { get; } = new(
        "compute", "Compute", "Compute",
        "Runs over a million GPU threads of single-precision (FP32) multiply-adds, the GPU's raw arithmetic speed.", "GFLOPS", 2200);

    public static GpuTestInfo Bandwidth { get; } = new(
        "bandwidth", "Memory bandwidth", "Compute",
        "Copies a 128 MB buffer within the graphics memory over and over.", "GB/s", 105);

    public static GpuTestInfo Physics { get; } = new(
        "physics", "Physics", "Compute",
        "Simulates gravity between 32,768 bodies, each pulling on every other (over a billion pairs a step).", "M pairs/s", 55000);

    public static IReadOnlyList<GpuTestInfo> All { get; } = [Lighting, RayMarching, Compute, Bandwidth, Physics];

    /// <summary>The overall score and the graphics and compute parts (geometric means, so every test counts the same).</summary>
    public static (double Overall, double Graphics, double Compute) Scores(IReadOnlyList<BenchmarkTestResult> tests) => (
        CpuBenchmark.GeometricMean(tests.Select(t => t.Score)),
        CpuBenchmark.GeometricMean(tests.Where(t => t.Category == "Graphics").Select(t => t.Score)),
        CpuBenchmark.GeometricMean(tests.Where(t => t.Category == "Compute").Select(t => t.Score)));
}

/// <summary>The graphics card a benchmark can run on.</summary>
/// <param name="Memory">Dedicated graphics memory in bytes.</param>
public sealed record GpuAdapter(string Name, uint VendorId, uint DeviceId, double Memory, long Luid, bool IsSoftware);

/// <summary>How a GPU benchmark is run.</summary>
/// <param name="Seconds">Measured time of each test.</param>
/// <param name="Small">Small sizes (for tests on Windows' software renderer).</param>
/// <param name="Software">Run on WARP, Windows' software renderer (for tests).</param>
public sealed record GpuBenchmarkOptions(double Seconds = 3, bool Small = false, bool Software = false);

/// <summary>The GPU benchmark's outcome.</summary>
public sealed record GpuBenchmarkScores(GpuAdapter Adapter, IReadOnlyList<BenchmarkTestResult> Tests, double Score, double Graphics, double Compute);
