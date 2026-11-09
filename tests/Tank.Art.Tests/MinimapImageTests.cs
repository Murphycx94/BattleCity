using Tank.Client.Art;
using Tank.Client.Art.Meshes;
using Tank.Client.Minimap;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Terrain;
using SN = System.Numerics;

namespace Tank.Art.Tests;

public class MinimapImageTests
{
    /// <summary>The riverland and its ground, generated and baked once for the class; tests that change props take a <see cref="Battlefield.Fresh"/> copy.</summary>
    private static readonly Lazy<(Battlefield Field, MinimapGround Ground)> Riverland = new(() =>
    {
        Battlefield field = MapCatalog.Riverland.NewBattlefield();
        return (field, MinimapGround.Bake(field, new GroundLayers(field.Surface).NotGrassAt));
    });

    /// <summary>The sandbox and its ground, likewise.</summary>
    private static readonly Lazy<(Battlefield Field, MinimapGround Ground)> Sandbox = new(() =>
    {
        Battlefield field = MapCatalog.Sandbox.NewBattlefield();
        return (field, MinimapGround.Bake(field, (_, _) => 0f));
    });

    private static Battlefield RiverlandField() => Riverland.Value.Field.Fresh();

    private static MinimapGround RiverlandGround => Riverland.Value.Ground;

    private static (int R, int G, int B) PixelAt(MinimapImage image, SN.Vector2 ground)
    {
        SN.Vector2 unit = image.Ground.Area.ToUnit(ground);
        int px = (int)(unit.X * image.Pixels), py = (int)(unit.Y * image.Pixels);
        int o = (py * image.Pixels + px) * 4;
        return (image.Rgba[o], image.Rgba[o + 1], image.Rgba[o + 2]);
    }

    private static (int R, int G, int B) Bytes(SN.Vector3 colour) =>
        ((int)MathF.Round(colour.X * 255f), (int)MathF.Round(colour.Y * 255f), (int)MathF.Round(colour.Z * 255f));

    private static Prop FirstHouse(Battlefield field) => field.Props.First(p => p.Kind == PropKind.House);

    [Fact]
    public void The_sandbox_shows_its_brick_walls_in_brick()
    {
        Battlefield field = MapCatalog.Sandbox.NewBattlefield();
        MinimapGround ground = MinimapGround.Bake(field, (_, _) => 0f);
        var image = new MinimapImage(ground, field);
        Assert.Equal(ground.Pixels, image.Pixels);
        Assert.Equal(ground.Pixels * ground.Pixels * 4, image.Rgba.Length);

        Prop[] bricks = field.Props.Where(p => p.Kind == PropKind.BrickWall).ToArray();
        Assert.NotEmpty(bricks);
        Prop middle = bricks[bricks.Length / 2];
        Assert.Equal(Bytes(Palette.Brick), PixelAt(image, middle.Footprint.Center));
        Prop stone = field.Props.First(p => p.Kind == PropKind.StoneWall);
        Assert.Equal(Bytes(Palette.Stone), PixelAt(image, stone.Footprint.Center));
    }

    [Fact]
    public void A_standing_house_is_roof_tile_inside_its_outline()
    {
        Battlefield field = RiverlandField();
        var image = new MinimapImage(RiverlandGround, field);
        Assert.Equal(Bytes(Palette.RoofTile), PixelAt(image, FirstHouse(field).Footprint.Center));
    }

    [Fact]
    public void A_house_knocked_down_repaints_only_its_tiles_as_a_whole_repaint_would()
    {
        Battlefield field = RiverlandField();
        MinimapGround ground = RiverlandGround;
        var image = new MinimapImage(ground, field);
        byte[] before = image.Rgba.ToArray();
        Prop house = FirstHouse(field);

        field.SetDurability(house.Id, 0);
        Assert.True(image.Refresh(field));
        Assert.InRange(image.LastRepaintedTiles, 1, 4);
        Assert.Equal(new MinimapImage(ground, field).Rgba, image.Rgba);
        Assert.NotEqual(Bytes(Palette.RoofTile), PixelAt(image, house.Footprint.Center));

        float step = ground.Area.MetresPerPixel(ground.Pixels);
        Obb2 grown = house.Footprint with { HalfExtents = house.Footprint.HalfExtents + new SN.Vector2(step) };
        int changed = 0;
        for (int py = 0; py < image.Pixels; py++)
        for (int px = 0; px < image.Pixels; px++)
        {
            int o = (py * image.Pixels + px) * 4;
            if (before.AsSpan(o, 4).SequenceEqual(image.Rgba.AsSpan(o, 4))) continue;
            changed++;
            SN.Vector2 centre = ground.Area.ToGround(new SN.Vector2((px + 0.5f) / image.Pixels, (py + 0.5f) / image.Pixels));
            Assert.True(grown.Contains(centre), $"pixel ({px}, {py}) at {centre} changed, outside house {house.Id}");
        }
        Assert.True(changed > 0);

        field.SetDurability(house.Id, house.MaxDurability);
        Assert.True(image.Refresh(field));
        Assert.Equal(before, image.Rgba);
    }

    [Fact]
    public void A_wall_hit_but_standing_repaints_nothing()
    {
        Battlefield field = RiverlandField();
        var image = new MinimapImage(RiverlandGround, field);
        byte[] before = image.Rgba.ToArray();
        Prop wall = field.Props.First(p => p.Kind is PropKind.BrickWall or PropKind.StoneWall);

        int version = field.StateVersion;
        field.SetDurability(wall.Id, wall.MaxDurability - 1);
        Assert.NotEqual(version, field.StateVersion);
        Assert.False(image.Refresh(field));
        Assert.Equal(0, image.LastRepaintedTiles);
        Assert.Equal(before, image.Rgba);
    }

    [Fact]
    public void A_fallen_wall_turns_to_pale_rubble()
    {
        Battlefield field = MapCatalog.Sandbox.NewBattlefield();
        MinimapGround ground = MinimapGround.Bake(field, (_, _) => 0f);
        var image = new MinimapImage(ground, field);
        Prop[] bricks = field.Props.Where(p => p.Kind == PropKind.BrickWall).ToArray();
        Prop middle = bricks[bricks.Length / 2];
        SN.Vector2 unit = ground.Area.ToUnit(middle.Footprint.Center);
        int o = ((int)(unit.Y * ground.Pixels) * ground.Pixels + (int)(unit.X * ground.Pixels)) * 4;
        (int R, int G, int B) bare = (ground.Rgba[o], ground.Rgba[o + 1], ground.Rgba[o + 2]);

        field.SetDurability(middle.Id, 0);
        Assert.True(image.Refresh(field));
        var rubble = PixelAt(image, middle.Footprint.Center);
        Assert.NotEqual(bare, rubble);
        Assert.True(rubble.R + rubble.G + rubble.B > bare.R + bare.G + bare.B, $"rubble {rubble} is no paler than the ground {bare}");
        Assert.NotEqual(Bytes(Palette.Brick), rubble);
        Assert.Equal(new MinimapImage(ground, field).Rgba, image.Rgba);
    }

    [Fact]
    public void Nothing_changed_repaints_nothing_and_rising_and_falling_allocates_nothing()
    {
        Battlefield field = RiverlandField();
        var image = new MinimapImage(RiverlandGround, field);
        Assert.False(image.Refresh(field));
        Assert.Equal(0, image.LastRepaintedTiles);

        Prop house = FirstHouse(field);
        Allocation.AssertNone(() =>
        {
            var fresh = RiverlandField();
            var picture = new MinimapImage(RiverlandGround, fresh);
            for (int round = 0; round < 2; round++)   // the first round warms up
            {
                fresh.SetDurability(house.Id, 0);
                picture.Refresh(fresh);
                fresh.SetDurability(house.Id, house.MaxDurability);
                picture.Refresh(fresh);
                picture.Refresh(fresh);
            }
            var window = Allocation.Start();
            fresh.SetDurability(house.Id, 0);
            bool fell = picture.Refresh(fresh);
            fresh.SetDurability(house.Id, house.MaxDurability);
            bool rose = picture.Refresh(fresh);
            bool still = picture.Refresh(fresh);
            var measured = window.Stop();
            Assert.True(fell && rose && !still);
            return measured;
        });
    }

    [Fact]
    public void A_fresh_battlefield_brings_back_everything_standing()
    {
        Battlefield field = RiverlandField();
        MinimapGround ground = RiverlandGround;
        Prop house = FirstHouse(field);
        field.SetDurability(house.Id, 0);
        var image = new MinimapImage(ground, field);

        Battlefield fresh = field.Fresh();   // StateVersion 0 again, so only the instance tells it apart
        Assert.True(image.Refresh(fresh));
        Assert.True(image.LastRepaintedTiles > 0);
        Assert.Equal(new MinimapImage(ground, RiverlandField()).Rgba, image.Rgba);
    }

    [Fact]
    public void Another_map_is_refused()
    {
        Battlefield sandbox = MapCatalog.Sandbox.NewBattlefield();
        Assert.Throws<ArgumentException>(() => new MinimapImage(RiverlandGround, sandbox));
        var image = new MinimapImage(RiverlandGround, RiverlandField());
        Assert.Throws<ArgumentException>(() => image.Refresh(sandbox));
    }

    private static bool IsMasonry(Prop p) => p.Kind is PropKind.House or PropKind.BrickWall or PropKind.StoneWall;

    /// <summary>The picture is byte for byte what a new one painted from <paramref name="field"/> as it stands would be.</summary>
    private static void AssertLikeAWholeRepaint(MinimapImage image, Battlefield field, string what)
    {
        byte[] whole = new MinimapImage(image.Ground, field).Rgba;
        int at = whole.AsSpan().CommonPrefixLength(image.Rgba);
        Assert.True(at == whole.Length, $"{what}: pixel ({at / 4 % image.Pixels}, {at / 4 / image.Pixels}) differs from a whole repaint");
    }

    public static TheoryData<int> Seeds => new() { 1, 2, 3, 4, 5 };

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Random_falls_and_rises_repaint_as_a_whole_repaint_would(int seed)
    {
        foreach ((string name, Battlefield source, MinimapGround ground) in new[]
                 { ("riverland", Riverland.Value.Field, RiverlandGround), ("sandbox", Sandbox.Value.Field, Sandbox.Value.Ground) })
        {
            Battlefield field = source.Fresh();
            var image = new MinimapImage(ground, field);
            Prop[] masonry = field.Props.Where(IsMasonry).ToArray();
            var random = new Random(seed);
            for (int round = 0; round < 3; round++)
            {
                foreach (Prop p in masonry)   // flip about 30 % of them: down if standing, up if fallen
                    if (random.NextDouble() < 0.3) field.SetDurability(p.Id, field.IsStanding(p.Id) ? 0 : p.MaxDurability);
                image.Refresh(field);
                AssertLikeAWholeRepaint(image, field, $"{name}, seed {seed}, round {round}");
            }
        }
    }

    // A made-up 200 m field (border 10, so 180 m on 128 pixels: tiles 45 m wide, their edges at x and z = 55, 100, 145).
    private const int SmallPixels = 128;
    private const float FieldSize = 200f, TileEdge = 100f;

    private static Battlefield SmallField(params Prop[] props)
    {
        const int samples = 101;
        var terrain = new HeightField(samples, samples, FieldSize / (samples - 1), new float[samples * samples]);
        return new Battlefield(terrain, props, border: 10f);
    }

    private static Prop Wall(int id, SN.Vector2 at, SN.Vector2 half, float yaw, PropKind kind = PropKind.BrickWall) =>
        new(id, kind, new Obb2(at, half, yaw), 0f, 2.5f);

    private static Prop House(int id, SN.Vector2 at, SN.Vector2 half) => new(id, PropKind.House, new Obb2(at, half, 0f), 0f, 6f);

    [Fact]
    public void A_turned_wall_across_tile_edges_and_a_wall_over_a_house_repaint_as_a_whole_repaint_would()
    {
        Prop turned = Wall(1, new SN.Vector2(TileEdge, 60f), new SN.Vector2(9f, 0.3f), yaw: 0.6f);       // crosses x = 100
        Prop house = House(2, new SN.Vector2(TileEdge, TileEdge), new SN.Vector2(5f, 4f));              // on four tiles
        Prop over = Wall(3, new SN.Vector2(TileEdge + 2f, TileEdge + 1f), new SN.Vector2(8f, 0.3f), yaw: 0f, PropKind.StoneWall);   // across the house
        Battlefield field = SmallField(turned, house, over);
        MinimapGround ground = MinimapGround.Bake(field, (_, _) => 0f, SmallPixels);
        var image = new MinimapImage(ground, field);
        byte[] standing = image.Rgba.ToArray();

        (int Id, bool Up)[] steps = { (1, false), (2, false), (3, false), (2, true), (1, true), (3, true), (3, false), (2, false), (2, true), (3, true) };
        foreach ((int id, bool up) in steps)
        {
            Prop prop = field.Props.Single(p => p.Id == id);
            field.SetDurability(id, up ? prop.MaxDurability : 0);
            Assert.True(image.Refresh(field), $"prop {id} {(up ? "rose" : "fell")} and nothing was repainted");
            AssertLikeAWholeRepaint(image, field, $"prop {id} {(up ? "up" : "down")}");
        }
        Assert.Equal(standing, image.Rgba);
    }

    private static (int R, int G, int B) PixelAt(MinimapImage image, int px, int py)
    {
        int o = (py * image.Pixels + px) * 4;
        return (image.Rgba[o], image.Rgba[o + 1], image.Rgba[o + 2]);
    }

    private static SN.Vector2 CentreOf(MinimapImage image, int px, int py) =>
        image.Ground.Area.ToGround(new SN.Vector2((px + 0.5f) / image.Pixels, (py + 0.5f) / image.Pixels));

    [Fact]
    public void A_standing_house_s_edge_pixels_are_the_rim_colour()
    {
        Prop house = House(1, new SN.Vector2(80f, 80f), new SN.Vector2(6f, 4f));
        Battlefield field = SmallField(house);
        var image = new MinimapImage(MinimapGround.Bake(field, (_, _) => 0f, SmallPixels), field);
        SN.Vector2 unit = image.Ground.Area.ToUnit(house.Footprint.Center);
        int cx = (int)(unit.X * SmallPixels), cy = (int)(unit.Y * SmallPixels);
        int left = cx;
        while (house.Footprint.Contains(CentreOf(image, left - 1, cy))) left--;   // the westmost pixel of the middle row inside the house

        Assert.Equal(Bytes(MinimapStyle.HouseRim), PixelAt(image, left, cy));
        Assert.Equal(Bytes(MinimapStyle.HouseRoof), PixelAt(image, left + 1, cy));
        Assert.Equal(Bytes(MinimapStyle.HouseRoof), PixelAt(image, cx, cy));
    }

    public static TheoryData<float, float> DiagonalWalls => new()
    {
        { 20f, 0.3f },   // as walls are built: long along Right, thin along Forward
        { 0.3f, 20f },   // thin along Right: the thinner side is widened whichever axis it is
    };

    [Theory]
    [MemberData(nameof(DiagonalWalls))]
    public void A_diagonal_wall_stays_at_least_two_pixels_thick_and_in_one_piece(float halfRight, float halfForward)
    {
        Prop wall = Wall(1, new SN.Vector2(TileEdge, TileEdge), new SN.Vector2(halfRight, halfForward), yaw: MathF.PI / 4);
        Battlefield field = SmallField(wall);
        var image = new MinimapImage(MinimapGround.Bake(field, (_, _) => 0f, SmallPixels), field);
        var brick = new bool[SmallPixels * SmallPixels];
        for (int py = 0; py < SmallPixels; py++)
        for (int px = 0; px < SmallPixels; px++)
            brick[py * SmallPixels + px] = PixelAt(image, px, py) == Bytes(Palette.Brick);

        // Every row the wall's middle 80 % crosses holds 2 brick pixels or more (a 45° strip 2 px across is ~2.8 px along a row).
        float reach = MathF.Max(halfRight, halfForward) * 0.8f * MathF.Sqrt(0.5f);
        int rows = 0;
        for (int py = 0; py < SmallPixels; py++)
        {
            if (MathF.Abs(CentreOf(image, 0, py).Y - TileEdge) > reach) continue;
            rows++;
            int count = 0;
            for (int px = 0; px < SmallPixels; px++) if (brick[py * SmallPixels + px]) count++;
            Assert.True(count >= 2, $"row {py} has {count} brick pixels");
        }
        Assert.True(rows > 10);

        // And they are one 4-connected piece.
        int total = brick.Count(b => b), first = Array.IndexOf(brick, true), reached = 0;
        var seen = new bool[brick.Length];
        var queue = new Queue<int>([first]);
        seen[first] = true;
        while (queue.TryDequeue(out int at))
        {
            reached++;
            int x = at % SmallPixels, y = at / SmallPixels;
            foreach ((int nx, int ny) in new[] { (x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1) })
            {
                if (nx < 0 || ny < 0 || nx >= SmallPixels || ny >= SmallPixels) continue;
                int n = ny * SmallPixels + nx;
                if (!brick[n] || seen[n]) continue;
                seen[n] = true;
                queue.Enqueue(n);
            }
        }
        Assert.Equal(total, reached);
    }

    [Fact]
    public void Another_battlefield_with_its_own_props_is_refused_even_of_the_same_map()
    {
        Battlefield field = RiverlandField();
        var image = new MinimapImage(RiverlandGround, field);
        var copy = new Battlefield(field.Terrain, field.Props, field.Border, field.Surface, field.WaterLevel);   // equal props, another array
        Assert.Throws<ArgumentException>(() => image.Refresh(copy));
        Assert.False(image.Refresh(field.Fresh()));   // a fresh copy shares the props: welcome, and nothing has fallen
    }
}
