namespace Tank.Update;

/// <summary>One item to replace: <paramref name="StagedName"/> inside the staging dir becomes <paramref name="TargetName"/>.</summary>
public readonly record struct SwapItem(string StagedName, string TargetName, bool IsDirectory);

/// <summary>Where the running game is installed and what an update must replace there.</summary>
public sealed record InstallTarget(UpdatePlatform Platform, string Directory, string Executable, string RelaunchPath)
{
    /// <summary>
    /// Locates the install from the running executable. macOS: the enclosing .app (whatever the user named it);
    /// Windows / Linux: the folder holding the executable. Returns null for layouts we do not recognise.
    /// </summary>
    public static InstallTarget? Locate(UpdatePlatform platform, string executablePath)
    {
        string exe = Path.GetFullPath(executablePath);
        if (platform == UpdatePlatform.MacOS)
        {
            var macOsDir = Path.GetDirectoryName(exe);
            var contents = macOsDir is null ? null : Path.GetDirectoryName(macOsDir);
            var bundle = contents is null ? null : Path.GetDirectoryName(contents);
            if (bundle is null || !bundle.EndsWith(".app", StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(macOsDir) != "MacOS" || Path.GetFileName(contents) != "Contents") return null;
            return new InstallTarget(platform, Path.GetDirectoryName(bundle)!, exe, bundle);
        }
        string? dir = Path.GetDirectoryName(exe);
        return dir is null ? null : new InstallTarget(platform, dir, exe, exe);
    }

    public IReadOnlyList<SwapItem> Items(string stagedDir) => Platform == UpdatePlatform.MacOS
        ? new[] { new SwapItem(UpdateArchive.MacBundleName, Path.GetFileName(RelaunchPath), true) }
        : UpdateArchive.PayloadItems(Platform, stagedDir)
            .Select(n => new SwapItem(n, n, System.IO.Directory.Exists(Path.Combine(stagedDir, n))))
            .ToArray();

    /// <summary>Whether we may replace files here (an install under a read-only location needs a manual update).</summary>
    public bool IsWritable()
    {
        try
        {
            string probe = Path.Combine(Directory, $".tank3d-write-test-{Environment.ProcessId}");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
