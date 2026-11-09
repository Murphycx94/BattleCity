namespace Tank.Sim.Maps.Building;

/// <summary>
/// Seeded value noise: hashed values on the integer lattice, smoothly interpolated between (the same idea as
/// <see cref="Terrain.TerrainGenerator"/>'s, which stays as it is). Integer and float arithmetic only, so the same seed
/// and point give the same value on every machine.
/// </summary>
public static class ValueNoise
{
    /// <summary>Each octave's seed is the previous one's plus this (as in <see cref="Terrain.TerrainGenerator"/>).</summary>
    private const uint OctaveSeedStep = 1013;

    /// <summary>Noise in [0, 1) with one feature per unit of <paramref name="x"/> and <paramref name="z"/>.</summary>
    public static float Value(uint seed, float x, float z)
    {
        float fx = MathF.Floor(x), fz = MathF.Floor(z);
        int xi = (int)fx, zi = (int)fz;
        float u = Smooth(x - fx), v = Smooth(z - fz);
        float near = Hash(seed, xi, zi) * (1 - u) + Hash(seed, xi + 1, zi) * u;
        float far = Hash(seed, xi, zi + 1) * (1 - u) + Hash(seed, xi + 1, zi + 1) * u;
        return near * (1 - v) + far * v;
    }

    /// <summary>
    /// Fractal noise in [0, 1]: <paramref name="octaves"/> layers of <see cref="Value"/>, each twice as fine and half as
    /// strong as the last, normalised by their total weight.
    /// </summary>
    public static float Fbm(uint seed, float x, float z, int octaves)
    {
        if (octaves < 1) throw new ArgumentOutOfRangeException(nameof(octaves), octaves, "Need at least one octave.");
        float sum = 0, amplitude = 1, norm = 0;
        uint octaveSeed = seed;
        for (int octave = 0; octave < octaves; octave++)
        {
            sum += Value(octaveSeed, x, z) * amplitude;
            norm += amplitude;
            amplitude *= 0.5f;
            x *= 2;
            z *= 2;
            unchecked { octaveSeed += OctaveSeedStep; }
        }
        return sum / norm;
    }

    /// <summary><see cref="Fbm"/> stretched to [−1, 1].</summary>
    public static float Signed(uint seed, float x, float z, int octaves) => Fbm(seed, x, z, octaves) * 2 - 1;

    private static float Smooth(float t) => t * t * (3 - 2 * t);

    /// <summary>Integer hash of a lattice point to [0, 1).</summary>
    private static float Hash(uint seed, int x, int z)
    {
        unchecked
        {
            uint h = seed * 374761393u + (uint)x * 668265263u + (uint)z * 2246822519u;
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777216f;
        }
    }
}
