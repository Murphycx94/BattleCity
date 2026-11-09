using System.Diagnostics;
using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Navigation;
using Tank.Testing;
using Xunit.Abstractions;

namespace Tank.Sim.Tests;

/// <summary>
/// The match's shared searches (stage 5 design §4.1): one budget of expansions a tick for every bot, served round-robin
/// by tank id, at most one search finished a call. Short requests run on <see cref="TestWorld.FlatField"/> (50 × 50
/// cells, the outer ring closed); long ones on the riverland.
/// </summary>
public sealed class PathServiceTests
{
    private static readonly Vector2 West = new(20, 100), East = new(60, 100), North = new(100, 20), South = new(100, 60);

    private readonly ITestOutputHelper _output;

    public PathServiceTests(ITestOutputHelper output) => _output = output;

    private static PathService Flat(out NavGrid grid, out Battlefield field)
    {
        field = TestWorld.FlatField();
        grid = NavGrid.Build(field);
        return new PathService(grid, field);
    }

    private static PathService Flat() => Flat(out _, out _);

    [Fact]
    public void A_long_request_is_served_over_several_ticks()
    {
        Vector2 from = new(112, 96), to = new(1140, 808);
        var finder = new PathFinder(RiverlandFixture.Grid, RiverlandFixture.Field);
        Assert.Equal(PathStatus.Found, finder.FindNow(from, to));
        int n = finder.Expansions;
        Assert.True(n > PathService.ExpansionsPerTick, $"{n} expansions fit one tick");
        int ticks = (n + PathService.ExpansionsPerTick - 1) / PathService.ExpansionsPerTick;
        var paths = new PathService(RiverlandFixture.Grid, RiverlandFixture.Field);
        paths.Register(4);

        paths.Request(4, from, to);
        for (int t = 1; t < ticks; t++)
        {
            paths.Run();
            Assert.Equal(PathStatus.Pending, paths.StatusOf(4));
            Assert.Equal(PathService.ExpansionsPerTick, paths.LastRunExpansions);
        }
        paths.Run();

        _output.WriteLine($"{n} expansions over {ticks} ticks, {finder.Points.Length} points");
        Assert.Equal(PathStatus.Found, paths.StatusOf(4));
        Assert.Equal(n - (ticks - 1) * PathService.ExpansionsPerTick, paths.LastRunExpansions);
        Assert.Equal(finder.Points.ToArray(), Take(paths, 4));
        Assert.Equal((1, 0), (paths.Searches, paths.Failures));
    }

    [Fact]
    public void Requests_are_served_round_robin_by_tank_id()
    {
        PathService paths = Flat();
        foreach (int id in new[] { 5, 2, 9 }) paths.Register(id);
        paths.Request(9, West, East);
        paths.Request(2, North, South);
        paths.Request(5, East, West);

        Assert.Equal(new[] { 2, 5, 9 }, new[] { RunUntilOneFinishes(paths), RunUntilOneFinishes(paths), RunUntilOneFinishes(paths) });

        paths.Request(9, South, North);
        paths.Request(2, West, South);
        Assert.Equal(2, RunUntilOneFinishes(paths));   // after 9, round to the start
        Assert.Equal(9, RunUntilOneFinishes(paths));
        Assert.Equal(5, paths.Searches);
    }

    [Fact]
    public void A_new_request_replaces_the_pending_one()
    {
        PathService paths = Flat();
        paths.Register(2);
        paths.Register(3);

        paths.Request(2, West, East);
        paths.Request(2, North, South);   // before any search
        Assert.Equal(2, RunUntilOneFinishes(paths));
        Assert.Equal(South, Take(paths, 2)[^1]);

        paths.Request(2, West, East);
        paths.Run(5);   // searching for 2 now
        Assert.Equal(PathStatus.Pending, paths.StatusOf(2));
        paths.Request(3, East, West);
        paths.Request(2, East, North);
        Assert.Equal(PathStatus.Pending, paths.StatusOf(2));
        Assert.Equal(3, RunUntilOneFinishes(paths));   // the search given up loses its turn: 3 goes first
        Assert.Equal(2, RunUntilOneFinishes(paths));   // then 2, with the new ends
        Assert.Equal(North, Take(paths, 2)[^1]);
        Assert.Equal(3, paths.Searches);   // the one given up doesn't count
    }

    [Fact]
    public void A_tank_that_asks_again_every_call_does_not_hold_up_the_others()
    {
        // Tank 1 asks for a long way afresh before every call: each time its search is dropped and it goes to the back,
        // so 2 and 3 still get theirs.
        PathService paths = Flat();
        for (int id = 1; id <= 3; id++) paths.Register(id);
        paths.Request(2, West, East);
        paths.Request(3, North, South);

        int calls = 0;
        while (paths.StatusOf(2) == PathStatus.Pending || paths.StatusOf(3) == PathStatus.Pending)
        {
            Assert.True(++calls < 200, "tank 1 holds the others up");
            paths.Request(1, new Vector2(20, 20), new Vector2(180, 180));
            paths.Run(10);
        }

        _output.WriteLine($"2 and 3 served in {calls} calls");
        Assert.Equal(PathStatus.Found, paths.StatusOf(2));
        Assert.Equal(PathStatus.Found, paths.StatusOf(3));
        Assert.Equal(PathStatus.Pending, paths.StatusOf(1));
        Assert.Equal(2, paths.Searches);
    }

    [Fact]
    public void At_most_one_search_finishes_a_call()
    {
        PathService paths = Flat();
        for (int id = 1; id <= 3; id++) paths.Register(id);
        paths.Request(1, West, East);
        paths.Request(2, North, South);
        paths.Request(3, East, North);

        for (int call = 1; call <= 3; call++)
        {
            paths.Run(100_000);
            Assert.Equal(call, paths.Searches);
            int found = 0;
            for (int id = 1; id <= 3; id++)
                if (paths.StatusOf(id) == PathStatus.Found) found++;
            Assert.Equal(call, found);
        }
        paths.Run(100_000);
        Assert.Equal(3, paths.Searches);
        Assert.Equal(0, paths.LastRunExpansions);
    }

    [Fact]
    public void Taking_a_path_copies_it_and_resets_to_idle()
    {
        PathService paths = Flat(out NavGrid grid, out Battlefield field);
        var finder = new PathFinder(grid, field);
        Assert.Equal(PathStatus.Found, finder.FindNow(West, new Vector2(150, 170)));
        paths.Register(7);
        Assert.Equal(PathStatus.Idle, paths.StatusOf(7));
        paths.Request(7, West, new Vector2(150, 170));
        RunUntilOneFinishes(paths);

        var into = new Vector2[PathFinder.MaxPoints];
        int count = paths.TakePath(7, into);

        Assert.Equal(finder.Points.ToArray(), into.AsSpan(0, count).ToArray());
        Assert.Equal(PathStatus.Idle, paths.StatusOf(7));
        Assert.Equal(0, paths.TakePath(7, into));
    }

    [Fact]
    public void A_request_without_a_way_reports_no_path()
    {
        // A ring of closed cells round (140, 140): its inside is open, but no way leads in.
        PathService paths = Flat(out NavGrid grid, out _);
        for (int k = 30; k <= 40; k++)
        {
            grid.SetCostForTests(k, 30, NavGrid.Blocked);
            grid.SetCostForTests(k, 40, NavGrid.Blocked);
            grid.SetCostForTests(30, k, NavGrid.Blocked);
            grid.SetCostForTests(40, k, NavGrid.Blocked);
        }
        Assert.True(grid.IsOpenAt(new Vector2(140, 140)));
        paths.Register(1);
        paths.Register(2);

        paths.Request(1, West, new Vector2(140, 140));
        while (paths.StatusOf(1) == PathStatus.Pending) paths.Run();

        Assert.Equal(PathStatus.NoPath, paths.StatusOf(1));
        Assert.Equal((1, 1), (paths.Searches, paths.Failures));
        Assert.Equal(0, paths.TakePath(1, new Vector2[PathFinder.MaxPoints]));
        Assert.Equal(PathStatus.NoPath, paths.StatusOf(1));   // until its next request

        // An end with no open cell near it fails at Begin, spends nothing and doesn't end the call.
        paths.Request(2, West, new Vector2(-100, -100));   // next after 1
        paths.Request(1, West, East);
        paths.Run();
        Assert.Equal(PathStatus.NoPath, paths.StatusOf(2));
        Assert.Equal(PathStatus.Found, paths.StatusOf(1));
        Assert.Equal((3, 2), (paths.Searches, paths.Failures));
    }

    [Fact]
    public void Unknown_tanks_are_refused()
    {
        PathService paths = Flat();
        paths.Register(3);

        Assert.Throws<ArgumentException>(() => paths.Request(4, West, East));
        Assert.Throws<ArgumentException>(() => paths.StatusOf(4));
        Assert.Throws<ArgumentException>(() => paths.TakePath(4, new Vector2[PathFinder.MaxPoints]));
        Assert.Throws<ArgumentException>(() => paths.Register(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => paths.Register(0));
        for (int id = 10; id < 10 + Simulation.MaxTanks - 1; id++) paths.Register(id);
        Assert.Throws<InvalidOperationException>(() => paths.Register(99));
        Assert.Throws<ArgumentOutOfRangeException>(() => paths.Run(-1));
    }

    [Fact]
    public void A_short_buffer_is_refused_and_the_path_kept()
    {
        PathService paths = Flat();
        paths.Register(1);
        paths.Request(1, West, new Vector2(150, 170));
        RunUntilOneFinishes(paths);

        Assert.Throws<ArgumentException>(() => paths.TakePath(1, Span<Vector2>.Empty));
        Assert.Equal(PathStatus.Found, paths.StatusOf(1));
        Assert.True(paths.TakePath(1, new Vector2[PathFinder.MaxPoints]) > 0);
    }

    [Fact]
    public void A_grid_that_changes_mid_search_neither_restarts_it_nor_overspends()
    {
        // A tree falls before every call (heard through the service's own Refresh): the search carries on, each call
        // spending its whole budget until the last, and all of them together no more than the cap.
        Battlefield field = RiverlandFixture.Field.Fresh();
        NavGrid grid = RiverlandFixture.Grid.Copy();
        var paths = new PathService(grid, field);
        paths.Register(1);
        Vector2 from = RiverlandMap.Waypoints[15], to = RiverlandMap.Waypoints[27];
        var trees = new Queue<int>(field.Props.Where(p => p.Kind == PropKind.Tree).Select(p => p.Id));
        paths.Request(1, from, to);

        int calls = 0, spent = 0;
        do
        {
            int version = grid.Version;
            while (grid.Version == version)
            {
                int id = trees.Dequeue();
                field.SetDurability(id, 0);
                paths.Refresh(new[] { Destroyed(id) });
            }
            paths.Run();
            calls++;
            spent += paths.LastRunExpansions;
            if (paths.StatusOf(1) == PathStatus.Pending) Assert.Equal(PathService.ExpansionsPerTick, paths.LastRunExpansions);
            else Assert.InRange(paths.LastRunExpansions, 1, PathService.ExpansionsPerTick);
        } while (paths.StatusOf(1) == PathStatus.Pending && calls < 100);

        _output.WriteLine($"{calls} calls, {spent} expansions");
        Assert.Equal(PathStatus.Found, paths.StatusOf(1));
        Assert.InRange(spent, 1, PathFinder.MaxExpansions);
        Assert.Equal(to, Take(paths, 1)[^1]);
    }

    [Fact]
    public void The_service_refreshes_and_syncs_its_own_grid_from_its_own_field()
    {
        Battlefield field = TestWorld.FlatField(
            new Prop(7, PropKind.House, new Obb2(new Vector2(100, 100), new Vector2(6, 4), 0f), -0.3f, 6f),
            new Prop(8, PropKind.House, new Obb2(new Vector2(60, 60), new Vector2(6, 4), 0f), -0.3f, 6f));
        NavGrid grid = NavGrid.Build(field);
        var paths = new PathService(grid, field);
        Assert.Equal(NavGrid.Blocked, grid.CostAt(25, 25));
        Assert.Equal(NavGrid.Blocked, grid.CostAt(15, 15));

        field.SetDurability(7, 0);
        Assert.True(paths.Refresh(new[] { Destroyed(7) }) > 0);

        Assert.Equal(571, grid.CostAt(25, 25));
        Assert.Equal(1, grid.Version);

        field.SetDurability(8, 0);   // unheard
        Assert.Equal(0, paths.Refresh(ReadOnlySpan<GameEvent>.Empty));
        Assert.True(paths.Sync() > 0);

        Assert.Equal(571, grid.CostAt(15, 15));
        Assert.Equal(2, grid.Version);
        Assert.Equal(0, paths.Sync());
    }

    [Fact]
    public void Running_allocates_nothing()
    {
        IReadOnlyList<Vector2> waypoints = RiverlandMap.Waypoints;
        var into = new Vector2[PathFinder.MaxPoints];
        Allocation.AssertNone(() =>
        {
            var paths = new PathService(RiverlandFixture.Grid, RiverlandFixture.Field);
            for (int id = 1; id <= 9; id++) paths.Register(id);
            for (int id = 1; id <= 9; id++) paths.Request(id, waypoints[id], waypoints[id + 20]);   // warm-up
            while (paths.Searches < 9) paths.Run();
            for (int id = 1; id <= 9; id++) paths.TakePath(id, into);

            Allocation.Window window = Allocation.Start();
            int taken = 0;
            for (int t = 0; t < 300; t++)
            {
                if (t % 10 == 0) paths.Request(t / 10 % 9 + 1, waypoints[t / 10 % 40], waypoints[(t / 10 * 7 + 13) % 40]);
                paths.Run();
                for (int id = 1; id <= 9; id++)
                    if (paths.StatusOf(id) == PathStatus.Found && paths.TakePath(id, into) > 0) taken++;
            }
            Allocation.Measured measured = window.Stop();

            Assert.True(taken >= 20, $"only {taken} paths taken");
            return measured;
        });
    }

    // ---- helpers ----

    /// <summary>Runs until a search finishes; the tank it was for.</summary>
    private static int RunUntilOneFinishes(PathService paths)
    {
        int before = paths.Searches;
        for (int calls = 0; paths.Searches == before; calls++)
        {
            Assert.True(calls < 1000, "no search finished");
            paths.Run();
        }
        Assert.Equal(before + 1, paths.Searches);
        return paths.LastServed;
    }

    private static GameEvent Destroyed(int propId) =>
        new(GameEventType.PropDestroyed, 1, 0, 0, Vector3.Zero, Vector3.Zero, PropId: propId);

    private static Vector2[] Take(PathService paths, int tankId)
    {
        var into = new Vector2[PathFinder.MaxPoints];
        return into.AsSpan(0, paths.TakePath(tankId, into)).ToArray();
    }
}

/// <summary>What a call of <see cref="PathService.Run"/> costs with its whole budget spent (Release limits; Debug only loose ones).</summary>
[Collection(TimingCollection.Name)]
[Trait("Category", "Benchmark")]   // wall-clock limits; CI skips them (coverage and shared runners, docs/04 §5)
public sealed class PathServiceTimingTests
{
    private const int Repeats = 3;

    private readonly ITestOutputHelper _output;

    public PathServiceTimingTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void A_tick_of_searching_is_cheap()
    {
        // Nine tanks asking for waypoint-to-waypoint paths again as soon as they have one: every call spends its budget,
        // and a good share of them also trace and smooth a found path.
        IReadOnlyList<Vector2> waypoints = RiverlandMap.Waypoints;
        var paths = new PathService(RiverlandFixture.Grid, RiverlandFixture.Field);
        for (int id = 1; id <= 9; id++) paths.Register(id);
        var into = new Vector2[PathFinder.MaxPoints];
        int next = 0;
        void Ask(int id)
        {
            paths.Request(id, waypoints[next % waypoints.Count], waypoints[(next * 7 + 13) % waypoints.Count]);
            next++;
        }
        for (int id = 1; id <= 9; id++) Ask(id);

        const int Warmup = 100, Block = 500;
        var p99 = new double[Repeats];
        var worst = new double[Repeats];
        var times = new double[Block];
        int finished = 0, full = 0;
        for (int r = -1; r < Repeats; r++)
        {
            int calls = r < 0 ? Warmup : Block;
            for (int c = 0; c < calls; c++)
            {
                long start = Stopwatch.GetTimestamp();
                paths.Run();
                double ms = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
                if (r >= 0)
                {
                    times[c] = ms;
                    if (paths.LastRunExpansions == PathService.ExpansionsPerTick) full++;
                }
                for (int id = 1; id <= 9; id++)
                {
                    PathStatus status = paths.StatusOf(id);
                    if (status == PathStatus.Pending) continue;
                    if (status == PathStatus.Found) paths.TakePath(id, into);
                    if (r >= 0) finished++;
                    Ask(id);
                }
            }
            if (r < 0) continue;
            Array.Sort(times);
            p99[r] = times[(int)(Block * 0.99) - 1];
            worst[r] = times[^1];
        }

        _output.WriteLine($"{Repeats} × {Block} calls ({full} with the whole budget spent, {finished} searches finished): p99 {Join(p99)} ms, worst {Join(worst)} ms");
#if DEBUG
        const double Limit = 4.0;
#else
        const double Limit = 1.0;
#endif
        Assert.True(full >= Repeats * Block / 2, $"only {full} calls spent the whole budget");
        Assert.True(p99.Min() <= Limit, $"p99 of a call was {p99.Min():F3} ms at best (limit {Limit} ms)");
    }

    private static string Join(double[] values) =>
        string.Join(", ", values.Select(v => v.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)));
}
