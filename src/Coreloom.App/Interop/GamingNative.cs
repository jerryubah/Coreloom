using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Coreloom.App.Interop;

/// <summary>What the graphics kernel says about hardware-accelerated GPU scheduling (as Windows' Settings app reads it).</summary>
internal readonly record struct GpuSchedulingCaps(bool Supported, bool Enabled, bool EnabledByDefault);

/// <summary>Windows calls the Gaming page needs: GPU scheduling support, running programs' files.</summary>
internal static unsafe partial class GamingNative
{
    private const int WddmCaps27 = 70; // KMTQAITYPE_WDDM_2_7_CAPS
    private const uint ProcessQueryLimitedInformation = 0x1000;

    [StructLayout(LayoutKind.Sequential)]
    private struct AdapterInfo
    {
        public uint Adapter;
        public uint LuidLow;
        public int LuidHigh;
        public uint Sources;
        public int PrecisePresentRegionsPreferred;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EnumAdapters2
    {
        public uint Count;
        public AdapterInfo* Adapters;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct QueryAdapterInfo
    {
        public uint Adapter;
        public int Type;
        public void* Data;
        public uint DataSize;
    }

    /// <summary>
    /// GPU scheduling capabilities of the graphics adapters (the first that supports it wins), or null when Windows is
    /// too old to report them (before version 2004).
    /// </summary>
    public static GpuSchedulingCaps? GpuScheduling()
    {
        try
        {
            var query = new EnumAdapters2();
            if (D3DKMTEnumAdapters2(&query) != 0 || query.Count == 0)
            {
                return null;
            }

            var adapters = new AdapterInfo[query.Count];
            fixed (AdapterInfo* p = adapters)
            {
                query.Adapters = p;
                if (D3DKMTEnumAdapters2(&query) != 0)
                {
                    return null;
                }
            }

            GpuSchedulingCaps? best = null;
            foreach (var adapter in adapters.AsSpan(0, (int)Math.Min(query.Count, (uint)adapters.Length)))
            {
                uint caps = 0;
                var info = new QueryAdapterInfo { Adapter = adapter.Adapter, Type = WddmCaps27, Data = &caps, DataSize = sizeof(uint) };
                if (D3DKMTQueryAdapterInfo(&info) == 0)
                {
                    var c = new GpuSchedulingCaps((caps & 1) != 0, (caps & 2) != 0, (caps & 4) != 0);
                    if (c.Supported && best is not { Supported: true })
                    {
                        best = c;
                    }
                    else
                    {
                        best ??= c;
                    }
                }

                uint handle = adapter.Adapter;
                D3DKMTCloseAdapter(&handle);
            }

            return best;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    /// <summary>The program file of a process, or null when Windows won't say.</summary>
    public static string? ProgramPath(int processId)
    {
        IntPtr handle = OpenProcess(ProcessQueryLimitedInformation, 0, (uint)processId);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            char* buffer = stackalloc char[1024];
            uint size = 1024;
            return QueryFullProcessImageName(handle, 0, buffer, &size) != 0 && size > 0 ? new string(buffer, 0, (int)size) : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    /// <summary>The programs running now (id and file), without the ones Windows protects.</summary>
    public static List<(int Id, string Path)> RunningPrograms()
    {
        var list = new List<(int, string)>();
        int own = Environment.ProcessId;
        char* buffer = stackalloc char[1024];
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                int id = process.Id;
                if (id is 0 or 4 || id == own)
                {
                    continue;
                }

                IntPtr handle = OpenProcess(ProcessQueryLimitedInformation, 0, (uint)id);
                if (handle == IntPtr.Zero)
                {
                    continue;
                }

                try
                {
                    uint size = 1024;
                    if (QueryFullProcessImageName(handle, 0, buffer, &size) != 0 && size > 0)
                    {
                        list.Add((id, new string(buffer, 0, (int)size)));
                    }
                }
                finally
                {
                    CloseHandle(handle);
                }
            }
        }

        return list;
    }

    [LibraryImport("gdi32.dll")]
    private static partial int D3DKMTEnumAdapters2(EnumAdapters2* query);

    [LibraryImport("gdi32.dll")]
    private static partial int D3DKMTQueryAdapterInfo(QueryAdapterInfo* info);

    [LibraryImport("gdi32.dll")]
    private static partial int D3DKMTCloseAdapter(uint* adapter);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr OpenProcess(uint access, int inherit, uint processId);

    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW")]
    private static partial int QueryFullProcessImageName(IntPtr process, uint flags, char* name, uint* size);

    [LibraryImport("kernel32.dll")]
    private static partial int CloseHandle(IntPtr handle);
}

/// <summary>
/// Starts programs and links as the signed-in user rather than as administrator (Coreloom runs elevated; games and their
/// launchers shouldn't). Asks the desktop's own shell to run it, the way Windows recommends (IShellDispatch2.ShellExecute
/// on the desktop window); falls back to Explorer, then to a normal start.
/// </summary>
internal static partial class ShellLaunch
{
    private static readonly Guid ShellWindowsClsid = new("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
    private static readonly Guid TopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");

    public static bool Start(string file, string? arguments = null, string? directory = null)
    {
        try
        {
            if (ViaDesktop(file, arguments, directory))
            {
                return true;
            }
        }
        catch (Exception e) when (e is COMException or InvalidCastException or InvalidOperationException or NotSupportedException or MissingMemberException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            Coreloom.Core.Diagnostics.Log.Debug("Gaming", $"The desktop shell couldn't start {file}: {e.Message}");
        }

        try
        {
            if (string.IsNullOrEmpty(arguments))
            {
                // Explorer hands the request to the running (non-elevated) desktop and exits.
                using var explorer = Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), $"\"{file}\"") { UseShellExecute = false });
                return explorer is not null;
            }

            using var process = Process.Start(new ProcessStartInfo(file, arguments) { UseShellExecute = true, WorkingDirectory = directory ?? string.Empty });
            return process is not null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            Coreloom.Core.Diagnostics.Log.Warn("Gaming", $"Couldn't start {file}.", e);
            return false;
        }
    }

    private static bool ViaDesktop(string file, string? arguments, string? directory)
    {
        var type = Type.GetTypeFromCLSID(ShellWindowsClsid, throwOnError: false);
        if (type is null)
        {
            return false;
        }

        object? windows = Activator.CreateInstance(type);
        if (windows is null)
        {
            return false;
        }

        try
        {
            dynamic shellWindows = windows;
            object location = 0; // CSIDL_DESKTOP
            object root = new();
            object? desktop = shellWindows.FindWindowSW(ref location, ref root, 8 /* SWC_DESKTOP */, out int _, 1 /* SWFO_NEEDDISPATCH */);
            if (desktop is not IServiceProvider provider)
            {
                return false;
            }

            Guid service = TopLevelBrowser;
            Guid browserId = typeof(IShellBrowser).GUID;
            if (provider.QueryService(ref service, ref browserId, out object browserObject) != 0 || browserObject is not IShellBrowser browser)
            {
                return false;
            }

            if (browser.QueryActiveShellView(out var view) != 0 || view is null)
            {
                return false;
            }

            Guid dispatchId = new("00020400-0000-0000-C000-000000000046"); // IDispatch
            if (view.GetItemObject(0 /* SVGIO_BACKGROUND */, ref dispatchId, out object folderView) != 0)
            {
                return false;
            }

            dynamic shell = ((dynamic)folderView).Application;
            shell.ShellExecute(file, arguments ?? string.Empty, directory ?? string.Empty, "open", 1);
            return true;
        }
        finally
        {
            Marshal.FinalReleaseComObject(windows);
        }
    }

    [ComImport]
    [Guid("6D5140C1-7436-11CE-8034-00AA006009FA")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IServiceProvider
    {
        [PreserveSig]
        int QueryService(ref Guid guidService, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppvObject);
    }

    [ComImport]
    [Guid("000214E2-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellBrowser
    {
        [PreserveSig] int GetWindow(out IntPtr phwnd);
        [PreserveSig] int ContextSensitiveHelp(int fEnterMode);
        [PreserveSig] int InsertMenusSB(IntPtr hmenuShared, IntPtr lpMenuWidths);
        [PreserveSig] int SetMenuSB(IntPtr hmenuShared, IntPtr holemenuRes, IntPtr hwndActiveObject);
        [PreserveSig] int RemoveMenusSB(IntPtr hmenuShared);
        [PreserveSig] int SetStatusTextSB(IntPtr pszStatusText);
        [PreserveSig] int EnableModelessSB(int fEnable);
        [PreserveSig] int TranslateAcceleratorSB(IntPtr pmsg, ushort wID);
        [PreserveSig] int BrowseObject(IntPtr pidl, uint wFlags);
        [PreserveSig] int GetViewStateStream(uint grfMode, out IntPtr ppStrm);
        [PreserveSig] int GetControlWindow(uint id, out IntPtr phwnd);
        [PreserveSig] int SendControlMsg(uint id, uint uMsg, IntPtr wParam, IntPtr lParam, out IntPtr pret);
        [PreserveSig] int QueryActiveShellView(out IShellView? ppshv);
    }

    [ComImport]
    [Guid("000214E3-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellView
    {
        [PreserveSig] int GetWindow(out IntPtr phwnd);
        [PreserveSig] int ContextSensitiveHelp(int fEnterMode);
        [PreserveSig] int TranslateAccelerator(IntPtr pmsg);
        [PreserveSig] int EnableModeless(int fEnable);
        [PreserveSig] int UIActivate(uint uState);
        [PreserveSig] int Refresh();
        [PreserveSig] int CreateViewWindow(IntPtr psvPrevious, IntPtr pfs, IntPtr psb, IntPtr prcView, out IntPtr phWnd);
        [PreserveSig] int DestroyViewWindow();
        [PreserveSig] int GetCurrentInfo(IntPtr pfs);
        [PreserveSig] int AddPropertySheetPages(uint dwReserved, IntPtr pfn, IntPtr lparam);
        [PreserveSig] int SaveViewState();
        [PreserveSig] int SelectItem(IntPtr pidlItem, uint uFlags);
        [PreserveSig] int GetItemObject(uint uItem, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
    }
}
