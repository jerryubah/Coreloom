using Coreloom.Core.Benchmarks;
using Coreloom.Core.Diagnostics;
using Coreloom.Hardware.Mac.Gpu;
using Coreloom.ViewModels.Services;

namespace Coreloom.Mac.Services;

/// <summary>
/// The Benchmarking page on a Mac: the same CPU benchmark and stress test as on Windows (Coreloom.Core, native arm64 or
/// x64 code), the GPU benchmark and stress test on Coreloom's own off-screen Metal device, and results saved as
/// benchmarks.json in Application Support. Nothing changes clocks or limits, and nothing touches a game.
/// </summary>
public sealed class MacBenchmarkService(string settingsDirectory) : IHardwareBenchmarkService
{
    private readonly BenchmarkStore _store = new(Path.Combine(settingsDirectory, BenchmarkStore.FileName));

    public bool GpuSupported => true;

    public IReadOnlyList<GpuAdapter> GraphicsCards() => MetalBenchmark.Adapters();

    public Task<CpuBenchmarkScores> RunCpuAsync(CpuBenchmarkOptions options, IProgress<BenchmarkProgress> progress, CancellationToken cancellationToken)
    {
        Log.Info("Benchmarks", $"CPU benchmark started on {options.ThreadCount} threads.");
        return CpuBenchmark.RunAsync(options, progress, cancellationToken);
    }

    public Task<GpuBenchmarkScores> RunGpuAsync(GpuAdapter? card, IProgress<BenchmarkProgress> progress, CancellationToken cancellationToken)
    {
        Log.Info("Benchmarks", $"GPU benchmark started on {card?.Name ?? "the default GPU"} (Metal).");
        return MetalBenchmark.RunAsync(new GpuBenchmarkOptions(), card, progress, cancellationToken);
    }

    public IStressLoad ProcessorStress(string device) => new CpuStressLoad(device);

    public IStressLoad? GraphicsStress(GpuAdapter? card) => new MetalStressLoad(card);

    public IReadOnlyList<BenchmarkResult> LoadResults() => _store.Load();

    public void SaveResults(IReadOnlyList<BenchmarkResult> results) => _store.Save(results);
}
