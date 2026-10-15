using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Combat;

/// <summary>
/// The aim circle (design §3.4). Each tick moving, hull turning and turret turning set a bloom target; bloom jumps up to
/// it at once and decays back towards it with time constant AimTime / 3, so a tank that stops is aimed within about one
/// AimTime. Firing adds <see cref="BloomFactors.Fire"/>. Shells leave inside the circle, more often near its centre.
/// </summary>
public static class Dispersion
{
    /// <summary>Aim circles are quoted at this range, m.</summary>
    public const float ReferenceRange = 100f;

    /// <summary>Bloom from motion this tick; each rate counts in proportion to the tank's top rate, capped at that.</summary>
    public static float Target(TankSpec spec, float speed, float hullTurnRate, float turretTurnRate)
    {
        BloomFactors f = spec.Bloom;
        return f.Moving * Share(speed, spec.MaxForwardSpeed)
               + f.HullTurn * Share(hullTurnRate, spec.HullTraverse)
               + f.TurretTurn * Share(turretTurnRate, spec.TurretTraverse);
    }

    /// <summary>One tick: rise to <paramref name="target"/> at once, or decay towards it.</summary>
    public static float Step(float bloom, float target, float aimTime) =>
        bloom <= target ? target : target + (bloom - target) * MathF.Exp(-SimConstants.Dt * 3f / aimTime);

    /// <summary>Aim-circle radius at <see cref="ReferenceRange"/>, m.</summary>
    public static float Radius(TankSpec spec, float bloom) => spec.Dispersion * (1f + bloom);

    /// <summary>
    /// <paramref name="direction"/> turned by a random offset inside a circle of <paramref name="radius"/> at 100 m: a
    /// uniform angle around the circle and a uniform share of the radius, so shots bunch towards the centre. Unit length.
    /// </summary>
    public static Vector3 Deflect(Vector3 direction, float radius, uint seed)
    {
        Vector3 forward = Vector3.Normalize(direction);
        Vector3 side = Vector3.Cross(forward, Vector3.UnitY);
        side = side.LengthSquared() < 1e-8f ? Vector3.UnitX : Vector3.Normalize(side);
        Vector3 up = Vector3.Cross(side, forward);
        float around = ShotRandom.Unit(seed, ShotRandom.DispersionAngle) * Angles.TwoPi;
        float off = radius * ShotRandom.Unit(seed, ShotRandom.DispersionRadius) / ReferenceRange;
        return Vector3.Normalize(forward + side * (off * MathF.Cos(around)) + up * (off * MathF.Sin(around)));
    }

    private static float Share(float rate, float top) => MathF.Min(MathF.Abs(rate) / top, 1f);
}
