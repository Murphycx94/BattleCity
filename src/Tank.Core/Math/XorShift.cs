namespace Tank.Core.Math;

/// <summary>Deterministic PRNG. State lives inside GameState so replays reproduce exactly.</summary>
public struct XorShift
{
    public uint State;

    public XorShift(uint seed) => State = seed == 0 ? 0x9E3779B9u : seed;

    public uint NextUInt()
    {
        uint x = State;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        State = x;
        return x;
    }

    /// <summary>Uniform integer in [0, max).</summary>
    public int Next(int max) => max <= 0 ? 0 : (int)(NextUInt() % (uint)max);

    /// <summary>True with probability num/den.</summary>
    public bool Chance(int num, int den) => Next(den) < num;
}
