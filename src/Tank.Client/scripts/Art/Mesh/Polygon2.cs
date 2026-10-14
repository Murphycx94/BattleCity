using System.Numerics;

namespace Tank.Client.Art.Meshes;

/// <summary>Helpers for small convex polygons (profiles and outlines), counter-clockwise unless stated.</summary>
public static class Polygon2
{
    private const float Epsilon = 1e-6f;

    /// <summary>Positive for counter-clockwise order.</summary>
    public static float SignedArea(IReadOnlyList<Vector2> poly)
    {
        float sum = 0;
        for (int i = 0; i < poly.Count; i++)
        {
            Vector2 a = poly[i], b = poly[(i + 1) % poly.Count];
            sum += a.X * b.Y - b.X * a.Y;
        }
        return sum * 0.5f;
    }

    public static Vector2[] EnsureCcw(IReadOnlyList<Vector2> poly)
    {
        var copy = poly.ToArray();
        if (SignedArea(copy) < 0) Array.Reverse(copy);
        return copy;
    }

    /// <summary>True when every corner of a counter-clockwise polygon turns left.</summary>
    public static bool IsStrictlyConvex(IReadOnlyList<Vector2> poly)
    {
        if (poly.Count < 3) return false;
        for (int i = 0; i < poly.Count; i++)
        {
            Vector2 a = poly[i], b = poly[(i + 1) % poly.Count], c = poly[(i + 2) % poly.Count];
            if (Cross(b - a, c - b) <= Epsilon) return false;
        }
        return true;
    }

    public static Vector2 Centroid(IReadOnlyList<Vector2> poly)
    {
        Vector2 sum = Vector2.Zero;
        foreach (Vector2 p in poly) sum += p;
        return sum / poly.Count;
    }

    /// <summary>Outward unit normal of edge i → i+1 of a counter-clockwise polygon.</summary>
    public static Vector2 EdgeNormal(IReadOnlyList<Vector2> poly, int i)
    {
        Vector2 d = Vector2.Normalize(poly[(i + 1) % poly.Count] - poly[i]);
        return new Vector2(d.Y, -d.X);
    }

    /// <summary>
    /// Cuts every corner by <paramref name="distance"/> along both edges. Corner i becomes two points; the first lies
    /// on edge i−1 and the second on edge i, which <paramref name="parentEdge"/> records for normal smoothing.
    /// </summary>
    public static Vector2[] CutCorners(IReadOnlyList<Vector2> poly, float distance, out int[] parentEdge)
    {
        int n = poly.Count;
        for (int i = 0; i < n; i++)
            if (Vector2.Distance(poly[i], poly[(i + 1) % n]) <= 2 * distance + Epsilon)
                throw new ArgumentException($"Bevel {distance} is too large for edge {i} of the profile.");
        var result = new Vector2[2 * n];
        parentEdge = new int[2 * n];
        for (int i = 0; i < n; i++)
        {
            Vector2 p = poly[i], prev = poly[(i + n - 1) % n], next = poly[(i + 1) % n];
            result[2 * i] = p + Vector2.Normalize(prev - p) * distance;
            result[2 * i + 1] = p + Vector2.Normalize(next - p) * distance;
            parentEdge[2 * i] = (i + n - 1) % n;
            parentEdge[2 * i + 1] = i;
        }
        return result;
    }

    /// <summary>
    /// Replaces corner i of a convex polygon (either winding) with a circular arc of radius <paramref name="radii"/>[i],
    /// tangent to both edges, sampled with <paramref name="segments"/> chords; a radius of 0 keeps the sharp corner.
    /// The result is counter-clockwise (a clockwise input is reversed first, radii with it).
    /// </summary>
    public static Vector2[] RoundCorners(IReadOnlyList<Vector2> poly, IReadOnlyList<float> radii, int segments)
    {
        if (radii.Count != poly.Count) throw new ArgumentException($"Need one radius per corner ({poly.Count}), got {radii.Count}.", nameof(radii));
        if (segments < 1) throw new ArgumentOutOfRangeException(nameof(segments), "Need at least one chord per arc.");
        for (int i = 0; i < radii.Count; i++)
            if (radii[i] < 0 || float.IsNaN(radii[i])) throw new ArgumentOutOfRangeException(nameof(radii), $"Corner {i} has a negative radius {radii[i]}.");
        for (int i = 0; i < poly.Count; i++)
            if (Vector2.Distance(poly[i], poly[(i + 1) % poly.Count]) <= Epsilon)
                throw new ArgumentException($"Edge {i} of the polygon has zero length.", nameof(poly));
        bool reversed = SignedArea(poly) < 0;
        Vector2[] ccw = EnsureCcw(poly);
        float[] r = reversed ? radii.Reverse().ToArray() : radii.ToArray();
        int n = ccw.Length;
        var half = new float[n];    // half the interior angle at each corner
        var reach = new float[n];   // how far each corner's arc eats into its two edges
        for (int i = 0; i < n; i++)
        {
            Vector2 p = ccw[i], u1 = Vector2.Normalize(ccw[(i + n - 1) % n] - p), u2 = Vector2.Normalize(ccw[(i + 1) % n] - p);
            half[i] = MathF.Acos(Math.Clamp(Vector2.Dot(u1, u2), -1f, 1f)) / 2;
            reach[i] = r[i] / MathF.Tan(half[i]);
        }
        for (int i = 0; i < n; i++)
            if (reach[i] + reach[(i + 1) % n] >= Vector2.Distance(ccw[i], ccw[(i + 1) % n]) - Epsilon)
                throw new ArgumentException($"Corner radii {r[i]} and {r[(i + 1) % n]} do not fit on edge {i}.", nameof(radii));

        var result = new List<Vector2>();
        for (int i = 0; i < n; i++)
        {
            Vector2 p = ccw[i];
            if (r[i] <= 0) { result.Add(p); continue; }
            Vector2 u1 = Vector2.Normalize(ccw[(i + n - 1) % n] - p), u2 = Vector2.Normalize(ccw[(i + 1) % n] - p);
            Vector2 centre = p + Vector2.Normalize(u1 + u2) * (r[i] / MathF.Sin(half[i]));
            Vector2 from = p + u1 * reach[i] - centre, to = p + u2 * reach[i] - centre;
            float start = MathF.Atan2(from.Y, from.X), sweep = MathF.PI - 2 * half[i];   // a left turn: counter-clockwise
            for (int k = 0; k <= segments; k++)
            {
                float angle = start + sweep * k / segments;
                result.Add(k == segments ? centre + to : centre + r[i] * new Vector2(MathF.Cos(angle), MathF.Sin(angle)));
            }
        }
        return result.ToArray();
    }

    /// <summary>Moves every edge of a convex counter-clockwise polygon inward by <paramref name="distance"/>.</summary>
    public static Vector2[] Inset(IReadOnlyList<Vector2> poly, float distance)
    {
        int n = poly.Count;
        var result = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            int prev = (i + n - 1) % n;
            Vector2 n0 = -EdgeNormal(poly, prev), n1 = -EdgeNormal(poly, i); // inward
            Vector2 a0 = poly[prev] + n0 * distance, d0 = poly[i] - poly[prev];
            Vector2 a1 = poly[i] + n1 * distance, d1 = poly[(i + 1) % n] - poly[i];
            float denom = Cross(d0, d1);
            if (MathF.Abs(denom) < Epsilon)
                throw new ArgumentException($"Edges {prev} and {i} of the profile are parallel; cannot inset.", nameof(poly));
            float t = Cross(a1 - a0, d1) / denom;
            result[i] = a0 + d0 * t;
        }
        for (int i = 0; i < n; i++)
        {
            Vector2 before = poly[(i + 1) % n] - poly[i], after = result[(i + 1) % n] - result[i];
            if (Vector2.Dot(before, after) <= 0)
                throw new ArgumentException($"Inset {distance} collapses edge {i} of the profile; corners sharper than about 70° need a smaller bevel.");
        }
        return result;
    }

    public static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;
}
