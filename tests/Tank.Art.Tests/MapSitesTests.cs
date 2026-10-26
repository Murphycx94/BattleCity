using Tank.Client.Sandbox;
using Tank.Sim;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Modes;
using Tank.Sim.Terrain;
using Tank.Sim.Vehicles;
using SN = System.Numerics;

namespace Tank.Art.Tests;

public class MapSitesTests
{
    /// <summary>The riverland as the test drive plays it (generated once per test run by the catalog).</summary>
    private static Battlefield Riverland => MapCatalog.Riverland.NewBattlefield();

    private static readonly string[] SandboxPresets =
        { "front", "side", "close", "closerear", "sprocket", "hill", "spin", "orbit", "bush", "target", "yard", "gate", "patrol", "base0", "base1", "base0near", "base1near", "base0far", "base1far" };

    [Fact]
    public void Unknown_or_missing_ids_get_the_sandbox()
    {
        Assert.Same(MapSites.Sandbox, MapSites.For(null));
        Assert.Same(MapSites.Sandbox, MapSites.For(""));
        Assert.Same(MapSites.Sandbox, MapSites.For("moon"));
        Assert.Same(MapSites.Sandbox, MapSites.For("sandbox"));
        Assert.Same(MapSites.Riverland, MapSites.For("RIVERLAND"));
    }

    [Fact]
    public void A_field_s_map_is_known_by_its_ground_grid()
    {
        Assert.Equal(RiverlandMap.Id, MapSites.IdOfGrid(new HeightField(RiverlandMap.Samples, RiverlandMap.Samples, RiverlandMap.CellSize, new float[RiverlandMap.Samples * RiverlandMap.Samples])));
        Assert.Equal(SandboxMap.Id, MapSites.IdOfGrid(new HeightField(SandboxMap.Samples, SandboxMap.Samples, SandboxMap.CellSize, new float[SandboxMap.Samples * SandboxMap.Samples])));
        Assert.Null(MapSites.IdOfGrid(new HeightField(33, 33, 1f, new float[33 * 33])));
        Assert.Null(MapSites.IdOfGrid(new HeightField(RiverlandMap.Samples, RiverlandMap.Samples, 1f, new float[RiverlandMap.Samples * RiverlandMap.Samples])));
    }

    [Fact]
    public void The_sandbox_site_is_todays_test_drive()
    {
        MapSite site = MapSites.Sandbox;
        Assert.Same(MapCatalog.Sandbox, site.Map);
        Assert.Equal("坦克 3D · 试驾场", site.Title);
        Assert.Equal(new SN.Vector2(200, 330), site.Spawn);
        Assert.Equal(0f, site.SpawnYaw);
        Assert.Same(SandboxMap.TargetSpots, site.Targets);
        Assert.True(site.HasPatrol);
        Assert.Equal(SandboxPresets, site.PresetNames);
        Assert.Equal(RangeDemo.Names, site.DemoNames);
        Assert.Equal(RangeDemo.Find("lamp"), site.FindDemo("lamp"));
        Assert.Null(site.FindDemo("moon"));
        Assert.Equal(new[] { "base0", "base0far", "base0near", "base1", "base1far", "base1near" }, site.Anchors.Keys.Order());   // its other cameras stay the constants in CameraPresets
        Assert.Equal(SandboxMap.BushPatches[0], site.PresetSpawns["bush"]);
        Assert.Equal(SandboxMap.PatrolWatch, site.PresetSpawns["patrol"]);
        Assert.Equal(2, site.PresetSpawns.Count);
        Assert.Equal(new SN.Vector2(200, 200), site.SpinEye);
        Assert.Equal(new SN.Vector2(200, 200), site.MapCentre);
        Assert.Equal(400f, site.MapSize);
    }

    [Fact]
    public void The_riverland_site_has_its_own_spots_and_no_patrol()
    {
        MapSite site = MapSites.Riverland;
        Assert.Same(MapCatalog.Riverland, site.Map);
        Assert.Equal("坦克 3D · 试驾场 · 河谷", site.Title);
        Assert.Equal(RiverlandMap.Spawn, site.Spawn);
        Assert.Equal(RiverlandMap.SpawnYaw, site.SpawnYaw);
        Assert.Same(RiverlandMap.TargetSpots, site.Targets);
        Assert.False(site.HasPatrol);
        foreach (string name in new[] { "overview", "town", "ford", "forest", "plateau", "dunes", "map", "orbit", "spin", "target", "front", "side" })
            Assert.Contains(name, site.PresetNames);
        foreach (string name in new[] { "yard", "gate", "patrol", "bush", "hill" })
            Assert.DoesNotContain(name, site.PresetNames);
        Assert.Equal(RiverlandDemos.Names, site.DemoNames);   // its own, none of the sandbox's
        Assert.Null(site.FindDemo("pen"));
        Assert.Equal(new SN.Vector2(600, 600), site.SpinEye);
        Assert.Equal(new SN.Vector2(600, 600), site.MapCentre);
        Assert.Equal(1200f, site.MapSize);
        Assert.Equal(new[] { "ford" }, site.PresetSpawns.Keys);
    }

    [Fact]
    public void Riverland_anchors_are_on_the_map_and_above_ground_and_water()
    {
        Battlefield field = Riverland;
        MapSite site = MapSites.Riverland;
        Assert.Equal(new[] { "overview", "town", "ford", "forest", "plateau", "dunes", "map", "base0", "base1", "base0near", "base1near", "base0far", "base1far" }.Order(), site.Anchors.Keys.Order());
        foreach (var (name, anchor) in site.Anchors)
        {
            foreach (SN.Vector2 p in new[] { anchor.Eye, anchor.Focus })
                Assert.True(p.X is >= 0 and <= 1200 && p.Y is >= 0 and <= 1200, $"{name}: {p} is off the map");
            Assert.True(anchor.EyeHeight >= 2f, $"{name}: the eye is {anchor.EyeHeight} m up");
            Assert.True(anchor.FocusHeight >= 0f, $"{name}: the focus is under the ground or the water");
            Assert.True(anchor.Eye != anchor.Focus || anchor.EyeHeight > anchor.FocusHeight, $"{name}: looks nowhere");
            // Nothing but air between the eye and what it looks at: the sight line clears the ground all the way.
            SN.Vector3 eye = OverSurface(field, anchor.Eye, anchor.EyeHeight), focus = OverSurface(field, anchor.Focus, anchor.FocusHeight);
            for (float t = 0.02f; t < 0.98f; t += 0.01f)
            {
                SN.Vector3 p = SN.Vector3.Lerp(eye, focus, t);
                Assert.True(p.Y > field.Terrain.HeightAt(p.X, p.Z), $"{name}: the ground hides the focus at {p}");
            }
        }
        Assert.True(site.Anchors["map"].EyeHeight >= 500f);
        Assert.Equal(site.MapCentre, site.Anchors["map"].Eye);
        Assert.Equal(site.MapCentre, site.Anchors["map"].Focus);
    }

    public static TheoryData<string> SiteIds => new() { SandboxMap.Id, RiverlandMap.Id };

    [Theory]
    [MemberData(nameof(SiteIds))]
    public void Base_cameras_look_at_each_base_from_its_own_team_s_side(string mapId)
    {
        MapSite site = MapSites.For(mapId);
        Assert.Equal(mapId, site.Map.Id);
        IReadOnlyList<TeamBase> bases = mapId == SandboxMap.Id ? SandboxMap.Bases : RiverlandMap.Bases;
        Assert.Same(bases, site.Map.Bases);
        foreach (TeamBase b in bases)
        {
            string name = $"base{b.Team}";
            foreach (string preset in new[] { name, name + "near", name + "far" })
                Assert.Contains(preset, site.PresetNames);   // what CameraPresets.IsKnownFor reads (it lives with Godot, outside these tests)
            CameraAnchor anchor = site.Anchors[name];
            Assert.Equal(b.Centre, anchor.Focus);
            Assert.Equal(0f, anchor.FocusHeight);
            Assert.Equal(30f, anchor.EyeHeight);
            Assert.Equal(45f, SN.Vector2.Distance(anchor.Eye, anchor.Focus), 3);
            // The eye stands on the line from the centre to the team's spawns: the side its own team comes from.
            SN.Vector2[] spawns = site.Map.Spawns.Team.Where(p => p.Team == b.Team).Select(p => p.Position).ToArray();
            SN.Vector2 home = spawns.Aggregate(SN.Vector2.Zero, (sum, p) => sum + p) / spawns.Length;
            SN.Vector2 toHome = SN.Vector2.Normalize(home - b.Centre);
            Assert.True(SN.Vector2.Dot(toHome, SN.Vector2.Normalize(anchor.Eye - b.Centre)) > 0.9999f, $"{name}: the eye is off the line to the team's spawns");

            // Near: a tank's-height eye 30 m out on the same line, looking at the centre.
            CameraAnchor near = site.Anchors[name + "near"];
            Assert.Equal(b.Centre, near.Focus);
            Assert.Equal(0f, near.FocusHeight);
            Assert.Equal(4f, near.EyeHeight);
            Assert.Equal(30f, SN.Vector2.Distance(near.Eye, b.Centre), 3);
            Assert.True(SN.Vector2.Dot(toHome, SN.Vector2.Normalize(near.Eye - b.Centre)) > 0.9999f, $"{name}near: the eye is off the line to the team's spawns");

            // Far: the follow camera of a tank 46 m out (the spawn distance) facing the base: 14 m behind and above it at
            // the start pitch, looking at the point 4.5 m over the tank.
            CameraAnchor far = site.Anchors[name + "far"];
            Assert.Equal(46f, SN.Vector2.Distance(far.Focus, b.Centre), 3);
            Assert.Equal(4.5f, far.FocusHeight);
            Assert.Equal(46f + 14f * MathF.Cos(0.25f), SN.Vector2.Distance(far.Eye, b.Centre), 2);
            Assert.Equal(4.5f + 14f * MathF.Sin(0.25f), far.EyeHeight, 2);
            Assert.True(SN.Vector2.Dot(toHome, SN.Vector2.Normalize(far.Eye - b.Centre)) > 0.9999f, $"{name}far: the eye is off the line to the team's spawns");
            Assert.True(SN.Vector2.Dot(toHome, SN.Vector2.Normalize(far.Focus - b.Centre)) > 0.9999f, $"{name}far: the focus is off the line to the team's spawns");
        }
    }

    /// <summary>The point <paramref name="height"/> m over the ground or the water at <paramref name="at"/>, whichever is higher.</summary>
    private static SN.Vector3 OverSurface(Battlefield field, SN.Vector2 at, float height) =>
        new(at.X, MathF.Max(field.Terrain.HeightAt(at.X, at.Y), field.WaterLevel ?? float.NegativeInfinity) + height, at.Y);

    [Fact]
    public void The_ford_preset_starts_on_the_road_beside_the_water()
    {
        Battlefield field = Riverland;
        SN.Vector2 ford = MapSites.Riverland.PresetSpawns["ford"];
        Assert.Equal(SurfaceKind.Road, field.SurfaceAt(ford));
        Assert.Equal(0f, field.WaterDepthAt(ford));
        bool waterNear = false;
        for (float dx = -20; dx <= 20; dx += 1)
        for (float dz = -20; dz <= 20; dz += 1)
            if (dx * dx + dz * dz <= 400 && field.WaterDepthAt(ford + new SN.Vector2(dx, dz)) > 0.3f) waterNear = true;
        Assert.True(waterNear, "no water within 20 m");
        // Parked there facing east, no tank overlaps a solid prop or a practice target.
        foreach (TankSpec spec in TankCatalog.All)
        {
            Obb2 hull = TankMotion.Footprint(spec, ford, MapSites.Riverland.SpawnYaw);
            foreach (Prop p in field.Props)
                Assert.False(p.IsSolid && Obb2.TryGetPenetration(hull, p.Footprint, out _), $"{spec.Id} parked on prop {p.Id}");
            foreach (TargetSpot s in RiverlandMap.TargetSpots)
                Assert.False(Obb2.TryGetPenetration(hull, TankMotion.Footprint(s.Spec, s.Position, s.Yaw), out _), $"{spec.Id} parked on a target");
        }
    }

    [Fact]
    public void A_demo_with_spots_and_an_aim_point_uses_them()
    {
        Battlefield field = Riverland;
        var stand = new SN.Vector2(600, 540);
        var aimAt = new SN.Vector3(655, 6f, 590);
        var demo = new RangeDemo(1, 0, 0, DemoAim.HullFront, AmmoType.HE, true, Stand: stand, Spots: RiverlandMap.TargetSpots, AimAt: aimAt);
        Assert.Equal(RiverlandMap.TargetSpots[1], demo.Spot);
        Assert.Equal(stand, demo.PlayerPosition);
        Assert.Equal(Angles.YawOf(new SN.Vector2(aimAt.X, aimAt.Z) - stand), demo.Bearing);

        var tank = new TankState { Spec = TankCatalog.Medium, Position = stand, Yaw = demo.PlayerYaw };
        InputCmd command = demo.Command(new InputCmd(1, 0, 0, demo.PlayerYaw, 0f, false), tank, field, hasFired: false);
        SN.Vector3 trunnion = TankPose.Compute(tank, field.Terrain).Trunnion(tank.Spec);
        var (yaw, pitch) = Angles.AimAngles(aimAt - trunnion, demo.PlayerYaw);
        Assert.Equal(yaw, command.AimYaw, 5);
        Assert.Equal(pitch, command.AimPitch, 5);
        // Without an aim point the gun goes on the target's armour, as before.
        var plain = demo with { AimAt = null };
        InputCmd onArmour = plain.Command(new InputCmd(1, 0, 0, demo.PlayerYaw, 0f, false), tank, field, hasFired: false);
        SN.Vector2 spot = RiverlandMap.TargetSpots[1].Position;
        var (armourYaw, _) = Angles.AimAngles(plain.AimPoint(field.Terrain.HeightAt(spot.X, spot.Y)) - trunnion, demo.PlayerYaw);
        Assert.Equal(armourYaw, onArmour.AimYaw, 5);
        Assert.Equal(Angles.YawOf(spot - stand), plain.Bearing);
    }
    [Fact]
    public void Fixed_cameras_stand_on_the_water_where_there_is_some()
    {
        Battlefield field = Riverland;
        Func<float, float, float> surface = MapSites.SurfaceHeight(field);
        float water = field.WaterLevel!.Value;
        int wet = 0, dry = 0;
        for (float x = 15f; x < field.Terrain.SizeX - 15f; x += 37f)
        for (float z = 15f; z < field.Terrain.SizeZ - 15f; z += 41f)
        {
            float ground = field.Terrain.HeightAt(x, z);
            Assert.Equal(MathF.Max(ground, water), surface(x, z));
            if (ground < water) wet++;
            else dry++;
        }
        Assert.True(wet > 10 && dry > 10, $"{wet} wet and {dry} dry points");
    }

    [Fact]
    public void Without_water_fixed_cameras_stand_on_the_ground()
    {
        Battlefield field = MapCatalog.Sandbox.NewBattlefield();
        Func<float, float, float> surface = MapSites.SurfaceHeight(field);
        for (float x = 5f; x < 400f; x += 23f)
        for (float z = 5f; z < 400f; z += 29f)
            Assert.Equal(field.Terrain.HeightAt(x, z), surface(x, z));
    }
}
