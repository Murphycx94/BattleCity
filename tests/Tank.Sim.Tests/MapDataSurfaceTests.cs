using System.Numerics;
using Tank.Sim.Maps;
using Tank.Sim.Terrain;

namespace Tank.Sim.Tests;

/// <summary>Map data carries the ground's kinds and the water level, and builds a battlefield with the same (riverland design §4.5).</summary>
public sealed class MapDataSurfaceTests
{
    private const int Cells = TestWorld.Samples - 1;

    /// <summary>Rolling ground under a water level of −0.37 m, its cells of every kind.</summary>
    private static Battlefield Mixed()
    {
        int samples = TestWorld.Samples;
        var heights = new float[samples * samples];
        for (int j = 0; j < samples; j++)
        for (int i = 0; i < samples; i++)
            heights[j * samples + i] = MathF.Sin(i * 0.21f) * 1.7f + MathF.Cos(j * 0.13f) * 0.9f;
        var cells = new SurfaceKind[Cells * Cells];
        for (int k = 0; k < cells.Length; k++) cells[k] = (SurfaceKind)(k * 7 / 5 % 4);
        return new Battlefield(new HeightField(samples, samples, TestWorld.Cell, heights), [], TestWorld.Border,
            new SurfaceMap(Cells, Cells, TestWorld.Cell, cells), waterLevel: -0.37f);
    }

    [Fact]
    public void Of_and_Build_carry_the_surface_and_the_water_level()
    {
        Battlefield field = Mixed();
        MapData data = MapData.Of(field);
        Assert.Equal(field.Surface.Cells.ToArray(), data.Surface);
        Assert.Equal(-37, data.WaterLevelCm);

        Battlefield built = data.Build();
        Assert.Equal(-0.37f, built.WaterLevel);
        for (int j = 0; j < Cells; j++)
        for (int i = 0; i < Cells; i++)
        {
            var centre = new Vector2((i + 0.5f) * TestWorld.Cell, (j + 0.5f) * TestWorld.Cell);
            Assert.Equal(field.SurfaceAt(centre), built.SurfaceAt(centre));
        }
        Assert.Contains(data.Surface, k => k == SurfaceKind.Road);
        Assert.Contains(data.Surface, k => k == SurfaceKind.Sand);
    }

    [Fact]
    public void The_six_argument_constructor_means_all_grass_and_no_water()
    {
        var data = new MapData(3, 3, 2f, new int[9], Array.Empty<Prop>(), 0.5f);
        Assert.Equal(4, data.Surface.Length);
        Assert.All(data.Surface, k => Assert.Equal(SurfaceKind.Grass, k));
        Assert.Null(data.WaterLevelCm);
        Battlefield built = data.Build();
        Assert.True(built.Surface.IsUniform(SurfaceKind.Grass));
        Assert.Null(built.WaterLevel);
    }

    [Fact]
    public void A_bad_surface_or_water_level_is_refused()
    {
        Assert.Throws<ArgumentException>(() => new MapData(3, 3, 2f, new int[9], Array.Empty<Prop>(), 0.5f, new SurfaceKind[3], null));
        var badKind = new SurfaceKind[4];
        badKind[2] = (SurfaceKind)9;
        Assert.Throws<ArgumentException>(() => new MapData(3, 3, 2f, new int[9], Array.Empty<Prop>(), 0.5f, badKind, null));
        Assert.Throws<ArgumentException>(() => new MapData(3, 3, 2f, new int[9], Array.Empty<Prop>(), 0.5f, null, 1_000_001));
        Assert.Throws<ArgumentException>(() => new MapData(3, 3, 2f, new int[9], Array.Empty<Prop>(), 0.5f, null, -1_000_001));
        Assert.Equal(MapData.MaxWaterLevelCm, new MapData(3, 3, 2f, new int[9], Array.Empty<Prop>(), 0.5f, null, MapData.MaxWaterLevelCm).WaterLevelCm);
    }

    [Fact]
    public void The_data_of_a_built_field_is_the_same_data()
    {
        MapData data = MapData.Of(Mixed());
        MapData again = MapData.Of(data.Build());
        Assert.Equal(data.HeightsCm, again.HeightsCm);
        Assert.Equal(data.Surface, again.Surface);
        Assert.Equal(data.WaterLevelCm, again.WaterLevelCm);
    }
}
