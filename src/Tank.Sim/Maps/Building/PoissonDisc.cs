using System.Numerics;
using Tank.Sim.Geometry;

namespace Tank.Sim.Maps.Building;

/// <summary>
/// Poisson-disc sampling (Bridson's algorithm) with a spacing that may vary with position: points as dense as their
/// spacing allows, none two closer than the larger of their spacings. Deterministic for a given generator state; no
/// trigonometry (directions come from rejection sampling), so it reproduces exactly.
/// </summary>
public static class PoissonDisc
{
    /// <summary>The spacing's lower bound is estimated from this many × this many evenly spread probes.</summary>
    private const int Probes = 9;

    /// <summary>The background grid is made coarser rather than have more cells than this.</summary>
    private const int MaxCells = 1 << 22;

    /// <summary>
    /// Points in [<paramref name="min"/>, <paramref name="max"/>) on both axes, in the order they were placed. A point p
    /// is kept only where <paramref name="accept"/>(p) holds, and only if every other point q is at least
    /// max(<paramref name="minDistance"/>(p), <paramref name="minDistance"/>(q)) away. New points are tried in the ring
    /// from one to two spacings around a random active point, <paramref name="attempts"/> times before it retires; when
    /// none is active, the area is swept cell by cell (one jittered try per empty cell) for a seed to grow from again,
    /// so every accepted patch gets filled, even ones cut off from the rest.
    /// </summary>
    /// <param name="minDistance">The spacing around a point, m: finite and positive wherever it is asked.</param>
    /// <param name="accept">Where points may go; a rejected point is neither kept nor grown from.</param>
    public static List<Vector2> Sample(ref XorShift rng, Vector2 min, Vector2 max, Func<Vector2, float> minDistance,
        Func<Vector2, bool> accept, int attempts = 30)
    {
        ArgumentNullException.ThrowIfNull(minDistance);
        ArgumentNullException.ThrowIfNull(accept);
        if (!float.IsFinite(min.X) || !float.IsFinite(min.Y) || !float.IsFinite(max.X) || !float.IsFinite(max.Y) || max.X <= min.X || max.Y <= min.Y)
            throw new ArgumentException($"The area {min} … {max} must be finite and not empty.", nameof(max));
        if (attempts < 1) throw new ArgumentOutOfRangeException(nameof(attempts), attempts, "Need at least one attempt.");

        var sampler = new Sampler(min, max, minDistance, accept, LowestSpacing(min, max, minDistance));
        for (int cell = 0; cell < sampler.CellCount; cell++)
        {
            if (sampler.IsOccupied(cell)) continue;
            if (!sampler.TryPlace(sampler.Jittered(cell, ref rng))) continue;
            while (sampler.ActiveCount > 0) sampler.Grow(ref rng, attempts);
        }
        return sampler.Points;
    }

    /// <summary>The smallest spacing among evenly spread probes: the background grid's cells are this / √2 wide.</summary>
    private static float LowestSpacing(Vector2 min, Vector2 max, Func<Vector2, float> minDistance)
    {
        float lowest = float.PositiveInfinity;
        for (int b = 0; b < Probes; b++)
        for (int a = 0; a < Probes; a++)
            lowest = MathF.Min(lowest, Spacing(minDistance, min + (max - min) * new Vector2(a, b) / (Probes - 1)));
        return lowest;
    }

    private static float Spacing(Func<Vector2, float> minDistance, Vector2 p)
    {
        float r = minDistance(p);
        if (!float.IsFinite(r) || r <= 0) throw new ArgumentException($"Spacing at {p} must be finite and positive, got {r}.", nameof(minDistance));
        return r;
    }

    /// <summary>The state of one run: points, their spacings, a grid of linked lists over them, and the active list.</summary>
    private sealed class Sampler
    {
        private readonly Vector2 _min, _max;
        private readonly Func<Vector2, float> _minDistance;
        private readonly Func<Vector2, bool> _accept;
        private readonly float _cell;
        private readonly int _cellsX, _cellsZ;
        private readonly int[] _head;                 // first point in each cell, −1 for none
        private readonly List<int> _next = new();     // the next point in the same cell, −1 for none
        private readonly List<float> _spacing = new();
        private readonly List<int> _active = new();
        private float _widest;

        public Sampler(Vector2 min, Vector2 max, Func<Vector2, float> minDistance, Func<Vector2, bool> accept, float lowest)
        {
            _min = min;
            _max = max;
            _minDistance = minDistance;
            _accept = accept;
            Vector2 size = max - min;
            float cell = lowest / MathF.Sqrt(2f);
            if ((double)(size.X / cell + 1) * (size.Y / cell + 1) > MaxCells) cell = MathF.Sqrt(size.X * size.Y / MaxCells) * 1.01f;
            _cell = cell;
            _cellsX = Math.Max(1, (int)MathF.Ceiling(size.X / cell));
            _cellsZ = Math.Max(1, (int)MathF.Ceiling(size.Y / cell));
            _head = new int[_cellsX * _cellsZ];
            Array.Fill(_head, -1);
        }

        public List<Vector2> Points { get; } = new();
        public int CellCount => _head.Length;
        public int ActiveCount => _active.Count;

        public bool IsOccupied(int cell) => _head[cell] >= 0;

        /// <summary>A random point in background cell <paramref name="cell"/>.</summary>
        public Vector2 Jittered(int cell, ref XorShift rng)
        {
            int i = cell % _cellsX, j = cell / _cellsX;
            float u = rng.NextFloat(), v = rng.NextFloat();
            return _min + new Vector2((i + u) * _cell, (j + v) * _cell);
        }

        /// <summary>Tries up to <paramref name="attempts"/> points around a random active one; retires it if none fits.</summary>
        public void Grow(ref XorShift rng, int attempts)
        {
            int slot = (int)(rng.NextUInt() % (uint)_active.Count);
            int from = _active[slot];
            for (int a = 0; a < attempts; a++)
                if (TryPlace(Ring(Points[from], _spacing[from], ref rng))) return;
            _active[slot] = _active[^1];
            _active.RemoveAt(_active.Count - 1);
        }

        /// <summary>Keeps <paramref name="p"/> (and makes it active) if it is in the area, spaced from the rest and accepted.</summary>
        public bool TryPlace(Vector2 p)
        {
            if (p.X < _min.X || p.X >= _max.X || p.Y < _min.Y || p.Y >= _max.Y) return false;
            float spacing = Spacing(_minDistance, p);
            if (Crowded(p, spacing) || !_accept(p)) return false;
            int index = Points.Count, cell = CellOf(p);
            Points.Add(p);
            _spacing.Add(spacing);
            _next.Add(_head[cell]);
            _head[cell] = index;
            _active.Add(index);
            _widest = MathF.Max(_widest, spacing);
            return true;
        }

        private bool Crowded(Vector2 p, float spacing)
        {
            // A point q needs max(spacing, its own) of room; no kept spacing exceeds _widest.
            float reach = MathF.Max(spacing, _widest);
            int i0 = Column(p.X - reach), i1 = Column(p.X + reach), j0 = Row(p.Y - reach), j1 = Row(p.Y + reach);
            for (int j = j0; j <= j1; j++)
            for (int i = i0; i <= i1; i++)
                for (int q = _head[j * _cellsX + i]; q >= 0; q = _next[q])
                {
                    float need = MathF.Max(spacing, _spacing[q]);
                    if (Vector2.DistanceSquared(p, Points[q]) < need * need) return true;
                }
            return false;
        }

        /// <summary>A uniformly random point of the ring from <paramref name="spacing"/> to twice it around <paramref name="center"/>.</summary>
        private static Vector2 Ring(Vector2 center, float spacing, ref XorShift rng)
        {
            float inner = spacing * spacing, outer = 4 * inner;
            while (true)
            {
                var d = new Vector2(rng.NextFloat() * 2 - 1, rng.NextFloat() * 2 - 1) * (2 * spacing);
                float lengthSquared = d.LengthSquared();
                if (lengthSquared >= inner && lengthSquared < outer) return center + d;
            }
        }

        private int CellOf(Vector2 p) => Row(p.Y) * _cellsX + Column(p.X);
        private int Column(float x) => (int)Math.Clamp(MathF.Floor((x - _min.X) / _cell), 0f, _cellsX - 1);
        private int Row(float z) => (int)Math.Clamp(MathF.Floor((z - _min.Y) / _cell), 0f, _cellsZ - 1);
    }
}
