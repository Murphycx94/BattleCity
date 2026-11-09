using System.Numerics;
using Tank.Sim.Geometry;
using L = Tank.Sim.Maps.Riverland.RiverlandLayout;

namespace Tank.Sim.Maps.Riverland;

/// <summary>What the rocks, bushes and trees share: the playable area, the roads, dry ground and drawing points.</summary>
internal sealed partial class ScatterBuilder
{
    // ================= shared =================

    /// <summary>A circle (or box) of <paramref name="reach"/> at <paramref name="p"/> lies inside the playable area.</summary>
    private static bool Inside(Vector2 p, float reach) =>
        p.X >= Edge + reach && p.Y >= Edge + reach && p.X <= Size - Edge - reach && p.Y <= Size - Edge - reach;

    /// <summary>How far from the edge of the nearest road or track, from the distance grids (+∞ beyond their reach).</summary>
    private float RoadEdge(Vector2 p) =>
        MathF.Min(_shape.Roads.DistanceAt(p.X, p.Y) - L.RoadHalfWidth, _shape.Tracks.DistanceAt(p.X, p.Y) - L.TrackHalfWidth);

    /// <summary>How far from the nearest road's or track's centre line.</summary>
    private float RoadCentre(Vector2 p) => MathF.Min(_shape.Roads.DistanceAt(p.X, p.Y), _shape.Tracks.DistanceAt(p.X, p.Y));

    /// <summary>Every height sample in the box <paramref name="p"/> ± <paramref name="half"/> (and the cells round its edges) is dry.</summary>
    private bool IsDry(Vector2 p, Vector2 half)
    {
        int i0 = Math.Max(0, (int)MathF.Floor((p.X - half.X) / Cell)), i1 = Math.Min(N - 1, (int)MathF.Ceiling((p.X + half.X) / Cell));
        int j0 = Math.Max(0, (int)MathF.Floor((p.Y - half.Y) / Cell)), j1 = Math.Min(N - 1, (int)MathF.Ceiling((p.Y + half.Y) / Cell));
        for (int j = j0; j <= j1; j++)
        for (int i = i0; i <= i1; i++)
            if (_terrain.Sample(i, j) < RiverlandMap.WaterLevel + DryHeight) return false;
        return true;
    }

    /// <summary>A point uniformly in the box [<paramref name="min"/>, <paramref name="max"/>).</summary>
    private static Vector2 Lerp(Vector2 min, Vector2 max, ref XorShift rng) =>
        min + (max - min) * new Vector2(rng.NextFloat(), rng.NextFloat());

    /// <summary>A point uniformly in the unit disc, by rejection (no trigonometry, so it reproduces exactly).</summary>
    private static Vector2 InDisc(ref XorShift rng)
    {
        while (true)
        {
            var d = new Vector2(rng.NextFloat() * 2 - 1, rng.NextFloat() * 2 - 1);
            if (d.LengthSquared() <= 1) return d;
        }
    }
}
