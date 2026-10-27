using System.Text.Json;
using Coreloom.Core.Overlay;
using Coreloom.Core.Settings;
using Coreloom.ViewModels.Connect;

namespace Coreloom.ViewModels.Tests;

public sealed class RemoteOverlayTests
{
    /// <summary>A game in front drawing frames of the given times (newest last), shown <see cref="Age"/> ago.</summary>
    private sealed class FakeFrames : IForegroundFrameRates
    {
        public string? Program { get; set; } = "Cyberpunk2077.exe";

        public List<double> Frames { get; } = [];

        public double Age { get; set; } = 2;

        public bool Start() => true;

        public void Stop()
        {
        }

        public string? Problem => null;

        public ForegroundFrames Read(Span<double> frameTimes)
        {
            int count = Math.Min(Frames.Count, frameTimes.Length);
            Frames.Skip(Frames.Count - count).ToArray().CopyTo(frameTimes);
            var stats = Frames.Count > 0 ? new FrameRateStats(1000 / Frames.Average(), Frames.Average(), 100, Frames.Count) : (FrameRateStats?)null;
            return new ForegroundFrames(Program, stats, count, Age);
        }
    }

    private static string Live(RemoteOverlay overlay)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            overlay.WriteLive(writer);
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    [Fact]
    public void Values_OfferEveryReadingThisComputerHas_AndStartFromTheInGameOverlays()
    {
        var (monitor, settings, _) = Fixture.Create();
        settings.Update("Overlay", s => s.Overlay.Items = [OverlayCatalog.Fps, OverlayCatalog.GpuTemperature, OverlayCatalog.Ram]);
        var overlay = new RemoteOverlay(Fixture.Context(monitor, settings), new FakeFrames());

        using var json = JsonDocument.Parse(overlay.Values(monitor.Engine.LatestFrame));
        var root = json.RootElement;
        var catalog = root.GetProperty("catalog").EnumerateArray().Select(c => c.GetProperty("id").GetString()).ToList();
        Assert.Contains(OverlayCatalog.Fps, catalog);
        Assert.Contains(OverlayCatalog.GpuLoad, catalog); // more than the in-game overlay shows
        Assert.DoesNotContain(OverlayCatalog.CpuTemperature, catalog); // this computer has no reading for it
        Assert.Equal(new[] { OverlayCatalog.Fps, OverlayCatalog.GpuTemperature, OverlayCatalog.Ram },
            root.GetProperty("shown").EnumerateArray().Select(s => s.GetString()).ToArray());
        Assert.Equal("38", root.GetProperty("items").GetProperty(OverlayCatalog.GpuTemperature).GetProperty("n").GetString());
        Assert.False(root.GetProperty("items").TryGetProperty(OverlayCatalog.Fps, out _)); // frame rates come live
        Assert.Equal("measured", root.GetProperty("frameRates").GetString());
        Assert.Equal(settings.Current.Overlay.TextColor, root.GetProperty("look").GetProperty("text").GetString());
        settings.Dispose();
    }

    [Fact]
    public void Values_OnAComputerThatCantMeasureFrameRates_LeaveThemOut()
    {
        var (monitor, settings, _) = Fixture.Create();
        var overlay = new RemoteOverlay(Fixture.Context(monitor, settings), frameRates: null);
        using var json = JsonDocument.Parse(overlay.Values(monitor.Engine.LatestFrame));
        Assert.DoesNotContain(json.RootElement.GetProperty("catalog").EnumerateArray(), c => c.GetProperty("id").GetString() == OverlayCatalog.Fps);
        Assert.Equal("unavailable", json.RootElement.GetProperty("frameRates").GetString());
        Assert.Equal("{\"now\":", Live(overlay)[..7]);
        settings.Dispose();
    }

    [Fact]
    public void FrameTimes_AreSentOnce_AndStartAfreshForAnotherGame()
    {
        var (monitor, settings, _) = Fixture.Create();
        var frames = new FakeFrames();
        frames.Frames.AddRange([7, 7, 7.2, 30, 7]);
        double now = 10_000;
        var overlay = new RemoteOverlay(Fixture.Context(monitor, settings), frames) { Clock = () => now };

        using (var first = JsonDocument.Parse(Live(overlay)))
        {
            var root = first.RootElement;
            Assert.Equal("Cyberpunk2077.exe", root.GetProperty("game").GetString());
            Assert.Equal(new[] { 7, 7, 7.2, 30, 7 }, root.GetProperty("frames").GetProperty("ms").EnumerateArray().Select(m => m.GetDouble()).ToArray());
            Assert.Equal(9_998, root.GetProperty("frames").GetProperty("end").GetDouble());
            Assert.True(root.TryGetProperty("fps", out _));
        }

        // Nothing new half a second later: no frames again. Then two more arrive (the newest shown just now): only those.
        now += 500;
        frames.Age = 502;
        using (var none = JsonDocument.Parse(Live(overlay)))
        {
            Assert.False(none.RootElement.TryGetProperty("frames", out _));
        }

        frames.Frames.AddRange([8, 9]); // shown right after the last ones: the newest ended 17 ms after them
        now += 20;
        frames.Age = 505;
        using (var next = JsonDocument.Parse(Live(overlay)))
        {
            Assert.Equal(new[] { 8d, 9d }, next.RootElement.GetProperty("frames").GetProperty("ms").EnumerateArray().Select(m => m.GetDouble()).ToArray());
        }

        frames.Program = "eldenring.exe";
        using (var other = JsonDocument.Parse(Live(overlay)))
        {
            Assert.Equal("eldenring.exe", other.RootElement.GetProperty("game").GetString());
            Assert.Equal(7, other.RootElement.GetProperty("frames").GetProperty("ms").GetArrayLength()); // its latest frames
        }

        settings.Dispose();
    }
}
