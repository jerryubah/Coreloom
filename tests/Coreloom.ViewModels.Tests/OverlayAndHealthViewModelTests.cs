using Coreloom.Core.Health;
using Coreloom.Core.Overlay;
using Coreloom.Core.Settings;
using Coreloom.Core.Telemetry;
using Coreloom.ViewModels.Overlay;
using Coreloom.ViewModels.Pages;

namespace Coreloom.ViewModels.Tests;

public class HealthViewModelTests
{
    [Fact]
    public void Health_IsSharedByThePages_AndRatesEachPart()
    {
        var (monitor, settings, _) = Fixture.Create();
        var context = Fixture.Context(monitor, settings);
        var gpu = new GpuViewModel(context);
        var cpu = new CpuViewModel(context);
        var overview = new OverviewViewModel(context);

        Assert.Same(context.Health.Gpu, gpu.Health);
        Assert.Same(context.Health.Cpu, cpu.Health);
        var tile = overview.Tiles.First(t => t.Id == DashboardCatalog.Health);
        Assert.Same(context.Health, Assert.IsType<HealthPanelViewModel>(tile.Content).Health);
        Assert.Equal((6, 2), (tile.ColumnSpan, tile.RowSpan));

        Assert.Equal(HealthStatus.Checking, context.Health.Overall);
        Assert.Equal("Checking…", gpu.Health.StatusText);
        for (int i = 0; i < 5; i++)
        {
            context.Health.Update(monitor.Engine.LatestFrame);
        }

        // A cool card; no CPU temperature without administrator rights and PawnIO; memory a third used.
        Assert.Equal(HealthStatus.Excellent, context.Health.Gpu.Status);
        Assert.Equal("Excellent", context.Health.Gpu.StatusText);
        Assert.Contains(context.Health.Gpu.Checks, c => c is { Title: "Temperature normal", Detail: "38 °C on average" });
        Assert.Equal(HealthStatus.Good, context.Health.Cpu.Status);
        Assert.Contains(context.Health.Cpu.Checks, c => c.State == CheckState.Unknown && c.Detail.Contains("administrator"));
        Assert.Equal(HealthStatus.Excellent, context.Health.Memory.Status);
        Assert.Equal(HealthStatus.Good, context.Health.Storage.Status); // no drives in the fixture
        Assert.Equal(4, context.Health.Parts.Count);
        Assert.Equal(HealthStatus.Good, context.Health.Overall);
        Assert.Equal("Good", context.Health.OverallText);
        settings.Dispose();
    }

    [Fact]
    public void Checks_AreUpdatedInPlace_WithProblemsFirst()
    {
        var report = new HealthReportViewModel("Graphics card", "Icon.Gpu");
        report.Apply(HealthReport.From([new HealthCheck("a", CheckState.Ok, "A", "1"), new HealthCheck("b", CheckState.Ok, "B", "1")]));
        var first = report.Checks[0];

        report.Apply(HealthReport.From([new HealthCheck("a", CheckState.Ok, "A", "2"), new HealthCheck("b", CheckState.Ok, "B", "2")]));
        Assert.Same(first, report.Checks[0]);
        Assert.Equal("2", first.Detail);

        report.Apply(HealthReport.From([new HealthCheck("a", CheckState.Ok, "A", "3"), new HealthCheck("b", CheckState.Problem, "B", "3")]));
        Assert.Equal("b", report.Checks[0].Id);
        Assert.Same(first, report.Checks[1]);
        Assert.Equal(HealthStatus.Attention, report.Status);
        Assert.Equal("Needs attention", report.StatusText);
    }
}

public class OverlayViewModelTests
{
    private sealed class FakeFrameRates : IFrameRateSource
    {
        public bool IsAvailable { get; set; } = true;

        public string? Problem { get; set; }

        public Dictionary<int, FrameRateStats> Rates { get; } = [];

        public Dictionary<int, double[]> FrameTimes { get; } = [];

        public FrameRateStats? Get(int processId) => Rates.TryGetValue(processId, out var stats) ? stats : null;

        public int RecentFrameTimes(int processId, Span<double> buffer)
        {
            if (!FrameTimes.TryGetValue(processId, out var times))
            {
                return 0;
            }

            int count = Math.Min(times.Length, buffer.Length);
            times.AsSpan(times.Length - count).CopyTo(buffer);
            return count;
        }
    }

    private static string Line(OverlayGroupViewModel group) =>
        group.Label + ": " + string.Join(" ", group.Values.Select(v => v.HasUnit ? $"{v.Number} {v.Unit}" : v.Number));

    [Fact]
    public void Overlay_ShowsTheChosenReadings_GroupedByPart_AndLeavesOutWhatThisPcLacks()
    {
        var (monitor, settings, _) = Fixture.Create();
        var context = Fixture.Context(monitor, settings);
        var overlay = context.Overlay;
        overlay.OnFrame(monitor.Engine.LatestFrame);

        // Defaults: FPS and frame time; GPU load, temperature, power and fan; CPU load and temperature; RAM.
        // The test PC has no fan readings or CPU temperature, so those are left out.
        Assert.Equal<string>(new[] { "FPS", "GPU", "CPU", "RAM" }, overlay.Groups.Select(g => g.Label));
        Assert.Equal("FPS: — — ms", Line(overlay.Groups[0]));
        Assert.Equal("GPU: 3 % 38 °C 95 W", Line(overlay.Groups[1]));
        Assert.Equal("CPU: 12 %", Line(overlay.Groups[2]));
        Assert.StartsWith("RAM: 24", Line(overlay.Groups[3]));
        Assert.False(overlay.IsEnabled);
        Assert.True(overlay.NeedsFrameRates);
        settings.Dispose();
    }

    [Fact]
    public void FrameRates_FollowTheProgramInFront()
    {
        var (monitor, settings, _) = Fixture.Create();
        var overlay = Fixture.Context(monitor, settings).Overlay;
        var rates = new FakeFrameRates();
        rates.Rates[1234] = new FrameRateStats(143.6, 6.96, 121.2, 144);
        overlay.FrameRates = rates;
        settings.Update("Overlay", s => s.Overlay.Items.Add(OverlayCatalog.LowFps));

        overlay.Tick(1234);
        Assert.Equal("FPS: 144 7.0 ms 121 1% low", Line(overlay.Groups[0]));

        overlay.Tick(99); // a program that isn't drawing
        Assert.Equal("FPS: — — ms — 1% low", Line(overlay.Groups[0]));
        overlay.Tick(null);
        Assert.Equal("—", overlay.Groups[0].Values[0].Number);
        settings.Dispose();
    }

    [Fact]
    public void FramePacerMark_AndGraph_FollowTheGameInFront()
    {
        var (monitor, settings, _) = Fixture.Create();
        var overlay = Fixture.Context(monitor, settings).Overlay;
        var rates = new FakeFrameRates();
        rates.Rates[1234] = new FrameRateStats(140.2, 7.13, 121.2, 140);
        rates.FrameTimes[1234] = [7.1, 7.0, 7.2, 16.4, 7.1];
        overlay.FrameRates = rates;
        settings.Update("Gaming", s =>
        {
            s.Gaming.GlobalFrameCap = 141;
            s.Gaming.GameFrameCaps.Add(new Core.Gaming.GameFrameCap { GameId = "steam:1", Name = "Paced", Fps = 90, Executables = ["paced.exe"] });
        });

        Assert.True(overlay.ShowGraph); // on by default, on the FPS line
        Assert.True(overlay.Groups[0].IsFrameRate);
        Assert.DoesNotContain(overlay.Groups[0].Values, v => v.Id == OverlayCatalog.FrameGraph);

        overlay.Tick(1234, @"C:\Games\Game\game.exe");
        Assert.True(overlay.IsPacing);
        Assert.Equal("141", overlay.PacingText);
        Assert.Equal(1000.0 / 141, overlay.FrameTimes.TargetFrameTime, 6);
        var buffer = new double[8];
        Assert.Equal(5, overlay.FrameTimes.Read(buffer));
        Assert.Equal(16.4, buffer[3]);

        overlay.Tick(1234, "Paced.exe"); // its own limit wins over the one for all games
        Assert.Equal("90", overlay.PacingText);

        overlay.Tick(99, "notepad.exe"); // not drawing frames: no mark, nothing to graph
        Assert.False(overlay.IsPacing);
        Assert.Equal(0, overlay.FrameTimes.Read(buffer));
        settings.Dispose();
    }

    [Fact]
    public void Appearance_AndWarnings_FollowSettings()
    {
        var (monitor, settings, _) = Fixture.Create();
        var overlay = Fixture.Context(monitor, settings).Overlay;
        var changed = new List<string?>();
        overlay.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        settings.Update("Overlay", s =>
        {
            s.Overlay.ShowLabels = false;
            s.Overlay.Layout = OverlayLayout.Horizontal;
            s.Overlay.FontSize = 20;
            s.Overlay.FontWeight = OverlayFontWeight.Bold;
            s.Overlay.BackgroundOpacity = 40;
            s.Overlay.TextColor = "#45D6A0";
        });

        Assert.Contains(string.Empty, changed); // everything refreshes
        Assert.True(overlay.IsHorizontal);
        Assert.Equal(20, overlay.FontSize);
        Assert.Equal(15.6, overlay.SmallFontSize);
        Assert.Equal("Bold", overlay.FontWeight);
        Assert.Equal(0.4, overlay.BackgroundOpacity);
        Assert.Equal("FPS", overlay.Groups[0].Values[0].Unit); // no labels: the unit says what it is
        overlay.OnFrame(monitor.Engine.LatestFrame);
        Assert.Equal("#45D6A0", overlay.Groups[1].Values[1].Color);

        // A GPU temperature alert at 40 °C makes 38 °C a warning.
        settings.Update("Alerts", s => s.Alerts.Rules.First(r => r.Id == "gpu-temp").Threshold = 40);
        overlay.OnFrame(monitor.Engine.LatestFrame);
        var temperature = overlay.Groups.First(g => g.Label == "GPU").Values.First(v => v.Id == OverlayCatalog.GpuTemperature);
        Assert.Equal(OverlayViewModel.WarningColor, temperature.Color);

        settings.Update("Overlay", s => s.Overlay.ColorWarnings = false);
        overlay.OnFrame(monitor.Engine.LatestFrame);
        temperature = overlay.Groups.First(g => g.Label == "GPU").Values.First(v => v.Id == OverlayCatalog.GpuTemperature);
        Assert.Equal("#45D6A0", temperature.Color);
        settings.Dispose();
    }
}

public class OverlaySettingsViewModelTests
{
    private static (OverlaySettingsViewModel Card, Services.SettingsService Settings) Create()
    {
        var (monitor, settings, _) = Fixture.Create();
        var gpu = new GpuViewModel(Fixture.Context(monitor, settings));
        return (gpu.OverlaySettings, settings);
    }

    [Fact]
    public void Switch_Items_AndPosition_AreSaved()
    {
        var (card, settings) = Create();
        Assert.False(card.Enabled);
        Assert.Equal("End", card.HotkeyText);
        Assert.Equal("Press End to show or hide it, also in games.", card.HotkeyHint);
        Assert.True(card.HotkeyIsSingleKey);

        Assert.True(settings.Current.Overlay.ActiveHotkey.IsNone); // End does nothing until the overlay is first used
        card.Enabled = true;
        Assert.True(settings.Current.Overlay.Enabled);
        Assert.True(card.Preview.IsEnabled);
        card.Enabled = false;
        Assert.True(settings.Current.Overlay.HasBeenEnabled);
        Assert.Equal("End", settings.Current.Overlay.ActiveHotkey.ToString()); // ...and then turns it back on
        card.Enabled = true;

        var voltage = card.ItemGroups.SelectMany(g => g.Items).First(i => i.Id == OverlayCatalog.GpuVoltage);
        voltage.IsShown = true;
        Assert.Contains(OverlayCatalog.GpuVoltage, settings.Current.Overlay.Items);
        card.ItemGroups.SelectMany(g => g.Items).First(i => i.Id == OverlayCatalog.Fps).IsShown = false;
        Assert.DoesNotContain(OverlayCatalog.Fps, settings.Current.Overlay.Items);
        Assert.Equal<string>(new[] { "Frame rate", "Graphics card", "Processor", "Memory" }, card.ItemGroups.Select(g => g.Name));

        var bottomRight = card.Positions.First(p => p.Position == OverlayPosition.BottomRight);
        bottomRight.SelectCommand.Execute(null);
        Assert.Equal(OverlayPosition.BottomRight, settings.Current.Overlay.Position);
        Assert.True(bottomRight.IsSelected);
        Assert.False(card.Positions[0].IsSelected);

        card.SelectedLayout = card.LayoutOptions[1];
        card.SelectedWeight = card.WeightOptions[3];
        card.FontSize = 18;
        card.BackgroundOpacity = 35;
        Assert.Equal(OverlayLayout.Horizontal, settings.Current.Overlay.Layout);
        Assert.Equal(OverlayFontWeight.Bold, settings.Current.Overlay.FontWeight);
        Assert.Equal(18, settings.Current.Overlay.FontSize);
        Assert.Equal(35, settings.Current.Overlay.BackgroundOpacity);
        settings.Dispose();
    }

    [Fact]
    public void Hotkey_IsCapturedFromTheNextKeyPress()
    {
        var (card, settings) = Create();
        Assert.False(card.CaptureHotkey(0x4F, HotkeyModifiers.Control)); // not waiting

        card.ChangeHotkeyCommand.Execute(null);
        Assert.True(card.IsCapturingHotkey);
        Assert.False(card.CaptureHotkey(0x11, HotkeyModifiers.Control)); // Ctrl on its own: keep waiting
        Assert.True(card.CaptureHotkey(0x4F, HotkeyModifiers.Control | HotkeyModifiers.Shift));
        Assert.False(card.IsCapturingHotkey);
        Assert.Equal("Ctrl + Shift + O", card.HotkeyText);
        Assert.False(card.HotkeyIsSingleKey);
        Assert.Equal(0x4F, settings.Current.Overlay.Hotkey.Key);

        card.ChangeHotkeyCommand.Execute(null);
        Assert.True(card.CaptureHotkey(0x1B, HotkeyModifiers.None)); // Esc cancels
        Assert.Equal("Ctrl + Shift + O", card.HotkeyText);

        card.ClearHotkeyCommand.Execute(null);
        Assert.Equal("None", card.HotkeyText);
        Assert.Equal("No hotkey set.", card.HotkeyHint);
        card.ResetHotkeyCommand.Execute(null);
        Assert.Equal("End", card.HotkeyText);
        settings.Dispose();
    }

    [Fact]
    public void Colors_AcceptHexOnly_AndResetKeepsTheSwitchAndHotkey()
    {
        var (card, settings) = Create();
        card.TextColor = "ffd54a";
        Assert.Equal("#FFD54A", settings.Current.Overlay.TextColor);

        card.TextColor = "yellow";
        Assert.Equal("yellow", card.TextColor); // kept in the box
        Assert.Equal("#FFD54A", settings.Current.Overlay.TextColor); // not applied

        card.SetBackgroundColorCommand.Execute("#0B1A33");
        Assert.Equal("#0B1A33", settings.Current.Overlay.BackgroundColor);

        card.Enabled = true;
        card.ChangeHotkeyCommand.Execute(null);
        card.CaptureHotkey(0x52, HotkeyModifiers.Alt);
        card.ResetCommand.Execute(null);

        Assert.Equal(OverlaySettings.DefaultTextColor, settings.Current.Overlay.TextColor);
        Assert.Equal(OverlaySettings.DefaultTextColor, card.TextColor);
        Assert.Equal(OverlaySettings.DefaultBackgroundColor, settings.Current.Overlay.BackgroundColor);
        Assert.True(settings.Current.Overlay.Enabled);
        Assert.Equal("Alt + R", card.HotkeyText);
        settings.Dispose();
    }

    [Fact]
    public void FrameRateNote_SaysWhyFpsIsMissing()
    {
        var (card, settings) = Create();
        Assert.Null(card.FrameRateNote);
        card.Preview.FrameRates = new UnavailableFrameRates();
        card.RefreshFrameRateNote();
        Assert.Equal("No access", card.FrameRateNote);
        settings.Dispose();
    }

    private sealed class UnavailableFrameRates : IFrameRateSource
    {
        public bool IsAvailable => false;

        public string? Problem => "No access";

        public FrameRateStats? Get(int processId) => null;

        public int RecentFrameTimes(int processId, Span<double> buffer) => 0;
    }
}
