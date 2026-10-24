using System.Net;
using System.Text;
using System.Text.Json;
using Coreloom.Core.Updates;

namespace Coreloom.Core.Tests;

public class SemanticVersionTests
{
    [Theory]
    [InlineData("1.2.3", 1, 2, 3, null)]
    [InlineData("v1.2.3", 1, 2, 3, null)]
    [InlineData("V10.0.1-beta.2", 10, 0, 1, "beta.2")]
    [InlineData("1.2", 1, 2, 0, null)]
    [InlineData("1.2.3.0", 1, 2, 3, null)]
    [InlineData("1.2.3+abc123", 1, 2, 3, null)]
    public void Parse_Valid(string text, int major, int minor, int patch, string? pre)
    {
        var v = SemanticVersion.Parse(text);
        Assert.Equal(major, v.Major);
        Assert.Equal(minor, v.Minor);
        Assert.Equal(patch, v.Patch);
        Assert.Equal(pre, v.PreRelease);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("1")]
    [InlineData("1.2.3-")]
    [InlineData("1.2.3.4")]
    [InlineData("1.-2.3")]
    [InlineData("1.2.3-a..b")]
    public void Parse_Invalid(string text)
    {
        Assert.False(SemanticVersion.TryParse(text, out _));
    }

    [Fact]
    public void Precedence_FollowsSemVerSpec()
    {
        string[] ordered = ["1.0.0-alpha", "1.0.0-alpha.1", "1.0.0-alpha.beta", "1.0.0-beta", "1.0.0-beta.2", "1.0.0-beta.11", "1.0.0-rc.1", "1.0.0", "1.0.1", "1.1.0", "2.0.0"];
        for (int i = 0; i < ordered.Length - 1; i++)
        {
            Assert.True(SemanticVersion.Parse(ordered[i]) < SemanticVersion.Parse(ordered[i + 1]), $"{ordered[i]} < {ordered[i + 1]}");
        }

        Assert.Equal(SemanticVersion.Parse("1.0.0+build1"), SemanticVersion.Parse("v1.0.0"));
    }
}

public class GitHubReleaseClientTests
{
    private const string ReleasesJson = """
        [
          { "tag_name": "v1.3.0-beta.1", "name": "Beta", "body": "beta notes", "draft": false, "prerelease": true,
            "html_url": "https://github.com/o/r/releases/tag/v1.3.0-beta.1", "published_at": "2026-10-10T10:00:00Z", "assets": [] },
          { "tag_name": "v1.4.0", "name": "Draft", "body": "", "draft": true, "prerelease": false, "html_url": "https://github.com/o/r", "assets": [] },
          { "tag_name": "v1.2.0", "name": "Coreloom 1.2.0", "body": "## What's new\n- Faster charts", "draft": false, "prerelease": false,
            "html_url": "https://github.com/o/r/releases/tag/v1.2.0", "published_at": "2026-10-01T10:00:00Z",
            "assets": [
              { "name": "Coreloom-1.2.0-win-x64-Setup.exe", "size": 52000000, "browser_download_url": "https://github.com/o/r/releases/download/v1.2.0/Coreloom-1.2.0-win-x64-Setup.exe" },
              { "name": "Coreloom-1.2.0-win-x64-Portable.zip", "size": 61000000, "browser_download_url": "https://github.com/o/r/releases/download/v1.2.0/Coreloom-1.2.0-win-x64-Portable.zip" },
              { "name": "SHA256SUMS.txt", "size": 300, "browser_download_url": "https://github.com/o/r/releases/download/v1.2.0/SHA256SUMS.txt" },
              { "name": "insecure.exe", "size": 1, "browser_download_url": "http://example.com/insecure.exe" }
            ] },
          { "tag_name": "not-a-version", "draft": false, "prerelease": false, "assets": [] }
        ]
        """;

    [Fact]
    public void ParseReleases_SkipsDraftsAndInvalidTags_AndSelectsAssets()
    {
        using var doc = JsonDocument.Parse(ReleasesJson);
        var releases = GitHubReleaseClient.ParseReleases(doc.RootElement);

        Assert.Equal(2, releases.Count);
        var stable = GitHubReleaseClient.SelectLatest(releases, includePreReleases: false)!;
        Assert.Equal("1.2.0", stable.Version.ToString());
        Assert.Equal("Coreloom 1.2.0", stable.Title);
        Assert.Contains("Faster charts", stable.Notes);
        Assert.Equal("Coreloom-1.2.0-win-x64-Setup.exe", stable.Installer!.Name);
        Assert.Equal(52_000_000, stable.Installer.Size);
        Assert.Equal("Coreloom-1.2.0-win-x64-Portable.zip", stable.Portable!.Name);
        Assert.NotNull(stable.Checksums);
        Assert.DoesNotContain(stable.Assets, a => a.Name == "insecure.exe");

        var withPre = GitHubReleaseClient.SelectLatest(releases, includePreReleases: true)!;
        Assert.Equal("1.3.0-beta.1", withPre.Version.ToString());
        Assert.True(withPre.IsPreRelease);
    }

    [Fact]
    public void AReleaseWithMacAndWindowsDownloads_GivesEachCopyItsOwnPackage()
    {
        static ReleaseAsset Asset(string name) => new(name, new Uri("https://github.com/o/r/releases/download/v1.6.0/" + name), 1);
        var release = new ReleaseInfo(SemanticVersion.Parse("1.6.0"), "v1.6.0", "Coreloom 1.6.0", "", new Uri("https://github.com/o/r"), null, false,
        [
            // Stable names for the download buttons first, so they'd win if the updater matched them by mistake.
            Asset("Coreloom-Windows-Setup.exe"), Asset("Coreloom-macOS-Apple-Silicon.dmg"), Asset("Coreloom-macOS-Intel.dmg"),
            Asset("Coreloom-1.6.0-macos-arm64.dmg"), Asset("Coreloom-1.6.0-macos-x64.dmg"),
            Asset("Coreloom-1.6.0-macos-arm64.zip"), Asset("Coreloom-1.6.0-macos-x64.zip"),
            Asset("Coreloom-1.6.0-win-x64-Setup.exe"), Asset("Coreloom-1.6.0-win-x64-Portable.zip"), Asset("SHA256SUMS.txt"),
        ]);

        Assert.Equal("Coreloom-1.6.0-win-x64-Setup.exe", release.PackageFor(UpdatePackageKind.Installer)!.Name);
        Assert.Equal("Coreloom-1.6.0-win-x64-Portable.zip", release.PackageFor(UpdatePackageKind.PortableZip)!.Name);
        Assert.Equal("Coreloom-1.6.0-macos-arm64.zip", release.PackageFor(UpdatePackageKind.MacArm64)!.Name);
        Assert.Equal("Coreloom-1.6.0-macos-x64.zip", release.PackageFor(UpdatePackageKind.MacX64)!.Name);
        Assert.Null(release.MacApp(UpdatePackageKind.Installer));
        Assert.True(UpdatePackageKind.MacX64.IsMac());
        Assert.False(UpdatePackageKind.PortableZip.IsMac());
    }

    [Fact]
    public void MacPackages_AreSavedAsZips_AndCleanedUp()
    {
        string dir = Path.Combine(Path.GetTempPath(), "coreloom-mac-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var store = new PendingUpdateStore(dir);
        var pending = new PendingUpdate { Version = "1.6.0", InstallerFileName = "Coreloom-1.6.0-macos-arm64.zip", Sha256 = new string('a', 64) };
        Assert.Equal(Path.Combine(dir, "Coreloom-1.6.0-macos-arm64.zip"), store.PackagePath(pending, UpdatePackageKind.MacArm64));
        Assert.Null(store.PackagePath(pending with { InstallerFileName = "Coreloom.exe" }, UpdatePackageKind.MacArm64));

        File.WriteAllText(Path.Combine(dir, "Coreloom-1.5.9-macos-arm64.zip"), "old");
        File.WriteAllText(Path.Combine(dir, "Coreloom-1.6.0-macos-x64.zip"), "current");
        File.WriteAllText(Path.Combine(dir, "Coreloom-1.7.0-macos-arm64.zip"), "newer");
        store.DeletePackagesUpTo(SemanticVersion.Parse("1.6.0"));
        Assert.Equal(new[] { "Coreloom-1.7.0-macos-arm64.zip" }, Directory.GetFiles(dir).Select(Path.GetFileName));
        store.DeleteDownloads(keepFileName: null);
        Assert.Empty(Directory.GetFiles(dir));
        Directory.Delete(dir);
    }

    [Fact]
    public void Platforms_AreNamed()
    {
        Assert.Equal("macOS", HostPlatform.MacOS.Name());
        Assert.Equal("Mac", HostPlatform.MacOS.Computer());
        Assert.Equal("PC", HostPlatform.Windows.Computer());
        Assert.True(HostPlatform.MacOS.IsMac());
    }

    [Fact]
    public async Task GetLatestAsync_UsesApiAndSendsUserAgent()
    {
        HttpRequestMessage? captured = null;
        var handler = new StubHandler(req =>
        {
            captured = req;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ReleasesJson, Encoding.UTF8, "application/json") };
        });
        var client = new GitHubReleaseClient(new HttpClient(handler), "owner/repo", "Coreloom/1.0.0");

        var latest = await client.GetLatestAsync(includePreReleases: false);

        Assert.Equal("1.2.0", latest!.Version.ToString());
        Assert.Equal("https://api.github.com/repos/owner/repo/releases?per_page=20", captured!.RequestUri!.ToString());
        Assert.Contains("Coreloom/1.0.0", captured.Headers.UserAgent.ToString());
    }

    [Fact]
    public async Task GetLatestAsync_NotFound_ThrowsFriendlyError()
    {
        var client = new GitHubReleaseClient(new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound))), "owner/repo", "ua");
        var ex = await Assert.ThrowsAsync<UpdateException>(() => client.GetLatestAsync(false));
        Assert.Contains("not found", ex.Message);
    }

    [Theory]
    [InlineData("owner/repo", true)]
    [InlineData("jerry-ubah/Coreloom", true)]
    [InlineData("OWNER/Coreloom", false)]
    [InlineData("owner", false)]
    [InlineData("owner/repo/extra", false)]
    [InlineData("owner/re po", false)]
    [InlineData(null, false)]
    public void IsValidRepository(string? repo, bool expected)
    {
        Assert.Equal(expected, GitHubReleaseClient.IsValidRepository(repo));
    }
}

public class UpdateCheckerTests
{
    private static ReleaseInfo Release(string version) =>
        new(SemanticVersion.Parse(version), "v" + version, version, "notes", new Uri("https://github.com/o/r"), null, false, []);

    [Fact]
    public async Task ReportsAvailableUpdate()
    {
        var checker = new UpdateChecker(SemanticVersion.Parse("1.0.0"), "o/r", (_, _) => Task.FromResult<ReleaseInfo?>(Release("1.1.0")));
        var result = await checker.CheckAsync(false);
        Assert.True(result.IsUpdateAvailable);
    }

    [Fact]
    public async Task UpToDate_WhenSameOrOlder()
    {
        var checker = new UpdateChecker(SemanticVersion.Parse("1.1.0"), "o/r", (_, _) => Task.FromResult<ReleaseInfo?>(Release("1.1.0")));
        Assert.Equal(UpdateCheckStatus.UpToDate, (await checker.CheckAsync(false)).Status);
    }

    [Fact]
    public async Task SkippedVersion_IsNotOffered()
    {
        var checker = new UpdateChecker(SemanticVersion.Parse("1.0.0"), "o/r", (_, _) => Task.FromResult<ReleaseInfo?>(Release("1.1.0")));
        var result = await checker.CheckAsync(false, skippedVersion: "1.1.0");
        Assert.False(result.IsUpdateAvailable);
    }

    [Fact]
    public async Task NotConfigured_WhenRepositoryIsPlaceholder()
    {
        var checker = new UpdateChecker(SemanticVersion.Parse("1.0.0"), "OWNER/Coreloom", (_, _) => throw new InvalidOperationException("should not be called"));
        Assert.Equal(UpdateCheckStatus.NotConfigured, (await checker.CheckAsync(false)).Status);
    }

    [Fact]
    public async Task NetworkFailure_IsReportedNotThrown()
    {
        var checker = new UpdateChecker(SemanticVersion.Parse("1.0.0"), "o/r", (_, _) => throw new HttpRequestException("offline"));
        var result = await checker.CheckAsync(false);
        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.Contains("offline", result.Message);
    }
}

public sealed class ChecksumsAndDownloadTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "coreloom-dl-" + Guid.NewGuid().ToString("N"));

    public ChecksumsAndDownloadTests()
    {
        Directory.CreateDirectory(_dir);
    }

    [Fact]
    public void Parse_ReadsCommonFormats()
    {
        string content = "# checksums\n"
            + "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855  Coreloom-1.2.0-win-x64-Setup.exe\r\n"
            + "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855 *Coreloom-1.2.0-win-x64-Portable.zip\n"
            + "garbage line\n";
        var map = Checksums.Parse(content);
        Assert.Equal(2, map.Count);
        Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", map["coreloom-1.2.0-win-x64-setup.exe"]);
        Assert.True(map.ContainsKey("Coreloom-1.2.0-win-x64-Portable.zip"));
    }

    [Fact]
    public async Task Verify_DetectsMatchAndMismatch()
    {
        string file = Path.Combine(_dir, "empty.bin");
        await File.WriteAllBytesAsync(file, []);
        Assert.True(await Checksums.VerifyAsync(file, "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855"));
        Assert.False(await Checksums.VerifyAsync(file, new string('0', 64)));
    }

    [Fact]
    public async Task Download_ResumesFromPartialFile()
    {
        byte[] payload = Enumerable.Range(0, 100_000).Select(i => (byte)(i % 251)).ToArray();
        string dest = Path.Combine(_dir, "setup.exe");
        await File.WriteAllBytesAsync(dest + ".partial", payload[..40_000]);
        long? requestedFrom = null;

        var handler = new StubHandler(req =>
        {
            long from = req.Headers.Range?.Ranges.First().From ?? 0;
            requestedFrom = from;
            var response = new HttpResponseMessage(from > 0 ? HttpStatusCode.PartialContent : HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload[(int)from..]),
            };
            return response;
        });

        await new ResumableDownloader(new HttpClient(handler)).DownloadAsync(new Uri("https://example.com/setup.exe"), dest, payload.Length, null, CancellationToken.None);

        Assert.Equal(40_000, requestedFrom);
        Assert.Equal(payload, await File.ReadAllBytesAsync(dest));
        Assert.False(File.Exists(dest + ".partial"));
    }

    [Fact]
    public async Task Download_RestartsWhenServerIgnoresRange()
    {
        byte[] payload = Enumerable.Range(0, 50_000).Select(i => (byte)(i % 13)).ToArray();
        string dest = Path.Combine(_dir, "setup2.exe");
        await File.WriteAllBytesAsync(dest + ".partial", new byte[10_000]);
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) });

        await new ResumableDownloader(new HttpClient(handler)).DownloadAsync(new Uri("https://example.com/x"), dest, payload.Length, null, CancellationToken.None);

        Assert.Equal(payload, await File.ReadAllBytesAsync(dest));
    }

    [Fact]
    public async Task Download_RetriesTransientFailures()
    {
        byte[] payload = [1, 2, 3, 4, 5];
        int calls = 0;
        var handler = new StubHandler(_ =>
        {
            if (++calls == 1)
            {
                throw new HttpRequestException("connection reset");
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) };
        });
        string dest = Path.Combine(_dir, "retry.bin");
        var downloader = new ResumableDownloader(new HttpClient(handler)) { MaxAttempts = 3 };

        await downloader.DownloadAsync(new Uri("https://example.com/x"), dest, payload.Length, null, CancellationToken.None);

        Assert.Equal(2, calls);
        Assert.Equal(payload, await File.ReadAllBytesAsync(dest));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, true);
        }
        catch
        {
            // ignore
        }
    }
}

internal sealed class StubHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

    public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        _respond = respond;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(_respond(request));
}
