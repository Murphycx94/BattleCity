using System.Numerics;
using Tank.Client.Art;
using Tank.Client.Art.Meshes;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;

namespace Tank.Art.Tests;

public class HouseShapesTests
{
    private const float Tolerance = 0.01f;

    /// <summary>
    /// The four sizes of riverland design §3.6 (length along the ridge, width across it, height to the ridge), each with
    /// the three seeds the game gives that style (<see cref="HouseShapes.SeedFor"/>).
    /// </summary>
    public static TheoryData<HouseStyle, float, float, float, uint> EveryStyleAndSeed()
    {
        var data = new TheoryData<HouseStyle, float, float, float, uint>();
        foreach (var (style, length, width, height) in Sizes)
            foreach (uint seed in SeedsOf(style)) data.Add(style, length, width, height, seed);
        return data;
    }

    /// <summary>The seeds the game gives a house of <paramref name="style"/>: style × 3 + 1 … 3 (1 … 12 over the four).</summary>
    private static IEnumerable<uint> SeedsOf(HouseStyle style) => Enumerable.Range(1, HouseShapes.Variants).Select(v => (uint)style * HouseShapes.Variants + (uint)v);

    public static TheoryData<HouseStyle, float, float, float> EveryStyle()
    {
        var data = new TheoryData<HouseStyle, float, float, float>();
        foreach (var (style, length, width, height) in Sizes) data.Add(style, length, width, height);
        return data;
    }

    private static readonly (HouseStyle Style, float Length, float Width, float Height)[] Sizes =
    {
        (HouseStyle.Cottage, 8f, 6f, 6.5f),
        (HouseStyle.Farmhouse, 12f, 7f, 7.5f),
        (HouseStyle.TwoStorey, 9f, 7f, 9f),
        (HouseStyle.Barn, 14f, 9f, 8f),
    };

    [Fact]
    public void Sizes_map_to_the_four_styles()
    {
        Assert.Equal(HouseStyle.Cottage, HouseShapes.StyleFor(8f, 6f, 6.5f));
        Assert.Equal(HouseStyle.Farmhouse, HouseShapes.StyleFor(12f, 7f, 7.5f));
        Assert.Equal(HouseStyle.TwoStorey, HouseShapes.StyleFor(9f, 7f, 9f));
        Assert.Equal(HouseStyle.Barn, HouseShapes.StyleFor(14f, 9f, 8f));
        Assert.Equal(HouseStyle.Farmhouse, HouseShapes.StyleFor(11.5f, 7f, 7.4f));
    }

    [Theory]
    [MemberData(nameof(EveryStyleAndSeed))]
    public void A_house_fills_its_box_exactly(HouseStyle style, float length, float width, float height, uint seed)
    {
        var (min, max) = MeshAssert.Bounds(HouseShapes.House(style, length, width, height, seed));
        Assert.Equal(new Vector3(-length / 2, 0f, -width / 2), min, new Near(Tolerance));
        Assert.Equal(new Vector3(length / 2, height, width / 2), max, new Near(Tolerance));
    }

    [Theory]
    [MemberData(nameof(EveryStyle))]
    public void The_ridge_runs_along_x(HouseStyle style, float length, float width, float height)
    {
        foreach (uint seed in SeedsOf(style))
        {
            MeshData house = HouseShapes.House(style, length, width, height, seed);
            int ridge = 0;
            foreach (Vector3 p in house.Positions)
            {
                if (p.Y <= height - Tolerance) continue;
                Assert.True(MathF.Abs(p.Z) < 0.3f, $"{style} seed {seed}: a top vertex at {p} is off the ridge");
                ridge++;
            }
            Assert.True(ridge >= 4, $"{style} seed {seed}: the ridge should run the house's length");
        }
    }

    [Theory]
    [MemberData(nameof(EveryStyle))]
    public void The_door_is_on_the_front(HouseStyle style, float length, float width, float height)
    {
        Vector3 door = Palette.Linear(Palette.Door);
        foreach (uint seed in SeedsOf(style))
        {
            MeshData house = HouseShapes.House(style, length, width, height, seed);
            int doorVertices = 0;
            for (int i = 0; i < house.VertexCount; i++)
            {
                Vector4 c = house.Colors[i];
                if (Vector3.Distance(new Vector3(c.X, c.Y, c.Z), door) > 1e-3f) continue;
                Assert.InRange(house.Positions[i].Z, -width / 2 - 0.08f, -width / 2 + 0.08f);
                doorVertices++;
            }
            Assert.True(doorVertices > 0, $"{style} seed {seed} has no door");
        }
    }

    [Theory]
    [MemberData(nameof(EveryStyleAndSeed))]
    public void Normals_are_clean_and_the_mesh_is_light(HouseStyle style, float length, float width, float height, uint seed)
    {
        MeshData house = HouseShapes.House(style, length, width, height, seed);
        MeshAssert.UnitNormals(house);
        foreach (Vector3 n in house.Normals) Assert.True(float.IsFinite(n.X) && float.IsFinite(n.Y) && float.IsFinite(n.Z));
        MeshAssert.WindingMatchesNormals(house);
        Assert.InRange(house.TriangleCount, 1, 2999);
        MeshData ruin = HouseShapes.Ruin(length, width, seed);
        MeshAssert.UnitNormals(ruin);
        MeshAssert.WindingMatchesNormals(ruin);
        Assert.InRange(ruin.TriangleCount, 1, 2999);
    }

    [Theory]
    [InlineData(1u)]
    [InlineData(2u)]
    [InlineData(3u)]
    [InlineData(7u)]
    public void A_ruin_covers_the_widened_footprint_low(uint seed)
    {
        var (min, max) = MeshAssert.Bounds(HouseShapes.Ruin(12f, 7f, seed));
        Assert.True(min.X >= -6.5f - Tolerance && max.X <= 6.5f + Tolerance, $"x from {min.X} to {max.X}");
        Assert.True(min.Z >= -4f - Tolerance && max.Z <= 4f + Tolerance, $"z from {min.Z} to {max.Z}");
        Assert.True(max.X - min.X > 9f && max.Z - min.Z > 5f, "the heap spreads over most of the plot");
        Assert.InRange(max.Y, 1f, 2.2f);
        Assert.True(min.Y > -0.5f, "nothing sinks deep under the ground");
    }

    [Theory]
    [MemberData(nameof(EveryStyle))]
    public void Seeds_pick_different_colour_sets(HouseStyle style, float length, float width, float height)
    {
        var sets = new List<HashSet<Vector4>>();
        foreach (uint seed in SeedsOf(style))
        {
            var colours = new HashSet<Vector4>(HouseShapes.House(style, length, width, height, seed).Colors);
            if (!sets.Any(set => set.SetEquals(colours))) sets.Add(colours);
        }
        Assert.True(sets.Count >= 2, $"{style}: its three seeds all have the same colours");
    }

    [Fact]
    public void The_games_seeds_use_all_four_colour_sets_and_draw_in_their_colours()
    {
        var sets = new HashSet<(Vector3, Vector3)>();
        foreach (var (style, length, width, height) in Sizes)
            foreach (uint seed in SeedsOf(style))
            {
                (Vector3 wall, Vector3 roof) = HouseShapes.ColoursOf(seed);
                sets.Add((wall, roof));
                var colours = new HashSet<Vector4>(HouseShapes.House(style, length, width, height, seed).Colors);
                Assert.Contains(new Vector4(Palette.Linear(wall), 1f), colours);
                Assert.Contains(new Vector4(Palette.Linear(roof), 1f), colours);
            }
        Assert.Equal(4, sets.Count);
        Assert.Contains((Palette.Plaster, Palette.RoofTile), sets);
        Assert.Contains((Palette.Planks, Palette.RoofSlate), sets);
        Assert.Contains((Palette.Plaster, Palette.RoofSlate), sets);
        Assert.Contains((Palette.Planks, Palette.RoofTile), sets);
    }

    [Theory]
    [MemberData(nameof(EveryStyle))]
    public void A_house_prop_gets_one_of_its_styles_three_seeds(HouseStyle style, float length, float width, float height)
    {
        var seen = new HashSet<uint>();
        for (int id = 1; id <= 40; id++)
        {
            var prop = new Prop(id, PropKind.House, new Obb2(new Vector2(300f, 200f), new Vector2(length / 2, width / 2), 0.4f), 2f, height + WallLine.Sink);
            uint seed = HouseShapes.SeedFor(prop);
            Assert.Equal(seed, HouseShapes.SeedFor(prop));
            Assert.Contains(seed, SeedsOf(style));
            (float l, float w, float h) = HouseShapes.SizeOf(prop);
            Assert.Equal(length, l, 1e-4f);
            Assert.Equal(width, w, 1e-4f);
            Assert.Equal(height, h, 1e-4f);
            seen.Add(seed);
        }
        Assert.Equal(3, seen.Count);
    }

    [Theory]
    [InlineData(1u)]
    [InlineData(6u)]
    [InlineData(12u)]
    public void A_ruin_is_the_same_for_a_seed(uint seed)
    {
        MeshData first = HouseShapes.Ruin(12f, 7f, seed), second = HouseShapes.Ruin(12f, 7f, seed);
        Assert.Equal(first.Positions, second.Positions);
        Assert.Equal(first.Colors, second.Colors);
        Assert.Equal(first.Indices, second.Indices);
        Assert.NotEqual(first.Positions, HouseShapes.Ruin(12f, 7f, seed + 1).Positions);
    }

    [Fact]
    public void Ruins_heap_half_a_metre_the_way_the_house_was_pushed()
    {
        Assert.Equal(Vector2.Zero, HouseShapes.RuinOffset(null));
        Assert.Equal(Vector2.Zero, HouseShapes.RuinOffset(Vector2.Zero));
        Vector2 shift = HouseShapes.RuinOffset(new Vector2(3f, -4f));
        Assert.Equal(0.3f, shift.X, 1e-5f);
        Assert.Equal(-0.4f, shift.Y, 1e-5f);
    }

    /// <summary>Every component within the tolerance.</summary>
    private sealed class Near(float tolerance) : IEqualityComparer<Vector3>
    {
        public bool Equals(Vector3 a, Vector3 b) => MathF.Abs(a.X - b.X) <= tolerance && MathF.Abs(a.Y - b.Y) <= tolerance && MathF.Abs(a.Z - b.Z) <= tolerance;
        public int GetHashCode(Vector3 v) => 0;
    }
}
