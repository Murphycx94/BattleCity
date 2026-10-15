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
/// Offline test drive: runs Tank.Sim locally at 30Hz and renders one tick behind, interpolated. The player's tank is slot 0;
/// the practice targets (<see cref="TargetRange"/>) take the next slots. Keys: 1 AP, 2 HE, R restock.
/// Launch: godot-mono --path src/Tank.Client res://scenes/Sandbox.tscn [-- --autodrive --camera-preset front|side|hill|spin|orbit|bush|target|...
/// --fps-log --sniper --range-demo pen|ricochet|nopen|he|kill --screenshot FILE --screenshot-at S --fx-shot S --shot-after fire|hit|kill:S --quit-after S]
/// </summary>
public partial class SandboxMain : Node3D
{
    private const double MaxCatchUp = 0.25;
    private const float AimRange = 900f;
    private const float SniperEyeAboveTrunnion = 0.45f;
    private const float MinAimDistance = 8f;
    private const float GrassCameraLean = 0.4f;   // grass focus sits this fraction of the way from tank to camera
    private const float FireShake = 0.08f;
    private const float FireShakeSeconds = 0.15f;
    private const float FootprintMargin = 0.15f;   // grass lies flat this far beyond the hull and tracks
    private const double FxShotWarmup = 3;          // --fx-shot: the first shot after this many seconds starts the clock
    private const float FxShotPitch = -0.1f;        // --fx-shot: gun dipped so shells land about 40 m ahead
    private const float ArmorHitSize = 0.5f;        // an AP shell striking armour: a small grey puff
    private const float DemoCameraPitch = 0.1f;     // --range-demo: looking over the tank at the target

    private readonly SandboxInput _input = new();
    private readonly InputCmd[] _commands = new InputCmd[1 + SandboxMap.TargetSpots.Count];
    private readonly FrameTimeLog _frameLog = new();
    private LaunchOptions _options = new();
    private Simulation _sim = null!;
    private TankState _previous;
    private TankState _current;
    private OrbitCamera _camera = null!;
    private TankModel _tank = null!;
    private GrassField _grass = null!;
    private BushField _bushes = null!;
    private DustTrail _dust = null!;
    private bool _playerFired;
    private ShellViews _shells = null!;
    private TargetRange _targets = null!;
    private BattleFx _fx = null!;
    private Reticle _reticle = null!;
    private StatusPanel _status = null!;
    private SniperVignette _vignette = null!;
    private double _accumulator;
    private double _time;
    private float _aimYaw;
    private float _aimPitch;
    private bool _screenshotTaken;
    private double _firstShotTime = -1;
    private RangeDemo? _demo;
    private bool _demoFired;
    private double _triggerTime = -1;   // --shot-after: when the awaited event happened

    public override void _Ready()
    {
        _options = LaunchOptions.Parse(OS.GetCmdlineUserArgs());
        GetWindow().Title = "坦克 3D · 试驾场";
        SandboxInput.RegisterActions();

        _demo = RangeDemo.Find(_options.RangeDemo);
        _sim = new Simulation(SandboxMap.Create());
        _sim.AddTank(TankCatalog.Medium, _demo?.PlayerPosition ?? CameraPresets.Spawn(_options.CameraPreset), _demo?.PlayerYaw ?? SandboxMap.SpawnYaw);
        _previous = _current = _sim.Tanks[0];
        _targets = new TargetRange();
        AddChild(_targets);
        _targets.Build(_sim);
        _aimYaw = _current.Yaw;

        AddChild(Scenery.CreateEnvironment());
        AddChild(Scenery.CreateSun());
        Scenery.ConfigureViewport(GetViewport());
        var terrain = new TerrainMesh();
        AddChild(terrain);
        var dirt = new DirtMask(_sim.Field.Terrain.SizeX, _sim.Field.Terrain.SizeZ, SandboxMap.BushPatches, SandboxMap.Seed);
        terrain.Build(_sim.Field.Terrain, dirt);
        var rocks = new RockMeshes();
        AddChild(rocks);
        rocks.Build(_sim.Field.Props, _sim.Field.Terrain);
        _bushes = new BushField();
        AddChild(_bushes);
        _bushes.Build(_sim.Field.Props, _sim.Field.Terrain);
        _grass = new GrassField();
        AddChild(_grass);
        _grass.Build(_sim.Field.Terrain, dirt, _sim.Field.Props);
        if (!CameraPresets.StreamsAroundCamera(_options.CameraPreset))
            _grass.Update(new Vector3(_current.Position.X, 0, _current.Position.Y), int.MaxValue);   // fill what is in range before the first frame
        var trees = new TreeLine();
        AddChild(trees);
        trees.Build(_sim.Field);
        _tank = new TankModel();
        AddChild(_tank);
        _tank.Build(TankCatalog.Medium);
        _dust = new DustTrail();
        _tank.AddChild(_dust);
        _dust.Build(TankLooks.For(TankCatalog.Medium.Id).TrackX, TankCatalog.Medium.HullLength / 2f);
        _shells = new ShellViews();
        AddChild(_shells);
        _fx = new BattleFx();
        AddChild(_fx);
        _camera = new OrbitCamera();
        AddChild(_camera);
        _camera.SetAngles(_current.Yaw, 0.25f);
        if (_options.Sniper) _camera.ToggleSniper();
        var hud = new CanvasLayer();
        AddChild(hud);
        _vignette = new SniperVignette();
        hud.AddChild(_vignette);
        _reticle = new Reticle();
        hud.AddChild(_reticle);
        _status = new StatusPanel();
        hud.AddChild(_status);

        if (_options.FpsLog) RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);
        if (_options.CameraPreset is { } preset && !CameraPresets.IsKnown(preset))
            GD.PushWarning($"Unknown --camera-preset '{preset}'; use front, side, close, closerear, sprocket, hill, spin, orbit, bush or target.");
        if (_options.RangeDemo is { } demo && _demo is null)
            GD.PushWarning($"Unknown --range-demo '{demo}'; use pen, ricochet, nopen, he or kill.");
        if (!_options.AutoDrive && !IgnoresInput) Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    private bool HasPreset => CameraPresets.IsKnown(_options.CameraPreset);

    /// <summary>Fixed-camera and scripted runs ignore stray input so screenshots stay repeatable.</summary>
    private bool IgnoresInput => HasPreset || _demo is not null;

    public override void _Input(InputEvent e)
    {
        if (IgnoresInput) return;
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
        _tank.Animate(tank.Speed, Angles.Wrap(_current.Yaw - _previous.Yaw) / SimConstants.Dt, (float)delta);
        _dust.Update(tank.Speed);
        if (_playerFired)
        {
            _fx.Muzzle(_tank.MuzzleTransform, withSmoke: !_camera.Sniper);   // own puffs would block the scope
            _camera.Shake(FireShake, FireShakeSeconds);
            _playerFired = false;
            if (_firstShotTime < 0 && _time > FxShotWarmup) _firstShotTime = _time;
        }
        _tank.Visible = !_camera.Sniper;
        if (_options.AutoDrive) _camera.SetAngles(tank.Yaw + 0.35f, 0.18f);
        else if (_demo is not null) _camera.SetAngles(_demo.PlayerYaw, DemoCameraPitch);
        _camera.Follow(pose.Position.ToGodot(), (pose.Trunnion(tank.Spec) + pose.Up * SniperEyeAboveTrunnion).ToGodot(), _sim.Field.Terrain);
        if (HasPreset)
        {
            var (eye, target) = CameraPresets.Get(_options.CameraPreset!, pose.Position.ToGodot(), tank.Yaw, _time, _sim.Field.Terrain.HeightAt);
            _camera.Camera.LookAtFromPosition(eye, target, Vector3.Up);
        }
        else if (_demo is null && !_input.FreeLook) UpdateAim(pose, tank.Spec);
        TankLook look = TankLooks.For(tank.Spec.Id);
        float trackHalf = look.TrackX + look.TrackWidth / 2 + FootprintMargin;
        _grass.SetTankFootprint(new Obb2(new SN.Vector2(pose.Position.X, pose.Position.Z),
            new SN.Vector2(MathF.Max(tank.Spec.HullWidth / 2 + FootprintMargin, trackHalf), tank.Spec.HullLength / 2 + FootprintMargin), tank.Yaw));
        // Detail streams around a point leaning from the tank toward the camera (so the ground right in front of a zoomed-out
        // camera is in the dense band); fixed far-away presets stream around their eye.
        Vector3 tankPos = pose.Position.ToGodot(), cameraPos = _camera.Camera.GlobalPosition;
        _grass.Update(CameraPresets.StreamsAroundCamera(_options.CameraPreset) ? cameraPos
            : new Vector3(Mathf.Lerp(tankPos.X, cameraPos.X, GrassCameraLean), 0f, Mathf.Lerp(tankPos.Z, cameraPos.Z, GrassCameraLean)));
        _bushes.Update(cameraPos, TankMotion.Footprint(tank.Spec, tank.Position, tank.Yaw), (float)delta);
        _targets.Sync(_sim, (float)delta);
        _shells.Sync(_sim.Shells, alpha, (float)delta);
        UpdateReticle(tank);
        RunAutomation(delta);
    }

    private void StepSimulation()
    {
        var (throttle, steer, fire) = _demo is not null ? DemoInput(_demo) : _options.AutoDrive ? AutoDrive() : HasPreset ? ((sbyte)0, (sbyte)0, false) : _input.Sample();
        if (_options.AutoDrive && _options.FxShotDelay is not null) _aimPitch = FxShotPitch;   // shells land in view
        if (!HasPreset && _input.TakeRestock()) _sim.Restock(_current.Id);
        _targets.ReviveDue(_sim);
        _commands[0] = new InputCmd(_sim.Tick + 1, throttle, steer, _aimYaw, _aimPitch, fire, _demo?.Ammo ?? _input.Ammo);
        _targets.FillInputs(_commands.AsSpan(1), _sim.Tick + 1);
        _sim.Step(_commands);
        foreach (GameEvent e in _sim.Events) OnEvent(e);
        _previous = _current;
        _current = _sim.Tanks[0];
    }

    private void OnEvent(in GameEvent e)
    {
        NoteTrigger(e);
        switch (e.Type)
        {
            case GameEventType.ShellFired when e.TankId == _current.Id:
                // Flash at the visible barrel tip (the sim event keeps its own muzzle position): spawned after this frame's pose update.
                _playerFired = true;
                _demoFired = true;
                _tank.Fire(_current.TurretYaw);
                break;
            case GameEventType.ShellFired:
                _fx.Muzzle(new Transform3D(Basis.Identity, e.Position.ToGodot()));   // no barrel pose for other tanks yet
                break;
            case GameEventType.ShellExploded:
                // Shells that stop above the ground hit a rock or a tank: grey dust instead of brown.
                bool onRock = e.Position.Y > _sim.Field.Terrain.HeightAt(e.Position.X, e.Position.Z) + 0.5f;
                _fx.Explosion(e.Position.ToGodot(), onRock);
                break;
            case GameEventType.TankHit when e.Ammo == AmmoType.AP:
                _fx.Explosion(e.Position.ToGodot(), onRock: true, ArmorHitSize);   // HE gets its own ShellExploded
                break;
        }
        _targets.OnEvent(e, _sim, _fx);
    }

    private (sbyte Throttle, sbyte Steer, bool Fire) AutoDrive() => (1, (sbyte)(Math.Sin(_time * 0.4) > 0.5 ? 1 : 0), true);

    /// <summary>--range-demo: stand still, lay the gun on the demo's aim point, fire once (or keep firing) after the warm-up.</summary>
    private (sbyte Throttle, sbyte Steer, bool Fire) DemoInput(RangeDemo demo)
    {
        TankPose pose = TankPose.Compute(_current, _sim.Field.Terrain);
        SN.Vector2 spot = demo.Spot.Position;
        SN.Vector3 aim = demo.AimPoint(_sim.Field.Terrain.HeightAt(spot.X, spot.Y));
        (_aimYaw, _aimPitch) = Angles.AimAngles(aim - pose.Trunnion(_current.Spec), _aimYaw);
        bool fire = _time >= RangeDemo.FireAfter && (demo.KeepFiring || !_demoFired);
        return (0, 0, fire);
    }

    /// <summary>--shot-after: remembers when the awaited event first happened.</summary>
    private void NoteTrigger(in GameEvent e)
    {
        if (_triggerTime >= 0 || _options.ShotAfterEvent is not { } awaited) return;
        bool due = awaited switch
        {
            "fire" => e.Type == GameEventType.ShellFired && e.TankId == _current.Id,
            "hit" => e.Type == GameEventType.TankHit,
            "kill" => e.Type == GameEventType.TankDestroyed,
            _ => false,
        };
        if (due) _triggerTime = _time;
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

    private void UpdateReticle(in TankState tank)
    {
        // Same launch maths the sim fires with, so the ring shows where the shot really goes (including a blocked barrel).
        Ballistics.Launch launch = Ballistics.Aim(tank, _sim.Field);
        SN.Vector3 impact = launch.BlockPoint;
        bool hasImpact = launch.Blocked || Ballistics.PredictImpact(launch.Origin, launch.Velocity, _sim.Field, out impact);
        Camera3D cam = _camera.Camera;
        Vector3 point = impact.ToGodot();
        Vector2? screen = !hasImpact || cam.IsPositionBehind(point) ? null : cam.UnprojectPosition(point);
        _reticle.UpdateState(screen, 1f - tank.Reload / tank.Spec.ReloadTime, _camera.Sniper ? _camera.Zoom : 0f);
        _status.UpdateState(tank.Speed * 3.6f, tank.Reload);
        _vignette.Visible = _camera.Sniper;
    }

    private static TankState Interpolate(in TankState a, in TankState b, float t) => b with
    {
        Position = SN.Vector2.Lerp(a.Position, b.Position, t),
        Yaw = Angles.Lerp(a.Yaw, b.Yaw, t),
        TurretYaw = Angles.Lerp(a.TurretYaw, b.TurretYaw, t),
        GunPitch = a.GunPitch + (b.GunPitch - a.GunPitch) * t,
        Speed = a.Speed + (b.Speed - a.Speed) * t,
    };

    private void RunAutomation(double delta)
    {
        if (_options.FpsLog)
        {
            var rid = GetViewport().GetViewportRid();
            if (_frameLog.Add(delta,
                    RenderingServer.ViewportGetMeasuredRenderTimeCpu(rid), RenderingServer.ViewportGetMeasuredRenderTimeGpu(rid),
                    Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0,
                    (long)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame),
                    (long)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalPrimitivesInFrame)) is { } line)
                GD.Print(line);
        }
        double shotAt = _options.ShotAfterEvent is not null ? (_triggerTime < 0 ? double.MaxValue : _triggerTime + _options.ShotAfterDelay)
            : _options.FxShotDelay is { } fxDelay ? (_firstShotTime < 0 ? double.MaxValue : _firstShotTime + fxDelay)
            : _options.ScreenshotAt;
        if (_options.ScreenshotPath is { } path && !_screenshotTaken && _time >= shotAt)
        {
            _screenshotTaken = true;
            Error error = GetViewport().GetTexture().GetImage().SavePng(path);
            GD.Print(error == Error.Ok ? $"Screenshot saved: {path}" : $"Screenshot failed ({error}): {path}");
        }
        if (_options.QuitAfter > 0 && _time >= _options.QuitAfter) GetTree().Quit();
    }
}
