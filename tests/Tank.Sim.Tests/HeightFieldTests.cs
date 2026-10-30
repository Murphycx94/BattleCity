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
    public void SurfaceNormalAt_Ramp_MatchesNormalAt()
    {
        Vector3 n = TestWorld.RampNorth(1f).SurfaceNormalAt(51.3f, 47.9f);
        Assert.Equal(MathF.Sqrt(0.5f), n.Y, 1e-4f);
        Assert.Equal(MathF.Sqrt(0.5f), n.Z, 1e-4f);
    }

    [Fact]
    public void SurfaceNormalAt_IsTheCellsOwnSlope()
    {
        // Small's cell (0, 0) rises 0.1 per metre along x and 0.3 along z throughout; on a sample line the cell after it counts.
        Vector3 inCell = Small.SurfaceNormalAt(5, 5), onLine = Small.SurfaceNormalAt(10, 5);
        Vector3 expected = Vector3.Normalize(new Vector3(-1, 10, -3));
        Assert.Equal(expected.X, inCell.X, 1e-6f);
        Assert.Equal(expected.Y, inCell.Y, 1e-6f);
        Assert.Equal(expected.Z, inCell.Z, 1e-6f);
        Assert.Equal(inCell, onLine);
        Assert.Equal(Vector3.UnitY, Small.SurfaceNormalAt(-5, 25));   // flat outside the grid, as the ground is there
    }

    [Fact]
    public void SurfaceNormalAt_KeepsAKinkWhereNormalAtSpreadsIt()
    {
        // Flat at 0 for z ≥ 100, rising steeply north of it: half a metre south of the kink the ground is flat.
        HeightField cliff = TestWorld.CliffNorth(100, 2.75f);
        Assert.Equal(Vector3.UnitY, cliff.SurfaceNormalAt(100, 100.5f));
        Assert.True(cliff.NormalAt(100, 100.5f).Y < MathF.Cos(32 * MathF.PI / 180));
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

    [Fact]
    public void Raycast_NonFiniteEndpoint_ReturnsFalse()
    {
        var field = TestWorld.Flat();
        Assert.False(field.Raycast(new Vector3(float.NaN, 5, 0), new Vector3(10, 5, 10), out float t));
        Assert.Equal(0f, t);
        Assert.False(field.Raycast(new Vector3(0, 5, 0), new Vector3(10, float.PositiveInfinity, 10), out _));
    }

    [Fact]
    public void Raycast_HugeSegment_ReturnsPromptly() =>
        Assert.False(TestWorld.Flat().Raycast(new Vector3(0, 5, 0), new Vector3(3e9f, 5, 0), out _));

    [Fact]
    public void Raycast_HorizontalIntoRamp_HitsWhereGroundRises()
    {
        Assert.True(TestWorld.RampNorth(0.5f).Raycast(new Vector3(100, 60, 150), new Vector3(100, 60, 50), out float t));
        Assert.Equal(0.7f, t, 1e-3f);
    }

    [Fact]
    public void Raycast_StartingBelowGround_HitsAtZero()
    {
        Assert.True(TestWorld.Flat().Raycast(new Vector3(10, -1, 10), new Vector3(20, 5, 20), out float t));
        Assert.Equal(0f, t);
    }

    [Fact]
    public void Ctor_CopiesHeights()
    {
        var source = new float[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 };
        var field = new HeightField(3, 3, 10f, source);
        source[4] = 100;
        Assert.Equal(4f, field.HeightAt(10, 10), 1e-5f);
    }
}
