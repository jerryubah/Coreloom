using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Coreloom.Core.Diagnostics;

namespace Coreloom.Core.Updates;

/// <summary>How a copy of Coreloom is updated.</summary>
public enum UpdatePackageKind
{
    /// <summary>Copies installed with the installer: the release's <c>-Setup.exe</c> runs and replaces the files.</summary>
    Installer,

    /// <summary>
    /// Portable copies (and any other unzipped copy): the release's <c>-Portable.zip</c> is unpacked next to the app
    /// and copied over it once Coreloom has exited (see <see cref="FolderUpdate"/>).
    /// </summary>
    PortableZip,

    /// <summary>The Mac app on Apple silicon: the release's <c>-macos-arm64.zip</c> (the app bundle) replaces Coreloom.app.</summary>
    MacArm64,

    /// <summary>The Mac app on Intel: the release's <c>-macos-x64.zip</c> replaces Coreloom.app.</summary>
    MacX64,
}

public static class UpdatePackageKinds
{
    public static bool IsMac(this UpdatePackageKind kind) => kind is UpdatePackageKind.MacArm64 or UpdatePackageKind.MacX64;

    /// <summary>The end of the package's file name ("-Setup.exe", "-Portable.zip", "-macos-arm64.zip").</summary>
    public static string Suffix(this UpdatePackageKind kind) => kind switch
    {
        UpdatePackageKind.Installer => "-Setup.exe",
        UpdatePackageKind.MacArm64 => "-macos-arm64.zip",
        UpdatePackageKind.MacX64 => "-macos-x64.zip",
        _ => "-Portable.zip",
    };
}

/// <summary>
/// A release whose package (installer or portable zip) has been downloaded and verified, waiting to be installed. Saved
/// next to the package as <c>pending-update.json</c> so the update survives a restart and can be installed when
/// Coreloom next starts. After the update, the same record describes the installed version (its notes feed "What's new").
/// </summary>
/// <remarks>
/// This file lives in a folder the user (and so any program running as the user) can write to. It is therefore only
/// a description: before Coreloom (which always runs as administrator) installs a package, the file's SHA-256 must
/// match the hash recorded in <see cref="IUpdateTrustStore"/>, which only administrators can write.
/// </remarks>
public sealed record PendingUpdate
{
    public required string Version { get; init; }

    public string? TagName { get; init; }

    public string? Title { get; init; }

    public string? Notes { get; init; }

    public string? HtmlUrl { get; init; }

    public DateTimeOffset? PublishedAt { get; init; }

    /// <summary>
    /// File name of the downloaded package inside the downloads folder (never a path): the installer (<c>.exe</c>) or
    /// the portable zip (<c>.zip</c>). The JSON name is kept from 1.1.0, which only downloaded installers.
    /// </summary>
    public required string InstallerFileName { get; init; }

    /// <summary>Lowercase hex SHA-256 of the package, from the release's SHA256SUMS.txt.</summary>
    public required string Sha256 { get; init; }

    public DateTime DownloadedUtc { get; init; }

    /// <summary>How many times an install was started for this version. Automatic attempts stop at <see cref="UpdateInstallPolicy.MaxAutomaticAttempts"/>.</summary>
    public int InstallAttempts { get; init; }

    /// <summary>Set when the installer reported that it could not finish (Coreloom was relaunched with <c>--update-failed</c>).</summary>
    public bool InstallFailed { get; init; }

    [JsonIgnore]
    public SemanticVersion? ParsedVersion => SemanticVersion.TryParse(Version, out var v) ? v : null;

    public static PendingUpdate FromRelease(ReleaseInfo release, ReleaseAsset package, string sha256, DateTime downloadedUtc) => new()
    {
        Version = release.Version.ToString(),
        TagName = release.TagName,
        Title = release.Title,
        Notes = release.Notes,
        HtmlUrl = release.HtmlUrl.ToString(),
        PublishedAt = release.PublishedAt,
        InstallerFileName = Path.GetFileName(package.Name),
        Sha256 = sha256.Trim().ToLowerInvariant(),
        DownloadedUtc = downloadedUtc,
    };
}

/// <summary>What a saved <see cref="PendingUpdate"/> means for the running version.</summary>
public enum PendingUpdateState
{
    /// <summary>Nothing was saved, or the file is unreadable.</summary>
    None,

    /// <summary>A newer version is downloaded and waiting to be installed.</summary>
    Ready,

    /// <summary>The saved version is the one now running: the update was installed.</summary>
    Installed,

    /// <summary>The saved version is older than the running one (installed some other way); it can be deleted.</summary>
    Stale,
}

/// <summary>
/// Where Coreloom keeps the SHA-256 of a downloaded package so it can be trusted after a restart. The Windows
/// implementation writes it under HKEY_LOCAL_MACHINE, which programs running without administrator rights cannot
/// change; this is what stops another program from swapping in its own installer or zip for Coreloom to run.
/// </summary>
public interface IUpdateTrustStore
{
    /// <summary>Records the expected hash for a version. Returns false when it could not be stored.</summary>
    bool TrySave(string version, string sha256);

    /// <summary>The recorded version and hash, or null.</summary>
    (string Version, string Sha256)? Load();

    void Clear();
}

/// <summary>A trust store that only remembers for the lifetime of the process (tests).</summary>
public sealed class InMemoryUpdateTrustStore : IUpdateTrustStore
{
    private (string Version, string Sha256)? _value;

    public bool TrySave(string version, string sha256)
    {
        _value = (version, sha256.Trim().ToLowerInvariant());
        return true;
    }

    public (string Version, string Sha256)? Load() => _value;

    public void Clear() => _value = null;
}

/// <summary>Reads and writes <c>pending-update.json</c>, the downloaded packages and the last installed release's notes in the downloads folder.</summary>
public sealed class PendingUpdateStore
{
    public const string PendingFileName = "pending-update.json";
    public const string InstalledFileName = "installed-release.json";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public PendingUpdateStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory = directory;
    }

    public string Directory { get; }

    public string PendingFile => Path.Combine(Directory, PendingFileName);

    public string InstalledFile => Path.Combine(Directory, InstalledFileName);

    /// <summary>
    /// Full path of a pending update's package when it is the kind this copy installs (an <c>.exe</c> installer or a
    /// <c>.zip</c>). Only a plain file name inside the folder is accepted.
    /// </summary>
    public string? PackagePath(PendingUpdate pending, UpdatePackageKind kind)
    {
        string name = pending.InstallerFileName;
        string extension = kind == UpdatePackageKind.Installer ? ".exe" : ".zip"; // every other kind is a zip
        if (string.IsNullOrWhiteSpace(name) || name != Path.GetFileName(name) || name.Contains("..", StringComparison.Ordinal)
            || name.IndexOfAny(['\\', '/', ':']) >= 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || !name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return Path.Combine(Directory, name);
    }

    public PendingUpdate? LoadPending() => Read(PendingFile);

    public PendingUpdate? LoadInstalled() => Read(InstalledFile);

    public void SavePending(PendingUpdate pending) => Write(PendingFile, pending);

    public static PendingUpdateState Evaluate(PendingUpdate? pending, SemanticVersion current)
    {
        if (pending?.ParsedVersion is not { } version || string.IsNullOrWhiteSpace(pending.Sha256))
        {
            return PendingUpdateState.None;
        }

        int order = version.CompareTo(current);
        return order > 0 ? PendingUpdateState.Ready : order == 0 ? PendingUpdateState.Installed : PendingUpdateState.Stale;
    }

    /// <summary>
    /// The update was installed: keeps its notes as <c>installed-release.json</c> (for "What's new") and removes
    /// the pending record and every downloaded package.
    /// </summary>
    public void MarkInstalled(PendingUpdate pending)
    {
        Write(InstalledFile, pending with { InstallAttempts = 0, InstallFailed = false });
        DeletePending();
    }

    /// <summary>Removes the pending record and downloaded packages (complete or partial).</summary>
    public void DeletePending()
    {
        TryDelete(PendingFile);
        DeleteDownloads(keepFileName: null);
    }

    /// <summary>Deletes installers, portable zips and partial downloads in the folder, except <paramref name="keepFileName"/>.</summary>
    public void DeleteDownloads(string? keepFileName)
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            return;
        }

        try
        {
            foreach (string file in System.IO.Directory.EnumerateFiles(Directory))
            {
                string name = Path.GetFileName(file);
                bool download = name.EndsWith("-Setup.exe", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith("-Portable.zip", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith("-macos-arm64.zip", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith("-macos-x64.zip", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith(".partial", StringComparison.OrdinalIgnoreCase);
                bool keep = keepFileName is not null
                    && (name.Equals(keepFileName, StringComparison.OrdinalIgnoreCase) || name.Equals(keepFileName + ".partial", StringComparison.OrdinalIgnoreCase));
                if (download && !keep)
                {
                    TryDelete(file);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Updates", "Cleaning up downloaded updates failed.", ex);
        }
    }

    /// <summary>
    /// Deletes downloaded packages (<c>Coreloom-&lt;version&gt;-win-x64-Setup.exe</c> or <c>-Portable.zip</c>) for
    /// versions up to <paramref name="current"/>: leftovers of updates that are done, including ones installed by
    /// Coreloom 1.0.0's updater, which didn't clean up. Newer downloads and partial downloads are kept.
    /// </summary>
    public void DeletePackagesUpTo(SemanticVersion current)
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            return;
        }

        const string prefix = "Coreloom-";
        try
        {
            foreach (string suffix in new[] { "-win-x64-Setup.exe", "-win-x64-Portable.zip", "-macos-arm64.zip", "-macos-x64.zip" })
            {
                foreach (string file in System.IO.Directory.EnumerateFiles(Directory, prefix + "*" + suffix))
                {
                    string name = Path.GetFileName(file);
                    if (name.Length > prefix.Length + suffix.Length
                        && SemanticVersion.TryParse(name[prefix.Length..^suffix.Length], out var version)
                        && version.CompareTo(current) <= 0)
                    {
                        TryDelete(file);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Updates", "Cleaning up old downloads failed.", ex);
        }
    }

    private static PendingUpdate? Read(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var value = JsonSerializer.Deserialize<PendingUpdate>(File.ReadAllText(path), Json);
            return value is { Version.Length: > 0, InstallerFileName.Length: > 0, Sha256.Length: 64 } ? value : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Log.Warn("Updates", $"{Path.GetFileName(path)} could not be read; ignoring it.", ex);
            return null;
        }
    }

    private void Write(string path, PendingUpdate value)
    {
        System.IO.Directory.CreateDirectory(Directory);
        string temp = path + ".tmp";

        // Coreloom runs as administrator in a folder the user can write to: never write through a link left there.
        // CreateNew refuses to open anything that already exists (a link included), and the move replaces the
        // destination entry itself rather than whatever a link there points to.
        File.Delete(temp);
        using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, value, Json);
        }

        File.Move(temp, path, overwrite: true);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path); // no-op when missing; removes a link itself, never its target
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Updates", $"Could not delete {Path.GetFileName(path)}.", ex);
        }
    }
}

/// <summary>
/// A downloaded package (installer or portable zip) that was opened, hashed and found to match the expected SHA-256.
/// The file stays open for reading (other programs may read it but not change, rename or delete it) until this object
/// is disposed, so the file that was checked is the file that runs or is unpacked.
/// </summary>
public sealed class VerifiedPackage : IDisposable
{
    private FileStream? _stream;

    private VerifiedPackage(string path, FileStream stream)
    {
        Path = path;
        _stream = stream;
    }

    public string Path { get; }

    /// <summary>Opens and hashes <paramref name="path"/>. Returns null (and closes the file) when it is missing or the hash differs.</summary>
    public static VerifiedPackage? Open(string path, string expectedSha256)
    {
        if (string.IsNullOrWhiteSpace(expectedSha256) || !File.Exists(path))
        {
            return null;
        }

        FileStream? stream = null;
        try
        {
            if (ResumableDownloader.IsReparsePoint(path, isDirectory: false))
            {
                return null; // never follow a link planted in the downloads folder
            }

            stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan);
            byte[] hash = SHA256.HashData(stream);
            string actual = Convert.ToHexStringLower(hash);
            if (!CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.ASCII.GetBytes(actual),
                    System.Text.Encoding.ASCII.GetBytes(expectedSha256.Trim().ToLowerInvariant())))
            {
                stream.Dispose();
                return null;
            }

            var verified = new VerifiedPackage(path, stream);
            stream = null;
            return verified;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Updates", $"Could not verify {System.IO.Path.GetFileName(path)}.", ex);
            stream?.Dispose();
            return null;
        }
    }

    /// <summary>
    /// A second read-only handle to the verified file (for unpacking a zip). The file can't change while this object
    /// still holds it, so what is read is what was checked.
    /// </summary>
    public Stream OpenRead()
    {
        ObjectDisposedException.ThrowIf(_stream is null, this);
        return new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan);
    }

    /// <summary>Lets go of the file early (used only if Windows refuses to start it while it is held open).</summary>
    public void Release()
    {
        _stream?.Dispose();
        _stream = null;
    }

    public void Dispose() => Release();
}
