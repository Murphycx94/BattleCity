using Godot;
using Tank.Client.Bootstrap;
using Tank.Client.Cameras;
using Tank.Client.Sandbox;
using Tank.Net.Battle;
using Tank.Sim;

namespace Tank.Client.Match;

/// <summary>
/// A match's scripted runs, for repeatable screenshots and frame-time logs (blueprint §5 T9): a fixed
/// <c>--camera-preset</c> (nobody drives), <c>--autodrive</c> (full throttle, steering right part of the time so it
/// drives loops, firing whenever the gun is roughly level), and the shared capture pieces — the timetable is a
/// <see cref="ShotPlan"/> (<c>--screenshot-at</c> counts from the match scene's creation, <c>--shot-after</c> waits for
/// <c>fire|hit|kill|death|ended</c>, <c>--quit-after</c> quits) and a <see cref="CaptureRun"/> executes it, with
/// <c>--fps-log</c>'s once-a-second line (a screenshot due while the map is still baking waits for its picture), and <c>--full-map</c> (the full-screen map opened once the scene is up); a
/// screenshot Main or an earlier match already took is not taken again. Any of these makes the run ignore the mouse and
/// keyboard. The moments come
/// from the scene: the server's events (<see cref="OnEvent"/>), the predicted own shot (<see cref="NotifyFired"/>: an
/// own shot never reaches the event queue), the own wreck state (<see cref="NotifyDeath"/>, which beats the late
/// destroy event) and the results screen (<see cref="NotifyEnded"/>). Times are the match clock — wall seconds, or the
/// virtual clock of a loopback run.
/// </summary>
public sealed class MatchAutomation
{
    private const float AutoCameraYaw = 0.35f;      // --autodrive: the camera looks this far off the hull's heading, rad
    private const float AutoCameraPitch = 0.18f;
    private const double AutoTurnRate = 0.4;        // --autodrive: steers right while sin(rate × time) is above the threshold
    private const double AutoTurnThreshold = 0.5;
    private const float LevelPitch = 0.2f;          // --autodrive: fires while the aim is within this of level, rad

    private readonly LaunchOptions _options;
    private readonly MapSite _site;
    private readonly ShotPlan _plan;
    private CaptureRun? _capture;   // built in Start: the fps log needs the host's viewport, so the host must be in the tree
    private bool _fullMapPending;   // --full-map: not yet opened

    /// <param name="screenshotTaken">The run's screenshot is already taken (by Main in the room, or by an earlier match): none here.</param>
    /// <param name="site">The match's map site, whose presets <c>--camera-preset</c> may name; null for the sandbox's.</param>
    public MatchAutomation(LaunchOptions options, bool screenshotTaken, MapSite? site = null)
    {
        _options = options;
        _site = site ?? MapSites.Sandbox;
        _plan = ShotPlan.ForMatch(options);
        if (screenshotTaken) _plan.MarkScreenshotTaken();
        HasPreset = CameraPresets.IsKnownFor(options.CameraPreset, _site);
        _fullMapPending = options.FullMap;
    }

    /// <summary>A <c>--camera-preset</c> known on the match's map is on: the camera is fixed and the tank stands still.</summary>
    public bool HasPreset { get; }

    public string? Preset => _options.CameraPreset;

    /// <summary>Whether a script drives the tank (<c>--autodrive</c>).</summary>
    public bool Drives => _options.AutoDrive;

    /// <summary>Whether the run's screenshot is spent (taken here, or before this match), for the owner to hand on to the next match.</summary>
    public bool ScreenshotTaken => _plan.ScreenshotTaken;

    /// <summary>Scripted and captured runs ignore stray input so screenshots stay repeatable.</summary>
    public bool IgnoresInput => HasPreset || _options.AutoDrive || _options.ScreenshotPath is not null || _options.QuitAfter > 0;

    /// <summary>Call from the scene's <c>_Ready</c> (the host now in the tree): the screenshot, fps log and quit arm here.</summary>
    public void Start(Node host) => _capture = new CaptureRun(_options, host, _plan);

    /// <summary>Warns about launch options that name no preset of the match's map or wait for a moment a match never reaches.</summary>
    public void ReportBadOptions()
    {
        foreach (string warning in _plan.Warnings) GD.PushWarning(warning);
        if (_options.CameraPreset is { } preset && !HasPreset)
            GD.PushWarning($"Unknown --camera-preset '{preset}' on {_site.Map.Name}; use {string.Join(", ", _site.PresetNames)}.");
    }

    /// <summary>Notes the first server event of each kind by (or destroying) the own tank.</summary>
    public void OnEvent(in GameEvent e, int ownTankId, double time) => _plan.OnEvent(e, ownTankId, time);

    /// <summary>The own tank fired (seen in the predicted shot count; a predicted shot never reaches the event queue).</summary>
    public void NotifyFired(double time) => _plan.OnMoment(ShotMoment.Fire, time);

    /// <summary>The own tank became a wreck (the state beats the destroy event by about the interpolation delay).</summary>
    public void NotifyDeath(double time) => _plan.OnMoment(ShotMoment.Death, time);

    /// <summary>The results screen came up.</summary>
    public void NotifyEnded(double time) => _plan.OnMoment(ShotMoment.Ended, time);

    /// <summary>The command for this frame: <paramref name="player"/>'s own, or the script's in its place.</summary>
    public LocalCommand Command(in LocalCommand player, double time)
    {
        if (_options.AutoDrive)
            return player with
            {
                Throttle = 1,
                Steer = (sbyte)(Math.Sin(time * AutoTurnRate) > AutoTurnThreshold ? 1 : 0),
                Fire = Mathf.Abs(player.AimPitch) < LevelPitch,
            };
        if (HasPreset) return player with { Throttle = 0, Steer = 0, Fire = false };
        return player;
    }

    /// <summary>Where a scripted run points the orbit camera, or null to leave it to the player.</summary>
    public (float Yaw, float Pitch)? CameraAngles(float tankYaw) =>
        _options.AutoDrive ? (tankYaw + AutoCameraYaw, AutoCameraPitch) : null;

    /// <summary><c>--full-map</c>: true once, the first frame the scene shows its map, for the scene to open the full one.</summary>
    public bool TakeFullMap()
    {
        if (!_fullMapPending) return false;
        _fullMapPending = false;
        return true;
    }

    /// <summary>Whether a due screenshot is held for the map's picture: Main's quit, on the process clock, waits for it.</summary>
    public bool HoldingScreenshot => _capture is { HoldingScreenshot: true };

    /// <summary>
    /// Call once per frame, last: logs render costs, takes the screenshot when due — held while <paramref name="mapReady"/>
    /// is false (the map still baking), up to <see cref="ShotPlan.MapWaitSeconds"/> — and quits when time is up.
    /// </summary>
    public void EndFrame(double delta, double time, bool mapReady) => _capture?.EndFrame(delta, time, mapReady);
}
