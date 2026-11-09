using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;
using Tank.Sim.Vision;
using L = Tank.Sim.Maps.Riverland.RiverlandLayout;

namespace Tank.Sim.Tests;

/// <summary>The riverland test drive's spawn, its eight targets and what each is there for (riverland design §3.11).</summary>
public sealed class RiverlandSitesTests
{
    private static Battlefield Field => RiverlandFixture.Field;
    private static Vector2 Spawn => RiverlandMap.Spawn;
    private static TargetSpot Target(int index) => RiverlandMap.TargetSpots[index];

    [Fact]
    public void The_test_drive_starts_on_the_road_west_of_town_facing_east()
    {
        Assert.Equal(SurfaceKind.Road, Field.SurfaceAt(Spawn));
        Assert.InRange(Vector2.Distance(Spawn, RiverlandMap.Landmarks.Town), 130f, 170f);
        Assert.True(Spawn.X < RiverlandMap.Landmarks.Town.X, "west of the town");
        Assert.True(MathF.Abs(Angles.Wrap(RiverlandMap.SpawnYaw - Angles.YawOf(new Vector2(1, 0)))) < Angles.Deg(10), "faces east");
        Assert.Equal(8, RiverlandMap.TargetSpots.Count);
    }

    [Fact]
    public void Target_0_is_a_medium_tank_on_the_road_at_the_west_entrance()
    {
        TargetSpot t = Target(RiverlandMap.RoadTarget);
        Assert.Same(TankCatalog.Medium, t.Spec);
        Assert.Equal(SurfaceKind.Road, Field.SurfaceAt(t.Position));
        Assert.InRange(Vector2.Distance(t.Position, Spawn), 70f, 90f);
        Assert.True(t.Position.X < RiverlandMap.Landmarks.Town.X, "west of the river");
        Assert.True(MathF.Abs(Angles.Wrap(t.Yaw - Angles.YawOf(Spawn - t.Position))) < Angles.Deg(15), "faces the spawn");
    }

    [Fact]
    public void Target_1_hides_behind_a_house_until_it_falls()
    {
        TargetSpot t = Target(RiverlandMap.HouseTarget);
        Assert.Same(TankCatalog.Heavy, t.Spec);
        Battlefield field = Field.Fresh();
        var sim = new Simulation(field);
        sim.AddTank(TankCatalog.Medium, Spawn, RiverlandMap.SpawnYaw, team: 0);
        sim.AddTank(t.Spec, t.Position, t.Yaw, team: 1);
        TankState observer = sim.Tanks[0], target = sim.Tanks[1];
        Assert.False(Spotting.Look(observer, target, field, sim.Tick, false).LineClear, "seen from the spawn");

        TankPose seer = TankPose.Compute(observer, field.Terrain), seen = TankPose.Compute(target, field.Terrain);
        Vector3 eye = Spotting.TurretTop(observer, seer);
        int house = 0;
        foreach (Vector3 end in new[] { seen.HullBox(target.Spec).Center, Spotting.TurretTop(target, seen) })
            if (field.Raycast(eye, end, out RayHit hit) && hit.PropId != RayHit.TerrainId && field.PropById(hit.PropId).Kind == PropKind.House)
                house = hit.PropId;
        Assert.True(house != 0, "neither sight line stops at a house first");
        Assert.True(Vector2.Distance(field.PropById(house).Footprint.Center, L.Town) <= L.TownRadius, "the house is in the town");

        field.SetDurability(house, 0);
        Assert.True(Spotting.Look(observer, target, field, sim.Tick, false).LineClear, $"still hidden with house {house} down");
    }

    [Fact]
    public void Target_2_wades_in_the_ford()
    {
        TargetSpot t = Target(RiverlandMap.FordTarget);
        Assert.Same(TankCatalog.Light, t.Spec);
        Assert.InRange(Field.WaterDepthAt(t.Position), 0.3f, 0.7f);
        Assert.True(Vector2.Distance(t.Position, RiverlandMap.Landmarks.Ford) <= 20f);
    }

    [Fact]
    public void Target_3_waits_under_a_crown_across_the_river()
    {
        TargetSpot t = Target(RiverlandMap.ForestTarget);
        Assert.Same(TankCatalog.Td, t.Spec);
        Assert.True(RiverSide(t.Position) < 0, "on the east bank");
        Assert.Contains(Field.Props, p => p.Kind == PropKind.Tree
            && Vector2.Distance(p.Footprint.Center, t.Position) <= Spotting.InsideBush * p.FoliageRadius);
        Assert.True(MathF.Abs(L.Forests[0].SignedDistance(t.Position)) <= 30f, "at the east forest's edge");
    }

    [Fact]
    public void Target_4_sits_in_a_bush_on_the_west_bank()
    {
        TargetSpot t = Target(RiverlandMap.BushTarget);
        Assert.Same(TankCatalog.Medium, t.Spec);
        Assert.Contains(Field.Props, p => p.Kind == PropKind.Bush
            && Vector2.Distance(p.Footprint.Center, t.Position) <= Spotting.InsideBush * p.Footprint.HalfExtents.X);
        Assert.True(RiverSide(t.Position) > 0, "on the west bank");
        Assert.True(L.River.DistanceTo(t.Position, out _) <= 120f);
    }

    [Fact]
    public void Target_5_watches_from_the_plateau_rim_far_away()
    {
        TargetSpot t = Target(RiverlandMap.PlateauTarget);
        Assert.Same(TankCatalog.Heavy, t.Spec);
        Assert.InRange(Vector2.Distance(t.Position, Spawn), 250f, 350f);
        float rise = Field.Terrain.HeightAt(t.Position.X, t.Position.Y) - Field.Terrain.HeightAt(Spawn.X, Spawn.Y);
        Assert.True(rise >= 18f, $"only {rise:F1} m above the spawn");
    }

    [Fact]
    public void Target_6_stands_on_beach_sand()
    {
        TargetSpot t = Target(RiverlandMap.BeachTarget);
        Assert.Same(TankCatalog.Light, t.Spec);
        Assert.Equal(SurfaceKind.Sand, Field.SurfaceAt(t.Position));
        Assert.Equal(0f, Field.WaterDepthAt(t.Position));
        Assert.InRange(L.River.DistanceTo(t.Position, out _), 18f, 30f);
    }

    [Fact]
    public void Target_7_stands_in_open_ground_to_the_south()
    {
        TargetSpot t = Target(RiverlandMap.FieldTarget);
        Assert.Same(TankCatalog.Medium, t.Spec);
        Assert.True(t.Position.Y - Spawn.Y >= 150f, $"only {t.Position.Y - Spawn.Y:F0} m south of the spawn");
        Assert.All(Field.Props.Where(p => p.IsSolid && p.Kind != PropKind.Tree), p =>
            Assert.True(p.Footprint.DistanceToPoint(t.Position) >= 15f, $"{p.Kind} {p.Id} within 15 m"));
    }

    [Fact]
    public void Every_target_is_clear_of_solid_props_and_inside_the_playable_area()
    {
        foreach (TargetSpot t in RiverlandMap.TargetSpots)
        {
            Obb2 hull = TankMotion.Footprint(t.Spec, t.Position, t.Yaw);
            Assert.Equal(t.Position, Field.ClampToPlayable(t.Position));
            Assert.All(Field.Props.Where(p => p.IsSolid && p.Kind != PropKind.Tree), p =>
                Assert.False(Obb2.TryGetPenetration(hull, p.Footprint, out _), $"the target at {t.Position} overlaps {p.Kind} {p.Id}"));
        }
    }

    [Fact]
    public void Landmarks_lie_where_the_layout_puts_them()
    {
        RiverlandLandmarks m = RiverlandMap.Landmarks;
        Assert.Equal(new Vector2(700, 560), m.Town);
        Assert.Equal(new Vector2(1015, 1025), m.Dunes);
        Assert.Equal(new Vector2(330, 800), m.Lake);
        Assert.Equal(new Vector2(600, 600), m.MapCentre);
        Assert.InRange(Field.WaterDepthAt(m.Ford), 0.4f, 0.6f);
        Assert.InRange(Field.WaterDepthAt(m.SouthFord), 0.4f, 0.6f);
        Assert.True(Vector2.Distance(m.SouthFord, new Vector2(850, 1000)) < 20f, $"the dune track's ford at {m.SouthFord}");
        Assert.Equal(SurfaceKind.Road, Field.SurfaceAt(m.ForestEdge));
        Assert.True(MathF.Abs(L.Forests[0].SignedDistance(m.ForestEdge)) <= L.ForestJitter, $"the forest edge at {m.ForestEdge}");
        Assert.True(Vector2.Distance(m.PlateauRim, L.Plateau.Max) < 30f, $"the plateau rim at {m.PlateauRim}");
        float rise = Field.Terrain.HeightAt(m.PlateauRim.X, m.PlateauRim.Y) - Field.Terrain.HeightAt(Spawn.X, Spawn.Y);
        Assert.True(rise >= 18f, $"the plateau rim is only {rise:F1} m above the spawn");
    }

    [Fact]
    public void The_public_forests_are_the_layouts()
    {
        Assert.Equal(L.Forests.Length, RiverlandMap.Forests.Count);
        for (int i = 0; i < L.Forests.Length; i++)
        {
            Assert.Equal(L.Forests[i].Min, RiverlandMap.Forests[i].Min);
            Assert.Equal(L.Forests[i].Max, RiverlandMap.Forests[i].Max);
        }
        Assert.Equal(new[] { "east", "west", "north", "north-east", "centre" }, RiverlandMap.Forests.Select(f => f.Name));
        Assert.Equal(L.ForestJitter, RiverlandMap.ForestJitter);
    }

    /// <summary>Positive west of the river's centre line, negative east of it (x east, z south, the river flowing south).</summary>
    private static float RiverSide(Vector2 p)
    {
        L.River.DistanceTo(p, out float along);
        Vector2 t = L.River.TangentAt(along), v = p - L.River.PointAt(along);
        return t.X * v.Y - t.Y * v.X;
    }
}
