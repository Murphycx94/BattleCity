using Tank.Client.Art.Meshes;
using Tank.Sim.Maps;

namespace Tank.Art.Tests;

public class GroundLayersTests
{
    private const float Cell = 2f;

    /// <summary>A grass map of <paramref name="cellsX"/> × <paramref name="cellsZ"/> cells with <paramref name="kind"/> where <paramref name="paint"/> says.</summary>
    private static SurfaceMap Map(int cellsX, int cellsZ, SurfaceKind kind, Func<int, int, bool> paint)
    {
        var cells = new SurfaceKind[cellsX * cellsZ];
        for (int j = 0; j < cellsZ; j++)
        for (int i = 0; i < cellsX; i++)
            cells[j * cellsX + i] = paint(i, j) ? kind : SurfaceKind.Grass;
        return new SurfaceMap(cellsX, cellsZ, Cell, cells);
    }

    private static byte Channel(byte[] rgba, int width, int i, int j, int channel) => rgba[(j * width + i) * 4 + channel];

    [Fact]
    public void Layers_are_needed_for_water_or_any_non_grass_cell()
    {
        SurfaceMap grass = SurfaceMap.Uniform(8, 8, Cell);
        Assert.False(GroundLayers.Needed(grass, null));
        Assert.True(GroundLayers.Needed(grass, 0f));
        Assert.True(GroundLayers.Needed(Map(8, 8, SurfaceKind.Road, (i, j) => i == 3 && j == 5), null));
    }

    [Fact]
    public void A_single_road_cell_blurs_into_its_neighbours()
    {
        byte[] rgba = new GroundLayers(Map(5, 5, SurfaceKind.Road, (i, j) => i == 2 && j == 2)).ToRgba();
        // R is "not grass" grown by one cell (the 3 × 3 block around the road) before the blur: per axis the clamped
        // 3 × 3 box sees 1, 2, 3, 2, 1 of that block's columns (rows), so a texel holds their product in ninths.
        int[] across = { 1, 2, 3, 2, 1 };
        for (int j = 0; j < 5; j++)
        for (int i = 0; i < 5; i++)
        {
            byte road = Math.Abs(i - 2) <= 1 && Math.Abs(j - 2) <= 1 ? (byte)28 : (byte)0;   // 255 / 9, rounded
            Assert.Equal((byte)((across[i] * across[j] * 255 + 4) / 9), Channel(rgba, 5, i, j, 0));
            Assert.Equal(0, Channel(rgba, 5, i, j, 1));
            Assert.Equal(road, Channel(rgba, 5, i, j, 2));
        }
        Assert.Equal(255, Channel(rgba, 5, 2, 2, 0));
        Assert.Equal(170, Channel(rgba, 5, 1, 2, 0));
        Assert.Equal(113, Channel(rgba, 5, 1, 1, 0));
        Assert.Equal(28, Channel(rgba, 5, 0, 0, 0));
    }

    [Fact]
    public void Uniform_sand_stays_full_to_the_edges()
    {
        byte[] rgba = new GroundLayers(SurfaceMap.Uniform(6, 4, Cell, SurfaceKind.Sand)).ToRgba();
        for (int j = 0; j < 4; j++)
        for (int i = 0; i < 6; i++)
        {
            Assert.Equal(255, Channel(rgba, 6, i, j, 0));
            Assert.Equal(255, Channel(rgba, 6, i, j, 1));
            Assert.Equal(0, Channel(rgba, 6, i, j, 2));
        }
    }

    [Fact]
    public void Not_grass_reads_like_the_gpu()
    {
        // A 10 × 10 block of dirt (cells 10..19) in a 30 × 30 grass map of 2 m cells.
        var layers = new GroundLayers(Map(30, 30, SurfaceKind.Dirt, (i, j) => i is >= 10 and < 20 && j is >= 10 and < 20));
        Assert.Equal(60f, layers.SizeX);
        Assert.Equal(60f, layers.SizeZ);
        Assert.Equal(1f, layers.NotGrassAt(30f, 30f), 4);
        Assert.Equal(0f, layers.NotGrassAt(3f, 3f), 4);
        // Grown by a cell, "not grass" covers cells 9..20: texel (8, 15) sees three of those cells of nine (85), texel
        // (9, 15) six (170), texel (10, 15) all nine; half-way between two texel centres (x 17, 19, 21; z 31) is the mean.
        Assert.Equal(85f / 255f, layers.NotGrassAt(17f, 31f), 4);
        Assert.Equal(170f / 255f, layers.NotGrassAt(19f, 31f), 4);
        Assert.Equal(1f, layers.NotGrassAt(21f, 31f), 4);
        Assert.Equal(127.5f / 255f, layers.NotGrassAt(18f, 31f), 4);
        Assert.Equal(212.5f / 255f, layers.NotGrassAt(20f, 31f), 4);
    }

    [Fact]
    public void Bytes_are_row_major_rgba()
    {
        // Sand everywhere except one grass cell at (6, 1) on the east edge of a 7 × 3 map.
        var layers = new GroundLayers(Map(7, 3, SurfaceKind.Sand, (i, j) => !(i == 6 && j == 1)));
        Assert.Equal(7, layers.Width);
        Assert.Equal(3, layers.Height);
        byte[] rgba = layers.ToRgba();
        Assert.Equal(7 * 3 * 4, rgba.Length);
        // Cell (6, 1)'s 3 × 3 box (edges clamped) holds the grass cell twice (itself, and itself again as the clamped
        // neighbour to the east), so G = 7 / 9 of 255; R is full, the lone grass cell being next to sand; A is 0.
        Assert.Equal(255, rgba[(1 * 7 + 6) * 4 + 0]);
        Assert.Equal(198, rgba[(1 * 7 + 6) * 4 + 1]);
        Assert.Equal(0, rgba[(1 * 7 + 6) * 4 + 3]);
        Assert.Equal(198, rgba[(0 * 7 + 6) * 4 + 1]);   // cell (6, 0): the same two grass samples from the row below
        Assert.Equal(227, rgba[(1 * 7 + 5) * 4 + 1]);   // cell (5, 1): one grass sample of nine
        Assert.Equal(255, rgba[(1 * 7 + 3) * 4 + 1]);   // cell (3, 1): all sand around it
        for (int k = 0; k < 7 * 3; k++) Assert.Equal(255, rgba[k * 4]);
    }

    /// <summary>
    /// Every non-grass cell centre keeps grass out even with the patch noise at its most grassy, and grass grows (with the
    /// noise at its least grassy) on every grass cell 6 m or more from ground that isn't grass. The map has a road five
    /// cells wide, a one-cell road on a diagonal, single cells of dirt and sand, sand along an edge and in a corner.
    /// </summary>
    [Fact]
    public void Grass_stops_where_the_ground_is_not_grass()
    {
        const int Size = 40;
        var cells = new SurfaceKind[Size * Size];
        for (int j = 0; j < Size; j++)
        for (int i = 0; i < Size; i++)
        {
            SurfaceKind kind = SurfaceKind.Grass;
            if (i is >= 18 and <= 22) kind = SurfaceKind.Road;                    // a wide road across the map
            else if (i == j && i < 14) kind = SurfaceKind.Road;                   // a narrow road on the diagonal
            else if (i == 30 && j == 8) kind = SurfaceKind.Dirt;                  // a single cell of dirt
            else if (i == 8 && j == 30) kind = SurfaceKind.Sand;                  // and one of sand
            else if (j == Size - 1 && i > 26) kind = SurfaceKind.Sand;            // sand along the south edge
            else if (i >= Size - 2 && j <= 1) kind = SurfaceKind.Sand;            // and in the north-east corner
            cells[j * Size + i] = kind;
        }
        var map = new SurfaceMap(Size, Size, Cell, cells);
        var layers = new GroundLayers(map);
        int notGrass = 0, farGrass = 0;
        for (int j = 0; j < Size; j++)
        for (int i = 0; i < Size; i++)
        {
            float x = (i + 0.5f) * Cell, z = (j + 0.5f) * Cell;
            (float least, float most) = GroundCover.FlatGrassWeightRange(layers.NotGrassAt(x, z));
            if (cells[j * Size + i] != SurfaceKind.Grass)
            {
                notGrass++;
                Assert.True(most < 0.5f, $"{cells[j * Size + i]} cell ({i}, {j}) may grow grass: up to {most:F3}");
            }
            else if (DistanceToNotGrass(cells, Size, x, z) >= 6f)
            {
                farGrass++;
                Assert.True(least > 0.5f, $"grass cell ({i}, {j}) may lose its grass: down to {least:F3}");
            }
        }
        Assert.True(notGrass > 200 && farGrass > 400, $"{notGrass} non-grass and {farGrass} far grass cells checked");
    }

    [Fact]
    public void The_grass_range_spans_the_patch_noise()
    {
        Assert.Equal((1f, 1f), GroundCover.FlatGrassWeightRange(0f));
        Assert.Equal((0f, 0f), GroundCover.FlatGrassWeightRange(1.2f));
        (float least, float most) = GroundCover.FlatGrassWeightRange(0.6f);   // half way between the patch's smoothstep edges
        Assert.True(least < 0.5f && most > 0.5f, $"{least} .. {most}");
        for (float x = 0.3f; x < 40f; x += 1.37f)
        for (float z = 0.7f; z < 40f; z += 1.91f)
            Assert.InRange(GroundCover.GrassWeight(x, z, 1f, 0.6f), least, most);
    }

    /// <summary>The distance from (x, z) to the nearest cell that isn't grass (to its square, 0 inside it).</summary>
    private static float DistanceToNotGrass(SurfaceKind[] cells, int size, float x, float z)
    {
        float best = float.MaxValue;
        for (int j = 0; j < size; j++)
        for (int i = 0; i < size; i++)
        {
            if (cells[j * size + i] == SurfaceKind.Grass) continue;
            float dx = MathF.Max(0f, MathF.Max(i * Cell - x, x - (i + 1) * Cell));
            float dz = MathF.Max(0f, MathF.Max(j * Cell - z, z - (j + 1) * Cell));
            best = MathF.Min(best, MathF.Sqrt(dx * dx + dz * dz));
        }
        return best;
    }
}
