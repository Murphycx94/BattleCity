using System.Numerics;

namespace Tank.Client.Art.Meshes;

/// <summary>
/// Surface of revolution around the Y axis. The profile is (radius, y) pairs listed bottom to top with the outside on
/// the right-hand side of the walk. A point with radius 0 closes that end at the axis; an end with radius &gt; 0 gets a
/// flat cap (bottom faces −Y, top faces +Y), so the result is always closed.
/// Neighbouring profile edges that turn by at most <c>smoothAngleDeg</c> share a normal, sharper turns stay crisp.
/// A <c>squareness</c> above 2 sweeps the profile round a <see cref="Superellipse"/> instead of a circle: a rounded block
/// whose sides sit at the profile's radius; at most <see cref="MaxSquareness"/>.
/// </summary>
public static class Lathe
{
    /// <summary>Radii below this count as lying on the axis.</summary>
    private const float AxisEpsilon = 1e-5f;

    /// <summary>The squarest cross-section allowed: beyond it the corners look no squarer and the superellipse's powers overflow.</summary>
    public const float MaxSquareness = 32f;

    public static MeshData Build(IReadOnlyList<Vector2> profile, int segments, float smoothAngleDeg = 35f, float squareness = 2f)
    {
        if (profile.Count < 2) throw new ArgumentException("A profile needs at least 2 points.", nameof(profile));
        if (segments < 3) throw new ArgumentOutOfRangeException(nameof(segments), "Need at least 3 segments.");
        if (!(squareness >= 2f)) throw new ArgumentOutOfRangeException(nameof(squareness), "2 is round; less would pinch the corners.");
        if (squareness > MaxSquareness)
            throw new ArgumentOutOfRangeException(nameof(squareness), "Beyond 32 the corners are square to the eye and the powers overflow.");
        smoothAngleDeg = Math.Clamp(smoothAngleDeg, 0f, 179.9f);
        for (int k = 0; k < profile.Count; k++)
        {
            if (profile[k].X < 0) throw new ArgumentException($"Point {k} has a negative radius.", nameof(profile));
            if (k > 0 && Vector2.DistanceSquared(profile[k], profile[k - 1]) < 1e-12f)
                throw new ArgumentException($"Points {k - 1} and {k} coincide.", nameof(profile));
            if (k > 0 && OnAxis(profile[k]) && OnAxis(profile[k - 1]))
                throw new ArgumentException($"Edge {k - 1} lies on the axis.", nameof(profile));
        }

        var loop = new List<Vector2> { new(0, profile[0].Y) };
        loop.AddRange(profile);
        loop.Add(new Vector2(0, profile[^1].Y));
        if (Polygon2.SignedArea(loop) <= 0)
            throw new ArgumentException("Profile must run bottom to top with the outside on the right.", nameof(profile));

        int edges = profile.Count - 1;
        var edgeNormals = new Vector2[edges];
        for (int k = 0; k < edges; k++)
        {
            Vector2 d = Vector2.Normalize(profile[k + 1] - profile[k]);
            edgeNormals[k] = new Vector2(d.Y, -d.X);
        }
        float smoothCos = MathF.Cos(smoothAngleDeg * MathF.PI / 180f);

        var b = new MeshBuilder();
        for (int k = 0; k < edges; k++)
        {
            Vector2 n0 = EndNormal(edgeNormals, k, k - 1, smoothCos);
            Vector2 n1 = EndNormal(edgeNormals, k, k + 1, smoothCos);
            int low = Ring(b, profile[k], n0, segments, (float)k / edges, squareness);
            int high = Ring(b, profile[k + 1], n1, segments, (float)(k + 1) / edges, squareness);
            for (int j = 0; j < segments; j++)
            {
                int a = low + j, bb = low + j + 1, c = high + j + 1, d = high + j;
                if (OnAxis(profile[k])) b.AddTriangle(a, c, d);
                else if (OnAxis(profile[k + 1])) b.AddTriangle(a, bb, c);
                else b.AddQuad(a, bb, c, d);
            }
        }
        if (!OnAxis(profile[0])) Cap(b, profile[0], segments, top: false, squareness);
        if (!OnAxis(profile[^1])) Cap(b, profile[^1], segments, top: true, squareness);
        return b.Build();
    }

    private static bool OnAxis(Vector2 p) => p.X < AxisEpsilon;

    /// <summary>Normal at the end of edge <paramref name="edge"/> shared with <paramref name="neighbour"/> if the turn is gentle.</summary>
    private static Vector2 EndNormal(Vector2[] normals, int edge, int neighbour, float smoothCos)
    {
        if (neighbour < 0 || neighbour >= normals.Length) return normals[edge];
        return Vector2.Dot(normals[edge], normals[neighbour]) >= smoothCos
            ? Vector2.Normalize(normals[edge] + normals[neighbour])
            : normals[edge];
    }

    /// <summary>
    /// One ring of the sweep. Off the circle the wall's normal is the section's normal tilted by the profile's: where the
    /// section bulges out (k &gt; 1 at a corner) the same rise in the profile leans the wall less, so the profile normal's
    /// vertical part is scaled by how far the section reaches along its own normal.
    /// </summary>
    private static int Ring(MeshBuilder b, Vector2 point, Vector2 normal, int segments, float v, float squareness)
    {
        int first = b.VertexCount;
        for (int j = 0; j <= segments; j++)
        {
            float angle = MathF.Tau * j / segments;
            float cos = MathF.Cos(angle), sin = -MathF.Sin(angle);
            float k = 1f, nx = cos, nz = sin, rise = 1f;
            if (squareness != 2f)
            {
                k = Superellipse.Radius(cos, sin, squareness);
                Vector2 across = Superellipse.Normal(cos, sin, squareness);
                (nx, nz) = (across.X, across.Y);
                rise = k * (nx * cos + nz * sin);
            }
            b.AddVertex(new Vector3(point.X * k * cos, point.Y, point.X * k * sin),
                Vector3.Normalize(new Vector3(normal.X * nx, normal.Y * rise, normal.X * nz)),
                new Vector2((float)j / segments, v));
        }
        return first;
    }

    private static void Cap(MeshBuilder b, Vector2 point, int segments, bool top, float squareness)
    {
        Vector3 normal = top ? Vector3.UnitY : -Vector3.UnitY;
        int center = b.AddVertex(new Vector3(0, point.Y, 0), normal, new Vector2(0.5f, 0.5f));
        int first = b.VertexCount;
        for (int j = 0; j <= segments; j++)
        {
            float angle = MathF.Tau * j / segments;
            float cos = MathF.Cos(angle), sin = -MathF.Sin(angle);
            float k = squareness == 2f ? 1f : Superellipse.Radius(cos, sin, squareness);
            b.AddVertex(new Vector3(point.X * k * cos, point.Y, point.X * k * sin), normal, new Vector2(0.5f + cos * 0.5f, 0.5f + sin * 0.5f));
        }
        for (int j = 0; j < segments; j++)
        {
            if (top) b.AddTriangle(center, first + j, first + j + 1);
            else b.AddTriangle(center, first + j + 1, first + j);
        }
    }
}
