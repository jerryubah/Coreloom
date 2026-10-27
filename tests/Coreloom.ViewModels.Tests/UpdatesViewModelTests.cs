using System.Net;
using System.Security.Cryptography;
using System.Text;
using Coreloom.Core;
using Coreloom.Core.Settings;
using Coreloom.Core.Updates;
using Coreloom.ViewModels.Services;

namespace Coreloom.ViewModels.Tests;

/// <summary>Serves a fake GitHub release (API listing, SHA256SUMS.txt, installer and portable zip) for the updater.</summary>
internal sealed class FakeGitHub : HttpMessageHandler
{
    public string Version { get; set; } = "1.1.0";

    public byte[] Installer { get; set; } = Encoding.ASCII.GetBytes("pretend this is Coreloom-Setup.exe");

    public byte[] Portable { get; set; } = Encoding.ASCII.GetBytes("pretend this is Coreloom-Portable.zip");

    /// <summary>Overrides the hash written to SHA256SUMS.txt (to simulate a corrupted download).</summary>
    public string? ChecksumOverride { get; set; }

    public bool Fail { get; set; }

    public string Notes { get; set; } = "A faster, calmer Coreloom.\n\n### Added\n- **Automatic updates.** New versions install themselves.\n- **What's new.** A short summary of every release.\n\n### Fixed\n- Tray icon stays put.";

    public int InstallerRequests { get; private set; }

    public int PortableRequests { get; private set; }

    /// <summary>False to publish a release without the portable zip.</summary>
    public bool IncludePortable { get; set; } = true;

    public int TagRequests { get; private set; }

    public string InstallerName => $"Coreloom-{Version}-win-x64-Setup.exe";

    public string PortableName => $"Coreloom-{Version}-win-x64-Portable.zip";

    public string ReleaseUrl => $"https://github.com/owner/repo/releases/tag/v{Version}";

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (Fail)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        }

        string url = request.RequestUri!.ToString();
        if (url.StartsWith("https://api.github.com/repos/owner/repo/releases/tags/", StringComparison.Ordinal))
        {
            TagRequests++;
            string tag = url[(url.LastIndexOf('/') + 1)..];
            if (tag != "v" + Version)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            string single = $$"""
                { "tag_name": "v{{Version}}", "name": "Coreloom {{Version}}", "body": {{System.Text.Json.JsonSerializer.Serialize(Notes)}},
                  "draft": false, "prerelease": false, "html_url": "{{ReleaseUrl}}", "published_at": "2026-10-20T10:00:00Z", "assets": [] }
                """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(single, Encoding.UTF8, "application/json") });
        }

        if (url.StartsWith("https://api.github.com/repos/owner/repo/releases", StringComparison.Ordinal))
        {
            string json = $$"""
                [ { "tag_name": "v{{Version}}", "name": "Coreloom {{Version}}", "body": {{System.Text.Json.JsonSerializer.Serialize(Notes)}},
                    "draft": false, "prerelease": false, "html_url": "{{ReleaseUrl}}", "published_at": "2026-10-20T10:00:00Z",
                    "assets": [
                      { "name": "{{InstallerName}}", "size": {{Installer.Length}}, "browser_download_url": "https://example.test/{{InstallerName}}" },
                      {{(IncludePortable ? $$"""{ "name": "{{PortableName}}", "size": {{Portable.Length}}, "browser_download_url": "https://example.test/{{PortableName}}" },""" : "")}}
                      { "name": "SHA256SUMS.txt", "size": 100, "browser_download_url": "https://example.test/SHA256SUMS.txt" } ] } ]
                """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }

        if (url == "https://example.test/SHA256SUMS.txt")
        {
            string hash = ChecksumOverride ?? Convert.ToHexStringLower(SHA256.HashData(Installer));
            string zipHash = ChecksumOverride ?? Convert.ToHexStringLower(SHA256.HashData(Portable));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($"{hash}  {InstallerName}\n{zipHash}  {PortableName}\n") });
        }

        if (url == $"https://example.test/{InstallerName}")
        {
            InstallerRequests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Installer) });
        }

        if (url == $"https://example.test/{PortableName}")
        {
            PortableRequests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Portable) });
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}

internal sealed class RecordingInstaller : IUpdateInstaller
{
    public bool CanInstallUpdates { get; set; } = true;

    public UpdatePackageKind PackageKind { get; set; } = UpdatePackageKind.Installer;

    public bool Result { get; set; } = true;

    public bool PrepareResult { get; set; } = true;

    public List<(string Path, byte[] Content)> Prepared { get; } = [];

    public List<(string Path, UpdateInstallOptions Options, byte[] Content)> Launches { get; } = [];

    public bool Prepare(VerifiedPackage package)
    {
        using var stream = package.OpenRead();
        var copy = new MemoryStream();
        stream.CopyTo(copy);
        Prepared.Add((package.Path, copy.ToArray()));
        return PrepareResult;
    }

    public bool LaunchAndExit(VerifiedPackage package, UpdateInstallOptions options)
    {
        Launches.Add((package.Path, options, File.ReadAllBytes(package.Path)));
        return Result;
    }
}

internal sealed class RecordingShell : IShellService
{
    public List<string> OpenedUrls { get; } = [];

    public void OpenUrl(string url) => OpenedUrls.Add(url);

    public void OpenFolder(string path)
    {
    }

    public void OpenFile(string path)
    {
    }

    public void CopyToClipboard(string text)
    {
    }

    public string? PickSaveFile(string suggestedName, string filter) => null;

    public void ShowMessage(string title, string message)
    {
    }

    public bool Confirm(string title, string message) => false;
}

public sealed class UpdatesViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "coreloom-upd-vm-" + Guid.NewGuid().ToString("N"));
    private readonly FakeGitHub _github = new();
    private readonly RecordingInstaller _installer = new();
    private readonly RecordingShell _shell = new();
    private readonly InMemoryUpdateTrustStore _trust = new();
    private readonly List<SettingsService> _settings = [];
    private DateTime _now = new(2026, 10, 21, 9, 0, 0, DateTimeKind.Utc);

    private string Downloads => Path.Combine(_dir, "Updates");

    private SettingsService Settings(Action<AppSettings>? configure = null)
    {
        var settings = new AppSettings().Normalize();
        configure?.Invoke(settings);
        var service = new SettingsService(new SettingsStore(Path.Combine(_dir, "settings.json")), settings);
        _settings.Add(service);
        return service;
    }

    private UpdatesViewModel Create(SettingsService? settings = null, string version = "1.0.0", string? repository = "owner/repo") =>
        new(new AppVersionInfo(SemanticVersion.Parse(version), version, repository, null), settings ?? Settings(), _installer, _shell,
            new HttpClient(_github), new PendingUpdateStore(Downloads), _trust, () => _now);

    [Fact]
    public async Task AutomaticUpdate_DownloadsVerifiesAndWaits_ThenInstallUpdateInstallsIt()
    {
        var updates = Create();
        updates.InitializeFromDisk();
        var notes = new List<UpdateNotification>();
        updates.NotificationRequested += notes.Add;

        await updates.CheckAsync(userInitiated: false);

        Assert.Equal(UpdatePhase.Ready, updates.Phase);
        Assert.True(updates.ShowBadge);
        Assert.Equal("Install update", updates.BadgeText);
        Assert.Equal("Coreloom 1.1.0 is ready to install", updates.HeadlineText);
        Assert.True(updates.ShowInstallButton);
        Assert.Contains("next time Coreloom starts", updates.DetailText);
        Assert.Equal(1, _github.InstallerRequests);
        Assert.Equal(0, _github.PortableRequests);
        Assert.Equal("1.1.0", new PendingUpdateStore(Downloads).LoadPending()!.Version);
        Assert.Equal("1.1.0", _trust.Load()!.Value.Version);

        var note = Assert.Single(notes);
        Assert.Equal("Coreloom 1.1.0 is ready to install", note.Title);
        Assert.StartsWith("Automatic updates, What's new and 1 more.", note.Message);
        Assert.False(note.UserInitiated);

        var summary = updates.UpdateSummary!;
        Assert.Equal("What's new in 1.1.0", summary.Heading);
        Assert.Equal("A faster, calmer Coreloom.", summary.Intro);
        Assert.Equal(3, summary.Items.Count);
        Assert.Equal("New", summary.Items[0].Category);
        Assert.Equal("Automatic updates", summary.Items[0].Title);
        Assert.Equal("Fixed", summary.Items[2].Category);
        Assert.Null(summary.MoreText);

        await updates.InstallCommand.ExecuteAsync(null);

        Assert.Equal(_github.Installer, Assert.Single(_installer.Prepared).Content);
        var launch = Assert.Single(_installer.Launches);
        Assert.Equal(Path.Combine(Downloads, _github.InstallerName), launch.Path);
        Assert.Equal(UpdateInstallOptions.Interactive, launch.Options);
        Assert.Equal(_github.Installer, launch.Content);
        Assert.Equal(UpdatePhase.Installing, updates.Phase);
        Assert.Equal(1, new PendingUpdateStore(Downloads).LoadPending()!.InstallAttempts);
    }

    [Fact]
    public async Task AutomaticUpdatesOff_OnlyAnnounces_UntilInstallUpdateDownloadsAndInstallsIt()
    {
        var updates = Create(Settings(s => s.Updates.AutoInstall = false));
        var notes = new List<UpdateNotification>();
        updates.NotificationRequested += notes.Add;

        await updates.CheckAsync(userInitiated: false);

        Assert.Equal(UpdatePhase.Available, updates.Phase);
        Assert.Equal(0, _github.InstallerRequests);
        Assert.True(updates.ShowBadge);
        Assert.Equal("Update available", updates.BadgeText);
        Assert.True(updates.ShowInstallButton);
        Assert.Contains("closes Coreloom for a moment", updates.DetailText);
        Assert.Equal("Coreloom 1.1.0 is available", Assert.Single(notes).Title);
        Assert.False(updates.TryInstallAtStartup(startHidden: false)); // nothing installs by itself while it's off

        await updates.InstallCommand.ExecuteAsync(null); // one click: download, verify, install

        Assert.Equal(1, _github.InstallerRequests);
        Assert.Equal(UpdatePhase.Installing, updates.Phase);
        var launch = Assert.Single(_installer.Launches);
        Assert.Equal(UpdateInstallOptions.Interactive, launch.Options);
        Assert.Equal(_github.Installer, launch.Content);
        Assert.Single(notes); // no "ready to install" toast for an update that installs right away
    }

    [Fact]
    public async Task TurningAutomaticUpdatesOn_StartsTheDownload()
    {
        var updates = Create(Settings(s => s.Updates.AutoInstall = false));
        await updates.CheckAsync(userInitiated: true);
        Assert.Equal(UpdatePhase.Available, updates.Phase);

        updates.AutoInstall = true;
        await WaitFor(() => updates.Phase == UpdatePhase.Ready);

        Assert.Equal(1, _github.InstallerRequests);
    }

    [Fact]
    public async Task PortableCopy_DownloadsThePortableZip_AndInstallsItInPlace()
    {
        _installer.PackageKind = UpdatePackageKind.PortableZip;
        var settings = Settings();
        var updates = Create(settings);
        updates.InitializeFromDisk();

        await updates.CheckAsync(userInitiated: false);

        Assert.Equal(UpdatePhase.Ready, updates.Phase);
        Assert.True(updates.ShowInstallButton);
        Assert.Equal(1, _github.PortableRequests);
        Assert.Equal(0, _github.InstallerRequests);
        Assert.Equal(_github.PortableName, new PendingUpdateStore(Downloads).LoadPending()!.InstallerFileName);

        // A restart keeps the downloaded zip, and the next start installs it.
        var nextStart = Create(settings);
        nextStart.InitializeFromDisk();
        Assert.Equal(UpdatePhase.Ready, nextStart.Phase);
        Assert.True(nextStart.TryInstallAtStartup(startHidden: false));

        var prepared = Assert.Single(_installer.Prepared);
        Assert.Equal(Path.Combine(Downloads, _github.PortableName), prepared.Path);
        Assert.Equal(_github.Portable, prepared.Content);
        Assert.Equal(UpdateInstallOptions.Interactive, Assert.Single(_installer.Launches).Options);
        Assert.Empty(_shell.OpenedUrls); // never sent to GitHub
    }

    [Fact]
    public async Task ADownloadForAnotherKindOfCopy_IsDiscarded()
    {
        var settings = Settings();
        await Create(settings).CheckAsync(userInitiated: false); // downloaded the installer...

        _installer.PackageKind = UpdatePackageKind.PortableZip; // ...but this copy installs from the zip
        var updates = Create(settings);
        updates.InitializeFromDisk();

        Assert.Equal(UpdatePhase.Idle, updates.Phase);
        Assert.Null(new PendingUpdateStore(Downloads).LoadPending());
        Assert.False(updates.TryInstallAtStartup(startHidden: false));
    }

    [Fact]
    public async Task AnUpdateThatCantBeUnpacked_StaysReady_AndExplains()
    {
        _installer.PackageKind = UpdatePackageKind.PortableZip;
        _installer.PrepareResult = false;
        var updates = Create();
        await updates.CheckAsync(userInitiated: false);

        await updates.InstallCommand.ExecuteAsync(null);

        Assert.Single(_installer.Prepared);
        Assert.Empty(_installer.Launches);
        Assert.Equal(UpdatePhase.Ready, updates.Phase);
        Assert.Contains("couldn't be unpacked", updates.DetailText);
    }

    [Fact]
    public async Task AReleaseWithoutThePackageThisCopyNeeds_Explains()
    {
        _installer.PackageKind = UpdatePackageKind.PortableZip;
        _github.IncludePortable = false;
        var updates = Create();

        await updates.CheckAsync(userInitiated: false);

        Assert.Equal(UpdatePhase.Available, updates.Phase);
        Assert.Contains("doesn't include the portable zip", updates.DetailText);
        Assert.Equal(0, _github.InstallerRequests);
        Assert.Equal(0, _github.PortableRequests);
    }

    [Fact]
    public async Task ChecksumMismatch_DeletesTheDownload_AndExplains()
    {
        _github.ChecksumOverride = new string('0', 64);
        var updates = Create();

        await updates.CheckAsync(userInitiated: true);

        Assert.Equal(UpdatePhase.Available, updates.Phase);
        Assert.Contains("SHA-256", updates.DetailText);
        Assert.Null(new PendingUpdateStore(Downloads).LoadPending());
        Assert.Empty(Directory.GetFiles(Downloads, "*Setup*"));
        Assert.Null(_trust.Load());
    }

    [Fact]
    public async Task NextStart_RestoresTheDownloadedUpdate_AndInstallsIt()
    {
        var settings = Settings();
        var first = Create(settings);
        first.InitializeFromDisk();
        await first.CheckAsync(userInitiated: false);
        Assert.Equal(UpdatePhase.Ready, first.Phase);

        var second = Create(settings);
        second.InitializeFromDisk();

        Assert.Equal(UpdatePhase.Ready, second.Phase);
        Assert.Equal("1.1.0", second.UpdateVersion);
        Assert.Equal(3, second.UpdateSummary!.Items.Count);
        Assert.True(second.TryInstallAtStartup(startHidden: true));
        Assert.Equal(UpdateInstallOptions.Background, Assert.Single(_installer.Launches).Options);
    }

    [Fact]
    public async Task FailedInstall_StopsAutomaticAttempts_ButKeepsTheUpdate()
    {
        var settings = Settings();
        var first = Create(settings);
        await first.CheckAsync(userInitiated: false);

        var afterFailure = Create(settings);
        afterFailure.InitializeFromDisk(launchedAfterFailedUpdate: true);

        Assert.Equal(UpdatePhase.Ready, afterFailure.Phase);
        Assert.Contains("couldn't finish installing", afterFailure.DetailText);
        Assert.False(afterFailure.TryInstallAtStartup(startHidden: false));
        Assert.Empty(_installer.Launches);

        await afterFailure.InstallCommand.ExecuteAsync(null); // the user can still try again
        Assert.Single(_installer.Launches);
    }

    [Fact]
    public async Task RepeatedStartupAttempts_AreCapped()
    {
        var settings = Settings();
        await Create(settings).CheckAsync(userInitiated: false);
        _installer.Result = true;

        for (int i = 0; i < 4; i++)
        {
            var updates = Create(settings);
            updates.InitializeFromDisk();
            updates.TryInstallAtStartup(startHidden: false);
        }

        Assert.Equal(UpdateInstallPolicy.MaxAutomaticAttempts, _installer.Launches.Count);
    }

    [Fact]
    public void PendingFileWithoutTrustedHash_IsDeleted()
    {
        Directory.CreateDirectory(Downloads);
        byte[] planted = [6, 6, 6];
        File.WriteAllBytes(Path.Combine(Downloads, "Coreloom-9.9.9-win-x64-Setup.exe"), planted);
        new PendingUpdateStore(Downloads).SavePending(new PendingUpdate
        {
            Version = "9.9.9",
            InstallerFileName = "Coreloom-9.9.9-win-x64-Setup.exe",
            Sha256 = Convert.ToHexStringLower(SHA256.HashData(planted)),
        });

        var updates = Create();
        updates.InitializeFromDisk();

        Assert.Equal(UpdatePhase.Idle, updates.Phase);
        Assert.False(updates.TryInstallAtStartup(startHidden: true));
        Assert.Empty(_installer.Launches);
        Assert.Null(new PendingUpdateStore(Downloads).LoadPending());
        Assert.Empty(Directory.GetFiles(Downloads, "*Setup*"));
    }

    [Fact]
    public async Task ChangedInstaller_IsNeverStarted()
    {
        var updates = Create();
        await updates.CheckAsync(userInitiated: false);
        File.WriteAllBytes(Path.Combine(Downloads, _github.InstallerName), [1, 2, 3]);

        Assert.False(updates.InstallNow(UpdateInstallOptions.Interactive));

        Assert.Empty(_installer.Launches);
        Assert.Equal(UpdatePhase.Failed, updates.Phase);
        Assert.Equal("Couldn't install the update", updates.HeadlineText);
        Assert.Contains("missing or damaged", updates.DetailText);
        Assert.Null(new PendingUpdateStore(Downloads).LoadPending());
    }

    [Fact]
    public async Task InstallerThatWontStart_LeavesTheUpdateReady()
    {
        _installer.Result = false;
        var updates = Create();
        await updates.CheckAsync(userInitiated: false);

        Assert.False(updates.InstallNow(UpdateInstallOptions.Interactive));

        Assert.Equal(UpdatePhase.Ready, updates.Phase);
        Assert.Contains("couldn't be started", updates.DetailText);
    }

    [Fact]
    public async Task BackgroundInstall_WaitsForAnIdlePc()
    {
        var updates = Create();
        await updates.CheckAsync(userInitiated: false);
        var idle = new BackgroundInstallSignals { WindowInUse = false, UserIdle = TimeSpan.FromHours(1), SystemQuiet = TimeSpan.FromHours(1) };

        Assert.False(updates.TryInstallInBackground(idle)); // just downloaded, just started

        _now = _now.AddMinutes(11);
        Assert.False(updates.TryInstallInBackground(idle with { WindowInUse = true }));
        Assert.False(updates.TryInstallInBackground(idle with { SystemQuiet = TimeSpan.FromMinutes(2) }));
        Assert.True(updates.TryInstallInBackground(idle));
        Assert.Equal(UpdateInstallOptions.Background, Assert.Single(_installer.Launches).Options);
    }

    [Fact]
    public void FirstStartAfterAnUpdate_ShowsTheBannerAndWhatsNew()
    {
        var settings = Settings(s => s.Updates.LastRunVersion = "1.0.0");
        Directory.CreateDirectory(Downloads);
        File.WriteAllText(Path.Combine(Downloads, "Coreloom-1.1.0-win-x64-Setup.exe"), "old installer");
        new PendingUpdateStore(Downloads).SavePending(new PendingUpdate
        {
            Version = "1.1.0",
            InstallerFileName = "Coreloom-1.1.0-win-x64-Setup.exe",
            Sha256 = new string('b', 64),
            Notes = _github.Notes,
            HtmlUrl = "https://github.com/owner/repo/releases/tag/v1.1.0",
        });
        _trust.TrySave("1.1.0", new string('b', 64));

        var updates = Create(settings, version: "1.1.0");
        updates.InitializeFromDisk();

        Assert.True(updates.ShowUpdatedBanner);
        Assert.Equal("Coreloom was updated to 1.1.0.", updates.UpdatedBannerText);
        Assert.True(updates.ShowInstalledSummary);
        Assert.Equal(3, updates.InstalledSummary!.Items.Count);
        Assert.Equal("https://github.com/owner/repo/releases/tag/v1.1.0", updates.InstalledReleaseUrl);
        Assert.StartsWith("Automatic updates, What's new and 1 more.", updates.UpdatedNotification!.Message);
        Assert.Null(new PendingUpdateStore(Downloads).LoadPending());
        Assert.Empty(Directory.GetFiles(Downloads, "*Setup*"));
        Assert.Null(_trust.Load());

        Assert.True(updates.JustUpdated);
        var nextStart = Create(settings, version: "1.1.0");
        nextStart.InitializeFromDisk();
        Assert.True(nextStart.ShowUpdatedBanner);     // the banner waits to be dismissed...
        Assert.Null(nextStart.UpdatedNotification);   // ...but the notification is only shown once
        Assert.Equal(3, nextStart.InstalledSummary!.Items.Count);

        updates.DismissUpdatedBannerCommand.Execute(null);

        Assert.False(updates.ShowUpdatedBanner);
        Assert.Null(settings.Current.Updates.WhatsNewVersion);
        Assert.True(updates.ShowInstalledSummary); // "What's new in this version" stays in Settings
    }

    [Fact]
    public async Task UpdateFromAVersionThatLeftNoNotes_StillShowsWhatsNew()
    {
        // 1.0.0 didn't record which version ran, and its own updater (or a manual install) leaves no notes behind.
        var settings = Settings(s => s.Updates.LastCheckUtc = new DateTime(2026, 10, 14, 8, 0, 0, DateTimeKind.Utc));
        var updates = Create(settings, version: "1.1.0");
        updates.InitializeFromDisk();

        Assert.True(updates.ShowUpdatedBanner);
        Assert.True(updates.ShowInstalledSummary);
        Assert.Null(updates.InstalledSummary);
        Assert.Contains("isn't available", updates.InstalledSummaryFallback);

        await updates.LoadInstalledNotesIfMissingAsync();

        Assert.Equal(1, _github.TagRequests);
        Assert.Equal(3, updates.InstalledSummary!.Items.Count);
        Assert.Equal("A faster, calmer Coreloom.", updates.InstalledSummary.Intro);

        await updates.LoadInstalledNotesIfMissingAsync(); // already have them: no second request
        Assert.Equal(1, _github.TagRequests);
    }

    [Fact]
    public async Task FreshInstall_ShowsNoBanner_AndFetchesNothing()
    {
        var updates = Create(version: "1.1.0");
        updates.InitializeFromDisk();
        await updates.LoadInstalledNotesIfMissingAsync();

        Assert.False(updates.ShowUpdatedBanner);
        Assert.False(updates.ShowInstalledSummary);
        Assert.Equal(0, _github.TagRequests);
    }

    [Fact]
    public async Task Skip_DiscardsTheDownload_AndLaterChecksIgnoreThatVersion()
    {
        var settings = Settings();
        var updates = Create(settings);
        await updates.CheckAsync(userInitiated: false);

        updates.SkipCommand.Execute(null);

        Assert.Equal(UpdatePhase.UpToDate, updates.Phase);
        Assert.False(updates.ShowBadge);
        Assert.Equal("1.1.0", settings.Current.Updates.SkippedVersion);
        Assert.Null(new PendingUpdateStore(Downloads).LoadPending());

        await updates.CheckAsync(userInitiated: false);
        Assert.Equal(UpdatePhase.UpToDate, updates.Phase);
        Assert.Equal(1, _github.InstallerRequests);
    }

    [Fact]
    public async Task NewerRelease_ReplacesAnOlderDownload()
    {
        var updates = Create();
        await updates.CheckAsync(userInitiated: false);
        string oldInstaller = Path.Combine(Downloads, _github.InstallerName);
        Assert.True(File.Exists(oldInstaller));

        _github.Version = "1.2.0";
        _github.Installer = Encoding.ASCII.GetBytes("newer installer");
        await updates.CheckAsync(userInitiated: true);

        Assert.Equal(UpdatePhase.Ready, updates.Phase);
        Assert.Equal("1.2.0", updates.UpdateVersion);
        Assert.False(File.Exists(oldInstaller));
        Assert.Equal("1.2.0", _trust.Load()!.Value.Version);
    }

    [Fact]
    public async Task FailedCheck_KeepsAReadyUpdate_OtherwiseExplains()
    {
        var updates = Create();
        _github.Fail = true;
        await updates.CheckAsync(userInitiated: true);
        Assert.Equal(UpdatePhase.Failed, updates.Phase);
        Assert.Equal("Couldn't check for updates", updates.HeadlineText);
        Assert.False(updates.ShowBadge);

        _github.Fail = false;
        await updates.CheckAsync(userInitiated: true);
        Assert.Equal(UpdatePhase.Ready, updates.Phase);

        _github.Fail = true;
        await updates.CheckAsync(userInitiated: true);
        Assert.Equal(UpdatePhase.Ready, updates.Phase);
        Assert.True(updates.ShowBadge);
    }

    [Fact]
    public async Task UpToDate_WhenTheLatestReleaseIsRunning()
    {
        var updates = Create(version: "1.1.0");
        await updates.CheckAsync(userInitiated: true);

        Assert.Equal(UpdatePhase.UpToDate, updates.Phase);
        Assert.Equal("You're up to date", updates.HeadlineText);
        Assert.Contains("is the latest version", updates.DetailText);
        Assert.False(updates.ShowBadge);
        Assert.True(updates.ShowCheckButton);
    }

    [Fact]
    public async Task LocalBuild_WithoutARepository_IsNotConfigured()
    {
        var updates = Create(repository: "OWNER/Coreloom");
        Assert.Equal(UpdatePhase.NotConfigured, updates.Phase);
        Assert.False(updates.CheckCommand.CanExecute(null));
        await updates.CheckAsync(userInitiated: true);
        Assert.Equal(UpdatePhase.NotConfigured, updates.Phase);
    }

    [Fact]
    public async Task ShellBadge_FollowsTheUpdater()
    {
        var (monitor, settings, _) = Fixture.Create(_dir);
        _settings.Add(settings);
        var updates = Create(settings);
        var shell = new ShellViewModel(new ShellDependencies
        {
            Monitor = monitor,
            Settings = settings,
            Alerts = new Coreloom.Core.Alerts.AlertEngine(TimeSpan.FromMinutes(10)),
            Theme = new NullTheme(),
            Startup = new NullStartup(),
            Elevation = new NullStartup(),
            Shell = _shell,
            Lifetime = new NullStartup(),
            Diagnostics = new NullStartup(),
            Updates = updates,
            App = new AppVersionInfo(SemanticVersion.Parse("1.0.0"), "1.0.0", "owner/repo", null),
        });
        var raised = new List<string?>();
        shell.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        Assert.False(shell.IsUpdateAvailable);
        await updates.CheckAsync(userInitiated: false);

        Assert.True(shell.IsUpdateAvailable);
        Assert.Contains(nameof(ShellViewModel.IsUpdateAvailable), raised);
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        for (int i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), "Timed out waiting for the condition.");
    }

    public void Dispose()
    {
        foreach (var s in _settings)
        {
            s.Dispose();
        }

        try
        {
            Directory.Delete(_dir, true);
        }
        catch
        {
            // ignore
        }
    }

    private sealed class NullTheme : IThemeService
    {
        public bool IsMicaSupported => false;

        public void Apply(AppearanceSettings appearance)
        {
        }
    }

    private sealed class NullStartup : IStartupService, IElevationService, IAppLifetime, IDiagnosticsService
    {
        public bool IsStartAtLogonRegistered => false;

        public bool IsElevated => true;

        public string LogDirectory => Path.GetTempPath();

        public Task<bool> SetStartAtLogonAsync(bool enabled, bool minimized) => Task.FromResult(true);

        public void Restart()
        {
        }

        public void Exit()
        {
        }

        public void SetVerboseLogging(bool enabled)
        {
        }

        public IReadOnlyList<Coreloom.Core.Diagnostics.LogEntry> RecentLog() => [];
    }

    [Fact]
    public async Task AFailedCheck_LeavesAfterFiveSeconds()
    {
        using var timer = new ManualDismissals();
        var updates = Create();
        _github.Fail = true;
        await updates.CheckAsync(userInitiated: true);
        Assert.Equal(UpdatePhase.Failed, updates.Phase);
        Assert.Equal("Couldn't check for updates", updates.HeadlineText);

        timer.Elapse();
        Assert.Equal(UpdatePhase.Idle, updates.Phase);
        Assert.DoesNotContain("Couldn't", updates.HeadlineText);
        Assert.Equal(0, timer.Waiting);

        _github.Fail = false;
        await updates.CheckAsync(userInitiated: true);
        Assert.Equal(0, timer.Waiting); // nothing went wrong: nothing to dismiss
    }

}
