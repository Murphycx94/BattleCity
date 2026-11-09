using System.Globalization;
using Tank.Client.Overlay;
using Tank.Sim.Maps;

namespace Tank.Art.Tests;

public class GroundTextTests
{
    [Fact]
    public void Roads_and_sand_name_the_ground()
    {
        Assert.Equal("公路 ×1.15", GroundText.Of(SurfaceKind.Road, 0f, 1.15f));
        Assert.Equal("沙地 ×0.80", GroundText.Of(SurfaceKind.Sand, 0f, 0.8f));
    }

    [Fact]
    public void Water_names_the_depth()
    {
        Assert.Equal("水深 0.8 m ×0.73", GroundText.Of(SurfaceKind.Sand, 0.8f, 0.7333f));
        Assert.Equal("水深 1.6 m ×0.50", GroundText.Of(SurfaceKind.Dirt, 1.6f, 0.5f));
    }

    [Fact]
    public void Very_shallow_water_shows_a_tenth()
    {
        Assert.Equal("水深 0.1 m ×0.80", GroundText.Of(SurfaceKind.Grass, 0.03f, 0.8f));
    }

    [Fact]
    public void Plain_ground_says_nothing()
    {
        Assert.Null(GroundText.Of(SurfaceKind.Grass, 0f, 1f));
        Assert.Null(GroundText.Of(SurfaceKind.Dirt, 0f, 1f));
        Assert.Null(GroundText.Of(SurfaceKind.Grass, 0f, 1.004f));
    }

    [Fact]
    public void Numbers_ignore_the_machines_culture()
    {
        CultureInfo before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("公路 ×1.15", GroundText.Of(SurfaceKind.Road, 0f, 1.15f));
            Assert.Equal("水深 0.8 m ×0.73", GroundText.Of(SurfaceKind.Sand, 0.8f, 0.7333f));
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }
    [Fact]
    public void Half_tenths_and_half_hundredths_round_up()
    {
        // 0.25 m and 0.625 are exact in binary, so these are true midpoints: away from zero, not to even.
        Assert.Equal("水深 0.3 m ×0.63", GroundText.Of(SurfaceKind.Sand, 0.25f, 0.625f));
        Assert.Equal("沙地 ×0.63", GroundText.Of(SurfaceKind.Sand, 0f, 0.625f));
    }

    [Fact]
    public void A_factor_that_is_not_a_number_says_nothing()
    {
        Assert.Null(GroundText.Of(SurfaceKind.Road, 0f, float.NaN));
        Assert.Null(GroundText.Of(SurfaceKind.Road, 0.5f, float.PositiveInfinity));
        Assert.Null(GroundText.Of(SurfaceKind.Sand, 0f, float.NegativeInfinity));
    }

    [Fact]
    public void A_depth_that_is_not_a_number_or_below_zero_counts_as_dry()
    {
        Assert.Equal("公路 ×1.15", GroundText.Of(SurfaceKind.Road, float.NaN, 1.15f));
        Assert.Equal("公路 ×1.15", GroundText.Of(SurfaceKind.Road, float.PositiveInfinity, 1.15f));
        Assert.Equal("沙地 ×0.80", GroundText.Of(SurfaceKind.Sand, -0.3f, 0.8f));
    }
}
