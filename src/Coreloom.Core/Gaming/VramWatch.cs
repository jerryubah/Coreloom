using System.Globalization;

namespace Coreloom.Core.Gaming;

/// <summary>
/// One reading of graphics memory while a game is in front (bytes): the game's own dedicated and shared use (Windows'
/// "GPU Process Memory" counters), everything in use on the card, and the card's total. Unknown values are negative.
/// </summary>
public readonly record struct VramSample(long GameDedicated, long GameShared, long CardUsed, long Total)
{
    /// <summary>What counts as "in use" for pressure: the whole card when known, else the game alone.</summary>
    public long InUse => Math.Max(CardUsed, GameDedicated);
}

/// <summary>How a game fits in graphics memory.</summary>
public enum VramFit
{
    /// <summary>Room to spare.</summary>
    Fits,

    /// <summary>Close to full: pop-in or stutter in new areas is likely when it grows.</summary>
    Tight,

    /// <summary>Full: textures were moved to system memory, which causes pop-in and stutter.</summary>
    Over,
}

/// <summary>
/// One play session's graphics memory: samples every couple of seconds while the game is in front, kept as peaks and the
/// share of time the card was nearly full. Turned into a <see cref="VramRecord"/> when the game leaves.
/// </summary>
public sealed class VramSession
{
    /// <summary>The card counts as nearly full from here (of its total).</summary>
    public const double NearFull = 0.95;

    private int _nearFull;

    public VramSession(string gameId, string name, DateTime started)
    {
        GameId = gameId;
        Name = name;
        Started = started;
    }

    public string GameId { get; }

    public string Name { get; }

    public DateTime Started { get; }

    public DateTime LastSample { get; private set; }

    public int Samples { get; private set; }

    public long PeakGame { get; private set; }

    public long PeakInUse { get; private set; }

    public long PeakShared { get; private set; }

    public long Total { get; private set; }

    /// <summary>The latest reading (for "Watching … now").</summary>
    public VramSample? Latest { get; private set; }

    /// <summary>The share of readings with the card nearly full (0…1).</summary>
    public double NearFullShare => Samples == 0 ? 0 : _nearFull / (double)Samples;

    public void Add(VramSample sample, DateTime at)
    {
        if (sample.Total <= 0 || sample.GameDedicated < 0)
        {
            return;
        }

        Samples++;
        LastSample = at;
        Latest = sample;
        Total = sample.Total;
        PeakGame = Math.Max(PeakGame, sample.GameDedicated);
        PeakInUse = Math.Max(PeakInUse, sample.InUse);
        PeakShared = Math.Max(PeakShared, Math.Max(0, sample.GameShared));
        if (sample.InUse >= sample.Total * NearFull)
        {
            _nearFull++;
        }
    }

    /// <summary>The session as a record to keep, merged with what was known about the game (peaks are the highest seen).</summary>
    public VramRecord ToRecord(VramRecord? previous, DateTime at)
    {
        double minutes = Math.Max(0, (LastSample - Started).TotalMinutes);
        var record = previous is not null && previous.TotalMb == Mb(Total) ? previous.Copy() : new VramRecord();
        record.GameId = GameId;
        record.Name = Name;
        record.TotalMb = Mb(Total);
        record.PeakGameMb = Math.Max(record.PeakGameMb, Mb(PeakGame));
        record.PeakInUseMb = Math.Max(record.PeakInUseMb, Mb(PeakInUse));
        record.PeakSharedMb = Math.Max(record.PeakSharedMb, Mb(PeakShared));
        record.NearFullPercent = Math.Max(record.NearFullPercent, (int)Math.Round(NearFullShare * 100));
        record.Minutes += (int)Math.Round(minutes);
        record.Sessions++;
        record.LastPlayed = at;
        return record;
    }

    private static int Mb(long bytes) => (int)Math.Clamp(bytes / (1024L * 1024L), 0, int.MaxValue);
}

/// <summary>What Coreloom keeps per game about its graphics memory (MB), for its recommendation.</summary>
public sealed class VramRecord
{
    public const int MaxGames = 40;

    public string GameId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>The card's dedicated memory when measured; a new card starts the record over.</summary>
    public int TotalMb { get; set; }

    /// <summary>The most the game itself used.</summary>
    public int PeakGameMb { get; set; }

    /// <summary>The most in use on the card while the game was in front (the game plus everything else).</summary>
    public int PeakInUseMb { get; set; }

    /// <summary>The most system memory the game's graphics used (textures moved out when the card was full).</summary>
    public int PeakSharedMb { get; set; }

    /// <summary>The share of readings with the card at 95 % or more.</summary>
    public int NearFullPercent { get; set; }

    public int Minutes { get; set; }

    public int Sessions { get; set; }

    public DateTime? LastPlayed { get; set; }

    public VramRecord Copy() => (VramRecord)MemberwiseClone();

    public static List<VramRecord> Normalize(List<VramRecord>? records) =>
        (records ?? [])
            .Where(r => r is not null && !string.IsNullOrWhiteSpace(r.GameId) && r.TotalMb > 0)
            .GroupBy(r => r.GameId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(r => r.LastPlayed ?? DateTime.MinValue).First())
            .Select(r =>
            {
                r.Name ??= string.Empty;
                r.PeakGameMb = Math.Max(0, r.PeakGameMb);
                r.PeakInUseMb = Math.Max(r.PeakGameMb, r.PeakInUseMb);
                r.PeakSharedMb = Math.Max(0, r.PeakSharedMb);
                r.NearFullPercent = Math.Clamp(r.NearFullPercent, 0, 100);
                r.Minutes = Math.Max(0, r.Minutes);
                r.Sessions = Math.Max(0, r.Sessions);
                return r;
            })
            .OrderByDescending(r => r.LastPlayed ?? DateTime.MinValue)
            .Take(MaxGames)
            .ToList();
}

/// <summary>
/// Intelligent VRAM management's advice (since 1.8.4). When graphics memory runs out, Windows moves a game's textures
/// into system memory, which shows as texture pop-in and stutter; the fix is a lower texture setting (or texture
/// streaming budget) and fewer other apps on the card. The judgement uses the most in use while the game was in front:
/// <list type="bullet">
/// <item>Over: the card was at 95 % or more for at least a tenth of the play, or at 92 % and up with 1 GB or more of the
/// game's graphics moved to system memory.</item>
/// <item>Tight: 88 % or more at the peak.</item>
/// <item>Fits: below that.</item>
/// </list>
/// </summary>
public static class VramAdvice
{
    public static VramFit Fit(VramRecord record)
    {
        if (record.TotalMb <= 0)
        {
            return VramFit.Fits;
        }

        double peak = record.PeakInUseMb / (double)record.TotalMb;
        if (record.NearFullPercent >= 10 || (peak >= 0.92 && record.PeakSharedMb >= 1024))
        {
            return VramFit.Over;
        }

        return peak >= 0.88 ? VramFit.Tight : VramFit.Fits;
    }

    /// <summary>"Fits", "Tight", "Too full".</summary>
    public static string FitText(VramFit fit) => fit switch
    {
        VramFit.Over => "Too full",
        VramFit.Tight => "Tight",
        _ => "Fits",
    };

    /// <summary>"9.8 of 16 GB at most".</summary>
    public static string PeakText(VramRecord record) => $"{Gb(record.PeakInUseMb)} of {Gb(record.TotalMb)} GB at most";

    /// <summary>The game's recommendation, in a sentence or two.</summary>
    public static string Advice(VramRecord record)
    {
        string game = string.IsNullOrWhiteSpace(record.Name) ? "This game" : record.Name;
        string used = $"{Gb(record.PeakInUseMb)} of {Gb(record.TotalMb)} GB";
        switch (Fit(record))
        {
            case VramFit.Over:
                string moved = record.PeakSharedMb >= 256 ? $" and {Gb(record.PeakSharedMb)} GB of its graphics moved to system memory" : string.Empty;
                return $"{game} filled graphics memory ({used} in use{moved}), which shows as texture pop-in and stutter. Lower Texture quality (or the texture streaming budget) one step, and close other apps that use the graphics card while you play.";
            case VramFit.Tight:
                return $"{game} used {used} at its peak: close to full. If textures pop in or it stutters in new areas, lower Texture quality one step.";
            default:
                double free = (record.TotalMb - record.PeakInUseMb) / 1024.0;
                string room = record.PeakInUseMb < record.TotalMb * 0.6 && record.TotalMb >= 8 * 1024
                    ? " There's room for a higher Texture quality if you'd like sharper textures."
                    : " Texture quality can stay where it is.";
                return $"{game} used {used} at its peak, {free.ToString("0.#", CultureInfo.CurrentCulture)} GB to spare.{room}";
        }
    }

    /// <summary>"9.8" (GB with one decimal, from MB).</summary>
    public static string Gb(int mb) => (mb / 1024.0).ToString(mb >= 100 * 1024 ? "0" : "0.#", CultureInfo.CurrentCulture);
}
