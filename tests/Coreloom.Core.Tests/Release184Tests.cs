using System.Text.Json;
using Coreloom.Core.Gaming;
using Coreloom.Core.Settings;

namespace Coreloom.Core.Tests;

/// <summary>1.8.4: keeping shader caches, the DirectStorage check, VRAM management's advice, G-SYNC without the frame limiter.</summary>
public class Release184Tests
{
    private const long Gb = 1L << 30;

    [Fact]
    public void KeepShaderCaches_IsOnWhenEveryCleanupHandlerSaysZero()
    {
        Assert.False(ShaderCacheCleanup.Status(false, null, false, null).IsSupported);
        Assert.False(ShaderCacheCleanup.Status(true, 1, true, 1).IsOn);       // Windows' default
        Assert.False(ShaderCacheCleanup.Status(true, null, false, null).IsOn); // no value: the handler runs
        Assert.False(ShaderCacheCleanup.Status(true, 0, true, 1).IsOn);       // the 32-bit copy still runs
        Assert.True(ShaderCacheCleanup.Status(true, 0, true, 0).IsOn);
        Assert.True(ShaderCacheCleanup.Status(true, 0, false, null).IsOn);
        Assert.Equal(0, ShaderCacheCleanup.AutorunFor(true));
        Assert.Equal(1, ShaderCacheCleanup.AutorunFor(false));
        Assert.EndsWith(@"VolumeCaches\D3D Shader Cache", ShaderCacheCleanup.Key);
    }

    [Fact]
    public void GSyncRecommendation_PointsToTheDriversOwnLimit()
    {
        Assert.Equal("Recommended: on for fullscreen and windowed games, with your 237 FPS limit for your 240 Hz display.", GSyncSetup.Recommendation(237, 240));
        Assert.Equal("Recommended: on for fullscreen and windowed games, with Max Frame Rate in NVIDIA's settings at about 224 FPS for your 240 Hz display.", GSyncSetup.Recommendation(0, 240));
        Assert.DoesNotContain("frame limiter", GSyncSetup.Recommendation(0, 0));
    }

    private static DirectStorageInputs Inputs(int build = 26100, int? model = 0x66, IReadOnlyList<GameDrive>? drives = null, IReadOnlyList<DirectStorageGame>? games = null) =>
        new(build, "NVIDIA GeForce RTX 5090", model, drives ?? [new GameDrive(@"D:\", "NVMe", true, new BypassIoState(true))], games ?? []);

    [Fact]
    public void DirectStorage_IsReady_WithShaderModel6_NVMe_AndBypassIO()
    {
        var games = new[] { new DirectStorageGame("steam:1", "Forspoken", @"D:\"), new DirectStorageGame("steam:2", "Ratchet & Clank: Rift Apart", @"D:\") };
        var report = DirectStorageCheck.Evaluate(Inputs(games: games));
        Assert.Equal(DirectStorageState.Ready, report.State);
        Assert.Null(report.Problem);
        Assert.Equal("Ready for 2 games that use it: GPU decompression and NVMe with BypassIO.", report.Summary);
        Assert.Contains(report.Rows, r => r.Name == "Graphics card" && r.Tone == CheckTone.Good && r.Detail!.Contains("Shader Model 6.6"));
        Assert.Contains(report.Rows, r => r.Name == "Drive D:" && r.Value == "NVMe" && r.Detail!.Contains("Forspoken and Ratchet & Clank: Rift Apart are on it."));
        Assert.Contains(report.Rows, r => r.Name == "BypassIO on D:" && r.Value == "Supported");
        Assert.Contains(report.Rows, r => r.Name == "Games using DirectStorage" && r.Value == "2");

        var none = DirectStorageCheck.Evaluate(Inputs());
        Assert.Equal(DirectStorageState.Ready, none.State); // nothing uses it yet, but the PC is ready
        Assert.Contains(none.Rows, r => r.Name == "Games using DirectStorage" && r.Tone == CheckTone.Info);
    }

    [Fact]
    public void DirectStorage_SaysWhatsInTheWay()
    {
        var sata = DirectStorageCheck.Evaluate(Inputs(drives: [new GameDrive(@"E:\", "SATA", true, new BypassIoState(true))]));
        Assert.Equal(DirectStorageState.PartlyReady, sata.State);
        Assert.Contains("NVMe drive", sata.Problem);

        var hdd = DirectStorageCheck.Evaluate(Inputs(drives: [new GameDrive(@"F:\", "SATA", false, null)]));
        Assert.Contains(hdd.Rows, r => r.Value == "Hard drive" && r.Tone == CheckTone.Problem);

        var blocked = DirectStorageCheck.Evaluate(Inputs(drives: [new GameDrive(@"D:\", "NVMe", true, new BypassIoState(false, "SomeFilter.sys", "The filter driver doesn't support BypassIO."))]));
        Assert.Equal(DirectStorageState.PartlyReady, blocked.State);
        Assert.Contains("SomeFilter.sys is in the way", blocked.Problem);
        Assert.Contains("Windows says: The filter driver doesn't support BypassIO.", blocked.Problem);

        // Windows 10: works without BypassIO (not asked), noted but not in the way.
        var win10 = DirectStorageCheck.Evaluate(Inputs(build: 19045, drives: [new GameDrive(@"D:\", "NVMe", true, new BypassIoState(false, "x", "y"))]));
        Assert.Equal(DirectStorageState.Ready, win10.State);
        Assert.Contains(win10.Rows, r => r.Name == "Windows" && r.Tone == CheckTone.Info);
        Assert.DoesNotContain(win10.Rows, r => r.Name.StartsWith("BypassIO", StringComparison.Ordinal));

        var old = DirectStorageCheck.Evaluate(Inputs(model: 0x51));
        Assert.Equal(DirectStorageState.NotAvailable, old.State);
        Assert.Contains("Shader Model 5.1", old.Problem);
        Assert.Equal(DirectStorageState.NotAvailable, DirectStorageCheck.Evaluate(Inputs(model: null)).State);
    }

    [Fact]
    public void DirectStorage_FindsTheGamesThatShipItsRuntime_AndTheirDrives()
    {
        string root = Path.Combine(Path.GetTempPath(), "coreloom-ds-" + Guid.NewGuid().ToString("N"));
        try
        {
            string uses = Path.Combine(root, "Forspoken");
            Directory.CreateDirectory(Path.Combine(uses, "bin", "x64"));
            File.WriteAllText(Path.Combine(uses, "bin", "x64", "DStorage.dll"), "x");
            string doesnt = Path.Combine(root, "Celeste");
            Directory.CreateDirectory(Path.Combine(doesnt, "Content"));
            File.WriteAllText(Path.Combine(doesnt, "Celeste.exe"), "x");
            string deep = Path.Combine(root, "Deep", "a", "b", "c", "d");
            Directory.CreateDirectory(deep);
            File.WriteAllText(Path.Combine(deep, "dstoragecore.dll"), "x");

            Assert.True(DirectStorageCheck.UsesDirectStorage(uses));
            Assert.False(DirectStorageCheck.UsesDirectStorage(doesnt));
            Assert.False(DirectStorageCheck.UsesDirectStorage(Path.Combine(root, "Deep"))); // deeper than it looks
            Assert.False(DirectStorageCheck.UsesDirectStorage(Path.Combine(root, "Missing")));
            Assert.False(DirectStorageCheck.UsesDirectStorage(null));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }

        var library = new[]
        {
            new GameEntry("a", "A", GameStore.Steam) { InstallPath = @"e:\Games\A" },
            new GameEntry("b", "B", GameStore.Epic) { InstallPath = @"D:\Epic\B" },
            new GameEntry("c", "C", GameStore.Gog) { InstallPath = @"E:\GOG\C" },
            new GameEntry("d", "D", GameStore.Xbox),
        };
        Assert.Equal([@"D:\", @"E:\"], DirectStorageCheck.DriveRoots(library, @"C:\"));
        Assert.Equal([@"C:\"], DirectStorageCheck.DriveRoots([], @"C:\"));
        Assert.Null(DirectStorageCheck.RootOf("/home/games"));
    }

    [Fact]
    public void VramSession_KeepsPeaks_AndTheTimeNearlyFull()
    {
        var t0 = new DateTime(2026, 10, 30, 20, 0, 0, DateTimeKind.Utc);
        var session = new VramSession("steam:1", "Game", t0);
        session.Add(new VramSample(6 * Gb, 100L << 20, 8 * Gb, 16 * Gb), t0);
        session.Add(new VramSample(14 * Gb, 2 * Gb, 15 * Gb + Gb / 2, 16 * Gb), t0.AddMinutes(10));
        session.Add(new VramSample(-1, 0, 4 * Gb, 16 * Gb), t0.AddMinutes(11)); // unreadable: ignored
        session.Add(new VramSample(7 * Gb, 0, -1, 16 * Gb), t0.AddMinutes(20)); // the card's total use unknown: the game's counts
        Assert.Equal(3, session.Samples);
        Assert.Equal(14 * Gb, session.PeakGame);
        Assert.Equal(15 * Gb + Gb / 2, session.PeakInUse);
        Assert.Equal(2 * Gb, session.PeakShared);
        Assert.Equal(1 / 3.0, session.NearFullShare, 3);

        var record = session.ToRecord(null, t0.AddMinutes(21));
        Assert.Equal(16 * 1024, record.TotalMb);
        Assert.Equal(15 * 1024 + 512, record.PeakInUseMb);
        Assert.Equal(33, record.NearFullPercent);
        Assert.Equal(20, record.Minutes);
        Assert.Equal(1, record.Sessions);
        Assert.Equal(VramFit.Over, VramAdvice.Fit(record));

        // The next session adds to it (peaks stay the highest); a different card starts over.
        var next = new VramSession("steam:1", "Game", t0.AddDays(1));
        next.Add(new VramSample(5 * Gb, 0, 6 * Gb, 16 * Gb), t0.AddDays(1));
        var merged = next.ToRecord(record, t0.AddDays(1));
        Assert.Equal(2, merged.Sessions);
        Assert.Equal(15 * 1024 + 512, merged.PeakInUseMb);
        var newCard = new VramSession("steam:1", "Game", t0.AddDays(2));
        newCard.Add(new VramSample(5 * Gb, 0, 6 * Gb, 24 * Gb), t0.AddDays(2));
        Assert.Equal(1, newCard.ToRecord(record, t0.AddDays(2)).Sessions);
    }

    private static VramRecord Record(int inUse, int total = 16 * 1024, int shared = 0, int nearFull = 0) =>
        new() { GameId = "g", Name = "Alan Wake 2", PeakInUseMb = inUse, PeakGameMb = inUse - 1024, TotalMb = total, PeakSharedMb = shared, NearFullPercent = nearFull };

    [Fact]
    public void VramAdvice_FitsTightOrTooFull_WithARecommendation()
    {
        var fits = Record(8 * 1024);
        Assert.Equal(VramFit.Fits, VramAdvice.Fit(fits));
        Assert.Equal("Alan Wake 2 used 8 of 16 GB at its peak, 8 GB to spare. There's room for a higher Texture quality if you'd like sharper textures.", VramAdvice.Advice(fits));
        Assert.Contains("can stay where it is", VramAdvice.Advice(Record(12 * 1024)));

        var tight = Record(14 * 1024 + 600);
        Assert.Equal(VramFit.Tight, VramAdvice.Fit(tight));
        Assert.Contains("close to full", VramAdvice.Advice(tight));
        Assert.Equal("14.6 of 16 GB at most", VramAdvice.PeakText(tight));

        var spilled = Record(15 * 1024, shared: 1536);
        Assert.Equal(VramFit.Over, VramAdvice.Fit(spilled));
        Assert.Contains("1.5 GB of its graphics moved to system memory", VramAdvice.Advice(spilled));
        Assert.Contains("Lower Texture quality", VramAdvice.Advice(spilled));
        Assert.Equal(VramFit.Over, VramAdvice.Fit(Record(15 * 1024 + 800, nearFull: 12)));
        Assert.Equal(VramFit.Tight, VramAdvice.Fit(Record(15 * 1024 + 800, nearFull: 4)));
        Assert.Equal("Too full", VramAdvice.FitText(VramFit.Over));
    }

    [Fact]
    public void VramRecords_AreKeptNewestFirst_AndSurviveTheSettingsFile()
    {
        var settings = new GamingSettings();
        for (int i = 0; i < 45; i++)
        {
            settings.KeepVram(new VramRecord { GameId = $"g{i}", Name = $"Game {i}", TotalMb = 8192, PeakInUseMb = 4000, LastPlayed = new DateTime(2026, 1, 1).AddDays(i) });
        }

        Assert.Equal(VramRecord.MaxGames, settings.VramGames.Count);
        Assert.Equal("g44", settings.VramGames[0].GameId);
        settings.KeepVram(new VramRecord { GameId = "G44", Name = "Game 44", TotalMb = 8192, PeakInUseMb = 5000, LastPlayed = new DateTime(2026, 6, 1) });
        Assert.Equal(VramRecord.MaxGames, settings.VramGames.Count); // replaced, not added (ids ignore case)
        Assert.Equal(5000, settings.VramFor("g44")!.PeakInUseMb);

        settings.VramManagement = true;
        settings.VramGames.Add(new VramRecord { GameId = "", TotalMb = 8192 });
        settings.VramGames.Add(new VramRecord { GameId = "bad", TotalMb = 0 });
        var copy = JsonSerializer.Deserialize<GamingSettings>(JsonSerializer.Serialize(settings))!;
        copy.Normalize();
        Assert.True(copy.VramManagement);
        Assert.Equal(VramRecord.MaxGames, copy.VramGames.Count);
        Assert.DoesNotContain(copy.VramGames, r => r.GameId is "" or "bad");
        Assert.False(new GamingSettings().VramManagement); // off until turned on
    }
}
