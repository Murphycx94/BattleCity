using System.Numerics;
using Tank.Client.Art.Meshes;

namespace Tank.Art.Tests;

public class RockFullnessTests
{
    private const float BuriedHeight = 0.3f;

    /// <summary>Raw-shape half extents for the given collision-box half extents, as RockMeshes builds them.</summary>
    public static IEnumerable<object[]> Boxes()
    {
        foreach (var box in new[] { new Vector3(2.0f, 1.6f, 1.2f), new Vector3(4.0f, 1.2f, 0.8f), new Vector3(1.0f, 2.4f, 1.0f), new Vector3(1.5f, 2.2f, 2.0f) })
            yield return new object[] { new Vector3(box.X * RockShape.Fit, box.Y, box.Z * RockShape.Fit) };
    }

    /// <summary>Max |x| and |z| over the points where mesh edges cross the horizontal plane y = height.</summary>
    private static (float X, float Z) SectionExtent(MeshData mesh, float height)
    {
        float maxX = 0, maxZ = 0;
        for (int t = 0; t < mesh.Indices.Count; t += 3)
            for (int e = 0; e < 3; e++)
            {
                Vector3 a = mesh.Positions[mesh.Indices[t + e]], b = mesh.Positions[mesh.Indices[t + (e + 1) % 3]];
                if ((a.Y - height) * (b.Y - height) > 0 || a.Y == b.Y) continue;
                Vector3 p = Vector3.Lerp(a, b, (height - a.Y) / (b.Y - a.Y));
                maxX = MathF.Max(maxX, MathF.Abs(p.X));
                maxZ = MathF.Max(maxZ, MathF.Abs(p.Z));
            }
        return (maxX, maxZ);
    }

    [Theory]
    [MemberData(nameof(Boxes))]
    public void Rock_cross_sections_nearly_fill_the_collision_box(Vector3 half)
    {
        for (uint seed = 1; seed <= 50; seed++)
        {
            MeshData rock = RockShape.Build(half, seed);
            foreach (float k in new[] { -0.5f, 0f, 0.5f })
            {
                var (x, z) = SectionExtent(rock, k * half.Y);
                Assert.True(x >= 0.85f * half.X, $"seed {seed} y={k}h: x {x} < 0.85*{half.X}");
                Assert.True(z >= 0.85f * half.Z, $"seed {seed} y={k}h: z {z} < 0.85*{half.Z}");
            }
            var (gx, gz) = SectionExtent(rock, -half.Y + BuriedHeight);
            Assert.True(gx >= 0.7f * half.X, $"seed {seed} ground: x {gx} of {half.X}");
            Assert.True(gz >= 0.7f * half.Z, $"seed {seed} ground: z {gz} of {half.Z}");
        }
    }

    /// <summary>Cartoon rocks are smooth lumps: neighbouring faces never meet at a sharp crease.</summary>
    [Theory]
    [MemberData(nameof(Boxes))]
    public void Rock_surface_bends_gently(Vector3 half)
    {
        for (uint seed = 1; seed <= 20; seed++)
        {
            float sharpest = MeshAssert.MaxCreaseDegrees(RockShape.Build(half, seed), -half.Y + BuriedHeight);   // the buried part never shows
            Assert.True(sharpest < 55f, $"seed {seed}: crease of {sharpest:0.0}°");   // the stage-1.5 rocks reached 80–95°; tall lumpy rocks need a little slack
        }
    }

    [Fact]
    public void Elongated_rock_is_well_formed()
    {
        MeshData rock = RockShape.Build(new Vector3(4.0f, 1.2f, 0.8f), 11);
        MeshAssert.Closed(rock);
        MeshAssert.WindingMatchesNormals(rock);
        MeshAssert.NormalsPointAwayFrom(rock, Vector3.Zero);
    }

    [Fact]
    public void Rock_vertex_golden_value()
    {
        MeshData rock = RockShape.Build(new Vector3(2.0f, 1.6f, 1.2f), 42);
        MeshAssert.Near(new Vector3(-0.017441958f, 0.44070926f, -1.1966033f), rock.Positions[100]);
    }

    [Fact]
    public void Noise_golden_values()
    {
        Assert.Equal(2380270929u, SeededNoise.Hash(3, -5, 7, 42));
        Assert.Equal(0.000762105f, SeededNoise.Value(new Vector3(1.37f, -4.2f, 9.9f), 7), 6);
        Assert.Equal(-0.324642718f, SeededNoise.Fbm(new Vector3(0.5f, 0.25f, -0.75f), 9, 3), 6);
    }
}
