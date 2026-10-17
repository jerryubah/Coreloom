using Coreloom.Core.Diagnostics;
using Coreloom.ViewModels.Pages;
using Coreloom.ViewModels.Services;

namespace Coreloom.ViewModels.Tests;

public class LogViewerViewModelTests
{
    private sealed class FakeLogs : IDiagnosticsService, IShellService
    {
        public List<LogRecord> Records { get; } =
        [
            new(new DateTime(2026, 10, 16, 20, 0, 0), LogLevel.Info, "Startup", "Coreloom 1.4.6 starting"),
            new(new DateTime(2026, 10, 16, 21, 0, 0), LogLevel.Debug, "Telemetry", "poll 12 ms"),
            new(new DateTime(2026, 10, 16, 22, 0, 0), LogLevel.Info, "Optimize", "Memory: Trimmed chrome.exe (PID 1234): 512.0 MB → 120.0 MB, 392.0 MB reclaimed"),
            new(new DateTime(2026, 10, 16, 22, 0, 1), LogLevel.Info, "Optimize", "Memory: Failed svchost.exe (PID 1220): access denied (a protected process)"),
            new(new DateTime(2026, 10, 16, 22, 5, 0), LogLevel.Warning, "Overlay", "The graph's window couldn't be created", "System.ComponentModel.Win32Exception: x"),
        ];

        public string? Copied { get; private set; }

        public string? OpenedFolder { get; private set; }

        public string LogDirectory => @"C:\Logs";

        public void SetVerboseLogging(bool enabled)
        {
        }

        public IReadOnlyList<LogEntry> RecentLog() => [];

        public IReadOnlyList<LogRecord> ReadLog() => Records;

        public void OpenUrl(string url)
        {
        }

        public void OpenFolder(string path) => OpenedFolder = path;

        public void OpenFile(string path)
        {
        }

        public void CopyToClipboard(string text) => Copied = text;

        public string? PickSaveFile(string suggestedName, string filter) => null;

        public void ShowMessage(string title, string message)
        {
        }

        public bool Confirm(string title, string message) => true;
    }

    [Fact]
    public async Task Loading_ShowsNewestFirst_WithCategoriesAndASummary()
    {
        var logs = new FakeLogs();
        var viewer = new LogViewerViewModel(logs, logs);
        await viewer.LoadAsync();
        Assert.Equal(["Overlay", "Optimize", "Optimize", "Telemetry", "Startup"], viewer.Lines.Select(l => l.Category));
        Assert.Equal([LogViewerViewModel.AllCategories, "Optimize", "Overlay", "Startup", "Telemetry"], viewer.Categories);
        Assert.StartsWith("5 of 5 entries", viewer.Summary);
        Assert.Equal("Warning", viewer.Lines[0].LevelText);
        Assert.True(viewer.Lines[0].HasDetails);
        Assert.False(viewer.HasFilters);
    }

    [Fact]
    public async Task Filters_NarrowTheList_AndClearAgain()
    {
        var logs = new FakeLogs();
        var viewer = new LogViewerViewModel(logs, logs);
        await viewer.LoadAsync();

        viewer.Search = "1234";
        Assert.Equal(["Memory: Trimmed chrome.exe (PID 1234): 512.0 MB → 120.0 MB, 392.0 MB reclaimed"], viewer.Lines.Select(l => l.Message));
        viewer.Search = string.Empty;
        viewer.MinimumLevel = LogLevel.Warning;
        Assert.Equal(["Overlay"], viewer.Lines.Select(l => l.Category));
        viewer.MinimumLevel = LogLevel.Debug;
        viewer.Category = "Optimize";
        Assert.Equal(2, viewer.Lines.Count);
        Assert.True(viewer.HasFilters);
        Assert.StartsWith("2 of 5 entries", viewer.Summary);

        viewer.ClearFiltersCommand.Execute(null);
        Assert.Equal(5, viewer.Lines.Count);
        Assert.False(viewer.HasFilters);
    }

    [Fact]
    public async Task OpenedFromAnotherPage_StartsFiltered_AndCopiesWhatIsShown()
    {
        var logs = new FakeLogs();
        var viewer = new LogViewerViewModel(logs, logs);
        viewer.Apply("Optimize", "Memory:");
        await viewer.LoadAsync();
        Assert.Equal("Optimize", viewer.Category);
        Assert.Equal(2, viewer.Lines.Count);

        viewer.CopyCommand.Execute(null);
        Assert.Contains("2026-10-16 22:00:01.000 Info [Optimize] Memory: Failed svchost.exe (PID 1220)", logs.Copied);
        Assert.Contains("chrome.exe", logs.Copied);
        viewer.Selected = viewer.Lines[1];
        viewer.CopyCommand.Execute(null);
        Assert.DoesNotContain("svchost", logs.Copied);

        viewer.OpenFolderCommand.Execute(null);
        Assert.Equal(@"C:\Logs", logs.OpenedFolder);
    }

    [Fact]
    public async Task ManyEntries_ShowTheNewestAndSaySo()
    {
        var logs = new FakeLogs();
        logs.Records.Clear();
        for (int i = 0; i < LogViewerViewModel.MaxShown + 10; i++)
        {
            logs.Records.Add(new LogRecord(new DateTime(2026, 10, 16).AddSeconds(i), LogLevel.Info, "Telemetry", $"entry {i}"));
        }

        var viewer = new LogViewerViewModel(logs, logs);
        await viewer.LoadAsync();
        Assert.Equal(LogViewerViewModel.MaxShown, viewer.Lines.Count);
        Assert.Equal($"entry {LogViewerViewModel.MaxShown + 9}", viewer.Lines[0].Message);
        Assert.StartsWith("Showing the newest 1,500 of 1,510 matching entries", viewer.Summary);
    }
}
