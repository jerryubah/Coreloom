using Avalonia;

namespace Coreloom.Mac;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseAvaloniaNative()
        .UseSkia()
        .With(new AvaloniaNativePlatformOptions { RenderingMode = [AvaloniaNativeRenderingMode.Metal, AvaloniaNativeRenderingMode.OpenGl, AvaloniaNativeRenderingMode.Software] })
        .With(new MacOSPlatformOptions { ShowInDock = true, DisableDefaultApplicationMenuItems = true })
        ;
}
