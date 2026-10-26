using System.Numerics;
using Tank.Client.Art.Meshes;
using Tank.Sim.Geometry;

namespace Tank.Art.Tests;

/// <summary>The segment clip the houses use for the line to the tank, and the sectors the trees and houses look in.</summary>
public class SectorGridTests
{
    private static readonly Vector2 Min = new(-1.5f), Max = new(1.5f);   // a 2 × 2 box grown by a 0.5 m margin

    [Fact]
    public void A_segment_through_a_grown_corner_crosses_and_one_past_it_misses()
    {
        Assert.True(SectorGrid.Crosses(new Vector2(0f, 2.5f), new Vector2(2.5f, 0f), Min, Max), "cuts the corner at (1.25, 1.25)");
        Assert.False(SectorGrid.Crosses(new Vector2(1f, 3f), new Vector2(3f, 1f), Min, Max), "a diagonal past the corner, its bounds overlapping the box");
        Assert.True(SectorGrid.Crosses(new Vector2(-3f, 1.4f), new Vector2(3f, 1.4f), Min, Max), "through the margin only");
    }

    [Fact]
    public void A_segment_along_a_slab_crosses_only_within_it()
    {
        Assert.False(SectorGrid.Crosses(new Vector2(2f, -5f), new Vector2(2f, 5f), Min, Max), "upright, beside the box");
        Assert.True(SectorGrid.Crosses(new Vector2(1.4f, -5f), new Vector2(1.4f, 5f), Min, Max), "upright, through its edge");
        Assert.False(SectorGrid.Crosses(new Vector2(-5f, -1.6f), new Vector2(5f, -1.6f), Min, Max), "level, under the box");
        Assert.True(SectorGrid.Crosses(new Vector2(0.2f, 0.3f), new Vector2(0.2f, 0.3f), Min, Max), "a point inside");
        Assert.False(SectorGrid.Crosses(new Vector2(0.2f, 1.6f), new Vector2(0.2f, 1.6f), Min, Max), "a point outside");
        Assert.False(SectorGrid.Crosses(new Vector2(0f, 2f), new Vector2(0f, 9f), Min, Max), "pointing away from it");
    }

    /// <summary>Against the box's own distance over many points along each segment, in a turned house's frame.</summary>
    [Fact]
    public void A_turned_box_is_crossed_where_its_points_say()
    {
        var box = new Obb2(new Vector2(50f, 40f), new Vector2(4.5f, 3.5f), 0.7f);
        var rng = new XorShift(11);
        int crossing = 0, missing = 0;
        for (int i = 0; i < 400; i++)
        {
            var a = new Vector2(30f + 40f * rng.NextFloat(), 20f + 40f * rng.NextFloat());
            var b = new Vector2(30f + 40f * rng.NextFloat(), 20f + 40f * rng.NextFloat());
            float nearest = float.MaxValue;
            for (int k = 0; k <= 2000; k++) nearest = MathF.Min(nearest, box.DistanceToPoint(Vector2.Lerp(a, b, k / 2000f)));
            if (nearest is > 0f and < 0.05f) continue;   // grazing it: too close to call by sampling
            bool crosses = SectorGrid.Crosses(box, a, b);
            Assert.True(crosses == (nearest == 0f), $"{a} → {b}: nearest {nearest}, crosses {crosses}");
            if (crosses) crossing++;
            else missing++;
        }
        Assert.True(crossing > 50 && missing > 50, $"{crossing} crossing, {missing} missing");
    }

    [Fact]
    public void Touching_writes_no_more_than_it_has_room_for()
    {
        var grid = new SectorGrid(80f);
        Span<(int X, int Z)> keys = stackalloc (int X, int Z)[2];
        int count = grid.Touching(new Vector2(10f, 10f), new Vector2(250f, 10f), 1f, keys);
        Assert.Equal(2, count);
        Assert.Equal((0, 0), keys[0]);
        Assert.Equal((1, 0), keys[1]);
    }

    [Fact]
    public void The_view_looks_in_each_sector_once()
    {
        var grid = new SectorGrid(80f);
        Span<(int X, int Z)> keys = stackalloc (int X, int Z)[64];
        // Around the eye near a corner of four sectors, and along a line from it to a tank in the next sector east.
        int count = grid.InView(new Vector2(78f, 78f), new Vector2(120f, 70f), near: 6f, reach: 4f, keys);
        (int X, int Z)[] seen = keys[..count].ToArray();
        Assert.Equal(seen.Distinct().Count(), seen.Length);
        Assert.Equal(new[] { (0, 0), (0, 1), (1, 0), (1, 1) }, seen.Order());
        count = grid.InView(new Vector2(78f, 78f), new Vector2(200f, 40f), near: 6f, reach: 4f, keys);
        seen = keys[..count].ToArray();
        Assert.Equal(seen.Distinct().Count(), seen.Length);
        Assert.Equal(new[] { (0, 0), (0, 1), (1, 0), (1, 1), (2, 0) }, seen.Order());
        count = grid.InView(new Vector2(40f, 40f), null, near: 6f, reach: 4f, keys);   // no tank: the eye only
        Assert.Equal(new[] { (0, 0) }, keys[..count].ToArray());
    }

    /// <summary>
    /// A view whose sectors might not all fit says so (−1) rather than leave some out, so the caller looks at everything
    /// (a tree or house in a left-out sector would never fade); one that fits writes them all.
    /// </summary>
    [Fact]
    public void A_view_too_wide_for_its_keys_says_so_instead_of_leaving_sectors_out()
    {
        var grid = new SectorGrid(80f);
        Span<(int X, int Z)> keys = stackalloc (int X, int Z)[8];
        Assert.Equal(-1, grid.InView(new Vector2(40f, 40f), new Vector2(1000f, 1000f), near: 6f, reach: 4f, keys));   // a long diagonal
        Assert.Equal(-1, grid.InView(new Vector2(40f, 40f), null, near: 6f, reach: 400f, keys));                       // a wide eye
        Assert.Equal(-1, grid.InView(new Vector2(78f, 78f), new Vector2(120f, 70f), near: 6f, reach: 4f, keys[..3]));    // four sectors, room for three
        Assert.Equal(4, grid.InView(new Vector2(78f, 78f), new Vector2(120f, 70f), near: 6f, reach: 4f, keys[..4]));
        Assert.Equal(4, grid.InView(new Vector2(78f, 78f), new Vector2(120f, 70f), near: 6f, reach: 4f, keys));
    }
}
