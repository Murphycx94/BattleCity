using System.Numerics;
using Tank.Client.Art.Meshes;

namespace Tank.Art.Tests;

public class ExtrudeTests
{
    /// <summary>Hull side profile (z, y) with an upper and a lower glacis, as in the medium tank's look.</summary>
    private static readonly Vector2[] Hull =
    {
        new(-2.75f, 0.45f), new(2.9f, 0.45f), new(3.2f, 0.75f), new(3.2f, 1.45f), new(-1.95f, 1.55f), new(-3.25f, 1.05f),
    };

    [Fact]
    public void Sharp_box_has_flat_faces_with_own_vertices()
    {
        MeshData box = Box.Build(new Vector3(2, 1, 3));
        Assert.Equal(24, box.VertexCount);
        Assert.Equal(12, box.TriangleCount);
    }

    [Fact]
    public void Beveled_box_is_four_rings_of_the_cut_rectangle()
    {
        MeshData box = Box.Build(new Vector3(2, 1, 3), 0.1f);
        Assert.Equal(32, box.VertexCount);   // 4 rings × 8 points
        Assert.Equal(60, box.TriangleCount); // walls 16 + chamfers 32 + caps 12
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(0.05f)]
    [InlineData(0.2f)]
    public void Box_is_a_closed_outward_solid_of_the_right_size(float bevel)
    {
        var size = new Vector3(2, 1, 3);
        MeshData box = Box.Build(size, bevel);
        MeshAssert.ConvexSolid(box, Vector3.Zero);
        var (min, max) = MeshAssert.Bounds(box);
        MeshAssert.Near(-size / 2, min);
        MeshAssert.Near(size / 2, max);
        float full = size.X * size.Y * size.Z, volume = MeshAssert.SignedVolume(box);
        if (bevel == 0) Assert.Equal(full, volume, 3);
        else Assert.InRange(volume, full * 0.85f, full);
    }

    [Theory]
    [InlineData(0f, 36, 20)]
    [InlineData(0.04f, 48, 92)]
    public void Hull_profile_counts_follow_the_formula(float bevel, int vertices, int triangles)
    {
        MeshData hull = Extrude.Build(Hull, 2.1f, bevel);
        Assert.Equal(vertices, hull.VertexCount);   // sharp: 6n, beveled: 8n
        Assert.Equal(triangles, hull.TriangleCount); // sharp: 4n − 4, beveled: 16n − 4
        MeshAssert.ConvexSolid(hull, new Vector3(0, 1.0f, 0));
    }

    [Fact]
    public void Profile_winding_does_not_matter()
    {
        MeshData forward = Extrude.Build(Hull, 2.1f, 0.04f);
        MeshData backward = Extrude.Build(Hull.Reverse().ToArray(), 2.1f, 0.04f);
        Assert.Equal(forward.TriangleCount, backward.TriangleCount);
        MeshAssert.ConvexSolid(backward, new Vector3(0, 1.0f, 0));
    }

    [Fact]
    public void Bevel_moves_walls_in_from_the_ends_and_caps_in_from_the_walls()
    {
        const float width = 2f, bevel = 0.1f;
        MeshData box = Box.Build(new Vector3(width, 1, 3), bevel);
        for (int i = 0; i < box.VertexCount; i++)
        {
            Vector3 p = box.Positions[i], n = box.Normals[i];
            bool cap = MathF.Abs(n.X) > 0.999f;
            if (cap)
            {
                Assert.Equal(width / 2, MathF.Abs(p.X), 4);
                Assert.True(MathF.Abs(p.Y) <= 0.5f - bevel + 1e-4f && MathF.Abs(p.Z) <= 1.5f - bevel + 1e-4f, $"cap vertex {p} not inset");
            }
            else
            {
                Assert.Equal(width / 2 - bevel, MathF.Abs(p.X), 4);
                Assert.True(MathF.Abs(n.X) < 1e-4f, "wall normals lie across the sweep axis");
            }
        }
    }

    [Fact]
    public void Chamfers_shade_round_while_flat_faces_stay_flat()
    {
        MeshData box = Box.Build(new Vector3(2, 1, 3), 0.1f);
        float worst = 0;
        int flat = 0;
        for (int t = 0; t < box.TriangleCount; t++)
        {
            int a = box.Indices[3 * t], b = box.Indices[3 * t + 1], c = box.Indices[3 * t + 2];
            Vector3 face = Vector3.Normalize(Vector3.Cross(box.Positions[b] - box.Positions[a], box.Positions[c] - box.Positions[a]));
            float deviation = new[] { a, b, c }.Max(i => 1 - Vector3.Dot(face, box.Normals[i]));
            worst = MathF.Max(worst, deviation);
            if (deviation < 1e-4f) flat++;
        }
        Assert.True(worst > 0.25f, "chamfer normals should differ from the chamfer face");
        Assert.True(flat >= 12 + 8, "caps (2 × 6) and straight walls (4 × 2) are flat");
    }

    [Fact]
    public void Upward_stands_on_the_ground_and_tapers()
    {
        var outline = new[] { new Vector2(-1, -1.2f), new Vector2(1, -1.2f), new Vector2(1.2f, 0.8f), new Vector2(-1.2f, 0.8f) };
        MeshData turret = Extrude.Upward(outline, 0.8f, 0.05f, 0.8f);
        var (min, max) = MeshAssert.Bounds(turret);
        Assert.Equal(0f, min.Y, 4);
        Assert.Equal(0.8f, max.Y, 4);
        float bottomWidth = 0, topWidth = 0;
        foreach (Vector3 p in turret.Positions)
        {
            if (p.Y < 0.06f) bottomWidth = MathF.Max(bottomWidth, MathF.Abs(p.X));
            if (p.Y > 0.79f) topWidth = MathF.Max(topWidth, MathF.Abs(p.X));
        }
        Assert.True(topWidth < bottomWidth * 0.85f, $"top {topWidth} should be narrower than bottom {bottomWidth}");
        MeshAssert.ConvexSolid(turret, new Vector3(0, 0.4f, -0.2f));
    }

    [Fact]
    public void Concave_profile_is_rejected()
    {
        var notch = new[] { new Vector2(0, 0), new Vector2(2, 0), new Vector2(1, 0.5f), new Vector2(2, 1), new Vector2(0, 1) };
        Assert.Throws<ArgumentException>(() => Extrude.Build(notch, 1f));
    }

    [Fact]
    public void Bevel_larger_than_half_an_edge_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => Box.Build(new Vector3(2, 0.3f, 3), 0.2f));
        Assert.Throws<ArgumentOutOfRangeException>(() => Box.Build(new Vector3(0.3f, 1, 3), 0.2f));
    }

    [Fact]
    public void Tapered_prism_normals_stay_perpendicular_to_faces()
    {
        var square = new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) };
        MeshData mesh = Prism.Build(square, 3f, 0f, 0.5f);
        for (int t = 0; t < mesh.TriangleCount; t++)
        {
            int a = mesh.Indices[3 * t], b = mesh.Indices[3 * t + 1], c = mesh.Indices[3 * t + 2];
            foreach (int i in new[] { a, b, c })
            {
                Vector3 n = mesh.Normals[i];
                Assert.True(MathF.Abs(Vector3.Dot(n, mesh.Positions[b] - mesh.Positions[a])) < 1e-5f, $"triangle {t}");
                Assert.True(MathF.Abs(Vector3.Dot(n, mesh.Positions[c] - mesh.Positions[a])) < 1e-5f, $"triangle {t}");
            }
        }
        MeshAssert.ConvexSolid(mesh, Vector3.Zero);
    }

    [Fact]
    public void Inset_rejects_parallel_consecutive_edges()
    {
        var withMidpoint = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(2, 0), new Vector2(2, 2), new Vector2(0, 2) };
        Assert.Throws<ArgumentException>(() => Polygon2.Inset(withMidpoint, 0.1f));
    }
}
