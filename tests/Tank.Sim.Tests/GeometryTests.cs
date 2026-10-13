using System.Numerics;
using Tank.Sim.Geometry;

namespace Tank.Sim.Tests;

public sealed class GeometryTests
{
    private static Obb2 Square(float x, float z, float yaw = 0) => new(new Vector2(x, z), new Vector2(1, 1), yaw);

    [Fact]
    public void Penetration_SeparatedBoxes_ReturnsFalse() =>
        Assert.False(Obb2.TryGetPenetration(Square(0, 0), Square(3, 0), out _));

    [Fact]
    public void Penetration_Overlap_PushesAlongShallowestAxis()
    {
        Assert.True(Obb2.TryGetPenetration(Square(0, 0), Square(1.5f, 0), out var push));
        Assert.Equal(-0.5f, push.X, 1e-5f);
        Assert.Equal(0f, push.Y, 1e-5f);
    }

    [Fact]
    public void Penetration_RotatedBox_UsesItsProjectedRadius()
    {
        Assert.True(Obb2.TryGetPenetration(Square(0, 0), Square(2.3f, 0, MathF.PI / 4), out var push));
        Assert.Equal(-(1 + MathF.Sqrt(2) - 2.3f), push.X, 1e-4f);
        Assert.Equal(0f, push.Y, 1e-4f);
    }

    [Fact]
    public void Contains_UsesLocalAxes()
    {
        var box = new Obb2(Vector2.Zero, new Vector2(1, 3), MathF.PI / 2); // long side now along X
        Assert.True(box.Contains(new Vector2(2.5f, 0)));
        Assert.False(box.Contains(new Vector2(0, 2.5f)));
    }

    [Fact]
    public void SegmentPrism_HitsTheNearFace()
    {
        Assert.True(Intersect.SegmentPrism(Square(0, 0), 0, 2, new Vector3(-5, 1, 0), new Vector3(5, 1, 0), out float t));
        Assert.Equal(0.4f, t, 1e-5f);
    }

    [Fact]
    public void SegmentPrism_PassingAboveTheTop_Misses() =>
        Assert.False(Intersect.SegmentPrism(Square(0, 0), 0, 2, new Vector3(-5, 3, 0), new Vector3(5, 3, 0), out _));

    [Fact]
    public void SegmentPrism_RotatedBox_HitsCorner()
    {
        Assert.True(Intersect.SegmentPrism(Square(0, 0, MathF.PI / 4), 0, 2, new Vector3(-5, 1, 0), new Vector3(5, 1, 0), out float t));
        Assert.Equal((5 - MathF.Sqrt(2)) / 10, t, 1e-4f);
    }

    [Fact]
    public void SegmentPrism_StartingInside_HitsAtZero()
    {
        Assert.True(Intersect.SegmentPrism(Square(0, 0), 0, 2, new Vector3(0, 1, 0), new Vector3(5, 1, 0), out float t));
        Assert.Equal(0f, t);
    }

    [Fact]
    public void Penetration_OnlyOtherBoxAxisSeparates_ReturnsFalse() =>
        Assert.False(Obb2.TryGetPenetration(Square(0, 0), Square(2.1f, 2.1f, MathF.PI / 4), out _));

    [Fact]
    public void Penetration_PushPointsTowardAsSide()
    {
        Assert.True(Obb2.TryGetPenetration(Square(0, 1.5f), Square(0, 0), out var push));
        Assert.Equal(0f, push.X, 1e-5f);
        Assert.Equal(0.5f, push.Y, 1e-5f);
    }

    [Fact]
    public void Penetration_AfterApplyingPush_BoxesAreSeparated()
    {
        var a = Square(0.4f, 0.9f, 0.3f);
        var b = Square(0, 0, 0.8f);
        Assert.True(Obb2.TryGetPenetration(a, b, out var push));
        Assert.False(Obb2.TryGetPenetration(a.WithCenter(a.Center + push * 1.0001f), b, out _));
    }

    [Fact]
    public void SegmentPrism_StoppingShortOfTheBox_Misses() =>
        Assert.False(Intersect.SegmentPrism(Square(0, 0), 0, 2, new Vector3(-5, 1, 0), new Vector3(-2, 1, 0), out _));

    [Fact]
    public void SegmentPrism_StartingPastTheBox_Misses() =>
        Assert.False(Intersect.SegmentPrism(Square(0, 0), 0, 2, new Vector3(3, 1, 0), new Vector3(8, 1, 0), out _));

    [Fact]
    public void SegmentPrism_DescendingOntoTop_HitsAtTheTopPlane()
    {
        Assert.True(Intersect.SegmentPrism(Square(0, 0), 0, 2, new Vector3(0, 5, 0), new Vector3(0, -1, 0), out float t));
        Assert.Equal(0.5f, t, 1e-5f);
    }

    [Fact]
    public void SegmentPrism_ParallelOutsideTheBox_Misses() =>
        Assert.False(Intersect.SegmentPrism(Square(0, 0), 0, 2, new Vector3(-5, 1, 3), new Vector3(5, 1, 3), out _));
}
