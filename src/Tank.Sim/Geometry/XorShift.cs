namespace Tank.Sim.Geometry;

/// <summary>Tiny seeded generator (xorshift32). Identical sequence on every platform.</summary>
public struct XorShift
{
    private uint _state;

    public XorShift(uint seed) => _state = seed == 0 ? 0x9E3779B9u : seed;

    public uint NextUInt()
    {
        uint x = _state;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        _state = x;
        return x;
    }

    /// <summary>Uniform in [0, 1).</summary>
    public float NextFloat() => (NextUInt() >> 8) / 16777216f;
}
