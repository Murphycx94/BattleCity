using System.Numerics;
using Tank.Client.Art.Meshes;

namespace Tank.Art.Tests;

public class RockShapeTests
{
    private static readonly Vector3 Half = new(2.0f, 1.6f, 1.2f);

    [Theory]
    [InlineData(0, 12, 20)]
    [InlineData(1, 42, 80)]
    [InlineData(2, 162, 320)]
    [InlineData(3, 642, 1280)]
    public void Icosphere_counts(int subdivisions, int vertices, int triangles)
    {
        MeshData sphere = Icosphere.Build(subdivisions);
        Assert.Equal(vertices, sphere.VertexCount);
        Assert.Equal(triangles, sphere.TriangleCount);
    }

    [Fact]
    public void Icosphere_is_a_closed_unit_sphere()
    {
        MeshData sphere = Icosphere.Build(2);
        MeshAssert.ConvexSolid(sphere, Vector3.Zero);
        Assert.All(sphere.Positions, p => Assert.Equal(1f, p.Length(), 5));
    }

    [Fact]
    public void Noise_is_repeatable_bounded_and_seeded()
    {
        var p = new Vector3(1.37f, -4.2f, 9.9f);
        Assert.Equal(SeededNoise.Value(p, 7), SeededNoise.Value(p, 7));
        Assert.NotEqual(SeededNoise.Value(p, 7), SeededNoise.Value(p, 8));
        for (int i = 0; i < 2000; i++)
        {
            float v = SeededNoise.Fbm(new Vector3(i * 0.173f, i * 0.071f, -i * 0.029f), 3, 4);
            Assert.InRange(v, -1f, 1f);
        }
    }

    [Fact]
    public void Noise_is_continuous()
    {
        var p = new Vector3(3.3f, 1.1f, -2.7f);
        Assert.True(MathF.Abs(SeededNoise.Value(p, 1) - SeededNoise.Value(p + new Vector3(1e-3f), 1)) < 0.02f);
    }

    [Fact]
    public void Rock_fills_its_box_exactly()
    {
        var (min, max) = MeshAssert.Bounds(RockShape.Build(Half, 42));
        MeshAssert.Near(-Half, min);
        MeshAssert.Near(Half, max);
    }

    [Theory]
    [InlineData(1u)]
    [InlineData(42u)]
    [InlineData(20261013u)]
    public void Rock_is_closed_and_faces_out(uint seed)
    {
        MeshData rock = RockShape.Build(Half, seed);
        MeshAssert.Closed(rock);
        MeshAssert.WindingMatchesNormals(rock);
        MeshAssert.UnitNormals(rock);
        MeshAssert.NormalsPointAwayFrom(rock, Vector3.Zero);
        float box = 8 * Half.X * Half.Y * Half.Z;
        Assert.InRange(MeshAssert.SignedVolume(rock), box * 0.45f, box);
    }

    [Fact]
    public void Same_seed_same_rock_other_seed_other_rock()
    {
        MeshData a = RockShape.Build(Half, 5), b = RockShape.Build(Half, 5), c = RockShape.Build(Half, 6);
        Assert.Equal(a.Positions, b.Positions);
        Assert.NotEqual(a.Positions, c.Positions);
    }

    [Fact]
    public void Rejects_flat_boxes()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RockShape.Build(new Vector3(1, 0, 1), 1));
    }
}
