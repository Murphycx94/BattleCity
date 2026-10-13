using Tank.Sim.Terrain;

namespace Tank.Sim.Tests;

public sealed class TerrainGeneratorTests
{
    private static readonly TerrainGenerator.Hill[] NoHills = Array.Empty<TerrainGenerator.Hill>();

    [Fact]
    public void SameSeed_GivesIdenticalTerrain()
    {
        var a = TerrainGenerator.Generate(7, 33, 2f, 5f, NoHills);
        var b = TerrainGenerator.Generate(7, 33, 2f, 5f, NoHills);
        for (int j = 0; j < 33; j++)
        for (int i = 0; i < 33; i++)
            Assert.Equal(a.Sample(i, j), b.Sample(i, j));
    }

    [Fact]
    public void DifferentSeed_GivesDifferentTerrain()
    {
        var a = TerrainGenerator.Generate(7, 33, 2f, 5f, NoHills);
        var b = TerrainGenerator.Generate(8, 33, 2f, 5f, NoHills);
        Assert.Contains(Enumerable.Range(0, 33), i => a.Sample(i, 16) != b.Sample(i, 16));
    }

    [Fact]
    public void Noise_StaysWithinRoughness()
    {
        var field = TerrainGenerator.Generate(3, 65, 2f, 5f, NoHills);
        for (int j = 0; j < 65; j++)
        for (int i = 0; i < 65; i++)
            Assert.InRange(field.Sample(i, j), 0f, 5f);
    }

    [Fact]
    public void Hill_PeaksAtItsCentre_AndFadesToZeroAtItsRadius()
    {
        var field = TerrainGenerator.Generate(1, 101, 2f, 0f, new[] { new TerrainGenerator.Hill(100, 100, 30, 10) });
        Assert.Equal(10f, field.HeightAt(100, 100), 1e-4f);
        Assert.Equal(0f, field.HeightAt(100, 132), 1e-4f);
    }
}
