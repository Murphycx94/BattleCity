using Godot;
using Tank.Client.Bootstrap;
using Tank.Client.Cameras;
using Tank.Sim;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;

namespace Tank.Client.Sandbox;

/// <summary>
/// The sandbox's scripted runs, for repeatable screenshots and frame-time logs. A script drives instead of the player
/// with --range-demo (lay the gun on a target and fire), --autodrive (drive loops, always firing) or a fixed
/// --camera-preset (stand still); a range demo that fires once stops after the player's first shot. The timetable (the
/// screenshot's moment and delay, the --fx-shot warm-up, --quit-after) is a <see cref="ShotPlan"/> and the screenshot,
/// fps log and quit themselves a <see cref="CaptureRun"/>, both shared with the match.
/// Moments and the delays after them are wall-clock seconds (the screenshot is of what is on screen); only a range
/// demo's first shot is timed in simulation ticks (<see cref="RangeDemo.FireTick"/>), so the shot itself is repeatable.
/// </summary>
public sealed class SandboxAutomation
{
    private const float FxShotPitch = -0.1f;        // --fx-shot: gun dipped so shells land about 40 m ahead
    private const float DemoCameraPitch = 0.1f;     // --range-demo: looking over the tank at the target
    private const float AutoCameraYaw = 0.35f;      // --autodrive: the camera looks this far off the hull's heading, rad
    private const float AutoCameraPitch = 0.18f;
    private const double AutoTurnRate = 0.4;        // --autodrive: steers right while sin(rate × time) is above the threshold
    private const double AutoTurnThreshold = 0.5;

    private readonly LaunchOptions _options;
    private readonly bool _standStill;
    private readonly MapSite _site;
    private readonly ShotPlan _plan;
    private readonly CaptureRun _capture;

    /// <param name="host">The sandbox node: its viewport is captured and measured, its tree quit.</param>
    /// <param name="standStill">A fixed camera preset is on: nobody drives.</param>
    /// <param name="site">The map driven on: its presets and range demos are the ones that work.</param>
    public SandboxAutomation(LaunchOptions options, Node host, bool standStill, MapSite site)
    {
        (_options, _standStill, _site) = (options, standStill, site);
        Demo = site.FindDemo(options.RangeDemo);
        _plan = ShotPlan.ForSandbox(options);
        _capture = new CaptureRun(options, host, _plan);
    }

    /// <summary>The scripted range demo, or null.</summary>
    public RangeDemo? Demo { get; }

    /// <summary>
    /// Warns about launch options that name no preset or demo known on this map or wait for a moment the sandbox never reaches, and —
    /// with <paramref name="unreadOptions"/>, when the sandbox parsed the arguments itself — the ones that couldn't be read
    /// (under Main, Main has already said those).
    /// </summary>
    public void ReportBadOptions(bool unreadOptions)
    {
        if (unreadOptions) foreach (string warning in _options.Warnings) GD.PushWarning(warning);
        foreach (string warning in _plan.Warnings) GD.PushWarning(warning);
        string map = _site.Map.Name;
        if (_options.CameraPreset is { } preset && !CameraPresets.IsKnownFor(preset, _site))
            GD.PushWarning($"Unknown --camera-preset '{preset}' on {map}; use {string.Join(", ", _site.PresetNames)}.");
        if (_options.RangeDemo is { } demo && Demo is null)
            GD.PushWarning($"Unknown --range-demo '{demo}' on {map}; use {string.Join(", ", _site.DemoNames)}.");
    }

    /// <summary>Notes the first time the player fired, hit or destroyed a tank, hurt a wall and destroyed a prop.</summary>
    public void OnEvent(in GameEvent e, int playerId, double time) => _plan.OnEvent(e, playerId, time);

    /// <summary>The player's command for the next tick: <paramref name="player"/>'s own, or a script's in its place.</summary>
    public InputCmd Command(in InputCmd player, in TankState tank, Battlefield field, double time)
    {
        InputCmd command = player;
        if (Demo is { } demo) command = demo.Command(player, tank, field, hasFired: _plan.Seen(ShotMoment.Fire));
        else if (_options.AutoDrive) command = AutoDrive(player, time);
        else if (_standStill) command = player with { Throttle = 0, Steer = 0, Fire = false };
        if (_options.AutoDrive && _options.FxShotDelay is not null) command = command with { AimPitch = FxShotPitch };   // shells land in view
        return command;
    }

    /// <summary>Where a scripted run points the orbit camera, or null to leave it to the player.</summary>
    public (float Yaw, float Pitch)? CameraAngles(float tankYaw)
    {
        if (_options.AutoDrive) return (tankYaw + AutoCameraYaw, AutoCameraPitch);
        if (Demo is { } demo) return (demo.Bearing, DemoCameraPitch);
        return null;
    }

    /// <summary>Call at the end of every frame: logs render costs, takes the screenshot when due and quits when time is up.</summary>
    public void EndFrame(double delta, double time) => _capture.EndFrame(delta, time);

    /// <summary>--autodrive: full throttle, steering right part of the time (so it drives loops), always firing.</summary>
    private static InputCmd AutoDrive(in InputCmd player, double time) => player with
    {
        Throttle = 1,
        Steer = (sbyte)(Math.Sin(time * AutoTurnRate) > AutoTurnThreshold ? 1 : 0),
        Fire = true,
    };
}
