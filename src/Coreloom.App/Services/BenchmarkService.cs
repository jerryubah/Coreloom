using System.IO;
using Coreloom.Core.Benchmarks;
using Coreloom.Core.Diagnostics;
using Coreloom.Hardware.Gpu;
using Coreloom.ViewModels.Services;

namespace Coreloom.App.Services;

/// <summary>
/// Windows' side of the Benchmarking page: the CPU benchmark and stress test (Coreloom.Core), the GPU benchmark and
/// stress test on Coreloom's own off-screen Direct3D 11 device (Coreloom.Hardware), and results saved as
/// <c>benchmarks.json</c> in the settings folder. Nothing here changes clocks, voltages or limits, and nothing touches a
/// game.
/// </summary>
internal sealed class WindowsBenchmarkService(string settingsDirectory) : IHardwareBenchmarkService
{
    private readonly BenchmarkStore _store = new(Path.Combine(settingsDirectory, BenchmarkStore.FileName));

    public bool GpuSupported => true;

    public IReadOnlyList<GpuAdapter> GraphicsCards() => GpuBenchmark.Adapters();

    public Task<CpuBenchmarkScores> RunCpuAsync(CpuBenchmarkOptions options, IProgress<BenchmarkProgress> progress, CancellationToken cancellationToken)
    {
        Log.Info("Benchmarks", $"CPU benchmark started on {options.ThreadCount} threads.");
        return CpuBenchmark.RunAsync(options, progress, cancellationToken);
    }

    public Task<GpuBenchmarkScores> RunGpuAsync(GpuAdapter? card, IProgress<BenchmarkProgress> progress, CancellationToken cancellationToken)
    {
        Log.Info("Benchmarks", $"GPU benchmark started on {card?.Name ?? "the default graphics card"}.");
        return GpuBenchmark.RunAsync(new GpuBenchmarkOptions(), card, progress, cancellationToken);
    }

    public IStressLoad ProcessorStress(string device) => new CpuStressLoad(device);

    public IStressLoad? GraphicsStress(GpuAdapter? card) => new GpuStressLoad(card);

    public IReadOnlyList<BenchmarkResult> LoadResults() => _store.Load();

    public void SaveResults(IReadOnlyList<BenchmarkResult> results) => _store.Save(results);
}
