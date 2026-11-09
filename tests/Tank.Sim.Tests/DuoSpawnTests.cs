using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Maps.Building;
using Tank.Sim.Modes;
using Tank.Sim.Terrain;
using Tank.Sim.Vehicles;
using Tank.Testing;
using Xunit.Abstractions;

namespace Tank.Sim.Tests;

/// <summary>Where the two of a duos team appear (stage 6 design §2.2; stage 6C plan, deviation 2).</summary>
public sealed class DuoSpawnTests
{
    /// <summary>
    /// The solo points have ground under 8° within 10 m (<c>RiverlandSpawnTests</c>); a member 5 m off its point is held to that
    /// within <see cref="LevelRadius"/>, its hull's reach (4.2 m) and a metre more; the hull itself lies inside the point's 10 m. Not within a
    /// full 10 m of the member: three points have a bank 5–10 m off one side spot (22.7°, 8.4°, 10.2°), well clear of the hull.
    /// </summary>
    private const float PointClearance = 10f, LevelRadius = 6f, MaxSlopeDegrees = 8f, Half = DuoSpawns.Spacing / 2;

    private readonly ITestOutputHelper _output;

    public DuoSpawnTests(ITestOutputHelper output) => _output = output;

    private static Battlefield Field => RiverlandFixture.Field;

    private static readonly Lazy<Passability> LazyPassable = new(() => new Passability(Field, Angles.Deg(32), 1.6f));

    private static Vector2 Side(float yaw) => new(MathF.Cos(yaw), -MathF.Sin(yaw));

    [Fact]
    public void The_spacing_and_the_clearance_are_the_plans()
    {
        Assert.Equal(10f, DuoSpawns.Spacing);
        Assert.Equal(1f, DuoSpawns.PropClearance);
    }

    [Fact]
    public void Both_of_every_riverland_team_land_ten_metres_apart_on_clear_dry_level_reachable_ground()
    {
        Vector2 first = RiverlandMap.Spawns.Solo[0].Position;
        int fellBack = 0;
        foreach (SpawnPoint point in RiverlandMap.Spawns.Solo)
        {
            SpawnPoint a = DuoSpawns.Member(point, 0, Field), b = DuoSpawns.Member(point, 1, Field);
            Assert.InRange(Vector2.Distance(a.Position, b.Position), DuoSpawns.Spacing - 0.01f, DuoSpawns.Spacing + 0.01f);
            Assert.Equal(point.Yaw, a.Yaw);
            Assert.Equal(point.Yaw, b.Yaw);
            if (a.Position != point.Position - Side(point.Yaw) * Half) fellBack++;
            foreach (SpawnPoint member in new[] { a, b })
            {
                string what = $"the team at {point.Position}: member at {member.Position}";
                AssertClearOfProps(member, what);
                AssertDryLevelAndInside(point, member, what);
                Assert.True(LazyPassable.Value.Connected(first, member.Position), $"{what} can't be reached from {first}");
            }
        }
        _output.WriteLine($"{fellBack} of {RiverlandMap.Spawns.Solo.Count} riverland points put the two ahead and behind");
        Assert.Equal(0, fellBack);
    }

    /// <summary>The heavy's hull keeps <see cref="DuoSpawns.PropClearance"/> from every solid prop's footprint, trees included.</summary>
    private static void AssertClearOfProps(SpawnPoint member, string what)
    {
        Obb2 hull = TankMotion.Footprint(TankCatalog.Heavy, member.Position, member.Yaw);
        foreach (Prop prop in Field.Props)
        {
            if (!prop.IsSolid || Vector2.Distance(prop.Footprint.Center, member.Position) > 30f) continue;
            float d = Gap(hull, prop);
            Assert.True(d >= DuoSpawns.PropClearance, $"{what} is {d:F2} m from {prop.Kind} {prop.Id}");
        }
    }

    /// <summary>
    /// How far apart the hull and the prop are, found the slow way: points every 5 cm round the hull's edge against the prop,
    /// and the prop's corners (a round one's centre, less its radius) against the hull, which also catches a prop inside it.
    /// </summary>
    private static float Gap(in Obb2 hull, in Prop prop)
    {
        float gap = float.MaxValue;
        Vector2 right = hull.AxisRight * hull.HalfExtents.X, forward = hull.AxisForward * hull.HalfExtents.Y;
        Vector2[] corners = { hull.Center - right - forward, hull.Center + right - forward, hull.Center + right + forward, hull.Center - right + forward };
        for (int k = 0; k < 4; k++)
        {
            Vector2 from = corners[k], to = corners[(k + 1) % 4];
            int steps = (int)MathF.Ceiling(Vector2.Distance(from, to) / 0.05f);
            for (int s = 0; s <= steps; s++) gap = MathF.Min(gap, PropDistance(prop, Vector2.Lerp(from, to, s / (float)steps)));
        }
        Obb2 f = prop.Footprint;
        if (prop.IsRound) return MathF.Min(gap, hull.DistanceToPoint(f.Center) - f.HalfExtents.X);
        Vector2 r = f.AxisRight * f.HalfExtents.X, u = f.AxisForward * f.HalfExtents.Y;
        foreach (Vector2 c in new[] { f.Center - r - u, f.Center + r - u, f.Center + r + u, f.Center - r + u })
            gap = MathF.Min(gap, hull.DistanceToPoint(c));
        return gap;
    }

    private static float PropDistance(in Prop prop, Vector2 point) => prop.IsRound
        ? MathF.Max(0f, Vector2.Distance(prop.Footprint.Center, point) - prop.Footprint.HalfExtents.X)
        : prop.Footprint.DistanceToPoint(point);

    /// <summary>No water under the hull's corners or centre, the hull inside the playable area and inside its point's level 10 m, ground under 8° within <see cref="LevelRadius"/>.</summary>
    private static void AssertDryLevelAndInside(SpawnPoint point, SpawnPoint member, string what)
    {
        Obb2 hull = TankMotion.Footprint(TankCatalog.Heavy, member.Position, member.Yaw);
        Vector2 right = hull.AxisRight * hull.HalfExtents.X, forward = hull.AxisForward * hull.HalfExtents.Y;
        foreach (Vector2 p in new[] { hull.Center, hull.Center - right - forward, hull.Center + right - forward, hull.Center + right + forward, hull.Center - right + forward })
        {
            Assert.True(Field.WaterDepthAt(p) == 0, $"{what}: water at {p}");
            Assert.Equal(p, Field.ClampToPlayable(p));
        }
        Assert.True(Vector2.Distance(point.Position, member.Position) + hull.HalfExtents.Length() <= PointClearance, $"{what}: the hull reaches out of its point's {PointClearance} m");
        float steepest = 0f;
        for (float dz = -LevelRadius; dz <= LevelRadius; dz += 1f)
        for (float dx = -LevelRadius; dx <= LevelRadius; dx += 1f)
        {
            if (dx * dx + dz * dz > LevelRadius * LevelRadius) continue;
            Vector2 p = member.Position + new Vector2(dx, dz);
            steepest = MathF.Max(steepest, MathF.Acos(Field.Terrain.NormalAt(p.X, p.Y).Y) * 180f / MathF.PI);
        }
        Assert.True(steepest < MaxSlopeDegrees, $"{what}: {steepest:F1}° slope within {LevelRadius} m");
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(1.1f)]
    [InlineData(-2.5f)]
    public void On_open_ground_the_two_stand_left_and_right_facing_as_the_point_does(float yaw)
    {
        Battlefield field = TestWorld.FlatField();
        var point = new SpawnPoint(new Vector2(100, 100), yaw, 3);

        SpawnPoint a = DuoSpawns.Member(point, 0, field), b = DuoSpawns.Member(point, 1, field);

        AssertNear(point.Position - Side(yaw) * Half, a.Position);
        AssertNear(point.Position + Side(yaw) * Half, b.Position);
        Assert.Equal(point with { Position = a.Position }, a);
        Assert.Equal(point with { Position = b.Position }, b);
    }

    [Fact]
    public void A_rock_three_metres_to_the_left_puts_the_two_behind_and_ahead()
    {
        var point = new SpawnPoint(new Vector2(100, 100), 0f, 0);
        var rock = new Prop(1, PropKind.Rock, new Obb2(point.Position - Side(0f) * 3f, new Vector2(0.5f, 0.5f), 0f), -1f, 2f);
        Battlefield field = TestWorld.FlatField(rock);

        SpawnPoint a = DuoSpawns.Member(point, 0, field), b = DuoSpawns.Member(point, 1, field);

        Vector2 forward = Angles.Forward(0f);
        AssertNear(point.Position - forward * Half, a.Position);
        AssertNear(point.Position + forward * Half, b.Position);
        Assert.Equal(0f, a.Yaw);
    }

    [Fact]
    public void A_tree_beside_the_right_spot_moves_both_of_the_team_not_just_the_one_it_blocks()
    {
        var point = new SpawnPoint(new Vector2(100, 100), 0f, 0);
        // The right hull reaches x = 106.8; the trunk's edge 0.6 m beyond it is within the clearance.
        var tree = new Prop(1, PropKind.Tree, new Obb2(new Vector2(107.7f, 100), new Vector2(0.3f, 0.3f), 0f), 0f, 8f);
        Battlefield field = TestWorld.FlatField(tree);

        Vector2 forward = Angles.Forward(0f);
        AssertNear(point.Position - forward * Half, DuoSpawns.Member(point, 0, field).Position);
        AssertNear(point.Position + forward * Half, DuoSpawns.Member(point, 1, field).Position);
    }

    [Fact]
    public void A_prop_just_beyond_the_clearance_leaves_them_side_by_side()
    {
        var point = new SpawnPoint(new Vector2(100, 100), 0f, 0);
        var tree = new Prop(1, PropKind.Tree, new Obb2(new Vector2(108.2f, 100), new Vector2(0.3f, 0.3f), 0f), 0f, 8f);   // 1.1 m off the hull
        Battlefield field = TestWorld.FlatField(tree);

        AssertNear(point.Position + Side(0f) * Half, DuoSpawns.Member(point, 1, field).Position);
    }

    /// <summary>
    /// A heavy's hull is <see cref="TankCatalog.Heavy"/>'s HullWidth 3.6 m by HullLength 7.5 m: at yaw 0 the right spot's hull, centred
    /// at x = 105, has its right side at 105 + 3.6 / 2 = 106.8. A 1 m rock (half-extents 0.5) whose near edge lies <paramref name="gap"/>
    /// beyond that side overlaps nothing, so only the distance between the two boxes' corners and sides decides.
    /// </summary>
    [Theory]
    [InlineData(0.5f, false)]
    [InlineData(1.2f, true)]
    public void A_rock_off_the_right_hulls_side_moves_the_two_only_when_nearer_than_the_clearance(float gap, bool sideBySide)
    {
        var point = new SpawnPoint(new Vector2(100, 100), 0f, 0);
        float hullSide = point.Position.X + Half + TankCatalog.Heavy.HullWidth / 2;   // 106.8
        Assert.Equal(3.6f, TankCatalog.Heavy.HullWidth);
        var rock = new Prop(1, PropKind.Rock, new Obb2(new Vector2(hullSide + gap + 0.5f, 100), new Vector2(0.5f, 0.5f), 0f), -1f, 2f);
        Battlefield field = TestWorld.FlatField(rock);

        SpawnPoint a = DuoSpawns.Member(point, 0, field), b = DuoSpawns.Member(point, 1, field);

        Vector2 across = Side(0f) * Half, along = Angles.Forward(0f) * Half;
        AssertNear(point.Position - (sideBySide ? across : along), a.Position);
        AssertNear(point.Position + (sideBySide ? across : along), b.Position);
    }

    /// <summary>
    /// Water under the left spot's hull: a pit in flat ground (the 2 m height samples at <paramref name="pitXs"/> × <paramref name="pitZs"/>
    /// sunk to <paramref name="pitDepth"/>) holding water at <paramref name="waterLevel"/>, dry everywhere else. The left hull at yaw 0 is
    /// centred at (95, 100) with its corners at x 93.2 / 96.8, z 96.25 / 103.75.
    /// </summary>
    [Theory]
    [InlineData(new[] { 92, 94, 96 }, new[] { 98, 100, 102 }, -1f, -0.5f, true)]   // under the centre (0.5 m deep), not the corners
    [InlineData(new[] { 92 }, new[] { 96 }, -2f, -0.3f, false)]                     // under one corner (0.4 m deep at (93.2, 96.25)), not the centre
    public void Water_under_the_left_spot_puts_the_two_behind_and_ahead(int[] pitXs, int[] pitZs, float pitDepth, float waterLevel, bool underCentre)
    {
        var heights = new float[TestWorld.Samples * TestWorld.Samples];
        foreach (int x in pitXs)
        foreach (int z in pitZs)
            heights[(int)(z / TestWorld.Cell) * TestWorld.Samples + (int)(x / TestWorld.Cell)] = pitDepth;
        var terrain = new HeightField(TestWorld.Samples, TestWorld.Samples, TestWorld.Cell, heights);
        Battlefield field = new(terrain, Array.Empty<Prop>(), TestWorld.Border, waterLevel: waterLevel);
        var point = new SpawnPoint(new Vector2(100, 100), 0f, 0);
        Assert.Equal(underCentre, field.WaterDepthAt(new Vector2(95, 100)) > 0);
        Assert.Equal(!underCentre, field.WaterDepthAt(new Vector2(93.2f, 96.25f)) > 0);
        Assert.Equal(0f, field.WaterDepthAt(point.Position + Side(0f) * Half));   // the right spot is dry

        Vector2 forward = Angles.Forward(0f);
        AssertNear(point.Position - forward * Half, DuoSpawns.Member(point, 0, field).Position);
        AssertNear(point.Position + forward * Half, DuoSpawns.Member(point, 1, field).Position);
    }

    [Fact]
    public void A_spot_off_the_playable_area_puts_the_two_behind_and_ahead()
    {
        var point = new SpawnPoint(new Vector2(TestWorld.Border + 2f, 100), 0f, 0);   // the left spot is 3 m off the field
        Battlefield field = TestWorld.FlatField();

        AssertNear(point.Position - Angles.Forward(0f) * Half, DuoSpawns.Member(point, 0, field).Position);
    }

    [Fact]
    public void Only_members_zero_and_one_are_placed()
    {
        var point = new SpawnPoint(new Vector2(100, 100), 0f, 0);
        Battlefield field = TestWorld.FlatField();

        Assert.Throws<ArgumentOutOfRangeException>("member", () => DuoSpawns.Member(point, 2, field));
        Assert.Throws<ArgumentOutOfRangeException>("member", () => DuoSpawns.Member(point, -1, field));
    }

    [Fact]
    public void Placing_a_member_allocates_nothing()
    {
        SpawnPoint point = RiverlandMap.Spawns.Solo[3];
        Allocation.AssertNone(() =>
        {
            DuoSpawns.Member(point, 0, Field);   // warm up
            Allocation.Window window = Allocation.Start();
            for (int i = 0; i < 100; i++) DuoSpawns.Member(point, i % 2, Field);
            return window.Stop();
        });
    }

    private static void AssertNear(Vector2 expected, Vector2 actual) =>
        Assert.True(Vector2.Distance(expected, actual) < 1e-4f, $"expected {expected}, got {actual}");
}
