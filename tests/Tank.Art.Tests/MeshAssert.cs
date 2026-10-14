using System.Numerics;
using Tank.Client.Art.Meshes;
using Xunit.Sdk;

namespace Tank.Art.Tests;

/// <summary>Geometry checks shared by every builder test.</summary>
internal static class MeshAssert
{
    private const float Weld = 1e-4f;

    /// <summary>
    /// Watertight and consistently wound: after welding coincident positions, every directed edge a→b appears exactly
    /// once and its reverse b→a appears exactly once, so each edge is shared by exactly two triangles.
    /// </summary>
    public static void Closed(MeshData mesh)
    {
        int[] ids = WeldIds(mesh);
        var directed = new Dictionary<(int, int), int>();
        for (int t = 0; t < mesh.TriangleCount; t++)
        {
            int a = ids[mesh.Indices[3 * t]], b = ids[mesh.Indices[3 * t + 1]], c = ids[mesh.Indices[3 * t + 2]];
            if (a == b || b == c || c == a) throw new XunitException($"Triangle {t} is degenerate.");
            foreach (var edge in new[] { (a, b), (b, c), (c, a) })
                directed[edge] = directed.GetValueOrDefault(edge) + 1;
        }
        foreach (var ((a, b), count) in directed)
        {
            if (count != 1) throw new XunitException($"Edge {a}->{b} is used {count} times (bad winding or non-manifold).");
            if (!directed.ContainsKey((b, a))) throw new XunitException($"Edge {a}->{b} has no partner: the mesh has a hole.");
        }
    }

    /// <summary>Each triangle's winding agrees with the normals stored on its corners.</summary>
    public static void WindingMatchesNormals(MeshData mesh)
    {
        for (int t = 0; t < mesh.TriangleCount; t++)
        {
            int a = mesh.Indices[3 * t], b = mesh.Indices[3 * t + 1], c = mesh.Indices[3 * t + 2];
            Vector3 face = Vector3.Cross(mesh.Positions[b] - mesh.Positions[a], mesh.Positions[c] - mesh.Positions[a]);
            Vector3 stored = mesh.Normals[a] + mesh.Normals[b] + mesh.Normals[c];
            if (Vector3.Dot(face, stored) <= 0) throw new XunitException($"Triangle {t} is wound against its normals.");
        }
    }

    /// <summary>For convex shapes: every normal points away from <paramref name="center"/>.</summary>
    public static void NormalsPointAwayFrom(MeshData mesh, Vector3 center)
    {
        for (int i = 0; i < mesh.VertexCount; i++)
        {
            Vector3 p = mesh.Positions[i];
            if (Vector3.Dot(mesh.Normals[i], p - center) <= 0)
                throw new XunitException($"Vertex {i} at {p} has normal {mesh.Normals[i]} pointing inward.");
        }
    }

    public static void UnitNormals(MeshData mesh)
    {
        for (int i = 0; i < mesh.VertexCount; i++)
            if (MathF.Abs(mesh.Normals[i].Length() - 1f) > 1e-4f) throw new XunitException($"Normal {i} is not unit length.");
    }

    /// <summary>Positive for a closed mesh whose faces point outward.</summary>
    public static float SignedVolume(MeshData mesh)
    {
        float sum = 0;
        for (int t = 0; t < mesh.TriangleCount; t++)
        {
            Vector3 a = mesh.Positions[mesh.Indices[3 * t]], b = mesh.Positions[mesh.Indices[3 * t + 1]], c = mesh.Positions[mesh.Indices[3 * t + 2]];
            sum += Vector3.Dot(a, Vector3.Cross(b, c));
        }
        return sum / 6f;
    }

    public static (Vector3 Min, Vector3 Max) Bounds(MeshData mesh)
    {
        Vector3 min = new(float.MaxValue), max = new(float.MinValue);
        foreach (Vector3 p in mesh.Positions)
        {
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }
        return (min, max);
    }

    public static void Near(Vector3 expected, Vector3 actual, float tolerance = 1e-4f)
    {
        if (Vector3.Distance(expected, actual) > tolerance) throw new XunitException($"Expected {expected}, got {actual}.");
    }

    /// <summary>Full check for a closed solid that is convex around <paramref name="center"/>.</summary>
    public static void ConvexSolid(MeshData mesh, Vector3 center)
    {
        Closed(mesh);
        WindingMatchesNormals(mesh);
        UnitNormals(mesh);
        NormalsPointAwayFrom(mesh, center);
        Assert.True(SignedVolume(mesh) > 0, "Signed volume should be positive.");
    }

    private static int[] WeldIds(MeshData mesh)
    {
        var lookup = new Dictionary<(long, long, long), int>();
        var ids = new int[mesh.VertexCount];
        for (int i = 0; i < ids.Length; i++)
        {
            Vector3 p = mesh.Positions[i];
            var key = ((long)MathF.Round(p.X / Weld), (long)MathF.Round(p.Y / Weld), (long)MathF.Round(p.Z / Weld));
            if (!lookup.TryGetValue(key, out int id))
            {
                id = lookup.Count;
                lookup[key] = id;
            }
            ids[i] = id;
        }
        return ids;
    }

    /// <summary>Largest angle (degrees) between the face normals of two triangles that share an edge (after welding).</summary>
    public static float MaxCreaseDegrees(MeshData mesh, float minY = float.NegativeInfinity)
    {
        int[] ids = WeldIds(mesh);
        var faces = new Dictionary<(int, int), Vector3>();
        float sharpest = 0;
        for (int t = 0; t < mesh.TriangleCount; t++)
        {
            int ia = mesh.Indices[3 * t], ib = mesh.Indices[3 * t + 1], ic = mesh.Indices[3 * t + 2];
            if (MathF.Min(mesh.Positions[ia].Y, MathF.Min(mesh.Positions[ib].Y, mesh.Positions[ic].Y)) < minY) continue;
            Vector3 normal = Vector3.Normalize(Vector3.Cross(mesh.Positions[ib] - mesh.Positions[ia], mesh.Positions[ic] - mesh.Positions[ia]));
            int a = ids[ia], b = ids[ib], c = ids[ic];
            foreach (var (u, v) in new[] { (a, b), (b, c), (c, a) })
            {
                if (faces.TryGetValue((v, u), out Vector3 other))
                    sharpest = MathF.Max(sharpest, MathF.Acos(Math.Clamp(Vector3.Dot(normal, other), -1f, 1f)) * 180f / MathF.PI);
                faces[(u, v)] = normal;
            }
        }
        return sharpest;
    }
}
