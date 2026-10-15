using System.Diagnostics;
using System.Runtime.InteropServices;
using Coreloom.Core.Diagnostics;
using Coreloom.Core.Overlay;
using Coreloom.ViewModels.Overlay;
using static Coreloom.App.Overlay.EtwNative;

namespace Coreloom.App.Overlay;

/// <summary>
/// Measures frame rates without touching the games: a real-time Event Tracing for Windows session listens to the
/// "present" events Windows' graphics runtime emits for every frame a program shows (DXGI for Direct3D 10, 11 and 12, and
/// Direct3D 9), the way PresentMon and the Xbox Game Bar do. Nothing is injected into games, so it is safe with
/// anti-cheat. Needs administrator rights, which Coreloom always has.
/// <para>
/// Vulkan and OpenGL games are counted when the graphics driver presents them through DXGI (NVIDIA's default on
/// recent drivers). Runs only while the overlay shows a frame-rate reading.
/// </para>
/// </summary>
internal sealed unsafe class EtwFrameRateSource : IFrameRateSource, IDisposable
{
    private const string LogCategory = "Overlay";
    private const string SessionName = "Coreloom-FrameRate";
    private const ushort DxgiPresentStart = 42;
    private const ushort DxgiPresentMultiplaneOverlayStart = 55;
    private const ushort D3D9PresentStart = 1;
    private const int PropertiesSize = 120 + 1024; // the struct plus room for the session name

    private static readonly Guid DxgiProvider = new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
    private static readonly Guid D3D9Provider = new("783ACA0A-790E-4D7F-8451-AA850511C6B9");

    private readonly FrameRateTracker _tracker = new(Stopwatch.Frequency);
    private readonly EventRecordCallback _callback;
    private ulong _session;
    private ulong _trace = InvalidProcessTraceHandle;
    private IntPtr _loggerName;
    private Thread? _thread;
    private volatile bool _running;
    private long _events;
    private readonly HashSet<string> _owners = [];

    public EtwFrameRateSource()
    {
        _callback = OnEvent; // kept in a field: native code calls it for as long as the session runs
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void EventRecordCallback(EventRecord* record);

    public bool IsAvailable => _running;

    public string? Problem { get; private set; }

    public bool IsRunning => _running;

    public FrameRateStats? Get(int processId) => _running ? _tracker.Get(processId, Stopwatch.GetTimestamp()) : null;

    public int RecentFrameTimes(int processId, Span<double> buffer) => _running ? _tracker.RecentFrameTimes(processId, Stopwatch.GetTimestamp(), buffer) : 0;

    /// <summary>
    /// Starts measuring for <paramref name="owner"/> (the overlay, the Gaming page): the session runs while anyone needs
    /// it. Returns false (see <see cref="Problem"/>) when it can't run.
    /// </summary>
    public bool Acquire(string owner)
    {
        lock (_owners)
        {
            _owners.Add(owner);
            return _running || Start();
        }
    }

    /// <summary>Stops measuring for <paramref name="owner"/>; the session stops when nobody needs it any more.</summary>
    public void Release(string owner)
    {
        lock (_owners)
        {
            if (_owners.Remove(owner) && _owners.Count == 0)
            {
                Stop();
            }
        }
    }

    /// <summary>Starts the session (if it isn't running). Returns false and sets <see cref="Problem"/> when it can't.</summary>
    public bool Start()
    {
        if (_running)
        {
            return true;
        }

        if (!LayoutIsValid)
        {
            Fail("Frame rates need 64-bit Windows.");
            return false;
        }

        try
        {
            StopSession(0, SessionName); // a session left behind by a crash would block the name
            uint status = StartSession(out _session, millisecondFlush: true);
            if (status == ErrorAlreadyExists)
            {
                StopSession(0, SessionName);
                status = StartSession(out _session, millisecondFlush: true);
            }

            if (status == ErrorInvalidParameter)
            {
                status = StartSession(out _session, millisecondFlush: false); // Windows without millisecond flushing
            }

            if (status != ErrorSuccess)
            {
                Fail(status == ErrorAccessDenied
                    ? "Frame rates need administrator rights."
                    : $"Windows couldn't start frame-rate tracing ({Describe(status)}).");
                return false;
            }

            foreach (var provider in new[] { DxgiProvider, D3D9Provider })
            {
                var id = provider;
                uint enabled = EnableTraceEx2(_session, &id, EventControlCodeEnableProvider, TraceLevelInformation, ulong.MaxValue, 0, 0, IntPtr.Zero);
                if (enabled != ErrorSuccess)
                {
                    Log.Warn(LogCategory, $"Enabling the {(provider == DxgiProvider ? "DXGI" : "Direct3D 9")} events failed: {Describe(enabled)}.");
                }
            }

            _loggerName = Marshal.StringToHGlobalUni(SessionName);
            var logfile = new EventTraceLogfile
            {
                LoggerName = _loggerName,
                ProcessTraceMode = ProcessTraceModeRealTime | ProcessTraceModeEventRecord | ProcessTraceModeRawTimestamp,
                EventRecordCallback = Marshal.GetFunctionPointerForDelegate(_callback),
            };
            _trace = OpenTrace(&logfile);
            if (_trace == InvalidProcessTraceHandle)
            {
                int error = Marshal.GetLastPInvokeError();
                StopSession(_session, null);
                Fail($"Windows couldn't open the frame-rate events ({Describe((uint)error)}).");
                return false;
            }

            _running = true;
            Problem = null;
            _tracker.Clear();
            _thread = new Thread(Pump) { IsBackground = true, Name = "Coreloom frame rates" };
            _thread.Start();
            Log.Info(LogCategory, "Frame-rate tracing started.");
            return true;
        }
        catch (Exception ex)
        {
            Fail("Frame-rate tracing isn't available on this PC.", ex);
            return false;
        }
    }

    /// <summary>Stops the session; frame rates read as unavailable until it starts again.</summary>
    public void Stop()
    {
        if (!_running && _trace == InvalidProcessTraceHandle)
        {
            return;
        }

        _running = false;
        try
        {
            if (_trace != InvalidProcessTraceHandle)
            {
                CloseTrace(_trace);
                _trace = InvalidProcessTraceHandle;
            }

            StopSession(_session, null);
            _thread?.Join(TimeSpan.FromSeconds(2));
        }
        catch (Exception ex)
        {
            Log.Warn(LogCategory, "Stopping frame-rate tracing failed.", ex);
        }
        finally
        {
            _thread = null;
            if (_loggerName != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_loggerName);
                _loggerName = IntPtr.Zero;
            }

            _tracker.Clear();
            Log.Info(LogCategory, $"Frame-rate tracing stopped ({Interlocked.Read(ref _events)} frames seen).");
        }
    }

    public void Dispose() => Stop();

    private static uint StartSession(out ulong session, bool millisecondFlush)
    {
        byte* buffer = stackalloc byte[PropertiesSize];
        new Span<byte>(buffer, PropertiesSize).Clear();
        var properties = (EventTraceProperties*)buffer;
        properties->Wnode.BufferSize = PropertiesSize;
        properties->Wnode.Flags = WnodeFlagTracedGuid;
        properties->Wnode.ClientContext = 1; // performance-counter timestamps
        properties->Wnode.Guid = Guid.NewGuid();
        properties->BufferSize = 64; // KB
        properties->MinimumBuffers = 4;
        properties->MaximumBuffers = 32;
        // One set of buffers keeps events in order; flushing every 100 ms gets frames to the counter promptly even when
        // few are drawn.
        properties->LogFileMode = EventTraceRealTimeMode | EventTraceNoPerProcessorBuffering | (millisecondFlush ? EventTraceUseMsFlushTimer : 0);
        properties->FlushTimer = millisecondFlush ? 100u : 1u;
        properties->LoggerNameOffset = (uint)sizeof(EventTraceProperties);
        return EtwNative.StartTrace(out session, SessionName, properties);
    }

    private static void StopSession(ulong session, string? name)
    {
        byte* buffer = stackalloc byte[PropertiesSize];
        new Span<byte>(buffer, PropertiesSize).Clear();
        var properties = (EventTraceProperties*)buffer;
        properties->Wnode.BufferSize = PropertiesSize;
        properties->LoggerNameOffset = (uint)sizeof(EventTraceProperties);
        ControlTrace(session, name, properties, EventTraceControlStop);
    }

    private void Pump()
    {
        try
        {
            ulong handle = _trace;
            uint status = ProcessTrace(&handle, 1, IntPtr.Zero, IntPtr.Zero);
            if (status != ErrorSuccess && status != ErrorCancelled && _running)
            {
                Fail($"Frame-rate tracing ended unexpectedly ({Describe(status)}).");
            }
        }
        catch (Exception ex)
        {
            Fail("Frame-rate tracing ended unexpectedly.", ex);
        }
        finally
        {
            _running = false;
        }
    }

    private void OnEvent(EventRecord* record)
    {
        try
        {
            ref EventHeader header = ref record->EventHeader;
            ushort id = header.EventDescriptor.Id;
            bool present = header.ProviderId == DxgiProvider
                ? id is DxgiPresentStart or DxgiPresentMultiplaneOverlayStart
                : header.ProviderId == D3D9Provider && id == D3D9PresentStart;
            if (present)
            {
                _tracker.OnPresent((int)header.ProcessId, header.TimeStamp);
                Interlocked.Increment(ref _events);
            }
        }
        catch
        {
            // Never let an exception unwind into the event-tracing thread.
        }
    }

    private void Fail(string problem, Exception? ex = null)
    {
        Problem = problem;
        _running = false;
        Log.Warn(LogCategory, problem, ex);
    }

    private static string Describe(uint status) => $"{new System.ComponentModel.Win32Exception((int)status).Message} ({status})";
}
