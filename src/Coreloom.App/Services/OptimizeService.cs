using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Coreloom.Core.Diagnostics;
using Coreloom.Core.Optimize;
using Coreloom.ViewModels.Services;
using Microsoft.Win32;

namespace Coreloom.App.Services;

/// <summary>
/// Smart Optimize on Windows. Everything goes through Windows' own tools so nothing risky is reinvented: junk is
/// deleted only inside known junk folders (see <see cref="WindowsJunk"/>), the Recycle Bin through the shell,
/// Microsoft Defender through MpCmdRun and its PowerShell module, startup apps through the same "StartupApproved"
/// switches Task Manager uses, memory through EmptyWorkingSet, and drives through defrag.exe.
/// </summary>
internal sealed partial class WindowsOptimizeService : IOptimizeService
{
    private const string LogCategory = "Optimize";
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint ProcessSetQuota = 0x0100;
    private const int SystemMemoryListInformation = 80;
    private const int MemoryPurgeStandbyList = 4;

    /// <summary>Never trimmed: Windows' own core processes and audio (trimming them can cause glitches).</summary>
    private static readonly HashSet<string> KeepProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Idle", "System", "Registry", "Memory Compression", "smss", "csrss", "wininit", "winlogon", "services", "lsass",
        "dwm", "audiodg", "fontdrvhost", "MsMpEng", "NisSrv",
    };

    private static readonly string[] StartupFileTypes = [".lnk", ".exe", ".bat", ".cmd", ".url", ".vbs"];

    public bool IsSupported => OperatingSystem.IsWindows();

    // ---- Cleanup ----------------------------------------------------------------------------------

    public IReadOnlyList<JunkCategory> JunkCategories() => WindowsJunk.Categories(new JunkFolders(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Path.GetTempPath(),
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)));

    public JunkSize RecycleBinSize()
    {
        var info = new RecycleBinInfo { Size = Marshal.SizeOf<RecycleBinInfo>() };
        int hr = SHQueryRecycleBin(null, ref info);
        return hr == 0 ? new JunkSize(info.Bytes, (int)Math.Min(int.MaxValue, info.Items)) : default;
    }

    public bool EmptyRecycleBin()
    {
        const uint NoConfirmation = 0x1, NoProgressUi = 0x2, NoSound = 0x4;
        int hr = SHEmptyRecycleBin(IntPtr.Zero, null, NoConfirmation | NoProgressUi | NoSound);
        bool emptied = hr == 0 || RecycleBinSize().Bytes == 0;
        Log.Info(LogCategory, emptied ? "Recycle Bin emptied." : $"Emptying the Recycle Bin failed (0x{hr:X8}).");
        return emptied;
    }

    // ---- Memory -----------------------------------------------------------------------------------

    public Task<MemoryCleanResult> FreeMemoryAsync(bool clearStandby, CancellationToken cancellationToken) => Task.Run(async () =>
    {
        var before = MemoryStatus();
        int foreground = ForegroundProcessId();
        int own = Environment.ProcessId;
        int trimmed = 0;
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    continue;
                }

                try
                {
                    if (process.Id is 0 or 4 || process.Id == foreground || process.Id == own || KeepProcesses.Contains(process.ProcessName))
                    {
                        continue; // the app you're using keeps its memory
                    }

                    IntPtr handle = OpenProcess(ProcessQueryLimitedInformation | ProcessSetQuota, false, (uint)process.Id);
                    if (handle == IntPtr.Zero)
                    {
                        continue; // protected process
                    }

                    try
                    {
                        if (EmptyWorkingSet(handle))
                        {
                            trimmed++;
                        }
                    }
                    finally
                    {
                        CloseHandle(handle);
                    }
                }
                catch (Exception e) when (e is InvalidOperationException or Win32Exception)
                {
                    // exited meanwhile
                }
            }
        }

        if (clearStandby && !cancellationToken.IsCancellationRequested)
        {
            ClearStandbyList();
        }

        await Task.Delay(1200, CancellationToken.None); // let Windows settle the lists before measuring
        var after = MemoryStatus();
        Log.Info(LogCategory, $"Trimmed {trimmed} processes; available memory {before.Available / (1 << 20)} → {after.Available / (1 << 20)} MB.");
        return new MemoryCleanResult((long)before.Available, (long)after.Available, (long)after.Total, trimmed);
    }, cancellationToken);

    private static void ClearStandbyList()
    {
        try
        {
            if (!EnablePrivilege("SeProfileSingleProcessPrivilege"))
            {
                Log.Warn(LogCategory, "Couldn't get the privilege to clear the standby list.");
                return;
            }

            int command = MemoryPurgeStandbyList;
            int status = NtSetSystemInformation(SystemMemoryListInformation, ref command, sizeof(int));
            Log.Info(LogCategory, status == 0 ? "Standby list cleared." : $"Clearing the standby list failed (0x{status:X8}).");
        }
        catch (Exception e) when (e is Win32Exception or EntryPointNotFoundException or DllNotFoundException)
        {
            Log.Warn(LogCategory, "Clearing the standby list failed.", e);
        }
    }

    private static (ulong Available, ulong Total) MemoryStatus()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref status) ? (status.AvailablePhys, status.TotalPhys) : (0, 0);
    }

    private static int ForegroundProcessId()
    {
        IntPtr window = GetForegroundWindow();
        return window != IntPtr.Zero && GetWindowThreadProcessId(window, out uint pid) != 0 ? (int)pid : -1;
    }

    // ---- Protection (Microsoft Defender) ----------------------------------------------------------

    public async Task<ProtectionStatus> ProtectionAsync(CancellationToken cancellationToken)
    {
        try
        {
            var (exit, output, error) = await PowerShellAsync(DefenderScripts.Status, TimeSpan.FromSeconds(60), cancellationToken);
            var status = DefenderScripts.ParseStatus(output);
            if (status.Problem is not null)
            {
                Log.Warn(LogCategory, $"Defender status: {status.Problem} (exit {exit}{(string.IsNullOrWhiteSpace(error) ? string.Empty : $", {error.Trim()}")})");
            }

            return status;
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or IOException or TimeoutException)
        {
            Log.Warn(LogCategory, "Couldn't read Defender's status.", e);
            return ProtectionStatus.Unavailable("Windows Security didn't answer. Try again in a moment.");
        }
    }

    public async Task<ScanOutcome> QuickScanAsync(CancellationToken cancellationToken)
    {
        string? tool = MpCmdRun();
        if (tool is null)
        {
            return new ScanOutcome(false, 0, "Microsoft Defender's scanner wasn't found on this PC.");
        }

        try
        {
            Log.Info(LogCategory, "Quick scan started.");
            var (exit, _, _) = await RunAsync(tool, "-Scan -ScanType 1", Timeout.InfiniteTimeSpan, cancellationToken);
            Log.Info(LogCategory, $"Quick scan finished (exit {exit}).");
            return exit switch
            {
                0 => new ScanOutcome(true, 0, null),
                2 => new ScanOutcome(true, 1, null), // threats found; the status lists them
                _ => new ScanOutcome(false, 0, $"Microsoft Defender couldn't run the scan (code {exit}). Is it turned on?"),
            };
        }
        catch (OperationCanceledException)
        {
            _ = RunAsync(tool, "-Scan -Cancel", TimeSpan.FromSeconds(30), CancellationToken.None); // stop the engine's scan too
            throw;
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or IOException or TimeoutException)
        {
            Log.Warn(LogCategory, "Quick scan failed.", e);
            return new ScanOutcome(false, 0, "The scan couldn't start.");
        }
    }

    public async Task<MaintenanceResult> UpdateSignaturesAsync(CancellationToken cancellationToken)
    {
        string? tool = MpCmdRun();
        if (tool is null)
        {
            return new MaintenanceResult(false, "Microsoft Defender wasn't found on this PC.");
        }

        try
        {
            var (exit, _, _) = await RunAsync(tool, "-SignatureUpdate", TimeSpan.FromMinutes(10), cancellationToken);
            Log.Info(LogCategory, $"Definition update finished (exit {exit}).");
            return exit == 0
                ? new MaintenanceResult(true, "Virus definitions are up to date.")
                : new MaintenanceResult(false, "The update didn't finish. Check your connection, or update from Windows Security.");
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or IOException or TimeoutException)
        {
            Log.Warn(LogCategory, "Definition update failed.", e);
            return new MaintenanceResult(false, "The update couldn't start.");
        }
    }

    public async Task<MaintenanceResult> RemoveThreatsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var (exit, _, error) = await PowerShellAsync(DefenderScripts.RemoveThreats, TimeSpan.FromMinutes(5), cancellationToken);
            Log.Info(LogCategory, $"Threat removal finished (exit {exit}).");
            return exit == 0
                ? new MaintenanceResult(true, "Threats removed.")
                : new MaintenanceResult(false, $"Microsoft Defender couldn't remove them{(string.IsNullOrWhiteSpace(error) ? "." : $": {FirstLine(error)}")} Open Windows Security to review.");
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or IOException or TimeoutException)
        {
            Log.Warn(LogCategory, "Threat removal failed.", e);
            return new MaintenanceResult(false, "Removal couldn't start. Open Windows Security to review the threats.");
        }
    }

    public void OpenSecurity()
    {
        try
        {
            Process.Start(new ProcessStartInfo("windowsdefender://threat/") { UseShellExecute = true })?.Dispose();
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            Log.Warn(LogCategory, "Couldn't open Windows Security.", e);
        }
    }

    /// <summary>The newest Defender platform's MpCmdRun.exe, or the one in Program Files.</summary>
    private static string? MpCmdRun()
    {
        try
        {
            string platform = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Microsoft\Windows Defender\Platform");
            if (Directory.Exists(platform))
            {
                var newest = new DirectoryInfo(platform).EnumerateDirectories()
                    .Select(d => (Dir: d, Version: Version.TryParse(d.Name.Split('-')[0], out var v) ? v : new Version(0, 0)))
                    .OrderByDescending(x => x.Version)
                    .Select(x => Path.Combine(x.Dir.FullName, "MpCmdRun.exe"))
                    .FirstOrDefault(File.Exists);
                if (newest is not null)
                {
                    return newest;
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // fall back below
        }

        string programFiles = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Windows Defender\MpCmdRun.exe");
        return File.Exists(programFiles) ? programFiles : null;
    }

    // ---- Startup apps -----------------------------------------------------------------------------

    public IReadOnlyList<StartupApp> StartupApps()
    {
        var apps = new List<StartupApp>();
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var user = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        AddRun(apps, user, StartupApproved.RunKey, StartupScope.User, user);
        AddRun(apps, machine, StartupApproved.RunKey, StartupScope.Machine, machine);
        AddRun(apps, machine, StartupApproved.RunKey32, StartupScope.Machine32, machine);
        AddFolder(apps, Environment.GetFolderPath(Environment.SpecialFolder.Startup), StartupScope.UserFolder, user);
        AddFolder(apps, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), StartupScope.CommonFolder, machine);
        return apps;
    }

    public bool SetStartupEnabled(StartupApp app, bool enabled)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(StartupApproved.IsUserHive(app.Scope) ? RegistryHive.CurrentUser : RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = root.CreateSubKey(StartupApproved.KeyFor(app.Scope), writable: true);
            key.SetValue(app.ValueName, StartupApproved.Value(enabled, DateTime.UtcNow), RegistryValueKind.Binary);
            Log.Info(LogCategory, $"Startup app \"{app.Name}\" turned {(enabled ? "on" : "off")}.");
            return true;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            Log.Warn(LogCategory, $"Couldn't change startup app \"{app.Name}\".", e);
            return false;
        }
    }

    private static void AddRun(List<StartupApp> apps, RegistryKey hive, string runKey, StartupScope scope, RegistryKey approvedHive)
    {
        try
        {
            using var run = hive.OpenSubKey(runKey);
            if (run is null)
            {
                return;
            }

            using var approved = approvedHive.OpenSubKey(StartupApproved.KeyFor(scope));
            foreach (string name in run.GetValueNames())
            {
                if (string.IsNullOrEmpty(name) || run.GetValue(name) is not string command || string.IsNullOrWhiteSpace(command))
                {
                    continue;
                }

                bool enabled = StartupApproved.IsEnabled(approved?.GetValue(name) as byte[]);
                apps.Add(Describe(name, command, scope, enabled));
            }
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            Log.Warn(LogCategory, $"Couldn't read startup apps in {runKey}.", e);
        }
    }

    private static void AddFolder(List<StartupApp> apps, string folder, StartupScope scope, RegistryKey approvedHive)
    {
        try
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                return;
            }

            using var approved = approvedHive.OpenSubKey(StartupApproved.KeyFor(scope));
            foreach (var file in new DirectoryInfo(folder).EnumerateFiles())
            {
                if (!StartupFileTypes.Contains(file.Extension, StringComparer.OrdinalIgnoreCase))
                {
                    continue; // desktop.ini and the like
                }

                bool enabled = StartupApproved.IsEnabled(approved?.GetValue(file.Name) as byte[]);
                apps.Add(Describe(file.Name, file.FullName, scope, enabled));
            }
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            Log.Warn(LogCategory, $"Couldn't read the startup folder {folder}.", e);
        }
    }

    private static StartupApp Describe(string valueName, string command, StartupScope scope, bool enabled)
    {
        string? exe = StartupApproved.Executable(command);
        string? publisher = null;
        string? description = null;
        try
        {
            if (exe is not null && exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(exe))
            {
                var info = FileVersionInfo.GetVersionInfo(exe);
                publisher = string.IsNullOrWhiteSpace(info.CompanyName) ? null : info.CompanyName.Trim();
                description = string.IsNullOrWhiteSpace(info.FileDescription) ? null : info.FileDescription.Trim();
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // no version info
        }

        string name = StartupApproved.NameFrom(valueName, exe);
        if (name == valueName && (valueName.Contains('{') || valueName.Length < 2) && description is not null)
        {
            name = description;
        }

        return new StartupApp(name, valueName, command, scope, enabled) { Publisher = publisher, Executable = exe };
    }

    // ---- Maintenance ------------------------------------------------------------------------------

    public Task<MaintenanceResult> FlushDnsAsync(CancellationToken cancellationToken) => Task.Run(() =>
    {
        try
        {
            bool ok = DnsFlushResolverCache() != 0;
            Log.Info(LogCategory, ok ? "DNS cache flushed." : "Flushing the DNS cache failed.");
            return ok ? new MaintenanceResult(true, "DNS cache cleared.") : new MaintenanceResult(false, "Windows didn't clear the DNS cache.");
        }
        catch (Exception e) when (e is EntryPointNotFoundException or DllNotFoundException)
        {
            return new MaintenanceResult(false, "This version of Windows can't clear the DNS cache this way.");
        }
    }, cancellationToken);

    public async Task<MaintenanceResult> OptimizeDrivesAsync(CancellationToken cancellationToken)
    {
        string defrag = Path.Combine(Environment.SystemDirectory, "defrag.exe");
        if (!File.Exists(defrag))
        {
            return new MaintenanceResult(false, "Windows' drive optimizer wasn't found.");
        }

        try
        {
            Log.Info(LogCategory, "Optimizing drives.");
            var (exit, _, _) = await RunAsync(defrag, "/C /O /H", TimeSpan.FromHours(2), cancellationToken);
            Log.Info(LogCategory, $"Drive optimization finished (exit {exit}).");
            return exit == 0
                ? new MaintenanceResult(true, "Drives optimized.")
                : new MaintenanceResult(false, $"Windows couldn't optimize every drive (code {exit}). Try Optimize Drives in Windows.");
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or IOException or TimeoutException)
        {
            Log.Warn(LogCategory, "Drive optimization failed.", e);
            return new MaintenanceResult(false, "The optimizer couldn't start.");
        }
    }

    // ---- Processes --------------------------------------------------------------------------------

    private static Task<(int Exit, string Output, string Error)> PowerShellAsync(string script, TimeSpan timeout, CancellationToken cancellationToken)
    {
        // UTF-8 out, so names and paths with any characters survive; the script goes in encoded, so nothing needs quoting.
        string full = "[Console]::OutputEncoding = [System.Text.Encoding]::UTF8\n" + script;
        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(full));
        string powershell = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
        return RunAsync(powershell, $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}", timeout, cancellationToken);
    }

    internal static async Task<(int Exit, string Output, string Error)> RunAsync(string file, string arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(file, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WindowStyle = ProcessWindowStyle.Hidden,
            },
        };
        process.Start();
        var output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var error = process.StandardError.ReadToEndAsync(CancellationToken.None);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeout != Timeout.InfiniteTimeSpan)
        {
            limit.CancelAfter(timeout);
        }

        try
        {
            await process.WaitForExitAsync(limit.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception e) when (e is InvalidOperationException or Win32Exception)
            {
                // already gone
            }

            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            throw new TimeoutException($"{Path.GetFileName(file)} took too long.");
        }

        return (process.ExitCode, await output, await error);
    }

    private static string FirstLine(string text) => text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? text.Trim();

    private static bool EnablePrivilege(string name)
    {
        if (!OpenProcessToken(GetCurrentProcess(), 0x0020 | 0x0008, out IntPtr token)) // TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY
        {
            return false;
        }

        try
        {
            if (!LookupPrivilegeValue(null, name, out long luid))
            {
                return false;
            }

            var privileges = new TokenPrivileges { Count = 1, Luid = luid, Attributes = 0x2 }; // SE_PRIVILEGE_ENABLED
            return AdjustTokenPrivileges(token, false, ref privileges, 0, IntPtr.Zero, IntPtr.Zero) && Marshal.GetLastPInvokeError() == 0;
        }
        finally
        {
            CloseHandle(token);
        }
    }

    // ---- Native -----------------------------------------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    private struct RecycleBinInfo
    {
        public int Size;
        public long Bytes;
        public long Items;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailablePhys;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TokenPrivileges
    {
        public uint Count;
        public long Luid;
        public uint Attributes;
    }

    [LibraryImport("shell32.dll", EntryPoint = "SHQueryRecycleBinW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHQueryRecycleBin(string? rootPath, ref RecycleBinInfo info);

    [LibraryImport("shell32.dll", EntryPoint = "SHEmptyRecycleBinW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHEmptyRecycleBin(IntPtr window, string? rootPath, uint flags);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);

    [LibraryImport("kernel32.dll", EntryPoint = "K32EmptyWorkingSet")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EmptyWorkingSet(IntPtr process);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr handle);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr GetCurrentProcess();

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalMemoryStatusEx(ref MemoryStatusEx status);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [LibraryImport("ntdll.dll")]
    private static partial int NtSetSystemInformation(int informationClass, ref int information, int length);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [LibraryImport("advapi32.dll", EntryPoint = "LookupPrivilegeValueW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool LookupPrivilegeValue(string? systemName, string name, out long luid);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AdjustTokenPrivileges(IntPtr token, [MarshalAs(UnmanagedType.Bool)] bool disableAll, ref TokenPrivileges newState, uint bufferLength, IntPtr previousState, IntPtr returnLength);

    [LibraryImport("dnsapi.dll")]
    private static partial int DnsFlushResolverCache();
}
