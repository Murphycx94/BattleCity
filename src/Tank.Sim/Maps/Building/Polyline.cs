using System.Collections.ObjectModel;
using System.Numerics;

namespace Tank.Sim.Maps.Building;

/// <summary>
/// A smooth line on the ground (a river's or a road's centre line): a centripetal Catmull-Rom curve (alpha 0.5, so no
/// loops or cusps) through every control point, the ends continued by mirrored virtual points, resampled into straight
/// pieces at most <c>step</c> m long. Each control point is one of <see cref="Points"/> exactly. Everything here — length,
/// points, distances — is measured along those straight pieces.
/// </summary>
public sealed class Polyline
{
    /// <summary>Each piece of the result is measured on this many finer pieces of the curve.</summary>
    private const int DensePerPiece = 16;

    private const float SlabPadding = 1e-3f;

    private readonly Vector2[] _points;
    private readonly float[] _along;     // _along[k]: distance along the line to _points[k]
    private readonly Vector2[] _dir;     // _dir[k]: unit direction of segment k (_points[k] → _points[k + 1])
    private readonly float[] _length;    // _length[k]: length of segment k

    /// <param name="controlPoints">At least two distinct points, all finite; a point equal to the one before it is dropped.</param>
    /// <param name="step">The longest piece, m.</param>
    public Polyline(IReadOnlyList<Vector2> controlPoints, float step = 2f)
    {
        ArgumentNullException.ThrowIfNull(controlPoints);
        if (!float.IsFinite(step) || step <= 0) throw new ArgumentOutOfRangeException(nameof(step), step, "Step must be finite and positive.");
        Vector2[] control = Distinct(controlPoints);
        var points = new List<Vector2> { control[0] };
        var dense = new List<Vector2>();
        var denseAlong = new List<float>();
        for (int k = 0; k + 1 < control.Length; k++)
        {
            Vector2 before = k > 0 ? control[k - 1] : 2 * control[0] - control[1];
            Vector2 after = k + 2 < control.Length ? control[k + 2] : 2 * control[k + 1] - control[k];
            Resample(before, control[k], control[k + 1], after, step, points, dense, denseAlong);
        }

        _points = points.ToArray();
        int segments = _points.Length - 1;
        _along = new float[_points.Length];
        _dir = new Vector2[segments];
        _length = new float[segments];
        for (int k = 0; k < segments; k++)
        {
            Vector2 d = _points[k + 1] - _points[k];
            _length[k] = d.Length();
            _dir[k] = d / _length[k];
            _along[k + 1] = _along[k] + _length[k];
        }
        Points = new ReadOnlyCollection<Vector2>(_points);
    }

    /// <summary>The resampled points, from the first control point to the last.</summary>
    public IReadOnlyList<Vector2> Points { get; }

    /// <summary>Length along <see cref="Points"/>, m.</summary>
    public float Length => _along[^1];

    /// <summary>The point <paramref name="along"/> m from the start (clamped to the line).</summary>
    public Vector2 PointAt(float along)
    {
        int k = SegmentAt(along, out float t);
        return _points[k] + _dir[k] * t;
    }

    /// <summary>The unit direction of the line <paramref name="along"/> m from the start (clamped to the line): that of the piece there.</summary>
    public Vector2 TangentAt(float along) => _dir[SegmentAt(along, out _)];

    /// <summary>
    /// Distance from <paramref name="p"/> to the line, and how far along it the nearest point is (the first such
    /// point when several are as near). Looks at every piece.
    /// </summary>
    public float DistanceTo(Vector2 p, out float along)
    {
        float best = float.PositiveInfinity;
        along = 0;
        for (int k = 0; k < _dir.Length; k++)
        {
            float d = DistanceSquared(k, p, out float t);
            if (d < best)
            {
                best = d;
                along = _along[k] + t;
            }
        }
        return MathF.Sqrt(best);
    }

    /// <summary>
    /// Writes this line into <paramref name="grid"/>: every sample within <paramref name="reach"/> m of it that is nearer
    /// to it than to whatever was stamped before gets the distance, how far along this line its nearest point is, and
    /// <paramref name="lineId"/>. Only the samples in each piece's strip (the piece widened by <paramref name="reach"/>
    /// on both sides, and continued past its ends just far enough to cover the round outside of each bend and the
    /// line's ends) are looked at, so the cost goes with the stamped band's area.
    /// </summary>
    public void Stamp(DistanceGrid grid, float reach, int lineId)
    {
        ArgumentNullException.ThrowIfNull(grid);
        if (!float.IsFinite(reach) || reach < 0) throw new ArgumentOutOfRangeException(nameof(reach), reach, "Reach must be finite and not negative.");
        for (int k = 0; k < _dir.Length; k++)
        {
            // Beyond a bend, the samples nearest to the bend's point lie in a wedge as wide as the turn; continuing both
            // pieces past it by reach · sin(turn / 2) covers it. The line's own ends need a whole reach (a half disc).
            float before = k == 0 ? reach : reach * HalfTurnSine(_dir[k - 1], _dir[k]);
            float after = k == _dir.Length - 1 ? reach : reach * HalfTurnSine(_dir[k], _dir[k + 1]);
            StampStrip(grid, k, -before, _length[k] + after, reach, lineId);
        }
    }

    private void StampStrip(DistanceGrid grid, int k, float from, float to, float reach, int lineId)
    {
        Vector2 a = _points[k], u = _dir[k], n = new(-u.Y, u.X);
        float cell = grid.CellSize, reachSquared = reach * reach;
        // The strip's corners bound the rows to visit.
        Vector2 c0 = a + u * from + n * reach, c1 = a + u * from - n * reach, c2 = a + u * to + n * reach, c3 = a + u * to - n * reach;
        float minX = MathF.Min(MathF.Min(c0.X, c1.X), MathF.Min(c2.X, c3.X)), maxX = MathF.Max(MathF.Max(c0.X, c1.X), MathF.Max(c2.X, c3.X));
        float minZ = MathF.Min(MathF.Min(c0.Y, c1.Y), MathF.Min(c2.Y, c3.Y)), maxZ = MathF.Max(MathF.Max(c0.Y, c1.Y), MathF.Max(c2.Y, c3.Y));
        int j0 = Math.Max(0, (int)MathF.Ceiling((minZ - SlabPadding) / cell)), j1 = Math.Min(grid.SamplesZ - 1, (int)MathF.Floor((maxZ + SlabPadding) / cell));
        for (int j = j0; j <= j1; j++)
        {
            float dz = j * cell - a.Y;
            // Along the row, the strip is where both the along-piece and the across-piece coordinates are in range.
            float lo = minX, hi = maxX;
            if (!Slab(u.X, dz * u.Y, from, to, a.X, ref lo, ref hi)) continue;
            if (!Slab(-u.Y, dz * u.X, -reach, reach, a.X, ref lo, ref hi)) continue;
            int i0 = Math.Max(0, (int)MathF.Ceiling((lo - SlabPadding) / cell)), i1 = Math.Min(grid.SamplesX - 1, (int)MathF.Floor((hi + SlabPadding) / cell));
            int row = j * grid.SamplesX;
            for (int i = i0; i <= i1; i++)
            {
                float d = DistanceSquared(k, new Vector2(i * cell, j * cell), out float t);
                if (d > reachSquared) continue;
                float distance = MathF.Sqrt(d);
                int at = row + i;
                if (distance >= grid.Distance[at]) continue;
                grid.Distance[at] = distance;
                grid.Along[at] = _along[k] + t;
                grid.Line[at] = lineId;
            }
        }
    }

    /// <summary>
    /// Narrows [lo, hi] to the x where low ≤ (x − originX)·slope + offset ≤ high, padded a little (the exact distance test
    /// decides). False when no x qualifies.
    /// </summary>
    private static bool Slab(float slope, float offset, float low, float high, float originX, ref float lo, ref float hi)
    {
        if (MathF.Abs(slope) < 1e-6f)
            return offset >= low - SlabPadding && offset <= high + SlabPadding;
        float x0 = originX + (low - offset) / slope, x1 = originX + (high - offset) / slope;
        if (x0 > x1) (x0, x1) = (x1, x0);
        lo = MathF.Max(lo, x0 - SlabPadding);
        hi = MathF.Min(hi, x1 + SlabPadding);
        return lo <= hi;
    }

    /// <summary>Squared distance from <paramref name="p"/> to segment <paramref name="k"/>, and how far along it the nearest point is.</summary>
    private float DistanceSquared(int k, Vector2 p, out float t)
    {
        t = Math.Clamp(Vector2.Dot(p - _points[k], _dir[k]), 0f, _length[k]);
        return Vector2.DistanceSquared(p, _points[k] + _dir[k] * t);
    }

    /// <summary>
    /// sin(θ / 2) for the angle θ between unit vectors <paramref name="a"/> and <paramref name="b"/>: half the chord
    /// between them. (√((1 − a·b) / 2) loses a gentle bend: below about 5e-4 rad, a·b rounds to 1 and the strips leave
    /// holes in the wedge outside the bend, tens of metres out.)
    /// </summary>
    private static float HalfTurnSine(Vector2 a, Vector2 b) => Vector2.Distance(a, b) * 0.5f;

    private int SegmentAt(float along, out float t)
    {
        along = Math.Clamp(along, 0f, Length);
        int k = Array.BinarySearch(_along, along);
        if (k < 0) k = ~k - 1;
        k = Math.Clamp(k, 0, _dir.Length - 1);
        t = Math.Clamp(along - _along[k], 0f, _length[k]);
        return k;
    }

    private static Vector2[] Distinct(IReadOnlyList<Vector2> controlPoints)
    {
        var kept = new List<Vector2>(controlPoints.Count);
        foreach (Vector2 p in controlPoints)
        {
            if (!float.IsFinite(p.X) || !float.IsFinite(p.Y)) throw new ArgumentException($"Control point {p} is not finite.", nameof(controlPoints));
            if (kept.Count == 0 || kept[^1] != p) kept.Add(p);
        }
        if (kept.Count < 2) throw new ArgumentException("A polyline needs at least two distinct control points.", nameof(controlPoints));
        return kept.ToArray();
    }

    /// <summary>
    /// Appends the curve from <paramref name="p1"/> to <paramref name="p2"/> (neighbours <paramref name="p0"/> and
    /// <paramref name="p3"/>) to <paramref name="points"/>, in pieces of equal arc length at most <paramref name="step"/>,
    /// ending exactly on <paramref name="p2"/>. The arc length comes from a finer polyline of the curve.
    /// </summary>
    private static void Resample(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float step, List<Vector2> points,
        List<Vector2> dense, List<float> denseAlong)
    {
        float t0 = 0, t1 = t0 + Knot(p0, p1), t2 = t1 + Knot(p1, p2), t3 = t2 + Knot(p2, p3);
        float chord = Vector2.Distance(p1, p2);
        int fine = Math.Max(DensePerPiece, (int)MathF.Ceiling(chord / step) * DensePerPiece);
        dense.Clear();
        denseAlong.Clear();
        dense.Add(p1);
        denseAlong.Add(0);
        for (int s = 1; s <= fine; s++)
        {
            Vector2 q = s == fine ? p2 : CatmullRom(p0, p1, p2, p3, t0, t1, t2, t3, t1 + (t2 - t1) * s / fine);
            denseAlong.Add(denseAlong[^1] + Vector2.Distance(dense[^1], q));
            dense.Add(q);
        }

        float arc = denseAlong[^1];
        int pieces = Math.Max(1, (int)MathF.Ceiling(arc / step));
        int at = 0;
        for (int piece = 1; piece < pieces; piece++)
        {
            float target = arc * piece / pieces;
            while (denseAlong[at + 1] < target) at++;
            float span = denseAlong[at + 1] - denseAlong[at];
            float w = span > 0 ? (target - denseAlong[at]) / span : 0f;
            Vector2 q = Vector2.Lerp(dense[at], dense[at + 1], w);
            if (q != points[^1]) points.Add(q);
        }
        if (p2 != points[^1]) points.Add(p2);
    }

    /// <summary>Centripetal knot spacing: |b − a|^0.5.</summary>
    private static float Knot(Vector2 a, Vector2 b) => MathF.Sqrt(Vector2.Distance(a, b));

    /// <summary>The Barry–Goldman pyramid for a non-uniform Catmull-Rom segment, t in [t1, t2].</summary>
    private static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t0, float t1, float t2, float t3, float t)
    {
        Vector2 a1 = Mix(p0, p1, t0, t1, t), a2 = Mix(p1, p2, t1, t2, t), a3 = Mix(p2, p3, t2, t3, t);
        Vector2 b1 = Mix(a1, a2, t0, t2, t), b2 = Mix(a2, a3, t1, t3, t);
        return Mix(b1, b2, t1, t2, t);
    }

    private static Vector2 Mix(Vector2 a, Vector2 b, float ta, float tb, float t) => (a * (tb - t) + b * (t - ta)) / (tb - ta);
}
