using Godot;
using Tank.Client.Cameras;
using Tank.Client.Combat;
using Tank.Client.Overlay;
using Tank.Client.World;
using Tank.Sim;
using Tank.Sim.Combat;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;
using Tank.Sim.Vision;
using SN = System.Numerics;

namespace Tank.Client.Sandbox;

/// <summary>
/// The sandbox's HUD layer, back to front: the sniper vignette, hit popups, the targets' and the damaged walls' health
/// bars (over the popups, so a bar stays in sight while it drops), the spotting debug lines (V, or --spot-debug), the
/// reticle and the status panel, whose lamp lights while an enemy sees the player.
/// <see cref="OnEvent"/> spawns hit verdicts and damage numbers (over targets and walls) for the player's own shots;
/// <see cref="Refresh"/> updates the rest once per frame. A target the player's team doesn't see gets no bar, verdict or
/// damage number. A wall's bar, smaller than a tank's and drawn on its face, shows only while it is standing and short
/// of full hit points.
/// </summary>
public partial class SandboxHud : CanvasLayer
{
    private const float KmhPerMs = 3.6f;
    private const float WallBarBelowTop = 1f;    // a wall's bar sits on its face, this far under the top, clear of any tank's bar behind it
    private const float WallBarWidth = 44f, WallBarHeight = 8f;   // smaller than a tank's, so the two don't read alike

    private OrbitCamera _camera = null!;
    private TargetRange _targets = null!;
    private Battlefield _field = null!;
    private SniperVignette _vignette = null!;
    private CombatPopups _popups = null!;
    private HealthBars _bars = null!;
    private HealthBars _wallBars = null!;
    private SpotLabels _spotLabels = null!;
    private VisionTracker _vision = null!;
    private int _playerId;
    private int _labelsTick = -1;   // the simulation tick the debug lines were last worked out for
    private Prop[] _walls = Array.Empty<Prop>();
    private Reticle _reticle = null!;
    private StatusPanel _status = null!;

    /// <param name="playerId">The player's tank: only its shots get verdicts and damage numbers.</param>
    public void Build(OrbitCamera camera, TargetRange targets, Battlefield field, VisionTracker vision, int playerId, bool spotDebug)
    {
        (_camera, _targets, _field, _vision, _playerId) = (camera, targets, field, vision, playerId);
        _vignette = Add(new SniperVignette());
        _popups = Add(new CombatPopups());
        _popups.SetCamera(camera.Camera);
        _bars = Add(new HealthBars());
        _bars.Setup(camera.Camera, targets.Count);
        _walls = field.Props.Where(p => p.IsWall).ToArray();
        _wallBars = Add(new HealthBars());
        _wallBars.Setup(camera.Camera, _walls.Length, WallBarWidth, WallBarHeight);
        _spotLabels = Add(new SpotLabels());
        _spotLabels.Setup(camera.Camera, targets.Count);
        _spotLabels.Visible = spotDebug;
        _reticle = Add(new Reticle());
        _status = Add(new StatusPanel());
    }

    /// <summary>
    /// For the player's own shots: a verdict where a shell strikes a tank, and damage numbers over a target's head (other
    /// tanks are skipped) or on the face of a wall that took a shell or a splash (<see cref="PropFx.IsDirectHit"/> tells
    /// them apart). Hits on a target the player's team doesn't see get nothing.
    /// </summary>
    public void OnEvent(in GameEvent e)
    {
        if (e.TankId != _playerId) return;   // the patrolling target's shots
        switch (e.Type)
        {
            case GameEventType.TankHit or GameEventType.SplashDamage when _targets.IsHidden(e.TargetTankId):
                break;
            case GameEventType.TankHit:
                _popups.Verdict(e.Position.ToGodot(), e.Result);
                if (e.Damage > 0 && _targets.TryGetHead(e.TargetTankId, out Vector3 head)) _popups.Damage(head, e.Damage, splash: false);
                break;
            case GameEventType.SplashDamage when _targets.TryGetHead(e.TargetTankId, out Vector3 splashed):
                _popups.Damage(splashed, e.Damage, splash: true);
                break;
            case GameEventType.PropDamaged:
                Prop wall = _field.PropById(e.PropId);
                _popups.Damage(WallHead(wall), e.Damage, splash: !e.IsDirectHit());
                break;
        }
    }

    /// <summary>Where a wall's bar and damage numbers go: on the middle of its face, a little under the top.</summary>
    private static Vector3 WallHead(in Prop wall) => wall.Footprint.Center.Ground(wall.Top - WallBarBelowTop);

    /// <summary>Shows or hides the spotting debug lines.</summary>
    public void ToggleSpotDebug() => _spotLabels.Visible = !_spotLabels.Visible;

    /// <summary>
    /// Once per frame, after the targets have been synced: reticle, status panel, lamp, vignette, health bars and debug
    /// lines (their looks worked out once per simulation <paramref name="tick"/>).
    /// </summary>
    public void Refresh(in TankState player, int tick)
    {
        UpdateSight(player);
        _status.SetVehicle(player.Spec.Name);
        _status.UpdateState(player.Speed * KmhPerMs, player.Reload, player.Health, player.Spec.MaxHealth, player.Ammo, player.ApLeft, player.HeLeft);
        _status.SetSpotted(!player.IsDestroyed && _vision.IsVisible(player.Id, TargetRange.TargetTeam));
        _vignette.Visible = _camera.Sniper;
        bool look = _spotLabels.Visible && tick != _labelsTick;
        for (int i = 0; i < _targets.Count; i++)
        {
            var (head, health, alive, shown) = _targets.Status(i);
            _bars.Set(i, head, health, alive && shown);
            if (!_spotLabels.Visible) continue;
            _spotLabels.Place(i, head, hasLine: alive);
            if (look) _spotLabels.Set(i, _vision.Inspect(player.Id, _targets.IdAt(i)), drawn: shown);
        }
        if (look) _labelsTick = tick;
        for (int i = 0; i < _walls.Length; i++)
        {
            int left = _field.Durability(_walls[i].Id), full = _walls[i].MaxDurability;
            _wallBars.Set(i, WallHead(_walls[i]), left / (float)full, shown: left > 0 && left < full);
        }
    }

    private T Add<T>(T control) where T : Node
    {
        AddChild(control);
        return control;
    }

    /// <summary>The reticle, from the same launch maths the sim fires with, so the ring shows where the shot really goes (including a blocked barrel).</summary>
    private void UpdateSight(in TankState player)
    {
        Ballistics.Launch launch = Ballistics.Aim(player, _field);
        SN.Vector3 impact = launch.BlockPoint;
        bool hasImpact = launch.Blocked || Ballistics.PredictImpact(launch.Origin, launch.Velocity, _field, out impact);
        Camera3D cam = _camera.Camera;
        Vector3 point = impact.ToGodot();
        Vector2? screen = null;
        float circle = 0f;
        if (hasImpact && !cam.IsPositionBehind(point))
        {
            screen = cam.UnprojectPosition(point);
            circle = AimCirclePixels(player, launch.Origin, point, screen.Value);
        }
        _reticle.UpdateState(screen, 1f - player.Reload / player.Spec.ReloadTime, _camera.Sniper ? _camera.Zoom : 0f, circle);
    }

    /// <summary>
    /// The aim circle's radius around the predicted impact on screen: the circle is quoted at 100 m from the gun, so it
    /// measures <c>radius × gun distance / 100</c> there; a point that far to the camera's side of the impact is projected
    /// and its distance from <paramref name="screen"/> (the impact's own projection) measured.
    /// </summary>
    private float AimCirclePixels(in TankState player, SN.Vector3 muzzle, Vector3 impact, Vector2 screen)
    {
        Camera3D cam = _camera.Camera;
        float worldRadius = Dispersion.Radius(player.Spec, player.Bloom) * SN.Vector3.Distance(muzzle, impact.ToSim()) / Dispersion.ReferenceRange;
        Vector3 edge = impact + cam.GlobalBasis.X * worldRadius;
        return cam.IsPositionBehind(edge) ? 0f : cam.UnprojectPosition(edge).DistanceTo(screen);
    }
}
