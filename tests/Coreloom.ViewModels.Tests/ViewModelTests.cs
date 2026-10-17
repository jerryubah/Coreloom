using System.Globalization;
using Coreloom.Core;
using Coreloom.Core.Alerts;
using Coreloom.Core.Diagnostics;
using Coreloom.Core.Formatting;
using Coreloom.Core.Hardware;
using Coreloom.Core.History;
using Coreloom.Core.Settings;
using Coreloom.Core.Telemetry;
using Coreloom.Core.Updates;
using Coreloom.ViewModels.Infrastructure;
using Coreloom.ViewModels.Pages;
using Coreloom.ViewModels.Services;

namespace Coreloom.ViewModels.Tests;

internal sealed class ScriptedProvider : ITelemetryProvider
{
    private readonly Dictionary<string, (MetricCategory Category, MetricKind Kind)> _definitions = new();
    private readonly Dictionary<string, MetricHandle> _handles = new();

    public Dictionary<string, double> Values { get; } = new();

    public string Name => "Scripted";

    public MonitoringSection Section => MonitoringSection.System;

    public int InitStage => 0;

    public TimeSpan MinimumPollInterval => TimeSpan.Zero;

    public ScriptedProvider Define(string id, MetricCategory category, MetricKind kind, double value)
    {
        _definitions[id] = (category, kind);
        Values[id] = value;
        return this;
    }

    public ProviderInitResult Initialize(IProviderContext context)
    {
        foreach (var (id, (category, kind)) in _definitions)
        {
            _handles[id] = context.Register(new MetricDescriptor(id, id, category, kind), Provenance.Measured, 10, "test source");
        }

        context.SetUnavailableReason(MetricIds.CpuTemperature, "Requires administrator rights");
        return ProviderInitResult.Running();
    }

    public void Poll(IMetricWriter writer)
    {
        foreach (var (id, value) in Values)
        {
            writer.Write(_handles[id], value);
        }
    }

    public void Shutdown()
    {
    }

    public void Dispose()
    {
    }
}

internal sealed class NullProcessSampler : IProcessSampler
{
    public IReadOnlyList<ProcessUsage> Sample() => [new ProcessUsage("chrome.exe", 12, 3.5, 1_500_000_000, 1_000_000_000), new ProcessUsage("Coreloom.exe", 1, 0.4, 90_000_000, 60_000_000)];
}

internal static class Fixture
{
    public static (MonitorContext Monitor, SettingsService Settings, ScriptedProvider Provider) Create(string? settingsDir = null, Func<ScriptedProvider, ScriptedProvider>? extra = null)
    {
        var provider = new ScriptedProvider()
            .Define(MetricIds.CpuLoad, MetricCategory.Cpu, MetricKind.Load, 12.4)
            .Define(MetricIds.GpuLoad(0), MetricCategory.Gpu, MetricKind.Load, 3)
            .Define(MetricIds.GpuTemperature(0), MetricCategory.Gpu, MetricKind.Temperature, 38)
            .Define(MetricIds.GpuPower(0), MetricCategory.Gpu, MetricKind.Power, 95)
            .Define(MetricIds.GpuPowerLimit(0), MetricCategory.Gpu, MetricKind.Power, 575)
            .Define(MetricIds.MemoryUsed, MetricCategory.Memory, MetricKind.Data, 24d * 1024 * 1024 * 1024)
            .Define(MetricIds.MemoryAvailable, MetricCategory.Memory, MetricKind.Data, 40d * 1024 * 1024 * 1024)
            .Define(MetricIds.MemoryTotal, MetricCategory.Memory, MetricKind.Data, 64d * 1024 * 1024 * 1024)
            .Define(MetricIds.MemoryLoad, MetricCategory.Memory, MetricKind.Load, 37.5);
        provider = extra?.Invoke(provider) ?? provider;

        var engine = new TelemetryEngine([provider], new MetricCatalog(), new DeviceRegistry(), new HistoryStore(256), SystemCapabilities.Unknown);
        engine.InitializeAll();
        engine.PollOnce();

        var summary = new SystemSummary("PC", "Windows 11 Pro", "24H2", "ASRock X870E Nova WiFi", null, "AMD Ryzen 9 9950X3D 16-Core Processor", 16, 32, 64L * 1024 * 1024 * 1024, []);
        var monitor = new MonitorContext
        {
            Engine = engine,
            Summary = summary,
            Processes = new NullProcessSampler(),
            PrimaryGpuSlot = () => 0,
            Gpus = () => [(0, "NVIDIA GeForce RTX 5090", false)],
            NetworkAdapters = () => [],
            LibreHardwareMonitorIncluded = true,
        };

        string dir = settingsDir ?? Path.Combine(Path.GetTempPath(), "coreloom-vm-" + Guid.NewGuid().ToString("N"));
        var settings = new SettingsService(new SettingsStore(Path.Combine(dir, "settings.json")), new AppSettings().Normalize());
        return (monitor, settings, provider);
    }

    public static PageContext Context(MonitorContext monitor, SettingsService settings) =>
        new() { Monitor = monitor, Settings = settings, Timeline = new TimelineViewModel(monitor.Engine.History) };
}

public class MetricViewModelTests
{
    [Fact]
    public void Update_FormatsValueLevelAndSource()
    {
        var (monitor, settings, _) = Fixture.Create();
        var metric = new MetricViewModel(MetricIds.GpuTemperature(0), "GPU temperature", ValueStyle.Temperature, ColorKeys.Gpu, new Thresholds(30, 80));

        metric.Update(monitor.Engine.LatestFrame, new UnitFormatter(culture: CultureInfo.InvariantCulture), monitor.Engine.Catalog);

        Assert.True(metric.HasValue);
        Assert.Equal("38", metric.Number);
        Assert.Equal("°C", metric.Unit);
        Assert.Equal(ThresholdLevel.Elevated, metric.Level);
        Assert.Equal(0.38, metric.Fraction, 6);
        Assert.Equal("Scripted", metric.Source);
        Assert.Contains("test source", metric.ToolTip);
        settings.Dispose();
    }

    [Fact]
    public void MissingValue_ShowsUnavailableWithReason()
    {
        var (monitor, settings, _) = Fixture.Create();
        var metric = new MetricViewModel(MetricIds.CpuTemperature, "CPU temperature", ValueStyle.Temperature);

        metric.Update(monitor.Engine.LatestFrame, UnitFormatter.Default, monitor.Engine.Catalog);

        Assert.False(metric.HasValue);
        Assert.Equal("—", metric.Number);
        Assert.Contains("Requires administrator rights", metric.ToolTip);
        settings.Dispose();
    }

    [Fact]
    public void Fahrenheit_IsAppliedToNumberAndStats()
    {
        var (monitor, settings, provider) = Fixture.Create();
        monitor.Engine.PollOnce();
        var f = new UnitFormatter(TemperatureUnit.Fahrenheit, culture: CultureInfo.InvariantCulture);
        var metric = new MetricViewModel(MetricIds.GpuTemperature(0), "GPU", ValueStyle.Temperature);

        metric.Update(monitor.Engine.LatestFrame, f, monitor.Engine.Catalog, monitor.Engine.History, 60_000);

        Assert.Equal("100", metric.Number);
        Assert.Equal("°F", metric.Unit);
        Assert.Equal("Min 100 · Avg 100 · Max 100", metric.StatsText);
        settings.Dispose();
    }

    [Fact]
    public void Temperature_GlowsInTheColorOfHowWarmItIs()
    {
        var (monitor, settings, _) = Fixture.Create();
        var metric = new MetricViewModel(MetricIds.GpuTemperature(0), "GPU temperature", ValueStyle.Temperature, ColorKeys.Gpu, Thresholds.GpuTemperature);
        Assert.True(metric.IsTemperature);
        Assert.Equal(ColorKeys.TempNormal, metric.GlowKey); // no reading yet
        var changes = new List<string?>();
        metric.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        metric.Update(monitor.Engine.LatestFrame, UnitFormatter.Default, monitor.Engine.Catalog); // 38 °C: cool
        Assert.Equal(ColorKeys.TempCool, metric.GlowKey);
        Assert.Contains(nameof(MetricViewModel.GlowKey), changes);

        metric.Thresholds = new Thresholds(60, 90, 30);
        Assert.Equal(ColorKeys.TempNormal, metric.GlowKey);
        metric.Thresholds = new Thresholds(30, 80);
        Assert.Equal(ColorKeys.TempWarm, metric.GlowKey);
        metric.Thresholds = new Thresholds(20, 35);
        Assert.Equal(ColorKeys.TempHot, metric.GlowKey);
        settings.Dispose();
    }

    [Fact]
    public void OtherMetrics_GlowInTheirSeriesColor()
    {
        var (monitor, settings, _) = Fixture.Create();
        var metric = new MetricViewModel(MetricIds.CpuLoad, "CPU load", ValueStyle.Percent, ColorKeys.Cpu, Thresholds.Load);
        metric.Update(monitor.Engine.LatestFrame, UnitFormatter.Default, monitor.Engine.Catalog);

        Assert.False(metric.IsTemperature);
        Assert.Equal(ColorKeys.Cpu, metric.GlowKey);
        settings.Dispose();
    }

    [Theory]
    [InlineData(ThresholdLevel.Cool, "Temp.Cool")]
    [InlineData(ThresholdLevel.Normal, "Temp.Normal")]
    [InlineData(ThresholdLevel.Elevated, "Temp.Warm")]
    [InlineData(ThresholdLevel.Critical, "Temp.Hot")]
    public void TemperatureColors_FollowTheLevel(ThresholdLevel level, string key) =>
        Assert.Equal(key, ColorKeys.ForTemperature(level));
}

public class OverviewViewModelTests
{
    [Fact]
    public void Layout_FollowsDashboardSettings()
    {
        var (monitor, settings, _) = Fixture.Create();
        settings.Current.Dashboard.Cards.First(c => c.Id == DashboardCatalog.GpuPower).Visible = false;
        var cards = settings.Current.Dashboard.Cards;
        var cpu = cards.First(c => c.Id == DashboardCatalog.CpuLoad);
        cards.Remove(cpu);
        cards.Insert(0, cpu);

        var overview = new OverviewViewModel(Fixture.Context(monitor, settings));

        Assert.Equal(DashboardCatalog.CpuLoad, overview.SummaryCards[0].Id);
        Assert.DoesNotContain(overview.SummaryCards, c => c.Id == DashboardCatalog.GpuPower);
        Assert.Equal(DashboardCatalog.All.Count(c => !c.IsSummaryCard), overview.Panels.Count);
        settings.Dispose();
    }

    [Fact]
    public void Update_FillsCardsAndHeader()
    {
        var (monitor, settings, _) = Fixture.Create();
        var overview = new OverviewViewModel(Fixture.Context(monitor, settings));

        overview.Activate(monitor.Engine.LatestFrame);

        var gpuPower = overview.SummaryCards.First(c => c.Id == DashboardCatalog.GpuPower).Metric;
        Assert.Equal("95", gpuPower.Number);
        Assert.Equal(575, gpuPower.GaugeMaximum);
        Assert.Contains("575", gpuPower.Detail);
        Assert.Equal("ASRock X870E Nova WiFi", overview.Specs.Title);
        Assert.Equal("NVIDIA GeForce RTX 5090", overview.Specs.Subtitle);
        Assert.Null(overview.Subtitle);
        var cpuTemp = overview.SummaryCards.First(c => c.Id == DashboardCatalog.CpuTemperature).Metric;
        Assert.False(cpuTemp.HasValue);
        settings.Dispose();
    }
}

public class NameTests
{
    [Theory]
    [InlineData("AMD Ryzen 9 9950X3D 16-Core Processor", "AMD Ryzen 9 9950X3D")]
    [InlineData("Intel(R) Core(TM) i9-14900K", "Intel Core i9-14900K")]
    [InlineData("Intel(R) Core(TM) i7-8700K CPU @ 3.70GHz", "Intel Core i7-8700K")]
    public void ShortCpuName(string input, string expected) => Assert.Equal(expected, PageViewModel.ShortCpuName(input));

    [Fact]
    public void ShortGpuName() => Assert.Equal("GeForce RTX 5090", PageViewModel.ShortGpuName("NVIDIA GeForce RTX 5090"));
}

public class CsvExporterTests
{
    [Fact]
    public void Build_AlignsColumnsByTimestamp()
    {
        var history = new HistoryStore(200);
        history.Append("a", 1000, 1.5);
        history.Append("a", 2000, 2.5);
        history.Append("b", 2000, 40);
        var metrics = new[]
        {
            new MetricDescriptor("a", "CPU utilization", MetricCategory.Cpu, MetricKind.Load),
            new MetricDescriptor("b", "GPU, temperature", MetricCategory.Gpu, MetricKind.Temperature),
        };
        var origin = new DateTime(2026, 10, 12, 12, 0, 0);

        string csv = CsvExporter.Build(history, metrics, 0, 5000, t => origin.AddMilliseconds(t));
        string[] lines = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("timestamp,CPU utilization (%),\"GPU, temperature (°C)\"", lines[0]);
        Assert.Equal("2026-10-12 12:00:01.000,1.5,", lines[1]);
        Assert.Equal("2026-10-12 12:00:02.000,2.5,40", lines[2]);
    }
}

public class SettingsServiceTests
{
    [Fact]
    public void Update_RaisesChanged_RebuildsFormatter_AndFlushPersists()
    {
        string dir = Path.Combine(Path.GetTempPath(), "coreloom-ss-" + Guid.NewGuid().ToString("N"));
        var store = new SettingsStore(Path.Combine(dir, "settings.json"));
        var service = new SettingsService(store, new AppSettings().Normalize());
        string? raised = null;
        service.Changed += s => raised = s;

        service.Update("Monitoring", s => s.Monitoring.TemperatureUnit = TemperatureUnit.Fahrenheit);
        service.Flush();

        Assert.Equal("Monitoring", raised);
        Assert.Equal(TemperatureUnit.Fahrenheit, service.Formatter.TemperatureUnit);
        Assert.Equal(TemperatureUnit.Fahrenheit, store.Load().Monitoring.TemperatureUnit);
        service.Dispose();
        Directory.Delete(dir, true);
    }
}

public class ShellViewModelTests
{
    private sealed class Stubs : IThemeService, IStartupService, IElevationService, IShellService, IAppLifetime, IDiagnosticsService, IUpdateInstaller
    {
        public AppearanceSettings? Applied;
        public bool IsMicaSupported => true;
        public void Apply(AppearanceSettings appearance) => Applied = appearance;
        public bool IsStartAtLogonRegistered { get; set; }
        public bool LastMinimized;
        public int StartupCalls;
        public bool StartupResult = true;
        public Task<bool> SetStartAtLogonAsync(bool enabled, bool minimized)
        {
            StartupCalls++;
            LastMinimized = minimized;
            if (StartupResult)
            {
                IsStartAtLogonRegistered = enabled;
            }

            return Task.FromResult(StartupResult);
        }

        public bool IsElevated => true;
        public void OpenUrl(string url) { }
        public void OpenFolder(string path) { }
        public void OpenFile(string path) { }
        public void CopyToClipboard(string text) { }
        public string? PickSaveFile(string suggestedName, string filter) => null;
        public void ShowMessage(string title, string message) { }
        public bool Confirm(string title, string message) => false;
        public void Restart() { }
        public void Exit() { }
        public string LogDirectory => Path.GetTempPath();
        public void SetVerboseLogging(bool enabled) { }
        public IReadOnlyList<LogEntry> RecentLog() => [];
        public bool CanInstallUpdates => false;
        public UpdatePackageKind PackageKind => UpdatePackageKind.Installer;
        public bool Prepare(VerifiedPackage package) => false;
        public bool LaunchAndExit(VerifiedPackage package, UpdateInstallOptions options) => false;
    }

    private static (ShellViewModel Shell, MonitorContext Monitor, SettingsService Settings, Stubs Stubs) Create()
    {
        var (monitor, settings, _) = Fixture.Create();
        var stubs = new Stubs();
        var app = new AppVersionInfo(SemanticVersion.Parse("1.0.0"), "1.0.0", "owner/repo", null);
        var updates = new UpdatesViewModel(app, settings, stubs, stubs, new HttpClient(), Path.GetTempPath());
        var shell = new ShellViewModel(new ShellDependencies
        {
            Monitor = monitor,
            Settings = settings,
            Alerts = new AlertEngine(TimeSpan.FromMinutes(10)),
            Theme = stubs,
            Startup = stubs,
            Elevation = stubs,
            Shell = stubs,
            Lifetime = stubs,
            Diagnostics = stubs,
            Updates = updates,
            App = app,
        });
        return (shell, monitor, settings, stubs);
    }

    [Fact]
    public void AGameInFront_MakesMonitoringLighter_AndStopsACoveredWindowRedrawing()
    {
        var (shell, monitor, settings, _) = Create();
        var engine = monitor.Engine;
        Assert.Equal(TimeSpan.FromSeconds(1), engine.EffectiveInterval);
        Assert.True(shell.Overview.IsActive);

        shell.SetGameInFront(gameInFront: true, coversWindow: true);
        Assert.True(shell.IsLighterMonitoring);
        Assert.Equal(TimeSpan.FromSeconds(2), engine.EffectiveInterval);
        Assert.Equal(TimeSpan.FromSeconds(1), engine.Interval); // the chosen interval (and the history) stay as they are
        Assert.True(engine.LowPriority);
        Assert.False(shell.Overview.IsActive); // covered by the game: no charts to draw

        // With the overlay showing sensor readings, they still update every second.
        settings.Update("Overlay", s => s.Overlay.Enabled = true);
        Assert.Equal(TimeSpan.FromSeconds(1), engine.EffectiveInterval);
        settings.Update("Overlay", s => s.Overlay.Enabled = false);
        Assert.Equal(TimeSpan.FromSeconds(2), engine.EffectiveInterval);

        // A slower gaming interval, then lighter monitoring turned off.
        settings.Update("Monitoring", s => s.Monitoring.GamingIntervalMs = 5000);
        Assert.Equal(TimeSpan.FromSeconds(5), engine.EffectiveInterval);
        settings.Update("Monitoring", s => s.Monitoring.LighterWhileGaming = false);
        Assert.Equal(TimeSpan.FromSeconds(1), engine.EffectiveInterval);
        Assert.False(engine.LowPriority);
        Assert.False(shell.IsLighterMonitoring);

        shell.SetGameInFront(gameInFront: false, coversWindow: false);
        Assert.True(shell.Overview.IsActive);
        settings.Dispose();
    }

    [Fact]
    public void Construction_AppliesSettings_AndShowsOverview()
    {
        var (shell, _, settings, stubs) = Create();
        Assert.Same(shell.Overview, shell.CurrentPage);
        Assert.NotNull(stubs.Applied);
        Assert.Equal(10, shell.Pages.Count);
        Assert.Same(shell.Gaming, shell.Pages[shell.Pages.IndexOf(shell.Gpu) - 1]); // Gaming sits above Graphics
        Assert.Same(shell.History, shell.Pages[^1]);
        Assert.Same(shell.Optimize, shell.Pages[^2]); // Smart Optimize sits above Performance History
        Assert.Same(shell.Devices, shell.Pages[shell.Pages.IndexOf(shell.Storage) + 1]); // Devices sits below Storage
        Assert.Equal("Updates", shell.Settings.Sections[^1]); // Updates sits at the bottom of Settings, below About
        Assert.Equal("About", shell.Settings.Sections[^2]);
        settings.Dispose();
    }

    [Fact]
    public void DisablingSection_HidesPage_AndNavigatesAway()
    {
        var (shell, monitor, settings, _) = Create();
        shell.Navigate(shell.Gpu);
        shell.Settings.GpuEnabled = false;

        Assert.False(shell.Gpu.IsEnabled);
        Assert.Same(shell.Overview, shell.CurrentPage);
        Assert.False(monitor.Engine.IsSectionEnabled(MonitoringSection.Gpu));
        settings.Dispose();
    }

    [Fact]
    public void OnFrame_UpdatesStatusAndTrayText()
    {
        var (shell, monitor, settings, _) = Create();
        var frame = monitor.Engine.PollOnce();
        shell.OnFrame(frame);

        Assert.True(shell.IsLive);
        Assert.StartsWith("Live", shell.StatusText);
        Assert.Contains("CPU 12 %", shell.TrayText.Replace(" ", " "));
        Assert.Equal(frame.Timestamp, shell.Timeline.Now);
        settings.Dispose();
    }

    [Fact]
    public void PollingIntervalSetting_IsAppliedToEngine()
    {
        var (shell, monitor, settings, _) = Create();
        shell.Settings.PollingIntervalMs = 2000;
        Assert.Equal(TimeSpan.FromSeconds(2), monitor.Engine.Interval);
        Assert.Equal(2000, shell.Timeline.IntervalMilliseconds);
        settings.Dispose();
    }

    [Fact]
    public async Task StartupSync_RegistersTheSignInTask_WhenEnabled()
    {
        var (shell, _, settings, stubs) = Create();
        settings.Update("Desktop", s =>
        {
            s.Desktop.StartWithWindows = true;
            s.Desktop.LaunchMinimized = true;
        });

        await shell.Settings.ApplyStartupAsync();   // what the app does at every launch
        Assert.True(stubs.IsStartAtLogonRegistered);
        Assert.True(stubs.LastMinimized);
        string message = shell.Settings.StartupMessage ?? string.Empty;
        Assert.True(message.Contains("administrator", StringComparison.OrdinalIgnoreCase), message);

        shell.Settings.StartWithWindows = false;    // a user change removes it again
        await shell.Settings.ApplyStartupAsync();
        Assert.False(stubs.IsStartAtLogonRegistered);
        Assert.Null(shell.Settings.StartupMessage);
        settings.Dispose();
    }

    [Fact]
    public async Task StartupSync_ReportsAFailedTaskUpdate()
    {
        var (shell, _, settings, stubs) = Create();
        stubs.StartupResult = false;
        settings.Update("Desktop", s => s.Desktop.StartWithWindows = true);

        await shell.Settings.ApplyStartupAsync();
        Assert.False(stubs.IsStartAtLogonRegistered);
        Assert.Contains("Could not update", shell.Settings.StartupMessage);
        settings.Dispose();
    }

    [Fact]
    public void CustomAccent_AppliesValidHex_AndIgnoresPartialInput()
    {
        var (shell, _, settings, _) = Create();
        shell.Settings.CustomAccent = "45d6a0";
        Assert.Equal("#45D6A0", settings.Current.Appearance.Accent);

        shell.Settings.CustomAccent = "#12";
        Assert.Equal("#45D6A0", settings.Current.Appearance.Accent);
        Assert.Equal("#12", shell.Settings.CustomAccent);

        shell.Settings.SetAccentCommand.Execute("#AA86FF");
        Assert.Equal("#AA86FF", shell.Settings.CustomAccent);
        settings.Dispose();
    }

    [Fact]
    public void AlertRule_ComparisonIndex_UpdatesStoredRule()
    {
        var (shell, _, settings, _) = Create();
        var rule = shell.Settings.Rules[0];
        Assert.Equal(0, rule.ComparisonIndex);

        rule.ComparisonIndex = 1;
        Assert.False(rule.IsAbove);
        Assert.Equal(AlertComparison.Below, settings.Current.Alerts.Rules[0].Comparison);
        settings.Dispose();
    }

    [Fact]
    public void History_DefaultSelection_AndRemoveChart()
    {
        var (shell, monitor, settings, _) = Create();
        shell.Navigate(shell.History);
        shell.OnFrame(monitor.Engine.PollOnce());

        var history = shell.History;
        Assert.NotEmpty(history.Options);
        // CPU load, GPU load and GPU temperature have data; CPU temperature is unavailable in the fixture.
        Assert.Equal(3, history.Charts.Count);
        Assert.Equal("3 of 6 selected", history.SelectionText);

        var first = history.Charts[0];
        history.RemoveChartCommand.Execute(first);
        Assert.Equal(2, history.Charts.Count);
        Assert.False(first.Option.IsSelected);
        settings.Dispose();
    }
}
