using System.Numerics;
using Tank.Sim.Terrain;

namespace Tank.Sim.Tests;

public sealed class HeightFieldTests
{
    // 3×3 samples, 10m apart: row j=0 is 0,1,2; j=1 is 3,4,5; j=2 is 6,7,8.
    private static readonly HeightField Small = new(3, 3, 10f, new float[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 });

    [Fact]
    public void HeightAt_OnSamples_ReturnsSampleValue() => Assert.Equal(4f, Small.HeightAt(10, 10), 1e-5f);

    [Fact]
    public void HeightAt_BetweenSamples_IsBilinear()
    {
        Assert.Equal(0.5f, Small.HeightAt(5, 0), 1e-5f);
        Assert.Equal(2f, Small.HeightAt(5, 5), 1e-5f);
    }

    [Fact]
    public void HeightAt_OutsideGrid_UsesEdge()
    {
        Assert.Equal(0f, Small.HeightAt(-50, 0), 1e-5f);
        Assert.Equal(8f, Small.HeightAt(100, 100), 1e-5f);
    }

    [Fact]
    public void Size_IsCellsTimesCellSize() => Assert.Equal(20f, Small.SizeX);

    [Fact]
    public void NormalAt_FlatGround_PointsUp()
    {
        Vector3 n = TestWorld.Flat(3).NormalAt(50, 50);
        Assert.Equal(1f, n.Y, 1e-5f);
    }

    [Fact]
    public void NormalAt_Ramp_TiltsAwayFromHighSide()
    {
        Vector3 n = TestWorld.RampNorth(1f).NormalAt(50, 50);
        Assert.Equal(MathF.Sqrt(0.5f), n.Y, 1e-4f);
        Assert.Equal(MathF.Sqrt(0.5f), n.Z, 1e-4f);
    }

    [Fact]
    public void Raycast_DownIntoFlatGround_HitsAtSurface()
    {
        Assert.True(TestWorld.Flat().Raycast(new Vector3(10, 10, 10), new Vector3(10, -10, 10), out float t));
        Assert.Equal(0.5f, t, 1e-3f);
    }

    [Fact]
    public void Raycast_AboveGround_Misses() =>
        Assert.False(TestWorld.Flat().Raycast(new Vector3(0, 5, 0), new Vector3(20, 5, 20), out _));

    [Fact]
    public void Ctor_RejectsWrongHeightCount() =>
        Assert.Throws<ArgumentException>(() => new HeightField(3, 3, 1f, new float[8]));
}
