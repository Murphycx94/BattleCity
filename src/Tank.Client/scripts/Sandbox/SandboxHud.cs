using Godot;
using Tank.Client.Cameras;
using Tank.Client.Overlay;
using Tank.Client.World;
using Tank.Sim;
using Tank.Sim.Combat;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;
using SN = System.Numerics;

namespace Tank.Client.Sandbox;

/// <summary>
/// The sandbox's HUD layer, back to front: the sniper vignette, hit popups, the targets' health bars, the reticle and
/// the status panel. <see cref="OnEvent"/> spawns hit verdicts and damage numbers; <see cref="Refresh"/> updates the
/// rest once per frame.
/// </summary>
public partial class SandboxHud : CanvasLayer
{
    private const float KmhPerMs = 3.6f;

    private OrbitCamera _camera = null!;
    private TargetRange _targets = null!;
    private Battlefield _field = null!;
    private SniperVignette _vignette = null!;
    private CombatPopups _popups = null!;
    private HealthBars _bars = null!;
    private Reticle _reticle = null!;
    private StatusPanel _status = null!;

    public void Build(OrbitCamera camera, TargetRange targets, Battlefield field)
    {
        (_camera, _targets, _field) = (camera, targets, field);
        _vignette = Add(new SniperVignette());
        _bars = Add(new HealthBars());
        _bars.Setup(camera.Camera, targets.Count);
        _popups = Add(new CombatPopups());
        _popups.SetCamera(camera.Camera);
        _reticle = Add(new Reticle());
        _status = Add(new StatusPanel());
    }

    /// <summary>A verdict where a shell strikes a tank, and damage numbers over a target's head (other tanks are skipped).</summary>
    public void OnEvent(in GameEvent e)
    {
        switch (e.Type)
        {
            case GameEventType.TankHit:
                _popups.Verdict(e.Position.ToGodot(), e.Result);
                if (e.Damage > 0 && _targets.TryGetHead(e.TargetTankId, out Vector3 head)) _popups.Damage(head, e.Damage, splash: false);
                break;
            case GameEventType.SplashDamage when _targets.TryGetHead(e.TargetTankId, out Vector3 splashed):
                _popups.Damage(splashed, e.Damage, splash: true);
                break;
        }
    }

    /// <summary>Once per frame, after the targets have been synced: reticle, status panel, vignette and health bars.</summary>
    public void Refresh(in TankState player)
    {
        UpdateSight(player);
        _status.UpdateState(player.Speed * KmhPerMs, player.Reload, player.Health, player.Spec.MaxHealth, player.Ammo, player.ApLeft, player.HeLeft);
        _vignette.Visible = _camera.Sniper;
        for (int i = 0; i < _targets.Count; i++)
        {
            var (head, health, alive) = _targets.Status(i);
            _bars.Set(i, head, health, alive);
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
        Vector2? screen = !hasImpact || cam.IsPositionBehind(point) ? null : cam.UnprojectPosition(point);
        float zoom = _camera.Sniper ? _camera.Zoom : 0f;
        _reticle.UpdateState(screen, 1f - player.Reload / player.Spec.ReloadTime, zoom, AimCirclePixels(player, launch.Origin, point));
    }

    /// <summary>
    /// The aim circle's radius at the predicted impact, as seen by the camera: the circle is quoted at 100 m from the gun,
    /// so it measures <c>radius × gun distance / 100</c> there, and shrinks with the camera's distance to that point.
    /// </summary>
    private float AimCirclePixels(in TankState player, SN.Vector3 muzzle, Vector3 impact)
    {
        Camera3D cam = _camera.Camera;
        float fromCamera = cam.GlobalPosition.DistanceTo(impact);
        if (fromCamera < 0.1f) return 0f;
        float worldRadius = Dispersion.Radius(player.Spec, player.Bloom) * SN.Vector3.Distance(muzzle, impact.ToSim()) / Dispersion.ReferenceRange;
        float focal = GetViewport().GetVisibleRect().Size.Y / 2f / MathF.Tan(Mathf.DegToRad(cam.Fov) / 2f);
        return worldRadius / fromCamera * focal;
    }
}
