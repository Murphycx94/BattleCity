using System.Numerics;
using Tank.Client.Art.Meshes;

namespace Tank.Art.Tests;

public class VegetationTests
{
    [Fact]
    public void Clumps_have_five_to_seven_three_segment_blades()
    {
        Assert.Equal(7, GrassClump.VerticesPerBlade);    // three pairs and a single tip vertex
        Assert.Equal(5, GrassClump.TrianglesPerBlade);   // two quads and the tip triangle
        var seen = new HashSet<int>();
        for (uint seed = 1; seed <= 60; seed++)
        {
            MeshData clump = GrassClump.Build(seed);
            Assert.Equal(0, clump.VertexCount % GrassClump.VerticesPerBlade);
            int blades = clump.VertexCount / GrassClump.VerticesPerBlade;
            Assert.InRange(blades, 5, 7);
            Assert.Equal(blades * GrassClump.TrianglesPerBlade, clump.TriangleCount);
            seen.Add(blades);
        }
        Assert.Equal(new[] { 5, 6, 7 }, seen.OrderBy(b => b));
    }

    [Fact]
    public void Blades_grow_from_the_ground_with_a_root_to_tip_gradient()
    {
        MeshData clump = GrassClump.Build(9);
        for (int i = 0; i < clump.VertexCount; i++)
        {
            Vector3 p = clump.Positions[i];
            float t = clump.Colors[i].X;
            Assert.InRange(p.Y, 0f, GrassClump.MaxHeight);
            if (t == 0) Assert.Equal(0f, p.Y);
            int k = i % GrassClump.VerticesPerBlade;
            if (k == GrassClump.VerticesPerBlade - 1) Assert.Equal(1f, t);              // the tip
            else if (k >= 2) Assert.True(t > clump.Colors[i - 2].X && t < 1f);         // rows climb towards it
            else Assert.Equal(0f, t);                                                  // the root pair
        }
        Assert.True(clump.HasColors);
        MeshAssert.WindingMatchesNormals(clump);
    }

    [Fact]
    public void Blades_are_wide_short_and_blunt_tipped()
    {
        Assert.Equal(0.25f, GrassClump.MinHeight);
        Assert.Equal(0.45f, GrassClump.MaxHeight);
        MeshData clump = GrassClump.Build(5);
        int v = GrassClump.VerticesPerBlade;
        for (int blade = 0; blade < clump.VertexCount / v; blade++)
        {
            int first = blade * v;
            Vector3 rootL = clump.Positions[first], rootR = clump.Positions[first + 1];
            Vector3 lastL = clump.Positions[first + v - 3], lastR = clump.Positions[first + v - 2], tip = clump.Positions[first + v - 1];
            Assert.True(Vector3.Distance(rootL, rootR) >= 0.14f, "wide cartoon blades");
            float tipWidth = Vector3.Distance(lastL, lastR);
            float tipLength = Vector3.Distance((lastL + lastR) / 2, tip);
            Assert.True(tipLength < tipWidth, $"blade {blade}: tip {tipLength} long on a {tipWidth} base is too pointed");
        }
    }

    [Fact]
    public void Clumps_are_repeatable()
    {
        Assert.Equal(GrassClump.Build(77).Positions, GrassClump.Build(77).Positions);
        Assert.NotEqual(GrassClump.Build(77).Positions, GrassClump.Build(78).Positions);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Trees_are_closed_low_poly_and_repeatable(bool conifer)
    {
        MeshData tree = conifer ? TreeShapes.Conifer(3) : TreeShapes.Broadleaf(3);
        MeshAssert.Closed(tree);
        MeshAssert.WindingMatchesNormals(tree);
        Assert.True(MeshAssert.SignedVolume(tree) > 0);
        Assert.InRange(tree.TriangleCount, 50, conifer ? 320 : 700);
        var (min, max) = MeshAssert.Bounds(tree);
        Assert.InRange(max.Y, 6f, 10f);
        Assert.True(min.Y < 0, "the trunk is sunk into the ground");
        MeshData again = conifer ? TreeShapes.Conifer(3) : TreeShapes.Broadleaf(3);
        Assert.Equal(tree.Positions, again.Positions);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Trees_get_darker_towards_the_ground(bool conifer)
    {
        MeshData tree = conifer ? TreeShapes.Conifer(4) : TreeShapes.Broadleaf(4);
        float Brightness(Vector4 c) => c.X + c.Y + c.Z;
        var foliage = Enumerable.Range(0, tree.VertexCount).Where(i => tree.Colors[i].W > 0f).ToArray();
        float low = foliage.Where(i => tree.Positions[i].Y < 4.5f).Average(i => Brightness(tree.Colors[i]));
        float high = foliage.Where(i => tree.Positions[i].Y > 6f).Average(i => Brightness(tree.Colors[i]));
        Assert.True(low < high, $"foot {low} should be darker than crown {high}");
    }

    [Fact]
    public void Broadleaf_crowns_are_three_to_five_round_blobs()
    {
        int blobVertices = Icosphere.Build(TreeShapes.BlobSubdivisions).VertexCount;
        var seen = new HashSet<int>();
        for (uint seed = 1; seed <= 40; seed++)
        {
            MeshData tree = TreeShapes.Broadleaf(seed);
            int foliage = Enumerable.Range(0, tree.VertexCount).Count(i => tree.Colors[i].W > 0f);
            Assert.Equal(0, foliage % blobVertices);
            seen.Add(foliage / blobVertices);
        }
        Assert.Equal(new[] { 3, 4, 5 }, seen.OrderBy(n => n));
    }

    [Fact]
    public void Conifers_stack_three_or_four_tiers()
    {
        int tierVertices = TreeShapes.Tier(1f, 1f).VertexCount;
        var seen = new HashSet<int>();
        for (uint seed = 1; seed <= 40; seed++)
        {
            MeshData tree = TreeShapes.Conifer(seed);
            int foliage = Enumerable.Range(0, tree.VertexCount).Count(i => tree.Colors[i].W > 0f);
            Assert.Equal(0, foliage % tierVertices);
            seen.Add(foliage / tierVertices);
        }
        Assert.Equal(new[] { 3, 4 }, seen.OrderBy(n => n));
    }

    [Fact]
    public void Tree_tiers_have_rounded_rims()
    {
        MeshData tier = TreeShapes.Tier(2f, 3f);
        MeshAssert.Closed(tier);
        Assert.True(MeshAssert.MaxCreaseDegrees(tier) < 70f, "the rim is bevelled, not a knife edge");
    }

    [Fact]
    public void Foliage_is_a_bright_green()
    {
        foreach (MeshData tree in new[] { TreeShapes.Conifer(2), TreeShapes.Broadleaf(2) })
        {
            Vector4 brightest = Enumerable.Range(0, tree.VertexCount).Where(i => tree.Colors[i].W > 0f)
                .Select(i => tree.Colors[i]).MaxBy(c => c.Y);
            Assert.True(brightest.Y > brightest.X && brightest.Y > brightest.Z, "green dominates");
            Assert.True(brightest.Y > 0.25f, $"crown top too dark: {brightest}");   // linear; the stage-1.5 pines topped out near 0.09
        }
    }

    [Fact]
    public void Trees_differ_by_seed()
    {
        Assert.NotEqual(TreeShapes.Conifer(1).Positions, TreeShapes.Conifer(2).Positions);
        Assert.NotEqual(TreeShapes.Broadleaf(1).Positions, TreeShapes.Broadleaf(2).Positions);
    }

    [Fact]
    public void Trunks_stay_still_and_crowns_sway()
    {
        MeshData tree = TreeShapes.Conifer(5);
        for (int i = 0; i < tree.VertexCount; i++)
        {
            Vector4 c = tree.Colors[i];
            bool bark = c.X > c.Y; // bark is brown, needles are green
            if (bark) Assert.Equal(0f, c.W);
            else Assert.True(c.W > 0.1f);
        }
    }

    [Fact]
    public void Grid_counts_and_faces_up()
    {
        MeshData grid = GridMesh.Build(0, 0, 4, 3, 2f, (i, j) => 0.1f * i, (i, j) => Vector3.UnitY);
        Assert.Equal(5 * 4, grid.VertexCount);
        Assert.Equal(2 * 4 * 3, grid.TriangleCount);
        MeshAssert.WindingMatchesNormals(grid);
        for (int t = 0; t < grid.TriangleCount; t++)
        {
            Vector3 a = grid.Positions[grid.Indices[3 * t]], b = grid.Positions[grid.Indices[3 * t + 1]], c = grid.Positions[grid.Indices[3 * t + 2]];
            Assert.True(Vector3.Cross(b - a, c - a).Y > 0);
        }
    }

    [Fact]
    public void Grid_uses_global_indices_so_patches_meet()
    {
        static float Height(int i, int j) => i * 0.3f - j * 0.2f;
        MeshData left = GridMesh.Build(0, 0, 4, 4, 2f, Height, (i, j) => Vector3.UnitY);
        MeshData right = GridMesh.Build(4, 0, 4, 4, 2f, Height, (i, j) => Vector3.UnitY);
        var leftEdge = left.Positions.Where(p => MathF.Abs(p.X - 8) < 1e-5f).OrderBy(p => p.Z).ToArray();
        var rightEdge = right.Positions.Where(p => MathF.Abs(p.X - 8) < 1e-5f).OrderBy(p => p.Z).ToArray();
        Assert.Equal(5, leftEdge.Length);
        Assert.Equal(leftEdge, rightEdge);
        Assert.Equal(Height(5, 2), right.Positions.Single(p => p.X == 10 && p.Z == 4).Y);
    }

    [Fact]
    public void Clump_blade_and_segment_counts_are_tunable()
    {
        MeshData far = GrassClump.Build(3, segments: 1, blades: 3);
        Assert.Equal(3 * 3, far.VertexCount);     // root pair + tip
        Assert.Equal(3 * 1, far.TriangleCount);
    }

    [Fact]
    public void Clump_heights_stay_within_min_and_max()
    {
        float top = 0;
        for (uint seed = 1; seed <= 200; seed++)
            foreach (Vector3 p in GrassClump.Build(seed).Positions) top = MathF.Max(top, p.Y);
        Assert.InRange(top, GrassClump.MinHeight, GrassClump.MaxHeight);
        Assert.True(top > GrassClump.MaxHeight * 0.9f);
    }

    [Fact]
    public void Grid_rejects_bad_arguments_naming_the_parameter()
    {
        Func<int, int, float> h = (i, j) => 0;
        Func<int, int, Vector3> n = (i, j) => Vector3.UnitY;
        Assert.Equal("cellsZ", Assert.Throws<ArgumentOutOfRangeException>(() => GridMesh.Build(0, 0, 2, 0, 1f, h, n)).ParamName);
        Assert.Equal("cellsX", Assert.Throws<ArgumentOutOfRangeException>(() => GridMesh.Build(0, 0, 0, 2, 1f, h, n)).ParamName);
        Assert.Equal("cellSize", Assert.Throws<ArgumentOutOfRangeException>(() => GridMesh.Build(0, 0, 2, 2, 0f, h, n)).ParamName);
    }
}
