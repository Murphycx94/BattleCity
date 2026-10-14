using System.Numerics;

namespace Tank.Client.Art.Meshes;

/// <summary>
/// A convex side profile (z, y) swept along X, like <see cref="Extrude.Build"/>, but with the edges at both ends rounded
/// over a quarter circle of radius <c>round</c> instead of chamfered, and smooth normals across gentle profile turns —
/// so a profile with rounded corners (<see cref="Polygon2.RoundCorners"/>) becomes a soft, soap-like body.
/// The body spans x ∈ [−width/2, width/2] and exactly the profile's (z, y) extent.
/// <para>
/// <c>round</c> must not exceed the profile's smallest corner radius: the end caps are the profile inset by
/// <c>round</c>, and a tighter corner would collapse (<see cref="Polygon2.Inset"/> throws).
/// </para>
/// </summary>
public static class RoundedSlab
{
    public static MeshData Build(IReadOnlyList<Vector2> profileZY, float width, float round, int steps = 4, float smoothAngleDeg = 35f)
    {
        if (profileZY.Count < 3) throw new ArgumentException("A profile needs at least 3 points.", nameof(profileZY));
        if (round <= 0 || round >= width / 2) throw new ArgumentOutOfRangeException(nameof(round), "Round must be in (0, width/2).");
        if (steps < 1) throw new ArgumentOutOfRangeException(nameof(steps), "Need at least one step per rounded edge.");
        Vector2[] poly = Polygon2.EnsureCcw(profileZY);
        if (!Polygon2.IsStrictlyConvex(poly)) throw new ArgumentException("Profile must be strictly convex.", nameof(profileZY));

        int n = poly.Length;
        var (startNormals, endNormals) = EdgeEndNormals(poly, smoothAngleDeg);

        // Rings from the −X cap round to the +X cap: φ is the angle the normal leans out of the profile plane towards ±X.
        float half = width / 2;
        var rings = new List<(float X, float Lean, Vector2[] Points)>();
        for (int k = steps; k >= 0; k--) rings.Add(Ring(poly, half, round, -1, MathF.PI / 2 * k / steps));
        for (int k = 0; k <= steps; k++) rings.Add(Ring(poly, half, round, +1, MathF.PI / 2 * k / steps));

        var b = new MeshBuilder();
        for (int r = 0; r + 1 < rings.Count; r++)
        {
            var (xa, leanA, pa) = rings[r];
            var (xb, leanB, pb) = rings[r + 1];
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                int a0 = b.AddVertex(At(xa, pa[i]), Normal(leanA, startNormals[i]), new Vector2(i, xa));
                int a1 = b.AddVertex(At(xa, pa[j]), Normal(leanA, endNormals[i]), new Vector2(i + 1, xa));
                int b1 = b.AddVertex(At(xb, pb[j]), Normal(leanB, endNormals[i]), new Vector2(i + 1, xb));
                int b0 = b.AddVertex(At(xb, pb[i]), Normal(leanB, startNormals[i]), new Vector2(i, xb));
                b.AddQuad(a0, b0, b1, a1);
            }
        }
        Cap(b, rings[0].X, rings[0].Points, -1);
        Cap(b, rings[^1].X, rings[^1].Points, +1);
        return b.Build();
    }

    /// <summary>
    /// 2D normal of each edge at its start and end corner. Corner i joins edge i−1 and edge i; on a gentle turn both edges
    /// share one normal there, weighted by edge length so a long straight wall keeps nearly its own normal where a
    /// rounded corner begins; a sharper turn than <paramref name="smoothAngleDeg"/> stays crisp.
    /// </summary>
    private static (Vector2[] Start, Vector2[] End) EdgeEndNormals(Vector2[] poly, float smoothAngleDeg)
    {
        int n = poly.Length;
        float smoothCos = MathF.Cos(Math.Clamp(smoothAngleDeg, 0f, 179.9f) * MathF.PI / 180f);
        var edgeNormals = new Vector2[n];
        var lengths = new float[n];
        for (int i = 0; i < n; i++)
        {
            edgeNormals[i] = Polygon2.EdgeNormal(poly, i);
            lengths[i] = Vector2.Distance(poly[i], poly[(i + 1) % n]);
        }
        var start = new Vector2[n];
        var end = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            int prev = (i + n - 1) % n;
            bool smooth = Vector2.Dot(edgeNormals[prev], edgeNormals[i]) >= smoothCos;
            Vector2 shared = Vector2.Normalize(edgeNormals[prev] * lengths[prev] + edgeNormals[i] * lengths[i]);
            start[i] = smooth ? shared : edgeNormals[i];
            end[prev] = smooth ? shared : edgeNormals[prev];
        }
        return (start, end);
    }

    /// <summary>Ring at lean angle <paramref name="phi"/> on one side: 0 on the straight wall, π/2 on the end cap.</summary>
    private static (float X, float Lean, Vector2[] Points) Ring(Vector2[] poly, float half, float round, int side, float phi)
    {
        float inset = round * (1 - MathF.Cos(phi));
        Vector2[] points = inset > 1e-6f ? Polygon2.Inset(poly, inset) : poly;
        return (side * (half - round + round * MathF.Sin(phi)), side * phi, points);
    }

    private static Vector3 At(float x, Vector2 zy) => new(x, zy.Y, zy.X);

    /// <summary>Profile normal (z, y) leaned by <paramref name="lean"/> radians towards +X (negative: −X).</summary>
    private static Vector3 Normal(float lean, Vector2 n) =>
        Vector3.Normalize(new Vector3(MathF.Sin(lean), MathF.Cos(lean) * n.Y, MathF.Cos(lean) * n.X));

    private static void Cap(MeshBuilder b, float x, Vector2[] points, int side)
    {
        var normal = new Vector3(side, 0, 0);
        int first = b.VertexCount;
        foreach (Vector2 p in points) b.AddVertex(At(x, p), normal, new Vector2(p.X, p.Y));
        for (int k = 1; k + 1 < points.Length; k++)
        {
            if (side > 0) b.AddTriangle(first, first + k + 1, first + k);
            else b.AddTriangle(first, first + k, first + k + 1);
        }
    }
}
