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

        Coreloom.Mac.Services.Backdrop.Initialize(Path.Combine(Path.GetTempPath(), "coreloom-macshots-backdrops"));
        var theme = new ThemeService(Application.Current!.Resources);
        theme.Initialize(new AppearanceSettings { ReducedMotion = true, AnimateValues = false, UseMica = false });

        // A picture to show as the Mac's desktop picture on the Overview's MacBook (--wallpaper=path).
        if (args.FirstOrDefault(a => a.StartsWith("--wallpaper=", StringComparison.Ordinal)) is { } wallpaper)
        {
            Coreloom.Mac.Services.DesktopPicture.ShowForPreview(Path.GetFullPath(wallpaper[12..]));
        }

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
        File.WriteAllLines(Path.Combine(output, "glow-check.txt"), GlowChecks);
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

        // A Bluetooth device renamed in Coreloom (Devices shows the given name, its tool tip the device's own).
        data.Settings.Update("Devices", s => s.Devices.BluetoothNames["3"] = "Desk trackpad");

        // A few readings in the menu bar, so Settings › Menu bar's preview has something to show.
        data.Settings.Update("MenuBar", s =>
        {
            foreach (string id in new[] { "gpu", "fans", "gpu-temp", "memory", "cpu" })
            {
                s.MenuBar.For(id).Shown = true;
            }
        });
        var shell = data.Shell(theme);

        // The window's background (--backdrop=gradient:aurora, image:silk, color:#0B1020, seethrough), and a picture's
        // strength and blur (--opacity=60 --blur=25).
        if (Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("--backdrop=", StringComparison.Ordinal)) is { } backdrop)
        {
            string[] parts = backdrop[11..].Split(':', 2);
            data.Settings.Update("Appearance", s =>
            {
                var b = s.Appearance.Backdrop;
                b.Kind = Enum.Parse<BackdropKind>(parts[0], ignoreCase: true);
                if (parts.Length > 1)
                {
                    switch (b.Kind)
                    {
                        case BackdropKind.Color: b.Color = parts[1]; break;
                        case BackdropKind.Gradient: b.Gradient = parts[1]; break;
                        case BackdropKind.Image: b.Image = parts[1]; break;
                    }
                }

                if (Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("--opacity=", StringComparison.Ordinal)) is { } opacity)
                {
                    b.ImageOpacity = int.Parse(opacity[10..], System.Globalization.CultureInfo.InvariantCulture);
                }

                if (Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("--blur=", StringComparison.Ordinal)) is { } blur)
                {
                    b.Blur = int.Parse(blur[7..], System.Globalization.CultureInfo.InvariantCulture);
                }
            });
        }

        // The sidebar's look (--sidebar=liquidglass, or --sidebar=solid:80:20 with its shadow and blur in percent).
        if (Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("--sidebar=", StringComparison.Ordinal)) is { } sidebar)
        {
            string[] parts = sidebar[10..].Split(':');
            data.Settings.Update("Appearance", s =>
            {
                var look = s.Appearance.Sidebar;
                look.Surface = Enum.Parse<SidebarSurface>(parts[0], ignoreCase: true);
                if (parts.Length > 1)
                {
                    look.Shadow = int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
                }

                if (parts.Length > 2)
                {
                    look.Blur = int.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
                }
            });
        }

        // The grain over the gradients (--noise=0 … 100; 50 is the standard).
        if (Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("--noise=", StringComparison.Ordinal)) is { } noise)
        {
            data.Settings.Update("Appearance", s => s.Appearance.Noise = int.Parse(noise[8..], System.Globalization.CultureInfo.InvariantCulture));
        }

        // The Mac picture's lighting color (--glow=Rainbow), and the desktop picture off (--no-wallpaper).
        if (Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("--glow=", StringComparison.Ordinal)) is { } glow)
        {
            data.Settings.Update("Dashboard", s => s.Dashboard.PcGlow = Enum.Parse<PcGlow>(glow[7..], ignoreCase: true));
        }

        if (Environment.GetCommandLineArgs().Contains("--no-wallpaper"))
        {
            data.Settings.Update("Dashboard", s => s.Dashboard.ShowDesktopPicture = false);
        }

        // An update to show in Settings › Updates (MACSHOTS_RELEASE=notes.md).
        if (Environment.GetEnvironmentVariable("MACSHOTS_RELEASE") is { Length: > 0 })
        {
            data.Settings.Update("Updates", s => s.Updates.AutoInstall = false);
            var check = shell.Updates.CheckCommand.ExecuteAsync(null);
            for (int i = 0; i < 400 && !check.IsCompleted; i++)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(5);
            }
        }

        if (Environment.GetCommandLineArgs().Contains("--customize"))
        {
            shell.Overview.CustomizeCommand.Execute(null);
        }
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
        // Re-parenting the content above re-binds the sidebar's list, which drops the selection the window made before
        // its first layout; the app itself keeps it. Put it back so the current page shows as selected in the pictures.
        window.FindControl<ListBox>("NavList")!.SelectedItem = shell.CurrentPage;
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
            if (name == "gaming")
            {
                DriveMacBoosts(window, data, shell);
            }

            Capture(window, Path.Combine(output, $"{name}.png"));
            GlowClipCheck(window, name);
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

        // A card's edge lit by the pointer near its top-left corner: the glow must spread outside the card, not stop at its
        // rectangle (1.9.4; glow-check.txt checks every page).
        if (only is null || only.Split(',').Contains("overview"))
        {
            shell.Navigate(shell.Overview);
            window.Height = WindowHeight * Scale;
            Settle(window, data, 4);
            var card = window.GetVisualDescendants().OfType<Coreloom.Mac.Controls.SquircleBorder>()
                .FirstOrDefault(b => b.EdgeGlow && b.DataContext is SummaryCardViewModel);
            if (card is not null)
            {
                card.ShowPointerLight(new Point(6, 6));
                Settle(window, data, 1);
                Capture(window, Path.Combine(output, "overview-hover.png"));
                card.ShowPointerLight(new Point(-1000, -1000));
                Settle(window, data, 1);
            }
        }

        // The drive speed test, scrolled to the top of the window (the website's "New in 1.8.4" picture for Mac visitors).
        if (only is null || only.Split(',').Contains("storage"))
        {
            shell.Navigate(shell.Pages.First(p => p.Key == "storage"));
            window.Height = WindowHeight * Scale;
            Settle(window, data, 4);
            var host = window.FindControl<Coreloom.Mac.Controls.PageHost>("Host")!;
            var scroller = host.GetVisualDescendants().OfType<ScrollViewer>().First();
            var title = host.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(b => b.Text == "Drive speed test");
            if (title is not null && scroller.Content is Visual inner && title.TranslatePoint(default, inner) is { } at)
            {
                scroller.Offset = new Vector(0, Math.Max(0, at.Y - 64));
                Settle(window, data, 2);
                Capture(window, Path.Combine(output, "storage-speedtest.png"));
                scroller.Offset = default;
            }
        }

        // A Bluetooth device being renamed.
        if (only is null || only.Split(',').Contains("devices"))
        {
            shell.Navigate(shell.Devices);
            window.Height = WindowHeight * Scale;
            Settle(window, data, 4);
            if (shell.Devices.BluetoothDevices.FirstOrDefault(d => d.Id == "2") is { } device)
            {
                device.RenameCommand.Execute(null);
                device.EditName = "Studio keyboard";
                Settle(window, data, 4);
                Capture(window, Path.Combine(output, "devices-rename.png"));
                device.CancelRenameCommand.Execute(null);
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

            // The Mac's own boosts (1.9.4): the cards, a few of their Details, and the background apps card.
            if (window.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(b => b.Text == "Performance boosts") is { } boostsTitle
                && scroller.Content is Visual gamingContent && boostsTitle.TranslatePoint(default, gamingContent) is { } boostsAt)
            {
                scroller.Offset = new Vector(0, Math.Max(0, boostsAt.Y - 24));
                Settle(window, data, 2);
                Capture(window, Path.Combine(output, "gaming-boosts.png"));
                scroller.Offset = default;
            }

            foreach (var (tweak, file) in new[]
            {
                (Coreloom.Core.Gaming.GamingTweak.MemoryGuard, "memory"), (Coreloom.Core.Gaming.GamingTweak.MetalFx, "metalfx"),
                (Coreloom.Core.Gaming.GamingTweak.DisplayPipeline, "display"), (Coreloom.Core.Gaming.GamingTweak.CoreWorkload, "cores"),
                (Coreloom.Core.Gaming.GamingTweak.HighPowerAutomation, "power"), (Coreloom.Core.Gaming.GamingTweak.ThermalHeadroom, "thermal"),
                (Coreloom.Core.Gaming.GamingTweak.NativeApps, "native"),
            })
            {
                shell.Gaming.Boosts.First(b => b.Tweak == tweak).DetailsCommand!.Execute(null);
                Settle(window, data, 3);
                Capture(window, Path.Combine(output, $"gaming-details-{file}.png"));
                shell.Gaming.Details.CloseCommand.Execute(null);
            }

            shell.Gaming.MacBoosts.Apps.IsOpen = true;
            Wait(shell.Gaming.MacBoosts.Apps.RefreshAsync());
            if (shell.Gaming.MacBoosts.Apps.Rows.FirstOrDefault(r => r.Name == "Google Chrome") is { } chrome)
            {
                chrome.IsSelected = true;
            }

            Settle(window, data, 3);
            Capture(window, Path.Combine(output, "gaming-apps.png"));
            shell.Gaming.MacBoosts.Apps.IsOpen = false;
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

            // Settings › Appearance scrolled to the sidebar's look and the gradient noise (the website's "New in 1.9.3"
            // picture for Mac visitors).
            shell.Settings.SelectedSection = "Appearance";
            Settle(window, data, 3);
            if (window.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(b => b.Text == "Sidebar" && b.IsEffectivelyVisible) is { } sidebarTitle
                && sidebarTitle.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault() is { } settingsScroller
                && settingsScroller.Content is Visual settingsContent && sidebarTitle.TranslatePoint(default, settingsContent) is { } sidebarAt)
            {
                settingsScroller.Offset = new Vector(0, Math.Max(0, sidebarAt.Y - 140));
                Settle(window, data, 3);
                Capture(window, Path.Combine(output, "settings-appearance-sidebar.png"));
                settingsScroller.Offset = default;
            }

            // Settings › Connect turned off, and in the light theme.
            if (shell.Settings.Connect is { } connect)
            {
                shell.Settings.SelectedSection = "Connect";
                connect.IsEnabled = false;
                Settle(window, data, 3);
                Capture(window, Path.Combine(output, "settings-connect-off.png"));
                connect.IsEnabled = true;
                theme.Apply(new AppearanceSettings { Theme = ThemePreference.Light, ReducedMotion = true, AnimateValues = false, UseMica = false });
                Settle(window, data, 3);
                Capture(window, Path.Combine(output, "settings-connect-light.png"));
                theme.Apply(new AppearanceSettings { Theme = ThemePreference.Dark, ReducedMotion = true, AnimateValues = false, UseMica = false });
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

    /// <summary>
    /// The Mac's own boosts on (1.9.4), fed a game that's running for a few seconds of readings, so their cards show what
    /// they found: High Power Mode raised, memory, heat, the cores, MetalFX, the displays, Rosetta and the background apps.
    /// </summary>
    private static void DriveMacBoosts(Window window, SampleData data, ShellViewModel shell)
    {
        data.Settings.Update("Gaming", s =>
        {
            var g = s.Gaming;
            g.HighPowerAutomation = g.MemoryGuard = g.ThermalHeadroom = g.CoreWorkload = g.MetalFx = g.DisplayPipeline = g.NativeApps = g.BackgroundGovernor = true;
        });
        var boosts = shell.Gaming.MacBoosts;
        var game = shell.Gaming.LibrarySnapshot().FirstOrDefault(g => g.Id == "steam:1");
        var start = DateTime.UtcNow;
        Wait(boosts.RefreshAsync(force: true));
        Wait(boosts.RefreshAsync(Coreloom.Core.Gaming.GamingTweak.MemoryGuard));
        for (int i = 1; i <= 4; i++)
        {
            Wait(boosts.TickAsync(new MacBoostTick(game, 401, true), start.AddSeconds(2 * i)));
        }

        Settle(window, data, 3);
    }

    /// <summary>Waits for work that comes back to the UI thread, running the UI thread's jobs meanwhile.</summary>
    private static void Wait(Task task)
    {
        var waited = System.Diagnostics.Stopwatch.StartNew();
        while (!task.IsCompleted && waited.Elapsed < TimeSpan.FromSeconds(30))
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }

        task.GetAwaiter().GetResult();
    }

    /// <summary>Lines of <c>glow-check.txt</c>: for each page, how many glowing cards there are and which would have their glow cut.</summary>
    private static readonly List<string> GlowChecks = [];

    /// <summary>
    /// Every card on the page whose rim glows under the pointer, checked against its ancestors: one that clips (a templated
    /// control does by default) must leave room for the 14 points the glow spreads past the card. A scroll viewer may cut
    /// it at its top and bottom (that's scrolling), not at its sides.
    /// </summary>
    private static void GlowClipCheck(Window window, string page)
    {
        const double Reach = 14;
        // Measured inside the 2× layout transform, in the app's own points.
        Visual root = window.Content is LayoutTransformControl { Child: { } inner } ? inner : window;
        int cards = 0, cut = 0;
        foreach (var card in window.GetVisualDescendants().OfType<Coreloom.Mac.Controls.SquircleBorder>().Where(b => b.EdgeGlow && b.IsEffectivelyVisible))
        {
            if (card.TranslatePoint(default, root) is not { } at || card.Bounds.Width <= 0)
            {
                continue;
            }

            cards++;
            var rect = new Rect(at, card.Bounds.Size);
            for (var v = card.GetVisualParent(); v is not null && v != root; v = v.GetVisualParent())
            {
                if (!v.ClipToBounds && v.Clip is null)
                {
                    continue;
                }

                if (v.TranslatePoint(default, root) is not { } vAt)
                {
                    break;
                }

                var clip = new Rect(vAt, v.Bounds.Size);
                bool scroller = v is Avalonia.Controls.Presenters.ScrollContentPresenter;
                double left = clip.Left - (rect.Left - Reach), right = (rect.Right + Reach) - clip.Right;
                double top = scroller ? 0 : clip.Top - (rect.Top - Reach), bottom = scroller ? 0 : (rect.Bottom + Reach) - clip.Bottom;
                if (left > 0.5 || right > 0.5 || top > 0.5 || bottom > 0.5)
                {
                    cut++;
                    GlowChecks.Add($"{page}: the glow of the card at {rect.X:0},{rect.Y:0} ({rect.Width:0} × {rect.Height:0}, {card.DataContext?.GetType().Name}) is cut by {v.GetType().Name}"
                        + (string.IsNullOrEmpty((v as Control)?.Name) ? string.Empty : $" \"{((Control)v).Name}\"")
                        + $": left {Math.Max(0, left):0}, top {Math.Max(0, top):0}, right {Math.Max(0, right):0}, bottom {Math.Max(0, bottom):0}");
                    break;
                }

                if (scroller)
                {
                    break; // what's outside the page's scroll area isn't the card's to glow over
                }
            }
        }

        GlowChecks.Add($"{page}: {cards} glowing cards, {cut} with their glow cut");
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

        // The window's background is drawn off the UI thread: wait for it (and draw what it changed).
        var waited = System.Diagnostics.Stopwatch.StartNew();
        bool drew = false;
        while (Coreloom.Mac.Services.Backdrop.IsBusy && waited.Elapsed < TimeSpan.FromSeconds(10))
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
            drew = true;
        }

        if (drew)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
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
