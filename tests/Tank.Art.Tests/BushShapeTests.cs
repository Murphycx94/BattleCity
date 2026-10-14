using System.Numerics;
using Tank.Client.Art.Meshes;

namespace Tank.Art.Tests;

public class BushShapeTests
{
    private static int[] LeafVertices(MeshData bush) => Enumerable.Range(0, bush.VertexCount).Where(i => bush.Colors[i].Z > 0.5f).ToArray();
    private static int[] CoreVertices(MeshData bush) => Enumerable.Range(0, bush.VertexCount).Where(i => bush.Colors[i].Z <= 0.5f).ToArray();

    [Fact]
    public void Bushes_have_thirty_to_fifty_leaves_and_one_or_two_core_balls()
    {
        int coreBall = Icosphere.Build(BushShape.CoreSubdivisions).VertexCount;
        var leaves = new HashSet<int>();
        var cores = new HashSet<int>();
        for (uint seed = 1; seed <= 40; seed++)
        {
            MeshData bush = BushShape.Build(seed);
            int leafVertices = LeafVertices(bush).Length;
            Assert.Equal(0, leafVertices % BushShape.VerticesPerLeaf);
            int count = leafVertices / BushShape.VerticesPerLeaf;
            Assert.InRange(count, 30, 50);
            leaves.Add(count);
            int core = CoreVertices(bush).Length;
            Assert.Equal(0, core % coreBall);
            cores.Add(core / coreBall);
        }
        Assert.True(leaves.Count > 5, "leaf counts vary with the seed");
        Assert.Equal(new[] { 1, 2 }, cores.OrderBy(n => n));
    }

    [Fact]
    public void A_unit_bush_fits_its_unit_cylinder_and_stands_on_the_ground()
    {
        for (uint seed = 1; seed <= 20; seed++)
        {
            MeshData bush = BushShape.Build(seed);
            var (min, max) = MeshAssert.Bounds(bush);
            Assert.True(min.Y >= -0.05f, $"seed {seed}: {min.Y} below ground");
            Assert.InRange(max.Y, 0.9f, 1.0f);
            Assert.All(bush.Positions, p => Assert.True(new Vector2(p.X, p.Z).Length() <= 1.0f, $"seed {seed}: {p} outside the radius"));
            Assert.True(bush.Positions.Max(p => new Vector2(p.X, p.Z).Length()) > 0.85f, "leaves reach the edge");
        }
    }

    [Fact]
    public void Leaves_lean_out_from_the_middle_and_carry_a_root_to_tip_gradient()
    {
        MeshData bush = BushShape.Build(3);
        int[] leaf = LeafVertices(bush);
        int v = BushShape.VerticesPerLeaf;
        for (int first = leaf[0]; first <= leaf[^1]; first += v)
        {
            Vector3 root = (bush.Positions[first] + bush.Positions[first + 1]) / 2, tip = bush.Positions[first + v - 1];
            Assert.True(new Vector2(tip.X, tip.Z).Length() > new Vector2(root.X, root.Z).Length() + 0.2f, "leaf leans outward");
            Assert.Equal(0f, bush.Colors[first].X);
            Assert.Equal(1f, bush.Colors[first + v - 1].X);
        }
    }

    [Fact]
    public void Normals_are_rounded_like_one_big_ball()
    {
        MeshData bush = BushShape.Build(5);
        for (int i = 0; i < bush.VertexCount; i++)
        {
            Vector3 p = bush.Positions[i], n = bush.Normals[i];
            Vector3 fromCentre = p - new Vector3(0, BushShape.ShadingCentreY, 0);
            Assert.True(Vector3.Dot(n, fromCentre) > 0f, $"vertex {i}: normal {n} points into the bush");
            Assert.Equal(1f, n.Length(), 3);
        }
    }

    [Fact]
    public void The_core_is_a_closed_ball_that_fills_the_lower_middle()
    {
        MeshData bush = BushShape.Build(8);
        Vector3[] core = CoreVertices(bush).Select(i => bush.Positions[i]).ToArray();
        float reach = core.Max(p => new Vector2(p.X, p.Z).Length());
        Assert.InRange(reach, 0.45f, 0.95f);
        Assert.True(core.Max(p => p.Y) <= 0.95f);
        MeshAssert.WindingMatchesNormals(BushShape.Core(0.6f, 0.5f, 1u));
        MeshAssert.Closed(BushShape.Core(0.6f, 0.5f, 1u));
    }

    [Fact]
    public void Same_seed_same_bush()
    {
        Assert.Equal(BushShape.Build(9).Positions, BushShape.Build(9).Positions);
        Assert.NotEqual(BushShape.Build(9).Positions, BushShape.Build(10).Positions);
    }

    [Fact]
    public void The_core_is_fat_and_tall_enough_to_hide_a_tank_behind()
    {
        for (uint seed = 1; seed <= 40; seed++)
        {
            Vector3[] core = CoreVertices(BushShape.Build(seed)).Select(i => BushShape.Build(seed).Positions[i]).ToArray();
            Assert.True(core.Max(p => p.Y) >= 0.8f, $"seed {seed}: core top {core.Max(p => p.Y)}");
            float halfWidth = (core.Max(p => p.X) - core.Min(p => p.X)) / 2;
            Assert.True(halfWidth >= 0.65f, $"seed {seed}: core half width {halfWidth}");
            Assert.True(core.Max(p => new Vector2(p.X, p.Z).Length()) <= 1.0f, $"seed {seed}: core pokes out of the unit cylinder");
        }
    }
}
