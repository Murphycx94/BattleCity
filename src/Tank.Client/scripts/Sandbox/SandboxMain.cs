using Godot;
using Tank.Client.Bootstrap;
using Tank.Client.Cameras;
using Tank.Client.Combat;
using Tank.Client.Overlay;
using Tank.Client.Vehicles;
using Tank.Client.World;
using Tank.Sim;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;
using SN = System.Numerics;

namespace Tank.Client.Sandbox;

/// <summary>
/// Stage-1 offline test drive: runs Tank.Sim locally at 30Hz and renders one tick behind, interpolated.
/// Launch: godot-mono --path src/Tank.Client res://scenes/Sandbox.tscn [-- --autodrive --screenshot FILE --quit-after S]
/// </summary>
public partial class SandboxMain : Node3D
{
    private const double MaxCatchUp = 0.25;
    private const float AimRange = 900f;
    private const float SniperEyeAboveTrunnion = 0.45f;
    private const float MinAimDistance = 8f;

    private readonly SandboxInput _input = new();
    private readonly InputCmd[] _commands = new InputCmd[1];
    private LaunchOptions _options = new();
    private Simulation _sim = null!;
    private TankState _previous;
    private TankState _current;
    private OrbitCamera _camera = null!;
    private TankModel _tank = null!;
    private ShellViews _shells = null!;
    private BattleFx _fx = null!;
    private Reticle _reticle = null!;
    private double _accumulator;
    private double _time;
    private float _aimYaw;
    private float _aimPitch;
    private bool _screenshotTaken;

    public override void _Ready()
    {
        _options = LaunchOptions.Parse(OS.GetCmdlineUserArgs());
        GetWindow().Title = "坦克 3D · 试驾场";
        SandboxInput.RegisterActions();

        _sim = new Simulation(SandboxMap.Create());
        _sim.AddTank(TankCatalog.Medium, SandboxMap.Spawn, SandboxMap.SpawnYaw);
        _previous = _current = _sim.Tanks[0];
        _aimYaw = _current.Yaw;

        AddChild(Scenery.CreateEnvironment());
        AddChild(Scenery.CreateSun());
        var terrain = new TerrainMesh();
        AddChild(terrain);
        terrain.Build(_sim.Field.Terrain);
        var props = new PropMeshes();
        AddChild(props);
        props.Build(_sim.Field.Props);
        _tank = new TankModel();
        AddChild(_tank);
        _tank.Build(TankCatalog.Medium);
        _shells = new ShellViews();
        AddChild(_shells);
        _fx = new BattleFx();
        AddChild(_fx);
        _camera = new OrbitCamera();
        AddChild(_camera);
        _camera.SetAngles(_current.Yaw, 0.25f);
        var hud = new CanvasLayer();
        AddChild(hud);
        _reticle = new Reticle();
        hud.AddChild(_reticle);

        if (!_options.AutoDrive) Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public override void _Input(InputEvent e)
    {
        // Releases always pass so a held fire can end; presses only count while captured (the capturing click must not shoot).
        if (e.IsActionReleased("fire_gun") || Input.MouseMode == Input.MouseModeEnum.Captured) _input.OnInputEvent(e);
    }

    public override void _Notification(int what)
    {
        if (what != NotificationApplicationFocusOut) return;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _input.ReleaseAll();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        bool captured = Input.MouseMode == Input.MouseModeEnum.Captured;
        switch (e)
        {
            case InputEventMouseMotion motion when captured:
                _camera.Look(motion.ScreenRelative);
                break;
            case InputEventMouseButton { Pressed: true } wheel when captured && wheel.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown:
                _camera.Scroll(wheel.ButtonIndex == MouseButton.WheelUp ? 1 : -1);
                break;
            case InputEventMouseButton { Pressed: true } when !captured:
                Input.MouseMode = Input.MouseModeEnum.Captured;
                GetViewport().SetInputAsHandled();
                break;
        }
        if (e.IsActionPressed("sniper_toggle")) _camera.ToggleSniper();
        if (e.IsActionPressed("ui_cancel")) Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    public override void _Process(double delta)
    {
        _time += delta;
        _accumulator = Math.Min(_accumulator + delta, MaxCatchUp);
        while (_accumulator >= SimConstants.Dt)
        {
            _accumulator -= SimConstants.Dt;
            StepSimulation();
        }

        float alpha = (float)(_accumulator / SimConstants.Dt);
        TankState tank = Interpolate(_previous, _current, alpha);
        TankPose pose = TankPose.Compute(tank, _sim.Field.Terrain);
        _tank.Apply(pose, tank.TurretYaw, tank.GunPitch);
        _tank.Visible = !_camera.Sniper;
        if (_options.AutoDrive) _camera.SetAngles(tank.Yaw + 0.35f, 0.18f);
        _camera.Follow(pose.Position.ToGodot(), (pose.Trunnion(tank.Spec) + pose.Up * SniperEyeAboveTrunnion).ToGodot(), _sim.Field.Terrain);
        if (!_input.FreeLook) UpdateAim(pose, tank.Spec);
        _shells.Sync(_sim.Shells, alpha);
        UpdateReticle(tank);
        RunAutomation();
    }

    private void StepSimulation()
    {
        var (throttle, steer, fire) = _options.AutoDrive ? AutoDrive() : _input.Sample();
        _commands[0] = new InputCmd(_sim.Tick + 1, throttle, steer, _aimYaw, _aimPitch, fire);
        _sim.Step(_commands);
        foreach (GameEvent e in _sim.Events)
        {
            if (e.Type == GameEventType.ShellFired) _fx.Muzzle(e.Position.ToGodot());
            else if (e.Type == GameEventType.ShellExploded) _fx.Explosion(e.Position.ToGodot());
        }
        _previous = _current;
        _current = _sim.Tanks[0];
    }

    private (sbyte Throttle, sbyte Steer, bool Fire) AutoDrive() => (1, (sbyte)(Math.Sin(_time * 0.4) > 0.5 ? 1 : 0), true);

    /// <summary>Casts from the orbit focus along the camera's view; the turret then chases that point.</summary>
    private void UpdateAim(TankPose pose, TankSpec spec)
    {
        Camera3D cam = _camera.Camera;
        SN.Vector3 look = (-cam.GlobalBasis.Z).ToSim();
        SN.Vector3 origin = cam.GlobalPosition.ToSim() + look * (_camera.Sniper ? 0f : _camera.Distance);
        SN.Vector3 end = origin + look * AimRange;
        SN.Vector3 target = _sim.Field.Raycast(origin, end, out RayHit hit) ? hit.Point : end;
        SN.Vector3 trunnion = pose.Trunnion(spec);
        // Looking steeply down puts the target almost under the turret, where yaw swings wildly; keep it at least MinAimDistance out.
        SN.Vector2 flat = new(target.X - trunnion.X, target.Z - trunnion.Z);
        if (flat.Length() < MinAimDistance)
        {
            SN.Vector2 dir = new(look.X, look.Z);
            dir = dir.LengthSquared() > 1e-6f ? SN.Vector2.Normalize(dir) : Angles.Forward(_aimYaw);
            target = new SN.Vector3(trunnion.X + dir.X * MinAimDistance, target.Y, trunnion.Z + dir.Y * MinAimDistance);
        }
        (_aimYaw, _aimPitch) = Angles.AimAngles(target - trunnion, _aimYaw);
    }

    private void UpdateReticle(in TankState tank)
    {
        // Same launch maths the sim fires with, so the ring shows where the shot really goes (including a blocked barrel).
        Ballistics.Launch launch = Ballistics.Aim(tank, _sim.Field);
        SN.Vector3 impact = launch.BlockPoint;
        bool hasImpact = launch.Blocked || Ballistics.PredictImpact(launch.Origin, launch.Velocity, _sim.Field, out impact);
        Camera3D cam = _camera.Camera;
        Vector3 point = impact.ToGodot();
        Vector2? screen = !hasImpact || cam.IsPositionBehind(point) ? null : cam.UnprojectPosition(point);
        string reload = tank.Reload > 0 ? $"{tank.Reload:0.0} 秒" : "就绪";
        string zoom = _camera.Sniper ? $"   狙击 {_camera.Zoom:0}×" : "";
        _reticle.UpdateState(screen, 1f - tank.Reload / tank.Spec.ReloadTime,
            $"速度 {MathF.Abs(tank.Speed) * 3.6f:0} km/h   装填 {reload}{zoom}");
    }

    private static TankState Interpolate(in TankState a, in TankState b, float t) => b with
    {
        Position = SN.Vector2.Lerp(a.Position, b.Position, t),
        Yaw = Angles.Lerp(a.Yaw, b.Yaw, t),
        TurretYaw = Angles.Lerp(a.TurretYaw, b.TurretYaw, t),
        GunPitch = a.GunPitch + (b.GunPitch - a.GunPitch) * t,
        Speed = a.Speed + (b.Speed - a.Speed) * t,
    };

    private void RunAutomation()
    {
        if (_options.ScreenshotPath is { } path && !_screenshotTaken && _time >= _options.ScreenshotAt)
        {
            _screenshotTaken = true;
            Error error = GetViewport().GetTexture().GetImage().SavePng(path);
            GD.Print(error == Error.Ok ? $"Screenshot saved: {path}" : $"Screenshot failed ({error}): {path}");
        }
        if (_options.QuitAfter > 0 && _time >= _options.QuitAfter) GetTree().Quit();
    }
}
