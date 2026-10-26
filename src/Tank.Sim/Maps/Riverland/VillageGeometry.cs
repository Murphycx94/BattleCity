using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Maps.Building;
using L = Tank.Sim.Maps.Riverland.RiverlandLayout;

namespace Tank.Sim.Maps.Riverland;

/// <summary>The geometry <see cref="VillageBuilder"/> measures plots, houses and walls with: boxes, samples, roads and ramps.</summary>
internal static class VillageGeometry
{
    private const int N = RiverlandMap.Samples;
    private const float Cell = RiverlandMap.CellSize;

    /// <summary>How far a road's or track's edge lies from its centre line.</summary>
    public static float HalfWidth(RoadLine road) => road.IsTrack ? L.TrackHalfWidth : L.RoadHalfWidth;

    /// <summary>
    /// How near the box's outline (every metre round it) comes to a road's or track's edge, from the distance grids
    /// <paramref name="roads"/> and <paramref name="tracks"/> (+∞ beyond their reach).
    /// </summary>
    public static float RoadEdgeDistance(in Obb2 box, DistanceGrid roads, DistanceGrid tracks)
    {
        Vector2[] corners = Corners(box);
        float best = float.PositiveInfinity;
        for (int k = 0; k < 4; k++)
        {
            Vector2 a = corners[k], b = corners[(k + 1) % 4];
            int steps = Math.Max(1, (int)MathF.Ceiling(Vector2.Distance(a, b)));
            for (int s = 0; s < steps; s++)
            {
                Vector2 p = Vector2.Lerp(a, b, s / (float)steps);
                best = MathF.Min(best, MathF.Min(roads.DistanceAt(p.X, p.Y) - L.RoadHalfWidth, tracks.DistanceAt(p.X, p.Y) - L.TrackHalfWidth));
            }
        }
        return best;
    }

    /// <summary>The road or track whose centre line is nearest <paramref name="p"/> (exactly), and how far along it.</summary>
    public static (RoadLine Road, float Along) NearestRoad(Vector2 p)
    {
        RoadLine best = L.AllRoads[0];
        float bestDistance = float.PositiveInfinity, bestAlong = 0;
        foreach (RoadLine road in L.AllRoads)
        {
            float d = road.Line.DistanceTo(p, out float along);
            if (d < bestDistance) (best, bestDistance, bestAlong) = (road, d, along);
        }
        return (best, bestAlong);
    }

    /// <summary>On a ramp up the plateau, its sides or the ground just past its ends.</summary>
    public static bool NearRamp(Vector2 p)
    {
        foreach (Ramp ramp in L.Ramps)
        {
            (float along, float across) = ramp.Locate(p);
            if (across <= L.RampHalfWidth + L.RampSide + 3 && along >= -L.RampLanding - 3 && along <= ramp.Length + L.RampFoot + 3) return true;
        }
        return false;
    }

    /// <summary>The indices of the height-field samples inside <paramref name="box"/>.</summary>
    public static IEnumerable<int> SamplesIn(Obb2 box)
    {
        float rx = box.Radius(Vector2.UnitX), rz = box.Radius(Vector2.UnitY);
        int i0 = Math.Max(0, (int)MathF.Ceiling((box.Center.X - rx) / Cell)), i1 = Math.Min(N - 1, (int)MathF.Floor((box.Center.X + rx) / Cell));
        int j0 = Math.Max(0, (int)MathF.Ceiling((box.Center.Y - rz) / Cell)), j1 = Math.Min(N - 1, (int)MathF.Floor((box.Center.Y + rz) / Cell));
        for (int j = j0; j <= j1; j++)
        for (int i = i0; i <= i1; i++)
            if (box.Contains(new Vector2(i * Cell, j * Cell))) yield return j * N + i;
    }

    /// <summary>The box's four corners, going round it.</summary>
    public static Vector2[] Corners(in Obb2 box)
    {
        Vector2 r = box.AxisRight * box.HalfExtents.X, f = box.AxisForward * box.HalfExtents.Y;
        return new[] { box.Center - r - f, box.Center + r - f, box.Center + r + f, box.Center - r + f };
    }

    /// <summary>The shortest distance between two boxes (0 when they overlap): in 2D, from a corner of one to the other.</summary>
    public static float Distance(in Obb2 a, in Obb2 b)
    {
        if (Obb2.TryGetPenetration(a, b, out _)) return 0;
        float best = float.PositiveInfinity;
        foreach (Vector2 corner in Corners(a)) best = MathF.Min(best, b.DistanceToPoint(corner));
        foreach (Vector2 corner in Corners(b)) best = MathF.Min(best, a.DistanceToPoint(corner));
        return best;
    }
}
