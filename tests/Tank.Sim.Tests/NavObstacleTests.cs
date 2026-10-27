using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Navigation;
using Tank.Sim.Vehicles;
using Tank.Testing;

namespace Tank.Sim.Tests;

/// <summary>
/// What the grid can't see (5C batch B review follow-up): wrecks and tanks standing still are discs a search steers round.
/// The finder takes them as <see cref="NavDisc"/>s fixed for the whole search; the path service makes them from the tanks
/// it observes, leaving out the tank that asks. Flat ground is <see cref="TestWorld.FlatField"/> (200 m, 50 × 50 cells).
/// </summary>
public sealed class NavObstacleTests
{
    /// <summary>Distances the tests measure in float may come out this much short of what the finder checked.</summary>
    private const float Slack = 1e-3f;

    /// <summary>Two rows of rock 80 m long make a channel 10 m wide (z 96–106); the grid leaves one row of cells open along it (z 100–104).</summary>
    private static readonly Prop[] Channel =
    {
        new(1, PropKind.Rock, new Obb2(new Vector2(100, 93), new Vector2(40, 3), 0f), -1f, 4f),
        new(2, PropKind.Rock, new Obb2(new Vector2(100, 109), new Vector2(40, 3), 0f), -1f, 4f),
    };

    private static readonly Vector2 West = new(30, 102), East = new(170, 102), InTheChannel = new(100, 102);

    /// <summary>A medium's half-diagonal, m.</summary>
    private static readonly float MediumRadius = NavDisc.RadiusOf(TankCatalog.Medium);

    private static PathFinder ChannelFinder(out NavGrid grid)
    {
        Battlefield field = TestWorld.FlatField(Channel);
        grid = NavGrid.Build(field);
        return new PathFinder(grid, field);
    }

    [Fact]
    public void A_disc_closes_the_cells_round_a_tank_and_the_path_keeps_off_it()
    {
        PathFinder finder = ChannelFinder(out NavGrid grid);
        Assert.Equal(PathStatus.Found, finder.FindNow(West, East));
        Vector2[] straight = finder.Points.ToArray();
        foreach (Vector2 p in straight) Assert.True(MathF.Abs(p.Y - 102) < 3f, $"{p} is off the channel");
        Assert.Equal(PathStatus.Found, finder.FindNow(West, East, ReadOnlySpan<NavDisc>.Empty));
        Assert.Equal(straight, finder.Points.ToArray());   // no discs: the search it always was

        var disc = new NavDisc(InTheChannel, MediumRadius);
        Assert.Equal(PathStatus.Found, finder.FindNow(West, East, new[] { disc }));
        Assert.Contains(finder.Points.ToArray(), p => p.Y < 88 || p.Y > 114);   // round the rocks
        foreach (int cell in finder.Cells)
        {
            Vector2 centre = NavGrid.CentreOf(cell % grid.CellsX, cell / grid.CellsX);
            Assert.True(Vector2.Distance(centre, disc.Centre) >= disc.Radius + NavGrid.Clearance, $"cell {centre} is in the disc");
        }
        var trail = new List<Vector2> { West };
        trail.AddRange(finder.Points.ToArray());
        for (int k = 1; k < trail.Count; k++)
            Assert.True(DistanceToSegment(disc.Centre, trail[k - 1], trail[k]) >= disc.Radius + PathFinder.HullClearance - Slack,
                $"{trail[k - 1]} → {trail[k]} passes {DistanceToSegment(disc.Centre, trail[k - 1], trail[k]):F2} m from the tank");
    }

    [Fact]
    public void A_tank_already_inside_a_discs_clearance_is_not_boxed_in_by_it()
    {
        // Side by side with a still tank, 3.5 m from its centre: the clearance round it would close every cell about the
        // start's. The disc shrinks to the start's distance for this search: nothing nearer the tank than it is opens.
        PathFinder finder = ChannelFinder(out NavGrid grid);
        var disc = new NavDisc(new Vector2(60, 60), MediumRadius);
        var from = new Vector2(60, 63.5f);
        float near = Vector2.Distance(from, disc.Centre);

        Assert.Equal(PathStatus.Found, finder.FindNow(from, new Vector2(60, 20), new[] { disc }));

        Assert.Equal(NavGrid.CellOf(from), CellAt(grid, finder.Cells[0]));   // the start's cell is open, whatever covers it
        for (int k = 1; k < finder.Cells.Length; k++)
            Assert.True(Vector2.Distance(Centre(grid, finder.Cells[k]), disc.Centre) >= near, $"{Centre(grid, finder.Cells[k])} is nearer the tank");
        Assert.Contains(finder.Cells.ToArray(), c => Vector2.Distance(Centre(grid, c), disc.Centre) < disc.Radius + NavGrid.Clearance);
    }

    [Fact]
    public void A_goal_inside_a_disc_moves_to_the_nearest_cell_clear_of_it_or_there_is_no_path()
    {
        PathFinder finder = ChannelFinder(out _);
        var goal = new Vector2(60, 40);
        var disc = new NavDisc(goal, MediumRadius);

        Assert.Equal(PathStatus.Found, finder.FindNow(new Vector2(60, 160), goal, new[] { disc }));
        Vector2 end = finder.Points[^1];
        Assert.True(Vector2.Distance(end, disc.Centre) >= disc.Radius + NavGrid.Clearance, $"ends at {end}");
        Assert.True(Vector2.Distance(end, goal) <= PathFinder.SnapRadius, $"ends {Vector2.Distance(end, goal):F1} m from the goal");
        Assert.Equal(NavGrid.CentreOf(NavGrid.CellOf(end).I, NavGrid.CellOf(end).J), end);

        var huge = new NavDisc(goal, PathFinder.SnapRadius);   // nothing open within 12 m of the goal
        Assert.Equal(PathStatus.NoPath, finder.FindNow(new Vector2(60, 160), goal, new[] { huge }));
    }

    [Fact]
    public void A_gap_closed_by_discs_leaves_no_path_when_there_is_no_other_way()
    {
        // The channel's east end shut by a rock: the only way into it is from the west.
        var props = new List<Prop>(Channel) { new(3, PropKind.Rock, new Obb2(new Vector2(143, 102), new Vector2(3, 10), 0f), -1f, 4f) };
        Battlefield field = TestWorld.FlatField(props.ToArray());
        var finder = new PathFinder(NavGrid.Build(field), field);
        var inside = new Vector2(120, 102);
        Assert.Equal(PathStatus.Found, finder.FindNow(West, inside));

        var two = new[] { new NavDisc(new Vector2(70, 102), MediumRadius), new NavDisc(new Vector2(78, 102), MediumRadius) };
        Assert.Equal(PathStatus.NoPath, finder.FindNow(West, inside, two));
        Assert.Equal(PathStatus.Found, finder.FindNow(West, new Vector2(30, 140), two));   // elsewhere they change nothing
    }

    [Fact]
    public void More_discs_than_tanks_are_refused()
    {
        PathFinder finder = ChannelFinder(out _);
        var many = new NavDisc[PathFinder.MaxDiscs + 1];
        Assert.Throws<ArgumentException>(() => finder.Begin(West, East, many));
        Assert.Throws<ArgumentException>(() => finder.Begin(West, East, new[] { new NavDisc(InTheChannel, float.NaN) }));
    }

    private static TankState At(int id, Vector2 at, bool wreck = false) =>
        new() { Id = id, Spec = TankCatalog.Medium, Position = at, Health = wreck ? 0 : TankCatalog.Medium.MaxHealth, IsDestroyed = wreck };

    [Fact]
    public void Wrecks_at_once_and_tanks_still_for_3_s_are_steered_round_by_everyone_but_themselves()
    {
        Battlefield field = TestWorld.FlatField(Channel);
        var paths = new PathService(NavGrid.Build(field), field);
        paths.Register(1);
        paths.Register(2);
        paths.Register(3);
        var tanks = new[] { At(1, new Vector2(60, 40)), At(2, InTheChannel), At(3, new Vector2(150, 150), wreck: true) };

        paths.Observe(tanks, tick: 0);
        Assert.Equal(1, paths.ObstacleCount);   // the wreck
        paths.Observe(tanks, PathService.StillTicks - 1);
        Assert.Equal(1, paths.ObstacleCount);
        paths.Observe(tanks, PathService.StillTicks);
        Assert.Equal(3, paths.ObstacleCount);   // and the two standing still

        // Tank 1 is steered round tank 2 in the channel; tank 2's own disc doesn't box it in.
        paths.Request(1, West, East);
        RunUntilSettled(paths);
        Assert.Equal(PathStatus.Found, paths.StatusOf(1));
        Vector2[] round = Take(paths, 1);
        Assert.Contains(round, p => p.Y < 88 || p.Y > 114);
        paths.Request(2, InTheChannel, new Vector2(60, 160));
        RunUntilSettled(paths);
        Assert.Equal(PathStatus.Found, paths.StatusOf(2));

        // Tank 2 drives off: moved 1 m or more, it is no obstacle until it has stood still again for 3 s.
        tanks[1].Position += new Vector2(PathService.StillDistance, 0);
        paths.Observe(tanks, PathService.StillTicks + 1);
        Assert.Equal(2, paths.ObstacleCount);
        paths.Request(1, West, East);
        RunUntilSettled(paths);
        Assert.All(Take(paths, 1), p => Assert.True(MathF.Abs(p.Y - 102) < 3f, $"{p} is off the channel"));
        paths.Observe(tanks, 2 * PathService.StillTicks);
        Assert.Equal(2, paths.ObstacleCount);
        paths.Observe(tanks, 2 * PathService.StillTicks + 1);
        Assert.Equal(3, paths.ObstacleCount);

        // A tank that left the battle is forgotten.
        paths.Observe(new[] { tanks[0], tanks[2] }, 2 * PathService.StillTicks + 2);
        Assert.Equal(2, paths.ObstacleCount);
    }

    [Fact]
    public void Observing_and_searching_round_discs_allocates_nothing()
    {
        Allocation.AssertNone(() =>
        {
            Battlefield field = TestWorld.FlatField(Channel);
            var paths = new PathService(NavGrid.Build(field), field);
            for (int id = 1; id <= 4; id++) paths.Register(id);
            var tanks = new[] { At(1, West), At(2, InTheChannel), At(3, new Vector2(150, 150), wreck: true), At(4, new Vector2(40, 160)) };
            var into = new Vector2[PathFinder.MaxPoints];
            for (int tick = 0; tick < 200; tick++) paths.Observe(tanks, tick);   // warm-up

            Allocation.Window window = Allocation.Start();
            for (int tick = 200; tick < 400; tick++)
            {
                tanks[3].Position += new Vector2(0.05f, 0);
                paths.Observe(tanks, tick);
                if (tick % 20 == 0) paths.Request(1, West, East);
                paths.Run();
                paths.TakePath(1, into);
            }
            Allocation.Measured measured = window.Stop();
            Assert.Equal(3, paths.ObstacleCount);
            return measured;
        });
    }

    private static void RunUntilSettled(PathService paths)
    {
        for (int k = 0; k < 100; k++) paths.Run();
    }

    private static Vector2[] Take(PathService paths, int id)
    {
        var into = new Vector2[PathFinder.MaxPoints];
        return into.AsSpan(0, paths.TakePath(id, into)).ToArray();
    }

    private static (int I, int J) CellAt(NavGrid grid, int cell) => (cell % grid.CellsX, cell / grid.CellsX);

    private static Vector2 Centre(NavGrid grid, int cell) => NavGrid.CentreOf(cell % grid.CellsX, cell / grid.CellsX);

    private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = ab.LengthSquared() > 0 ? Math.Clamp(Vector2.Dot(p - a, ab) / ab.LengthSquared(), 0f, 1f) : 0f;
        return Vector2.Distance(p, a + ab * t);
    }
}
