using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Maps.Riverland;
using L = Tank.Sim.Maps.Riverland.RiverlandLayout;

namespace Tank.Sim.Tests;

/// <summary>The riverland's villages: houses on level plots facing their roads, yard walls and farm fences (riverland design §3.6).</summary>
public sealed class RiverlandVillageTests
{
    /// <summary>A plot is the house's footprint grown by this much on every side.</summary>
    private const float PlotMargin = 3f;
    /// <summary>Two houses in the same row are at most 12 m apart; this much or more apart they are in separate rows.</summary>
    private const float RowBreak = 20f;

    private static Battlefield Field => RiverlandFixture.Field;

    private static readonly (string Name, Vector2 Centre, float Radius, int Min, int Max)[] Villages =
    {
        ("town", L.Town, L.TownRadius, 20, 24),
        ("north village", L.NorthVillage, L.NorthVillageRadius, 8, 12),
        ("west village", L.WestVillage, L.WestVillageRadius, 6, 10),
        ("south village", L.SouthVillage, L.SouthVillageRadius, 8, 12),
    };

    private static readonly (float Length, float Width, float Height)[] Sizes = { (8, 6, 6.5f), (12, 7, 7.5f), (9, 7, 9), (14, 9, 8) };

    private static List<Prop> Houses => Field.Props.Where(p => p.Kind == PropKind.House).ToList();

    private static List<Prop> WallsAndFences =>
        Field.Props.Where(p => p.Kind is PropKind.BrickWall or PropKind.StoneWall or PropKind.Fence).ToList();

    [Fact]
    public void There_are_55_to_60_houses_spread_over_the_villages()
    {
        List<Prop> houses = Houses;
        Assert.InRange(houses.Count, 55, 60);

        foreach ((string name, Vector2 centre, float radius, int min, int max) in Villages)
        {
            int count = houses.Count(h => Vector2.Distance(h.Footprint.Center, centre) <= radius);
            Assert.True(count >= min && count <= max, $"{name}: {count} houses (want {min}–{max})");
        }
        foreach (Vector2 farm in L.Farms)
        {
            int count = houses.Count(h => Vector2.Distance(h.Footprint.Center, farm) <= L.FarmRadius);
            Assert.True(count is >= 1 and <= 2, $"farm {farm}: {count} houses");
        }
        foreach (Prop house in houses)
        {
            int areas = Villages.Count(v => Vector2.Distance(house.Footprint.Center, v.Centre) <= v.Radius)
                        + L.Farms.Count(f => Vector2.Distance(house.Footprint.Center, f) <= L.FarmRadius);
            Assert.True(areas == 1, $"house {house.Id} at {house.Footprint.Center} lies in {areas} village or farm areas");
        }
    }

    [Fact]
    public void The_town_has_8_or_more_houses_on_each_bank()
    {
        List<Prop> town = Houses.Where(h => Vector2.Distance(h.Footprint.Center, L.Town) <= L.TownRadius).ToList();
        int left = town.Count(h => RiverSide(h.Footprint.Center) < 0), right = town.Count - left;
        Assert.True(left >= 8 && right >= 8, $"the town has {left} houses on one bank and {right} on the other");
    }

    [Fact]
    public void Houses_come_in_four_sizes()
    {
        foreach (Prop house in Houses)
        {
            float length = 2 * house.Footprint.HalfExtents.X, width = 2 * house.Footprint.HalfExtents.Y, height = house.Height - WallLine.Sink;
            Assert.True(Sizes.Any(s => MathF.Abs(s.Length - length) <= 0.01f && MathF.Abs(s.Width - width) <= 0.01f && MathF.Abs(s.Height - height) <= 0.01f),
                $"house {house.Id}: {length} × {width} m, {height} m high");
        }
    }

    [Fact]
    public void Each_house_stands_on_a_level_plot()
    {
        foreach (Prop house in Houses)
        {
            float lo = float.PositiveInfinity, hi = float.NegativeInfinity;
            foreach ((int i, int j) in SamplesIn(Plot(house)))
            {
                float h = Field.Terrain.Sample(i, j);
                lo = MathF.Min(lo, h);
                hi = MathF.Max(hi, h);
            }
            Assert.True(hi - lo <= 0.3f, $"house {house.Id} at {house.Footprint.Center}: plot from {lo:F2} to {hi:F2} m");
            Assert.True(MathF.Abs(house.Bottom + WallLine.Sink - lo) <= 0.3f, $"house {house.Id} stands at {house.Bottom + WallLine.Sink:F2} m, its plot at {lo:F2} m");
        }
    }

    [Fact]
    public void Houses_keep_off_roads_water_forests_and_each_other()
    {
        List<Prop> houses = Houses;
        foreach (Prop house in houses)
        {
            float road = RoadEdgeDistance(house.Footprint);
            Assert.True(road >= 3, $"house {house.Id} at {house.Footprint.Center} is {road:F2} m from a road's edge");
            foreach ((int i, int j) in SamplesIn(Plot(house)))
            {
                var p = new Vector2(i * RiverlandMap.CellSize, j * RiverlandMap.CellSize);
                Assert.True(Field.WaterDepthAt(p) <= 0, $"house {house.Id}'s plot is under water at {p}");
                Assert.False(L.InForest(p), $"house {house.Id}'s plot is in a forest at {p}");
            }
        }
        for (int a = 0; a < houses.Count; a++)
        for (int b = a + 1; b < houses.Count; b++)
            Assert.False(Obb2.TryGetPenetration(Plot(houses[a]), Plot(houses[b]), out _),
                $"plots of houses {houses[a].Id} at {houses[a].Footprint.Center} and {houses[b].Id} at {houses[b].Footprint.Center} overlap");
    }

    [Fact]
    public void Village_houses_face_their_road_from_3_to_10_metres()
    {
        int checkedHouses = 0;
        foreach (Prop house in Houses.Where(h => VillageOf(h) >= 0))
        {
            float road = RoadEdgeDistance(house.Footprint);
            Assert.True(road is >= 3 and <= 10, $"house {house.Id} at {house.Footprint.Center} is {road:F2} m from its road's edge");
            (RoadLine line, float along, _) = NearestRoad(house.Footprint.Center);
            Vector2 toRoad = Vector2.Normalize(line.Line.PointAt(along) - house.Footprint.Center);
            float angle = MathF.Acos(Math.Clamp(Vector2.Dot(toRoad, house.Footprint.AxisForward), -1f, 1f)) * 180 / MathF.PI;
            Assert.True(angle < 30, $"house {house.Id} at {house.Footprint.Center} faces {angle:F0}° away from {line.Name}");
            checkedHouses++;
        }
        Assert.True(checkedHouses >= 42, $"only {checkedHouses} village houses");
    }

    /// <summary>
    /// Houses in a village on the same side of the same road, in order along it: neighbours (less than 20 m apart) leave
    /// 4–12 m between them; farther apart they are separate rows (cut by a junction, a bend, the river…). Most houses have
    /// a neighbour.
    /// </summary>
    [Fact]
    public void Neighbours_along_a_road_leave_4_to_12_metres()
    {
        var streets = Houses.Where(h => VillageOf(h) >= 0)
            .Select(h => (House: h, Village: VillageOf(h), Road: NearestRoad(h.Footprint.Center)))
            .GroupBy(x => (x.Village, x.Road.Line.Name, x.Road.Side));
        int pairs = 0, inRows = 0, houses = 0;
        var paired = new HashSet<int>();
        foreach (var street in streets)
        {
            var row = street.OrderBy(x => x.Road.Along).Select(x => x.House).ToList();
            houses += row.Count;
            for (int k = 1; k < row.Count; k++)
            {
                float gap = Distance(row[k - 1].Footprint, row[k].Footprint);
                pairs++;
                Assert.True(gap >= 4, $"houses {row[k - 1].Id} and {row[k].Id} at {row[k].Footprint.Center} are only {gap:F2} m apart");
                if (gap >= RowBreak) continue;
                Assert.True(gap <= 12, $"houses {row[k - 1].Id} and {row[k].Id} at {row[k].Footprint.Center} leave {gap:F2} m between them");
                inRows++;
                paired.Add(row[k - 1].Id);
                paired.Add(row[k].Id);
            }
        }
        Assert.True(pairs > 0 && paired.Count >= 0.75f * houses, $"only {paired.Count} of {houses} village houses have a neighbour ({inRows} of {pairs} pairs)");
    }

    [Fact]
    public void Walls_and_fences_are_whole_segments_off_the_roads()
    {
        List<Prop> walls = WallsAndFences;
        List<Obb2> plots = Houses.Select(Plot).ToList();
        foreach (Prop wall in walls)
        {
            Obb2 f = wall.Footprint;
            Vector2 half = f.AxisRight * f.HalfExtents.X;
            Assert.Equal(1, new WallLine(wall.Kind, f.Center - half, f.Center + half).SegmentCount);
            Assert.Equal(WallLine.Thickness(wall.Kind) / 2, f.HalfExtents.Y, 0.001f);
            float road = RoadEdgeDistance(f);
            Assert.True(road >= 1, $"{wall.Kind} {wall.Id} at {f.Center} is {road:F2} m from a road's edge");
            foreach (Vector2 p in Corners(f).Append(f.Center))
                Assert.True(Field.WaterDepthAt(p) <= 0, $"{wall.Kind} {wall.Id} stands in water at {p}");
            foreach (Obb2 plot in plots)
                Assert.False(Obb2.TryGetPenetration(f, plot, out _), $"{wall.Kind} {wall.Id} at {f.Center} runs into the plot at {plot.Center}");
        }

        int townWalls = walls.Count(w => w.Kind != PropKind.Fence && Vector2.Distance(w.Footprint.Center, L.Town) <= L.TownRadius + 20);
        Assert.True(townWalls >= 6 * 5, $"only {townWalls} wall segments in the town (want six yards of 10–16 m)");
    }

    [Fact]
    public void Each_farm_has_a_fence_ring_with_a_gap()
    {
        List<Prop> fences = WallsAndFences.Where(w => w.Kind == PropKind.Fence).ToList();
        foreach (Vector2 farm in L.Farms)
        {
            List<Prop> ring = fences.Where(f => Vector2.Distance(f.Footprint.Center, farm) <= L.FarmRadius + 10).ToList();
            Assert.True(ring.Count >= 20, $"farm {farm}: only {ring.Count} fence panels");
            List<Vector2> chain = Chain(ring, out string problem);
            Assert.True(problem.Length == 0, $"farm {farm}: {problem}");
            float gap = Vector2.Distance(chain[0], chain[^1]);
            Assert.True(gap >= 4 - 0.01f, $"farm {farm}: the ring's gap is {gap:F2} m");
            float perimeter = 0;
            for (int k = 1; k < chain.Count; k++) perimeter += Vector2.Distance(chain[k - 1], chain[k]);
            Assert.True(gap < perimeter / 4, $"farm {farm}: a {gap:F1} m gap in {perimeter:F0} m of fence is no ring");

            List<Prop> houses = Houses.Where(h => Vector2.Distance(h.Footprint.Center, farm) <= L.FarmRadius).ToList();
            foreach (Prop house in houses)
            foreach (Vector2 corner in Corners(house.Footprint))
                Assert.True(InPolygon(chain, corner), $"farm {farm}: house {house.Id}'s corner {corner} is outside its fence");
        }
    }

    [Fact]
    public void Yard_ground_is_dirt()
    {
        const int Cells = RiverlandMap.Samples - 1;
        foreach (Prop house in Houses)
        {
            Obb2 plot = Plot(house);
            int cells = 0;
            for (int j = 0; j < Cells; j++)
            for (int i = 0; i < Cells; i++)
            {
                var p = new Vector2((i + 0.5f) * RiverlandMap.CellSize, (j + 0.5f) * RiverlandMap.CellSize);
                if (Vector2.DistanceSquared(p, plot.Center) > 15 * 15 || !plot.Contains(p) || Field.Surface[i, j] == SurfaceKind.Road) continue;
                Assert.True(Field.Surface[i, j] == SurfaceKind.Dirt, $"house {house.Id}'s yard cell {p} is {Field.Surface[i, j]}");
                cells++;
            }
            Assert.True(cells >= 30, $"house {house.Id}'s plot covers only {cells} cells");
        }
    }

    // ---- helpers ----

    /// <summary>Which side of the river's centre line <paramref name="p"/> lies on: the sign of the turn from its flow to <paramref name="p"/>.</summary>
    private static float RiverSide(Vector2 p)
    {
        L.River.DistanceTo(p, out float along);
        Vector2 flow = L.River.TangentAt(along), off = p - L.River.PointAt(along);
        return MathF.Sign(flow.X * off.Y - flow.Y * off.X);
    }

    private static Obb2 Plot(Prop house) => house.Footprint with { HalfExtents = house.Footprint.HalfExtents + new Vector2(PlotMargin) };

    private static Vector2[] Corners(Obb2 box)
    {
        Vector2 r = box.AxisRight * box.HalfExtents.X, f = box.AxisForward * box.HalfExtents.Y;
        return new[] { box.Center - r - f, box.Center + r - f, box.Center + r + f, box.Center - r + f };
    }

    /// <summary>The height-field samples inside <paramref name="box"/>.</summary>
    private static IEnumerable<(int I, int J)> SamplesIn(Obb2 box)
    {
        float cell = RiverlandMap.CellSize, rx = box.Radius(Vector2.UnitX), rz = box.Radius(Vector2.UnitY);
        for (int j = (int)MathF.Ceiling((box.Center.Y - rz) / cell); j <= (int)MathF.Floor((box.Center.Y + rz) / cell); j++)
        for (int i = (int)MathF.Ceiling((box.Center.X - rx) / cell); i <= (int)MathF.Floor((box.Center.X + rx) / cell); i++)
            if (box.Contains(new Vector2(i * cell, j * cell))) yield return (i, j);
    }

    /// <summary>How far the box comes to the edge of the nearest road (4 m half width) or track (3 m), from its outline every 0.5 m.</summary>
    private static float RoadEdgeDistance(Obb2 box)
    {
        Vector2[] corners = Corners(box);
        float best = float.PositiveInfinity;
        for (int k = 0; k < 4; k++)
        {
            Vector2 a = corners[k], b = corners[(k + 1) % 4];
            int steps = Math.Max(1, (int)MathF.Ceiling(Vector2.Distance(a, b) / 0.5f));
            for (int s = 0; s < steps; s++)
            {
                Vector2 p = Vector2.Lerp(a, b, s / (float)steps);
                foreach (RoadLine road in L.AllRoads)
                    best = MathF.Min(best, road.Line.DistanceTo(p, out _) - (road.IsTrack ? L.TrackHalfWidth : L.RoadHalfWidth));
            }
        }
        return best;
    }

    /// <summary>The road or track whose centre line is nearest <paramref name="p"/>, how far along it, and which side of it p is on.</summary>
    private static (RoadLine Line, float Along, int Side) NearestRoad(Vector2 p)
    {
        RoadLine best = L.AllRoads[0];
        float bestDistance = float.PositiveInfinity, bestAlong = 0;
        foreach (RoadLine road in L.AllRoads)
        {
            float d = road.Line.DistanceTo(p, out float along);
            if (d < bestDistance) (best, bestDistance, bestAlong) = (road, d, along);
        }
        Vector2 t = best.Line.TangentAt(bestAlong), off = p - best.Line.PointAt(bestAlong);
        return (best, bestAlong, t.X * off.Y - t.Y * off.X > 0 ? 1 : -1);
    }

    /// <summary>The village whose area holds the house, or −1 (a farm's).</summary>
    private static int VillageOf(Prop house) =>
        Array.FindIndex(Villages, v => Vector2.Distance(house.Footprint.Center, v.Centre) <= v.Radius);

    /// <summary>The shortest distance between two boxes that don't overlap (0 when they do).</summary>
    private static float Distance(Obb2 a, Obb2 b)
    {
        if (Obb2.TryGetPenetration(a, b, out _)) return 0;
        return MathF.Min(Corners(a).Min(b.DistanceToPoint), Corners(b).Min(a.DistanceToPoint));
    }

    /// <summary>
    /// Links the panels end to end (ends within 0.3 m) into one open chain of points from one loose end to the other.
    /// <paramref name="problem"/> says what is wrong when they don't form exactly one chain.
    /// </summary>
    private static List<Vector2> Chain(List<Prop> panels, out string problem)
    {
        var ends = panels.Select(p => (A: p.Footprint.Center - p.Footprint.AxisRight * p.Footprint.HalfExtents.X,
                                       B: p.Footprint.Center + p.Footprint.AxisRight * p.Footprint.HalfExtents.X)).ToList();
        const float Join = 0.3f;
        int Links(Vector2 q, int self) => ends.Where((e, k) => k != self && (Vector2.Distance(e.A, q) < Join || Vector2.Distance(e.B, q) < Join)).Count();
        var loose = new List<(int Panel, bool AtA)>();
        for (int k = 0; k < ends.Count; k++)
        {
            int a = Links(ends[k].A, k), b = Links(ends[k].B, k);
            if (a > 1 || b > 1) { problem = $"panel at {panels[k].Footprint.Center} branches"; return new List<Vector2>(); }
            if (a == 0) loose.Add((k, true));
            if (b == 0) loose.Add((k, false));
        }
        if (loose.Count != 2) { problem = $"{loose.Count} loose panel ends (want 2: one gap)"; return new List<Vector2>(); }

        var chain = new List<Vector2>();
        var used = new bool[ends.Count];
        (int panel, bool atA) = loose[0];
        Vector2 at = atA ? ends[panel].A : ends[panel].B;
        chain.Add(at);
        while (true)
        {
            used[panel] = true;
            at = atA ? ends[panel].B : ends[panel].A;
            chain.Add(at);
            int next = -1;
            for (int k = 0; k < ends.Count && next < 0; k++)
                if (!used[k] && (Vector2.Distance(ends[k].A, at) < Join || Vector2.Distance(ends[k].B, at) < Join)) next = k;
            if (next < 0) break;
            (panel, atA) = (next, Vector2.Distance(ends[next].A, at) < Join);
        }
        problem = used.All(u => u) ? "" : $"{used.Count(u => !u)} panels are not on the ring";
        return chain;
    }

    /// <summary>Whether <paramref name="p"/> is inside the polygon (closed from the last point back to the first).</summary>
    private static bool InPolygon(List<Vector2> polygon, Vector2 p)
    {
        bool inside = false;
        for (int a = 0, b = polygon.Count - 1; a < polygon.Count; b = a++)
        {
            Vector2 pa = polygon[a], pb = polygon[b];
            if ((pa.Y > p.Y) != (pb.Y > p.Y) && p.X < pb.X + (pa.X - pb.X) * (p.Y - pb.Y) / (pa.Y - pb.Y)) inside = !inside;
        }
        return inside;
    }
}
