using System.Numerics;

namespace Tank.Client.Art.Meshes;

/// <summary>
/// Engine-neutral ground mask: 0 (grass) to 1 (dirt), baked into a square byte grid. The ground is mostly dirt; grass grows
/// on islands: one round, ragged-edged "oasis" around every given centre (the bush patches, so bushes always stand on grass)
/// plus a few seeded-noise islands elsewhere. <c>World.DirtMask</c> wraps it for Godot (texture upload); tests cover it here
/// without the engine.
/// </summary>
public sealed class DirtPatches
{
    public const int Resolution = 256;
    private const float OasisCore = 18f;      // all grass within this distance of an island centre...
    private const float OasisEdge = 26f;      // ...fading to dirt by this distance
    private const float EdgeFeature = 9f;     // metres per bump of the ragged oasis edge
    private const float EdgeWobble = 7f;      // how far (m) the bumps push the edge in or out
    private const float IslandFeature = 28f;  // metres per noise feature of the scattered islands
    private const float IslandFrom = 0.16f;   // noise value where a scattered island starts...
    private const float IslandTo = 0.30f;     // ...and where it is all grass
    private const uint EdgeSeedOffset = 0x5EED;
    private const uint IslandSeedOffset = 1616;   // picked so the sandbox spawn and the start of its lane are bare dirt
    private readonly byte[] _values = new byte[Resolution * Resolution];

    public DirtPatches(float sizeX, float sizeZ, IReadOnlyList<Vector2> grassIslands, uint seed)
    {
        SizeX = sizeX;
        SizeZ = sizeZ;
        for (int j = 0; j < Resolution; j++)
        for (int i = 0; i < Resolution; i++)
        {
            var p = new Vector2((i + 0.5f) / Resolution * sizeX, (j + 0.5f) / Resolution * sizeZ);
            float grass = MathF.Max(Oasis(p, grassIslands, seed), NoiseIsland(p, seed));
            _values[j * Resolution + i] = (byte)MathF.Round((1f - grass) * 255f);
        }
    }

    public float SizeX { get; }
    public float SizeZ { get; }

    /// <summary>The baked bytes, row-major (row = Z), one byte per texel.</summary>
    public byte[] ToBytes() => (byte[])_values.Clone();

    /// <summary>Bilinear lookup matching the GPU's linear filter.</summary>
    public float At(float x, float z)
    {
        float fx = Math.Clamp(x / SizeX * Resolution - 0.5f, 0f, Resolution - 1.001f);
        float fz = Math.Clamp(z / SizeZ * Resolution - 0.5f, 0f, Resolution - 1.001f);
        int i = (int)fx, j = (int)fz;
        float u = fx - i, v = fz - j;
        float Px(int a, int b) => _values[b * Resolution + a] / 255f;
        float near = Lerp(Px(i, j), Px(i + 1, j), u), far = Lerp(Px(i, j + 1), Px(i + 1, j + 1), u);
        return Lerp(near, far, v);
    }

    /// <summary>1 near an island centre, fading to 0 across a noise-wobbled ring so the island isn't a perfect circle.</summary>
    private static float Oasis(Vector2 p, IReadOnlyList<Vector2> centres, uint seed)
    {
        if (centres.Count == 0) return 0f;
        float nearest = float.MaxValue;
        foreach (Vector2 c in centres) nearest = MathF.Min(nearest, Vector2.Distance(p, c));
        if (nearest > OasisEdge + EdgeWobble) return 0f;
        float wobble = SeededNoise.Fbm(new Vector3(p.X / EdgeFeature, p.Y / EdgeFeature, 0.5f), seed + EdgeSeedOffset, 3);
        return 1f - SmoothStep(OasisCore, OasisEdge, nearest + wobble * EdgeWobble);
    }

    private static float NoiseIsland(Vector2 p, uint seed) =>
        SmoothStep(IslandFrom, IslandTo, SeededNoise.Fbm(new Vector3(p.X / IslandFeature, p.Y / IslandFeature, 0.5f), seed + IslandSeedOffset, 4));

    private static float SmoothStep(float from, float to, float x)
    {
        float t = Math.Clamp((x - from) / (to - from), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
