using System.Text;

namespace Coreloom.Core.Connect;

/// <summary>QR error correction: how much of the code can be damaged and still read (about 7, 15, 25 and 30 %).</summary>
public enum QrErrorCorrection
{
    Low,
    Medium,
    Quartile,
    High,
}

/// <summary>
/// A QR Code (ISO/IEC 18004) for a short text such as Coreloom Connect's pairing link: byte mode, versions 1–40,
/// the smallest version that fits, the strongest error correction that fits in it, and the mask with the lowest
/// penalty. The same construction as Project Nayuki's reference generator (MIT).
/// </summary>
public sealed class QrCode
{
    private static readonly sbyte[,] EccCodewordsPerBlock =
    {
        // Version: (0 unused) 1 … 40
        { -1, 7, 10, 15, 20, 26, 18, 20, 24, 30, 18, 20, 24, 26, 30, 22, 24, 28, 30, 28, 28, 28, 28, 30, 30, 26, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30 }, // Low
        { -1, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26, 30, 22, 22, 24, 24, 28, 28, 26, 26, 26, 26, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28 }, // Medium
        { -1, 13, 22, 18, 26, 18, 24, 18, 22, 20, 24, 28, 26, 24, 20, 30, 24, 28, 28, 26, 30, 28, 30, 30, 30, 30, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30 }, // Quartile
        { -1, 17, 28, 22, 16, 22, 28, 26, 26, 24, 28, 24, 28, 22, 24, 24, 30, 28, 28, 26, 28, 30, 24, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30 }, // High
    };

    private static readonly sbyte[,] ErrorCorrectionBlocks =
    {
        { -1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 4, 4, 4, 4, 4, 6, 6, 6, 6, 7, 8, 8, 9, 9, 10, 12, 12, 12, 13, 14, 15, 16, 17, 18, 19, 19, 20, 21, 22, 24, 25 },
        { -1, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5, 5, 8, 9, 9, 10, 10, 11, 13, 14, 16, 17, 17, 18, 20, 21, 23, 25, 26, 28, 29, 31, 33, 35, 37, 38, 40, 43, 45, 47, 49 },
        { -1, 1, 1, 2, 2, 4, 4, 6, 6, 8, 8, 8, 10, 12, 16, 12, 17, 16, 18, 21, 20, 23, 23, 25, 27, 29, 34, 34, 35, 38, 40, 43, 45, 48, 51, 53, 56, 59, 62, 65, 68 },
        { -1, 1, 1, 2, 4, 4, 4, 5, 6, 8, 8, 11, 11, 16, 16, 18, 16, 19, 21, 25, 25, 25, 34, 30, 32, 35, 37, 40, 42, 45, 48, 51, 54, 57, 60, 63, 66, 70, 74, 77, 81 },
    };

    private readonly bool[,] _modules;
    private readonly bool[,] _isFunction;

    private QrCode(int version, QrErrorCorrection ecc, byte[] dataCodewords)
    {
        Version = version;
        ErrorCorrection = ecc;
        Size = (version * 4) + 17;
        _modules = new bool[Size, Size];
        _isFunction = new bool[Size, Size];

        DrawFunctionPatterns();
        DrawCodewords(AddErrorCorrectionAndInterleave(dataCodewords));

        int bestMask = 0;
        long bestPenalty = long.MaxValue;
        for (int mask = 0; mask < 8; mask++)
        {
            ApplyMask(mask);
            DrawFormatBits(mask);
            long penalty = PenaltyScore();
            if (penalty < bestPenalty)
            {
                bestMask = mask;
                bestPenalty = penalty;
            }

            ApplyMask(mask); // XOR undoes it
        }

        Mask = bestMask;
        ApplyMask(bestMask);
        DrawFormatBits(bestMask);
    }

    public int Version { get; }

    /// <summary>Modules per side, without the quiet zone (4 modules on each side when drawn).</summary>
    public int Size { get; }

    public QrErrorCorrection ErrorCorrection { get; }

    public int Mask { get; }

    /// <summary>True for a dark module. Out-of-range coordinates are light (the quiet zone).</summary>
    public bool this[int x, int y] => x >= 0 && y >= 0 && x < Size && y < Size && _modules[y, x];

    /// <summary>Encodes text (UTF-8, byte mode) at the given error correction or better.</summary>
    public static QrCode Encode(string text, QrErrorCorrection minimum = QrErrorCorrection.Medium)
    {
        ArgumentNullException.ThrowIfNull(text);
        byte[] data = Encoding.UTF8.GetBytes(text);

        int version = 1;
        int usedBits;
        while (true)
        {
            int capacityBits = DataCodewords(version, minimum) * 8;
            usedBits = 4 + CountBits(version) + (data.Length * 8);
            if (data.Length < (1 << CountBits(version)) && usedBits <= capacityBits)
            {
                break;
            }

            if (++version > 40)
            {
                throw new ArgumentException("The text is too long for a QR code.", nameof(text));
            }
        }

        var ecc = minimum;
        foreach (var stronger in new[] { QrErrorCorrection.Medium, QrErrorCorrection.Quartile, QrErrorCorrection.High })
        {
            if (stronger > ecc && usedBits <= DataCodewords(version, stronger) * 8)
            {
                ecc = stronger;
            }
        }

        // Mode (byte), length, data, terminator, padding.
        var bits = new BitBuffer();
        bits.Append(0b0100, 4);
        bits.Append(data.Length, CountBits(version));
        foreach (byte b in data)
        {
            bits.Append(b, 8);
        }

        int capacity = DataCodewords(version, ecc) * 8;
        bits.Append(0, Math.Min(4, capacity - bits.Count));
        bits.Append(0, (8 - (bits.Count % 8)) % 8);
        for (int pad = 0xEC; bits.Count < capacity; pad ^= 0xEC ^ 0x11)
        {
            bits.Append(pad, 8);
        }

        return new QrCode(version, ecc, bits.ToBytes());
    }

    /// <summary>The code as SVG path data ("M x y h1 v1 h-1z" per dark module, merged across rows), quiet zone included.</summary>
    public string ToSvgPath(int border = 4)
    {
        var sb = new StringBuilder();
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                if (!_modules[y, x])
                {
                    continue;
                }

                int start = x;
                while (x + 1 < Size && _modules[y, x + 1])
                {
                    x++;
                }

                sb.Append('M').Append(start + border).Append(' ').Append(y + border).Append('h').Append(x - start + 1).Append("v1h-").Append(x - start + 1).Append('z');
            }
        }

        return sb.ToString();
    }

    private static int CountBits(int version) => version <= 9 ? 8 : 16;

    private static int RawDataModules(int version)
    {
        int result = (((16 * version) + 128) * version) + 64;
        if (version >= 2)
        {
            int alignments = (version / 7) + 2;
            result -= (((25 * alignments) - 10) * alignments) - 55;
            if (version >= 7)
            {
                result -= 36;
            }
        }

        return result;
    }

    private static int DataCodewords(int version, QrErrorCorrection ecc) =>
        (RawDataModules(version) / 8) - (EccCodewordsPerBlock[(int)ecc, version] * ErrorCorrectionBlocks[(int)ecc, version]);

    // ---- Drawing --------------------------------------------------------------------------------------------------

    private void SetFunction(int x, int y, bool dark)
    {
        _modules[y, x] = dark;
        _isFunction[y, x] = true;
    }

    private void DrawFunctionPatterns()
    {
        for (int i = 0; i < Size; i++)
        {
            SetFunction(6, i, i % 2 == 0);
            SetFunction(i, 6, i % 2 == 0);
        }

        DrawFinder(3, 3);
        DrawFinder(Size - 4, 3);
        DrawFinder(3, Size - 4);

        int[] positions = AlignmentPositions();
        int last = positions.Length - 1;
        for (int i = 0; i < positions.Length; i++)
        {
            for (int j = 0; j < positions.Length; j++)
            {
                if ((i == 0 && j == 0) || (i == 0 && j == last) || (i == last && j == 0))
                {
                    continue; // the finders' corners
                }

                for (int dy = -2; dy <= 2; dy++)
                {
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        SetFunction(positions[i] + dx, positions[j] + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
                    }
                }
            }
        }

        DrawFormatBits(0); // reserves the area; drawn again with the chosen mask
        DrawVersion();
    }

    private void DrawFinder(int cx, int cy)
    {
        for (int dy = -4; dy <= 4; dy++)
        {
            for (int dx = -4; dx <= 4; dx++)
            {
                int x = cx + dx;
                int y = cy + dy;
                if (x >= 0 && x < Size && y >= 0 && y < Size)
                {
                    int distance = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    SetFunction(x, y, distance != 2 && distance != 4);
                }
            }
        }
    }

    private int[] AlignmentPositions()
    {
        if (Version == 1)
        {
            return [];
        }

        int count = (Version / 7) + 2;
        int step = Version == 32 ? 26 : (((Version * 4) + (count * 2) + 1) / ((count * 2) - 2)) * 2;
        var result = new int[count];
        result[0] = 6;
        for (int i = count - 1, position = Size - 7; i >= 1; i--, position -= step)
        {
            result[i] = position;
        }

        return result;
    }

    private void DrawFormatBits(int mask)
    {
        int formatBits = ErrorCorrection switch
        {
            QrErrorCorrection.Low => 1,
            QrErrorCorrection.Medium => 0,
            QrErrorCorrection.Quartile => 3,
            _ => 2,
        };
        int data = (formatBits << 3) | mask;
        int remainder = data;
        for (int i = 0; i < 10; i++)
        {
            remainder = (remainder << 1) ^ ((remainder >> 9) * 0x537);
        }

        int bits = ((data << 10) | remainder) ^ 0x5412;

        for (int i = 0; i <= 5; i++)
        {
            SetFunction(8, i, Bit(bits, i));
        }

        SetFunction(8, 7, Bit(bits, 6));
        SetFunction(8, 8, Bit(bits, 7));
        SetFunction(7, 8, Bit(bits, 8));
        for (int i = 9; i < 15; i++)
        {
            SetFunction(14 - i, 8, Bit(bits, i));
        }

        for (int i = 0; i < 8; i++)
        {
            SetFunction(Size - 1 - i, 8, Bit(bits, i));
        }

        for (int i = 8; i < 15; i++)
        {
            SetFunction(8, Size - 15 + i, Bit(bits, i));
        }

        SetFunction(8, Size - 8, true); // the dark module
    }

    private void DrawVersion()
    {
        if (Version < 7)
        {
            return;
        }

        int remainder = Version;
        for (int i = 0; i < 12; i++)
        {
            remainder = (remainder << 1) ^ ((remainder >> 11) * 0x1F25);
        }

        int bits = (Version << 12) | remainder;
        for (int i = 0; i < 18; i++)
        {
            bool bit = Bit(bits, i);
            int a = Size - 11 + (i % 3);
            int b = i / 3;
            SetFunction(a, b, bit);
            SetFunction(b, a, bit);
        }
    }

    private byte[] AddErrorCorrectionAndInterleave(byte[] data)
    {
        int ecc = (int)ErrorCorrection;
        int blocks = ErrorCorrectionBlocks[ecc, Version];
        int blockEcc = EccCodewordsPerBlock[ecc, Version];
        int raw = RawDataModules(Version) / 8;
        int shortBlocks = blocks - (raw % blocks);
        int shortBlockLength = raw / blocks;

        byte[] divisor = ReedSolomonDivisor(blockEcc);
        var all = new List<byte[]>(blocks);
        for (int i = 0, k = 0; i < blocks; i++)
        {
            int length = shortBlockLength - blockEcc + (i < shortBlocks ? 0 : 1);
            byte[] block = data[k..(k + length)];
            k += length;
            byte[] remainder = ReedSolomonRemainder(block, divisor);
            var full = new byte[shortBlockLength + 1];
            if (i < shortBlocks)
            {
                block.CopyTo(full, 0); // a placeholder byte after the data, skipped when interleaving
                remainder.CopyTo(full, length + 1);
            }
            else
            {
                block.CopyTo(full, 0);
                remainder.CopyTo(full, length);
            }

            all.Add(full);
        }

        var result = new byte[raw];
        int n = 0;
        for (int i = 0; i < all[0].Length; i++)
        {
            for (int j = 0; j < all.Count; j++)
            {
                if (i != shortBlockLength - blockEcc || j >= shortBlocks)
                {
                    result[n++] = all[j][i];
                }
            }
        }

        return result;
    }

    private void DrawCodewords(byte[] data)
    {
        int i = 0;
        for (int right = Size - 1; right >= 1; right -= 2)
        {
            if (right == 6)
            {
                right = 5; // the vertical timing pattern
            }

            for (int vertical = 0; vertical < Size; vertical++)
            {
                for (int j = 0; j < 2; j++)
                {
                    int x = right - j;
                    bool upward = ((right + 1) & 2) == 0;
                    int y = upward ? Size - 1 - vertical : vertical;
                    if (!_isFunction[y, x] && i < data.Length * 8)
                    {
                        _modules[y, x] = Bit(data[i >> 3], 7 - (i & 7));
                        i++;
                    }
                }
            }
        }
    }

    private void ApplyMask(int mask)
    {
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                bool invert = mask switch
                {
                    0 => (x + y) % 2 == 0,
                    1 => y % 2 == 0,
                    2 => x % 3 == 0,
                    3 => (x + y) % 3 == 0,
                    4 => ((x / 3) + (y / 2)) % 2 == 0,
                    5 => ((x * y) % 2) + ((x * y) % 3) == 0,
                    6 => (((x * y) % 2) + ((x * y) % 3)) % 2 == 0,
                    _ => (((x + y) % 2) + ((x * y) % 3)) % 2 == 0,
                };
                if (invert && !_isFunction[y, x])
                {
                    _modules[y, x] = !_modules[y, x];
                }
            }
        }
    }

    // ---- Mask penalty (ISO/IEC 18004 §7.8.3) ------------------------------------------------------------------------

    private long PenaltyScore()
    {
        const int n1 = 3, n2 = 3, n3 = 40, n4 = 10;
        long result = 0;

        for (int pass = 0; pass < 2; pass++)
        {
            for (int a = 0; a < Size; a++)
            {
                bool runDark = false;
                int run = 0;
                var history = new int[7];
                for (int b = 0; b < Size; b++)
                {
                    bool isDark = pass == 0 ? _modules[a, b] : _modules[b, a];
                    if (isDark == runDark)
                    {
                        run++;
                        if (run == 5)
                        {
                            result += n1;
                        }
                        else if (run > 5)
                        {
                            result++;
                        }
                    }
                    else
                    {
                        AddHistory(run, history);
                        if (!runDark)
                        {
                            result += CountFinderLike(history) * n3;
                        }

                        runDark = isDark;
                        run = 1;
                    }
                }

                result += TerminateAndCount(runDark, run, history) * n3;
            }
        }

        for (int y = 0; y < Size - 1; y++)
        {
            for (int x = 0; x < Size - 1; x++)
            {
                bool c = _modules[y, x];
                if (c == _modules[y, x + 1] && c == _modules[y + 1, x] && c == _modules[y + 1, x + 1])
                {
                    result += n2;
                }
            }
        }

        int dark = 0;
        foreach (bool m in _modules)
        {
            if (m)
            {
                dark++;
            }
        }

        int total = Size * Size;
        int k = ((Math.Abs((dark * 20) - (total * 10)) + total - 1) / total) - 1;
        result += k * n4;
        return result;
    }

    private int CountFinderLike(int[] history)
    {
        int n = history[1];
        bool core = n > 0 && history[2] == n && history[3] == n * 3 && history[4] == n && history[5] == n;
        return (core && history[0] >= n * 4 && history[6] >= n ? 1 : 0) + (core && history[6] >= n * 4 && history[0] >= n ? 1 : 0);
    }

    private int TerminateAndCount(bool runDark, int run, int[] history)
    {
        if (runDark)
        {
            AddHistory(run, history);
            run = 0;
        }

        run += Size; // the light quiet zone
        AddHistory(run, history);
        return CountFinderLike(history);
    }

    private void AddHistory(int run, int[] history)
    {
        if (history[0] == 0)
        {
            run += Size; // the quiet zone before the first run
        }

        Array.Copy(history, 0, history, 1, history.Length - 1);
        history[0] = run;
    }

    // ---- Reed–Solomon over GF(2^8), polynomial 0x11D ----------------------------------------------------------------

    private static byte[] ReedSolomonDivisor(int degree)
    {
        var result = new byte[degree];
        result[degree - 1] = 1;
        int root = 1;
        for (int i = 0; i < degree; i++)
        {
            for (int j = 0; j < degree; j++)
            {
                result[j] = Multiply(result[j], root);
                if (j + 1 < degree)
                {
                    result[j] ^= result[j + 1];
                }
            }

            root = Multiply(root, 0x02);
        }

        return result;
    }

    private static byte[] ReedSolomonRemainder(byte[] data, byte[] divisor)
    {
        var result = new byte[divisor.Length];
        foreach (byte b in data)
        {
            int factor = b ^ result[0];
            Array.Copy(result, 1, result, 0, result.Length - 1);
            result[^1] = 0;
            for (int i = 0; i < result.Length; i++)
            {
                result[i] ^= Multiply(divisor[i], factor);
            }
        }

        return result;
    }

    private static byte Multiply(int x, int y)
    {
        int z = 0;
        for (int i = 7; i >= 0; i--)
        {
            z = (z << 1) ^ ((z >> 7) * 0x11D);
            z ^= ((y >> i) & 1) * x;
        }

        return (byte)z;
    }

    private static bool Bit(int value, int index) => ((value >> index) & 1) != 0;

    private sealed class BitBuffer
    {
        private readonly List<bool> _bits = [];

        public int Count => _bits.Count;

        public void Append(int value, int length)
        {
            for (int i = length - 1; i >= 0; i--)
            {
                _bits.Add(((value >> i) & 1) != 0);
            }
        }

        public byte[] ToBytes()
        {
            var result = new byte[_bits.Count / 8];
            for (int i = 0; i < _bits.Count; i++)
            {
                if (_bits[i])
                {
                    result[i >> 3] |= (byte)(0x80 >> (i & 7));
                }
            }

            return result;
        }
    }
}
