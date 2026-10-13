using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;

namespace Tank.Update;

/// <summary>
/// Unpacks a downloaded release archive into a staging directory and checks it has the expected layout.
/// Entries escaping the destination (zip-slip) and links are rejected.
/// </summary>
public static class UpdateArchive
{
    public const string MacBundleName = "Tank 3D.app";
    public const string WindowsExe = "Tank3D.exe";
    public const string LinuxExe = "Tank3D.x86_64";

    public static void Extract(UpdatePlatform platform, string archive, string destDir)
    {
        if (Directory.Exists(destDir)) Directory.Delete(destDir, recursive: true);
        Directory.CreateDirectory(destDir);
        try
        {
            ExtractCore(platform, archive, destDir);
        }
        catch (InvalidDataException ex)
        {
            throw new UpdateException($"The downloaded package is damaged: {ex.Message}", ex);
        }
        ValidateLayout(platform, destDir);
    }

    private static void ExtractCore(UpdatePlatform platform, string archive, string destDir)
    {
        switch (platform)
        {
            case UpdatePlatform.MacOS:
                CheckZipEntries(archive, destDir);
                // ditto keeps the bundle's signature, permissions and extended attributes intact.
                RunOrThrow("/usr/bin/ditto", "-x", "-k", archive, destDir);
                break;
            case UpdatePlatform.Windows:
                CheckZipEntries(archive, destDir);
                ZipFile.ExtractToDirectory(archive, destDir, overwriteFiles: true);
                break;
            default:
                ExtractTarGz(archive, destDir);
                break;
        }
    }

    /// <summary>Names of the top-level items an install of this platform consists of.</summary>
    public static IReadOnlyList<string> PayloadItems(UpdatePlatform platform, string stagedDir) => platform switch
    {
        UpdatePlatform.MacOS => new[] { MacBundleName },
        _ => Directory.EnumerateFileSystemEntries(stagedDir).Select(Path.GetFileName).OfType<string>()
            .Where(n => !n.StartsWith('.')).OrderBy(n => n, StringComparer.Ordinal).ToArray(),
    };

    internal static void ValidateLayout(UpdatePlatform platform, string dir)
    {
        bool ok = platform switch
        {
            UpdatePlatform.MacOS => Directory.Exists(Path.Combine(dir, MacBundleName, "Contents", "MacOS")),
            UpdatePlatform.Windows => File.Exists(Path.Combine(dir, WindowsExe)) && Directory.EnumerateDirectories(dir, "data_*").Any(),
            _ => File.Exists(Path.Combine(dir, LinuxExe)) && Directory.EnumerateDirectories(dir, "data_*").Any(),
        };
        if (!ok) throw new UpdateException("The downloaded package does not look like a Tank 3D release.");
    }

    private static void CheckZipEntries(string archive, string destDir)
    {
        using var zip = ZipFile.OpenRead(archive);
        foreach (var entry in zip.Entries) EnsureInside(destDir, entry.FullName);
    }

    private static void ExtractTarGz(string archive, string destDir)
    {
        using var file = File.OpenRead(archive);
        using var gz = new GZipStream(file, CompressionMode.Decompress);
        using var tar = new TarReader(gz);
        while (tar.GetNextEntry() is { } entry)
        {
            if (entry.Name is "./" or "." or "") continue;
            string target = EnsureInside(destDir, entry.Name);
            switch (entry.EntryType)
            {
                case TarEntryType.Directory:
                    Directory.CreateDirectory(target);
                    break;
                case TarEntryType.RegularFile or TarEntryType.V7RegularFile:
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    entry.ExtractToFile(target, overwrite: true);
                    break;
                default:
                    throw new UpdateException($"Unexpected entry type {entry.EntryType} in update package.");
            }
        }
    }

    internal static string EnsureInside(string root, string relative)
    {
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string full = Path.GetFullPath(Path.Combine(fullRoot, relative));
        if (!full.StartsWith(fullRoot, StringComparison.Ordinal) && full + Path.DirectorySeparatorChar != fullRoot)
            throw new UpdateException($"Update package entry '{relative}' points outside the install directory.");
        return full;
    }

    private static void RunOrThrow(string exe, params string[] args)
    {
        var psi = new ProcessStartInfo(exe) { RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new UpdateException($"Could not start {exe}.");
        string err = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new UpdateException($"{Path.GetFileName(exe)} failed: {err.Trim()}");
    }
}
