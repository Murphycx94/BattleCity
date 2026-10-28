using System.Diagnostics;
using Tank.Client.Art;
using Tank.Client.Art.Meshes;
using Tank.Client.Minimap;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Terrain;
using Xunit.Abstractions;
using SN = System.Numerics;

namespace Tank.Art.Tests;

public class MinimapGroundTests
{
    private const float Cell = 2f;
    private const int FieldCells = 100;          // the made-up fields: 200 m square
    private const float FieldSize = FieldCells * Cell, Middle = FieldSize / 2;
    private const float Border = 10f;
    private const int SmallPixels = 128;         // ~1.4 m a pixel over the 180 m playable square

    private readonly ITestOutputHelper _output;

    /// <summary>The riverland, its bare-ground mask and its baked ground (with the bake's wall-clock time), shared by the class.</summary>
    private static readonly Lazy<(Battlefield Field, Func<float, float, float> NotGrassAt, MinimapGround Ground, TimeSpan BakeTime)> Riverland = new(() =>
    {
        Battlefield field = MapCatalog.Riverland.NewBattlefield();
        Func<float, float, float> notGrassAt = new GroundLayers(field.Surface).NotGrassAt;
        var clock = Stopwatch.StartNew();
        MinimapGround ground = MinimapGround.Bake(field, notGrassAt);
        return (field, notGrassAt, ground, clock.Elapsed);
    });

    /// <summary>The riverland without its trees (terrain, surface, water and every other prop the same), baked: what the forest is drawn over.</summary>
    private static readonly Lazy<MinimapGround> TreelessRiverland = new(() =>
    {
        Battlefield field = Riverland.Value.Field;
        var treeless = new Battlefield(field.Terrain, field.Props.Where(p => p.Kind != PropKind.Tree), field.Border, field.Surface, field.WaterLevel);
        return MinimapGround.Bake(treeless, Riverland.Value.NotGrassAt);
    });

    public MinimapGroundTests(ITestOutputHelper output) => _output = output;

    private static HeightField Terrain(Func<float, float, float> height)
    {
        const int samples = FieldCells + 1;
        var heights = new float[samples * samples];
        for (int j = 0; j < samples; j++)
        for (int i = 0; i < samples; i++)
            heights[j * samples + i] = height(i * Cell, j * Cell);
        return new HeightField(samples, samples, Cell, heights);
    }

    private static Battlefield Field(Func<float, float, float> height, SurfaceMap? surface = null, float? water = null, IEnumerable<Prop>? props = null) =>
        new(Terrain(height), props ?? [], Border, surface, water);

    private static Battlefield Flat(SurfaceMap? surface = null, IEnumerable<Prop>? props = null) => Field((_, _) => 0f, surface, props: props);

    /// <summary>The pixel's colour as 0–255 per channel (and its alpha).</summary>
    private static (int R, int G, int B, int A) PixelAt(MinimapGround ground, int px, int py)
    {
        int o = (py * ground.Pixels + px) * 4;
        ReadOnlySpan<byte> rgba = ground.Rgba;
        return (rgba[o], rgba[o + 1], rgba[o + 2], rgba[o + 3]);
    }

    /// <summary>The pixel that covers <paramref name="at"/> (x, z).</summary>
    private static (int R, int G, int B, int A) PixelAt(MinimapGround ground, SN.Vector2 at)
    {
        SN.Vector2 unit = ground.Area.ToUnit(at);
        return PixelAt(ground, (int)(unit.X * ground.Pixels), (int)(unit.Y * ground.Pixels));
    }

    /// <summary>The ground (x, z) at the centre of pixel (<paramref name="px"/>, <paramref name="py"/>).</summary>
    private static SN.Vector2 CentreOf(MinimapGround ground, int px, int py) =>
        ground.Area.ToGround(new SN.Vector2((px + 0.5f) / ground.Pixels, (py + 0.5f) / ground.Pixels));

    /// <summary>Flat ground's light: the normal straight up, no height shading (the field has one height).</summary>
    private static float FlatShade => MinimapStyle.ShadeAmbient + MinimapStyle.ShadeDiffuse * MinimapStyle.LightDirection.Y;

    private static SN.Vector3 Bytes(SN.Vector3 colour) => colour * 255f;

    /// <summary>
    /// How far a pixel of flat ground whose dirt mask is 1 may be from the bare colour: the patch noise still lets up to
    /// <see cref="GroundCover.FlatGrassWeightRange"/>'s grass through, as on the 3D ground; plus a byte for rounding.
    /// </summary>
    private static SN.Vector3 BareTolerance(SN.Vector3 bare)
    {
        float most = GroundCover.FlatGrassWeightRange(1f).Most;
        return SN.Vector3.Abs(MinimapStyle.Grass - bare) * most * FlatShade * 255f + new SN.Vector3(1f);
    }

    private static void AssertNear(SN.Vector3 expected, (int R, int G, int B, int A) pixel, SN.Vector3 tolerance, string what)
    {
        Assert.True(MathF.Abs(pixel.R - expected.X) <= tolerance.X && MathF.Abs(pixel.G - expected.Y) <= tolerance.Y && MathF.Abs(pixel.B - expected.Z) <= tolerance.Z,
            $"{what}: expected {expected} ± {tolerance}, got {pixel}");
    }

    [Fact]
    public void Flat_grass_is_one_shade_of_grass_everywhere()
    {
        MinimapGround ground = MinimapGround.Bake(Flat(), (_, _) => 0f, SmallPixels);
        Assert.Equal(SmallPixels, ground.Pixels);
        var first = PixelAt(ground, 0, 0);
        for (int py = 0; py < SmallPixels; py++)
        for (int px = 0; px < SmallPixels; px++)
            Assert.Equal(first, PixelAt(ground, px, py));
        Assert.True(first.G > first.R && first.G > first.B, $"{first} isn't green");
        AssertNear(Bytes(MinimapStyle.Grass) * FlatShade, first, new SN.Vector3(1f), "flat grass");
    }

    [Fact]
    public void Flat_bare_ground_is_the_dirt_colour()
    {
        MinimapGround ground = MinimapGround.Bake(Flat(), (_, _) => 1f, SmallPixels);
        SN.Vector3 expected = Bytes(Palette.Dirt) * FlatShade, tolerance = BareTolerance(Palette.Dirt);
        int exact = 0;
        for (int py = 0; py < SmallPixels; py++)
        for (int px = 0; px < SmallPixels; px++)
        {
            var pixel = PixelAt(ground, px, py);
            AssertNear(expected, pixel, tolerance, $"pixel ({px}, {py})");
            if (MathF.Abs(pixel.R - expected.X) <= 1 && MathF.Abs(pixel.G - expected.Y) <= 1 && MathF.Abs(pixel.B - expected.Z) <= 1) exact++;
        }
        Assert.True(exact > SmallPixels * SmallPixels / 2, $"only {exact} pixels are bare dirt");
    }

    [Fact]
    public void A_road_half_shows_the_road_colour()
    {
        var cells = new SurfaceKind[FieldCells * FieldCells];
        for (int j = 0; j < FieldCells; j++)
        for (int i = 0; i < FieldCells; i++)
            cells[j * FieldCells + i] = i < FieldCells / 2 ? SurfaceKind.Road : SurfaceKind.Grass;
        var surface = new SurfaceMap(FieldCells, FieldCells, Cell, cells);
        MinimapGround ground = MinimapGround.Bake(Flat(surface), (x, _) => x < Middle ? 1f : 0f, SmallPixels);
        SN.Vector3 road = Bytes(Palette.Road) * FlatShade, tolerance = BareTolerance(Palette.Road);
        for (int py = 0; py < SmallPixels; py++)
        for (int px = 0; px < SmallPixels; px++)
        {
            SN.Vector2 at = CentreOf(ground, px, py);
            if (at.X < Middle - Cell) AssertNear(road, PixelAt(ground, px, py), tolerance, $"road at {at}");
            else if (at.X > Middle + Cell) AssertNear(Bytes(MinimapStyle.Grass) * FlatShade, PixelAt(ground, px, py), new SN.Vector3(1f), $"grass at {at}");
        }
    }

    [Fact]
    public void Image_row_0_is_the_smallest_z()
    {
        // Road where z < Middle (the north half, up on the "map" view), grass elsewhere: read straight off the rows.
        var cells = new SurfaceKind[FieldCells * FieldCells];
        for (int j = 0; j < FieldCells; j++)
        for (int i = 0; i < FieldCells; i++)
            cells[j * FieldCells + i] = j < FieldCells / 2 ? SurfaceKind.Road : SurfaceKind.Grass;
        var surface = new SurfaceMap(FieldCells, FieldCells, Cell, cells);
        MinimapGround ground = MinimapGround.Bake(Flat(surface), (_, z) => z < Middle ? 1f : 0f, SmallPixels);
        SN.Vector3 road = Bytes(Palette.Road) * FlatShade, grass = Bytes(MinimapStyle.Grass) * FlatShade;
        for (int px = 0; px < SmallPixels; px++)
        {
            AssertNear(road, PixelAt(ground, px, 2), BareTolerance(Palette.Road), $"row 2, column {px}");
            AssertNear(grass, PixelAt(ground, px, SmallPixels - 3), new SN.Vector3(1f), $"row {SmallPixels - 3}, column {px}");
        }
    }

    [Fact]
    public void The_shore_line_is_shallow_water_half_foam()
    {
        MinimapGround ground = MinimapGround.Bake(Field((_, _) => -0.1f, water: 0f), (_, _) => 0f, SmallPixels);
        SN.Vector3 water = SN.Vector3.Lerp(Palette.WaterShallow, Palette.WaterDeep, GroundCover.SmoothStep(MinimapStyle.ShallowDepth, MinimapStyle.DeepDepth, 0.1f));
        SN.Vector3 shore = SN.Vector3.Lerp(water, Palette.Foam, MinimapStyle.ShoreFoam);
        (int R, int G, int B, int A) expected = (MinimapStyle.ToByte(shore.X), MinimapStyle.ToByte(shore.Y), MinimapStyle.ToByte(shore.Z), 255);
        Assert.Equal(expected, PixelAt(ground, SmallPixels / 2, SmallPixels / 2));
    }

    [Fact]
    public void A_steep_slope_is_bare_rock_under_the_hillshade()
    {
        // A plane rising 50° towards +x: past the rock fade's top even with the slope noise, so neither grass nor dirt shows.
        float rise = MathF.Tan(50f * MathF.PI / 180f);
        MinimapGround ground = MinimapGround.Bake(Field((x, _) => rise * x), (_, _) => 0f, SmallPixels);
        int px = SmallPixels / 2, py = SmallPixels / 2;
        SN.Vector3 normal = SN.Vector3.Normalize(new SN.Vector3(-rise, 1f, 0f));
        float shade = MinimapStyle.ShadeAmbient + MinimapStyle.ShadeDiffuse * MathF.Max(0f, SN.Vector3.Dot(normal, MinimapStyle.LightDirection));
        // The height shading spans the pixel centres' heights: the first column's to the last's.
        float low = rise * CentreOf(ground, 0, py).X, high = rise * CentreOf(ground, SmallPixels - 1, py).X;
        float up = (rise * CentreOf(ground, px, py).X - low) / (high - low);
        shade *= 1f + MinimapStyle.HeightShade * (2f * up - 1f);
        AssertNear(Bytes(Palette.Rock) * shade, PixelAt(ground, px, py), new SN.Vector3(1.5f), "a 50° slope");
    }

    [Fact]
    public void Water_takes_its_colour_from_the_depth()
    {
        // Ground 1 m up, with a round hollow 2 m under the water (level 0) in the middle, its sides rising 20 … 30 m out.
        const float depth = 2f;
        float Height(float x, float z)
        {
            float r = SN.Vector2.Distance(new SN.Vector2(x, z), new SN.Vector2(Middle, Middle));
            return r <= 20f ? -depth : r >= 30f ? 1f : -depth + (r - 20f) / 10f * (1f + depth);
        }
        MinimapGround ground = MinimapGround.Bake(Field(Height, water: 0f), (_, _) => 0f, SmallPixels);
        var pixel = PixelAt(ground, new SN.Vector2(Middle, Middle));
        Assert.True(pixel.B > pixel.R && pixel.B > pixel.G, $"{pixel} isn't water");
        SN.Vector3 water = SN.Vector3.Lerp(Palette.WaterShallow, Palette.WaterDeep, GroundCover.SmoothStep(MinimapStyle.ShallowDepth, MinimapStyle.DeepDepth, depth));
        AssertNear(Bytes(water), pixel, new SN.Vector3(3f), "2 m of water");
        var dry = PixelAt(ground, new SN.Vector2(Middle + 50, Middle));
        Assert.True(dry.G > dry.B, $"{dry} is dry grass");
    }

    [Fact]
    public void A_slope_facing_north_west_is_lit_brighter_than_one_facing_south_east()
    {
        // A ridge from north-east to south-west through the middle, 20° on each side: grass on both, only the light differs.
        float rise = MathF.Tan(20f * MathF.PI / 180f);
        float Height(float x, float z) => 30f - rise * MathF.Abs((x - Middle + (z - Middle)) / MathF.Sqrt(2f));
        MinimapGround ground = MinimapGround.Bake(Field(Height), (_, _) => 0f, SmallPixels);
        float off = 20f / MathF.Sqrt(2f);
        var northWest = PixelAt(ground, new SN.Vector2(Middle - off, Middle - off));
        var southEast = PixelAt(ground, new SN.Vector2(Middle + off, Middle + off));
        int lit = northWest.R + northWest.G + northWest.B, shaded = southEast.R + southEast.G + southEast.B;
        Assert.True(lit - shaded > 30, $"north-west {northWest} vs south-east {southEast}");
    }

    [Fact]
    public void A_wood_darkens_the_ground_under_its_crowns_and_nowhere_else()
    {
        const float trunk = 0.4f, spacing = 5f;
        var trees = new List<Prop>();
        for (int a = 0; a < 6; a++)
        for (int b = 0; b < 5; b++)
        {
            var at = new SN.Vector2(Middle + (a - 2.5f) * spacing, Middle + (b - 2f) * spacing);
            trees.Add(new Prop(trees.Count + 1, PropKind.Tree, new Obb2(at, new SN.Vector2(trunk), 0f), 0f, 10f));
        }
        Assert.Equal(30, trees.Count);
        MinimapGround bare = MinimapGround.Bake(Flat(), (_, _) => 0f, SmallPixels);
        MinimapGround wooded = MinimapGround.Bake(Flat(props: trees), (_, _) => 0f, SmallPixels);

        var open = PixelAt(bare, new SN.Vector2(Middle, Middle));
        var wood = PixelAt(wooded, new SN.Vector2(Middle, Middle));   // between two trunks of the middle row
        Assert.True(wood.G < open.G, $"wood {wood} vs open {open}");
        Assert.True(wood.R + wood.G + wood.B < open.R + open.G + open.B, $"wood {wood} vs open {open}");

        float crown = trees[0].FoliageRadius;
        int far = 0;
        for (int py = 0; py < SmallPixels; py++)
        for (int px = 0; px < SmallPixels; px++)
        {
            SN.Vector2 at = CentreOf(wooded, px, py);
            if (trees.Min(t => SN.Vector2.Distance(at, t.Footprint.Center)) - crown <= 10f) continue;
            Assert.Equal(PixelAt(bare, px, py), PixelAt(wooded, px, py));
            far++;
        }
        Assert.True(far > SmallPixels * SmallPixels / 2);
    }

    [Fact]
    public void A_lone_tree_stays_a_dot_the_size_of_its_crown()
    {
        var at = new SN.Vector2(Middle, Middle);
        var tree = new Prop(1, PropKind.Tree, new Obb2(at, new SN.Vector2(0.5f), 0f), 0f, 10f);
        MinimapGround bare = MinimapGround.Bake(Flat(), (_, _) => 0f, SmallPixels);
        MinimapGround lone = MinimapGround.Bake(Flat(props: [tree]), (_, _) => 0f, SmallPixels);
        float step = lone.Area.MetresPerPixel(SmallPixels);
        int changed = 0;
        for (int py = 0; py < SmallPixels; py++)
        for (int px = 0; px < SmallPixels; px++)
        {
            if (PixelAt(bare, px, py) == PixelAt(lone, px, py)) continue;
            changed++;
            float distance = SN.Vector2.Distance(CentreOf(lone, px, py), at);
            Assert.True(distance <= tree.FoliageRadius + step, $"pixel ({px}, {py}) {distance:F2} m from the trunk changed: the closing grew a lone crown");
        }
        Assert.True(changed > 0);
    }

    [Fact]
    public void The_same_field_bakes_to_the_same_bytes()
    {
        float Height(float x, float z) => 4f * MathF.Sin(x * 0.05f) * MathF.Cos(z * 0.04f);
        Func<float, float, float> notGrass = (x, z) => x > z ? 0.8f : 0.1f;
        MinimapGround first = MinimapGround.Bake(Field(Height, water: -2f), notGrass, SmallPixels);
        MinimapGround second = MinimapGround.Bake(Field(Height, water: -2f), notGrass, SmallPixels);
        Assert.True(first.Rgba.SequenceEqual(second.Rgba));
    }

    [Fact]
    public void The_riverland_bakes_a_full_size_map_with_its_river()
    {
        (Battlefield field, _, MinimapGround ground, TimeSpan bakeTime) = Riverland.Value;
        _output.WriteLine($"riverland {ground.Pixels}² baked in {bakeTime.TotalMilliseconds:F0} ms");

        Assert.Equal(MinimapGround.DefaultPixels, ground.Pixels);
        Assert.Equal(1024 * 1024 * 4, ground.Rgba.Length);
        ReadOnlySpan<byte> rgba = ground.Rgba;
        for (int o = 3; o < rgba.Length; o += 4)
            Assert.Equal(255, rgba[o]);

        SN.Vector2 ford = RiverlandMap.Landmarks.Ford;   // 0.4 … 0.6 m under the water
        Assert.True(field.WaterDepthAt(ford) > MinimapStyle.ShoreBand);
        var water = PixelAt(ground, ford);
        Assert.True(water.B > water.R + 40 && water.B >= water.G, $"the ford at {ford} is {water}");
        foreach (var b in RiverlandMap.Bases)
        {
            var dry = PixelAt(ground, b.Centre);
            Assert.False(dry.B > dry.R + 40 && dry.B >= dry.G, $"team {b.Team}'s base at {b.Centre} is {dry}");
        }
        // Only a guard against a pathological slowdown (Debug, a loaded machine): the target is 400 ms in Release.
        Assert.True(bakeTime < TimeSpan.FromSeconds(20), $"{bakeTime.TotalMilliseconds:F0} ms");
    }

    /// <summary>How far in from its rectangle a forest's interior starts: past the outline's jitter, and 20 m more where the trees thin out.</summary>
    private const float ForestInset = RiverlandMap.ForestJitter + 20f;

    [Fact]
    public void The_riverland_forests_read_as_forest_inside()
    {
        MinimapGround ground = Riverland.Value.Ground;
        // Open grass as the map draws it on flat ground; a forest is green and darker than that.
        SN.Vector3 grass = Bytes(MinimapStyle.Grass) * FlatShade;
        float openGrass = grass.X + grass.Y + grass.Z;
        foreach (RiverlandForest forest in RiverlandMap.Forests)
        {
            SN.Vector2 min = forest.Min + new SN.Vector2(ForestInset), max = forest.Max - new SN.Vector2(ForestInset);
            int inside = 0, wooded = 0;
            long r = 0, g = 0, b = 0;
            for (int py = 0; py < ground.Pixels; py++)
            for (int px = 0; px < ground.Pixels; px++)
            {
                SN.Vector2 at = CentreOf(ground, px, py);
                if (at.X <= min.X || at.X >= max.X || at.Y <= min.Y || at.Y >= max.Y) continue;
                var pixel = PixelAt(ground, px, py);
                inside++;
                r += pixel.R; g += pixel.G; b += pixel.B;
                if (pixel.G > pixel.R + 20 && pixel.R + pixel.G + pixel.B < openGrass) wooded++;
            }
            Assert.True(inside > 0, $"the {forest.Name} forest has no interior");
            float share = (float)wooded / inside;
            _output.WriteLine($"{forest.Name}: {inside} px inside, {share:P1} forest-green, mean ({r / inside}, {g / inside}, {b / inside})");
            Assert.True(share >= 0.9f, $"only {share:P1} of the {forest.Name} forest's interior reads as forest");
        }
    }

    [Fact]
    public void The_canopy_closes_only_near_trees()
    {
        // The closing reaches at most k pixels past a crown; a pixel farther than the widest crown, those k pixels and one
        // pixel more from every trunk is the ground the trees stand on, byte for byte.
        (Battlefield field, _, MinimapGround wooded, _) = Riverland.Value;
        MinimapGround bare = TreelessRiverland.Value;
        int pixels = wooded.Pixels;
        float step = wooded.Area.MetresPerPixel(pixels);
        int k = (int)MathF.Ceiling(MinimapStyle.CanopyGapMetres / step);
        Prop[] trees = field.Props.Where(p => p.Kind == PropKind.Tree).ToArray();
        float reach = trees.Max(t => t.FoliageRadius) + (k + 1) * step;

        var near = new bool[pixels * pixels];
        foreach (Prop tree in trees)
        {
            SN.Vector2 c = tree.Footprint.Center, unit = wooded.Area.ToUnit(c);
            int cx = (int)(unit.X * pixels), cy = (int)(unit.Y * pixels), span = (int)MathF.Ceiling(reach / step) + 1;
            for (int py = Math.Max(0, cy - span); py <= Math.Min(pixels - 1, cy + span); py++)
            for (int px = Math.Max(0, cx - span); px <= Math.Min(pixels - 1, cx + span); px++)
                if (SN.Vector2.Distance(CentreOf(wooded, px, py), c) <= reach) near[py * pixels + px] = true;
        }
        ReadOnlySpan<byte> a = wooded.Rgba, b = bare.Rgba;
        int far = 0;
        for (int i = 0; i < near.Length; i++)
        {
            if (near[i]) continue;
            far++;
            if (!a.Slice(i * 4, 4).SequenceEqual(b.Slice(i * 4, 4)))
                Assert.Fail($"pixel ({i % pixels}, {i / pixels}) at {CentreOf(wooded, i % pixels, i / pixels)} is {reach:F1} m or more from every trunk but not the bare ground");
        }
        Assert.True(far > pixels * pixels / 2, $"only {far} pixels are far from the trees");
    }

    [Fact]
    public void Bake_refuses_no_pixels()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MinimapGround.Bake(Flat(), (_, _) => 0f, 0));
    }
}
