using System.Globalization;
using Coreloom.Core.Diagnostics;
using Coreloom.Core.Optimize;
using Coreloom.Hardware.Mac.Native;
using Coreloom.Mac.Native;
using Coreloom.ViewModels.Services;

namespace Coreloom.Mac.Services;

/// <summary>
/// Smart Optimize on a Mac: caches, logs and temporary files in the user's own folders (<see cref="MacJunk"/>), the Trash
/// (emptied by Finder), freeing inactive memory (macOS's purge), macOS's protections as its own tools report them, the
/// background items that start at login (launch agents, switched per user with launchctl) and refreshing the DNS cache.
/// The two changes that need administrator rights (purge, the DNS cache) ask for the password through macOS's own
/// dialog, only when the user clicks.
/// </summary>
public sealed class MacOptimizeService : IOptimizeService
{
    private const string LogCategory = "Optimize";
    private static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public bool IsSupported => OperatingSystem.IsMacOS();

    public IReadOnlyList<JunkCategory> JunkCategories() =>
        MacJunk.Categories(new MacJunkFolders(Home, Darwin.UserCacheDirectory(), Path.GetTempPath()));

    // ---- Trash ---------------------------------------------------------------------------------------------

    private static string TrashFolder => Path.Combine(Home, ".Trash");

    /// <summary>The Trash's size, when macOS lets Coreloom look (it asks for Full Disk Access to read it), else nothing.</summary>
    public JunkSize RecycleBinSize()
    {
        try
        {
            long bytes = 0;
            int files = 0;
            foreach (var file in new DirectoryInfo(TrashFolder).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }))
            {
                bytes += file.Length;
                files++;
            }

            return new JunkSize(bytes, files);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Debug(LogCategory, $"The Trash can't be measured: {e.Message}");
            return default;
        }
    }

    /// <summary>Empties the Trash through Finder (macOS asks once whether Coreloom may control Finder).</summary>
    public bool EmptyRecycleBin()
    {
        var result = Commands.RunAsync("/usr/bin/osascript", ["-e", "tell application \"Finder\" to empty trash"], TimeSpan.FromMinutes(5)).GetAwaiter().GetResult();
        if (!result.Succeeded)
        {
            Log.Warn(LogCategory, $"Emptying the Trash failed: {result.Error}");
        }

        return result.Succeeded;
    }

    // ---- Memory --------------------------------------------------------------------------------------------

    /// <summary>
    /// macOS manages memory itself (compression and swap); what can be freed is its cache of files, with purge (which
    /// needs the user's password). Apps aren't trimmed, so the process list stays empty.
    /// </summary>
    public async Task<MemoryCleanResult> FreeMemoryAsync(bool clearStandby, CancellationToken cancellationToken)
    {
        long total = Darwin.SysctlLong("hw.memsize") ?? 0;
        long before = Available(total);
        var result = await Commands.RunAsAdministratorAsync("/usr/sbin/purge", "Coreloom wants to free the memory macOS uses to cache files.", cancellationToken);
        if (!result.Succeeded)
        {
            Log.Info(LogCategory, Commands.WasCancelled(result) ? "Freeing memory: the password prompt was cancelled." : $"purge failed: {result.Error}");
            return new MemoryCleanResult(before, before, total, 0);
        }

        await Task.Delay(500, cancellationToken);
        long after = Available(total);
        Log.Info(LogCategory, $"Memory: purge freed {Math.Max(0, after - before) / (1 << 20)} MB.");
        return new MemoryCleanResult(before, after, total, 1);
    }

    /// <summary>Free + inactive + speculative + purgeable pages: memory macOS can hand out right away.</summary>
    private static long Available(long total)
    {
        if (!Darwin.VmStatistics(out var vm))
        {
            return 0;
        }

        long page = Darwin.PageSize();
        long available = ((long)vm.FreeCount + vm.InactiveCount + vm.SpeculativeCount + vm.PurgeableCount) * page;
        return Math.Clamp(available, 0, total);
    }

    // ---- Protection ----------------------------------------------------------------------------------------

    public async Task<ProtectionStatus> ProtectionAsync(CancellationToken cancellationToken)
    {
        var gatekeeper = Commands.RunAsync("/usr/sbin/spctl", ["--status"], cancellationToken: cancellationToken);
        var sip = Commands.RunAsync("/usr/bin/csrutil", ["status"], cancellationToken: cancellationToken);
        var fileVault = Commands.RunAsync("/usr/bin/fdesetup", ["status"], cancellationToken: cancellationToken);
        var firewall = Commands.RunAsync("/usr/libexec/ApplicationFirewall/socketfilterfw", ["--getglobalstate"], cancellationToken: cancellationToken);
        await Task.WhenAll(gatekeeper, sip, fileVault, firewall);

        static string Text(CommandResult r) => r.Output.Length > 0 ? r.Output : r.Error;
        var (xprotectVersion, xprotectUpdated) = XProtect();
        var checks = new List<SecurityCheck>
        {
            new("xprotect", "XProtect", xprotectVersion is not null, "Apple's built-in malware protection: checks apps and downloads as they open, and removes known malware.", true),
        };
        if (MacSecurity.Gatekeeper(Text(await gatekeeper)) is bool g)
        {
            checks.Add(new("gatekeeper", "Gatekeeper", g, "Only opens apps from the App Store and identified developers unless you allow one.", true));
        }

        if (MacSecurity.IntegrityProtection(Text(await sip)) is bool s)
        {
            checks.Add(new("sip", "System Integrity Protection", s, "Keeps even administrators from changing macOS's own files.", true));
        }

        if (MacSecurity.FileVault(Text(await fileVault)) is bool f)
        {
            checks.Add(new("filevault", "FileVault", f, "Encrypts the startup disk, so your files are safe if the Mac is lost.", false));
        }

        if (MacSecurity.Firewall(Text(await firewall)) is bool w)
        {
            checks.Add(new("firewall", "Firewall", w, "Blocks incoming connections to apps you haven't allowed.", false));
        }

        return new ProtectionStatus
        {
            DefenderAvailable = false,
            RealTimeProtection = xprotectVersion is not null,
            AntivirusEnabled = xprotectVersion is not null,
            SignatureVersion = xprotectVersion is null ? null : $"XProtect {xprotectVersion}",
            SignaturesUpdated = xprotectUpdated,
            Checks = checks,
            Problem = checks.Count <= 1 ? "macOS's security tools didn't answer." : null,
        };
    }

    /// <summary>XProtect's version and when it was last updated (its bundle in /Library/Apple, or the older location).</summary>
    private static (string? Version, DateTime? Updated) XProtect()
    {
        foreach (string bundle in new[] { "/Library/Apple/System/Library/CoreServices/XProtect.bundle", "/System/Library/CoreServices/XProtect.bundle" })
        {
            string plist = Path.Combine(bundle, "Contents", "Info.plist");
            if (CF.ReadPropertyList(plist) is Dictionary<string, object?> info)
            {
                string? version = CF.Text(info, "CFBundleShortVersionString") ?? CF.Text(info, "CFBundleVersion");
                DateTime? updated = File.Exists(plist) ? File.GetLastWriteTime(plist) : null;
                return (version, updated);
            }
        }

        return (null, null);
    }

    public Task<ScanOutcome> QuickScanAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new ScanOutcome(false, 0, "macOS scans apps and downloads with XProtect as they open; there's no scan to run by hand."));

    public Task<MaintenanceResult> UpdateSignaturesAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new MaintenanceResult(true, "macOS updates XProtect by itself (System Settings › General › Software Update › Automatic Updates)."));

    public Task<MaintenanceResult> RemoveThreatsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new MaintenanceResult(true, "XProtect removes known malware by itself."));

    public void OpenSecurity() => Commands.Start("/usr/bin/open", "x-apple.systempreferences:com.apple.preference.security");

    // ---- Startup -------------------------------------------------------------------------------------------

    private static string Uid => Environment.GetEnvironmentVariable("UID") ?? Commands.RunAsync("/usr/bin/id", "-u").GetAwaiter().GetResult().Output.Trim();

    /// <summary>The background items that start at login: launch agents in ~/Library/LaunchAgents and /Library/LaunchAgents.</summary>
    public IReadOnlyList<StartupApp> StartupApps()
    {
        string uid = Uid;
        var disabled = LaunchAgents.ParseDisabled(Commands.RunAsync("/bin/launchctl", "print-disabled", $"gui/{uid}").GetAwaiter().GetResult().Output);
        var apps = new List<StartupApp>();
        foreach (var (folder, scope) in new[] { (Path.Combine(Home, "Library", "LaunchAgents"), StartupScope.MacUserAgent), ("/Library/LaunchAgents", StartupScope.MacSystemAgent) })
        {
            if (!Directory.Exists(folder))
            {
                continue;
            }

            foreach (string file in Directory.EnumerateFiles(folder, "*.plist"))
            {
                if (CF.ReadPropertyList(file) is not Dictionary<string, object?> plist || CF.Text(plist, "Label") is not { Length: > 0 } label)
                {
                    continue;
                }

                if (label.StartsWith("com.jerryubah.coreloom", StringComparison.Ordinal))
                {
                    continue; // Coreloom's own login item is switched in Settings
                }

                bool runsAtLoad = (plist.TryGetValue("RunAtLoad", out var run) && run is true) || plist.ContainsKey("KeepAlive");
                if (!runsAtLoad)
                {
                    continue; // started on demand, not at login
                }

                string? program = CF.Text(plist, "Program") ?? (plist.TryGetValue("ProgramArguments", out var args) && args is List<object?> { Count: > 0 } list ? list[0] as string : null);
                string? app = program is null ? null : AppOf(program);
                bool plistDisabled = plist.TryGetValue("Disabled", out var d) && d is true;
                bool enabled = !plistDisabled && !(disabled.TryGetValue(label, out bool off) && off);
                apps.Add(new StartupApp(app ?? LaunchAgents.FriendlyName(label), label, program ?? file, scope, enabled)
                {
                    Executable = program,
                    Publisher = LaunchAgents.Publisher(label),
                });
            }
        }

        return apps;
    }

    private static string? AppOf(string program)
    {
        int app = program.IndexOf(".app/", StringComparison.Ordinal);
        if (app < 0)
        {
            return null;
        }

        int start = program.LastIndexOf('/', app) + 1;
        return program[start..app];
    }

    /// <summary>Switches a launch agent for this user (launchctl enable/disable, and starting or stopping it now).</summary>
    public bool SetStartupEnabled(StartupApp app, bool enabled)
    {
        string uid = Uid;
        string target = $"gui/{uid}/{app.ValueName}";
        var change = Commands.RunAsync("/bin/launchctl", enabled ? "enable" : "disable", target).GetAwaiter().GetResult();
        if (!change.Succeeded)
        {
            Log.Warn(LogCategory, $"launchctl couldn't {(enabled ? "enable" : "disable")} {app.ValueName}: {change.Error}");
            return false;
        }

        string folder = app.Scope == StartupScope.MacSystemAgent ? "/Library/LaunchAgents" : Path.Combine(Home, "Library", "LaunchAgents");
        string plist = Directory.EnumerateFiles(folder, "*.plist").FirstOrDefault(f => CF.ReadPropertyList(f) is Dictionary<string, object?> p && CF.Text(p, "Label") == app.ValueName) ?? string.Empty;
        _ = enabled
            ? Commands.RunAsync("/bin/launchctl", "bootstrap", $"gui/{uid}", plist).GetAwaiter().GetResult()
            : Commands.RunAsync("/bin/launchctl", "bootout", target).GetAwaiter().GetResult();
        Log.Info(LogCategory, $"{app.Name} {(enabled ? "opens" : "no longer opens")} at login.");
        return true;
    }

    // ---- Maintenance ---------------------------------------------------------------------------------------

    public async Task<MaintenanceResult> FlushDnsAsync(CancellationToken cancellationToken)
    {
        var result = await Commands.RunAsAdministratorAsync("/usr/bin/dscacheutil -flushcache; /usr/bin/killall -HUP mDNSResponder", "Coreloom wants to refresh the network's DNS cache.", cancellationToken);
        return result.Succeeded
            ? new MaintenanceResult(true, "DNS cache refreshed.")
            : new MaintenanceResult(false, Commands.WasCancelled(result) ? "Nothing changed: the password prompt was cancelled." : $"The DNS cache couldn't be refreshed: {result.Error}");
    }

    /// <summary>Disk Utility's First Aid check of the startup volume, read only (macOS keeps SSDs trimmed itself).</summary>
    public async Task<MaintenanceResult> OptimizeDrivesAsync(CancellationToken cancellationToken)
    {
        var result = await Commands.RunAsync("/usr/sbin/diskutil", ["verifyVolume", "/"], TimeSpan.FromMinutes(10), cancellationToken);
        string text = (result.Output + "\n" + result.Error).Trim();
        if (result.Succeeded && text.Contains("appears to be OK", StringComparison.OrdinalIgnoreCase))
        {
            return new MaintenanceResult(true, "The startup disk checked out OK. macOS keeps SSDs trimmed by itself.");
        }

        Log.Info(LogCategory, $"diskutil verifyVolume: {text}");
        return result.Succeeded
            ? new MaintenanceResult(true, "The startup disk was checked. macOS keeps SSDs trimmed by itself.")
            : new MaintenanceResult(false, "The check couldn't run here. Use First Aid in Disk Utility to check the disk.");
    }
}

/// <summary>Reading launchctl's lists and naming launch agents.</summary>
public static class LaunchAgents
{
    /// <summary>`launchctl print-disabled gui/501` lines like "com.foo.agent" => disabled (or true/false on older macOS).</summary>
    public static Dictionary<string, bool> ParseDisabled(string output)
    {
        var result = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (string raw in output.Split('\n'))
        {
            string line = raw.Trim();
            int arrow = line.IndexOf("=>", StringComparison.Ordinal);
            if (arrow <= 0 || !line.StartsWith('"'))
            {
                continue;
            }

            string label = line[1..line.IndexOf('"', 1)];
            string state = line[(arrow + 2)..].Trim().TrimEnd(',', ';');
            result[label] = state is "disabled" or "true";
        }

        return result;
    }

    /// <summary>"com.google.keystone.agent" → "Keystone agent (Google)"-style names when no app is known.</summary>
    public static string FriendlyName(string label)
    {
        string[] parts = label.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var words = parts.Length > 2 ? parts[2..] : parts;
        string name = string.Join(' ', words.Select(w => w.Length > 0 ? char.ToUpper(w[0], CultureInfo.InvariantCulture) + w[1..] : w));
        return name.Length > 0 ? name : label;
    }

    /// <summary>"com.google.keystone.agent" → "Google".</summary>
    public static string? Publisher(string label)
    {
        string[] parts = label.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && parts[0] is "com" or "org" or "net" or "io" or "us" or "co" or "app"
            ? char.ToUpper(parts[1][0], CultureInfo.InvariantCulture) + parts[1][1..]
            : null;
    }
}
