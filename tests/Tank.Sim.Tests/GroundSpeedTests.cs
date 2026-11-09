using Tank.Sim.Maps;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Tests;

/// <summary>Top-speed factors of the ground and of shallow water (riverland design §2.1–2.3).</summary>
public sealed class GroundSpeedTests
{
    [Fact]
    public void Ground_factors_are_the_designs()
    {
        Assert.Equal(1f, GroundSpeed.Of(SurfaceKind.Grass));
        Assert.Equal(1f, GroundSpeed.Of(SurfaceKind.Dirt));
        Assert.Equal(0.8f, GroundSpeed.Of(SurfaceKind.Sand));
        Assert.Equal(1.15f, GroundSpeed.Of(SurfaceKind.Road));
    }

    [Theory]
    [InlineData(0.01f, 0.8f)]
    [InlineData(0.6f, 0.8f)]
    [InlineData(0.9f, 0.7f)]
    [InlineData(1.2f, 0.6f)]
    [InlineData(1.3f, 0.5667f)]
    [InlineData(1.5f, 0.5f)]
    [InlineData(1.6f, 0.5f)]
    [InlineData(3f, 0.5f)]
    public void Water_factor_is_one_minus_a_third_of_the_depth_clamped(float depth, float factor) =>
        Assert.Equal(factor, GroundSpeed.InWater(depth), 1e-4f);

    [Fact]
    public void Water_replaces_the_ground_factor()
    {
        Assert.Equal(0.8f, GroundSpeed.Factor(SurfaceKind.Road, 0.5f));
        Assert.Equal(0.6f, GroundSpeed.Factor(SurfaceKind.Sand, 1.2f), 1e-6f);
        Assert.Equal(1.15f, GroundSpeed.Factor(SurfaceKind.Road, 0f));
        Assert.Equal(0.8f, GroundSpeed.Factor(SurfaceKind.Sand, 0f));
    }

    /// <summary>Road, grass, sand, the river's middle (1.3 m) and the lake's (1.6 m), km/h (riverland design §2.3).</summary>
    [Theory]
    [InlineData("light", 69f, 60f, 48f, 34f, 30f)]
    [InlineData("medium", 57.5f, 50f, 40f, 28.3f, 25f)]
    [InlineData("heavy", 40.3f, 35f, 28f, 19.8f, 17.5f)]
    [InlineData("td", 46f, 40f, 32f, 22.7f, 20f)]
    public void Top_speeds_match_the_design_table(string tank, float road, float grass, float sand, float river, float lake)
    {
        float top = TankCatalog.ById(tank).MaxForwardSpeed * 3.6f;
        Assert.Equal(road, top * GroundSpeed.Factor(SurfaceKind.Road, 0f), 0.1f);
        Assert.Equal(grass, top * GroundSpeed.Factor(SurfaceKind.Grass, 0f), 0.1f);
        Assert.Equal(sand, top * GroundSpeed.Factor(SurfaceKind.Sand, 0f), 0.1f);
        Assert.Equal(river, top * GroundSpeed.Factor(SurfaceKind.Sand, 1.3f), 0.1f);
        Assert.Equal(lake, top * GroundSpeed.Factor(SurfaceKind.Sand, 1.6f), 0.1f);
    }
}
