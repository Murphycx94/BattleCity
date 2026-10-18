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
    /// <summary>The hull box's rear face.</summary>
    HullRear,
}

/// <summary>
/// Scripted shooting at the practice targets, for screenshots (<c>--range-demo NAME</c>): the player's tank is parked
/// <see cref="Distance"/> m from target <see cref="Target"/>, <see cref="AngleOffFront"/> degrees round from straight
/// ahead of it (or at <see cref="Stand"/>), its hull <see cref="PlayerTurn"/> degrees to the left of facing the target,
/// lays its gun on the <see cref="Aim"/> face of that target's own armour (<see cref="AimLocal"/>, worked out from
/// <see cref="Spot"/>'s <see cref="TankSpec"/>, so it can't drift from the kind of tank standing there) and pulls the
/// trigger from <see cref="FireTick"/> on, once or (<see cref="KeepFiring"/>) whenever reloaded. A demo with
/// <see cref="DriveSeconds"/> fires nothing: from <see cref="FireTick"/> it drives at full throttle for that long, then
/// stops. The patrolling target stays parked at its start unless <see cref="PatrolDrives"/>.
/// </summary>
public sealed record RangeDemo(int Target, float AngleOffFront, float Distance, DemoAim Aim, AmmoType Ammo, bool KeepFiring,
    float PlayerTurn = 0f, float DriveSeconds = 0f, double FireAfter = RangeDemo.DefaultFireAfter, SN.Vector2? Stand = null,
    bool PatrolDrives = false)
{
    public const double DefaultFireAfter = 3.0;

    /// <summary>
    /// The earliest sim tick the demo pulls the trigger: <see cref="FireAfter"/> s of simulation time, not wall-clock
    /// time, so a startup hitch can't make it fire before the turret has settled, and the shot (its tick seeds the
    /// dispersion) is the same every run. The player's tank starts with AP loaded, so a demo with another round
    /// (<c>he</c>) first waits out the full reload that switching starts (<c>TryFire</c> in the sim): it then fires
    /// <c>ReloadTime</c> after tick 1 rather than at <see cref="FireTick"/>.
    /// </summary>
    public int FireTick => (int)Math.Ceiling(FireAfter * SimConstants.TickRate);

    /// <summary>
    /// Where the <c>lamp</c> demo parks the player: open ground 269 m west of the patrolling target's first bush patch,
    /// in sight of it, about 120 m from target 0. A medium tank standing still is out of the patrol's detection range
    /// there (229 m) and inside it for 3 s after a shot (282 m). Worked out for the medium tank only: a heavy tank is seen
    /// before its shot, a tank destroyer not even after it.
    /// </summary>
    public static readonly SN.Vector2 LampStand = new(38, 306);

    /// <summary>The demos' names, in the order <see cref="Find"/> documents them.</summary>
    public static IReadOnlyList<string> Names { get; } =
        new[] { "pen", "ricochet", "nopen", "he", "kill", "nopen-heavy", "kill-td", "turn", "wall", "breach", "fence", "ram", "bush", "lamp" };

    /// <summary>
    /// pen, ricochet, nopen, he or kill (at target 0, a medium tank); nopen-heavy (the far heavy tank's 200 mm turret
    /// front, head-on from the spawn's side, on the sight line the target tests keep clear: no light tank gets through;
    /// 52 m out, where even the heavy tank parks clear of the rocks); kill-td (the tank
    /// destroyer's rear until it is knocked out); turn (target 0 again, the hull starting 60° off: a tank destroyer has to
    /// turn its hull to bring the gun round). At the yard target (stage 2C): wall (one AP shot from 30 m in front, into
    /// the stone wall between), breach (HE from there until the wall is down and the target behind it), fence (AP at its
    /// rear from 30 m west by north-west, in through a panel of the fence gate, in sight of the target through the
    /// gate), ram (from 17 m north of it, 2.6 s at full throttle into the north wall: a heavy tank breaks through, the
    /// others are stopped); bush (target 0's rear from 50 m, through a bush
    /// patch). For the spotting lamp (stage 3): lamp (from <see cref="LampStand"/>, with the patrol driving, one AP shot at
    /// target 0 after 8 s, while the patrol waits in its first bush patch: the player is unseen until the shot, then seen);
    /// null for any other name. Each shoots, or drives, with whatever tank --tank gives the player (lamp is meant for the
    /// medium tank, the default).
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
        "wall" => new RangeDemo(SandboxMap.YardTarget, 0f, 30f, DemoAim.HullFront, AmmoType.AP, false),
        "breach" => new RangeDemo(SandboxMap.YardTarget, 0f, 30f, DemoAim.HullFront, AmmoType.HE, true),
        "fence" => new RangeDemo(SandboxMap.YardTarget, YardGateOffFront, 30f, DemoAim.HullRear, AmmoType.AP, false),
        "ram" => new RangeDemo(SandboxMap.YardTarget, YardNorthOffFront, 17f, DemoAim.HullFront, AmmoType.AP, false, DriveSeconds: 2.6f),
        "bush" => new RangeDemo(0, 210f, 50f, DemoAim.HullRear, AmmoType.AP, false),
        "lamp" => new RangeDemo(0, 0f, 0f, DemoAim.HullFront, AmmoType.AP, false, FireAfter: 8.0, Stand: LampStand, PatrolDrives: true),
        _ => null,
    };

    private const float DegreesPerRadian = 180f / MathF.PI;

    /// <summary>
    /// Degrees round from the yard target's front to west by north-west: the line from its rear in through the gate's
    /// north panel (straight west would park the player, and the camera behind it, against the rocks there), close enough
    /// to west that a sight line to the target passes through the gate too, not into the brick wall north of it.
    /// </summary>
    private static float YardGateOffFront => YardOffFront(new SN.Vector2(-1, -0.27f));

    /// <summary>Degrees round from the yard target's front to due north (−Z), square on to the yard's north wall.</summary>
    private static float YardNorthOffFront => YardOffFront(new SN.Vector2(0, -1));

    /// <summary>Degrees round from the yard target's front to the ground direction <paramref name="direction"/>.</summary>
    private static float YardOffFront(SN.Vector2 direction) =>
        Angles.Wrap(Angles.YawOf(direction) - SandboxMap.TargetSpots[SandboxMap.YardTarget].Yaw) * DegreesPerRadian;

    /// <summary>Ticks of full throttle for a driving demo, starting at <see cref="FireTick"/>.</summary>
    public int DriveTicks => (int)MathF.Round(DriveSeconds * SimConstants.TickRate);

    /// <summary>A driving demo (<see cref="DriveSeconds"/>): it drives into something instead of shooting at it.</summary>
    public bool IsDriving => DriveTicks > 0;

    /// <summary>The throttle on sim tick <paramref name="tick"/>: full for a driving demo's <see cref="DriveTicks"/> from <see cref="FireTick"/>, else none.</summary>
    public sbyte Throttle(int tick) => (sbyte)(tick >= FireTick && tick < FireTick + DriveTicks ? 1 : 0);

    /// <summary>
    /// The player's command for <paramref name="player"/>'s tick, in place of its own (the tests drive the sim with it
    /// too). Never steers. A shooting demo stands still, lays the gun on its aim point and pulls the trigger from
    /// <see cref="FireTick"/> on, once (until <paramref name="hasFired"/>) or, with <see cref="KeepFiring"/>, whenever
    /// loaded. A driving demo keeps the gun level and straight ahead (so a tank destroyer's hull doesn't turn after an
    /// aim outside its arc) and drives at <see cref="Throttle"/>.
    /// </summary>
    public InputCmd Command(in InputCmd player, in TankState tank, Battlefield field, bool hasFired)
    {
        if (IsDriving)
            return player with { Throttle = Throttle(player.Tick), Steer = 0, AimYaw = tank.Yaw, AimPitch = 0f, Fire = false, Ammo = Ammo };
        TankPose pose = TankPose.Compute(tank, field.Terrain);
        SN.Vector3 aim = AimPoint(field.Terrain.HeightAt(Spot.Position.X, Spot.Position.Y));
        var (yaw, pitch) = Angles.AimAngles(aim - pose.Trunnion(tank.Spec), player.AimYaw);
        bool fire = player.Tick >= FireTick && (KeepFiring || !hasFired);
        return player with { Throttle = 0, Steer = 0, AimYaw = yaw, AimPitch = pitch, Fire = fire, Ammo = Ammo };
    }

    public TargetSpot Spot => SandboxMap.TargetSpots[Target];

    /// <summary>Where the player's tank is parked: <see cref="Stand"/>, else round from the target.</summary>
    public SN.Vector2 PlayerPosition => Stand ?? Spot.Position + Angles.Forward(Spot.Yaw + Angles.Deg(AngleOffFront)) * Distance;

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
                DemoAim.HullRear => FaceMiddle(spec.HullArmor, 0f, front: false),
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
