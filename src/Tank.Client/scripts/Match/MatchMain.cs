using Godot;
using Tank.Client.Bootstrap;
using Tank.Client.Cameras;
using Tank.Client.Combat;
using Tank.Client.Minimap;
using Tank.Client.Online;
using Tank.Client.Overlay;
using Tank.Client.Sandbox;
using Tank.Client.UI;
using Tank.Client.Vehicles;
using Tank.Client.World;
using Tank.Net.Battle;
using Tank.Sim;
using Tank.Sim.Maps;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;
using SN = System.Numerics;

namespace Tank.Client.Match;

/// <summary>
/// The match scene (blueprint §1.1, §3.2): a child of <c>Main</c> that lives while <c>BattleClient.Phase</c> is
/// <c>InMatch</c> and draws what the client knows. It alone calls <c>client.Update</c> while it exists, once per frame,
/// and feeds it the player's command; the own tank is drawn from the prediction (between its last two ticks, at the
/// client's render position), the others from the interpolator as <see cref="TankActors"/>, the shells from
/// <c>client.Shells</c>, the props as the field has them; events become effects (MatchMain.Events.cs); the camera and
/// aim follow the sandbox's, a dead player's camera the spectate chain, and Esc the pause layer (MatchMain.Camera.cs);
/// the map (<see cref="MinimapLayer"/>) sits in the corner, M opens it full-screen, and the results hide it. In a royale
/// the zone is read once a frame from the client at the render tick (<see cref="ZoneWall"/> on its circle, the HUD's
/// zone line and the tint while the own live tank is outside, the circles on the map); it stays up on the results.
/// Nothing here reads <c>Simulation</c>. A loopback host runs on a
/// virtual clock, so every frame then steps by <see cref="VirtualFrame"/>. <see cref="Finished"/> fires once when the
/// phase leaves the match; the owner then frees the scene.
/// </summary>
public partial class MatchMain : Node3D
{
    private const double VirtualFrame = 1.0 / 60;
    private const float StartPitch = 0.25f;   // the orbit camera's pitch on entering, looking a little down on the tank

    private readonly MatchInput _input = new();
    private readonly ShotDetector _shots = new();
    private readonly WreckTracker _ownWreck = new();
    private readonly ResultsClock _resultsClock = new();
    private readonly SpectateState _spectate = new();
    private readonly List<int> _candidates = new(BattleProtocol.MaxPlayers);   // the living tanks a dead player may watch, refilled each frame
    private readonly string?[] _names = new string?[BattleProtocol.MaxPlayers];
    private readonly HeadLookup _headOf;   // cached: a method group would make a new delegate every call
    private readonly MinimapFrame _minimapFrame = new();   // the map's icons, filled again every frame

    private BattleSession _session = null!;
    private BattleClient _client = null!;
    private LaunchOptions _options = new();
    private MatchAutomation _automation = null!;
    private MapSite _site = MapSites.Sandbox;   // the room's map's site: the fixed cameras of scripted screenshots
    private bool _titled;          // the room state has come: the window title names the map and _site is its site
    private bool _mapViewOn;       // the "map" preset has turned the camera orthographic
    private RoomStateMessage? _namesFrom;   // the room state the names were read from
    private Battlefield? _field;
    private Func<float, float, float> _groundAt = null!;   // the ground or the water over it, for the fixed cameras
    private BattleWorld? _world;   // null until the field is known
    private ZoneWall? _zoneWall;   // a royale's light wall; null in the other modes
    private ZoneState _zone;       // the zone at this frame's render tick (Phase None outside a royale)
    private TankActor? _tank;      // null until the own tank is known
    private TankSpec? _tankSpec;   // the spec it was built for
    private bool _tankFirstSight;
    private TankActors _actors = null!;
    private ShellViews _shells = null!;
    private BattleFx _fx = null!;
    private OrbitCamera _camera = null!;
    private CombatHud _combat = null!;
    private MatchHud _hud = null!;
    private MinimapLayer _minimap = null!;
    private CanvasLayer _loading = null!;
    private PauseOverlay? _pause;   // built on the first Esc
    private double _time;
    private float _aimYaw;
    private float _aimPitch;
    private bool _finished;
    private bool _prewarmed;       // the roster's actors were built from the first snapshot
    private bool _ringsBuilt;      // the bases' rings are drawn (a standard match's), or there are none to draw
    private bool _resultsShown;    // the match has ended and the results screen is up
    private bool _dead;            // the own tank is a wreck: commands are empty and the spectate chain has the camera
    private SN.Vector2 _focus;     // where the spectating camera looks (the free camera moves it)
    private string? _spectateName; // the followed player's name for the HUD bar
    private bool _spectateFree;    // the free camera is on: the bar shows its hints

    /// <summary>The client left the match (it ended, or the connection did); raised once.</summary>
    public event Action? Finished;

    /// <summary>离开房间 on the pause layer: the owner drops the whole session (this scene has already stopped itself).</summary>
    public event Action? LeaveRequested;

    public MatchMain() => _headOf = TryGetHead;

    /// <summary>
    /// Hands over the session to draw; call before adding the node to the tree. <paramref name="screenshotTaken"/>: the
    /// run's one screenshot is already taken (by Main before any match, or by an earlier match), so this one takes none.
    /// </summary>
    public void Setup(BattleSession session, LaunchOptions options, bool screenshotTaken)
    {
        (_session, _client, _options) = (session, session.Client, options);
        _site = MapSites.For(_client.Room?.Map);   // no room state yet (a mid-match join): the sandbox's, which only scripted shots use
        _automation = new MatchAutomation(options, screenshotTaken, _site, hasZone: ModeRules.HasZone(Mode));
    }

    /// <summary>Whether the run's screenshot is spent, this match's own or an earlier one; the owner reads it as the scene goes.</summary>
    public bool ScreenshotTaken => _automation.ScreenshotTaken;

    /// <summary>Whether a due screenshot waits for the map's picture: the owner's <c>--quit-after</c> waits with it.</summary>
    public bool HoldingScreenshot => _automation.HoldingScreenshot;

    public override void _Ready()
    {
        UpdateTitle();
        MatchInput.RegisterActions();
        _input.NetPanelOpen = _options.NetGraph;
        _loading = BuildLoadingLayer();
        AddChild(_loading);
        _automation.Start(this);   // --screenshot-at (and --quit-after) count from here, not from the process start
        _automation.ReportBadOptions();
        if (!_automation.IgnoresInput) Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    /// <summary>
    /// Names the map in the window title once the room state is known: at once when it is, else on the first frame it is
    /// (joining a running match, the match start comes before the room state), when the map's site is picked again too.
    /// Until then the title says 对局; it is set (and logged) only when it changes.
    /// </summary>
    private void UpdateTitle()
    {
        RoomStateMessage? room = _client.Room;
        if (room is not null) _site = MapSites.For(room.Map);
        WindowTitle.Set(this, MapText.MatchTitle(room?.Map));
        _titled = room is not null;
    }

    public override void _Process(double delta)
    {
        if (_finished) return;
        double dt = _session.IsVirtualClock ? VirtualFrame : delta;
        _time += dt;
        _session.Advance(dt);

        // The command first, then exactly one Update; a latched click is spent only once a tick has taken it (R1).
        LocalCommand cmd = Command();
        int before = _client.Predictor.LastPredictedTick;
        _client.Update(dt, cmd);
        if (_client.Predictor.LastPredictedTick != before) _input.EndTick();

        if (_client.Phase != BattlePhase.InMatch)
        {
            _finished = true;
            Finished?.Invoke();
            return;
        }
        if (!_titled && _client.Room is not null) UpdateTitle();
        StepMatch(dt);
        // Last, and on loading frames too: the capture schedule runs on the match clock; a due screenshot waits for the map's picture.
        _automation.EndFrame(delta, _time, mapReady: _minimap is null || _minimap.HasPicture);
    }

    /// <summary>The frame's match work once the client is known to be in one; early returns while the field or the first snapshot is still missing.</summary>
    private void StepMatch(double dt)
    {
        bool ended = _client.Match.Phase == MatchPhase.Ended;
        if (ended != _resultsShown) ShowResults(ended);
        // Taken every frame: an Enter pressed during play (the captured mouse forwards every key) must not wait for the
        // results and send the host back the frame they appear.
        bool returnToRoom = _input.TakeReturnToRoom();
        if (ended && returnToRoom && _client.IsHost) _client.ReturnToRoom();
        if (_client.Field is not { } field) return;   // InMatch always has a field; a defensive frame if not
        // The scenery is built as soon as the field is known: one long frame, best spent before the first snapshot sets the clocks.
        if (_world is null) BuildScene(field);
        else if (!ReferenceEquals(field, _field)) TakeField(field);   // a resync swapped the field instance (R2)
        if (!_client.HasTank && _client.Latest is null)
        {
            _hud.Visible = false;
            _minimap.Visible = false;
            _loading.Visible = true;   // the match start is here, the first snapshot (and so the own tank's spec) not yet (R3)
            return;
        }
        if (!_prewarmed && _client.Latest is { } roster)
        {
            // The first snapshot names the whole roster: every tank's model is built now, before the loading shade comes
            // off (the long frame is spent with the shade still on screen), so nobody's first sight costs a hitch
            // mid-fight (a later joiner still builds lazily).
            _prewarmed = true;
            _actors.Prewarm(roster.Players, _client.OwnTankId, Mode, OwnTeam(_client.Own));
        }
        if (!_ringsBuilt) BuildRings(field);
        _loading.Visible = false;
        _hud.Visible = true;
        _minimap.Visible = true;
        RefreshNames();

        float fdt = (float)dt;
        while (_client.TryDequeueEvent(out GameEvent e)) OnEvent(e);

        bool hasTank = _client.HasTank;
        TankState own = default;
        TankPose pose = default;
        if (hasTank)
        {
            EnsureOwnActor();
            own = RenderMath.OwnDisplay(_client.Predictor.Previous, _client.Predictor.Current, (float)_client.Clock.Alpha, _client.OwnRenderPosition);
            pose = TankPose.Compute(own, field.Terrain);
            ShowOwn(own, pose, fdt);
            SyncDeath(own);
        }
        bool outside = ReadZone(hasTank, in own);
        (TankState? focus, TankPose? focusPose) = UpdateView(hasTank, own, pose, field, fdt);
        Vector3 cameraPos = _camera.Camera.GlobalPosition;
        _world!.Update(focus, focusPose, cameraPos, fdt);   // only one tank flattens the grass: the own, or the one followed (R12)
        _actors.Sync(_client.Others, Mode, OwnTeam(own), fdt);
        _shells.Sync(_client.Shells, (float)_client.Clock.Alpha, fdt);
        _combat.ShowOwn(hasTank && !ended && !_dead);   // the dead get no reticle or status panel
        _combat.Refresh(own, spotted: hasTank && _client.Spotted && !own.IsDestroyed);
        _actors.FillBars(_combat.TankBars, BattleProtocol.MaxPlayers, _names);
        int resultsLeft = _resultsClock.Observe(_client.Match.Phase, dt);
        bool boardOpen = _input.ScoreboardHeld && !Paused;   // Tab is polled, so the pause layer has to veto it here
        if (boardOpen) _minimap.CloseFull();   // the board shares the full map's layer and would sit under it: Tab wins
        _hud.SetScoreboardOpen(boardOpen);
        _hud.Refresh(MatchHudFrame.From(_client, _session, OwnTeam(own), _names, (int)Engine.GetFramesPerSecond(), _spectateName, _spectateFree, resultsLeft, ZoneHud.From(_zone, outside)), _input.NetPanelOpen, _time);
        ShowMinimap(hasTank, own, field);
    }

    /// <summary>
    /// The zone as the render tick sees it (the server alone deals its damage), into <see cref="_zone"/>: the wall shows it,
    /// then the camera (the zone preset looks at the wall), the HUD and the map read this frame's copy. Returns whether
    /// the own tank, as drawn, is outside it; the first such frame goes to the automation (the plan keeps the first time).
    /// </summary>
    private bool ReadZone(bool hasTank, in TankState own)
    {
        _zone = _client.ZoneAt(_client.Interpolator.RenderTick);
        _zoneWall?.Show(_zone.Current);
        bool outside = hasTank && !own.IsDestroyed && _zone.IsOutside(own.Position);
        if (outside) _automation.NotifyOutside(_time);
        return outside;
    }

    /// <summary>
    /// The map's icons for this frame (stage 5 design §5): the bases with their progress, the eye (<see cref="MinimapEyes.Pick"/>:
    /// the own tank, the one followed, or a ring on the free focus or the wreck) with the fan along the camera's view, and
    /// every other tank the snapshot holds — the server's filter, so an enemy shows only while the team sees it.
    /// </summary>
    private void ShowMinimap(bool hasTank, in TankState own, Battlefield field)
    {
        MinimapFrame frame = _minimapFrame;
        frame.Clear();
        byte team = OwnTeam(own);
        MinimapFeed.AddBases(frame, _client.MatchInfo?.Bases, team, _client.Match);
        MinimapFeed.AddZone(frame, _zone);
        bool watching = _dead && !_automation.HasPreset;
        RemoteTank? followed = watching && _spectate.Current == SpectateState.Mode.Follow && TryFindRemote(_spectate.TargetId, out RemoteTank remote) ? remote : null;
        MinimapEyeChoice eye = MinimapEyes.Pick(hasTank, watching, _spectate.Current, followed, own.Position, own.Yaw, _focus, _camera.GroundHeading(), _camera.HalfFov());
        frame.Eye = eye.Eye;
        frame.OwnWreck = eye.OwnWreck;
        MinimapFeed.AddOthers(frame, _client.Others, Mode, team, eye.EyeTankId);
        _minimap.Show(frame, field);
        if (_automation.TakeFullMap() && !_minimap.FullOpen) _minimap.ToggleFull();
    }

    /// <summary>
    /// The own tank crossed between fighting and wreck. Death starts the spectate chain (the close-up, then — with no
    /// respawn coming — a target on its own) with the scope off and the fight's stray Q/E/F/clicks dropped; the respawn
    /// puts the model back at once (the own state is the word: the revive event, a frame or so behind, then finds it
    /// intact and does nothing) with AP loaded (<see cref="RestoreOwn"/>), and snaps the camera back behind the tank.
    /// </summary>
    private void SyncDeath(in TankState own)
    {
        if (own.IsDestroyed == _dead) return;
        _dead = own.IsDestroyed;
        if (_dead) _automation.NotifyDeath(_time);   // the state beats the destroy event (R5); the plan takes the first
        (_spectateName, _spectateFree) = (null, false);
        if (!_dead)
        {
            if (_ownWreck.Revived() == WreckTracker.Change.Restore) RestoreOwn();
            _aimYaw = own.Yaw;
            _camera.SetAngles(own.Yaw, StartPitch);
            return;
        }
        _spectate.OnDeath(autoFollow: !ModeRules.Respawns(Mode));
        _focus = own.Position;
        if (_camera.Sniper) _camera.ToggleSniper();   // the scope belongs to the gun; the dead orbit
        _input.TakeSpectateStep();
        _input.TakeSpectateClick();
        _input.TakeSpectateFree();
    }

    /// <summary>
    /// The player's sticks, trigger and aim for this frame (blueprint §5 T8). Dead or spectating, on the results screen or
    /// paused, the tank stands and holds its fire, but the aim stays where it was: an aim of zero would swing the turret
    /// to world yaw 0 for everyone watching. With no tank yet there is nothing to aim.
    /// </summary>
    private LocalCommand Command()
    {
        if (!_client.HasTank) return LocalCommand.None;
        if (_resultsShown || _client.Own.IsDestroyed || Paused) return new LocalCommand(0, 0, _aimYaw, _aimPitch, false, _input.Ammo);
        var (throttle, steer, fire) = _input.Peek();
        return _automation.Command(new LocalCommand(throttle, steer, _aimYaw, _aimPitch, fire, _input.Ammo), _time);
    }

    /// <summary>Whether the pause layer is up (the mouse is then free and the commands empty; the match runs on underneath).</summary>
    private bool Paused => _pause is { Visible: true };

    /// <summary>The results screen comes up (or goes, were a match to restart on the same scene): the mouse is let go for it, held keys drop and the pause layer closes (it must not cover the verdict or eat the host's Enter).</summary>
    private void ShowResults(bool shown)
    {
        _resultsShown = shown;
        _minimap?.SetHidden(shown);   // the maps go with the rest of the play HUD
        if (!shown) return;
        _automation.NotifyEnded(_time);
        _pause?.Close();
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _input.ReleaseAll();
    }

    private BattleMode Mode => _client.MatchInfo?.Mode ?? _client.Room?.Mode ?? BattleMode.TeamBattle;

    /// <summary>The player's team from the scoreboard (known even while the own tank isn't), else the own tank's.</summary>
    private byte OwnTeam(in TankState own)
    {
        if (_client.Latest is { } latest && _client.Slot < latest.Players.Length) return latest.Players[_client.Slot].Team;
        return own.Team;
    }

    private void BuildScene(Battlefield field)
    {
        _field = field;
        _groundAt = MapSites.SurfaceHeight(field);
        TankState focus = _client.HasTank ? _client.Own : new TankState { Position = new SN.Vector2(field.Terrain.SizeX / 2f, field.Terrain.SizeZ / 2f) };
        _world = Add(new BattleWorld());
        // The room state names the map; joining a running match it may not have come yet, so its ground's grid does.
        _world.Build(field, focus, MapStyle.For(_client.Room?.Map ?? MapSites.IdOfGrid(field.Terrain)), CameraPresets.StreamsAroundCamera(_automation.Preset));
        if (ModeRules.HasZone(Mode))
        {
            _zoneWall = Add(new ZoneWall());
            _zoneWall.Build(field);
        }
        _fx = Add(new BattleFx());
        _actors = Add(new TankActors());
        _actors.Build(field.Terrain, _fx);
        _shells = Add(new ShellViews());
        _camera = Add(new OrbitCamera());
        if (_options.Sniper) _camera.ToggleSniper();
        _combat = Add(new CombatHud());
        _combat.Build(_camera, field, _client.OwnTankId, BattleProtocol.MaxPlayers, _headOf);
        _hud = Add(new MatchHud());
        _minimap = Add(new MinimapLayer());   // after the HUD: on top of it in the same layer
        // Baked on a worker thread (the corner shows an empty panel meanwhile) with the scenery's own bare-ground mask,
        // named as the title names it (no name while the map is unknown).
        string? mapId = _client.Room?.Map ?? MapSites.IdOfGrid(field.Terrain);
        Func<float, float, float> notGrassAt = _world.GroundMask is { } mask ? mask.At : (_, _) => 0f;
        _minimap.Build(field, notGrassAt, mapId is null ? null : MapText.Name(mapId));
        if (_resultsShown) _minimap.SetHidden(true);
    }

    /// <summary>
    /// The bases' rings (stage 5 design §3.4), once the player's team is known (the first snapshot, or the own tank),
    /// coloured by it: a standard match's only, as the match start names no bases in the others. Built once: a resync's
    /// new field (<see cref="TakeField"/>) has the same ground and the bases never move.
    /// </summary>
    private void BuildRings(Battlefield field)
    {
        if (_client.MatchInfo is not { } info) return;
        _ringsBuilt = true;
        if (info.Bases is not { Count: > 0 } bases) return;
        Add(new BaseRings()).Build(bases, OwnTeam(_client.Own), field.Terrain);
    }

    private void TakeField(Battlefield field)
    {
        _field = field;
        _groundAt = MapSites.SurfaceHeight(field);
        _world!.SetField(field);
        _combat.SetField(field);
        _actors.SetTerrain(field.Terrain);
        // One scene, one map: a new match gets a new scene, so a field swapped in here is the same match's (a resync's
        // Fresh() copy), which the map layer repaints by itself; it reports a field on another terrain rather than mispaint.
    }

    /// <summary>Builds the own actor once the own tank's spec is known (a new spec, which only a new match brings, rebuilds it).</summary>
    private void EnsureOwnActor()
    {
        TankSpec spec = _client.Own.Spec;
        if (_tank is not null && _tankSpec == spec) return;
        _tank?.QueueFree();
        _tank = Add(new TankActor());
        _tank.Build(spec);   // the look's own paint: toy orange
        _tankSpec = spec;
        _tankFirstSight = true;
        _aimYaw = _client.Own.Yaw;
        _camera.SetAngles(_client.Own.Yaw, StartPitch);
    }

    /// <summary>
    /// Poses the own tank with the recoil, flash and camera shake of a shot it fired since the last frame (seen in its
    /// predicted shot count: a predicted shot never reaches the event queue), and wrecks or restores it as the state says
    /// when no event did.
    /// </summary>
    private void ShowOwn(in TankState own, TankPose pose, float dt)
    {
        TankActor tank = _tank!;
        bool fired = _shots.Update(_client.Own.ShotCount, _client.Own.Id);
        if (fired)
        {
            tank.Kick(own.TurretYaw);
            _automation.NotifyFired(_time);   // a predicted own shot never reaches the event queue
        }
        switch (_ownWreck.Observe(own.IsDestroyed, dt, _tankFirstSight))
        {
            case WreckTracker.Change.Settle: tank.Wreck(own.TurretYaw, settled: true); break;
            case WreckTracker.Change.Restore: RestoreOwn(); break;
        }
        _tankFirstSight = false;
        float yawRate = RenderMath.YawRate(_client.Predictor.Previous.Yaw, _client.Predictor.Current.Yaw, SimConstants.Dt);
        tank.Present(own, pose, yawRate, dt, shown: !_camera.Sniper);
        if (!fired) return;
        _fx.Muzzle(tank.MuzzleTransform, withSmoke: !_camera.Sniper);   // own puffs would block the scope
        _camera.Shake(FireShake, FireShakeSeconds);
    }

    /// <summary>The names by room slot, re-read whenever the room state changes (a record the client replaces).</summary>
    private void RefreshNames()
    {
        RoomStateMessage? room = _client.Room;
        if (ReferenceEquals(room, _namesFrom)) return;
        _namesFrom = room;
        Array.Clear(_names);
        if (room is null) return;
        for (int i = 0; i < room.Players.Count; i++)
        {
            RoomPlayer p = room.Players[i];
            if (p.Slot < _names.Length) _names[p.Slot] = p.Name;
        }
    }

    private bool TryGetHead(int tankId, out Vector3 head) => _actors.TryGetHead(tankId, out head);

    private static CanvasLayer BuildLoadingLayer()
    {
        var layer = new CanvasLayer { Layer = 2 };
        var shade = new ColorRect { Color = UiKit.Background, MouseFilter = Control.MouseFilterEnum.Ignore };
        layer.AddChild(shade);
        shade.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        Label label = UiKit.Label("对局加载中…", 40, UiKit.Dim, HorizontalAlignment.Center);
        label.VerticalAlignment = VerticalAlignment.Center;
        layer.AddChild(label);
        label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        return layer;
    }

    private T Add<T>(T node) where T : Node
    {
        AddChild(node);
        return node;
    }
}
