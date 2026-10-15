namespace Tank.Sim.Combat;

/// <summary>
/// Stateless random numbers for one shot. The seed comes from (tick, tank id, shot index) and each use has its own salt,
/// so the same shot gives the same dispersion and rolls on every machine, in any order.
/// </summary>
public static class ShotRandom
{
    public const uint DispersionAngle = 1;
    public const uint DispersionRadius = 2;
    public const uint PenetrationRoll = 3;
    public const uint DamageRoll = 4;

    public static uint Seed(int tick, int tankId, int shotIndex) => Mix(Mix(Mix((uint)tick) + (uint)tankId) + (uint)shotIndex);

    /// <summary>Uniform in [0, 1).</summary>
    public static float Unit(uint seed, uint salt) => (Mix(seed ^ (salt * 0x9E3779B9u)) >> 8) / 16777216f;

    /// <summary>Uniform in [<paramref name="min"/>, <paramref name="max"/>]; float rounding can reach max.</summary>
    public static float Range(uint seed, uint salt, float min, float max) => min + (max - min) * Unit(seed, salt);

    /// <summary>An integer hash with good avalanche (Chris Wellons' lowbias32).</summary>
    private static uint Mix(uint x)
    {
        x ^= x >> 16;
        x *= 0x7FEB352Du;
        x ^= x >> 15;
        x *= 0x846CA68Bu;
        x ^= x >> 16;
        return x;
    }
}
