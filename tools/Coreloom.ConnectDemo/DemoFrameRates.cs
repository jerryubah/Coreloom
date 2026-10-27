using System.Diagnostics;
using Coreloom.Core.Overlay;
using Coreloom.ViewModels.Connect;

namespace Coreloom.ConnectDemo;

/// <summary>A game at about 141 FPS (a 144 Hz screen with a limiter) with small jitter and a stutter now and then.</summary>
internal sealed class DemoFrameRates : IForegroundFrameRates
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly List<(double At, double Ms)> _frames = [];
    private readonly Random _random = new(7);
    private readonly object _gate = new();
    private double _next;

    public string? Problem => null;

    public bool Start() => true;

    public void Stop()
    {
    }

    public ForegroundFrames Read(Span<double> frameTimes)
    {
        lock (_gate)
        {
            double now = _clock.Elapsed.TotalMilliseconds;
            while (_next <= now)
            {
                double ms = 7.09 + ((_random.NextDouble() - 0.5) * 0.9);
                if (_random.NextDouble() < 0.004)
                {
                    ms *= 2.2 + (_random.NextDouble() * 2); // a stutter
                }

                _next += ms;
                _frames.Add((_next, ms));
            }

            _frames.RemoveAll(f => f.At < now - 12000);
            var shown = _frames.Where(f => f.At <= now).ToList();
            int count = Math.Min(frameTimes.Length, shown.Count);
            for (int i = 0; i < count; i++)
            {
                frameTimes[i] = shown[shown.Count - count + i].Ms;
            }

            var lastSecond = shown.Where(f => f.At > now - 1000).Select(f => f.Ms).ToList();
            var lastTen = shown.Where(f => f.At > now - 10000).Select(f => f.Ms).OrderDescending().ToList();
            double fps = lastSecond.Count;
            double low = lastTen.Count > 0 ? 1000 / lastTen.Take(Math.Max(1, lastTen.Count / 100)).Average() : 0;
            var stats = new FrameRateStats(fps, lastSecond.Count > 0 ? lastSecond.Average() : 0, low, lastSecond.Count);
            double age = shown.Count > 0 ? now - shown[^1].At : 0;
            return new ForegroundFrames("Cyberpunk2077.exe", stats, count, age);
        }
    }
}
