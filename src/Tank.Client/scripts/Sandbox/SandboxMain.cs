using Godot;
using Tank.Client.Bootstrap;
using Tank.Client.Cameras;
using Tank.Client.Combat;
using Tank.Client.Driving;
using Tank.Client.Vehicles;
using Tank.Client.World;
using Tank.Sim;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;
using Tank.Sim.Vision;

namespace Tank.Client.Sandbox;

/// <summary>
/// Offline test drive: runs Tank.Sim locally at 30Hz and renders one tick behind, interpolated. The player's tank is slot 0
/// (team 0); the practice targets (<see cref="TargetRange"/>, team 1, the patrolling one last) take the next slots. A
/// <see cref="VisionTracker"/> works out after every step who sees whom: targets the player's team doesn't see aren't drawn.
/// Keys: 1 AP, 2 HE, R restock, T restore the walls, fences and bushes, F1–F4 another tank (<c>TankCatalog.All</c> in
/// order), V the spotting numbers over every target. Scenery, HUD and scripted runs live in <see cref="BattleWorld"/>,
/// <see cref="SandboxHud"/> and <see cref="SandboxAutomation"/>.
/// Launch: godot-mono --path src/Tank.Client res://scenes/Sandbox.tscn [-- --tank light|medium|heavy|td --autodrive
/// --camera-preset front|side|hill|spin|orbit|bush|target|yard|gate|patrol|... --fps-log --sniper --spot-debug
/// --range-demo pen|ricochet|nopen|he|kill|nopen-heavy|kill-td|turn|wall|breach|fence|ram|bush|lamp
/// --screenshot FILE --screenshot-at S --fx-shot S --shot-after fire|hit|kill|prop|down:S --quit-after S]
/// </summary>
public partial class SandboxMain : Node3D
{
    private const double MaxCatchUp = 0.25;
    private const float SniperEyeAboveTrunnion = 0.45f;
    private const float FireShake = 0.08f;
    private const float FireShakeSeconds = 0.15f;
    private const float ArmorHitSize = 0.5f;        // an AP shell striking armour: a small grey puff
    private const float GroundBurstHeight = 0.5f;   // a shell bursting higher than this over the ground hit a rock or a tank

    private readonly SandboxInput _input = new();

    /// <summary>Raised by a second Esc (the first lets go of the mouse); only the menu, which hosts the sandbox as a child, listens.</summary>
    public event Action? ExitRequested;

    private InputCmd[] _commands = Array.Empty<InputCmd>();
    private LaunchOptions? _given;   // Main's options (already reported); null when the sandbox is the scene run on its own
    private LaunchOptions _options = new();
    private SandboxAutomation _automation = null!;
    private Simulation _sim = null!;
    private Func<float, float, float> _groundAt = null!;   // cached: a method group would make a new delegate every frame
    private TankState _previous;
    private TankState _current;
    private OrbitCamera _camera = null!;
    private BattleWorld _world = null!;
    private TankActor _tank = null!;
    private bool _playerFired;
    private ShellViews _shells = null!;
    private TargetRange _targets = null!;
    private VisionTracker _vision = null!;
    private BattleFx _fx = null!;
    private SandboxHud _hud = null!;
    private double _accumulator;
    private double _time;
    private float _aimYaw;
    private float _aimPitch;

    /// <summary>Hands over Main's launch options, whose unreadable values Main has already reported; call before adding the node to the tree.</summary>
    public void Setup(LaunchOptions options) => _given = options;

    public override void _Ready()
    {
        _options = _given ?? LaunchOptions.Parse(OS.GetCmdlineUserArgs());
        GetWindow().Title = "坦克 3D · 试驾场";
        SandboxInput.RegisterActions();
        _automation = new SandboxAutomation(_options, this, standStill: HasPreset);

        RangeDemo? demo = _automation.Demo;
        _sim = new Simulation(SandboxMap.Create());
        TankSpec start = _options.Tank is { } id ? TankCatalog.ById(id) : TankCatalog.Medium;
        _sim.AddTank(start, demo?.PlayerPosition ?? CameraPresets.Spawn(_options.CameraPreset), demo?.PlayerYaw ?? SandboxMap.SpawnYaw, TargetRange.PlayerTeam);
        _previous = _current = _sim.Tanks[0];
        _vision = new VisionTracker(_sim);
        _targets = Add(new TargetRange());
        _targets.Build(_sim, _vision, parkPatrol: demo is { PatrolDrives: false });
        _vision.Update();   // who is seen at the start, before the first frame is drawn
        _commands = new InputCmd[_sim.TankCount];
        _groundAt = _sim.Field.Terrain.HeightAt;
        _aimYaw = _current.Yaw;

        _world = Add(new BattleWorld());
        _world.Build(_sim.Field, _current, MapStyle.Sandbox, CameraPresets.StreamsAroundCamera(_options.CameraPreset));
        BuildTank(start);
        _shells = Add(new ShellViews());
        _fx = Add(new BattleFx());
        _camera = Add(new OrbitCamera());
        _camera.SetAngles(_current.Yaw, 0.25f);
        if (_options.Sniper) _camera.ToggleSniper();
        _hud = Add(new SandboxHud());
        _hud.Build(_camera, _targets, _sim.Field, _vision, _current.Id, _options.SpotDebug);

        _automation.ReportBadOptions(unreadOptions: _given is null);
        if (!_options.AutoDrive && !IgnoresInput) Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    private bool HasPreset => CameraPresets.IsKnown(_options.CameraPreset);

    /// <summary>Fixed-camera and scripted runs ignore stray input so screenshots stay repeatable.</summary>
    private bool IgnoresInput => HasPreset || _automation.Demo is not null;

    public override void _Input(InputEvent e)
    {
        if (IgnoresInput) return;
        // Releases always pass so a held fire can end; presses only count while captured (the capturing click must not shoot).
        if (e.IsActionReleased(DriveInput.FireGun) || Input.MouseMode == Input.MouseModeEnum.Captured) _input.OnInputEvent(e);
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
        if (e.IsActionPressed(DriveInput.SniperToggle)) _camera.ToggleSniper();
        if (e.IsActionPressed(SandboxInput.SpotDebug)) _hud.ToggleSpotDebug();
        if (e.IsActionPressed(DriveInput.UiCancel))
        {
            if (captured) Input.MouseMode = Input.MouseModeEnum.Visible;
            else ExitRequested?.Invoke();
        }
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
        TankState tank = TankInterpolation.Between(_previous, _current, alpha);
        TankPose pose = TankPose.Compute(tank, _sim.Field.Terrain);
        ShowPlayer(tank, pose, (float)delta);
        UpdateCamera(tank, pose);
        _world.Update(tank, pose, _camera.Camera.GlobalPosition, (float)delta);
        _targets.Sync(_sim, alpha, (float)delta);
        _shells.Sync(_sim.Shells, alpha, (float)delta);
        _hud.Refresh(tank, _sim.Tick);
        _automation.EndFrame(delta, _time);
    }

    private void StepSimulation()
    {
        if (!HasPreset && _input.TakeRestock()) _sim.Restock(_current.Id);
        if (!HasPreset && _input.TakeRestoreProps()) _sim.RestoreProps();
        if (_input.TakeTankPick() is int pick && pick < TankCatalog.All.Count) ChangeTank(TankCatalog.All[pick]);
        _targets.ReviveDue(_sim);
        var (throttle, steer, fire) = _input.Sample();
        var own = new InputCmd(_sim.Tick + 1, throttle, steer, _aimYaw, _aimPitch, fire, _input.Ammo);
        _commands[0] = _automation.Command(own, _current, _sim.Field, _time);   // a scripted run may drive and aim instead
        (_aimYaw, _aimPitch) = (_commands[0].AimYaw, _commands[0].AimPitch);
        _targets.FillInputs(_commands.AsSpan(1), _sim.Tick + 1, _sim, _current.Position);
        _sim.Step(_commands);
        _vision.Update();
        _targets.AfterStep(_sim);
        foreach (GameEvent e in _sim.Events) OnEvent(e);
        _previous = _current;
        _current = _sim.Tanks[0];
    }

    /// <summary>Makes the player's tank <paramref name="spec"/> where it stands, with a fresh model and dust trail and AP selected.</summary>
    private void ChangeTank(TankSpec spec)
    {
        if (spec == _current.Spec) return;
        _sim.ChangeSpec(_current.Id, spec);
        _input.ResetAmmo();   // the new tank starts with AP loaded; still asking for HE would cost it a full reload
        _previous = _current = _sim.Tanks[0];
        _tank.QueueFree();   // the smoke and the dust trail go with it
        BuildTank(spec);
    }

    private void BuildTank(TankSpec spec)
    {
        _tank = Add(new TankActor());
        _tank.Build(spec);
    }

    private void OnEvent(in GameEvent e)
    {
        _automation.OnEvent(e, _current.Id, _time);
        switch (e.Type)
        {
            case GameEventType.ShellFired when e.TankId == _current.Id:
                // Flash at the visible barrel tip (the sim event keeps its own muzzle position): spawned after this frame's pose update.
                _playerFired = true;
                _tank.Kick(_current.TurretYaw);
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
        _hud.OnShotEvent(e);
        _targets.OnEvent(e, _sim, _fx);
        _world.OnEvent(e);
        PropFx.OnEvent(e, _sim.Field, _fx);
    }

    /// <summary>Poses and animates the player's tank, with the muzzle blast of a shot fired this frame; hidden in the sniper sight.</summary>
    private void ShowPlayer(in TankState tank, TankPose pose, float delta)
    {
        _tank.Present(tank, pose, Angles.Wrap(_current.Yaw - _previous.Yaw) / SimConstants.Dt, delta, shown: !_camera.Sniper);
        if (_playerFired)
        {
            _fx.Muzzle(_tank.MuzzleTransform, withSmoke: !_camera.Sniper);   // own puffs would block the scope
            _camera.Shake(FireShake, FireShakeSeconds);
            _playerFired = false;
        }
    }

    /// <summary>Follows the tank (or looks from a fixed preset), then lays the gun where the camera looks unless a script aims it.</summary>
    private void UpdateCamera(in TankState tank, TankPose pose)
    {
        if (_automation.CameraAngles(tank.Yaw) is { } angles) _camera.SetAngles(angles.Yaw, angles.Pitch);
        _camera.Follow(pose.Position.ToGodot(), (pose.Trunnion(tank.Spec) + pose.Up * SniperEyeAboveTrunnion).ToGodot(), _sim.Field.Terrain);
        if (HasPreset)
        {
            var (eye, target) = CameraPresets.Get(_options.CameraPreset!, pose.Position.ToGodot(), tank.Yaw, _time, _groundAt, targetIndex: _automation.Demo?.Target ?? 0);
            _camera.Camera.LookAtFromPosition(eye, target, Vector3.Up);
        }
        else if (_automation.Demo is null && !_input.FreeLook) UpdateAim(pose, tank.Spec);
    }

    /// <summary>Lays the gun where the camera looks (<see cref="AimSolver"/>); the turret then chases that point.</summary>
    private void UpdateAim(TankPose pose, TankSpec spec) =>
        (_aimYaw, _aimPitch) = AimSolver.Solve(_camera, _sim.Field, pose.Trunnion(spec), _aimYaw);

    private T Add<T>(T node) where T : Node
    {
        AddChild(node);
        return node;
    }
}
