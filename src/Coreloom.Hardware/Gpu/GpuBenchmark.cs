using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Coreloom.Core.Benchmarks;

namespace Coreloom.Hardware.Gpu;

/// <summary>
/// The GPU benchmark: two graphics tests (frames drawn by pixel shaders) and three compute tests (arithmetic, memory
/// bandwidth and an N-body physics step), each run flat out for a few seconds on Coreloom's own off-screen Direct3D 11
/// device. Nothing is drawn on screen and no game is touched.
/// </summary>
[SupportedOSPlatform("windows")]
public static class GpuBenchmark
{
    /// <summary>The graphics cards the benchmark can run on, best first.</summary>
    public static IReadOnlyList<GpuAdapter> Adapters()
    {
        try
        {
            return Direct3D.Adapters();
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
        using var gpu = Direct3D.Create(adapter, options.Software);
        var workloads = GpuWorkload.All(gpu, options.Small);
        var results = new List<BenchmarkTestResult>();
        for (int i = 0; i < workloads.Count; i++)
        {
            var workload = workloads[i];
            progress?.Report(new BenchmarkProgress(workload.Info.Name, i / (double)workloads.Count, workload.Info.Category));
            double rate = Measure(gpu, workload, options.Seconds, cancellationToken);
            results.Add(new BenchmarkTestResult(workload.Info.Id, workload.Info.Name, workload.Info.Category, workload.Info.Unit, rate, CpuBenchmark.Score(rate, workload.Info.Reference)));
        }

        progress?.Report(new BenchmarkProgress("Done", 1));
        var (overall, graphics, compute) = GpuTests.Scores(results);
        return new GpuBenchmarkScores(gpu.Adapter, results, overall, graphics, compute);
    }

    /// <summary>
    /// Runs a workload for <paramref name="seconds"/> and returns its rate. Work goes to the GPU in batches of about
    /// 30 ms (sized on a first try), so each wait for the GPU costs little and no single submission runs long.
    /// </summary>
    internal static double Measure(Direct3D gpu, GpuWorkload workload, double seconds, CancellationToken cancellationToken)
    {
        workload.Bind();
        int batch = Batch(gpu, workload, cancellationToken);
        RunFor(gpu, workload, batch, Math.Min(0.5, seconds / 3), cancellationToken); // clocks up before timing
        var clock = Stopwatch.StartNew();
        long units = RunFor(gpu, workload, batch, seconds, cancellationToken);
        return units * workload.WorkPerUnit / clock.Elapsed.TotalSeconds;
    }

    /// <summary>Units per batch: about 30 ms of work (at most 2,048 units).</summary>
    internal static int Batch(Direct3D gpu, GpuWorkload workload, CancellationToken cancellationToken)
    {
        int batch = 1;
        var clock = new Stopwatch();
        for (int attempt = 0; attempt < 12; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            clock.Restart();
            for (int i = 0; i < batch; i++)
            {
                workload.Unit();
            }

            gpu.Finish();
            double elapsed = clock.Elapsed.TotalSeconds;
            if (elapsed >= 0.02 || batch >= 2048)
            {
                break;
            }

            batch = Math.Clamp((int)(batch * 0.03 / Math.Max(elapsed, 1e-5)), batch + 1, 2048);
        }

        return batch;
    }

    internal static long RunFor(Direct3D gpu, GpuWorkload workload, int batch, double seconds, CancellationToken cancellationToken)
    {
        long units = 0;
        var clock = Stopwatch.StartNew();
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int i = 0; i < batch; i++)
            {
                workload.Unit();
            }

            gpu.Finish();
            units += batch;
        }
        while (clock.Elapsed.TotalSeconds < seconds);
        return units;
    }
}

/// <summary>
/// The graphics card stress test: ray-marched frames and N-body physics steps back to back, with the frame and the
/// physics answer checked against the first ones every few seconds (a different answer is a calculation error).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class GpuStressLoad(GpuAdapter? adapter, bool software = false, bool small = false) : IStressLoad
{
    private CancellationTokenSource? _stop;
    private Thread? _thread;
    private long _work;
    private int _errors;
    private string? _failure;
    private string _device = adapter?.Name ?? "Graphics card";

    public string Name => "Graphics card";

    /// <summary>How often the answers are checked.</summary>
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
            using var gpu = Direct3D.Create(adapter, software);
            Volatile.Write(ref _device, gpu.Adapter.Name);
            var frames = new RayMarchingWorkload(gpu, small);
            var physics = new PhysicsWorkload(gpu, small);
            long frameAnswer = frames.Verify(), physicsAnswer = physics.Verify();
            frames.Bind();
            int frameBatch = GpuBenchmark.Batch(gpu, frames, token);
            physics.Bind();
            int physicsBatch = GpuBenchmark.Batch(gpu, physics, token);
            var sinceCheck = Stopwatch.StartNew();
            while (!token.IsCancellationRequested)
            {
                frames.Bind();
                for (int i = 0; i < frameBatch; i++)
                {
                    frames.Unit();
                }

                physics.Bind();
                for (int i = 0; i < physicsBatch; i++)
                {
                    physics.Unit();
                }

                gpu.Finish();
                Interlocked.Increment(ref _work);
                if (sinceCheck.Elapsed.TotalSeconds >= VerifySeconds)
                {
                    bool framesRight = frames.Verify() == frameAnswer;
                    bool physicsRight = physics.Verify() == physicsAnswer;
                    if (!framesRight || !physicsRight)
                    {
                        if (Interlocked.Increment(ref _errors) == 1)
                        {
                            Interlocked.CompareExchange(ref _failure, $"The graphics card drew a {(framesRight ? "physics step" : "frame")} differently from the first time.", null);
                        }
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

/// <summary>One GPU test's resources and work.</summary>
[SupportedOSPlatform("windows")]
internal abstract class GpuWorkload(Direct3D gpu)
{
    protected Direct3D Gpu { get; } = gpu;

    public abstract GpuTestInfo Info { get; }

    /// <summary>Work one unit does, in the test's unit (a frame, GFLOP, GB, million pairs).</summary>
    public abstract double WorkPerUnit { get; }

    /// <summary>Sets the GPU up for this test's units.</summary>
    public abstract void Bind();

    /// <summary>Queues one unit of work (a frame, a dispatch).</summary>
    public abstract void Unit();

    /// <summary>Runs one fixed unit and returns a checksum of the GPU's answer (the same every time on a stable card).</summary>
    public abstract long Verify();

    public static IReadOnlyList<GpuWorkload> All(Direct3D gpu, bool small) =>
    [
        new LightingWorkload(gpu, small),
        new RayMarchingWorkload(gpu, small),
        new ComputeWorkload(gpu, small),
        new BandwidthWorkload(gpu, small),
        new PhysicsWorkload(gpu, small),
    ];

    [StructLayout(LayoutKind.Sequential)]
    internal struct Constants
    {
        public float Width, Height, Time;
        public uint Count;
    }

    internal const string Common = """
        cbuffer Constants : register(b0)
        {
            float Width;
            float Height;
            float Time;
            uint Count;
        };

        """;
}

/// <summary>A full-screen pixel shader drawn into an off-screen texture: one frame per unit.</summary>
[SupportedOSPlatform("windows")]
internal abstract class FrameWorkload : GpuWorkload
{
    private const string Vertex = """
        struct Pixel
        {
            float4 Position : SV_Position;
            float2 Uv : TEXCOORD0;
        };

        Pixel FullScreen(uint id : SV_VertexID)
        {
            Pixel o;
            o.Uv = float2((id << 1) & 2, id & 2);
            o.Position = float4(o.Uv * float2(2, -2) + float2(-1, 1), 0, 1);
            return o;
        }

        """;

    private readonly nint _vertex, _pixel, _constants, _texture, _view, _staging;
    private readonly int _width, _height;
    private readonly uint _count;
    private int _frame;

    protected FrameWorkload(Direct3D gpu, string pixelShader, string entry, int width, int height, uint count)
        : base(gpu)
    {
        string source = Common + Vertex + pixelShader;
        _vertex = gpu.VertexShader(source, "FullScreen");
        _pixel = gpu.PixelShader(source, entry);
        _constants = gpu.ConstantBuffer(16);
        (_texture, _view, _staging) = gpu.RenderTarget(width, height);
        (_width, _height, _count) = (width, height, count);
    }

    public override double WorkPerUnit => 1;

    public override void Bind() => Gpu.UseDraw(_vertex, _pixel, _constants, _view, _width, _height);

    public override void Unit()
    {
        Gpu.SetConstants(_constants, new Constants { Width = _width, Height = _height, Time = (_frame++ % 600) / 60f, Count = _count });
        Gpu.DrawFullScreen();
    }

    public override long Verify()
    {
        Bind();
        Gpu.SetConstants(_constants, new Constants { Width = _width, Height = _height, Time = 0, Count = _count });
        Gpu.DrawFullScreen();
        Gpu.Finish();
        return Gpu.TextureChecksum(_texture, _staging, _width, _height);
    }
}

[SupportedOSPlatform("windows")]
internal sealed class LightingWorkload(Direct3D gpu, bool small)
    : FrameWorkload(gpu, Shader, "Lighting", small ? 256 : 2560, small ? 144 : 1440, small ? 16u : 64u)
{
    private const string Shader = """
        float4 Lighting(Pixel input) : SV_Target
        {
            float2 p = input.Uv * float2(Width / Height, 1) * 8.0;
            float h = 0;
            float2 g = 0;
            [unroll] for (int w = 0; w < 6; w++)
            {
                float2 dir = float2(cos(w * 1.7), sin(w * 1.3));
                float f = 1.0 + w * 0.7;
                float phase = dot(p, dir) * f + Time * (0.5 + w * 0.1);
                h += sin(phase) / f;
                g += cos(phase) * dir;
            }

            float3 n = normalize(float3(-g.x * 0.3, 1, -g.y * 0.3));
            float3 position = float3(p.x, h * 0.3, p.y);
            float3 view = normalize(float3(4, 6, -4) - position);
            float3 color = 0.03;
            [loop] for (uint l = 0; l < Count; l++)
            {
                float a = l * 3.8832221 + Time * 0.2;
                float3 lightPosition = float3(4 + 3.5 * cos(a), 1.5 + 0.5 * sin(l * 1.9), 4 + 3.5 * sin(a));
                float3 lightColor = 0.5 + 0.5 * cos(float3(0, 2.1, 4.2) + l);
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

[SupportedOSPlatform("windows")]
internal sealed class RayMarchingWorkload(Direct3D gpu, bool small)
    : FrameWorkload(gpu, Shader, "RayMarch", small ? 160 : 1920, small ? 90 : 1080, small ? 32u : 96u)
{
    private const string Shader = """
        float Scene(float3 p)
        {
            float d = p.y + 1.0;
            [unroll] for (int k = 0; k < 6; k++)
            {
                float3 c = float3((k % 3 - 1) * 2.2, 0.3 * sin(Time + k), 5.0 + (k / 3) * 2.5);
                float3 q = p - c;
                float sphere = length(q) - 0.8;
                float3 b = abs(q) - 0.6;
                float box = length(max(b, 0.0)) + min(max(b.x, max(b.y, b.z)), 0.0) - 0.1;
                d = min(d, lerp(sphere, box, 0.5 + 0.5 * sin(Time * 0.7 + k)));
            }

            return d;
        }

        float4 RayMarch(Pixel input) : SV_Target
        {
            float2 uv = (input.Uv * 2 - 1) * float2(Width / Height, -1);
            float3 origin = float3(0, 1.2, -1.5);
            float3 ray = normalize(float3(uv.x, uv.y - 0.25, 1.6));
            float t = 0;
            bool hit = false;
            [loop] for (uint s = 0; s < Count; s++)
            {
                float d = Scene(origin + ray * t);
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
            float3 n = normalize(e.xyy * Scene(p + e.xyy) + e.yyx * Scene(p + e.yyx) + e.yxy * Scene(p + e.yxy) + e.xxx * Scene(p + e.xxx));
            float3 light = normalize(float3(-0.6, 1.0, -0.5));
            float shadow = 1;
            float st = 0.02;
            [loop] for (uint i = 0; i < 32; i++)
            {
                float h = Scene(p + light * st);
                shadow = min(shadow, 8 * h / st);
                st += clamp(h, 0.02, 0.5);
                if (shadow < 0.001 || st > 20)
                {
                    break;
                }
            }

            shadow = saturate(shadow);
            float occlusion = 0;
            [unroll] for (int a = 1; a <= 5; a++)
            {
                float hd = 0.08 * a;
                occlusion += (hd - Scene(p + n * hd)) / a;
            }

            occlusion = saturate(1 - 2 * occlusion);
            float diffuse = saturate(dot(n, light)) * shadow;
            float3 color = (0.15 * occlusion + 0.85 * diffuse) * float3(0.9, 0.8, 0.7);
            color = lerp(color, sky, 1 - exp(-0.002 * t * t));
            return float4(color, 1);
        }
        """;

    public override GpuTestInfo Info => GpuTests.RayMarching;
}

/// <summary>A compute shader over a buffer: one dispatch per unit.</summary>
[SupportedOSPlatform("windows")]
internal abstract class DispatchWorkload : GpuWorkload
{
    private readonly nint _shader, _constants, _staging;
    private readonly nint[] _views;
    private readonly nint _result;
    private readonly int _resultBytes;

    protected DispatchWorkload(Direct3D gpu, string shader, string entry, uint groups, uint count, nint[] views, nint result, int resultBytes, int stride)
        : base(gpu)
    {
        _shader = gpu.ComputeShader(Common + shader, entry);
        _constants = gpu.ConstantBuffer(16);
        gpu.SetConstants(_constants, new Constants { Count = count });
        _views = views;
        Groups = groups;
        _result = result;
        _resultBytes = resultBytes;
        _staging = resultBytes > 0 ? gpu.StagingBuffer(resultBytes, stride) : 0;
    }

    protected uint Groups { get; }

    public override void Bind() => Gpu.UseCompute(_shader, _constants, _views);

    public override void Unit() => Gpu.Dispatch(Groups);

    public override long Verify()
    {
        if (_staging == 0)
        {
            return 0;
        }

        Bind();
        Unit();
        Gpu.Finish();
        return Gpu.Checksum(_result, _staging, _resultBytes);
    }
}

[SupportedOSPlatform("windows")]
internal sealed class ComputeWorkload : DispatchWorkload
{
    private const string Shader = """
        RWStructuredBuffer<float> Numbers : register(u0);

        [numthreads(256, 1, 1)]
        void Flops(uint3 id : SV_DispatchThreadID)
        {
            float a0 = frac(id.x * 0.000001) * 0.5;
            float a1 = a0 + 0.01, a2 = a0 + 0.02, a3 = a0 + 0.03;
            float b0 = a0 + 0.1, b1 = a1 + 0.1, b2 = a2 + 0.1, b3 = a3 + 0.1;
            [loop] for (uint i = 0; i < Count; i++)
            {
                [unroll] for (int u = 0; u < 16; u++)
                {
                    a0 = mad(a0, b0, 0.25); b0 = mad(b0, a0, -0.25);
                    a1 = mad(a1, b1, 0.25); b1 = mad(b1, a1, -0.25);
                    a2 = mad(a2, b2, 0.25); b2 = mad(b2, a2, -0.25);
                    a3 = mad(a3, b3, 0.25); b3 = mad(b3, a3, -0.25);
                }
            }

            Numbers[id.x] = a0 + a1 + a2 + a3 + b0 + b1 + b2 + b3;
        }
        """;

    private readonly double _work;

    public ComputeWorkload(Direct3D gpu, bool small)
        : this(gpu, small ? 16u : 4096u, small ? 8u : 256u)
    {
    }

    private ComputeWorkload(Direct3D gpu, uint groups, uint count)
        : this(gpu, groups, count, gpu.StructuredBuffer<float>(new float[groups * 256], 4))
    {
    }

    private ComputeWorkload(Direct3D gpu, uint groups, uint count, (nint Buffer, nint View) numbers)
        : base(gpu, Shader, "Flops", groups, count, [numbers.View], numbers.Buffer, (int)groups * 256 * 4, 4) =>
        _work = groups * 256.0 * count * 256 / 1e9; // 8 multiply-adds (16 operations) × 16 per loop

    public override GpuTestInfo Info => GpuTests.Compute;

    public override double WorkPerUnit => _work;
}

[SupportedOSPlatform("windows")]
internal sealed class BandwidthWorkload : DispatchWorkload
{
    private const string Shader = """
        RWStructuredBuffer<uint4> Source : register(u0);
        RWStructuredBuffer<uint4> Target : register(u1);

        [numthreads(256, 1, 1)]
        void Copy(uint3 id : SV_DispatchThreadID)
        {
            Target[id.x] = Source[id.x] + Count;
        }
        """;

    private readonly double _work;

    public BandwidthWorkload(Direct3D gpu, bool small)
        : this(gpu, small ? 64 * 1024 : 8 * 1024 * 1024)
    {
    }

    private BandwidthWorkload(Direct3D gpu, int elements)
        : base(gpu, Shader, "Copy", (uint)(elements / 256), 1, Views(gpu, elements), 0, 0, 16) =>
        _work = 2.0 * elements * 16 / 1e9; // read and write 16 bytes per element

    public override GpuTestInfo Info => GpuTests.Bandwidth;

    public override double WorkPerUnit => _work;

    private static nint[] Views(Direct3D gpu, int elements) =>
        [gpu.StructuredBuffer(elements * 16, 16).View, gpu.StructuredBuffer(elements * 16, 16).View];
}

[SupportedOSPlatform("windows")]
internal sealed class PhysicsWorkload : DispatchWorkload
{
    private const string Shader = """
        RWStructuredBuffer<float4> Bodies : register(u0);
        RWStructuredBuffer<float4> Pulls : register(u1);
        groupshared float4 Tile[64];

        [numthreads(64, 1, 1)]
        void Gravity(uint3 id : SV_DispatchThreadID, uint3 local : SV_GroupThreadID)
        {
            float3 me = Bodies[id.x].xyz;
            float3 pull = 0;
            [loop] for (uint start = 0; start < Count; start += 64)
            {
                Tile[local.x] = Bodies[start + local.x];
                GroupMemoryBarrierWithGroupSync();
                [loop] for (uint j = 0; j < 64; j++)
                {
                    float4 other = Tile[j];
                    float3 d = other.xyz - me;
                    float inverse = rsqrt(dot(d, d) + 0.01);
                    pull += d * (other.w * inverse * inverse * inverse);
                }

                GroupMemoryBarrierWithGroupSync();
            }

            Pulls[id.x] = float4(pull, 0);
        }
        """;

    private readonly double _work;

    public PhysicsWorkload(Direct3D gpu, bool small)
        : this(gpu, small ? 1024 : 32768)
    {
    }

    private PhysicsWorkload(Direct3D gpu, int bodies)
        : this(gpu, bodies, gpu.StructuredBuffer<float>(Bodies(bodies), 16), gpu.StructuredBuffer<float>(new float[bodies * 4], 16))
    {
    }

    private PhysicsWorkload(Direct3D gpu, int bodies, (nint Buffer, nint View) positions, (nint Buffer, nint View) pulls)
        : base(gpu, Shader, "Gravity", (uint)(bodies / 64), (uint)bodies, [positions.View, pulls.View], pulls.Buffer, bodies * 16, 16) =>
        _work = (double)bodies * bodies / 1e6;

    public override GpuTestInfo Info => GpuTests.Physics;

    public override double WorkPerUnit => _work;

    /// <summary>Bodies spread through a cube (x, y, z and a mass each), the same every time.</summary>
    private static float[] Bodies(int count)
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
