using System.Numerics;

namespace Tank.Client.Art.Meshes;

/// <summary>
/// Height-field patch with shared, smooth vertices. Sample (i, j) sits at (i·cell, height, j·cell) in world space, with
/// i and j global grid indices so neighbouring patches line up exactly. Diagonals alternate per cell so long slopes
/// don't streak; triangles face +Y.
/// </summary>
public static class GridMesh
{
    public static MeshData Build(int firstI, int firstJ, int cellsX, int cellsZ, float cellSize,
        Func<int, int, float> height, Func<int, int, Vector3> normal)
    {
        if (cellsX < 1) throw new ArgumentOutOfRangeException(nameof(cellsX), "Need at least one cell.");
        if (cellsZ < 1) throw new ArgumentOutOfRangeException(nameof(cellsZ), "Need at least one cell.");
        if (!(cellSize > 0)) throw new ArgumentOutOfRangeException(nameof(cellSize), "Cell size must be positive.");
        var b = new MeshBuilder();
        for (int j = 0; j <= cellsZ; j++)
        for (int i = 0; i <= cellsX; i++)
        {
            int gi = firstI + i, gj = firstJ + j;
            b.AddVertex(new Vector3(gi * cellSize, height(gi, gj), gj * cellSize), normal(gi, gj), new Vector2(gi * cellSize, gj * cellSize));
        }
        int stride = cellsX + 1;
        for (int j = 0; j < cellsZ; j++)
        for (int i = 0; i < cellsX; i++)
        {
            int a = j * stride + i, bb = a + 1, c = a + stride, d = c + 1; // a=(i,j) b=(i+1,j) c=(i,j+1) d=(i+1,j+1)
            if (((firstI + i + firstJ + j) & 1) == 0)
            {
                b.AddTriangle(a, d, bb);
                b.AddTriangle(a, c, d);
            }
            else
            {
                b.AddTriangle(a, c, bb);
                b.AddTriangle(bb, c, d);
            }
        }
        return b.Build();
    }
}
