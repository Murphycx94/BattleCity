using System.Numerics;

namespace Tank.Client.Art.Meshes;

/// <summary>
/// Core of <see cref="Extrude"/> and <see cref="Box"/>: a convex profile in the XY plane swept along Z from −length/2
/// to +length/2, optionally shrunk towards its centroid at the +Z end (<paramref name="endScale"/>), with every edge
/// chamfered by <c>bevel</c>.
/// <para>
/// With a bevel the mesh is four rings of the corner-cut profile: start cap, start of the walls, end of the walls, end
/// cap. Wall vertices on a cut corner carry the normal of the edge they came from, and cap-ring vertices carry the cap
/// normal, so chamfers shade as rounded edges. Without a bevel every face is flat with its own vertices.
/// </para>
/// </summary>
public static class Prism
{
    public static MeshData Build(IReadOnlyList<Vector2> profile, float length, float bevel = 0f, float endScale = 1f)
    {
        if (profile.Count < 3) throw new ArgumentException("A profile needs at least 3 points.", nameof(profile));
        if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length), "Length must be positive.");
        if (bevel < 0 || 2 * bevel >= length) throw new ArgumentOutOfRangeException(nameof(bevel), "Bevel must be in [0, length/2).");
        if (endScale <= 0) throw new ArgumentOutOfRangeException(nameof(endScale), "End scale must be positive.");
        Vector2[] poly = Polygon2.EnsureCcw(profile);
        if (!Polygon2.IsStrictlyConvex(poly)) throw new ArgumentException("Profile must be strictly convex.", nameof(profile));
        var shape = new Shape(poly, length, endScale);
        return bevel > 0 ? Beveled(shape, bevel) : Sharp(shape);
    }

    private static MeshData Sharp(Shape s)
    {
        var b = new MeshBuilder();
        int n = s.Poly.Length;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            Vector3 normal = s.WallNormal(i);
            int a0 = b.AddVertex(s.At(s.Poly[i], s.Z0), normal, new Vector2(i, 0));
            int b0 = b.AddVertex(s.At(s.Poly[j], s.Z0), normal, new Vector2(i + 1, 0));
            int b1 = b.AddVertex(s.At(s.Poly[j], s.Z1), normal, new Vector2(i + 1, 1));
            int a1 = b.AddVertex(s.At(s.Poly[i], s.Z1), normal, new Vector2(i, 1));
            b.AddQuad(a0, b0, b1, a1);
        }
        AddCap(b, s, s.Poly, s.Z0, Ring(b, s, s.Poly, s.Z0, -Vector3.UnitZ));
        AddCap(b, s, s.Poly, s.Z1, Ring(b, s, s.Poly, s.Z1, Vector3.UnitZ));
        return b.Build();
    }

    private static MeshData Beveled(Shape s, float bevel)
    {
        Vector2[] cut = Polygon2.CutCorners(s.Poly, bevel, out int[] parent);
        Vector2[] inset = Polygon2.Inset(cut, bevel);
        int m = cut.Length;
        var b = new MeshBuilder();
        var wallNormals = new Vector3[m];
        for (int k = 0; k < m; k++) wallNormals[k] = s.WallNormal(parent[k]);

        int startCap = Ring(b, s, inset, s.Z0, -Vector3.UnitZ);
        int startWall = RingWithNormals(b, s, cut, s.Z0 + bevel, wallNormals);
        int endWall = RingWithNormals(b, s, cut, s.Z1 - bevel, wallNormals);
        int endCap = Ring(b, s, inset, s.Z1, Vector3.UnitZ);

        Band(b, startCap, startWall, m);
        Band(b, startWall, endWall, m);
        Band(b, endWall, endCap, m);
        AddCap(b, s, inset, s.Z0, startCap);
        AddCap(b, s, inset, s.Z1, endCap);
        return b.Build();
    }

    /// <summary>Quads between ring <paramref name="low"/> (smaller z) and ring <paramref name="high"/>.</summary>
    private static void Band(MeshBuilder b, int low, int high, int m)
    {
        for (int k = 0; k < m; k++)
        {
            int k1 = (k + 1) % m;
            b.AddQuad(low + k, low + k1, high + k1, high + k);
        }
    }

    private static int Ring(MeshBuilder b, Shape s, Vector2[] points, float z, Vector3 normal)
    {
        int first = b.VertexCount;
        for (int k = 0; k < points.Length; k++) b.AddVertex(s.At(points[k], z), normal, s.Uv(points, k, z));
        return first;
    }

    private static int RingWithNormals(MeshBuilder b, Shape s, Vector2[] points, float z, Vector3[] normals)
    {
        int first = b.VertexCount;
        for (int k = 0; k < points.Length; k++) b.AddVertex(s.At(points[k], z), normals[k], s.Uv(points, k, z));
        return first;
    }

    /// <summary>Fan over a ring that already exists; the +Z cap keeps the profile order, the −Z cap reverses it.</summary>
    private static void AddCap(MeshBuilder b, Shape s, Vector2[] points, float z, int first)
    {
        bool end = z > 0;
        for (int k = 1; k < points.Length - 1; k++)
        {
            if (end) b.AddTriangle(first, first + k, first + k + 1);
            else b.AddTriangle(first, first + k + 1, first + k);
        }
    }

    private sealed class Shape
    {
        public Shape(Vector2[] poly, float length, float endScale)
        {
            Poly = poly;
            Length = length;
            EndScale = endScale;
            Center = Polygon2.Centroid(poly);
            Z0 = -length / 2;
            Z1 = length / 2;
        }

        public Vector2[] Poly { get; }
        public float Length { get; }
        public float EndScale { get; }
        public Vector2 Center { get; }
        public float Z0 { get; }
        public float Z1 { get; }

        public Vector3 At(Vector2 p, float z)
        {
            float scale = 1f + (EndScale - 1f) * (z - Z0) / Length;
            Vector2 q = Center + (p - Center) * scale;
            return new Vector3(q.X, q.Y, z);
        }

        /// <summary>Outward normal of the wall on profile edge <paramref name="edge"/>, tilted when the prism tapers.</summary>
        public Vector3 WallNormal(int edge)
        {
            Vector2 n2 = Polygon2.EdgeNormal(Poly, edge);
            float reach = Vector2.Dot(n2, Poly[edge] - Center);
            return Vector3.Normalize(new Vector3(n2.X, n2.Y, reach * (1f - EndScale) / Length));
        }

        public Vector2 Uv(Vector2[] points, int k, float z) => new(points[k].X + points[k].Y, z / Length + 0.5f);
    }
}
