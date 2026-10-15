using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
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
    private static readonly SN.Vector3 HullFront = new(0, 1.02f, -3.1f);
    private static readonly SN.Vector3 TurretFront = new(0, 2.24f, -1.75f);

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
    public float PlayerYaw
    {
        get
        {
            SN.Vector2 toTarget = Spot.Position - PlayerPosition;
            return MathF.Atan2(-toTarget.X, -toTarget.Y);
        }
    }

    /// <summary>The aim point in world space, given the ground height under the target.</summary>
    public SN.Vector3 AimPoint(float groundY)
    {
        SN.Vector3 local = SN.Vector3.Transform(AimLocal, SN.Quaternion.CreateFromAxisAngle(SN.Vector3.UnitY, Spot.Yaw));
        return new SN.Vector3(Spot.Position.X + local.X, groundY + local.Y, Spot.Position.Y + local.Z);
    }
}
