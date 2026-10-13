using System.Text.Json;

namespace Tank.Update;

public enum UpdateStatus
{
    Idle,
    Checking,
    UpToDate,
    Downloading,
    Ready,          // staged and installable: offer "restart to update"
    ManualRequired, // newer version exists but this install cannot be replaced in place
    Failed,
}

public sealed record UpdateConfig(
    Uri FeedBase,
    string PublicKeyPem,
    SemVer CurrentVersion,
    UpdatePlatform Platform,
    string WorkDir,
    string ExecutablePath);

/// <summary>
/// Background update flow: check → download → verify → unpack to staging → <see cref="UpdateStatus.Ready"/>.
/// <see cref="PrepareInstall"/> then writes the swap script for the presentation layer to launch before quitting.
/// Status properties are safe to poll from another thread.
/// </summary>
public sealed class Updater
{
    private const string StagedFile = "staged.json";
    private const string LastRunFile = "last-version.txt";
    private readonly UpdateConfig _config;
    private readonly UpdateClient _client;
    private readonly object _gate = new();
    private UpdateStatus _status = UpdateStatus.Idle;
    private double _progress;
    private string? _error;
    private UpdateManifest? _available;

    public Updater(UpdateConfig config, HttpClient http)
    {
        _config = config;
        _client = new UpdateClient(http, new UpdateVerifier(config.PublicKeyPem), config.FeedBase);
        Directory.CreateDirectory(config.WorkDir);
    }

    public UpdateStatus Status { get { lock (_gate) return _status; } }
    public double Progress { get { lock (_gate) return _progress; } }
    public string? Error { get { lock (_gate) return _error; } }
    public UpdateManifest? Available { get { lock (_gate) return _available; } }

    private string StagingDir(SemVer v) => Path.Combine(_config.WorkDir, "staged-" + v);
    private string ArchivePath(SemVer v) => Path.Combine(_config.WorkDir, $"download-{v}.pkg");

    /// <summary>
    /// The version that ran last time, if it was older than this one: lets the UI say "updated to vX".
    /// Also records the current version and removes leftovers of updates that are now installed.
    /// </summary>
    public SemVer? ConsumeJustUpdatedFrom()
    {
        string file = Path.Combine(_config.WorkDir, LastRunFile);
        SemVer? previous = File.Exists(file) && SemVer.TryParse(File.ReadAllText(file), out var p) ? p : null;
        File.WriteAllText(file, _config.CurrentVersion.ToString());
        CleanUpInstalled();
        return previous is { } prev && _config.CurrentVersion > prev ? prev : null;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        try
        {
            if (TryResumeStaged()) return;
            Set(UpdateStatus.Checking);
            var manifest = await _client.FetchManifestAsync(ct);
            if (!(manifest.Version > _config.CurrentVersion) || !manifest.Packages.TryGetValue(_config.Platform, out var package))
            {
                Set(UpdateStatus.UpToDate);
                return;
            }
            lock (_gate) _available = manifest;

            var target = InstallTarget.Locate(_config.Platform, _config.ExecutablePath);
            if (target is null || !target.IsWritable())
            {
                Set(UpdateStatus.ManualRequired);
                return;
            }

            Set(UpdateStatus.Downloading);
            string archive = ArchivePath(manifest.Version);
            await _client.DownloadAsync(package, archive, new Progress<double>(p => { lock (_gate) _progress = p; }), ct);
            string staged = StagingDir(manifest.Version);
            await Task.Run(() => UpdateArchive.Extract(_config.Platform, archive, staged), ct);
            File.Delete(archive);
            File.WriteAllText(Path.Combine(_config.WorkDir, StagedFile), JsonSerializer.Serialize(new StagedInfo(manifest.Version.ToString(), manifest.Notes, staged)));
            Set(UpdateStatus.Ready);
        }
        catch (OperationCanceledException)
        {
            Set(UpdateStatus.Idle);
        }
        catch (Exception ex) when (ex is UpdateException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            lock (_gate)
            {
                _status = UpdateStatus.Failed;
                _error = ex.Message;
            }
        }
    }

    /// <summary>Writes the swap script; returns the program and arguments to start (detached) just before quitting.</summary>
    public (string Program, string[] Arguments, string ScriptPath) PrepareInstall(int pid)
    {
        var manifest = Available ?? throw new InvalidOperationException("No update is ready.");
        if (Status != UpdateStatus.Ready) throw new InvalidOperationException("No update is ready.");
        var target = InstallTarget.Locate(_config.Platform, _config.ExecutablePath)
            ?? throw new UpdateException("Cannot locate the installed game.");
        string staged = StagingDir(manifest.Version);
        UpdateArchive.ValidateLayout(_config.Platform, staged);
        string script = Path.Combine(_config.WorkDir, SwapScript.FileName(_config.Platform));
        string log = Path.Combine(_config.WorkDir, "update.log");
        File.WriteAllText(script, SwapScript.Build(target, staged, pid, log));
        if (_config.Platform == UpdatePlatform.Windows)
            return ("cmd.exe", new[] { "/c", script }, script);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return ("/bin/sh", new[] { script }, script);
    }

    private bool TryResumeStaged()
    {
        string file = Path.Combine(_config.WorkDir, StagedFile);
        if (!File.Exists(file)) return false;
        try
        {
            var info = JsonSerializer.Deserialize<StagedInfo>(File.ReadAllText(file));
            if (info is null || !SemVer.TryParse(info.Version, out var v) || !(v > _config.CurrentVersion)) return false;
            UpdateArchive.ValidateLayout(_config.Platform, info.Path);
            lock (_gate)
            {
                _available = new UpdateManifest(v, info.Notes, new Dictionary<UpdatePlatform, UpdatePackage>());
                _status = UpdateStatus.Ready;
            }
            return true;
        }
        catch (Exception ex) when (ex is JsonException or UpdateException or IOException)
        {
            File.Delete(file); // stale or damaged: check the feed again
            return false;
        }
    }

    private void CleanUpInstalled()
    {
        foreach (var dir in Directory.EnumerateDirectories(_config.WorkDir, "staged-*"))
        {
            if (SemVer.TryParse(Path.GetFileName(dir)["staged-".Length..], out var v) && !(v > _config.CurrentVersion))
                TryDelete(dir);
        }
        foreach (var file in Directory.EnumerateFiles(_config.WorkDir, "download-*")) TryDelete(file);
        string staged = Path.Combine(_config.WorkDir, StagedFile);
        if (File.Exists(staged) && !StagedIsNewer(staged)) TryDelete(staged);
    }

    private bool StagedIsNewer(string file)
    {
        try
        {
            var info = JsonSerializer.Deserialize<StagedInfo>(File.ReadAllText(file));
            return info is not null && SemVer.TryParse(info.Version, out var v) && v > _config.CurrentVersion;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            else if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Leftovers are harmless; they will be retried next launch.
        }
    }

    private void Set(UpdateStatus status)
    {
        lock (_gate) _status = status;
    }

    private sealed record StagedInfo(string Version, string Notes, string Path);
}
