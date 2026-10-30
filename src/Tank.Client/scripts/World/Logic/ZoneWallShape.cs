using System.Numerics;
using Tank.Sim.Modes;

namespace Tank.Client.World;

/// <summary>
/// The light wall's shape on the ground (stage 6 design §3.3), engine-neutral: <see cref="Segments"/> columns round the
/// safe circle, each standing on the ground (or the water) under it, rising <see cref="Height"/> m and sunk
/// <see cref="Sink"/> m into it so slopes between two columns never open a gap at its foot. <see cref="SampleBase"/>
/// fills the heights the zone_wall shader lifts the columns by (a 256 × 1 texture); <c>ZoneWall</c> calls it only when
/// the circle moved more than half a metre.
/// </summary>
public static class ZoneWallShape
{
    public const int Segments = 256;

    /// <summary>m: the wall rises Height above the ground under it, its foot Sink below.</summary>
    public const float Height = 60f, Sink = 3f;

    /// <summary>m: a circle smaller than this (the zone closed) draws no wall.</summary>
    public const float MinRadius = 1f;

    /// <summary>
    /// The ground under each of the <see cref="Segments"/> points round <paramref name="circle"/>, into
    /// <paramref name="into"/> (length <see cref="Segments"/>): point k at centre + r × (cos 2πk/N, sin 2πk/N), from +X
    /// towards +Z like the wall's columns, its value groundAt(x, z). No allocation.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="into"/> is not <see cref="Segments"/> long.</exception>
    public static void SampleBase(Func<float, float, float> groundAt, in ZoneRing circle, Span<float> into)
    {
        if (into.Length != Segments) throw new ArgumentException($"The wall has {Segments} columns, not {into.Length}.", nameof(into));
        Vector2 centre = circle.Centre;
        float r = circle.Radius;
        for (int k = 0; k < Segments; k++)
        {
            float angle = MathF.Tau * k / Segments;
            into[k] = groundAt(centre.X + r * MathF.Cos(angle), centre.Y + r * MathF.Sin(angle));
        }
    }
}
