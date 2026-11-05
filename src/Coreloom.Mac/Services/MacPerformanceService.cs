using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia.Threading;
using Coreloom.Core.Diagnostics;
using Coreloom.Core.Gaming;
using Coreloom.Hardware.Mac;
using Coreloom.Hardware.Mac.Native;
using Coreloom.Hardware.Mac.Providers;
using Coreloom.Mac.Native;
using Coreloom.ViewModels.Services;

namespace Coreloom.Mac.Services;

/// <summary>
/// What the Mac's Gaming boosts read and change (since 1.9.4), from macOS itself: the charger's power mode (pmset, and
/// Coreloom's one sudoers rule for it), memory pressure (sysctl, the kernel's page counts), the thermal state
/// (NSProcessInfo), each process's time on performance cores and per QoS class (proc_pid_rusage), MetalFX in games'
/// programs (their names, read from the files), the displays (CoreGraphics and NSScreen), programs under Rosetta 2
/// (the kernel's P_TRANSLATED flag), and the apps running (NSWorkspace, with their GPU time from the IORegistry).
/// </summary>
public sealed partial class MacPerformanceService : IMacPerformanceService
{
    private const string LogCategory = "Gaming";
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string AppKitLibrary = "/System/Library/Frameworks/AppKit.framework/AppKit";

    private readonly object _gate = new();
    private readonly Dictionary<int, (string Name, string? Path)> _names = [];
    private readonly Dictionary<int, (ulong Ticks, long Time)> _appCpu = [];
    private readonly Dictionary<int, (ulong Gpu, long Time)> _appGpu = [];
    private DateTime _namesPruned = DateTime.MinValue;

    public bool IsSupported => OperatingSystem.IsMacOS();

    public bool IsAppleSilicon => MacSystemInfo.IsAppleSilicon;

    public double TicksToSeconds => Darwin.TicksToNanoseconds * 1e-9;

    // ------------------------------------------------------------------------------------------------ power

    public async Task<MacPowerState> PowerAsync(CancellationToken cancellationToken)
    {
        var custom = await Commands.RunAsync("/usr/bin/pmset", ["-g", "custom"], cancellationToken: cancellationToken);
        var charger = custom.Succeeded ? PowerSettings.ChargerSection(custom.Output) : [];
        bool hasHighPower = charger.TryGetValue("powermode", out int mode);
        var source = await Commands.RunAsync("/usr/bin/pmset", ["-g", "ps"], cancellationToken: cancellationToken);
        bool onCharger = !source.Succeeded || source.Output.Contains("'AC Power'", StringComparison.Ordinal) || !source.Output.Contains("Battery Power", StringComparison.Ordinal);
        bool silent = false;
        if (File.Exists(HighPowerAutomation.SudoersFile))
        {
            var check = await Commands.RunAsync("/usr/bin/sudo", ["-n", "-l", "/usr/bin/pmset", "-c", "powermode", "2"], TimeSpan.FromSeconds(10), cancellationToken);
            silent = check.Succeeded;
        }

        return new MacPowerState(hasHighPower, onCharger, hasHighPower ? mode : 0, silent);
    }

    public async Task<TweakResult> AllowPowerSwitchingAsync(bool allow, CancellationToken cancellationToken)
    {
        string file = HighPowerAutomation.SudoersFile;
        if (!allow)
        {
            if (!File.Exists(file))
            {
                return new TweakResult(true, "Coreloom no longer switches the power mode.");
            }

            var removed = await Commands.RunAsAdministratorAsync($"/bin/rm -f {Commands.Quote(file)}",
                "Coreloom wants to remove its permission to switch High Power Mode.", cancellationToken);
            if (Commands.WasCancelled(removed))
            {
                return new TweakResult(true, "Turned off. Coreloom's permission stays until you turn this off again and enter your password.");
            }

            Log.Info(LogCategory, removed.Succeeded ? "Removed the permission to switch the charger's power mode." : $"Removing the power mode permission failed: {removed.Error}");
            return removed.Succeeded ? new TweakResult(true, "Coreloom no longer switches the power mode.") : new TweakResult(false, $"macOS didn't accept the change: {removed.Error}");
        }

        if (HighPowerAutomation.SafeUserName(Environment.UserName) is not { } user)
        {
            return new TweakResult(false, "Your user name can't be used in the permission macOS needs.");
        }

        // Written to a file whose name has a dot (sudo ignores those), checked by visudo, then moved into place: a mistake
        // can never become a live rule.
        string temporary = file + ".new";
        string command = string.Join(" && ",
            "umask 0337",
            $"/bin/mkdir -p {Commands.Quote(Path.GetDirectoryName(file)!)}",
            $"/usr/bin/printf '%s\\n' {Commands.Quote(HighPowerAutomation.SudoersRule(user))} > {Commands.Quote(temporary)}",
            $"/usr/sbin/visudo -cf {Commands.Quote(temporary)}",
            $"/usr/sbin/chown root:wheel {Commands.Quote(temporary)}",
            $"/bin/chmod 0440 {Commands.Quote(temporary)}",
            $"/bin/mv -f {Commands.Quote(temporary)} {Commands.Quote(file)}")
            + $" || {{ /bin/rm -f {Commands.Quote(temporary)}; exit 1; }}";
        var result = await Commands.RunAsAdministratorAsync(command,
            "Coreloom wants to switch High Power Mode on and off by itself while you play on the charger.", cancellationToken);
        if (Commands.WasCancelled(result))
        {
            return new TweakResult(false, "Nothing changed: the password prompt was cancelled.");
        }

        if (!result.Succeeded)
        {
            Log.Warn(LogCategory, $"Allowing the power mode switch failed: {result.Error}");
            return new TweakResult(false, $"macOS didn't accept the change: {result.Error}");
        }

        Log.Info(LogCategory, $"Allowed Coreloom to set the charger's power mode for {user} ({file}).");
        return new TweakResult(true, "High Power Mode follows your games now.");
    }

    public async Task<bool> SetChargerPowerModeAsync(int mode, CancellationToken cancellationToken)
    {
        if (mode is < 0 or > 2)
        {
            return false;
        }

        var result = await Commands.RunAsync("/usr/bin/sudo", ["-n", "/usr/bin/pmset", "-c", "powermode", mode.ToString(CultureInfo.InvariantCulture)], TimeSpan.FromSeconds(15), cancellationToken);
        if (!result.Succeeded)
        {
            Log.WarnThrottled("mac-powermode-set", TimeSpan.FromMinutes(10), LogCategory, $"Setting the charger's power mode to {mode} failed: {result.Error}");
        }

        return result.Succeeded;
    }

    // ------------------------------------------------------------------------------------------------ memory and heat

    public unsafe MemorySnapshot? Memory()
    {
        long total = Darwin.SysctlLong("hw.memsize") ?? 0;
        if (total <= 0 || !Darwin.VmStatistics(out var vm))
        {
            return null;
        }

        var memory = MemoryMath.FromStatistics(vm, Darwin.PageSize(), total);
        long swap = Darwin.SysctlStruct("vm.swapusage", out Darwin.XswUsage usage) ? (long)usage.Used : 0;
        var pressure = (Darwin.SysctlLong("kern.memorystatus_vm_pressure_level") ?? 1) switch
        {
            >= 4 => MemoryPressure.Critical,
            >= 2 => MemoryPressure.Warning,
            _ => MemoryPressure.Normal,
        };
        var apps = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (int pid in Darwin.AllPids())
        {
            if (pid <= 0)
            {
                continue;
            }

            Darwin.RusageInfoV4 rusage;
            if (Darwin.proc_pid_rusage(pid, Darwin.RusageInfoVersion4, &rusage) != 0)
            {
                continue;
            }

            string name = NameOf(pid).Name;
            apps[name] = apps.GetValueOrDefault(name) + (long)rusage.PhysFootprint;
        }

        PruneNames();
        return new MemorySnapshot(pressure, total, (long)memory.Used, (long)memory.Compressed, swap,
            [.. apps.OrderByDescending(a => a.Value).Take(12).Select(a => new AppMemory(a.Key, a.Value))]);
    }

    public ThermalPressure? ThermalState()
    {
        nint pool = ObjC.objc_autoreleasePoolPush();
        try
        {
            nint info = ObjC.Send(ObjC.Class("NSProcessInfo"), ObjC.Sel("processInfo"));
            return info == 0 ? null : (ThermalPressure)Math.Clamp((int)ObjC.SendLongResult(info, ObjC.Sel("thermalState")), 0, 3);
        }
        finally
        {
            ObjC.objc_autoreleasePoolPop(pool);
        }
    }

    // ------------------------------------------------------------------------------------------------ cores

    public unsafe IReadOnlyList<CoreTimes> CoreTimes()
    {
        var times = new List<CoreTimes>();
        byte* buffer = stackalloc byte[1024];
        foreach (int pid in Darwin.AllPids())
        {
            if (pid <= 0)
            {
                continue;
            }

            Darwin.ProcTaskInfo task;
            if (Darwin.proc_pidinfo(pid, Darwin.ProcPidTaskInfo, 0, &task, sizeof(Darwin.ProcTaskInfo)) != sizeof(Darwin.ProcTaskInfo))
            {
                continue;
            }

            new Span<byte>(buffer, 1024).Clear();
            if (Darwin.proc_pid_rusage(pid, 6, buffer) != 0) // RUSAGE_INFO_V6 (macOS 12+): adds the time on performance cores
            {
                continue;
            }

            // After the 16-byte UUID, 64-bit fields in order (sys/resource.h): 0 user time, 1 system time, … 18 QoS
            // default, 19 maintenance, 20 background, 21 utility, 22 legacy, 23 user-initiated, 24 user-interactive, …
            // 35 flags, 36 user time on P-cores, 37 system time on P-cores.
            ulong* f = (ulong*)(buffer + 16);
            times.Add(new CoreTimes(pid, NameOf(pid).Name, task.TotalUser + task.TotalSystem, f[0] + f[1], f[36] + f[37],
                f[24], f[23], f[18] + f[22], f[21], f[20] + f[19]));
        }

        PruneNames();
        return times;
    }

    private (string Name, string? Path) NameOf(int pid)
    {
        lock (_gate)
        {
            if (_names.TryGetValue(pid, out var known))
            {
                return known;
            }

            string? path = Darwin.ProcessPath(pid);
            string name = MacProcessSampler.AppName(path) ?? Darwin.ProcessName(pid) ?? (path is null ? $"Process {pid.ToString(CultureInfo.InvariantCulture)}" : Path.GetFileName(path));
            _names[pid] = (name, path);
            return (name, path);
        }
    }

    private void PruneNames()
    {
        lock (_gate)
        {
            if (DateTime.UtcNow - _namesPruned < TimeSpan.FromMinutes(1))
            {
                return;
            }

            _namesPruned = DateTime.UtcNow;
            var alive = Darwin.AllPids().ToHashSet();
            foreach (int gone in _names.Keys.Where(p => !alive.Contains(p)).ToArray())
            {
                _names.Remove(gone);
                _appCpu.Remove(gone);
                _appGpu.Remove(gone);
            }
        }
    }

    // ------------------------------------------------------------------------------------------------ games' programs

    public Task<MetalFxFeatures> MetalFxAsync(GameEntry game, CancellationToken cancellationToken) => Task.Run(() =>
    {
        var found = MetalFxFeatures.None;
        foreach (string program in Programs(game).Take(80))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var stream = new FileStream(program, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1, FileOptions.SequentialScan);
                found |= MetalFx.ScanProgram(stream);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or EndOfStreamException or ArgumentException)
            {
            }
        }

        return found;
    }, cancellationToken);

    public Task<CpuArchitecture> ArchitectureAsync(GameEntry game, CancellationToken cancellationToken) => Task.Run(() =>
    {
        if (MainProgram(game) is not { } program)
        {
            return CpuArchitecture.Unknown;
        }

        try
        {
            using var stream = File.OpenRead(program);
            byte[] header = new byte[(int)Math.Min(4096, stream.Length)];
            stream.ReadExactly(header);
            return MachO.Architecture(header);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or EndOfStreamException)
        {
            return CpuArchitecture.Unknown;
        }
    }, cancellationToken);

    /// <summary>The game's app bundle: its program if that's an app, its folder if that is one, or the first app in its folder.</summary>
    private static string? Bundle(GameEntry game)
    {
        foreach (string? path in new[] { game.Executable, game.InstallPath })
        {
            if (path is null)
            {
                continue;
            }

            int app = path.IndexOf(".app", StringComparison.OrdinalIgnoreCase);
            if (app > 0 && (app + 4 == path.Length || path[app + 4] == '/'))
            {
                string bundle = path[..(app + 4)];
                if (Directory.Exists(bundle))
                {
                    return bundle;
                }
            }
        }

        if (game.InstallPath is { } folder && Directory.Exists(folder))
        {
            try
            {
                return Directory.EnumerateDirectories(folder, "*.app", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 2, IgnoreInaccessible = true })
                    .OrderBy(d => d.Length)
                    .FirstOrDefault();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }

        return null;
    }

    private static string? MainProgram(GameEntry game)
    {
        if (Bundle(game) is { } bundle)
        {
            if (CF.ReadPropertyList(Path.Combine(bundle, "Contents", "Info.plist")) is Dictionary<string, object?> plist
                && CF.Text(plist, "CFBundleExecutable") is { Length: > 0 } executable
                && File.Exists(Path.Combine(bundle, "Contents", "MacOS", executable)))
            {
                return Path.Combine(bundle, "Contents", "MacOS", executable);
            }

            return SafeFiles(Path.Combine(bundle, "Contents", "MacOS"), recurse: false).FirstOrDefault();
        }

        return game.Executable is { } exe && File.Exists(exe) ? exe : null;
    }

    /// <summary>The game's program and the frameworks and plug-ins inside its app (where engines keep their renderers).</summary>
    private static IEnumerable<string> Programs(GameEntry game)
    {
        string? main = MainProgram(game);
        if (main is not null)
        {
            yield return main;
        }

        if (Bundle(game) is not { } bundle)
        {
            yield break;
        }

        foreach (string folder in new[] { "MacOS", "Frameworks", "PlugIns" })
        {
            foreach (string file in SafeFiles(Path.Combine(bundle, "Contents", folder), recurse: folder != "MacOS"))
            {
                if (file != main && LooksLikeProgram(file))
                {
                    yield return file;
                }
            }
        }
    }

    private static IEnumerable<string> SafeFiles(string folder, bool recurse)
    {
        if (!Directory.Exists(folder))
        {
            return [];
        }

        try
        {
            return Directory.EnumerateFiles(folder, "*", new EnumerationOptions { RecurseSubdirectories = recurse, MaxRecursionDepth = 5, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint })
                .ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>A Mach-O program or library (its first bytes), not a picture, sound or script.</summary>
    private static bool LooksLikeProgram(string file)
    {
        try
        {
            string extension = Path.GetExtension(file);
            if (extension.Length > 0 && !extension.Equals(".dylib", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".so", StringComparison.OrdinalIgnoreCase)
                && !extension.Equals(".bundle", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            using var stream = File.OpenRead(file);
            if (stream.Length < 4096)
            {
                return false;
            }

            Span<byte> head = stackalloc byte[64];
            stream.ReadExactly(head);
            return MachO.CpuTypes(head).Count > 0 || MachO.PreferredSlice(head, stream.Length) is not null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or EndOfStreamException)
        {
            return false;
        }
    }

    // ------------------------------------------------------------------------------------------------ displays

    [LibraryImport(CoreGraphics)]
    private static unsafe partial int CGGetActiveDisplayList(uint maxDisplays, uint* displays, out uint count);

    [LibraryImport(CoreGraphics)]
    private static partial nint CGDisplayCopyDisplayMode(uint display);

    [LibraryImport(CoreGraphics)]
    private static partial nint CGDisplayCopyAllDisplayModes(uint display, nint options);

    [LibraryImport(CoreGraphics)]
    private static partial void CGDisplayModeRelease(nint mode);

    [LibraryImport(CoreGraphics)]
    private static partial nuint CGDisplayModeGetWidth(nint mode);

    [LibraryImport(CoreGraphics)]
    private static partial nuint CGDisplayModeGetHeight(nint mode);

    [LibraryImport(CoreGraphics)]
    private static partial nuint CGDisplayModeGetPixelWidth(nint mode);

    [LibraryImport(CoreGraphics)]
    private static partial nuint CGDisplayModeGetPixelHeight(nint mode);

    [LibraryImport(CoreGraphics)]
    private static partial double CGDisplayModeGetRefreshRate(nint mode);

    [LibraryImport(CoreGraphics)]
    private static partial uint CGDisplayModeGetIOFlags(nint mode);

    [LibraryImport(CoreGraphics)]
    private static partial uint CGDisplayIsBuiltin(uint display);

    [LibraryImport(AppKitLibrary)]
    private static partial nint NSBitsPerSampleFromDepth(int depth);

    private const uint NativeModeFlag = 0x02000000; // kDisplayModeNativeFlag

    private sealed record ScreenFacts(string Name, double EdrHeadroom, int Bits, int MaxFps, bool Variable);

    public unsafe IReadOnlyList<DisplayDescription> Displays()
    {
        var screens = Dispatcher.UIThread.CheckAccess() ? Screens() : Dispatcher.UIThread.Invoke(Screens);
        uint* ids = stackalloc uint[16];
        if (CGGetActiveDisplayList(16, ids, out uint count) != 0)
        {
            return [];
        }

        var result = new List<DisplayDescription>();
        for (int i = 0; i < count; i++)
        {
            uint id = ids[i];
            nint mode = CGDisplayCopyDisplayMode(id);
            if (mode == 0)
            {
                continue;
            }

            try
            {
                int width = (int)CGDisplayModeGetWidth(mode), height = (int)CGDisplayModeGetHeight(mode);
                int pixelWidth = (int)CGDisplayModeGetPixelWidth(mode), pixelHeight = (int)CGDisplayModeGetPixelHeight(mode);
                double refresh = CGDisplayModeGetRefreshRate(mode);
                var (maxRefresh, nativeWidth, nativeHeight) = Modes(id, width, height);
                screens.TryGetValue(id, out var facts);
                bool builtIn = CGDisplayIsBuiltin(id) != 0;
                if (refresh <= 0)
                {
                    refresh = facts?.MaxFps ?? 60; // built-in panels report no mode rate on older macOS
                }

                maxRefresh = Math.Max(maxRefresh, Math.Max(refresh, facts?.Variable == true ? facts.MaxFps : 0));
                result.Add(new DisplayDescription(facts?.Name ?? (builtIn ? "Built-in display" : "Display"), builtIn,
                    new DisplayMode(pixelWidth, pixelHeight, width, height, refresh), maxRefresh, facts?.Variable == true,
                    facts?.EdrHeadroom ?? 1, facts?.Bits ?? 0, nativeWidth, nativeHeight));
            }
            finally
            {
                CGDisplayModeRelease(mode);
            }
        }

        return result;
    }

    /// <summary>The fastest refresh at this size, and the panel's own pixels (the mode macOS marks native).</summary>
    private static (double MaxRefresh, int NativeWidth, int NativeHeight) Modes(uint display, int width, int height)
    {
        nint modes = CGDisplayCopyAllDisplayModes(display, 0);
        if (modes == 0)
        {
            return (0, 0, 0);
        }

        try
        {
            double max = 0;
            int nativeWidth = 0, nativeHeight = 0;
            long n = CF.CFArrayGetCount(modes);
            for (long i = 0; i < n; i++)
            {
                nint mode = CF.CFArrayGetValueAtIndex(modes, (nint)i);
                if ((int)CGDisplayModeGetWidth(mode) == width && (int)CGDisplayModeGetHeight(mode) == height)
                {
                    max = Math.Max(max, CGDisplayModeGetRefreshRate(mode));
                }

                if ((CGDisplayModeGetIOFlags(mode) & NativeModeFlag) != 0)
                {
                    int w = (int)CGDisplayModeGetPixelWidth(mode);
                    if (w > nativeWidth)
                    {
                        nativeWidth = w;
                        nativeHeight = (int)CGDisplayModeGetPixelHeight(mode);
                    }
                }
            }

            return (max, nativeWidth, nativeHeight);
        }
        finally
        {
            CF.CFRelease(modes);
        }
    }

    /// <summary>NSScreen's names, HDR headroom, colour depth and refresh (main thread only), by display id.</summary>
    private static Dictionary<uint, ScreenFacts> Screens()
    {
        var result = new Dictionary<uint, ScreenFacts>();
        nint pool = ObjC.objc_autoreleasePoolPush();
        try
        {
            nint screens = ObjC.Send(ObjC.Class("NSScreen"), ObjC.Sel("screens"));
            ulong count = screens == 0 ? 0 : ObjC.SendULong(screens, ObjC.Sel("count"));
            for (ulong i = 0; i < count; i++)
            {
                nint screen = ObjC.Send(screens, ObjC.Sel("objectAtIndex:"), (nint)i);
                nint description = ObjC.Send(screen, ObjC.Sel("deviceDescription"));
                nint number = ObjC.Send(description, ObjC.Sel("objectForKey:"), ObjC.NSString("NSScreenNumber"));
                if (number == 0)
                {
                    continue;
                }

                uint id = (uint)ObjC.SendULong(number, ObjC.Sel("unsignedLongLongValue"));
                string? name = Responds(screen, "localizedName") ? ObjC.ToStringValue(ObjC.Send(screen, ObjC.Sel("localizedName"))) : null;
                double edr = Responds(screen, "maximumPotentialExtendedDynamicRangeColorComponentValue")
                    ? ObjC.SendDouble(screen, ObjC.Sel("maximumPotentialExtendedDynamicRangeColorComponentValue"))
                    : 1;
                int depth = (int)(ObjC.SendLongResult(screen, ObjC.Sel("depth")) & 0xFFFFFFFF);
                int bits = depth == 0 ? 0 : (int)NSBitsPerSampleFromDepth(depth);
                int maxFps = Responds(screen, "maximumFramesPerSecond") ? (int)ObjC.SendLongResult(screen, ObjC.Sel("maximumFramesPerSecond")) : 0;
                bool variable = false;
                if (Responds(screen, "maximumRefreshInterval") && Responds(screen, "minimumRefreshInterval"))
                {
                    double slowest = ObjC.SendDouble(screen, ObjC.Sel("maximumRefreshInterval"));
                    double fastest = ObjC.SendDouble(screen, ObjC.Sel("minimumRefreshInterval"));
                    variable = fastest > 0 && slowest > fastest * 1.5;
                }

                result[id] = new ScreenFacts(name ?? "Display", double.IsFinite(edr) && edr > 0 ? edr : 1, bits is > 0 and < 32 ? bits : 0, maxFps, variable);
            }
        }
        catch (Exception e) when (e is InvalidOperationException or EntryPointNotFoundException or DllNotFoundException)
        {
            Log.WarnThrottled("mac-screens", TimeSpan.FromMinutes(30), LogCategory, "Reading the screens failed.", e);
        }
        finally
        {
            ObjC.objc_autoreleasePoolPop(pool);
        }

        return result;
    }

    private static bool Responds(nint receiver, string selector) =>
        ObjC.SendBool(receiver, ObjC.Sel("respondsToSelector:"), ObjC.Sel(selector));

    // ------------------------------------------------------------------------------------------------ Rosetta

    [LibraryImport("/usr/lib/libSystem.dylib", SetLastError = true)]
    private static unsafe partial int sysctl(int* name, uint length, void* oldValue, nuint* oldLength, void* newValue, nuint newLength);

    private const int TranslatedFlag = 0x00020000; // P_TRANSLATED in extern_proc.p_flag

    /// <summary>Whether macOS runs a process through Rosetta 2 (its kinfo_proc's P_TRANSLATED flag).</summary>
    private static unsafe bool IsTranslated(int pid)
    {
        int* mib = stackalloc int[4] { 1, 14, 1, pid }; // CTL_KERN, KERN_PROC, KERN_PROC_PID
        byte* info = stackalloc byte[1024];
        nuint length = 1024;
        if (sysctl(mib, 4, info, &length, null, 0) != 0 || length < 36)
        {
            return false;
        }

        int flags = *(int*)(info + 32); // after p_un (16 bytes), p_vmspace and p_sigacts (8 each)
        return (flags & TranslatedFlag) != 0;
    }

    public IReadOnlyList<TranslatedProcess> TranslatedProcesses()
    {
        if (!IsAppleSilicon)
        {
            return [];
        }

        var result = new List<TranslatedProcess>();
        foreach (int pid in Darwin.AllPids())
        {
            if (pid > 0 && IsTranslated(pid))
            {
                result.Add(new TranslatedProcess(pid, NameOf(pid).Name));
            }
        }

        return result;
    }

    // ------------------------------------------------------------------------------------------------ apps

    public unsafe IReadOnlyList<RunningAppInfo> RunningApps()
    {
        var apps = new List<(int Pid, string Name, string? BundleId, string? Path, bool Regular)>();
        nint pool = ObjC.objc_autoreleasePoolPush();
        try
        {
            nint workspace = ObjC.Send(ObjC.Class("NSWorkspace"), ObjC.Sel("sharedWorkspace"));
            nint running = ObjC.Send(workspace, ObjC.Sel("runningApplications"));
            ulong count = running == 0 ? 0 : ObjC.SendULong(running, ObjC.Sel("count"));
            for (ulong i = 0; i < count; i++)
            {
                nint app = ObjC.Send(running, ObjC.Sel("objectAtIndex:"), (nint)i);
                if (ObjC.SendBool(app, ObjC.Sel("isTerminated")))
                {
                    continue;
                }

                int pid = (int)(ObjC.SendLongResult(app, ObjC.Sel("processIdentifier")) & 0xFFFFFFFF);
                long policy = ObjC.SendLongResult(app, ObjC.Sel("activationPolicy"));
                string? name = ObjC.ToStringValue(ObjC.Send(app, ObjC.Sel("localizedName")));
                string? bundleId = ObjC.ToStringValue(ObjC.Send(app, ObjC.Sel("bundleIdentifier")));
                nint url = ObjC.Send(app, ObjC.Sel("bundleURL"));
                string? path = url == 0 ? null : ObjC.ToStringValue(ObjC.Send(url, ObjC.Sel("path")));
                if (pid > 0 && pid != Environment.ProcessId && name is { Length: > 0 })
                {
                    apps.Add((pid, name, bundleId, path, policy == 0));
                }
            }
        }
        finally
        {
            ObjC.objc_autoreleasePoolPop(pool);
        }

        // Each app's processes: those inside its bundle (helpers, renderers), and the app itself.
        long now = Stopwatch.GetTimestamp();
        var gpu = GpuTimes();
        var totals = apps.ToDictionary(a => a.Pid, _ => (Ticks: 0UL, Memory: 0L, Gpu: 0UL));
        var owners = apps.Where(a => a.Path is not null).Select(a => (a.Pid, Prefix: a.Path!.TrimEnd('/') + "/")).ToList();
        foreach (int pid in Darwin.AllPids())
        {
            if (pid <= 0)
            {
                continue;
            }

            int owner = totals.ContainsKey(pid) ? pid : NameOf(pid).Path is { } path ? owners.FirstOrDefault(o => path.StartsWith(o.Prefix, StringComparison.Ordinal)).Pid : 0;
            if (owner == 0)
            {
                continue;
            }

            Darwin.ProcTaskInfo task;
            if (Darwin.proc_pidinfo(pid, Darwin.ProcPidTaskInfo, 0, &task, sizeof(Darwin.ProcTaskInfo)) != sizeof(Darwin.ProcTaskInfo))
            {
                continue;
            }

            long memory = (long)task.ResidentSize;
            Darwin.RusageInfoV4 rusage;
            if (Darwin.proc_pid_rusage(pid, Darwin.RusageInfoVersion4, &rusage) == 0)
            {
                memory = (long)rusage.PhysFootprint;
            }

            var t = totals[owner];
            totals[owner] = (t.Ticks + task.TotalUser + task.TotalSystem, t.Memory + memory, t.Gpu + gpu.GetValueOrDefault(pid));
        }

        var result = new List<RunningAppInfo>();
        lock (_gate)
        {
            foreach (var app in apps)
            {
                var t = totals[app.Pid];
                double cpu = 0;
                if (_appCpu.TryGetValue(app.Pid, out var before) && t.Ticks >= before.Ticks)
                {
                    double seconds = Stopwatch.GetElapsedTime(before.Time, now).TotalSeconds;
                    cpu = seconds > 0.2 ? (t.Ticks - before.Ticks) * Darwin.TicksToNanoseconds / 1e9 / seconds * 100 : 0;
                }

                double? gpuPercent = null;
                if (gpu.Count > 0)
                {
                    gpuPercent = 0;
                    if (_appGpu.TryGetValue(app.Pid, out var gpuBefore) && t.Gpu >= gpuBefore.Gpu)
                    {
                        double seconds = Stopwatch.GetElapsedTime(gpuBefore.Time, now).TotalSeconds;
                        gpuPercent = seconds > 0.2 ? Math.Clamp((t.Gpu - gpuBefore.Gpu) / 1e9 / seconds * 100, 0, 100) : 0;
                    }

                    _appGpu[app.Pid] = (t.Gpu, now);
                }

                _appCpu[app.Pid] = (t.Ticks, now);
                result.Add(new RunningAppInfo(app.Pid, app.Name, app.BundleId, app.Path, Math.Max(0, cpu), t.Memory, gpuPercent, app.Regular));
            }
        }

        PruneNames();
        return result;
    }

    /// <summary>
    /// Each process's GPU time so far (nanoseconds), from the graphics driver's per-app records in the IORegistry
    /// (Apple silicon's AGXDeviceUserClient: IOUserClientCreator "pid 123, Name" and AppUsage's accumulatedGPUTime).
    /// Empty where the driver doesn't keep them.
    /// </summary>
    private static Dictionary<int, ulong> GpuTimes()
    {
        var result = new Dictionary<int, ulong>();
        try
        {
            foreach (var (_, properties) in IOKit.Services("AGXDeviceUserClient"))
            {
                if (CF.Text(properties, "IOUserClientCreator") is not { } creator || !creator.StartsWith("pid ", StringComparison.Ordinal)
                    || !int.TryParse(creator.AsSpan(4, Math.Max(0, (creator.IndexOf(',') < 0 ? creator.Length : creator.IndexOf(',')) - 4)), NumberStyles.Integer, CultureInfo.InvariantCulture, out int pid)
                    || properties.GetValueOrDefault("AppUsage") is not List<object?> usage)
                {
                    continue;
                }

                ulong total = 0;
                foreach (var entry in usage.OfType<Dictionary<string, object?>>())
                {
                    if (CF.Number(entry, "accumulatedGPUTime") is { } time && time > 0)
                    {
                        total += (ulong)time;
                    }
                }

                result[pid] = result.GetValueOrDefault(pid) + total;
            }
        }
        catch (Exception e) when (e is InvalidOperationException or InvalidCastException or EntryPointNotFoundException)
        {
            Log.WarnThrottled("mac-gpu-apps", TimeSpan.FromMinutes(30), LogCategory, "Reading the apps' GPU time failed.", e);
        }

        return result;
    }

    public async Task<bool> QuitAsync(RunningAppInfo app, CancellationToken cancellationToken)
    {
        bool asked = Dispatcher.UIThread.CheckAccess() ? Terminate(app.ProcessId) : await Dispatcher.UIThread.InvokeAsync(() => Terminate(app.ProcessId));
        if (!asked)
        {
            return false;
        }

        // An app with unsaved work shows its own dialog and stays: that's its choice, and nothing more is done.
        for (int i = 0; i < 40; i++)
        {
            await Task.Delay(200, cancellationToken);
            if (!IsRunning(app.ProcessId))
            {
                Log.Info(LogCategory, $"Quit {app.Name} for gaming.");
                return true;
            }
        }

        Log.Info(LogCategory, $"{app.Name} didn't quit (it may be asking to save).");
        return false;
    }

    private static bool Terminate(int pid)
    {
        nint pool = ObjC.objc_autoreleasePoolPush();
        try
        {
            nint app = ObjC.Send(ObjC.Class("NSRunningApplication"), ObjC.Sel("runningApplicationWithProcessIdentifier:"), pid);
            return app != 0 && ObjC.SendBool(app, ObjC.Sel("terminate"));
        }
        finally
        {
            ObjC.objc_autoreleasePoolPop(pool);
        }
    }

    private static bool IsRunning(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    public bool Reopen(string bundlePath)
    {
        if (!Directory.Exists(bundlePath) || !bundlePath.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // In the background and hidden: the game (or whatever is in front) stays in front.
        bool started = Commands.Start("/usr/bin/open", "-g", "-j", bundlePath);
        Log.Info(LogCategory, started ? $"Reopened {Path.GetFileNameWithoutExtension(bundlePath)}." : $"Reopening {bundlePath} failed.");
        return started;
    }
}
