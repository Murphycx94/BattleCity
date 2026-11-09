using System.Numerics;
using Tank.Sim.Maps.Building;
using L = Tank.Sim.Maps.Riverland.RiverlandLayout;

namespace Tank.Sim.Maps.Riverland;

/// <summary>
/// The riverland's sculpted ground: the height samples (row-major, sample (i, j) at (2i, 2j)), the distance grids of the
/// river, the roads and the tracks (line ids are <see cref="RiverlandLayout"/>'s road and track ids), and the villages
/// whose plots were levelled into it.
/// </summary>
internal sealed record RiverlandShape(float[] Heights, DistanceGrid River, DistanceGrid Roads, DistanceGrid Tracks, RiverlandVillages Villages);

/// <summary>A road's surface height along its centre line, at its <see cref="Polyline.Points"/>.</summary>
internal sealed class RoadProfile
{
    private readonly float[] _along, _height;

    public RoadProfile(float[] along, float[] height) => (_along, _height) = (along, height);

    /// <summary>The surface height <paramref name="along"/> m from the start, interpolated between points, clamped to the ends.</summary>
    public float At(float along)
    {
        if (along <= _along[0]) return _height[0];
        if (along >= _along[^1]) return _height[^1];
        int k = Array.BinarySearch(_along, along);
        if (k >= 0) return _height[k];
        k = ~k - 1;
        float t = (along - _along[k]) / (_along[k + 1] - _along[k]);
        return _height[k] + (_height[k + 1] - _height[k]) * t;
    }
}

/// <summary>
/// Sculpts the riverland's height field (riverland design §3.3–3.5) in a fixed order: base noise, plateau and hills;
/// the river's and lake's valleys lowered; the dunes; the plateau's ramps; the river and the lake dug, the fords raised;
/// roads and tracks levelled; the villages' houses placed and their plots levelled (riverland design §3.6); dry land kept
/// above the water; the border band raised. The dunes and the ramps come after the valleys: the river's valley reaches
/// into the dune field and to the east ramp's foot, and would otherwise flatten the ridges and bend the ramp.
/// </summary>
internal static class RiverlandTerrain
{
    private const int N = RiverlandMap.Samples;
    private const float Cell = RiverlandMap.CellSize;
    private const float DryLandMin = 0.4f, DryRiverDistance = 30f, DryLakeDistance = 15f;
    private const float BorderRise = 8f, BorderOpenFrom = 32f, BorderOpenTo = 40f;

    public static RiverlandShape Build()
    {
        var river = new DistanceGrid(N, N, Cell);
        L.River.Stamp(river, L.RiverReach, 0);
        var roads = new DistanceGrid(N, N, Cell);
        foreach (RoadLine road in L.Roads) road.Line.Stamp(roads, L.RoadReach, road.Id);
        var tracks = new DistanceGrid(N, N, Cell);
        foreach (RoadLine track in L.Tracks) track.Line.Stamp(tracks, L.RoadReach, track.Id);

        var h = new float[N * N];
        LayNaturalGround(h);
        LowerValleys(h, river);
        AddDunes(h);
        foreach (Ramp ramp in L.Ramps) BuildRamp(h, ramp);
        DigRiver(h, river);
        DigLake(h);
        foreach (Ford ford in L.Fords) RaiseFord(h, river, ford.Road.IsTrack ? tracks : roads, ford.Road.Id);

        var profiles = new RoadProfile?[L.AllRoads.Length];   // main roads come first, so a side road finds its main one levelled
        for (int k = 0; k < L.AllRoads.Length; k++) profiles[k] = Level(h, river, L.AllRoads[k], profiles);
        RoadProfile[] roadProfiles = L.Roads.Select(r => profiles[Array.IndexOf(L.AllRoads, r)]!).ToArray();
        RoadProfile[] trackProfiles = L.Tracks.Select(r => profiles[Array.IndexOf(L.AllRoads, r)]!).ToArray();
        Pave(h, tracks, trackProfiles, L.TrackHalfWidth);   // roads last: where both reach, the road's surface wins
        Pave(h, roads, roadProfiles, L.RoadHalfWidth);
        RiverlandVillages villages = RiverlandVillages.Lay(h, river, roads, tracks);

        KeepLandDry(h, river);
        RaiseBorder(h, river);
        return new RiverlandShape(h, river, roads, tracks, villages);
    }

    /// <summary>The ground's height at (x, z), bilinear between samples, clamped to the grid.</summary>
    internal static float HeightAt(float[] h, Vector2 p)
    {
        float fx = Math.Clamp(p.X / Cell, 0, N - 1), fz = Math.Clamp(p.Y / Cell, 0, N - 1);
        int i = Math.Min((int)fx, N - 2), j = Math.Min((int)fz, N - 2);
        float u = fx - i, v = fz - j;
        int at = j * N + i;
        float near = h[at] + (h[at + 1] - h[at]) * u, far = h[at + N] + (h[at + N + 1] - h[at + N]) * u;
        return near + (far - near) * v;
    }

    /// <summary>The ground before the valleys: base noise, hills, and the plateau rising 22 m behind a 7 m cliff.</summary>
    private static void LayNaturalGround(float[] h)
    {
        float margin = L.PlateauJitter + 1;
        for (int j = 0; j < N; j++)
        for (int i = 0; i < N; i++)
        {
            float x = i * Cell, z = j * Cell;
            float height = L.BaseHeight(x, z) + L.HillsHeight(x, z);
            if (x > L.Plateau.Min.X - margin && x < L.Plateau.Max.X + margin && z > L.Plateau.Min.Y - margin && z < L.Plateau.Max.Y + margin)
                height += L.PlateauRise * HeightSculpt.SmoothStep(0, L.CliffWidth, -L.PlateauEdgeDistance(new Vector2(x, z)));
            h[j * N + i] = height;
        }
    }

    /// <summary>Within 60 m of the river's centre line (and of a line 18 m inside the lake shore) the land is lowered to the valley floor, easing out by 120 m.</summary>
    private static void LowerValleys(float[] h, DistanceGrid river)
    {
        float lakeReach = L.LakeRadius + L.LakeJitter + L.ValleyEdge;
        for (int j = 0; j < N; j++)
        for (int i = 0; i < N; i++)
        {
            int at = j * N + i;
            float w = 1 - HeightSculpt.SmoothStep(L.ValleyFlat, L.ValleyEdge, river.Distance[at]);
            var p = new Vector2(i * Cell, j * Cell);
            if (w < 1 && Vector2.DistanceSquared(p, L.LakeCentre) < lakeReach * lakeReach)
                w = MathF.Max(w, 1 - HeightSculpt.SmoothStep(L.ValleyFlat, L.ValleyEdge, L.LakeShoreDistance(p) + L.RiverHalfWidth));
            if (w > 0) h[at] += (L.ValleyHeight - h[at]) * w;
        }
    }

    private static void AddDunes(float[] h)
    {
        ForSamplesIn(L.Dunes.Min, L.Dunes.Max, (at, p) => h[at] += L.DuneHeight(p));
    }

    /// <summary>
    /// A straight lane 14 m wide from the plateau's top (base ground + 22 m there) evenly down to the ground at its
    /// bottom end; level for a little way behind the top so it always meets the plateau; its sides drop to the ground
    /// around over a few metres.
    /// </summary>
    private static void BuildRamp(float[] h, Ramp ramp)
    {
        float length = ramp.Length;
        Vector2 down = ramp.Down;
        float top = L.BaseHeight(ramp.Top.X, ramp.Top.Y) + L.HillsHeight(ramp.Top.X, ramp.Top.Y) + L.PlateauRise;
        float bottom = HeightAt(h, ramp.Bottom);
        float reach = L.RampHalfWidth + L.RampSide;
        Vector2 a = ramp.Top - down * L.RampLanding, b = ramp.Bottom + down * L.RampFoot;
        ForSamplesIn(Vector2.Min(a, b) - new Vector2(reach), Vector2.Max(a, b) + new Vector2(reach), (at, p) =>
        {
            (float along, float across) = ramp.Locate(p);
            float w = 1 - HeightSculpt.SmoothStep(L.RampHalfWidth, reach, across);
            if (w <= 0 || along < -L.RampLanding || along > length + L.RampFoot) return;
            if (along < 0)
            {
                w *= 1 - HeightSculpt.SmoothStep(L.RampLanding - 5, L.RampLanding, -along);
                h[at] = MathF.Max(h[at], h[at] + (top - h[at]) * w);
            }
            else if (along <= length)
            {
                float lane = top + (bottom - top) * (along / length);
                h[at] += (lane - h[at]) * w;
            }
            else
            {
                w *= 1 - HeightSculpt.SmoothStep(length, length + L.RampFoot, along);
                h[at] += (bottom - h[at]) * w;
            }
        });
    }

    /// <summary>The river's bed (1.3 m deep in the middle), its sand bank rising to +1 m, then easing to the valley floor.</summary>
    private static void DigRiver(float[] h, DistanceGrid river)
    {
        float maxReach = L.RiverHalfWidth + L.BankWidth + L.BankJitter + L.BankBlend;
        for (int at = 0; at < h.Length; at++)
        {
            float d = river.Distance[at];
            if (d >= maxReach) continue;
            if (d < L.RiverHalfWidth)
            {
                float r = d / L.RiverHalfWidth;
                h[at] = -L.RiverDepth * (1 - r * r);
                continue;
            }
            float bank = L.BankOuter(new Vector2(at % N * Cell, at / N * Cell));
            if (d < bank) h[at] = L.BankHeight * (d - L.RiverHalfWidth) / (bank - L.RiverHalfWidth);
            else h[at] = L.BankHeight + (h[at] - L.BankHeight) * HeightSculpt.SmoothStep(bank, bank + L.BankBlend, d);
        }
    }

    /// <summary>The lake: 1.6 m deep at the centre, a 10 m beach rising to +1 m, then easing to the ground around.</summary>
    private static void DigLake(float[] h)
    {
        float reach = L.LakeRadius + L.LakeJitter + L.BeachWidth + L.BankBlend;
        ForSamplesIn(L.LakeCentre - new Vector2(reach), L.LakeCentre + new Vector2(reach), (at, p) =>
        {
            float r = Vector2.Distance(p, L.LakeCentre);
            if (r >= reach) return;
            float shore = L.LakeShoreRadius(p);
            if (r < shore)
            {
                float t = r / shore;
                h[at] = -L.LakeDepth * (1 - t * t);
            }
            else if (r < shore + L.BeachWidth) h[at] = L.BankHeight * (r - shore) / L.BeachWidth;
            else h[at] = L.BankHeight + (h[at] - L.BankHeight) * HeightSculpt.SmoothStep(shore + L.BeachWidth, shore + L.BeachWidth + L.BankBlend, r);
        });
    }

    /// <summary>Within 12 m of the ford road's centre the river is only 0.5 m deep, easing back to the bed over 6 m.</summary>
    private static void RaiseFord(float[] h, DistanceGrid river, DistanceGrid grid, int lineId)
    {
        for (int at = 0; at < h.Length; at++)
        {
            if (river.Distance[at] >= L.RiverHalfWidth || grid.Line[at] != lineId) continue;
            float d = grid.Distance[at];
            if (d >= L.FordHalfWidth + L.FordBlend) continue;
            float ford = -L.FordDepth + (h[at] + L.FordDepth) * HeightSculpt.SmoothStep(L.FordHalfWidth, L.FordHalfWidth + L.FordBlend, d);
            h[at] = MathF.Max(h[at], ford);
        }
    }

    /// <summary>
    /// The road's surface height at each of its points: the ground there averaged over 60 m, its grade then limited to 8°
    /// (cutting through rises rather than filling dips), with some stretches held: −0.5 m through a ford, the ground
    /// itself up a ramp, and one level height where a side road meets its main road.
    /// </summary>
    private static RoadProfile Level(float[] h, DistanceGrid river, RoadLine road, RoadProfile?[] done)
    {
        IReadOnlyList<Vector2> points = road.Line.Points;
        int n = points.Count;
        var along = new float[n];
        var ground = new float[n];
        for (int k = 0; k < n; k++)
        {
            if (k > 0) along[k] = along[k - 1] + Vector2.Distance(points[k - 1], points[k]);
            ground[k] = HeightAt(h, points[k]);
        }

        float[] held = HoldFordsAndRamps(river, road, ground);
        foreach (Junction junction in L.Junctions.Where(j => j.Branch == road))
        {
            Vector2 end = junction.AtStart ? points[0] : points[^1];
            junction.Main.Line.DistanceTo(end, out float mainAlong);
            RoadProfile main = done[Array.IndexOf(L.AllRoads, junction.Main)]
                ?? throw new InvalidOperationException($"{junction.Main.Name} must be levelled before {road.Name}.");
            Hold(held, along, junction.AtStart ? 0 : along[^1], main.At(mainAlong));
        }

        float[] surface = Smooth(ground, along, held);
        foreach (Junction junction in L.Junctions.Where(j => j.Main == road))
        {
            Vector2 end = junction.AtStart ? junction.Branch.Line.Points[0] : junction.Branch.Line.Points[^1];
            road.Line.DistanceTo(end, out float at);
            Hold(held, along, at, new RoadProfile(along, surface).At(at));
        }
        for (int k = 0; k < n; k++)
            if (!float.IsNaN(held[k])) surface[k] = held[k];
        LimitGrade(surface, along, held, L.MaxRoadGrade);
        return new RoadProfile(along, surface);
    }

    /// <summary>Per point of the road, the height it is held at (NaN: free): −0.5 m in a ford's water, the ground on a ramp's lane.</summary>
    private static float[] HoldFordsAndRamps(DistanceGrid river, RoadLine road, float[] ground)
    {
        IReadOnlyList<Vector2> points = road.Line.Points;
        var held = new float[points.Count];
        Array.Fill(held, float.NaN);
        bool fords = L.Fords.Any(f => f.Road == road);
        for (int k = 0; k < points.Count; k++)
        {
            Vector2 p = points[k];
            if (fords && river.DistanceAt(p.X, p.Y) < L.RiverHalfWidth) held[k] = -L.FordDepth;
            foreach (Ramp ramp in L.Ramps)
            {
                (float s, float across) = ramp.Locate(p);
                if (across <= L.RampHalfWidth && s >= -L.RampLanding && s <= ramp.Length) held[k] = ground[k];
            }
        }
        return held;
    }

    /// <summary>Holds every point within <see cref="RiverlandLayout.JunctionPad"/> m of <paramref name="at"/> that isn't held already at <paramref name="height"/>.</summary>
    private static void Hold(float[] held, float[] along, float at, float height)
    {
        for (int k = 0; k < held.Length; k++)
            if (MathF.Abs(along[k] - at) <= L.JunctionPad && float.IsNaN(held[k])) held[k] = height;
    }

    /// <summary>
    /// The mean of <paramref name="ground"/> over the points within half the smoothing length either side, taken within
    /// each stretch of points that aren't held (so a ramp or a ford doesn't drag its neighbours).
    /// </summary>
    private static float[] Smooth(float[] ground, float[] along, float[] held)
    {
        int n = ground.Length;
        var result = (float[])ground.Clone();
        var prefix = new double[n + 1];
        for (int k = 0; k < n; k++) prefix[k + 1] = prefix[k] + ground[k];
        float half = L.RoadSmoothing / 2;
        int start = 0;
        while (start < n)
        {
            if (!float.IsNaN(held[start])) { start++; continue; }
            int end = start;
            while (end < n && float.IsNaN(held[end])) end++;   // the stretch [start, end)
            int lo = start, hi = start;
            for (int k = start; k < end; k++)
            {
                while (along[lo] < along[k] - half) lo++;
                while (hi < end && along[hi] <= along[k] + half) hi++;
                result[k] = (float)((prefix[hi] - prefix[lo]) / (hi - lo));
            }
            start = end;
        }
        return result;
    }

    /// <summary>
    /// Limits the grade between neighbouring points to <paramref name="grade"/> without moving held points: first raises
    /// each free point to within reach of the held points that bound its stretch (a held stretch may itself be steeper,
    /// like a ramp, so only the nearest one each way counts), then lowers whatever still climbs too steeply, sweeping
    /// from both ends.
    /// </summary>
    private static void LimitGrade(float[] surface, float[] along, float[] held, float grade)
    {
        int n = surface.Length;
        var floor = new float[n];
        float reach = float.NegativeInfinity;
        for (int k = 0; k < n; k++)
        {
            reach = !float.IsNaN(held[k]) ? held[k] : k > 0 ? reach - grade * (along[k] - along[k - 1]) : reach;
            floor[k] = reach;
        }
        reach = float.NegativeInfinity;
        for (int k = n - 1; k >= 0; k--)
        {
            reach = !float.IsNaN(held[k]) ? held[k] : k < n - 1 ? reach - grade * (along[k + 1] - along[k]) : reach;
            floor[k] = MathF.Max(floor[k], reach);
        }
        for (int k = 0; k < n; k++)
            if (float.IsNaN(held[k])) surface[k] = MathF.Max(surface[k], floor[k]);
        for (int k = 1; k < n; k++)
            if (float.IsNaN(held[k])) surface[k] = MathF.Min(surface[k], surface[k - 1] + grade * (along[k] - along[k - 1]));
        for (int k = n - 2; k >= 0; k--)
            if (float.IsNaN(held[k])) surface[k] = MathF.Min(surface[k], surface[k + 1] + grade * (along[k + 1] - along[k]));
    }

    /// <summary>Within the half width the ground is the line's surface; over the next 5 m (the shoulder) it eases back.</summary>
    private static void Pave(float[] h, DistanceGrid grid, RoadProfile[] profiles, float halfWidth)
    {
        float reach = halfWidth + L.Shoulder;
        for (int at = 0; at < h.Length; at++)
        {
            float d = grid.Distance[at];
            if (d >= reach) continue;
            float surface = profiles[grid.Line[at]].At(grid.Along[at]);
            h[at] = d <= halfWidth ? surface : surface + (h[at] - surface) * HeightSculpt.SmoothStep(halfWidth, reach, d);
        }
    }

    /// <summary>Away from the river and the lake, no land lies lower than 0.4 m: no stray puddles.</summary>
    private static void KeepLandDry(float[] h, DistanceGrid river)
    {
        float lakeReach = L.LakeRadius + L.LakeJitter + DryLakeDistance;
        for (int at = 0; at < h.Length; at++)
        {
            if (h[at] >= DryLandMin || river.Distance[at] < DryRiverDistance) continue;
            var p = new Vector2(at % N * Cell, at / N * Cell);
            if (Vector2.DistanceSquared(p, L.LakeCentre) < lakeReach * lakeReach && L.LakeShoreDistance(p) < DryLakeDistance) continue;
            h[at] = DryLandMin;
        }
    }

    /// <summary>
    /// Across the 30 m border band the ground eases from the playable area's edge to 8 m above it at the map's edge;
    /// not within 32–40 m of the river's centre line, where the river runs off the map.
    /// </summary>
    private static void RaiseBorder(float[] h, DistanceGrid river)
    {
        float border = RiverlandMap.Border, size = L.Size;
        int innerMin = (int)(border / Cell), innerMax = N - 1 - innerMin;
        for (int j = 0; j < N; j++)
        for (int i = 0; i < N; i++)
        {
            float x = i * Cell, z = j * Cell;
            float edge = MathF.Min(MathF.Min(x, z), MathF.Min(size - x, size - z));
            if (edge >= border) continue;
            int at = j * N + i;
            float open = HeightSculpt.SmoothStep(BorderOpenFrom, BorderOpenTo, river.Distance[at]);
            if (open <= 0) continue;
            float s = HeightSculpt.SmoothStep(0, border, border - edge);
            float inner = h[Math.Clamp(j, innerMin, innerMax) * N + Math.Clamp(i, innerMin, innerMax)];
            float raised = h[at] + (inner - h[at]) * s + BorderRise * s;
            h[at] += (raised - h[at]) * open;
        }
    }

    /// <summary>Calls <paramref name="visit"/>(index, position) for every sample in the box [min, max] (clipped to the grid).</summary>
    private static void ForSamplesIn(Vector2 min, Vector2 max, Action<int, Vector2> visit)
    {
        int i0 = Math.Max(0, (int)MathF.Ceiling(min.X / Cell)), i1 = Math.Min(N - 1, (int)MathF.Floor(max.X / Cell));
        int j0 = Math.Max(0, (int)MathF.Ceiling(min.Y / Cell)), j1 = Math.Min(N - 1, (int)MathF.Floor(max.Y / Cell));
        for (int j = j0; j <= j1; j++)
        for (int i = i0; i <= i1; i++)
            visit(j * N + i, new Vector2(i * Cell, j * Cell));
    }
}
