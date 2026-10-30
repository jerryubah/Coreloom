using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Coreloom.App.Themes;
using Coreloom.App.Views;
using Coreloom.Core.Benchmarks;
using Coreloom.Core.Settings;
using Coreloom.ViewModels;
using Coreloom.ViewModels.Pages;

namespace Coreloom.PageShots;

/// <summary>Draws pages with sample data into PNGs (at 125 % scaling, 1600 px of page width like a 1080p/1440p window).</summary>
internal static class Program
{
    private const double PageWidth = 1600;
    private const double Dpi = 120;

    [STAThread]
    private static int Main(string[] args)
    {
        string output = Path.GetFullPath(args.Length > 0 ? args[0] : "pageshots");
        Directory.CreateDirectory(output);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (string name in new[] { "Icons", "Converters", "Controls", "Templates" })
        {
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"pack://application:,,,/Coreloom;component/Themes/{name}.xaml") });
        }

        Coreloom.App.Services.Backdrop.Initialize(Path.Combine(Path.GetTempPath(), "coreloom-pageshots-backdrops"));
        Coreloom.App.Services.Backdrop.ScaleOverride = 2; // the pictures are drawn at 192 dpi
        new ThemeService(app.Resources).Initialize(new AppearanceSettings { ReducedMotion = true });

        int exitCode = 0;
        app.Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                await RunAsync(output);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e);
                exitCode = 1;
            }
            finally
            {
                app.Shutdown();
            }
        });
        app.Run();
        return exitCode;
    }

    private static async Task RunAsync(string output)
    {
        try
        {
            await ShootWindowAsync(output);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"The window shot failed: {e}");
            File.WriteAllText(Path.Combine(output, "windows-overview-error.txt"), e.ToString());
        }

        var data = new SampleData();

        var (settings, connect) = SampleSettings.Create(data);
        await ShootAsync(Path.Combine(output, "settings-connect.png"), new SettingsPage { DataContext = settings }, 1150, () => settings.Refresh(data));
        await ShootAsync(Path.Combine(output, "settings-connect-narrow.png"), new SettingsPage { DataContext = settings }, 1250, () => settings.Refresh(data), 1000);

        // Settings › Updates with a newer version out: the "What's new" card (its notes are CHANGELOG.md's newest section).
        data.Settings.Update("Updates", s => s.Updates.AutoInstall = false);
        await settings.Updates.CheckCommand.ExecuteAsync(null);
        settings.SelectedSection = "Updates";
        await ShootAsync(Path.Combine(output, "settings-updates.png"), new SettingsPage { DataContext = settings }, 1500, () => settings.Refresh(data));
        await connect.DisposeAsync();

        var gaming = data.Gaming();
        await ShootAsync(Path.Combine(output, "gaming.png"), new GamingPage { DataContext = gaming }, null, () => gaming.Refresh(data));
        await ShootAsync(Path.Combine(output, "gaming-narrow.png"), new GamingPage { DataContext = gaming }, null, () => gaming.Refresh(data), 1000);
        gaming.CoreScheduling.OpenDetailsCommand.Execute(null);
        await ShootAsync(Path.Combine(output, "gaming-core-scheduling.png"), new GamingPage { DataContext = gaming }, 1150, () => gaming.Refresh(data));
        gaming.CoreScheduling.CloseDetailsCommand.Execute(null);

        // 1.8.4: the details behind DirectStorage's check and VRAM management's games.
        gaming.DirectStorageBoost.DetailsCommand!.Execute(null);
        await ShootAsync(Path.Combine(output, "gaming-directstorage.png"), new GamingPage { DataContext = gaming }, 1000, () => gaming.Refresh(data));
        gaming.VramBoost.DetailsCommand!.Execute(null);
        await ShootAsync(Path.Combine(output, "gaming-vram.png"), new GamingPage { DataContext = gaming }, 1000, () => gaming.Refresh(data));
        gaming.Details.CloseCommand.Execute(null);

        var memory = data.Page(c => new MemoryViewModel(c));
        await ShootAsync(Path.Combine(output, "memory.png"), new MemoryPage { DataContext = memory }, null, () => memory.Refresh(data));

        var cpu = data.Page(c => new CpuViewModel(c));
        await ShootAsync(Path.Combine(output, "processor.png"), new CpuPage { DataContext = cpu }, null, () => cpu.Refresh(data));

        var gpu = data.Page(c => new GpuViewModel(c));
        await ShootAsync(Path.Combine(output, "graphics.png"), new GpuPage { DataContext = gpu }, null, () => gpu.Refresh(data));

        var optimize = data.Page(c => new OptimizeViewModel(c));
        await ShootAsync(Path.Combine(output, "optimize.png"), new OptimizePage { DataContext = optimize }, null, () => optimize.Refresh(data));

        var storage = data.Page(c => new StorageViewModel(c));
        await ShootAsync(Path.Combine(output, "storage.png"), new StoragePage { DataContext = storage }, null, () => storage.Refresh(data));

        await ShootBenchmarkingAsync(output, data);
        await CheckBenchmarksAsync(output);
    }

    private static async Task ShootBenchmarkingAsync(string output, SampleData data)
    {
        var bench = data.Page(c => new BenchmarkingViewModel(c));
        await ShootAsync(Path.Combine(output, "benchmarking.png"), new BenchmarkingPage { DataContext = bench }, null, () => bench.Refresh(data));
        await ShootAsync(Path.Combine(output, "benchmarking-narrow.png"), new BenchmarkingPage { DataContext = bench }, null, () => bench.Refresh(data), 1000);

        foreach (var (kind, name) in new[] { (BenchmarkKind.Cpu, "cpu"), (BenchmarkKind.Gpu, "gpu"), (BenchmarkKind.Stress, "stress") })
        {
            bench.OpenDetailsCommand.Execute(bench.Results.First(r => r.Result.Kind == kind && (kind == BenchmarkKind.Stress || r.IsBest)));
            await ShootAsync(Path.Combine(output, $"benchmarking-details-{name}.png"), new BenchmarkingPage { DataContext = bench }, 1150, () => bench.Refresh(data));
            var scrolled = new BenchmarkingPage { DataContext = bench };
            await ShootAsync(Path.Combine(output, $"benchmarking-details-{name}-end.png"), scrolled, 1150, () =>
            {
                bench.Refresh(data);
                (scrolled.FindName("DetailsScroll") as ScrollViewer)?.ScrollToEnd();
            });
            bench.CloseDetailsCommand.Execute(null);
        }

        bench.RunCpuCommand.Execute(null);
        await ShootAsync(Path.Combine(output, "benchmarking-running.png"), new BenchmarkingPage { DataContext = bench }, null, () => bench.Refresh(data));
        bench.CancelCommand.Execute(null);
        await Task.Delay(500);

        bench.StartStressCommand.Execute(null);
        await Task.Delay(TimeSpan.FromSeconds(32)); // three 10-second loops
        await ShootAsync(Path.Combine(output, "benchmarking-stress.png"), new BenchmarkingPage { DataContext = bench }, null, () => bench.Refresh(data));
        bench.StopStressCommand.Execute(null);
        await Task.Delay(1500);
    }

    /// <summary>
    /// Runs the real CPU benchmark on the build machine and the real GPU benchmark on WARP (Windows' software renderer),
    /// at full size, and writes what they measured to benchmark-check.txt.
    /// </summary>
    private static async Task CheckBenchmarksAsync(string output)
    {
        var lines = new List<string> { $"Processor: {Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER")}, {Environment.ProcessorCount} logical processors" };
        try
        {
            var cpu = await CpuBenchmark.RunAsync(new CpuBenchmarkOptions(Seconds: 1, WarmupSeconds: 0.3), null, CancellationToken.None);
            foreach (var t in cpu.Tests)
            {
                lines.Add($"CPU {t.Name}: single {t.SingleRate:0.###} {t.Unit} = {t.SingleScore:0} · multi {t.MultiRate:0.###} {t.Unit} = {t.MultiScore:0}");
            }

            lines.Add($"CPU single-core {cpu.SingleScore:0} · multi-core {cpu.MultiScore:0} on {cpu.Threads} threads · memory {cpu.MemoryBandwidthGBs:0.0} GB/s, {cpu.MemoryLatencyNs:0.0} ns");
        }
        catch (Exception e)
        {
            lines.Add($"CPU benchmark failed: {e}");
        }

        try
        {
            foreach (var adapter in Coreloom.Hardware.Gpu.GpuBenchmark.Adapters())
            {
                lines.Add($"Adapter: {adapter.Name} ({adapter.Memory / (1L << 20):0} MB)");
            }

            var clock = System.Diagnostics.Stopwatch.StartNew();
            var gpu = await Coreloom.Hardware.Gpu.GpuBenchmark.RunAsync(new GpuBenchmarkOptions(Seconds: 1.5, Software: true), null, null, CancellationToken.None);
            foreach (var t in gpu.Tests)
            {
                lines.Add($"GPU {t.Name} on {gpu.Adapter.Name}: {t.Rate:0.###} {t.Unit} = {t.Score:0.#}");
            }

            lines.Add($"GPU overall {gpu.Score:0.#} · graphics {gpu.Graphics:0.#} · compute {gpu.Compute:0.#} in {clock.Elapsed.TotalSeconds:0.0} s");
        }
        catch (Exception e)
        {
            lines.Add($"GPU benchmark failed: {e}");
        }

        File.WriteAllLines(Path.Combine(output, "benchmark-check.txt"), lines);
        foreach (string line in lines)
        {
            Console.WriteLine(line);
        }
    }

    private static void Refresh(this PageViewModel page, SampleData data)
    {
        data.Engine.PollOnce();
        page.Update(data.Engine.LatestFrame);
    }

    /// <summary>Lays the page out at its full height (or <paramref name="height"/>), lets it settle and saves a PNG.</summary>
    /// <summary>
    /// The whole window at twice its size, for the website's pictures of Coreloom on Windows: the Overview dark and light
    /// (windows-overview.png, windows-overview-light.png), Gaming and Smart Optimize.
    /// </summary>
    private static async Task ShootWindowAsync(string output)
    {
        var data = new SampleData(sampleClock: true);
        for (int i = 0; i < 120; i++)
        {
            data.Poll(); // two minutes of readings, so the charts are full
        }

        var releases = new SampleReleases(none: true);
        var shell = SampleSettings.Shell(data, releases);
        var window = new MainWindow(shell)
        {
            Width = 1440,
            Height = 960,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -3000,
            Top = 0,
            ShowActivated = false,
            ShowInTaskbar = false,
        };
        window.Show();

        // The build machine's screen is smaller than the window: the window's content goes on a canvas inside it, where
        // it takes its own size (1440 × 960), as on a bigger screen.
        var root = (FrameworkElement)window.Content;
        window.Content = null;
        root.Width = 1440;
        root.Height = 960;
        var canvas = new Canvas();
        canvas.Children.Add(root);
        window.Content = canvas;
        window.IsCompactSidebar = false;

        // The build machine isn't an administrator: the note about it isn't part of the picture.
        shell.DismissBannerCommand.Execute(null);
        static BackdropSettings Picture(string id) => new() { Kind = BackdropKind.Image, Image = id };
        var shots = new (string Name, ThemePreference Theme, PageViewModel Page, BackdropSettings Backdrop)[]
        {
            ("windows-overview", ThemePreference.Dark, shell.Overview, new BackdropSettings()),
            ("windows-overview-light", ThemePreference.Light, shell.Overview, new BackdropSettings()),
            ("windows-gaming", ThemePreference.Dark, shell.Gaming, new BackdropSettings()),
            ("windows-optimize", ThemePreference.Dark, shell.Optimize, new BackdropSettings()),
            ("windows-overview-aurora", ThemePreference.Dark, shell.Overview, Picture("aurora")),
            ("windows-overview-dunes", ThemePreference.Dark, shell.Overview, Picture("dunes")),
            ("windows-overview-midnight", ThemePreference.Dark, shell.Overview, new BackdropSettings { Gradient = "midnight" }),
            ("windows-settings-appearance", ThemePreference.Dark, shell.Settings, Picture("bloom")),
        };
        foreach (var (name, preference, page, backdrop) in shots)
        {
            ThemeService.Instance?.Apply(new AppearanceSettings { Theme = preference, ReducedMotion = true, Backdrop = backdrop });
            data.Settings.Current.Appearance.Backdrop = backdrop; // what Settings › Appearance shows
            shell.Settings.Backdrop.Refresh();
            shell.Settings.SelectedSection = "Appearance";
            shell.Navigate(page);
            for (int i = 0; i < 40; i++)
            {
                shell.OnFrame(data.Poll());
                await Task.Delay(120);
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            }

            Save(root, Path.Combine(output, name + ".png"));
        }

        // A newer version out: the sidebar's update button, the red dot on Settings and Settings › Updates.
        ThemeService.Instance?.Apply(new AppearanceSettings { Theme = ThemePreference.Dark, ReducedMotion = true });
        releases.None = false;
        data.Settings.Update("Updates", u => u.Updates.AutoInstall = false);
        await shell.Updates.CheckCommand.ExecuteAsync(null);
        shell.Settings.SelectedSection = "Updates";
        shell.Navigate(shell.Settings);
        for (int i = 0; i < 20; i++)
        {
            shell.OnFrame(data.Poll());
            await Task.Delay(120);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        }

        Save(root, Path.Combine(output, "windows-settings-update.png"));

        ThemeService.Instance?.Apply(new AppearanceSettings { Theme = ThemePreference.Dark, ReducedMotion = true });
        try
        {
            await ShootOverlayOnScreenAsync(output, shell, data);
        }
        catch (Exception e)
        {
            File.WriteAllText(Path.Combine(output, "overlay-on-screen-error.txt"), e.ToString());
        }

        window.AllowClose = true;
        window.Close();
    }

    /// <summary>
    /// The in-game overlay on the real screen (since 1.8.3), over a black window, after the theme has been applied again
    /// (as when the background or accent changes, which used to ask Windows for round corners on every window and gave
    /// the overlay a grey border on Windows 11): overlay-on-screen.png and the pixels at its window's edge in
    /// overlay-on-screen.txt. Then once more with round corners asked for, as before, for comparison
    /// (overlay-on-screen-rounded.png).
    /// </summary>
    private static async Task ShootOverlayOnScreenAsync(string output, ShellViewModel shell, SampleData data)
    {
        var black = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            Background = Brushes.Black,
            Left = 0,
            Top = 0,
            Width = 640,
            Height = 400,
            ShowInTaskbar = false,
            ShowActivated = false,
        };
        black.Show();
        data.Settings.Update("Overlay", s => s.Overlay.Enabled = true);
        var overlay = new OverlayWindow(shell.Overlay) { Left = 80, Top = 60 };
        overlay.Show();
        for (int i = 0; i < 12; i++)
        {
            shell.OnFrame(data.Poll());
            await Task.Delay(120);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        }

        ThemeService.Instance?.Apply(new AppearanceSettings { Theme = ThemePreference.Dark, ReducedMotion = true });
        await Task.Delay(800);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var report = new System.Text.StringBuilder();
        IntPtr hwnd = overlay.Handle;
        ScreenCapture.Save(new Int32Rect(0, 0, 640, 400), Path.Combine(output, "overlay-on-screen.png"));
        report.AppendLine("After the theme was applied again (1.8.3: the overlay is left out, and asks for no border or round corners):");
        report.AppendLine(ScreenCapture.DescribeEdge(hwnd));

        // The old behaviour for comparison: round corners asked for, as the theme did before 1.8.3.
        ScreenCapture.AskForRoundCorners(hwnd);
        await Task.Delay(800);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        ScreenCapture.Save(new Int32Rect(0, 0, 640, 400), Path.Combine(output, "overlay-on-screen-rounded.png"));
        report.AppendLine("With round corners asked for (before 1.8.3):");
        report.AppendLine(ScreenCapture.DescribeEdge(hwnd));
        File.WriteAllText(Path.Combine(output, "overlay-on-screen.txt"), report.ToString());

        overlay.Close();
        black.Close();
        data.Settings.Update("Overlay", s => s.Overlay.Enabled = false);
    }

    /// <summary>Draws an element at twice its size over the window's background into a PNG.</summary>
    private static void Save(FrameworkElement root, string file)
    {
        root.UpdateLayout();
        const double dpi = 192;
        var background = new DrawingVisual();
        using (var dc = background.RenderOpen())
        {
            dc.DrawRectangle(Application.Current.TryFindResource("Brush.WindowBackground") as Brush ?? Brushes.Black, null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
        }

        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth * dpi / 96), (int)Math.Ceiling(root.ActualHeight * dpi / 96), dpi, dpi, PixelFormats.Pbgra32);
        bitmap.Render(background);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(file))
        {
            encoder.Save(stream);
        }

        Console.WriteLine($"{Path.GetFileName(file)}: {root.ActualWidth:0} × {root.ActualHeight:0}");
    }

    private static async Task ShootAsync(string file, FrameworkElement page, double? height, Action tick, double width = PageWidth)
    {
        page.Width = width;
        if (height is { } h)
        {
            page.Height = h;
        }

        // A canvas gives the page all the height it wants, so its scroll viewer shows everything.
        var canvas = new Canvas();
        canvas.Children.Add(page);
        var window = new Window
        {
            Width = 800,
            Height = 600,
            Left = -2000,
            Top = 0,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            ShowActivated = false,
            Content = canvas,
            Background = Application.Current.TryFindResource("Brush.WindowBackground") as Brush ?? new SolidColorBrush(Color.FromRgb(0x0E, 0x10, 0x14)),
        };

        // As the main window sets them (Views/MainWindow.xaml), so text measures the same.
        window.SetResourceReference(Control.ForegroundProperty, "Brush.Text");
        window.FontFamily = (FontFamily)Application.Current.FindResource("UiFont");
        window.FontSize = 13;
        TextOptions.SetTextFormattingMode(window, TextFormattingMode.Display);
        TextOptions.SetTextHintingMode(window, TextHintingMode.Fixed);
        window.Show();
        for (int i = 0; i < 12; i++)
        {
            tick();
            await Task.Delay(250);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        }

        page.UpdateLayout();
        width = page.ActualWidth;
        double pageHeight = page.ActualHeight;
        var background = new DrawingVisual();
        using (var dc = background.RenderOpen())
        {
            dc.DrawRectangle(window.Background, null, new Rect(0, 0, width, pageHeight));
        }

        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width * Dpi / 96), (int)Math.Ceiling(pageHeight * Dpi / 96), Dpi, Dpi, PixelFormats.Pbgra32);
        bitmap.Render(background);
        bitmap.Render(page);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(file))
        {
            encoder.Save(stream);
        }

        Console.WriteLine($"{Path.GetFileName(file)}: {width:0} × {pageHeight:0}");
        window.Close();
    }
}
