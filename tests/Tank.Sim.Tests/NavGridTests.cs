using System.Diagnostics;
using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Navigation;
using Tank.Sim.Terrain;
using Xunit.Abstractions;

namespace Tank.Sim.Tests;

/// <summary>
/// The bots' navigation grid (stage 5 design §4.1): 4 m cells costed in milliseconds at 10 m/s. Flat ground is
/// <see cref="TestWorld.Flat"/> (200 m, border 4 m: 50 × 50 cells); cell (25, 25) covers x, z ∈ [100, 104), centre (102, 102).
/// </summary>
public sealed class NavGridTests
{
    private const ushort Blocked = NavGrid.Blocked;

    private readonly ITestOutputHelper _output;

    public NavGridTests(ITestOutputHelper output) => _output = output;

    private static Prop Box(int id, PropKind kind, Vector2 centre, Vector2 half, float yaw = 0f) =>
        new(id, kind, new Obb2(centre, half, yaw), -0.3f, 3f);

    private static Prop Trunk(int id, Vector2 at, float radius = 0.4f) => TreeTests.Tree(id, at, radius);

    private static GameEvent Destroyed(int propId) =>
        new(GameEventType.PropDestroyed, 1, 0, 0, Vector3.Zero, Vector3.Zero, PropId: propId);

    /// <summary>Flat ground at height 0, every terrain cell <paramref name="kind"/>, water standing at <paramref name="waterLevel"/>.</summary>
    private static Battlefield Ground(SurfaceKind kind, float? waterLevel = null) =>
        new(TestWorld.Flat(), Array.Empty<Prop>(), TestWorld.Border,
            SurfaceMap.Uniform(TestWorld.Samples - 1, TestWorld.Samples - 1, TestWorld.Cell, kind), waterLevel);

    [Fact]
    public void A_flat_grass_cell_costs_400()
    {
        NavGrid grid = NavGrid.Build(TestWorld.FlatField());

        Assert.Equal(50, grid.CellsX);
        Assert.Equal(50, grid.CellsZ);
        Assert.Equal(400, grid.CostAt(25, 25));
        Assert.True(grid.IsOpen(25, 25));
        Assert.Equal(400, grid.MinCost);
        Assert.Equal(0, grid.Version);
    }

    [Theory]
    [InlineData(SurfaceKind.Road, null, 348)]    // 400 / 1.15 = 347.8
    [InlineData(SurfaceKind.Sand, null, 500)]    // 400 / 0.8
    [InlineData(SurfaceKind.Dirt, null, 400)]
    [InlineData(SurfaceKind.Grass, 0.6f, 500)]   // 0.6 m deep: 0.8
    [InlineData(SurfaceKind.Grass, 1.2f, 667)]   // 1.2 m: 0.6
    [InlineData(SurfaceKind.Grass, 1.5f, 800)]   // 1.5 m: 0.5
    [InlineData(SurfaceKind.Road, 1.5f, 800)]    // water takes the ground's place
    public void Ground_and_water_set_the_cost(SurfaceKind kind, float? waterLevel, int cost)
    {
        NavGrid grid = NavGrid.Build(Ground(kind, waterLevel));

        Assert.Equal(cost, grid.CostAt(25, 25));
        Assert.Equal(cost, grid.MinCost);
    }

    [Theory]
    [InlineData(0.364f, 453)]   // 20°: 400 × (1 + 0.364²)
    [InlineData(0.6f, 544)]     // 31°: 400 × 1.36, still climbable
    public void Slopes_slow(float grade, int cost)
    {
        NavGrid grid = NavGrid.Build(TestWorld.Field(TestWorld.RampNorth(grade)));

        Assert.True(grid.IsOpen(25, 25));
        Assert.InRange(grid.CostAt(25, 25), cost - 1, cost + 1);
    }

    [Fact]
    public void The_steep_ones_block()
    {
        NavGrid grid = NavGrid.Build(TestWorld.Field(TestWorld.RampNorth(0.65f)));   // 33°: over every tank's 32°

        Assert.Equal(Blocked, grid.CostAt(25, 25));
        Assert.Equal(32f, NavGrid.MaxSlope * 180f / MathF.PI, 3);
    }

    [Theory]
    [InlineData(PropKind.Rock)]
    [InlineData(PropKind.House)]
    [InlineData(PropKind.BrickWall)]
    [InlineData(PropKind.StoneWall)]
    public void Rocks_houses_and_walls_close_their_footprint_and_three_metres_round(PropKind kind)
    {
        NavGrid grid = NavGrid.Build(TestWorld.FlatField(Box(1, kind, new Vector2(100, 100), new Vector2(2, 2))));

        Assert.False(grid.IsOpen(25, 25));          // centre (102, 102): inside
        Assert.True(grid.IsOpen(26, 25));           // (106, 102): 4 m from the footprint
        Assert.True(grid.IsOpen(25, 26));           // (102, 106)
        Assert.False(grid.IsOpen(24, 24));          // (98, 98): inside
        Assert.True(grid.IsOpen(23, 24));           // (94, 98): 4 m off
        Assert.Equal(400, grid.CostAt(26, 25));
    }

    [Fact]
    public void A_cell_exactly_clearance_from_a_rock_is_closed()
    {
        // The rock's east face at x = 103: cell (26, 25)'s centre (106, 102) lies exactly 3 m from it.
        NavGrid grid = NavGrid.Build(TestWorld.FlatField(Box(1, PropKind.Rock, new Vector2(101, 100), new Vector2(2, 2))));

        Assert.False(grid.IsOpen(26, 25));
        Assert.True(grid.IsOpen(27, 25));
    }

    [Theory]
    [InlineData(PropKind.Fence)]
    [InlineData(PropKind.Bush)]
    [InlineData(PropKind.Tree)]
    public void Fences_bushes_and_trees_do_not_close_cells(PropKind kind)
    {
        NavGrid grid = NavGrid.Build(TestWorld.FlatField(Box(1, kind, new Vector2(100, 100), new Vector2(2, 2))));

        Assert.True(grid.IsOpen(25, 25));
        Assert.True(grid.IsOpen(24, 24));
    }

    [Fact]
    public void Trees_add_a_quarter_each_up_to_double()
    {
        NavGrid one = NavGrid.Build(TestWorld.FlatField(Trunk(1, new Vector2(101, 101))));
        NavGrid five = NavGrid.Build(TestWorld.FlatField(
            Trunk(1, new Vector2(100.5f, 100.5f)), Trunk(2, new Vector2(103.5f, 100.5f)), Trunk(3, new Vector2(100.5f, 103.5f)),
            Trunk(4, new Vector2(103.5f, 103.5f)), Trunk(5, new Vector2(102, 102))));
        NavGrid outside = NavGrid.Build(TestWorld.FlatField(Trunk(1, new Vector2(99.9f, 102))));

        Assert.Equal(500, one.CostAt(25, 25));
        Assert.Equal(800, five.CostAt(25, 25));
        Assert.Equal(400, outside.CostAt(25, 25));
        Assert.Equal(500, outside.CostAt(24, 25));
        Assert.Equal(400, one.MinCost);
    }

    [Fact]
    public void The_border_band_is_closed()
    {
        NavGrid grid = NavGrid.Build(TestWorld.FlatField());

        Assert.False(grid.IsOpen(0, 25));    // centre x 2
        Assert.False(grid.IsOpen(49, 25));   // 198
        Assert.True(grid.IsOpen(1, 25));     // 6 = border 4 + margin 2
        Assert.True(grid.IsOpen(48, 25));    // 194
        Assert.False(grid.IsOpen(25, 0));
        Assert.True(grid.IsOpen(25, 1));
    }

    [Fact]
    public void Off_the_grid_is_closed_and_cells_floor()
    {
        NavGrid grid = NavGrid.Build(TestWorld.FlatField());

        Assert.Equal(Blocked, grid.CostAt(-1, 0));
        Assert.Equal(Blocked, grid.CostAt(25, 50));
        Assert.False(grid.IsOpen(50, 0));
        Assert.False(grid.IsOpen(0, -1));
        Assert.Equal((0, 1), NavGrid.CellOf(new Vector2(3.99f, 4.0f)));
        Assert.Equal((-1, 0), NavGrid.CellOf(new Vector2(-0.01f, 0f)));
        Assert.Equal(new Vector2(2, 6), NavGrid.CentreOf(0, 1));
        Assert.True(grid.IsOpenAt(new Vector2(102, 102)));
        Assert.False(grid.IsOpenAt(new Vector2(float.NaN, 0)));
        Assert.False(grid.IsOpenAt(new Vector2(100, float.PositiveInfinity)));
        Assert.False(grid.IsOpenAt(new Vector2(-1e30f, 100)));
    }

    [Fact]
    public void A_fallen_house_reopens_its_cells_as_rubble()
    {
        Battlefield field = TestWorld.FlatField(Box(7, PropKind.House, new Vector2(100, 100), new Vector2(6, 4)));
        NavGrid grid = NavGrid.Build(field);
        Assert.Equal(Blocked, grid.CostAt(25, 25));
        Assert.Equal(400, grid.CostAt(30, 25));

        field.SetDurability(7, 0);
        int changed = grid.Refresh(field, new[] { Destroyed(7) });

        Assert.Equal(571, grid.CostAt(25, 25));   // 400 / 0.7: rubble
        Assert.Equal(400, grid.CostAt(30, 25));   // out of the house's reach
        Assert.True(changed > 0);
        Assert.Equal(1, grid.Version);
        Assert.Equal(400, grid.MinCost);
        Assert.Equal(0, grid.Refresh(field, new[] { Destroyed(7) }));
        Assert.Equal(1, grid.Version);
        AssertSameCells(NavGrid.Build(field), grid);
    }

    [Fact]
    public void A_felled_tree_makes_its_cell_cheaper()
    {
        Battlefield field = TestWorld.FlatField(Trunk(3, new Vector2(101, 101)));
        NavGrid grid = NavGrid.Build(field);
        Assert.Equal(500, grid.CostAt(25, 25));

        field.SetDurability(3, 0);

        Assert.Equal(1, grid.Refresh(field, new[] { Destroyed(3) }));
        Assert.Equal(400, grid.CostAt(25, 25));
        Assert.Equal(1, grid.Version);
    }

    [Fact]
    public void Fences_and_bushes_falling_change_nothing()
    {
        Battlefield field = TestWorld.FlatField(
            Box(1, PropKind.Fence, new Vector2(100, 100), new Vector2(1, 0.1f)),
            Box(2, PropKind.Bush, new Vector2(120, 100), new Vector2(2, 2)));
        NavGrid grid = NavGrid.Build(field);
        field.SetDurability(1, 0);
        field.SetDurability(2, 0);

        Assert.Equal(0, grid.Refresh(field, new[] { Destroyed(1), Destroyed(2) }));
        Assert.Equal(0, grid.Version);
    }

    [Fact]
    public void Other_events_are_ignored()
    {
        Battlefield field = TestWorld.FlatField(Box(7, PropKind.House, new Vector2(100, 100), new Vector2(6, 4)));
        NavGrid grid = NavGrid.Build(field);
        field.SetDurability(7, 0);
        var damaged = new GameEvent(GameEventType.PropDamaged, 1, 0, 0, Vector3.Zero, Vector3.Zero, Damage: 500, PropId: 7);

        Assert.Equal(0, grid.Refresh(field, new[] { damaged }));
        Assert.Equal(Blocked, grid.CostAt(25, 25));
    }

    [Fact]
    public void Refreshing_after_every_house_falls_equals_building_afresh()
    {
        Battlefield field = RiverlandFixture.Field.Fresh();
        NavGrid grid = NavGrid.Build(field);
        var events = new List<GameEvent>();
        foreach (Prop p in field.Props)
        {
            if (p.Kind != PropKind.House) continue;
            field.SetDurability(p.Id, 0);
            events.Add(Destroyed(p.Id));
        }
        int blockedBefore = CountBlocked(grid);
        NavGrid before = grid.Copy();

        int changed = grid.Refresh(field, events.ToArray());

        AssertNoneClosedOrRaised(before, grid);
        int reopened = blockedBefore - CountBlocked(grid);
        _output.WriteLine($"{events.Count} houses down: {changed} cells changed, {reopened} reopened");
        Assert.True(reopened > 0);
        AssertSameCells(NavGrid.Build(field), grid);
    }

    [Fact]
    public void Refreshing_after_walls_and_trees_fall_equals_building_afresh()
    {
        Battlefield field = RiverlandFixture.Field.Fresh();
        NavGrid grid = NavGrid.Build(field);
        var events = new List<GameEvent>();
        int k = 0;
        foreach (Prop p in field.Props)
        {
            if (!p.IsDestructible || k++ % 3 != 0) continue;   // a third of every destructible kind
            field.SetDurability(p.Id, 0);
            events.Add(Destroyed(p.Id));
        }
        NavGrid before = grid.Copy();

        grid.Refresh(field, events.ToArray());

        AssertNoneClosedOrRaised(before, grid);
        AssertSameCells(NavGrid.Build(field), grid);
    }

    [Fact]
    public void Refreshing_never_closes_or_raises_a_cell_whatever_falls()
    {
        // Every destructible prop on the riverland goes down, a kind at a time in batches of 40 events: no refresh may
        // close a cell or make one dearer (a search carries on over the change, so its heuristic and path must hold).
        Battlefield field = RiverlandFixture.Field.Fresh();
        NavGrid grid = NavGrid.Build(field);
        int batches = 0, changed = 0;
        foreach (PropKind kind in new[] { PropKind.House, PropKind.BrickWall, PropKind.StoneWall, PropKind.Tree, PropKind.Fence, PropKind.Bush })
        {
            Prop[] props = field.Props.Where(p => p.Kind == kind && p.IsDestructible).ToArray();
            for (int at = 0; at < props.Length; at += 40)
            {
                var events = new List<GameEvent>();
                foreach (Prop p in props.Skip(at).Take(40))
                {
                    field.SetDurability(p.Id, 0);
                    events.Add(Destroyed(p.Id));
                }
                NavGrid before = grid.Copy();

                changed += grid.Refresh(field, events.ToArray());

                AssertNoneClosedOrRaised(before, grid);
                batches++;
            }
        }
        _output.WriteLine($"{batches} batches, {changed} cells changed");
        Assert.True(changed > 0);
        AssertSameCells(NavGrid.Build(field), grid);
    }

    [Fact]
    public void Syncing_after_props_fell_unheard_equals_building_afresh()
    {
        // Props go down with no events reaching the grid (ticks the director skipped): Sync catches it up.
        Battlefield field = RiverlandFixture.Field.Fresh();
        NavGrid grid = NavGrid.Build(field);
        Assert.Equal(0, grid.Sync(field));   // nothing down yet
        Assert.Equal(0, grid.Version);
        int k = 0;
        foreach (Prop p in field.Props)
            if (p.IsDestructible && k++ % 3 == 0)
                field.SetDurability(p.Id, 0);
        NavGrid before = grid.Copy();

        int changed = grid.Sync(field);

        _output.WriteLine($"{k / 3} props down unheard: {changed} cells changed");
        Assert.True(changed > 0);
        Assert.Equal(1, grid.Version);
        AssertNoneClosedOrRaised(before, grid);
        AssertSameCells(NavGrid.Build(field), grid);
        Assert.Equal(0, grid.Sync(field));
        Assert.Equal(1, grid.Version);
    }

    [Fact]
    public void Syncing_a_house_that_fell_unheard_reopens_it()
    {
        Battlefield field = TestWorld.FlatField(Box(7, PropKind.House, new Vector2(100, 100), new Vector2(6, 4)), Trunk(8, new Vector2(141, 141)));
        NavGrid grid = NavGrid.Build(field);
        field.SetDurability(7, 0);
        field.SetDurability(8, 0);

        Assert.True(grid.Sync(field) > 0);

        Assert.Equal(571, grid.CostAt(25, 25));
        Assert.Equal(400, grid.CostAt(35, 35));
        AssertSameCells(NavGrid.Build(field), grid);
    }

    [Fact]
    public void The_maps_grids_have_the_measured_shape()
    {
        (NavGrid river, long riverMs) = Timed(RiverlandFixture.Field);
        (NavGrid sandbox, long sandboxMs) = Timed(MapCatalog.Sandbox.NewBattlefield());
        int riverOpen = CountOpen(river), sandboxOpen = CountOpen(sandbox);
        _output.WriteLine($"riverland: {river.CellsX} × {river.CellsZ}, {riverOpen} open, min cost {river.MinCost}, built in {riverMs} ms");
        _output.WriteLine($"sandbox: {sandbox.CellsX} × {sandbox.CellsZ}, {sandboxOpen} open, min cost {sandbox.MinCost}, built in {sandboxMs} ms");

        Assert.Equal((300, 300), (river.CellsX, river.CellsZ));
        Assert.InRange(riverOpen, 75_003 * 0.98, 75_003 * 1.02);
        Assert.Equal(348, river.MinCost);
        Assert.Equal((100, 100), (sandbox.CellsX, sandbox.CellsZ));
        Assert.InRange(sandboxOpen, 7_848 * 0.98, 7_848 * 1.02);
        Assert.Equal(400, sandbox.MinCost);
    }

    [Fact]
    public void The_nearest_open_cell()
    {
        // The rock closes cells (24, 24) … (25, 25), centres 98 and 102; eight open centres lie √40 m from (100, 100).
        NavGrid grid = NavGrid.Build(TestWorld.FlatField(Box(1, PropKind.Rock, new Vector2(100, 100), new Vector2(2, 2))));

        Assert.True(grid.TryNearestOpen(new Vector2(100, 100), 12f, out int i, out int j));
        Assert.Equal((24, 23), (i, j));   // the ties: lowest j, then lowest i
        Assert.True(grid.TryNearestOpen(new Vector2(103.5f, 101), 12f, out i, out j));
        Assert.Equal((26, 25), (i, j));
        Assert.True(grid.TryNearestOpen(new Vector2(50, 50), 0.5f, out i, out j));   // an open cell: its own
        Assert.Equal((12, 12), (i, j));
        Assert.False(grid.TryNearestOpen(new Vector2(100, 100), 6f, out _, out _));
        Assert.False(grid.TryNearestOpen(new Vector2(float.NaN, 100), 12f, out _, out _));
        Assert.True(grid.TryNearestOpen(new Vector2(1, 1), 8f, out i, out j));   // off the band, into the playable area
        Assert.Equal((1, 1), (i, j));
    }

    [Fact]
    public void The_nearest_open_cell_agrees_with_a_scan_of_every_cell()
    {
        var rocks = new List<Prop>();
        uint seed = 12345;
        for (int id = 1; id <= 60; id++)
        {
            float x = 10 + Next(ref seed) % 180, z = 10 + Next(ref seed) % 180;
            rocks.Add(Box(id, PropKind.Rock, new Vector2(x, z), new Vector2(1 + Next(ref seed) % 5, 1 + Next(ref seed) % 5), Next(ref seed) % 7));
        }
        NavGrid grid = NavGrid.Build(TestWorld.FlatField(rocks.ToArray()));
        for (int n = 0; n < 400; n++)
        {
            var p = new Vector2(Next(ref seed) % 2000 / 10f, Next(ref seed) % 2000 / 10f);
            float radius = Next(ref seed) % 160 / 10f;

            bool found = grid.TryNearestOpen(p, radius, out int i, out int j);

            bool expected = Scan(grid, p, radius, out int ei, out int ej);
            Assert.Equal(expected, found);
            if (expected) Assert.Equal((ei, ej), (i, j));
        }
    }

    [Fact]
    public void A_copy_is_its_own()
    {
        Battlefield field = TestWorld.FlatField(Box(7, PropKind.House, new Vector2(100, 100), new Vector2(6, 4)));
        NavGrid grid = NavGrid.Build(field);
        NavGrid copy = grid.Copy();
        Assert.Equal((grid.CellsX, grid.CellsZ, grid.MinCost, grid.Version), (copy.CellsX, copy.CellsZ, copy.MinCost, copy.Version));

        field.SetDurability(7, 0);
        grid.Refresh(field, new[] { Destroyed(7) });

        Assert.Equal(571, grid.CostAt(25, 25));
        Assert.Equal(Blocked, copy.CostAt(25, 25));
        Assert.Equal(0, copy.Version);
        NavGrid second = grid.Copy();
        Assert.Equal(1, second.Version);
        AssertSameCells(grid, second);
    }

    [Fact]
    public void Setting_a_cell_for_tests_lowers_the_minimum_and_bumps_the_version()
    {
        NavGrid grid = NavGrid.Build(TestWorld.FlatField());

        grid.SetCostForTests(10, 10, 250);
        grid.SetCostForTests(11, 10, Blocked);

        Assert.Equal(250, grid.CostAt(10, 10));
        Assert.False(grid.IsOpen(11, 10));
        Assert.Equal(250, grid.MinCost);
        Assert.Equal(2, grid.Version);
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.SetCostForTests(50, 0, 400));
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.SetCostForTests(0, 0, 0));
    }

    [Fact]
    public void Refreshing_and_syncing_allocate_nothing()
    {
        Allocation.AssertNone(() =>
        {
            var props = new List<Prop>();
            var events = new GameEvent[20];
            for (int k = 0; k < 20; k++)
            {
                var at = new Vector2(20 + k % 5 * 35, 20 + k / 5 * 40);
                PropKind kind = (k % 4) switch { 0 => PropKind.House, 1 => PropKind.BrickWall, 2 => PropKind.Tree, _ => PropKind.Fence };
                props.Add(kind == PropKind.Tree ? Trunk(k + 1, at) : Box(k + 1, kind, at, kind == PropKind.House ? new Vector2(6, 4) : new Vector2(1, 0.25f)));
                events[k] = Destroyed(k + 1);
            }
            Battlefield field = TestWorld.FlatField(props.ToArray());
            NavGrid grid = NavGrid.Build(field), unheard = NavGrid.Build(field);
            grid.Refresh(field, events);   // warm-up: nothing has fallen yet
            unheard.Sync(field);
            foreach (Prop p in props) field.SetDurability(p.Id, 0);

            Allocation.Window window = Allocation.Start();
            int changed = grid.Refresh(field, events);
            int synced = unheard.Sync(field);
            Allocation.Measured measured = window.Stop();

            Assert.True(changed > 0);
            Assert.Equal(changed, synced);
            return measured;
        });
    }

    // ---- helpers ----

    private static (NavGrid Grid, long Ms) Timed(Battlefield field)
    {
        NavGrid.Build(field);   // warm-up
        var watch = Stopwatch.StartNew();
        NavGrid grid = NavGrid.Build(field);
        return (grid, watch.ElapsedMilliseconds);
    }

    private static int CountOpen(NavGrid grid)
    {
        int open = 0;
        for (int j = 0; j < grid.CellsZ; j++)
        for (int i = 0; i < grid.CellsX; i++)
            if (grid.IsOpen(i, j)) open++;
        return open;
    }

    private static int CountBlocked(NavGrid grid) => grid.CellsX * grid.CellsZ - CountOpen(grid);

    /// <summary>No cell of <paramref name="after"/> is closed or dearer than it was in <paramref name="before"/> (a closed one may open).</summary>
    private static void AssertNoneClosedOrRaised(NavGrid before, NavGrid after)
    {
        for (int j = 0; j < before.CellsZ; j++)
        for (int i = 0; i < before.CellsX; i++)
        {
            ushort was = before.CostAt(i, j), now = after.CostAt(i, j);
            if (was != Blocked && now > was) Assert.Fail($"cell ({i}, {j}): {was} → {(now == Blocked ? "closed" : now)}");
        }
    }

    private static void AssertSameCells(NavGrid expected, NavGrid actual)
    {
        Assert.Equal((expected.CellsX, expected.CellsZ), (actual.CellsX, actual.CellsZ));
        for (int j = 0; j < expected.CellsZ; j++)
        for (int i = 0; i < expected.CellsX; i++)
            if (expected.CostAt(i, j) != actual.CostAt(i, j))
                Assert.Fail($"cell ({i}, {j}): expected {expected.CostAt(i, j)}, got {actual.CostAt(i, j)}");
    }

    /// <summary>The nearest open cell within <paramref name="radius"/> by looking at them all (ties: lower j, then lower i).</summary>
    private static bool Scan(NavGrid grid, Vector2 p, float radius, out int bi, out int bj)
    {
        (bi, bj) = (-1, -1);
        float best = float.MaxValue;
        for (int j = 0; j < grid.CellsZ; j++)
        for (int i = 0; i < grid.CellsX; i++)
        {
            if (!grid.IsOpen(i, j)) continue;
            float d = Vector2.DistanceSquared(NavGrid.CentreOf(i, j), p);
            if (d > radius * radius || d >= best) continue;
            (best, bi, bj) = (d, i, j);
        }
        return bi >= 0;
    }

    private static uint Next(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return state;
    }
}
