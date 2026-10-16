using Tank.Sim;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;
using SN = System.Numerics;

namespace Tank.Client.Sandbox;

/// <summary>Which of the target's armour faces a <see cref="RangeDemo"/> lays its gun on: the middle of the face of the armour box named.</summary>
public enum DemoAim
{
    /// <summary>The hull box's front face.</summary>
    HullFront,
    /// <summary>The turret box's front face (a casemate's box is the hull's, centred on the hull origin).</summary>
    TurretFront,
    /// <summary>The turret box's rear face (a casemate's box is the hull's, centred on the hull origin).</summary>
    TurretRear,
}

/// <summary>
/// Scripted shooting at the practice targets, for screenshots (<c>--range-demo NAME</c>): the player's tank is parked
/// <see cref="Distance"/> m from target <see cref="Target"/>, <see cref="AngleOffFront"/> degrees round from straight
/// ahead of it, its hull <see cref="PlayerTurn"/> degrees to the left of facing the target, lays its gun on the
/// <see cref="Aim"/> face of that target's own armour (<see cref="AimLocal"/>, worked out from <see cref="Spot"/>'s
/// <see cref="TankSpec"/>, so it can't drift from the kind of tank standing there) and pulls the trigger from
/// <see cref="FireTick"/> on, once or (<see cref="KeepFiring"/>) whenever reloaded.
/// </summary>
public sealed record RangeDemo(int Target, float AngleOffFront, float Distance, DemoAim Aim, AmmoType Ammo, bool KeepFiring, float PlayerTurn = 0f)
{
    public const double FireAfter = 3.0;

    /// <summary>
    /// The earliest sim tick the demo pulls the trigger: <see cref="FireAfter"/> s of simulation time, not wall-clock
    /// time, so a startup hitch can't make it fire before the turret has settled, and the shot (its tick seeds the
    /// dispersion) is the same every run. The player's tank starts with AP loaded, so a demo with another round
    /// (<c>he</c>) first waits out the full reload that switching starts (<c>TryFire</c> in the sim): it then fires
    /// <c>ReloadTime</c> after tick 1 rather than at <see cref="FireTick"/>.
    /// </summary>
    public static readonly int FireTick = (int)Math.Ceiling(FireAfter * SimConstants.TickRate);

    /// <summary>The demos' names, in the order <see cref="Find"/> documents them.</summary>
    public static IReadOnlyList<string> Names { get; } = new[] { "pen", "ricochet", "nopen", "he", "kill", "nopen-heavy", "kill-td", "turn" };

    /// <summary>
    /// pen, ricochet, nopen, he or kill (at target 0, a medium tank); nopen-heavy (the far heavy tank's 200 mm turret
    /// front, head-on from the spawn's side, on the sight line the target tests keep clear: no light tank gets through;
    /// 52 m out, where even the heavy tank parks clear of the rocks); kill-td (the tank
    /// destroyer's rear until it is knocked out); turn (target 0 again, the hull starting 60° off: a tank destroyer has to
    /// turn its hull to bring the gun round); null for any other name. Each shoots with whatever tank --tank gives the
    /// player.
    /// </summary>
    public static RangeDemo? Find(string? name) => name switch
    {
        "pen" => new RangeDemo(0, 0f, 45f, DemoAim.HullFront, AmmoType.AP, false),
        "ricochet" => new RangeDemo(0, 76f, 45f, DemoAim.HullFront, AmmoType.AP, false),
        "nopen" => new RangeDemo(0, 60f, 45f, DemoAim.TurretFront, AmmoType.AP, false),
        "he" => new RangeDemo(0, 0f, 45f, DemoAim.HullFront, AmmoType.HE, false),
        "kill" => new RangeDemo(0, 0f, 45f, DemoAim.HullFront, AmmoType.AP, true),
        "nopen-heavy" => new RangeDemo(5, 0f, 52f, DemoAim.TurretFront, AmmoType.AP, false),
        "kill-td" => new RangeDemo(3, 180f, 45f, DemoAim.TurretRear, AmmoType.AP, true),
        "turn" => new RangeDemo(0, 0f, 45f, DemoAim.HullFront, AmmoType.AP, false, PlayerTurn: 60f),
        _ => null,
    };

    public TargetSpot Spot => SandboxMap.TargetSpots[Target];

    /// <summary>Where the player's tank is parked.</summary>
    public SN.Vector2 PlayerPosition => Spot.Position + Angles.Forward(Spot.Yaw + Angles.Deg(AngleOffFront)) * Distance;

    /// <summary>Heading from the player's tank to the target (where the demo camera looks).</summary>
    public float Bearing => Angles.YawOf(Spot.Position - PlayerPosition);

    /// <summary>The player's hull: facing the target, turned <see cref="PlayerTurn"/> degrees to the left.</summary>
    public float PlayerYaw => Angles.Wrap(Bearing + Angles.Deg(PlayerTurn));

    /// <summary>
    /// The aim point in the target's hull space (x right, y up, −z forward): the middle of the <see cref="Aim"/> face of
    /// <see cref="Spot"/>'s armour. A turret's box is centred <c>TurretOffset</c> ahead of the hull origin, a casemate's
    /// on it (<c>TankPose.TurretBox</c>); the target's turret is not turned.
    /// </summary>
    public SN.Vector3 AimLocal
    {
        get
        {
            TankSpec spec = Spot.Spec;
            float turretAhead = spec.HasTurret ? spec.TurretOffset : 0f;
            return Aim switch
            {
                DemoAim.HullFront => FaceMiddle(spec.HullArmor, 0f, front: true),
                DemoAim.TurretFront => FaceMiddle(spec.TurretArmor, turretAhead, front: true),
                DemoAim.TurretRear => FaceMiddle(spec.TurretArmor, turretAhead, front: false),
                _ => throw new InvalidOperationException($"Unknown {nameof(DemoAim)} {Aim}."),
            };
        }
    }

    /// <summary>Middle of the front (or rear) face of an armour box centred <paramref name="ahead"/> m ahead of the hull origin (hull space).</summary>
    private static SN.Vector3 FaceMiddle(ArmorBox box, float ahead, bool front) =>
        new(0, box.LocalCenter.Y, -ahead + (front ? -box.Length : box.Length) / 2);

    /// <summary>The aim point in world space, given the ground height under the target.</summary>
    public SN.Vector3 AimPoint(float groundY)
    {
        SN.Vector3 local = SN.Vector3.Transform(AimLocal, SN.Quaternion.CreateFromAxisAngle(SN.Vector3.UnitY, Spot.Yaw));
        return new SN.Vector3(Spot.Position.X + local.X, groundY + local.Y, Spot.Position.Y + local.Z);
    }
}
