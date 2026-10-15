using Godot;
using Tank.Client.Bootstrap;
using Tank.Client.Cameras;
using Tank.Client.Combat;
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
/// Offline test drive: runs Tank.Sim locally at 30Hz and renders one tick behind, interpolated. The player's tank is slot 0;
/// the practice targets (<see cref="TargetRange"/>) take the next slots. Keys: 1 AP, 2 HE, R restock. Scenery, HUD and
/// scripted runs live in <see cref="SandboxWorld"/>, <see cref="SandboxHud"/> and <see cref="SandboxAutomation"/>.
/// Launch: godot-mono --path src/Tank.Client res://scenes/Sandbox.tscn [-- --autodrive --camera-preset front|side|hill|spin|orbit|bush|target|...
/// --fps-log --sniper --range-demo pen|ricochet|nopen|he|kill --screenshot FILE --screenshot-at S --fx-shot S --shot-after fire|hit|kill:S --quit-after S]
/// </summary>
public partial class SandboxMain : Node3D
{
    private const double MaxCatchUp = 0.25;
    private const float AimRange = 900f;
    private const float SniperEyeAboveTrunnion = 0.45f;
    private const float MinAimDistance = 8f;
    private const float FireShake = 0.08f;
    private const float FireShakeSeconds = 0.15f;
    private const float ArmorHitSize = 0.5f;        // an AP shell striking armour: a small grey puff
    private const float GroundBurstHeight = 0.5f;   // a shell bursting higher than this over the ground hit a rock or a tank

    private readonly SandboxInput _input = new();
    private InputCmd[] _commands = Array.Empty<InputCmd>();
    private LaunchOptions _options = new();
    private SandboxAutomation _automation = null!;
    private Simulation _sim = null!;
    private Func<float, float, float> _groundAt = null!;   // cached: a method group would make a new delegate every frame
    private TankState _previous;
    private TankState _current;
    private OrbitCamera _camera = null!;
    private SandboxWorld _world = null!;
    private TankModel _tank = null!;
    private DustTrail _dust = null!;
    private bool _playerFired;
    private ShellViews _shells = null!;
    private TargetRange _targets = null!;
    private BattleFx _fx = null!;
    private SandboxHud _hud = null!;
    private double _accumulator;
    private double _time;
    private float _aimYaw;
    private float _aimPitch;

    public override void _Ready()
    {
        _options = LaunchOptions.Parse(OS.GetCmdlineUserArgs());
        GetWindow().Title = "坦克 3D · 试驾场";
        SandboxInput.RegisterActions();
        _automation = new SandboxAutomation(_options, this, standStill: HasPreset);

        RangeDemo? demo = _automation.Demo;
        _sim = new Simulation(SandboxMap.Create());
        _sim.AddTank(TankCatalog.Medium, demo?.PlayerPosition ?? CameraPresets.Spawn(_options.CameraPreset), demo?.PlayerYaw ?? SandboxMap.SpawnYaw);
        _previous = _current = _sim.Tanks[0];
        _targets = Add(new TargetRange());
        _targets.Build(_sim);
        _commands = new InputCmd[_sim.TankCount];
        _groundAt = _sim.Field.Terrain.HeightAt;
        _aimYaw = _current.Yaw;

        _world = Add(new SandboxWorld());
        _world.Build(_sim.Field, _current, CameraPresets.StreamsAroundCamera(_options.CameraPreset));
        _tank = Add(new TankModel());
        _tank.Build(TankCatalog.Medium);
        _dust = new DustTrail();
        _tank.AddChild(_dust);
        _dust.Build(TankLooks.For(TankCatalog.Medium.Id).TrackX, TankCatalog.Medium.HullLength / 2f);
        _shells = Add(new ShellViews());
        _fx = Add(new BattleFx());
        _camera = Add(new OrbitCamera());
        _camera.SetAngles(_current.Yaw, 0.25f);
        if (_options.Sniper) _camera.ToggleSniper();
        _hud = Add(new SandboxHud());
        _hud.Build(_camera, _targets, _sim.Field);

        _automation.ReportBadOptions();
        if (!_options.AutoDrive && !IgnoresInput) Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    private bool HasPreset => CameraPresets.IsKnown(_options.CameraPreset);

    /// <summary>Fixed-camera and scripted runs ignore stray input so screenshots stay repeatable.</summary>
    private bool IgnoresInput => HasPreset || _automation.Demo is not null;

    public override void _Input(InputEvent e)
    {
        if (IgnoresInput) return;
        // Releases always pass so a held fire can end; presses only count while captured (the capturing click must not shoot).
        if (e.IsActionReleased(SandboxInput.FireGun) || Input.MouseMode == Input.MouseModeEnum.Captured) _input.OnInputEvent(e);
    }

    public override void _Notification(int what)
    {
        if (what != NotificationApplicationFocusOut) return;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _input.ReleaseAll();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (IgnoresInput) return;
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
        if (e.IsActionPressed(SandboxInput.SniperToggle)) _camera.ToggleSniper();
        if (e.IsActionPressed(SandboxInput.UiCancel)) Input.MouseMode = Input.MouseModeEnum.Visible;
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
        ShowPlayer(tank, pose, (float)delta);
        UpdateCamera(tank, pose);
        _world.Update(tank, pose, _camera.Camera.GlobalPosition, (float)delta);
        _targets.Sync(_sim, (float)delta);
        _shells.Sync(_sim.Shells, alpha, (float)delta);
        _hud.Refresh(tank);
        _automation.EndFrame(delta, _time);
    }

    private void StepSimulation()
    {
        if (!HasPreset && _input.TakeRestock()) _sim.Restock(_current.Id);
        _targets.ReviveDue(_sim);
        var (throttle, steer, fire) = _input.Sample();
        var own = new InputCmd(_sim.Tick + 1, throttle, steer, _aimYaw, _aimPitch, fire, _input.Ammo);
        _commands[0] = _automation.Command(own, _current, _sim.Field, _time);   // a scripted run may drive and aim instead
        (_aimYaw, _aimPitch) = (_commands[0].AimYaw, _commands[0].AimPitch);
        _targets.FillInputs(_commands.AsSpan(1), _sim.Tick + 1);
        _sim.Step(_commands);
        foreach (GameEvent e in _sim.Events) OnEvent(e);
        _previous = _current;
        _current = _sim.Tanks[0];
    }

    private void OnEvent(in GameEvent e)
    {
        _automation.OnEvent(e, _current.Id, _time);
        switch (e.Type)
        {
            case GameEventType.ShellFired when e.TankId == _current.Id:
                // Flash at the visible barrel tip (the sim event keeps its own muzzle position): spawned after this frame's pose update.
                _playerFired = true;
                _tank.Fire(_current.TurretYaw);
                break;
            case GameEventType.ShellFired:
                _fx.Muzzle(new Transform3D(Basis.Identity, e.Position.ToGodot()));   // no barrel pose for other tanks yet
                break;
            case GameEventType.ShellExploded:
                // Shells that stop above the ground hit a rock or a tank: grey dust instead of white smoke.
                bool aboveGround = e.Position.Y > _sim.Field.Terrain.HeightAt(e.Position.X, e.Position.Z) + GroundBurstHeight;
                _fx.Explosion(e.Position.ToGodot(), aboveGround ? PuffColor.Grey : PuffColor.White);
                break;
            case GameEventType.TankHit when e.Ammo == AmmoType.AP:   // HE gets its own ShellExploded
                _fx.Explosion(e.Position.ToGodot(), PuffColor.Grey, ArmorHitSize);
                break;
        }
        _hud.OnEvent(e);
        _targets.OnEvent(e, _sim, _fx);
    }

    /// <summary>Poses and animates the player's tank, with the muzzle blast of a shot fired this frame; hidden in the sniper sight.</summary>
    private void ShowPlayer(in TankState tank, TankPose pose, float delta)
    {
        _tank.Apply(pose, tank.TurretYaw, tank.GunPitch);
        _tank.Animate(tank.Speed, Angles.Wrap(_current.Yaw - _previous.Yaw) / SimConstants.Dt, delta);
        _dust.Update(tank.Speed);
        if (_playerFired)
        {
            _fx.Muzzle(_tank.MuzzleTransform, withSmoke: !_camera.Sniper);   // own puffs would block the scope
            _camera.Shake(FireShake, FireShakeSeconds);
            _playerFired = false;
        }
        _tank.Visible = !_camera.Sniper;
    }

    /// <summary>Follows the tank (or looks from a fixed preset), then lays the gun where the camera looks unless a script aims it.</summary>
    private void UpdateCamera(in TankState tank, TankPose pose)
    {
        if (_automation.CameraAngles(tank.Yaw) is { } angles) _camera.SetAngles(angles.Yaw, angles.Pitch);
        _camera.Follow(pose.Position.ToGodot(), (pose.Trunnion(tank.Spec) + pose.Up * SniperEyeAboveTrunnion).ToGodot(), _sim.Field.Terrain);
        if (HasPreset)
        {
            var (eye, target) = CameraPresets.Get(_options.CameraPreset!, pose.Position.ToGodot(), tank.Yaw, _time, _groundAt);
            _camera.Camera.LookAtFromPosition(eye, target, Vector3.Up);
        }
        else if (_automation.Demo is null && !_input.FreeLook) UpdateAim(pose, tank.Spec);
    }

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

    private static TankState Interpolate(in TankState a, in TankState b, float t) => b with
    {
        Position = SN.Vector2.Lerp(a.Position, b.Position, t),
        Yaw = Angles.Lerp(a.Yaw, b.Yaw, t),
        TurretYaw = Angles.Lerp(a.TurretYaw, b.TurretYaw, t),
        GunPitch = a.GunPitch + (b.GunPitch - a.GunPitch) * t,
        Speed = a.Speed + (b.Speed - a.Speed) * t,
        Bloom = a.Bloom + (b.Bloom - a.Bloom) * t,
    };

    private T Add<T>(T node) where T : Node
    {
        AddChild(node);
        return node;
    }
}
