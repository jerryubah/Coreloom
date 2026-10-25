using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Logging;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Coreloom.Core.Settings;
using Coreloom.Mac.Themes;
using Coreloom.Mac.Views;
using Coreloom.ViewModels;
using Coreloom.ViewModels.Pages;

namespace Coreloom.MacShots;

/// <summary>
/// Draws the Mac app's window with sample data, page by page, into PNGs at Retina scale (2×), headlessly. Binding errors
/// are collected and written to binding-errors.txt (and fail the run with --strict), so a broken binding can't slip by.
/// </summary>
internal static class Program
{
    private const double Scale = 2;

    private static int Main(string[] args)
    {
        string output = Path.GetFullPath(args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal)) ?? "macshots");
        bool strict = args.Contains("--strict");
        string? only = args.FirstOrDefault(a => a.StartsWith("--only=", StringComparison.Ordinal))?[7..];
        if (args.FirstOrDefault(a => a.StartsWith("--width=", StringComparison.Ordinal)) is { } width)
        {
            WindowWidth = double.Parse(width[8..], System.Globalization.CultureInfo.InvariantCulture);
        }

        if (args.FirstOrDefault(a => a.StartsWith("--from-width=", StringComparison.Ordinal)) is { } from)
        {
            FirstWidth = double.Parse(from[13..], System.Globalization.CultureInfo.InvariantCulture);
        }
        Directory.CreateDirectory(output);
        Coreloom.Core.Settings.Hotkeys.UseMacNames = true;
        Coreloom.Core.Formatting.UnitFormatter.BinaryMemoryByDefault = true;
        var sink = new BindingErrorSink();
        Logger.Sink = sink;

        AppBuilder.Configure<Coreloom.Mac.App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();

        var theme = new ThemeService(Application.Current!.Resources);
        theme.Initialize(new AppearanceSettings { ReducedMotion = true, AnimateValues = false, UseMica = false });

        int exitCode = 0;
        if (args.Contains("--test-glow"))
        {
            foreach (var (name, glow, sheen) in new[] { ("plain", (string?)null, 0.0), ("glow", "Success", 0.0), ("sheen", null, 0.5) })
            {
                var card = new Coreloom.Mac.Controls.SquircleBorder { Width = 300, Height = 120, Background = Brushes.DimGray, CornerRadius = new CornerRadius(18), Padding = new Thickness(16), GlowKey = glow, Sheen = sheen, Child = new TextBlock { Text = "Hello " + name, FontSize = 24, Foreground = Brushes.White } };
                var w = new Window { Width = 320, Height = 140, Content = card };
                w.Show();
                Dispatcher.UIThread.RunJobs();
                Save(card, Path.Combine(output, $"test-{name}.png"));
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                w.CaptureRenderedFrame()?.Save(Path.Combine(output, $"test-{name}-frame.png"));
            }

            return 0;
        }

        try
        {
            Run(output, theme, only);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            exitCode = 1;
        }

        File.WriteAllLines(Path.Combine(output, "binding-errors.txt"), sink.Errors.Distinct());
        Console.WriteLine($"{sink.Errors.Distinct().Count()} distinct binding errors (see binding-errors.txt)");
        if (strict && sink.Errors.Count > 0)
        {
            exitCode = Math.Max(exitCode, 2);
        }

        return exitCode;
    }

    /// <summary>The window's width in points (--width=944 is the window on a 1024 × 768 screen).</summary>
    private static double WindowWidth = 1440;

    /// <summary>A width to lay the window out at before <see cref="WindowWidth"/> (--from-width=1280), as when a window is
    /// fitted to a small screen after it opened.</summary>
    private static double? FirstWidth;
    private const double WindowHeight = 960;

    private static void Run(string output, ThemeService theme, string? only)
    {
        var data = new SampleData();
        data.Prefill();
        var shell = data.Shell(theme);
        if (Environment.GetCommandLineArgs().Contains("--collapsed"))
        {
            shell.IsSidebarCollapsed = true;
        }

        var window = new MainWindow(shell);

        // Retina: the window is drawn at twice its size through a layout transform (headless windows have no scaling).
        var content = (Control)window.Content!;
        window.Content = null;
        window.Content = new LayoutTransformControl { LayoutTransform = new ScaleTransform(Scale, Scale), Child = content };
        window.Width = (FirstWidth ?? WindowWidth) * Scale;
        window.Height = WindowHeight * Scale;
        window.Show();
        Settle(window, data, 4);
        if (FirstWidth is not null)
        {
            window.Width = WindowWidth * Scale;
            Settle(window, data, 4);
        }

        // The Mac's specs arrive after the page is first laid out, as they do in the app (--late-specs).
        if (Environment.GetCommandLineArgs().Contains("--late-specs"))
        {
            var specs = shell.Overview.Specs;
            var left = specs.LeftSpecs.ToList();
            var right = specs.RightSpecs.ToList();
            specs.LeftSpecs.Clear();
            specs.RightSpecs.Clear();
            Settle(window, data, 4);
            // First without the last row on the left (Storage comes once the drives are known), then all of them.
            specs.SetSpecs(left.Take(left.Count - 1).ToList(), right);
            Settle(window, data, 4);
            specs.SetSpecs(left, right);
            Settle(window, data, 4);
        }

        foreach (var page in shell.Pages.Append(shell.Settings))
        {
            string name = page.Key.ToLowerInvariant();
            if (only is not null && !only.Split(',').Contains(name))
            {
                continue;
            }

            shell.Navigate(page);
            window.Height = WindowHeight * Scale;
            Settle(window, data, 6);
            Capture(window, Path.Combine(output, $"{name}.png"));
            if (Environment.GetEnvironmentVariable("MACSHOTS_DUMP") == name)
            {
                Dump(window.FindControl<Coreloom.Mac.Controls.PageHost>("Host")!, 0);
            }

            // The whole page: the window made as tall as the page's content.
            var host = window.FindControl<Coreloom.Mac.Controls.PageHost>("Host")!;
            var scroller = host.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            if (scroller is not null && scroller.Extent.Height > scroller.Viewport.Height + 1)
            {
                double top = scroller.TranslatePoint(default, content)?.Y ?? 0;
                window.Height = Math.Min(16000, (top + scroller.Extent.Height + 8) * Scale);
                Settle(window, data, 2);
                Capture(window, Path.Combine(output, $"{name}-full.png"));
            }
        }

        // Dialogs over their pages.
        if (only is null || only.Split(',').Contains("gpu"))
        {
            shell.Navigate(shell.Gpu);
            window.Height = WindowHeight * Scale;
            shell.Gpu.OverlaySettings.OpenCommand.Execute(null);
            Settle(window, data, 4);
            foreach (var sv in window.GetVisualDescendants().OfType<ScrollViewer>())
            {
                Console.WriteLine($"scroll {sv.Name} offset {sv.Offset} extent {sv.Extent} viewport {sv.Viewport} visible {sv.IsEffectivelyVisible}");
            }

            Capture(window, Path.Combine(output, "gpu-overlay.png"));
            shell.Gpu.OverlaySettings.CloseCommand.Execute(null);
        }

        // The game library as covers, and the card for adding a game.
        if (only is null || only.Split(',').Contains("gaming"))
        {
            shell.Navigate(shell.Gaming);
            window.Height = WindowHeight * Scale;
            shell.Gaming.IsGridView = true;
            Settle(window, data, 4);
            var host = window.FindControl<Coreloom.Mac.Controls.PageHost>("Host")!;
            var scroller = host.GetVisualDescendants().OfType<ScrollViewer>().First();
            scroller.Offset = new Vector(0, scroller.Extent.Height);
            Settle(window, data, 2);
            Capture(window, Path.Combine(output, "gaming-grid.png"));
            shell.Gaming.IsGridView = false;
            scroller.Offset = default;
            shell.Gaming.Editor.Add();
            Settle(window, data, 4);
            Capture(window, Path.Combine(output, "gaming-editor.png"));
            shell.Gaming.Editor.CancelCommand.Execute(null);
        }

        // Every section of Settings.
        if (only is null || only.Split(',').Contains("settings"))
        {
            shell.Navigate(shell.Settings);
            window.Height = WindowHeight * Scale;
            foreach (string section in shell.Settings.Sections)
            {
                shell.Settings.SelectedSection = section;
                Settle(window, data, 3);
                Capture(window, Path.Combine(output, $"settings-{section.ToLowerInvariant()}.png"));
            }

            shell.Settings.SelectedSection = shell.Settings.Sections[0];
        }

        // The light theme, once.
        theme.Apply(new AppearanceSettings { Theme = ThemePreference.Light, ReducedMotion = true, AnimateValues = false, UseMica = false });
        shell.Navigate(shell.Overview);
        window.Height = WindowHeight * Scale;
        Settle(window, data, 4);
        Capture(window, Path.Combine(output, "overview-light.png"));
        window.Close();
    }

    /// <summary>A few readings arrive, layout settles and every animation has finished.</summary>
    private static void Settle(Window window, SampleData data, int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            data.Tick();
            if (window.DataContext is ShellViewModel shell)
            {
                shell.OnFrame(data.Engine.LatestFrame);
            }

            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }

        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }

    private static void Capture(Window window, string file)
    {
        using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Nothing was drawn.");
        frame.Save(file);
        Console.WriteLine($"{Path.GetFileName(file)}: {frame.PixelSize.Width} × {frame.PixelSize.Height}");
    }

    private static void Save(Visual visual, string file)
    {
        var size = visual.Bounds.Size;
        var pixels = new PixelSize((int)Math.Ceiling(size.Width * Scale), (int)Math.Ceiling(size.Height * Scale));
        using var bitmap = new RenderTargetBitmap(pixels, new Vector(96 * Scale, 96 * Scale));
        bitmap.Render(visual);
        bitmap.Save(file);
        Console.WriteLine($"{Path.GetFileName(file)}: {size.Width:0} × {size.Height:0}");
    }

    private static void Dump(Visual visual, int depth)
    {
        string extra = visual is TextBlock t ? $" \"{t.Text}\"" : visual is Control { DataContext: { } dc } && depth < 40 ? $" dc={dc.GetType().Name}" : string.Empty;
        Console.WriteLine($"{new string(' ', depth * 2)}{visual.GetType().Name} {visual.Bounds} vis={visual.IsVisible} op={visual.Opacity}{extra}");
        foreach (var child in Avalonia.VisualTree.VisualExtensions.GetVisualChildren(visual))
        {
            Dump(child, depth + 1);
        }
    }

    /// <summary>Collects binding errors (a missing property, a wrong path) from Avalonia's log.</summary>
    private sealed class BindingErrorSink : ILogSink
    {
        public List<string> Errors { get; } = [];

        public bool IsEnabled(LogEventLevel level, string area) => level >= LogEventLevel.Warning;

        public void Log(LogEventLevel level, string area, object? source, string messageTemplate) => Add(level, area, source, messageTemplate, []);

        public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues) =>
            Add(level, area, source, messageTemplate, propertyValues);

        private void Add(LogEventLevel level, string area, object? source, string template, object?[] values)
        {
            string message = template;
            foreach (var value in values)
            {
                int start = message.IndexOf('{');
                int end = start >= 0 ? message.IndexOf('}', start) : -1;
                if (end < 0)
                {
                    break;
                }

                message = message[..start] + value + message[(end + 1)..];
            }

            string line = $"[{level}] {area}: {message} ({source?.GetType().Name})";
            lock (Errors)
            {
                Errors.Add(line);
            }
        }
    }
}
