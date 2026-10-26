using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;

namespace Tank.Sim.Tests;

/// <summary>The map catalog and its map definitions (riverland design §4.4; stage 5 design §2).</summary>
public sealed class MapCatalogTests
{
    [Fact]
    public void The_catalog_lists_sandbox_then_riverland()
    {
        Assert.Equal(new[] { MapCatalog.Sandbox, MapCatalog.Riverland }, MapCatalog.All);
        Assert.Equal("sandbox", MapCatalog.Sandbox.Id);
        Assert.Equal("试驾场地图", MapCatalog.Sandbox.Name);
        Assert.Equal("riverland", MapCatalog.Riverland.Id);
        Assert.Equal("河谷", MapCatalog.Riverland.Name);
        Assert.Equal("sandbox", SandboxMap.Id);
        Assert.Equal("riverland", RiverlandMap.Id);
    }

    [Fact]
    public void Lookup_ignores_case_and_rejects_unknown_ids()
    {
        Assert.True(MapCatalog.TryGet("RiverLand", out MapDefinition? riverland));
        Assert.Same(MapCatalog.Riverland, riverland);
        Assert.True(MapCatalog.TryGet("sandbox", out MapDefinition? sandbox));
        Assert.Same(MapCatalog.Sandbox, sandbox);
        Assert.False(MapCatalog.TryGet("x", out _));
        Assert.False(MapCatalog.TryGet(null, out _));
        Assert.False(MapCatalog.TryGet("", out _));
        Assert.Same(MapCatalog.Riverland, MapCatalog.Get("RIVERLAND"));
        Assert.Throws<ArgumentException>(() => MapCatalog.Get("x"));
    }

    [Fact]
    public void Spawns_and_waypoints_are_the_maps_own()
    {
        Assert.Same(SandboxMap.Spawns, MapCatalog.Sandbox.Spawns);
        Assert.Same(SandboxMap.Waypoints, MapCatalog.Sandbox.Waypoints);
        Assert.Same(RiverlandMap.Spawns, MapCatalog.Riverland.Spawns);
        Assert.Same(RiverlandMap.Waypoints, MapCatalog.Riverland.Waypoints);
    }

    [Fact]
    public void Map_data_is_built_once()
    {
        int calls = 0;
        MapDefinition map = Counted(() => calls++);
        Assert.False(map.IsDataReady);
        MapData first = map.Data, second = map.Data;
        Assert.Same(first, second);
        Assert.Equal(1, calls);
        Assert.True(map.IsDataReady);
    }

    [Fact]
    public void New_battlefields_share_the_geometry_but_not_the_state()
    {
        int calls = 0;
        MapDefinition map = Counted(() => calls++);
        Battlefield a = map.NewBattlefield(), b = map.NewBattlefield();
        Assert.NotSame(a, b);
        Prop wall = a.Props.First(p => p.IsWall);
        a.SetDurability(wall.Id, 0);
        Assert.False(a.IsStanding(wall.Id));
        Assert.True(b.IsStanding(wall.Id));
        Assert.Equal(wall.MaxDurability, b.Durability(wall.Id));
        Assert.Equal(a.Props, b.Props);
        for (int j = 0; j < a.Terrain.SamplesZ; j++)
        for (int i = 0; i < a.Terrain.SamplesX; i++)
            Assert.Equal(a.Terrain.Sample(i, j), b.Terrain.Sample(i, j));
        _ = map.Data;
        Assert.True(map.NewBattlefield().IsStanding(wall.Id), "a third one is fresh too");
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Riverland_data_round_trips_to_the_same_ground()
    {
        Battlefield generated = RiverlandFixture.Field;
        Battlefield built = MapCatalog.Riverland.Data.Build();
        Assert.Equal(generated.Surface.Cells.ToArray(), built.Surface.Cells.ToArray());
        Assert.Equal(0f, built.WaterLevel);
        Assert.Equal(generated.Props, built.Props);
        var rng = new XorShift(RiverlandMap.Seed);
        for (int k = 0; k < 100; k++)
        {
            var p = new Vector2(rng.NextFloat() * built.Terrain.SizeX, rng.NextFloat() * built.Terrain.SizeZ);
            Assert.True(MathF.Abs(generated.Terrain.HeightAt(p.X, p.Y) - built.Terrain.HeightAt(p.X, p.Y)) <= 0.005f, $"height at {p}");
        }
    }

    [Fact]
    public void A_new_riverland_battlefield_is_bit_for_bit_what_clients_build_from_its_data()
    {
        Battlefield local = MapCatalog.Riverland.NewBattlefield();
        Battlefield client = MapCatalog.Riverland.Data.Build();

        Assert.Equal(Bits(client.Border), Bits(local.Border));
        Assert.Equal(Bits(client.Terrain.CellSize), Bits(local.Terrain.CellSize));
        for (int j = 0; j < local.Terrain.SamplesZ; j++)
        for (int i = 0; i < local.Terrain.SamplesX; i++)
            if (Bits(local.Terrain.Sample(i, j)) != Bits(client.Terrain.Sample(i, j)))
                Assert.Fail($"sample ({i}, {j}): {local.Terrain.Sample(i, j)} here, {client.Terrain.Sample(i, j)} on a client");
        var rng = new XorShift(RiverlandMap.Seed + 1);
        for (int k = 0; k < 1000; k++)
        {
            var p = new Vector2(rng.NextFloat() * local.Terrain.SizeX, rng.NextFloat() * local.Terrain.SizeZ);
            Assert.Equal(Bits(client.Terrain.HeightAt(p.X, p.Y)), Bits(local.Terrain.HeightAt(p.X, p.Y)));
        }
        Assert.True(local.Surface.Cells.SequenceEqual(client.Surface.Cells), "ground kinds");
        Assert.Equal(Bits(client.WaterLevel!.Value), Bits(local.WaterLevel!.Value));
        Assert.Equal(client.Props.Count, local.Props.Count);
        for (int k = 0; k < local.Props.Count; k++)
            Assert.True(PropBits(local.Props[k]).SequenceEqual(PropBits(client.Props[k])), $"prop {local.Props[k].Id}");
    }

    private static int Bits(float value) => BitConverter.SingleToInt32Bits(value);

    private static int[] PropBits(Prop p) =>
    [
        p.Id, (int)p.Kind, Bits(p.Footprint.Center.X), Bits(p.Footprint.Center.Y), Bits(p.Footprint.HalfExtents.X),
        Bits(p.Footprint.HalfExtents.Y), Bits(p.Footprint.Yaw), Bits(p.Bottom), Bits(p.Height),
    ];

    /// <summary>A map of the sandbox whose generator calls <paramref name="onCreate"/> each time it runs.</summary>
    private static MapDefinition Counted(Action onCreate) =>
        new("counted", "计数", () => { onCreate(); return SandboxMap.Create(); }, SandboxMap.Spawns, SandboxMap.Waypoints);
}
