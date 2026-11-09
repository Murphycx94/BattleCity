using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Modes;
using Tank.Sim.Navigation;
using Xunit.Abstractions;

namespace Tank.Sim.Tests;

/// <summary>
/// What bots know about the two maps (stage 5 design §4.2): three attack lanes and four cover points a team, found with
/// the riverland probe's <c>nav</c> command and checked here on the navigation grid; and <see cref="BotMapData"/> itself.
/// </summary>
public sealed class MapBotDataTests
{
    private const float MinCoverDistance = 30f, MaxCoverDistance = 75f;
    private const float MaxCoverOffDegrees = 80f;
    private const float MinCoverSpawnDistance = 12f, MinCoverSpacing = 19.5f;   // the riverland's nearest two are 20.0 m apart, the sandbox's 21.5 m
    private const float MaxCoverSlopeDegrees = 8f;
    private const float CostTolerance = 0.15f;

    public static TheoryData<string> Maps => new() { "sandbox", "riverland" };

    private static readonly Lazy<Battlefield> LazySandbox = new(MapCatalog.Sandbox.NewBattlefield);

    private readonly ITestOutputHelper _output;

    public MapBotDataTests(ITestOutputHelper output) => _output = output;

    private static Battlefield FieldOf(string id) => id == RiverlandMap.Id ? RiverlandFixture.Field : LazySandbox.Value;

    private sealed record Expected(string[] Names, Vector2[][] Via, Vector2[] Cover0, Vector2[] Cover1, int Flank, int[,] Seconds);

    private static Expected ExpectedOf(string id) => id == RiverlandMap.Id
        ? new Expected(
            new[] { "north", "town", "south" },
            new[]
            {
                new Vector2[] { new(330, 450), new(560, 410), new(800, 410), new(960, 520), new(1040, 700) },
                new Vector2[] { new(330, 530), new(500, 548), new(670, 556), new(860, 600), new(930, 700) },
                new Vector2[] { new(300, 620), new(470, 720), new(630, 910), new(855, 1000), new(930, 950) },
            },
            new Vector2[] { new(182, 422), new(182, 442), new(218, 386), new(214, 430) },
            new Vector2[] { new(990, 798), new(1018, 786), new(1002, 826), new(966, 822) },
            0,
            new[,] { { 127, 127 }, { 104, 100 }, { 133, 134 } })
        : new Expected(
            new[] { "north", "middle", "south" },
            new[]
            {
                new Vector2[] { new(100, 220), new(150, 120), new(226, 70) },
                new Vector2[] { new(166, 238), new(240, 160) },
                new Vector2[] { new(180, 320), new(280, 260), new(330, 160) },
            },
            new Vector2[] { new(70, 334), new(94, 322), new(78, 290), new(58, 278) },
            new Vector2[] { new(274, 114), new(254, 106), new(322, 114), new(286, 86) },
            2,
            new[,] { { 41, 44 }, { 35, 37 }, { 47, 48 } });

    [Theory]
    [MemberData(nameof(Maps))]
    public void Each_map_has_three_lanes_and_four_cover_points_a_team(string id)
    {
        BotMapData bots = MapCatalog.Get(id).Bots;
        Expected expected = ExpectedOf(id);

        Assert.False(bots.IsEmpty);
        Assert.Equal(expected.Names, bots.Lanes.Select(l => l.Name));
        for (int k = 0; k < expected.Via.Length; k++)
        {
            Assert.Equal(expected.Via[k], bots.Lanes[k].Via);
            Assert.Equal(expected.Via[k], bots.ViaFor(k, 0));
            Assert.Equal(expected.Via[k].Reverse(), bots.ViaFor(k, 1));
        }
        Assert.Equal(expected.Cover0, bots.CoverOf(0));
        Assert.Equal(expected.Cover1, bots.CoverOf(1));
    }

    [Theory]
    [MemberData(nameof(Maps))]
    public void Every_via_and_cover_point_stands_in_an_open_cell_with_open_neighbours(string id)
    {
        MapDefinition map = MapCatalog.Get(id);
        NavGrid grid = map.NewNavGrid();

        foreach ((string what, Vector2 p) in Points(map.Bots))
        {
            (int ci, int cj) = NavGrid.CellOf(p);
            for (int dj = -1; dj <= 1; dj++)
            for (int di = -1; di <= 1; di++)
                Assert.True(grid.IsOpen(ci + di, cj + dj), $"{what} {p}: cell ({ci + di}, {cj + dj}) is closed");
        }
    }

    [Theory]
    [MemberData(nameof(Maps))]
    public void Cover_points_lie_in_front_of_their_base(string id)
    {
        MapDefinition map = MapCatalog.Get(id);
        Battlefield field = FieldOf(id);
        BotMapData bots = map.Bots;

        foreach (TeamBase b in map.Bases)
        {
            IReadOnlyList<Vector2> cover = bots.CoverOf(b.Team);
            Vector2 approach = Vector2.Zero;
            for (int k = 0; k < bots.Lanes.Count; k++) approach += bots.ViaFor(k, (byte)(1 - b.Team))[^1];
            approach /= bots.Lanes.Count;
            Vector2[] spawns = map.Spawns.Team.Where(s => s.Team == b.Team).Select(s => s.Position).ToArray();

            for (int k = 0; k < cover.Count; k++)
            {
                Vector2 p = cover[k];
                string where = $"team {b.Team}'s cover point {p}";
                Assert.InRange(Vector2.Distance(p, b.Centre), MinCoverDistance, MaxCoverDistance);
                float off = Degrees(MathF.Acos(Math.Clamp(Vector2.Dot(Vector2.Normalize(p - b.Centre), Vector2.Normalize(approach - b.Centre)), -1f, 1f)));
                Assert.True(off <= MaxCoverOffDegrees, $"{where} is {off:F0}° off the lanes' approach {approach}");
                foreach (Vector2 s in spawns)
                    Assert.True(Vector2.Distance(p, s) >= MinCoverSpawnDistance, $"{where} is {Vector2.Distance(p, s):F1} m from spawn {s}");
                for (int m = k + 1; m < cover.Count; m++)
                    Assert.True(Vector2.Distance(p, cover[m]) >= MinCoverSpacing, $"{where} is {Vector2.Distance(p, cover[m]):F1} m from {cover[m]}");
                float slope = Degrees(MathF.Acos(field.Terrain.NormalAt(p.X, p.Y).Y));
                Assert.True(slope <= MaxCoverSlopeDegrees, $"{where} stands on a {slope:F1}° slope");
                Assert.Equal(0f, field.WaterDepthAt(p));
            }
        }
    }

    [Theory]
    [MemberData(nameof(Maps))]
    public void Every_lane_leads_from_each_teams_spawns_to_the_enemy_base(string id)
    {
        MapDefinition map = MapCatalog.Get(id);
        BotMapData bots = map.Bots;
        Expected expected = ExpectedOf(id);
        var finder = new PathFinder(map.NewNavGrid(), FieldOf(id));

        for (byte team = 0; team <= 1; team++)
        {
            Vector2 from = SpawnCentre(map, team);
            Vector2 goal = map.Bases.Single(b => b.Team != team).Centre;
            for (int lane = 0; lane < bots.Lanes.Count; lane++)
            {
                int cost = 0, worst = 0;
                float length = 0;
                Vector2 at = from;
                IReadOnlyList<Vector2> via = bots.ViaFor(lane, team);
                for (int k = 0; k <= via.Count; k++)
                {
                    Vector2 to = k < via.Count ? via[k] : goal;
                    Assert.True(finder.FindNow(at, to) == PathStatus.Found, $"team {team}, lane {bots.Lanes[lane].Name}: no path {at} → {to}");
                    cost += finder.Cost;
                    worst = Math.Max(worst, finder.Expansions);
                    length += Vector2.Distance(at, to);
                    at = to;
                }
                int seconds = expected.Seconds[lane, team];
                _output.WriteLine($"{id} team {team} {bots.Lanes[lane].Name}: {cost / 1000f:F1} s at {NavGrid.ReferenceSpeed} m/s " +
                    $"(plan {seconds} s), {length:F0} m as the crow flies, most expansions in a leg {worst}");
                Assert.InRange(cost, seconds * 1000 * (1 - CostTolerance), seconds * 1000 * (1 + CostTolerance));
            }
        }
    }

    [Theory]
    [MemberData(nameof(Maps))]
    public void Everything_a_bot_drives_to_is_connected(string id)
    {
        MapDefinition map = MapCatalog.Get(id);
        NavGrid grid = map.NewNavGrid();
        var finder = new PathFinder(grid, FieldOf(id));
        Vector2 home = map.Bases.Single(b => b.Team == 0).Centre;

        var points = new List<(string What, Vector2 P)>();
        points.Add(("team 0's base", home));
        points.AddRange(map.Spawns.Team.Select(s => ($"team {s.Team} spawn", s.Position)));
        points.AddRange(map.Spawns.Solo.Select(s => ("solo spawn", s.Position)));
        points.AddRange(map.Waypoints.Select(w => ("waypoint", w)));
        points.Add(("team 1's base", map.Bases.Single(b => b.Team == 1).Centre));
        points.AddRange(Points(map.Bots));

        int most = 0;
        foreach ((string what, Vector2 p) in points)
        {
            // In an open cell itself: the search never has to snap it (up to 12 m) to somewhere else.
            Assert.True(grid.IsOpenAt(p), $"{what} {p} is in a closed cell");
            Assert.True(finder.FindNow(p, home) == PathStatus.Found, $"{what} {p}: no path to team 0's base");
            most = Math.Max(most, finder.Expansions);
        }
        _output.WriteLine($"{id}: {points.Count} points, each in an open cell, connected to team 0's base, at most {most} expansions");
    }

    [Theory]
    [MemberData(nameof(Maps))]
    public void The_flank_lane_is_the_outermost(string id)
    {
        MapDefinition map = MapCatalog.Get(id);
        Assert.Equal(ExpectedOf(id).Flank, map.Bots.FlankLane(map.Bases));
    }

    [Fact]
    public void Bot_map_data_refuses_bad_data()
    {
        Vector2[] via = { new(1, 2), new(3, 4), new(5, 6) };
        Vector2[] cover = { new(10, 0), new(20, 0), new(30, 0), new(40, 0) };
        BotLane Lane(string name = "a") => new(name, via);
        BotLane[] Lanes(int n) => Enumerable.Range(0, n).Select(k => Lane("lane" + k)).ToArray();

        _ = new BotMapData(Lanes(2), cover, cover);
        _ = new BotMapData(Lanes(3), cover, cover);
        _ = new BotMapData(Lanes(0), Array.Empty<Vector2>(), Array.Empty<Vector2>());
        _ = new BotMapData(Lanes(2), Array.Empty<Vector2>(), Array.Empty<Vector2>());
        _ = new BotMapData(new[] { new BotLane("a", via[..1]), new BotLane("b", Enumerable.Repeat(via[0], 8).ToArray()) }, cover, cover[..3]);

        Assert.Throws<ArgumentException>(() => new BotMapData(Lanes(1), cover, cover));
        Assert.Throws<ArgumentException>(() => new BotMapData(Lanes(4), cover, cover));
        Assert.Throws<ArgumentException>(() => new BotMapData(new[] { Lane(""), Lane() }, cover, cover));
        Assert.Throws<ArgumentException>(() => new BotMapData(new[] { new BotLane("a", Array.Empty<Vector2>()), Lane() }, cover, cover));
        Assert.Throws<ArgumentException>(() => new BotMapData(new[] { new BotLane("a", Enumerable.Repeat(via[0], 9).ToArray()), Lane() }, cover, cover));
        Assert.Throws<ArgumentException>(() => new BotMapData(new[] { new BotLane("a", new[] { new Vector2(float.NaN, 0) }), Lane() }, cover, cover));
        Assert.Throws<ArgumentException>(() => new BotMapData(new[] { new BotLane("a", null!), Lane() }, cover, cover));
        Assert.Throws<ArgumentException>(() => new BotMapData(new[] { null!, Lane() }, cover, cover));
        Assert.Throws<ArgumentException>(() => new BotMapData(Lanes(2), cover[..2], cover));
        Assert.Throws<ArgumentException>(() => new BotMapData(Lanes(2), cover, cover[..2]));
        Assert.Throws<ArgumentException>(() => new BotMapData(Lanes(2), cover.Append(new Vector2(50, 0)).Append(new Vector2(60, 0)).ToArray(), cover));
        Assert.Throws<ArgumentException>(() => new BotMapData(Lanes(2), cover, Array.Empty<Vector2>()));
        Assert.Throws<ArgumentException>(() => new BotMapData(Lanes(2), Array.Empty<Vector2>(), cover));
        Assert.Throws<ArgumentException>(() => new BotMapData(Lanes(2), cover, cover[..3].Append(new Vector2(0, float.PositiveInfinity)).ToArray()));
        Assert.Throws<ArgumentNullException>(() => new BotMapData(null!, cover, cover));
        Assert.Throws<ArgumentNullException>(() => new BotMapData(Lanes(2), null!, cover));
        Assert.Throws<ArgumentNullException>(() => new BotMapData(Lanes(2), cover, null!));

        Assert.True(BotMapData.None.IsEmpty);
        Assert.Empty(BotMapData.None.Lanes);
        Assert.Empty(BotMapData.None.CoverOf(0));
        Assert.Empty(BotMapData.None.CoverOf(1));
        Assert.Equal(-1, BotMapData.None.FlankLane(SandboxMap.Bases));

        var data = new BotMapData(Lanes(2), cover, cover);
        Assert.False(data.IsEmpty);
        Assert.False(new BotMapData(Lanes(2), Array.Empty<Vector2>(), Array.Empty<Vector2>()).IsEmpty);
        Assert.False(new BotMapData(Lanes(0), cover, cover).IsEmpty);
        Assert.Throws<ArgumentOutOfRangeException>(() => data.CoverOf(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => data.ViaFor(0, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => data.ViaFor(2, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => data.ViaFor(-1, 0));
        Assert.Equal(via, data.ViaFor(0, 0));
        Assert.Equal(via.Reverse(), data.ViaFor(0, 1));
        Assert.Same(data.ViaFor(1, 1), data.ViaFor(1, 1));
        Assert.Throws<ArgumentException>(() => data.FlankLane(Array.Empty<TeamBase>()));
        Assert.Throws<ArgumentException>(() => data.FlankLane(SandboxMap.Bases.Take(1).ToArray()));
        Assert.Throws<ArgumentNullException>(() => data.FlankLane(null!));
    }

    [Fact]
    public void Bot_map_data_keeps_its_own_copies()
    {
        Vector2[] via = { new(1, 2), new(3, 4) };
        Vector2[] cover = { new(10, 0), new(20, 0), new(30, 0) };
        var data = new BotMapData(new[] { new BotLane("a", via), new BotLane("b", via) }, cover, cover);

        via[0] = new Vector2(99, 99);
        cover[0] = new Vector2(99, 99);

        Assert.Equal(new Vector2(1, 2), data.Lanes[0].Via[0]);
        Assert.Equal(new Vector2(1, 2), data.ViaFor(0, 0)[0]);
        Assert.Equal(new Vector2(1, 2), data.ViaFor(0, 1)[^1]);
        Assert.Equal(new Vector2(10, 0), data.CoverOf(0)[0]);
    }

    [Fact]
    public void Bot_map_data_hands_out_lists_no_cast_can_change()
    {
        var data = new BotMapData(new[] { new BotLane("a", new Vector2[] { new(1, 2), new(3, 4) }), new BotLane("b", new Vector2[] { new(5, 6) }) },
            new Vector2[] { new(10, 0), new(20, 0), new(30, 0) }, new Vector2[] { new(40, 0), new(50, 0), new(60, 0) });
        IReadOnlyList<Vector2>[] lists = { data.Lanes[0].Via, data.ViaFor(0, 0), data.ViaFor(0, 1), data.CoverOf(0), data.CoverOf(1), RiverlandMap.Bots.CoverOf(0), RiverlandMap.Bots.ViaFor(1, 1) };

        Assert.False(data.Lanes is BotLane[]);
        Assert.Throws<NotSupportedException>(() => ((IList<BotLane>)data.Lanes)[0] = new BotLane("x", new[] { Vector2.Zero }));
        foreach (IReadOnlyList<Vector2> list in lists)
        {
            Assert.False(list is Vector2[], $"{list.GetType()} is an array");
            Assert.Throws<NotSupportedException>(() => ((IList<Vector2>)list)[0] = new Vector2(99, 99));
        }

        Assert.Equal("a", data.Lanes[0].Name);
        Assert.Equal(new Vector2(1, 2), data.Lanes[0].Via[0]);
        Assert.Equal(new Vector2(1, 2), data.ViaFor(0, 0)[0]);
        Assert.Equal(new Vector2(3, 4), data.ViaFor(0, 1)[0]);
        Assert.Equal(new Vector2(10, 0), data.CoverOf(0)[0]);
        Assert.Equal(new Vector2(40, 0), data.CoverOf(1)[0]);
        Assert.Same(data.CoverOf(0), data.CoverOf(0));   // made once, not per call
    }

    [Fact]
    public void The_flank_lane_strays_farthest_from_the_line_between_the_bases()
    {
        TeamBase[] bases = { new(1, new Vector2(100, 0), 25), new(0, new Vector2(0, 0), 25) };
        Vector2[] cover = Array.Empty<Vector2>();
        static BotLane Lane(params Vector2[] via) => new("l", via);

        Assert.Equal(1, new BotMapData(new[] { Lane(new Vector2(10, 5)), Lane(new Vector2(10, 2), new Vector2(50, -30)), Lane(new Vector2(20, 20)) }, cover, cover).FlankLane(bases));
        Assert.Equal(0, new BotMapData(new[] { Lane(new Vector2(10, 30)), Lane(new Vector2(50, -30)) }, cover, cover).FlankLane(bases));   // a tie: the lower index
        Assert.Equal(2, new BotMapData(new[] { Lane(new Vector2(10, 5)), Lane(new Vector2(200, 0)), Lane(new Vector2(-50, 9)) }, cover, cover).FlankLane(bases));   // the line, not the segment
    }

    [Fact]
    public void The_navigation_grid_is_built_once_and_copied()
    {
        MapDefinition map = MapCatalog.Sandbox;
        NavGrid a = map.NewNavGrid(), b = map.NewNavGrid();
        Assert.NotSame(a, b);
        AssertSameCells(a, b);
        AssertSameCells(NavGrid.Build(map.NewBattlefield()), a);

        Battlefield field = map.NewBattlefield();
        Prop wall = field.Props.First(p => p.IsWall);
        (int hi, int hj) = NavGrid.CellOf(wall.Footprint.Center);
        ushort before = a.CostAt(hi, hj);
        field.SetDurability(wall.Id, 0);
        int changed = a.Refresh(field, new[] { new GameEvent(GameEventType.PropDestroyed, 1, 0, 0, Vector3.Zero, Vector3.Zero, PropId: wall.Id) });

        Assert.True(changed > 0);
        Assert.Equal(1, a.Version);
        Assert.Equal(0, b.Version);
        Assert.Equal(before, b.CostAt(hi, hj));
        NavGrid c = map.NewNavGrid();
        Assert.Equal(0, c.Version);
        AssertSameCells(b, c);
    }

    private static IEnumerable<(string What, Vector2 P)> Points(BotMapData bots)
    {
        foreach (BotLane lane in bots.Lanes)
        foreach (Vector2 v in lane.Via)
            yield return ($"lane {lane.Name}'s via point", v);
        for (byte team = 0; team <= 1; team++)
        foreach (Vector2 c in bots.CoverOf(team))
            yield return ($"team {team}'s cover point", c);
    }

    private static Vector2 SpawnCentre(MapDefinition map, byte team)
    {
        Vector2[] own = map.Spawns.Team.Where(s => s.Team == team).Select(s => s.Position).ToArray();
        return own.Aggregate(Vector2.Zero, (sum, p) => sum + p) / own.Length;
    }

    private static float Degrees(float radians) => radians * 180f / MathF.PI;

    private static void AssertSameCells(NavGrid expected, NavGrid actual)
    {
        Assert.Equal((expected.CellsX, expected.CellsZ), (actual.CellsX, actual.CellsZ));
        for (int j = 0; j < expected.CellsZ; j++)
        for (int i = 0; i < expected.CellsX; i++)
            if (expected.CostAt(i, j) != actual.CostAt(i, j))
                Assert.Fail($"cell ({i}, {j}): expected {expected.CostAt(i, j)}, got {actual.CostAt(i, j)}");
    }
}
