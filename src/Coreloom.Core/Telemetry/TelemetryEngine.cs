using System.Diagnostics;
using Coreloom.Core.Diagnostics;
using Coreloom.Core.History;

namespace Coreloom.Core.Telemetry;

/// <summary>
/// Owns the telemetry providers and the polling loop. Polling runs on a dedicated background thread,
/// completely decoupled from UI rendering. Each cycle produces an immutable <see cref="TelemetryFrame"/>,
/// appends tracked metrics to the <see cref="HistoryStore"/> and raises <see cref="FrameProduced"/>.
/// Provider failures are isolated: a provider that throws repeatedly is shut down and re-initialized
/// with exponential back-off (handles GPU driver updates, device resets and transient API errors).
/// </summary>
public sealed class TelemetryEngine : IDisposable
{
    public static readonly TimeSpan MinimumInterval = TimeSpan.FromMilliseconds(250);
    public static readonly TimeSpan MaximumInterval = TimeSpan.FromSeconds(10);

    private const int FailuresBeforeFault = 3;
    private const string LogCategory = "Telemetry";

    private readonly ProviderSlot[] _slots;
    private readonly IMonotonicClock _clock;
    private readonly int[] _sectionEnabled;
    private readonly AutoResetEvent _wake = new(false);
    private readonly object _lifecycleGate = new();

    private Thread? _thread;
    private volatile bool _stopRequested;
    private volatile bool _paused;
    private volatile bool _reinitializeRequested;
    private long _intervalMs = 1000;
    private long _temporaryIntervalMs;
    private volatile bool _lowPriority;
    private long _sequence;
    private TelemetryFrame _latest = TelemetryFrame.Empty;
    private IReadOnlyList<ProviderStatus> _statuses = [];
    private string _statusSignature = string.Empty;
    private bool _disposed;

    public TelemetryEngine(
        IEnumerable<ITelemetryProvider> providers,
        MetricCatalog catalog,
        DeviceRegistry devices,
        HistoryStore history,
        SystemCapabilities capabilities,
        IMonotonicClock? clock = null)
    {
        ArgumentNullException.ThrowIfNull(providers);
        Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        Devices = devices ?? throw new ArgumentNullException(nameof(devices));
        History = history ?? throw new ArgumentNullException(nameof(history));
        Capabilities = capabilities ?? SystemCapabilities.Unknown;
        _clock = clock ?? SystemMonotonicClock.Instance;
        _slots = providers.Select(p => new ProviderSlot(p)).ToArray();
        _sectionEnabled = Enumerable.Repeat(1, Enum.GetValues<MonitoringSection>().Length).ToArray();
        PublishStatuses(force: true);
    }

    /// <summary>Raised on the telemetry thread after every polling cycle.</summary>
    public event Action<TelemetryFrame>? FrameProduced;

    /// <summary>Raised on the telemetry thread when a provider's state or message changes.</summary>
    public event Action? ProviderStatusesChanged;

    public MetricCatalog Catalog { get; }

    public DeviceRegistry Devices { get; }

    public HistoryStore History { get; }

    public SystemCapabilities Capabilities { get; }

    public IMonotonicClock Clock => _clock;

    public bool IsRunning => _thread is { IsAlive: true };

    public bool IsPaused => _paused;

    public TelemetryFrame LatestFrame => Volatile.Read(ref _latest);

    public IReadOnlyList<ProviderStatus> ProviderStatuses => Volatile.Read(ref _statuses);

    public IReadOnlyList<ITelemetryProvider> Providers => _slots.Select(s => s.Provider).ToArray();

    /// <summary>Polling interval. Clamped to [250 ms, 10 s]. Changing it resizes history buffers.</summary>
    public TimeSpan Interval
    {
        get => TimeSpan.FromMilliseconds(Interlocked.Read(ref _intervalMs));
        set
        {
            var clamped = value < MinimumInterval ? MinimumInterval : value > MaximumInterval ? MaximumInterval : value;
            long ms = (long)clamped.TotalMilliseconds;
            if (Interlocked.Exchange(ref _intervalMs, ms) != ms)
            {
                History.SetCapacity(HistoryStore.CapacityFor(clamped));
                _wake.Set();
            }
        }
    }

    /// <summary>
    /// A slower interval for a while (lighter monitoring while a game is in front), without resizing the history: the
    /// slower of this and <see cref="Interval"/> is used. Null goes back to <see cref="Interval"/>.
    /// </summary>
    public TimeSpan? TemporaryInterval
    {
        get => Interlocked.Read(ref _temporaryIntervalMs) is > 0 and long ms ? TimeSpan.FromMilliseconds(ms) : null;
        set
        {
            long ms = value is { } v ? (long)Math.Clamp(v.TotalMilliseconds, MinimumInterval.TotalMilliseconds, MaximumInterval.TotalMilliseconds) : 0;
            if (Interlocked.Exchange(ref _temporaryIntervalMs, ms) != ms)
            {
                _wake.Set();
            }
        }
    }

    /// <summary>The interval polling uses now.</summary>
    public TimeSpan EffectiveInterval => TimeSpan.FromMilliseconds(Math.Max(Interlocked.Read(ref _intervalMs), Interlocked.Read(ref _temporaryIntervalMs)));

    /// <summary>Polls at below-normal thread priority (while a game is in front), else normal.</summary>
    public bool LowPriority
    {
        get => _lowPriority;
        set
        {
            if (_lowPriority != value)
            {
                _lowPriority = value;
                _wake.Set();
            }
        }
    }

    public void Start()
    {
        lock (_lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_thread is not null)
            {
                return;
            }

            _stopRequested = false;
            _thread = new Thread(Run)
            {
                IsBackground = true,
                Name = "Coreloom telemetry",
                Priority = ThreadPriority.Normal, // (above normal up to 1.4.5; normal so it never gets ahead of a game)
            };
            _thread.Start();
            Log.Info(LogCategory, $"Telemetry started ({_slots.Length} providers, interval {Interval.TotalMilliseconds:0} ms).");
        }
    }

    public void Stop(TimeSpan? timeout = null)
    {
        Thread? thread;
        lock (_lifecycleGate)
        {
            thread = _thread;
            _thread = null;
        }

        if (thread is null)
        {
            return;
        }

        _stopRequested = true;
        _wake.Set();
        if (!thread.Join(timeout ?? TimeSpan.FromSeconds(5)))
        {
            Log.Warn(LogCategory, "Telemetry thread did not stop in time.");
        }

        ShutdownProviders();
        Log.Info(LogCategory, "Telemetry stopped.");
    }

    /// <summary>Stops collecting readings (history and alerts pause too). Providers stay initialized.</summary>
    public void Pause()
    {
        _paused = true;
        _wake.Set();
    }

    public void Resume()
    {
        _paused = false;
        _wake.Set();
    }

    /// <summary>Shuts down and re-initializes all providers (e.g. after resume from sleep or a driver update).</summary>
    public void RequestReinitialize()
    {
        _reinitializeRequested = true;
        _wake.Set();
    }

    public void SetSectionEnabled(MonitoringSection section, bool enabled)
    {
        Volatile.Write(ref _sectionEnabled[(int)section], enabled ? 1 : 0);
        _wake.Set();
    }

    public bool IsSectionEnabled(MonitoringSection section) =>
        section == MonitoringSection.System || Volatile.Read(ref _sectionEnabled[(int)section]) == 1;

    public static MonitoringSection SectionOf(MetricCategory category) => category switch
    {
        MetricCategory.Cpu => MonitoringSection.Cpu,
        MetricCategory.Motherboard => MonitoringSection.Cpu,
        MetricCategory.Gpu => MonitoringSection.Gpu,
        MetricCategory.Memory => MonitoringSection.Memory,
        MetricCategory.Storage => MonitoringSection.Storage,
        MetricCategory.Network => MonitoringSection.Network,
        _ => MonitoringSection.System,
    };

    // ---------------------------------------------------------------------------------------
    // Synchronous API used by the diagnostics tool and tests (not to be mixed with Start()).
    // ---------------------------------------------------------------------------------------

    /// <summary>Initializes every provider on the calling thread, stage by stage.</summary>
    public void InitializeAll()
    {
        if (IsRunning)
        {
            throw new InvalidOperationException("InitializeAll cannot be used while the polling thread is running.");
        }

        foreach (var stage in _slots.Select(s => s.Provider.InitStage).Distinct().Order())
        {
            foreach (var slot in _slots.Where(s => s.Provider.InitStage == stage))
            {
                if (!IsSectionEnabled(slot.Provider.Section))
                {
                    slot.State = ProviderState.Disabled;
                    continue;
                }

                ApplyInitOutcome(slot, InitializeSlot(slot));
            }
        }

        PublishStatuses(force: true);
    }

    /// <summary>Runs one polling cycle on the calling thread and returns the frame.</summary>
    public TelemetryFrame PollOnce()
    {
        if (IsRunning)
        {
            throw new InvalidOperationException("PollOnce cannot be used while the polling thread is running.");
        }

        AdvanceInitialization(runAsync: false);
        return PollCore();
    }

    // ---------------------------------------------------------------------------------------

    private void Run()
    {
        long next = _clock.NowMilliseconds;
        bool lowered = false;
        while (!_stopRequested)
        {
            if (_lowPriority != lowered)
            {
                lowered = _lowPriority;
                try
                {
                    Thread.CurrentThread.Priority = lowered ? ThreadPriority.BelowNormal : ThreadPriority.Normal;
                }
                catch (ThreadStateException)
                {
                }
            }

            try
            {
                AdvanceInitialization(runAsync: true);
            }
            catch (Exception ex)
            {
                Log.Error(LogCategory, "Provider initialization scheduling failed.", ex);
            }

            if (_paused)
            {
                PublishStatuses(force: false);
                _wake.WaitOne(500);
                next = _clock.NowMilliseconds;
                continue;
            }

            try
            {
                PollCore();
            }
            catch (Exception ex)
            {
                Log.Error(LogCategory, "Polling cycle failed.", ex);
            }

            long interval = Math.Max(Interlocked.Read(ref _intervalMs), Interlocked.Read(ref _temporaryIntervalMs));
            long now = _clock.NowMilliseconds;
            next += interval;
            if (next <= now)
            {
                // We fell behind (system sleep, debugger, very slow provider): re-anchor instead of bursting.
                next = now + Math.Max(interval / 4, 50);
            }

            if (_wake.WaitOne((int)Math.Min(int.MaxValue, next - now)))
            {
                // Woken early (interval change, resume, re-init request): re-anchor the schedule.
                next = _clock.NowMilliseconds;
            }
        }
    }

    private TelemetryFrame PollCore()
    {
        var cycle = Stopwatch.StartNew();
        long now = _clock.NowMilliseconds;
        var builder = new FrameBuilder(category => IsSectionEnabled(SectionOf(category)));

        foreach (var slot in _slots)
        {
            if (slot.State is not (ProviderState.Running or ProviderState.Degraded))
            {
                continue;
            }

            if (!IsSectionEnabled(slot.Provider.Section))
            {
                continue;
            }

            double minIntervalMs = slot.Provider.MinimumPollInterval.TotalMilliseconds;
            bool due = slot.LastPollAt is null || now - slot.LastPollAt.Value >= minIntervalMs - 25;
            if (!due)
            {
                foreach (var (handle, value) in slot.LastValues)
                {
                    builder.Write(handle, value);
                }

                continue;
            }

            slot.Writer.Begin(builder);
            var sw = Stopwatch.StartNew();
            try
            {
                slot.Provider.Poll(slot.Writer);
                slot.ConsecutiveFailures = 0;
                slot.LastSuccess = _clock.LocalNow;
                slot.LastError = null;
            }
            catch (Exception ex)
            {
                slot.ConsecutiveFailures++;
                slot.TotalFailures++;
                slot.LastError = ex.Message;
                Log.WarnThrottled($"poll:{slot.Provider.Name}", TimeSpan.FromMinutes(1), LogCategory, $"{slot.Provider.Name} poll failed ({slot.ConsecutiveFailures} in a row): {ex.Message}", ex);
                if (slot.ConsecutiveFailures >= FailuresBeforeFault)
                {
                    Fault(slot, ex, now);
                }
            }
            finally
            {
                sw.Stop();
                slot.LastPollAt = now;
                slot.LastPollMs = sw.Elapsed.TotalMilliseconds;
                slot.AveragePollMs = slot.AveragePollMs <= 0 ? slot.LastPollMs : (slot.AveragePollMs * 0.9) + (slot.LastPollMs * 0.1);
                slot.MetricsWritten = slot.Writer.Count;
            }
        }

        long timestamp = _clock.NowMilliseconds;
        cycle.Stop();
        var frame = builder.Build(Interlocked.Increment(ref _sequence), timestamp, _clock.LocalNow, cycle.Elapsed);

        foreach (var (id, sample) in frame.Samples)
        {
            if (sample.Source.Descriptor.TrackHistory)
            {
                History.Append(id, timestamp, sample.Value);
            }
        }

        Volatile.Write(ref _latest, frame);
        PublishStatuses(force: false);

        var handlers = FrameProduced;
        if (handlers is not null)
        {
            foreach (Action<TelemetryFrame> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(frame);
                }
                catch (Exception ex)
                {
                    Log.WarnThrottled("frame-handler", TimeSpan.FromMinutes(1), LogCategory, "A frame subscriber threw an exception.", ex);
                }
            }
        }

        return frame;
    }

    private void AdvanceInitialization(bool runAsync)
    {
        long now = _clock.NowMilliseconds;

        foreach (var slot in _slots)
        {
            if (slot.InitTask is { IsCompleted: true } task)
            {
                slot.InitTask = null;
                ApplyInitOutcome(slot, task.Result);
            }
        }

        if (_reinitializeRequested)
        {
            _reinitializeRequested = false;
            Log.Info(LogCategory, "Re-initializing all providers.");
            foreach (var slot in _slots.Where(s => s.InitTask is null))
            {
                SafeShutdown(slot);
                slot.State = ProviderState.NotInitialized;
                slot.NextInitAttemptAt = 0;
                slot.LastValues.Clear();
                slot.LastPollAt = null;
            }
        }

        int allowedStage = _slots.Where(s => !s.FirstAttemptDone).Select(s => s.Provider.InitStage).DefaultIfEmpty(int.MaxValue).Min();

        foreach (var slot in _slots)
        {
            if (slot.InitTask is not null)
            {
                continue;
            }

            bool sectionEnabled = IsSectionEnabled(slot.Provider.Section);
            if (!sectionEnabled)
            {
                if (slot.State == ProviderState.NotInitialized)
                {
                    slot.State = ProviderState.Disabled;
                    slot.FirstAttemptDone = true;
                }

                continue;
            }

            if (slot.State == ProviderState.Disabled)
            {
                // The section was re-enabled before this provider was ever initialized.
                slot.State = ProviderState.NotInitialized;
            }

            bool wantsInit = slot.State == ProviderState.NotInitialized
                || (slot.State == ProviderState.Faulted && now >= slot.NextInitAttemptAt);
            if (!wantsInit)
            {
                continue;
            }

            if (!slot.FirstAttemptDone && slot.Provider.InitStage > allowedStage)
            {
                continue;
            }

            slot.State = ProviderState.Initializing;
            if (runAsync)
            {
                slot.InitTask = Task.Run(() => InitializeSlot(slot));
            }
            else
            {
                ApplyInitOutcome(slot, InitializeSlot(slot));
            }
        }
    }

    private InitOutcome InitializeSlot(ProviderSlot slot)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var result = slot.Provider.Initialize(new ProviderContext(this, slot));
            return new InitOutcome(result ?? ProviderInitResult.Running(), null, sw.Elapsed);
        }
        catch (Exception ex)
        {
            try
            {
                slot.Provider.Shutdown();
            }
            catch
            {
                // ignore secondary failures
            }

            return new InitOutcome(null, ex, sw.Elapsed);
        }
    }

    private void ApplyInitOutcome(ProviderSlot slot, InitOutcome outcome)
    {
        slot.FirstAttemptDone = true;
        slot.InitMs = outcome.Duration.TotalMilliseconds;
        slot.LastPollAt = null;
        slot.LastValues.Clear();

        if (outcome.Error is not null)
        {
            Fault(slot, outcome.Error, _clock.NowMilliseconds);
            Log.Warn(LogCategory, $"{slot.Provider.Name} failed to initialize: {outcome.Error.Message}", outcome.Error);
            return;
        }

        var result = outcome.Result!;
        slot.State = result.State;
        slot.LastInitState = result.State;
        slot.Message = result.Message;
        slot.WasInitialized = result.State is ProviderState.Running or ProviderState.Degraded;
        slot.ConsecutiveFailures = 0;
        if (slot.WasInitialized)
        {
            slot.FaultCount = 0;
        }

        Log.Info(LogCategory, $"{slot.Provider.Name}: {result.State} in {outcome.Duration.TotalMilliseconds:0} ms{(result.Message is null ? string.Empty : " — " + result.Message)}");
    }

    private void Fault(ProviderSlot slot, Exception error, long now)
    {
        SafeShutdown(slot);
        slot.State = ProviderState.Faulted;
        slot.LastError = error.Message;
        slot.FaultCount++;
        slot.WasInitialized = false;
        slot.LastValues.Clear();
        slot.LastPollAt = null;
        long backoff = BackoffMilliseconds(slot.FaultCount);
        slot.NextInitAttemptAt = now + backoff;
        slot.Message = $"Retrying in {backoff / 1000} s";
    }

    internal static long BackoffMilliseconds(int faultCount) => faultCount switch
    {
        <= 1 => 5_000,
        2 => 10_000,
        3 => 30_000,
        4 => 60_000,
        _ => 120_000,
    };

    private static void SafeShutdown(ProviderSlot slot)
    {
        try
        {
            slot.Provider.Shutdown();
        }
        catch (Exception ex)
        {
            Log.Warn(LogCategory, $"{slot.Provider.Name} shutdown failed.", ex);
        }
    }

    private void ShutdownProviders()
    {
        foreach (var slot in _slots)
        {
            try
            {
                slot.InitTask?.Wait(TimeSpan.FromSeconds(3));
            }
            catch
            {
                // ignore
            }

            SafeShutdown(slot);
            slot.State = ProviderState.NotInitialized;
            slot.FirstAttemptDone = false;
        }
    }

    private void PublishStatuses(bool force)
    {
        var list = new ProviderStatus[_slots.Length];
        var signature = new System.Text.StringBuilder();
        for (int i = 0; i < _slots.Length; i++)
        {
            var s = _slots[i];
            var state = s.State;
            if (!IsSectionEnabled(s.Provider.Section) && state is ProviderState.Running or ProviderState.Degraded)
            {
                state = ProviderState.Disabled;
            }

            list[i] = new ProviderStatus(
                s.Provider.Name,
                s.Provider.Section,
                state,
                s.Message,
                s.LastError,
                s.ConsecutiveFailures,
                s.TotalFailures,
                s.LastPollMs,
                s.AveragePollMs,
                s.InitMs,
                s.LastSuccess,
                s.MetricsWritten);
            signature.Append(state).Append('|').Append(s.Message).Append('|').Append(s.LastError).Append(';');
        }

        Volatile.Write(ref _statuses, list);
        string sig = signature.ToString();
        if (force || sig != _statusSignature)
        {
            _statusSignature = sig;
            try
            {
                ProviderStatusesChanged?.Invoke();
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory, "A status subscriber threw an exception.", ex);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        foreach (var slot in _slots)
        {
            try
            {
                slot.Provider.Dispose();
            }
            catch
            {
                // ignore
            }
        }

        _wake.Dispose();
        _disposed = true;
    }

    // ---------------------------------------------------------------------------------------

    private sealed record InitOutcome(ProviderInitResult? Result, Exception? Error, TimeSpan Duration);

    private sealed class ProviderSlot
    {
        public ProviderSlot(ITelemetryProvider provider)
        {
            Provider = provider;
            Writer = new RecordingWriter(LastValues);
        }

        public ITelemetryProvider Provider { get; }

        public RecordingWriter Writer { get; }

        public Dictionary<MetricHandle, double> LastValues { get; } = new(ReferenceEqualityComparer.Instance);

        public ProviderState State { get; set; } = ProviderState.NotInitialized;

        public ProviderState? LastInitState { get; set; }

        public bool WasInitialized { get; set; }

        public bool FirstAttemptDone { get; set; }

        public Task<InitOutcome>? InitTask { get; set; }

        public long NextInitAttemptAt { get; set; }

        public int FaultCount { get; set; }

        public int ConsecutiveFailures { get; set; }

        public int TotalFailures { get; set; }

        public string? Message { get; set; }

        public string? LastError { get; set; }

        public long? LastPollAt { get; set; }

        public double LastPollMs { get; set; }

        public double AveragePollMs { get; set; }

        public double InitMs { get; set; }

        public DateTime? LastSuccess { get; set; }

        public int MetricsWritten { get; set; }
    }

    /// <summary>Forwards writes to the frame and remembers them for carry-forward.</summary>
    private sealed class RecordingWriter : IMetricWriter
    {
        private readonly Dictionary<MetricHandle, double> _record;
        private IMetricWriter? _target;

        public RecordingWriter(Dictionary<MetricHandle, double> record)
        {
            _record = record;
        }

        public int Count => _record.Count;

        public void Begin(IMetricWriter target)
        {
            _target = target;
            _record.Clear();
        }

        public void Write(MetricHandle handle, double value)
        {
            if (!double.IsFinite(value))
            {
                return;
            }

            _record[handle] = value;
            _target?.Write(handle, value);
        }
    }

    private sealed class ProviderContext : IProviderContext
    {
        private readonly TelemetryEngine _engine;
        private readonly ProviderSlot _slot;

        public ProviderContext(TelemetryEngine engine, ProviderSlot slot)
        {
            _engine = engine;
            _slot = slot;
        }

        public SystemCapabilities Capabilities => _engine.Capabilities;

        public MetricHandle Register(MetricDescriptor descriptor, Provenance provenance = Provenance.Measured, int priority = 0, string? note = null) =>
            _engine.Catalog.Register(descriptor, _slot.Provider.Name, provenance, priority, note);

        public void SetUnavailableReason(string metricId, string? reason) => _engine.Catalog.SetUnavailableReason(metricId, reason);

        public void UpsertDevice(DeviceInfo device) => _engine.Devices.Upsert(device);

        public void RemoveDevice(string deviceId) => _engine.Devices.Remove(deviceId);

        public bool IsSectionEnabled(MonitoringSection section) => _engine.IsSectionEnabled(section);
    }
}
