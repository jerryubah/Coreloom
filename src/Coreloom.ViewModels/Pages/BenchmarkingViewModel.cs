using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Coreloom.Core;
using Coreloom.Core.Benchmarks;
using Coreloom.Core.Diagnostics;
using Coreloom.Core.Hardware;
using Coreloom.Core.Telemetry;
using Coreloom.ViewModels.Infrastructure;
using Coreloom.ViewModels.Services;

namespace Coreloom.ViewModels.Pages;

/// <summary>A live reading shown while a benchmark or stress test runs (clock, temperature, power).</summary>
public sealed class LiveReadingViewModel(string label, string iconKey, string colorKey, string? toolTip = null) : ObservableObject
{
    private string _value = "—";
    private string? _detail;

    public string Label { get; } = label;

    public string? ToolTip { get; } = toolTip;

    /// <summary>A line under the value ("14 min 25 s left").</summary>
    public string? Detail
    {
        get => _detail;
        set => SetProperty(ref _detail, value);
    }

    public string IconKey { get; } = iconKey;

    public string ColorKey { get; } = colorKey;

    public string Value
    {
        get => _value;
        set => SetProperty(ref _value, value);
    }
}

/// <summary>A part of a score, shown under the headline ("Single-core 2,345").</summary>
public sealed record ScorePartItem(string Label, string Value);

/// <summary>
/// The Processor or Graphics card card at the top of the page: the latest run's score and its parts (the best result is
/// marked in the results list, not here), a Run button, and — while its benchmark runs — the test it's on, progress and
/// live readings.
/// </summary>
public sealed class BenchmarkCardViewModel : ObservableObject
{
    private string _device = string.Empty;
    private string _score = "—";
    private bool _hasScore;
    private string _scoreDate = "No results yet";
    private IReadOnlyList<ScorePartItem> _parts = [];
    private bool _isRunning;
    private double _progress;
    private string _step = string.Empty;
    private string? _stepDetail;
    private bool _isAvailable = true;
    private string? _unavailableReason;

    public BenchmarkCardViewModel(BenchmarkKind kind, string title, string iconKey, string colorKey, string scoreLabel, string about, LiveReadingViewModel[] live)
    {
        Kind = kind;
        Title = title;
        IconKey = iconKey;
        ColorKey = colorKey;
        ScoreLabel = scoreLabel;
        About = about;
        Live = live;
    }

    public BenchmarkKind Kind { get; }

    public string Title { get; }

    public string IconKey { get; }

    public string ColorKey { get; }

    /// <summary>"Multi-core score" or "GPU score".</summary>
    public string ScoreLabel { get; }

    /// <summary>What the benchmark tests (the ⓘ tooltip).</summary>
    public string About { get; }

    public IReadOnlyList<LiveReadingViewModel> Live { get; }

    /// <summary>Runs this benchmark (set by the page).</summary>
    public IAsyncRelayCommand? RunCommand { get; internal set; }

    /// <summary>Stops it (set by the page).</summary>
    public IRelayCommand? CancelCommand { get; internal set; }

    public string Device
    {
        get => _device;
        set => SetProperty(ref _device, value);
    }

    /// <summary>The latest run's score ("—" before the first run).</summary>
    public string Score
    {
        get => _score;
        private set => SetProperty(ref _score, value);
    }

    public bool HasScore
    {
        get => _hasScore;
        private set => SetProperty(ref _hasScore, value);
    }

    /// <summary>"Latest run · 22 Oct 2026, 07:45" or "No results yet".</summary>
    public string ScoreDate
    {
        get => _scoreDate;
        private set => SetProperty(ref _scoreDate, value);
    }

    public IReadOnlyList<ScorePartItem> Parts
    {
        get => _parts;
        private set => SetProperty(ref _parts, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        set
        {
            if (SetProperty(ref _isRunning, value))
            {
                OnPropertyChanged(nameof(IsIdle));
            }
        }
    }

    public bool IsIdle => !_isRunning;

    public double Progress
    {
        get => _progress;
        set => SetProperty(ref _progress, value);
    }

    /// <summary>The test running ("Machine learning").</summary>
    public string Step
    {
        get => _step;
        set => SetProperty(ref _step, value);
    }

    /// <summary>"Multi-core · 32 threads".</summary>
    public string? StepDetail
    {
        get => _stepDetail;
        set => SetProperty(ref _stepDetail, value);
    }

    /// <summary>False when this benchmark can't run here (no graphics card Direct3D 11 can use).</summary>
    public bool IsAvailable
    {
        get => _isAvailable;
        set => SetProperty(ref _isAvailable, value);
    }

    public string? UnavailableReason
    {
        get => _unavailableReason;
        set => SetProperty(ref _unavailableReason, value);
    }

    /// <summary>Shows the latest run of this kind.</summary>
    public void Show(BenchmarkHistory history)
    {
        var latest = history.Latest(Kind);
        HasScore = latest is not null;
        Score = latest is null ? "—" : BenchmarkFormat.Score(latest.Score);
        ScoreDate = latest is null ? "No results yet" : $"Latest run · {BenchmarkFormat.DateTime(latest.Finished)}";
        Parts = latest?.Subscores.Select(s => new ScorePartItem(s.Name, BenchmarkFormat.Score(s.Score))).ToList() ?? [];
    }
}

/// <summary>A saved result in the list.</summary>
public sealed class BenchmarkResultViewModel : ObservableObject
{
    private bool _isBest;
    private double _fraction;

    public BenchmarkResultViewModel(BenchmarkResult result)
    {
        Result = result;
        (Title, IconKey, ColorKey) = result.Kind switch
        {
            BenchmarkKind.Cpu => ("CPU benchmark", "Icon.Cpu", ColorKeys.Cpu),
            BenchmarkKind.Gpu => ("GPU benchmark", "Icon.Gpu", ColorKeys.Gpu),
            _ => ($"Stress test · {result.Target}", "Icon.Flame", ColorKeys.Temperature),
        };
        When = BenchmarkFormat.DateTime(result.Finished);
        Score = result.IsStress ? BenchmarkFormat.Percent(result.Score) : BenchmarkFormat.Score(result.Score);
        ScoreLabel = result.Kind switch
        {
            BenchmarkKind.Cpu => "Multi-core",
            BenchmarkKind.Gpu => "GPU score",
            _ => "Stability",
        };
        Detail = result.IsStress
            ? $"Ran {BenchmarkFormat.Duration(result.Seconds)}{(result.Errors > 0 ? $" · {result.Errors} error{(result.Errors == 1 ? string.Empty : "s")}" : string.Empty)}"
            : string.Join(" · ", result.Subscores.Where(s => s.Name != ScoreLabel && s.Name != "Multi-core").Select(s => $"{s.Name} {BenchmarkFormat.Score(s.Score)}"));
        VerdictColorKey = !result.IsStress ? ColorKeys.Neutral : result.Passed ? ColorKeys.ToneCool : result.Errors > 0 || result.Verdict.StartsWith("Failed", StringComparison.Ordinal) ? ColorKeys.ToneHot : ColorKeys.ToneWarm;
        VerdictText = !result.IsStress ? string.Empty : result.Passed ? "Passed" : result.Verdict.Split(':')[0];
    }

    public BenchmarkResult Result { get; }

    public string Title { get; }

    public string IconKey { get; }

    public string ColorKey { get; }

    public string Device => Result.Device;

    public string When { get; }

    public string Score { get; }

    public string ScoreLabel { get; }

    /// <summary>"Single-core 2,345" / "Graphics 4,512 · Compute 3,980" / "15 min · Passed".</summary>
    public string Detail { get; }

    public bool IsStress => Result.IsStress;

    /// <summary>"Passed", "Failed", "Stopped" (stress tests).</summary>
    public string VerdictText { get; }

    public string VerdictColorKey { get; }

    public bool IsBest
    {
        get => _isBest;
        set => SetProperty(ref _isBest, value);
    }

    /// <summary>The score against the best of its kind (0–1), for the bar.</summary>
    public double Fraction
    {
        get => _fraction;
        set => SetProperty(ref _fraction, value);
    }
}

public sealed record BenchmarkFilterOption(string Key, string Label);

public sealed record DurationOption(TimeSpan? Duration, string Label);

public sealed record StressTargetOption(string Key, string Label, bool Processor, bool Graphics);

/// <summary>A test's row in a result's details.</summary>
public sealed record TestRowItem(string Name, string Category, string Rate, string Score, string SingleRate, string SingleScore, double Fraction);

/// <summary>A reading's row in a result's details (lowest, average, highest).</summary>
public sealed record ReadingRowItem(string Group, string Name, string Min, string Average, string Max);

/// <summary>An overclocking and tuning fact in a result's details; <paramref name="Label"/> names the part only when there are two.</summary>
public sealed record TuningRowItem(string Group, string Name, string Label, string Value, string Detail, bool Raised);

/// <summary>A stress test loop's bar (its work against the best loop).</summary>
public sealed record LoopBarItem(double Percent, string ToolTip, bool Low)
{
    /// <summary>The bar's height (0–1): 80 % and below is a stub, 100 % fills it, so small dips show.</summary>
    public double Fill => Math.Clamp((Percent - 80) / 20, 0.04, 1);
}

/// <summary>Everything about one saved result, shown in the details window.</summary>
public sealed class BenchmarkDetailsViewModel
{
    public BenchmarkDetailsViewModel(BenchmarkResultViewModel item, bool isBest, Core.Formatting.UnitFormatter f)
    {
        var r = item.Result;
        Item = item;
        IsBest = isBest;
        Parts = r.Subscores.Select(s => new ScorePartItem(s.Name, BenchmarkFormat.Score(s.Score))).ToList();
        double topScore = r.Tests.Count == 0 ? 0 : r.Tests.Max(t => t.Score);
        Tests = r.Tests.Select(t => new TestRowItem(
            t.Name,
            t.Category,
            $"{BenchmarkFormat.Rate(t.Rate)} {t.Unit}",
            BenchmarkFormat.Score(t.Score),
            t.SingleRate is double s ? $"{BenchmarkFormat.Rate(s)} {t.Unit}" : string.Empty,
            t.SingleScore is double ss ? BenchmarkFormat.Score(ss) : string.Empty,
            topScore > 0 ? t.Score / topScore : 0)).ToList();
        HasSingle = r.Tests.Any(t => t.SingleScore is not null);
        Readings = r.Readings.Select(x => new ReadingRowItem(x.Group, x.Name, Format(f, x, x.Min), Format(f, x, x.Average), Format(f, x, x.Max))).ToList();
        ShowGroups = r.Readings.Select(x => x.Group).Concat(r.Tuning.Select(t => t.Group)).Distinct().Count() > 1;
        Tuning = r.Tuning.Select(t => new TuningRowItem(t.Group, t.Name, ShowGroups ? $"{t.Name} · {t.Group}" : t.Name, t.Value, t.Detail, t.Raised)).ToList();
        var system = new List<KeyValueItem>();
        void Add(string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                system.Add(new KeyValueItem(key, value));
            }
        }

        Add("Processor", r.System.Processor);
        Add("Cores", r.System.Cores > 0 ? $"{r.System.Cores} cores, {r.System.Threads} threads" : null);
        Add("Graphics card", r.System.GraphicsCard);
        Add("Graphics driver", r.System.GraphicsDriver);
        Add("Graphics memory", r.System.GraphicsMemory > 0 ? f.Bytes(r.System.GraphicsMemory, 0).ToString() : null);
        Add("Memory", r.System.Memory > 0 ? $"{f.Bytes(r.System.Memory, 0)}{(string.IsNullOrEmpty(r.System.MemorySpeed) ? string.Empty : " · " + r.System.MemorySpeed)}" : null);
        bool mac = r.System.OperatingSystem.StartsWith("macOS", StringComparison.Ordinal);
        Add(mac ? "Model" : "Motherboard", r.System.Motherboard);
        Add(mac ? "Firmware" : "BIOS", r.System.Bios);
        Add(mac ? "macOS" : "Windows", r.System.OperatingSystem);
        Add("Coreloom", r.AppVersion);
        Add("Ran for", BenchmarkFormat.Duration(r.Seconds));
        if (r.MemoryBandwidth > 0)
        {
            Add("Memory bandwidth", $"{BenchmarkFormat.Rate(r.MemoryBandwidth)} GB/s (copying, every thread)");
            Add("Memory latency", $"{BenchmarkFormat.Rate(r.MemoryLatency)} ns (random reads)");
        }

        System = system;
        var relative = r.Loops;
        Loops = relative.Select((p, i) => new LoopBarItem(p, $"Loop {i + 1}: {BenchmarkFormat.Percent(p)} of the best loop", p < StressScoring.PassStability)).ToList();
    }

    public BenchmarkResultViewModel Item { get; }

    public BenchmarkResult Result => Item.Result;

    public bool IsBest { get; }

    public IReadOnlyList<ScorePartItem> Parts { get; }

    public IReadOnlyList<TestRowItem> Tests { get; }

    public bool HasTests => Tests.Count > 0;

    /// <summary>The CPU benchmark's tests have single-core columns too.</summary>
    public bool HasSingle { get; }

    /// <summary>The heading over the main results: "Multi-core" for the CPU benchmark's, "Result" otherwise.</summary>
    public string ResultHeading => HasSingle ? "Multi-core" : "Result";

    public IReadOnlyList<ReadingRowItem> Readings { get; }

    /// <summary>Readings from both the processor and the graphics card (a combined stress test): each row names its part.</summary>
    public bool ShowGroups { get; }

    public bool HasReadings => Readings.Count > 0;

    public IReadOnlyList<TuningRowItem> Tuning { get; }

    public bool HasTuning => Tuning.Count > 0;

    public IReadOnlyList<KeyValueItem> System { get; }

    public IReadOnlyList<LoopBarItem> Loops { get; }

    public bool HasLoops => Loops.Count > 0;

    public string Errors => Result.Errors == 1 ? "1 calculation error" : $"{Result.Errors.ToString(CultureInfo.CurrentCulture)} calculation errors";

    public string Note => Result.Note;

    public bool HasNote => !string.IsNullOrWhiteSpace(Result.Note);

    /// <summary>Percent levels as percentages, processor clocks in GHz (graphics clocks stay in MHz), the rest by kind.</summary>
    private static string Format(Core.Formatting.UnitFormatter f, BenchmarkReading reading, double value) => reading.Kind switch
    {
        MetricKind.Level => BenchmarkFormat.Percent(value),
        MetricKind.Clock when reading.Group == BenchmarkReadings.Processor => f.ClockGigahertz(value).ToString(),
        _ => f.Format(reading.Kind, value).ToString(),
    };
}

/// <summary>Number formats the Benchmarking page uses.</summary>
public static class BenchmarkFormat
{
    public static string Score(double score) => Math.Round(score).ToString("N0", CultureInfo.CurrentCulture);

    public static string Percent(double percent) => $"{percent.ToString(percent >= 99.95 ? "0" : "0.0", CultureInfo.CurrentCulture)} %";

    public static string Rate(double rate) => ToneValueViewModel.Format(rate);

    public static string Date(DateTimeOffset when) => when.ToLocalTime().ToString("d MMM yyyy", CultureInfo.CurrentCulture);

    public static string DateTime(DateTimeOffset when) => when.ToLocalTime().ToString("d MMM yyyy, HH:mm", CultureInfo.CurrentCulture);

    /// <summary>"45 s", "3 min 05 s", "1 h 02 min".</summary>
    public static string Duration(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, Math.Round(seconds)));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours} h {t.Minutes:00} min"
            : t.TotalMinutes >= 1 ? t.Seconds == 0 ? $"{(int)t.TotalMinutes} min" : $"{(int)t.TotalMinutes} min {t.Seconds:00} s"
            : $"{t.Seconds} s";
    }
}

/// <summary>
/// The Benchmarking page: CPU and GPU benchmarks scored on Coreloom's own scale (1,000 points per CPU test and 10,000 per
/// GPU test at a reference rate; overall scores are geometric means), saved results with a Best badge on each kind's highest
/// score, a details window per result (every test, the clocks, voltages, temperatures and power during the run, and
/// what they say about overclocking), and a stress test for the processor, the graphics card or both. Nothing here
/// changes clocks, voltages or limits.
/// </summary>
public sealed class BenchmarkingViewModel : PageViewModel
{
    private const string Processor = BenchmarkReadings.Processor;
    private const string Graphics = BenchmarkReadings.Graphics;

    private readonly BenchmarkHistory _history;
    private readonly string _appVersion;
    private CancellationTokenSource? _cts;
    private bool _isRunning;
    private bool _isStressRunning;
    private string _status = "Close other apps for the steadiest scores. Each benchmark takes about a minute.";
    private string? _error;
    private BenchmarkFilterOption _filter;
    private GpuAdapter? _selectedCard;
    private bool _cardsLoaded;
    private BenchmarkDetailsViewModel? _details;
    private bool _isDetailsOpen;
    private StressTargetOption _stressTarget;
    private DurationOption _stressDuration;
    private string _stressElapsed = "0 s";
    private string _stressRemaining = string.Empty;
    private double _stressProgress;
    private string _stressStability = "—";
    private string _stressErrors = "None";
    private string _stressStatus = "Runs your hardware flat out and checks every answer, the way OCCT and 3DMark do.";
    private string? _stressVerdict;
    private string _stressVerdictColorKey = ColorKeys.Neutral;
    private readonly LiveReadingViewModel _cpuTemperature, _cpuClock, _cpuPower, _gpuTemperature, _gpuClock, _gpuPower;
    private readonly LiveReadingViewModel _elapsedTile, _stabilityTile, _errorsTile;

    public BenchmarkingViewModel(PageContext context)
        : base("benchmarking", "Benchmarking", "Icon.Benchmark", context)
    {
        _appVersion = AppVersionInfo.FromAssembly(typeof(BenchmarkingViewModel).Assembly).Version.ToString();
        Cpu = new BenchmarkCardViewModel(
            BenchmarkKind.Cpu, "Processor", "Icon.Cpu", ColorKeys.Cpu, "Multi-core score",
            "Eight tests of everyday and demanding work — file compression, text processing, cryptography, navigation, ray tracing, "
            + "machine learning, photo filtering and physics — first on one core, then on every thread at once.",
            [new("Clock", "Icon.Gauge", ColorKeys.Cpu), new("Temperature", "Icon.Temperature", ColorKeys.Temperature), new("Power", "Icon.Power", ColorKeys.Power)]);
        Gpu = new BenchmarkCardViewModel(
            BenchmarkKind.Gpu, IsMac ? "Graphics" : "Graphics card", "Icon.Gpu", ColorKeys.Gpu, "GPU score",
            "Two graphics tests (64 moving lights at 1440p, a ray-marched 3D scene at 1080p) and three compute tests (raw FP32 "
            + $"arithmetic, memory bandwidth, an N-body physics step), drawn off-screen with {(IsMac ? "Metal" : "Direct3D 11")}.",
            [new("Clock", "Icon.Gauge", ColorKeys.Gpu), new("Temperature", "Icon.Temperature", ColorKeys.Temperature), new("Power", "Icon.Power", ColorKeys.Power)]);
        Filters =
        [
            new("all", "All"),
            new("cpu", "Processor"),
            new("gpu", IsMac ? "Graphics" : "Graphics card"),
            new("stress", "Stress tests"),
        ];
        _filter = Filters[0];
        StressTargets =
        [
            new("cpu", "Processor", true, false),
            new("gpu", IsMac ? "Graphics" : "Graphics card", false, true),
            new("both", "Both", true, true),
        ];
        _stressTarget = StressTargets[0];
        Durations =
        [
            new(TimeSpan.FromMinutes(1), "1 min"),
            new(TimeSpan.FromMinutes(5), "5 min"),
            new(TimeSpan.FromMinutes(15), "15 min"),
            new(TimeSpan.FromMinutes(30), "30 min"),
            new(TimeSpan.FromMinutes(60), "1 hour"),
            new(null, "Until stopped"),
        ];
        _stressDuration = Durations[2];
        _cpuTemperature = new("Processor", "Icon.Temperature", ColorKeys.Temperature);
        _cpuClock = new("Processor clock", "Icon.Gauge", ColorKeys.Cpu);
        _cpuPower = new("Processor power", "Icon.Power", ColorKeys.Power);
        _gpuTemperature = new("Graphics card", "Icon.Temperature", ColorKeys.Temperature);
        _gpuClock = new("Graphics clock", "Icon.Gauge", ColorKeys.Gpu);
        _gpuPower = new("Graphics power", "Icon.Power", ColorKeys.Power);
        _elapsedTile = new("Running for", "Icon.Clock", ColorKeys.Accent);
        _stabilityTile = new("Stability", "Icon.Pulse", ColorKeys.Accent, "The slowest loop as a percentage of the fastest. 97 % or more passes.");
        _errorsTile = new("Errors", "Icon.Check", ColorKeys.Accent, "Answers that came out different from the first time: an unstable overclock, undervolt or overheating.");
        ShowStressTiles(_stressTarget);

        RunCpuCommand = new AsyncRelayCommand(() => RunAsync(cpu: true, gpu: false), () => !IsBusy);
        RunGpuCommand = new AsyncRelayCommand(() => RunAsync(cpu: false, gpu: true), () => !IsBusy && Gpu.IsAvailable);
        RunBothCommand = new AsyncRelayCommand(() => RunAsync(cpu: true, gpu: true), () => !IsBusy && Gpu.IsAvailable);
        CancelCommand = new RelayCommand(() => _cts?.Cancel(), () => IsBusy);
        StartStressCommand = new AsyncRelayCommand(StressAsync, () => !IsBusy && (!StressTarget.Graphics || Gpu.IsAvailable));
        StopStressCommand = new RelayCommand(() => _cts?.Cancel(), () => IsStressRunning);
        SelectFilterCommand = new RelayCommand<BenchmarkFilterOption>(option => Filter = option ?? Filter);
        SelectStressTargetCommand = new RelayCommand<StressTargetOption>(option => StressTarget = option ?? StressTarget, _ => !IsBusy);
        SelectDurationCommand = new RelayCommand<DurationOption>(option => StressDuration = option ?? StressDuration, _ => !IsBusy);
        OpenDetailsCommand = new RelayCommand<BenchmarkResultViewModel>(Open);
        CloseDetailsCommand = new RelayCommand(() => IsDetailsOpen = false);
        DeleteCommand = new RelayCommand(Delete, () => Details is not null && !IsBusy);
        (Cpu.RunCommand, Cpu.CancelCommand) = (RunCpuCommand, CancelCommand);
        (Gpu.RunCommand, Gpu.CancelCommand) = (RunGpuCommand, CancelCommand);

        IReadOnlyList<BenchmarkResult> saved;
        try
        {
            saved = Service.LoadResults();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            saved = [];
        }

        _history = new BenchmarkHistory(saved);
        Cpu.Device = ShortCpuName(Monitor.Summary.CpuName);
        Refresh();
    }

    public BenchmarkCardViewModel Cpu { get; }

    public BenchmarkCardViewModel Gpu { get; }

    public ObservableCollection<GpuAdapter> GraphicsCards { get; } = [];

    public bool HasSeveralCards => GraphicsCards.Count > 1;

    public GpuAdapter? SelectedCard
    {
        get => _selectedCard;
        set
        {
            if (SetProperty(ref _selectedCard, value) && value is not null)
            {
                Gpu.Device = value.Name;
            }
        }
    }

    public IReadOnlyList<BenchmarkFilterOption> Filters { get; }

    public BenchmarkFilterOption Filter
    {
        get => _filter;
        set
        {
            if (SetProperty(ref _filter, value))
            {
                ShowResults();
            }
        }
    }

    /// <summary>Saved results (filtered), newest first.</summary>
    public ObservableCollection<BenchmarkResultViewModel> Results { get; } = [];

    public bool HasResults => Results.Count > 0;

    public string ResultsSummary { get; private set; } = string.Empty;

    public string EmptyText => _history.Results.Count == 0
        ? "No results yet. Run a benchmark or a stress test and it's saved here."
        : "No results of this kind yet.";

    public IAsyncRelayCommand RunCpuCommand { get; }

    public IAsyncRelayCommand RunGpuCommand { get; }

    public IAsyncRelayCommand RunBothCommand { get; }

    public IRelayCommand CancelCommand { get; }

    public IAsyncRelayCommand StartStressCommand { get; }

    public IRelayCommand StopStressCommand { get; }

    public IRelayCommand<BenchmarkFilterOption> SelectFilterCommand { get; }

    public IRelayCommand<StressTargetOption> SelectStressTargetCommand { get; }

    public IRelayCommand<DurationOption> SelectDurationCommand { get; }

    public IRelayCommand<BenchmarkResultViewModel> OpenDetailsCommand { get; }

    public IRelayCommand CloseDetailsCommand { get; }

    public IRelayCommand DeleteCommand { get; }

    /// <summary>A benchmark is running.</summary>
    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetProperty(ref _isRunning, value))
            {
                BusyChanged();
            }
        }
    }

    public bool IsStressRunning
    {
        get => _isStressRunning;
        private set
        {
            if (SetProperty(ref _isStressRunning, value))
            {
                OnPropertyChanged(nameof(IsStressIdle));
                BusyChanged();
            }
        }
    }

    public bool IsStressIdle => !_isStressRunning;

    /// <summary>A benchmark or a stress test is running (only one at a time).</summary>
    public bool IsBusy => _isRunning || _isStressRunning;

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public string? Error
    {
        get => _error;
        private set
        {
            if (SetProperty(ref _error, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrEmpty(_error);

    // ----- Stress test --------------------------------------------------------------------------------------------

    public IReadOnlyList<StressTargetOption> StressTargets { get; }

    public StressTargetOption StressTarget
    {
        get => _stressTarget;
        set
        {
            if (SetProperty(ref _stressTarget, value))
            {
                ShowStressTiles(value);
                StartStressCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public IReadOnlyList<DurationOption> Durations { get; }

    public DurationOption StressDuration
    {
        get => _stressDuration;
        set => SetProperty(ref _stressDuration, value);
    }

    /// <summary>The live readings of the parts being stress tested (temperatures, clocks, power).</summary>
    public IReadOnlyList<LiveReadingViewModel> StressLive { get; private set; } = [];

    /// <summary>Everything shown while a stress test runs: time, stability, errors, then the live readings.</summary>
    public ObservableCollection<LiveReadingViewModel> StressTiles { get; } = [];

    public ObservableCollection<LoopBarItem> StressLoops { get; } = [];

    public string StressElapsed
    {
        get => _stressElapsed;
        private set
        {
            SetProperty(ref _stressElapsed, value);
            _elapsedTile.Value = value;
        }
    }

    /// <summary>"12 min 30 s left" (empty when it runs until stopped).</summary>
    public string StressRemaining
    {
        get => _stressRemaining;
        private set
        {
            SetProperty(ref _stressRemaining, value);
            _elapsedTile.Detail = string.IsNullOrEmpty(value) ? null : value;
        }
    }

    public double StressProgress
    {
        get => _stressProgress;
        private set => SetProperty(ref _stressProgress, value);
    }

    public string StressStability
    {
        get => _stressStability;
        private set
        {
            SetProperty(ref _stressStability, value);
            _stabilityTile.Value = value;
        }
    }

    public string StressErrors
    {
        get => _stressErrors;
        private set
        {
            SetProperty(ref _stressErrors, value);
            _errorsTile.Value = value;
        }
    }

    public string StressStatus
    {
        get => _stressStatus;
        private set => SetProperty(ref _stressStatus, value);
    }

    /// <summary>The last stress test's verdict ("Passed · 99.2 % stability"), null before one has run.</summary>
    public string? StressVerdict
    {
        get => _stressVerdict;
        private set
        {
            if (SetProperty(ref _stressVerdict, value))
            {
                OnPropertyChanged(nameof(HasStressVerdict));
            }
        }
    }

    public bool HasStressVerdict => _stressVerdict is not null;

    public string StressVerdictColorKey
    {
        get => _stressVerdictColorKey;
        private set => SetProperty(ref _stressVerdictColorKey, value);
    }

    public string StressSafety =>
        $"Stops on the first calculation error, or if the processor stays at {Formatter.Temperature(StressGuard.ProcessorLimit)} or the graphics card at "
        + $"{Formatter.Temperature(StressGuard.GraphicsLimit)} for {StressGuard.HoldSeconds} seconds. Passing needs no errors and {StressScoring.PassStability:0} % stability.";

    // ----- Details ------------------------------------------------------------------------------------------------

    public BenchmarkDetailsViewModel? Details
    {
        get => _details;
        private set
        {
            if (SetProperty(ref _details, value))
            {
                DeleteCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsDetailsOpen
    {
        get => _isDetailsOpen;
        set => SetProperty(ref _isDetailsOpen, value);
    }

    /// <summary>How often readings are taken while something runs (shorter in tests).</summary>
    internal TimeSpan Tick { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>A stress test length to use instead of the chosen one (tests).</summary>
    internal TimeSpan? DurationOverride { get; set; }

    /// <summary>Loop length to use instead of the standard one (tests).</summary>
    internal double? LoopSecondsOverride { get; set; }

    /// <summary>CPU benchmark options (shorter in tests).</summary>
    internal CpuBenchmarkOptions CpuOptions { get; set; } = new();

    private IHardwareBenchmarkService Service => Context.Tools.Benchmarks;

    protected override void OnUpdate(TelemetryFrame frame)
    {
    }

    protected override void OnActivated()
    {
        if (!_cardsLoaded)
        {
            LoadCards();
        }
    }

    /// <summary>Lists the graphics cards (the first time the page opens).</summary>
    public void LoadCards()
    {
        _cardsLoaded = true;
        GraphicsCards.Clear();
        if (Service.GpuSupported)
        {
            try
            {
                foreach (var card in Service.GraphicsCards())
                {
                    GraphicsCards.Add(card);
                }
            }
            catch (Exception e) when (e is InvalidOperationException or DllNotFoundException or EntryPointNotFoundException)
            {
                Log.Warn("Benchmarks", "Couldn't list the graphics cards for the GPU benchmark.", e);
            }
        }

        // Prefer the card the Graphics page shows (by name), else the one with the most memory.
        string? shown = Monitor.Gpus().FirstOrDefault(g => g.Slot == Context.GpuSlot).Name;
        SelectedCard = GraphicsCards.FirstOrDefault(c => SameCard(c.Name, shown)) ?? GraphicsCards.FirstOrDefault();
        Gpu.IsAvailable = SelectedCard is not null;
        Gpu.UnavailableReason = Gpu.IsAvailable ? null
            : IsMac ? (Service.GpuSupported ? "macOS doesn't list a GPU that supports Metal." : "The GPU benchmark needs Metal.")
            : Service.GpuSupported ? "Windows doesn't list a graphics card that supports Direct3D 11 compute shaders."
            : "The GPU benchmark needs Windows and Direct3D 11.";
        if (!Gpu.IsAvailable)
        {
            Gpu.Device = shown ?? "No graphics card found";
        }

        OnPropertyChanged(nameof(HasSeveralCards));
        RunGpuCommand.NotifyCanExecuteChanged();
        RunBothCommand.NotifyCanExecuteChanged();
        StartStressCommand.NotifyCanExecuteChanged();
    }

    // ----- Benchmarks ---------------------------------------------------------------------------------------------

    private async Task RunAsync(bool cpu, bool gpu)
    {
        if (IsBusy)
        {
            return;
        }

        if (!_cardsLoaded)
        {
            LoadCards();
        }

        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        Error = null;
        IsRunning = true;
        try
        {
            if (cpu)
            {
                await RunCpuAsync(token);
            }

            if (gpu && Gpu.IsAvailable)
            {
                await RunGpuAsync(token);
            }

            Status = "Saved. Scores are on Coreloom's own scale: compare them with your other runs, not with other benchmark apps.";
        }
        catch (OperationCanceledException)
        {
            Status = "Cancelled. Nothing was saved for the benchmark that was running.";
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Log.Warn("Benchmarks", "A benchmark failed.", e);
            Error = $"The benchmark stopped: {e.Message}";
            Status = "The benchmark didn't finish.";
        }
        finally
        {
            foreach (var card in new[] { Cpu, Gpu })
            {
                card.IsRunning = false;
                card.Progress = 0;
                card.Step = string.Empty;
                card.StepDetail = null;
                foreach (var live in card.Live)
                {
                    live.Value = "—";
                }
            }

            IsRunning = false;
        }
    }

    private async Task RunCpuAsync(CancellationToken token)
    {
        Cpu.IsRunning = true;
        Cpu.Step = "Starting";
        Status = "Running the CPU benchmark. Leave the PC alone for the steadiest score.";
        var readings = new ReadingRecorder();
        var watts = new List<double>();
        bool fromPpt = false;
        var clock = Stopwatch.StartNew();
        var progress = new ContextProgress<BenchmarkProgress>(p =>
        {
            if (Cpu.IsRunning)
            {
                Cpu.Progress = Math.Max(Cpu.Progress, p.Fraction);
                Cpu.Step = p.Step;
                Cpu.StepDetail = p.Detail;
            }
        });

        using var sampling = CancellationTokenSource.CreateLinkedTokenSource(token);
        var sampler = SampleAsync(frame =>
        {
            var (w, ppt) = BenchmarkReadings.RecordProcessor(readings, frame);
            if (double.IsFinite(w))
            {
                watts.Add(w);
                fromPpt |= ppt;
            }

            ShowProcessorLive(Cpu.Live, frame);
        }, sampling.Token);

        CpuBenchmarkScores scores;
        try
        {
            scores = await Service.RunCpuAsync(CpuOptions, progress, token);
        }
        finally
        {
            sampling.Cancel();
            await sampler;
        }

        var amd = HardwareVendors.Detect(null, Monitor.Summary.CpuName) == HardwareVendor.Amd ? AmdProcessors.Create(Monitor.Summary.CpuName) : null;
        var result = new BenchmarkResult
        {
            Kind = BenchmarkKind.Cpu,
            Finished = DateTimeOffset.Now,
            AppVersion = _appVersion,
            PointsAtReference = BenchmarkScale.Cpu,
            Score = scores.MultiScore,
            Subscores = [new("Single-core", scores.SingleScore), new("Multi-core", scores.MultiScore)],
            Device = Cpu.Device,
            Tests = scores.Tests.Select(t => new BenchmarkTestResult(t.Id, t.Name, t.Category, t.Unit, t.MultiRate, t.MultiScore, t.SingleRate, t.SingleScore)).ToList(),
            Readings = readings.Readings(),
            Tuning = BenchmarkTuning.Processor(readings, amd, watts, fromPpt, Formatter),
            System = Snapshot(),
            Seconds = clock.Elapsed.TotalSeconds,
            Threads = scores.Threads,
            MemoryBandwidth = scores.MemoryBandwidthGBs,
            MemoryLatency = scores.MemoryLatencyNs,
        };
        Add(result);
        Cpu.IsRunning = false;
    }

    private async Task RunGpuAsync(CancellationToken token)
    {
        Gpu.IsRunning = true;
        Gpu.Step = "Starting";
        Status = $"Running the GPU benchmark on {Gpu.Device}. Leave the PC alone for the steadiest score.";
        var card = SelectedCard;
        int slot = GpuSlotFor(card?.Name);
        var readings = new ReadingRecorder();
        ulong reasons = 0;
        var clock = Stopwatch.StartNew();
        var progress = new ContextProgress<BenchmarkProgress>(p =>
        {
            if (Gpu.IsRunning)
            {
                Gpu.Progress = Math.Max(Gpu.Progress, p.Fraction);
                Gpu.Step = p.Step;
                Gpu.StepDetail = p.Detail;
            }
        });

        using var sampling = CancellationTokenSource.CreateLinkedTokenSource(token);
        var sampler = SampleAsync(frame =>
        {
            reasons |= BenchmarkReadings.RecordGraphics(readings, frame, slot);
            ShowGraphicsLive(Gpu.Live, frame, slot);
        }, sampling.Token);

        GpuBenchmarkScores scores;
        try
        {
            scores = await Service.RunGpuAsync(card, progress, token);
        }
        finally
        {
            sampling.Cancel();
            await sampler;
        }

        var device = Devices.Get(MetricIds.GpuDeviceId(slot));
        var result = new BenchmarkResult
        {
            Kind = BenchmarkKind.Gpu,
            Finished = DateTimeOffset.Now,
            AppVersion = _appVersion,
            PointsAtReference = BenchmarkScale.Gpu,
            Score = scores.Score,
            Subscores = [new("Graphics", scores.Graphics), new("Compute", scores.Compute)],
            Device = scores.Adapter.Name,
            Tests = [.. scores.Tests],
            Readings = readings.Readings(),
            Tuning = BenchmarkTuning.Graphics(readings, device, reasons, Formatter),
            System = Snapshot(scores.Adapter, device),
            Seconds = clock.Elapsed.TotalSeconds,
        };
        Add(result);
        Gpu.IsRunning = false;
    }

    // ----- Stress test --------------------------------------------------------------------------------------------

    private async Task StressAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (!_cardsLoaded)
        {
            LoadCards();
        }

        var target = StressTarget;
        TimeSpan? duration = DurationOverride ?? StressDuration.Duration;
        double loopSeconds = LoopSecondsOverride ?? StressScoring.LoopSeconds(duration);
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        Error = null;
        StressVerdict = null;
        StressLoops.Clear();
        StressStability = "—";
        StressErrors = "None";
        StressProgress = 0;
        IsStressRunning = true;

        var loads = new List<(IStressLoad Load, StressLoops Loops)>();
        int slot = GpuSlotFor(SelectedCard?.Name);
        var readings = new ReadingRecorder();
        var watts = new List<double>();
        bool fromPpt = false;
        ulong reasons = 0;
        var guard = new StressGuard();
        var reason = StressStopReason.None;
        string? failure = null;
        var clock = Stopwatch.StartNew();
        try
        {
            if (target.Processor)
            {
                loads.Add((Service.ProcessorStress(Cpu.Device), new StressLoops(loopSeconds)));
            }

            if (target.Graphics && Service.GraphicsStress(SelectedCard) is { } graphics)
            {
                loads.Add((graphics, new StressLoops(loopSeconds)));
            }

            foreach (var (load, _) in loads)
            {
                load.Start();
            }

            StressStatus = $"Stress testing the {string.Join(" and the ", loads.Select(l => l.Load.Name.ToLowerInvariant()))}. Leave the PC alone: other work slows the test and counts against its stability.";
            while (reason == StressStopReason.None)
            {
                try
                {
                    await Task.Delay(Tick, token);
                }
                catch (OperationCanceledException)
                {
                    reason = StressStopReason.Cancelled;
                    break;
                }

                double seconds = clock.Elapsed.TotalSeconds;
                var frame = Monitor.Engine.LatestFrame;
                double cpuTemp = double.NaN, gpuTemp = double.NaN;
                if (target.Processor)
                {
                    var (w, ppt) = BenchmarkReadings.RecordProcessor(readings, frame);
                    if (double.IsFinite(w))
                    {
                        watts.Add(w);
                        fromPpt |= ppt;
                    }

                    cpuTemp = frame.GetValue(MetricIds.CpuTemperature) ?? double.NaN;
                }

                if (target.Graphics)
                {
                    reasons |= BenchmarkReadings.RecordGraphics(readings, frame, slot);
                    gpuTemp = frame.GetValue(MetricIds.GpuTemperature(slot)) ?? double.NaN;
                }

                ShowStressLive(frame, slot);
                bool newLoop = false;
                foreach (var (load, loops) in loads)
                {
                    newLoop |= loops.Add(seconds, load.Work);
                }

                if (newLoop)
                {
                    ShowLoops(loads);
                }

                // The failure first, then the count: a load counts an error before it describes it, so a failure seen
                // here with no errors counted is a load that stopped working (a driver reset), not a wrong answer.
                string? broken = loads.Select(l => l.Load.Failure).FirstOrDefault(f => f is not null);
                int errors = loads.Sum(l => l.Load.Errors);
                StressErrors = errors == 0 ? "None" : errors == 1 ? "1 error" : $"{errors} errors";
                StressElapsed = BenchmarkFormat.Duration(seconds);
                if (duration is { } d)
                {
                    StressProgress = Math.Clamp(seconds / d.TotalSeconds, 0, 1);
                    StressRemaining = $"{BenchmarkFormat.Duration(Math.Max(0, d.TotalSeconds - seconds))} left";
                }
                else
                {
                    StressRemaining = string.Empty;
                }

                if (errors > 0)
                {
                    reason = StressStopReason.Error;
                    failure = broken ?? loads.Select(l => l.Load.Failure).FirstOrDefault(f => f is not null);
                }
                else if (broken is not null)
                {
                    reason = StressStopReason.Failed;
                    failure = broken;
                }
                else if (guard.Check(cpuTemp, gpuTemp) is var hot && hot != StressStopReason.None)
                {
                    reason = hot;
                }
                else if (duration is { } limit && seconds >= limit.TotalSeconds)
                {
                    reason = StressStopReason.Finished;
                }
            }
        }
        finally
        {
            foreach (var (load, _) in loads)
            {
                load.Stop();
                load.Dispose();
            }
        }

        try
        {
            Finish(target, loads, reason, failure, clock.Elapsed.TotalSeconds, readings, watts, fromPpt, slot, reasons);
        }
        finally
        {
            foreach (var live in StressLive)
            {
                live.Value = "—";
            }

            StressRemaining = string.Empty;
            IsStressRunning = false;
        }
    }

    private void Finish(StressTargetOption target, List<(IStressLoad Load, StressLoops Loops)> loads, StressStopReason reason, string? failure, double seconds, ReadingRecorder readings, List<double> watts, bool fromPpt, int slot, ulong reasons)
    {
        int errors = loads.Sum(l => l.Load.Errors);
        var relative = Combined(loads);
        double stability = loads.Count == 0 ? 0 : loads.Min(l => StressScoring.Stability(l.Loops.Loops));
        bool passed = loads.Count > 0 && loads.All(l => StressScoring.Passed(reason, l.Load.Errors, l.Loops.Loops));
        string verdict = reason switch
        {
            _ when passed => "Passed",
            StressStopReason.Finished => $"Failed: stability {BenchmarkFormat.Percent(stability)} is under {StressScoring.PassStability:0} % (it slowed down during the test, often from heat or power limits)",
            StressStopReason.Error => $"Failed: {failure ?? "a calculation error"}",
            StressStopReason.Failed => $"Failed: {failure}",
            StressStopReason.ProcessorTooHot => $"Stopped: the processor stayed at {Formatter.Temperature(StressGuard.ProcessorLimit)} or hotter",
            StressStopReason.GraphicsTooHot => $"Stopped: the graphics card stayed at {Formatter.Temperature(StressGuard.GraphicsLimit)} or hotter",
            _ => "Stopped early",
        };

        StressVerdict = passed ? $"Passed · {BenchmarkFormat.Percent(stability)} stability" : verdict;
        StressVerdictColorKey = passed ? ColorKeys.ToneCool : reason is StressStopReason.Error or StressStopReason.Failed or StressStopReason.Finished ? ColorKeys.ToneHot : ColorKeys.ToneWarm;
        bool anyLoop = loads.Any(l => l.Loops.Loops.Count > 0);
        if (!anyLoop && errors == 0 && reason is StressStopReason.Cancelled)
        {
            StressStatus = "Stopped before the first loop finished, so nothing was saved.";
            return;
        }

        var tuning = new List<BenchmarkTuningNote>();
        var amd = HardwareVendors.Detect(null, Monitor.Summary.CpuName) == HardwareVendor.Amd ? AmdProcessors.Create(Monitor.Summary.CpuName) : null;
        if (target.Processor)
        {
            tuning.AddRange(BenchmarkTuning.Processor(readings, amd, watts, fromPpt, Formatter));
        }

        var device = target.Graphics ? Devices.Get(MetricIds.GpuDeviceId(slot)) : null;
        if (target.Graphics)
        {
            tuning.AddRange(BenchmarkTuning.Graphics(readings, device, reasons, Formatter));
        }

        var graphicsLoad = loads.FirstOrDefault(l => l.Load.Name == "Graphics card").Load;
        var result = new BenchmarkResult
        {
            Kind = BenchmarkKind.Stress,
            Finished = DateTimeOffset.Now,
            AppVersion = _appVersion,
            Score = Math.Round(stability, 1),
            Device = string.Join(" + ", loads.Select(l => l.Load.Device)),
            Target = target.Key switch { "cpu" => "Processor", "gpu" => "Graphics card", _ => "Processor and graphics card" },
            Readings = readings.Readings(),
            Tuning = tuning,
            System = target.Graphics && SelectedCard is { } card ? Snapshot(card, device) : Snapshot(),
            Seconds = seconds,
            Threads = target.Processor ? Environment.ProcessorCount : 0,
            Loops = relative,
            Errors = errors,
            Passed = passed,
            Verdict = verdict,
            Note = graphicsLoad is null && target.Graphics ? "The graphics card part couldn't run here." : string.Empty,
        };
        Add(result);
        StressStatus = passed
            ? "Passed: no calculation errors, and the hardware held its speed. Saved below."
            : "Saved below. Open it for the temperatures, clocks and power during the test.";
    }

    /// <summary>Each loop's work against the best loop; with both loads, the lower of the two.</summary>
    private static List<double> Combined(List<(IStressLoad Load, StressLoops Loops)> loads)
    {
        var lists = loads.Select(l => StressScoring.Relative(l.Loops.Loops)).Where(l => l.Count > 0).ToList();
        if (lists.Count == 0)
        {
            return [];
        }

        int count = lists.Min(l => l.Count);
        return Enumerable.Range(0, count).Select(i => lists.Min(l => l[i])).ToList();
    }

    private void ShowLoops(List<(IStressLoad Load, StressLoops Loops)> loads)
    {
        var relative = Combined(loads);
        StressLoops.Clear();
        foreach (var (p, i) in relative.Select((p, i) => (p, i)))
        {
            StressLoops.Add(new LoopBarItem(p, $"Loop {i + 1}: {BenchmarkFormat.Percent(p)} of the best loop", p < StressScoring.PassStability));
        }

        double stability = loads.Min(l => StressScoring.Stability(l.Loops.Loops));
        StressStability = loads.All(l => l.Loops.Loops.Count >= 2) ? BenchmarkFormat.Percent(stability) : "—";
    }

    // ----- Results ------------------------------------------------------------------------------------------------

    private void Add(BenchmarkResult result)
    {
        _history.Add(result);
        Save();
        Refresh();
    }

    private void Delete()
    {
        if (Details?.Result is not { } result || IsBusy)
        {
            return;
        }

        _history.Remove(result.Id);
        Save();
        IsDetailsOpen = false;
        Details = null;
        Refresh();
    }

    private void Save()
    {
        try
        {
            Service.SaveResults(_history.Results);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Benchmarks", "Couldn't save the benchmark results.", e);
            Error = $"The result couldn't be saved: {e.Message}";
        }
    }

    private void Refresh()
    {
        Cpu.Show(_history);
        Gpu.Show(_history);
        ShowResults();
    }

    private void ShowResults()
    {
        Results.Clear();
        foreach (var result in _history.Results.Where(r => Filter.Key switch
        {
            "cpu" => r.Kind == BenchmarkKind.Cpu,
            "gpu" => r.Kind == BenchmarkKind.Gpu,
            "stress" => r.Kind == BenchmarkKind.Stress,
            _ => true,
        }))
        {
            Results.Add(new BenchmarkResultViewModel(result)
            {
                IsBest = _history.IsBest(result),
                Fraction = result.IsStress ? result.Score / 100 : _history.FractionOfBest(result),
            });
        }

        int total = _history.Results.Count;
        ResultsSummary = total == 0 ? string.Empty : total == 1 ? "1 saved result" : $"{total.ToString(CultureInfo.CurrentCulture)} saved results";
        OnPropertyChanged(nameof(ResultsSummary));
        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(EmptyText));
    }

    private void Open(BenchmarkResultViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        Details = new BenchmarkDetailsViewModel(item, _history.IsBest(item.Result), Formatter);
        IsDetailsOpen = true;
    }

    // ----- Readings -----------------------------------------------------------------------------------------------

    /// <summary>Takes the latest telemetry frame every tick until cancelled (each new frame once).</summary>
    private async Task SampleAsync(Action<TelemetryFrame> take, CancellationToken token)
    {
        long last = -1;
        while (true)
        {
            var frame = Monitor.Engine.LatestFrame;
            if (frame.Sequence != last && frame.Sequence > 0)
            {
                last = frame.Sequence;
                take(frame);
            }

            try
            {
                await Task.Delay(Tick, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private void ShowProcessorLive(IReadOnlyList<LiveReadingViewModel> live, TelemetryFrame frame)
    {
        double clock = frame.GetValue(MetricIds.CpuClockEffectiveAverage) ?? frame.GetValue(MetricIds.CpuClockAverage) ?? double.NaN;
        live[0].Value = double.IsFinite(clock) ? Formatter.ClockGigahertz(clock).ToString() : "—";
        live[1].Value = frame.GetValue(MetricIds.CpuTemperature) is double t ? Formatter.Temperature(t).ToString() : "—";
        live[2].Value = frame.GetValue(MetricIds.CpuPower) is double p ? Formatter.Power(p).ToString() : "—";
    }

    private void ShowGraphicsLive(IReadOnlyList<LiveReadingViewModel> live, TelemetryFrame frame, int slot)
    {
        live[0].Value = frame.GetValue(MetricIds.GpuCoreClock(slot)) is double c ? Formatter.Clock(c).ToString() : "—";
        live[1].Value = frame.GetValue(MetricIds.GpuTemperature(slot)) is double t ? Formatter.Temperature(t).ToString() : "—";
        live[2].Value = frame.GetValue(MetricIds.GpuPower(slot)) is double p ? Formatter.Power(p).ToString() : "—";
    }

    private void ShowStressLive(TelemetryFrame frame, int slot)
    {
        _cpuTemperature.Value = frame.GetValue(MetricIds.CpuTemperature) is double ct ? Formatter.Temperature(ct).ToString() : "—";
        double clock = frame.GetValue(MetricIds.CpuClockEffectiveAverage) ?? frame.GetValue(MetricIds.CpuClockAverage) ?? double.NaN;
        _cpuClock.Value = double.IsFinite(clock) ? Formatter.ClockGigahertz(clock).ToString() : "—";
        _cpuPower.Value = frame.GetValue(MetricIds.CpuPower) is double cp ? Formatter.Power(cp).ToString() : "—";
        _gpuTemperature.Value = frame.GetValue(MetricIds.GpuTemperature(slot)) is double gt ? Formatter.Temperature(gt).ToString() : "—";
        _gpuClock.Value = frame.GetValue(MetricIds.GpuCoreClock(slot)) is double gc ? Formatter.Clock(gc).ToString() : "—";
        _gpuPower.Value = frame.GetValue(MetricIds.GpuPower(slot)) is double gp ? Formatter.Power(gp).ToString() : "—";
    }

    /// <summary>The tiles for a stress test of <paramref name="target"/>: its parts' readings only.</summary>
    private void ShowStressTiles(StressTargetOption target)
    {
        StressLive = target switch
        {
            { Processor: true, Graphics: true } => [_cpuTemperature, _cpuClock, _gpuTemperature, _gpuClock],
            { Graphics: true } => [_gpuTemperature, _gpuClock, _gpuPower],
            _ => [_cpuTemperature, _cpuClock, _cpuPower],
        };
        StressTiles.Clear();
        foreach (var tile in new[] { _elapsedTile, _stabilityTile, _errorsTile }.Concat(StressLive))
        {
            StressTiles.Add(tile);
        }

        OnPropertyChanged(nameof(StressLive));
    }

    /// <summary>The monitoring slot of the graphics card the benchmark runs on (matched by name).</summary>
    private int GpuSlotFor(string? name)
    {
        foreach (var (slot, gpuName, _) in Monitor.Gpus())
        {
            if (SameCard(gpuName, name))
            {
                return slot;
            }
        }

        return Context.GpuSlot;
    }

    private static bool SameCard(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
        {
            return false;
        }

        string x = Normalize(a), y = Normalize(b);
        return x == y || x.Contains(y, StringComparison.Ordinal) || y.Contains(x, StringComparison.Ordinal);

        static string Normalize(string s) => s.Replace("(R)", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("(TM)", string.Empty, StringComparison.OrdinalIgnoreCase).Trim().ToUpperInvariant();
    }

    private BenchmarkSystem Snapshot(GpuAdapter? card = null, DeviceInfo? gpu = null)
    {
        var summary = Monitor.Summary;
        var module = summary.MemoryModules.FirstOrDefault();
        string? speed = module is null ? null : MemorySpeed(module);
        return new BenchmarkSystem
        {
            Processor = ShortCpuName(summary.CpuName),
            Cores = summary.PhysicalCores,
            Threads = summary.LogicalProcessors,
            GraphicsCard = card?.Name ?? gpu?.Name ?? string.Empty,
            GraphicsDriver = gpu?.GetProperty("Driver") ?? string.Empty,
            GraphicsMemory = card?.Memory ?? 0,
            Memory = summary.InstalledMemoryBytes,
            MemorySpeed = speed ?? string.Empty,
            Motherboard = summary.Motherboard ?? string.Empty,
            Bios = summary.Bios ?? string.Empty,
            OperatingSystem = string.IsNullOrEmpty(summary.OsVersion) ? summary.OperatingSystem : $"{summary.OperatingSystem} {summary.OsVersion}",
        };
    }

    private static string? MemorySpeed(MemoryModuleInfo module) =>
        module.ConfiguredSpeedMtps is int mts and > 0 ? $"{mts.ToString(CultureInfo.CurrentCulture)} MT/s" : null;

    private void BusyChanged()
    {
        OnPropertyChanged(nameof(IsBusy));
        RunCpuCommand.NotifyCanExecuteChanged();
        RunGpuCommand.NotifyCanExecuteChanged();
        RunBothCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        StartStressCommand.NotifyCanExecuteChanged();
        StopStressCommand.NotifyCanExecuteChanged();
        SelectStressTargetCommand.NotifyCanExecuteChanged();
        SelectDurationCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
    }
}
