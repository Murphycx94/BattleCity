using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;

namespace Tank.Sim.Tests;

public sealed class WallLineTests
{
    [Fact]
    public void Lays_two_metre_segments_end_to_end_along_the_line()
    {
        Prop[] segments = new WallLine(PropKind.BrickWall, new Vector2(100, 100), new Vector2(106, 100)).Segments(TestWorld.Flat(), 7).ToArray();

        Assert.Equal(new[] { 7, 8, 9 }, segments.Select(s => s.Id));
        Assert.Equal(new[] { 101f, 103f, 105f }, segments.Select(s => s.Footprint.Center.X));
        Assert.All(segments, s =>
        {
            Assert.Equal(PropKind.BrickWall, s.Kind);
            Assert.Equal(100f, s.Footprint.Center.Y, 1e-4f);
            Assert.Equal(new Vector2(1f, 0.3f), s.Footprint.HalfExtents);
            Assert.Equal(1f, s.Footprint.AxisRight.X, 1e-5f);   // long along the line, thin across it
        });
    }

    [Fact]
    public void A_slanted_line_turns_its_segments_to_follow_it()
    {
        var from = new Vector2(50, 50);
        var to = from + Vector2.Normalize(new Vector2(3, -4)) * 4f;
        Prop[] segments = new WallLine(PropKind.StoneWall, from, to).Segments(TestWorld.Flat(), 1).ToArray();

        Assert.Equal(2, segments.Length);
        Assert.All(segments, s => Assert.Equal(1f, Vector2.Dot(s.Footprint.AxisRight, Vector2.Normalize(to - from)), 1e-5f));
        Assert.Equal(from + (to - from) * 0.25f, segments[0].Footprint.Center);
        Assert.Equal(new Vector2(1f, 0.5f), segments[0].Footprint.HalfExtents);
    }

    [Theory]
    [InlineData(PropKind.BrickWall, 0.6f, 3f)]
    [InlineData(PropKind.StoneWall, 1f, 3f)]
    [InlineData(PropKind.Fence, 0.2f, 1.5f)]
    public void Each_kind_has_its_size_and_stands_on_the_ground(PropKind kind, float thickness, float height)
    {
        var line = new WallLine(kind, new Vector2(100, 60), new Vector2(100, 64));   // north–south on a slope rising north
        foreach (Prop s in line.Segments(TestWorld.RampNorth(0.1f), 1))
        {
            float ground = TestWorld.RampNorth(0.1f).HeightAt(s.Footprint.Center.X, s.Footprint.Center.Y);
            Assert.Equal(thickness / 2, s.Footprint.HalfExtents.Y, 1e-5f);
            Assert.Equal(ground - WallLine.Sink, s.Bottom, 1e-4f);
            Assert.Equal(ground + height, s.Top, 1e-4f);
        }
    }

    // The bad lines below are rejected as soon as Segments is called, without enumerating what it returns.

    [Theory]
    [InlineData(5f)]
    [InlineData(0f)]
    public void Rejects_a_line_that_is_not_a_whole_number_of_segments(float length) =>
        Assert.Throws<InvalidOperationException>(() =>
            new WallLine(PropKind.BrickWall, new Vector2(100, 100), new Vector2(100 + length, 100)).Segments(TestWorld.Flat(), 1));

    [Theory]
    [InlineData(PropKind.Rock)]
    [InlineData(PropKind.Bush)]
    public void Only_walls_and_fences_come_in_lines(PropKind kind) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new WallLine(kind, new Vector2(100, 100), new Vector2(102, 100)).Segments(TestWorld.Flat(), 1));

    [Theory]
    [InlineData(PropKind.Rock, 0, true, false, true, false)]
    [InlineData(PropKind.Bush, 1, false, false, false, true)]
    [InlineData(PropKind.BrickWall, 300, true, true, true, false)]
    [InlineData(PropKind.StoneWall, 900, true, true, true, false)]
    [InlineData(PropKind.Fence, 1, true, false, false, true)]
    public void Kinds_have_their_rules(PropKind kind, int durability, bool solid, bool wall, bool stopsShells, bool flimsy)
    {
        var prop = new Prop(1, kind, new Obb2(Vector2.Zero, Vector2.One, 0), 0, 1);
        Assert.Equal(durability, prop.MaxDurability);
        Assert.Equal(durability > 0, prop.IsDestructible);
        Assert.Equal(solid, prop.IsSolid);
        Assert.Equal(wall, prop.IsWall);
        Assert.Equal(stopsShells, prop.StopsShells);
        Assert.Equal(flimsy, prop.IsFlimsy);
    }

    [Fact]
    public void Rubble_spreads_half_a_metre_past_both_faces_of_its_wall()
    {
        Prop wall = new WallLine(PropKind.BrickWall, new Vector2(100, 100), new Vector2(102, 100)).Segments(TestWorld.Flat(), 1).Single();
        Obb2 rubble = wall.RubbleFootprint;
        Assert.Equal(wall.Footprint.Center, rubble.Center);
        Assert.Equal(new Vector2(1f, 0.8f), rubble.HalfExtents);
        Assert.True(rubble.Contains(new Vector2(101, 100.79f)) && !rubble.Contains(new Vector2(101, 100.81f)));
    }
}
