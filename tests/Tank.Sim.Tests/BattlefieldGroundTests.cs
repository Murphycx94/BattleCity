using System.Numerics;
using Tank.Sim.Maps;
using Tank.Sim.Terrain;

namespace Tank.Sim.Tests;

/// <summary>The ground and water a battlefield carries, and the count of its prop-state changes (riverland design §4.2).</summary>
public sealed class BattlefieldGroundTests
{
    private const int Cells = TestWorld.Samples - 1;

    private static Battlefield Field(HeightField terrain, SurfaceKind kind, float? waterLevel = null, params Prop[] props) =>
        new(terrain, props, TestWorld.Border, SurfaceMap.Uniform(Cells, Cells, TestWorld.Cell, kind), waterLevel);

    private static readonly Prop Wall =
        new WallLine(PropKind.BrickWall, new Vector2(98, 100), new Vector2(100, 100)).Segments(TestWorld.Flat(), 1).Single();

    private static readonly Prop Fence =
        new WallLine(PropKind.Fence, new Vector2(120, 100), new Vector2(122, 100)).Segments(TestWorld.Flat(), 2).Single();

    [Fact]
    public void The_three_argument_constructor_gives_grass_and_no_water()
    {
        Battlefield field = TestWorld.FlatField();
        Assert.Equal(Cells, field.Surface.CellsX);
        Assert.Equal(Cells, field.Surface.CellsZ);
        Assert.Equal(TestWorld.Cell, field.Surface.CellSize);
        Assert.True(field.Surface.IsUniform(SurfaceKind.Grass));
        Assert.Null(field.WaterLevel);
        foreach (Vector2 p in new[] { new Vector2(0, 0), new Vector2(57.3f, 141.9f), new Vector2(200, 200), new Vector2(-10, 300) })
        {
            Assert.Equal(SurfaceKind.Grass, field.SurfaceAt(p));
            Assert.Equal(0f, field.WaterDepthAt(p));
            Assert.Equal(1f, field.SpeedFactorAt(p));
        }
    }

    [Fact]
    public void A_mismatched_surface_or_a_bad_water_level_is_refused()
    {
        HeightField flat = TestWorld.Flat();
        Assert.Throws<ArgumentException>(() => new Battlefield(flat, [], TestWorld.Border, SurfaceMap.Uniform(Cells - 1, Cells, TestWorld.Cell)));
        Assert.Throws<ArgumentException>(() => new Battlefield(flat, [], TestWorld.Border, SurfaceMap.Uniform(Cells, Cells - 1, TestWorld.Cell)));
        Assert.Throws<ArgumentException>(() => new Battlefield(flat, [], TestWorld.Border, SurfaceMap.Uniform(Cells, Cells, 1f)));
        Assert.Throws<ArgumentException>(() => new Battlefield(flat, [], TestWorld.Border, null, float.NaN));
        Assert.Throws<ArgumentException>(() => new Battlefield(flat, [], TestWorld.Border, null, float.PositiveInfinity));
        Assert.Throws<ArgumentException>(() => new Battlefield(flat, [], TestWorld.Border, null, float.NegativeInfinity));
    }

    [Fact]
    public void Water_depth_is_the_level_above_the_ground()
    {
        var p = new Vector2(100, 100);
        Assert.Equal(1.3f, Field(TestWorld.Flat(-1.3f), SurfaceKind.Sand, 0f).WaterDepthAt(p), 1e-5f);
        Assert.Equal(0f, Field(TestWorld.Flat(0.5f), SurfaceKind.Sand, 0f).WaterDepthAt(p));
        Assert.Equal(0f, Field(TestWorld.Flat(-1.3f), SurfaceKind.Sand).WaterDepthAt(p));
        Assert.Null(Field(TestWorld.Flat(-1.3f), SurfaceKind.Sand).WaterLevel);
    }

    [Fact]
    public void Speed_factor_combines_ground_and_water()
    {
        var p = new Vector2(100, 100);
        Assert.Equal(1.15f, Field(TestWorld.Flat(), SurfaceKind.Road).SpeedFactorAt(p));
        Assert.Equal(0.6f, Field(TestWorld.Flat(-1.2f), SurfaceKind.Road, 0f).SpeedFactorAt(p), 1e-5f);
        Assert.Equal(0.8f, Field(TestWorld.Flat(), SurfaceKind.Sand).SpeedFactorAt(p));
        Assert.Equal(SurfaceKind.Road, Field(TestWorld.Flat(-1.2f), SurfaceKind.Road, 0f).SurfaceAt(p));
    }

    [Fact]
    public void Down_a_sloped_shore_the_water_deepens_smoothly_from_the_waterline()
    {
        // Ground falling 5 cm a metre towards +z, 10 m high at z = 0; the water at 5 m: the waterline is at z = 100.
        const float grade = 0.05f, step = 0.25f;
        foreach (SurfaceKind kind in Enum.GetValues<SurfaceKind>())
        {
            Battlefield field = Field(TestWorld.RampNorth(grade), kind, waterLevel: 5f);
            float before = 0f;
            for (float z = 80f; z <= 140f; z += step)
            {
                var p = new Vector2(57.3f, z);
                float depth = field.WaterDepthAt(p), factor = field.SpeedFactorAt(p);
                Assert.InRange(depth - before, 0f, grade * step + 1e-4f);   // rises, and without a jump
                before = depth;
                if (z < 99.9f) Assert.Equal((0f, GroundSpeed.Of(kind)), (depth, factor));
                if (z > 100.1f) Assert.True(depth > 0f, $"dry at z = {z}");
                if (z > 100.1f && z <= 103f) Assert.Equal(GroundSpeed.WaterMax, factor);   // just past the waterline: 0.8, whatever the ground
                if (depth > 0f) Assert.Equal(GroundSpeed.InWater(depth), factor);
                Assert.Equal(kind, field.SurfaceAt(p));
            }
            Assert.Equal(2f, before, 1e-3f);
            Assert.Equal(GroundSpeed.WaterMin, field.SpeedFactorAt(new Vector2(57.3f, 140f)));
        }
    }

    [Fact]
    public void Non_finite_positions_take_an_edge_cell_and_a_finite_factor()
    {
        // The corner cell (0, 0) dirt, the far corner road, the rest of the last row and column sand, grass inside.
        const int last = Cells - 1;
        static SurfaceKind KindOf(int i, int j) =>
            (i, j) == (0, 0) ? SurfaceKind.Dirt : (i, j) == (last, last) ? SurfaceKind.Road : i == last || j == last ? SurfaceKind.Sand : SurfaceKind.Grass;
        var cells = new SurfaceKind[Cells * Cells];
        for (int j = 0; j < Cells; j++)
        for (int i = 0; i < Cells; i++)
            cells[j * Cells + i] = KindOf(i, j);
        var surface = new SurfaceMap(Cells, Cells, TestWorld.Cell, cells);
        Battlefield dry = new(TestWorld.Flat(), [], TestWorld.Border, surface);
        Battlefield wet = new(TestWorld.Flat(-0.5f), [], TestWorld.Border, surface, waterLevel: 0f);

        const float nan = float.NaN, inf = float.PositiveInfinity, max = float.MaxValue;
        var cases = new (float X, float Z, int I, int J)[]
        {
            (nan, nan, 0, 0), (-inf, -inf, 0, 0), (inf, inf, last, last), (inf, -inf, last, 0), (-inf, inf, 0, last),
            (nan, inf, 0, last), (inf, nan, last, 0), (nan, 100f, 0, 50), (100f, nan, 50, 0), (max, -max, last, 0),
        };
        foreach (var (x, z, i, j) in cases)
        {
            var p = new Vector2(x, z);
            Assert.Equal(KindOf(i, j), dry.SurfaceAt(p));
            Assert.Equal(KindOf(i, j), wet.SurfaceAt(p));
            Assert.Equal(GroundSpeed.Of(KindOf(i, j)), dry.SpeedFactorAt(p));
            float factor = wet.SpeedFactorAt(p);
            Assert.True(float.IsFinite(factor) && factor >= GroundSpeed.WaterMin && factor <= GroundSpeed.Road, $"factor {factor} at {p}");
        }
    }

    [Fact]
    public void State_version_counts_real_changes_only()
    {
        Battlefield field = TestWorld.FlatField(Wall, Fence);
        int wall = field.IndexOf(Wall.Id), fence = field.IndexOf(Fence.Id);
        Assert.Equal(0, field.StateVersion);

        field.Damage(wall, 100);
        Assert.Equal(1, field.StateVersion);
        field.Damage(wall, 0);
        Assert.Equal(1, field.StateVersion);

        field.Destroy(fence);
        Assert.Equal(2, field.StateVersion);
        field.Destroy(fence);
        Assert.Equal(2, field.StateVersion);

        field.Restore(wall);
        Assert.Equal(3, field.StateVersion);
        field.Restore(wall);
        Assert.Equal(3, field.StateVersion);

        field.SetDurability(Wall.Id, Wall.MaxDurability);
        Assert.Equal(3, field.StateVersion);
        field.SetDurability(Wall.Id, 0);
        Assert.Equal(4, field.StateVersion);

        Assert.False(field.IsStanding(Wall.Id));
        Assert.False(field.Raycast(new Vector3(99, 1, 110), new Vector3(99, 1, 90), out _));
        field.BushesNear(new Vector2(99, 100), 50f);
        Assert.Equal(4, field.StateVersion);
    }

    [Fact]
    public void Fresh_gives_the_same_ground_and_props_all_standing()
    {
        var surface = new SurfaceKind[Cells * Cells];
        for (int k = 0; k < surface.Length; k++) surface[k] = (SurfaceKind)(k % 4);
        var field = new Battlefield(TestWorld.Flat(-0.4f), [Wall, Fence], TestWorld.Border,
            new SurfaceMap(Cells, Cells, TestWorld.Cell, surface), waterLevel: 0f);
        field.Destroy(field.IndexOf(Wall.Id));
        field.Damage(field.IndexOf(Fence.Id), 1);
        Assert.Equal(2, field.StateVersion);

        Battlefield fresh = field.Fresh();
        Assert.Same(field.Terrain, fresh.Terrain);
        Assert.Same(field.Surface, fresh.Surface);
        Assert.Equal(field.Props, fresh.Props);
        Assert.Equal(field.WaterLevel, fresh.WaterLevel);
        Assert.Equal(field.Border, fresh.Border);
        Assert.Equal(0, fresh.StateVersion);
        Assert.All(fresh.Props, p => Assert.True(fresh.IsStanding(p.Id)));
        Assert.Equal(Wall.MaxDurability, fresh.Durability(Wall.Id));
        Assert.Equal(Fence.MaxDurability, fresh.Durability(Fence.Id));
        Assert.True(fresh.Raycast(new Vector3(99, 1, 110), new Vector3(99, 1, 90), out RayHit hit) && hit.PropId == Wall.Id);
        Assert.False(field.IsStanding(Wall.Id), "the old field keeps its state");
    }
}
