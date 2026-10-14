using System.Numerics;

namespace Tank.Client.Art.Meshes;

/// <summary>
/// Seeded value noise. Integer hashing only, so the same seed gives the same shape on every machine, run after run.
/// </summary>
public static class SeededNoise
{
    public static uint Hash(int x, int y, int z, uint seed)
    {
        unchecked
        {
            uint h = seed ^ 0x9E3779B9u;
            h = Mix(h ^ (uint)x * 0x85EBCA6Bu);
            h = Mix(h ^ (uint)y * 0xC2B2AE35u);
            h = Mix(h ^ (uint)z * 0x27D4EB2Fu);
            return h;
        }
    }

    /// <summary>Uniform in [0, 1).</summary>
    public static float Hash01(int x, int y, int z, uint seed) => (Hash(x, y, z, seed) >> 8) / 16777216f;

    /// <summary>Smooth noise in [−1, 1], one feature per unit.</summary>
    public static float Value(Vector3 p, uint seed)
    {
        int x0 = (int)MathF.Floor(p.X), y0 = (int)MathF.Floor(p.Y), z0 = (int)MathF.Floor(p.Z);
        float fx = Fade(p.X - x0), fy = Fade(p.Y - y0), fz = Fade(p.Z - z0);
        float Corner(int dx, int dy, int dz) => Hash01(x0 + dx, y0 + dy, z0 + dz, seed) * 2f - 1f;
        float x00 = Lerp(Corner(0, 0, 0), Corner(1, 0, 0), fx), x10 = Lerp(Corner(0, 1, 0), Corner(1, 1, 0), fx);
        float x01 = Lerp(Corner(0, 0, 1), Corner(1, 0, 1), fx), x11 = Lerp(Corner(0, 1, 1), Corner(1, 1, 1), fx);
        return Lerp(Lerp(x00, x10, fy), Lerp(x01, x11, fy), fz);
    }

    /// <summary>Sum of <paramref name="octaves"/> layers, each twice the frequency and half the weight; stays in [−1, 1].</summary>
    public static float Fbm(Vector3 p, uint seed, int octaves)
    {
        float sum = 0, weight = 0.5f, total = 0;
        for (int o = 0; o < octaves; o++)
        {
            sum += Value(p, seed + (uint)o * 1013u) * weight;
            total += weight;
            p *= 2.03f;
            weight *= 0.5f;
        }
        return sum / total;
    }

    private static uint Mix(uint h)
    {
        unchecked
        {
            h ^= h >> 16;
            h *= 0x7FEB352Du;
            h ^= h >> 15;
            h *= 0x846CA68Bu;
            h ^= h >> 16;
            return h;
        }
    }

    private static float Fade(float t) => t * t * t * (t * (t * 6 - 15) + 10);

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
