using System.Diagnostics;
using System.Runtime.InteropServices;
using Coreloom.Core.Formatting;
using Microsoft.Win32.SafeHandles;

namespace Coreloom.Core.Benchmarks;

/// <summary>One drive test: block size, how many requests are kept in flight, read or write, sequential or random.</summary>
public sealed record DiskTest(string Id, string Name, int BlockSize, int QueueDepth, bool Write, bool Random)
{
    /// <summary>"SEQ1M Q8", "RND4K Q32"… (the usual shorthand).</summary>
    public string ShortName => $"{(Random ? "RND" : "SEQ")}{(BlockSize >= 1 << 20 ? $"{BlockSize >> 20}M" : $"{BlockSize >> 10}K")} Q{QueueDepth}";
}

/// <summary>The tests a run is made of, in the order they run.</summary>
public static class DiskTests
{
    public static DiskTest SequentialRead { get; } = new("seq-read", "Sequential read", 1 << 20, 8, false, false);

    public static DiskTest SequentialWrite { get; } = new("seq-write", "Sequential write", 1 << 20, 8, true, false);

    public static DiskTest RandomDeepRead { get; } = new("rnd32-read", "Random read, deep queue", 4096, 32, false, true);

    public static DiskTest RandomDeepWrite { get; } = new("rnd32-write", "Random write, deep queue", 4096, 32, true, true);

    public static DiskTest RandomRead { get; } = new("rnd1-read", "Random read", 4096, 1, false, true);

    public static DiskTest RandomWrite { get; } = new("rnd1-write", "Random write", 4096, 1, true, true);

    /// <summary>Writes come before reads so every read finds real data on the drive.</summary>
    public static IReadOnlyList<DiskTest> All { get; } =
        [SequentialWrite, SequentialRead, RandomDeepWrite, RandomDeepRead, RandomWrite, RandomRead];
}

/// <summary>Where and how long to test.</summary>
public sealed record DiskBenchmarkOptions(string Directory, long FileSize)
{
    /// <summary>How long each test runs at most.</summary>
    public TimeSpan TestDuration { get; init; } = TimeSpan.FromSeconds(4);

    /// <summary>Sequential tests stop after this many passes over the file, even with time left (limits wear).</summary>
    public int MaxPasses { get; init; } = 2;

    /// <summary>Bypass the file cache (Windows' unbuffered I/O, macOS's F_NOCACHE), so the drive itself is measured.</summary>
    public bool Unbuffered { get; init; } = true;

    /// <summary>Kept free on the drive besides the test file.</summary>
    public long Headroom { get; init; } = 256L << 20;
}

/// <summary>Result of one test.</summary>
public sealed record DiskTestResult(DiskTest Test, double BytesPerSecond, double Iops, double AverageLatencyMs)
{
    public double MegabytesPerSecond => BytesPerSecond / 1_000_000;
}

public sealed record DiskBenchmarkResult(IReadOnlyList<DiskTestResult> Tests, long FileSize, TimeSpan Elapsed)
{
    public DiskTestResult? Get(DiskTest test) => Tests.FirstOrDefault(t => t.Test.Id == test.Id);
}

/// <summary>What is happening: the test running (null while the file is prepared), overall progress and the live speed.</summary>
public readonly record struct DiskBenchmarkProgress(DiskTest? Test, double Fraction, double BytesPerSecond);

/// <summary>A problem the user can act on (not enough space, no permission…).</summary>
public sealed class DiskBenchmarkException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>A drive that can be tested and the folder the test file goes in.</summary>
public sealed record BenchmarkDrive(string Name, string Label, string Folder, long FreeBytes, long TotalBytes);

/// <summary>
/// Measures how fast a drive reads and writes, like CrystalDiskMark: sequential 1 MiB blocks with 8 requests in flight,
/// and random 4 KiB blocks with 32 and with 1 in flight. Works on a temporary file (deleted afterwards, also if the app
/// is closed mid-run) that bypasses the file cache on Windows, so the numbers are the drive's, not memory's.
/// </summary>
public static class DiskBenchmark
{
    public const string FolderName = "Coreloom benchmark";

    /// <summary>FILE_FLAG_NO_BUFFERING: transfers go straight to the drive. Needs sector-aligned buffers and offsets.</summary>
    private const FileOptions NoBuffering = (FileOptions)0x20000000;

    private const int Alignment = 4096;

    /// <summary>Test file sizes offered, smallest first.</summary>
    public static IReadOnlyList<long> Sizes { get; } = [256L << 20, 1L << 30, 4L << 30];

    public static long DefaultSize => 1L << 30;

    /// <summary>Fixed and removable drives that are ready, with the folder to test in (the temp folder on the system drive).</summary>
    public static IReadOnlyList<BenchmarkDrive> Drives()
    {
        var drives = new List<BenchmarkDrive>();
        string temp = Path.GetTempPath();
        string? tempRoot = Path.GetPathRoot(temp);
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady || drive.DriveType is not (DriveType.Fixed or DriveType.Removable) || drive.TotalSize <= 0)
                {
                    continue;
                }

                bool macVolume = OperatingSystem.IsMacOS() && drive.Name.StartsWith("/Volumes/", StringComparison.Ordinal)
                    && drive.DriveFormat is not ("udf" or "cd9660" or "afpfs" or "smbfs" or "nfs" or "webdav");
                if (!OperatingSystem.IsWindows() && drive.Name != tempRoot && !macVolume)
                {
                    continue; // elsewhere the temp folder's file system (and on a Mac the volumes in /Volumes) are what's useful
                }

                string root = drive.RootDirectory.FullName;
                string folder = string.Equals(root, tempRoot, StringComparison.OrdinalIgnoreCase)
                    ? Path.Combine(temp, FolderName)
                    : Path.Combine(root, FolderName);
                string name = OperatingSystem.IsWindows() ? root.TrimEnd('\\') : root;
                string label = OperatingSystem.IsMacOS()
                    ? macVolume ? Path.GetFileName(root.TrimEnd('/')) : "Macintosh HD"
                    : string.IsNullOrWhiteSpace(drive.VolumeLabel) ? string.Empty : drive.VolumeLabel;
                drives.Add(new BenchmarkDrive(name, label, folder, drive.AvailableFreeSpace, drive.TotalSize));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // a drive that went away or can't be read: not offered
            }
        }

        return drives;
    }

    public static async Task<DiskBenchmarkResult> RunAsync(DiskBenchmarkOptions options, IReadOnlyList<DiskTest>? tests = null, IProgress<DiskBenchmarkProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        tests ??= DiskTests.All;
        long fileSize = Math.Max(1L << 20, options.FileSize / (1 << 20) * (1 << 20)); // whole MiB, so every block is aligned

        bool createdFolder = !Directory.Exists(options.Directory);
        string path = Path.Combine(options.Directory, $"coreloom-{Guid.NewGuid():N}.tmp");
        var total = Stopwatch.StartNew();
        try
        {
            Directory.CreateDirectory(options.Directory);
            CheckSpace(options.Directory, fileSize + options.Headroom);

            var fileOptions = FileOptions.Asynchronous | FileOptions.DeleteOnClose;
            if (options.Unbuffered && OperatingSystem.IsWindows())
            {
                fileOptions |= NoBuffering;
            }

            using var handle = File.OpenHandle(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, fileOptions, fileSize);
            if (options.Unbuffered && OperatingSystem.IsMacOS())
            {
                MacNoCache(handle); // macOS's equivalent of unbuffered I/O: the unified buffer cache is bypassed
            }

            int maxQueue = tests.Max(t => t.QueueDepth);
            int maxBlock = tests.Max(t => t.BlockSize);
            var source = AlignedBuffer.Create(maxBlock);
            System.Random.Shared.NextBytes(source.Span); // incompressible data
            var readBuffers = Enumerable.Range(0, maxQueue).Select(_ => AlignedBuffer.Create(maxBlock)).ToArray();

            int steps = tests.Count + 1;
            await PrepareAsync(handle, fileSize, source, p => progress?.Report(new DiskBenchmarkProgress(null, p / steps, 0)), cancellationToken).ConfigureAwait(false);

            var results = new List<DiskTestResult>(tests.Count);
            for (int i = 0; i < tests.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int step = i + 1;
                var test = tests[i];
                results.Add(await RunTestAsync(handle, test, fileSize, options, source, readBuffers,
                    (fraction, speed) => progress?.Report(new DiskBenchmarkProgress(test, (step + fraction) / steps, speed)), cancellationToken).ConfigureAwait(false));
            }

            progress?.Report(new DiskBenchmarkProgress(null, 1, 0));
            return new DiskBenchmarkResult(results, fileSize, total.Elapsed);
        }
        catch (UnauthorizedAccessException e)
        {
            throw new DiskBenchmarkException(OperatingSystem.IsMacOS()
                ? "Coreloom can't write a test file on this volume. Pick another one, or allow Coreloom to use it in System Settings › Privacy & Security."
                : "Coreloom can't write a test file on this drive. Pick another drive, or run Coreloom as administrator.", e);
        }
        catch (IOException e) when ((e.HResult & 0xFFFF) is 0x70 or 0x27) // ERROR_DISK_FULL, ERROR_HANDLE_DISK_FULL
        {
            throw new DiskBenchmarkException("The drive ran out of space during the test.", e);
        }
        finally
        {
            TryDelete(path);
            if (createdFolder)
            {
                try
                {
                    if (Directory.Exists(options.Directory) && !Directory.EnumerateFileSystemEntries(options.Directory).Any())
                    {
                        Directory.Delete(options.Directory);
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // left behind empty; harmless
                }
            }
        }
    }

    private static void CheckSpace(string directory, long needed)
    {
        try
        {
            string? root = Path.GetPathRoot(Path.GetFullPath(directory));
            if (root is not null && new DriveInfo(root).AvailableFreeSpace < needed)
            {
                throw new DiskBenchmarkException($"Not enough free space: the test needs {UnitFormatterLite.Bytes(needed)} free on this drive.");
            }
        }
        catch (ArgumentException)
        {
            // not a drive DriveInfo understands (network path…): just try
        }
    }

    /// <summary>Writes the whole file once, so the tests run on allocated, written space (as CrystalDiskMark does).</summary>
    private static async Task PrepareAsync(SafeFileHandle handle, long fileSize, AlignedBuffer source, Action<double> report, CancellationToken cancellationToken)
    {
        int block = 1 << 20;
        var data = source.Memory[..Math.Min(block, source.Length)];
        block = data.Length;
        long lastReport = 0;
        var clock = Stopwatch.StartNew();
        for (long offset = 0; offset < fileSize; offset += block)
        {
            int length = (int)Math.Min(block, fileSize - offset);
            await RandomAccess.WriteAsync(handle, data[..length], offset, cancellationToken).ConfigureAwait(false);
            if (clock.ElapsedMilliseconds - lastReport >= 200)
            {
                lastReport = clock.ElapsedMilliseconds;
                report((double)offset / fileSize);
            }
        }

        RandomAccess.FlushToDisk(handle);
        report(1);
    }

    private static async Task<DiskTestResult> RunTestAsync(SafeFileHandle handle, DiskTest test, long fileSize, DiskBenchmarkOptions options,
        AlignedBuffer source, AlignedBuffer[] readBuffers, Action<double, double> report, CancellationToken cancellationToken)
    {
        long blocks = Math.Max(1, fileSize / test.BlockSize);
        long maxOperations = test.Random ? long.MaxValue : blocks * Math.Max(1, options.MaxPasses);
        long next = -1;
        long bytes = 0;
        long operations = 0;
        long latencyTicks = 0;
        var duration = options.TestDuration;
        var clock = Stopwatch.StartNew();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        async Task Worker(int index)
        {
            var random = new Random(HashCode.Combine(test.Id, index, Environment.TickCount));
            var buffer = test.Write ? source.Memory[..test.BlockSize] : readBuffers[index].Memory[..test.BlockSize];
            while (!stop.IsCancellationRequested && clock.Elapsed < duration)
            {
                long n = Interlocked.Increment(ref next);
                if (n >= maxOperations)
                {
                    break;
                }

                long offset = (test.Random ? random.NextInt64(blocks) : n % blocks) * test.BlockSize;
                long started = Stopwatch.GetTimestamp();
                if (test.Write)
                {
                    await RandomAccess.WriteAsync(handle, buffer, offset, stop.Token).ConfigureAwait(false);
                }
                else
                {
                    await RandomAccess.ReadAsync(handle, buffer, offset, stop.Token).ConfigureAwait(false);
                }

                Interlocked.Add(ref latencyTicks, Stopwatch.GetTimestamp() - started);
                Interlocked.Add(ref bytes, test.BlockSize);
                Interlocked.Increment(ref operations);
            }
        }

        var workers = Task.WhenAll(Enumerable.Range(0, test.QueueDepth).Select(i => Task.Run(() => Worker(i), CancellationToken.None)));
        long lastBytes = 0;
        var lastTime = TimeSpan.Zero;
        while (!workers.IsCompleted)
        {
            await Task.WhenAny(workers, Task.Delay(200, CancellationToken.None)).ConfigureAwait(false);
            var now = clock.Elapsed;
            long done = Interlocked.Read(ref bytes);
            double seconds = (now - lastTime).TotalSeconds;
            double speed = seconds > 0 ? (done - lastBytes) / seconds : 0;
            lastBytes = done;
            lastTime = now;
            double fraction = test.Random
                ? now.TotalSeconds / duration.TotalSeconds
                : Math.Max(now.TotalSeconds / duration.TotalSeconds, (double)Interlocked.Read(ref operations) / maxOperations);
            report(Math.Clamp(fraction, 0, 1), speed);
        }

        await workers.ConfigureAwait(false); // rethrows the first failure or the cancellation
        if (test.Write && !(options.Unbuffered && (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())))
        {
            RandomAccess.FlushToDisk(handle); // cached writes only count once they're on the drive
        }

        double elapsed = Math.Max(clock.Elapsed.TotalSeconds, 1e-6);
        long ops = Interlocked.Read(ref operations);
        double latencyMs = ops > 0 ? Interlocked.Read(ref latencyTicks) * 1000.0 / Stopwatch.Frequency / ops : 0;
        return new DiskTestResult(test, bytes / elapsed, ops / elapsed, latencyMs);
    }

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "fcntl", SetLastError = true)]
    private static extern int Fcntl(int fd, int command, int argument);

    /// <summary>fcntl(F_NOCACHE): reads and writes on this file skip macOS's file cache, so the drive itself is measured.</summary>
    private static void MacNoCache(SafeFileHandle handle)
    {
        const int FNoCache = 48;
        try
        {
            _ = Fcntl((int)handle.DangerousGetHandle(), FNoCache, 1);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            // cached figures are better than none
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // DeleteOnClose removes it when the last handle closes
        }
    }

    /// <summary>A buffer at a 4 KiB boundary (unbuffered I/O needs sector alignment), on the pinned heap so it never moves.</summary>
    private sealed class AlignedBuffer
    {
        private readonly byte[] _array;
        private readonly int _offset;

        private AlignedBuffer(byte[] array, int offset, int length)
        {
            _array = array;
            _offset = offset;
            Length = length;
        }

        public int Length { get; }

        public Memory<byte> Memory => _array.AsMemory(_offset, Length);

        public Span<byte> Span => _array.AsSpan(_offset, Length);

        public static AlignedBuffer Create(int length)
        {
            var array = GC.AllocateUninitializedArray<byte>(length + Alignment, pinned: true);
            long address = Marshal.UnsafeAddrOfPinnedArrayElement(array, 0).ToInt64();
            int offset = (int)((Alignment - (address % Alignment)) % Alignment);
            return new AlignedBuffer(array, offset, length);
        }
    }
}

/// <summary>Rates a speed on the four-color scale: excellent (green), good (blue), slow (orange), very slow (red).</summary>
public static class SpeedRating
{
    /// <summary>Higher is better: at or above <paramref name="excellent"/> is cool/green, below <paramref name="fair"/> critical/red.</summary>
    public static ThresholdLevel HigherIsBetter(double value, double excellent, double good, double fair) =>
        !double.IsFinite(value) || value <= 0 ? ThresholdLevel.Normal
        : value >= excellent ? ThresholdLevel.Cool
        : value >= good ? ThresholdLevel.Normal
        : value >= fair ? ThresholdLevel.Elevated
        : ThresholdLevel.Critical;

    /// <summary>Lower is better (latency): at or below <paramref name="excellent"/> is green, above <paramref name="fair"/> red.</summary>
    public static ThresholdLevel LowerIsBetter(double value, double excellent, double good, double fair) =>
        !double.IsFinite(value) || value < 0 ? ThresholdLevel.Normal
        : value <= excellent ? ThresholdLevel.Cool
        : value <= good ? ThresholdLevel.Normal
        : value <= fair ? ThresholdLevel.Elevated
        : ThresholdLevel.Critical;

    /// <summary>Position on the gauge scale (0 green … 3 red) for a level.</summary>
    public static double Tone(ThresholdLevel level) => level switch
    {
        ThresholdLevel.Cool => GaugeTone.Green,
        ThresholdLevel.Elevated => GaugeTone.Orange,
        ThresholdLevel.Critical => GaugeTone.Red,
        _ => GaugeTone.Blue,
    };

    /// <summary>A drive test's result in MB/s: NVMe-class is excellent, SATA-SSD-class good, hard-drive-class slow.</summary>
    public static ThresholdLevel Disk(DiskTest test, double megabytesPerSecond) => test switch
    {
        { Random: false } => HigherIsBetter(megabytesPerSecond, 2500, 400, 100),
        { QueueDepth: >= 8 } => HigherIsBetter(megabytesPerSecond, 600, 150, 5),
        _ => HigherIsBetter(megabytesPerSecond, 45, 20, 2),
    };

    /// <summary>The top of a drive test's gauge in MB/s (a fast PCIe 5 drive).</summary>
    public static double DiskCeiling(DiskTest test) => test.Random ? (test.QueueDepth >= 8 ? 4000 : 120) : 14000;

    /// <summary>
    /// Gauge fraction for a speed against a ceiling, on a square-root scale so slow and fast drives (or connections)
    /// both show a readable arc.
    /// </summary>
    public static double GaugeFraction(double value, double ceiling) =>
        !double.IsFinite(value) || value <= 0 || ceiling <= 0 ? 0 : Math.Clamp(Math.Sqrt(value / ceiling), 0, 1);
}

/// <summary>Byte counts in binary units without a formatter instance (for messages).</summary>
internal static class UnitFormatterLite
{
    public static string Bytes(long bytes) => bytes >= 1L << 30
        ? $"{bytes / (double)(1L << 30):0.#} GB"
        : $"{bytes / (double)(1L << 20):0} MB";
}
