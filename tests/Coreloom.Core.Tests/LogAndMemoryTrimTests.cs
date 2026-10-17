using Coreloom.Core.Diagnostics;
using Coreloom.Core.Optimize;

namespace Coreloom.Core.Tests;

public class LogFileTests
{
    [Fact]
    public void Lines_WrittenByTheLog_AreReadBack()
    {
        var written = new LogEntry(new DateTime(2026, 10, 16, 22, 41, 5, 123), LogLevel.Warning, "Optimize", "Failed svchost.exe (PID 1220): access denied", new InvalidOperationException("boom"));
        var records = LogFile.Parse(written.Format().Split(Environment.NewLine));
        var record = Assert.Single(records);
        Assert.Equal(new DateTime(2026, 10, 16, 22, 41, 5, 123), record.Time);
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Equal("Optimize", record.Category);
        Assert.Equal("Failed svchost.exe (PID 1220): access denied", record.Message);
        Assert.Contains("System.InvalidOperationException: boom", record.Details);
    }

    [Fact]
    public void Parse_KeepsEntriesInOrder_AndIgnoresStrayLines()
    {
        string[] lines =
        [
            "garbage before any entry",
            "2026-10-16 22:00:00.000 INF [Startup] Coreloom 1.4.6 starting",
            "2026-10-16 22:00:01.500 DBG [Telemetry] poll 12 ms",
            "2026-10-16 22:00:02.000 ERR [Overlay] The frame-time graph's thread stopped.",
            "    System.Exception: x",
            "   at Somewhere()",
            "2026-10-16 22:00:03.000 INF [Gaming] ",
            "2026-10-16 22:00:04.000 XXX [Gaming] not a level",
            "2026-10-16 22:00:05.000 INF [Gaming]",
        ];
        var records = LogFile.Parse(lines);
        Assert.Equal(["Startup", "Telemetry", "Overlay", "Gaming", "Gaming"], records.Select(r => r.Category));
        Assert.Equal(LogLevel.Error, records[2].Level);
        Assert.Equal("    System.Exception: x" + Environment.NewLine + "   at Somewhere()", records[2].Details);
        Assert.Equal(string.Empty, records[3].Message);
        Assert.Contains("not a level", records[3].Details); // a malformed line is kept as detail, not dropped
    }

    [Fact]
    public void Matches_FiltersByLevelCategoryAndEveryWord()
    {
        var record = new LogRecord(DateTime.Now, LogLevel.Info, "Optimize", "Trimmed chrome.exe (PID 1234): 512.0 MB → 120.0 MB, 392.0 MB reclaimed");
        Assert.True(LogFile.Matches(record, null, LogLevel.Debug, null));
        Assert.True(LogFile.Matches(record, "CHROME reclaimed", LogLevel.Info, "optimize"));
        Assert.False(LogFile.Matches(record, "chrome failed", LogLevel.Info, null));
        Assert.False(LogFile.Matches(record, null, LogLevel.Warning, null));
        Assert.False(LogFile.Matches(record, null, LogLevel.Debug, "Gaming"));
        Assert.True(LogFile.Matches(new LogRecord(DateTime.Now, LogLevel.Error, "X", "y", "inner detail"), "detail", LogLevel.Debug, null));
    }

    [Fact]
    public void Files_AreReadOldestFirst_AndTheNewestKept()
    {
        string dir = Path.Combine(Path.GetTempPath(), "coreloom-logtest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllLines(Path.Combine(dir, "coreloom.2.log"), ["2026-10-16 20:00:00.000 INF [A] oldest"]);
            File.WriteAllLines(Path.Combine(dir, "coreloom.1.log"), ["2026-10-16 21:00:00.000 INF [A] older"]);
            File.WriteAllLines(Path.Combine(dir, "coreloom.log"), ["2026-10-16 22:00:00.000 INF [A] newest"]);
            File.WriteAllText(Path.Combine(dir, "other.log"), "2026-10-16 23:00:00.000 INF [A] not ours");
            var files = LogFile.Files(dir);
            Assert.Equal(["coreloom.2.log", "coreloom.1.log", "coreloom.log"], files.Select(Path.GetFileName));
            Assert.Equal(["oldest", "older", "newest"], LogFile.Read(files).Select(r => r.Message));
            Assert.Equal(["older", "newest"], LogFile.Read(files, limit: 2).Select(r => r.Message));
            Assert.Empty(LogFile.Files(Path.Combine(dir, "missing")));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}

public class MemoryTrimTests
{
    [Theory]
    [InlineData(4, "System", 99, 50, "part of Windows itself")]
    [InlineData(50, "Coreloom", 99, 50, "Coreloom itself")]
    [InlineData(99, "game", 99, 50, "the app you're using keeps its memory")]
    [InlineData(120, "dwm", 99, 50, "Windows needs it to stay responsive")]
    [InlineData(121, "chrome", 99, 50, null)]
    public void SkipReasons(int pid, string name, int foreground, int own, string? expected) =>
        Assert.Equal(expected, MemoryTrim.SkipReason(pid, name, foreground, own));

    [Fact]
    public void Entries_ReadWellInTheLog()
    {
        var trimmed = new MemoryTrimEntry(1234, "chrome", TrimOutcome.Trimmed, 512L << 20, 120L << 20);
        Assert.Equal(392L << 20, trimmed.Reclaimed);
        Assert.Equal("Trimmed chrome.exe (PID 1234): 512.0 MB → 120.0 MB, 392.0 MB reclaimed", trimmed.LogLine());
        Assert.Equal("Skipped dwm.exe (PID 88) at 80.0 MB: Windows needs it to stay responsive",
            new MemoryTrimEntry(88, "dwm", TrimOutcome.Skipped, 80L << 20, Reason: "Windows needs it to stay responsive").LogLine());
        Assert.Equal("Failed svchost.exe (PID 1220): access denied (a protected process)",
            new MemoryTrimEntry(1220, "svchost", TrimOutcome.Failed, Reason: MemoryTrim.Describe(5)).LogLine());
        Assert.Equal("Skipped System (PID 4): part of Windows itself", new MemoryTrimEntry(4, "System", TrimOutcome.Skipped, Reason: "part of Windows itself").LogLine());
        Assert.Equal(0, new MemoryTrimEntry(1, "x", TrimOutcome.Failed, 100, 10).Reclaimed); // only trimmed ones count
        Assert.Equal(0, new MemoryTrimEntry(1, "x", TrimOutcome.Trimmed, 10, 100).Reclaimed); // never negative
    }

    [Fact]
    public void Totals_AndOrder()
    {
        MemoryTrimEntry[] entries =
        [
            new(1, "small", TrimOutcome.Trimmed, 20L << 20, 10L << 20),
            new(2, "skipped", TrimOutcome.Skipped, 900L << 20, Reason: "the app you're using keeps its memory"),
            new(3, "big", TrimOutcome.Trimmed, 900L << 20, 100L << 20),
            new(4, "failed", TrimOutcome.Failed, Reason: "access denied (a protected process)"),
        ];
        var totals = MemoryTrim.Totals(entries);
        Assert.Equal((2, 1, 1, 810L << 20), (totals.Trimmed, totals.Skipped, totals.Failed, totals.Reclaimed));
        Assert.Equal(0, totals.Ended);
        Assert.Equal("Trimmed 2 · skipped 1 · failed 1 · ended 0 (Coreloom never ends processes)", totals.Summary);
        Assert.Equal(["big", "small", "failed", "skipped"], MemoryTrim.Ordered(entries).Select(e => e.Name));
        Assert.Equal("access denied (a protected process)", MemoryTrim.Describe(5));
        Assert.Equal("it exited meanwhile", MemoryTrim.Describe(87));
        Assert.Equal("Windows error 1450", MemoryTrim.Describe(1450));
    }
}
