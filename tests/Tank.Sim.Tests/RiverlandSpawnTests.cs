using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Maps.Building;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;
using L = Tank.Sim.Maps.Riverland.RiverlandLayout;

namespace Tank.Sim.Tests;

/// <summary>The riverland's spawn points and bot waypoints (riverland design §3.10, §9).</summary>
public sealed class RiverlandSpawnTests
{
    private const float Clearance = 10f, RockClearance = 15f, MaxSlopeDegrees = 8f, PlotMargin = 3f;
    private static readonly Vector2 Middle = new(600, 600);

    private static Battlefield Field => RiverlandFixture.Field;

    private static readonly Lazy<Prop[]> LazyBlocking = new(() => Field.Props.Where(IsBlocking).ToArray());
    private static readonly Lazy<Prop[]> LazyTrees = new(() => Field.Props.Where(p => p.Kind == PropKind.Tree).ToArray());

    private static bool IsBlocking(Prop p) => p.Kind is PropKind.Rock or PropKind.House or PropKind.BrickWall or PropKind.StoneWall or PropKind.Fence;

    private static IEnumerable<SpawnPoint> AllSpawns => RiverlandMap.Spawns.Team.Concat(RiverlandMap.Spawns.Solo);

    /// <summary>
    /// The rules every spawn and waypoint keeps: the hull inside the playable area; ground under 8° everywhere within 10 m;
    /// at least 10 m from every rock, house, wall and fence (a spawn at least 15 m from every rock).
    /// </summary>
    private static void AssertOpenGround(Vector2 at, string what, bool spawn)
    {
        Obb2 hull = TankMotion.Footprint(TankCatalog.Heavy, at, 0f);
        float reach = hull.HalfExtents.Length();
        Assert.Equal(at, Field.ClampToPlayable(at));
        Assert.True(at.X - reach >= Field.Border && at.Y - reach >= Field.Border
            && at.X + reach <= Field.Terrain.SizeX - Field.Border && at.Y + reach <= Field.Terrain.SizeZ - Field.Border, $"{what} at {at} is too near the edge");
        float steepest = Around(at, Clearance).Max(p => MathF.Acos(Field.Terrain.NormalAt(p.X, p.Y).Y) * 180f / MathF.PI);
        Assert.True(steepest < MaxSlopeDegrees, $"{what} at {at}: {steepest:F1}° slope within {Clearance} m");
        foreach (Prop p in LazyBlocking.Value)
        {
            float d = p.Footprint.DistanceToPoint(at);
            Assert.True(d >= Clearance, $"{what} at {at} is {d:F1} m from {p.Kind} {p.Id}");
            if (spawn && p.Kind == PropKind.Rock) Assert.True(d >= RockClearance, $"{what} at {at} is {d:F1} m from rock {p.Id}");
        }
    }

    /// <summary>No water, house plot (its footprint plus 3 m) or tree trunk within 10 m, and outside every forest's outline.</summary>
    private static void AssertOffWaterPlotsAndForests(Vector2 at, string what)
    {
        Assert.All(Around(at, Clearance), p => Assert.True(Field.WaterDepthAt(p) == 0, $"{what} at {at}: water at {p}"));
        foreach (Prop house in Field.Props.Where(p => p.Kind == PropKind.House))
        {
            Obb2 plot = house.Footprint with { HalfExtents = house.Footprint.HalfExtents + new Vector2(PlotMargin) };
            Assert.True(plot.DistanceToPoint(at) >= Clearance, $"{what} at {at} is near house {house.Id}'s plot");
        }
        foreach (Prop tree in LazyTrees.Value)
        {
            float d = Vector2.Distance(tree.Footprint.Center, at) - tree.Footprint.HalfExtents.X;
            Assert.True(d >= Clearance, $"{what} at {at} is {d:F1} m from tree {tree.Id}");
        }
        Assert.True(L.ForestDepth(at) < 0, $"{what} at {at} is in a forest");
    }

    [Fact]
    public void Each_team_has_five_spawns_west_and_east_facing_each_other()
    {
        IReadOnlyList<SpawnPoint> team = RiverlandMap.Spawns.Team;
        Assert.Equal(10, team.Count);
        Assert.Equal(5, team.Count(p => p.Team == 0));
        Assert.Equal(5, team.Count(p => p.Team == 1));
        Vector2 centre0 = Average(team.Where(p => p.Team == 0)), centre1 = Average(team.Where(p => p.Team == 1));
        Assert.True(Vector2.Distance(centre0, centre1) >= 700f, $"the teams start {Vector2.Distance(centre0, centre1):F0} m apart");
        Assert.True(centre0.X < 400f, $"team 0 starts at {centre0}");
        Assert.True(centre1.X > 800f, $"team 1 starts at {centre1}");
        foreach (SpawnPoint p in team)
        {
            AssertOpenGround(p.Position, $"team {p.Team} spawn", spawn: true);
            Vector2 enemy = p.Team == 0 ? centre1 : centre0;
            Assert.True(MathF.Abs(Angles.Wrap(p.Yaw - Angles.YawOf(enemy - p.Position))) < Angles.Deg(10), $"spawn at {p.Position} doesn't face the enemy");
            foreach (SpawnPoint q in team.Where(q => q.Team == p.Team && q != p))
                Assert.InRange(Vector2.Distance(p.Position, q.Position), 8f, 15f);
        }
    }

    [Fact]
    public void Sixteen_solo_spawns_spread_over_the_map()
    {
        IReadOnlyList<SpawnPoint> solo = RiverlandMap.Spawns.Solo;
        Assert.Equal(16, solo.Count);
        foreach (SpawnPoint p in solo)
        {
            AssertOpenGround(p.Position, "solo spawn", spawn: true);
            Assert.True(MathF.Abs(Angles.Wrap(p.Yaw - Angles.YawOf(Middle - p.Position))) < Angles.Deg(10), $"solo spawn at {p.Position} doesn't face the middle");
            Assert.All(solo.Where(q => q != p), q => Assert.True(Vector2.Distance(p.Position, q.Position) >= 150f, $"solo spawns {p.Position} and {q.Position} are close"));
        }
    }

    [Fact]
    public void Waypoints_cover_the_map_on_open_ground()
    {
        IReadOnlyList<Vector2> points = RiverlandMap.Waypoints;
        Assert.InRange(points.Count, 35, 45);
        foreach (Vector2 p in points)
        {
            AssertOpenGround(p, "waypoint", spawn: false);
            AssertOffWaterPlotsAndForests(p, "waypoint");
            Assert.All(points.Where(q => q != p), q => Assert.True(Vector2.Distance(p, q) >= 60f, $"waypoints {p} and {q} are close"));
        }
        for (int qx = 0; qx < 2; qx++)
        for (int qz = 0; qz < 2; qz++)
            Assert.True(points.Count(p => (p.X >= Middle.X) == (qx == 1) && (p.Y >= Middle.Y) == (qz == 1)) >= 6, $"quarter {qx},{qz} has too few waypoints");
    }

    [Fact]
    public void Every_spawn_and_waypoint_is_reachable_from_every_other()
    {
        var passable = new Passability(Field, Angles.Deg(32), 1.6f);
        Vector2 first = RiverlandMap.Spawns.Team[0].Position;
        Assert.True(passable.IsPassableAt(first));
        foreach (Vector2 p in AllSpawns.Select(s => s.Position).Concat(RiverlandMap.Waypoints))
            Assert.True(passable.Connected(first, p), $"{p} can't be reached from {first}");
    }

    [Fact]
    public void Spawns_keep_off_water_plots_and_forests()
    {
        foreach (SpawnPoint p in AllSpawns) AssertOffWaterPlotsAndForests(p.Position, "spawn");
    }

    /// <summary>Points 1 m apart within <paramref name="radius"/> of <paramref name="at"/>.</summary>
    private static IEnumerable<Vector2> Around(Vector2 at, float radius)
    {
        for (float dz = -radius; dz <= radius; dz += 1f)
        for (float dx = -radius; dx <= radius; dx += 1f)
            if (dx * dx + dz * dz <= radius * radius) yield return at + new Vector2(dx, dz);
    }

    private static Vector2 Average(IEnumerable<SpawnPoint> points)
    {
        Vector2 sum = Vector2.Zero;
        int n = 0;
        foreach (SpawnPoint p in points) { sum += p.Position; n++; }
        return sum / n;
    }
}
