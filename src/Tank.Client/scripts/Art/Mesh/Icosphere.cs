using System.Numerics;

namespace Tank.Client.Art.Meshes;

/// <summary>Unit sphere from a subdivided icosahedron, vertices shared so it is closed and smooth.</summary>
public static class Icosphere
{
    public static MeshData Build(int subdivisions)
    {
        if (subdivisions is < 0 or > 5) throw new ArgumentOutOfRangeException(nameof(subdivisions), "Use 0..5.");
        float t = (1f + MathF.Sqrt(5f)) / 2f;
        var points = new List<Vector3>
        {
            new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0),
            new(0, -1, t), new(0, 1, t), new(0, -1, -t), new(0, 1, -t),
            new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1),
        };
        for (int i = 0; i < points.Count; i++) points[i] = Vector3.Normalize(points[i]);
        var faces = new List<(int, int, int)>
        {
            (0, 11, 5), (0, 5, 1), (0, 1, 7), (0, 7, 10), (0, 10, 11),
            (1, 5, 9), (5, 11, 4), (11, 10, 2), (10, 7, 6), (7, 1, 8),
            (3, 9, 4), (3, 4, 2), (3, 2, 6), (3, 6, 8), (3, 8, 9),
            (4, 9, 5), (2, 4, 11), (6, 2, 10), (8, 6, 7), (9, 8, 1),
        };
        for (int s = 0; s < subdivisions; s++)
        {
            var midpoints = new Dictionary<long, int>();
            int Mid(int a, int b)
            {
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (midpoints.TryGetValue(key, out int index)) return index;
                points.Add(Vector3.Normalize((points[a] + points[b]) * 0.5f));
                midpoints[key] = points.Count - 1;
                return points.Count - 1;
            }
            var next = new List<(int, int, int)>(faces.Count * 4);
            foreach (var (a, b, c) in faces)
            {
                int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                next.Add((a, ab, ca));
                next.Add((b, bc, ab));
                next.Add((c, ca, bc));
                next.Add((ab, bc, ca));
            }
            faces = next;
        }

        var builder = new MeshBuilder();
        foreach (Vector3 p in points)
            builder.AddVertex(p, p, new Vector2(0.5f + MathF.Atan2(p.Z, p.X) / MathF.Tau, 0.5f - MathF.Asin(p.Y) / MathF.PI));
        foreach (var (a, b, c) in faces) builder.AddTriangle(a, b, c);
        return builder.Build();
    }
}
