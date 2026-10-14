using System.Numerics;

namespace Tank.Client.Art.Meshes;

/// <summary>
/// Smooth cartoon boulder: an icosphere blown out into a rounded box (a superellipsoid, so there are no creases), given
/// a few soft lumps by seeded noise, then scaled so its bounding box is exactly ±<c>halfExtents</c>. The fit is exact,
/// so the visible rock always matches the collision box.
/// </summary>
public static class RockShape
{
    /// <summary>Raw shape width / collision width; the lumpy surface ends up at roughly 0.88–1.05 of the raw box.</summary>
    public const float Fit = 1.05f;
    private const float MinRoundness = 2.6f;     // superellipsoid exponent range: 2 = sphere, higher = boxier with rounded edges
    private const float MaxRoundness = 3.4f;
    private const float BasePull = 0.3f;        // below the equator |y| becomes |y|^BasePull: a flatter base that fills the box at the ground line
    private const float PullStart = 0.35f;       // depth below the equator (fraction of the half height) over which the pull eases in
    private const float LumpAmplitude = 0.075f;
    private const float LumpFrequency = 1.3f;

    public static MeshData Build(Vector3 halfExtents, uint seed, int subdivisions = 3)
    {
        if (halfExtents.X <= 0 || halfExtents.Y <= 0 || halfExtents.Z <= 0)
            throw new ArgumentOutOfRangeException(nameof(halfExtents), "Half extents must be positive.");
        float roundness = MinRoundness + (MaxRoundness - MinRoundness) * SeededNoise.Hash01(1, 2, 3, seed);
        MeshData sphere = Icosphere.Build(subdivisions);
        var raw = new Vector3[sphere.VertexCount];
        Vector3 min = new(float.MaxValue), max = new(float.MinValue);
        for (int i = 0; i < raw.Length; i++)
        {
            Vector3 d = sphere.Positions[i];
            float norm = MathF.Pow(MathF.Pow(MathF.Abs(d.X), roundness) + MathF.Pow(MathF.Abs(d.Y), roundness) + MathF.Pow(MathF.Abs(d.Z), roundness), 1f / roundness);
            Vector3 boxy = d / norm;
            // Flatten the base: below the equator, push points down so the lower half is fuller and fills the box near the ground.
            if (boxy.Y < 0)
            {
                float depth = -boxy.Y;
                float pull = MathF.Min(depth / PullStart, 1f);
                boxy.Y = -(depth + (MathF.Pow(depth, BasePull) - depth) * pull * pull * (3 - 2 * pull));   // eased in, so no crease at the equator
            }
            float bump = 1f + LumpAmplitude * SeededNoise.Fbm(d * LumpFrequency, seed, 2);
            raw[i] = boxy * bump;
            min = Vector3.Min(min, raw[i]);
            max = Vector3.Max(max, raw[i]);
        }
        Vector3 center = (min + max) * 0.5f, half = (max - min) * 0.5f;
        var positions = new Vector3[raw.Length];
        for (int i = 0; i < raw.Length; i++) positions[i] = (raw[i] - center) / half * halfExtents;

        var normals = SmoothNormals(positions, sphere.Indices);
        var builder = new MeshBuilder();
        for (int i = 0; i < positions.Length; i++) builder.AddVertex(positions[i], normals[i], sphere.Uvs[i], new Vector4((positions[i].Y / halfExtents.Y + 1) * 0.5f, 0, 0, 1));   // R: 0 at the base, 1 at the top
        for (int t = 0; t < sphere.Indices.Count; t += 3)
            builder.AddTriangle(sphere.Indices[t], sphere.Indices[t + 1], sphere.Indices[t + 2]);
        return builder.Build();
    }

    /// <summary>Area-weighted average of the faces around each vertex.</summary>
    public static Vector3[] SmoothNormals(IReadOnlyList<Vector3> positions, IReadOnlyList<int> indices)
    {
        var sums = new Vector3[positions.Count];
        for (int t = 0; t < indices.Count; t += 3)
        {
            int a = indices[t], b = indices[t + 1], c = indices[t + 2];
            Vector3 face = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
            sums[a] += face;
            sums[b] += face;
            sums[c] += face;
        }
        for (int i = 0; i < sums.Length; i++) sums[i] = Vector3.Normalize(sums[i]);
        return sums;
    }
}
