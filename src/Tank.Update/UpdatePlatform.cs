namespace Tank.Update;

public enum UpdatePlatform
{
    MacOS,
    Windows,
    Linux,
}

public static class UpdatePlatformNames
{
    public static string Key(this UpdatePlatform p) => p switch
    {
        UpdatePlatform.MacOS => "macos",
        UpdatePlatform.Windows => "windows",
        _ => "linux",
    };

    public static UpdatePlatform? FromKey(string key) => key switch
    {
        "macos" => UpdatePlatform.MacOS,
        "windows" => UpdatePlatform.Windows,
        "linux" => UpdatePlatform.Linux,
        _ => null,
    };

    /// <summary>The platform this process runs on, or null when online update is unsupported.</summary>
    public static UpdatePlatform? Current()
    {
        if (OperatingSystem.IsMacOS()) return UpdatePlatform.MacOS;
        if (OperatingSystem.IsWindows()) return UpdatePlatform.Windows;
        if (OperatingSystem.IsLinux()) return UpdatePlatform.Linux;
        return null;
    }
}
