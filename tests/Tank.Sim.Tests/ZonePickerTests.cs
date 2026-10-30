using System.Diagnostics;
using System.Numerics;
using Tank.Sim.Maps;
using Tank.Sim.Modes;
using Tank.Sim.Navigation;
using Xunit.Abstractions;

namespace Tank.Sim.Tests;

/// <summary>Where the rings go (stage 6 design §3.1): nested, centred on driveable land the spawns reach, the same for the same seed.</summary>
public sealed class ZonePickerTests
{
    private static readonly Lazy<bool[]> RiverlandLand =
        new(() => ZonePicker.Land(RiverlandFixture.Field, RiverlandFixture.Grid, RiverlandMap.Spawns.Solo));

    private readonly ITestOutputHelper _output;

    public ZonePickerTests(ITestOutputHelper output) => _output = output;

    private static ZoneRing[] PickRiverland(uint seed) =>
        ZonePicker.Pick(RiverlandFixture.Field, RiverlandFixture.Grid, RiverlandMap.Spawns.Solo, seed);

    [Fact]
    public void Riverland_rings_over_two_hundred_seeds_are_nested_on_dry_land_with_scheduled_radii()
    {
        const int Seeds = 200;
        bool[] land = RiverlandLand.Value;
        Battlefield field = RiverlandFixture.Field;
        NavGrid grid = RiverlandFixture.Grid;
        ZoneRing outer = ZoneSchedule.Outer(field);
        int scans = 0;
        float nearestWater = float.MaxValue;
        for (uint seed = 1; seed <= Seeds; seed++)
        {
            ZoneRing[] rings = ZonePicker.Pick(field, grid, RiverlandMap.Spawns.Solo, seed ^ ZonePicker.SeedSalt, out int scanned);
            scans += scanned;
            Assert.Equal(1 + ZoneSchedule.Rings, rings.Length);
            Assert.Equal(outer, rings[0]);
            for (int k = 1; k <= ZoneSchedule.Rings; k++)
            {
                Vector2 c = rings[k].Centre;
                Assert.Equal(ZoneSchedule.Stage(k).Radius, rings[k].Radius);
                Assert.True(Vector2.Distance(c, rings[k - 1].Centre) + rings[k].Radius <= rings[k - 1].Radius + 0.01f,
                    $"seed {seed}: ring {k} {rings[k]} reaches out of ring {k - 1} {rings[k - 1]}");
                (int i, int j) = NavGrid.CellOf(c);
                Assert.True(land[j * grid.CellsX + i], $"seed {seed}: ring {k}'s centre {c} is not on land the spawns reach");
                Assert.True(field.WaterDepthAt(c) <= 0f, $"seed {seed}: ring {k}'s centre {c} is {field.WaterDepthAt(c):F3} m under water");
                nearestWater = MathF.Min(nearestWater, DistanceToWater(field, c));
            }
            ZoneSchedule.Check(rings, "rings");
        }
        _output.WriteLine($"riverland, seeds 1–{Seeds}: {scans} of {Seeds * ZoneSchedule.Rings} rings scanned for land after {ZonePicker.Tries} misses; " +
                          $"nearest water to a centre {(nearestWater == float.MaxValue ? "none within 30 m" : $"{nearestWater:F1} m")}");
        Assert.True(scans <= 2, $"{scans} rings fell back to the scan");
    }

    [Fact]
    public void When_every_draw_misses_the_centre_is_the_nearest_land_cells()
    {
        // The riverland's grid with only two one-cell islands left open: A about 200 m east of the middle, B about 270 m north. A
        // ring-1 draw lands on a given cell once in some 30 000, so all 64 miss and the scan takes the nearer island, A; from then
        // on each disc round A holds only A.
        Battlefield field = RiverlandFixture.Field;
        NavGrid grid = RiverlandFixture.Grid.Copy();
        (int ai, int aj) = DryOpenCellNear(field, grid, new Vector2(800, 600));
        (int bi, int bj) = DryOpenCellNear(field, grid, new Vector2(600, 330));
        for (int j = 0; j < grid.CellsZ; j++)
        for (int i = 0; i < grid.CellsX; i++)
            if ((i, j) != (ai, aj) && (i, j) != (bi, bj) && grid.IsOpen(i, j)) grid.SetCostForTests(i, j, NavGrid.Blocked);
        var spawns = new[] { new SpawnPoint(NavGrid.CentreOf(ai, aj), 0f, 0), new SpawnPoint(NavGrid.CentreOf(bi, bj), 0f, 0) };

        ZoneRing[] rings = ZonePicker.Pick(field, grid, spawns, 5, out int scanned);
        _output.WriteLine($"island A ({ai}, {aj}), B ({bi}, {bj}); {scanned} rings scanned; ring 1 {rings[1]}");
        Assert.Equal(NavGrid.CentreOf(ai, aj), rings[1].Centre);
        for (int k = 2; k <= ZoneSchedule.Rings; k++) Assert.Equal((ai, aj), NavGrid.CellOf(rings[k].Centre));
        Assert.True(scanned >= 2, $"only {scanned} rings scanned");
        ZoneSchedule.Check(rings, "rings");
        Assert.Equal(rings, ZonePicker.Pick(field, grid, spawns, 5));
    }

    [Fact]
    public void A_map_whose_spawns_reach_no_land_throws()
    {
        Battlefield field = RiverlandFixture.Field;
        Assert.Throws<InvalidOperationException>(() => ZonePicker.Pick(field, RiverlandFixture.Grid, Array.Empty<SpawnPoint>(), 1));

        Battlefield sandbox = MapCatalog.Sandbox.NewBattlefield();
        NavGrid closed = MapCatalog.Sandbox.NewNavGrid();   // every cell closed: no spawn finds an open one
        for (int j = 0; j < closed.CellsZ; j++)
        for (int i = 0; i < closed.CellsX; i++)
            if (closed.IsOpen(i, j)) closed.SetCostForTests(i, j, NavGrid.Blocked);
        Assert.Throws<InvalidOperationException>(() => ZonePicker.Pick(sandbox, closed, MapCatalog.Sandbox.Spawns.Solo, 1));
    }

    /// <summary>How far from <paramref name="p"/> the nearest point under water lies, sampled every metre within 30 m (+∞ for none).</summary>
    private static float DistanceToWater(Battlefield field, Vector2 p)
    {
        const int Reach = 30;
        float best = float.MaxValue;
        for (int dz = -Reach; dz <= Reach; dz++)
        for (int dx = -Reach; dx <= Reach; dx++)
        {
            float d = MathF.Sqrt(dx * dx + dz * dz);
            if (d > Reach || d >= best) continue;
            if (field.WaterDepthAt(p + new Vector2(dx, dz)) > 0f) best = d;
        }
        return best;
    }

    /// <summary>The open cell with a dry centre nearest <paramref name="p"/>.</summary>
    private static (int I, int J) DryOpenCellNear(Battlefield field, NavGrid grid, Vector2 p)
    {
        (int I, int J) best = (-1, -1);
        float bestD = float.MaxValue;
        (int ci, int cj) = NavGrid.CellOf(p);
        for (int j = cj - 20; j <= cj + 20; j++)
        for (int i = ci - 20; i <= ci + 20; i++)
        {
            if (!grid.IsOpen(i, j) || field.WaterDepthAt(NavGrid.CentreOf(i, j)) > 0f) continue;
            float d = Vector2.DistanceSquared(NavGrid.CentreOf(i, j), p);
            if (d < bestD) (best, bestD) = ((i, j), d);
        }
        Assert.True(best.I >= 0, $"no dry open cell near {p}");
        return best;
    }

    [Fact]
    public void The_same_seed_picks_the_same_rings_and_seeds_differ()
    {
        Assert.Equal(PickRiverland(7), PickRiverland(7));
        var firstCentres = new HashSet<Vector2>();
        for (uint seed = 1; seed <= 20; seed++) firstCentres.Add(PickRiverland(seed)[1].Centre);
        _output.WriteLine($"{firstCentres.Count} different ring-1 centres over 20 seeds");
        Assert.True(firstCentres.Count >= 15, $"only {firstCentres.Count} different ring-1 centres");
    }

    [Fact]
    public void Picking_changes_neither_the_field_nor_the_grid()
    {
        Battlefield field = RiverlandFixture.Field;
        NavGrid grid = RiverlandFixture.Grid;
        int fieldVersion = field.StateVersion, gridVersion = grid.Version;
        PickRiverland(3);
        ZonePicker.Land(field, grid, RiverlandMap.Spawns.Solo);
        Assert.Equal(fieldVersion, field.StateVersion);
        Assert.Equal(gridVersion, grid.Version);
    }

    [Fact]
    public void Riverland_land_holds_every_spawn_but_not_the_lake_or_a_closed_cell()
    {
        bool[] land = RiverlandLand.Value;
        NavGrid grid = RiverlandFixture.Grid;
        foreach (SpawnPoint spawn in RiverlandMap.Spawns.Solo)
        {
            (int i, int j) = NavGrid.CellOf(spawn.Position);
            Assert.True(land[j * grid.CellsX + i], $"the spawn at {spawn.Position} is not on land");
        }
        (int li, int lj) = NavGrid.CellOf(RiverlandMap.Landmarks.Lake);
        Assert.False(land[lj * grid.CellsX + li]);

        int blocked = 0;
        for (int j = grid.CellsZ / 4; j < grid.CellsZ * 3 / 4; j++)
        for (int i = grid.CellsX / 4; i < grid.CellsX * 3 / 4; i++)
        {
            if (grid.IsOpen(i, j)) continue;
            Assert.False(land[j * grid.CellsX + i], $"closed cell ({i}, {j}) counts as land");
            blocked++;
        }
        Assert.True(blocked > 0, "no closed cell in the middle of the riverland to check");
    }

    [Fact]
    public void Cells_walled_off_from_the_spawns_are_not_land()
    {
        Battlefield field = MapCatalog.Sandbox.NewBattlefield();
        NavGrid grid = MapCatalog.Sandbox.NewNavGrid();
        IReadOnlyList<SpawnPoint> spawns = MapCatalog.Sandbox.Spawns.Solo;
        (int i0, int j0) = OpenSquare(grid, spawns, 9);   // the 5 × 5 block, its wall round it and the cells just outside
        for (int j = 1; j <= 7; j++)
        for (int i = 1; i <= 7; i++)
            if (i == 1 || i == 7 || j == 1 || j == 7) grid.SetCostForTests(i0 + i, j0 + j, NavGrid.Blocked);

        bool[] land = ZonePicker.Land(field, grid, spawns);
        for (int j = 2; j <= 6; j++)
        for (int i = 2; i <= 6; i++)
            Assert.False(land[(j0 + j) * grid.CellsX + i0 + i], $"walled-in cell ({i0 + i}, {j0 + j}) counts as land");
        for (int j = 0; j <= 8; j++)
        for (int i = 0; i <= 8; i++)
            if (i == 0 || i == 8 || j == 0 || j == 8)
                Assert.True(land[(j0 + j) * grid.CellsX + i0 + i], $"cell ({i0 + i}, {j0 + j}) beside the wall is not land");
    }

    [Fact]
    public void Sandbox_rings_are_nested_too()
    {
        Battlefield field = MapCatalog.Sandbox.NewBattlefield();
        NavGrid grid = MapCatalog.Sandbox.NewNavGrid();
        for (uint seed = 1; seed <= 5; seed++)
        {
            ZoneRing[] rings = ZonePicker.Pick(field, grid, MapCatalog.Sandbox.Spawns.Solo, seed);
            Assert.Equal(ZoneSchedule.Outer(field), rings[0]);
            for (int k = 1; k <= ZoneSchedule.Rings; k++)
                Assert.True(Vector2.Distance(rings[k].Centre, rings[k - 1].Centre) + rings[k].Radius <= rings[k - 1].Radius + 0.01f,
                    $"seed {seed}: ring {k} {rings[k]} reaches out of ring {k - 1} {rings[k - 1]}");
            ZoneSchedule.Check(rings, "rings");
        }
    }

    [Fact]
    public void Picking_without_a_field_grid_or_spawns_throws()
    {
        Assert.Throws<ArgumentNullException>(() => ZonePicker.Pick(null!, RiverlandFixture.Grid, RiverlandMap.Spawns.Solo, 1));
        Assert.Throws<ArgumentNullException>(() => ZonePicker.Pick(RiverlandFixture.Field, null!, RiverlandMap.Spawns.Solo, 1));
        Assert.Throws<ArgumentNullException>(() => ZonePicker.Land(RiverlandFixture.Field, RiverlandFixture.Grid, null!));
        Assert.Throws<ArgumentException>(() => ZonePicker.Land(RiverlandFixture.Field, MapCatalog.Sandbox.NewNavGrid(), RiverlandMap.Spawns.Solo));
    }

    /// <summary>The corner of a size × size square of open cells away from every spawn's cell.</summary>
    private static (int I, int J) OpenSquare(NavGrid grid, IReadOnlyList<SpawnPoint> spawns, int size)
    {
        var spawnCells = spawns.Select(s => NavGrid.CellOf(s.Position)).ToHashSet();
        for (int j0 = 0; j0 + size <= grid.CellsZ; j0++)
        for (int i0 = 0; i0 + size <= grid.CellsX; i0++)
        {
            bool fits = true;
            for (int j = 0; j < size && fits; j++)
            for (int i = 0; i < size && fits; i++)
                fits = grid.IsOpen(i0 + i, j0 + j) && !spawnCells.Contains((i0 + i, j0 + j));
            if (fits) return (i0, j0);
        }
        throw new InvalidOperationException($"no open {size} × {size} square on the grid");
    }
}

/// <summary>What picking a match's rings costs on the riverland (it runs once a match, on the server, before the tanks join).</summary>
[Collection(TimingCollection.Name)]
public sealed class ZonePickerTimingTests
{
    private const int Repeats = 3;

    private readonly ITestOutputHelper _output;

    public ZonePickerTimingTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Picking_the_riverlands_rings_is_quick()
    {
        Battlefield field = RiverlandFixture.Field;
        NavGrid grid = RiverlandFixture.Grid;
        ZonePicker.Pick(field, grid, RiverlandMap.Spawns.Solo, 1);   // warm-up
        double best = double.MaxValue;
        for (int r = 0; r < Repeats; r++)
        {
            long start = Stopwatch.GetTimestamp();
            ZonePicker.Pick(field, grid, RiverlandMap.Spawns.Solo, (uint)(r + 2));
            best = Math.Min(best, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
        _output.WriteLine($"ZonePicker.Pick(riverland): {best:F1} ms at best of {Repeats}");
#if DEBUG
        const double Limit = 2000.0;
#else
        const double Limit = 500.0;
#endif
        Assert.True(best <= Limit, $"picking took {best:F1} ms at best (limit {Limit} ms)");
    }
}
