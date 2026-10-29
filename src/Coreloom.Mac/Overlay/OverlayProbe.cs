using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Coreloom.Core.Diagnostics;
using Coreloom.Core.Settings;
using Coreloom.ViewModels.Services;

namespace Coreloom.Mac.Overlay;

/// <summary>
/// TEMPORARY PROBE (branch probe-183 only, never merged): with ~/Library/Application Support/Coreloom/overlay-probe
/// present, turns readings on and off every few seconds as a person would on the Graphics page, and after each change
/// logs the window's sizes and photographs the screen around it (soon after the change and once it has settled).
/// </summary>
internal static class OverlayProbe
{
    private static readonly string[][] Sets =
    [
        ["gpu-load", "cpu-load", "ram"],
        ["gpu-load", "gpu-temp", "gpu-power", "cpu-load", "cpu-temp", "cpu-power", "ram"],
        ["cpu-load"],
        ["gpu-load", "gpu-temp", "gpu-power", "gpu-clock", "gpu-vram", "cpu-load", "cpu-temp", "cpu-clock", "ram"],
        ["gpu-load", "cpu-load"],
        ["gpu-load", "gpu-temp", "cpu-load", "cpu-temp", "ram"],
    ];

    public static void Start(SettingsService settings, Func<OverlayWindow?> window)
    {
        string flag = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "Coreloom", "overlay-probe");
        if (!File.Exists(flag))
        {
            return;
        }

        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Logs", "Coreloom", "probe");
        Directory.CreateDirectory(folder);
        Log.Info("Probe", "Overlay probe running.");
        int step = 0;
        DispatcherTimer.RunOnce(() => Next(), TimeSpan.FromSeconds(4));

        void Next()
        {
            if (step >= Sets.Length)
            {
                Log.Info("Probe", "Overlay probe done.");
                return;
            }

            var items = Sets[step];
            int number = ++step;
            Describe(window(), $"step {number} before");
            settings.Update("Overlay", s => s.Overlay.Items = [.. items]);
            Log.Info("Probe", $"step {number}: items {string.Join(",", items)}");
            DispatcherTimer.RunOnce(() =>
            {
                Describe(window(), $"step {number} +0.15 s");
            }, TimeSpan.FromMilliseconds(150));
            DispatcherTimer.RunOnce(() =>
            {
                Describe(window(), $"step {number} +1.6 s");
            }, TimeSpan.FromMilliseconds(1600));
            DispatcherTimer.RunOnce(Next, TimeSpan.FromSeconds(3.5));
        }
    }

    private static void Describe(OverlayWindow? window, string when)
    {
        if (window is null)
        {
            Log.Info("Probe", $"{when}: no window");
            return;
        }

        var frame = OverlayNative.Frame(window.NativeHandle);
        var content = window.Content is Canvas canvas && canvas.Children.Count > 0 ? canvas.Children[0] : window.Content as Control;
        var panel = content is Panel grid && grid.Children.Count > 0 ? grid.Children[0] : null;
        Log.Info("Probe", string.Create(CultureInfo.InvariantCulture,
            $"{when}: frame {frame.X:0.#},{frame.Y:0.#} {frame.Width:0.#}×{frame.Height:0.#}; client {window.ClientSize.Width:0.#}×{window.ClientSize.Height:0.#}; " +
            $"bounds {window.Bounds.Width:0.#}×{window.Bounds.Height:0.#}; content desired {content?.DesiredSize.Width:0.#}×{content?.DesiredSize.Height:0.#}, " +
            $"bounds {content?.Bounds.Width:0.#}×{content?.Bounds.Height:0.#}; panel {panel?.Bounds.Width:0.#}×{panel?.Bounds.Height:0.#}; " +
            $"scaling {window.RenderScaling:0.##}; measure valid {content?.IsMeasureValid}, arrange valid {content?.IsArrangeValid}; SizeToContent {window.SizeToContent}"));
    }

    private static void Shoot(OverlayWindow? window, string file)
    {
        if (window is null || OverlayNative.ScreenOf(window.NativeHandle) is not { } screen)
        {
            return;
        }

        var frame = OverlayNative.Frame(window.NativeHandle);
        // screencapture -R takes x,y,w,h from the top left of the main screen, in points.
        int x = (int)Math.Floor(frame.X - 30), y = (int)Math.Floor(screen.Y + screen.Height - frame.Y - frame.Height - 30);
        int w = (int)Math.Ceiling(frame.Width + 60), h = (int)Math.Ceiling(frame.Height + 60);
        try
        {
            using var process = Process.Start(new ProcessStartInfo("/usr/sbin/screencapture", $"-x -R{Math.Max(0, x)},{Math.Max(0, y)},{w},{h} \"{file}\"") { UseShellExecute = false });
            process?.WaitForExit(4000);
        }
        catch (Exception e)
        {
            Log.Info("Probe", $"screencapture failed: {e.Message}");
        }
    }
}
