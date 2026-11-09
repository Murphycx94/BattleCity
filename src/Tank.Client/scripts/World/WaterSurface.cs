using Godot;
using Tank.Client.Art;
using Tank.Client.Art.Meshes;
using Tank.Sim.Maps;
using Tank.Sim.Terrain;

namespace Tank.Client.World;

/// <summary>
/// The cartoon water of a map with a water level (riverland design §6.3): flat <see cref="WaterMesh"/> sheets over the
/// wet cells only, in the terrain's 32×32-cell chunks (a dry chunk gets nothing), drawn opaque with the water shader
/// and casting no shadow. The sheets take the drawn ground's heights, so their depth-0 line is the drawn shore. The node
/// always joins <see cref="Group"/>, so other parts (splashes, cameras) can ask for the depth without being wired to it.
/// </summary>
public partial class WaterSurface : Node3D
{
    public const string Group = "water_surface";
    private const int ChunkCells = 32;   // the same chunks as TerrainMesh

    private HeightField? _terrain;

    /// <summary>The water surface's world height, or null on a map without water.</summary>
    public float? Level { get; private set; }

    public void Build(Battlefield field)
    {
        AddToGroup(Group);
        _terrain = field.Terrain;
        Level = field.WaterLevel;
        if (Level is not { } level) return;
        HeightField terrain = field.Terrain;
        ShaderMaterial material = Materials.FromShader("water");
        int cellsX = terrain.SamplesX - 1, cellsZ = terrain.SamplesZ - 1;
        for (int z0 = 0; z0 < cellsZ; z0 += ChunkCells)
        for (int x0 = 0; x0 < cellsX; x0 += ChunkCells)
        {
            MeshData chunk = WaterMesh.Build(x0, z0, Math.Min(ChunkCells, cellsX - x0), Math.Min(ChunkCells, cellsZ - z0), terrain.CellSize,
                (i, j) => SheetHeight(terrain, i * terrain.CellSize, j * terrain.CellSize), level);
            if (chunk.TriangleCount == 0) continue;
            AddChild(new MeshInstance3D
            {
                Mesh = chunk.ToArrayMesh(),
                MaterialOverride = material,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
        }
    }

    /// <summary>
    /// The ground height the sheet measures its depth from: the drawn height (<see cref="TerrainMesh.VisualHeightAt"/>),
    /// except where the drawn ground sinks below the real ground at the map edge: there the real ground decides, so a dry
    /// map edge sinking to the far-ground level doesn't come out as a rim of water around the map.
    /// </summary>
    private static float SheetHeight(HeightField terrain, float x, float z) =>
        MathF.Max(TerrainMesh.VisualHeightAt(terrain, x, z), terrain.HeightAt(x, z));

    /// <summary>Water depth over the simulated ground at (x, z): max(0, Level − height); 0 without water.</summary>
    public float DepthAt(float x, float z) =>
        Level is { } level && _terrain is { } terrain ? MathF.Max(0f, level - terrain.HeightAt(x, z)) : 0f;

    /// <summary>
    /// The scene's water surface, or null before one is built; one queued for deletion (the last match's, its scene going
    /// as the next one comes) doesn't count. Callers cache what they find and look again while it is null.
    /// </summary>
    public static WaterSurface? Find(Node anyNodeInTree)
    {
        SceneTree tree = anyNodeInTree.GetTree();
        if (tree.GetFirstNodeInGroup(Group) is WaterSurface first && !first.IsQueuedForDeletion()) return first;   // the usual case, no array
        foreach (Node node in tree.GetNodesInGroup(Group))
            if (node is WaterSurface water && !water.IsQueuedForDeletion()) return water;
        return null;
    }
}
