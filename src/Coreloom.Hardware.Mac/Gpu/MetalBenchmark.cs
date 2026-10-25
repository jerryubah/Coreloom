using System.Diagnostics;
using System.Runtime.InteropServices;
using Coreloom.Core.Benchmarks;
using Coreloom.Hardware.Mac.Native;

namespace Coreloom.Hardware.Mac.Gpu;

/// <summary>
/// The GPU benchmark on a Mac: the same five tests as on Windows (two graphics tests drawn by fragment shaders, three
/// compute tests), with the same sizes, the same arithmetic and the same scoring, written in Metal Shading Language and
/// run flat out for a few seconds each on Coreloom's own off-screen Metal device. Nothing is drawn on screen and no game
/// is touched.
/// </summary>
public static class MetalBenchmark
{
    /// <summary>The Mac's Metal GPUs, built-in first.</summary>
    public static IReadOnlyList<GpuAdapter> Adapters()
    {
        try
        {
            return Metal.Adapters();
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException)
        {
            return [];
        }
    }

    public static Task<GpuBenchmarkScores> RunAsync(GpuBenchmarkOptions options, GpuAdapter? adapter, IProgress<BenchmarkProgress>? progress, CancellationToken cancellationToken)
    {
        var done = new TaskCompletionSource<GpuBenchmarkScores>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                done.SetResult(Run(options, adapter, progress, cancellationToken));
            }
            catch (OperationCanceledException)
            {
                done.SetCanceled(cancellationToken);
            }
            catch (Exception e)
            {
                done.SetException(e);
            }
        })
        {
            IsBackground = true,
            Name = "Coreloom GPU benchmark",
        };
        thread.Start();
        return done.Task;
    }

    private static GpuBenchmarkScores Run(GpuBenchmarkOptions options, GpuAdapter? adapter, IProgress<BenchmarkProgress>? progress, CancellationToken cancellationToken)
    {
        progress?.Report(new BenchmarkProgress("Preparing", 0, "Compiling shaders"));
        using var gpu = Metal.Create(adapter);
        var workloads = MetalWorkload.All(gpu, options.Small);
        var results = new List<BenchmarkTestResult>();
        for (int i = 0; i < workloads.Count; i++)
        {
            var workload = workloads[i];
            progress?.Report(new BenchmarkProgress(workload.Info.Name, i / (double)workloads.Count, workload.Info.Category));
            double rate = Measure(gpu, workload, options.Seconds, cancellationToken);
            results.Add(new BenchmarkTestResult(workload.Info.Id, workload.Info.Name, workload.Info.Category, workload.Info.Unit, rate, GpuTests.Score(rate, workload.Info.Reference)));
        }

        progress?.Report(new BenchmarkProgress("Done", 1));
        var (overall, graphics, compute) = GpuTests.Scores(results);
        return new GpuBenchmarkScores(gpu.Adapter, results, overall, graphics, compute);
    }

    /// <summary>Runs a workload for <paramref name="seconds"/> after a short warm-up and returns its rate.</summary>
    internal static double Measure(Metal gpu, MetalWorkload workload, double seconds, CancellationToken cancellationToken)
    {
        int batch = Batch(gpu, workload, cancellationToken);
        RunFor(gpu, workload, batch, Math.Min(0.5, seconds / 3), cancellationToken);
        var clock = Stopwatch.StartNew();
        long units = RunFor(gpu, workload, batch, seconds, cancellationToken);
        return units * workload.WorkPerUnit / clock.Elapsed.TotalSeconds;
    }

    /// <summary>Units per command buffer: about 30 ms of work (at most 2,048 units), so no submission runs long.</summary>
    internal static int Batch(Metal gpu, MetalWorkload workload, CancellationToken cancellationToken)
    {
        int batch = 1;
        var clock = new Stopwatch();
        for (int attempt = 0; attempt < 12; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            clock.Restart();
            workload.Queue(batch);
            double elapsed = clock.Elapsed.TotalSeconds;
            if (elapsed >= 0.02 || batch >= 2048)
            {
                break;
            }

            batch = Math.Clamp((int)(batch * 0.03 / Math.Max(elapsed, 1e-5)), batch + 1, 2048);
        }

        return batch;
    }

    internal static long RunFor(Metal gpu, MetalWorkload workload, int batch, double seconds, CancellationToken cancellationToken)
    {
        long units = 0;
        var clock = Stopwatch.StartNew();
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            workload.Queue(batch);
            units += batch;
        }
        while (clock.Elapsed.TotalSeconds < seconds);
        return units;
    }
}

/// <summary>
/// The GPU stress test on a Mac: ray-marched frames and N-body physics steps back to back, with the frame and the
/// physics answer checked against the first ones every few seconds (a different answer is a calculation error).
/// </summary>
public sealed class MetalStressLoad(GpuAdapter? adapter, bool small = false) : IStressLoad
{
    private CancellationTokenSource? _stop;
    private Thread? _thread;
    private long _work;
    private int _errors;
    private string? _failure;
    private string _device = adapter?.Name ?? "GPU";

    /// <summary>"Graphics card", as the Windows load is named (the Benchmarking page matches loads by it).</summary>
    public string Name => "Graphics card";

    public double VerifySeconds { get; init; } = 5;

    public string Device => Volatile.Read(ref _device);

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
        _thread = new Thread(() => Run(token)) { IsBackground = true, Name = "Coreloom GPU stress" };
        _thread.Start();
    }

    private void Run(CancellationToken token)
    {
        try
        {
            using var gpu = Metal.Create(adapter);
            Volatile.Write(ref _device, gpu.Adapter.Name);
            var frames = new MetalRayMarching(gpu, small);
            var physics = new MetalPhysics(gpu, small);
            long frameAnswer = frames.Verify(), physicsAnswer = physics.Verify();
            int frameBatch = MetalBenchmark.Batch(gpu, frames, token);
            int physicsBatch = MetalBenchmark.Batch(gpu, physics, token);
            var sinceCheck = Stopwatch.StartNew();
            while (!token.IsCancellationRequested)
            {
                frames.Queue(frameBatch);
                physics.Queue(physicsBatch);
                Interlocked.Increment(ref _work);
                if (sinceCheck.Elapsed.TotalSeconds >= VerifySeconds)
                {
                    bool framesRight = frames.Verify() == frameAnswer;
                    bool physicsRight = physics.Verify() == physicsAnswer;
                    if ((!framesRight || !physicsRight) && Interlocked.Increment(ref _errors) == 1)
                    {
                        Interlocked.CompareExchange(ref _failure, $"The GPU drew a {(framesRight ? "physics step" : "frame")} differently from the first time.", null);
                    }

                    sinceCheck.Restart();
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            Interlocked.CompareExchange(ref _failure, e.Message, null);
        }
    }

    public void Stop()
    {
        _stop?.Cancel();
        _thread?.Join();
        _thread = null;
    }

    public void Dispose()
    {
        Stop();
        _stop?.Dispose();
    }
}

/// <summary>One GPU test's resources and work on Metal.</summary>
internal abstract class MetalWorkload(Metal gpu)
{
    protected Metal Gpu { get; } = gpu;

    public abstract GpuTestInfo Info { get; }

    public abstract double WorkPerUnit { get; }

    /// <summary>Queues <paramref name="units"/> units in one command buffer and waits for the GPU to finish them.</summary>
    public abstract void Queue(int units);

    /// <summary>Runs one fixed unit and returns a checksum of the GPU's answer.</summary>
    public abstract long Verify();

    public static IReadOnlyList<MetalWorkload> All(Metal gpu, bool small) =>
    [
        new MetalLighting(gpu, small),
        new MetalRayMarching(gpu, small),
        new MetalCompute(gpu, small),
        new MetalBandwidth(gpu, small),
        new MetalPhysics(gpu, small),
    ];

    [StructLayout(LayoutKind.Sequential)]
    internal struct Constants
    {
        public float Width, Height, Time;
        public uint Count;
    }

    internal const string Common = """
        #include <metal_stdlib>
        using namespace metal;

        struct Constants
        {
            float Width;
            float Height;
            float Time;
            uint Count;
        };

        struct Pixel
        {
            float4 Position [[position]];
            float2 Uv;
        };

        vertex Pixel FullScreen(uint id [[vertex_id]])
        {
            Pixel o;
            o.Uv = float2((id << 1) & 2, id & 2);
            o.Position = float4(o.Uv * float2(2, -2) + float2(-1, 1), 0, 1);
            return o;
        }

        """;
}

/// <summary>A full-screen fragment shader drawn into an off-screen texture: one frame per unit.</summary>
internal abstract unsafe class MetalFrames : MetalWorkload
{
    private readonly nint _pipeline, _target, _readback;
    private readonly int _width, _height;
    private readonly uint _count;
    private int _frame;

    protected MetalFrames(Metal gpu, string shader, string entry, int width, int height, uint count)
        : base(gpu)
    {
        using var pool = new ObjC.Pool();
        nint library = gpu.Library(Common + shader);
        _pipeline = gpu.RenderPipeline(gpu.Function(library, "FullScreen"), gpu.Function(library, entry));
        _target = gpu.RenderTarget(width, height);
        _readback = gpu.Buffer((long)width * height * 4, Metal.StorageShared);
        (_width, _height, _count) = (width, height, count);
    }

    public override double WorkPerUnit => 1;

    public override void Queue(int units)
    {
        using var pool = new ObjC.Pool();
        for (int i = 0; i < units; i++)
        {
            var constants = new Constants { Width = _width, Height = _height, Time = (_frame++ % 600) / 60f, Count = _count };
            Gpu.Draw(_pipeline, _target, &constants, sizeof(Constants));
        }

        Gpu.Finish();
    }

    public override long Verify()
    {
        using var pool = new ObjC.Pool();
        var constants = new Constants { Width = _width, Height = _height, Time = 0, Count = _count };
        Gpu.Draw(_pipeline, _target, &constants, sizeof(Constants));
        Gpu.CopyToBuffer(_target, _width, _height, _readback);
        Gpu.Finish();
        return Metal.Checksum(_readback, _width * _height * 4);
    }
}

internal sealed class MetalLighting(Metal gpu, bool small)
    : MetalFrames(gpu, Shader, "Lighting", small ? 256 : 2560, small ? 144 : 1440, small ? 16u : 64u)
{
    private const string Shader = """
        fragment float4 Lighting(Pixel input [[stage_in]], constant Constants& c [[buffer(0)]])
        {
            float2 p = input.Uv * float2(c.Width / c.Height, 1) * 8.0;
            float h = 0;
            float2 g = float2(0);
            for (int w = 0; w < 6; w++)
            {
                float2 dir = float2(cos(w * 1.7), sin(w * 1.3));
                float f = 1.0 + w * 0.7;
                float phase = dot(p, dir) * f + c.Time * (0.5 + w * 0.1);
                h += sin(phase) / f;
                g += cos(phase) * dir;
            }

            float3 n = normalize(float3(-g.x * 0.3, 1, -g.y * 0.3));
            float3 position = float3(p.x, h * 0.3, p.y);
            float3 view = normalize(float3(4, 6, -4) - position);
            float3 color = float3(0.03);
            for (uint l = 0; l < c.Count; l++)
            {
                float a = l * 3.8832221 + c.Time * 0.2;
                float3 lightPosition = float3(4 + 3.5 * cos(a), 1.5 + 0.5 * sin(l * 1.9), 4 + 3.5 * sin(a));
                float3 lightColor = 0.5 + 0.5 * cos(float3(0, 2.1, 4.2) + float(l));
                float3 d = lightPosition - position;
                float distance2 = dot(d, d);
                float3 toLight = d * rsqrt(distance2);
                float3 halfway = normalize(toLight + view);
                float diffuse = saturate(dot(n, toLight));
                float specular = pow(saturate(dot(n, halfway)), 48.0);
                color += lightColor * (diffuse + specular) / (1.0 + distance2 * 0.15);
            }

            return float4(color / (1 + color), 1);
        }
        """;

    public override GpuTestInfo Info => GpuTests.Lighting;
}

internal sealed class MetalRayMarching(Metal gpu, bool small)
    : MetalFrames(gpu, Shader, "RayMarch", small ? 160 : 1920, small ? 90 : 1080, small ? 32u : 96u)
{
    private const string Shader = """
        static float Scene(float3 p, float time)
        {
            float d = p.y + 1.0;
            for (int k = 0; k < 6; k++)
            {
                float3 c = float3(float(k % 3 - 1) * 2.2, 0.3 * sin(time + k), 5.0 + float(k / 3) * 2.5);
                float3 q = p - c;
                float sphere = length(q) - 0.8;
                float3 b = abs(q) - 0.6;
                float box = length(max(b, 0.0)) + min(max(b.x, max(b.y, b.z)), 0.0) - 0.1;
                d = min(d, mix(sphere, box, 0.5 + 0.5 * sin(time * 0.7 + k)));
            }

            return d;
        }

        fragment float4 RayMarch(Pixel input [[stage_in]], constant Constants& c [[buffer(0)]])
        {
            float2 uv = (input.Uv * 2 - 1) * float2(c.Width / c.Height, -1);
            float3 origin = float3(0, 1.2, -1.5);
            float3 ray = normalize(float3(uv.x, uv.y - 0.25, 1.6));
            float t = 0;
            bool hit = false;
            for (uint s = 0; s < c.Count; s++)
            {
                float d = Scene(origin + ray * t, c.Time);
                if (d < 0.001 * t)
                {
                    hit = true;
                    break;
                }

                t += d;
                if (t > 40)
                {
                    break;
                }
            }

            float3 sky = float3(0.55, 0.7, 0.95) - ray.y * 0.3;
            if (!hit)
            {
                return float4(sky, 1);
            }

            float3 p = origin + ray * t;
            const float2 e = float2(0.001, -0.001);
            float3 n = normalize(e.xyy * Scene(p + e.xyy, c.Time) + e.yyx * Scene(p + e.yyx, c.Time) + e.yxy * Scene(p + e.yxy, c.Time) + e.xxx * Scene(p + e.xxx, c.Time));
            float3 light = normalize(float3(-0.6, 1.0, -0.5));
            float shadow = 1;
            float st = 0.02;
            for (uint i = 0; i < 32; i++)
            {
                float h = Scene(p + light * st, c.Time);
                shadow = min(shadow, 8 * h / st);
                st += clamp(h, 0.02, 0.5);
                if (shadow < 0.001 || st > 20)
                {
                    break;
                }
            }

            shadow = saturate(shadow);
            float occlusion = 0;
            for (int a = 1; a <= 5; a++)
            {
                float hd = 0.08 * a;
                occlusion += (hd - Scene(p + n * hd, c.Time)) / a;
            }

            occlusion = saturate(1 - 2 * occlusion);
            float diffuse = saturate(dot(n, light)) * shadow;
            float3 color = (0.15 * occlusion + 0.85 * diffuse) * float3(0.9, 0.8, 0.7);
            color = mix(color, sky, 1 - exp(-0.002 * t * t));
            return float4(color, 1);
        }
        """;

    public override GpuTestInfo Info => GpuTests.RayMarching;
}

/// <summary>A compute kernel over buffers: one dispatch per unit.</summary>
internal abstract unsafe class MetalDispatch : MetalWorkload
{
    private readonly nint _pipeline;
    private readonly nint[] _buffers;
    private readonly nint _result, _readback;
    private readonly int _resultBytes;
    private readonly uint _groups, _threads;
    private Constants _constants;

    protected MetalDispatch(Metal gpu, string shader, string entry, uint groups, uint threads, uint count, nint[] buffers, nint result, int resultBytes)
        : base(gpu)
    {
        using var pool = new ObjC.Pool();
        _pipeline = gpu.ComputePipeline(gpu.Function(gpu.Library(Common + shader), entry));
        _buffers = buffers;
        _result = result;
        _resultBytes = resultBytes;
        _readback = resultBytes > 0 ? gpu.Buffer(resultBytes, Metal.StorageShared) : 0;
        (_groups, _threads) = (groups, threads);
        _constants = new Constants { Count = count };
    }

    public override void Queue(int units)
    {
        using var pool = new ObjC.Pool();
        fixed (Constants* c = &_constants)
        {
            nint encoder = Gpu.BeginCompute(_pipeline, _buffers, c, sizeof(Constants));
            for (int i = 0; i < units; i++)
            {
                Metal.Dispatch(encoder, _groups, _threads);
            }

            Metal.EndCompute(encoder);
        }

        Gpu.Finish();
    }

    public override long Verify()
    {
        if (_readback == 0)
        {
            return 0;
        }

        using var pool = new ObjC.Pool();
        Queue(1);
        Gpu.CopyBuffer(_result, _readback, _resultBytes);
        Gpu.Finish();
        return Metal.Checksum(_readback, _resultBytes);
    }
}

internal sealed class MetalCompute : MetalDispatch
{
    private const string Shader = """
        kernel void Flops(device float* Numbers [[buffer(0)]], constant Constants& c [[buffer(1)]], uint id [[thread_position_in_grid]])
        {
            float a0 = fract(id * 0.000001) * 0.5;
            float a1 = a0 + 0.01, a2 = a0 + 0.02, a3 = a0 + 0.03;
            float b0 = a0 + 0.1, b1 = a1 + 0.1, b2 = a2 + 0.1, b3 = a3 + 0.1;
            for (uint i = 0; i < c.Count; i++)
            {
                for (int u = 0; u < 16; u++)
                {
                    a0 = fma(a0, b0, 0.25); b0 = fma(b0, a0, -0.25);
                    a1 = fma(a1, b1, 0.25); b1 = fma(b1, a1, -0.25);
                    a2 = fma(a2, b2, 0.25); b2 = fma(b2, a2, -0.25);
                    a3 = fma(a3, b3, 0.25); b3 = fma(b3, a3, -0.25);
                }
            }

            Numbers[id] = a0 + a1 + a2 + a3 + b0 + b1 + b2 + b3;
        }
        """;

    private readonly double _work;

    public MetalCompute(Metal gpu, bool small)
        : this(gpu, small ? 16u : 4096u, small ? 8u : 256u)
    {
    }

    private MetalCompute(Metal gpu, uint groups, uint count)
        : this(gpu, groups, count, gpu.Buffer((long)groups * 256 * 4))
    {
    }

    private MetalCompute(Metal gpu, uint groups, uint count, nint numbers)
        : base(gpu, Shader, "Flops", groups, 256, count, [numbers], numbers, (int)groups * 256 * 4) =>
        _work = groups * 256.0 * count * 256 / 1e9; // 8 multiply-adds (16 operations) × 16 per loop

    public override GpuTestInfo Info => GpuTests.Compute;

    public override double WorkPerUnit => _work;
}

internal sealed class MetalBandwidth : MetalDispatch
{
    private const string Shader = """
        kernel void Copy(device const uint4* Source [[buffer(0)]], device uint4* Target [[buffer(1)]], constant Constants& c [[buffer(2)]], uint id [[thread_position_in_grid]])
        {
            Target[id] = Source[id] + c.Count;
        }
        """;

    private readonly double _work;

    public MetalBandwidth(Metal gpu, bool small)
        : this(gpu, small ? 64 * 1024 : 8 * 1024 * 1024)
    {
    }

    private MetalBandwidth(Metal gpu, int elements)
        : base(gpu, Shader, "Copy", (uint)(elements / 256), 256, 1, [gpu.Buffer(elements * 16L), gpu.Buffer(elements * 16L)], 0, 0) =>
        _work = 2.0 * elements * 16 / 1e9; // read and write 16 bytes per element

    public override GpuTestInfo Info => GpuTests.Bandwidth;

    public override double WorkPerUnit => _work;
}

internal sealed class MetalPhysics : MetalDispatch
{
    private const string Shader = """
        kernel void Gravity(device const float4* Bodies [[buffer(0)]], device float4* Pulls [[buffer(1)]], constant Constants& c [[buffer(2)]],
            uint id [[thread_position_in_grid]], uint local [[thread_position_in_threadgroup]])
        {
            threadgroup float4 Tile[64];
            float3 me = Bodies[id].xyz;
            float3 pull = float3(0);
            for (uint start = 0; start < c.Count; start += 64)
            {
                Tile[local] = Bodies[start + local];
                threadgroup_barrier(mem_flags::mem_threadgroup);
                for (uint j = 0; j < 64; j++)
                {
                    float4 other = Tile[j];
                    float3 d = other.xyz - me;
                    float inverse = rsqrt(dot(d, d) + 0.01);
                    pull += d * (other.w * inverse * inverse * inverse);
                }

                threadgroup_barrier(mem_flags::mem_threadgroup);
            }

            Pulls[id] = float4(pull, 0);
        }
        """;

    private readonly double _work;

    public MetalPhysics(Metal gpu, bool small)
        : this(gpu, small ? 1024 : 32768)
    {
    }

    private MetalPhysics(Metal gpu, int bodies)
        : this(gpu, bodies, gpu.PrivateBuffer<float>(Bodies(bodies)), gpu.Buffer(bodies * 16L))
    {
    }

    private MetalPhysics(Metal gpu, int bodies, nint positions, nint pulls)
        : base(gpu, Shader, "Gravity", (uint)(bodies / 64), 64, (uint)bodies, [positions, pulls], pulls, bodies * 16) =>
        _work = (double)bodies * bodies / 1e9; // billions of pairs

    public override GpuTestInfo Info => GpuTests.Physics;

    public override double WorkPerUnit => _work;

    /// <summary>Bodies spread through a cube (x, y, z and a mass each), the same as the Windows test's.</summary>
    internal static float[] Bodies(int count)
    {
        var data = new float[count * 4];
        ulong state = 0x9E3779B97F4A7C15UL;
        for (int i = 0; i < data.Length; i++)
        {
            state ^= state << 13;
            state ^= state >> 7;
            state ^= state << 17;
            data[i] = (i % 4) == 3 ? 1 + (state % 100) / 10f : ((state % 2000) / 1000f) - 1;
        }

        return data;
    }
}
