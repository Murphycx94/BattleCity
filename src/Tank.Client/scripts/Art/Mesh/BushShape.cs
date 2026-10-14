using System.Numerics;

namespace Tank.Client.Art.Meshes;

/// <summary>
/// A tall cartoon bush of unit size (radius 1, height 1, rooted at y = 0); the bush view scales each instance to its
/// prop's radius and height. 30–50 broad leaves rise from near the middle and arch outward, around a "fluffy core" of
/// one or two softly lumpy balls that blocks the view through the gaps.
/// Normals all point away from one shading centre, so the whole bush lights like a single soft ball.
/// Vertex colour: R = height along the leaf / up the core (0 root → 1 top, for the colour gradient and wind),
/// G = per-leaf shade in [0, 1], B = 1 on leaves and 0 on the core (the core barely sways), A = 1.
/// </summary>
public static class BushShape
{
    public const int LeafRows = 3;
    public const int VerticesPerLeaf = LeafRows * 2 + 1;
    public const int CoreSubdivisions = 2;
    public const float ShadingCentreY = 0.3f;

    private const int MinLeaves = 30, MaxLeaves = 50;
    private const float LeafWidth = 0.3f;
    private const float LeafTaper = 0.45f;
    private const float CoreY = 0.42f;
    private const float CoreLumps = 0.07f;

    public static MeshData Build(uint seed)
    {
        var b = new MeshBuilder();
        int leaves = MinLeaves + (int)(SeededNoise.Hash(1, 2, 3, seed) % (MaxLeaves - MinLeaves + 1));
        for (int i = 0; i < leaves; i++) AddLeaf(b, i, seed);
        int balls = 1 + (int)(SeededNoise.Hash(4, 5, 6, seed) % 2);
        for (int k = 0; k < balls; k++)
        {
            float R(int n) => SeededNoise.Hash01(k, n, 9, seed);
            float halfWidth = 0.72f + 0.06f * R(0), halfHeight = 0.41f + 0.04f * R(1);
            float angle = R(2) * MathF.Tau, offset = balls == 1 ? 0f : 0.12f;
            var at = new Vector3(MathF.Cos(angle) * offset, CoreY, MathF.Sin(angle) * offset);
            MeshData ball = Core(halfWidth, halfHeight, seed + (uint)k * 101u).Transformed(Matrix4x4.CreateTranslation(at));
            int first = b.VertexCount;
            for (int v = 0; v < ball.VertexCount; v++)
            {
                Vector3 p = ball.Positions[v];
                b.AddVertex(p, Shading(p), ball.Uvs[v], new Vector4(Math.Clamp(p.Y, 0f, 1f), 0.5f, 0f, 1f));
            }
            for (int t = 0; t < ball.Indices.Count; t += 3)
                b.AddTriangle(first + ball.Indices[t], first + ball.Indices[t + 1], first + ball.Indices[t + 2]);
        }
        return b.Build();
    }

    /// <summary>A closed, softly lumpy ellipsoid centred on the origin (smooth normals).</summary>
    public static MeshData Core(float halfWidth, float halfHeight, uint seed)
    {
        MeshData sphere = Icosphere.Build(CoreSubdivisions);
        var positions = new Vector3[sphere.VertexCount];
        for (int i = 0; i < positions.Length; i++)
        {
            Vector3 d = sphere.Positions[i];
            positions[i] = d * new Vector3(halfWidth, halfHeight, halfWidth) * (1f + CoreLumps * SeededNoise.Fbm(d * 1.7f, seed, 2));
        }
        Vector3[] normals = RockShape.SmoothNormals(positions, sphere.Indices);
        var b = new MeshBuilder();
        for (int i = 0; i < positions.Length; i++) b.AddVertex(positions[i], normals[i], sphere.Uvs[i]);
        for (int t = 0; t < sphere.Indices.Count; t += 3) b.AddTriangle(sphere.Indices[t], sphere.Indices[t + 1], sphere.Indices[t + 2]);
        return b.Build();
    }

    /// <summary>One broad leaf: rows of vertex pairs climbing and arching outward, closed by a blunt tip vertex.</summary>
    private static void AddLeaf(MeshBuilder b, int index, uint seed)
    {
        float R(int n) => SeededNoise.Hash01(index, n, 1, seed);
        float angle = (index + R(0) * 0.8f) * 2.39996f;   // golden-angle spiral so leaves spread evenly round the bush
        var outward = new Vector3(MathF.Cos(angle), 0, MathF.Sin(angle));
        var across = new Vector3(-outward.Z, 0, outward.X);
        float rootRadius = 0.08f + 0.27f * R(1), tipRadius = 0.6f + 0.33f * R(2);
        float height = 0.65f + 0.35f * R(3);
        float shade = R(4);
        float twist = (R(5) - 0.5f) * 0.6f;   // tilt the blade a little about its own axis
        Vector3 Spine(float t) => outward * (rootRadius + (tipRadius - rootRadius) * MathF.Pow(t, 1.4f)) + Vector3.UnitY * (height * t * (1.15f - 0.15f * t));

        int first = b.VertexCount;
        for (int row = 0; row < LeafRows; row++)
        {
            float t = MathF.Sin((float)row / LeafRows * MathF.PI / 2);
            float half = LeafWidth * (1 - LeafTaper * t * t) / 2;
            Vector3 side = Vector3.Normalize(across + Vector3.UnitY * twist) * half;
            Vector3 left = Spine(t) - side, right = Spine(t) + side;
            var color = new Vector4(t, shade, 1f, 1f);
            b.AddVertex(left, Shading(left), new Vector2(0, t), color);
            b.AddVertex(right, Shading(right), new Vector2(1, t), color);
        }
        Vector3 tip = Spine(1f);
        int tipIndex = b.AddVertex(tip, Shading(tip), new Vector2(0.5f, 1f), new Vector4(1f, shade, 1f, 1f));
        for (int row = 0; row < LeafRows - 1; row++)
        {
            int l0 = first + row * 2;
            b.AddQuad(l0, l0 + 1, l0 + 3, l0 + 2);
        }
        int last = first + (LeafRows - 1) * 2;
        b.AddTriangle(last, last + 1, tipIndex);
    }

    private static Vector3 Shading(Vector3 p)
    {
        Vector3 d = p - new Vector3(0, ShadingCentreY, 0);
        return d.LengthSquared() < 1e-8f ? Vector3.UnitY : Vector3.Normalize(d);
    }
}
