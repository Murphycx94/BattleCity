using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Terrain;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Tests;

/// <summary>Tanks driving north (−Z) into a three-segment line across x = 97 … 103 at z = 90: ids 1, 2, 3.</summary>
public sealed class RamTests
{
    private const float LineZ = 90f;
    private static readonly InputCmd Full = TestWorld.Cmd(throttle: 1);

    private static Battlefield Across(PropKind kind) =>
        TestWorld.FlatField(new WallLine(kind, new Vector2(97, LineZ), new Vector2(103, LineZ)).Segments(TestWorld.Flat(), 1).ToArray());

    /// <summary>
    /// A <paramref name="spec"/> tank at rest heading north at x = 100, its nose <paramref name="gap"/> m short of the near
    /// (south) face of a line of <paramref name="kind"/> (<see cref="Across"/>).
    /// </summary>
    private static TankState Facing(TankSpec spec, PropKind kind, float gap) =>
        new() { Id = 1, Spec = spec, Position = new Vector2(100, LineZ + WallLine.Thickness(kind) / 2 + gap + spec.HullLength / 2), Yaw = 0f };

    private static bool AllDown(Battlefield field) => field.Props.All(p => !field.IsStanding(p.Id));

    private static bool AllStanding(Battlefield field) => field.Props.All(p => field.IsStanding(p.Id));

    [Fact]
    public void Only_the_heavy_tank_rams_brick_walls_and_from_15_kmh()
    {
        Assert.Equal(15f / 3.6f, TankCatalog.Heavy.BrickWallRamSpeed, 1e-5f);
        Assert.All(TankCatalog.All.Where(s => s != TankCatalog.Heavy), s => Assert.Equal(float.PositiveInfinity, s.BrickWallRamSpeed));
    }

    [Theory]
    [InlineData("light")]
    [InlineData("medium")]
    [InlineData("heavy")]
    [InlineData("td")]
    public void Any_tank_knocks_fences_flat_and_drives_on_as_if_they_were_not_there(string id)
    {
        TankSpec spec = TankCatalog.ById(id);
        Battlefield fenced = Across(PropKind.Fence);
        TankState through = Facing(spec, PropKind.Fence, 10f), open = Facing(spec, PropKind.Fence, 10f);
        var felled = new List<int>();
        for (int i = 0; i < 4 * SimConstants.TickRate; i++)
        {
            TankMotion.Drive(ref through, Full, fenced, ReadOnlySpan<Obb2>.Empty, felled);
            TankMotion.Drive(ref open, Full, TestWorld.FlatField(), ReadOnlySpan<Obb2>.Empty);
        }
        Assert.Equal(new[] { 1, 2, 3 }, felled.Order());   // every panel the hull touched (it is wider than one)
        Assert.True(AllDown(fenced));
        Assert.Equal(open.Position, through.Position);
        Assert.Equal(open.Speed, through.Speed);
    }

    [Fact]
    public void A_heavy_tank_above_15_kmh_breaks_through_a_brick_wall_at_half_its_speed()
    {
        Battlefield field = Across(PropKind.BrickWall);
        TankState heavy = Facing(TankCatalog.Heavy, PropKind.BrickWall, 12f);   // room to pass 15 km/h first
        var felled = new List<int>();
        float before = 0f;
        int ticks = 0;
        while (felled.Count == 0 && ticks++ < 10 * SimConstants.TickRate)
        {
            before = heavy.Speed;
            TankMotion.Drive(ref heavy, Full, field, ReadOnlySpan<Obb2>.Empty, felled);
        }

        Assert.Equal(new[] { 1, 2, 3 }, felled.Order());   // all in one tick: the hull spans the three segments
        Assert.True(AllDown(field));
        Assert.True(before > TankCatalog.Heavy.BrickWallRamSpeed, $"rammed at {before * 3.6f:F1} km/h");
        float accelerated = MathF.Min(before + TankCatalog.Heavy.Acceleration * SimConstants.Dt, TankCatalog.Heavy.MaxForwardSpeed);
        Assert.Equal(accelerated * 0.5f, heavy.Speed, 1e-4f);   // halved once, not once per segment

        TestWorld.Drive(ref heavy, field, Full, 2 * SimConstants.TickRate);
        Assert.True(heavy.Position.Y < LineZ - TankCatalog.Heavy.HullLength / 2, $"stuck at z = {heavy.Position.Y}");
    }

    [Fact]
    public void A_heavy_tank_slower_than_15_kmh_is_stopped_by_a_brick_wall()
    {
        Battlefield field = Across(PropKind.BrickWall);
        TankState heavy = Facing(TankCatalog.Heavy, PropKind.BrickWall, 2f);   // reaches only about 12 km/h by the wall
        float fastest = 0f;
        for (int i = 0; i < 3 * SimConstants.TickRate; i++)
        {
            TankMotion.Drive(ref heavy, Full, field, ReadOnlySpan<Obb2>.Empty);
            fastest = MathF.Max(fastest, heavy.Speed);
        }
        Assert.True(fastest < TankCatalog.Heavy.BrickWallRamSpeed, $"reached {fastest * 3.6f:F1} km/h");
        Assert.True(AllStanding(field));
        Assert.True(heavy.Position.Y > LineZ + 0.3f + TankCatalog.Heavy.HullLength / 2 - 0.01f);
    }

    [Theory]
    [InlineData("medium", PropKind.BrickWall)]
    [InlineData("light", PropKind.BrickWall)]
    [InlineData("heavy", PropKind.StoneWall)]
    public void Other_rams_are_stopped_like_a_rock(string id, PropKind kind)
    {
        Battlefield field = Across(kind);
        TankState tank = Facing(TankCatalog.ById(id), kind, 25f);   // flat out by the time it arrives
        TestWorld.Drive(ref tank, field, Full, 5 * SimConstants.TickRate);
        Assert.True(AllStanding(field));
        Assert.True(tank.Position.Y > LineZ + WallLine.Thickness(kind) / 2 + tank.Spec.HullLength / 2 - 0.01f);
    }

    [Fact]
    public void Rubble_holds_a_tank_to_70_percent_of_its_top_speed()
    {
        // A long brick wall shot to rubble along the tank's whole run, heading east along it.
        Prop[] wall = new WallLine(PropKind.BrickWall, new Vector2(60, 100), new Vector2(160, 100)).Segments(TestWorld.Flat(), 1).ToArray();
        Battlefield field = TestWorld.FlatField(wall);
        foreach (Prop segment in wall) field.Destroy(field.IndexOf(segment.Id));
        TankState onRubble = TestWorld.Tank(70, 100, yaw: -MathF.PI / 2), open = TestWorld.Tank(70, 100, yaw: -MathF.PI / 2);
        TestWorld.Drive(ref onRubble, field, Full, 6 * SimConstants.TickRate);
        TestWorld.Drive(ref open, TestWorld.FlatField(), Full, 6 * SimConstants.TickRate);

        Assert.True(onRubble.Position.X < 155, "still on the rubble");
        Assert.Equal(TankCatalog.Medium.MaxForwardSpeed, open.Speed, 1e-4f);
        Assert.Equal(TankCatalog.Medium.MaxForwardSpeed * TankMotion.RubbleSpeedFactor, onRubble.Speed, 1e-4f);
    }

    /// <summary>One brick segment across x = 98 … 100 at z = 100, its face at z = 100.3 and its rubble's edge at z = 100.8.</summary>
    private static readonly Prop RubbleWall =
        new WallLine(PropKind.BrickWall, new Vector2(98, 100), new Vector2(100, 100)).Segments(TestWorld.Flat(), 1).Single();

    /// <summary>
    /// A medium tank heading east along z = <paramref name="z"/> flat out past <see cref="RubbleWall"/>, shot to rubble:
    /// its lowest speed from when it first reaches top speed (before its nose gets to the rubble) until its tail is past it.
    /// </summary>
    private static float LowestSpeedPastTheRubble(float z, bool overlapsTheSpread)
    {
        Battlefield field = TestWorld.FlatField(RubbleWall);
        field.Destroy(field.IndexOf(RubbleWall.Id));
        TankState tank = TestWorld.Tank(60, z, yaw: -MathF.PI / 2);
        float half = tank.Spec.HullLength / 2, top = tank.Spec.MaxForwardSpeed;
        Obb2 over = TankMotion.Footprint(tank.Spec, new Vector2(99, z), tank.Yaw);   // the hull right over the rubble
        Assert.False(Obb2.TryGetPenetration(over, RubbleWall.Footprint, out _), "clear of where the wall stood");
        Assert.Equal(overlapsTheSpread, Obb2.TryGetPenetration(over, RubbleWall.RubbleFootprint, out _));

        float? noseAtTopSpeed = null;
        float lowest = float.MaxValue;
        for (int i = 0; i < 10 * SimConstants.TickRate && tank.Position.X - half <= 100; i++)
        {
            TankMotion.Drive(ref tank, Full, field, ReadOnlySpan<Obb2>.Empty);
            if (noseAtTopSpeed is null && tank.Speed >= top - 1e-4f) noseAtTopSpeed = tank.Position.X + half;
            if (noseAtTopSpeed is not null) lowest = MathF.Min(lowest, tank.Speed);
        }
        Assert.True(noseAtTopSpeed < 98f, $"reached top speed with its nose at x = {noseAtTopSpeed}, not short of the rubble");
        Assert.True(tank.Position.X - half > 100, $"its tail is still on the rubble at x = {tank.Position.X - half}");
        return lowest;
    }

    [Fact]
    public void A_hull_just_clear_of_the_rubble_is_not_slowed()
    {
        // 1.65 m south of the rubble's edge: the hull reaches 1.6 m to either side, so it passes 5 cm clear.
        float lowest = LowestSpeedPastTheRubble(100.8f + 1.65f, overlapsTheSpread: false);
        Assert.Equal(TankCatalog.Medium.MaxForwardSpeed, lowest, 1e-4f);
    }

    [Fact]
    public void A_hull_over_only_the_spread_of_the_rubble_is_slowed_to_70_percent()
    {
        // The hull's edge 25 cm past the wall's face: clear of where the wall stood, on the half metre its rubble spread.
        float lowest = LowestSpeedPastTheRubble(100.3f + 0.25f + 1.6f, overlapsTheSpread: true);
        Assert.Equal(TankCatalog.Medium.MaxForwardSpeed * TankMotion.RubbleSpeedFactor, lowest, 1e-4f);
    }

    [Fact]
    public void Turning_on_the_spot_into_a_fence_knocks_it_away_from_the_hull()
    {
        // A fence panel square across the hull's right-front corner: turning right on the spot swings the corner into it.
        Prop panel = new WallLine(PropKind.Fence, new Vector2(102.6f, 95.5f), new Vector2(102.6f, 97.5f)).Segments(TestWorld.Flat(), 1).Single();
        var sim = new Simulation(TestWorld.FlatField(panel));
        int id = sim.AddTank(TankCatalog.Medium, new Vector2(100, 100), 0f);
        Assert.False(Obb2.TryGetPenetration(TankMotion.Footprint(TankCatalog.Medium, new Vector2(100, 100), 0f), panel.Footprint, out _), "clear to start");
        var events = new List<GameEvent>();
        for (int i = 0; i < 2 * SimConstants.TickRate && sim.Field.IsStanding(1); i++)
        {
            sim.Step(new[] { TestWorld.Cmd(steer: 1) });
            events.AddRange(sim.Events);
        }

        GameEvent down = Assert.Single(events, e => e.Type == GameEventType.PropDestroyed);
        Assert.Equal((id, 0), (down.TankId, down.ShellId));
        Vector3 away = Vector3.Normalize(new Vector3(panel.Footprint.Center.X - 100, 0, panel.Footprint.Center.Y - 100));
        Assert.True(Vector3.Dot(away, down.Velocity) > 0.999f, $"pushed {down.Velocity}");
        Assert.NotEqual(0f, sim.Tanks[0].Yaw);   // the turn went on
    }

    [Fact]
    public void Ramming_allocates_nothing_once_warmed_up()
    {
        // Every 3 s a medium tank is put back 4.65 m short of a row of fences and a heavy tank 5.95 m short of a brick
        // wall, everything stands again, and both drive flat out: the medium knocks fences down, the heavy breaks through.
        HeightField flat = TestWorld.Flat();
        var props = new List<Prop>();
        props.AddRange(new WallLine(PropKind.Fence, new Vector2(94, 90), new Vector2(106, 90)).Segments(flat, 1));
        props.AddRange(new WallLine(PropKind.BrickWall, new Vector2(124, 90), new Vector2(136, 90)).Segments(flat, props.Count + 1));
        var sim = new Simulation(TestWorld.Field(flat, props.ToArray()));
        int medium = sim.AddTank(TankCatalog.Medium, new Vector2(100, 98), 0f), heavy = sim.AddTank(TankCatalog.Heavy, new Vector2(130, 100), 0f);
        var inputs = new[] { Full, Full };
        int fences = 0, walls = 0;
        void Run(int cycles)
        {
            for (int cycle = 0; cycle < cycles; cycle++)
            {
                sim.TankRef(medium) = sim.TankRef(medium) with { Position = new Vector2(100, 98), Yaw = 0f, Speed = 0f };
                sim.TankRef(heavy) = sim.TankRef(heavy) with { Position = new Vector2(130, 100), Yaw = 0f, Speed = 0f };
                for (int i = 0; i < sim.Field.Props.Count; i++) sim.Field.Restore(i);
                for (int tick = 0; tick < 3 * SimConstants.TickRate; tick++)
                {
                    sim.Step(inputs);
                    foreach (GameEvent e in sim.Events)
                        if (e.Type == GameEventType.PropDestroyed) _ = e.PropId <= 6 ? fences++ : walls++;
                }
            }
        }
        Run(2);
        (fences, walls) = (0, 0);
        long before = GC.GetAllocatedBytesForCurrentThread();
        Run(4);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.True(fences >= 8 && walls >= 8, $"{fences} fences and {walls} walls knocked down while measured");
    }

    [Fact]
    public void The_simulation_reports_what_a_tank_drove_down()
    {
        var sim = new Simulation(Across(PropKind.Fence));
        int id = sim.AddTank(TankCatalog.Medium, Facing(TankCatalog.Medium, PropKind.Fence, 3f).Position, 0f);
        var events = new List<GameEvent>();
        for (int i = 0; i < 2 * SimConstants.TickRate; i++)
        {
            sim.Step(new[] { Full });
            events.AddRange(sim.Events);
        }

        GameEvent[] down = events.Where(e => e.Type == GameEventType.PropDestroyed).ToArray();
        Assert.Equal(new[] { 1, 2, 3 }, down.Select(e => e.PropId).Order());
        Assert.All(down, e =>
        {
            Assert.Equal((id, 0), (e.TankId, e.ShellId));
            Assert.Equal(new Vector3(0, 0, -1), e.Velocity);   // pushed the way the hull was going
            Assert.Equal(LineZ, e.Position.Z, 1e-4f);
        });
    }
}
