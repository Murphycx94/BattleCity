using System.Numerics;
using Tank.Client.Art.Meshes;

namespace Tank.Art.Tests;

public class RoundedSlabTests
{
    private static readonly Vector2[] Square = { new(-1, -1), new(1, -1), new(1, 1), new(-1, 1) };

    [Fact]
    public void Round_corners_replaces_every_corner_with_an_arc_tangent_to_both_edges()
    {
        Vector2[] round = Polygon2.RoundCorners(Square, new[] { 0.5f, 0.5f, 0.5f, 0.5f }, 4);
        Assert.Equal(4 * 5, round.Length);
        Assert.True(Polygon2.IsStrictlyConvex(round));
        Assert.True(Polygon2.SignedArea(round) > 0, "counter-clockwise");
        foreach (Vector2 p in round)
        {
            Assert.True(MathF.Abs(p.X) <= 1 + 1e-5f && MathF.Abs(p.Y) <= 1 + 1e-5f, $"{p} outside the square");
            // Every point near a corner lies on the circle of radius 0.5 around (±0.5, ±0.5).
            var centre = new Vector2(MathF.Sign(p.X) * 0.5f, MathF.Sign(p.Y) * 0.5f);
            if (MathF.Abs(p.X) >= 0.5f - 1e-5f && MathF.Abs(p.Y) >= 0.5f - 1e-5f)
                Assert.Equal(0.5f, Vector2.Distance(p, centre), 4);
        }
        Assert.Contains(round, p => Vector2.Distance(p, new Vector2(0.5f, -1)) < 1e-5f);   // tangent point on the bottom edge
    }

    [Fact]
    public void Round_corners_keeps_a_corner_with_zero_radius_and_accepts_either_winding()
    {
        Vector2[] round = Polygon2.RoundCorners(Square.Reverse().ToArray(), new[] { 0f, 0.3f, 0.3f, 0.3f }, 3);
        Assert.Equal(1 + 3 * 4, round.Length);
        Assert.Contains(round, p => p == new Vector2(-1, 1));   // the reversed list starts at (−1, 1)
        Assert.True(Polygon2.IsStrictlyConvex(round));
    }

    [Fact]
    public void Round_corners_rejects_radii_that_do_not_fit_on_an_edge()
    {
        Assert.Throws<ArgumentException>(() => Polygon2.RoundCorners(Square, new[] { 1f, 1f, 0.5f, 0.5f }, 4));
        Assert.Throws<ArgumentException>(() => Polygon2.RoundCorners(Square, new[] { 0.5f, 0.5f }, 4));
    }

    [Fact]
    public void Round_corners_rejects_negative_radii_and_zero_length_edges()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Polygon2.RoundCorners(Square, new[] { 0.2f, -0.1f, 0.2f, 0.2f }, 4));
        var doubled = new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) };
        Assert.Throws<ArgumentException>(() => Polygon2.RoundCorners(doubled, new[] { 0.2f, 0.2f, 0.2f, 0.2f, 0.2f }, 4));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Slab_is_a_closed_outward_solid_with_the_profile_bounds(bool reversed)
    {
        Vector2[] profile = Polygon2.RoundCorners(Square, new[] { 0.4f, 0.4f, 0.4f, 0.4f }, 4);
        if (reversed) profile = profile.Reverse().ToArray();
        MeshData slab = RoundedSlab.Build(profile, 3f, 0.3f, 4);
        MeshAssert.ConvexSolid(slab, Vector3.Zero);
        var (min, max) = MeshAssert.Bounds(slab);
        MeshAssert.Near(new Vector3(-1.5f, -1, -1), min);
        MeshAssert.Near(new Vector3(1.5f, 1, 1), max);
    }

    [Fact]
    public void Slab_edges_are_round_not_creased()
    {
        Vector2[] profile = Polygon2.RoundCorners(Square, new[] { 0.4f, 0.4f, 0.4f, 0.4f }, 4);
        MeshData slab = RoundedSlab.Build(profile, 3f, 0.3f, 4);
        Assert.True(MeshAssert.MaxCreaseDegrees(slab) <= 90f / 4 + 0.5f, $"sharpest crease {MeshAssert.MaxCreaseDegrees(slab):F1}°");
        // Vertex normals are shared across the gentle turns, so the shading is smooth too, but a long straight wall
        // keeps (nearly) its own normal up to where the arc starts.
        int flat = 0;
        for (int i = 0; i < slab.VertexCount; i++)
        {
            Vector3 p = slab.Positions[i];
            if (MathF.Abs(p.X) > 1.2f + 1e-4f || MathF.Abs(p.Y) > 0.6f + 1e-4f) continue;   // straight wall of the z = ±1 faces
            Assert.True(MathF.Abs(slab.Normals[i].Z) > 0.99f, $"flat face normal {slab.Normals[i]} at {p}");
            flat++;
        }
        Assert.True(flat > 0);
    }

    [Fact]
    public void Slab_rejects_a_round_as_wide_as_half_the_slab()
    {
        Vector2[] profile = Polygon2.RoundCorners(Square, new[] { 0.4f, 0.4f, 0.4f, 0.4f }, 4);
        Assert.Throws<ArgumentOutOfRangeException>(() => RoundedSlab.Build(profile, 0.6f, 0.3f, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => RoundedSlab.Build(profile, 3f, 0f, 4));
    }

    [Fact]
    public void Slab_rejects_a_round_bigger_than_the_profiles_smallest_corner_radius()
    {
        Vector2[] profile = Polygon2.RoundCorners(Square, new[] { 0.4f, 0.4f, 0.1f, 0.4f }, 4);
        Assert.Throws<ArgumentException>(() => RoundedSlab.Build(profile, 3f, 0.3f, 4));
    }
}
