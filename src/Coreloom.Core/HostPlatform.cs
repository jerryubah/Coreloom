namespace Coreloom.Core;

/// <summary>The operating system Coreloom runs on. The view models word things and offer features to suit it.</summary>
public enum HostPlatform
{
    Windows,

    MacOS,
}

public static class HostPlatforms
{
    /// <summary>The platform this process runs on (Windows anywhere else, as Coreloom's other layers are Windows-first).</summary>
    public static HostPlatform Current => OperatingSystem.IsMacOS() ? HostPlatform.MacOS : HostPlatform.Windows;

    /// <summary>"Windows" or "macOS".</summary>
    public static string Name(this HostPlatform platform) => platform == HostPlatform.MacOS ? "macOS" : "Windows";

    /// <summary>"PC" or "Mac": how Coreloom refers to the computer.</summary>
    public static string Computer(this HostPlatform platform) => platform == HostPlatform.MacOS ? "Mac" : "PC";

    public static bool IsMac(this HostPlatform platform) => platform == HostPlatform.MacOS;
}
