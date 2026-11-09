using System.Numerics;

namespace Tank.Client.Art.Meshes;

/// <summary>
/// C# twin of the terrain shader's layer weights (<c>terrain_toon.gdshader</c>: slope, dirt patch and rock weights, including
/// their value-noise perturbation), so the things that grow on the ground (grass) stop where the drawn grass layer does.
/// The two slope limits are also sent to the shader as uniforms, so there is one source of truth for them.
/// The "dry hilltop" rule is not mirrored.
/// </summary>
public static class GroundCover
{
    public const float GrassMaxSlopeDegrees = 27f;
    public const float RockMinSlopeDegrees = 38f;

    private const float SlopeNoiseScale = 0.35f, SlopeNoiseDegrees = 6f;
    private const float DirtNoiseScale = 2.2f, DirtNoiseAmount = 0.45f;

    /// <summary>The rock layer fades in from this far below <see cref="RockMinSlopeDegrees"/> (noisy slope) to this far above it, degrees.</summary>
    private const float RockFadeBelow = 4f, RockFadeAbove = 3f;

    /// <summary>Weight (0..1) the terrain gives its grass layer at a point. <paramref name="normalY"/> is the surface normal's Y; <paramref name="dirtMask"/> the baked patch mask there.</summary>
    public static float GrassWeight(float x, float z, float normalY, float dirtMask)
    {
        float slope = NoisySlope(x, z, normalY);
        float dirtPatch = DirtPatch(dirtMask + (ValueNoise2(x * DirtNoiseScale, z * DirtNoiseScale) - 0.5f) * DirtNoiseAmount);
        float rock = RockOf(slope);
        float dirt = MathF.Max(SmoothStep(GrassMaxSlopeDegrees - 2f, GrassMaxSlopeDegrees + 6f, slope), dirtPatch) * (1f - rock);
        return MathF.Max(1f - dirt - rock, 0f);
    }

    /// <summary>
    /// Weight (0..1) the terrain gives its rock layer at a point: the same noisy slope <see cref="GrassWeight"/> reads, so
    /// the two agree on where rock takes over. <paramref name="normalY"/> is the surface normal's Y.
    /// </summary>
    public static float RockWeight(float x, float z, float normalY) => RockOf(NoisySlope(x, z, normalY));

    /// <summary>The slope in degrees, moved up or down by the shader's slope noise (±<see cref="SlopeNoiseDegrees"/>/2).</summary>
    private static float NoisySlope(float x, float z, float normalY) =>
        SlopeDegrees(normalY) + (ValueNoise2(x * SlopeNoiseScale, z * SlopeNoiseScale) - 0.5f) * SlopeNoiseDegrees;

    private static float RockOf(float noisySlope) => SmoothStep(RockMinSlopeDegrees - RockFadeBelow, RockMinSlopeDegrees + RockFadeAbove, noisySlope);

    /// <summary>
    /// The least and most grass weight flat ground can get for a dirt-mask value, over every value the patch noise takes:
    /// what the mask alone guarantees wherever the point is.
    /// </summary>
    internal static (float Least, float Most) FlatGrassWeightRange(float dirtMask) =>
        (1f - DirtPatch(dirtMask + DirtNoiseAmount / 2f), 1f - DirtPatch(dirtMask - DirtNoiseAmount / 2f));

    /// <summary>The dirt patch weight of a noisy mask value (the shader's <c>smoothstep(0.25, 0.95, dirt_patch + …)</c>).</summary>
    private static float DirtPatch(float noisyMask) => SmoothStep(0.25f, 0.95f, noisyMask);

    public static float SlopeDegrees(float normalY) => MathF.Acos(Math.Clamp(normalY, 0f, 1f)) * (180f / MathF.PI);

    public static float SmoothStep(float edge0, float edge1, float x)
    {
        float t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    /// <summary>The shader's <c>hash12</c> (common.gdshaderinc), float for float.</summary>
    public static float Hash12(float x, float y)
    {
        Vector3 p = new(Fract(x * 0.1031f), Fract(y * 0.1031f), Fract(x * 0.1031f));
        p += new Vector3(Vector3.Dot(p, new Vector3(p.Y, p.Z, p.X) + new Vector3(33.33f)));
        return Fract((p.X + p.Y) * p.Z);
    }

    /// <summary>The shader's <c>value_noise2</c>: smooth noise in [0, 1], one feature per unit.</summary>
    public static float ValueNoise2(float x, float y)
    {
        float ix = MathF.Floor(x), iy = MathF.Floor(y);
        float fx = x - ix, fy = y - iy;
        float ux = fx * fx * (3f - 2f * fx), uy = fy * fy * (3f - 2f * fy);
        float a = Hash12(ix, iy), b = Hash12(ix + 1, iy), c = Hash12(ix, iy + 1), d = Hash12(ix + 1, iy + 1);
        return Lerp(Lerp(a, b, ux), Lerp(c, d, ux), uy);
    }

    private static float Fract(float v) => v - MathF.Floor(v);

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
