using Tank.Core.Math;

namespace Tank.Core.World;

/// <summary>
/// Terrain at cell resolution (1 cell = 4px). Every mutation is appended to <see cref="Changes"/>
/// so the server can replicate it and tests can assert on it.
/// </summary>
public sealed class GameMap
{
    private readonly byte[] _cells;
    private readonly List<CellChange> _changes = new(256);

    public GameMap(int widthCells, int heightCells)
    {
        if (widthCells <= 0 || heightCells <= 0)
            throw new ArgumentOutOfRangeException(nameof(widthCells), "Map dimensions must be positive.");
        WidthCells = widthCells;
        HeightCells = heightCells;
        _cells = new byte[widthCells * heightCells];
    }

    public int WidthCells { get; }
    public int HeightCells { get; }
    public int WidthSub => WidthCells * Units.Cell;
    public int HeightSub => HeightCells * Units.Cell;
    public ReadOnlySpan<byte> Cells => _cells;
    public IReadOnlyList<CellChange> Changes => _changes;

    /// <summary>Incremented on every content change; lets renderers rebuild only when needed.</summary>
    public int Version { get; private set; }

    public bool InBounds(int cx, int cz) => (uint)cx < (uint)WidthCells && (uint)cz < (uint)HeightCells;

    public CellType Get(int cx, int cz) => InBounds(cx, cz) ? (CellType)_cells[cz * WidthCells + cx] : CellType.Steel;

    public void Set(int cx, int cz, CellType type)
    {
        if (!InBounds(cx, cz)) return;
        int index = cz * WidthCells + cx;
        if (_cells[index] == (byte)type) return;
        _cells[index] = (byte)type;
        _changes.Add(new CellChange(index, type));
        Version++;
    }

    /// <summary>Applies a replicated change without recording it (client side).</summary>
    public void Apply(CellChange change)
    {
        if ((uint)change.Index >= (uint)_cells.Length || _cells[change.Index] == (byte)change.Type) return;
        _cells[change.Index] = (byte)change.Type;
        Version++;
    }

    public void ClearChanges() => _changes.Clear();

    public void LoadCells(ReadOnlySpan<byte> cells)
    {
        if (cells.Length != _cells.Length)
            throw new ArgumentException($"Expected {_cells.Length} cells, got {cells.Length}.", nameof(cells));
        cells.CopyTo(_cells);
        _changes.Clear();
        Version++;
    }

    public GameMap Clone()
    {
        var copy = new GameMap(WidthCells, HeightCells);
        copy.LoadCells(_cells);
        return copy;
    }

    /// <summary>True when the box leaves the map or overlaps any cell that blocks tanks.</summary>
    public bool BlocksTank(in Box box)
    {
        if (box.X < 0 || box.Z < 0 || box.MaxX > WidthSub || box.MaxZ > HeightSub) return true;
        int cx0 = box.X / Units.Cell, cx1 = (box.MaxX - 1) / Units.Cell;
        int cz0 = box.Z / Units.Cell, cz1 = (box.MaxZ - 1) / Units.Cell;
        for (int cz = cz0; cz <= cz1; cz++)
        {
            int row = cz * WidthCells;
            for (int cx = cx0; cx <= cx1; cx++)
            {
                if (((CellType)_cells[row + cx]).BlocksTank()) return true;
            }
        }
        return false;
    }

    public CellType CellAt(int x, int z) =>
        Get(IntMath.FloorDiv(x, Units.Cell), IntMath.FloorDiv(z, Units.Cell));

    public uint ComputeChecksum()
    {
        // FNV-1a: cheap, deterministic, good enough to detect divergence.
        uint hash = 2166136261;
        foreach (byte b in _cells)
        {
            hash ^= b;
            hash *= 16777619;
        }
        return hash;
    }
}
