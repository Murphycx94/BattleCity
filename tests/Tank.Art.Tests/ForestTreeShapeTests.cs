using System.Numerics;
using Tank.Client.Art.Meshes;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;

namespace Tank.Art.Tests;

public class ForestTreeShapeTests
{
    [Fact]
    public void The_tree_line_keeps_its_trees()
    {
        Assert.Equal(TreeShapes.Conifer(7).Positions, TreeShapes.Conifer(7, 0.24f).Positions);
        Assert.Equal(TreeShapes.Broadleaf(11).Positions, TreeShapes.Broadleaf(11, 0.28f).Positions);
        Assert.Equal(TreeShapes.Conifer(7).Colors, TreeShapes.Conifer(7, 0.24f).Colors);
        Assert.Equal(TreeShapes.Broadleaf(11).Indices, TreeShapes.Broadleaf(11, 0.28f).Indices);
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    public void Forest_trees_have_a_trunk_a_seventh_of_the_crown(bool conifer, int variant)
    {
        var (mesh, shape) = TreeFit.Variant(conifer, variant);
        TreeShapes.Shape measured = TreeShapes.Measure(mesh, shape.TrunkRadius);
        Assert.Equal(shape, measured);
        Assert.Equal(1f / Prop.CrownPerTrunk, measured.TrunkRadius / measured.CrownRadius, 0.05f / Prop.CrownPerTrunk);
        Assert.Equal(TrunkRadiusAt(mesh, -0.29f), shape.TrunkRadius, 0.02f);   // the mesh's trunk is that thick at its foot
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_tree_is_drawn_the_size_of_its_prop(bool conifer)
    {
        var tree = new Prop(1, PropKind.Tree, new Obb2(new Vector2(100, 100), new Vector2(0.5f), 0f), 3.7f, 12.3f);
        var (mesh, shape) = TreeFit.Variant(conifer, 0);
        Vector3 scale = TreeFit.Scale(tree, shape);
        Assert.Equal(scale.X, scale.Z);
        MeshData drawn = mesh.Transformed(Matrix4x4.CreateScale(scale));
        TreeShapes.Shape seen = TreeShapes.Measure(drawn, shape.TrunkRadius * scale.X);
        Assert.Equal(3.5f, seen.CrownRadius, 3.5f * 0.05f);
        Assert.Equal(12f, seen.Height, 12f * 0.02f);
        Assert.InRange(TrunkRadiusAt(drawn, 1f), 0.4f, 0.6f);
    }

    [Fact]
    public void Conifers_and_broadleaves_mix_by_region()
    {
        IReadOnlyList<TreeFit.ConiferRegion> forests = TreeFit.ForestsOf(RiverlandMap.Id);
        Assert.True(TreeFit.ConiferShare(forests, 760, 120) >= 0.7f, "the north forest is mostly conifers");
        Assert.True(TreeFit.ConiferShare(forests, 1100, 190) >= 0.7f, "so is the north-east one");
        Assert.Equal(0.5f, TreeFit.ConiferShare(forests, 1000, 600));
        Assert.True(TreeFit.ConiferShare(forests, 145, 750) <= 0.4f, "the west forest is mostly broadleaves");
        Assert.True(TreeFit.ConiferShare(forests, 360, 360) <= 0.4f, "so is the middle one");
        Assert.Equal(TreeFit.ElsewhereShare, TreeFit.ConiferShare(forests, 600, 1000));
        int conifers = 0;
        for (int id = 1; id <= 400; id++)
        {
            var tree = new Prop(id, PropKind.Tree, new Obb2(new Vector2(760, 120), new Vector2(0.4f), 0f), 0f, 10f);
            bool isConifer = TreeFit.IsConifer(tree, forests);
            Assert.Equal(isConifer, TreeFit.IsConifer(tree, forests));
            if (isConifer) conifers++;
        }
        Assert.InRange(conifers, 0.7f * 400, 0.9f * 400);
    }

    [Fact]
    public void The_riverland_regions_are_its_forests_grown_by_their_ragged_edge()
    {
        IReadOnlyList<TreeFit.ConiferRegion> regions = TreeFit.ForestsOf("RIVERLAND");
        Assert.Equal(RiverlandMap.Forests.Count, regions.Count);
        for (int i = 0; i < regions.Count; i++)
        {
            Assert.Equal(RiverlandMap.Forests[i].Min - new Vector2(RiverlandMap.ForestJitter), regions[i].Min);
            Assert.Equal(RiverlandMap.Forests[i].Max + new Vector2(RiverlandMap.ForestJitter), regions[i].Max);
        }
        Assert.Equal(new[] { 0.5f, 0.3f, 0.8f, 0.8f, 0.3f }, regions.Select(r => r.Share));   // east, west, north, north-east, centre
    }

    [Theory]
    [InlineData("sandbox")]
    [InlineData(null)]
    [InlineData("moon")]
    public void Other_maps_mix_their_trees_evenly(string? mapId)
    {
        IReadOnlyList<TreeFit.ConiferRegion> forests = TreeFit.ForestsOf(mapId);
        Assert.Empty(forests);
        Assert.Equal(TreeFit.ElsewhereShare, TreeFit.ConiferShare(forests, 760, 120));   // where the riverland's north forest is
    }

    [Fact]
    public void Sector_keys_floor_and_cover()
    {
        var grid = new SectorGrid(80f);
        Assert.Equal((0, 0), grid.KeyOf(79.9f, 0));
        Assert.Equal((1, 0), grid.KeyOf(80f, 0));
        Assert.Equal((-1, 0), grid.KeyOf(-0.1f, 0));
        Assert.Equal((-1, -2), grid.KeyOf(-80f, -80.5f));
        Span<(int X, int Z)> keys = stackalloc (int X, int Z)[16];
        int count = grid.Touching(new Vector2(10, 10), new Vector2(170, 10), 5f, keys);
        Assert.Equal(new[] { (0, 0), (1, 0), (2, 0) }, keys[..count].ToArray().Order());
        count = grid.Touching(new Vector2(78, 78), new Vector2(78, 78), 5f, keys);
        Assert.Equal(new[] { (0, 0), (0, 1), (1, 0), (1, 1) }, keys[..count].ToArray().Order());
    }

    [Fact]
    public void Grouping_by_centre_puts_each_prop_in_one_sector_in_order()
    {
        var rng = new XorShift(5);
        var props = new List<Prop>();
        for (int i = 0; i < 500; i++)
        {
            var centre = new Vector2(rng.NextFloat() * 500f - 100f, rng.NextFloat() * 500f - 100f);
            PropKind kind = i % 5 == 0 ? PropKind.Bush : PropKind.Tree;
            props.Add(new Prop(i + 1, kind, new Obb2(centre, new Vector2(0.4f), 0f), 0f, 10f));
        }
        Dictionary<(int X, int Z), List<int>> sectors = SectorGrid.GroupByCentre(props, p => p.Kind == PropKind.Tree, 80f);
        var grid = new SectorGrid(80f);
        var seen = new HashSet<int>();
        foreach (var (key, indices) in sectors)
        {
            Assert.NotEmpty(indices);
            for (int k = 0; k < indices.Count; k++)
            {
                if (k > 0) Assert.True(indices[k] > indices[k - 1]);
                Prop prop = props[indices[k]];
                Assert.Equal(PropKind.Tree, prop.Kind);
                Assert.Equal(key, grid.KeyOf(prop.Footprint.Center.X, prop.Footprint.Center.Y));
                Assert.True(seen.Add(indices[k]));
            }
        }
        Assert.Equal(props.Count(p => p.Kind == PropKind.Tree), seen.Count);
    }

    /// <summary>
    /// How thick the trunk of <paramref name="tree"/> is at height <paramref name="y"/>: its bark vertices (colour A 0) off
    /// the axis, split into the bottom and top rings, each ring's mean distance from its own centre, interpolated.
    /// </summary>
    private static float TrunkRadiusAt(MeshData tree, float y)
    {
        Vector3[] bark = Enumerable.Range(0, tree.VertexCount).Where(i => tree.Colors[i].W == 0f).Select(i => tree.Positions[i]).ToArray();
        float middle = (bark.Min(p => p.Y) + bark.Max(p => p.Y)) / 2;
        (float Y, float Radius) Ring(IEnumerable<Vector3> points)
        {
            Vector3[] ring = points.ToArray();
            Vector3 centre = ring.Aggregate(Vector3.Zero, (sum, p) => sum + p) / ring.Length;
            float[] radii = ring.Select(p => new Vector2(p.X - centre.X, p.Z - centre.Z).Length()).ToArray();
            float outer = radii.Max();
            return (centre.Y, radii.Where(r => r > 0.5f * outer).Average());   // leave out the caps' centres
        }
        var (lowY, lowRadius) = Ring(bark.Where(p => p.Y < middle));
        var (highY, highRadius) = Ring(bark.Where(p => p.Y >= middle));
        return lowRadius + (highRadius - lowRadius) * (y - lowY) / (highY - lowY);
    }
}
