using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Tests;

/// <summary>The sandbox map's spawn points and bot waypoints (stage 4 design §2.3).</summary>
public sealed class SandboxSpawnTests
{
    private static readonly Battlefield Field = SandboxMap.Create();
    private static readonly Prop[] Solid = Field.Props.Where(p => p.Kind != PropKind.Bush).ToArray();

    private static void AssertOpenGround(Vector2 at, float clearance, string what)
    {
        Assert.Equal(at, Field.ClampToPlayable(at));
        float slope = MathF.Acos(Field.Terrain.NormalAt(at.X, at.Y).Y) * 180f / MathF.PI;
        Assert.True(slope <= 6f, $"{what} at {at}: {slope:F1}° slope");
        Obb2 hull = TankMotion.Footprint(TankCatalog.Heavy, at, 0f);
        Obb2 roomy = hull with { HalfExtents = hull.HalfExtents + new Vector2(clearance) };
        Assert.All(Solid, p => Assert.False(Obb2.TryGetPenetration(roomy, p.Footprint, out _), $"{what} at {at} is within {clearance} m of {p.Kind} {p.Id}"));
        Assert.All(Field.Props.Where(p => p.Kind == PropKind.Bush), b =>
            Assert.True(Vector2.Distance(b.Footprint.Center, at) >= b.Footprint.HalfExtents.X + 4f, $"{what} at {at} is in a bush"));
    }

    [Fact]
    public void Each_team_has_five_spawn_points_on_open_ground_facing_the_other_team()
    {
        IReadOnlyList<SpawnPoint> team = SandboxMap.Spawns.Team;
        Assert.Equal(5, team.Count(p => p.Team == 0));
        Assert.Equal(5, team.Count(p => p.Team == 1));
        foreach (SpawnPoint p in team) AssertOpenGround(p.Position, 6f, $"team {p.Team} spawn");
        Vector2 centre0 = Average(team.Where(p => p.Team == 0)), centre1 = Average(team.Where(p => p.Team == 1));
        Assert.InRange(Vector2.Distance(centre0, centre1), 240f, 320f);
        foreach (SpawnPoint p in team)
        {
            Vector2 enemy = p.Team == 0 ? centre1 : centre0;
            Assert.True(MathF.Abs(Angles.Wrap(p.Yaw - Angles.YawOf(enemy - p.Position))) < Angles.Deg(10), $"spawn at {p.Position} doesn't face the enemy");
            Assert.All(team.Where(q => q != p), q => Assert.True(Vector2.Distance(p.Position, q.Position) >= 8f, $"spawns {p.Position} and {q.Position} crowd each other"));
        }
    }

    [Fact]
    public void Ten_solo_spawns_lie_spread_over_the_map_well_inside_it()
    {
        IReadOnlyList<SpawnPoint> solo = SandboxMap.Spawns.Solo;
        Assert.Equal(10, solo.Count);
        foreach (SpawnPoint p in solo)
        {
            AssertOpenGround(p.Position, 6f, "solo spawn");
            Assert.InRange(p.Position.X, 40f, 360f);
            Assert.InRange(p.Position.Y, 40f, 360f);
            Assert.All(solo.Where(q => q != p), q => Assert.True(Vector2.Distance(p.Position, q.Position) >= 80f, $"solo spawns {p.Position} and {q.Position} are close"));
        }
        Assert.True(solo.Any(p => p.Position.X < 200 && p.Position.Y < 200) && solo.Any(p => p.Position.X >= 200 && p.Position.Y >= 200));
    }

    [Fact]
    public void Twenty_waypoints_cover_the_map_on_open_ground()
    {
        IReadOnlyList<Vector2> points = SandboxMap.Waypoints;
        Assert.Equal(20, points.Count);
        foreach (Vector2 p in points)
        {
            AssertOpenGround(p, 8f, "waypoint");
            Assert.All(points.Where(q => q != p), q => Assert.True(Vector2.Distance(p, q) >= 45f, $"waypoints {p} and {q} are close"));
        }
        for (int qx = 0; qx < 2; qx++)
        for (int qz = 0; qz < 2; qz++)
            Assert.True(points.Count(p => (p.X >= 200) == (qx == 1) && (p.Y >= 200) == (qz == 1)) >= 3, $"quarter {qx},{qz} has too few waypoints");
    }

    private static Vector2 Average(IEnumerable<SpawnPoint> points)
    {
        Vector2 sum = Vector2.Zero;
        int n = 0;
        foreach (SpawnPoint p in points) { sum += p.Position; n++; }
        return sum / n;
    }
}
