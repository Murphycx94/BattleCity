using Godot;
using Tank.Client.Combat;
using Tank.Client.World;
using Tank.Sim.Vehicles;
using SN = System.Numerics;

namespace Tank.Client.Vehicles;

/// <summary>
/// One tank on screen: its <see cref="TankModel"/>, the smoke that pours from it once wrecked (<see cref="WreckSmoke"/>)
/// and, if asked for, the dust behind its tracks (<see cref="DustTrail"/>). The player's tank, the practice targets and
/// every tank of a match are all this. The caller poses it each frame with <see cref="Present"/>; the kill itself
/// (<see cref="Wreck"/>, <see cref="Restore"/>) and the blast cloud over it (<see cref="BattleFx"/>) stay the caller's to
/// time. The actor sits at the origin: the model moves itself to the pose in world space.
/// </summary>
public partial class TankActor : Node3D
{
    private const float HeadMargin = 0.6f;   // the head point floats this far above the turret's armour box

    private TankModel _model = null!;
    private WreckSmoke _smoke = null!;
    private DustTrail? _dust;
    private float _headHeight;

    /// <summary>The model, for what the actor doesn't wrap.</summary>
    public TankModel Model => _model;

    /// <summary>World point over the turret (where a health bar goes), as of the latest <see cref="Present"/>.</summary>
    public Vector3 Head { get; private set; }

    public bool IsWrecked => _model.IsWrecked;

    /// <summary>Barrel tip in world space, for muzzle effects.</summary>
    public Transform3D MuzzleTransform => _model.MuzzleTransform;

    /// <param name="paint">Overrides the look's paint (the practice targets are blue-grey).</param>
    /// <param name="dust">Whether it kicks up dust above 10 km/h.</param>
    public void Build(TankSpec spec, TankPaint? paint = null, bool dust = true)
    {
        _headHeight = spec.TurretArmor.Top + HeadMargin;   // 3.4 m over the hull origin on the medium tank
        _model = new TankModel();
        AddChild(_model);
        _model.Build(spec, paint);
        _smoke = new WreckSmoke();
        _model.AddChild(_smoke);
        _smoke.Build(_model.TurretRing);   // pours from the open turret ring (a casemate's roof)
        if (!dust) return;
        _dust = new DustTrail();
        _model.AddChild(_dust);
        _dust.Build(TankLooks.For(spec.Id).TrackX, spec.HullLength / 2f);
    }

    /// <summary>
    /// Poses and animates the tank for this frame: hull on <paramref name="pose"/>, turret and gun from <paramref name="state"/>,
    /// tracks rolling with its speed and <paramref name="yawRate"/> (rad/s, positive turning left), dust from its speed.
    /// <paramref name="shown"/> false hides the tank and stops its dust, which then starts afresh when it shows again.
    /// </summary>
    public void Present(in TankState state, TankPose pose, float yawRate, float delta, bool shown)
    {
        if (shown != Visible) Visible = shown;
        Head = (pose.Position + SN.Vector3.UnitY * _headHeight).ToGodot();
        _model.Apply(pose, state.TurretYaw, state.GunPitch);
        _model.Animate(state.Speed, yawRate, delta);
        _dust?.Update(shown ? state.Speed : 0f);
    }

    /// <summary>
    /// Knocks the tank out: the turret flies off, the paint chars and smoke rises. <paramref name="settled"/> skips the
    /// flight and the build-up, for a wreck seen for the first time already lying there (joining late, a resync).
    /// </summary>
    public void Wreck(float turretYaw, bool settled = false)
    {
        _model.Wreck(turretYaw, settled);
        if (settled) _smoke.StartSettled();
        else _smoke.Emitting = true;
    }

    /// <summary>Puts the turret back, the paint back to normal and stops the smoke (a revived tank).</summary>
    public void Restore()
    {
        _model.Restore();
        _smoke.Emitting = false;
    }

    /// <summary>
    /// The barrel's recoil and the hull's small rock for a shot: the model half of firing, without the muzzle flash (the
    /// caller spawns that at <see cref="MuzzleTransform"/> when it wants one, and not for a shot it only heard of).
    /// </summary>
    public void Kick(float turretYaw) => _model.Fire(turretYaw);
}
