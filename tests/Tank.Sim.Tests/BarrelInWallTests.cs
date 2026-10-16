using System.Numerics;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Terrain;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Tests;

/// <summary>
/// Shots fired with the barrel pushed into a wall: one brick segment (id 1) across x = 99 … 101 at z = 100, its faces at
/// z = 99.7 and 100.3, with tanks parked nose to it 5 cm off, their barrels 0.2 m into it.
/// </summary>
public sealed class BarrelInWallTests
{
    private const int SegmentId = 1;
    private const float NorthFace = 99.7f, SouthFace = 100.3f, Gap = 0.05f;

    private static readonly Prop Segment =
        new WallLine(PropKind.BrickWall, new Vector2(99, 100), new Vector2(101, 100)).Segments(TestWorld.Flat(), SegmentId).Single();

    /// <summary>Where a <paramref name="spec"/> tank heading north (yaw 0) stands, its nose 5 cm off the segment's south face.</summary>
    private static Vector2 SouthOfIt(TankSpec spec) => new(100, SouthFace + Gap + spec.HullLength / 2);

    /// <summary>Where one heading south (yaw π) stands, its nose 5 cm off the north face.</summary>
    private static Vector2 NorthOfIt(TankSpec spec) => new(100, NorthFace - Gap - spec.HullLength / 2);

    /// <summary>Holds the gun level along <paramref name="aimYaw"/> (0 north, π south).</summary>
    private static InputCmd Hold(float aimYaw, bool fire = false, sbyte throttle = 0) => new(0, throttle, 0, aimYaw, 0f, fire);

    /// <summary>The segment worn down to its last hit point (any shell brings it down), and <paramref name="more"/> after it.</summary>
    private static Simulation WornDown(params Prop[] more)
    {
        var sim = new Simulation(TestWorld.FlatField(more.Prepend(Segment).ToArray()));
        sim.Field.Damage(sim.Field.IndexOf(SegmentId), Segment.MaxDurability - 1);
        return sim;
    }

    private static void AssertBarrelInTheSegment(Simulation sim, int slot)
    {
        Ballistics.Launch launch = Ballistics.Aim(sim.Tanks[slot], sim.Field);
        Assert.True(launch.Blocked, $"slot {slot}'s barrel is clear");
        Assert.Equal(SegmentId, launch.BlockPropId);
    }

    [Fact]
    public void A_barrel_in_a_wall_that_came_down_this_tick_bursts_on_whatever_else_is_in_it()
    {
        // Two heavy tanks nose to nose across the segment fire together. The north one's shell (the lower slot's, so it
        // goes off first) brings the segment down. The south one's barrel also holds a thin rock sunk into the wall's
        // south half (z = 100.12 … 100.28, past the face its barrel goes in by and short of its muzzle at z = 100.1), so
        // its shell bursts on that.
        var rock = new Prop(2, PropKind.Rock, new Obb2(new Vector2(100, 100.2f), new Vector2(0.5f, 0.08f), 0), -0.3f, 3.3f);
        Simulation sim = WornDown(rock);
        TankSpec heavy = TankCatalog.Heavy;
        int north = sim.AddTank(heavy, NorthOfIt(heavy), MathF.PI), south = sim.AddTank(heavy, SouthOfIt(heavy), 0f);
        AssertBarrelInTheSegment(sim, 0);
        AssertBarrelInTheSegment(sim, 1);   // the wall's face comes before the rock's

        sim.Step(new[] { Hold(MathF.PI, fire: true), Hold(0f, fire: true) });
        var events = sim.Events.ToArray().ToList();

        int shell = Assert.Single(events, e => e.Type == GameEventType.ShellFired && e.TankId == south).ShellId;
        GameEvent down = Assert.Single(events, e => e.Type == GameEventType.PropDestroyed);
        Assert.Equal((SegmentId, north), (down.PropId, down.TankId));
        GameEvent burst = Assert.Single(events, e => e.Type == GameEventType.ShellExploded && e.ShellId == shell);
        Assert.True(events.IndexOf(down) < events.IndexOf(burst), "the segment was down before the south shell went off");
        Assert.Equal(100.28f, burst.Position.Z, 1e-3f);   // on the rock's south face
        Assert.Equal(heavy.TrunnionHeight, burst.Position.Y, 1e-3f);
        Assert.DoesNotContain(events, e => e.Type == GameEventType.PropDamaged && e.TankId == south);
        Assert.DoesNotContain(events, e => e.Type == GameEventType.TankHit);
        Assert.Equal(0, sim.ShellCount);
    }

    /// <summary>
    /// A medium tank (slot 0) south of the segment with its barrel in it, gun level to the north; a heavy tank (slot 1)
    /// 12 m north of it (room to pass 15 km/h), heading south.
    /// </summary>
    private static (Simulation Sim, int Medium, int Heavy) RamSetup()
    {
        var sim = new Simulation(TestWorld.FlatField(Segment));
        TankSpec heavy = TankCatalog.Heavy;
        int medium = sim.AddTank(TankCatalog.Medium, SouthOfIt(TankCatalog.Medium), 0f);
        int rammer = sim.AddTank(heavy, new Vector2(100, NorthFace - 12f - heavy.HullLength / 2), MathF.PI);
        AssertBarrelInTheSegment(sim, 0);
        return (sim, medium, rammer);
    }

    /// <summary>The heavy drives flat out at the segment, the medium holds still and fires if <paramref name="fire"/>.</summary>
    private static InputCmd[] RamInputs(bool fire) => new[] { Hold(0f, fire), Hold(MathF.PI, throttle: 1) };

    [Fact]
    public void A_barrel_in_a_wall_a_tank_in_a_later_slot_rams_down_this_tick_fires_out_through_the_gap()
    {
        // A dry run (nobody fires) finds the tick the heavy rams the segment down.
        var (dry, _, dryHeavy) = RamSetup();
        int ramTick = 0;
        while (ramTick == 0 && dry.Tick < 5 * SimConstants.TickRate)
        {
            dry.Step(RamInputs(fire: false));
            foreach (GameEvent e in dry.Events)
                if (e.Type == GameEventType.PropDestroyed && e.TankId == dryHeavy) ramTick = dry.Tick;
        }
        Assert.True(ramTick > 0, "the heavy never rammed the segment down");

        // The same again, but the medium fires on that tick: its turn comes first, so its barrel is still in the segment
        // when it fires, and by the shell phase the segment is rubble.
        var (sim, medium, heavy) = RamSetup();
        var events = new List<GameEvent>();
        while (sim.Tick < ramTick)
        {
            sim.Step(RamInputs(fire: sim.Tick + 1 == ramTick));
            events.AddRange(sim.Events);
        }

        GameEvent fired = Assert.Single(events, e => e.Type == GameEventType.ShellFired);
        GameEvent down = Assert.Single(events, e => e.Type == GameEventType.PropDestroyed);
        Assert.Equal((ramTick, ramTick, heavy, 0), (fired.Tick, down.Tick, down.TankId, down.ShellId));
        Assert.True(events.IndexOf(fired) < events.IndexOf(down), "fired before the heavy rammed");
        Assert.DoesNotContain(events, e => e.Type == GameEventType.PropDamaged);
        GameEvent hit = Assert.Single(events, e => e.Type == GameEventType.TankHit);   // out of the muzzle into the heavy
        Assert.Equal((medium, heavy, fired.ShellId, ramTick), (hit.TankId, hit.TargetTankId, hit.ShellId, hit.Tick));
    }

    [Fact]
    public void Prop_events_from_a_barrel_burst_carry_the_shells_own_id()
    {
        Simulation sim = WornDown();
        int tank = sim.AddTank(Duel.Spec, SouthOfIt(Duel.Spec), 0f);
        AssertBarrelInTheSegment(sim, 0);
        sim.Step(new[] { Hold(0f, fire: true) });
        GameEvent[] events = sim.Events.ToArray();

        int shell = Assert.Single(events, e => e.Type == GameEventType.ShellFired).ShellId;
        Assert.NotEqual(tank, shell);
        GameEvent hurt = Assert.Single(events, e => e.Type == GameEventType.PropDamaged);
        GameEvent down = Assert.Single(events, e => e.Type == GameEventType.PropDestroyed);
        Assert.Equal((shell, shell), (hurt.ShellId, down.ShellId));
    }

    [Fact]
    public void Prop_events_from_a_shell_in_flight_carry_its_own_id()
    {
        // Across a duel: a fence line (ids 1–3) at z = 110, then one brick segment (4) worn to its last hit point at
        // z = 106, in front of the target. The shell knocks the middle panel flat, then strikes the segment and brings it down.
        HeightField flat = TestWorld.Flat();
        Prop[] fence = new WallLine(PropKind.Fence, new Vector2(97, 110), new Vector2(103, 110)).Segments(flat, 1).ToArray();
        Prop wall = new WallLine(PropKind.BrickWall, new Vector2(99, 106), new Vector2(101, 106)).Segments(flat, 4).Single();
        var duel = new Duel(targetYaw: MathF.PI, fence.Append(wall).ToArray());
        duel.Sim.Field.Damage(duel.Sim.Field.IndexOf(wall.Id), wall.MaxDurability - 1);
        var events = duel.Fire(duel.AimAt(duel.OnTarget(new Vector3(0, 1.02f, -3.1f))));

        GameEvent fired = Assert.Single(events, e => e.Type == GameEventType.ShellFired);
        Assert.NotEqual(fired.TankId, fired.ShellId);
        GameEvent[] props = events.Where(e => e.Type is GameEventType.PropDamaged or GameEventType.PropDestroyed).ToArray();
        Assert.Equal(new[] { (GameEventType.PropDestroyed, 2), (GameEventType.PropDamaged, 4), (GameEventType.PropDestroyed, 4) },
            props.Select(e => (e.Type, e.PropId)));
        Assert.All(props, e => Assert.Equal(fired.ShellId, e.ShellId));
    }
}
