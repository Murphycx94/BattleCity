using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Tests;

public sealed class SandboxMapTests
{
    [Fact]
    public void Create_IsDeterministic()
    {
        Battlefield a = SandboxMap.Create(), b = SandboxMap.Create();
        Assert.Equal(a.Props.Count, b.Props.Count);
        Assert.Equal(a.Props[0], b.Props[0]);
        Assert.Equal(a.Terrain.HeightAt(123, 45), b.Terrain.HeightAt(123, 45));
    }

    [Fact]
    public void Spawn_IsPlayable_AndClearOfRocks()
    {
        Battlefield field = SandboxMap.Create();
        Assert.Equal(SandboxMap.Spawn, field.ClampToPlayable(SandboxMap.Spawn));
        Obb2 hull = TankMotion.Footprint(TankCatalog.Medium, SandboxMap.Spawn, 0);
        Assert.All(field.Props, p => Assert.False(Obb2.TryGetPenetration(hull, p.Footprint, out _)));
    }

    [Fact]
    public void Terrain_HasAHillWorthClimbing()
    {
        Battlefield field = SandboxMap.Create();
        float rise = field.Terrain.HeightAt(200, 150) - field.Terrain.HeightAt(SandboxMap.Spawn.X, SandboxMap.Spawn.Y);
        Assert.True(rise > 15, $"hill only {rise}m above spawn");
    }

    [Fact]
    public void Rocks_SitOnTheGround()
    {
        Battlefield field = SandboxMap.Create();
        Assert.All(field.Props, p =>
            Assert.True(p.Top > field.Terrain.HeightAt(p.Footprint.Center.X, p.Footprint.Center.Y) + 1f, $"rock {p.Id} is buried"));
    }

    [Fact]
    public void DrivingStraightFromSpawn_ClimbsTheBigHill()
    {
        var sim = new Simulation(SandboxMap.Create());
        sim.AddTank(TankCatalog.Medium, SandboxMap.Spawn, SandboxMap.SpawnYaw);
        var input = new[] { TestWorld.Cmd(throttle: 1) };
        float highest = 0f;
        for (int i = 0; i < 20 * SimConstants.TickRate; i++)
        {
            sim.Step(input);
            Vector2 p = sim.Tanks[0].Position;
            highest = MathF.Max(highest, sim.Field.Terrain.HeightAt(p.X, p.Y));
        }
        Assert.True(highest > 18f, $"only reached {highest}m");
        Assert.All(sim.Field.Props.Where(p => p.IsWall || p.Kind == PropKind.Fence),
            p => Assert.True(sim.Field.IsStanding(p.Id), $"{p.Kind} {p.Id} was knocked down on the way"));
    }

    [Fact]
    public void Terrain_HasSlopesSteeperThanTheClimbLimit()
    {
        var terrain = SandboxMap.Create().Terrain;
        float steepest = 0f;
        for (float z = 0; z <= terrain.SizeZ; z += 2f)
        for (float x = 0; x <= terrain.SizeX; x += 2f)
            steepest = MathF.Max(steepest, MathF.Acos(terrain.NormalAt(x, z).Y));
        Assert.True(steepest > TankCatalog.Medium.MaxClimb, $"steepest {steepest} rad");
    }

    private static readonly Vector2 BigHill = new(200, 150);

    private static float DistanceToLane(Vector2 p)
    {
        Vector2 ab = BigHill - SandboxMap.Spawn;
        float t = Math.Clamp(Vector2.Dot(p - SandboxMap.Spawn, ab) / ab.LengthSquared(), 0f, 1f);
        return Vector2.Distance(p, SandboxMap.Spawn + ab * t);
    }

    private static Prop[] Bushes(Battlefield field) => field.Props.Where(p => p.Kind == PropKind.Bush).ToArray();

    [Fact]
    public void Twelve_bush_patches_of_sixteen_to_twenty_two_bushes()
    {
        Battlefield field = SandboxMap.Create();
        Assert.Equal(12, SandboxMap.BushPatches.Count);
        foreach (Vector2 patch in SandboxMap.BushPatches)
        {
            Prop[] members = Bushes(field).Where(b => Vector2.Distance(b.Footprint.Center, patch) <= SandboxMap.BushPatchRadius).ToArray();
            int wanted = SandboxMap.WantedBushes(SandboxMap.BushPatches.ToList().IndexOf(patch));
            Assert.InRange(wanted, 16, 22);
            Assert.True(members.Length >= 0.9f * wanted && members.Length <= wanted, $"patch {patch}: {members.Length} of {wanted} planted");
            float width = members.Max(a => members.Max(b => Vector2.Distance(a.Footprint.Center, b.Footprint.Center) + a.Footprint.HalfExtents.X + b.Footprint.HalfExtents.X));
            Assert.InRange(width, 16f, 34f);
            float minCentre = patch == SandboxMap.BushPatches[0] ? 4.0f : 3.8f;   // the spawn patch gets a roomier centre bush
            Assert.Contains(members, b => b.Footprint.Center == patch && b.Footprint.HalfExtents.X >= minCentre);
        }
        Assert.Equal(Bushes(field).Length, SandboxMap.BushPatches.Sum(patch => Bushes(field).Count(b => Vector2.Distance(b.Footprint.Center, patch) <= SandboxMap.BushPatchRadius)));
    }

    [Fact]
    public void Bush_patches_are_spread_out_over_the_map()
    {
        IReadOnlyList<Vector2> patches = SandboxMap.BushPatches;
        foreach (Vector2 a in patches)
            Assert.All(patches.Where(b => b != a), b => Assert.True(Vector2.Distance(a, b) >= 45f, $"patches {a} and {b} are crowded"));
        // Every quarter of the map has some cover.
        Assert.True(patches.Any(p => p.X < 200 && p.Y < 200) && patches.Any(p => p.X >= 200 && p.Y < 200)
                    && patches.Any(p => p.X < 200 && p.Y >= 200) && patches.Any(p => p.X >= 200 && p.Y >= 200));
    }

    [Fact]
    public void Bush_patch_centres_are_flat_for_thirteen_metres_around()
    {
        var terrain = SandboxMap.Create().Terrain;
        foreach (Vector2 patch in SandboxMap.BushPatches)
            for (float dx = -13; dx <= 13; dx += 1f)
            for (float dz = -13; dz <= 13; dz += 1f)
            {
                if (dx * dx + dz * dz > 13 * 13) continue;
                float slope = MathF.Acos(terrain.NormalAt(patch.X + dx, patch.Y + dz).Y) * 180f / MathF.PI;
                Assert.True(slope <= 10f, $"patch {patch}: {slope:F1}° at offset ({dx}, {dz})");
            }
    }

    private static Prop[] Rocks(Battlefield field) => field.Props.Where(p => p.Kind == PropKind.Rock).ToArray();

    private const float RockSink = 1f;

    private static float BoundingRadius(Prop p) => p.Footprint.HalfExtents.Length();

    [Fact]
    public void There_are_at_least_eighty_rocks_and_most_stand_taller_than_a_tank()
    {
        Prop[] rocks = Rocks(SandboxMap.Create());
        Assert.True(rocks.Length >= 80, $"only {rocks.Length} rocks");
        int tall = rocks.Count(r => r.Height - RockSink >= 2.8f);
        Assert.True(tall >= 0.7f * rocks.Length, $"only {tall} of {rocks.Length} rocks are 2.8 m or taller");
        Assert.All(rocks, r =>
        {
            Assert.InRange(r.Height - RockSink, 2.5f, 6f);
            Assert.InRange(r.Footprint.HalfExtents.X, 2f, 4.5f);
            Assert.InRange(r.Footprint.HalfExtents.Y, 2f, 5.5f);
        });
    }

    [Fact]
    public void No_rock_intrudes_on_the_spawn_to_hill_lane()
    {
        Assert.All(Rocks(SandboxMap.Create()), r =>
            Assert.True(DistanceToLane(r.Footprint.Center) - BoundingRadius(r) >= 6f, $"rock {r.Id} narrows the lane"));
    }

    [Fact]
    public void Rocks_leave_room_for_the_patch_centres_and_never_stack()
    {
        Prop[] rocks = Rocks(SandboxMap.Create());
        Assert.All(rocks, r =>
        {
            Assert.True(Vector2.Distance(r.Footprint.Center, SandboxMap.Spawn) >= 30f, $"rock {r.Id} crowds the spawn");
            foreach (Vector2 patch in SandboxMap.BushPatches)
                Assert.True(Vector2.Distance(r.Footprint.Center, patch) - BoundingRadius(r) >= 8f, $"rock {r.Id} sits on patch {patch}");
            foreach (Prop other in rocks.Where(o => o.Id != r.Id))
                Assert.True(Vector2.Distance(r.Footprint.Center, other.Footprint.Center) >= 0.5f * (BoundingRadius(r) + BoundingRadius(other)),
                    $"rocks {r.Id} and {other.Id} overlap too much");
        });
    }

    [Fact]
    public void A_patch_with_no_room_for_its_centre_bush_is_a_map_error()
    {
        // The boulder's near face is 3.9 m from the centre: room for a 3.8 m bush, but the spawn patch insists on 4.0 m.
        var boulder = new Prop(1, PropKind.Rock, new Obb2(SandboxMap.BushPatches[0] + new Vector2(7.9f, 0f), new Vector2(4f, 4f), 0f), 0f, 3f);
        var error = Assert.Throws<InvalidOperationException>(() => SandboxMap.PlantBushes(SandboxMap.GenerateTerrain(), new List<Prop> { boulder }));
        Assert.Contains("patch 0", error.Message);
        Assert.Contains("r = 4.0", error.Message);
    }

    [Fact]
    public void Bushes_are_taller_than_a_tank_and_round()
    {
        Battlefield field = SandboxMap.Create();
        Assert.All(Bushes(field), b =>
        {
            Assert.InRange(b.Footprint.HalfExtents.X, 3.2f, 4.6f);
            Assert.Equal(b.Footprint.HalfExtents.X, b.Footprint.HalfExtents.Y);
            float ground = field.Terrain.HeightAt(b.Footprint.Center.X, b.Footprint.Center.Y);
            Assert.InRange(b.Top - ground, 4.2f, 5.2f);
            Assert.True(b.Top - ground > TankCatalog.Medium.TrunnionHeight + 0.5f, "a bush hides the whole tank");
            Assert.False(b.IsSolid);
        });
    }

    [Fact]
    public void Bushes_grow_on_flat_ground_clear_of_rocks_the_spawn_and_the_lane()
    {
        Battlefield field = SandboxMap.Create();
        Prop[] rocks = field.Props.Where(p => p.Kind == PropKind.Rock).ToArray();
        Assert.All(Bushes(field), b =>
        {
            Vector2 c = b.Footprint.Center;
            float r = b.Footprint.HalfExtents.X;
            Assert.True(MathF.Acos(field.Terrain.NormalAt(c.X, c.Y).Y) < 12f * MathF.PI / 180f, $"bush {b.Id} on a slope");
            Assert.All(rocks, rock => Assert.False(Obb2.TryGetPenetration(b.Footprint, rock.Footprint, out _), $"bush {b.Id} overlaps rock {rock.Id}"));
            Assert.True(DistanceToLane(c) - r >= 6f, $"bush {b.Id} narrows the spawn-to-hill lane");
            Assert.True(Vector2.Distance(c, SandboxMap.Spawn) - r >= 20f, $"bush {b.Id} crowds the spawn");
            Assert.Equal(c, field.ClampToPlayable(c));
        });
    }

    [Fact]
    public void The_nearest_bush_patch_is_beside_the_lane_about_forty_metres_out()
    {
        Vector2 nearest = SandboxMap.BushPatches.MinBy(p => Vector2.Distance(p, SandboxMap.Spawn));
        Assert.InRange(Vector2.Distance(nearest, SandboxMap.Spawn), 35f, 45f);
        Assert.InRange(DistanceToLane(nearest), 12f, 30f);
    }

    [Fact]
    public void Bushes_come_after_the_rocks_so_rock_ids_are_unchanged()
    {
        Battlefield field = SandboxMap.Create();
        int lastRock = field.Props.Where(p => p.Kind == PropKind.Rock).Max(p => p.Id);
        Assert.All(Bushes(field), b => Assert.True(b.Id > lastRock));
        Assert.Equal(Bushes(field), Bushes(SandboxMap.Create()));
    }

    private const float CoreScale = 0.75f;

    private static (Vector2 C, float R)[] Discs(Battlefield field, Vector2 patch) =>
        Bushes(field).Where(b => Vector2.Distance(b.Footprint.Center, patch) <= SandboxMap.BushPatchRadius)
            .Select(b => (b.Footprint.Center, b.Footprint.HalfExtents.X * CoreScale)).ToArray();

    [Fact]
    public void Every_patch_has_a_pose_that_hides_the_hull_from_all_eight_directions()
    {
        Battlefield field = SandboxMap.Create();
        foreach (Vector2 patch in SandboxMap.BushPatches)
        {
            var (best, pos, _) = BushCoverage.BestHide(Discs(field, patch), patch, 0.95f);
            Assert.True(best >= 0.95f, $"patch at {patch}: best worst-direction cover {best:P0} at {pos}");
        }
    }

    [Fact]
    public void A_tank_at_the_patch_centre_is_hidden_whatever_its_yaw()
    {
        Battlefield field = SandboxMap.Create();
        foreach (Vector2 patch in SandboxMap.BushPatches)
        {
            var discs = Discs(field, patch);
            foreach (float yaw in BushCoverage.Yaws)
            {
                float worst = BushCoverage.WorstOverDirections(discs, patch, yaw);
                Assert.True(worst >= 0.9f, $"patch at {patch}, yaw {yaw}: only {worst:P0} covered");
            }
        }
    }
}
