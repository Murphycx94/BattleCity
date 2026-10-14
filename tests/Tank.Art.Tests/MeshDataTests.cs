using System.Numerics;
using Tank.Client.Art.Meshes;
using Xunit.Sdk;

namespace Tank.Art.Tests;

public class MeshDataTests
{
    /// <summary>Closed tetrahedron with flat, outward faces, built by hand.</summary>
    internal static MeshData Tetrahedron()
    {
        Vector3[] p = { new(0, 0, 0), new(1, 0, 0), new(0, 0, 1), new(0, 1, 0) };
        (int, int, int)[] faces = { (0, 1, 2), (0, 3, 1), (0, 2, 3), (1, 3, 2) };
        var b = new MeshBuilder();
        foreach (var (i, j, k) in faces)
        {
            Vector3 n = Vector3.Normalize(Vector3.Cross(p[j] - p[i], p[k] - p[i]));
            b.AddTriangle(b.AddVertex(p[i], n, Vector2.Zero), b.AddVertex(p[j], n, Vector2.Zero), b.AddVertex(p[k], n, Vector2.Zero));
        }
        return b.Build();
    }

    [Fact]
    public void Constructor_rejects_index_out_of_range()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MeshData(
            new[] { Vector3.Zero }, new[] { Vector3.UnitY }, new[] { Vector2.Zero }, new[] { Vector4.One }, new[] { 0, 0, 1 }, false));
    }

    [Fact]
    public void Constructor_rejects_mismatched_attribute_lengths()
    {
        Assert.Throws<ArgumentException>(() => new MeshData(
            new[] { Vector3.Zero, Vector3.One }, new[] { Vector3.UnitY }, new[] { Vector2.Zero }, new[] { Vector4.One }, Array.Empty<int>(), false));
    }

    [Fact]
    public void Builder_quad_makes_two_triangles()
    {
        var b = new MeshBuilder();
        int a = b.AddVertex(new Vector3(0, 0, 0), Vector3.UnitY, Vector2.Zero);
        int c = b.AddVertex(new Vector3(0, 0, -1), Vector3.UnitY, Vector2.Zero);
        int d = b.AddVertex(new Vector3(1, 0, -1), Vector3.UnitY, Vector2.Zero);
        int e = b.AddVertex(new Vector3(1, 0, 0), Vector3.UnitY, Vector2.Zero);
        b.AddQuad(a, e, d, c);
        MeshData mesh = b.Build();
        Assert.Equal(4, mesh.VertexCount);
        Assert.Equal(2, mesh.TriangleCount);
        Assert.False(mesh.HasColors);
        MeshAssert.WindingMatchesNormals(mesh);
    }

    [Fact]
    public void Tetrahedron_passes_the_solid_checks()
    {
        MeshAssert.ConvexSolid(Tetrahedron(), new Vector3(0.25f, 0.25f, 0.25f));
    }

    [Fact]
    public void Closed_check_catches_a_hole()
    {
        MeshData tetra = Tetrahedron();
        var open = new MeshData(tetra.Positions.ToArray(), tetra.Normals.ToArray(), tetra.Uvs.ToArray(), tetra.Colors.ToArray(),
            tetra.Indices.Take(9).ToArray(), false);
        Assert.Throws<XunitException>(() => MeshAssert.Closed(open));
    }

    [Fact]
    public void Mirror_transform_flips_winding_so_faces_stay_outward()
    {
        MeshData mirrored = Tetrahedron().Transformed(Matrix4x4.CreateScale(-1, 1, 1));
        MeshAssert.ConvexSolid(mirrored, new Vector3(-0.25f, 0.25f, 0.25f));
    }

    [Fact]
    public void Non_uniform_scale_keeps_normals_perpendicular_to_faces()
    {
        MeshData scaled = Tetrahedron().Transformed(Matrix4x4.CreateScale(3, 0.5f, 1) * Matrix4x4.CreateTranslation(5, 1, 0));
        for (int t = 0; t < scaled.TriangleCount; t++)
        {
            Vector3 a = scaled.Positions[3 * t], b = scaled.Positions[3 * t + 1], c = scaled.Positions[3 * t + 2];
            Vector3 n = scaled.Normals[3 * t];
            Assert.True(MathF.Abs(Vector3.Dot(n, b - a)) < 1e-5f);
            Assert.True(MathF.Abs(Vector3.Dot(n, c - a)) < 1e-5f);
        }
        MeshAssert.ConvexSolid(scaled, new Vector3(5.75f, 1.125f, 0.25f));
    }

    [Fact]
    public void Merge_concatenates_parts_and_offsets_indices()
    {
        MeshData tetra = Tetrahedron();
        MeshData merged = Merge.Build((tetra, Matrix4x4.Identity), (tetra, Matrix4x4.CreateTranslation(3, 0, 0)));
        Assert.Equal(2 * tetra.VertexCount, merged.VertexCount);
        Assert.Equal(2 * tetra.TriangleCount, merged.TriangleCount);
        MeshAssert.Closed(merged);
        MeshAssert.WindingMatchesNormals(merged);
        Assert.Equal(2 * MeshAssert.SignedVolume(tetra), MeshAssert.SignedVolume(merged), 4);
    }

    [Fact]
    public void WithColor_paints_every_vertex()
    {
        var green = new Vector4(0, 1, 0, 0.5f);
        MeshData painted = Tetrahedron().WithColor(green);
        Assert.True(painted.HasColors);
        Assert.All(painted.Colors, c => Assert.Equal(green, c));
    }
}
