using System.Numerics;
using Tank.Sim.Ai;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Terrain;
using Tank.Sim.Vehicles;
using Xunit.Abstractions;

namespace Tank.Sim.Tests;

/// <summary>A 400 m square of ground, 201 × 201 samples 2 m apart: room for the engagement range, which <see cref="TestWorld"/>'s 200 m lacks.</summary>
internal static class Ground400
{
    public const int Samples = 201;
    public const float Cell = 2f;
    public const float Size = (Samples - 1) * Cell;

    public static HeightField Flat() => Raised(_ => false, 0f);

    /// <summary>Flat at 0, and <paramref name="height"/> wherever <paramref name="raised"/> holds for a sample's x.</summary>
    public static HeightField Raised(Func<float, bool> raised, float height)
    {
        var heights = new float[Samples * Samples];
        for (int j = 0; j < Samples; j++)
        for (int i = 0; i < Samples; i++)
            heights[j * Samples + i] = raised(i * Cell) ? height : 0f;
        return new HeightField(Samples, Samples, Cell, heights);
    }

    public static Battlefield Field(HeightField terrain, params Prop[] props) => new(terrain, props, TestWorld.Border);

    public static Battlefield FlatField(params Prop[] props) => Field(Flat(), props);
}

/// <summary>Laying a bot's gun on a tank (stage 5 design §4.3): lead worked out twice, the drop, the gun's travel.</summary>
public sealed class GunneryTests
{
    private static readonly float East = Angles.YawOf(new Vector2(1, 0));
    private static readonly float North = Angles.YawOf(new Vector2(0, -1));

    private readonly ITestOutputHelper _output;

    public GunneryTests(ITestOutputHelper output) => _output = output;

    private static TankState Tank(TankSpec spec, int id, float x, float z, float yaw, float speed = 0f) =>
        new() { Id = id, Spec = spec, Position = new Vector2(x, z), Yaw = yaw, Speed = speed, Health = spec.MaxHealth, ApLeft = spec.Ap.Capacity };

    private static Vector3 Centre(in TankState t, HeightField terrain) => TankPose.Compute(t, terrain).HullBox(t.Spec).Center;

    private static Vector3 Trunnion(in TankState t, HeightField terrain) => TankPose.Compute(t, terrain).Trunnion(t.Spec);

    [Fact]
    public void A_still_target_is_aimed_at_its_hull_centre_raised_for_the_drop()
    {
        HeightField terrain = Ground400.Flat();
        TankState shooter = Tank(TankCatalog.Medium, 1, 100, 100, East);
        TankState target = Tank(TankCatalog.Medium, 2, 400, 100, North);

        Vector3 aim = Gunnery.AimPoint(shooter, target, terrain);

        Vector3 centre = Centre(target, terrain);
        float t = Vector3.Distance(centre, Trunnion(shooter, terrain)) / TankCatalog.Medium.Ap.Speed;
        float drop = 0.5f * SimConstants.Gravity * t * t;
        Assert.Equal(centre.X, aim.X, 0.01f);
        Assert.Equal(centre.Z, aim.Z, 0.01f);
        Assert.Equal(centre.Y + drop, aim.Y, 0.01f);
        Assert.InRange(drop, 0.70f, 0.75f);   // 300 m at 780 m/s: about 0.726 m
    }

    [Fact]
    public void A_crossing_target_is_led_by_its_flight_time()
    {
        HeightField terrain = Ground400.Flat();
        TankState shooter = Tank(TankCatalog.Medium, 1, 100, 100, East);
        TankState target = Tank(TankCatalog.Medium, 2, 400, 100, North, speed: 10f);

        Vector3 aim = Gunnery.AimPoint(shooter, target, terrain);

        Vector3 centre = Centre(target, terrain);
        float lead = centre.Z - aim.Z;   // north is −z
        Assert.Equal(centre.X, aim.X, 0.01f);
        Assert.Equal(3.85f, lead, 0.05f);
        float exact = 10f * Vector3.Distance(aim, Trunnion(shooter, terrain)) / TankCatalog.Medium.Ap.Speed;
        Assert.True(MathF.Abs(lead - exact) < 0.01f, $"lead {lead:F4} m, the fixed point {exact:F4} m");
    }

    [Fact]
    public void The_gun_reaches_only_within_its_elevation_and_depression()
    {
        // 60 m away on a 30 m shelf: about 25° up, the medium's gun stops at 20°.
        HeightField shelf = Ground400.Raised(x => x >= 150, 30f);
        TankState shooter = Tank(TankCatalog.Medium, 1, 100, 100, East);
        TankState high = Tank(TankCatalog.Medium, 2, 160, 100, North);
        Lay up = Gunnery.LayOn(shooter, Gunnery.AimPoint(shooter, high, shelf), shelf);
        Assert.False(up.CanLay);
        Assert.InRange(up.Pitch, Angles.Deg(22), Angles.Deg(28));

        // 100 m away and 20 m below: about −11°, the gun dips only 7°.
        HeightField ledge = Ground400.Raised(x => x <= 120, 20f);
        TankState low = Tank(TankCatalog.Medium, 2, 200, 100, North);
        Lay down = Gunnery.LayOn(shooter, Gunnery.AimPoint(shooter, low, ledge), ledge);
        Assert.False(down.CanLay);
        Assert.InRange(down.Pitch, Angles.Deg(-14), Angles.Deg(-9));

        HeightField flat = Ground400.Flat();
        TankState level = Tank(TankCatalog.Medium, 2, 400, 100, North);
        Assert.True(Gunnery.LayOn(shooter, Gunnery.AimPoint(shooter, level, flat), flat).CanLay);
    }

    [Fact]
    public void A_casemate_can_lay_on_a_target_behind_it_by_turning_the_hull()
    {
        HeightField terrain = Ground400.Flat();
        TankState td = Tank(TankCatalog.Td, 1, 300, 100, East);
        TankState behind = Tank(TankCatalog.Medium, 2, 100, 100, North);

        Lay lay = Gunnery.LayOn(td, Gunnery.AimPoint(td, behind, terrain), terrain);

        Assert.True(lay.CanLay);
        Assert.False(lay.Laid);
        Assert.Equal(Angles.YawOf(new Vector2(-1, 0)), lay.Yaw, 0.01f);
    }

    [Fact]
    public void The_lay_is_laid_once_the_gun_is_on_the_aim()
    {
        HeightField terrain = Ground400.Flat();
        TankState shooter = Tank(TankCatalog.Medium, 1, 100, 100, East);
        TankState target = Tank(TankCatalog.Medium, 2, 300, 160, North);
        Vector3 aim = Gunnery.AimPoint(shooter, target, terrain);
        Lay lay = Gunnery.LayOn(shooter, aim, terrain);
        Assert.True(lay.CanLay);
        Assert.False(lay.Laid);   // the turret still faces along the hull, 17° off

        TankPose pose = TankPose.Compute(shooter, terrain);
        var (turret, gun) = pose.ToHullAngles(Angles.Direction(lay.Yaw, lay.Pitch), 0f);
        TankState laid = shooter with { TurretYaw = turret, GunPitch = gun };
        Assert.True(Gunnery.LayOn(laid, aim, terrain).Laid);
    }

    [Fact]
    public void An_aim_point_that_is_not_finite_cannot_be_laid_on()
    {
        HeightField terrain = Ground400.Flat();
        TankState shooter = Tank(TankCatalog.Medium, 1, 100, 100, East);
        foreach (Vector3 aim in new[] { new Vector3(float.NaN, 1, 100), new Vector3(300, float.PositiveInfinity, 100), new Vector3(300, 1, float.NegativeInfinity) })
        {
            Lay lay = Gunnery.LayOn(shooter, aim, terrain);
            Assert.False(lay.CanLay, $"{aim}");
            Assert.False(lay.Laid, $"{aim}");
            Assert.True(float.IsFinite(lay.Yaw) && float.IsFinite(lay.Pitch), $"{aim}: {lay}");
        }
    }

    [Fact]
    public void Out_of_ap_the_lead_and_the_drop_are_worked_out_for_he()
    {
        HeightField terrain = Ground400.Flat();
        TankState full = Tank(TankCatalog.Medium, 1, 100, 100, East) with { HeLeft = TankCatalog.Medium.He.Capacity };
        TankState noAp = full with { ApLeft = 0 };
        TankState target = Tank(TankCatalog.Medium, 2, 400, 100, North, speed: 10f);
        Assert.Equal(AmmoType.AP, Gunnery.AmmoFor(full));
        Assert.Equal(AmmoType.HE, Gunnery.AmmoFor(noAp));
        Assert.Equal(AmmoType.AP, Gunnery.AmmoFor(noAp with { HeLeft = 0 }));   // out of both: as before

        Vector3 aim = Gunnery.AimPoint(noAp, target, terrain);

        Vector3 centre = Centre(target, terrain);
        float t = Vector3.Distance(aim with { Y = centre.Y }, Trunnion(noAp, terrain)) / TankCatalog.Medium.He.Speed;
        Assert.Equal(10f * t, centre.Z - aim.Z, 0.02f);   // north is −z
        Assert.Equal(centre.Y + 0.5f * SimConstants.Gravity * t * t, aim.Y, 0.02f);
        Vector3 apAim = Gunnery.AimPoint(full, target, terrain);
        Assert.True(aim.Y - apAim.Y > 0.3f, $"HE's drop {aim.Y - centre.Y:F2} m against AP's {apAim.Y - centre.Y:F2} m");
        Assert.True(apAim.Z - aim.Z > 0.8f, $"HE's lead {centre.Z - aim.Z:F2} m against AP's {centre.Z - apAim.Z:F2} m");
    }

    [Fact]
    public void Point_blank_below_the_gun_it_lays_on_the_turret_or_the_casemate_instead()
    {
        HeightField terrain = Ground400.Flat();
        TankState heavy = Tank(TankCatalog.Heavy, 1, 100, 100, East);

        // 12 m off, the medium's hull centre is about 7° down: the heavy's gun dips 6°. Its turret is within reach.
        TankState medium = Tank(TankCatalog.Medium, 2, 112, 100, North);
        Assert.False(Gunnery.LayOn(heavy, Gunnery.AimPoint(heavy, medium, terrain), terrain).CanLay);
        Lay lay = Gunnery.Aim(heavy, medium, terrain, out Vector3 aim);
        Assert.True(lay.CanLay);
        Vector3 turret = TankPose.Compute(medium, terrain).TurretBox(medium.Spec, medium.TurretYaw).Center;
        Assert.Equal(turret.X, aim.X, 0.01f);
        Assert.Equal(turret.Z, aim.Z, 0.01f);
        Assert.Equal(turret.Y, aim.Y, 0.01f);   // 12 m: the drop is a fraction of a millimetre

        // A tank destroyer's casemate: its box, over most of the hull.
        TankState td = Tank(TankCatalog.Td, 2, 112, 100, North);
        Assert.True(Gunnery.Aim(heavy, td, terrain, out aim).CanLay);
        Assert.Equal(TankPose.Compute(td, terrain).TurretBox(td.Spec, 0f).Center.Y, aim.Y, 0.01f);

        // Within reach, the hull centre as ever.
        TankState far = Tank(TankCatalog.Medium, 2, 300, 100, North);
        Assert.True(Gunnery.Aim(heavy, far, terrain, out aim).CanLay);
        Assert.Equal(Gunnery.AimPoint(heavy, far, terrain), aim);

        // 6 m from a light, neither its hull nor its turret: the hull centre's lay, which can't be laid.
        TankState light = Tank(TankCatalog.Light, 2, 106, 100, North);
        lay = Gunnery.Aim(heavy, light, terrain, out aim);
        Assert.False(lay.CanLay);
        Assert.Equal(Gunnery.AimPoint(heavy, light, terrain), aim);
    }

    /// <summary>One engagement in a real simulation: who fires at whom, from where, across which line, for how long.</summary>
    private sealed record Engagement(string Name, TankSpec Shooter, Vector2 ShooterAt, TankSpec Target, Vector2 TargetFrom, Vector2 Heading, int Ticks, bool Crossing);

    private static readonly Engagement[] Engagements =
    {
        new("medium on a crossing medium", TankCatalog.Medium, new(40, 200), TankCatalog.Medium, new(340, 16), new(0, 1), 27 * SimConstants.TickRate, Crossing: true),
        new("heavy on a crossing light", TankCatalog.Heavy, new(40, 200), TankCatalog.Light, new(240, 384), new(0, -1), 21 * SimConstants.TickRate, Crossing: true),
        new("light on a heavy slanting away", TankCatalog.Light, new(20, 300), TankCatalog.Heavy, new(240, 330), new(0.3f, -1), 30 * SimConstants.TickRate, Crossing: false),
        new("tank destroyer on a crossing medium", TankCatalog.Td, new(16, 200), TankCatalog.Medium, new(380, 120), new(0, 1), 14 * SimConstants.TickRate, Crossing: true),
    };

    /// <summary>A shell that struck the target: how far from the target's hull centre (as the hit test placed it) it passed.</summary>
    private readonly record struct Strike(float Range, float Up, float Across);

    private sealed record Outcome(int Fired, List<Strike> Strikes)
    {
        public int Hits => Strikes.Count;
        public float Nearest => Strikes.Count == 0 ? 0 : Strikes.Min(s => s.Range);
        public float Farthest => Strikes.Count == 0 ? 0 : Strikes.Max(s => s.Range);
        public float WorstUp => Strikes.Count == 0 ? 0 : Strikes.Max(s => MathF.Abs(s.Up));
        public float MeanUp => Strikes.Count == 0 ? 0 : Strikes.Average(s => s.Up);
        public float WorstAcross => Strikes.Count == 0 ? 0 : Strikes.Max(s => s.Across);
    }

    /// <summary>
    /// The shooter stands still and, every tick, lays on <paramref name="aimAt"/>'s point for the target and fires when the
    /// gun is laid; the target drives straight on at full throttle (kept alive). Firing starts once the target is up to
    /// speed and ends early enough for the last shell to land. <paramref name="exact"/>: the shooter's gun has no
    /// dispersion, so each shell flies exactly where it was laid. Each strike is measured against the target's hull
    /// centre where the hit test had it (after the step): up, and across the line of fire on the ground.
    /// </summary>
    private static Outcome Fire(Engagement e, Func<TankState, TankState, HeightField, Vector3> aimAt, bool exact = false)
    {
        const int RunUp = 3 * SimConstants.TickRate, LastShell = 2 * SimConstants.TickRate;
        var sim = new Simulation(Ground400.FlatField());
        HeightField terrain = sim.Field.Terrain;
        TankSpec gun = exact ? e.Shooter with { Dispersion = 0f } : e.Shooter;
        int shooter = sim.AddTank(gun, e.ShooterAt, Angles.YawOf(e.TargetFrom - e.ShooterAt), team: 0);
        int target = sim.AddTank(e.Target, e.TargetFrom, Angles.YawOf(e.Heading), team: 1);
        var inputs = new InputCmd[2];
        var flights = new Dictionary<int, Vector3>();
        var strikes = new List<Strike>();
        for (int k = 0; k < e.Ticks; k++)
        {
            int tick = sim.Tick + 1;
            TankState me = sim.Tanks[0], them = sim.Tanks[1];
            Lay lay = Gunnery.LayOn(me, aimAt(me, them, terrain), terrain);
            bool fire = k >= RunUp && k < e.Ticks - LastShell && lay.CanLay && lay.Laid;
            inputs[0] = new InputCmd(tick, 0, 0, lay.Yaw, lay.Pitch, fire);
            inputs[1] = new InputCmd(tick, 1, 0, them.Yaw, 0f, false);
            sim.Step(inputs);
            sim.TankRef(target).Health = e.Target.MaxHealth;
            Vector3 centre = Centre(sim.Tanks[1], terrain);
            foreach (GameEvent ev in sim.Events)
            {
                if (ev.Type == GameEventType.ShellFired && ev.TankId == shooter) flights[ev.ShellId] = ev.Velocity;
                if (ev.Type != GameEventType.TankHit || ev.TankId != shooter || ev.TargetTankId != target) continue;
                Vector3 off = ev.Position - centre;
                Vector2 along = Vector2.Normalize(new Vector2(flights[ev.ShellId].X, flights[ev.ShellId].Z));
                var level = new Vector2(off.X, off.Z);
                float across = (level - along * Vector2.Dot(level, along)).Length();
                strikes.Add(new Strike(Vector2.Distance(sim.Tanks[0].Position, sim.Tanks[1].Position), off.Y, across));
            }
        }
        Assert.Equal(0, sim.ShellCount);
        return new Outcome(flights.Count, strikes);
    }

    private static Vector3 Aimed(TankState me, TankState them, HeightField terrain) => Gunnery.AimPoint(me, them, terrain);

    private static Vector3 Unled(TankState me, TankState them, HeightField terrain) => Gunnery.AimPoint(me, them with { Speed = 0f }, terrain);

    /// <summary>The aim point lowered by the drop <see cref="Gunnery.AimPoint"/> allowed for (½·g·t² over the flight to it).</summary>
    private static Vector3 Undropped(TankState me, TankState them, HeightField terrain)
    {
        Vector3 aim = Gunnery.AimPoint(me, them, terrain);
        float t = Vector3.Distance(Trunnion(me, terrain), aim) / me.Spec.Ap.Speed;
        return aim - Vector3.UnitY * (0.5f * SimConstants.Gravity * t * t);
    }

    [Fact]
    public void Shells_without_dispersion_strike_moving_targets_at_the_hull_centre()
    {
        foreach (Engagement e in Engagements)
        {
            Outcome aimed = Fire(e, Aimed, exact: true), unled = Fire(e, Unled, exact: true), undropped = Fire(e, Undropped, exact: true);
            _output.WriteLine($"{e.Name}: {aimed.Hits}/{aimed.Fired} at {aimed.Nearest:F0}–{aimed.Farthest:F0} m, " +
                              $"up {aimed.MeanUp:+0.00;-0.00} m (worst {aimed.WorstUp:F2}), across at worst {aimed.WorstAcross:F2} m; " +
                              $"without the lead {unled.Hits}/{unled.Fired}; without the drop {undropped.Hits}/{undropped.Fired}, up {undropped.MeanUp:+0.00;-0.00} m");
            Assert.True(aimed.Fired >= 2, $"{e.Name}: only {aimed.Fired} shots");
            Assert.Equal(aimed.Fired, aimed.Hits);
            Assert.True(aimed.WorstUp <= 0.1f, $"{e.Name}: {aimed.WorstUp:F2} m off the centre's height");
            // The hit test checks a tick's whole flight against the target where it is at the end of the tick, so a
            // shell laid on the exact meeting point can find the hull up to one tick's drive further on.
            float tickDrive = e.Target.MaxForwardSpeed * SimConstants.Dt;
            Assert.True(aimed.WorstAcross <= tickDrive + 0.1f, $"{e.Name}: {aimed.WorstAcross:F2} m off the centre across the line of fire");
            Assert.True(undropped.Hits < aimed.Hits || undropped.MeanUp <= -0.4f,
                $"{e.Name}: without the drop {undropped.Hits} shells still strike, {undropped.MeanUp:F2} m off");
            if (e.Crossing) Assert.Equal(0, unled.Hits);
        }
    }

    [Fact]
    public void Shells_fired_at_the_aim_point_hit_targets_on_the_move()
    {
        int fired = 0, hits = 0, crossingFired = 0, crossingUnled = 0;
        foreach (Engagement e in Engagements)
        {
            Outcome aimed = Fire(e, Aimed), unled = Fire(e, Unled);
            _output.WriteLine($"{e.Name}: {aimed.Hits}/{aimed.Fired} hit at {aimed.Nearest:F0}–{aimed.Farthest:F0} m; without the lead {unled.Hits}/{unled.Fired}");
            fired += aimed.Fired;
            hits += aimed.Hits;
            if (!e.Crossing) continue;
            crossingFired += unled.Fired;
            crossingUnled += unled.Hits;
        }
        _output.WriteLine($"all: {hits}/{fired} hit ({100f * hits / fired:F0}%); crossing targets without the lead {crossingUnled}/{crossingFired}");
        Assert.True(fired >= 15, $"only {fired} shots");
        Assert.True(hits >= fired * 0.8f, $"{hits} of {fired} hit");
        Assert.True(crossingUnled <= crossingFired * 0.2f, $"without the lead {crossingUnled} of {crossingFired} still hit");
    }
}
