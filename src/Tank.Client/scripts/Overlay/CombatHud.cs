using Godot;
using Tank.Client.Cameras;
using Tank.Client.Combat;
using Tank.Client.World;
using Tank.Sim;
using Tank.Sim.Combat;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;
using SN = System.Numerics;

namespace Tank.Client.Overlay;

/// <summary>
/// Where tank <paramref name="tankId"/>'s head is (the point its bar and damage numbers go on); false for a tank that is
/// unknown or mustn't be drawn now (not in sight), which then gets no bar, verdict or damage number.
/// </summary>
public delegate bool HeadLookup(int tankId, out Vector3 head);

/// <summary>
/// The combat HUD both the sandbox and a match use, back to front: the sniper vignette, hit popups, tank health bars (the
/// caller feeds them through <see cref="TankBars"/>, with a name label if it likes), the damaged walls' bars (over the
/// popups, so a bar stays in sight while it drops), then whatever a subclass adds in <see cref="AddBehindSight"/>, the
/// reticle and the status panel, whose lamp lights while an enemy sees the player.
/// <see cref="OnShotEvent"/> spawns hit verdicts and damage numbers (over tanks and walls) for the player's own shots;
/// <see cref="Refresh"/> updates the rest once per frame. A wall's bar, smaller than a tank's and drawn on its face,
/// shows only while it is standing and short of full hit points.
/// </summary>
public partial class CombatHud : CanvasLayer
{
    private const float KmhPerMs = 3.6f;
    private const float WallBarBelowTop = 1f;    // a wall's bar sits on its face, this far under the top, clear of any tank's bar behind it
    private const float WallBarWidth = 44f, WallBarHeight = 8f;   // smaller than a tank's, so the two don't read alike

    private Battlefield _field = null!;
    private HeadLookup _headOf = null!;
    private SniperVignette _vignette = null!;
    private CombatPopups _popups = null!;
    private HealthBars _wallBars = null!;
    private Prop[] _walls = Array.Empty<Prop>();
    private Reticle _reticle = null!;
    private StatusPanel _status = null!;
    private int _ownTankId;
    private bool _aiming = true;

    protected OrbitCamera Cam { get; private set; } = null!;

    /// <summary>The tank health bars, <c>tankBarCount</c> of them: <see cref="HealthBars.Set"/> each every frame.</summary>
    public HealthBars TankBars { get; private set; } = null!;

    /// <param name="ownTankId">The player's tank: only its shots get verdicts and damage numbers.</param>
    /// <param name="tankBarCount">How many tank bars <see cref="TankBars"/> has.</param>
    /// <param name="headOf">Where a tank's head is, or false for one that gets nothing drawn.</param>
    public void Build(OrbitCamera cam, Battlefield field, int ownTankId, int tankBarCount, HeadLookup headOf)
    {
        (Cam, _ownTankId, _headOf) = (cam, ownTankId, headOf);
        _vignette = Add(new SniperVignette());
        _popups = Add(new CombatPopups());
        _popups.SetCamera(cam.Camera);
        TankBars = Add(new HealthBars());
        TankBars.Setup(cam.Camera, tankBarCount);
        _wallBars = Add(new HealthBars());
        SetField(field);
        AddBehindSight();
        _reticle = Add(new Reticle());
        _status = Add(new StatusPanel());
    }

    /// <summary>A subclass's own layers, between the bars and the reticle (the sandbox's spotting debug lines).</summary>
    protected virtual void AddBehindSight()
    {
    }

    /// <summary>Takes the field a resync swapped in; the wall bars are rebuilt from its walls.</summary>
    public void SetField(Battlefield field)
    {
        _field = field;
        _walls = field.Props.Where(p => p.IsWall).ToArray();
        _wallBars.Setup(Cam.Camera, _walls.Length, WallBarWidth, WallBarHeight);
    }

    /// <summary>Shows the reticle and status panel while the player is at the controls; <paramref name="aiming"/> false (spectating) hides them.</summary>
    public void ShowOwn(bool aiming)
    {
        _aiming = aiming;
        _reticle.Visible = _status.Visible = aiming;
    }

    /// <summary>
    /// For the player's own shots: a verdict where a shell strikes a tank, and damage numbers over a tank's head (other
    /// tanks' shots are skipped) or on the face of a wall that took a shell or a splash (<see cref="PropFx.IsDirectHit"/>
    /// tells them apart). Hits on a tank <see cref="HeadLookup"/> won't place get nothing.
    /// </summary>
    public void OnShotEvent(in GameEvent e)
    {
        if (e.TankId != _ownTankId) return;
        switch (e.Type)
        {
            case GameEventType.TankHit when _headOf(e.TargetTankId, out Vector3 head):
                _popups.Verdict(e.Position.ToGodot(), e.Result);
                if (e.Damage > 0) _popups.Damage(head, e.Damage, splash: false);
                break;
            case GameEventType.SplashDamage when _headOf(e.TargetTankId, out Vector3 splashed):
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

    /// <summary>
    /// Once per frame: reticle, status panel, lamp (<paramref name="spotted"/>: an enemy sees the player), vignette and
    /// the walls' bars. The tank bars are the caller's.
    /// </summary>
    public void Refresh(in TankState player, bool spotted)
    {
        if (_aiming)
        {
            UpdateSight(player);
            _status.SetVehicle(player.Spec.Name);
            _status.UpdateState(player.Speed * KmhPerMs, player.Reload, player.Health, player.Spec.MaxHealth, player.Ammo, player.ApLeft, player.HeLeft);
            _status.SetSpotted(spotted);
        }
        _vignette.Visible = Cam.Sniper;
        for (int i = 0; i < _walls.Length; i++)
        {
            int left = _field.Durability(_walls[i].Id), full = _walls[i].MaxDurability;
            _wallBars.Set(i, WallHead(_walls[i]), left / (float)full, shown: left > 0 && left < full);
        }
    }

    protected T Add<T>(T control) where T : Node
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
        Camera3D cam = Cam.Camera;
        Vector3 point = impact.ToGodot();
        Vector2? screen = null;
        float circle = 0f;
        if (hasImpact && !cam.IsPositionBehind(point))
        {
            screen = cam.UnprojectPosition(point);
            circle = AimCirclePixels(player, launch.Origin, point, screen.Value);
        }
        _reticle.UpdateState(screen, 1f - player.Reload / player.Spec.ReloadTime, Cam.Sniper ? Cam.Zoom : 0f, circle);
    }

    /// <summary>
    /// The aim circle's radius around the predicted impact on screen: the circle is quoted at 100 m from the gun, so it
    /// measures <c>radius × gun distance / 100</c> there; a point that far to the camera's side of the impact is projected
    /// and its distance from <paramref name="screen"/> (the impact's own projection) measured.
    /// </summary>
    private float AimCirclePixels(in TankState player, SN.Vector3 muzzle, Vector3 impact, Vector2 screen)
    {
        Camera3D cam = Cam.Camera;
        float worldRadius = Dispersion.Radius(player.Spec, player.Bloom) * SN.Vector3.Distance(muzzle, impact.ToSim()) / Dispersion.ReferenceRange;
        Vector3 edge = impact + cam.GlobalBasis.X * worldRadius;
        return cam.IsPositionBehind(edge) ? 0f : cam.UnprojectPosition(edge).DistanceTo(screen);
    }
}
