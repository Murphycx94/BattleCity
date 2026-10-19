using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Ai;

/// <summary>
/// How a driver gets a tank to a point: steer toward it (turning on the spot when it lies more than
/// <see cref="TurnInPlace"/> off the heading), and brake in time to stop on it when asked to. Shared by the
/// <see cref="PatrolDriver"/> and the <see cref="SkirmishBot"/>.
/// </summary>
public static class Steering
{
    /// <summary>A point further off the heading than this (radians) is turned to on the spot.</summary>
    public static readonly float TurnInPlace = Angles.Deg(35);

    /// <summary>A point to stop at counts as reached once the hull has stopped within this of it, m.</summary>
    public const float StopRadius = 2f;

    /// <summary>The gun counts as laid once turret yaw and gun pitch are within this of the aim, radians.</summary>
    public static readonly float OnTarget = Angles.Deg(1);

    /// <summary>Whether the gun has come round to the hull-relative <paramref name="turret"/> yaw and <paramref name="gun"/> pitch.</summary>
    public static bool IsLaid(in TankState tank, float turret, float gun) =>
        MathF.Abs(Angles.Wrap(turret - tank.TurretYaw)) <= OnTarget && MathF.Abs(gun - tank.GunPitch) <= OnTarget;

    /// <summary>Slower than this (m/s) the hull counts as stopped.</summary>
    public const float StoppedSpeed = 0.3f;

    private static readonly float SteerDeadZone = Angles.Deg(2);

    /// <summary>Extra distance allowed for when braking to a stop, m.</summary>
    private const float BrakeMargin = 0.5f;

    /// <summary>Throttle and steer that take <paramref name="tank"/> toward <paramref name="point"/>, braking to stop there if <paramref name="stopThere"/>.</summary>
    public static (sbyte Throttle, sbyte Steer) Toward(in TankState tank, Vector2 point, bool stopThere)
    {
        float distance = Vector2.Distance(tank.Position, point);
        float off = Angles.Wrap(Angles.YawOf(point - tank.Position) - tank.Yaw);
        sbyte steer = (sbyte)(MathF.Abs(off) <= SteerDeadZone ? 0 : off > 0 ? -1 : 1);   // +yaw is to the left; Steer +1 turns right
        bool braking = stopThere && distance <= tank.Speed * tank.Speed / (2 * tank.Spec.Braking) + BrakeMargin;
        sbyte throttle = (sbyte)(MathF.Abs(off) > TurnInPlace || braking || distance <= StopRadius / 2 ? 0 : 1);
        return (throttle, steer);
    }

    /// <summary>Whether the hull has come to a stop within <see cref="StopRadius"/> of <paramref name="point"/>.</summary>
    public static bool StoppedAt(in TankState tank, Vector2 point) =>
        Vector2.Distance(tank.Position, point) <= StopRadius && MathF.Abs(tank.Speed) < StoppedSpeed;
}
