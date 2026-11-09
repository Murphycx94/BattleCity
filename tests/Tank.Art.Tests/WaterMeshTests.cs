using System.Numerics;
using Tank.Client.Art.Meshes;

namespace Tank.Art.Tests;

public class WaterMeshTests
{
    private const float Cell = 2f;

    /// <summary>5 × 5 samples (4 × 4 cells): −1 at the centre sample, +1 everywhere else.</summary>
    private static float Pit(int i, int j) => i == 2 && j == 2 ? -1f : 1f;

    private static MeshData PitWater() => WaterMesh.Build(0, 0, 4, 4, Cell, Pit, 0f);

    /// <summary>The (x, z) corners of each triangle, as a set of corner sets, for comparing two meshes' triangles by position.</summary>
    private static HashSet<string> TriangleCorners(MeshData mesh, Func<Vector3, bool> keep)
    {
        var set = new HashSet<string>();
        for (int t = 0; t < mesh.TriangleCount; t++)
        {
            Vector3[] corners = Enumerable.Range(0, 3).Select(k => mesh.Positions[mesh.Indices[3 * t + k]]).ToArray();
            if (!keep((corners[0] + corners[1] + corners[2]) / 3f)) continue;
            set.Add(string.Join(";", corners.Select(c => $"{c.X:F3},{c.Z:F3}").Order()));
        }
        return set;
    }

    [Fact]
    public void Only_cells_with_a_corner_under_water_are_built()
    {
        MeshData water = PitWater();
        Assert.Equal(8, water.TriangleCount);
        foreach (Vector3 p in water.Positions)
        {
            Assert.Equal(0f, p.Y);
            Assert.InRange(p.X, 1 * Cell, 3 * Cell);   // the four cells around the centre sample
            Assert.InRange(p.Z, 1 * Cell, 3 * Cell);
        }
    }

    [Fact]
    public void Depth_is_the_signed_level_above_each_corner()
    {
        MeshData water = PitWater();
        for (int v = 0; v < water.VertexCount; v++)
        {
            Vector3 p = water.Positions[v];
            int i = (int)MathF.Round(p.X / Cell), j = (int)MathF.Round(p.Z / Cell);
            Assert.Equal(0f - Pit(i, j), water.Uvs[v].X);
            Assert.Equal(0f, water.Uvs[v].Y);
        }
        Assert.Contains(water.Uvs, uv => uv.X == 1f);    // the centre
        Assert.Contains(water.Uvs, uv => uv.X == -1f);   // the dry ring around it
    }

    [Fact]
    public void Diagonals_match_the_terrain()
    {
        MeshData water = PitWater();
        MeshData ground = GridMesh.Build(0, 0, 4, 4, Cell, Pit, (_, _) => Vector3.UnitY);
        bool InsideTheWetCells(Vector3 c) => c.X > Cell && c.X < 3 * Cell && c.Z > Cell && c.Z < 3 * Cell;
        HashSet<string> wet = TriangleCorners(water, _ => true);
        Assert.Equal(8, wet.Count);
        Assert.True(wet.SetEquals(TriangleCorners(ground, InsideTheWetCells)), "the water splits each cell like the terrain does");
        MeshAssert.WindingMatchesNormals(water);
    }

    [Fact]
    public void The_shoreline_is_where_the_terrain_meets_the_level()
    {
        // An uneven bank so the crossings land at different fractions along the edges.
        float Bank(int i, int j) => 0.7f * i - 1.9f + 0.35f * j * j - 0.4f * MathF.Sin(i * 1.3f + j);
        const float level = 0.5f;
        MeshData water = WaterMesh.Build(0, 0, 6, 5, Cell, Bank, level);
        int crossings = 0;
        for (int t = 0; t < water.TriangleCount; t++)
        for (int k = 0; k < 3; k++)
        {
            int a = water.Indices[3 * t + k], b = water.Indices[3 * t + (k + 1) % 3];
            float da = water.Uvs[a].X, db = water.Uvs[b].X;
            if (MathF.Sign(da) * MathF.Sign(db) >= 0) continue;
            Vector3 pa = water.Positions[a], pb = water.Positions[b];
            float ha = Bank((int)MathF.Round(pa.X / Cell), (int)MathF.Round(pa.Z / Cell));
            float hb = Bank((int)MathF.Round(pb.X / Cell), (int)MathF.Round(pb.Z / Cell));
            float depthZero = da / (da - db);                 // where the interpolated depth is 0
            float terrainAtLevel = (level - ha) / (hb - ha);  // where the drawn terrain edge reaches the level
            Assert.Equal(terrainAtLevel, depthZero, 1e-5f);
            crossings++;
        }
        Assert.True(crossings > 4, "the bank should cross the level on several edges");
    }

    [Fact]
    public void A_dry_chunk_is_empty_and_a_wet_one_full()
    {
        Assert.Same(MeshData.Empty, WaterMesh.Build(0, 0, 3, 3, Cell, (_, _) => 2f, 0f));
        Assert.Same(MeshData.Empty, WaterMesh.Build(0, 0, 3, 3, Cell, (_, _) => 0f, 0f));   // level with the water: still dry
        MeshData full = WaterMesh.Build(0, 0, 3, 2, Cell, (i, j) => -1f - i - j, 0f);
        Assert.Equal(3 * 2 * 2, full.TriangleCount);
        Assert.Equal(4 * 3, full.VertexCount);   // corners shared between neighbouring cells
    }

    [Fact]
    public void Chunks_line_up_in_world_space()
    {
        MeshData chunk = WaterMesh.Build(32, 64, 2, 3, Cell, (i, j) => i - 33.5f, 0f);
        Assert.Equal(2 * 3 * 2, chunk.TriangleCount);   // column 33 is wet on every row, so both cells of each row are
        foreach (Vector3 p in chunk.Positions)
        {
            float i = p.X / Cell, j = p.Z / Cell;
            Assert.Equal(MathF.Round(i), i);
            Assert.Equal(MathF.Round(j), j);
            Assert.InRange(i, 32, 34);
            Assert.InRange(j, 64, 67);
        }
        Assert.All(chunk.Normals, n => Assert.Equal(Vector3.UnitY, n));
        Assert.Contains(chunk.Positions, p => p == new Vector3(32 * Cell, 0f, 64 * Cell));
    }
}
