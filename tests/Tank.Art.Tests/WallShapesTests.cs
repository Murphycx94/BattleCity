using System.Numerics;
using Tank.Client.Art.Meshes;
using Tank.Sim.Maps;

namespace Tank.Art.Tests;

public class WallShapesTests
{
    private const float Sink = WallLine.Sink;

    private static (Vector3 Min, Vector3 Max) Bounds(MeshData mesh) =>
        (mesh.Positions.Aggregate(new Vector3(float.MaxValue), Vector3.Min), mesh.Positions.Aggregate(new Vector3(float.MinValue), Vector3.Max));

    [Theory]
    [InlineData(PropKind.BrickWall)]
    [InlineData(PropKind.StoneWall)]
    public void A_wall_block_fills_its_prop_exactly(PropKind kind)
    {
        float thickness = WallLine.Thickness(kind), height = WallLine.Height(kind);
        MeshData block = WallShapes.Block(WallLine.SegmentLength, height, thickness, Sink);
        var (min, max) = Bounds(block);
        Assert.Equal(new Vector3(-1f, -Sink, -thickness / 2), min, new Near(1e-4f));
        Assert.Equal(new Vector3(1f, height, thickness / 2), max, new Near(1e-4f));
        MeshAssert.Closed(block);
        MeshAssert.WindingMatchesNormals(block);
    }

    [Fact]
    public void A_fence_panel_stays_inside_its_prop()
    {
        float thickness = WallLine.Thickness(PropKind.Fence), height = WallLine.Height(PropKind.Fence);
        MeshData fence = WallShapes.Fence(WallLine.SegmentLength, height, thickness, Sink);
        var (min, max) = Bounds(fence);
        Assert.True(min.X >= -1f - 1e-4f && max.X <= 1f + 1e-4f, $"x {min.X} … {max.X}");
        Assert.True(min.Z >= -thickness / 2 - 1e-4f && max.Z <= thickness / 2 + 1e-4f, $"z {min.Z} … {max.Z}");
        Assert.Equal(-Sink, min.Y, 1e-4f);
        Assert.Equal(height, max.Y, 1e-4f);
        Assert.True(max.X - min.X > 1.9f, "the rails run the whole panel");
        MeshAssert.WindingMatchesNormals(fence);
    }

    [Fact]
    public void A_fence_panel_too_short_for_its_two_posts_is_refused()
    {
        float thickness = WallLine.Thickness(PropKind.Fence), height = WallLine.Height(PropKind.Fence);
        var e = Assert.Throws<ArgumentOutOfRangeException>(() => WallShapes.Fence(0.4f, height, thickness, Sink));
        Assert.Equal("length", e.ParamName);
        WallShapes.Fence(0.6f, height, thickness, Sink);   // room for both posts, if only just
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void A_fallen_fence_lies_flat_on_the_side_it_fell_to(int side)
    {
        float thickness = WallLine.Thickness(PropKind.Fence), post = MathF.Min(WallShapes.PostWidth, thickness);
        MeshData fallen = WallShapes.Fence(WallLine.SegmentLength, WallLine.Height(PropKind.Fence), thickness, Sink)
            .Transformed(FenceFall.Pose(1f, side, post));   // as WallViews turns it: about its posts' face
        var (min, max) = Bounds(fallen);
        Assert.True(max.Y <= 0.3f, $"y up to {max.Y}");
        Assert.Equal(0f, min.Y, 1e-4f);   // resting on its posts, not hovering over the ground
        float reach = side > 0 ? max.Z : -min.Z;
        Assert.InRange(reach, 1.5f, 1.65f);   // the panel's 1.5 m now lies along the ground on that side
    }

    [Fact]
    public void A_fence_falls_faster_and_faster_and_then_lies_still()
    {
        Assert.Equal(0f, FenceFall.Progress(0f));
        Assert.True(FenceFall.Progress(FenceFall.Seconds / 2) < 0.5f, "starts slowly");
        Assert.Equal(1f, FenceFall.Progress(FenceFall.Seconds));
        Assert.Equal(1f, FenceFall.Progress(10f));
        Assert.Equal(Matrix4x4.Identity, FenceFall.Pose(0f, 1, 0.2f));
    }

    [Theory]
    [InlineData(PropKind.BrickWall, true)]
    [InlineData(PropKind.StoneWall, false)]
    public void Rubble_is_a_low_heap_spread_over_the_rubble_footprint(PropKind kind, bool blocky)
    {
        float depth = WallLine.Thickness(kind) + 2 * Prop.RubbleSpread;
        MeshData heap = WallShapes.Rubble(WallLine.SegmentLength, depth, 0.8f, blocky, 42u);
        var (min, max) = Bounds(heap);
        Assert.True(min.X >= -1f - 1e-4f && max.X <= 1f + 1e-4f, $"x {min.X} … {max.X}");
        Assert.True(min.Z >= -depth / 2 - 1e-4f && max.Z <= depth / 2 + 1e-4f, $"z {min.Z} … {max.Z}");
        Assert.True(max.Y <= 0.8f + 1e-4f, $"{max.Y} m tall");
        Assert.True(max.Y > 0.4f, "a heap, not a carpet");
        Assert.True(max.X - min.X > 1.4f, "spread along the whole segment");
        Assert.All(heap.Colors, c => Assert.InRange(c.X, 0.8f, 1f));
        MeshAssert.WindingMatchesNormals(heap);
    }

    [Theory]
    [InlineData(PropKind.BrickWall, true)]
    [InlineData(PropKind.StoneWall, false)]
    public void Rubble_is_highest_along_the_middle_and_lower_towards_the_edges(PropKind kind, bool blocky)
    {
        float depth = WallLine.Thickness(kind) + 2 * Prop.RubbleSpread;
        for (uint seed = 1; seed <= 8; seed++)
        {
            MeshData heap = WallShapes.Rubble(WallLine.SegmentLength, depth, 0.8f, blocky, seed);
            float top = heap.Positions.Max(p => p.Y);
            float outerTop = heap.Positions.Where(p => MathF.Abs(p.Z) > depth / 4).Max(p => p.Y);
            Assert.True(outerTop < top, $"seed {seed}: {outerTop} m in the outer quarters, {top} m at the top");
        }
    }

    [Fact]
    public void Rubble_refuses_a_footprint_too_small_for_its_chunks()
    {
        var shortHeap = Assert.Throws<ArgumentOutOfRangeException>(() => WallShapes.Rubble(0.5f, 1.6f, 0.8f, true, 1u));
        Assert.Equal("length", shortHeap.ParamName);
        var thinHeap = Assert.Throws<ArgumentOutOfRangeException>(() => WallShapes.Rubble(2f, 0.5f, 0.8f, false, 1u));
        Assert.Equal("depth", thinHeap.ParamName);
    }

    [Fact]
    public void Rubble_is_the_same_for_the_same_seed_and_differs_between_seeds()
    {
        MeshData a = WallShapes.Rubble(2f, 1.6f, 0.8f, true, 7u), b = WallShapes.Rubble(2f, 1.6f, 0.8f, true, 7u), c = WallShapes.Rubble(2f, 1.6f, 0.8f, true, 8u);
        Assert.Equal(a.Positions, b.Positions);
        Assert.NotEqual(a.Positions, c.Positions);
    }

    private sealed class Near(float tolerance) : IEqualityComparer<Vector3>
    {
        public bool Equals(Vector3 a, Vector3 b) => Vector3.Distance(a, b) <= tolerance;
        public int GetHashCode(Vector3 v) => 0;
    }
}
