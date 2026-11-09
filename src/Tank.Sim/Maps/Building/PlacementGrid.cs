using System.Numerics;

namespace Tank.Sim.Maps.Building;

/// <summary>
/// Circles on the ground in a uniform grid of square cells over [0, sizeX] × [0, sizeZ], for the neighbour checks of
/// scattering (riverland design §3.12). A circle is listed in every cell its bounding square touches (clamped to the
/// edge cells), so a query only looks at the cells its own reach touches.
/// </summary>
public sealed class PlacementGrid
{
    private readonly float _cellSize;
    private readonly int _cellsX, _cellsZ;
    private readonly List<int>?[] _cells;
    private readonly List<Vector2> _centers = new();
    private readonly List<float> _radii = new();
    private float _largest;

    public PlacementGrid(float sizeX, float sizeZ, float cellSize)
    {
        if (!float.IsFinite(sizeX) || sizeX <= 0 || !float.IsFinite(sizeZ) || sizeZ <= 0)
            throw new ArgumentOutOfRangeException(nameof(sizeX), $"Size {sizeX} × {sizeZ} must be finite and positive.");
        if (!float.IsFinite(cellSize) || cellSize <= 0) throw new ArgumentOutOfRangeException(nameof(cellSize), cellSize, "Cell size must be finite and positive.");
        _cellSize = cellSize;
        _cellsX = Math.Max(1, (int)MathF.Ceiling(sizeX / cellSize));
        _cellsZ = Math.Max(1, (int)MathF.Ceiling(sizeZ / cellSize));
        _cells = new List<int>?[_cellsX * _cellsZ];
    }

    /// <summary>How many circles were added.</summary>
    public int Count => _centers.Count;

    public void Add(Vector2 center, float radius)
    {
        if (!float.IsFinite(center.X) || !float.IsFinite(center.Y)) throw new ArgumentOutOfRangeException(nameof(center), center, "Center must be finite.");
        if (!float.IsFinite(radius) || radius < 0) throw new ArgumentOutOfRangeException(nameof(radius), radius, "Radius must be finite and not negative.");
        int index = _centers.Count;
        _centers.Add(center);
        _radii.Add(radius);
        _largest = MathF.Max(_largest, radius);
        CellRange(center, radius, out int i0, out int i1, out int j0, out int j1);
        for (int j = j0; j <= j1; j++)
        for (int i = i0; i <= i1; i++)
            (_cells[j * _cellsX + i] ??= new List<int>()).Add(index);
    }

    /// <summary>
    /// Whether some added circle comes closer than <paramref name="gap"/> to the circle (<paramref name="center"/>,
    /// <paramref name="radius"/>): its centre is nearer than <paramref name="radius"/> + its radius + <paramref name="gap"/>.
    /// </summary>
    public bool AnyWithin(Vector2 center, float radius, float gap)
    {
        float reach = radius + gap;
        if (!float.IsFinite(reach) || !float.IsFinite(center.X) || !float.IsFinite(center.Y))
            throw new ArgumentOutOfRangeException(nameof(center), $"Query ({center}, {radius}, gap {gap}) must be finite.");
        if (reach + _largest <= 0) return false;
        // Each circle is listed wherever its own square reaches, so looking where the query's reach goes finds it.
        CellRange(center, MathF.Max(reach, 0f), out int i0, out int i1, out int j0, out int j1);
        for (int j = j0; j <= j1; j++)
        for (int i = i0; i <= i1; i++)
        {
            List<int>? cell = _cells[j * _cellsX + i];
            if (cell is null) continue;
            foreach (int k in cell)
            {
                float limit = reach + _radii[k];
                if (limit > 0 && Vector2.DistanceSquared(center, _centers[k]) < limit * limit) return true;
            }
        }
        return false;
    }

    private void CellRange(Vector2 center, float reach, out int i0, out int i1, out int j0, out int j1)
    {
        i0 = Cell(center.X - reach, _cellsX);
        i1 = Cell(center.X + reach, _cellsX);
        j0 = Cell(center.Y - reach, _cellsZ);
        j1 = Cell(center.Y + reach, _cellsZ);
    }

    private int Cell(float coordinate, int cells) => (int)Math.Clamp(MathF.Floor(coordinate / _cellSize), 0f, cells - 1);
}
