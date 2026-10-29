using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace Coreloom.PageShots;

/// <summary>
/// Reads the real screen (with layered windows, so the in-game overlay shows) into PNGs, and the pixels along a
/// window's edge, to check what Windows draws around the overlay (1.8.3).
/// </summary>
internal static partial class ScreenCapture
{
    private const uint SrcCopy = 0x00CC0020;
    private const uint CaptureBlt = 0x40000000;

    public static void Save(Int32Rect area, string file)
    {
        IntPtr screen = GetDC(IntPtr.Zero);
        IntPtr memory = CreateCompatibleDC(screen);
        IntPtr bitmap = CreateCompatibleBitmap(screen, area.Width, area.Height);
        IntPtr old = SelectObject(memory, bitmap);
        try
        {
            BitBlt(memory, 0, 0, area.Width, area.Height, screen, area.X, area.Y, SrcCopy | CaptureBlt);
            SelectObject(memory, old);
            var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var stream = File.Create(file);
            encoder.Save(stream);
        }
        finally
        {
            DeleteObject(bitmap);
            DeleteDC(memory);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    /// <summary>The window's rectangle and the pixels on a line across its left edge and its top edge (outside to inside).</summary>
    public static string DescribeEdge(IntPtr hwnd)
    {
        GetWindowRect(hwnd, out var rect);
        var text = new StringBuilder();
        text.AppendLine($"  window {rect.Left},{rect.Top} – {rect.Right},{rect.Bottom}");
        IntPtr screen = GetDC(IntPtr.Zero);
        try
        {
            int midY = (rect.Top + rect.Bottom) / 2;
            int midX = (rect.Left + rect.Right) / 2;
            text.Append("  left edge, x from ").Append(rect.Left - 3).Append(':');
            for (int x = rect.Left - 3; x <= rect.Left + 12; x++)
            {
                text.Append(' ').Append(Hex(GetPixel(screen, x, midY)));
            }

            text.AppendLine();
            text.Append("  top edge, y from ").Append(rect.Top - 3).Append(':');
            for (int y = rect.Top - 3; y <= rect.Top + 12; y++)
            {
                text.Append(' ').Append(Hex(GetPixel(screen, midX, y)));
            }

            text.AppendLine();
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screen);
        }

        return text.ToString();

        static string Hex(uint colorRef) => $"{colorRef & 0xFF:X2}{(colorRef >> 8) & 0xFF:X2}{(colorRef >> 16) & 0xFF:X2}";
    }

    /// <summary>What the theme did to every window before 1.8.3: round corners (and with them, Windows 11's border).</summary>
    public static void AskForRoundCorners(IntPtr hwnd)
    {
        int round = 2; // DWMWCP_ROUND
        DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));
        int color = unchecked((int)0xFFFFFFFF); // DWMWA_COLOR_DEFAULT
        DwmSetWindowAttribute(hwnd, 34, ref color, sizeof(int));
        int policy = 0; // DWMNCRP_USEWINDOWSTYLE
        DwmSetWindowAttribute(hwnd, 2, ref policy, sizeof(int));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetDC(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    private static partial int ReleaseDC(IntPtr hwnd, IntPtr dc);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(IntPtr hwnd, out Rect rect);

    [LibraryImport("gdi32.dll")]
    private static partial IntPtr CreateCompatibleDC(IntPtr dc);

    [LibraryImport("gdi32.dll")]
    private static partial IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);

    [LibraryImport("gdi32.dll")]
    private static partial IntPtr SelectObject(IntPtr dc, IntPtr gdiObject);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BitBlt(IntPtr destination, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, uint operation);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(IntPtr gdiObject);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(IntPtr dc);

    [LibraryImport("gdi32.dll")]
    private static partial uint GetPixel(IntPtr dc, int x, int y);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
