using Tank.Sim.Maps;

namespace Tank.Sim.Tests;

/// <summary>
/// A map's fingerprint for the pinning tests: FNV-1a over what a match plays on, quantised so that it survives another
/// CPU or maths library (the generators use sines, cosines and arctangents, whose last bit may differ between Apple
/// silicon and Intel): <see cref="MapData.Of"/>'s heights in whole centimetres, ground kinds and water level, then each
/// prop's id, kind, footprint centre, half extents, bottom and height in whole millimetres and yaw in 1e-4 rad.
/// </summary>
/// <remarks>
/// Raw float bits would pin the map more tightly but only on one platform. A value within a rounding error of a half
/// millimetre (or centimetre) could still round the other way elsewhere; that is unlikely enough to accept.
/// </remarks>
internal static class MapFingerprint
{
    private const ulong FnvOffset = 14695981039346656037;
    private const ulong FnvPrime = 1099511628211;
    private const double PerMetre = 1000;      // millimetres
    private const double PerRadian = 10_000;   // 1e-4 rad

    public static ulong Of(Battlefield field)
    {
        MapData data = MapData.Of(field);
        ulong hash = FnvOffset;
        hash = Feed(hash, data.SamplesX);
        hash = Feed(hash, data.SamplesZ);
        hash = Feed(hash, Quantise(data.CellSize, PerMetre));
        foreach (int height in data.HeightsCm) hash = Feed(hash, height);
        foreach (SurfaceKind kind in data.Surface) hash = FeedByte(hash, (byte)kind);
        hash = FeedByte(hash, data.WaterLevelCm.HasValue ? (byte)1 : (byte)0);
        hash = Feed(hash, data.WaterLevelCm ?? 0);
        hash = Feed(hash, Quantise(data.Border, PerMetre));
        foreach (Prop p in data.Props)
        {
            hash = Feed(hash, p.Id);
            hash = Feed(hash, (int)p.Kind);
            hash = Feed(hash, Quantise(p.Footprint.Center.X, PerMetre));
            hash = Feed(hash, Quantise(p.Footprint.Center.Y, PerMetre));
            hash = Feed(hash, Quantise(p.Footprint.HalfExtents.X, PerMetre));
            hash = Feed(hash, Quantise(p.Footprint.HalfExtents.Y, PerMetre));
            hash = Feed(hash, Quantise(p.Footprint.Yaw, PerRadian));
            hash = Feed(hash, Quantise(p.Bottom, PerMetre));
            hash = Feed(hash, Quantise(p.Height, PerMetre));
        }
        return hash;
    }

    /// <summary><paramref name="value"/> in whole units of 1 / <paramref name="perUnit"/> (exact in double: a float times a small integer).</summary>
    private static int Quantise(float value, double perUnit) => checked((int)Math.Round(value * perUnit));

    /// <summary>The four bytes of <paramref name="value"/>, least significant first.</summary>
    private static ulong Feed(ulong hash, int value)
    {
        for (int shift = 0; shift < 32; shift += 8) hash = FeedByte(hash, (byte)(value >> shift));
        return hash;
    }

    private static ulong FeedByte(ulong hash, byte value) => (hash ^ value) * FnvPrime;
}
