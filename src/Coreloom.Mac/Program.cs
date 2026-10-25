using Avalonia;
using Coreloom.Core;
using Coreloom.Mac.Services;

namespace Coreloom.Mac;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Another launch while Coreloom runs (from the Terminal, a script, `--page`): it hands its request to the running
        // copy and quits before any of the interface starts. (Finder, the Dock and `open` reopen the running copy themselves.)
        var command = MacCommandLine.Parse(args);
        if (command.WaitForProcessId is int pid)
        {
            // A restart (Move to Applications, an update) passes the old copy's PID: wait for it so the lock is free.
            SingleInstance.WaitForProcessExit(pid, TimeSpan.FromSeconds(15));
        }

        var instance = new SingleInstance(AppPaths.ForMac(AppContext.BaseDirectory).SettingsDirectory);
        if (!instance.TryAcquire())
        {
            instance.SignalPrimary(command.Page);
            instance.Dispose();
            return 0;
        }

        App.PrimaryInstance = instance;
        Coreloom.Mac.Diagnostics.LayoutProbe.Install();
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseAvaloniaNative()
        .UseSkia()
        .With(new AvaloniaNativePlatformOptions { RenderingMode = [AvaloniaNativeRenderingMode.Metal, AvaloniaNativeRenderingMode.OpenGl, AvaloniaNativeRenderingMode.Software] })
        .With(new MacOSPlatformOptions { ShowInDock = true, DisableDefaultApplicationMenuItems = true })
        ;
}
