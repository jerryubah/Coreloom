using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Coreloom.Core;
using Coreloom.Core.Diagnostics;
using Coreloom.Core.Updates;
using Coreloom.Mac.Native;
using Coreloom.ViewModels.Services;

namespace Coreloom.Mac.Services;

/// <summary>
/// Updates Coreloom.app in place from the release's zip for this Mac's architecture (Apple silicon or Intel). Prepare
/// copies the verified zip into a private folder, checks it again there (so nothing can swap it in between), unpacks it
/// with ditto and checks the new bundle's identity and code signature. LaunchAndExit starts a small helper that waits for
/// Coreloom to quit, swaps the old bundle for the new one (putting the old one back if anything fails) and opens
/// Coreloom again. Nothing here needs administrator rights; a copy in a folder the user can't write to (or one macOS
/// runs from a translocated, read-only location) can't update itself and says so.
/// </summary>
public sealed partial class MacUpdateInstaller : IUpdateInstaller
{
    public const string BundleIdentifier = "com.jerryubah.coreloom";
    private const int WriteAccess = 2;
    private readonly AppPaths _paths;
    private readonly Action _exit;
    private Task _cleanup = Task.CompletedTask;

    [LibraryImport("/usr/lib/libSystem.dylib", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int access(string path, int mode);

    public MacUpdateInstaller(AppPaths paths, Action exit)
    {
        _paths = paths;
        _exit = exit;
    }

    public static UpdatePackageKind CurrentKind => RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? UpdatePackageKind.MacArm64 : UpdatePackageKind.MacX64;

    public UpdatePackageKind PackageKind => CurrentKind;

    /// <summary>True when Coreloom runs from an app bundle it can replace.</summary>
    public bool CanInstallUpdates => CannotUpdateReason is null;

    /// <summary>Why this copy can't update itself, or null.</summary>
    public static string? CannotUpdateReason
    {
        get
        {
            string? bundle = MacLifetimeService.BundlePath;
            if (bundle is null)
            {
                return "Coreloom isn't running from its app bundle.";
            }

            if (IsTranslocated(bundle))
            {
                return "macOS is running Coreloom from a temporary, read-only location. Move Coreloom to the Applications folder to update it.";
            }

            string parent = Path.GetDirectoryName(bundle) ?? "/";
            return access(parent, WriteAccess) == 0 && access(bundle, WriteAccess) == 0
                ? null
                : $"Coreloom can't replace itself in {parent}: you don't have permission to change that folder.";
        }
    }

    public static bool IsTranslocated(string path) => path.Contains("/AppTranslocation/", StringComparison.Ordinal);

    private string StagingDirectory => Path.Combine(_paths.DownloadDirectory, "staged");

    private string StagedBundle => Path.Combine(StagingDirectory, "unpacked", "Coreloom.app");

    /// <summary>Removes what the last update left (the staged copy and a leftover backup of the old version).</summary>
    public void StartCleanup()
    {
        _cleanup = Task.Run(() =>
        {
            TryDelete(StagingDirectory);
            if (MacLifetimeService.BundlePath is { } bundle)
            {
                TryDelete(BackupPath(bundle));
            }
        });
    }

    internal static string BackupPath(string bundle) => bundle[..^".app".Length] + " (previous).app";

    public bool Prepare(VerifiedPackage package)
    {
        try
        {
            _cleanup.Wait();
            TryDelete(StagingDirectory);
            Directory.CreateDirectory(StagingDirectory);
            File.SetUnixFileMode(StagingDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            string zip = Path.Combine(StagingDirectory, "update.zip");
            using (var source = package.OpenRead())
            using (var target = new FileStream(zip, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[1 << 16];
                int read;
                while ((read = source.Read(buffer)) > 0)
                {
                    sha.AppendData(buffer, 0, read);
                    target.Write(buffer, 0, read);
                }

                if (!string.Equals(Convert.ToHexStringLower(sha.GetHashAndReset()), package.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    Log.Warn("Updates", "The update changed while it was being copied; it won't be installed.");
                    return false;
                }
            }

            var unpack = Commands.RunAsync("/usr/bin/ditto", ["-x", "-k", zip, Path.Combine(StagingDirectory, "unpacked")], TimeSpan.FromMinutes(3)).GetAwaiter().GetResult();
            File.Delete(zip);
            if (!unpack.Succeeded || !Directory.Exists(StagedBundle))
            {
                Log.Warn("Updates", $"Unpacking the update failed: {unpack.Error}");
                return false;
            }

            if (ReadBundleIdentifier(StagedBundle) != BundleIdentifier)
            {
                Log.Warn("Updates", "The update isn't a Coreloom app bundle.");
                return false;
            }

            var signature = Commands.RunAsync("/usr/bin/codesign", ["--verify", "--deep", "--strict", StagedBundle], TimeSpan.FromMinutes(2)).GetAwaiter().GetResult();
            if (!signature.Succeeded)
            {
                Log.Warn("Updates", $"The update's code signature doesn't check out: {signature.Error}");
                return false;
            }

            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Log.Warn("Updates", "Preparing the update failed.", e);
            return false;
        }
    }

    public bool LaunchAndExit(VerifiedPackage package, UpdateInstallOptions options)
    {
        if (MacLifetimeService.BundlePath is not { } bundle || !Directory.Exists(StagedBundle))
        {
            return false;
        }

        string script = Path.Combine(StagingDirectory, "apply.sh");
        File.WriteAllText(script, HelperScript);
        File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        string relaunch = options.RelaunchMinimized ? "--minimized" : string.Empty;
        if (!Commands.Start("/bin/sh", script, Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture), bundle, StagedBundle, BackupPath(bundle), _paths.LogDirectory, relaunch))
        {
            return false;
        }

        Log.Info("Updates", $"Replacing {bundle} once Coreloom has quit.");
        _exit();
        return true;
    }

    /// <summary>
    /// Waits for Coreloom (pid $1) to quit, moves the old bundle ($2) aside ($4), copies the new one ($3) in and opens
    /// it with --updated; on any failure it puts the old one back and opens that with --update-failed.
    /// </summary>
    internal const string HelperScript = """
        #!/bin/sh
        pid="$1"; target="$2"; staged="$3"; backup="$4"; logs="$5"; extra="$6"
        mkdir -p "$logs"
        log="$logs/update.log"
        echo "$(date) updating $target" >> "$log"
        i=0
        while kill -0 "$pid" 2>/dev/null; do
          sleep 0.2
          i=$((i + 1))
          if [ "$i" -gt 600 ]; then echo "$(date) Coreloom didn't quit" >> "$log"; exit 1; fi
        done
        rm -rf "$backup"
        if mv "$target" "$backup" && /usr/bin/ditto "$staged" "$target"; then
          rm -rf "$backup" "$staged"
          echo "$(date) updated" >> "$log"
          /usr/bin/open -n -a "$target" --args --updated $extra
        else
          echo "$(date) the update failed; restoring the previous version" >> "$log"
          if [ -d "$backup" ]; then rm -rf "$target"; mv "$backup" "$target"; fi
          /usr/bin/open -n -a "$target" --args --update-failed $extra
        fi
        """;

    private static string? ReadBundleIdentifier(string bundle)
    {
        var result = Commands.RunAsync("/usr/bin/defaults", ["read", Path.Combine(bundle, "Contents", "Info"), "CFBundleIdentifier"]).GetAwaiter().GetResult();
        return result.Succeeded ? result.Output.Trim() : null;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Updates", $"Couldn't remove {path}.", e);
        }
    }
}

/// <summary>
/// Remembers the downloaded update's expected SHA-256 between launches, in a file only the user can read and write
/// (Application Support/Coreloom/Updates/trust.json, mode 600). On a Mac, programs running as the user can already change
/// the app bundle itself, so this is as strong as the store needs to be; the hash still comes from the release's
/// checksums, fetched over HTTPS when the update was downloaded.
/// </summary>
public sealed class FileUpdateTrustStore(string directory) : IUpdateTrustStore
{
    private string FilePath => Path.Combine(directory, "trust.json");

    public bool TrySave(string version, string sha256)
    {
        try
        {
            Directory.CreateDirectory(directory);
            string json = JsonSerializer.Serialize(new Entry(version, sha256.Trim().ToLowerInvariant()));
            File.WriteAllText(FilePath, json);
            File.SetUnixFileMode(FilePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Updates", "Saving the update's checksum failed.", e);
            return false;
        }
    }

    public (string Version, string Sha256)? Load()
    {
        try
        {
            if (!File.Exists(FilePath) || File.GetUnixFileMode(FilePath).HasFlag(UnixFileMode.OtherWrite) || File.GetUnixFileMode(FilePath).HasFlag(UnixFileMode.GroupWrite))
            {
                return null;
            }

            return JsonSerializer.Deserialize<Entry>(File.ReadAllText(FilePath)) is { Version.Length: > 0, Sha256.Length: 64 } entry ? (entry.Version, entry.Sha256) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public void Clear()
    {
        try
        {
            File.Delete(FilePath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed record Entry(string Version, string Sha256);
}
