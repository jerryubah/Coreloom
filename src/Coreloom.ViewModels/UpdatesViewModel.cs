using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Coreloom.Core;
using Coreloom.Core.Diagnostics;
using Coreloom.Core.Updates;
using Coreloom.ViewModels.Infrastructure;
using Coreloom.ViewModels.Services;

namespace Coreloom.ViewModels;

/// <summary>Where the updater is. Drives the Settings › Updates card, the sidebar badge and the tray.</summary>
public enum UpdatePhase
{
    /// <summary>Not checked during this session yet.</summary>
    Idle,
    Checking,
    UpToDate,

    /// <summary>A newer version exists and hasn't been downloaded (automatic updates off, or the download failed).</summary>
    Available,
    Downloading,

    /// <summary>The update is downloaded and verified; it installs on "Install update", at the next start or while the PC is idle.</summary>
    Ready,
    Installing,

    /// <summary>The last check failed (offline, rate limited...).</summary>
    Failed,

    /// <summary>The build has no GitHub repository to look at (local builds).</summary>
    NotConfigured,
}

/// <summary>A notification the app may show in the notification area.</summary>
/// <param name="UserInitiated">True when it follows something the user just did in the window (the app may skip the toast).</param>
public sealed record UpdateNotification(string Title, string Message, bool UserInitiated);

/// <summary>
/// Automatic updates from GitHub Releases. A check that finds a newer version downloads its package in the background
/// (resumable): the installer for copies installed with it, the portable zip for every other copy. The download is
/// verified against the release's SHA256SUMS.txt and its hash recorded where only administrators can write. The update
/// then installs when the user clicks "Install update" (which also downloads it first when needed), the next time
/// Coreloom starts, or while the PC is idle (see <see cref="UpdateInstallPolicy"/>). Either way Coreloom exits, the
/// files are replaced (by the installer, or by the new version copying itself over a portable copy; see
/// <see cref="FolderUpdate"/>) and Coreloom opens again. Running executables are never overwritten in place.
/// </summary>
public sealed class UpdatesViewModel : ObservableObject
{
    private readonly AppVersionInfo _app;
    private readonly SettingsService _settings;
    private readonly IUpdateInstaller _installer;
    private readonly IShellService _shell;
    private readonly HttpClient _http;
    private readonly PendingUpdateStore _store;
    private readonly IUpdateTrustStore _trust;
    private readonly Func<DateTime> _utcNow;
    private readonly DateTime _startedUtc;
    private CancellationTokenSource? _downloadCts;
    private bool _downloadCancelledByUser;
    private bool _justUpdated;
    private bool _installedNotesLoading;
    private UpdatePhase _phase;
    private ReleaseInfo? _release;
    private PendingUpdate? _pending;
    private PendingUpdate? _installed;
    private (string Version, string Sha256)? _sessionHash;
    private DateTime _readySinceUtc;
    private string? _problem;
    private string? _failedHeadline;
    private string? _resultMessage;
    private double _progress;
    private string? _progressText;
    private ReleaseSummaryViewModel? _updateSummary;
    private ReleaseSummaryViewModel? _installedSummary;

    public UpdatesViewModel(AppVersionInfo app, SettingsService settings, IUpdateInstaller installer, IShellService shell, HttpClient http, string downloadDirectory)
        : this(app, settings, installer, shell, http, new PendingUpdateStore(downloadDirectory), new InMemoryUpdateTrustStore())
    {
    }

    public UpdatesViewModel(
        AppVersionInfo app,
        SettingsService settings,
        IUpdateInstaller installer,
        IShellService shell,
        HttpClient http,
        PendingUpdateStore store,
        IUpdateTrustStore trust,
        Func<DateTime>? utcNow = null)
    {
        _app = app;
        _settings = settings;
        _installer = installer;
        _shell = shell;
        _http = http;
        _store = store;
        _trust = trust;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _startedUtc = _utcNow();
        _phase = IsConfigured ? UpdatePhase.Idle : UpdatePhase.NotConfigured;

        CheckCommand = new AsyncRelayCommand(() => CheckAsync(userInitiated: true), () => IsConfigured && !IsBusy);
        InstallCommand = new AsyncRelayCommand(InstallAsync, () => CanInstall && (_phase == UpdatePhase.Ready || (_phase == UpdatePhase.Available && _release is not null)));
        CancelDownloadCommand = new RelayCommand(CancelDownload, () => _phase == UpdatePhase.Downloading);
        SkipCommand = new RelayCommand(Skip, () => _phase is UpdatePhase.Available or UpdatePhase.Ready);
        OpenInstalledReleaseCommand = new RelayCommand(() => _shell.OpenUrl(InstalledReleaseUrl ?? RepositoryUrl ?? "https://github.com"), () => InstalledReleaseUrl is not null || RepositoryUrl is not null);
        DismissUpdatedBannerCommand = new RelayCommand(DismissUpdatedBanner);
    }

    /// <summary>Asks the app to show a notification (a new version is ready or available).</summary>
    public event Action<UpdateNotification>? NotificationRequested;

    // ---- Version and source -----------------------------------------------------------------------

    public string CurrentVersion => _app.DisplayVersion;

    public string InformationalVersion => _app.InformationalVersion;

    public string? Repository => string.IsNullOrWhiteSpace(_settings.Current.Updates.RepositoryOverride) ? _app.Repository : _settings.Current.Updates.RepositoryOverride;

    public string? RepositoryUrl => GitHubReleaseClient.IsValidRepository(Repository) ? $"https://github.com/{Repository}" : null;

    public bool IsConfigured => GitHubReleaseClient.IsValidRepository(Repository);

    /// <summary>True when this copy can update itself (installed and portable copies alike).</summary>
    public bool CanInstall => _installer.CanInstallUpdates;

    // ---- State ------------------------------------------------------------------------------------

    public UpdatePhase Phase
    {
        get => _phase;
        private set
        {
            if (SetProperty(ref _phase, value))
            {
                RaiseState();
            }
        }
    }

    public bool IsChecking => _phase == UpdatePhase.Checking;

    public bool IsDownloading => _phase == UpdatePhase.Downloading;

    public bool IsReady => _phase == UpdatePhase.Ready;

    public bool IsBusy => _phase is UpdatePhase.Checking or UpdatePhase.Downloading or UpdatePhase.Installing;

    /// <summary>A newer version is known: shows the badge in the sidebar and on the Updates section.</summary>
    /// <remarks>It stays visible while a later check runs, so the badge doesn't flicker.</remarks>
    public bool ShowBadge => _phase is UpdatePhase.Available or UpdatePhase.Downloading or UpdatePhase.Ready or UpdatePhase.Installing
        || (_phase == UpdatePhase.Checking && (_pending is not null || _release is not null));

    public string BadgeText => _phase switch
    {
        UpdatePhase.Ready => "Install update",
        UpdatePhase.Checking when _pending is not null => "Install update",
        UpdatePhase.Downloading => "Downloading update",
        UpdatePhase.Installing => "Updating…",
        _ => "Update available",
    };

    public string BadgeToolTip => _phase switch
    {
        UpdatePhase.Ready => $"Coreloom {UpdateVersion} is ready to install",
        UpdatePhase.Checking when _pending is not null => $"Coreloom {UpdateVersion} is ready to install",
        UpdatePhase.Downloading => $"Downloading Coreloom {UpdateVersion}",
        UpdatePhase.Installing => $"Installing Coreloom {UpdateVersion}",
        _ => $"Coreloom {UpdateVersion} is available",
    };

    /// <summary>Icon resource key for the status card.</summary>
    public string StatusIcon => _phase switch
    {
        UpdatePhase.UpToDate => "Icon.Check",
        UpdatePhase.Available or UpdatePhase.Downloading or UpdatePhase.Ready or UpdatePhase.Installing => "Icon.Download",
        UpdatePhase.Failed => "Icon.Warning",
        UpdatePhase.NotConfigured => "Icon.Info",
        _ => "Icon.Refresh",
    };

    /// <summary>"Success", "Accent", "Critical" or "Neutral" (colors the status icon).</summary>
    public string StatusTone => _phase switch
    {
        UpdatePhase.UpToDate => "Success",
        UpdatePhase.Available or UpdatePhase.Downloading or UpdatePhase.Ready or UpdatePhase.Installing => "Accent",
        UpdatePhase.Failed => "Critical",
        _ => "Neutral",
    };

    public string HeadlineText => _phase switch
    {
        UpdatePhase.Checking => "Checking for updates…",
        UpdatePhase.UpToDate => "You're up to date",
        UpdatePhase.Available => $"Coreloom {UpdateVersion} is available",
        UpdatePhase.Downloading => $"Downloading Coreloom {UpdateVersion}",
        UpdatePhase.Ready => $"Coreloom {UpdateVersion} is ready to install",
        UpdatePhase.Installing => $"Installing Coreloom {UpdateVersion}…",
        UpdatePhase.Failed => _failedHeadline ?? "Couldn't check for updates",
        UpdatePhase.NotConfigured => "Update checks aren't set up for this build",
        _ => $"Coreloom {CurrentVersion}",
    };

    public string DetailText => _phase switch
    {
        UpdatePhase.Checking => "Looking for a newer version on GitHub.",
        UpdatePhase.UpToDate => _resultMessage ?? $"Coreloom {CurrentVersion} is the latest version · {CheckedText}",
        UpdatePhase.Available => _problem ?? (CanInstall
            ? $"You have {CurrentVersion}. Installing it closes Coreloom for a moment and opens the new version."
            : $"You have {CurrentVersion}. This copy can't update itself."),
        UpdatePhase.Downloading => _progressText ?? "Starting the download…",
        UpdatePhase.Ready => _problem ?? (_pending?.InstallFailed == true
            ? "Coreloom couldn't finish installing it automatically. Try again."
            : AutoInstall
                ? "Install it now, or it installs by itself the next time Coreloom starts or while your PC is idle."
                : "Installing it closes Coreloom for a moment and opens the new version."),
        UpdatePhase.Installing => "Coreloom will close and reopen in a moment.",
        UpdatePhase.Failed => _problem ?? "Something went wrong. Try again later.",
        UpdatePhase.NotConfigured => "This copy wasn't built by the release workflow, so it doesn't know where to look for updates.",
        _ => _settings.Current.Updates.LastCheckUtc is null ? $"You have Coreloom {CurrentVersion}. Not checked for updates yet." : $"You have Coreloom {CurrentVersion} · {CheckedText}",
    };

    /// <summary>Download progress 0–1.</summary>
    public double Progress
    {
        get => _progress;
        private set => SetProperty(ref _progress, value);
    }

    // Which action the status card offers.
    public bool ShowCheckButton => _phase is UpdatePhase.Idle or UpdatePhase.Checking or UpdatePhase.UpToDate or UpdatePhase.Failed or UpdatePhase.NotConfigured;

    /// <summary>"Install update": downloads the update if needed, then closes Coreloom, installs it and opens Coreloom again.</summary>
    public bool ShowInstallButton => CanInstall && _phase is UpdatePhase.Available or UpdatePhase.Ready;

    /// <summary>The status card shows "Install update" only when the What's new card (which has it) isn't shown.</summary>
    public bool ShowStatusInstallButton => ShowInstallButton && !ShowUpdateSummary;

    public bool ShowCancelButton => _phase == UpdatePhase.Downloading;

    // ---- The new version --------------------------------------------------------------------------

    /// <summary>The version that is available, downloading or ready.</summary>
    public string? UpdateVersion => _phase == UpdatePhase.Ready || _release is null ? _pending?.Version ?? _release?.Version.ToString() : _release.Version.ToString();

    /// <summary>"What's new" for the available version (shown while an update is available, downloading or ready).</summary>
    public ReleaseSummaryViewModel? UpdateSummary => ShowBadge ? _updateSummary : null;

    public bool ShowUpdateSummary => UpdateSummary is not null;

    // ---- The installed version --------------------------------------------------------------------

    /// <summary>"What's new" for the running version, when Coreloom updated itself to it.</summary>
    public ReleaseSummaryViewModel? InstalledSummary => _installedSummary;

    public bool ShowInstalledSummary => !ShowBadge && (_installedSummary is not null || ShowUpdatedBanner);

    public string InstalledHeading => $"What's new in {CurrentVersion}";

    /// <summary>Shown in "What's new in this version" while its notes are being fetched, or when they can't be.</summary>
    public string InstalledSummaryFallback => _installedNotesLoading
        ? "Getting the release notes…"
        : "The summary isn't available right now. The full release notes are on GitHub.";

    public string? InstalledReleaseUrl => _installed?.HtmlUrl ?? (RepositoryUrl is null ? null : $"{RepositoryUrl}/releases/tag/v{CurrentVersion}");

    /// <summary>True on the first launches after an update, until the banner is dismissed.</summary>
    public bool ShowUpdatedBanner => SemanticVersion.TryParse(_settings.Current.Updates.WhatsNewVersion, out var v) && v.Equals(_app.Version);

    public string UpdatedBannerText => $"Coreloom was updated to {CurrentVersion}.";

    /// <summary>True only for the first launch after an update (later launches keep the banner but don't notify again).</summary>
    public bool JustUpdated => _justUpdated;

    /// <summary>Notification for the first launch after an update when it opens no window (a background update).</summary>
    public UpdateNotification? UpdatedNotification => _justUpdated && ShowUpdatedBanner
        ? new UpdateNotification($"Coreloom was updated to {CurrentVersion}", WithTail(_installedSummary?.OneLine, "Click to see what's new."), false)
        : null;

    // ---- Settings ---------------------------------------------------------------------------------

    public bool AutoInstall
    {
        get => _settings.Current.Updates.AutoInstall;
        set
        {
            if (_settings.Current.Updates.AutoInstall == value)
            {
                return;
            }

            _settings.Update("Updates", s => s.Updates.AutoInstall = value);
            OnPropertyChanged();
            RaiseState();
            if (value && _phase == UpdatePhase.Available && CanInstall && _problem is null)
            {
                _ = DownloadAsync(userInitiated: true);
            }
        }
    }

    public string AutoInstallDescription => CanInstall
        ? "Download new versions in the background and install them the next time Coreloom starts, or while your PC is idle."
        : "This copy can't update itself. It will tell you when a new version is out.";

    // ---- Commands ---------------------------------------------------------------------------------

    public IAsyncRelayCommand CheckCommand { get; }

    /// <summary>"Install update" (Settings › Updates and the tray).</summary>
    public IAsyncRelayCommand InstallCommand { get; }

    public IRelayCommand CancelDownloadCommand { get; }

    public IRelayCommand SkipCommand { get; }


    public IRelayCommand OpenInstalledReleaseCommand { get; }

    public IRelayCommand DismissUpdatedBannerCommand { get; }

    // ---- Startup ----------------------------------------------------------------------------------

    /// <summary>
    /// Reads what an earlier session left behind: records this launch (detecting an update that just finished),
    /// restores a downloaded update whose hash is trusted, and discards anything stale or untrusted.
    /// </summary>
    /// <param name="launchedAfterFailedUpdate">The installer started this copy with --update-failed.</param>
    public void InitializeFromDisk(bool launchedAfterFailedUpdate = false)
    {
        bool upgraded = false;
        bool ranBefore = _settings.Current.Updates.LastCheckUtc is not null;
        _settings.Update("Updates", s => upgraded = s.Updates.RecordLaunch(_app.Version, ranBefore));
        _justUpdated = upgraded;
        if (upgraded)
        {
            Log.Info("Updates", $"First start of {CurrentVersion} after an update.");
        }

        var pending = _store.LoadPending();
        switch (PendingUpdateStore.Evaluate(pending, _app.Version))
        {
            case PendingUpdateState.Installed:
                Log.Info("Updates", $"Update to {pending!.Version} installed.");
                _store.MarkInstalled(pending);
                _trust.Clear();
                break;

            case PendingUpdateState.Stale:
                _store.DeletePending();
                _trust.Clear();
                break;

            case PendingUpdateState.Ready:
                RestorePending(pending!, launchedAfterFailedUpdate);
                break;
        }

        _store.DeletePackagesUpTo(_app.Version);
        _installed = _store.LoadInstalled() is { } installed && installed.ParsedVersion?.Equals(_app.Version) == true ? installed : null;
        _installedSummary = _installed is null ? null : ReleaseSummaryViewModel.From(CurrentVersion, _installed.Notes, _installed.PublishedAt);
        RaiseState();
    }

    private void RestorePending(PendingUpdate pending, bool launchedAfterFailedUpdate)
    {
        var trusted = _trust.Load();
        string? path = _store.PackagePath(pending, _installer.PackageKind);
        if (!CanInstall || path is null || !File.Exists(path) || trusted is null
            || trusted.Value.Version != pending.Version || !HashEquals(trusted.Value.Sha256, pending.Sha256))
        {
            Log.Warn("Updates", $"Discarding the downloaded update to {pending.Version}: it can't be installed here or its hash isn't trusted.");
            _store.DeletePending();
            _trust.Clear();
            return;
        }

        if (launchedAfterFailedUpdate)
        {
            Log.Warn("Updates", $"The installer couldn't finish updating to {pending.Version}; automatic installs of it are off.");
            pending = pending with { InstallFailed = true };
            TrySave(pending);
        }

        _pending = pending;
        _readySinceUtc = _utcNow();
        _updateSummary = ReleaseSummaryViewModel.From(pending.Version, pending.Notes, pending.PublishedAt);
        Phase = UpdatePhase.Ready;
    }

    /// <summary>True when <see cref="TryInstallAtStartup"/> would install the downloaded update.</summary>
    public bool WillInstallAtStartup => _phase == UpdatePhase.Ready && UpdateInstallPolicy.ShouldInstallAtStartup(AutoInstall, CanInstall, _pending);

    /// <summary>
    /// After an update that left no notes behind (the update was installed by an older Coreloom or by hand), fetches
    /// the running version's release notes from GitHub for "What's new". One request, only while the banner shows.
    /// </summary>
    public async Task LoadInstalledNotesIfMissingAsync()
    {
        if (_installedSummary is not null || _installedNotesLoading || !ShowUpdatedBanner || !IsConfigured)
        {
            return;
        }

        _installedNotesLoading = true;
        OnPropertyChanged(nameof(InstalledSummaryFallback));
        try
        {
            var client = new GitHubReleaseClient(_http, Repository!, $"Coreloom/{_app.DisplayVersion}");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var release = await client.GetByTagAsync($"v{CurrentVersion}", cts.Token);
            if (release is not null)
            {
                _installedSummary = ReleaseSummaryViewModel.From(CurrentVersion, release.Notes, release.PublishedAt);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Updates", "Getting the release notes for this version failed.", ex);
        }
        finally
        {
            _installedNotesLoading = false;
            OnPropertyChanged(nameof(InstalledSummaryFallback));
            RaiseState();
        }
    }

    /// <summary>Installs a downloaded update right away when automatic updates allow it. Returns true when Coreloom is exiting.</summary>
    public bool TryInstallAtStartup(bool startHidden)
    {
        if (!WillInstallAtStartup)
        {
            return false;
        }

        Log.Info("Updates", $"Installing the downloaded update to {_pending!.Version} at startup.");
        return InstallNow(startHidden ? UpdateInstallOptions.Background : UpdateInstallOptions.Interactive);
    }

    /// <summary>Installs a downloaded update while nobody is using the PC (called periodically). Returns true when Coreloom is exiting.</summary>
    public bool TryInstallInBackground(BackgroundInstallSignals signals)
    {
        if (_phase != UpdatePhase.Ready
            || !UpdateInstallPolicy.ShouldInstallInBackground(AutoInstall, CanInstall, _pending, _readySinceUtc, _startedUtc, _utcNow(), signals))
        {
            return false;
        }

        Log.Info("Updates", $"Installing the downloaded update to {_pending!.Version} while the PC is idle.");
        return InstallNow(UpdateInstallOptions.Background);
    }

    // ---- Checking ---------------------------------------------------------------------------------

    /// <summary>Checks if the configured frequency says a check is due (called at startup and periodically).</summary>
    public Task CheckIfDueAsync(bool isStartup)
    {
        return _settings.Current.Updates.IsCheckDue(_utcNow(), isStartup) ? CheckAsync(userInitiated: false) : Task.CompletedTask;
    }

    public async Task CheckAsync(bool userInitiated)
    {
        if (IsBusy || !IsConfigured)
        {
            if (!IsConfigured)
            {
                Phase = UpdatePhase.NotConfigured;
            }

            return;
        }

        var before = _phase;
        _problem = null;
        _failedHeadline = null;
        Phase = UpdatePhase.Checking;
        UpdateCheckResult result;
        try
        {
            string? repository = Repository;
            var checker = new UpdateChecker(_app.Version, repository, async (pre, ct) =>
            {
                var client = new GitHubReleaseClient(_http, repository!, $"Coreloom/{_app.DisplayVersion}");
                return await client.GetLatestAsync(pre, ct).ConfigureAwait(false);
            });

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            result = await checker.CheckAsync(_settings.Current.Updates.IncludePrereleases, userInitiated ? null : _settings.Current.Updates.SkippedVersion, cts.Token);
        }
        catch (OperationCanceledException)
        {
            result = new UpdateCheckResult(UpdateCheckStatus.Failed, null, "The update check timed out. Check your connection and try again.");
        }

        if (result.Status is UpdateCheckStatus.UpToDate or UpdateCheckStatus.UpdateAvailable or UpdateCheckStatus.NoReleases)
        {
            _settings.Update("Updates", s => s.Updates.LastCheckUtc = _utcNow());
        }

        switch (result.Status)
        {
            case UpdateCheckStatus.UpdateAvailable:
                await OnUpdateAvailableAsync(result.Release!, userInitiated);
                break;

            case UpdateCheckStatus.Failed:
                if (_pending is not null)
                {
                    Phase = UpdatePhase.Ready; // keep the update we already have
                }
                else
                {
                    _problem = result.Message;
                    Phase = before == UpdatePhase.Available && _release is not null ? UpdatePhase.Available : UpdatePhase.Failed;
                }

                break;

            case UpdateCheckStatus.NotConfigured:
                Phase = UpdatePhase.NotConfigured;
                break;

            default:
                if (_pending is not null)
                {
                    Phase = UpdatePhase.Ready; // e.g. pre-releases were turned off after downloading one
                }
                else
                {
                    _release = null;
                    _resultMessage = result.Status == UpdateCheckStatus.NoReleases
                        ? $"No releases have been published yet · {CheckedText}"
                        : null;
                    Phase = UpdatePhase.UpToDate;
                }

                break;
        }

        RaiseState();
    }

    private async Task OnUpdateAvailableAsync(ReleaseInfo release, bool userInitiated)
    {
        _resultMessage = null;
        if (_pending is not null && _pending.ParsedVersion is { } have)
        {
            if (have.CompareTo(release.Version) >= 0)
            {
                Phase = UpdatePhase.Ready; // what we downloaded is still the newest
                return;
            }

            Log.Info("Updates", $"Version {release.Version} replaces the downloaded {have}.");
            DiscardPending();
        }

        _release = release;
        _updateSummary = ReleaseSummaryViewModel.From(release.Version.ToString(), release.Notes, release.PublishedAt);
        Phase = UpdatePhase.Available;

        if (AutoInstall && CanInstall)
        {
            await DownloadAsync(userInitiated);
        }
        else
        {
            Notify($"Coreloom {release.Version} is available", WithTail(_updateSummary.OneLine, "Click to see what's new."), userInitiated);
        }
    }

    // ---- Downloading ------------------------------------------------------------------------------

    /// <param name="userInitiated">The user asked (a check, the switch or "Install update").</param>
    /// <param name="installNext">"Install update" installs right after, so there is nothing to announce.</param>
    private async Task DownloadAsync(bool userInitiated, bool installNext = false)
    {
        var release = _release;
        if (release is null || !CanInstall || _phase is UpdatePhase.Downloading or UpdatePhase.Installing)
        {
            return;
        }

        var package = release.PackageFor(_installer.PackageKind);
        var checksums = release.Checksums;
        if (package is null || checksums is null)
        {
            _problem = _installer.PackageKind switch
            {
                UpdatePackageKind.Installer => "This release doesn't include the Windows installer and its checksums, so Coreloom can't install it.",
                UpdatePackageKind.PortableZip => "This release doesn't include the portable zip and its checksums, so Coreloom can't install it.",
                _ => "This release doesn't include the Mac app for this Mac and its checksums, so Coreloom can't install it.",
            };
            Phase = UpdatePhase.Available;
            RaiseState();
            return;
        }

        _downloadCts = new CancellationTokenSource();
        _downloadCancelledByUser = false;
        var token = _downloadCts.Token;
        _problem = null;
        Progress = 0;
        _progressText = null;
        Phase = UpdatePhase.Downloading;
        string fileName = Path.GetFileName(package.Name);
        string path = Path.Combine(_store.Directory, fileName);
        try
        {
            string sums = await _http.GetStringAsync(checksums.DownloadUrl, token);
            if (!Checksums.Parse(sums).TryGetValue(fileName, out string? expected))
            {
                throw new UpdateException($"{checksums.Name} doesn't list {fileName}, so the download can't be verified.");
            }

            _store.DeleteDownloads(keepFileName: fileName); // leftovers of other versions
            var downloader = new ResumableDownloader(_http) { UserAgent = $"Coreloom/{_app.DisplayVersion}" };
            var progress = new Progress<DownloadProgress>(p =>
            {
                Progress = p.Fraction ?? 0;
                _progressText = p.TotalBytes is long total
                    ? string.Format(CultureInfo.CurrentCulture, "{0:0.0} of {1:0.0} MB · {2:0} %", p.BytesReceived / 1048576.0, total / 1048576.0, (p.Fraction ?? 0) * 100)
                    : string.Format(CultureInfo.CurrentCulture, "{0:0.0} MB", p.BytesReceived / 1048576.0);
                OnPropertyChanged(nameof(DetailText));
            });
            await downloader.DownloadAsync(package.DownloadUrl, path, package.Size > 0 ? package.Size : null, progress, token);

            _progressText = "Verifying the download…";
            OnPropertyChanged(nameof(DetailText));
            if (!await Checksums.VerifyAsync(path, expected, token))
            {
                TryDeleteFile(path);
                throw new UpdateException("The download didn't match its SHA-256 checksum and was deleted. Try again.");
            }

            var pending = PendingUpdate.FromRelease(release, package, expected, _utcNow());
            _sessionHash = (pending.Version, pending.Sha256);
            if (!_trust.TrySave(pending.Version, pending.Sha256))
            {
                Log.Warn("Updates", "The update's hash couldn't be recorded; it can only be installed during this session.");
            }

            _store.SavePending(pending);
            _pending = pending;
            _readySinceUtc = _utcNow();
            Log.Info("Updates", $"Coreloom {pending.Version} downloaded and verified ({fileName}).");
            Phase = UpdatePhase.Ready;
            if (!installNext)
            {
                Notify(
                    $"Coreloom {pending.Version} is ready to install",
                    WithTail(_updateSummary?.OneLine, AutoInstall ? "It installs the next time Coreloom starts. Click to see what's new." : "Click to see what's new and install it."),
                    userInitiated);
            }
        }
        catch (OperationCanceledException)
        {
            _problem = _downloadCancelledByUser ? "Download paused. It picks up where it stopped next time." : null;
            Phase = UpdatePhase.Available;
        }
        catch (Exception ex)
        {
            Log.Warn("Updates", "Downloading the update failed.", ex);
            _problem = ex is UpdateException ? ex.Message : $"The download failed: {ex.Message}";
            Phase = UpdatePhase.Available;
        }
        finally
        {
            _downloadCts?.Dispose();
            _downloadCts = null;
            RaiseState();
        }
    }

    private void CancelDownload()
    {
        _downloadCancelledByUser = true;
        _downloadCts?.Cancel();
    }

    // ---- Installing -------------------------------------------------------------------------------

    /// <summary>"Install update": downloads the update first when needed, then installs it right away.</summary>
    private async Task InstallAsync()
    {
        if (_phase == UpdatePhase.Available)
        {
            await DownloadAsync(userInitiated: true, installNext: true);
        }

        if (_phase == UpdatePhase.Ready)
        {
            await InstallNowAsync(UpdateInstallOptions.Interactive);
        }
    }

    /// <summary>
    /// Installs the downloaded update now: verifies it again (holding the file so it can't change), prepares it (a
    /// portable zip is unpacked next to the app), starts it and lets Coreloom exit. Used at startup and while the PC is
    /// idle, where waiting a few seconds is fine. Returns true when Coreloom is exiting.
    /// </summary>
    public bool InstallNow(UpdateInstallOptions options)
    {
        using var verified = BeginInstall();
        return verified is not null && Launch(verified, options, TryPrepare(verified));
    }

    /// <summary>The same as <see cref="InstallNow"/>, with the slow part off the UI thread (for "Install update").</summary>
    public async Task<bool> InstallNowAsync(UpdateInstallOptions options)
    {
        using var verified = BeginInstall();
        if (verified is null)
        {
            return false;
        }

        bool prepared = await Task.Run(() => TryPrepare(verified));
        return Launch(verified, options, prepared);
    }

    private VerifiedPackage? BeginInstall()
    {
        var pending = _pending;
        if (_phase != UpdatePhase.Ready || pending is null || !CanInstall)
        {
            return null;
        }

        string? path = _store.PackagePath(pending, _installer.PackageKind);
        string? expected = TrustedHash(pending);
        var verified = path is null || expected is null ? null : VerifiedPackage.Open(path, expected);
        if (verified is null)
        {
            Log.Warn("Updates", $"The downloaded update to {pending.Version} is missing, changed or untrusted; deleting it.");
            DiscardPending();
            _problem = "The downloaded update was missing or damaged, so it was deleted. Check for updates to download it again.";
            _failedHeadline = "Couldn't install the update";
            Phase = UpdatePhase.Failed;
            RaiseState();
            return null;
        }

        pending = pending with { InstallAttempts = pending.InstallAttempts + 1 };
        _pending = pending;
        TrySave(pending);
        _problem = null;
        Phase = UpdatePhase.Installing;
        Log.Info("Updates", $"Installing Coreloom {pending.Version} from {Path.GetFileName(verified.Path)}.");
        return verified;
    }

    private bool TryPrepare(VerifiedPackage verified)
    {
        try
        {
            return _installer.Prepare(verified);
        }
        catch (Exception ex)
        {
            Log.Warn("Updates", "Preparing the update failed.", ex);
            return false;
        }
    }

    private bool Launch(VerifiedPackage verified, UpdateInstallOptions options, bool prepared)
    {
        bool started = false;
        if (prepared)
        {
            try
            {
                started = _installer.LaunchAndExit(verified, options);
            }
            catch (Exception ex)
            {
                Log.Warn("Updates", "Starting the update failed.", ex);
            }
        }

        if (!started)
        {
            _problem = prepared
                ? "The update couldn't be started. Try again in a moment."
                : "The update couldn't be unpacked next to Coreloom. Check that its folder isn't read-only and the disk has space, then try again.";
            Phase = UpdatePhase.Ready;
            RaiseState();
        }

        return started;
    }

    /// <summary>Stops a running download (on exit). A partial download resumes next time.</summary>
    public void Shutdown()
    {
        _downloadCts?.Cancel();
    }

    // ---- Skip / banner ----------------------------------------------------------------------------

    private void Skip()
    {
        string? version = UpdateVersion;
        if (version is null)
        {
            return;
        }

        _settings.Update("Updates", s => s.Updates.SkippedVersion = version);
        if (_pending is not null)
        {
            DiscardPending();
        }

        _release = null;
        _problem = null;
        _resultMessage = $"Version {version} will be skipped. You'll hear about the next one.";
        Phase = UpdatePhase.UpToDate;
        RaiseState();
    }

    private void DismissUpdatedBanner()
    {
        if (_settings.Current.Updates.WhatsNewVersion is null)
        {
            return;
        }

        _settings.Update("Updates", s => s.Updates.WhatsNewVersion = null);
        OnPropertyChanged(nameof(ShowUpdatedBanner));
        OnPropertyChanged(nameof(ShowInstalledSummary));
        OnPropertyChanged(nameof(UpdatedNotification));
    }

    public void NotifyRepositoryChanged()
    {
        OnPropertyChanged(nameof(Repository));
        OnPropertyChanged(nameof(RepositoryUrl));
        OnPropertyChanged(nameof(IsConfigured));
        if (!IsConfigured)
        {
            Phase = UpdatePhase.NotConfigured;
        }
        else if (_phase == UpdatePhase.NotConfigured)
        {
            Phase = UpdatePhase.Idle;
        }

        RaiseState();
    }

    // ---- Helpers ----------------------------------------------------------------------------------

    private string CheckedText
    {
        get
        {
            if (_settings.Current.Updates.LastCheckUtc is not DateTime last)
            {
                return "not checked yet";
            }

            var local = DateTime.SpecifyKind(last, DateTimeKind.Utc).ToLocalTime();
            var today = _utcNow().ToLocalTime().Date;
            string time = local.ToString("t", CultureInfo.CurrentCulture);
            return local.Date == today ? $"checked today at {time}"
                : local.Date == today.AddDays(-1) ? $"checked yesterday at {time}"
                : $"checked {local.ToString("MMM d", CultureInfo.CurrentCulture)} at {time}";
        }
    }

    private string? TrustedHash(PendingUpdate pending)
    {
        if (_sessionHash is { } session && session.Version == pending.Version && HashEquals(session.Sha256, pending.Sha256))
        {
            return session.Sha256;
        }

        return _trust.Load() is { } stored && stored.Version == pending.Version && HashEquals(stored.Sha256, pending.Sha256)
            ? stored.Sha256
            : null;
    }

    private static bool HashEquals(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    private void DiscardPending()
    {
        _store.DeletePending();
        _trust.Clear();
        _pending = null;
        _sessionHash = null;
    }

    private void TrySave(PendingUpdate pending)
    {
        try
        {
            _store.SavePending(pending);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Updates", "Saving the update state failed.", ex);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Updates", $"Could not delete {Path.GetFileName(path)}.", ex);
        }
    }

    private void Notify(string title, string message, bool userInitiated) =>
        NotificationRequested?.Invoke(new UpdateNotification(title, message, userInitiated));

    private static string WithTail(string? summary, string tail) =>
        string.IsNullOrWhiteSpace(summary) ? tail : $"{summary} {tail}";

    private void RaiseState()
    {
        OnPropertyChanged(nameof(IsChecking));
        OnPropertyChanged(nameof(IsDownloading));
        OnPropertyChanged(nameof(IsReady));
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(ShowBadge));
        OnPropertyChanged(nameof(BadgeText));
        OnPropertyChanged(nameof(BadgeToolTip));
        OnPropertyChanged(nameof(StatusIcon));
        OnPropertyChanged(nameof(StatusTone));
        OnPropertyChanged(nameof(HeadlineText));
        OnPropertyChanged(nameof(DetailText));
        OnPropertyChanged(nameof(ShowCheckButton));
        OnPropertyChanged(nameof(ShowInstallButton));
        OnPropertyChanged(nameof(ShowStatusInstallButton));
        OnPropertyChanged(nameof(ShowCancelButton));
        OnPropertyChanged(nameof(UpdateVersion));
        OnPropertyChanged(nameof(UpdateSummary));
        OnPropertyChanged(nameof(ShowUpdateSummary));
        OnPropertyChanged(nameof(InstalledSummary));
        OnPropertyChanged(nameof(ShowInstalledSummary));
        OnPropertyChanged(nameof(ShowUpdatedBanner));
        OnPropertyChanged(nameof(AutoInstallDescription));
        CheckCommand.NotifyCanExecuteChanged();
        InstallCommand.NotifyCanExecuteChanged();
        CancelDownloadCommand.NotifyCanExecuteChanged();
        SkipCommand.NotifyCanExecuteChanged();
        OpenInstalledReleaseCommand.NotifyCanExecuteChanged();
    }
}

/// <summary>One "What's new" line, ready to bind: a category chip, a lead line (bold when it's a title) and an optional detail.</summary>
public sealed record WhatsNewItem(string? Category, string? Title, string Text) : StaticRecord
{
    public bool HasCategory => Category is not null;

    public bool HasTitle => Title is not null;

    /// <summary>The title, or the sentence itself when the item has no title.</summary>
    public string Lead => Title ?? Text;

    /// <summary>The sentence under a title (null when the item has no title).</summary>
    public string? Detail => Title is null ? null : Text;

    public bool HasDetail => Detail is not null;
}

/// <summary>The concise "What's new" summary of one release, ready to bind (see <see cref="ReleaseNotesSummary"/>).</summary>
public sealed class ReleaseSummaryViewModel : StaticItem
{
    private ReleaseSummaryViewModel(string version, ReleaseNotesSummary summary, DateTimeOffset? published)
    {
        Version = version;
        Intro = summary.Intro;
        Items = summary.Items.Select(i => new WhatsNewItem(i.Category, i.Title, i.Text)).ToArray();
        MoreText = summary.MoreCount switch
        {
            0 => null,
            1 => "…and 1 more change in the full release notes.",
            int n => $"…and {n} more changes in the full release notes.",
        };
        PublishedText = published is { } p ? $"Released {p.ToLocalTime().ToString("MMMM d, yyyy", CultureInfo.CurrentCulture)}" : null;
        OneLine = summary.ToOneLine();
    }

    public string Version { get; }

    public string Heading => $"What's new in {Version}";

    public string? Intro { get; }

    public bool HasIntro => Intro is not null;

    public IReadOnlyList<WhatsNewItem> Items { get; }

    public bool HasItems => Items.Count > 0;

    /// <summary>True when the notes had nothing to summarize (the card then just links to the release page).</summary>
    public bool IsEmpty => Intro is null && Items.Count == 0;

    public string? MoreText { get; }

    public string? PublishedText { get; }

    /// <summary>Short line for notifications ("Overview dashboard, Alerts and 3 more.").</summary>
    public string OneLine { get; }

    public static ReleaseSummaryViewModel From(string version, string? notes, DateTimeOffset? published) =>
        new(version, ReleaseNotesSummary.Parse(notes), published);
}
