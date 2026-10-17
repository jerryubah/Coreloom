using Coreloom.Core.Gaming;
using Coreloom.Core.Settings;

namespace Coreloom.Core.Tests;

public class CustomGameTests
{
    private static CustomGame Game(string exe, string name = "Hollow Knight", string? args = null, string? picture = null) => new()
    {
        Id = CustomGames.NewId(exe, new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero)),
        Name = name,
        Executable = exe,
        Arguments = args,
        Picture = picture,
        Added = new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero),
    };

    [Fact]
    public void Entry_StartsItsOwnProgram_UnderAddedByYou()
    {
        var entry = CustomGames.ToEntry(Game(@"D:\Games\Hollow Knight\hollow_knight.exe", args: "-windowed"));
        Assert.StartsWith("custom:", entry.Id);
        Assert.Equal(8, entry.Id.Length - "custom:".Length);
        Assert.Equal(GameStore.Custom, entry.Store);
        Assert.Equal("Added by you", GameLibrary.StoreName(entry.Store));
        Assert.Equal(@"D:\Games\Hollow Knight", entry.InstallPath);
        Assert.Equal(@"""D:\Games\Hollow Knight\hollow_knight.exe"" -windowed", entry.Launch);
        Assert.Null(entry.Artwork.Cover);

        var plain = CustomGames.ToEntry(Game(@"D:\Games\Hollow Knight\hollow_knight.exe", picture: @"C:\Pics\hk.png"));
        Assert.Equal(@"D:\Games\Hollow Knight\hollow_knight.exe", plain.Launch);
        Assert.Equal(@"C:\Pics\hk.png", plain.Artwork.Cover);
    }

    [Theory]
    [InlineData(@"D:\Games\HollowKnight.exe", null, null, "Hollow Knight")]
    [InlineData(@"D:\Games\dark_souls_3.exe", null, null, "Dark Souls 3")]
    [InlineData(@"D:\G\Stray-Win64-Shipping.exe", null, null, "Stray")]
    [InlineData(@"D:\G\launcher.exe", "Launcher", "Celeste", "Celeste")]
    [InlineData(@"D:\G\game.exe", "Unity Player", "", "Game")]
    [InlineData(@"D:\G\x.exe", "Disco Elysium", null, "Disco Elysium")]
    public void Names_AreSuggestedFromTheProgram(string exe, string? product, string? description, string expected) =>
        Assert.Equal(expected, CustomGames.SuggestName(exe, product, description));

    [Theory]
    [InlineData(@"C:\", true)]
    [InlineData(@"C:\Games", true)]
    [InlineData(@"D:\Games\Celeste", false)]
    [InlineData(@"C:\Users\jerry\Desktop", true)]
    [InlineData(@"C:\Users\jerry\Downloads", true)]
    [InlineData(@"C:\Users\jerry", true)]
    [InlineData(@"C:\Program Files", true)]
    [InlineData(@"C:\Program Files\Celeste", false)]
    [InlineData(null, true)]
    public void BroadFolders_AreSpotted(string? folder, bool broad) => Assert.Equal(broad, CustomGames.IsBroadFolder(folder));

    [Fact]
    public void Running_MatchesTheProgram_AndAvoidsBroadFolders()
    {
        var desktopGame = CustomGames.ToEntry(Game(@"C:\Users\jerry\Desktop\TinyGame.exe", "Tiny Game"));
        var folderGame = CustomGames.ToEntry(Game(@"D:\Games\Celeste\Celeste.exe", "Celeste"));
        GameEntry[] games = [desktopGame, folderGame];

        // Another program on the desktop isn't the desktop game.
        Assert.Null(GameLibrary.FindRunning(games, [new RunningProcess(10, @"C:\Users\jerry\Desktop\notes.exe")]));
        Assert.Same(desktopGame, GameLibrary.FindRunning(games, [new RunningProcess(11, @"C:\Users\jerry\Desktop\TinyGame.exe")])!.Game);
        // A game's helper in its own folder still counts.
        var celeste = GameLibrary.FindRunning(games, [new RunningProcess(12, @"D:\Games\Celeste\bin\Celeste-Shipping.exe")]);
        Assert.Same(folderGame, celeste!.Game);
        Assert.Equal([12], celeste.ProcessIds);
    }

    [Fact]
    public void FrameLimits_UseThePickedProgramFirst()
    {
        var entry = CustomGames.ToEntry(Game(@"D:\Games\Celeste\launcher.exe", "Celeste"));
        var picked = GameExecutables.Pick(entry, [(@"D:\Games\Celeste\Celeste.exe", 50_000_000L)]);
        Assert.Equal("launcher.exe", picked[0]); // even though "launcher" would otherwise look like a helper
        Assert.Contains("Celeste.exe", picked);
    }

    [Fact]
    public void Settings_DropBrokenEntries()
    {
        var good = Game(@"D:\Games\Celeste\Celeste.exe", " Celeste ", args: "  ", picture: @"C:\x.txt");
        var settings = new AppSettings();
        settings.Gaming.CustomGames =
        [
            good,
            new CustomGame { Id = "custom:1", Name = "No program", Executable = "" },
            new CustomGame { Id = "steam:1", Name = "Wrong id", Executable = @"C:\a.exe" },
            new CustomGame { Id = good.Id, Name = "Duplicate", Executable = @"C:\b.exe" },
            null!,
            new CustomGame { Id = "custom:2", Name = "", Executable = @"E:\G\SuperHot.exe" },
            new CustomGame { Id = "custom:3", Name = "Relative", Executable = @"game.exe" },
        ];
        settings.Normalize();
        Assert.Equal(["Celeste", "Super Hot"], settings.Gaming.CustomGames.Select(g => g.Name));
        Assert.Null(settings.Gaming.CustomGames[0].Arguments);
        Assert.Null(settings.Gaming.CustomGames[0].Picture); // not a picture file
        Assert.True(CustomGames.IsPicture(@"C:\x.JPG"));
        Assert.False(CustomGames.IsProgram(@"C:\x.bat"));
    }
}
