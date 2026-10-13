namespace Tank.Sim.Terrain;

/// <summary>Seeded fractal value noise plus round hills. Same inputs give the same terrain on every machine.</summary>
public static class TerrainGenerator
{
    private const float NoiseScale = 60f;
    private const int Octaves = 4;

    /// <summary>Smooth bump: full <see cref="Height"/> at the centre, zero at <see cref="Radius"/>.</summary>
    public readonly record struct Hill(float X, float Z, float Radius, float Height);

    public static HeightField Generate(int seed, int samples, float cellSize, float roughness, IReadOnlyList<Hill> hills)
    {
        var heights = new float[samples * samples];
        for (int j = 0; j < samples; j++)
        for (int i = 0; i < samples; i++)
        {
            float x = i * cellSize, z = j * cellSize;
            float h = Fractal(seed, x / NoiseScale, z / NoiseScale) * roughness;
            for (int k = 0; k < hills.Count; k++) h += HillHeight(hills[k], x, z);
            heights[j * samples + i] = h;
        }
        return new HeightField(samples, samples, cellSize, heights);
    }

    private static float HillHeight(in Hill hill, float x, float z)
    {
        float dx = x - hill.X, dz = z - hill.Z;
        float d = MathF.Sqrt(dx * dx + dz * dz) / hill.Radius;
        if (d >= 1) return 0;
        float s = 1 - d * d;
        return hill.Height * s * s;
    }

    private static float Fractal(int seed, float x, float z)
    {
        float sum = 0, amplitude = 1, norm = 0;
        for (int octave = 0; octave < Octaves; octave++)
        {
            sum += Value(seed + octave * 1013, x, z) * amplitude;
            norm += amplitude;
            amplitude *= 0.5f;
            x *= 2;
            z *= 2;
        }
        return sum / norm;
    }

    private static float Value(int seed, float x, float z)
    {
        int xi = (int)MathF.Floor(x), zi = (int)MathF.Floor(z);
        float u = Smooth(x - xi), v = Smooth(z - zi);
        float near = Hash(seed, xi, zi) * (1 - u) + Hash(seed, xi + 1, zi) * u;
        float far = Hash(seed, xi, zi + 1) * (1 - u) + Hash(seed, xi + 1, zi + 1) * u;
        return near * (1 - v) + far * v;
    }

    private static float Smooth(float t) => t * t * (3 - 2 * t);

    /// <summary>Integer hash to [0, 1).</summary>
    private static float Hash(int seed, int x, int z)
    {
        unchecked
        {
            uint h = (uint)seed * 374761393u + (uint)x * 668265263u + (uint)z * 2246822519u;
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777216f;
        }
    }
}
