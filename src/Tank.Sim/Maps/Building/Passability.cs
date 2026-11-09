using System.Numerics;
using Tank.Sim.Terrain;

namespace Tank.Sim.Maps.Building;

/// <summary>
/// Where a tank can drive, cell by cell (riverland design §9), and which cells reach which. The cells are the terrain's
/// (cell (i, j) covers x ∈ [i·CellSize, (i+1)·CellSize), z likewise) and each is judged at its centre: it is blocked when
/// the ground there is steeper than the climb limit, when it is outside the playable area, or when it lies in the
/// footprint of a standing rock, house or wall grown by the clearance on every side. Trees, water, fences and bushes
/// don't block. Cells connect to their four side neighbours. Everything is worked out once, here; afterwards it is only
/// read. The stage 5C navigation grid (4 m) is built separately.
/// </summary>
public sealed class Passability
{
    private readonly int _cellsX, _cellsZ;
    private readonly float _cellSize;
    private readonly int[] _component;   // per cell: −1 blocked, else the number of its connected piece (0, 1, … in row-major order of first cell)

    /// <param name="maxSlopeRadians">The steepest passable ground (riverland: the vehicles' smallest climb limit, 32°).</param>
    /// <param name="clearance">How far around a rock's, house's or wall's footprint is blocked too, m (riverland: 1.6 m).</param>
    public Passability(Battlefield field, float maxSlopeRadians, float clearance)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (!float.IsFinite(maxSlopeRadians) || maxSlopeRadians <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxSlopeRadians), maxSlopeRadians, "The slope limit must be finite and positive.");
        if (!float.IsFinite(clearance) || clearance < 0) throw new ArgumentOutOfRangeException(nameof(clearance), clearance, "Clearance must be finite and not negative.");
        HeightField terrain = field.Terrain;
        _cellsX = terrain.SamplesX - 1;
        _cellsZ = terrain.SamplesZ - 1;
        _cellSize = terrain.CellSize;
        var open = new bool[_cellsX * _cellsZ];
        MarkGround(field, MathF.Cos(maxSlopeRadians), open);
        foreach (Prop prop in field.Props)
            if (prop.StopsShells && field.IsStanding(prop.Id)) Block(prop, clearance, open);
        _component = Label(open);
    }

    /// <summary>Whether cell (<paramref name="i"/>, <paramref name="j"/>) can be driven on; false outside the grid.</summary>
    public bool IsPassable(int i, int j) => i >= 0 && i < _cellsX && j >= 0 && j < _cellsZ && _component[j * _cellsX + i] >= 0;

    /// <summary>Whether the cell under <paramref name="p"/> (x, z) can be driven on; false off the grid.</summary>
    public bool IsPassableAt(Vector2 p) => ComponentAt(p) >= 0;

    /// <summary>The number of the connected piece the cell under <paramref name="p"/> belongs to; −1 for a blocked cell or off the grid.</summary>
    public int ComponentAt(Vector2 p)
    {
        if (!float.IsFinite(p.X) || !float.IsFinite(p.Y)) return -1;
        float fi = MathF.Floor(p.X / _cellSize), fj = MathF.Floor(p.Y / _cellSize);
        if (fi < 0 || fi >= _cellsX || fj < 0 || fj >= _cellsZ) return -1;
        return _component[(int)fj * _cellsX + (int)fi];
    }

    /// <summary>Whether a tank can drive from <paramref name="a"/> to <paramref name="b"/>: both cells passable and in the same piece.</summary>
    public bool Connected(Vector2 a, Vector2 b)
    {
        int piece = ComponentAt(a);
        return piece >= 0 && piece == ComponentAt(b);
    }

    /// <summary>Opens every cell whose centre is in the playable area on ground no steeper than the limit.</summary>
    private void MarkGround(Battlefield field, float minNormalY, bool[] open)
    {
        float low = field.Border, highX = field.Terrain.SizeX - field.Border, highZ = field.Terrain.SizeZ - field.Border;
        for (int j = 0; j < _cellsZ; j++)
        {
            float z = (j + 0.5f) * _cellSize;
            if (z < low || z > highZ) continue;
            for (int i = 0; i < _cellsX; i++)
            {
                float x = (i + 0.5f) * _cellSize;
                if (x < low || x > highX) continue;
                open[j * _cellsX + i] = field.Terrain.NormalAt(x, z).Y >= minNormalY;
            }
        }
    }

    /// <summary>Closes every cell whose centre lies in <paramref name="prop"/>'s footprint grown by <paramref name="clearance"/>.</summary>
    private void Block(in Prop prop, float clearance, bool[] open)
    {
        Vector2 center = prop.Footprint.Center, right = prop.Footprint.AxisRight, forward = prop.Footprint.AxisForward;
        Vector2 half = prop.Footprint.HalfExtents + new Vector2(clearance, clearance);
        float reachX = half.X * MathF.Abs(right.X) + half.Y * MathF.Abs(forward.X);
        float reachZ = half.X * MathF.Abs(right.Y) + half.Y * MathF.Abs(forward.Y);
        // Cells whose centres (k + 0.5)·cell fall within the bounding box.
        int i0 = Math.Max(0, (int)MathF.Ceiling((center.X - reachX) / _cellSize - 0.5f));
        int i1 = Math.Min(_cellsX - 1, (int)MathF.Floor((center.X + reachX) / _cellSize - 0.5f));
        int j0 = Math.Max(0, (int)MathF.Ceiling((center.Y - reachZ) / _cellSize - 0.5f));
        int j1 = Math.Min(_cellsZ - 1, (int)MathF.Floor((center.Y + reachZ) / _cellSize - 0.5f));
        for (int j = j0; j <= j1; j++)
        for (int i = i0; i <= i1; i++)
        {
            Vector2 d = new Vector2((i + 0.5f) * _cellSize, (j + 0.5f) * _cellSize) - center;
            if (MathF.Abs(Vector2.Dot(d, right)) <= half.X && MathF.Abs(Vector2.Dot(d, forward)) <= half.Y)
                open[j * _cellsX + i] = false;
        }
    }

    /// <summary>Numbers the 4-connected pieces of open cells by flood fill, in row-major order of their first cell.</summary>
    private int[] Label(bool[] open)
    {
        var component = new int[open.Length];
        Array.Fill(component, -1);
        var queue = new int[open.Length];
        int pieces = 0;
        for (int start = 0; start < open.Length; start++)
        {
            if (!open[start] || component[start] >= 0) continue;
            int piece = pieces++, head = 0, tail = 0;
            component[start] = piece;
            queue[tail++] = start;
            while (head < tail)
            {
                int at = queue[head++], i = at % _cellsX;
                if (i > 0) Visit(at - 1);
                if (i < _cellsX - 1) Visit(at + 1);
                if (at >= _cellsX) Visit(at - _cellsX);
                if (at + _cellsX < open.Length) Visit(at + _cellsX);
            }

            void Visit(int cell)
            {
                if (!open[cell] || component[cell] >= 0) return;
                component[cell] = piece;
                queue[tail++] = cell;
            }
        }
        return component;
    }
}
