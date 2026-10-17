using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Coreloom.App.Themes;
using Coreloom.App.Views;
using Coreloom.Core.Settings;
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
        var data = new SampleData();

        var gaming = data.Gaming();
        await ShootAsync(Path.Combine(output, "gaming.png"), new GamingPage { DataContext = gaming }, null, () => gaming.Refresh(data));
        await ShootAsync(Path.Combine(output, "gaming-narrow.png"), new GamingPage { DataContext = gaming }, null, () => gaming.Refresh(data), 1000);
        gaming.CoreScheduling.OpenDetailsCommand.Execute(null);
        await ShootAsync(Path.Combine(output, "gaming-core-scheduling.png"), new GamingPage { DataContext = gaming }, 1150, () => gaming.Refresh(data));

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
    }

    private static void Refresh(this PageViewModel page, SampleData data)
    {
        data.Engine.PollOnce();
        page.Update(data.Engine.LatestFrame);
    }

    /// <summary>Lays the page out at its full height (or <paramref name="height"/>), lets it settle and saves a PNG.</summary>
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
