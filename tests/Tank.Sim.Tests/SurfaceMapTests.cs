using Tank.Sim.Maps;

namespace Tank.Sim.Tests;

/// <summary>The ground's kinds, one per 2 m cell of the height field (riverland design §2.1).</summary>
public sealed class SurfaceMapTests
{
    /// <summary>4 × 3 cells of 2 m, neighbours always of different kinds so a wrong index shows.</summary>
    private static SurfaceMap Mixed()
    {
        var cells = new SurfaceKind[12];
        for (int k = 0; k < cells.Length; k++) cells[k] = (SurfaceKind)(k % 3);
        return new SurfaceMap(4, 3, 2f, cells);
    }

    [Fact]
    public void Construction_checks_sizes_kinds_and_cell_size()
    {
        Assert.Throws<ArgumentException>(() => new SurfaceMap(4, 3, 2f, new SurfaceKind[11]));
        Assert.Throws<ArgumentException>(() => new SurfaceMap(4, 3, 2f, new SurfaceKind[13]));
        var bad = new SurfaceKind[12];
        bad[7] = (SurfaceKind)4;
        Assert.Throws<ArgumentException>(() => new SurfaceMap(4, 3, 2f, bad));
        Assert.Throws<ArgumentException>(() => new SurfaceMap(4, 3, 0f, new SurfaceKind[12]));
        Assert.Throws<ArgumentException>(() => new SurfaceMap(4, 3, float.NaN, new SurfaceKind[12]));
        Assert.Throws<ArgumentException>(() => new SurfaceMap(4, 3, float.PositiveInfinity, new SurfaceKind[12]));
        Assert.Throws<ArgumentException>(() => new SurfaceMap(0, 3, 2f, Array.Empty<SurfaceKind>()));
        Assert.Throws<ArgumentException>(() => new SurfaceMap(4, 0, 2f, Array.Empty<SurfaceKind>()));
        Assert.Equal(SurfaceKind.Road, SurfaceMap.LastKind);
    }

    [Fact]
    public void The_cells_are_copied()
    {
        var cells = new SurfaceKind[12];
        var map = new SurfaceMap(4, 3, 2f, cells);
        cells[5] = SurfaceKind.Road;
        Assert.True(map.IsUniform(SurfaceKind.Grass));
        Assert.Equal(SurfaceKind.Grass, map[1, 1]);
    }

    [Fact]
    public void A_point_falls_in_the_cell_it_lies_in()
    {
        SurfaceMap map = Mixed();
        Assert.Equal(4, map.CellsX);
        Assert.Equal(3, map.CellsZ);
        Assert.Equal(2f, map.CellSize);
        Assert.Equal(map[1, 0], map.At(3.99f, 0.5f));
        Assert.Equal(map[2, 0], map.At(4f, 0.5f));
        Assert.Equal(map[0, 1], map.At(1f, 2f));
        Assert.NotEqual(map[1, 0], map[2, 0]);
        Assert.NotEqual(map[0, 0], map[0, 1]);
        for (int j = 0; j < 3; j++)
        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(map.Cells[j * 4 + i], map[i, j]);
            Assert.Equal(map[i, j], map.At(i * 2f + 1f, j * 2f + 1f));
        }
    }

    [Fact]
    public void Points_off_the_grid_clamp_to_the_edge_cells()
    {
        SurfaceMap map = Mixed();
        Assert.Equal(map[0, 0], map.At(-5f, -5f));
        Assert.Equal(map[3, 2], map.At(1e6f, 1e6f));
        Assert.Equal(map[3, 2], map.At(8f, 6f));
        Assert.Equal(map[0, 0], map[-1, -1]);
        Assert.Equal(map[3, 2], map[4, 3]);
    }

    [Fact]
    public void Uniform_is_uniform()
    {
        SurfaceMap sand = SurfaceMap.Uniform(5, 4, 2f, SurfaceKind.Sand);
        Assert.True(sand.IsUniform(SurfaceKind.Sand));
        Assert.False(sand.IsUniform(SurfaceKind.Grass));
        Assert.Equal(20, sand.Cells.Length);
        Assert.True(SurfaceMap.Uniform(5, 4, 2f).IsUniform(SurfaceKind.Grass));
        Assert.False(Mixed().IsUniform(SurfaceKind.Grass));
    }
}
