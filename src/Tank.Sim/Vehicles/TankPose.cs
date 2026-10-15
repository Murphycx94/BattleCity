using System.Numerics;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Terrain;

namespace Tank.Sim.Vehicles;

/// <summary>World placement of a hull resting on the terrain, recomputed from <see cref="TankState"/> whenever needed.</summary>
public readonly record struct TankPose(Vector3 Position, Quaternion Rotation)
{
    /// <summary>Fraction of the hull's half-size where the ground is sampled; inside the tracks so edges don't dig in.</summary>
    private const float ContactSpread = 0.8f;

    public Vector3 Forward => Vector3.Transform(-Vector3.UnitZ, Rotation);

    public Vector3 Up => Vector3.Transform(Vector3.UnitY, Rotation);

    public static TankPose Compute(in TankState t, HeightField terrain)
    {
        TankSpec spec = t.Spec;
        Vector2 center = t.Position;
        Vector2 f = Angles.Forward(t.Yaw) * (spec.HullLength * 0.5f * ContactSpread);
        Vector2 r = Angles.Right(t.Yaw) * (spec.HullWidth * 0.5f * ContactSpread);
        float frontRight = Height(terrain, center + f + r), frontLeft = Height(terrain, center + f - r);
        float backRight = Height(terrain, center - f + r), backLeft = Height(terrain, center - f - r);
        float front = (frontRight + frontLeft) * 0.5f, back = (backRight + backLeft) * 0.5f;
        float right = (frontRight + backRight) * 0.5f, left = (frontLeft + backLeft) * 0.5f;
        float pitch = MathF.Atan2(front - back, f.Length() * 2);
        float roll = MathF.Atan2(right - left, r.Length() * 2);
        float y = MathF.Max((front + back) * 0.5f, Height(terrain, center)); // don't sink into crests
        return new TankPose(new Vector3(center.X, y, center.Y), Quaternion.CreateFromYawPitchRoll(t.Yaw, pitch, roll));
    }

    /// <summary>Gun pivot in world space.</summary>
    public Vector3 Trunnion(TankSpec spec) =>
        Position + Vector3.Transform(new Vector3(0, spec.TrunnionHeight, -spec.TurretOffset), Rotation);

    public Vector3 GunDirection(float turretYaw, float gunPitch) =>
        Vector3.Transform(Angles.Direction(turretYaw, gunPitch), Rotation);

    public Vector3 Muzzle(TankSpec spec, float turretYaw, float gunPitch) =>
        Trunnion(spec) + GunDirection(turretYaw, gunPitch) * spec.GunLength;

    /// <summary>The hull's armour box in world space; it tilts with the hull.</summary>
    public OrientedBox HullBox(TankSpec spec) =>
        new(Position + Vector3.Transform(spec.HullArmor.LocalCenter, Rotation), spec.HullArmor.HalfExtents, Rotation);

    /// <summary>The turret's armour box in world space: centred over the turret ring, tilted with the hull, turned by <paramref name="turretYaw"/>.</summary>
    public OrientedBox TurretBox(TankSpec spec, float turretYaw)
    {
        Vector3 ring = new(0, 0, -spec.TurretOffset);
        Quaternion rotation = Rotation * Quaternion.CreateFromAxisAngle(Vector3.UnitY, turretYaw);
        return new OrientedBox(Position + Vector3.Transform(ring + spec.TurretArmor.LocalCenter, Rotation), spec.TurretArmor.HalfExtents, rotation);
    }

    /// <summary>Turret yaw and gun pitch (hull-relative) that point the barrel along world <paramref name="direction"/>.</summary>
    public (float TurretYaw, float GunPitch) ToHullAngles(Vector3 direction, float fallbackYaw = 0f) =>
        Angles.AimAngles(Vector3.Transform(direction, Quaternion.Inverse(Rotation)), fallbackYaw);

    private static float Height(HeightField terrain, Vector2 p) => terrain.HeightAt(p.X, p.Y);
}
