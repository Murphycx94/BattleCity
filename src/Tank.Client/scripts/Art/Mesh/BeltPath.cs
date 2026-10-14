using System.Numerics;

namespace Tank.Client.Art.Meshes;

public readonly record struct Circle2(Vector2 Center, float Radius);

/// <summary>
/// A belt wrapped tightly around wheels, like a track around its sprocket, idler and road wheels. Circles are given in
/// counter-clockwise order around the loop (all on its convex hull; collinear ones are fine). The belt runs
/// counter-clockwise: the straight piece leaving circle i is the outer common tangent to circle i+1.
/// </summary>
public sealed class BeltPath
{
    private const float CollinearTolerance = 1e-2f;
    private readonly List<Piece> _pieces = new();

    public BeltPath(IReadOnlyList<Circle2> circles)
    {
        if (circles.Count < 2) throw new ArgumentException("Need at least two circles.", nameof(circles));
        for (int i = 0; i < circles.Count; i++)
            if (!(circles[i].Radius > 0)) throw new ArgumentException($"Circle {i} needs a positive radius.", nameof(circles));
        int n = circles.Count;
        float totalSweep = 0;
        var leave = new Vector2[n];  // outward normal where the belt leaves circle i
        var arrive = new Vector2[n]; // outward normal where the belt reaches circle i
        for (int i = 0; i < n; i++)
        {
            Circle2 a = circles[i], b = circles[(i + 1) % n];
            Vector2 delta = b.Center - a.Center;
            float distance = delta.Length();
            if (distance <= MathF.Abs(a.Radius - b.Radius) + 1e-5f)
                throw new ArgumentException($"Circles {i} and {(i + 1) % n} overlap too much for a belt.", nameof(circles));
            Vector2 dir = delta / distance, right = new(dir.Y, -dir.X);
            float sin = (a.Radius - b.Radius) / distance, cos = MathF.Sqrt(1 - sin * sin);
            Vector2 normal = dir * sin + right * cos;
            leave[i] = normal;
            arrive[(i + 1) % n] = normal;
        }
        for (int i = 0; i < n; i++)
        {
            Circle2 a = circles[i], b = circles[(i + 1) % n];
            Vector2 from = a.Center + leave[i] * a.Radius, to = b.Center + leave[i] * b.Radius;
            _pieces.Add(Piece.Line(from, to));
            int j = (i + 1) % n;
            float start = MathF.Atan2(arrive[j].Y, arrive[j].X), end = MathF.Atan2(leave[j].Y, leave[j].X);
            float sweep = end - start;
            while (sweep < 0) sweep += MathF.Tau;
            if (sweep > MathF.Tau - CollinearTolerance || sweep < CollinearTolerance)
            {
                // Touching: a sweep within tolerance of zero (either side) is not wrapped at all.
                totalSweep += sweep > MathF.PI ? sweep - MathF.Tau : sweep;
                sweep = 0;
            }
            else
            {
                totalSweep += sweep;
            }
            if (sweep > 0) _pieces.Add(Piece.Arc(b.Center, b.Radius, start, sweep));
        }
        if (MathF.Abs(totalSweep - MathF.Tau) > 1e-3f)
            throw new ArgumentException("circles must be counter-clockwise on the convex hull of the running gear", nameof(circles));
        foreach (Piece p in _pieces) Length += p.Length;
    }

    public float Length { get; }

    /// <summary>Point and unit direction of travel at <paramref name="distance"/> along the belt (wraps around).</summary>
    public (Vector2 Position, Vector2 Tangent) Sample(float distance)
    {
        float s = distance % Length;
        if (s < 0) s += Length;
        foreach (Piece p in _pieces)
        {
            if (s <= p.Length) return p.At(s);
            s -= p.Length;
        }
        return _pieces[^1].At(_pieces[^1].Length);
    }

    private readonly record struct Piece(bool IsArc, Vector2 A, Vector2 B, float Radius, float Start, float Sweep, float Length)
    {
        public static Piece Line(Vector2 from, Vector2 to) => new(false, from, to, 0, 0, 0, Vector2.Distance(from, to));

        public static Piece Arc(Vector2 center, float radius, float start, float sweep) =>
            new(true, center, Vector2.Zero, radius, start, sweep, radius * sweep);

        public (Vector2, Vector2) At(float s)
        {
            if (!IsArc)
            {
                Vector2 dir = Length > 0 ? (B - A) / Length : Vector2.UnitX;
                return (A + dir * s, dir);
            }
            float angle = Start + (Radius > 0 ? s / Radius : 0);
            var radial = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            return (A + radial * Radius, new Vector2(-radial.Y, radial.X));
        }
    }
}
