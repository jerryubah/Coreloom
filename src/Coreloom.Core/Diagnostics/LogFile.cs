using System.Globalization;

namespace Coreloom.Core.Diagnostics;

/// <summary>An entry read back from Coreloom's log files: its time, level, category and message, and any lines under it.</summary>
public sealed record LogRecord(DateTime Time, LogLevel Level, string Category, string Message, string? Details = null)
{
    /// <summary>The whole entry as one text (for search and copying).</summary>
    public string Text => Details is null ? Message : Message + Environment.NewLine + Details;
}

/// <summary>
/// Reads Coreloom's log files back (the format <see cref="LogEntry.Format"/> writes: "2026-10-16 22:41:05.123 INF
/// [Optimize] message", with an exception's lines indented under it) and filters them for the log viewer.
/// </summary>
public static class LogFile
{
    private const string TimeFormat = "yyyy-MM-dd HH:mm:ss.fff";

    /// <summary>The log files in a folder, oldest first: coreloom.4.log … coreloom.1.log, then coreloom.log.</summary>
    public static IReadOnlyList<string> Files(string directory, string prefix = "coreloom")
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var rolled = Directory.GetFiles(directory, prefix + ".*.log")
            .Select(f => (File: f, Index: int.TryParse(Path.GetFileNameWithoutExtension(f)[(prefix.Length + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : -1))
            .Where(f => f.Index > 0)
            .OrderByDescending(f => f.Index)
            .Select(f => f.File)
            .ToList();
        string current = Path.Combine(directory, prefix + ".log");
        if (File.Exists(current))
        {
            rolled.Add(current);
        }

        return rolled;
    }

    /// <summary>Reads and parses the files (shared with the writer), keeping the newest <paramref name="limit"/> entries.</summary>
    public static IReadOnlyList<LogRecord> Read(IEnumerable<string> files, int limit = 20000)
    {
        var records = new List<LogRecord>();
        foreach (string file in files)
        {
            try
            {
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                records.AddRange(Parse(Lines(reader)));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // being rolled over, or gone: skip it
            }
        }

        return records.Count > limit ? records.GetRange(records.Count - limit, limit) : records;
    }

    /// <summary>Entries from log lines; lines that don't start an entry belong to the one before.</summary>
    public static IReadOnlyList<LogRecord> Parse(IEnumerable<string> lines)
    {
        var records = new List<LogRecord>();
        LogRecord? current = null;
        List<string>? details = null;

        void Finish()
        {
            if (current is not null)
            {
                records.Add(details is { Count: > 0 } ? current with { Details = string.Join(Environment.NewLine, details) } : current);
            }

            current = null;
            details = null;
        }

        foreach (string line in lines)
        {
            if (TryParseLine(line, out var record))
            {
                Finish();
                current = record;
            }
            else if (current is not null && line.Length > 0)
            {
                (details ??= []).Add(line.TrimEnd());
            }
        }

        Finish();
        return records;
    }

    /// <summary>One entry line: "2026-10-16 22:41:05.123 INF [Category] message".</summary>
    public static bool TryParseLine(string line, out LogRecord record)
    {
        record = null!;
        if (line.Length < TimeFormat.Length + 7 || line[TimeFormat.Length] != ' '
            || !DateTime.TryParseExact(line.AsSpan(0, TimeFormat.Length), TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            return false;
        }

        var level = line.AsSpan(TimeFormat.Length + 1, 3) switch
        {
            "DBG" => LogLevel.Debug,
            "INF" => LogLevel.Info,
            "WRN" => LogLevel.Warning,
            "ERR" => LogLevel.Error,
            _ => (LogLevel)(-1),
        };
        int rest = TimeFormat.Length + 5;
        if ((int)level < 0 || line.Length <= rest || line[rest - 1] != ' ' || line[rest] != '[')
        {
            return false;
        }

        int close = line.IndexOf("] ", rest, StringComparison.Ordinal);
        if (close < 0)
        {
            close = line.EndsWith(']') ? line.Length - 1 : -1;
            if (close < 0)
            {
                return false;
            }
        }

        string category = line[(rest + 1)..close];
        string message = close + 2 <= line.Length ? line[Math.Min(line.Length, close + 2)..] : string.Empty;
        record = new LogRecord(time, level, category, message);
        return true;
    }

    /// <summary>
    /// Whether an entry is shown: at least <paramref name="minimum"/>, in <paramref name="category"/> (null for all), and
    /// containing every word of <paramref name="search"/> in its category, message or details (any case).
    /// </summary>
    public static bool Matches(LogRecord record, string? search, LogLevel minimum, string? category)
    {
        if (record.Level < minimum || (category is not null && !string.Equals(record.Category, category, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(search))
        {
            return true;
        }

        foreach (string word in search.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!record.Message.Contains(word, StringComparison.OrdinalIgnoreCase)
                && !record.Category.Contains(word, StringComparison.OrdinalIgnoreCase)
                && !(record.Details?.Contains(word, StringComparison.OrdinalIgnoreCase) ?? false))
            {
                return false;
            }
        }

        return true;
    }

    private static IEnumerable<string> Lines(StreamReader reader)
    {
        while (reader.ReadLine() is { } line)
        {
            yield return line;
        }
    }
}
