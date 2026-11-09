using System.Diagnostics;
using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Modes;
using Tank.Sim.Navigation;
using Tank.Sim.Terrain;
using Xunit.Abstractions;

namespace Tank.Sim.Tests;

/// <summary>
/// Where a match's crates go (stage 6 design §4.1): 48 on the riverland, 29 by the houses and the rest by the roads and at the
/// forests' edges, 25 m apart, on clear ground the spawns reach; the same for the same seed.
/// </summary>
public sealed class CratePlacerTests
{
    private static readonly Lazy<bool[]> RiverlandLand =
        new(() => ZonePicker.Land(RiverlandFixture.Field, RiverlandFixture.Grid, RiverlandMap.Spawns.Solo));

    private readonly ITestOutputHelper _output;

    public CratePlacerTests(ITestOutputHelper output) => _output = output;

    private static Crate[] PlaceRiverland(uint seed) =>
        CratePlacer.Place(RiverlandFixture.Field, RiverlandFixture.Grid, RiverlandMap.Spawns.Solo, seed, RiverlandLand.Value);

    private static int[] CountKinds(IEnumerable<CrateKind> kinds)
    {
        var counts = new int[CrateRules.Kinds];
        foreach (CrateKind kind in kinds) counts[(int)kind]++;
        return counts;
    }

    private static readonly int[] RiverlandShares = { 12, 10, 10, 10, 6 };

    [Fact]
    public void Riverland_seeds_1_to_50_place_48_crates_by_houses_roads_and_forest_edges_on_clear_ground()
    {
        const int Seeds = 50;
        Battlefield field = RiverlandFixture.Field;
        NavGrid grid = RiverlandFixture.Grid;
        IReadOnlyList<SpawnPoint> spawns = RiverlandMap.Spawns.Solo;
        bool[] land = RiverlandLand.Value;
        int houseQuota = (int)MathF.Round(CratePlacer.Count * CratePlacer.HouseShare);
        int roadQuota = (int)MathF.Round((CratePlacer.Count - houseQuota) * CratePlacer.RoadShare, MidpointRounding.AwayFromZero);
        Assert.Equal(29, houseQuota);
        Assert.Equal(10, roadQuota);
        int leastMargin = int.MaxValue, mostAttempts = 0;
        float minNormalY = MathF.Cos(CratePlacer.MaxSlopeDegrees * MathF.PI / 180f);
        for (uint seed = 1; seed <= Seeds; seed++)
        {
            Crate[] crates = CratePlacer.Place(field, grid, spawns, seed ^ CratePlacer.SeedSalt, land, out int attempts);
            Assert.Equal(CratePlacer.Count, crates.Length);
            leastMargin = Math.Min(leastMargin, CratePlacer.Attempts - attempts);
            mostAttempts = Math.Max(mostAttempts, attempts);
            for (int i = 0; i < crates.Length; i++)
            {
                Crate crate = crates[i];
                Vector2 p = crate.Position;
                Assert.Equal(i, crate.Id);
                bool byHouse = CratePlacer.ByHouse(field, p);
                Assert.True(byHouse == i < houseQuota, $"seed {seed}: crate {i} at {p} is {(byHouse ? "" : "not ")}by a house");
                if (i >= houseQuota && i < houseQuota + roadQuota)
                    Assert.True(CratePlacer.ByRoad(field, p), $"seed {seed}: crate {i} at {p} is not by a road");
                if (i >= houseQuota + roadQuota)
                    Assert.True(CratePlacer.ByForest(field, p), $"seed {seed}: crate {i} at {p} is not by a forest");
                Assert.True(CratePlacer.Clear(field, grid, land, spawns, p), $"seed {seed}: crate {i} at {p} is not on clear ground");
                Assert.True(field.WaterDepthAt(p) <= 0f, $"seed {seed}: crate {i} at {p} is under water");
                Assert.True(field.Terrain.NormalAt(p.X, p.Y).Y >= minNormalY, $"seed {seed}: crate {i} at {p} is on a slope");
                (int ci, int cj) = NavGrid.CellOf(p);
                Assert.True(grid.IsOpen(ci, cj), $"seed {seed}: crate {i} at {p} is on a closed cell");
                Assert.True(land[cj * grid.CellsX + ci], $"seed {seed}: crate {i} at {p} is not on land the spawns reach");
                foreach (SpawnPoint spawn in spawns)
                    Assert.True(Vector2.Distance(spawn.Position, p) >= CratePlacer.SpawnClear, $"seed {seed}: crate {i} at {p} is by the spawn at {spawn.Position}");
                for (int k = 0; k < i; k++)
                    Assert.True(Vector2.Distance(crates[k].Position, p) >= CratePlacer.Spacing, $"seed {seed}: crates {k} and {i} are {Vector2.Distance(crates[k].Position, p):F1} m apart");
            }
            Assert.Equal(RiverlandShares, CountKinds(crates.Select(c => c.Kind)));
        }
        _output.WriteLine($"riverland, seeds 1–{Seeds}: at most {mostAttempts} of {CratePlacer.Attempts} draws used; least margin {leastMargin} " +
                          $"({(float)leastMargin / CratePlacer.Attempts:P0} of the draws left; within a quarter counts as roomy)");
    }

    [Fact]
    public void The_same_seed_places_the_same_crates_and_seeds_differ()
    {
        Assert.Equal(PlaceRiverland(7), PlaceRiverland(7));
        Assert.NotEqual(PlaceRiverland(1)[0].Position, PlaceRiverland(2)[0].Position);
        var firsts = new HashSet<Vector2>();
        for (uint seed = 1; seed <= 20; seed++) firsts.Add(PlaceRiverland(seed)[0].Position);
        _output.WriteLine($"{firsts.Count} different first crates over 20 seeds");
        Assert.True(firsts.Count >= 15, $"only {firsts.Count} different first crates");
    }

    [Fact]
    public void Kinds_of_48_are_12_10_10_10_6()
    {
        var rng = new XorShift(3);
        CrateKind[] kinds = CratePlacer.Kinds(48, ref rng);
        Assert.Equal(48, kinds.Length);
        Assert.Equal(RiverlandShares, CountKinds(kinds));
    }

    [Fact]
    public void Kinds_of_10_are_3_2_2_2_1_and_of_1_is_one()
    {
        var rng = new XorShift(3);
        Assert.Equal(new[] { 3, 2, 2, 2, 1 }, CountKinds(CratePlacer.Kinds(10, ref rng)));
        Assert.Single(CratePlacer.Kinds(1, ref rng));
        Assert.Empty(CratePlacer.Kinds(0, ref rng));
    }

    [Fact]
    public void Kinds_are_shuffled_by_the_generator()
    {
        var a = new XorShift(1);
        var b = new XorShift(2);
        CrateKind[] first = CratePlacer.Kinds(48, ref a), second = CratePlacer.Kinds(48, ref b);
        Assert.NotEqual(first, second);
        var again = new XorShift(1);
        Assert.Equal(first, CratePlacer.Kinds(48, ref again));
    }

    // ---- synthetic fields ----

    /// <summary>One spawn in the middle of TestWorld's 200 m flat field.</summary>
    private static readonly SpawnPoint[] MiddleSpawn = { new(new Vector2(100, 100), 0f, 0) };

    private static Prop House(int id, float x, float z) => HouseTests.House(id, new Vector2(x, z));

    private static (Battlefield Field, NavGrid Grid, bool[] Land) World(Battlefield field)
    {
        NavGrid grid = NavGrid.Build(field);
        return (field, grid, ZonePicker.Land(field, grid, MiddleSpawn));
    }

    private static void AssertSpacedAndClear(Crate[] crates, Battlefield field, NavGrid grid, bool[] land)
    {
        for (int i = 0; i < crates.Length; i++)
        {
            Assert.Equal(i, crates[i].Id);
            Assert.True(CratePlacer.Clear(field, grid, land, MiddleSpawn, crates[i].Position), $"crate {i} at {crates[i].Position} is not on clear ground");
            for (int k = 0; k < i; k++)
                Assert.True(Vector2.Distance(crates[k].Position, crates[i].Position) >= CratePlacer.Spacing, $"crates {k} and {i} are too close");
        }
    }

    [Fact]
    public void A_flat_field_without_houses_roads_or_trees_gets_no_crates()
    {
        (Battlefield field, NavGrid grid, bool[] land) = World(TestWorld.FlatField());
        Assert.Empty(CratePlacer.Place(field, grid, MiddleSpawn, 1, land));
        Assert.Empty(CratePlacer.Place(field, grid, MiddleSpawn, 1));
    }

    [Fact]
    public void Four_houses_on_flat_ground_get_house_crates_only()
    {
        (Battlefield field, NavGrid grid, bool[] land) = World(TestWorld.FlatField(House(1, 60, 60), House(2, 140, 60), House(3, 60, 140), House(4, 140, 140)));
        Crate[] crates = CratePlacer.Place(field, grid, MiddleSpawn, 5, land, out int attempts);
        _output.WriteLine($"four houses: {crates.Length} crates after {attempts} draws");
        Assert.InRange(crates.Length, 8, 29);
        foreach (Crate crate in crates)
        {
            Assert.True(CratePlacer.ByHouse(field, crate.Position), $"crate {crate.Id} at {crate.Position} is not by a house");
            float nearest = field.Props.Min(h => h.Footprint.DistanceToPoint(crate.Position));
            Assert.InRange(nearest, CratePlacer.HouseMin, CratePlacer.HouseMax);
        }
        AssertSpacedAndClear(crates, field, grid, land);
        Assert.Equal(CratePlacer.Attempts, attempts);   // the quota of 29 can't be met: every house draw is spent, and the open draws find nothing
    }

    [Fact]
    public void Clear_refuses_a_house_footprint_the_spawn_and_the_edge()
    {
        (Battlefield field, NavGrid grid, bool[] land) = World(TestWorld.FlatField(House(1, 60, 60)));
        Assert.False(CratePlacer.Clear(field, grid, land, MiddleSpawn, new Vector2(60, 60)));        // in the house
        Assert.False(CratePlacer.Clear(field, grid, land, MiddleSpawn, new Vector2(60, 64.5f)));     // 1 m off its wall: under PropClear
        Assert.True(CratePlacer.Clear(field, grid, land, MiddleSpawn, new Vector2(60, 70)));         // 6.5 m off it
        Assert.False(CratePlacer.Clear(field, grid, land, MiddleSpawn, new Vector2(100, 115)));      // 15 m from the spawn
        Assert.True(CratePlacer.Clear(field, grid, land, MiddleSpawn, new Vector2(100, 125)));
        Assert.False(CratePlacer.Clear(field, grid, land, MiddleSpawn, new Vector2(13, 100)));       // inside Border + EdgeMargin = 14
        Assert.True(CratePlacer.Clear(field, grid, land, MiddleSpawn, new Vector2(15, 100)));
        Assert.False(CratePlacer.Clear(field, grid, land, MiddleSpawn, new Vector2(float.NaN, 100)));
    }

    [Fact]
    public void Clear_refuses_a_slope_and_water()
    {
        // Flat south of z = 100, rising north of it at 20 %: 11°.
        Battlefield field = TestWorld.Field(TestWorld.CliffNorth(100f, 0.2f));
        NavGrid grid = NavGrid.Build(field);
        bool[] land = ZonePicker.Land(field, grid, MiddleSpawn);
        Assert.False(CratePlacer.Clear(field, grid, land, MiddleSpawn, new Vector2(60, 80)));
        Assert.True(CratePlacer.Clear(field, grid, land, MiddleSpawn, new Vector2(60, 130)));

        Battlefield flooded = new(TestWorld.Flat(), Array.Empty<Prop>(), TestWorld.Border, waterLevel: 0.2f);
        NavGrid floodedGrid = NavGrid.Build(flooded);
        Assert.False(CratePlacer.Clear(flooded, floodedGrid, new bool[floodedGrid.CellsX * floodedGrid.CellsZ], MiddleSpawn, new Vector2(60, 130)));
    }

    [Fact]
    public void Clear_refuses_a_point_whose_metre_neighbour_is_steep()
    {
        // The same cliff: height = 0.2 × (100 − z) north of z = 100 (a 20 % plane), 0 south of it. NormalAt(x, z) takes the
        // central difference over z ± 1 m, so its slope is (HeightAt(z − 1) − HeightAt(z + 1)) / 2 and it passes 8° where
        // |HeightAt(z − 1) − HeightAt(z + 1)| ≤ 2 tan 8° = 0.281: for z + 1 ≥ 100 that is 0.2 × (101 − z) ≤ 0.281, z ≥ 99.6.
        // So (60, 100.5) is level itself (0.2 × 0.5 = 0.1 over 2 m: 2.9°), its neighbour 1 m north at z = 99.5 is not
        // (0.2 × 1.5 = 0.3: 8.5°), and 2 m further into the flat, (60, 102.5)'s neighbour at 101.5 is dead level.
        // Both points lie in nav cell (15, 25), z ∈ [100, 104), so the grid and the land say the same for both.
        Battlefield field = TestWorld.Field(TestWorld.CliffNorth(100f, 0.2f));
        NavGrid grid = NavGrid.Build(field);
        bool[] land = ZonePicker.Land(field, grid, MiddleSpawn);
        float minNormalY = MathF.Cos(CratePlacer.MaxSlopeDegrees * MathF.PI / 180f);
        Assert.True(field.Terrain.NormalAt(60, 100.5f).Y >= minNormalY, "the point itself is level");
        Assert.True(field.Terrain.NormalAt(60, 99.5f).Y < minNormalY, "its neighbour a metre north is steep");
        Assert.True(field.Terrain.NormalAt(60, 101.5f).Y >= minNormalY, "the other point's neighbour a metre north is level");
        Assert.Equal(NavGrid.CellOf(new Vector2(60, 100.5f)), NavGrid.CellOf(new Vector2(60, 102.5f)));

        Assert.False(CratePlacer.Clear(field, grid, land, MiddleSpawn, new Vector2(60, 100.5f)));
        Assert.True(CratePlacer.Clear(field, grid, land, MiddleSpawn, new Vector2(60, 102.5f)));
    }

    [Fact]
    public void A_road_across_flat_ground_gets_road_crates_only()
    {
        // A road 8 m wide across the middle of the field (cells j = 48 … 51: z ∈ [96, 104)).
        int cells = TestWorld.Samples - 1;
        var kinds = new SurfaceKind[cells * cells];
        for (int j = 48; j <= 51; j++)
        for (int i = 0; i < cells; i++)
            kinds[j * cells + i] = SurfaceKind.Road;
        Battlefield field = new(TestWorld.Flat(), Array.Empty<Prop>(), TestWorld.Border, new SurfaceMap(cells, cells, TestWorld.Cell, kinds));
        (_, NavGrid grid, bool[] land) = World(field);
        Assert.True(CratePlacer.ByRoad(field, new Vector2(50, 100)));
        Assert.True(CratePlacer.ByRoad(field, new Vector2(50, 111)));    // 7 m off the road's edge
        Assert.False(CratePlacer.ByRoad(field, new Vector2(50, 113)));   // 9 m off it
        Assert.False(CratePlacer.ByForest(field, new Vector2(50, 100)));

        Crate[] crates = CratePlacer.Place(field, grid, MiddleSpawn, 9, land, out int attempts);
        _output.WriteLine($"a road: {crates.Length} crates after {attempts} draws");
        Assert.InRange(crates.Length, 5, 10);
        foreach (Crate crate in crates)
        {
            Assert.True(CratePlacer.ByRoad(field, crate.Position), $"crate {crate.Id} at {crate.Position} is not by the road");
            Assert.False(CratePlacer.ByHouse(field, crate.Position));
        }
        AssertSpacedAndClear(crates, field, grid, land);
    }

    /// <summary>Thirty trees (0.5 m trunks, 3.5 m crowns) in a 6 × 5 grid 6 m apart, from (85, 88) to (115, 112).</summary>
    private static Prop[] Woods()
    {
        var trees = new Prop[30];
        for (int k = 0; k < trees.Length; k++) trees[k] = TreeTests.Tree(1 + k, new Vector2(85 + 6 * (k % 6), 88 + 6 * (k / 6)));
        return trees;
    }

    [Fact]
    public void By_forest_holds_at_the_woods_edge_but_not_under_the_crowns_by_a_trunk_or_in_the_open()
    {
        Battlefield field = TestWorld.FlatField(Woods());
        Assert.True(CratePlacer.ByForest(field, new Vector2(100, 82)));       // 6 m south of the first row: 6 trunks within 20 m, none within 3, no crown
        Assert.True(CratePlacer.ByForest(field, new Vector2(80, 100)));       // 5 m west of the first column
        Assert.False(CratePlacer.ByForest(field, new Vector2(97, 100)));      // under the crown of the trunk at (97, 100)
        Assert.False(CratePlacer.ByForest(field, new Vector2(99, 100)));      // 2 m from that trunk
        Assert.False(CratePlacer.ByForest(field, new Vector2(100, 60)));      // the open: 28 m from the nearest trunk
        Assert.False(CratePlacer.ByForest(field, new Vector2(100, 70)));      // only two trunks within 20 m: (97, 88) and (103, 88) at 18.2 m, the next at 20.1
    }

    [Fact]
    public void A_fallen_tree_no_longer_counts()
    {
        Battlefield field = TestWorld.FlatField(TreeTests.Tree(1, new Vector2(90, 100)), TreeTests.Tree(2, new Vector2(100, 100)), TreeTests.Tree(3, new Vector2(110, 100)));
        var p = new Vector2(100, 106);
        Assert.True(CratePlacer.ByForest(field, p));
        field.SetDurability(2, 0);
        Assert.False(CratePlacer.ByForest(field, p));
    }

    [Fact]
    public void Woods_on_flat_ground_get_forest_crates_off_the_trunks_and_crowns()
    {
        (Battlefield field, NavGrid grid, bool[] land) = World(TestWorld.FlatField(Woods()));
        Crate[] crates = CratePlacer.Place(field, grid, MiddleSpawn, 11, land, out int attempts);
        _output.WriteLine($"woods: {crates.Length} crates after {attempts} draws");
        Assert.InRange(crates.Length, 4, 12);
        foreach (Crate crate in crates)
        {
            Assert.True(CratePlacer.ByForest(field, crate.Position), $"crate {crate.Id} at {crate.Position} is not by the woods");
            foreach (Prop tree in field.Props)
            {
                float d = Vector2.Distance(tree.Footprint.Center, crate.Position);
                Assert.True(d >= CratePlacer.TrunkClear && d > tree.FoliageRadius, $"crate {crate.Id} at {crate.Position} is {d:F1} m from tree {tree.Id}");
            }
        }
        AssertSpacedAndClear(crates, field, grid, land);
    }

    [Fact]
    public void Placing_changes_neither_the_field_nor_the_grid()
    {
        Battlefield field = RiverlandFixture.Field;
        NavGrid grid = RiverlandFixture.Grid;
        int fieldVersion = field.StateVersion, gridVersion = grid.Version;
        CratePlacer.Place(field, grid, RiverlandMap.Spawns.Solo, 3);
        Assert.Equal(fieldVersion, field.StateVersion);
        Assert.Equal(gridVersion, grid.Version);
    }

    [Fact]
    public void Placing_without_a_field_grid_or_spawns_throws()
    {
        Assert.Throws<ArgumentNullException>(() => CratePlacer.Place(null!, RiverlandFixture.Grid, RiverlandMap.Spawns.Solo, 1));
        Assert.Throws<ArgumentNullException>(() => CratePlacer.Place(RiverlandFixture.Field, null!, RiverlandMap.Spawns.Solo, 1));
        Assert.Throws<ArgumentNullException>(() => CratePlacer.Place(RiverlandFixture.Field, RiverlandFixture.Grid, null!, 1));
        Assert.Throws<ArgumentException>(() => CratePlacer.Clear(RiverlandFixture.Field, RiverlandFixture.Grid, new bool[1], RiverlandMap.Spawns.Solo, new Vector2(600, 600)));
    }
}

/// <summary>What placing a match's crates costs on the riverland (once a match, on the server, with the land it reads computed too).</summary>
[Collection(TimingCollection.Name)]
public sealed class CratePlacerTimingTests
{
    private const int Repeats = 3;

    private readonly ITestOutputHelper _output;

    public CratePlacerTimingTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Placing_the_riverlands_crates_is_quick()
    {
        Battlefield field = RiverlandFixture.Field;
        NavGrid grid = RiverlandFixture.Grid;
        CratePlacer.Place(field, grid, RiverlandMap.Spawns.Solo, 1);   // warm-up
        double best = double.MaxValue;
        for (int r = 0; r < Repeats; r++)
        {
            long start = Stopwatch.GetTimestamp();
            CratePlacer.Place(field, grid, RiverlandMap.Spawns.Solo, (uint)(r + 2) ^ CratePlacer.SeedSalt);
            best = Math.Min(best, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
        _output.WriteLine($"CratePlacer.Place(riverland), land included: {best:F1} ms at best of {Repeats}");
#if DEBUG
        const double Limit = 3000.0;
#else
        const double Limit = 1000.0;
#endif
        Assert.True(best <= Limit, $"placing took {best:F1} ms at best (limit {Limit} ms)");
    }
}
