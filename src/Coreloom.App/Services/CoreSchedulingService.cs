using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Coreloom.App.Overlay;
using Coreloom.Core.Diagnostics;
using Coreloom.Core.Gaming;
using Coreloom.ViewModels;
using Coreloom.ViewModels.Pages;
using Coreloom.ViewModels.Services;
using Microsoft.Win32;

namespace Coreloom.App.Services;

/// <summary>
/// Windows' side of intelligent CPU core scheduling: the core layout from <c>GetLogicalProcessorInformationEx</c>, AMD's
/// 3D V-Cache driver and service from the service manager and its preferences from the registry (both only read), and
/// a game's cores through <c>Get/SetProcessAffinityMask</c> — the same change as Task Manager's "Set affinity", made
/// from outside the game; nothing is added to it.
/// </summary>
internal sealed unsafe partial class WindowsCoreSchedulingService : ICoreSchedulingService
{
    private const string LogCategory = "Gaming";
    private const int RelationAll = 0xFFFF;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint ProcessSetInformation = 0x0200;
    private const uint ScManagerConnect = 0x0001;
    private const uint ServiceQueryStatus = 0x0004;
    private const uint ServiceRunning = 4;

    public bool IsSupported => true;

    public CoreLayout ReadLayout()
    {
        uint length = 0;
        GetLogicalProcessorInformationEx(RelationAll, null, ref length);
        if (length == 0 || length > 16 * 1024 * 1024)
        {
            return CoreLayout.Unknown;
        }

        var buffer = new byte[length];
        fixed (byte* p = buffer)
        {
            if (!GetLogicalProcessorInformationEx(RelationAll, p, ref length))
            {
                return CoreLayout.Unknown;
            }
        }

        var layout = CoreLayout.Parse(buffer.AsSpan(0, (int)Math.Min(length, (uint)buffer.Length)));
        Log.Info(LogCategory, $"Core layout: {layout.Summary} ({string.Join("; ", layout.Groups.Select(g => $"{g.Name}: {g.Description}, processors {string.Join(",", g.Processors)}"))}).");
        return layout;
    }

    public AmdVCacheStatus ReadAmdStatus()
    {
        var driver = StateOf(AmdVCacheStatus.DriverName);
        var service = StateOf(AmdVCacheStatus.ServiceName);
        int? defaultType = null;
        var apps = new List<AmdAppPreference>();
        using (var preferences = Registry.LocalMachine.OpenSubKey(AmdVCacheStatus.PreferencesKey))
        {
            if (preferences?.GetValue("DefaultType") is int type)
            {
                defaultType = type;
            }
        }

        using (var appsKey = Registry.LocalMachine.OpenSubKey(AmdVCacheStatus.AppsKey))
        {
            foreach (string name in appsKey?.GetSubKeyNames() ?? [])
            {
                using var app = appsKey!.OpenSubKey(name);
                if (app?.GetValue("EndsWith") is string ends && !string.IsNullOrWhiteSpace(ends) && AmdVCacheStatus.Kind(app.GetValue("Type") as int?) is { } kind)
                {
                    apps.Add(new AmdAppPreference(name, ends.Trim(), kind));
                }
            }
        }

        return new AmdVCacheStatus(driver, service, defaultType, apps);
    }

    public ProcessAffinity? GetAffinity(int processId)
    {
        IntPtr process = OpenProcess(ProcessQueryLimitedInformation, false, (uint)processId);
        if (process == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return GetProcessAffinityMask(process, out nuint mask, out nuint system) ? new ProcessAffinity(mask, system) : null;
        }
        finally
        {
            CloseHandle(process);
        }
    }

    public bool SetAffinity(int processId, ulong mask)
    {
        if (mask == 0)
        {
            return false;
        }

        IntPtr process = OpenProcess(ProcessSetInformation | ProcessQueryLimitedInformation, false, (uint)processId);
        if (process == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            return SetProcessAffinityMask(process, (nuint)mask);
        }
        finally
        {
            CloseHandle(process);
        }
    }

    private static ServiceState StateOf(string name)
    {
        IntPtr manager = OpenSCManager(null, null, ScManagerConnect);
        if (manager == IntPtr.Zero)
        {
            return ServiceState.Missing;
        }

        try
        {
            IntPtr service = OpenService(manager, name, ServiceQueryStatus);
            if (service == IntPtr.Zero)
            {
                return ServiceState.Missing;
            }

            try
            {
                return QueryServiceStatus(service, out var status) && status.CurrentState == ServiceRunning ? ServiceState.Running : ServiceState.Stopped;
            }
            finally
            {
                CloseServiceHandle(service);
            }
        }
        finally
        {
            CloseServiceHandle(manager);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus
    {
        public uint ServiceType;
        public uint CurrentState;
        public uint ControlsAccepted;
        public uint Win32ExitCode;
        public uint ServiceSpecificExitCode;
        public uint CheckPoint;
        public uint WaitHint;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetLogicalProcessorInformationEx(int relationship, byte* buffer, ref uint length);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetProcessAffinityMask(IntPtr process, out nuint processMask, out nuint systemMask);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetProcessAffinityMask(IntPtr process, nuint mask);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr handle);

    [LibraryImport("advapi32.dll", EntryPoint = "OpenSCManagerW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr OpenSCManager(string? machine, string? database, uint access);

    [LibraryImport("advapi32.dll", EntryPoint = "OpenServiceW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr OpenService(IntPtr manager, string name, uint access);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryServiceStatus(IntPtr service, out ServiceStatus status);

    [LibraryImport("advapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseServiceHandle(IntPtr handle);
}

/// <summary>
/// Feeds core scheduling once a second while the user has it on: the game in front (from gaming mode), the load on each
/// logical processor (from the sensor frame) and, only during a test the user started, the game's newest second of
/// frames (Windows' graphics events, as for the overlay). Nothing runs while it's off. Closing Coreloom gives the games
/// it changed Windows' choice of cores back.
/// </summary>
internal sealed class CoreSchedulingTicker : IDisposable
{
    private const string Owner = "core scheduling";
    private const string LogCategory = "Gaming";

    private readonly ShellViewModel _shell;
    private readonly GamingModeService _gamingMode;
    private readonly EtwFrameRateSource? _frames;
    private readonly Func<bool> _isOn;
    private readonly Func<IReadOnlyList<double>?> _threadLoads;
    private readonly DispatcherTimer _timer;
    private readonly double[] _buffer = new double[2048];
    private bool _acquired;
    private bool _wasOn;

    public CoreSchedulingTicker(ShellViewModel shell, GamingModeService gamingMode, EtwFrameRateSource? frames, Func<bool> isOn, Func<IReadOnlyList<double>?> threadLoads, Dispatcher dispatcher)
    {
        _shell = shell;
        _gamingMode = gamingMode;
        _frames = frames;
        _isOn = isOn;
        _threadLoads = threadLoads;
        _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Tick();
    }

    public void Start() => _timer.Start();

    public void Dispose()
    {
        _timer.Stop();
        Release();
        try
        {
            _shell.Gaming.CoreScheduling.RestoreAll();
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Log.Warn(LogCategory, "Giving games Windows' choice of cores back failed.", e);
        }
    }

    private void Tick()
    {
        try
        {
            var scheduling = _shell.Gaming.CoreScheduling;
            if (!_isOn())
            {
                Release();
                if (_wasOn)
                {
                    _wasOn = false;
                    scheduling.Tick(default); // gives back the cores it changed
                }

                return;
            }

            _wasOn = true;
            int? pid = _gamingMode.GameProcessId;
            PacingSecond? second = null;
            string? problem = null;
            if (scheduling.WantsFrames && pid is int id && _frames is not null)
            {
                if (!_acquired)
                {
                    _acquired = true;
                    _frames.Acquire(Owner);
                }

                problem = _frames.IsRunning ? null : _frames.Problem ?? "Windows' graphics events aren't available.";
                if (problem is null)
                {
                    int n = _frames.RecentFrameTimes(id, _buffer, out double age);
                    if (n >= 2 && age < 1500)
                    {
                        second = PacingSecond.From(_buffer.AsSpan(0, n));
                    }
                }
            }
            else if (!scheduling.WantsFrames)
            {
                Release();
            }

            scheduling.Tick(new CoreTick(pid, _gamingMode.GameProgram, _threadLoads(), second, problem));
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception or IOException)
        {
            Log.WarnThrottled("core-scheduling", TimeSpan.FromMinutes(10), LogCategory, "Core scheduling couldn't read the game in front.", e);
        }
    }

    private void Release()
    {
        if (_acquired)
        {
            _acquired = false;
            _frames?.Release(Owner);
        }
    }
}
