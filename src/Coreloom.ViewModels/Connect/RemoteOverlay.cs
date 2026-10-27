using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Coreloom.Core.Formatting;
using Coreloom.Core.Overlay;
using Coreloom.Core.Settings;
using Coreloom.Core.Telemetry;
using Coreloom.ViewModels.Infrastructure;
using Coreloom.ViewModels.Overlay;
using Coreloom.ViewModels.Pages;

namespace Coreloom.ViewModels.Connect;

/// <summary>The program in front and its frame rate, as the phone's overlay shows it.</summary>
/// <param name="Program">"Cyberpunk2077.exe"; null when Coreloom itself or nothing is in front.</param>
/// <param name="Stats">Its frame rate, or null when it isn't drawing frames.</param>
/// <param name="FrameTimeCount">How many of its latest frame times were written to the buffer (oldest first).</param>
/// <param name="NewestAgeMilliseconds">How long ago the newest of them was shown.</param>
public readonly record struct ForegroundFrames(string? Program, FrameRateStats? Stats, int FrameTimeCount, double NewestAgeMilliseconds);

/// <summary>
/// Frame rates of the program in front, for the phone's overlay. Windows measures them (from its graphics events, as the
/// in-game overlay does), whether or not the in-game overlay is on; a Mac can't measure another app's frames.
/// </summary>
public interface IForegroundFrameRates
{
    /// <summary>Starts measuring for the phone; false (see <see cref="Problem"/>) when frame rates can't be measured.</summary>
    bool Start();

    /// <summary>Stops measuring for the phone (the in-game overlay and others keep their own measurement).</summary>
    void Stop();

    string? Problem { get; }

    /// <summary>The program in front, its frame rate and its latest frame times. Safe to call from any thread.</summary>
    ForegroundFrames Read(Span<double> frameTimes);
}

/// <summary>
/// The phone's overlay: every overlay reading this computer has (so the phone can show more than the in-game overlay),
/// the ones the in-game overlay shows (the phone's default), its look, and the frame rate and frame times of the game in
/// front. Readings come with each frame; frame rates are read up to twice a second while a phone shows its overlay.
/// Phones only read: what they choose to show stays on the phone.
/// </summary>
public sealed class RemoteOverlay
{
    private const int MaximumFrames = 1200;

    private readonly PageContext _context;
    private readonly IForegroundFrameRates? _frameRates;
    private readonly OverlayReadings _readings;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly double[] _buffer = new double[2048];
    private readonly object _gate = new();
    private double _lastFrameEnd = double.NegativeInfinity;
    private string? _lastProgram;

    public RemoteOverlay(PageContext context, IForegroundFrameRates? frameRates)
    {
        _context = context;
        _frameRates = frameRates;
        _readings = new OverlayReadings(context);
    }

    /// <summary>This object's clock in milliseconds (tests set their own).</summary>
    internal Func<double>? Clock { get; init; }

    /// <summary>Whether this computer measures frame rates (Windows) or not (a Mac).</summary>
    public bool MeasuresFrameRates => _frameRates is not null;

    /// <summary>The readings part: {"catalog", "shown", "items", "look", "frameRates"} as UTF-8 JSON. Build it on the UI thread.</summary>
    public byte[] Values(TelemetryFrame frame)
    {
        var options = _context.Settings.Current.Overlay;
        int slot = _context.GpuSlot;
        bool mac = _context.IsMac;
        var f = _context.Settings.Formatter;
        var catalog = OverlayCatalog.All
            .Where(i => !mac || OverlayCatalog.OnMac(i.Id))
            .Where(i => OverlayCatalog.IsFrameRate(i.Id) ? MeasuresFrameRates : i.Id == OverlayCatalog.CpuFan ? _readings.CpuFanRpm(frame) is not null : _readings.IsAvailable(i.Id, frame, slot))
            .ToList();

        using var stream = new MemoryStream(4096);
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WritePropertyName("catalog");
            w.WriteStartArray();
            foreach (var (id, name, group) in catalog)
            {
                w.WriteStartObject();
                w.WriteString("id", id);
                w.WriteString("name", name);
                w.WriteString("group", GroupKey(group));
                w.WriteEndObject();
            }

            w.WriteEndArray();

            // What the in-game overlay shows (whether or not it's on): the phone shows the same until told otherwise there.
            w.WritePropertyName("shown");
            w.WriteStartArray();
            foreach (string id in options.Items.Where(i => catalog.Any(c => c.Id == i)))
            {
                w.WriteStringValue(id);
            }

            w.WriteEndArray();

            w.WritePropertyName("items");
            w.WriteStartObject();
            foreach (var (id, _, _) in catalog.Where(c => OverlayCatalog.IsValue(c.Id) && !OverlayCatalog.IsFrameRate(c.Id)))
            {
                string number, unit;
                int level = 0;
                if (id == OverlayCatalog.CpuFan)
                {
                    var fan = _readings.CpuFan(frame);
                    (number, unit) = (fan.Number, fan.Unit);
                }
                else if (_readings.MetricFor(id, slot, frame) is { } metric)
                {
                    metric.Update(frame, f, _context.Monitor.Engine.Catalog);
                    (number, unit) = (metric.Number, metric.Unit);
                    level = metric.Level switch
                    {
                        ThresholdLevel.Critical => 3,
                        ThresholdLevel.Elevated => 2,
                        _ => 0,
                    };
                }
                else
                {
                    continue;
                }

                w.WritePropertyName(id);
                w.WriteStartObject();
                w.WriteString("n", number);
                w.WriteString("u", unit);
                if (level > 0)
                {
                    w.WriteNumber("l", level);
                }

                w.WriteEndObject();
            }

            w.WriteEndObject();

            w.WritePropertyName("look");
            w.WriteStartObject();
            w.WriteString("text", options.TextColor);
            w.WriteString("background", options.BackgroundColor);
            w.WriteNumber("backgroundOpacity", options.BackgroundOpacity);
            w.WriteBoolean("labels", options.ShowLabels);
            w.WriteBoolean("warnings", options.ColorWarnings);
            w.WriteString("warning", OverlayViewModel.WarningColor);
            w.WriteString("critical", OverlayViewModel.CriticalColor);
            w.WriteEndObject();

            w.WriteString("frameRates", MeasuresFrameRates ? "measured" : "unavailable");
            w.WriteEndObject();
        }

        return stream.ToArray();
    }

    /// <summary>
    /// The live part: {"now": ms, "game": "…", "fps": {fps, ft, low}, "frames": {"end": ms, "ms": [...]}} with the frame
    /// times shown since the last call (on this object's clock, so the phone can place them in time). Any thread.
    /// </summary>
    public void WriteLive(Utf8JsonWriter w)
    {
        w.WriteStartObject();
        lock (_gate)
        {
            double now = Clock?.Invoke() ?? _clock.Elapsed.TotalMilliseconds;
            w.WriteNumber("now", Math.Round(now, 1));
            if (_frameRates is { } source)
            {
                var read = source.Read(_buffer);
                if (read.Program != _lastProgram)
                {
                    _lastProgram = read.Program;
                    _lastFrameEnd = double.NegativeInfinity; // another game: its frames start afresh
                }

                if (read.Program is { } program)
                {
                    w.WriteString("game", program);
                }

                if (read.Stats is { } stats)
                {
                    w.WritePropertyName("fps");
                    w.WriteStartObject();
                    w.WriteString("fps", Math.Round(stats.Fps).ToString("0", CultureInfo.InvariantCulture));
                    w.WriteString("ft", stats.FrameTimeMilliseconds > 0 ? stats.FrameTimeMilliseconds.ToString("0.0", CultureInfo.InvariantCulture) : "—");
                    w.WriteString("low", Math.Round(stats.LowFps).ToString("0", CultureInfo.InvariantCulture));
                    w.WriteEndObject();
                }

                WriteFrames(w, read, now);
            }
        }

        w.WriteEndObject();
    }

    /// <summary>The frames newer than the last ones sent: the newest one's time and each frame's time, oldest first.</summary>
    private void WriteFrames(Utf8JsonWriter w, ForegroundFrames read, double now)
    {
        int count = Math.Min(read.FrameTimeCount, _buffer.Length);
        if (count == 0)
        {
            return;
        }

        double end = now - read.NewestAgeMilliseconds;
        int first = count;
        double t = end;
        while (first > 0 && count - first < MaximumFrames)
        {
            if (t <= _lastFrameEnd + 0.25 || end - t > 3500)
            {
                break; // sent already, or older than the phone's graph shows
            }

            first--;
            t -= _buffer[first];
        }

        if (first == count)
        {
            return;
        }

        _lastFrameEnd = end;
        w.WritePropertyName("frames");
        w.WriteStartObject();
        w.WriteNumber("end", Math.Round(end, 2));
        w.WritePropertyName("ms");
        w.WriteStartArray();
        for (int i = first; i < count; i++)
        {
            w.WriteNumberValue(Math.Round(_buffer[i], 2));
        }

        w.WriteEndArray();
        w.WriteEndObject();
    }

    /// <summary>Begins a new watch: the next frames sent are the latest second or so.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _lastFrameEnd = double.NegativeInfinity;
            _lastProgram = null;
        }
    }

    private static string GroupKey(OverlayGroup group) => group switch
    {
        OverlayGroup.FrameRate => "fps",
        OverlayGroup.Graphics => "gpu",
        OverlayGroup.Processor => "cpu",
        _ => "ram",
    };
}
