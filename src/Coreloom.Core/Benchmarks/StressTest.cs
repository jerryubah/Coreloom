namespace Coreloom.Core.Benchmarks;

/// <summary>
/// A load a stress test puts on a piece of hardware: it works flat out on its own threads, counts the work it gets
/// done, and checks every answer against the one it worked out at the start — a wrong answer means the hardware made a
/// calculation error (an unstable overclock or undervolt, or overheating).
/// </summary>
public interface IStressLoad : IDisposable
{
    /// <summary>"Processor" or "Graphics card".</summary>
    string Name { get; }

    /// <summary>The device's name ("AMD Ryzen 9 9950X3D").</summary>
    string Device { get; }

    /// <summary>Work units done so far (they only make sense compared with each other).</summary>
    long Work { get; }

    /// <summary>Calculation errors found so far.</summary>
    int Errors { get; }

    /// <summary>What the first error was, or why the load stopped working (a lost graphics device).</summary>
    string? Failure { get; }

    /// <summary>Starts the load (returns at once).</summary>
    void Start();

    /// <summary>Stops the load and waits for its threads.</summary>
    void Stop();
}

/// <summary>
/// The processor stress test: every logical processor alternates between multiplying matrices with vector (AVX)
/// instructions — the heaviest load, which draws the most power — and tracing rays in double precision, and checks each
/// answer against the one worked out before the test began.
/// </summary>
public sealed class CpuStressLoad : IStressLoad
{
    private readonly int _threads;
    private readonly bool _small;
    private readonly List<Thread> _workers = [];
    private Thread? _starter;
    private CancellationTokenSource? _stop;
    private long _work;
    private int _errors;
    private string? _failure;

    public CpuStressLoad(string device, int threads = 0, bool small = false)
    {
        Device = device;
        _threads = threads > 0 ? threads : Environment.ProcessorCount;
        _small = small;
    }

    public string Name => "Processor";

    public string Device { get; }

    public int Threads => _threads;

    public long Work => Interlocked.Read(ref _work);

    public int Errors => Volatile.Read(ref _errors);

    public string? Failure => Volatile.Read(ref _failure);

    public void Start()
    {
        if (_stop is not null)
        {
            return;
        }

        _stop = new CancellationTokenSource();
        var token = _stop.Token;

        // Works out the right answers first (off the caller's thread: it takes a moment), then starts the workers.
        _starter = new Thread(() =>
        {
            CpuTest matrix = new MatrixTest(_small), rays = new RayTracingTest(_small);
            long matrixAnswer = Answer(matrix), raysAnswer = Answer(rays);
            lock (_workers)
            {
                for (int t = 0; t < _threads && !token.IsCancellationRequested; t++)
                {
                    int index = t;
                    var thread = new Thread(() => Loop(index, matrix, matrixAnswer, rays, raysAnswer, token))
                    {
                        IsBackground = true,
                        Priority = ThreadPriority.BelowNormal,
                        Name = $"Coreloom stress {index}",
                    };
                    _workers.Add(thread);
                    thread.Start();
                }
            }
        })
        {
            IsBackground = true,
            Name = "Coreloom stress start",
        };
        _starter.Start();
    }

    /// <summary>The answer a test gives with seed 0 (after a few runs, so the code it runs is the final, optimized code).</summary>
    private static long Answer(CpuTest test)
    {
        var worker = test.CreateWorker(0);
        long answer = 0;
        for (int i = 0; i < 3; i++)
        {
            answer = worker.Run();
        }

        return answer;
    }

    private void Loop(int index, CpuTest matrix, long matrixAnswer, CpuTest rays, long raysAnswer, CancellationToken token)
    {
        var m = matrix.CreateWorker(0);
        var r = rays.CreateWorker(0);
        while (!token.IsCancellationRequested)
        {
            long a = m.Run();
            long b = r.Run();
            if (a != matrixAnswer || b != raysAnswer)
            {
                if (Interlocked.Increment(ref _errors) == 1)
                {
                    Interlocked.CompareExchange(
                        ref _failure,
                        $"Logical processor {index + 1} got a {(a != matrixAnswer ? "matrix" : "ray tracing")} calculation wrong.",
                        null);
                }
            }

            Interlocked.Increment(ref _work);
        }
    }

    public void Stop()
    {
        _stop?.Cancel();
        _starter?.Join();
        lock (_workers)
        {
            foreach (var worker in _workers)
            {
                worker.Join();
            }

            _workers.Clear();
        }
    }

    public void Dispose()
    {
        Stop();
        _stop?.Dispose();
    }
}

/// <summary>Why a stress test stopped on its own.</summary>
public enum StressStopReason
{
    None,

    /// <summary>The time chosen ran out (it finished).</summary>
    Finished,

    /// <summary>The user stopped it.</summary>
    Cancelled,

    /// <summary>A calculation error (the hardware isn't stable).</summary>
    Error,

    /// <summary>The processor stayed at the temperature limit.</summary>
    ProcessorTooHot,

    /// <summary>The graphics card stayed at the temperature limit.</summary>
    GraphicsTooHot,

    /// <summary>The load stopped working (the graphics driver reset, for one).</summary>
    Failed,
}

/// <summary>
/// Coreloom's own safety net for stress tests. Processors and graphics cards protect themselves (they slow down at
/// their limits), but if one stays this hot for several seconds, Coreloom stops the test anyway.
/// </summary>
public sealed class StressGuard(double processorLimit = StressGuard.ProcessorLimit, double graphicsLimit = StressGuard.GraphicsLimit, int seconds = StressGuard.HoldSeconds)
{
    /// <summary>Above every desktop processor's normal ceiling (95–100 °C); they throttle long before.</summary>
    public const double ProcessorLimit = 100;

    /// <summary>The graphics core (not the hot spot) at 95 °C is past every card's normal operating range.</summary>
    public const double GraphicsLimit = 95;

    public const int HoldSeconds = 5;

    private int _processorHot, _graphicsHot;

    public double Processor => processorLimit;

    public double Graphics => graphicsLimit;

    /// <summary>Takes one second's temperatures (NaN when there's no reading) and says whether to stop.</summary>
    public StressStopReason Check(double processorCelsius, double graphicsCelsius)
    {
        _processorHot = processorCelsius >= processorLimit ? _processorHot + 1 : 0;
        _graphicsHot = graphicsCelsius >= graphicsLimit ? _graphicsHot + 1 : 0;
        return _processorHot >= seconds ? StressStopReason.ProcessorTooHot
            : _graphicsHot >= seconds ? StressStopReason.GraphicsTooHot
            : StressStopReason.None;
    }
}

/// <summary>
/// How a stress test is judged, the way 3DMark's stress tests are: the run is split into loops of equal length, each
/// loop's work is compared with the best loop's, and the stability is the worst loop as a percentage of the best. A
/// system that holds its speed (no thermal or power throttling) and makes no calculation errors scores 97 % or more
/// and passes.
/// </summary>
public static class StressScoring
{
    public const double PassStability = 97;

    /// <summary>Loop length: 10 seconds, or 5 for runs shorter than five minutes (so a short run still has loops).</summary>
    public static double LoopSeconds(TimeSpan? duration) => duration is { TotalMinutes: < 5 } ? 5 : 10;

    /// <summary>The worst loop as a percentage of the best (100 with one loop, 0 with none).</summary>
    public static double Stability(IReadOnlyList<double> loops)
    {
        if (loops.Count == 0)
        {
            return 0;
        }

        double best = loops.Max();
        return best > 0 ? loops.Min() / best * 100 : 0;
    }

    /// <summary>Each loop as a percentage of the best one.</summary>
    public static List<double> Relative(IReadOnlyList<double> loops)
    {
        double best = loops.Count > 0 ? loops.Max() : 0;
        return loops.Select(l => best > 0 ? Math.Round(l / best * 100, 1) : 0).ToList();
    }

    /// <summary>Passed only when it ran to the end with no errors, at least two loops and 97 % stability.</summary>
    public static bool Passed(StressStopReason reason, int errors, IReadOnlyList<double> loops) =>
        reason == StressStopReason.Finished && errors == 0 && loops.Count >= 2 && Stability(loops) >= PassStability;
}

/// <summary>Turns a running total of work into per-loop amounts.</summary>
public sealed class StressLoops(double loopSeconds)
{
    private readonly List<double> _loops = [];
    private double _loopStart;
    private long _workAtLoopStart;

    public double LoopSeconds => loopSeconds;

    public IReadOnlyList<double> Loops => _loops;

    /// <summary>Takes the total work at <paramref name="seconds"/> since the start; closes a loop when one is due.</summary>
    public bool Add(double seconds, long work)
    {
        if (seconds - _loopStart < loopSeconds)
        {
            return false;
        }

        _loops.Add((work - _workAtLoopStart) / (seconds - _loopStart));
        _loopStart = seconds;
        _workAtLoopStart = work;
        return true;
    }
}
