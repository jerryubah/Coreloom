namespace Coreloom.Core.Overlay;

/// <summary>Frame rate of one process: average FPS over the last second, average frame time and the 1% low.</summary>
public readonly record struct FrameRateStats(double Fps, double FrameTimeMilliseconds, double LowFps, int FramesInLastSecond);

/// <summary>
/// Turns present (frame shown) timestamps into frame rates, per process. Fed from the frame-rate event stream on
/// its own thread and read from the UI; thread-safe. Timestamps are in any monotonic unit given by
/// <c>ticksPerSecond</c> (the overlay uses the performance counter).
/// </summary>
public sealed class FrameRateTracker
{
    /// <summary>Frames kept per process: enough for the 1% low over about ten seconds at high frame rates.</summary>
    public const int Capacity = 4096;

    private static readonly TimeSpan LowWindow = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ForgetAfter = TimeSpan.FromSeconds(30);

    private readonly object _gate = new();
    private readonly Dictionary<int, Frames> _processes = [];
    private readonly long _ticksPerSecond;
    private double[] _scratch = new double[Capacity];

    public FrameRateTracker(long ticksPerSecond)
    {
        _ticksPerSecond = Math.Max(1, ticksPerSecond);
    }

    /// <summary>Processes with frames on record (for diagnostics).</summary>
    public int ProcessCount
    {
        get
        {
            lock (_gate)
            {
                return _processes.Count;
            }
        }
    }

    /// <summary>Records that a process showed a frame.</summary>
    public void OnPresent(int processId, long timestamp)
    {
        lock (_gate)
        {
            if (!_processes.TryGetValue(processId, out var frames))
            {
                frames = new Frames();
                _processes[processId] = frames;
            }

            frames.Add(timestamp);
        }
    }

    /// <summary>
    /// The process's frame rate, or null when it hasn't shown a frame for a moment before <paramref name="now"/> (it is
    /// not drawing, is minimized, or isn't a game).
    /// </summary>
    public FrameRateStats? Get(int processId, long now)
    {
        lock (_gate)
        {
            Forget(now);
            if (!_processes.TryGetValue(processId, out var frames) || frames.Count == 0)
            {
                return null;
            }

            long last = frames.Latest;
            if (now - last > Ticks(StaleAfter))
            {
                return null;
            }

            // Average over the second up to the latest frame (events arrive in small batches, so measuring up to "now"
            // would miss the newest frames).
            long from = last - _ticksPerSecond;
            int inWindow = 0;
            long first = last;
            for (int i = frames.Count - 1; i >= 0; i--)
            {
                long t = frames[i];
                if (t < from)
                {
                    break;
                }

                first = t;
                inWindow++;
            }

            double fps = inWindow >= 2 && last > first
                ? (inWindow - 1) * (double)_ticksPerSecond / (last - first)
                : inWindow;
            double frameTime = fps > 0 ? 1000.0 / fps : 0;

            // 1% low: the average frame rate of the slowest 1% of frames over the last ten seconds.
            long lowFrom = last - Ticks(LowWindow);
            int intervals = 0;
            for (int i = frames.Count - 1; i >= 1; i--)
            {
                long t = frames[i];
                if (t < lowFrom)
                {
                    break;
                }

                long gap = t - frames[i - 1];
                if (gap > 0)
                {
                    _scratch[intervals++] = gap;
                }
            }

            double low = fps;
            if (intervals > 0)
            {
                Array.Sort(_scratch, 0, intervals);
                int slowest = Math.Max(1, (int)Math.Ceiling(intervals * 0.01));
                double sum = 0;
                for (int i = intervals - slowest; i < intervals; i++)
                {
                    sum += _scratch[i];
                }

                double average = sum / slowest;
                low = Math.Min(fps, _ticksPerSecond / average);
            }

            return new FrameRateStats(fps, frameTime, low, inWindow);
        }
    }

    /// <summary>
    /// The times between the process's latest frames, in milliseconds, oldest first (for the overlay's frame-time graph):
    /// fills as much of <paramref name="buffer"/> as there are frames and returns how many. None when the process
    /// hasn't shown a frame for a moment before <paramref name="now"/>.
    /// </summary>
    public int RecentFrameTimes(int processId, long now, Span<double> buffer)
    {
        lock (_gate)
        {
            if (buffer.Length == 0 || !_processes.TryGetValue(processId, out var frames) || frames.Count < 2 || now - frames.Latest > Ticks(StaleAfter))
            {
                return 0;
            }

            int count = Math.Min(buffer.Length, frames.Count - 1);
            int first = frames.Count - count;
            double toMilliseconds = 1000.0 / _ticksPerSecond;
            for (int i = 0; i < count; i++)
            {
                buffer[i] = (frames[first + i] - frames[first + i - 1]) * toMilliseconds;
            }

            return count;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _processes.Clear();
        }
    }

    private long Ticks(TimeSpan span) => (long)(span.TotalSeconds * _ticksPerSecond);

    private void Forget(long now)
    {
        if (_processes.Count < 8)
        {
            return;
        }

        foreach (int id in _processes.Where(p => now - p.Value.Latest > Ticks(ForgetAfter)).Select(p => p.Key).ToArray())
        {
            _processes.Remove(id);
        }
    }

    /// <summary>A ring of the latest present timestamps, oldest first.</summary>
    private sealed class Frames
    {
        private readonly long[] _items = new long[Capacity];
        private int _start;

        public int Count { get; private set; }

        public long Latest => this[Count - 1];

        public long this[int index] => _items[(_start + index) % Capacity];

        public void Add(long timestamp)
        {
            if (Count > 0 && timestamp < Latest)
            {
                Insert(timestamp); // events from different processors can arrive slightly out of order
                return;
            }

            if (Count < Capacity)
            {
                _items[(_start + Count) % Capacity] = timestamp;
                Count++;
            }
            else
            {
                _items[_start] = timestamp;
                _start = (_start + 1) % Capacity;
            }
        }

        private void Insert(long timestamp)
        {
            // Find its place from the end (it is nearly always among the last few) and shift the newer ones along.
            int index = Count;
            while (index > 0 && this[index - 1] > timestamp)
            {
                index--;
            }

            if (index == 0 && Count == Capacity)
            {
                return; // older than everything kept
            }

            long carry = timestamp;
            for (int i = index; i < Count; i++)
            {
                int physical = (_start + i) % Capacity;
                (carry, _items[physical]) = (_items[physical], carry);
            }

            if (Count < Capacity)
            {
                _items[(_start + Count) % Capacity] = carry;
                Count++;
            }
            else
            {
                // Full: the oldest drops out.
                _items[_start] = carry;
                _start = (_start + 1) % Capacity;
            }
        }
    }
}
