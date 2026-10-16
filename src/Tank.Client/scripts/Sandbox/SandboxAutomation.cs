using Godot;
using Tank.Client.Bootstrap;
using Tank.Client.Cameras;
using Tank.Sim;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;
using SN = System.Numerics;

namespace Tank.Client.Sandbox;

/// <summary>
/// The sandbox's scripted runs, for repeatable screenshots and frame-time logs. A script drives instead of the player
/// with --range-demo (lay the gun on a target and fire), --autodrive (drive loops, always firing) or a fixed
/// --camera-preset (stand still). Timed steps wait for a <see cref="Moment"/>, each noted the first time it happens:
/// the screenshot is taken a delay after the run's start (--screenshot-at), the player's first shot (--fx-shot, or
/// --shot-after fire) or the first tank hit or kill (--shot-after hit|kill), and a range demo that fires once stops
/// after the player's first shot. With --fx-shot, nothing before a warm-up counts. Also --fps-log and --quit-after.
/// Moments and the delays after them are wall-clock seconds (the screenshot is of what is on screen); only a range
/// demo's first shot is timed in simulation ticks (<see cref="RangeDemo.FireTick"/>), so the shot itself is repeatable.
/// </summary>
public sealed class SandboxAutomation
{
    private const double FxShotWarmup = 3;          // --fx-shot: moments before this many seconds don't count
    private const float FxShotPitch = -0.1f;        // --fx-shot: gun dipped so shells land about 40 m ahead
    private const float DemoCameraPitch = 0.1f;     // --range-demo: looking over the tank at the target
    private const float AutoCameraYaw = 0.35f;      // --autodrive: the camera looks this far off the hull's heading, rad
    private const float AutoCameraPitch = 0.18f;
    private const double AutoTurnRate = 0.4;        // --autodrive: steers right while sin(rate × time) is above the threshold
    private const double AutoTurnThreshold = 0.5;

    /// <summary>What a timed step waits for.</summary>
    private enum Moment { Start, Fire, Hit, Kill }

    private readonly LaunchOptions _options;
    private readonly Node _host;
    private readonly bool _standStill;
    private readonly double _countFrom;
    private readonly double[] _firstSeen = { 0, -1, -1, -1 };   // per Moment: when it first happened, s; -1 not yet
    private readonly Moment _shotAfter;
    private readonly double _shotDelay;
    private readonly FrameTimeLog _frameLog = new();
    private bool _screenshotTaken;

    /// <param name="host">The sandbox node: its viewport is captured and measured, its tree quit.</param>
    /// <param name="standStill">A fixed camera preset is on: nobody drives.</param>
    public SandboxAutomation(LaunchOptions options, Node host, bool standStill)
    {
        (_options, _host, _standStill) = (options, host, standStill);
        Demo = RangeDemo.Find(options.RangeDemo);
        _countFrom = options.FxShotDelay is null ? 0 : FxShotWarmup;
        (_shotAfter, _shotDelay) = PlanScreenshot(options);
        if (options.FpsLog) RenderingServer.ViewportSetMeasureRenderTime(host.GetViewport().GetViewportRid(), true);
    }

    /// <summary>The scripted range demo, or null.</summary>
    public RangeDemo? Demo { get; }

    /// <summary>Warns about launch options that couldn't be read or name no known preset or demo.</summary>
    public void ReportBadOptions()
    {
        foreach (string warning in _options.Warnings) GD.PushWarning(warning);
        if (_options.CameraPreset is { } preset && !CameraPresets.IsKnown(preset))
            GD.PushWarning($"Unknown --camera-preset '{preset}'; use front, side, close, closerear, sprocket, hill, spin, orbit, bush or target.");
        if (_options.RangeDemo is { } demo && Demo is null)
            GD.PushWarning($"Unknown --range-demo '{demo}'; use {string.Join(", ", RangeDemo.Names)}.");
    }

    /// <summary>Notes the first time the player fired, a tank was hit and a tank was destroyed.</summary>
    public void OnEvent(in GameEvent e, int playerId, double time)
    {
        Moment? moment = e.Type switch
        {
            GameEventType.ShellFired when e.TankId == playerId => Moment.Fire,
            GameEventType.TankHit => Moment.Hit,
            GameEventType.TankDestroyed => Moment.Kill,
            _ => null,
        };
        if (moment is { } m && time >= _countFrom && _firstSeen[(int)m] < 0) _firstSeen[(int)m] = time;
    }

    /// <summary>The player's command for the next tick: <paramref name="player"/>'s own, or a script's in its place.</summary>
    public InputCmd Command(in InputCmd player, in TankState tank, Battlefield field, double time)
    {
        InputCmd command = player;
        if (Demo is { } demo) command = DemoCommand(demo, player, tank, field);
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
    public void EndFrame(double delta, double time)
    {
        if (_options.FpsLog) LogFrame(delta);
        if (_options.ScreenshotPath is { } path && !_screenshotTaken && ScreenshotDue(time))
        {
            _screenshotTaken = true;
            Error error = _host.GetViewport().GetTexture().GetImage().SavePng(path);
            GD.Print(error == Error.Ok ? $"Screenshot saved: {path}" : $"Screenshot failed ({error}): {path}");
        }
        if (_options.QuitAfter > 0 && time >= _options.QuitAfter) _host.GetTree().Quit();
    }

    /// <summary>--shot-after waits for its event, --fx-shot for the player's first shot, otherwise --screenshot-at counts from the start.</summary>
    private static (Moment After, double Delay) PlanScreenshot(LaunchOptions options)
    {
        if (options.ShotAfterEvent is { } awaited)
            return (awaited switch { "fire" => Moment.Fire, "hit" => Moment.Hit, _ => Moment.Kill }, options.ShotAfterDelay);
        if (options.FxShotDelay is { } fxDelay) return (Moment.Fire, fxDelay);
        return (Moment.Start, options.ScreenshotAt);
    }

    private bool ScreenshotDue(double time)
    {
        double since = _firstSeen[(int)_shotAfter];
        return since >= 0 && time >= since + _shotDelay;
    }

    /// <summary>--range-demo: stand still, lay the gun on the demo's aim point, fire once (or keep firing) from <see cref="RangeDemo.FireTick"/> on.</summary>
    private InputCmd DemoCommand(RangeDemo demo, in InputCmd player, in TankState tank, Battlefield field)
    {
        TankPose pose = TankPose.Compute(tank, field.Terrain);
        SN.Vector2 spot = demo.Spot.Position;
        SN.Vector3 aim = demo.AimPoint(field.Terrain.HeightAt(spot.X, spot.Y));
        var (yaw, pitch) = Angles.AimAngles(aim - pose.Trunnion(tank.Spec), player.AimYaw);
        bool fire = player.Tick >= RangeDemo.FireTick && (demo.KeepFiring || _firstSeen[(int)Moment.Fire] < 0);
        return player with { Throttle = 0, Steer = 0, AimYaw = yaw, AimPitch = pitch, Fire = fire, Ammo = demo.Ammo };
    }

    /// <summary>--autodrive: full throttle, steering right part of the time (so it drives loops), always firing.</summary>
    private static InputCmd AutoDrive(in InputCmd player, double time) => player with
    {
        Throttle = 1,
        Steer = (sbyte)(Math.Sin(time * AutoTurnRate) > AutoTurnThreshold ? 1 : 0),
        Fire = true,
    };

    private void LogFrame(double delta)
    {
        Rid rid = _host.GetViewport().GetViewportRid();
        string? line = _frameLog.Add(delta,
            RenderingServer.ViewportGetMeasuredRenderTimeCpu(rid), RenderingServer.ViewportGetMeasuredRenderTimeGpu(rid),
            Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0,
            (long)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame),
            (long)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalPrimitivesInFrame));
        if (line is not null) GD.Print(line);
    }
}
