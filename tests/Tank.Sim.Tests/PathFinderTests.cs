using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Navigation;
using Tank.Sim.Terrain;
using Xunit.Abstractions;

namespace Tank.Sim.Tests;

/// <summary>
/// A* over the navigation grid (stage 5 design §4.1). Flat ground is <see cref="TestWorld.Flat"/> unless said otherwise:
/// 200 m, border 4 m, 50 × 50 cells of 400 ms, the outermost ring of cells closed.
/// </summary>
public sealed class PathFinderTests
{
    private const float Sample = 0.5f;

    /// <summary>Step for adding up what a segment costs.</summary>
    private const float CostSample = 0.02f;

    /// <summary>Distances the tests measure in float may come out this much short of what the finder checked.</summary>
    private const float Slack = 1e-3f;

    private readonly ITestOutputHelper _output;

    public PathFinderTests(ITestOutputHelper output) => _output = output;

    private static NavGrid RiverGrid => RiverlandFixture.Grid;

    private static Prop Rock(int id, Vector2 centre) => new(id, PropKind.Rock, new Obb2(centre, new Vector2(2, 2), 0f), -0.3f, 3f);

    private static GameEvent Destroyed(int propId) =>
        new(GameEventType.PropDestroyed, 1, 0, 0, Vector3.Zero, Vector3.Zero, PropId: propId);

    [Fact]
    public void A_path_over_open_ground_runs_straight_to_its_goal()
    {
        Battlefield field = TestWorld.FlatField();
        var finder = new PathFinder(NavGrid.Build(field), field);

        PathStatus status = finder.FindNow(new Vector2(20, 100), new Vector2(180, 100));

        Assert.Equal(PathStatus.Found, status);
        Assert.Equal(PathStatus.Found, finder.Status);
        Assert.Equal(new Vector2(180, 100), finder.Points[^1]);
        foreach (Vector2 p in finder.Points) Assert.True(MathF.Abs(p.Y - 100) < 2.1f, $"{p} strays from z = 100");
        Assert.InRange(finder.Points.Length, 1, 3);
        Assert.InRange(finder.Cost, 40 * 400 - 400, 40 * 400 + 400);
    }

    [Fact]
    public void It_goes_round_a_line_of_rocks()
    {
        // 4 × 4 m rocks at x = 100, z = 20, 26, …, 158: a wall from z 18 to 160. Round its south end (large z) is shorter.
        var rocks = new List<Prop>();
        for (int n = 0; n * 6 + 20 <= 158; n++) rocks.Add(Rock(n + 1, new Vector2(100, 20 + n * 6)));
        Battlefield field = TestWorld.FlatField(rocks.ToArray());
        NavGrid grid = NavGrid.Build(field);
        var finder = new PathFinder(grid, field);

        Assert.Equal(PathStatus.Found, finder.FindNow(new Vector2(60, 100), new Vector2(140, 100)));

        foreach (int cell in finder.Cells) Assert.True(grid.IsOpen(cell % grid.CellsX, cell / grid.CellsX));
        List<Vector2> trail = Trail(finder, new Vector2(60, 100));
        foreach (Vector2 p in Samples(trail))
        foreach (Prop rock in rocks)
            Assert.True(rock.Footprint.DistanceToPoint(p) >= PathFinder.HullClearance - Slack, $"{p} is {rock.Footprint.DistanceToPoint(p)} m from rock {rock.Id}");
        Assert.Contains(finder.Points.ToArray(), p => p.Y >= 163);
        Assert.InRange(Length(trail), 150f, 230f);
    }

    [Fact]
    public void It_keeps_to_a_road_that_bends_a_little()
    {
        // An 8 m road along (40, 100) → (100, 115) → (160, 100); grass elsewhere. Straight over grass is 30 cells × 400 =
        // 12 000; along the road, on eight ways, each leg 15 cells across and 3.75 up: 3.75 × 492 + 11.25 × 348 ≈ 5 760.
        Vector2 a = new(40, 100), bend = new(100, 115), b = new(160, 100);
        int cells = TestWorld.Samples - 1;
        var kinds = new SurfaceKind[cells * cells];
        for (int j = 0; j < cells; j++)
        for (int i = 0; i < cells; i++)
        {
            var c = new Vector2((i + 0.5f) * TestWorld.Cell, (j + 0.5f) * TestWorld.Cell);
            bool road = MathF.Min(SandboxMap.DistanceToSegment(c, a, bend), SandboxMap.DistanceToSegment(c, bend, b)) <= 4f;
            kinds[j * cells + i] = road ? SurfaceKind.Road : SurfaceKind.Grass;
        }
        var field = new Battlefield(TestWorld.Flat(), Array.Empty<Prop>(), TestWorld.Border, new SurfaceMap(cells, cells, TestWorld.Cell, kinds));
        NavGrid grid = NavGrid.Build(field);
        var finder = new PathFinder(grid, field);

        Assert.Equal(PathStatus.Found, finder.FindNow(a, b));

        int onRoad = 0;
        foreach (int cell in finder.Cells)
            if (field.SurfaceAt(Centre(grid, cell)) == SurfaceKind.Road) onRoad++;
        _output.WriteLine($"{finder.Cells.Length} cells, {onRoad} on the road, cost {finder.Cost}, {finder.Points.Length} points");
        Assert.True(onRoad >= 0.8f * finder.Cells.Length, $"only {onRoad} of {finder.Cells.Length} cells on the road");
        foreach (Vector2 p in Samples(Trail(finder, a)))
        {
            (int i, int j) = NavGrid.CellOf(p);
            Assert.True(grid.CostAt(i, j) < NavGrid.GrassCost, $"{p} leaves the road (cell cost {grid.CostAt(i, j)})");
        }
    }

    [Fact]
    public void It_goes_round_ground_too_steep_to_climb()
    {
        // A 45° cone 30 m round at (100, 100).
        var heights = new float[TestWorld.Samples * TestWorld.Samples];
        for (int j = 0; j < TestWorld.Samples; j++)
        for (int i = 0; i < TestWorld.Samples; i++)
            heights[j * TestWorld.Samples + i] = MathF.Max(0f, 30f - Vector2.Distance(new Vector2(i, j) * TestWorld.Cell, new Vector2(100, 100)));
        var terrain = new HeightField(TestWorld.Samples, TestWorld.Samples, TestWorld.Cell, heights);
        Battlefield field = TestWorld.Field(terrain);
        NavGrid grid = NavGrid.Build(field);
        var finder = new PathFinder(grid, field);

        Assert.Equal(PathStatus.Found, finder.FindNow(new Vector2(40, 100), new Vector2(160, 100)));

        float flattest = MathF.Cos(NavGrid.MaxSlope);
        foreach (int cell in finder.Cells)
        {
            Vector2 c = Centre(grid, cell);
            Assert.True(terrain.NormalAt(c.X, c.Y).Y >= flattest, $"cell centre {c} is steeper than 32°");
        }
        Assert.True(Length(Trail(finder, new Vector2(40, 100))) > 125f);
    }

    [Fact]
    public void A_fallen_house_opens_the_short_way()
    {
        // Rocks along x = 100 at z = 8 … 92 and 108 … 192, a house in the 12 m gap between them: the west is shut in.
        var props = new List<Prop>();
        int id = 1;
        for (int z = 8; z <= 92; z += 6) props.Add(Rock(id++, new Vector2(100, z)));
        for (int z = 108; z <= 192; z += 6) props.Add(Rock(id++, new Vector2(100, z)));
        int house = id;
        props.Add(new Prop(house, PropKind.House, new Obb2(new Vector2(100, 100), new Vector2(5, 6), 0f), -0.3f, 6f));
        Battlefield field = TestWorld.FlatField(props.ToArray());
        NavGrid grid = NavGrid.Build(field);
        var finder = new PathFinder(grid, field);
        int westOpen = 0;
        for (int j = 0; j < grid.CellsZ; j++)
        for (int i = 0; i < grid.CellsX / 2; i++)
            if (grid.IsOpen(i, j)) westOpen++;

        Assert.Equal(PathStatus.NoPath, finder.FindNow(new Vector2(60, 100), new Vector2(140, 100)));
        Assert.InRange(finder.Expansions, 1, westOpen);
        Assert.True(finder.Points.IsEmpty);

        field.SetDurability(house, 0);
        grid.Refresh(field, new[] { Destroyed(house) });

        Assert.Equal(PathStatus.Found, finder.FindNow(new Vector2(60, 100), new Vector2(140, 100)));
        List<Vector2> trail = Trail(finder, new Vector2(60, 100));
        Assert.True(Length(trail) < 100f);
        Assert.Contains(Samples(trail), p => Vector2.Distance(p, new Vector2(100, 100)) < 8f);
    }

    [Fact]
    public void Astar_costs_equal_dijkstra_on_random_grids()
    {
        int found = 0, none = 0;
        for (uint k = 0; k < 20; k++)
        {
            var rng = new XorShift(k);
            NavGrid grid = NavGrid.Build(TestWorld.FlatField());
            for (int j = 1; j < grid.CellsZ - 1; j++)
            for (int i = 1; i < grid.CellsX - 1; i++)
                grid.SetCostForTests(i, j, rng.NextUInt() % 100 < 30 ? NavGrid.Blocked : (ushort)(300 + rng.NextUInt() % 1201));
            var finder = new PathFinder(grid, TestWorld.FlatField());
            for (int pair = 0; pair < 5; pair++)
            {
                int start = RandomOpenCell(grid, ref rng), goal = RandomOpenCell(grid, ref rng);

                PathStatus status = finder.FindNow(Centre(grid, start), Centre(grid, goal));

                int expected = Dijkstra(grid, start, goal);
                if (expected < 0)
                {
                    Assert.Equal(PathStatus.NoPath, status);
                    none++;
                    continue;
                }
                Assert.Equal(PathStatus.Found, status);
                Assert.Equal(expected, finder.Cost);
                Assert.Equal(expected, CostAlong(grid, finder.Cells));
                Assert.Equal(start, finder.Cells[0]);
                Assert.Equal(goal, finder.Cells[^1]);
                found++;
            }
        }
        _output.WriteLine($"{found} found, {none} with no path");
        Assert.True(found >= 50 && none >= 1, $"{found} found, {none} not: the grids should give both");
    }

    [Fact]
    public void A_start_or_goal_in_a_closed_cell_snaps_within_12_m()
    {
        // The rock closes cells (24, 24) … (25, 25); the nearest open centre to (100, 100) is (24, 23)'s, (98, 94), √40 m off.
        Battlefield field = TestWorld.FlatField(Rock(1, new Vector2(100, 100)));
        NavGrid grid = NavGrid.Build(field);
        var finder = new PathFinder(grid, field);

        Assert.Equal(PathStatus.Found, finder.FindNow(new Vector2(100, 100), new Vector2(160, 100)));
        Assert.Equal(23 * grid.CellsX + 24, finder.Cells[0]);
        Assert.Equal(new Vector2(160, 100), finder.Points[^1]);

        Assert.Equal(PathStatus.Found, finder.FindNow(new Vector2(40, 100), new Vector2(100, 100)));
        Assert.Equal(new Vector2(98, 94), finder.Points[^1]);   // the snapped cell's centre, not the goal

        for (int j = 20; j < 30; j++)
        for (int i = 20; i < 30; i++)
            grid.SetCostForTests(i, j, NavGrid.Blocked);   // 40 × 40 m closed round (100, 100)

        Assert.Equal(PathStatus.NoPath, finder.Begin(new Vector2(100, 100), new Vector2(160, 100)));
        Assert.Equal(PathStatus.NoPath, finder.Status);
        Assert.Equal(PathStatus.NoPath, finder.Begin(new Vector2(160, 100), new Vector2(100, 100)));
        Assert.Equal(PathStatus.NoPath, finder.Begin(new Vector2(float.NaN, 100), new Vector2(160, 100)));
        Assert.Equal(0, finder.Expansions);
    }

    [Fact]
    public void There_is_no_path_into_a_walled_off_pocket()
    {
        // A square of rocks 40 m a side round (100, 100), 5 m apart: the 1 m gaps between them are inside the closed clearance.
        var rocks = new List<Prop>();
        for (int s = 80; s <= 120; s += 5)
        {
            rocks.Add(Rock(rocks.Count + 1, new Vector2(80, s)));
            rocks.Add(Rock(rocks.Count + 1, new Vector2(120, s)));
            if (s is > 80 and < 120)
            {
                rocks.Add(Rock(rocks.Count + 1, new Vector2(s, 80)));
                rocks.Add(Rock(rocks.Count + 1, new Vector2(s, 120)));
            }
        }
        Battlefield field = TestWorld.FlatField(rocks.ToArray());
        NavGrid grid = NavGrid.Build(field);
        var finder = new PathFinder(grid, field);
        Assert.True(grid.IsOpenAt(new Vector2(100, 100)));

        Assert.Equal(PathStatus.NoPath, finder.FindNow(new Vector2(40, 40), new Vector2(100, 100)));
        Assert.Equal(PathStatus.NoPath, finder.FindNow(new Vector2(100, 100), new Vector2(40, 40)));
        Assert.Equal(PathStatus.Found, finder.FindNow(new Vector2(96, 96), new Vector2(104, 106)));
    }

    [Fact]
    public void A_search_split_over_many_calls_finds_the_same_path()
    {
        Battlefield field = RiverlandFixture.Field;
        var whole = new PathFinder(RiverGrid, field);
        var split = new PathFinder(RiverGrid, field);
        Vector2 from = RiverlandMap.Waypoints[15], to = RiverlandMap.Waypoints[27];   // (60, 444) → (1140, 808)

        Assert.Equal(PathStatus.Found, whole.FindNow(from, to));
        Assert.Equal(PathStatus.Pending, split.Begin(from, to));
        int calls = 0;
        while (split.Continue(50) == PathStatus.Pending)
        {
            calls++;
            Assert.True(split.Points.IsEmpty);
            Assert.Equal(50 * calls, split.Expansions);
        }

        _output.WriteLine($"{whole.Expansions} expansions, cost {whole.Cost}, {whole.Points.Length} points, {calls + 1} calls");
        Assert.Equal(PathStatus.Found, split.Status);
        Assert.Equal(whole.Expansions, split.Expansions);
        Assert.Equal(whole.Cost, split.Cost);
        Assert.Equal(whole.Cells.ToArray(), split.Cells.ToArray());
        Assert.Equal(whole.Points.ToArray(), split.Points.ToArray());
        Assert.Equal(PathStatus.Found, split.Continue(50));   // a finished search stays as it is
    }

    [Fact]
    public void A_search_gives_up_at_the_cap()
    {
        var finder = new PathFinder(RiverGrid, RiverlandFixture.Field, maxExpansions: 100);

        Assert.Equal(PathStatus.NoPath, finder.FindNow(RiverlandMap.Waypoints[15], RiverlandMap.Waypoints[27]));
        Assert.Equal(100, finder.Expansions);
        Assert.True(finder.Points.IsEmpty);
        Assert.Throws<ArgumentOutOfRangeException>(() => new PathFinder(RiverGrid, RiverlandFixture.Field, maxExpansions: 0));
    }

    [Fact]
    public void Smoothed_paths_on_the_riverland_keep_clear_and_never_cost_more_than_the_search()
    {
        // Each leg from the bot's real start through the points to its real goal: over open cells, clear of standing
        // obstacles (but a leg inside one cell, to or from a cell's centre), no cell dearer than the dearest the search took
        // between its ends, and costing at most SmoothingExcess more than the search did between them (the real start's
        // and goal's way to their cells' centres counted in).
        Battlefield field = RiverlandFixture.Field;
        NavGrid grid = RiverGrid;
        var finder = new PathFinder(grid, field);
        var rng = new XorShift(5);
        IReadOnlyList<Vector2> waypoints = RiverlandMap.Waypoints;
        var buffer = new int[Battlefield.QueryBufferSize];
        int searches = 0, points = 0, samples = 0, cut = 0, extraStarts = 0, extraGoals = 0;
        long expansions = 0;
        double worstRatio = 0;
        while (searches < 100)
        {
            int a = (int)(rng.NextUInt() % (uint)waypoints.Count), b = (int)(rng.NextUInt() % (uint)waypoints.Count);
            if (a == b) continue;
            searches++;
            Vector2 start = waypoints[a], goal = waypoints[b];
            string route = $"{start} → {goal}";

            Assert.Equal(PathStatus.Found, finder.FindNow(start, goal));

            expansions += finder.Expansions;
            points += finder.Points.Length;
            ReadOnlySpan<int> cells = finder.Cells;
            int last = cells.Length - 1;
            Vector2 at = start;
            int from = -1;   // the path cell `at` is the centre of; −1 for the real start (in cell 0)
            for (int n = 0; n < finder.Points.Length; n++)
            {
                Vector2 point = finder.Points[n];
                (int pi, int pj) = NavGrid.CellOf(point);
                int to = IndexOf(cells, pj * grid.CellsX + pi, from);
                bool atGoal = to < 0 && n == finder.Points.Length - 1 && pj * grid.CellsX + pi == cells[last];
                if (atGoal) to = last;   // the goal after its cell's centre
                Assert.True(to >= 0, $"{route}: point {point} is not a later cell of the path");
                bool realEnd = point != Centre(grid, cells[to]);
                Assert.True(!realEnd || n == finder.Points.Length - 1, $"{route}: point {point} is no cell's centre");
                int lo = System.Math.Max(from, 0);
                if (to == 0) extraStarts++;
                if (atGoal) extraGoals++;

                int dearest = 0, g = 0;
                for (int k = lo; k <= to; k++) dearest = System.Math.Max(dearest, grid.Costs[cells[k]]);
                for (int k = lo + 1; k <= to; k++) g += Step(grid, cells[k - 1], cells[k]);
                double allowed = g;
                if (from < 0) allowed += grid.Costs[cells[0]] * Vector2.Distance(at, Centre(grid, cells[0])) / NavGrid.CellSize;
                if (realEnd) allowed += grid.Costs[cells[to]] * Vector2.Distance(point, Centre(grid, cells[to])) / NavGrid.CellSize;
                double cost = SegmentCost(grid, at, point);
                if (allowed > 0) worstRatio = System.Math.Max(worstRatio, cost / allowed);
                Assert.True(cost <= allowed * (1 + PathFinder.SmoothingExcess) + 2, $"{route}: {at} → {point} costs {cost:F0}, the search {allowed:F0}");

                bool inOneCell = to == lo;
                foreach (Vector2 p in Samples(at, point))
                {
                    samples++;
                    (int i, int j) = NavGrid.CellOf(p);
                    Assert.True(grid.IsOpen(i, j), $"{route}: {p} is in a closed cell");
                    // A one-cell leg is the A* step itself: its corner may touch a dearer side cell (open: no corner cutting).
                    if (to > lo + 1)
                        Assert.True(grid.CostAt(i, j) <= dearest, $"{route}: {p} costs {grid.CostAt(i, j)}, the path there at most {dearest}");
                    if (inOneCell) continue;
                    foreach (int k in field.Near(PropSet.ShellStoppers, p, PathFinder.HullClearance, buffer))
                    {
                        if (!field.StandingAt(k)) continue;
                        float d = field.PropAt(k).Footprint.DistanceToPoint(p);
                        Assert.True(d >= PathFinder.HullClearance - Slack, $"{route}: {p} is {d} m from {field.PropAt(k).Kind} {field.PropAt(k).Id}");
                    }
                }
                (at, from) = (point, to);
            }
            if (finder.Points.Length == PathFinder.MaxPoints) cut++;   // cut short: the bot replans at its last point
            else
            {
                Assert.Equal(last, from);
                Assert.Equal(goal, finder.Points[^1]);
            }
        }
        _output.WriteLine($"{searches} searches: {expansions / searches} expansions and {points / (float)searches:F1} points on average, " +
            $"{cut} cut at {PathFinder.MaxPoints} points, {extraStarts} by way of the start's centre, {extraGoals} of the goal's, " +
            $"a leg at most {worstRatio:F3} × the search's cost, {samples} samples");
    }

    [Fact]
    public void The_same_search_twice_gives_the_same_points()
    {
        Battlefield field = RiverlandFixture.Field;
        var finder = new PathFinder(RiverGrid, field);
        var other = new PathFinder(RiverGrid, field);
        Vector2 from = RiverlandMap.Waypoints[3], to = RiverlandMap.Waypoints[30];

        Assert.Equal(PathStatus.Found, finder.FindNow(from, to));
        Vector2[] first = finder.Points.ToArray();
        (int cost, int expansions) = (finder.Cost, finder.Expansions);
        Assert.Equal(PathStatus.Found, finder.FindNow(to, from));
        Assert.Equal(PathStatus.Found, finder.FindNow(from, to));
        Assert.Equal(PathStatus.Found, other.FindNow(from, to));

        Assert.Equal(first, finder.Points.ToArray());
        Assert.Equal(first, other.Points.ToArray());
        Assert.Equal((cost, expansions), (finder.Cost, finder.Expansions));
        Assert.Equal((cost, expansions), (other.Cost, other.Expansions));
    }

    [Fact]
    public void A_search_carries_on_while_trees_fall_under_it()
    {
        // A tree goes down before every call of 50 expansions. Cells only ever open or get cheaper, so the search goes on
        // where it was rather than starting over (which would never end with props falling every tick), and the path it
        // finds is a real one on the grid as it ends up, costing no more than the search reckoned.
        Battlefield field = RiverlandFixture.Field.Fresh();
        NavGrid grid = RiverGrid.Copy();
        var finder = new PathFinder(grid, field);
        Vector2 from = RiverlandMap.Waypoints[15], to = RiverlandMap.Waypoints[27];
        var still = new PathFinder(RiverGrid, RiverlandFixture.Field);
        Assert.Equal(PathStatus.Found, still.FindNow(from, to));
        var trees = new Queue<int>(field.Props.Where(p => p.Kind == PropKind.Tree).Select(p => p.Id));

        Assert.Equal(PathStatus.Pending, finder.Begin(from, to));
        int calls = 0;
        PathStatus status;
        do
        {
            FellATree(field, grid, trees);
            int before = finder.Expansions;
            status = finder.Continue(50);
            calls++;
            if (status == PathStatus.Pending) Assert.Equal(50 * calls, finder.Expansions);   // carried on, not started over
            else Assert.InRange(finder.Expansions - before, 1, 50);
        } while (status == PathStatus.Pending);

        _output.WriteLine($"{calls} calls, {calls} grid changes: {finder.Expansions} expansions (still grid {still.Expansions}), cost {finder.Cost} (still {still.Cost}), {finder.Points.Length} points");
        Assert.Equal(PathStatus.Found, status);
        Assert.Equal(calls, grid.Version);
        Assert.InRange(finder.Expansions, 1, PathFinder.MaxExpansions);
        Assert.Equal(still.Cells[0], finder.Cells[0]);
        Assert.Equal(still.Cells[^1], finder.Cells[^1]);
        Assert.InRange(CostAlong(grid, finder.Cells), 1, finder.Cost);   // every cell open, every step a legal one, now
        Assert.Equal(to, finder.Points[^1]);
        foreach (Vector2 p in Samples(Trail(finder, from)))
            Assert.True(grid.IsOpenAt(p), $"{p} is in a closed cell");
    }

    [Fact]
    public void A_search_whose_grid_changes_every_call_still_gives_up_at_the_cap()
    {
        Battlefield field = RiverlandFixture.Field.Fresh();
        NavGrid grid = RiverGrid.Copy();
        var finder = new PathFinder(grid, field, maxExpansions: 100);
        var trees = new Queue<int>(field.Props.Where(p => p.Kind == PropKind.Tree).Select(p => p.Id));

        Assert.Equal(PathStatus.Pending, finder.Begin(RiverlandMap.Waypoints[15], RiverlandMap.Waypoints[27]));
        int calls = 0;
        PathStatus status;
        do
        {
            FellATree(field, grid, trees);
            status = finder.Continue(10);
            calls++;
        } while (status == PathStatus.Pending && calls < 1000);

        Assert.Equal(PathStatus.NoPath, status);
        Assert.Equal(100, finder.Expansions);
        Assert.Equal(10, calls);
    }

    [Fact]
    public void A_start_beside_a_rock_drives_to_its_cells_centre_before_a_far_first_point()
    {
        // The rock covers x, z ∈ [98, 102]. The start (96.2, 104.2) is in the corner of open cell (24, 26), 2.8 m from the
        // rock's corner; the cell's centre (98, 106) is 4 m off. From the centre the way to the goal clears the rock by
        // 3 m; from the start itself it would pass 0.9 m from it.
        Prop rock = Rock(1, new Vector2(100, 100));
        Battlefield field = TestWorld.FlatField(rock);
        NavGrid grid = NavGrid.Build(field);
        var finder = new PathFinder(grid, field);
        Vector2 from = new(96.2f, 104.2f), to = new(160, 90);
        Assert.True(grid.IsOpenAt(from));

        Assert.Equal(PathStatus.Found, finder.FindNow(from, to));

        Assert.Equal(new Vector2(98, 106), finder.Points[0]);
        Assert.Equal(to, finder.Points[^1]);
        Assert.True(Samples(from, finder.Points[1]).Min(p => rock.Footprint.DistanceToPoint(p)) < PathFinder.HullClearance);   // straight on would cut it
        foreach (Vector2 p in Samples(from, finder.Points[0]))   // away from the rock
            Assert.True(rock.Footprint.DistanceToPoint(p) >= rock.Footprint.DistanceToPoint(from) - Slack, $"{p} is {rock.Footprint.DistanceToPoint(p)} m from the rock");
        foreach (Vector2 p in Samples(Trail(finder, from).Skip(1).ToList()))
            Assert.True(rock.Footprint.DistanceToPoint(p) >= PathFinder.HullClearance - Slack, $"{p} is {rock.Footprint.DistanceToPoint(p)} m from the rock");
    }

    [Fact]
    public void A_start_with_a_clear_way_to_its_first_point_needs_no_extra_one()
    {
        Battlefield field = TestWorld.FlatField(Rock(1, new Vector2(100, 100)));
        var finder = new PathFinder(NavGrid.Build(field), field);

        Assert.Equal(PathStatus.Found, finder.FindNow(new Vector2(97, 107), new Vector2(160, 106)));

        Assert.Equal(new[] { new Vector2(160, 106) }, finder.Points.ToArray());
    }

    [Fact]
    public void A_goal_beside_a_wall_is_reached_through_its_cells_centre()
    {
        // A brick wall along z ∈ [97.2, 98.2] from x 60 to 104; the goal (101.9, 95.9) is 1.3 m south of it in open cell
        // (25, 23), whose centre (102, 94) is 3.2 m off. Along the row of centres the way clears the wall; a straight line
        // from the start to the goal itself would close in on it the whole last 30 m.
        Prop wall = new(1, PropKind.BrickWall, new Obb2(new Vector2(82, 97.7f), new Vector2(22, 0.5f), 0f), -0.3f, 3f);
        Battlefield field = TestWorld.FlatField(wall);
        NavGrid grid = NavGrid.Build(field);
        var finder = new PathFinder(grid, field);
        Vector2 from = new(30, 94), to = new(101.9f, 95.9f);
        Assert.True(grid.IsOpenAt(to));

        Assert.Equal(PathStatus.Found, finder.FindNow(from, to));

        Assert.Equal(new[] { new Vector2(102, 94), to }, finder.Points.ToArray());
        List<Vector2> trail = Trail(finder, from);
        foreach (Vector2 p in Samples(trail.Take(trail.Count - 1).ToList()))
            Assert.True(wall.Footprint.DistanceToPoint(p) >= PathFinder.HullClearance - Slack, $"{p} is {wall.Footprint.DistanceToPoint(p)} m from the wall");
    }

    [Fact]
    public void Smoothing_does_not_wade_down_a_river_the_road_runs_beside()
    {
        // Flat grass; a road (348) down column 10 beside a river (700) twelve cells wide, columns 11–22, that narrows to
        // two at rows 8–10, the one ford. From the road at row 40 to the far bank at (24, 6) the search keeps to the road
        // and crosses at the ford. Every river cell is no dearer than the ford, so "no cell dearer than the search took"
        // would let a segment cut down the river (100 m of it); its cost (about 18 s against the road's 11) doesn't. Cutting
        // the corner into the ford, wading about as far as the ford itself, costs little and is allowed: no water is
        // crossed more than three rows (12 m) up from the ford.
        Battlefield field = TestWorld.FlatField();
        NavGrid grid = NavGrid.Build(field);
        for (int j = 1; j < grid.CellsZ - 1; j++)
        {
            if (j >= 4) grid.SetCostForTests(10, j, 348);
            for (int i = 11; i <= 22; i++)
                if (j is < 8 or > 10 || i <= 12) grid.SetCostForTests(i, j, 700);
        }
        var finder = new PathFinder(grid, field);
        Vector2 from = NavGrid.CentreOf(10, 40), to = NavGrid.CentreOf(24, 6);

        Assert.Equal(PathStatus.Found, finder.FindNow(from, to));

        _output.WriteLine($"cost {finder.Cost}, {finder.Points.Length} points: {string.Join(" ", finder.Points.ToArray())}");
        int wading = 0;
        foreach (Vector2 p in Samples(Trail(finder, from)))
        {
            (int i, int j) = NavGrid.CellOf(p);
            if (j > 13 && grid.CostAt(i, j) == 700) wading++;
        }
        Assert.Equal(0, wading);
        Assert.True(TrailCost(grid, Trail(finder, from)) <= finder.Cost * (1 + PathFinder.SmoothingExcess) + 50, "the smoothed path costs more than the search's");
    }

    [Fact]
    public void Smoothing_does_not_squeeze_between_two_steep_cells_touching_at_a_corner()
    {
        // Two spikes 4 m high at (82, 82) and (86, 86) close cells (20, 20) and (21, 21) by slope alone (no prop for the
        // hull clearance to catch); they meet at the corner (84, 84). The line from (74, 94) to (94, 74) runs through
        // that corner between them, over open cells only.
        var heights = new float[TestWorld.Samples * TestWorld.Samples];
        heights[41 * TestWorld.Samples + 41] = 4f;
        heights[43 * TestWorld.Samples + 43] = 4f;
        Battlefield field = TestWorld.Field(new HeightField(TestWorld.Samples, TestWorld.Samples, TestWorld.Cell, heights));
        NavGrid grid = NavGrid.Build(field);
        for (int j = 17; j <= 24; j++)
        for (int i = 17; i <= 24; i++)
            Assert.Equal((i, j) is not ((20, 20) or (21, 21)), grid.IsOpen(i, j));
        var finder = new PathFinder(grid, field);
        Vector2 from = new(74, 94), to = new(94, 74), corner = new(84, 84);

        Assert.Equal(PathStatus.Found, finder.FindNow(from, to));

        List<Vector2> trail = Trail(finder, from);
        for (int k = 1; k < trail.Count; k++)
            Assert.True(Samples(trail[k - 1], trail[k]).Min(p => Vector2.Distance(p, corner)) > 1f, $"{trail[k - 1]} → {trail[k]} squeezes through {corner}");
        foreach (Vector2 p in Samples(trail))
            Assert.True(grid.IsOpenAt(p), $"{p} is in a closed cell");
    }

    [Fact]
    public void Searches_come_out_the_same_across_the_generation_counter_wrapping()
    {
        Battlefield field = RiverlandFixture.Field;
        IReadOnlyList<Vector2> waypoints = RiverlandMap.Waypoints;
        var finder = new PathFinder(RiverGrid, field);
        finder.SetGenerationForTests(int.MaxValue - 3);

        for (int k = 0; k < 8; k++)
        {
            Vector2 from = waypoints[k * 3], to = waypoints[(k * 11 + 7) % waypoints.Count];
            var fresh = new PathFinder(RiverGrid, field);
            Assert.Equal(PathStatus.Found, fresh.FindNow(from, to));

            Assert.Equal(PathStatus.Pending, finder.Begin(from, to));
            while (finder.Continue(700) == PathStatus.Pending) { }

            Assert.Equal(PathStatus.Found, finder.Status);
            Assert.Equal((fresh.Cost, fresh.Expansions), (finder.Cost, finder.Expansions));
            Assert.Equal(fresh.Cells.ToArray(), finder.Cells.ToArray());
            Assert.Equal(fresh.Points.ToArray(), finder.Points.ToArray());
        }
        Assert.InRange(finder.Generation, 1, 8);   // it wrapped
    }

    [Fact]
    public void Before_a_search_nothing_is_found_and_bad_budgets_are_refused()
    {
        Battlefield field = TestWorld.FlatField();
        var finder = new PathFinder(NavGrid.Build(field), field);

        Assert.Equal(PathStatus.Idle, finder.Status);
        Assert.Equal(PathStatus.Idle, finder.Continue(100));
        Assert.True(finder.Points.IsEmpty);
        Assert.True(finder.Cells.IsEmpty);
        Assert.Throws<ArgumentOutOfRangeException>(() => finder.Continue(-1));

        Assert.Equal(PathStatus.Pending, finder.Begin(new Vector2(102, 102), new Vector2(103, 103)));
        Assert.Equal(PathStatus.Pending, finder.Continue(0));
        Assert.Equal(PathStatus.Found, finder.Continue(1));   // the start's cell is the goal's
        Assert.Equal(new[] { new Vector2(103, 103) }, finder.Points.ToArray());
        Assert.Equal(0, finder.Cost);
        Assert.Throws<ArgumentException>(() => new PathFinder(RiverGrid, field));
    }

    [Fact]
    public void Searching_allocates_nothing()
    {
        IReadOnlyList<Vector2> waypoints = RiverlandMap.Waypoints;
        Allocation.AssertNone(() =>
        {
            var finder = new PathFinder(RiverGrid, RiverlandFixture.Field);
            for (int k = 0; k < 5; k++) finder.FindNow(waypoints[k], waypoints[39 - k]);   // warm-up

            Allocation.Window window = Allocation.Start();
            int found = 0;
            for (int k = 0; k < 20; k++)
            {
                if (finder.FindNow(waypoints[k], waypoints[(k * 7 + 11) % waypoints.Count]) == PathStatus.Found) found++;
                finder.Begin(waypoints[k + 1], waypoints[k + 20]);
                while (finder.Continue(500) == PathStatus.Pending) { }
            }
            Allocation.Measured measured = window.Stop();

            Assert.Equal(20, found);
            return measured;
        });
    }

    // ---- helpers ----

    private static Vector2 Centre(NavGrid grid, int cell) => NavGrid.CentreOf(cell % grid.CellsX, cell / grid.CellsX);

    /// <summary>The way a tank drives it: from where it asked from through every point.</summary>
    private static List<Vector2> Trail(PathFinder finder, Vector2 from)
    {
        var trail = new List<Vector2> { from };
        trail.AddRange(finder.Points.ToArray());
        return trail;
    }

    /// <summary>Fells the next standing trees until the grid changes.</summary>
    private static void FellATree(Battlefield field, NavGrid grid, Queue<int> trees)
    {
        int version = grid.Version;
        while (grid.Version == version)
        {
            int id = trees.Dequeue();
            field.SetDurability(id, 0);
            grid.Refresh(field, new[] { Destroyed(id) });
        }
    }

    /// <summary>
    /// What driving <paramref name="trail"/> costs on <paramref name="grid"/>, sampled every <see cref="CostSample"/> m
    /// (each step's length in cells × the cost of the cell its middle is in).
    /// </summary>
    private static double TrailCost(NavGrid grid, List<Vector2> trail)
    {
        double cost = 0;
        for (int k = 1; k < trail.Count; k++) cost += SegmentCost(grid, trail[k - 1], trail[k]);
        return cost;
    }

    private static double SegmentCost(NavGrid grid, Vector2 a, Vector2 b)
    {
        float length = Vector2.Distance(a, b);
        int steps = System.Math.Max(1, (int)MathF.Ceiling(length / CostSample));
        double cost = 0;
        for (int s = 0; s < steps; s++)
        {
            (int i, int j) = NavGrid.CellOf(Vector2.Lerp(a, b, (s + 0.5f) / steps));
            cost += grid.CostAt(i, j);
        }
        return cost * length / steps / NavGrid.CellSize;
    }

    private static float Length(List<Vector2> trail)
    {
        float length = 0f;
        for (int k = 1; k < trail.Count; k++) length += Vector2.Distance(trail[k - 1], trail[k]);
        return length;
    }

    /// <summary>Every <see cref="Sample"/> m along each leg of <paramref name="trail"/>, both ends included.</summary>
    private static IEnumerable<Vector2> Samples(List<Vector2> trail)
    {
        for (int k = 1; k < trail.Count; k++)
            foreach (Vector2 p in Samples(trail[k - 1], trail[k]))
                yield return p;
    }

    private static List<Vector2> Samples(Vector2 a, Vector2 b)
    {
        int steps = System.Math.Max(1, (int)MathF.Ceiling(Vector2.Distance(a, b) / Sample));
        var points = new List<Vector2>(steps + 1);
        for (int s = 0; s <= steps; s++) points.Add(Vector2.Lerp(a, b, s / (float)steps));
        return points;
    }

    private static int IndexOf(ReadOnlySpan<int> cells, int cell, int after)
    {
        for (int k = after + 1; k < cells.Length; k++)
            if (cells[k] == cell)
                return k;
        return -1;
    }

    private static int RandomOpenCell(NavGrid grid, ref XorShift rng)
    {
        while (true)
        {
            int cell = (int)(rng.NextUInt() % (uint)(grid.CellsX * grid.CellsZ));
            if (grid.Costs[cell] != NavGrid.Blocked) return cell;
        }
    }

    /// <summary>The step a path takes from cell a to its neighbour b, by the finder's rules; −1 when it may not.</summary>
    private static int Step(NavGrid grid, int a, int b)
    {
        int ai = a % grid.CellsX, aj = a / grid.CellsX, bi = b % grid.CellsX, bj = b / grid.CellsX;
        int ca = grid.CostAt(ai, aj), cb = grid.CostAt(bi, bj);
        if (ca == NavGrid.Blocked || cb == NavGrid.Blocked || System.Math.Abs(ai - bi) > 1 || System.Math.Abs(aj - bj) > 1 || a == b) return -1;
        if (ai == bi || aj == bj) return (ca + cb) / 2;
        if (!grid.IsOpen(bi, aj) || !grid.IsOpen(ai, bj)) return -1;
        return (ca + cb) * 707 / 1000;
    }

    private static int CostAlong(NavGrid grid, ReadOnlySpan<int> cells)
    {
        int total = 0;
        for (int k = 1; k < cells.Length; k++)
        {
            int step = Step(grid, cells[k - 1], cells[k]);
            Assert.True(step >= 0, $"cells {cells[k - 1]} → {cells[k]} is not a step");
            total += step;
        }
        return total;
    }

    /// <summary>Plain Dijkstra over every cell with the same steps: the cheapest cost from start to goal, or −1.</summary>
    private static int Dijkstra(NavGrid grid, int start, int goal)
    {
        var best = new int[grid.CellsX * grid.CellsZ];
        Array.Fill(best, int.MaxValue);
        var queue = new PriorityQueue<int, int>();
        best[start] = 0;
        queue.Enqueue(start, 0);
        while (queue.TryDequeue(out int cell, out int g))
        {
            if (g > best[cell]) continue;
            if (cell == goal) return g;
            int ci = cell % grid.CellsX, cj = cell / grid.CellsX;
            for (int dj = -1; dj <= 1; dj++)
            for (int di = -1; di <= 1; di++)
            {
                int ni = ci + di, nj = cj + dj;
                if (!grid.IsOpen(ni, nj)) continue;
                int next = nj * grid.CellsX + ni, step = Step(grid, cell, next);
                if (step < 0 || g + step >= best[next]) continue;
                best[next] = g + step;
                queue.Enqueue(next, g + step);
            }
        }
        return -1;
    }
}
