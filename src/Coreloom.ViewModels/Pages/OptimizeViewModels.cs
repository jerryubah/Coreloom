using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Coreloom.Core.Formatting;
using Coreloom.Core.Optimize;
using Coreloom.Core.Telemetry;
using Coreloom.ViewModels.Infrastructure;
using Coreloom.ViewModels.Services;

namespace Coreloom.ViewModels.Pages;

/// <summary>Where Smart Scan is: not started, scanning, showing what it found, cleaning up, finished.</summary>
public enum OptimizeStage
{
    Idle,
    Scanning,
    Ready,
    Running,
    Done,
}

/// <summary>Shared helpers for Smart Optimize's texts.</summary>
internal static class OptimizeText
{
    public static string Ago(DateTime? when, DateTime now)
    {
        if (when is not { } at)
        {
            return "never";
        }

        var age = now - at;
        if (age < TimeSpan.FromMinutes(2))
        {
            return "just now";
        }

        if (age < TimeSpan.FromHours(1))
        {
            return $"{(int)age.TotalMinutes} minutes ago";
        }

        if (now.Date == at.Date)
        {
            return $"today at {at.ToString("t", CultureInfo.CurrentCulture)}";
        }

        int days = (now.Date - at.Date).Days;
        return days == 1 ? "yesterday" : $"{days} days ago";
    }

    public static string Count(int n, string one, string many) => n == 1 ? $"1 {one}" : $"{n.ToString("N0", CultureInfo.CurrentCulture)} {many}";
}

/// <summary>One of the four summary tiles in the Smart Scan hero (Cleanup, Protection, Memory, Speed).</summary>
public sealed class OptimizeModuleViewModel : ObservableObject
{
    private string _status = "Not scanned yet";
    private string? _detail;
    private string _colorKey = ColorKeys.Neutral;
    private bool _isWorking;
    private bool _isIncluded = true;
    private bool _canInclude;
    private bool _isDone;

    public OptimizeModuleViewModel(string id, string title, string iconKey)
    {
        Id = id;
        Title = title;
        IconKey = iconKey;
    }

    public string Id { get; }

    public string Title { get; }

    public string IconKey { get; }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public string? Detail
    {
        get => _detail;
        set => SetProperty(ref _detail, value);
    }

    /// <summary>"Tone.Cool" (all good) … "Tone.Hot" (needs attention), "Neutral" before scanning.</summary>
    public string ColorKey
    {
        get => _colorKey;
        set => SetProperty(ref _colorKey, value);
    }

    public bool IsWorking
    {
        get => _isWorking;
        set => SetProperty(ref _isWorking, value);
    }

    /// <summary>Part of the clean-up run (the user can leave a module out).</summary>
    public bool IsIncluded
    {
        get => _isIncluded;
        set => SetProperty(ref _isIncluded, value);
    }

    /// <summary>There is something to do for this module.</summary>
    public bool CanInclude
    {
        get => _canInclude;
        set => SetProperty(ref _canInclude, value);
    }

    /// <summary>Finished in the last run (shows a check).</summary>
    public bool IsDone
    {
        get => _isDone;
        set => SetProperty(ref _isDone, value);
    }

    public void Reset()
    {
        Status = "Not scanned yet";
        Detail = null;
        ColorKey = ColorKeys.Neutral;
        IsWorking = false;
        IsDone = false;
        CanInclude = false;
    }
}

/// <summary>A kind of junk with its size, a checkbox, and what cleaning it freed.</summary>
public sealed class JunkCategoryViewModel : ObservableObject
{
    private readonly Action _selectionChanged;
    private bool _isSelected;
    private JunkSize _size;
    private bool _isMeasured;
    private bool _isWorking;
    private string? _result;
    private string _sizeText = "—";

    public JunkCategoryViewModel(JunkCategory category, Action selectionChanged)
    {
        Category = category;
        _isSelected = category.SelectedByDefault;
        _selectionChanged = selectionChanged;
    }

    public JunkCategory Category { get; }

    public string Name => Category.Name;

    public string Description => Category.Description;

    public string? Caution => Category.Caution;

    public string IconKey => Category.IconKey;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                _selectionChanged();
            }
        }
    }

    public JunkSize Size => _size;

    public long Bytes => _size.Bytes;

    public bool IsMeasured
    {
        get => _isMeasured;
        private set => SetProperty(ref _isMeasured, value);
    }

    public bool HasJunk => _isMeasured && _size.Bytes > 0;

    /// <summary>"1.2 GB · 3,412 files", "Nothing to clean", or "—" before scanning.</summary>
    public string SizeText
    {
        get => _sizeText;
        private set => SetProperty(ref _sizeText, value);
    }

    public bool IsWorking
    {
        get => _isWorking;
        set => SetProperty(ref _isWorking, value);
    }

    /// <summary>"Freed 1.1 GB" after cleaning (with how many files were in use).</summary>
    public string? Result
    {
        get => _result;
        set => SetProperty(ref _result, value);
    }

    public void SetSize(JunkSize size, Func<long, string> formatBytes)
    {
        _size = size;
        IsMeasured = true;
        SizeText = size.Bytes <= 0 ? "Nothing to clean"
            : size.Files > 0 ? $"{formatBytes(size.Bytes)} · {OptimizeText.Count(size.Files, "file", "files")}"
            : formatBytes(size.Bytes);
        OnPropertyChanged(nameof(Size));
        OnPropertyChanged(nameof(Bytes));
        OnPropertyChanged(nameof(HasJunk));
    }
}

/// <summary>The Cleanup card: junk categories to measure and clean.</summary>
public sealed class CleanupViewModel : ObservableObject
{
    private readonly IOptimizeService _service;
    private readonly Func<long, string> _formatBytes;
    private bool _isBusy;
    private double _progress;
    private string _status = "Scan to see how much space junk files take up.";
    private string? _lastResult;
    private CancellationTokenSource? _cts;

    public CleanupViewModel(IOptimizeService service, Func<long, string> formatBytes)
    {
        _service = service;
        _formatBytes = formatBytes;
        ScanCommand = new AsyncRelayCommand(() => Guarded(ct => ScanAsync(ct)), () => !IsBusy && Categories.Count > 0);
        CleanCommand = new AsyncRelayCommand(() => Guarded(async ct => { await CleanAsync(ct); }), () => !IsBusy && SelectedBytes > 0);
        CancelCommand = new RelayCommand(() => _cts?.Cancel(), () => IsBusy);
    }

    public ObservableCollection<JunkCategoryViewModel> Categories { get; } = [];

    public IAsyncRelayCommand ScanCommand { get; }

    public IAsyncRelayCommand CleanCommand { get; }

    public IRelayCommand CancelCommand { get; }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                ScanCommand.NotifyCanExecuteChanged();
                CleanCommand.NotifyCanExecuteChanged();
                CancelCommand.NotifyCanExecuteChanged();
            }
        }
    }

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

    public string? LastResult
    {
        get => _lastResult;
        private set => SetProperty(ref _lastResult, value);
    }

    public bool IsMeasured => Categories.Count > 0 && Categories.All(c => c.IsMeasured);

    public long TotalBytes => Categories.Sum(c => c.Bytes);

    public long SelectedBytes => Categories.Where(c => c.IsSelected).Sum(c => c.Bytes);

    /// <summary>"2.4 GB selected of 3.1 GB found".</summary>
    public string SelectedText => !IsMeasured ? "Not scanned yet"
        : TotalBytes <= 0 ? "No junk found"
        : $"{_formatBytes(SelectedBytes)} selected of {_formatBytes(TotalBytes)} found";

    public void Load()
    {
        if (Categories.Count > 0)
        {
            return;
        }

        foreach (var category in _service.JunkCategories())
        {
            Categories.Add(new JunkCategoryViewModel(category, SelectionChanged));
        }

        ScanCommand.NotifyCanExecuteChanged();
        OnTotalsChanged();
    }

    /// <summary>Measures every category (Smart Scan calls this too). <paramref name="report"/> gets 0–1.</summary>
    public async Task ScanAsync(CancellationToken cancellationToken, Action<double>? report = null)
    {
        Load();
        bool owner = !IsBusy;
        IsBusy = true;
        Status = "Looking for junk…";
        LastResult = null;
        try
        {
            for (int i = 0; i < Categories.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = Categories[i];
                item.IsWorking = true;
                item.Result = null;
                var category = item.Category;
                var size = category.Kind == JunkKind.RecycleBin
                    ? await Task.Run(_service.RecycleBinSize, cancellationToken)
                    : await Task.Run(() => JunkCleaner.Measure(category, cancellationToken), cancellationToken);
                item.SetSize(size, _formatBytes);
                item.IsWorking = false;
                Progress = (i + 1.0) / Categories.Count;
                report?.Invoke(Progress);
                OnTotalsChanged();
            }

            Status = TotalBytes > 0 ? $"Found {_formatBytes(TotalBytes)} of junk." : "No junk found. Nice and tidy.";
        }
        finally
        {
            foreach (var item in Categories)
            {
                item.IsWorking = false;
            }

            if (owner)
            {
                IsBusy = false;
            }
        }
    }

    /// <summary>Cleans the selected categories and measures them again.</summary>
    public async Task<CleanResult> CleanAsync(CancellationToken cancellationToken, Action<double>? report = null)
    {
        bool owner = !IsBusy;
        IsBusy = true;
        var total = default(CleanResult);
        var selected = Categories.Where(c => c.IsSelected && c.HasJunk).ToList();
        Status = "Cleaning…";
        try
        {
            for (int i = 0; i < selected.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = selected[i];
                item.IsWorking = true;
                var category = item.Category;
                CleanResult result;
                if (category.Kind == JunkKind.RecycleBin)
                {
                    long before = item.Bytes;
                    bool emptied = await Task.Run(_service.EmptyRecycleBin, cancellationToken);
                    result = emptied ? new CleanResult(before, item.Size.Files, 0) : new CleanResult(0, 0, 1);
                    item.SetSize(await Task.Run(_service.RecycleBinSize, cancellationToken), _formatBytes);
                }
                else
                {
                    result = await Task.Run(() => JunkCleaner.Clean(category, cancellationToken), cancellationToken);
                    item.SetSize(await Task.Run(() => JunkCleaner.Measure(category, cancellationToken), cancellationToken), _formatBytes);
                }

                item.Result = result.FreedBytes > 0
                    ? $"Freed {_formatBytes(result.FreedBytes)}{(result.Skipped > 0 ? $" · {OptimizeText.Count(result.Skipped, "file", "files")} in use, left alone" : string.Empty)}"
                    : result.Skipped > 0 ? "Files are in use; try again after closing apps" : "Cleaned";
                item.IsWorking = false;
                total += result;
                Progress = (i + 1.0) / Math.Max(1, selected.Count);
                report?.Invoke(Progress);
                OnTotalsChanged();
            }

            LastResult = total.FreedBytes > 0 ? $"Freed {_formatBytes(total.FreedBytes)}" : "Nothing was freed";
            Status = total.FreedBytes > 0
                ? $"Done. Freed {_formatBytes(total.FreedBytes)} of disk space."
                : "Done. Nothing could be freed; the files may be in use.";
            return total;
        }
        finally
        {
            foreach (var item in Categories)
            {
                item.IsWorking = false;
            }

            if (owner)
            {
                IsBusy = false;
            }
        }
    }

    private async Task Guarded(Func<CancellationToken, Task> action)
    {
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        try
        {
            await action(_cts.Token);
        }
        catch (OperationCanceledException)
        {
            Status = "Stopped.";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Status = $"Something went wrong: {e.Message}";
        }
    }

    private void SelectionChanged() => OnTotalsChanged();

    private void OnTotalsChanged()
    {
        OnPropertyChanged(nameof(IsMeasured));
        OnPropertyChanged(nameof(TotalBytes));
        OnPropertyChanged(nameof(SelectedBytes));
        OnPropertyChanged(nameof(SelectedText));
        CleanCommand.NotifyCanExecuteChanged();
    }
}

public sealed class ThreatViewModel(ThreatInfo threat)
{
    public string Name => threat.Name;

    public string Severity => threat.SeverityText;

    public string? Resource => threat.Resource;

    public string ColorKey => threat.Severity >= 4 ? ColorKeys.ToneHot : ColorKeys.ToneWarm;
}

/// <summary>The Protection card: Microsoft Defender's state, scans, definitions and threats.</summary>
public sealed class ProtectionViewModel : ObservableObject
{
    private readonly IOptimizeService _service;
    private readonly Func<DateTime> _now;
    private ProtectionStatus? _current;
    private bool _isBusy;
    private bool _isScanning;
    private string _title = "Checking…";
    private string _summary = "Reading Windows Security…";
    private string _colorKey = ColorKeys.Neutral;
    private string? _message;
    private string _realTime = "—";
    private string _definitions = "—";
    private string _lastScan = "—";
    private string _engine = "Microsoft Defender";
    private CancellationTokenSource? _cts;
    private readonly bool _mac;

    public ProtectionViewModel(IOptimizeService service, Func<DateTime> now, bool mac = false)
    {
        _service = service;
        _now = now;
        _mac = mac;
        if (mac)
        {
            _summary = "Reading macOS's security settings…";
            _engine = "XProtect";
        }

        RefreshCommand = new AsyncRelayCommand(() => RefreshAsync(CancellationToken.None), () => !IsBusy);
        QuickScanCommand = new AsyncRelayCommand(QuickScanAsync, () => !IsBusy && CanUseDefender);
        UpdateCommand = new AsyncRelayCommand(UpdateAsync, () => !IsBusy && CanUseDefender);
        RemoveThreatsCommand = new AsyncRelayCommand(() => RemoveThreatsAsync(CancellationToken.None), () => !IsBusy && HasThreats);
        CancelScanCommand = new RelayCommand(() => _cts?.Cancel(), () => IsScanning);
        OpenSecurityCommand = new RelayCommand(_service.OpenSecurity);
    }

    public ObservableCollection<ThreatViewModel> Threats { get; } = [];

    /// <summary>macOS: each built-in protection and whether it is on.</summary>
    public ObservableCollection<SecurityCheck> Checks { get; } = [];

    public bool IsMac => _mac;

    public IAsyncRelayCommand RefreshCommand { get; }

    public IAsyncRelayCommand QuickScanCommand { get; }

    public IAsyncRelayCommand UpdateCommand { get; }

    public IAsyncRelayCommand RemoveThreatsCommand { get; }

    public IRelayCommand CancelScanCommand { get; }

    public IRelayCommand OpenSecurityCommand { get; }

    public ProtectionStatus? Current => _current;

    public bool HasThreats => Threats.Count > 0;

    public bool CanUseDefender => _current?.IsDefenderInCharge == true;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                NotifyCommands();
            }
        }
    }

    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (SetProperty(ref _isScanning, value))
            {
                CancelScanCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>"Protected", "Threats found", "Not protected", "Checking…".</summary>
    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    public string Summary
    {
        get => _summary;
        private set => SetProperty(ref _summary, value);
    }

    public string ColorKey
    {
        get => _colorKey;
        private set => SetProperty(ref _colorKey, value);
    }

    /// <summary>The outcome of the last action ("Definitions updated", "No threats found").</summary>
    public string? Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    public string RealTime
    {
        get => _realTime;
        private set => SetProperty(ref _realTime, value);
    }

    public string Definitions
    {
        get => _definitions;
        private set => SetProperty(ref _definitions, value);
    }

    public string LastScan
    {
        get => _lastScan;
        private set => SetProperty(ref _lastScan, value);
    }

    /// <summary>"Microsoft Defender" or the antivirus in charge.</summary>
    public string Engine
    {
        get => _engine;
        private set => SetProperty(ref _engine, value);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        bool owner = !IsBusy;
        IsBusy = true;
        try
        {
            Apply(await StatusAsync(cancellationToken));
        }
        finally
        {
            if (owner)
            {
                IsBusy = false;
            }
        }
    }

    public async Task<int> RemoveThreatsAsync(CancellationToken cancellationToken)
    {
        bool owner = !IsBusy;
        IsBusy = true;
        int before = Threats.Count;
        Message = "Removing threats…";
        try
        {
            var result = await Safely(() => _service.RemoveThreatsAsync(cancellationToken));
            Apply(await StatusAsync(cancellationToken));
            Message = result.Succeeded ? (HasThreats ? "Some threats remain. Open Windows Security to review them." : "Threats removed.") : result.Message;
            return Math.Max(0, before - Threats.Count);
        }
        finally
        {
            if (owner)
            {
                IsBusy = false;
            }
        }
    }

    private async Task QuickScanAsync()
    {
        IsBusy = true;
        IsScanning = true;
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        Message = "Running a quick scan with Microsoft Defender. This usually takes a few minutes…";
        try
        {
            var outcome = await _service.QuickScanAsync(_cts.Token);
            Apply(await StatusAsync(CancellationToken.None));
            Message = !outcome.Completed ? outcome.Problem ?? "The scan didn't finish."
                : HasThreats ? $"Scan finished: {OptimizeText.Count(Threats.Count, "threat", "threats")} found."
                : "Scan finished: no threats found.";
        }
        catch (OperationCanceledException)
        {
            Message = "Scan cancelled.";
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Message = $"The scan couldn't run: {e.Message}";
        }
        finally
        {
            IsScanning = false;
            IsBusy = false;
        }
    }

    private async Task UpdateAsync()
    {
        IsBusy = true;
        Message = "Updating virus definitions…";
        try
        {
            var result = await Safely(() => _service.UpdateSignaturesAsync(CancellationToken.None));
            Apply(await StatusAsync(CancellationToken.None));
            Message = result.Succeeded ? "Virus definitions are up to date." : result.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>The status, or why it couldn't be read; never throws (except when cancelled).</summary>
    private async Task<ProtectionStatus> StatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _service.ProtectionAsync(cancellationToken);
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return ProtectionStatus.Unavailable(_mac ? $"macOS's security settings couldn't be read ({e.Message})." : $"Windows Security couldn't be read ({e.Message}).");
        }
    }

    private static async Task<MaintenanceResult> Safely(Func<Task<MaintenanceResult>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return new MaintenanceResult(false, e.Message);
        }
    }

    private void Apply(ProtectionStatus status)
    {
        if (_mac)
        {
            ApplyMac(status);
            return;
        }

        _current = status;
        var now = _now();
        Threats.Clear();
        foreach (var threat in status.Threats.Where(t => t.Active))
        {
            Threats.Add(new ThreatViewModel(threat));
        }

        Engine = status.IsDefenderInCharge || status.OtherProduct is null ? "Microsoft Defender" : status.OtherProduct;
        if (status.Problem is not null && !status.DefenderAvailable && status.OtherProduct is null)
        {
            Title = "Status unavailable";
            Summary = status.Problem;
            ColorKey = ColorKeys.ToneWarm;
            RealTime = Definitions = LastScan = "—";
        }
        else if (!status.IsDefenderInCharge)
        {
            Title = status.IsProtected ? "Protected" : "Not protected";
            Summary = status.IsProtected ? $"{status.OtherProduct} is protecting this PC." : "No antivirus is turned on.";
            ColorKey = status.IsProtected ? ColorKeys.ToneCool : ColorKeys.ToneHot;
            RealTime = status.IsProtected ? $"On ({status.OtherProduct})" : "Off";
            Definitions = LastScan = "Managed by " + (status.OtherProduct ?? "your antivirus");
        }
        else
        {
            bool outdated = status.SignaturesOutdated(now);
            bool overdue = status.ScanOverdue(now);
            Title = HasThreats ? "Threats found" : status.RealTimeProtection ? "Protected" : "Not protected";
            Summary = HasThreats ? $"{OptimizeText.Count(Threats.Count, "threat needs", "threats need")} your attention."
                : !status.RealTimeProtection ? "Real-time protection is off. Turn it on in Windows Security."
                : outdated ? "Protected, but the virus definitions are out of date."
                : overdue ? "Protected. A quick scan is a good idea: the last one was a while ago."
                : "Real-time protection is on and everything is up to date.";
            ColorKey = HasThreats || !status.RealTimeProtection ? ColorKeys.ToneHot
                : outdated || overdue ? ColorKeys.ToneWarm
                : ColorKeys.ToneCool;
            RealTime = status.RealTimeProtection ? "On" : "Off";
            Definitions = status.SignatureVersion is { } version
                ? $"{version} · updated {OptimizeText.Ago(status.SignaturesUpdated, now)}"
                : $"Updated {OptimizeText.Ago(status.SignaturesUpdated, now)}";
            LastScan = ProtectionStatus.Latest(status.LastQuickScan, status.LastFullScan) is { } scanned ? OptimizeText.Ago(scanned, now) : "never";
        }

        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(HasThreats));
        OnPropertyChanged(nameof(CanUseDefender));
        NotifyCommands();
    }

    /// <summary>
    /// macOS protects itself: XProtect scans what's opened and is updated by Apple, Gatekeeper checks apps before they
    /// run, System Integrity Protection guards the system. The card reports them (and FileVault and the firewall) and
    /// opens System Settings for changes; there is nothing to scan or update by hand.
    /// </summary>
    private void ApplyMac(ProtectionStatus status)
    {
        _current = status;
        var now = _now();
        Threats.Clear();
        Checks.Clear();
        foreach (var check in status.Checks)
        {
            Checks.Add(check);
        }

        Engine = "XProtect";
        var off = status.Checks.Where(c => !c.IsOn).ToList();
        var essentialOff = off.Where(c => c.Essential).ToList();
        if (status.Checks.Count == 0)
        {
            Title = "Status unavailable";
            Summary = status.Problem ?? "macOS's security settings couldn't be read.";
            ColorKey = ColorKeys.ToneWarm;
        }
        else if (essentialOff.Count > 0)
        {
            Title = "Not fully protected";
            Summary = $"{string.Join(" and ", essentialOff.Select(c => c.Name))} {(essentialOff.Count == 1 ? "is" : "are")} off. Turn {(essentialOff.Count == 1 ? "it" : "them")} back on to keep the Mac safe.";
            ColorKey = ColorKeys.ToneHot;
        }
        else if (off.Count > 0)
        {
            Title = "Protected";
            Summary = $"macOS's protections are on. {string.Join(" and ", off.Select(c => c.Name))} {(off.Count == 1 ? "is" : "are")} off: worth turning on in System Settings.";
            ColorKey = ColorKeys.ToneWarm;
        }
        else
        {
            Title = "Protected";
            Summary = "XProtect, Gatekeeper and System Integrity Protection are on, the disk is encrypted and the firewall is up.";
            ColorKey = ColorKeys.ToneCool;
        }

        RealTime = status.RealTimeProtection ? "On (built into macOS)" : "Off";
        Definitions = status.SignatureVersion is { } version
            ? status.SignaturesUpdated is { } updated ? $"{version} · updated {OptimizeText.Ago(updated, now)}" : version
            : "Updated by macOS";
        LastScan = "Automatic (macOS scans apps and downloads as they open)";
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(HasThreats));
        OnPropertyChanged(nameof(CanUseDefender));
        NotifyCommands();
    }

    private void NotifyCommands()
    {
        RefreshCommand.NotifyCanExecuteChanged();
        QuickScanCommand.NotifyCanExecuteChanged();
        UpdateCommand.NotifyCanExecuteChanged();
        RemoveThreatsCommand.NotifyCanExecuteChanged();
    }
}

/// <summary>The Memory card: RAM use on the four-color ring and "Free up RAM".</summary>
/// <summary>A process in the memory results: its PID, memory before and after, what was reclaimed and what happened.</summary>
public sealed class MemoryTrimRowViewModel(MemoryTrimEntry entry, Func<long, string> formatBytes)
{
    public MemoryTrimEntry Entry { get; } = entry;

    public string Name => Entry.FileName;

    public string PidText { get; } = "PID " + entry.ProcessId.ToString(CultureInfo.CurrentCulture);

    public string BeforeText { get; } = entry.Before is { } b ? formatBytes(b) : "—";

    public string AfterText { get; } = entry.After is { } a ? formatBytes(a) : "—";

    public string ReclaimedText { get; } = entry.Outcome == TrimOutcome.Trimmed ? formatBytes(entry.Reclaimed) : "—";

    /// <summary>"Trimmed", "Skipped" or "Failed" (also the key for its color).</summary>
    public string OutcomeText { get; } = entry.Outcome.ToString();

    public string? Reason => Entry.Reason;
}

public sealed class MemoryOptimizeViewModel : ObservableObject
{
    private readonly IOptimizeService _service;
    private readonly Func<long, string> _formatBytes;
    private readonly Action? _openLogs;
    private IReadOnlyList<MemoryTrimEntry> _entries = [];
    private string _resultFilter = "All";
    private bool _showDetails;
    private string? _totalsText;
    private double _fraction;
    private double _load = double.NaN;
    private string _usedText = "—";
    private string _availableText = "—";
    private string _colorKey = ColorKeys.ToneNormal;
    private bool _clearStandby;
    private bool _isBusy;
    private string? _lastResult;

    public MemoryOptimizeViewModel(IOptimizeService service, Func<long, string> formatBytes, Action? openLogs = null)
    {
        _service = service;
        _formatBytes = formatBytes;
        _openLogs = openLogs;
        FreeUpCommand = new AsyncRelayCommand(async () => await FreeUpAsync(CancellationToken.None), () => !IsBusy && _service.IsSupported);
        OpenLogsCommand = new RelayCommand(() => _openLogs?.Invoke());
        ToggleDetailsCommand = new RelayCommand(() => ShowDetails = !ShowDetails, () => HasResults);
    }

    public IAsyncRelayCommand FreeUpCommand { get; }

    /// <summary>Opens Settings › Logs at this clean-up's lines.</summary>
    public IRelayCommand OpenLogsCommand { get; }

    public IRelayCommand ToggleDetailsCommand { get; }

    /// <summary>Every process of the last clean-up, filtered by <see cref="ResultFilter"/>.</summary>
    public ObservableCollection<MemoryTrimRowViewModel> Results { get; } = [];

    public bool HasResults => _entries.Count > 0;

    /// <summary>"All", "Trimmed", "Skipped" or "Failed".</summary>
    public IReadOnlyList<Option<string>> ResultFilters { get; private set; } = [new("All", "All")];

    public string ResultFilter
    {
        get => _resultFilter;
        set
        {
            if (SetProperty(ref _resultFilter, value ?? "All"))
            {
                FillResults();
            }
        }
    }

    public bool ShowDetails
    {
        get => _showDetails;
        set => SetProperty(ref _showDetails, value);
    }

    /// <summary>"Trimmed 142 · skipped 9 · failed 37 · ended 0 (Coreloom never ends processes) · 3.1 GB taken out of working sets".</summary>
    public string? TotalsText
    {
        get => _totalsText;
        private set => SetProperty(ref _totalsText, value);
    }

    /// <summary>Shows a clean-up's per-process results (also used by the Smart Optimize run).</summary>
    public void ShowResults(MemoryCleanResult result)
    {
        _entries = result.Processes;
        var totals = MemoryTrim.Totals(_entries);
        TotalsText = _entries.Count == 0 ? null : $"{totals.Summary} · {_formatBytes(totals.Reclaimed)} taken out of working sets";
        ResultFilters =
        [
            new("All", $"All {_entries.Count.ToString(CultureInfo.CurrentCulture)}"),
            new("Trimmed", $"Trimmed {totals.Trimmed.ToString(CultureInfo.CurrentCulture)}"),
            new("Skipped", $"Skipped {totals.Skipped.ToString(CultureInfo.CurrentCulture)}"),
            new("Failed", $"Failed {totals.Failed.ToString(CultureInfo.CurrentCulture)}"),
        ];
        OnPropertyChanged(nameof(ResultFilters));
        _resultFilter = "All";
        OnPropertyChanged(nameof(ResultFilter));
        FillResults();
        OnPropertyChanged(nameof(HasResults));
        ToggleDetailsCommand.NotifyCanExecuteChanged();
    }

    private void FillResults()
    {
        Results.Clear();
        foreach (var entry in _entries)
        {
            if (_resultFilter == "All" || entry.Outcome.ToString() == _resultFilter)
            {
                Results.Add(new MemoryTrimRowViewModel(entry, _formatBytes));
            }
        }
    }

    /// <summary>Share of RAM in use (0–1), for the ring.</summary>
    public double Fraction
    {
        get => _fraction;
        private set => SetProperty(ref _fraction, value);
    }

    /// <summary>Percent in use (NaN until the first reading).</summary>
    public double Load
    {
        get => _load;
        private set => SetProperty(ref _load, value);
    }

    public Func<double, string> PercentFormatter { get; } = v => Math.Round(v).ToString("0", CultureInfo.CurrentCulture);

    public string UsedText
    {
        get => _usedText;
        private set => SetProperty(ref _usedText, value);
    }

    public string AvailableText
    {
        get => _availableText;
        private set => SetProperty(ref _availableText, value);
    }

    public string ColorKey
    {
        get => _colorKey;
        private set => SetProperty(ref _colorKey, value);
    }

    /// <summary>Also empty the standby list (cached files). Off by default: Windows refills it, and apps load slower until it does.</summary>
    public bool ClearStandby
    {
        get => _clearStandby;
        set => SetProperty(ref _clearStandby, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                FreeUpCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string? LastResult
    {
        get => _lastResult;
        private set => SetProperty(ref _lastResult, value);
    }

    public void Update(TelemetryFrame frame)
    {
        if (frame.TryGetValue(MetricIds.MemoryUsed, out double used) && frame.TryGetValue(MetricIds.MemoryAvailable, out double available) && used + available > 0)
        {
            double total = used + available;
            Fraction = used / total;
            Load = Fraction * 100;
            ColorKey = ColorKeys.ForLevel(Thresholds.Memory.Evaluate(Load));
            UsedText = $"{_formatBytes((long)used)} of {_formatBytes((long)total)} in use";
            AvailableText = $"{_formatBytes((long)available)} available";
        }
    }

    public async Task<MemoryCleanResult?> FreeUpAsync(CancellationToken cancellationToken)
    {
        bool owner = !IsBusy;
        IsBusy = true;
        LastResult = "Freeing up memory…";
        try
        {
            var result = await _service.FreeMemoryAsync(ClearStandby, cancellationToken);
            ShowResults(result);
            LastResult = result.Freed > 0
                ? $"Freed {_formatBytes(result.Freed)} from {OptimizeText.Count(result.ProcessesTrimmed, "app", "apps")}."
                : result.ProcessesTrimmed > 0 ? "Memory was already in good shape." : "Nothing could be freed.";
            return result;
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            LastResult = $"Memory couldn't be freed: {e.Message}";
            return null;
        }
        finally
        {
            if (owner)
            {
                IsBusy = false;
            }
        }
    }
}

/// <summary>A startup app with an on/off switch (the same switch as Task Manager's Startup apps).</summary>
public sealed class StartupAppViewModel : ObservableObject
{
    private readonly IOptimizeService _service;
    private StartupApp _app;
    private bool _isEnabled;
    private string? _problem;

    public StartupAppViewModel(StartupApp app, IOptimizeService service)
    {
        _app = app;
        _service = service;
        _isEnabled = app.Enabled;
    }

    public string Name => _app.Name;

    public string Publisher => _app.Publisher ?? _app.ScopeText;

    public string Command => _app.Command;

    public string Id => _app.Id;

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (value == _isEnabled)
            {
                return;
            }

            if (_service.SetStartupEnabled(_app, value))
            {
                _app = _app with { Enabled = value };
                Problem = null;
                SetProperty(ref _isEnabled, value);
                EnabledChanged?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                Problem = "Windows didn't allow the change.";
                OnPropertyChanged(); // the switch goes back
            }
        }
    }

    public string? Problem
    {
        get => _problem;
        private set => SetProperty(ref _problem, value);
    }

    public event EventHandler? EnabledChanged;
}

/// <summary>A one-click maintenance task (flush DNS, optimize drives) with its state.</summary>
public sealed class MaintenanceTaskViewModel : ObservableObject
{
    private readonly Func<CancellationToken, Task<MaintenanceResult>> _run;
    private bool _isBusy;
    private string? _message;
    private string _colorKey = ColorKeys.Neutral;

    public MaintenanceTaskViewModel(string title, string description, string iconKey, Func<CancellationToken, Task<MaintenanceResult>> run)
    {
        Title = title;
        Description = description;
        IconKey = iconKey;
        _run = run;
        RunCommand = new AsyncRelayCommand(async () => await RunAsync(CancellationToken.None), () => !IsBusy);
    }

    public string Title { get; }

    public string Description { get; }

    public string IconKey { get; }

    public IAsyncRelayCommand RunCommand { get; }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RunCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string? Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    public string ColorKey
    {
        get => _colorKey;
        private set => SetProperty(ref _colorKey, value);
    }

    public async Task<MaintenanceResult> RunAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        Message = "Working…";
        ColorKey = ColorKeys.ToneNormal;
        try
        {
            MaintenanceResult result;
            try
            {
                result = await _run(cancellationToken);
            }
            catch (Exception e) when (e is IOException or InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            {
                result = new MaintenanceResult(false, e.Message);
            }

            Message = result.Message;
            ColorKey = result.Succeeded ? ColorKeys.ToneCool : ColorKeys.ToneWarm;
            return result;
        }
        finally
        {
            IsBusy = false;
        }
    }
}

/// <summary>The Speed card: startup apps and maintenance tasks.</summary>
public sealed class SpeedUpViewModel : ObservableObject
{
    private readonly IOptimizeService _service;
    private bool _loaded;

    private readonly bool _mac;

    public SpeedUpViewModel(IOptimizeService service, bool mac = false)
    {
        _service = service;
        _mac = mac;
        FlushDns = new MaintenanceTaskViewModel("Flush DNS cache", "Fixes sites that won't load after a network or DNS change", "Icon.Globe", _service.FlushDnsAsync);
        OptimizeDrives = mac
            ? new MaintenanceTaskViewModel("Check the startup disk", "Disk Utility's First Aid check, read only: confirms the disk's file system is sound", "Icon.Storage", _service.OptimizeDrivesAsync)
            : new MaintenanceTaskViewModel("Optimize drives", "Trims SSDs and defragments hard drives, as Windows does weekly", "Icon.Storage", _service.OptimizeDrivesAsync);
        RefreshCommand = new RelayCommand(Refresh);
    }

    public ObservableCollection<StartupAppViewModel> StartupApps { get; } = [];

    public MaintenanceTaskViewModel FlushDns { get; }

    public MaintenanceTaskViewModel OptimizeDrives { get; }

    public IReadOnlyList<MaintenanceTaskViewModel> Tasks => [FlushDns, OptimizeDrives];

    public IRelayCommand RefreshCommand { get; }

    public int EnabledCount => StartupApps.Count(a => a.IsEnabled);

    /// <summary>"6 of 11 apps start with Windows".</summary>
    public string StartupSummary => StartupApps.Count == 0 ? (_mac ? "No login items found" : "No startup apps found")
        : $"{EnabledCount} of {OptimizeText.Count(StartupApps.Count, "app", "apps")} {(_mac ? "open at login" : "start with Windows")}";

    public bool HasStartupApps => StartupApps.Count > 0;

    public void EnsureLoaded()
    {
        if (!_loaded)
        {
            Refresh();
        }
    }

    public void Refresh()
    {
        _loaded = true;
        foreach (var app in StartupApps)
        {
            app.EnabledChanged -= OnEnabledChanged;
        }

        StartupApps.Clear();
        IReadOnlyList<StartupApp> apps;
        try
        {
            apps = _service.StartupApps();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            apps = [];
        }

        foreach (var app in apps.OrderByDescending(a => a.Enabled).ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var item = new StartupAppViewModel(app, _service);
            item.EnabledChanged += OnEnabledChanged;
            StartupApps.Add(item);
        }

        OnCountsChanged();
    }

    private void OnEnabledChanged(object? sender, EventArgs e) => OnCountsChanged();

    private void OnCountsChanged()
    {
        OnPropertyChanged(nameof(EnabledCount));
        OnPropertyChanged(nameof(StartupSummary));
        OnPropertyChanged(nameof(HasStartupApps));
    }
}

/// <summary>
/// Smart Optimize: one Smart Scan looks for junk, checks protection, memory and startup apps; one click then cleans up,
/// removes threats, frees memory and refreshes the network cache. Each part also has its own card with more control.
/// </summary>
public sealed class OptimizeViewModel : PageViewModel
{
    private readonly IOptimizeService _service;
    private OptimizeStage _stage;
    private string _headline = "Ready when you are";
    private string _caption = "Smart Scan finds junk files, checks your protection and memory, and suggests what to tidy up.";
    private double _ringProgress;
    private double _ringTone = GaugeTone.Blue;
    private string? _resultText;
    private bool _loaded;
    private CancellationTokenSource? _cts;

    public OptimizeViewModel(PageContext context)
        : base("optimize", "Smart Optimize", "Icon.Optimize", context)
    {
        _service = context.Tools.Optimizer;
        Func<long, string> bytes = b => Formatter.Bytes(b).ToString();
        Cleanup = new CleanupViewModel(_service, bytes);
        Protection = new ProtectionViewModel(_service, () => Now(), IsMac);
        Memory = new MemoryOptimizeViewModel(_service, b => Formatter.MemoryBytes(b).ToString(), () => Context.OpenLogs?.Invoke("Optimize", "Memory:"));
        Speed = new SpeedUpViewModel(_service, IsMac);
        CleanupModule = new("cleanup", "Cleanup", "Icon.Trash");
        ProtectionModule = new("protection", "Protection", "Icon.Shield");
        MemoryModule = new("memory", "Memory", "Icon.Memory");
        SpeedModule = new("speed", "Speed", "Icon.Rocket");
        Modules = [CleanupModule, ProtectionModule, MemoryModule, SpeedModule];

        ScanCommand = new AsyncRelayCommand(SmartScanAsync, () => !IsWorking && IsSupported);
        RunCommand = new AsyncRelayCommand(RunAsync, () => Stage == OptimizeStage.Ready && IsSupported);
        CancelCommand = new RelayCommand(() => _cts?.Cancel(), () => IsWorking);
    }

    /// <summary>For tests: the clock texts are based on.</summary>
    public Func<DateTime> Now { get; init; } = () => DateTime.Now;

    public bool IsSupported => _service.IsSupported;

    public CleanupViewModel Cleanup { get; }

    public ProtectionViewModel Protection { get; }

    public MemoryOptimizeViewModel Memory { get; }

    public SpeedUpViewModel Speed { get; }

    public OptimizeModuleViewModel CleanupModule { get; }

    public OptimizeModuleViewModel ProtectionModule { get; }

    public OptimizeModuleViewModel MemoryModule { get; }

    public OptimizeModuleViewModel SpeedModule { get; }

    public IReadOnlyList<OptimizeModuleViewModel> Modules { get; }

    public IAsyncRelayCommand ScanCommand { get; }

    public IAsyncRelayCommand RunCommand { get; }

    public IRelayCommand CancelCommand { get; }

    public OptimizeStage Stage
    {
        get => _stage;
        private set
        {
            if (SetProperty(ref _stage, value))
            {
                OnPropertyChanged(nameof(StageKey));
                OnPropertyChanged(nameof(StageText));
                OnPropertyChanged(nameof(IsWorking));
                OnPropertyChanged(nameof(IsReady));
                OnPropertyChanged(nameof(IsDone));
                OnPropertyChanged(nameof(ScanText));
                ScanCommand.NotifyCanExecuteChanged();
                RunCommand.NotifyCanExecuteChanged();
                CancelCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>"Idle", "Scanning", "Ready", "Running", "Done" (templates switch on it).</summary>
    public string StageKey => _stage.ToString();

    /// <summary>A word under the headline in the ring (none before the first scan).</summary>
    public string StageText => _stage switch
    {
        OptimizeStage.Scanning => "scanning",
        OptimizeStage.Ready => "found",
        OptimizeStage.Running => "optimizing",
        OptimizeStage.Done => "finished",
        _ => string.Empty, // the headline says it ("Ready when you are")
    };

    public bool IsWorking => _stage is OptimizeStage.Scanning or OptimizeStage.Running;

    public bool IsReady => _stage == OptimizeStage.Ready;

    public bool IsDone => _stage == OptimizeStage.Done;

    public string ScanText => _stage switch
    {
        OptimizeStage.Ready or OptimizeStage.Done => "Scan again",
        _ => "Smart Scan",
    };

    public string Headline
    {
        get => _headline;
        private set => SetProperty(ref _headline, value);
    }

    public string Caption
    {
        get => _caption;
        private set => SetProperty(ref _caption, value);
    }

    /// <summary>The hero ring: 0–1 progress of the scan or run, full when finished.</summary>
    public double RingProgress
    {
        get => _ringProgress;
        private set => SetProperty(ref _ringProgress, value);
    }

    /// <summary>The ring's color: blue while working, green when all is well, orange or red when something needs attention.</summary>
    public double RingTone
    {
        get => _ringTone;
        private set => SetProperty(ref _ringTone, value);
    }

    /// <summary>After a run: what it did, line by line.</summary>
    public string? ResultText
    {
        get => _resultText;
        private set => SetProperty(ref _resultText, value);
    }

    public string UnsupportedText => IsMac
        ? "Smart Optimize works with macOS features (the Trash, login items, its security settings) that aren't available here."
        : "Smart Optimize works with Windows features (Microsoft Defender, the Recycle Bin, startup apps) that aren't available here.";

    protected override void OnActivated()
    {
        if (_loaded || !IsSupported)
        {
            return;
        }

        _loaded = true;
        Cleanup.Load();
        Speed.EnsureLoaded();
        _ = RefreshProtectionQuietlyAsync();
    }

    protected override void OnUpdate(TelemetryFrame frame) => Memory.Update(frame);

    private async Task RefreshProtectionQuietlyAsync()
    {
        try
        {
            await Protection.RefreshAsync(CancellationToken.None);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // the card shows "Checking…"; the user can refresh
        }
    }

    private async Task SmartScanAsync()
    {
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        Stage = OptimizeStage.Scanning;
        ResultText = null;
        RingProgress = 0;
        RingTone = GaugeTone.Blue;
        Headline = "Scanning…";
        foreach (var module in Modules)
        {
            module.Reset();
            module.IsIncluded = true;
        }

        try
        {
            // Cleanup: 0 – 55 %
            Caption = "Looking for junk files…";
            CleanupModule.IsWorking = true;
            await Cleanup.ScanAsync(token, p => RingProgress = p * 0.55);
            CleanupModule.IsWorking = false;
            long junk = Cleanup.SelectedBytes;
            CleanupModule.Status = Cleanup.TotalBytes > 0 ? $"{Formatter.Bytes(Cleanup.TotalBytes)} of junk" : "No junk found";
            CleanupModule.Detail = Cleanup.TotalBytes > 0 ? $"{Formatter.Bytes(junk)} selected to clean" : "Nothing to clean";
            CleanupModule.ColorKey = junk >= 1L << 30 ? ColorKeys.ToneWarm : ColorKeys.ToneCool;
            CleanupModule.CanInclude = junk > 0;
            CleanupModule.IsIncluded = junk > 0;

            // Protection: 55 – 80 %
            token.ThrowIfCancellationRequested();
            Caption = "Checking your protection…";
            ProtectionModule.IsWorking = true;
            await Protection.RefreshAsync(token);
            RingProgress = 0.8;
            ProtectionModule.IsWorking = false;
            ProtectionModule.Status = Protection.Title;
            ProtectionModule.Detail = Protection.HasThreats ? "Threats will be removed" : Protection.Summary;
            ProtectionModule.ColorKey = Protection.ColorKey;
            ProtectionModule.CanInclude = Protection.HasThreats;
            ProtectionModule.IsIncluded = Protection.HasThreats;

            // Memory: 80 – 90 %
            Caption = "Checking memory…";
            MemoryModule.IsWorking = true;
            await Task.Delay(250, token); // a beat, so the step reads
            MemoryModule.IsWorking = false;
            RingProgress = 0.9;
            MemoryModule.Status = double.IsFinite(Memory.Load) ? $"{Memory.PercentFormatter(Memory.Load)}% in use" : "Memory";
            MemoryModule.Detail = "Background apps will be trimmed";
            MemoryModule.ColorKey = Memory.ColorKey;
            MemoryModule.CanInclude = true;

            // Speed: 90 – 100 %
            Caption = "Checking startup apps…";
            SpeedModule.IsWorking = true;
            Speed.Refresh();
            await Task.Delay(200, token);
            SpeedModule.IsWorking = false;
            RingProgress = 1;
            SpeedModule.Status = Speed.StartupSummary;
            SpeedModule.Detail = "The DNS cache will be refreshed";
            SpeedModule.ColorKey = Speed.EnabledCount > 12 ? ColorKeys.ToneWarm : ColorKeys.ToneCool;
            SpeedModule.CanInclude = true;

            bool attention = Protection.HasThreats || Protection.ColorKey == ColorKeys.ToneHot;
            RingTone = attention ? GaugeTone.Red : junk >= 1L << 30 ? GaugeTone.Orange : GaugeTone.Green;
            Headline = junk > 0 ? $"{Formatter.Bytes(junk)}" : "Looking good";
            Caption = attention ? "Something needs your attention. Review below, then clean up."
                : junk > 0 ? "of junk can be cleaned. Clean up to free the space, trim memory and refresh the network cache."
                : "No junk to clean. You can still trim memory and refresh the network cache.";
            Stage = OptimizeStage.Ready;
        }
        catch (OperationCanceledException)
        {
            StopModules();
            Headline = "Scan stopped";
            Caption = "Run Smart Scan again whenever you like.";
            RingProgress = 0;
            Stage = OptimizeStage.Idle;
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            StopModules();
            Headline = "Scan didn't finish";
            Caption = e.Message;
            RingProgress = 0;
            Stage = OptimizeStage.Idle;
        }
    }

    private void StopModules()
    {
        foreach (var module in Modules)
        {
            module.IsWorking = false;
        }
    }

    private async Task RunAsync()
    {
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        Stage = OptimizeStage.Running;
        RingProgress = 0;
        RingTone = GaugeTone.Blue;
        Headline = "Optimizing…";
        var lines = new List<string>();
        long freedDisk = 0;
        long freedMemory = 0;
        try
        {
            if (CleanupModule.IsIncluded && CleanupModule.CanInclude)
            {
                Caption = "Cleaning junk files…";
                CleanupModule.IsWorking = true;
                var cleaned = await Cleanup.CleanAsync(token, p => RingProgress = p * 0.5);
                freedDisk = cleaned.FreedBytes;
                CleanupModule.IsWorking = false;
                CleanupModule.IsDone = true;
                CleanupModule.Status = freedDisk > 0 ? $"Freed {Formatter.Bytes(freedDisk)}" : "Nothing freed";
                CleanupModule.Detail = cleaned.Skipped > 0 ? $"{OptimizeText.Count(cleaned.Skipped, "file was", "files were")} in use and left alone" : "Junk cleaned";
                CleanupModule.ColorKey = ColorKeys.ToneCool;
                lines.Add(freedDisk > 0 ? $"Freed {Formatter.Bytes(freedDisk)} of disk space" : "No disk space could be freed");
            }

            RingProgress = 0.5;
            if (ProtectionModule.IsIncluded && ProtectionModule.CanInclude)
            {
                token.ThrowIfCancellationRequested();
                Caption = "Removing threats…";
                ProtectionModule.IsWorking = true;
                int removed = await Protection.RemoveThreatsAsync(token);
                ProtectionModule.IsWorking = false;
                ProtectionModule.IsDone = true;
                ProtectionModule.Status = Protection.Title;
                ProtectionModule.Detail = Protection.Message;
                ProtectionModule.ColorKey = Protection.ColorKey;
                lines.Add(removed > 0 ? $"Removed {OptimizeText.Count(removed, "threat", "threats")}" : "Threats need a look in Windows Security");
            }
            else if (!Protection.HasThreats && Protection.ColorKey != ColorKeys.ToneHot)
            {
                ProtectionModule.IsDone = true; // checked, nothing to do
            }

            RingProgress = 0.65;
            if (MemoryModule.IsIncluded)
            {
                token.ThrowIfCancellationRequested();
                Caption = "Freeing up memory…";
                MemoryModule.IsWorking = true;
                var memory = await Memory.FreeUpAsync(token);
                freedMemory = memory?.Freed ?? 0;
                MemoryModule.IsWorking = false;
                MemoryModule.IsDone = true;
                MemoryModule.Status = freedMemory > 0 ? $"Freed {Formatter.MemoryBytes(freedMemory)}" : "Already in good shape";
                MemoryModule.Detail = Memory.LastResult;
                MemoryModule.ColorKey = ColorKeys.ToneCool;
                if (freedMemory > 0)
                {
                    lines.Add($"Freed {Formatter.MemoryBytes(freedMemory)} of memory");
                }
            }

            RingProgress = 0.85;
            if (SpeedModule.IsIncluded)
            {
                token.ThrowIfCancellationRequested();
                Caption = "Refreshing the network cache…";
                SpeedModule.IsWorking = true;
                var dns = await Speed.FlushDns.RunAsync(token);
                SpeedModule.IsWorking = false;
                SpeedModule.IsDone = true;
                SpeedModule.Status = dns.Succeeded ? "Network cache refreshed" : "Network cache unchanged";
                SpeedModule.Detail = Speed.StartupSummary;
                SpeedModule.ColorKey = ColorKeys.ToneCool;
                if (dns.Succeeded)
                {
                    lines.Add("Refreshed the network (DNS) cache");
                }
            }

            RingProgress = 1;
            bool attention = Protection.HasThreats;
            RingTone = attention ? GaugeTone.Orange : GaugeTone.Green;
            Headline = attention ? "Almost done" : "All done";
            Caption = attention ? "Some threats remain; open Windows Security to review them." : "Your PC is tidy and ready to go.";
            ResultText = lines.Count > 0 ? string.Join(Environment.NewLine, lines) : "Nothing needed doing.";
            Stage = OptimizeStage.Done;
        }
        catch (Exception e) when (e is OperationCanceledException or IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            StopModules();
            Headline = e is OperationCanceledException ? "Stopped" : "Didn't finish";
            Caption = e is OperationCanceledException
                ? "What was finished stays done. Run Smart Scan again to pick up where you left off."
                : e.Message;
            ResultText = lines.Count > 0 ? string.Join(Environment.NewLine, lines) : null;
            RingTone = GaugeTone.Orange;
            Stage = OptimizeStage.Done;
        }
    }
}
