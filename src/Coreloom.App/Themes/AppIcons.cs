namespace Coreloom.App.Themes;

/// <summary>
/// The app icon's resources. The light icon is the blue mark on a white tile; the dark-mode icon is the same mark on a dark
/// glass tile with a blue rim (assets/branding/dark, made by build/dark-icon.py). The app shows the dark one everywhere it
/// draws its own icon while the dark theme is on: the window, the taskbar button, the tray, the splash and the sidebar.
/// The .exe's own icon (Explorer, Start menu) stays the light one, since Windows picks it before the app runs.
/// </summary>
internal static class AppIcons
{
    public static string Png(bool dark, int size) =>
        $"pack://application:,,,/Assets/coreloom{(dark ? "-dark" : string.Empty)}-{size}.png";

    public static string Ico(bool dark) =>
        $"pack://application:,,,/Assets/coreloom{(dark ? "-dark" : string.Empty)}.ico";
}
