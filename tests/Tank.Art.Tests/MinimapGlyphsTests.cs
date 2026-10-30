using Tank.Client.Art;
using Tank.Client.Match;
using Tank.Client.Minimap;
using SN = System.Numerics;

namespace Tank.Art.Tests;

public class MinimapGlyphsTests
{
    private const float Tolerance = 1e-4f;
    private static readonly SN.Vector2 Centre = new(50, 80);

    private static void AssertNear(SN.Vector2 expected, SN.Vector2 actual) =>
        Assert.True(SN.Vector2.Distance(expected, actual) <= Tolerance, $"expected {expected}, got {actual}");

    private static float AngleBetween(SN.Vector2 a, SN.Vector2 b) =>
        MathF.Acos(Math.Clamp(SN.Vector2.Dot(SN.Vector2.Normalize(a), SN.Vector2.Normalize(b)), -1f, 1f));

    [Fact]
    public void Yaw_0_points_up_and_a_quarter_turn_points_left()
    {
        AssertNear(new SN.Vector2(0, -1), MinimapGlyphs.Direction(0));
        AssertNear(new SN.Vector2(-1, 0), MinimapGlyphs.Direction(MathF.PI / 2));
        AssertNear(new SN.Vector2(0, 1), MinimapGlyphs.Direction(MathF.PI));
    }

    [Fact]
    public void The_arrow_s_tip_is_half_its_length_ahead_of_the_centre()
    {
        Span<SN.Vector2> points = stackalloc SN.Vector2[MinimapGlyphs.ArrowPoints];
        const float size = 11;

        int n = MinimapGlyphs.Arrow(Centre, 0, size, points);

        Assert.Equal(MinimapGlyphs.ArrowPoints, n);
        AssertNear(Centre + new SN.Vector2(0, -size / 2), points[0]);
        for (int i = 1; i < n; i++) Assert.True(points[i].Y > points[0].Y, $"point {i} {points[i]} is above the tip");
        // The dart's tail: the notch sits between the rear corners, ahead of them.
        Assert.True(points[2].Y < points[1].Y && points[2].Y < points[3].Y);
        Assert.True(points[1].X > Centre.X && points[3].X < Centre.X);   // right rear corner, then the left one
    }

    [Fact]
    public void A_quarter_turn_points_the_arrow_left()
    {
        Span<SN.Vector2> points = stackalloc SN.Vector2[MinimapGlyphs.ArrowPoints];
        MinimapGlyphs.Arrow(Centre, MathF.PI / 2, 10, points);
        AssertNear(Centre + new SN.Vector2(-5, 0), points[0]);
        for (int i = 1; i < points.Length; i++) Assert.True(points[i].X > points[0].X);
    }

    [Fact]
    public void The_four_shapes_differ_and_lie_in_their_circle()
    {
        const float size = 7;
        var counts = new Dictionary<MinimapShape, int>();
        var sets = new List<SN.Vector2[]>();
        foreach (MinimapShape shape in Enum.GetValues<MinimapShape>())
        {
            var points = new SN.Vector2[MinimapGlyphs.ShapeMaxPoints];
            int n = MinimapGlyphs.Shape(shape, Centre, size, points);
            Assert.InRange(n, 3, MinimapGlyphs.ShapeMaxPoints);
            counts[shape] = n;
            for (int i = 0; i < n; i++)
                Assert.True(SN.Vector2.Distance(points[i], Centre) <= size / 2 + Tolerance, $"{shape} point {i} {points[i]} is outside the circle");
            sets.Add(points[..n]);
        }

        Assert.Equal(4, counts[MinimapShape.Light]);
        Assert.Equal(8, counts[MinimapShape.Medium]);
        Assert.Equal(4, counts[MinimapShape.Heavy]);
        Assert.Equal(3, counts[MinimapShape.Td]);
        for (int i = 0; i < sets.Count; i++)
            for (int j = i + 1; j < sets.Count; j++)
                Assert.False(sets[i].Length == sets[j].Length && sets[i].All(p => sets[j].Any(q => SN.Vector2.Distance(p, q) <= Tolerance)),
                    $"shapes {i} and {j} are the same");
    }

    [Fact]
    public void The_diamond_stands_on_a_point_the_square_on_a_side_and_the_triangle_points_down()
    {
        Span<SN.Vector2> points = stackalloc SN.Vector2[MinimapGlyphs.ShapeMaxPoints];
        MinimapGlyphs.Shape(MinimapShape.Light, Centre, 10, points);
        Assert.Contains(Centre + new SN.Vector2(0, -5), points[..4].ToArray());
        Assert.Contains(Centre + new SN.Vector2(0, 5), points[..4].ToArray());

        MinimapGlyphs.Shape(MinimapShape.Heavy, Centre, 10, points);
        Assert.All(points[..4].ToArray(), p => Assert.Equal(MathF.Abs(p.X - Centre.X), MathF.Abs(p.Y - Centre.Y), 4));

        MinimapGlyphs.Shape(MinimapShape.Td, Centre, 10, points);
        SN.Vector2[] tri = points[..3].ToArray();
        SN.Vector2 lowest = tri.MaxBy(p => p.Y);
        AssertNear(Centre + new SN.Vector2(0, 5), lowest);
        Assert.Equal(2, tri.Count(p => p.Y < Centre.Y));
    }

    [Fact]
    public void The_cross_is_two_diagonals_of_half_length_half_the_size()
    {
        (SN.Vector2 a, SN.Vector2 b, SN.Vector2 c, SN.Vector2 d) = MinimapGlyphs.Cross(Centre, 7);
        foreach (SN.Vector2 end in new[] { a, b, c, d }) Assert.Equal(3.5f, SN.Vector2.Distance(end, Centre), 4);
        AssertNear(Centre, (a + b) / 2);
        AssertNear(Centre, (c + d) / 2);
        Assert.Equal(MathF.PI / 2, AngleBetween(b - a, d - c), 4);
        Assert.Equal(MathF.PI / 4, AngleBetween(b - a, new SN.Vector2(1, 0)) % (MathF.PI / 2), 4);
    }

    [Fact]
    public void The_fan_opens_twice_the_half_angle_about_the_camera_s_heading()
    {
        Span<SN.Vector2> points = stackalloc SN.Vector2[MinimapGlyphs.FanPoints];
        const float halfFov = 0.7f, length = 34;

        int n = MinimapGlyphs.Fan(Centre, cameraYaw: 0.4f, halfFov, length, points);

        Assert.Equal(MinimapGlyphs.FanPoints, n);
        Assert.Equal(12, n);
        AssertNear(Centre, points[0]);
        Assert.Equal(2 * halfFov, AngleBetween(points[1] - Centre, points[n - 1] - Centre), 4);
        for (int i = 1; i < n; i++) Assert.Equal(length, SN.Vector2.Distance(points[i], Centre), 3);
        // The middle of the arc lies along the camera's heading.
        AssertNear(Centre + MinimapGlyphs.Direction(0.4f) * length, points[6]);
    }

    [Fact]
    public void The_progress_arc_runs_clockwise_from_twelve_o_clock()
    {
        var points = new SN.Vector2[MinimapGlyphs.ArcMaxPoints];
        const float radius = 20;

        Assert.Equal(0, MinimapGlyphs.Arc(Centre, radius, 0, points));

        int full = MinimapGlyphs.Arc(Centre, radius, 100, points);
        Assert.Equal(MinimapGlyphs.ArcMaxPoints, full);
        AssertNear(Centre + new SN.Vector2(0, -radius), points[0]);
        AssertNear(points[0], points[full - 1]);

        int half = MinimapGlyphs.Arc(Centre, radius, 50, points);
        Assert.Equal(1 + 16, half);
        AssertNear(Centre + new SN.Vector2(0, -radius), points[0]);
        AssertNear(Centre + new SN.Vector2(0, radius), points[half - 1]);

        int quarter = MinimapGlyphs.Arc(Centre, radius, 25, points);
        Assert.Equal(1 + 8, quarter);
        AssertNear(Centre + new SN.Vector2(radius, 0), points[quarter - 1]);   // clockwise: right of the centre, not left
        Assert.True(points[1].X > Centre.X);

        Assert.Equal(2, MinimapGlyphs.Arc(Centre, radius, 1, points));
        Assert.Equal(MinimapGlyphs.ArcMaxPoints, MinimapGlyphs.Arc(Centre, radius, 255, points));   // past 100 is a full turn
    }

    [Fact]
    public void No_glyph_writes_more_than_its_constant()
    {
        var big = new SN.Vector2[64];
        Assert.True(MinimapGlyphs.Arrow(Centre, 1.3f, 9, big) <= MinimapGlyphs.ArrowPoints);
        foreach (MinimapShape shape in Enum.GetValues<MinimapShape>())
            Assert.True(MinimapGlyphs.Shape(shape, Centre, 9, big) <= MinimapGlyphs.ShapeMaxPoints);
        Assert.True(MinimapGlyphs.Fan(Centre, 2f, 0.9f, 70, big) <= MinimapGlyphs.FanPoints);
        for (int p = 0; p <= 100; p++) Assert.True(MinimapGlyphs.Arc(Centre, 10, (byte)p, big) <= MinimapGlyphs.ArcMaxPoints);

        // And each fits a buffer of exactly that size.
        MinimapGlyphs.Arrow(Centre, 1.3f, 9, new SN.Vector2[MinimapGlyphs.ArrowPoints]);
        MinimapGlyphs.Shape(MinimapShape.Medium, Centre, 9, new SN.Vector2[MinimapGlyphs.ShapeMaxPoints]);
        MinimapGlyphs.Fan(Centre, 2f, 0.9f, 70, new SN.Vector2[MinimapGlyphs.FanPoints]);
        MinimapGlyphs.Arc(Centre, 10, 100, new SN.Vector2[MinimapGlyphs.ArcMaxPoints]);
    }

    [Fact]
    public void Icon_sizes_and_colours_follow_the_design()
    {
        Assert.Equal(new MinimapIconSizes(Tank: 10, Arrow: 15, Cross: 9, FanLength: 40, BaseMinRadius: 9,
            CrossWidth: 2.5f, RingWidth: 2.5f, ArcWidth: 3, OutlineWidth: 1.5f, EyeRingWidth: 2), MinimapStyle.Icons(full: false));
        Assert.Equal(new MinimapIconSizes(Tank: 15, Arrow: 22, Cross: 13, FanLength: 90, BaseMinRadius: 12,
            CrossWidth: 3, RingWidth: 3, ArcWidth: 4, OutlineWidth: 2, EyeRingWidth: 2.5f, ZoneWidth: 3, NextZoneWidth: 2), MinimapStyle.Icons(full: true));
        Assert.Equal((2f, 1.5f), (MinimapStyle.CornerIcons.ZoneWidth, MinimapStyle.CornerIcons.NextZoneWidth));
        Assert.Equal(Palette.ZoneWall, MinimapStyle.ZoneLine);
        Assert.Equal(Palette.UiText, MinimapStyle.NextZoneLine);
        Assert.Equal(Palette.UiBackground, MinimapStyle.ZoneShade);
        Assert.Equal(0.35f, MinimapStyle.ZoneShadeOpacity);
        Assert.Equal(0.5f, MinimapStyle.EyeRingShare);
        Assert.Equal(0.25f, MinimapStyle.BaseFillOpacity);

        Assert.Equal(new SN.Vector4(Palette.TankBody, 1), MinimapStyle.TankColor(Side.Own, false));
        Assert.Equal(new SN.Vector4(Palette.AllyBody, 1), MinimapStyle.TankColor(Side.Ally, false));
        Assert.Equal(new SN.Vector4(Palette.EnemyBody, 1), MinimapStyle.TankColor(Side.Enemy, false));
        Assert.Equal(new SN.Vector4(Palette.UiDim, 1), MinimapStyle.TankColor(Side.Ally, true));
        Assert.Equal(new SN.Vector4(Palette.EnemyBody, 0.55f), MinimapStyle.TankColor(Side.Enemy, true));
        Assert.Equal(new SN.Vector4(Palette.TankBody, 1), MinimapStyle.TankColor(Side.Own, true));

        Assert.Equal(Palette.AllyBody, MinimapStyle.BaseRing(own: true));
        Assert.Equal(Palette.EnemyBody, MinimapStyle.BaseRing(own: false));
        Assert.Equal(Palette.UiDanger, MinimapStyle.BaseArc(own: true));                     // the own base being taken: red, as its bar
        Assert.Equal(MatchColors.Ui(Side.Ally), MinimapStyle.BaseArc(own: false));           // the enemy base: blue-grey
        Assert.Equal(Palette.UiOutline, MinimapStyle.IconOutline);
        Assert.Equal(Palette.UiText, MinimapStyle.Fan);
        Assert.Equal(0.30f, MinimapStyle.FanOpacity);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_base_s_arc_runs_outside_its_ring_and_its_fill_inside(bool full)
    {
        MinimapIconSizes sizes = MinimapStyle.Icons(full);
        const float ring = 20;
        float ringOuter = ring + sizes.RingWidth / 2, ringInner = ring - sizes.RingWidth / 2;
        float arc = sizes.ArcRadius(ring);
        Assert.Equal(ring + sizes.RingWidth / 2 + sizes.ArcWidth / 2 + 0.5f, arc, 5);
        Assert.True(arc - sizes.ArcWidth / 2 > ringOuter);              // the arc's inner edge clears the ring: half a pixel of gap
        Assert.Equal(ringInner, sizes.FillRadius(ring), 5);             // the fill stops where the ring starts
        Assert.True(sizes.FillRadius(sizes.BaseMinRadius) > 0);
    }
}
