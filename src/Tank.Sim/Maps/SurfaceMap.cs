namespace Tank.Sim.Maps;

/// <summary>
/// Immutable grid of <see cref="SurfaceKind"/>, one per height-field cell: cell (i, j) covers
/// x ∈ [i·CellSize, (i+1)·CellSize), z likewise (riverland design §2.1). Points off the grid take the nearest edge cell.
/// </summary>
public sealed class SurfaceMap
{
    /// <summary>The highest kind there is; anything above it is refused (it may come off the wire).</summary>
    public const SurfaceKind LastKind = SurfaceKind.Road;

    private readonly SurfaceKind[] _cells;

    /// <param name="cells">Row-major, <c>j * cellsX + i</c>; copied.</param>
    public SurfaceMap(int cellsX, int cellsZ, float cellSize, SurfaceKind[] cells)
    {
        if (cellsX < 1 || cellsZ < 1) throw new ArgumentException($"A surface map needs at least 1 × 1 cells, got {cellsX} × {cellsZ}.");
        if (!(cellSize > 0f) || !float.IsFinite(cellSize)) throw new ArgumentException($"Cell size must be positive and finite, got {cellSize}.", nameof(cellSize));
        if (cells.Length != (long)cellsX * cellsZ)
            throw new ArgumentException($"Expected {(long)cellsX * cellsZ} cells, got {cells.Length}.", nameof(cells));
        for (int k = 0; k < cells.Length; k++)
            if (cells[k] > LastKind) throw new ArgumentException($"Cell {k} has unknown kind {(byte)cells[k]}.", nameof(cells));
        (CellsX, CellsZ, CellSize) = (cellsX, cellsZ, cellSize);
        _cells = (SurfaceKind[])cells.Clone();
    }

    /// <summary>A map that is <paramref name="kind"/> everywhere.</summary>
    public static SurfaceMap Uniform(int cellsX, int cellsZ, float cellSize, SurfaceKind kind = SurfaceKind.Grass)
    {
        var cells = new SurfaceKind[System.Math.Max(0, cellsX) * System.Math.Max(0, cellsZ)];
        Array.Fill(cells, kind);
        return new SurfaceMap(cellsX, cellsZ, cellSize, cells);
    }

    public int CellsX { get; }
    public int CellsZ { get; }
    public float CellSize { get; }

    /// <summary>Row-major: cell (i, j) at <c>j * CellsX + i</c>.</summary>
    public ReadOnlySpan<SurfaceKind> Cells => _cells;

    /// <summary>Cell (<paramref name="i"/>, <paramref name="j"/>), each clamped to the grid.</summary>
    public SurfaceKind this[int i, int j] =>
        _cells[System.Math.Clamp(j, 0, CellsZ - 1) * CellsX + System.Math.Clamp(i, 0, CellsX - 1)];

    /// <summary>The kind of the cell under (<paramref name="x"/>, <paramref name="z"/>), clamped to the grid.</summary>
    public SurfaceKind At(float x, float z) => this[Index(x, CellsX), Index(z, CellsZ)];

    /// <summary>Whether every cell is <paramref name="kind"/>.</summary>
    public bool IsUniform(SurfaceKind kind)
    {
        foreach (SurfaceKind k in _cells)
            if (k != kind) return false;
        return true;
    }

    /// <summary>
    /// The cell along one axis, clamped to the grid before the cast so a huge or infinite coordinate can't overflow it.
    /// NaN would slip through a clamp (and casting it to int is platform-defined), so it takes the first cell explicitly.
    /// </summary>
    private int Index(float v, int cells)
    {
        float cell = MathF.Floor(v / CellSize);
        if (!(cell >= 0f)) return 0;   // below the grid, −∞ or NaN
        return (int)MathF.Min(cell, cells - 1);
    }
}
