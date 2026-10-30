using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Coreloom.Core.Gaming;
using Coreloom.Hardware.Gpu;
using Coreloom.Hardware.Native;
using Microsoft.Win32.SafeHandles;

namespace Coreloom.Hardware.Gaming;

/// <summary>
/// What the DirectStorage check reads from Windows (since 1.8.4): how a drive is connected (from the volume's disk), whether
/// Windows 11's BypassIO storage path works on it (<c>FSCTL_MANAGE_BYPASS_IO</c>, the query <c>fsutil bypassio state</c> makes),
/// and the graphics card's Direct3D 12 shader model. Everything is read-only.
/// </summary>
[SupportedOSPlatform("windows")]
public static unsafe class GameStorageProbe
{
    /// <summary>FSCTL_MANAGE_BYPASS_IO: CTL_CODE(FILE_DEVICE_FILE_SYSTEM, 0x112, METHOD_BUFFERED, FILE_ANY_ACCESS).</summary>
    internal const uint FsctlManageBypassIo = 0x00090448;

    /// <summary>FS_BPIO_OP_QUERY: whether BypassIO can be used here.</summary>
    internal const int OperationQuery = 3;

    // FS_BPIO_OUTPUT: Operation (4), OutFlags (4), Reserved1 (8), Reserved2 (8), then FS_BPIO_RESULTS:
    // OpStatus (4), FailingDriverNameLen (2), FailingDriverName[32], FailureReasonLen (2), FailureReason[128].
    internal const int OutputSize = 24 + 4 + 2 + 64 + 2 + 256;
    private const int OpStatusOffset = 24;
    private const int DriverNameOffset = 30;
    private const int ReasonOffset = 96;

    private const uint FileReadAttributes = 0x80;
    private const uint FileShareAll = 0x7;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;

    /// <summary>"NVMe", "SATA"… and solid state or not, for the disk a drive ("D:\") is on; ("Unknown", null) when unreadable.</summary>
    public static (string Bus, bool? IsSsd) DriveKind(string root)
    {
        try
        {
            if (StorageInterop.TryGetVolumeDiskNumber(root) is int number && StorageInterop.TryQueryDisk(number) is { } disk)
            {
                return (disk.BusType, disk.IsSsd);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ExternalException)
        {
        }

        return ("Unknown", null);
    }

    /// <summary>
    /// Whether BypassIO works on a drive ("D:\"): supported, or not with the driver in the way and Windows' reason. Null
    /// when Windows can't be asked (older Windows, a drive that can't be opened).
    /// </summary>
    public static BypassIoState? BypassIo(string root)
    {
        using SafeFileHandle handle = Win32.CreateFile(root, FileReadAttributes, FileShareAll, IntPtr.Zero, OpenExisting, FileFlagBackupSemantics, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            return null;
        }

        byte* input = stackalloc byte[24];
        new Span<byte>(input, 24).Clear();
        *(int*)input = OperationQuery;
        byte* output = stackalloc byte[OutputSize];
        new Span<byte>(output, OutputSize).Clear();
        if (!Win32.DeviceIoControl(handle, FsctlManageBypassIo, input, 24, output, OutputSize, out uint returned, IntPtr.Zero) || returned < OpStatusOffset + 4)
        {
            return null; // ERROR_INVALID_FUNCTION on Windows without BypassIO, or a file system that doesn't answer
        }

        return ParseQuery(new ReadOnlySpan<byte>(output, (int)Math.Min(returned, (uint)OutputSize)));
    }

    /// <summary>Reads an FS_BPIO_OUTPUT for the query: OpStatus 0 (STATUS_SUCCESS) is supported, else the driver and reason.</summary>
    internal static BypassIoState ParseQuery(ReadOnlySpan<byte> output)
    {
        int status = MemoryMarshal.Read<int>(output[OpStatusOffset..]);
        if (status == 0)
        {
            return new BypassIoState(true);
        }

        string? driver = ReadName(output, DriverNameOffset, 32);
        string? reason = ReadName(output, ReasonOffset, 128);
        return new BypassIoState(false, driver, reason);
    }

    /// <summary>The graphics card's Direct3D 12 shader model (the most dedicated memory first), or null without Direct3D 12.</summary>
    public static (string Name, int? ShaderModel)? GraphicsCard()
    {
        try
        {
            return Direct3D.HighestShaderModel() is { } found ? (found.Adapter.Name, found.ShaderModel) : null;
        }
        catch (Exception e) when (e is InvalidOperationException or ExternalException or DllNotFoundException)
        {
            return null;
        }
    }

    private static string? ReadName(ReadOnlySpan<byte> output, int offset, int chars)
    {
        if (output.Length < offset + 2)
        {
            return null;
        }

        int length = Math.Min(chars, (output.Length - offset) / 2) * 2;
        string text = Encoding.Unicode.GetString(output.Slice(offset, length));
        int end = text.IndexOf('\0');
        text = (end >= 0 ? text[..end] : text).Trim();
        return text.Length == 0 ? null : text;
    }
}
