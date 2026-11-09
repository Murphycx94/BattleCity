using Godot;
using Tank.Client.Art;
using Tank.Client.Art.Meshes;
using Tank.Sim.Terrain;

namespace Tank.Client.World;

/// <summary>
/// Smooth ground built from the sim's height field: same vertices as the sim, per-vertex normals from
/// <see cref="HeightField.NormalAt"/>, split into 32×32-cell chunks for culling, drawn with the cartoon terrain shader.
/// A large plane below the map edge carries the far-ground shader (same grass colour) out to the horizon.
/// </summary>
public partial class TerrainMesh : Node3D
{
    public const float FarGroundY = -0.3f;
    private const int ChunkCells = 32;
    private const float FarGroundSize = 4000f;
    private const float EdgeFadeMetres = 8f; // outside the 10 m playable border, so purely visual; terrain_toon fades its wet band over the same 8 m

    /// <summary>Weight of the real terrain: 0 on the map edge, 1 from <see cref="EdgeFadeMetres"/> inwards.</summary>
    private static float EdgeWeight(HeightField field, float x, float z)
    {
        float distance = MathF.Min(MathF.Min(x, field.SizeX - x), MathF.Min(z, field.SizeZ - z));
        float t = Math.Clamp(distance / EdgeFadeMetres, 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    /// <summary>
    /// Height of the ground as drawn: equal to <see cref="HeightField.HeightAt"/> inside the map and eased down to the
    /// far-ground level over the outer <see cref="EdgeFadeMetres"/>. Use this (not HeightAt) to place visual-only
    /// things such as grass so they follow the mesh at the map edge.
    /// </summary>
    public static float VisualHeightAt(HeightField field, float x, float z) =>
        FarGroundY + (field.HeightAt(x, z) - FarGroundY) * EdgeWeight(field, x, z);

    /// <summary>Terrain height at a grid vertex, sinking to the far-ground level at the map edge so there is no visible step.</summary>
    private static float EdgeHeight(HeightField field, int i, int j) =>
        VisualHeightAt(field, i * field.CellSize, j * field.CellSize);

    /// <summary>Normal of the eased surface from central differences of <see cref="VisualHeightAt"/>.</summary>
    private static System.Numerics.Vector3 EdgeNormal(HeightField field, int i, int j)
    {
        float x = i * field.CellSize, z = j * field.CellSize, d = field.CellSize;
        float dx = (VisualHeightAt(field, x + d, z) - VisualHeightAt(field, x - d, z)) / (2 * d);
        float dz = (VisualHeightAt(field, x, z + d) - VisualHeightAt(field, x, z - d)) / (2 * d);
        return System.Numerics.Vector3.Normalize(new System.Numerics.Vector3(-dx, 1f, -dz));
    }

    /// <param name="dirt">The dirt mask; a layered one (a map with ground kinds) switches the shader to its layered mode.</param>
    /// <param name="waterLevel">The map's water surface, for the riverbed and the wet band along the shore (layered mode only).</param>
    public void Build(HeightField field, DirtMask dirt, float? waterLevel = null)
    {
        ShaderMaterial material = Materials.FromShader("terrain_toon")
            .With("dirt_mask", dirt.ToTexture())
            .With("map_size", new Vector2(field.SizeX, field.SizeZ))
            .With("grass_max_slope", GroundCover.GrassMaxSlopeDegrees)   // one source of truth with the grass scatter rules
            .With("rock_min_slope", GroundCover.RockMinSlopeDegrees);
        if (dirt.IsLayered)
        {
            material.With("layered", true);
            if (waterLevel is { } level) material.With("water_level", level);
        }
        int cellsX = field.SamplesX - 1, cellsZ = field.SamplesZ - 1;
        for (int z0 = 0; z0 < cellsZ; z0 += ChunkCells)
        for (int x0 = 0; x0 < cellsX; x0 += ChunkCells)
        {
            MeshData chunk = GridMesh.Build(x0, z0, Math.Min(ChunkCells, cellsX - x0), Math.Min(ChunkCells, cellsZ - z0), field.CellSize,
                (i, j) => EdgeHeight(field, i, j), (i, j) => EdgeNormal(field, i, j));
            AddChild(new MeshInstance3D { Mesh = chunk.ToArrayMesh(), MaterialOverride = material });
        }
        AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(FarGroundSize, FarGroundSize) },
            Position = new Vector3(field.SizeX / 2f, FarGroundY, field.SizeZ / 2f),
            MaterialOverride = Materials.FromShader("far_ground"),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }
}
