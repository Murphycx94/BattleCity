using Godot;
using HttpClient = System.Net.Http.HttpClient;
using Tank.Update;

namespace Tank.Client.Update;

/// <summary>
/// Godot side of online update: wires the engine (version, paths, process spawning) to <see cref="Updater"/>,
/// which does the real work on a background task. Only active in exported builds.
/// </summary>
public sealed class UpdateService : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly HttpClient _http;

    private UpdateService(Updater updater, SemVer current, HttpClient http)
    {
        Updater = updater;
        Current = current;
        _http = http;
        JustUpdatedFrom = updater.ConsumeJustUpdatedFrom();
    }

    public Updater Updater { get; }
    public SemVer Current { get; }
    public SemVer? JustUpdatedFrom { get; }

    public static string CurrentVersionText() =>
        ProjectSettings.GetSetting("application/config/version", "dev").AsString();

    /// <summary>Null in the editor / source runs, on unsupported platforms, or when disabled with --no-update.</summary>
    public static UpdateService? TryCreate(bool disabled)
    {
        if (disabled || !OS.HasFeature("template")) return null;
        if (!SemVer.TryParse(CurrentVersionText(), out var current) || UpdatePlatformNames.Current() is not { } platform) return null;

        string feed = System.Environment.GetEnvironmentVariable(UpdateDefaults.FeedOverrideVariable) is { Length: > 0 } custom
            ? custom
            : UpdateDefaults.FeedUrl;
        if (!Uri.TryCreate(feed, UriKind.Absolute, out var feedUri)) return null;

        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"Tank3D/{current}");
        var config = new UpdateConfig(feedUri, UpdateDefaults.PublicKeyPem, current, platform,
            ProjectSettings.GlobalizePath("user://updates"), OS.GetExecutablePath());
        try
        {
            return new UpdateService(new Updater(config, http), current, http);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            GD.PushWarning($"Online update disabled: {ex.Message}");
            http.Dispose();
            return null;
        }
    }

    public void Start() => _ = Task.Run(() => Updater.RunAsync(_cts.Token));

    /// <summary>Launches the swap helper. The caller must quit right after a true result.</summary>
    public bool LaunchInstaller(out string error)
    {
        error = string.Empty;
        try
        {
            var (program, args, _) = Updater.PrepareInstall(OS.GetProcessId());
            int pid = OS.CreateProcess(program, args);
            if (pid > 0) return true;
            error = "Could not start the updater.";
        }
        catch (Exception ex) when (ex is UpdateException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
        }
        return false;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _http.Dispose();
        _cts.Dispose();
    }
}
