using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Coreloom.Core.Benchmarks;
using Coreloom.Core.Formatting;
using Coreloom.ViewModels.Infrastructure;
using Coreloom.ViewModels.Services;

namespace Coreloom.ViewModels.Pages;

/// <summary>
/// A measured value on the four-color scale: number and unit, a detail line, a 0–1 bar or gauge fraction, its color
/// key ("Tone.Cool"…) and gauge tone (0 green … 3 red), and whether it is being measured right now.
/// </summary>
public sealed class ToneValueViewModel : ObservableObject
{
    private string _number = "—";
    private double _value = double.NaN;
    private string _unit;
    private string? _detail;
    private double _fraction;
    private double _tone = GaugeTone.Blue;
    private string _colorKey = ColorKeys.ToneNormal;
    private bool _hasValue;
    private bool _isActive;

    public ToneValueViewModel(string label, string unit, string iconKey = "Icon.Gauge")
    {
        Label = label;
        _unit = unit;
        IconKey = iconKey;
    }

    public string Label { get; }

    public string IconKey { get; }

    public string Number
    {
        get => _number;
        private set => SetProperty(ref _number, value);
    }

    /// <summary>The raw value (NaN when there is none), for numbers that count smoothly to each new reading.</summary>
    public double Value
    {
        get => _value;
        private set => SetProperty(ref _value, value);
    }

    /// <summary>Formats <see cref="Value"/> like <see cref="Number"/>.</summary>
    public Func<double, string> NumberFormatter { get; } = Format;

    public string Unit
    {
        get => _unit;
        private set => SetProperty(ref _unit, value);
    }

    public string? Detail
    {
        get => _detail;
        set => SetProperty(ref _detail, value);
    }

    public double Fraction
    {
        get => _fraction;
        private set => SetProperty(ref _fraction, value);
    }

    /// <summary>Position on the gauge color scale (0 green … 3 red).</summary>
    public double Tone
    {
        get => _tone;
        private set => SetProperty(ref _tone, value);
    }

    public string ColorKey
    {
        get => _colorKey;
        private set => SetProperty(ref _colorKey, value);
    }

    public bool HasValue
    {
        get => _hasValue;
        private set => SetProperty(ref _hasValue, value);
    }

    /// <summary>Being measured right now (the card glows).</summary>
    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    public void Set(double value, string number, ThresholdLevel level, double fraction, string? unit = null)
    {
        if (!double.IsFinite(value))
        {
            Clear();
            return;
        }

        Number = number;
        Value = value;
        if (unit is not null)
        {
            Unit = unit;
        }

        Fraction = Math.Clamp(fraction, 0, 1);
        Tone = SpeedRating.Tone(level);
        ColorKey = ColorKeys.ForLevel(level);
        HasValue = true;
    }

    public void Clear()
    {
        Number = "—";
        Value = double.NaN;
        Detail = null;
        Fraction = 0;
        Tone = GaugeTone.Blue;
        ColorKey = ColorKeys.ToneNormal;
        HasValue = false;
        IsActive = false;
    }

    /// <summary>"7,012", "512", "48.6", "3.25": fewer decimals for bigger numbers.</summary>
    public static string Format(double value) => value switch
    {
        >= 100 => value.ToString("N0", CultureInfo.CurrentCulture),
        >= 10 => value.ToString("N1", CultureInfo.CurrentCulture),
        _ => value.ToString("N2", CultureInfo.CurrentCulture),
    };
}

/// <summary>One row of the drive benchmark: a test pair (read and write) such as "SEQ1M Q8".</summary>
public sealed class DiskBenchmarkRowViewModel
{
    public DiskBenchmarkRowViewModel(DiskTest read, DiskTest write, string name, string description)
    {
        ReadTest = read;
        WriteTest = write;
        Name = name;
        Description = description;
        Read = new ToneValueViewModel("Read", "MB/s", "Icon.Download");
        Write = new ToneValueViewModel("Write", "MB/s", "Icon.Upload");
    }

    public DiskTest ReadTest { get; }

    public DiskTest WriteTest { get; }

    public string Name { get; }

    public string ShortName => ReadTest.ShortName;

    public string Description { get; }

    public ToneValueViewModel Read { get; }

    public ToneValueViewModel Write { get; }

    public ToneValueViewModel? CellFor(DiskTest test) => test.Id == ReadTest.Id ? Read : test.Id == WriteTest.Id ? Write : null;
}

public sealed record BenchmarkDriveOption(BenchmarkDrive Drive, string Text);

public sealed record BenchmarkSizeOption(long Bytes, string Text);

/// <summary>
/// The Storage page's drive benchmark: pick a drive and a test size, run, and watch the read and write gauges move
/// while sequential and random tests run; results land in the table, colored by how fast they are for that test.
/// </summary>
public sealed class StorageBenchmarkViewModel : ObservableObject
{
    private readonly IDiskBenchmarkService _service;
    private CancellationTokenSource? _cts;
    private int _run;
    private BenchmarkDriveOption? _selectedDrive;
    private BenchmarkSizeOption _selectedSize;
    private bool _isRunning;
    private double _progress;
    private string _status = "Measures how fast the drive reads and writes, using a temporary file that is deleted afterwards.";
    private string? _error;
    private readonly Dismissal _errorDismissal = new();
    private string? _summary;
    private bool _hasResult;
    private bool _drivesLoaded;

    public StorageBenchmarkViewModel(IDiskBenchmarkService service)
    {
        _service = service;
        Sizes = DiskBenchmark.Sizes.Select(b => new BenchmarkSizeOption(b, b >= 1L << 30 ? $"{b >> 30} GB" : $"{b >> 20} MB")).ToList();
        _selectedSize = Sizes.First(s => s.Bytes == DiskBenchmark.DefaultSize);
        Rows =
        [
            new(DiskTests.SequentialRead, DiskTests.SequentialWrite, "Sequential", "Large files: copying, installing, loading games"),
            new(DiskTests.RandomDeepRead, DiskTests.RandomDeepWrite, "Random, busy", "Many small reads and writes at once"),
            new(DiskTests.RandomRead, DiskTests.RandomWrite, "Random", "One small read or write at a time: how snappy the system feels"),
        ];
        RunCommand = new AsyncRelayCommand(RunAsync, () => !IsRunning && SelectedDrive is not null);
        CancelCommand = new RelayCommand(() => _cts?.Cancel(), () => IsRunning);
        RefreshDrivesCommand = new RelayCommand(RefreshDrives, () => !IsRunning);
    }

    public ObservableCollection<BenchmarkDriveOption> Drives { get; } = [];

    public IReadOnlyList<BenchmarkSizeOption> Sizes { get; }

    public IReadOnlyList<DiskBenchmarkRowViewModel> Rows { get; }

    /// <summary>The big read gauge: live speed while a read test runs, the sequential result afterwards.</summary>
    public ToneValueViewModel ReadGauge { get; } = new("Read", "MB/s", "Icon.Download");

    public ToneValueViewModel WriteGauge { get; } = new("Write", "MB/s", "Icon.Upload");

    public IAsyncRelayCommand RunCommand { get; }

    public IRelayCommand CancelCommand { get; }

    public IRelayCommand RefreshDrivesCommand { get; }

    public BenchmarkDriveOption? SelectedDrive
    {
        get => _selectedDrive;
        set
        {
            if (SetProperty(ref _selectedDrive, value))
            {
                RunCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public BenchmarkSizeOption SelectedSize
    {
        get => _selectedSize;
        set => SetProperty(ref _selectedSize, value ?? _selectedSize);
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetProperty(ref _isRunning, value))
            {
                OnPropertyChanged(nameof(IsIdle));
                RunCommand.NotifyCanExecuteChanged();
                CancelCommand.NotifyCanExecuteChanged();
                RefreshDrivesCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsIdle => !_isRunning;

    public double Progress
    {
        get => _progress;
        private set => SetProperty(ref _progress, value);
    }

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
            SetProperty(ref _error, value);
            _errorDismissal.Update(value is not null, () => Error = null);
        }
    }

    /// <summary>"C: · 1 GB test · finished in 31 s".</summary>
    public string? Summary
    {
        get => _summary;
        private set => SetProperty(ref _summary, value);
    }

    public bool HasResult
    {
        get => _hasResult;
        private set => SetProperty(ref _hasResult, value);
    }

    /// <summary>Lists the drives the first time the page opens (and when asked).</summary>
    public void EnsureDrives()
    {
        if (!_drivesLoaded)
        {
            RefreshDrives();
        }
    }

    public void RefreshDrives()
    {
        _drivesLoaded = true;
        string? selected = SelectedDrive?.Drive.Name;
        IReadOnlyList<BenchmarkDrive> drives;
        try
        {
            drives = _service.Drives();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            drives = [];
        }

        Drives.Clear();
        foreach (var drive in drives)
        {
            string label = string.IsNullOrEmpty(drive.Label) ? string.Empty : $" {drive.Label}";
            Drives.Add(new BenchmarkDriveOption(drive, $"{drive.Name}{label} · {FormatBytes(drive.FreeBytes)} free of {FormatBytes(drive.TotalBytes)}"));
        }

        SelectedDrive = Drives.FirstOrDefault(d => d.Drive.Name == selected) ?? Drives.FirstOrDefault();
    }

    private async Task RunAsync()
    {
        if (SelectedDrive is not { } drive)
        {
            return;
        }

        int run = ++_run;
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        Error = null;
        HasResult = false;
        Summary = null;
        Progress = 0;
        foreach (var row in Rows)
        {
            row.Read.Clear();
            row.Write.Clear();
        }

        ReadGauge.Clear();
        WriteGauge.Clear();
        IsRunning = true;
        Status = "Preparing the test file…";
        var progress = new ContextProgress<DiskBenchmarkProgress>(p => OnProgress(run, p));
        try
        {
            var options = new DiskBenchmarkOptions(drive.Drive.Folder, SelectedSize.Bytes);
            var result = await _service.RunAsync(options, progress, _cts.Token);
            _run++; // late progress reports are ignored from here on
            Show(result);
            Summary = $"{drive.Drive.Name} · {SelectedSize.Text} test · finished in {Math.Max(1, Math.Round(result.Elapsed.TotalSeconds)).ToString(CultureInfo.CurrentCulture)} s";
            Status = "Done. Results are for this drive with the computer as it is now; other activity lowers them.";
            HasResult = true;
        }
        catch (OperationCanceledException)
        {
            _run++;
            ResetLive();
            Status = "Cancelled. The test file was deleted.";
        }
        catch (DiskBenchmarkException e)
        {
            _run++;
            ResetLive();
            Error = e.Message;
            Status = "The test didn't run.";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _run++;
            ResetLive();
            Error = $"The test stopped: {e.Message}";
            Status = "The test didn't finish.";
        }
        finally
        {
            Progress = HasResult ? 1 : 0;
            IsRunning = false;
        }
    }

    private void OnProgress(int run, DiskBenchmarkProgress p)
    {
        if (run != _run || !IsRunning)
        {
            return;
        }

        Progress = Math.Max(Progress, p.Fraction);
        if (p.Test is not { } test)
        {
            Status = "Preparing the test file…";
            return;
        }

        double mbps = p.BytesPerSecond / 1_000_000;
        var level = SpeedRating.Disk(test, mbps);
        double fraction = SpeedRating.GaugeFraction(mbps, SpeedRating.DiskCeiling(test));
        string number = ToneValueViewModel.Format(mbps);
        var gauge = test.Write ? WriteGauge : ReadGauge;
        var other = test.Write ? ReadGauge : WriteGauge;
        gauge.IsActive = true;
        other.IsActive = false;
        gauge.Set(mbps, number, level, fraction);
        gauge.Detail = test.ShortName;

        foreach (var row in Rows)
        {
            foreach (var cell in new[] { row.Read, row.Write })
            {
                cell.IsActive = ReferenceEquals(cell, row.CellFor(test));
            }

            if (row.CellFor(test) is { } active && p.BytesPerSecond > 0)
            {
                active.Set(mbps, number, level, fraction);
            }
        }

        Status = $"{test.Name} ({test.ShortName})…";
    }

    private void Show(DiskBenchmarkResult result)
    {
        foreach (var row in Rows)
        {
            Fill(row.Read, result.Get(row.ReadTest));
            Fill(row.Write, result.Get(row.WriteTest));
        }

        Fill(ReadGauge, result.Get(DiskTests.SequentialRead));
        Fill(WriteGauge, result.Get(DiskTests.SequentialWrite));
        ReadGauge.Detail = "Sequential read";
        WriteGauge.Detail = "Sequential write";
    }

    private static void Fill(ToneValueViewModel cell, DiskTestResult? result)
    {
        cell.IsActive = false;
        if (result is null)
        {
            cell.Clear();
            return;
        }

        double mbps = result.MegabytesPerSecond;
        cell.Set(mbps, ToneValueViewModel.Format(mbps), SpeedRating.Disk(result.Test, mbps), SpeedRating.GaugeFraction(mbps, SpeedRating.DiskCeiling(result.Test)));
        cell.Detail = $"{result.Iops.ToString("N0", CultureInfo.CurrentCulture)} IOPS · {result.AverageLatencyMs.ToString(result.AverageLatencyMs < 10 ? "N2" : "N0", CultureInfo.CurrentCulture)} ms";
    }

    private void ResetLive()
    {
        foreach (var row in Rows)
        {
            row.Read.Clear();
            row.Write.Clear();
        }

        ReadGauge.Clear();
        WriteGauge.Clear();
    }

    private static string FormatBytes(long bytes) => bytes >= 1L << 40
        ? $"{(bytes / (double)(1L << 40)).ToString("0.#", CultureInfo.CurrentCulture)} TB"
        : $"{(bytes / (double)(1L << 30)).ToString("0", CultureInfo.CurrentCulture)} GB";
}

/// <summary>
/// The Network page's internet speed test: latency, then download and upload with a live gauge that glides as the
/// speed changes, rated on the four-color scale.
/// </summary>
public sealed class SpeedTestViewModel : ObservableObject
{
    private readonly ISpeedTestService _service;
    private CancellationTokenSource? _cts;
    private int _run;
    private bool _isRunning;
    private double _progress;
    private string _phase = "Ready";
    private string _status = "Tests your connection with Cloudflare's speed test servers. On a fast connection a test uses a lot of data (about 1 GB at 1 Gbit/s).";
    private string? _error;
    private readonly Dismissal _errorDismissal = new();
    private string? _server;
    private string? _finished;
    private bool _hasResult;
    private double _ceiling = 100;

    public SpeedTestViewModel(ISpeedTestService service)
    {
        _service = service;
        RunCommand = new AsyncRelayCommand(RunAsync, () => !IsRunning);
        CancelCommand = new RelayCommand(() => _cts?.Cancel(), () => IsRunning);
    }

    /// <summary>The big gauge: the live speed of the transfer running now, in Mbit/s.</summary>
    public ToneValueViewModel Live { get; } = new("Speed", "Mbps");

    public ToneValueViewModel Download { get; } = new("Download", "Mbps", "Icon.Download");

    public ToneValueViewModel Upload { get; } = new("Upload", "Mbps", "Icon.Upload");

    public ToneValueViewModel Ping { get; } = new("Ping", "ms", "Icon.Clock");

    public ToneValueViewModel Jitter { get; } = new("Jitter", "ms", "Icon.Network");

    public IReadOnlyList<ToneValueViewModel> Results => [Download, Upload, Ping, Jitter];

    public IAsyncRelayCommand RunCommand { get; }

    public IRelayCommand CancelCommand { get; }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetProperty(ref _isRunning, value))
            {
                OnPropertyChanged(nameof(IsIdle));
                OnPropertyChanged(nameof(RunText));
                RunCommand.NotifyCanExecuteChanged();
                CancelCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsIdle => !_isRunning;

    public string RunText => _hasResult ? "Test again" : "Start test";

    public double Progress
    {
        get => _progress;
        private set => SetProperty(ref _progress, value);
    }

    /// <summary>"Ping", "Download", "Upload", "Done".</summary>
    public string Phase
    {
        get => _phase;
        private set => SetProperty(ref _phase, value);
    }

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
            SetProperty(ref _error, value);
            _errorDismissal.Update(value is not null, () => Error = null);
        }
    }

    /// <summary>"Cloudflare · Lagos (LOS) · Example Telecom".</summary>
    public string? Server
    {
        get => _server;
        private set => SetProperty(ref _server, value);
    }

    /// <summary>"Tested at 14:32".</summary>
    public string? Finished
    {
        get => _finished;
        private set => SetProperty(ref _finished, value);
    }

    public bool HasResult
    {
        get => _hasResult;
        private set
        {
            if (SetProperty(ref _hasResult, value))
            {
                OnPropertyChanged(nameof(RunText));
            }
        }
    }

    private async Task RunAsync()
    {
        int run = ++_run;
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        Error = null;
        Progress = 0;
        _ceiling = 100;
        foreach (var value in Results)
        {
            value.Clear();
        }

        Live.Clear();
        Phase = "Connecting";
        Status = "Connecting to the test server…";
        IsRunning = true;
        var progress = new ContextProgress<SpeedTestProgress>(p => OnProgress(run, p));
        try
        {
            var result = await _service.RunAsync(progress, _cts.Token);
            _run++;
            Show(result);
            HasResult = true;
            Phase = "Done";
            Status = "Done. Speeds vary with the time of day, Wi-Fi signal and other devices using the connection.";
        }
        catch (OperationCanceledException)
        {
            _run++;
            Reset();
            Status = "Cancelled.";
        }
        catch (SpeedTestException e)
        {
            _run++;
            Reset();
            Error = e.Message;
            Status = "The test didn't finish.";
        }
        finally
        {
            Progress = HasResult ? 1 : 0;
            IsRunning = false;
        }
    }

    private void Reset()
    {
        foreach (var value in Results)
        {
            value.Clear();
        }

        Live.Clear();
        Phase = HasResult ? "Done" : "Ready";
    }

    private void OnProgress(int run, SpeedTestProgress p)
    {
        if (run != _run || !IsRunning)
        {
            return;
        }

        Progress = Math.Max(Progress, p.Fraction);
        if (p.PingMs is double ping && !Ping.HasValue)
        {
            SetLatency(ping, p.JitterMs ?? 0);
        }

        double mbps = p.BitsPerSecond / 1_000_000;
        switch (p.Phase)
        {
            case SpeedTestPhase.Latency:
                Phase = "Ping";
                Status = "Measuring latency…";
                Ping.IsActive = true;
                break;
            case SpeedTestPhase.Download:
            case SpeedTestPhase.Upload:
            {
                bool down = p.Phase == SpeedTestPhase.Download;
                Phase = down ? "Download" : "Upload";
                Status = down ? "Measuring download speed…" : "Measuring upload speed…";
                Ping.IsActive = false;
                var cell = down ? Download : Upload;
                Download.IsActive = down;
                Upload.IsActive = !down;
                if (mbps > 0)
                {
                    _ceiling = Math.Max(_ceiling, InternetRating.Ceiling(mbps));
                    var level = down ? InternetRating.Download(mbps) : InternetRating.Upload(mbps);
                    double fraction = SpeedRating.GaugeFraction(mbps, _ceiling);
                    string number = ToneValueViewModel.Format(mbps);
                    Live.Set(mbps, number, level, fraction);
                    Live.Detail = Phase;
                    cell.Set(mbps, number, level, fraction);
                }

                break;
            }
        }
    }

    private void SetLatency(double ping, double jitter)
    {
        Ping.IsActive = false;
        Ping.Set(ping, ToneValueViewModel.Format(ping), InternetRating.Ping(ping), 1 - SpeedRating.GaugeFraction(ping, 200));
        Jitter.Set(jitter, ToneValueViewModel.Format(jitter), SpeedRating.LowerIsBetter(jitter, 5, 15, 30), 1 - SpeedRating.GaugeFraction(jitter, 60));
    }

    private void Show(SpeedTestResult result)
    {
        SetLatency(result.PingMs, result.JitterMs);
        _ceiling = Math.Max(_ceiling, InternetRating.Ceiling(Math.Max(result.DownloadMbps, result.UploadMbps)));
        Download.IsActive = Upload.IsActive = false;
        Download.Set(result.DownloadMbps, ToneValueViewModel.Format(result.DownloadMbps), InternetRating.Download(result.DownloadMbps), SpeedRating.GaugeFraction(result.DownloadMbps, _ceiling));
        Upload.Set(result.UploadMbps, ToneValueViewModel.Format(result.UploadMbps), InternetRating.Upload(result.UploadMbps), SpeedRating.GaugeFraction(result.UploadMbps, _ceiling));
        Live.Set(result.DownloadMbps, Download.Number, InternetRating.Download(result.DownloadMbps), Download.Fraction);
        Live.Detail = "Download";
        Server = string.Join(" · ", new[] { result.Server.Provider, result.Server.Location, result.Server.Network }.Where(s => !string.IsNullOrWhiteSpace(s)));
        Finished = $"Tested at {result.Finished.ToString("t", CultureInfo.CurrentCulture)}";
    }
}
