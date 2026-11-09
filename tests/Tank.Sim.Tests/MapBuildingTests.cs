using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Maps.Building;
using Tank.Sim.Terrain;

namespace Tank.Sim.Tests;

/// <summary>The generic map-building tools the riverland generator uses (riverland design §3, §3.12, §9).</summary>
public sealed class MapBuildingTests
{
    private const float Clearance = 1.6f;
    private static readonly float MaxSlope = Angles.Deg(32);

    [Fact]
    public void A_straight_polyline_measures_and_finds_points()
    {
        var line = new Polyline(new[] { new Vector2(0, 0), new Vector2(100, 0) }, step: 2f);

        Assert.Equal(100f, line.Length, 0.01f);
        Vector2 middle = line.PointAt(50);
        Assert.Equal(50f, middle.X, 0.01f);
        Assert.Equal(0f, middle.Y, 0.01f);
        Assert.Equal(10f, line.DistanceTo(new Vector2(50, 10), out float along), 0.01f);
        Assert.Equal(50f, along, 0.01f);
        Assert.Equal(51, line.Points.Count);
    }

    [Fact]
    public void A_curved_polyline_passes_through_its_control_points()
    {
        Vector2[] control = { new(0, 0), new(50, 30), new(100, 0), new(150, 40) };
        var line = new Polyline(control);

        foreach (Vector2 c in control)
            Assert.True(line.DistanceTo(c, out _) < 0.01f, $"control point {c} is off the line");
        for (float along = 0; along <= line.Length; along += 3.7f)
            Assert.Equal(1f, line.TangentAt(along).Length(), 0.0001f);
        for (int k = 1; k < line.Points.Count; k++)
            Assert.True(Vector2.Distance(line.Points[k - 1], line.Points[k]) <= 2f + 0.001f, $"points {k - 1} and {k} are more than a step apart");
        float chords = 0;
        for (int k = 1; k < control.Length; k++) chords += Vector2.Distance(control[k - 1], control[k]);
        Assert.True(line.Length > chords && line.Length < chords * 1.2f, $"chords {chords} m, curve {line.Length} m");
    }

    [Fact]
    public void Stamping_writes_the_distance_within_reach_only()
    {
        DistanceGrid grid = Grid100();
        new Polyline(new[] { new Vector2(0, 50), new Vector2(100, 50) }).Stamp(grid, reach: 10f, lineId: 1);

        int near = Index(grid, 20, 56), far = Index(grid, 20, 70);
        Assert.Equal(6f, grid.Distance[near], 0.01f);
        Assert.Equal(20f, grid.Along[near], 0.01f);
        Assert.Equal(1, grid.Line[near]);
        Assert.Equal(float.PositiveInfinity, grid.Distance[far]);
        Assert.Equal(-1, grid.Line[far]);
        Assert.Equal(6f, grid.DistanceAt(20, 56), 0.01f);
        Assert.Equal(5f, grid.DistanceAt(21, 55), 0.01f);
    }

    [Fact]
    public void Two_lines_keep_the_nearer()
    {
        DistanceGrid grid = Grid100();
        new Polyline(new[] { new Vector2(0, 50), new Vector2(100, 50) }).Stamp(grid, reach: 10f, lineId: 1);
        new Polyline(new[] { new Vector2(0, 60), new Vector2(100, 60) }).Stamp(grid, reach: 10f, lineId: 2);

        int near = Index(grid, 20, 56), below = Index(grid, 20, 52);
        Assert.Equal(4f, grid.Distance[near], 0.01f);
        Assert.Equal(2, grid.Line[near]);
        Assert.Equal(2f, grid.Distance[below], 0.01f);
        Assert.Equal(1, grid.Line[below]);
    }

    [Fact]
    public void Stamping_a_curve_matches_the_brute_force_distance()
    {
        var line = new Polyline(new[] { new Vector2(10, 10), new Vector2(60, 80), new Vector2(90, 20), new Vector2(40, 45), new Vector2(95, 95) });
        DistanceGrid grid = Grid100();
        const float reach = 14f;
        line.Stamp(grid, reach, lineId: 3);

        for (int j = 0; j < grid.SamplesZ; j++)
        for (int i = 0; i < grid.SamplesX; i++)
        {
            var p = new Vector2(i * grid.CellSize, j * grid.CellSize);
            float exact = line.DistanceTo(p, out _);
            int index = j * grid.SamplesX + i;
            if (exact <= reach - 0.001f)
            {
                Assert.True(MathF.Abs(grid.Distance[index] - exact) < 0.001f, $"sample {p}: stamped {grid.Distance[index]}, exact {exact}");
                Assert.Equal(3, grid.Line[index]);
            }
            else if (exact > reach + 0.001f)
            {
                Assert.Equal(float.PositiveInfinity, grid.Distance[index]);
            }
        }
    }

    /// <summary>
    /// The riverland's river bends so gently between its 2 m pieces that cos(turn) rounds to 1: the strips must still
    /// cover the wedge outside each bend, out to the whole reach (130 m), or the valley keeps stray spikes of high ground.
    /// </summary>
    [Fact]
    public void Stamping_a_gentle_curve_leaves_no_holes()
    {
        var grid = new DistanceGrid(RiverlandMap.Samples, RiverlandMap.Samples, RiverlandMap.CellSize);
        const float reach = Maps.Riverland.RiverlandLayout.RiverReach;
        Maps.Riverland.RiverlandLayout.River.Stamp(grid, reach, lineId: 0);

        // Distance is 1-Lipschitz: a sample 2 m from one stamped nearer than reach − 2 m lies within reach itself.
        int n = grid.SamplesX;
        for (int j = 1; j < grid.SamplesZ - 1; j++)
        for (int i = 1; i < n - 1; i++)
        {
            int at = j * n + i;
            if (!float.IsPositiveInfinity(grid.Distance[at])) continue;
            float nearest = MathF.Min(MathF.Min(grid.Distance[at - 1], grid.Distance[at + 1]), MathF.Min(grid.Distance[at - n], grid.Distance[at + n]));
            Assert.False(nearest < reach - grid.CellSize - 0.01f, $"sample ({i * grid.CellSize}, {j * grid.CellSize}) was missed; a neighbour is {nearest:F2} m from the line");
        }
    }

    [Fact]
    public void Value_noise_is_seeded_and_bounded()
    {
        Assert.Equal(ValueNoise.Value(7, 3.7f, 5.2f), ValueNoise.Value(7, 3.7f, 5.2f));
        Assert.NotEqual(ValueNoise.Value(7, 3.7f, 5.2f), ValueNoise.Value(8, 3.7f, 5.2f));
        Assert.Equal(ValueNoise.Fbm(7, 3.7f, 5.2f, 4), ValueNoise.Fbm(7, 3.7f, 5.2f, 4));

        var rng = new XorShift(99);
        float low = float.MaxValue, high = float.MinValue;
        for (int n = 0; n < 10_000; n++)
        {
            float x = (rng.NextFloat() - 0.5f) * 200f, z = (rng.NextFloat() - 0.5f) * 200f;
            float value = ValueNoise.Value(11, x, z), fbm = ValueNoise.Fbm(11, x, z, 4), signed = ValueNoise.Signed(11, x, z, 3);
            Assert.InRange(value, 0f, 1f);
            Assert.InRange(fbm, 0f, 1f);
            Assert.InRange(signed, -1f, 1f);
            low = MathF.Min(low, signed);
            high = MathF.Max(high, signed);
        }
        Assert.True(low < -0.3f && high > 0.3f, $"signed noise should swing both ways, got {low} … {high}");
    }

    [Fact]
    public void Bump_is_one_at_the_centre_and_zero_from_the_radius()
    {
        Assert.Equal(1f, HeightSculpt.Bump(0, 10));
        Assert.Equal(0.5625f, HeightSculpt.Bump(5, 10), 0.0001f);
        Assert.Equal(0f, HeightSculpt.Bump(10, 10));
        Assert.Equal(0f, HeightSculpt.Bump(12, 10));
        Assert.Equal(0f, HeightSculpt.SmoothStep(2, 4, 1));
        Assert.Equal(0.5f, HeightSculpt.SmoothStep(2, 4, 3), 0.0001f);
        Assert.Equal(1f, HeightSculpt.SmoothStep(2, 4, 5));
    }

    [Fact]
    public void Flatten_levels_the_box_and_eases_back()
    {
        // 100 × 100 m at 1 m a sample, rising 0.2 m per metre eastward; 5 m high at x = 50, the box's centre.
        const int samples = 101;
        static float Slope(float x) => 5f + (x - 50f) * 0.2f;
        var heights = new float[samples * samples];
        for (int j = 0; j < samples; j++)
        for (int i = 0; i < samples; i++)
            heights[j * samples + i] = Slope(i);
        var box = new Obb2(new Vector2(50, 50), new Vector2(5, 3), 0f);   // x 45…55, z 47…53

        HeightSculpt.Flatten(heights, samples, 1f, box, height: 5f, blend: 3f);

        for (int j = 0; j < samples; j++)
        for (int i = 0; i < samples; i++)
        {
            float h = heights[j * samples + i], distance = box.DistanceToPoint(new Vector2(i, j));
            if (distance <= 0) Assert.Equal(5f, h);
            else if (distance >= 3f) Assert.Equal(Slope(i), h);
        }
        // Along the row through the centre the ground climbs east of the box and falls west of it, without overshooting.
        float Row(int i) => heights[50 * samples + i];
        for (int i = 55; i < 60; i++) Assert.True(Row(i + 1) >= Row(i), $"east of the box, x {i + 1} is below x {i}");
        for (int i = 45; i > 40; i--) Assert.True(Row(i - 1) <= Row(i), $"west of the box, x {i - 1} is above x {i}");
        Assert.True(Row(56) > 5f && Row(56) < Slope(56));
    }

    [Fact]
    public void Placement_grid_checks_the_gap()
    {
        var grid = new PlacementGrid(100, 100, 8);
        grid.Add(new Vector2(10, 10), 2);

        Assert.True(grid.AnyWithin(new Vector2(14.5f, 10), 2, gap: 0.6f));
        Assert.False(grid.AnyWithin(new Vector2(14.5f, 10), 2, gap: 0.4f));
        Assert.False(grid.AnyWithin(new Vector2(15.9f, 10), 0.1f, gap: 0.1f));

        grid.Add(new Vector2(16.1f, 10), 0.1f);
        Assert.True(grid.AnyWithin(new Vector2(15.9f, 10), 0.1f, gap: 0.1f));
        Assert.Equal(2, grid.Count);

        grid.Add(new Vector2(50, 50), 20);   // a big circle spills over many cells
        Assert.True(grid.AnyWithin(new Vector2(75, 50), 4, gap: 1.5f));
        Assert.False(grid.AnyWithin(new Vector2(75, 50), 4, gap: 0.5f));
        Assert.False(grid.AnyWithin(new Vector2(-1, -1), 0.5f, gap: 0f));
    }

    [Fact]
    public void Poisson_samples_keep_their_distance()
    {
        List<Vector2> Sample(uint seed, Func<Vector2, bool> accept)
        {
            var rng = new XorShift(seed);
            return PoissonDisc.Sample(ref rng, Vector2.Zero, new Vector2(200, 200), _ => 6f, accept);
        }

        List<Vector2> points = Sample(5, _ => true);
        AssertSpacing(points, 6f);
        Assert.True(points.Count > 600, $"only {points.Count} points");
        Assert.All(points, p => Assert.True(p.X >= 0 && p.X < 200 && p.Y >= 0 && p.Y < 200, $"{p} is outside the area"));
        Assert.Equal(points, Sample(5, _ => true));
        Assert.NotEqual(points, Sample(6, _ => true));

        List<Vector2> east = Sample(5, p => p.X >= 50);
        Assert.DoesNotContain(east, p => p.X < 50);
        Assert.True(east.Count > 450, $"only {east.Count} points east of x 50");
        AssertSpacing(east, 6f);
    }

    [Fact]
    public void Poisson_spacing_may_vary_with_position()
    {
        var rng = new XorShift(17);
        List<Vector2> points = PoissonDisc.Sample(ref rng, Vector2.Zero, new Vector2(200, 200), p => p.X < 100 ? 6f : 12f, _ => true);

        List<Vector2> west = points.Where(p => p.X < 100).ToList(), east = points.Where(p => p.X >= 100).ToList();
        AssertSpacing(west, 6f);
        AssertSpacing(east, 12f);
        AssertSpacing(points, 6f);
        Assert.True(west.Count > 2.5f * east.Count, $"west {west.Count}, east {east.Count}");
        Assert.True(east.Count > 60, $"only {east.Count} points in the sparse half");
    }

    [Fact]
    public void Passability_marks_slopes_props_and_the_border()
    {
        var flat = new Passability(TestWorld.FlatField(), MaxSlope, Clearance);
        int cells = TestWorld.Samples - 1;
        for (int j = 0; j < cells; j++)
        for (int i = 0; i < cells; i++)
        {
            float x = (i + 0.5f) * TestWorld.Cell, z = (j + 0.5f) * TestWorld.Cell;
            bool playable = x >= TestWorld.Border && x <= TestWorld.Size - TestWorld.Border && z >= TestWorld.Border && z <= TestWorld.Size - TestWorld.Border;
            Assert.Equal(playable, flat.IsPassable(i, j));
        }
        Assert.False(flat.IsPassable(-1, 10));
        Assert.False(flat.IsPassable(cells, 10));
        Assert.False(flat.IsPassableAt(new Vector2(-5, 100)));
        Assert.True(flat.IsPassableAt(new Vector2(100, 100)));

        var steep = new Passability(TestWorld.Field(TestWorld.RampNorth(MathF.Tan(Angles.Deg(40)))), MaxSlope, Clearance);
        Assert.False(steep.IsPassableAt(new Vector2(100, 100)));
        Assert.Equal(-1, steep.ComponentAt(new Vector2(100, 100)));
        var gentle = new Passability(TestWorld.Field(TestWorld.RampNorth(MathF.Tan(Angles.Deg(25)))), MaxSlope, Clearance);
        Assert.True(gentle.IsPassableAt(new Vector2(100, 100)));

        // A 2 × 2 m rock centred on (101, 101), grown by 1.6 m: x and z 98.4 … 103.6, so cell centres 99, 101, 103.
        var rock = new Prop(1, PropKind.Rock, new Obb2(new Vector2(101, 101), new Vector2(1, 1), 0), -0.3f, 2f);
        var bush = new Prop(2, PropKind.Bush, new Obb2(new Vector2(41, 41), new Vector2(3.5f, 3.5f), 0), 0, 4.5f);
        Prop tree = TreeTests.Tree(3, new Vector2(61, 41));
        Prop[] fence = new WallLine(PropKind.Fence, new Vector2(80, 41), new Vector2(90, 41)).Segments(TestWorld.Flat(), 4).ToArray();
        var props = new[] { rock, bush, tree }.Concat(fence);
        var wet = new Passability(new Battlefield(TestWorld.Flat(), props, TestWorld.Border, waterLevel: 0.5f), MaxSlope, Clearance);
        foreach (float c in new[] { 99f, 101f, 103f })
        {
            Assert.False(wet.IsPassableAt(new Vector2(c, 101)), $"({c}, 101) is under the rock's clearance");
            Assert.False(wet.IsPassableAt(new Vector2(101, c)), $"(101, {c}) is under the rock's clearance");
        }
        Assert.True(wet.IsPassableAt(new Vector2(105, 101)));
        Assert.True(wet.IsPassableAt(new Vector2(97, 101)));
        Assert.True(wet.IsPassableAt(new Vector2(41, 41)), "a bush");
        Assert.True(wet.IsPassableAt(new Vector2(61, 41)), "a tree");
        Assert.True(wet.IsPassableAt(new Vector2(85, 41)), "a fence");
        Assert.True(wet.Connected(new Vector2(41, 41), new Vector2(150, 150)), "water everywhere, but all one piece");
    }

    [Fact]
    public void A_walled_square_is_cut_off()
    {
        HeightField ground = TestWorld.Flat();
        Vector2 inside = new(100, 100), outside = new(50, 50);
        var nw = new Vector2(90, 90);
        var ne = new Vector2(110, 90);
        var se = new Vector2(110, 110);
        var sw = new Vector2(90, 110);
        WallLine[] closed =
        {
            new(PropKind.StoneWall, nw, ne), new(PropKind.StoneWall, ne, se), new(PropKind.StoneWall, se, sw), new(PropKind.StoneWall, sw, nw),
        };
        Passability walled = Yard(ground, closed);
        Assert.True(walled.IsPassableAt(inside) && walled.IsPassableAt(outside));
        Assert.False(walled.Connected(inside, outside));
        Assert.NotEqual(walled.ComponentAt(inside), walled.ComponentAt(outside));

        // A 6 m gap in the north wall, x 96 … 102, closed only by a fence.
        WallLine[] gated =
        {
            new(PropKind.StoneWall, nw, new Vector2(96, 90)), new(PropKind.Fence, new Vector2(96, 90), new Vector2(102, 90)),
            new(PropKind.StoneWall, new Vector2(102, 90), ne), new(PropKind.StoneWall, ne, se), new(PropKind.StoneWall, se, sw),
            new(PropKind.StoneWall, sw, nw),
        };
        Passability open = Yard(ground, gated);
        Assert.True(open.Connected(inside, outside));
        Assert.Equal(open.ComponentAt(inside), open.ComponentAt(outside));
        Assert.False(open.Connected(inside, new Vector2(1, 1)), "the border is impassable");
    }

    private static Passability Yard(HeightField ground, IEnumerable<WallLine> lines)
    {
        var props = new List<Prop>();
        foreach (WallLine line in lines) props.AddRange(line.Segments(ground, props.Count + 1));
        return new Passability(new Battlefield(ground, props, TestWorld.Border), MaxSlope, Clearance);
    }

    private static DistanceGrid Grid100() => new(51, 51, 2f);

    private static int Index(DistanceGrid grid, float x, float z) =>
        (int)MathF.Round(z / grid.CellSize) * grid.SamplesX + (int)MathF.Round(x / grid.CellSize);

    private static void AssertSpacing(List<Vector2> points, float spacing)
    {
        for (int a = 0; a < points.Count; a++)
        for (int b = a + 1; b < points.Count; b++)
            Assert.True(Vector2.Distance(points[a], points[b]) >= spacing - 0.0001f, $"{points[a]} and {points[b]} are closer than {spacing}");
    }
}
