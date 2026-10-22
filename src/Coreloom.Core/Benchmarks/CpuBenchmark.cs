using System.Diagnostics;
using System.IO.Compression;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Coreloom.Core.Benchmarks;

/// <summary>
/// One CPU test: a fixed piece of real-world-style work (compressing files, processing text, hashing, finding routes,
/// tracing rays, multiplying matrices, filtering an image, simulating physics), run over and over — on one thread for
/// the single-core score and on every thread at once for the multi-core score.
/// </summary>
public abstract class CpuTest
{
    /// <summary>Sizes for the real benchmark; small ones keep unit tests fast.</summary>
    protected CpuTest(bool small) => Small = small;

    protected bool Small { get; }

    public abstract string Id { get; }

    public abstract string Name { get; }

    /// <summary>"Integer", "Floating point" or "Cryptography".</summary>
    public abstract string Category { get; }

    /// <summary>What the test does, in a sentence.</summary>
    public abstract string Description { get; }

    /// <summary>The unit its rate is measured in ("MB/s", "Mpixels/s", "GFLOPS"…).</summary>
    public abstract string Unit { get; }

    /// <summary>Work one iteration does, in the unit's quantity (megabytes, megapixels…).</summary>
    public abstract double WorkPerIteration { get; }

    /// <summary>
    /// The rate that scores 1,000 points: one core of Coreloom's reference machine (a 2.8 GHz Intel Xeon server core,
    /// the machine Coreloom is built on). A core twice as fast scores 2,000.
    /// </summary>
    public abstract double Reference { get; }

    /// <summary>A worker with its own data, for one thread.</summary>
    public abstract CpuWorker CreateWorker(int seed);
}

/// <summary>One thread's copy of a test's data and its work.</summary>
public abstract class CpuWorker
{
    /// <summary>Runs one iteration and returns a checksum of the result (the same every time on a stable CPU).</summary>
    public abstract long Run();
}

/// <summary>The CPU tests, in the order they run.</summary>
public static class CpuTests
{
    public static IReadOnlyList<CpuTest> All(bool small = false) =>
    [
        new CompressionTest(small),
        new TextProcessingTest(small),
        new CryptographyTest(small),
        new NavigationTest(small),
        new RayTracingTest(small),
        new MatrixTest(small),
        new ImageFilterTest(small),
        new PhysicsTest(small),
    ];

    /// <summary>Deterministic pseudo-random numbers (the same data on every PC, every run).</summary>
    internal static ulong Next(ref ulong state)
    {
        state ^= state << 13;
        state ^= state >> 7;
        state ^= state << 17;
        return state;
    }

    internal static ulong Seed(int seed) => 0x9E3779B97F4A7C15UL ^ (ulong)(uint)(seed + 1) * 0xBF58476D1CE4E5B9UL;

    /// <summary>Text made of common words, numbers and punctuation (compresses and tokenizes like real documents).</summary>
    internal static byte[] Text(int bytes, int seed)
    {
        string[] words =
        [
            "the", "of", "and", "to", "in", "a", "is", "that", "for", "it", "as", "was", "with", "be", "by", "on", "not",
            "processor", "graphics", "memory", "frame", "render", "texture", "shader", "thread", "cache", "clock", "power",
            "temperature", "benchmark", "performance", "Coreloom", "Windows", "driver", "update", "monitor", "storage",
            "network", "game", "score", "result", "voltage", "fan", "speed", "system", "window", "light", "shadow",
        ];
        var data = new byte[bytes];
        ulong state = Seed(seed);
        int i = 0;
        while (i < bytes)
        {
            ulong r = Next(ref state);
            string word = (r % 11) == 0 ? ((r >> 8) % 100000).ToString(System.Globalization.CultureInfo.InvariantCulture) : words[(int)((r >> 16) % (ulong)words.Length)];
            for (int c = 0; c < word.Length && i < bytes; c++)
            {
                data[i++] = (byte)word[c];
            }

            if (i < bytes)
            {
                data[i++] = (r % 17) == 0 ? (byte)'.' : (r % 23) == 0 ? (byte)'\n' : (r % 13) == 0 ? (byte)',' : (byte)' ';
            }
        }

        return data;
    }
}

/// <summary>Compresses and decompresses a document (Deflate, as in .zip files).</summary>
public sealed class CompressionTest(bool small = false) : CpuTest(small)
{
    private int Size => Small ? 64 * 1024 : 1024 * 1024;

    public override string Id => "compression";

    public override string Name => "File compression";

    public override string Category => "Integer";

    public override string Description => "Compresses and unpacks a 1 MB document, as when zipping files.";

    public override string Unit => "MB/s";

    public override double WorkPerIteration => Size / 1e6;

    public override double Reference => 15.0;

    public override CpuWorker CreateWorker(int seed) => new Worker(CpuTests.Text(Size, seed));

    private sealed class Worker(byte[] data) : CpuWorker
    {
        private readonly MemoryStream _compressed = new(data.Length);
        private readonly byte[] _restored = new byte[data.Length];

        public override long Run()
        {
            _compressed.Position = 0;
            _compressed.SetLength(0);
            using (var deflate = new DeflateStream(_compressed, CompressionLevel.Optimal, leaveOpen: true))
            {
                deflate.Write(data);
            }

            long size = _compressed.Length;
            _compressed.Position = 0;
            using (var inflate = new DeflateStream(_compressed, CompressionMode.Decompress, leaveOpen: true))
            {
                inflate.ReadExactly(_restored);
            }

            return (size * 31) + _restored[^1];
        }
    }
}

/// <summary>Splits text into words and counts them, and searches it (as in a search index or a code editor).</summary>
public sealed class TextProcessingTest(bool small = false) : CpuTest(small)
{
    private const int TableSize = 1 << 16;

    private int Size => Small ? 64 * 1024 : 1024 * 1024;

    public override string Id => "text";

    public override string Name => "Text processing";

    public override string Category => "Integer";

    public override string Description => "Splits a 1 MB document into words, counts each word and searches for phrases.";

    public override string Unit => "MB/s";

    public override double WorkPerIteration => Size / 1e6;

    public override double Reference => 264;

    public override CpuWorker CreateWorker(int seed) => new Worker(CpuTests.Text(Size, seed));

    private sealed class Worker(byte[] data) : CpuWorker
    {
        private readonly uint[] _keys = new uint[TableSize];
        private readonly int[] _counts = new int[TableSize];
        private static readonly byte[][] Phrases = ["the processor"u8.ToArray(), "frame"u8.ToArray(), "shader cache"u8.ToArray()];

        public override long Run()
        {
            Array.Clear(_keys);
            Array.Clear(_counts);
            int words = 0, distinct = 0, longest = 0;
            uint hash = 2166136261;
            int length = 0;
            foreach (byte b in data)
            {
                bool letter = (uint)((b | 0x20) - 'a') <= 'z' - 'a' || (uint)(b - '0') <= 9;
                if (letter)
                {
                    hash = (hash ^ (uint)(b | 0x20)) * 16777619;
                    length++;
                    continue;
                }

                if (length > 0)
                {
                    words++;
                    longest = Math.Max(longest, length);
                    uint key = hash | 1;
                    int slot = (int)(hash & (TableSize - 1));
                    while (_keys[slot] != 0 && _keys[slot] != key)
                    {
                        slot = (slot + 1) & (TableSize - 1);
                    }

                    if (_keys[slot] == 0)
                    {
                        _keys[slot] = key;
                        distinct++;
                    }

                    _counts[slot]++;
                    hash = 2166136261;
                    length = 0;
                }
            }

            long found = 0;
            foreach (byte[] phrase in Phrases)
            {
                var rest = data.AsSpan();
                int at;
                while ((at = rest.IndexOf(phrase)) >= 0)
                {
                    found++;
                    rest = rest[(at + phrase.Length)..];
                }
            }

            return (words * 1_000_003L) + (distinct * 1009L) + (longest * 7L) + found;
        }
    }
}

/// <summary>Hashes (SHA-256) and encrypts (AES-GCM) data, as when verifying downloads and storing files securely.</summary>
public sealed class CryptographyTest(bool small = false) : CpuTest(small)
{
    private int Size => Small ? 64 * 1024 : 1024 * 1024;

    public override string Id => "crypto";

    public override string Name => "Cryptography";

    public override string Category => "Cryptography";

    public override string Description => "Hashes 1 MB with SHA-256 and encrypts 1 MB with AES-GCM, using the processor's crypto instructions.";

    public override string Unit => "MB/s";

    public override double WorkPerIteration => 2 * Size / 1e6;

    public override double Reference => 750;

    public override CpuWorker CreateWorker(int seed) => new Worker(CpuTests.Text(Size, seed), seed);

    private sealed class Worker : CpuWorker
    {
        private readonly byte[] _data;
        private readonly byte[] _cipher;
        private readonly byte[] _hash = new byte[32];
        private readonly byte[] _tag = new byte[16];
        private readonly byte[] _nonce = new byte[12];
        private readonly AesGcm? _aes;

        public Worker(byte[] data, int seed)
        {
            _data = data;
            _cipher = new byte[data.Length];
            var key = new byte[16];
            key[0] = (byte)seed;
            _aes = AesGcm.IsSupported ? new AesGcm(key, 16) : null;
        }

        public override long Run()
        {
            SHA256.HashData(_data, _hash);
            if (_aes is not null)
            {
                _aes.Encrypt(_nonce, _data, _cipher, _tag);
            }
            else
            {
                SHA256.HashData(_hash, _cipher.AsSpan(0, 32)); // no AES here: hash twice so the work stays comparable
                SHA256.HashData(_data, _hash);
            }

            return BitConverter.ToInt64(_hash, 0) ^ BitConverter.ToInt64(_tag, 0);
        }
    }
}

/// <summary>Finds the shortest route across a map (Dijkstra on a weighted grid), as in navigation apps and game AI.</summary>
public sealed class NavigationTest(bool small = false) : CpuTest(small)
{
    private int Side => Small ? 48 : 256;

    public override string Id => "navigation";

    public override string Name => "Navigation";

    public override string Category => "Integer";

    public override string Description => "Finds the shortest route between two points of a 256 × 256 road map.";

    public override string Unit => "routes/s";

    public override double WorkPerIteration => 1;

    public override double Reference => 300;

    public override CpuWorker CreateWorker(int seed) => new Worker(Side, seed);

    private sealed class Worker : CpuWorker
    {
        private readonly int _side;
        private readonly byte[] _cost;
        private readonly int[] _distance;
        private readonly PriorityQueue<int, int> _queue;
        private ulong _state;

        public Worker(int side, int seed)
        {
            _side = side;
            _cost = new byte[side * side];
            _distance = new int[side * side];
            _queue = new PriorityQueue<int, int>(side * side * 2);
            _state = CpuTests.Seed(seed);
            for (int i = 0; i < _cost.Length; i++)
            {
                _cost[i] = (byte)(1 + (CpuTests.Next(ref _state) % 9));
            }
        }

        public override long Run()
        {
            int n = _cost.Length;
            int from = (int)(CpuTests.Next(ref _state) % (ulong)n);
            int to = (int)(CpuTests.Next(ref _state) % (ulong)n);
            Array.Fill(_distance, int.MaxValue);
            _queue.Clear();
            _distance[from] = 0;
            _queue.Enqueue(from, 0);
            int settled = 0;
            while (_queue.TryDequeue(out int node, out int distance))
            {
                if (distance > _distance[node])
                {
                    continue;
                }

                settled++;
                if (node == to)
                {
                    break;
                }

                int x = node % _side, y = node / _side;
                if (x > 0)
                {
                    Relax(node - 1, distance);
                }

                if (x < _side - 1)
                {
                    Relax(node + 1, distance);
                }

                if (y > 0)
                {
                    Relax(node - _side, distance);
                }

                if (y < _side - 1)
                {
                    Relax(node + _side, distance);
                }
            }

            return ((long)_distance[to] << 20) + settled;
        }

        private void Relax(int next, int distance)
        {
            int candidate = distance + _cost[next];
            if (candidate < _distance[next])
            {
                _distance[next] = candidate;
                _queue.Enqueue(next, candidate);
            }
        }
    }
}

/// <summary>Traces rays through a scene of spheres with shadows and reflections (double precision), as in renderers.</summary>
public sealed class RayTracingTest(bool small = false) : CpuTest(small)
{
    private int Width => Small ? 32 : 320;

    private int Height => Small ? 18 : 180;

    public override string Id => "raytracing";

    public override string Name => "Ray tracing";

    public override string Category => "Floating point";

    public override string Description => "Renders a scene of shiny spheres with shadows and reflections, one ray at a time.";

    public override string Unit => "Mpixels/s";

    public override double WorkPerIteration => Width * Height / 1e6;

    public override double Reference => 8.0;

    public override CpuWorker CreateWorker(int seed) => new Worker(Width, Height, seed);

    private readonly record struct V3(double X, double Y, double Z)
    {
        public static V3 operator +(V3 a, V3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

        public static V3 operator -(V3 a, V3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

        public static V3 operator *(V3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);

        public double Dot(V3 b) => (X * b.X) + (Y * b.Y) + (Z * b.Z);

        public V3 Normalize() => this * (1 / Math.Sqrt(Dot(this)));
    }

    private sealed class Worker : CpuWorker
    {
        private readonly int _width, _height;
        private readonly V3[] _centers;
        private readonly double[] _radii;
        private readonly V3[] _colors;
        private readonly V3 _light = new V3(-0.6, 1, -0.5).Normalize();

        public Worker(int width, int height, int seed)
        {
            _width = width;
            _height = height;
            ulong state = CpuTests.Seed(seed);
            _centers = new V3[8];
            _radii = new double[8];
            _colors = new V3[8];
            for (int i = 0; i < 8; i++)
            {
                double Rnd() => (CpuTests.Next(ref state) % 1000) / 1000.0;
                _centers[i] = new V3((Rnd() * 6) - 3, (Rnd() * 1.5) - 0.5, 4 + (Rnd() * 5));
                _radii[i] = 0.35 + (Rnd() * 0.65);
                _colors[i] = new V3(0.2 + Rnd(), 0.2 + Rnd(), 0.2 + Rnd()) * 0.8;
            }
        }

        public override long Run()
        {
            double sum = 0;
            for (int y = 0; y < _height; y++)
            {
                for (int x = 0; x < _width; x++)
                {
                    var direction = new V3(((x + 0.5) / _width * 2 - 1) * 1.6, (1 - ((y + 0.5) / _height * 2)) * 0.9, 1).Normalize();
                    var color = Trace(new V3(0, 0.3, -1), direction, 3);
                    sum += color.X + color.Y + color.Z;
                }
            }

            return (long)(sum * 1000);
        }

        private V3 Trace(V3 origin, V3 direction, int depth)
        {
            int hit = -1;
            double nearest = double.MaxValue;
            for (int i = 0; i < _centers.Length; i++)
            {
                double t = Intersect(origin, direction, i);
                if (t > 1e-4 && t < nearest)
                {
                    nearest = t;
                    hit = i;
                }
            }

            double floor = direction.Y < -1e-6 ? (-1 - origin.Y) / direction.Y : double.MaxValue;
            if (hit < 0 && floor == double.MaxValue)
            {
                return new V3(0.55, 0.7, 0.95) * (0.6 + (0.4 * direction.Y)); // the sky
            }

            V3 point, normal, albedo;
            double reflect;
            if (floor < nearest)
            {
                point = origin + (direction * floor);
                normal = new V3(0, 1, 0);
                bool check = ((int)Math.Floor(point.X) + (int)Math.Floor(point.Z) & 1) == 0;
                albedo = check ? new V3(0.8, 0.8, 0.8) : new V3(0.25, 0.25, 0.3);
                reflect = 0.15;
            }
            else
            {
                point = origin + (direction * nearest);
                normal = (point - _centers[hit]).Normalize();
                albedo = _colors[hit];
                reflect = 0.45;
            }

            double light = Math.Max(0, normal.Dot(_light));
            if (light > 0 && InShadow(point + (normal * 1e-3)))
            {
                light *= 0.2;
            }

            var color = albedo * (0.12 + (0.88 * light));
            if (depth > 0)
            {
                var bounce = direction - (normal * (2 * direction.Dot(normal)));
                color = (color * (1 - reflect)) + (Trace(point + (normal * 1e-3), bounce, depth - 1) * reflect);
            }

            return color;
        }

        private bool InShadow(V3 point)
        {
            for (int i = 0; i < _centers.Length; i++)
            {
                if (Intersect(point, _light, i) > 1e-4)
                {
                    return true;
                }
            }

            return false;
        }

        private double Intersect(V3 origin, V3 direction, int i)
        {
            var oc = origin - _centers[i];
            double b = oc.Dot(direction);
            double c = oc.Dot(oc) - (_radii[i] * _radii[i]);
            double h = (b * b) - c;
            if (h < 0)
            {
                return -1;
            }

            h = Math.Sqrt(h);
            double t = -b - h;
            return t > 1e-4 ? t : -b + h;
        }
    }
}

/// <summary>Multiplies matrices with the processor's vector instructions (AVX), the core of machine learning.</summary>
public sealed class MatrixTest(bool small = false) : CpuTest(small)
{
    private int N => Small ? 64 : 384;

    public override string Id => "matrix";

    public override string Name => "Machine learning";

    public override string Category => "Floating point";

    public override string Description => "Multiplies two 384 × 384 matrices with vector (SIMD) instructions, as neural networks do.";

    public override string Unit => "GFLOPS";

    public override double WorkPerIteration => 2.0 * N * N * N / 1e9;

    public override double Reference => 18.2;

    public override CpuWorker CreateWorker(int seed) => new Worker(N, seed);

    internal sealed class Worker : CpuWorker
    {
        private readonly int _n;
        private readonly float[] _a, _b, _c;

        public Worker(int n, int seed)
        {
            _n = n;
            _a = new float[n * n];
            _b = new float[n * n];
            _c = new float[n * n];
            ulong state = CpuTests.Seed(seed);
            for (int i = 0; i < _a.Length; i++)
            {
                _a[i] = ((CpuTests.Next(ref state) % 2001) - 1000) / 1000f;
                _b[i] = ((CpuTests.Next(ref state) % 2001) - 1000) / 1000f;
            }
        }

        public override long Run()
        {
            int n = _n, width = Vector<float>.Count;
            Array.Clear(_c);
            ref float a = ref MemoryMarshal.GetArrayDataReference(_a);
            ref float b = ref MemoryMarshal.GetArrayDataReference(_b);
            ref float c = ref MemoryMarshal.GetArrayDataReference(_c);
            for (int i = 0; i < n; i++)
            {
                nuint row = (nuint)(i * n);
                int k = 0;
                for (; k <= n - 4; k += 4)
                {
                    // Four rows of B at a time, so each part of C's row is loaded and stored once per four.
                    var f0 = new Vector<float>(Unsafe.Add(ref a, row + (nuint)k));
                    var f1 = new Vector<float>(Unsafe.Add(ref a, row + (nuint)k + 1));
                    var f2 = new Vector<float>(Unsafe.Add(ref a, row + (nuint)k + 2));
                    var f3 = new Vector<float>(Unsafe.Add(ref a, row + (nuint)k + 3));
                    nuint b0 = (nuint)(k * n), b1 = b0 + (nuint)n, b2 = b1 + (nuint)n, b3 = b2 + (nuint)n;
                    int j = 0;
                    for (; j <= n - width; j += width)
                    {
                        var sum = Vector.LoadUnsafe(ref c, row + (nuint)j);
                        sum += f0 * Vector.LoadUnsafe(ref b, b0 + (nuint)j);
                        sum += f1 * Vector.LoadUnsafe(ref b, b1 + (nuint)j);
                        sum += f2 * Vector.LoadUnsafe(ref b, b2 + (nuint)j);
                        sum += f3 * Vector.LoadUnsafe(ref b, b3 + (nuint)j);
                        sum.StoreUnsafe(ref c, row + (nuint)j);
                    }

                    for (; j < n; j++)
                    {
                        Unsafe.Add(ref c, row + (nuint)j) +=
                            (f0[0] * Unsafe.Add(ref b, b0 + (nuint)j)) + (f1[0] * Unsafe.Add(ref b, b1 + (nuint)j)) +
                            (f2[0] * Unsafe.Add(ref b, b2 + (nuint)j)) + (f3[0] * Unsafe.Add(ref b, b3 + (nuint)j));
                    }
                }

                for (; k < n; k++)
                {
                    float f = Unsafe.Add(ref a, row + (nuint)k);
                    for (int j = 0; j < n; j++)
                    {
                        Unsafe.Add(ref c, row + (nuint)j) += f * Unsafe.Add(ref b, (nuint)((k * n) + j));
                    }
                }
            }

            double total = 0;
            for (int i = 0; i < _c.Length; i += 7)
            {
                total += _c[i];
            }

            return BitConverter.DoubleToInt64Bits(total);
        }
    }
}

/// <summary>Blurs a photo (a separable Gaussian filter with vector instructions), as photo editors do.</summary>
public sealed class ImageFilterTest(bool small = false) : CpuTest(small)
{
    private const int Radius = 6;

    private int Width => Small ? 128 : 1280;

    private int Height => Small ? 72 : 720;

    public override string Id => "imagefilter";

    public override string Name => "Photo filter";

    public override string Category => "Floating point";

    public override string Description => "Blurs a 1280 × 720 photo with a 13-tap Gaussian filter, as photo editors do.";

    public override string Unit => "Mpixels/s";

    public override double WorkPerIteration => Width * Height / 1e6;

    public override double Reference => 124;

    public override CpuWorker CreateWorker(int seed) => new Worker(Width, Height, seed);

    private sealed class Worker : CpuWorker
    {
        private readonly int _width, _height;
        private readonly float[] _image, _temp, _output;
        private readonly float[] _weights = new float[(2 * Radius) + 1];

        public Worker(int width, int height, int seed)
        {
            _width = width;
            _height = height;
            int stride = width + (2 * Radius);
            _image = new float[stride * (height + (2 * Radius))];
            _temp = new float[stride * (height + (2 * Radius))];
            _output = new float[width * height];
            ulong state = CpuTests.Seed(seed);
            for (int i = 0; i < _image.Length; i++)
            {
                _image[i] = (CpuTests.Next(ref state) % 256) / 255f;
            }

            float total = 0;
            for (int k = -Radius; k <= Radius; k++)
            {
                _weights[k + Radius] = MathF.Exp(-(k * k) / (2f * 3f * 3f));
                total += _weights[k + Radius];
            }

            for (int k = 0; k < _weights.Length; k++)
            {
                _weights[k] /= total;
            }
        }

        public override long Run()
        {
            int stride = _width + (2 * Radius), width = Vector<float>.Count;
            var image = _image.AsSpan();
            var temp = _temp.AsSpan();
            var output = _output.AsSpan();

            // Across: each row (with its margins) into temp.
            for (int y = 0; y < _height + (2 * Radius); y++)
            {
                var source = image.Slice(y * stride, stride);
                var target = temp.Slice((y * stride) + Radius, _width);
                int x = 0;
                for (; x <= _width - width; x += width)
                {
                    var sum = Vector<float>.Zero;
                    for (int k = 0; k < _weights.Length; k++)
                    {
                        sum += new Vector<float>(_weights[k]) * new Vector<float>(source[(x + k)..]);
                    }

                    sum.CopyTo(target[x..]);
                }

                for (; x < _width; x++)
                {
                    float sum = 0;
                    for (int k = 0; k < _weights.Length; k++)
                    {
                        sum += _weights[k] * source[x + k];
                    }

                    target[x] = sum;
                }
            }

            // Down: each output row from the rows above and below.
            for (int y = 0; y < _height; y++)
            {
                var target = output.Slice(y * _width, _width);
                int x = 0;
                for (; x <= _width - width; x += width)
                {
                    var sum = Vector<float>.Zero;
                    for (int k = 0; k < _weights.Length; k++)
                    {
                        sum += new Vector<float>(_weights[k]) * new Vector<float>(temp[(((y + k) * stride) + Radius + x)..]);
                    }

                    sum.CopyTo(target[x..]);
                }

                for (; x < _width; x++)
                {
                    float sum = 0;
                    for (int k = 0; k < _weights.Length; k++)
                    {
                        sum += _weights[k] * temp[((y + k) * stride) + Radius + x];
                    }

                    target[x] = sum;
                }
            }

            double total = 0;
            for (int i = 0; i < output.Length; i += 13)
            {
                total += output[i];
            }

            return BitConverter.DoubleToInt64Bits(total);
        }
    }
}

/// <summary>Simulates gravity between bodies (an N-body step in double precision), as in physics engines.</summary>
public sealed class PhysicsTest(bool small = false) : CpuTest(small)
{
    private int Bodies => Small ? 128 : 1024;

    public override string Id => "physics";

    public override string Name => "Physics";

    public override string Category => "Floating point";

    public override string Description => "Simulates gravity between 1,024 bodies, each pulling on every other (about a million pairs a step).";

    public override string Unit => "M pairs/s";

    public override double WorkPerIteration => (double)Bodies * Bodies / 1e6;

    public override double Reference => 151;

    public override CpuWorker CreateWorker(int seed) => new Worker(Bodies, seed);

    private sealed class Worker : CpuWorker
    {
        private readonly double[] _x, _y, _z, _vx, _vy, _vz, _mass;
        private readonly double[] _startX, _startY, _startZ;

        public Worker(int n, int seed)
        {
            _x = new double[n];
            _y = new double[n];
            _z = new double[n];
            _vx = new double[n];
            _vy = new double[n];
            _vz = new double[n];
            _mass = new double[n];
            ulong state = CpuTests.Seed(seed);
            for (int i = 0; i < n; i++)
            {
                _x[i] = (CpuTests.Next(ref state) % 2000) - 1000;
                _y[i] = (CpuTests.Next(ref state) % 2000) - 1000;
                _z[i] = (CpuTests.Next(ref state) % 2000) - 1000;
                _mass[i] = 1 + (CpuTests.Next(ref state) % 100);
            }

            (_startX, _startY, _startZ) = ((double[])_x.Clone(), (double[])_y.Clone(), (double[])_z.Clone());
        }

        public override long Run()
        {
            // Every step starts from the same bodies at rest, so every run does the same work and gets the same answer.
            _startX.CopyTo(_x, 0);
            _startY.CopyTo(_y, 0);
            _startZ.CopyTo(_z, 0);
            Array.Clear(_vx);
            Array.Clear(_vy);
            Array.Clear(_vz);
            int n = _x.Length;
            const double dt = 0.01, soften = 10;
            for (int i = 0; i < n; i++)
            {
                double ax = 0, ay = 0, az = 0, xi = _x[i], yi = _y[i], zi = _z[i];
                for (int j = 0; j < n; j++)
                {
                    double dx = _x[j] - xi, dy = _y[j] - yi, dz = _z[j] - zi;
                    double d2 = (dx * dx) + (dy * dy) + (dz * dz) + soften;
                    double inv = 1 / Math.Sqrt(d2);
                    double f = _mass[j] * inv * inv * inv;
                    ax += dx * f;
                    ay += dy * f;
                    az += dz * f;
                }

                _vx[i] += ax * dt;
                _vy[i] += ay * dt;
                _vz[i] += az * dt;
            }

            double energy = 0;
            for (int i = 0; i < n; i++)
            {
                _x[i] += _vx[i] * dt;
                _y[i] += _vy[i] * dt;
                _z[i] += _vz[i] * dt;
                energy += _mass[i] * ((_vx[i] * _vx[i]) + (_vy[i] * _vy[i]) + (_vz[i] * _vz[i]));
            }

            return BitConverter.DoubleToInt64Bits(energy);
        }
    }
}

/// <summary>A test's measured rates and scores.</summary>
public sealed record CpuTestScore(string Id, string Name, string Category, string Unit, double SingleRate, double MultiRate, double SingleScore, double MultiScore);

/// <summary>How a CPU benchmark is run.</summary>
/// <param name="Seconds">Measured time of each test, on one thread and on all of them.</param>
/// <param name="WarmupSeconds">Untimed run before each measurement (code compiled, caches and clocks up).</param>
/// <param name="Threads">Threads of the multi-core part (0: one per logical processor).</param>
/// <param name="Small">Small sizes (for tests).</param>
public sealed record CpuBenchmarkOptions(double Seconds = 1.5, double WarmupSeconds = 0.3, int Threads = 0, bool Small = false)
{
    public int ThreadCount => Threads > 0 ? Threads : Environment.ProcessorCount;
}

/// <summary>Where a benchmark is: the step's name and how far it is overall (0–1).</summary>
public readonly record struct BenchmarkProgress(string Step, double Fraction, string? Detail = null);

/// <summary>The CPU benchmark's outcome.</summary>
public sealed record CpuBenchmarkScores(IReadOnlyList<CpuTestScore> Tests, double SingleScore, double MultiScore, int Threads, double MemoryBandwidthGBs, double MemoryLatencyNs);

/// <summary>
/// Runs the CPU tests: each one first on a single thread (single-core score) and then on one thread per logical
/// processor at once (multi-core score), timing how much work gets done. A test's score is its rate against the
/// reference (1,000 = one core of the reference machine); the overall scores are the geometric means, so every test
/// counts the same. Memory bandwidth and latency are measured too, for the details (they aren't scored).
/// </summary>
public static class CpuBenchmark
{
    private static int _sink;

    public static async Task<CpuBenchmarkScores> RunAsync(CpuBenchmarkOptions options, IProgress<BenchmarkProgress>? progress, CancellationToken cancellationToken)
    {
        var tests = CpuTests.All(options.Small);
        var scores = new List<CpuTestScore>();
        int threads = options.ThreadCount;
        int steps = (tests.Count * 2) + 1;
        int step = 0;
        foreach (var test in tests)
        {
            progress?.Report(new BenchmarkProgress(test.Name, step++ / (double)steps, "Single-core"));
            double single = await Task.Run(() => Measure(test, 1, options.Seconds, options.WarmupSeconds, ThreadPriority.Normal, cancellationToken), cancellationToken);
            progress?.Report(new BenchmarkProgress(test.Name, step++ / (double)steps, $"Multi-core · {threads} threads"));
            double multi = await Task.Run(() => Measure(test, threads, options.Seconds, options.WarmupSeconds, ThreadPriority.BelowNormal, cancellationToken), cancellationToken);
            scores.Add(new CpuTestScore(test.Id, test.Name, test.Category, test.Unit, single, multi, Score(single, test.Reference), Score(multi, test.Reference)));
        }

        progress?.Report(new BenchmarkProgress("Memory", step / (double)steps, "Bandwidth and latency"));
        var (bandwidth, latency) = await Task.Run(() => MeasureMemory(threads, options.Small, cancellationToken), cancellationToken);
        progress?.Report(new BenchmarkProgress("Done", 1));
        return new CpuBenchmarkScores(scores, GeometricMean(scores.Select(s => s.SingleScore)), GeometricMean(scores.Select(s => s.MultiScore)), threads, bandwidth, latency);
    }

    /// <summary>Points for a rate: 1,000 at the reference rate.</summary>
    public static double Score(double rate, double reference) => rate > 0 && reference > 0 ? rate / reference * 1000 : 0;

    /// <summary>The geometric mean (0 when any is 0 or there are none).</summary>
    public static double GeometricMean(IEnumerable<double> values)
    {
        double logs = 0;
        int n = 0;
        foreach (double v in values)
        {
            if (!(v > 0))
            {
                return 0;
            }

            logs += Math.Log(v);
            n++;
        }

        return n == 0 ? 0 : Math.Exp(logs / n);
    }

    /// <summary>
    /// Runs a test on <paramref name="threads"/> threads at once for <paramref name="seconds"/> (after a warm-up) and
    /// returns the total rate (work per second, summed over the threads).
    /// </summary>
    public static double Measure(CpuTest test, int threads, double seconds, double warmupSeconds, ThreadPriority priority, CancellationToken cancellationToken)
    {
        var rates = new double[threads];
        var failures = new Exception?[threads];
        using var start = new Barrier(threads);
        var workers = new Thread[threads];
        for (int t = 0; t < threads; t++)
        {
            int index = t;
            workers[t] = new Thread(() =>
            {
                try
                {
                    var worker = test.CreateWorker(index);
                    var clock = Stopwatch.StartNew();
                    do
                    {
                        worker.Run();
                    }
                    while (clock.Elapsed.TotalSeconds < warmupSeconds && !cancellationToken.IsCancellationRequested);

                    start.SignalAndWait(cancellationToken);
                    long iterations = 0;
                    clock.Restart();
                    do
                    {
                        worker.Run();
                        iterations++;
                    }
                    while (clock.Elapsed.TotalSeconds < seconds && !cancellationToken.IsCancellationRequested);

                    rates[index] = iterations * test.WorkPerIteration / clock.Elapsed.TotalSeconds;
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception e)
                {
                    failures[index] = e;
                    start.RemoveParticipant();
                }
            })
            {
                IsBackground = true,
                Priority = priority,
                Name = $"Coreloom benchmark {index}",
            };
            workers[t].Start();
        }

        foreach (var worker in workers)
        {
            worker.Join();
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (failures.FirstOrDefault(f => f is not null) is { } failure)
        {
            throw new InvalidOperationException($"The {test.Name} test failed: {failure.Message}", failure);
        }

        return rates.Sum();
    }

    /// <summary>Copy bandwidth (GB/s, read + write, every thread) and latency (ns per dependent random read).</summary>
    public static (double BandwidthGBs, double LatencyNs) MeasureMemory(int threads, bool small, CancellationToken cancellationToken)
    {
        // Bandwidth: each thread copies its own 8 MB (well past the caches when all copy at once) back and forth.
        int perThread = small ? 1 << 20 : 8 << 20;
        var totals = new double[threads];
        using var start = new Barrier(threads);
        var workers = Enumerable.Range(0, threads).Select(index => new Thread(() =>
        {
            var a = new byte[perThread];
            var b = new byte[perThread];
            a.AsSpan().Fill(1);
            start.SignalAndWait();
            var clock = Stopwatch.StartNew();
            long bytes = 0;
            do
            {
                a.AsSpan().CopyTo(b);
                b.AsSpan().CopyTo(a);
                bytes += 4L * perThread; // read + write, twice
            }
            while (clock.Elapsed.TotalSeconds < (small ? 0.05 : 0.6) && !cancellationToken.IsCancellationRequested);

            totals[index] = bytes / clock.Elapsed.TotalSeconds;
        })
        { IsBackground = true, Priority = ThreadPriority.BelowNormal }).ToArray();
        foreach (var worker in workers)
        {
            worker.Start();
        }

        foreach (var worker in workers)
        {
            worker.Join();
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Latency: a random cycle through 64 MB, each read depending on the one before.
        int count = small ? 1 << 16 : 16 << 20;
        var next = new int[count];
        for (int i = 0; i < count; i++)
        {
            next[i] = i;
        }

        ulong state = CpuTests.Seed(42);
        for (int i = count - 1; i > 0; i--)
        {
            int j = (int)(CpuTests.Next(ref state) % (ulong)i); // Sattolo: one cycle through every slot
            (next[i], next[j]) = (next[j], next[i]);
        }

        int steps = small ? 100_000 : 10_000_000;
        int at = 0;
        var timer = Stopwatch.StartNew();
        for (int i = 0; i < steps; i++)
        {
            at = next[at];
        }

        double latency = timer.Elapsed.TotalMilliseconds * 1e6 / steps;
        Volatile.Write(ref _sink, at); // keeps the chase from being optimized away
        return (totals.Sum() / 1e9, latency);
    }
}
