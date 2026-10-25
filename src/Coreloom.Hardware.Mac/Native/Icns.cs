using System.Buffers.Binary;

namespace Coreloom.Hardware.Mac.Native;

/// <summary>
/// Reads the pictures in a macOS icon file (.icns): a list of (type, length, data) entries, where the modern sizes are
/// stored as PNG. Coreloom shows an app's icon from them (the interface can't decode .icns itself).
/// </summary>
public static class Icns
{
    /// <summary>Pixel size of each PNG-capable entry type.</summary>
    private static readonly Dictionary<string, int> Sizes = new(StringComparer.Ordinal)
    {
        ["icp4"] = 16,
        ["icp5"] = 32,
        ["icp6"] = 64,
        ["ic07"] = 128,
        ["ic08"] = 256,
        ["ic09"] = 512,
        ["ic10"] = 1024,
        ["ic11"] = 32,
        ["ic12"] = 64,
        ["ic13"] = 256,
        ["ic14"] = 512,
    };

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// The PNG closest to <paramref name="preferred"/> pixels (the smallest at least that big, else the largest), or null
    /// when the file has no PNG pictures.
    /// </summary>
    public static byte[]? BestPng(ReadOnlySpan<byte> icns, int preferred = 256)
    {
        if (icns.Length < 8 || !icns[..4].SequenceEqual("icns"u8))
        {
            return null;
        }

        int total = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(icns[4..8]), (uint)icns.Length);
        (int Size, int Offset, int Length)? best = null;
        int offset = 8;
        while (offset + 8 <= total)
        {
            string type = System.Text.Encoding.ASCII.GetString(icns.Slice(offset, 4));
            int length = (int)BinaryPrimitives.ReadUInt32BigEndian(icns.Slice(offset + 4, 4));
            if (length < 8 || offset + length > total)
            {
                break;
            }

            var data = icns.Slice(offset + 8, length - 8);
            if (Sizes.TryGetValue(type, out int size) && data.Length > 8 && data[..8].SequenceEqual(PngSignature))
            {
                bool better = best is not { } b
                    || (b.Size < preferred && size > b.Size)
                    || (size >= preferred && size < b.Size);
                if (better)
                {
                    best = (size, offset + 8, length - 8);
                }
            }

            offset += length;
        }

        return best is { } found ? icns.Slice(found.Offset, found.Length).ToArray() : null;
    }
}
