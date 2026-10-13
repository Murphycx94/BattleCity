using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;

namespace Tank.Sim.Tests;

public sealed class BattlefieldTests
{
    // A 2m-thick slab across the X axis at x = 59..61, 10m tall.
    private static readonly Prop Slab = new(1, PropKind.Rock, new Obb2(new Vector2(60, 100), new Vector2(1, 5), 0), -1, 10);

    private static Battlefield WithSlab() => new(TestWorld.Flat(), new[] { Slab }, TestWorld.Border);

    [Fact]
    public void Raycast_ReturnsPropWhenItIsNearer()
    {
        Assert.True(WithSlab().Raycast(new Vector3(20, 1, 100), new Vector3(120, 1, 100), out var hit));
        Assert.Equal(Slab.Id, hit.PropId);
        Assert.Equal(59f, hit.Point.X, 1e-3f);
    }

    [Fact]
    public void Raycast_GroundHit_ReportsTerrain()
    {
        Assert.True(WithSlab().Raycast(new Vector3(20, 5, 100), new Vector3(20, -5, 100), out var hit));
        Assert.Equal(RayHit.TerrainId, hit.PropId);
        Assert.Equal(0f, hit.Point.Y, 1e-3f);
    }

    [Fact]
    public void Raycast_ClearPath_Misses() =>
        Assert.False(WithSlab().Raycast(new Vector3(20, 5, 100), new Vector3(40, 5, 100), out _));

    [Fact]
    public void ClampToPlayable_KeepsPointsInsideTheBorder()
    {
        Vector2 p = WithSlab().ClampToPlayable(new Vector2(-10, 300));
        Assert.Equal(new Vector2(TestWorld.Border, TestWorld.Size - TestWorld.Border), p);
    }

    [Fact]
    public void Ctor_RejectsBorderWiderThanHalfTheMap() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new Battlefield(TestWorld.Flat(), Array.Empty<Prop>(), 120));
}
