using System.Numerics;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Terrain;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Ai;

/// <summary>
/// Laying a gun on a tank (stage 5 design §4.3), for the bots. A bot fires AP while it has any, then HE
/// (<see cref="AmmoFor"/>, 5C stalemate fix 2); the lead and the drop are worked out for the round it fires. The caller
/// passes the target only while its team sees it: the velocity used is the one it has now, never guessed.
/// </summary>
public static class Gunnery
{
    /// <summary>The flight time is worked out this many times, each from the point the last one led to.</summary>
    private const int LeadIterations = 2;

    /// <summary>
    /// The round a bot fires: AP while it has any, then HE; out of both, AP (it picks no target then; asking for HE would
    /// only throw a reload away).
    /// </summary>
    public static AmmoType AmmoFor(in TankState shooter) => shooter.ApLeft == 0 && shooter.HeLeft > 0 ? AmmoType.HE : AmmoType.AP;

    /// <summary>
    /// Where to point the gun to hit <paramref name="target"/>'s hull centre with the round <paramref name="shooter"/>
    /// fires (<see cref="AmmoFor"/>): the centre moved on by the target's velocity (Forward(yaw) × Speed, level) times the
    /// flight time, worked out twice (flight time = distance from the trunnion / the round's speed), then raised by ½·g·t²
    /// for the drop.
    /// </summary>
    public static Vector3 AimPoint(in TankState shooter, in TankState target, HeightField terrain) =>
        Lead(TankPose.Compute(shooter, terrain).Trunnion(shooter.Spec), ShellSpeed(shooter),
            TankPose.Compute(target, terrain).HullBox(target.Spec).Center, target);

    /// <summary>The speed of the round <paramref name="shooter"/> fires (<see cref="AmmoFor"/>), m/s.</summary>
    internal static float ShellSpeed(in TankState shooter) => shooter.Spec.Round(AmmoFor(shooter)).Speed;

    /// <summary>
    /// The gun laid on <paramref name="target"/> (5C stalemate fix 2): on its hull centre (<see cref="AimPoint"/>), or,
    /// when the gun can't reach that in pitch, on the centre of its turret box (a casemate's: the upper hull), led and
    /// raised for the drop the same way; a hit on either box is a hit (<see cref="ArmorHit.Find"/>). A few metres off, a
    /// lower tank's hull centre is further down than a heavy or a tank destroyer can dip its gun; its turret isn't. When
    /// neither can be laid on: the hull centre's lay (CanLay false) and aim point.
    /// </summary>
    public static Lay Aim(in TankState shooter, in TankState target, HeightField terrain, out Vector3 aim)
    {
        TankPose pose = TankPose.Compute(shooter, terrain);
        return Aim(shooter, pose, pose.Trunnion(shooter.Spec), ShellSpeed(shooter), target, terrain, out aim);
    }

    /// <summary><see cref="Aim(in TankState, in TankState, HeightField, out Vector3)"/> from a pose, trunnion and round speed already found.</summary>
    internal static Lay Aim(in TankState shooter, in TankPose pose, Vector3 trunnion, float shellSpeed, in TankState target,
        HeightField terrain, out Vector3 aim)
    {
        TankPose at = TankPose.Compute(target, terrain);
        aim = Lead(trunnion, shellSpeed, at.HullBox(target.Spec).Center, target);
        Lay lay = LayOn(shooter, pose, trunnion, aim);
        if (lay.CanLay) return lay;
        Vector3 upper = Lead(trunnion, shellSpeed, at.TurretBox(target.Spec, target.TurretYaw).Center, target);
        Lay high = LayOn(shooter, pose, trunnion, upper);
        if (!high.CanLay) return lay;
        aim = upper;
        return high;
    }

    /// <summary><paramref name="centre"/> led by <paramref name="target"/>'s velocity over the flight time, worked out twice, and raised for the drop.</summary>
    private static Vector3 Lead(Vector3 trunnion, float shellSpeed, Vector3 centre, in TankState target)
    {
        Vector2 ground = Angles.Forward(target.Yaw) * target.Speed;
        var velocity = new Vector3(ground.X, 0f, ground.Y);
        Vector3 aim = centre;
        float t = 0f;
        for (int i = 0; i < LeadIterations; i++)
        {
            t = Vector3.Distance(aim, trunnion) / shellSpeed;
            aim = centre + velocity * t;
        }
        aim.Y += 0.5f * SimConstants.Gravity * t * t;
        return aim;
    }

    /// <summary>
    /// World aim angles for <paramref name="aim"/> (the command's AimYaw and AimPitch). CanLay: the gun pitch it needs is
    /// within −GunDepression … GunElevation (a casemate's hull turns for the yaw, <see cref="TankMotion.TraverseHull"/>, so
    /// only pitch limits it); Laid: <see cref="Steering.IsLaid"/> on those angles. An aim point that isn't finite can't be
    /// laid on: the gun's present yaw and a level pitch, neither CanLay nor Laid.
    /// </summary>
    public static Lay LayOn(in TankState shooter, Vector3 aim, HeightField terrain)
    {
        TankPose pose = TankPose.Compute(shooter, terrain);
        return LayOn(shooter, pose, pose.Trunnion(shooter.Spec), aim);
    }

    /// <summary><see cref="LayOn(in TankState, Vector3, HeightField)"/> from a pose and trunnion already found.</summary>
    internal static Lay LayOn(in TankState shooter, in TankPose pose, Vector3 trunnion, Vector3 aim)
    {
        if (!float.IsFinite(aim.X) || !float.IsFinite(aim.Y) || !float.IsFinite(aim.Z))
            return new Lay(Angles.Wrap(shooter.Yaw + shooter.TurretYaw), 0f, false, false);
        var (yaw, pitch) = Angles.AimAngles(aim - trunnion, shooter.Yaw);
        var (turret, gun) = pose.ToHullAngles(Angles.Direction(yaw, pitch), shooter.TurretYaw);
        bool canLay = gun <= shooter.Spec.GunElevation && gun >= -shooter.Spec.GunDepression;
        return new Lay(yaw, pitch, canLay, Steering.IsLaid(shooter, turret, gun));
    }
}

/// <summary>
/// The gun laid on an aim point: world <paramref name="Yaw"/> and <paramref name="Pitch"/> from the trunnion (what the
/// command asks for), whether the gun can reach that pitch at all, and whether it is already there.
/// </summary>
public readonly record struct Lay(float Yaw, float Pitch, bool CanLay, bool Laid);
