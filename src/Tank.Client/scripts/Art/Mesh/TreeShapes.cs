using System.Numerics;

namespace Tank.Client.Art.Meshes;

/// <summary>
/// Cartoon background trees, rooted at the origin and about 7–9 m tall at scale 1, leaning a little.
/// Broadleaf: a warm brown trunk under 3–5 smooth, slightly lumpy green balls. Conifer: 3–4 stacked cones whose rims
/// are bevelled round. Vertex colour RGB is the base colour (linear) already darkened towards the ground (cheap
/// ambient occlusion); A is how much the part sways in the wind (0 trunk, up to 1 at the crown).
/// </summary>
public static class TreeShapes
{
    public const int BlobSubdivisions = 1;
    private const int TierSides = 8;
    private const int TrunkSides = 6;
    private const float TreeHeight = 8.5f;
    private const float MaxLean = 0.06f;
    private const float AoFloor = 0.6f;                     // brightness at the foot of the tree
    private const float BlobLumps = 0.06f;
    private static readonly Vector3 Bark = Palette.Linear(Palette.Bark);
    private static readonly Vector3 Needles = Palette.Linear(Palette.Needles);
    private static readonly Vector3 Leaves = Palette.Linear(Palette.Leaves);

    public static MeshData Conifer(uint seed)
    {
        float Rnd(int a, int b) => SeededNoise.Hash01(a, b, 0, seed);
        var parts = new List<(MeshData, Matrix4x4)> { Part(Trunk(0.24f, 2.4f), Bark, 1f, 0f, Matrix4x4.Identity) };
        int tiers = 3 + (int)(SeededNoise.Hash(9, 9, 9, seed) % 2);
        float y = 1.6f, radius = 2.6f, height = 3.4f;
        for (int tier = 0; tier < tiers; tier++)
        {
            float r = radius * (0.9f + 0.2f * Rnd(tier, 0));
            var place = Matrix4x4.CreateRotationY(Rnd(tier, 1) * MathF.Tau)
                * Matrix4x4.CreateTranslation((Rnd(tier, 3) - 0.5f) * 0.3f, y, (Rnd(tier, 4) - 0.5f) * 0.3f);
            parts.Add(Part(Tier(r, height * (0.9f + 0.2f * Rnd(tier, 5))), Needles, 0.9f + 0.2f * Rnd(tier, 6), y, place));
            y += height * 0.55f;
            radius *= 0.75f;
            height *= 0.85f;
        }
        return Lean(parts, seed);
    }

    public static MeshData Broadleaf(uint seed)
    {
        float Rnd(int a, int b) => SeededNoise.Hash01(a, b, 0, seed);
        var parts = new List<(MeshData, Matrix4x4)> { Part(Trunk(0.28f, 4.4f), Bark, 1f, 0f, Matrix4x4.Identity) };
        int blobs = 3 + (int)(SeededNoise.Hash(7, 7, 7, seed) % 3);
        for (int blob = 0; blob < blobs; blob++)
        {
            float r = blob == 0 ? 2.0f : 1.3f + 0.5f * Rnd(blob, 0);
            float angle = blob * MathF.Tau / Math.Max(blobs - 1, 1) + Rnd(blob, 1) * 0.8f;
            float spread = blob == 0 ? 0f : 1.2f + 0.4f * Rnd(blob, 3);
            float cy = blob == 0 ? 5.4f : 4.7f + 0.9f * Rnd(blob, 4);
            var offset = new Vector3(MathF.Cos(angle) * spread, cy, MathF.Sin(angle) * spread);
            parts.Add(Part(Blob(r, seed + (uint)blob * 31u), Leaves, 0.9f + 0.2f * Rnd(blob, 2), cy, Matrix4x4.CreateTranslation(offset)));
        }
        return Lean(parts, seed);
    }

    /// <summary>One conifer tier of base radius <paramref name="radius"/>: a cone whose rim is bevelled round.</summary>
    internal static MeshData Tier(float radius, float height) => Lathe.Build(new[]
    {
        new Vector2(0, 0), new Vector2(radius * 0.85f, 0.03f * height), new Vector2(radius * 0.98f, 0.09f * height),
        new Vector2(radius * 0.97f, 0.15f * height), new Vector2(radius * 0.85f, 0.24f * height), new Vector2(0, height),
    }, TierSides, smoothAngleDeg: 75f);

    /// <summary>A smooth, softly lumpy ball for a broadleaf crown.</summary>
    private static MeshData Blob(float radius, uint seed)
    {
        MeshData sphere = Icosphere.Build(BlobSubdivisions);
        var positions = new Vector3[sphere.VertexCount];
        for (int i = 0; i < positions.Length; i++)
        {
            Vector3 d = sphere.Positions[i];
            positions[i] = d * radius * new Vector3(1f, 0.85f, 1f) * (1f + BlobLumps * SeededNoise.Fbm(d * 1.4f, seed, 2));
        }
        Vector3[] normals = RockShape.SmoothNormals(positions, sphere.Indices);
        var b = new MeshBuilder();
        for (int i = 0; i < positions.Length; i++) b.AddVertex(positions[i], normals[i], sphere.Uvs[i]);
        for (int t = 0; t < sphere.Indices.Count; t += 3) b.AddTriangle(sphere.Indices[t], sphere.Indices[t + 1], sphere.Indices[t + 2]);
        return b.Build();
    }

    /// <summary>Colours a part: base colour × brightness variation × height-based darkening.</summary>
    private static (MeshData, Matrix4x4) Part(MeshData mesh, Vector3 baseColor, float variation, float partY, Matrix4x4 place)
    {
        bool isBark = baseColor == Bark;
        Vector3 tinted = baseColor * (isBark ? 1f : variation);
        MeshData coloured = mesh.WithColors(p =>
        {
            float height = Math.Clamp((partY + p.Y) / TreeHeight, 0f, 1f);
            float ao = AoFloor + (1f - AoFloor) * height;
            return new Vector4(tinted * ao, isBark ? 0f : Math.Clamp(height, 0.15f, 1f));
        });
        return (coloured, place);
    }

    private static MeshData Lean(List<(MeshData Mesh, Matrix4x4 Transform)> parts, uint seed)
    {
        float tilt = (SeededNoise.Hash01(0, 0, 7, seed) - 0.5f) * 2f * MaxLean;
        float bearing = SeededNoise.Hash01(0, 1, 7, seed) * MathF.Tau;
        var lean = Matrix4x4.CreateFromAxisAngle(new Vector3(MathF.Cos(bearing), 0, MathF.Sin(bearing)), tilt);
        return Merge.Build(parts.Select(p => (p.Mesh, p.Transform * lean)));
    }

    private static MeshData Trunk(float radius, float height) =>
        Lathe.Build(new[] { new Vector2(radius, -0.3f), new Vector2(radius * 0.65f, height) }, TrunkSides);
}
