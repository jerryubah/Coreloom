using System.IO;
using System.Net;
using System.Net.Http;
using Coreloom.Core;
using Coreloom.Core.Connect;
using Coreloom.Core.Diagnostics;
using Coreloom.Core.Settings;
using Coreloom.Core.Updates;
using Coreloom.ViewModels;
using Coreloom.ViewModels.Connect;
using Coreloom.ViewModels.Pages;
using Coreloom.ViewModels.Services;

namespace Coreloom.PageShots;

/// <summary>Settings with Coreloom Connect turned on, on a home Wi-Fi address, with two paired phones.</summary>
internal static class SampleSettings
{
    public static (SettingsViewModel Page, ConnectHost Host) Create(SampleData data)
    {
        string folder = Path.Combine(Path.GetTempPath(), "coreloom-shots-connect-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var phones = new PairedDeviceStore(Path.Combine(folder, "connect.json"));
        var now = DateTimeOffset.UtcNow;
        phones.Touch(phones.Add("Galaxy S25", "code", now.AddDays(-9)).Id, now.AddDays(-2));
        phones.Touch(phones.Add("Jerry's iPhone", "link", now.AddDays(-3)).Id, now.AddMinutes(-4));

        data.Settings.Current.Connect.Enabled = true;
        data.Settings.Current.Connect.Port = 47121;
        var stubs = new Stubs();
        var app = new AppVersionInfo(SemanticVersion.Parse("1.7.0"), "1.7.0", "jerryubah/Coreloom", null);
        ConnectHost? host = null;
        var page = data.Page(context =>
        {
            host = new ConnectHost(context, new ConnectEnvironment
            {
                DataDirectory = folder,
                Platform = new SamplePlatform(),
                FindAddresses = () =>
                [
                    new LocalAddress(IPAddress.Parse("192.168.1.24"), "Wi-Fi", true),
                    new LocalAddress(IPAddress.Parse("10.8.0.3"), "Ethernet 2", false),
                ],
            }, app.DisplayVersion);
            host.ApplyAsync().GetAwaiter().GetResult();
            return new SettingsViewModel(context, new SettingsDependencies
            {
                Theme = stubs,
                Startup = stubs,
                Elevation = stubs,
                Shell = stubs,
                Lifetime = stubs,
                Diagnostics = stubs,
                Updates = new UpdatesViewModel(app, data.Settings, stubs, stubs, new HttpClient(new SampleReleases()), Path.GetTempPath()),
                App = app,
                Connect = host,
            });
        });
        page.SelectedSection = "Connect";
        return (page, host!);
    }

    /// <summary>The whole app (sidebar and Overview) with no update waiting, for the website's picture of Coreloom on Windows.</summary>
    public static ShellViewModel Shell(SampleData data)
    {
        var stubs = new Stubs();
        var app = new AppVersionInfo(SemanticVersion.Parse("1.8.1"), "1.8.1", "jerryubah/Coreloom", null);
        return new ShellViewModel(new ShellDependencies
        {
            Monitor = data.Monitor,
            Settings = data.Settings,
            Alerts = new Coreloom.Core.Alerts.AlertEngine(TimeSpan.FromMinutes(10)),
            Theme = stubs,
            Startup = stubs,
            Elevation = stubs,
            Shell = stubs,
            Lifetime = stubs,
            Diagnostics = stubs,
            Updates = new UpdatesViewModel(app, data.Settings, stubs, stubs, new HttpClient(new SampleReleases(none: true)), Path.GetTempPath()),
            App = app,
            Tools = data.Tools,
            Platform = HostPlatform.Windows,
        });
    }

    /// <summary>Windows' side of Connect without touching the firewall: says the network is Public, to show the note.</summary>
    private sealed class SamplePlatform : IConnectPlatform
    {
        public bool CanOpenNetworkSettings => true;

        public Task<string?> AllowAsync(int port, CancellationToken token) =>
            Task.FromResult<string?>("Your network is set to Public, so Windows keeps phones out. Set it to Private in Windows' network settings to connect your phone.");

        public Task RemoveAsync(CancellationToken token) => Task.CompletedTask;

        public void OpenNetworkSettings()
        {
        }
    }

    internal sealed class Stubs : IThemeService, IStartupService, IElevationService, IShellService, IAppLifetime, IDiagnosticsService, IUpdateInstaller
    {
        public bool IsMicaSupported => true;

        public void Apply(AppearanceSettings appearance)
        {
        }

        public bool IsStartAtLogonRegistered => true;

        public Task<bool> SetStartAtLogonAsync(bool enabled, bool minimized) => Task.FromResult(true);

        public bool IsElevated => true;

        public void OpenUrl(string url)
        {
        }

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

        public void Restart()
        {
        }

        public void Exit()
        {
        }

        public string LogDirectory => Path.GetTempPath();

        public void SetVerboseLogging(bool enabled)
        {
        }

        public IReadOnlyList<LogEntry> RecentLog() => [];

        public bool CanInstallUpdates => true;

        public UpdatePackageKind PackageKind => UpdatePackageKind.Installer;

        public bool Prepare(VerifiedPackage package) => false;

        public bool LaunchAndExit(VerifiedPackage package, UpdateInstallOptions options) => false;
    }
}

/// <summary>
/// GitHub's releases as the update check sees them: one newer release whose notes are the newest section of CHANGELOG.md
/// (so Settings › Updates shows an update and its "What's new").
/// </summary>
internal sealed class SampleReleases(bool none = false) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (none)
        {
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("[]", System.Text.Encoding.UTF8, "application/json") });
        }

        string notes = "A newer version.";
        string version = "9.9.9";
        if (File.Exists("CHANGELOG.md"))
        {
            var match = System.Text.RegularExpressions.Regex.Match(File.ReadAllText("CHANGELOG.md"), @"^## \[(\d+\.\d+\.\d+)\][^\n]*\n(.*?)(?=^## \[)",
                System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.Singleline);
            if (match.Success)
            {
                version = match.Groups[1].Value;
                notes = match.Groups[2].Value;
            }
        }

        var releases = new System.Text.Json.Nodes.JsonArray
        {
            new System.Text.Json.Nodes.JsonObject
            {
                ["tag_name"] = "v" + version,
                ["name"] = "Coreloom " + version,
                ["body"] = notes,
                ["html_url"] = $"https://github.com/jerryubah/Coreloom/releases/tag/v{version}",
                ["published_at"] = "2026-10-28T18:00:00Z",
                ["draft"] = false,
                ["prerelease"] = false,
                ["assets"] = new System.Text.Json.Nodes.JsonArray(),
            },
        };
        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(releases.ToJsonString(), System.Text.Encoding.UTF8, "application/json"),
        });
    }
}
