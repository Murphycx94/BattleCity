using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;
using SN = System.Numerics;

namespace Tank.Client.Sandbox;

/// <summary>
/// Scripted shooting at the practice targets, for screenshots (<c>--range-demo NAME</c>): the player's tank is parked
/// <see cref="Distance"/> m from target <see cref="Target"/>, <see cref="AngleOffFront"/> degrees round from straight
/// ahead of it, lays its gun on <see cref="AimLocal"/> (target hull space: x right, y up, −z forward) and fires from
/// <see cref="FireAfter"/> s on, once or (<see cref="KeepFiring"/>) whenever reloaded.
/// </summary>
public sealed record RangeDemo(int Target, float AngleOffFront, float Distance, SN.Vector3 AimLocal, AmmoType Ammo, bool KeepFiring)
{
    public const double FireAfter = 3.0;
    private static readonly SN.Vector3 HullFront = FrontFace(TankCatalog.Medium.HullArmor, 0f);
    private static readonly SN.Vector3 TurretFront = FrontFace(TankCatalog.Medium.TurretArmor, TankCatalog.Medium.TurretOffset);

    /// <summary>pen, ricochet, nopen, he or kill; null for any other name.</summary>
    public static RangeDemo? Find(string? name) => name switch
    {
        "pen" => new RangeDemo(0, 0f, 45f, HullFront, AmmoType.AP, false),
        "ricochet" => new RangeDemo(0, 76f, 45f, HullFront, AmmoType.AP, false),
        "nopen" => new RangeDemo(0, 60f, 45f, TurretFront, AmmoType.AP, false),
        "he" => new RangeDemo(0, 0f, 45f, HullFront, AmmoType.HE, false),
        "kill" => new RangeDemo(0, 0f, 45f, HullFront, AmmoType.AP, true),
        _ => null,
    };

    public TargetSpot Spot => SandboxMap.TargetSpots[Target];

    /// <summary>Where the player's tank is parked.</summary>
    public SN.Vector2 PlayerPosition => Spot.Position + Angles.Forward(Spot.Yaw + Angles.Deg(AngleOffFront)) * Distance;

    /// <summary>The player's hull faces the target.</summary>
    public float PlayerYaw => Angles.YawOf(Spot.Position - PlayerPosition);

    /// <summary>Middle of a target armour box's front face (hull space), for a box centred <paramref name="ahead"/> m ahead of the hull origin.</summary>
    private static SN.Vector3 FrontFace(ArmorBox box, float ahead) => new(0, box.LocalCenter.Y, -ahead - box.Length / 2);

    /// <summary>The aim point in world space, given the ground height under the target.</summary>
    public SN.Vector3 AimPoint(float groundY)
    {
        SN.Vector3 local = SN.Vector3.Transform(AimLocal, SN.Quaternion.CreateFromAxisAngle(SN.Vector3.UnitY, Spot.Yaw));
        return new SN.Vector3(Spot.Position.X + local.X, groundY + local.Y, Spot.Position.Y + local.Z);
    }
}
