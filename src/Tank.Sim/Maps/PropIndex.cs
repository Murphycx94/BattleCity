using System.Numerics;

namespace Tank.Sim.Maps;

/// <summary>The groups of props the simulation queries (riverland design §4.1).</summary>
internal enum PropSet : byte { Blockers, ShellStoppers, Flimsy, Walls, Foliage }

/// <summary>
/// A uniform grid over the map telling, per <see cref="PropSet"/>, which props might be near a point or a segment
/// (riverland design §4.1), so a query looks at a few dozen props instead of thousands. Each prop is registered by a
/// circle round its footprint's centre that holds everything the set's exact tests can touch (<see cref="Reach"/>), in
/// every cell of the circle's bounding square; what lies off the map is put in the edge cells. Built once with the
/// battlefield and never changed (the props' shapes don't change, only their state), so queries are safe from many
/// threads. A query copies the candidates of the cells it covers into the caller's buffer, sorts them and drops
/// duplicates: ascending prop indices, a superset of what the exact tests would find, so callers testing them in that
/// order do exactly what a scan of the whole set would.
/// </summary>
internal sealed class PropIndex
{
    /// <summary>
    /// Added to every query's reach, so that float rounding where a circle just touches a cell edge never loses a
    /// prop (returning one more is harmless).
    /// </summary>
    public const float Slack = 0.01f;

    private static readonly PropSet[] Sets = Enum.GetValues<PropSet>();

    private readonly float _cellSize;
    private readonly int _cellsX;
    private readonly int _cellsZ;
    private readonly int[][] _all = new int[Sets.Length][];
    /// <summary>Per set, CSR over the cells (row-major, j * cellsX + i): cell c's props are items[cellStart[c] .. cellStart[c + 1]).</summary>
    private readonly int[][] _cellStart = new int[Sets.Length][];
    private readonly int[][] _items = new int[Sets.Length][];

    public PropIndex(ReadOnlySpan<Prop> props, float sizeX, float sizeZ, float cellSize)
    {
        _cellSize = cellSize;
        _cellsX = System.Math.Max(1, (int)MathF.Ceiling(sizeX / cellSize));
        _cellsZ = System.Math.Max(1, (int)MathF.Ceiling(sizeZ / cellSize));
        foreach (PropSet set in Sets) Build(set, props);
    }

    /// <summary>Whether <paramref name="set"/> holds <paramref name="prop"/>.</summary>
    public static bool Holds(PropSet set, in Prop prop) => set switch
    {
        PropSet.Blockers => prop.IsSolid,
        PropSet.ShellStoppers => prop.StopsShells,
        PropSet.Flimsy => prop.IsFlimsy,
        PropSet.Walls => prop.IsWall,
        PropSet.Foliage => prop.IsFoliage,
        _ => throw new ArgumentOutOfRangeException(nameof(set), set, null),
    };

    /// <summary>
    /// The radius <paramref name="prop"/> is registered by in <paramref name="set"/>, round its footprint's centre: the
    /// footprint's circumcircle, the rubble's for walls (what lies on the ground once they fall), and for foliage the
    /// circle that hides (<see cref="Prop.FoliageRadius"/>: a bush's own, a tree's crown).
    /// </summary>
    public static float Reach(PropSet set, in Prop prop) => set switch
    {
        PropSet.Walls => prop.RubbleFootprint.HalfExtents.Length(),
        PropSet.Foliage => prop.FoliageRadius,
        _ => prop.Footprint.HalfExtents.Length(),
    };

    /// <summary>Ascending indices of the props in <paramref name="set"/>.</summary>
    public ReadOnlySpan<int> All(PropSet set) => _all[(int)set];

    /// <summary>
    /// Ascending, distinct indices of the props of <paramref name="set"/> whose registered circle may reach the disc
    /// (<paramref name="center"/>, <paramref name="radius"/>); <see cref="All"/> if <paramref name="buffer"/> can't hold them.
    /// </summary>
    public ReadOnlySpan<int> Near(PropSet set, Vector2 center, float radius, Span<int> buffer)
    {
        float reach = radius + Slack;
        int i0 = Column(center.X - reach), i1 = Column(center.X + reach);
        int j0 = Row(center.Y - reach), j1 = Row(center.Y + reach);
        int count = 0;
        for (int j = j0; j <= j1; j++)
            if (!Gather(set, j, i0, i1, buffer, ref count)) return All(set);
        return Distinct(buffer[..count]);
    }

    /// <summary>
    /// The same for the segment <paramref name="from"/> → <paramref name="to"/> widened by <paramref name="margin"/>: row
    /// by row, the cells under the part of the segment within the margin of that row, widened by the margin.
    /// </summary>
    public ReadOnlySpan<int> Along(PropSet set, Vector2 from, Vector2 to, float margin, Span<int> buffer)
    {
        float reach = margin + Slack;
        Vector2 d = to - from;
        int j0 = Row(MathF.Min(from.Y, to.Y) - reach), j1 = Row(MathF.Max(from.Y, to.Y) + reach);
        int count = 0;
        for (int j = j0; j <= j1; j++)
        {
            // The row's band of z (unbounded beyond the map's edge rows, which also hold what lies off the map).
            float low = j == 0 ? float.NegativeInfinity : j * _cellSize - reach;
            float high = j == _cellsZ - 1 ? float.PositiveInfinity : (j + 1) * _cellSize + reach;
            float t0 = 0f, t1 = 1f;
            if (d.Y != 0)
            {
                float a = (low - from.Y) / d.Y, b = (high - from.Y) / d.Y;
                t0 = MathF.Max(t0, MathF.Min(a, b));
                t1 = MathF.Min(t1, MathF.Max(a, b));
                if (t0 > t1) continue;
            }
            float x0 = from.X + d.X * t0, x1 = from.X + d.X * t1;
            if (!Gather(set, j, Column(MathF.Min(x0, x1) - reach), Column(MathF.Max(x0, x1) + reach), buffer, ref count)) return All(set);
        }
        return Distinct(buffer[..count]);
    }

    private void Build(PropSet set, ReadOnlySpan<Prop> props)
    {
        var all = new List<int>();
        for (int i = 0; i < props.Length; i++)
            if (Holds(set, props[i])) all.Add(i);
        int[] cellStart = new int[_cellsX * _cellsZ + 1];
        foreach (int i in all) ForEachCell(props[i], set, cell => cellStart[cell + 1]++);
        for (int c = 0; c < _cellsX * _cellsZ; c++) cellStart[c + 1] += cellStart[c];
        int[] items = new int[cellStart[^1]];
        int[] next = cellStart[..^1];
        foreach (int i in all) ForEachCell(props[i], set, cell => items[next[cell]++] = i);   // ascending within every cell
        _all[(int)set] = all.ToArray();
        _cellStart[(int)set] = cellStart;
        _items[(int)set] = items;
    }

    private void ForEachCell(in Prop prop, PropSet set, Action<int> visit)
    {
        Vector2 c = prop.Footprint.Center;
        float reach = Reach(set, prop);
        int i0 = Column(c.X - reach), i1 = Column(c.X + reach);
        for (int j = Row(c.Y - reach); j <= Row(c.Y + reach); j++)
        for (int i = i0; i <= i1; i++)
            visit(j * _cellsX + i);
    }

    /// <summary>Appends the props of cells i0 … i1 of row j (contiguous in the CSR); false if they don't fit.</summary>
    private bool Gather(PropSet set, int j, int i0, int i1, Span<int> buffer, ref int count)
    {
        int[] cellStart = _cellStart[(int)set];
        int start = cellStart[j * _cellsX + i0], end = cellStart[j * _cellsX + i1 + 1];
        if (count + (end - start) > buffer.Length) return false;
        _items[(int)set].AsSpan(start, end - start).CopyTo(buffer[count..]);
        count += end - start;
        return true;
    }

    private static ReadOnlySpan<int> Distinct(Span<int> found)
    {
        found.Sort();
        int n = 0;
        foreach (int i in found)
            if (n == 0 || i != found[n - 1]) found[n++] = i;
        return found[..n];
    }

    private int Column(float x) => Cell(x, _cellsX);

    private int Row(float z) => Cell(z, _cellsZ);

    /// <summary>The cell holding coordinate <paramref name="v"/>, clamped to the grid (NaN goes to 0).</summary>
    private int Cell(float v, int cells)
    {
        float cell = MathF.Floor(v / _cellSize);
        return cell >= 0 ? (cell < cells - 1 ? (int)cell : cells - 1) : 0;
    }
}
