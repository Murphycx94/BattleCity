using Godot;
using Tank.Sim.Terrain;

namespace Tank.Client.World;

/// <summary>
/// Flat-shaded low-poly ground built from the sim's height field. Split into chunks so the renderer can cull
/// what is off screen. Colour comes from slope (grass → dirt → rock) with a little per-cell variation.
/// </summary>
public partial class TerrainMesh : Node3D
{
    private const int ChunkCells = 32;
    private const float RockBelowUp = 0.82f;  // steeper than ~35°
    private const float DirtBelowUp = 0.94f;  // steeper than ~20°
    private const float DryAtHeight = 30f;

    private static readonly Color Grass = new(0.36f, 0.47f, 0.24f);
    private static readonly Color DryGrass = new(0.52f, 0.52f, 0.30f);
    private static readonly Color Dirt = new(0.47f, 0.38f, 0.26f);
    private static readonly Color Rock = new(0.46f, 0.45f, 0.43f);

    public void Build(HeightField field)
    {
        var material = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            Roughness = 1f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        int cellsX = field.SamplesX - 1, cellsZ = field.SamplesZ - 1;
        for (int z0 = 0; z0 < cellsZ; z0 += ChunkCells)
        for (int x0 = 0; x0 < cellsX; x0 += ChunkCells)
        {
            var chunk = BuildChunk(field, x0, z0, Math.Min(ChunkCells, cellsX - x0), Math.Min(ChunkCells, cellsZ - z0));
            AddChild(new MeshInstance3D { Mesh = chunk, MaterialOverride = material });
        }
    }

    private static ArrayMesh BuildChunk(HeightField field, int x0, int z0, int width, int depth)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        for (int j = z0; j < z0 + depth; j++)
        for (int i = x0; i < x0 + width; i++)
        {
            Vector3 a = Point(field, i, j), b = Point(field, i + 1, j), c = Point(field, i, j + 1), d = Point(field, i + 1, j + 1);
            // Alternate the diagonal so long slopes don't streak in one direction.
            if (((i + j) & 1) == 0)
            {
                AddTriangle(st, a, b, d, i, j);
                AddTriangle(st, a, d, c, i, j);
            }
            else
            {
                AddTriangle(st, a, b, c, i, j);
                AddTriangle(st, b, d, c, i, j);
            }
        }
        return st.Commit();
    }

    private static Vector3 Point(HeightField field, int i, int j) => new(i * field.CellSize, field.Sample(i, j), j * field.CellSize);

    private static void AddTriangle(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, int i, int j)
    {
        Vector3 normal = (b - a).Cross(c - a).Normalized();
        if (normal.Y < 0) normal = -normal;
        st.SetNormal(normal);
        st.SetColor(Shade(normal.Y, (a.Y + b.Y + c.Y) / 3f, i, j));
        st.AddVertex(a);
        st.AddVertex(b);
        st.AddVertex(c);
    }

    private static Color Shade(float up, float height, int i, int j)
    {
        if (up < RockBelowUp) return Rock;
        if (up < DirtBelowUp) return Dirt;
        float dryness = Hash(i, j) * 0.4f + Mathf.Clamp(height / DryAtHeight, 0f, 0.6f);
        return Grass.Lerp(DryGrass, dryness);
    }

    private static float Hash(int i, int j)
    {
        unchecked
        {
            uint h = (uint)i * 73856093u ^ (uint)j * 19349663u;
            h ^= h >> 13;
            h *= 0x5bd1e995u;
            h ^= h >> 15;
            return (h & 0xFFFF) / 65535f;
        }
    }
}
