using System.Numerics;

namespace Tank.Client.Art.Meshes;

/// <summary>
/// Flat water sheet over the wet cells of a height-field patch (riverland design §6.3). Only cells with a corner below
/// the level are built; every vertex sits at the level with its normal up, and its UV.x carries the signed depth
/// (level − ground height: negative on dry corners). The triangles split each cell along the same diagonal as
/// <see cref="GridMesh"/>, so the depth interpolates linearly across exactly the triangles the terrain is drawn with and
/// its zero line is the drawn shoreline: the shader discards where the depth is negative, no depth texture needed.
/// Sample (i, j) sits at (i·cell, level, j·cell) with global indices, so chunks line up like the terrain's.
/// </summary>
public static class WaterMesh
{
    public static MeshData Build(int firstI, int firstJ, int cellsX, int cellsZ, float cellSize, Func<int, int, float> height, float level)
    {
        if (cellsX < 1) throw new ArgumentOutOfRangeException(nameof(cellsX), "Need at least one cell.");
        if (cellsZ < 1) throw new ArgumentOutOfRangeException(nameof(cellsZ), "Need at least one cell.");
        if (!(cellSize > 0)) throw new ArgumentOutOfRangeException(nameof(cellSize), "Cell size must be positive.");
        int stride = cellsX + 1;
        var depth = new float[stride * (cellsZ + 1)];
        for (int j = 0; j <= cellsZ; j++)
        for (int i = 0; i <= cellsX; i++)
            depth[j * stride + i] = level - height(firstI + i, firstJ + j);

        var b = new MeshBuilder();
        var vertex = new int[depth.Length];
        Array.Fill(vertex, -1);
        int Corner(int k)
        {
            if (vertex[k] < 0)
            {
                int gi = firstI + k % stride, gj = firstJ + k / stride;
                vertex[k] = b.AddVertex(new Vector3(gi * cellSize, level, gj * cellSize), Vector3.UnitY, new Vector2(depth[k], 0f));
            }
            return vertex[k];
        }
        for (int j = 0; j < cellsZ; j++)
        for (int i = 0; i < cellsX; i++)
        {
            int a = j * stride + i, bb = a + 1, c = a + stride, d = c + 1; // a=(i,j) b=(i+1,j) c=(i,j+1) d=(i+1,j+1)
            if (!(depth[a] > 0 || depth[bb] > 0 || depth[c] > 0 || depth[d] > 0)) continue;
            int va = Corner(a), vb = Corner(bb), vc = Corner(c), vd = Corner(d);
            if (((firstI + i + firstJ + j) & 1) == 0)   // the same split as GridMesh
            {
                b.AddTriangle(va, vd, vb);
                b.AddTriangle(va, vc, vd);
            }
            else
            {
                b.AddTriangle(va, vc, vb);
                b.AddTriangle(vb, vc, vd);
            }
        }
        return b.VertexCount == 0 ? MeshData.Empty : b.Build();
    }
}
