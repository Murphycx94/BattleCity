using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Maps.Riverland;
using Tank.Sim.Terrain;
using L = Tank.Sim.Maps.Riverland.RiverlandLayout;

namespace Tank.Sim.Tests;

/// <summary>The riverland's ground: terrain, river, lake, fords, roads and ground kinds (riverland design §3.1–3.5, §3.12).</summary>
public sealed class RiverlandTerrainTests
{
    private const int Cells = RiverlandMap.Samples - 1;

    private static Battlefield Field => RiverlandFixture.Field;
    private static HeightField Terrain => Field.Terrain;

    [Fact]
    public void Riverland_has_the_designed_size()
    {
        Assert.Equal("riverland", RiverlandMap.Id);
        Assert.Equal(601, Terrain.SamplesX);
        Assert.Equal(601, Terrain.SamplesZ);
        Assert.Equal(2f, Terrain.CellSize);
        Assert.Equal(30f, Field.Border);
        Assert.Equal(0f, Field.WaterLevel);
        Assert.Equal(600, Field.Surface.CellsX);
        Assert.Equal(600, Field.Surface.CellsZ);
    }

    [Fact]
    public void Generation_is_deterministic()
    {
        // The generator's raw output twice: the fixture's field is the catalog's, its heights rounded to centimetres.
        Battlefield first = RiverlandMap.Create(), again = RiverlandMap.Create();

        for (int j = 0; j < Terrain.SamplesZ; j++)
        for (int i = 0; i < Terrain.SamplesX; i++)
        {
            int a = BitConverter.SingleToInt32Bits(first.Terrain.Sample(i, j)), b = BitConverter.SingleToInt32Bits(again.Terrain.Sample(i, j));
            if (a != b) Assert.Fail($"sample ({i}, {j}) differs: {first.Terrain.Sample(i, j)} vs {again.Terrain.Sample(i, j)}");
        }
        Assert.True(first.Surface.Cells.SequenceEqual(again.Surface.Cells));
        Assert.Equal(first.Props, again.Props);
        Assert.Equal(first.Props, Field.Props);   // props go into the map data as they are
    }

    [Fact]
    public void Water_lies_only_in_the_river_and_the_lake()
    {
        var offenders = new List<Vector2>();
        int wet = 0;
        for (int j = 0; j < Cells; j++)
        for (int i = 0; i < Cells; i++)
        {
            Vector2 p = CellCentre(i, j);
            if (Field.WaterDepthAt(p) <= 0) continue;
            wet++;
            if (RiverDistance(p) < 30 || Vector2.Distance(p, L.LakeCentre) < 110) continue;
            offenders.Add(p);
        }
        Assert.True(wet > 10_000, $"only {wet} wet cells");
        Assert.True(offenders.Count == 0, $"{offenders.Count} wet cells away from the river and lake, e.g. {string.Join(", ", offenders.Take(5))}");
    }

    [Fact]
    public void The_deepest_water_is_the_lake_centre()
    {
        float lowest = float.PositiveInfinity;
        for (int j = 0; j < Terrain.SamplesZ; j++)
        for (int i = 0; i < Terrain.SamplesX; i++)
            lowest = MathF.Min(lowest, Terrain.Sample(i, j));

        Assert.True(-lowest <= 1.62f, $"deepest water {-lowest} m");
        Assert.Equal(1.6f, Field.WaterDepthAt(L.LakeCentre), 0.1f);
        Assert.Equal(1.3f, Field.WaterDepthAt(new Vector2(615, 300)), 0.1f);
    }

    [Fact]
    public void The_fords_are_half_a_metre_deep()
    {
        Assert.Equal(2, L.Fords.Length);
        foreach (Ford ford in L.Fords)
        {
            Assert.True(RiverDistance(ford.Centre) < 3, $"{ford.Road.Name}'s ford {ford.Centre} is off the river");
            Assert.Equal(0.5f, Field.WaterDepthAt(ford.Centre), 0.05f);
            ford.Road.Line.DistanceTo(ford.Centre, out float along);
            Vector2 across = Perpendicular(ford.Road.Line.TangentAt(along));
            int bed = 0;
            for (float o = 6; o <= 10; o += 1)
            foreach (int side in new[] { -1, 1 })
            {
                Vector2 p = ford.Centre + across * (o * side);
                if (Field.WaterDepthAt(p) <= 0) continue;
                bed++;
                Assert.Equal(0.5f, Field.WaterDepthAt(p), 0.05f);
                Assert.True(Field.SurfaceAt(p) == SurfaceKind.Dirt, $"{ford.Road.Name}'s ford bed at {p} is {Field.SurfaceAt(p)}");
            }
            Assert.True(bed >= 6, $"{ford.Road.Name}'s ford has only {bed} wet bed points");
        }
    }

    [Fact]
    public void Dry_land_stays_above_the_water()
    {
        var offenders = new List<string>();
        for (int j = 0; j < Terrain.SamplesZ; j++)
        for (int i = 0; i < Terrain.SamplesX; i++)
        {
            var p = new Vector2(i * RiverlandMap.CellSize, j * RiverlandMap.CellSize);
            if (RiverDistance(p) < 40 || L.LakeShoreDistance(p) < 25) continue;
            if (Terrain.Sample(i, j) < 0.4f) offenders.Add($"{p}: {Terrain.Sample(i, j)}");
        }
        Assert.True(offenders.Count == 0, $"{offenders.Count} low dry samples, e.g. {string.Join("; ", offenders.Take(5))}");
    }

    [Fact]
    public void Roads_are_flat_across_and_gentle_along()
    {
        float maxGrade = MathF.Tan(Angles.Deg(8.3f));
        var problems = new List<string>();
        foreach (RoadLine road in L.AllRoads)
        {
            float halfCheck = road.IsTrack ? 2f : 3f;
            bool hasFord = L.Fords.Any(f => f.Road == road);
            for (float s = 0; s <= road.Line.Length; s += 2)
            {
                Vector2 p = road.Line.PointAt(s), across = Perpendicular(road.Line.TangentAt(s));
                if (!NearOtherRoad(road, p))
                {
                    float lo = float.PositiveInfinity, hi = float.NegativeInfinity;
                    for (float o = -halfCheck; o <= halfCheck; o += 1)
                    {
                        float h = Height(p + across * o);
                        lo = MathF.Min(lo, h);
                        hi = MathF.Max(hi, h);
                    }
                    if (hi - lo > 0.1f) problems.Add($"{road.Name} at {s:F0} m {p}: {hi - lo:F2} m across");
                }

                if (s + 10 <= road.Line.Length && !OnRamp(p) && !OnRamp(road.Line.PointAt(s + 10)))
                {
                    float grade = MathF.Abs(Height(road.Line.PointAt(s + 10)) - Height(p)) / 10;
                    if (grade > maxGrade) problems.Add($"{road.Name} at {s:F0} m {p}: {MathF.Atan(grade) * 180 / MathF.PI:F1}° along");
                }

                if (Field.WaterDepthAt(p) > 0 && !(hasFord && RiverDistance(p) < L.FordRiverReach))
                    problems.Add($"{road.Name} at {s:F0} m {p}: {Field.WaterDepthAt(p):F2} m under water");
            }
        }
        Assert.True(problems.Count == 0, $"{problems.Count} problems: {string.Join("; ", problems.Take(8))}");
    }

    [Fact]
    public void Road_cells_are_road_and_dirt_road_cells_dirt()
    {
        foreach (RoadLine road in L.AllRoads)
            for (float s = 0; s <= road.Line.Length; s += 1)
            {
                Vector2 p = road.Line.PointAt(s);
                SurfaceKind kind = Field.SurfaceAt(p);
                if (!road.IsTrack) Assert.True(kind == SurfaceKind.Road, $"{road.Name} at {s} m {p} is {kind}");
                else Assert.True(kind == SurfaceKind.Dirt || (kind == SurfaceKind.Road && RiverlandFixture.Roads.DistanceAt(p.X, p.Y) <= L.RoadHalfWidth + 2),
                    $"{road.Name} at {s} m {p} is {kind}");
            }
    }

    [Fact]
    public void The_plateau_stands_22_metres_up_behind_cliffs_with_two_ramps()
    {
        float top = Height(new Vector2(240, 200));
        var south = new List<float>();
        for (float x = 80; x <= 400; x += 10)
        {
            var p = new Vector2(x, L.Plateau.Max.Y + 60);
            if (L.SouthRamp.Locate(p).Across < 25) continue;
            south.Add(Height(p));
        }
        Assert.True(top - south.Average() >= 18, $"plateau centre {top:F1} m, the plain south of it {south.Average():F1} m");

        // Each side: some line across its rim has a step steeper than 38° between neighbouring samples.
        float steep = MathF.Tan(Angles.Deg(38));
        Assert.True(AnySteep(x => new Vector2(x, L.Plateau.Min.Y), Vector2.UnitY, L.Plateau.Min.X + 20, L.Plateau.Max.X - 20, steep), "north side has no cliff");
        Assert.True(AnySteep(x => new Vector2(x, L.Plateau.Max.Y), Vector2.UnitY, L.Plateau.Min.X + 20, L.Plateau.Max.X - 20, steep), "south side has no cliff");
        Assert.True(AnySteep(z => new Vector2(L.Plateau.Min.X, z), Vector2.UnitX, L.Plateau.Min.Y + 20, L.Plateau.Max.Y - 20, steep), "west side has no cliff");
        Assert.True(AnySteep(z => new Vector2(L.Plateau.Max.X, z), Vector2.UnitX, L.Plateau.Min.Y + 20, L.Plateau.Max.Y - 20, steep), "east side has no cliff");

        float rampLimit = MathF.Tan(Angles.Deg(14));
        foreach (Ramp ramp in L.Ramps)
        {
            Vector2 down = ramp.Down;
            for (float s = 0; s + 2 <= ramp.Length; s += 2)
            {
                float grade = MathF.Abs(Height(ramp.Top + down * (s + 2)) - Height(ramp.Top + down * s)) / 2;
                Assert.True(grade <= rampLimit, $"ramp {ramp.Top} at {s} m: {MathF.Atan(grade) * 180 / MathF.PI:F1}°");
            }
            float plateauTop = Height(ramp.Top - down * 15), rampTop = Height(ramp.Top);
            float plain = Height(ramp.Bottom + down * 15), rampBottom = Height(ramp.Bottom);
            Assert.True(MathF.Abs(rampTop - plateauTop) <= 1, $"ramp {ramp.Top}: top {rampTop:F2} m, plateau {plateauTop:F2} m");
            Assert.True(MathF.Abs(rampBottom - plain) <= 1, $"ramp {ramp.Top}: bottom {rampBottom:F2} m, plain {plain:F2} m");
            Assert.True(rampTop - rampBottom >= 18, $"ramp {ramp.Top} climbs only {rampTop - rampBottom:F1} m");
        }
    }

    [Fact]
    public void Hills_and_dunes_are_where_the_layout_puts_them()
    {
        Assert.True(Prominence(L.SouthWestHill) >= 14, $"south-west hill stands {Prominence(L.SouthWestHill):F1} m");
        Assert.True(Prominence(L.EastHill) >= 11, $"east hill stands {Prominence(L.EastHill):F1} m");

        for (int j = 0; j < Cells; j++)
        for (int i = 0; i < Cells; i++)
        {
            Vector2 p = CellCentre(i, j);
            if (!L.Dunes.Contains(p)) continue;
            SurfaceKind kind = Field.SurfaceAt(p);
            float track = RiverlandFixture.Tracks.DistanceAt(p.X, p.Y);
            bool trackOrFord = track <= L.TrackHalfWidth + 1.5f
                               || (track <= L.FordHalfWidth + 1.5f && Field.WaterDepthAt(p) > 0 && RiverDistance(p) < L.FordRiverReach);
            Assert.True(kind == SurfaceKind.Sand || (kind == SurfaceKind.Dirt && trackOrFord), $"dune cell {p} is {kind}");
        }

        // Crest to the mean of the troughs either side, along (1, 1), away from the river, the track and the edges.
        Vector2 diagonal = Vector2.Normalize(new Vector2(1, 1));
        float half = L.DuneSpacing / 2, root2 = MathF.Sqrt(2f);
        int ridges = 0;
        for (float x0 = L.Dunes.Min.X; x0 <= L.Dunes.Max.X; x0 += 20)
        for (float z0 = L.Dunes.Min.Y; z0 <= L.Dunes.Max.Y; z0 += 20)
        {
            float u0 = (x0 + z0) / root2, crestU = L.DuneSpacing / 4 + L.DuneSpacing * MathF.Round((u0 - L.DuneSpacing / 4) / L.DuneSpacing);
            Vector2 crest = new Vector2(x0, z0) + diagonal * (crestU - u0);
            Vector2[] points = { crest - diagonal * half, crest, crest + diagonal * half };
            if (points.Any(q => L.Dunes.Inside(q) < L.DuneFade || RiverDistance(q) < 45 || RiverlandFixture.Tracks.DistanceAt(q.X, q.Y) < 15)) continue;
            float rise = Height(crest) - (Height(points[0]) + Height(points[2])) / 2;
            Assert.True(rise is >= 3 and <= 5, $"dune crest {crest} stands {rise:F2} m over its troughs");
            ridges++;
        }
        Assert.True(ridges >= 20, $"only {ridges} ridges measured");
    }

    [Fact]
    public void Banks_are_sand_and_open_plains_grass()
    {
        int banks = 0;
        for (float s = 0; s <= L.River.Length; s += 4)
        {
            Vector2 c = L.River.PointAt(s), across = Perpendicular(L.River.TangentAt(s));
            foreach (int side in new[] { -1, 1 })
            {
                Vector2 p = c + across * (22 * side);
                if (!InMap(p) || Vector2.Distance(p, c) < 1 || NearAnyRoad(p, 15)) continue;
                Assert.True(Field.SurfaceAt(p) == SurfaceKind.Sand, $"bank at {p} is {Field.SurfaceAt(p)}");
                banks++;
            }
        }
        Assert.True(banks > 500, $"only {banks} bank points");

        var open = new List<Vector2>();
        for (float z = 50; z <= 1150 && open.Count < 20; z += 47)
        for (float x = 50; x <= 1150 && open.Count < 20; x += 61)
        {
            var p = new Vector2(x, z);
            if (IsOpenPlain(p)) open.Add(p);
        }
        Assert.Equal(20, open.Count);
        foreach (Vector2 p in open) Assert.True(Field.SurfaceAt(p) == SurfaceKind.Grass, $"open plain at {p} is {Field.SurfaceAt(p)}");
    }

    [Fact]
    public void The_border_rises_except_where_the_river_runs_out()
    {
        int n = Terrain.SamplesX - 1;
        var problems = new List<string>();
        for (int k = 0; k <= n; k++)
        {
            foreach ((int i, int j) in new[] { (k, 0), (k, n), (0, k), (n, k) })
            {
                var edge = new Vector2(i * RiverlandMap.CellSize, j * RiverlandMap.CellSize);
                if (RiverDistance(edge) < 40) continue;
                Vector2 inner = Vector2.Clamp(edge, new Vector2(RiverlandMap.Border), new Vector2(L.Size - RiverlandMap.Border));
                float rise = Terrain.Sample(i, j) - Height(inner);
                if (rise < 6) problems.Add($"{edge}: {rise:F2} m over {inner}");
            }
        }
        Assert.True(problems.Count == 0, $"{problems.Count} low edge samples, e.g. {string.Join("; ", problems.Take(5))}");

        Assert.True(Field.WaterDepthAt(new Vector2(640, 0)) > 0, "no water where the river leaves the north edge");
        Assert.True(Field.WaterDepthAt(new Vector2(980, L.Size)) > 0, "no water where the river leaves the south edge");
    }

    // ---- helpers ----

    private static Vector2 CellCentre(int i, int j) => new((i + 0.5f) * RiverlandMap.CellSize, (j + 0.5f) * RiverlandMap.CellSize);

    private static float Height(Vector2 p) => Terrain.HeightAt(p.X, p.Y);

    private static Vector2 Perpendicular(Vector2 v) => new(-v.Y, v.X);

    private static bool InMap(Vector2 p) => p.X >= 0 && p.X <= L.Size && p.Y >= 0 && p.Y <= L.Size;

    private static float RiverDistance(Vector2 p) => RiverlandFixture.River.DistanceAt(p.X, p.Y);

    private static bool NearAnyRoad(Vector2 p, float distance) =>
        RiverlandFixture.Roads.DistanceAt(p.X, p.Y) < distance || RiverlandFixture.Tracks.DistanceAt(p.X, p.Y) < distance;

    /// <summary>Within 15 m of another road's or track's centre line: a junction, where two levelled surfaces meet.</summary>
    private static bool NearOtherRoad(RoadLine road, Vector2 p) =>
        L.AllRoads.Any(other => other != road && other.Line.DistanceTo(p, out _) < 15);

    private static bool OnRamp(Vector2 p) => L.Ramps.Any(ramp =>
    {
        (float along, float across) = ramp.Locate(p);
        return across <= L.RampHalfWidth + 4 && along >= -L.RampLanding - 4 && along <= ramp.Length + 4;
    });

    /// <summary>A long way from roads, water, forests, dunes and villages: nothing should be there but grass.</summary>
    private static bool IsOpenPlain(Vector2 p) =>
        !NearAnyRoad(p, 50) && RiverDistance(p) > L.RiverHalfWidth + 50 && L.LakeShoreDistance(p) > 50
        && L.Forests.All(f => f.SignedDistance(p) > L.ForestJitter + 50) && L.Dunes.SignedDistance(p) > 50
        && p.X >= 30 + 20 && p.Y >= 30 + 20 && p.X <= L.Size - 50 && p.Y <= L.Size - 50
        && new[] { L.Town, L.NorthVillage, L.WestVillage, L.SouthVillage }.Concat(L.Farms).All(v => Vector2.Distance(p, v) > 160);

    /// <summary>Some line across the rim at <paramref name="at"/>(t), t in [from, to], has a step between samples steeper than <paramref name="grade"/>.</summary>
    private static bool AnySteep(Func<float, Vector2> at, Vector2 across, float from, float to, float grade)
    {
        for (float t = from; t <= to; t += 4)
        {
            Vector2 rim = at(t);
            for (float o = -L.PlateauJitter - 10; o < L.PlateauJitter + 10; o += 1)
            {
                float step = MathF.Abs(Height(rim + across * (o + 2)) - Height(rim + across * o)) / 2;
                if (step > grade) return true;
            }
        }
        return false;
    }

    /// <summary>The hill's top over the mean of the ground 200 m around it (where that is in the playable area).</summary>
    private static float Prominence(TerrainGenerator.Hill hill)
    {
        var centre = new Vector2(hill.X, hill.Z);
        var ring = new List<float>();
        for (int k = 0; k < 36; k++)
        {
            float a = k * MathF.PI / 18;
            Vector2 p = centre + 200 * new Vector2(MathF.Cos(a), MathF.Sin(a));
            if (p.X < 30 || p.Y < 30 || p.X > L.Size - 30 || p.Y > L.Size - 30) continue;
            ring.Add(Height(p));
        }
        return Height(centre) - ring.Average();
    }
}
