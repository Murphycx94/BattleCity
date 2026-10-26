using System.Numerics;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;
using Tank.Sim.Vision;

namespace Tank.Sim.Tests;

/// <summary>
/// Houses (riverland design §2.5, §4.3) on flat ground: 12 × 7 m and 7.5 m to the ridge, centred on (100, 60), its front
/// facing south. Its south face is at z = 63.5, its north face at z = 56.5, its ends at x = 94 and 106.
/// </summary>
public sealed class HouseTests
{
    internal static readonly Vector2 HouseAt = new(100, 60);
    internal const float SouthFace = 63.5f, NorthFace = 56.5f, EastEnd = 106f;
    private static readonly InputCmd Full = TestWorld.Cmd(throttle: 1);

    /// <summary>
    /// A house <paramref name="length"/> × <paramref name="width"/> m along its ridge and across, <paramref name="height"/>
    /// m to the ridge, its front (door) facing <paramref name="yaw"/>'s forward; like every planted prop it starts 0.3 m
    /// under the ground.
    /// </summary>
    internal static Prop House(int id, Vector2 at, float yaw = MathF.PI, float length = 12f, float width = 7f, float height = 7.5f) =>
        new(id, PropKind.House, new Obb2(at, new Vector2(length / 2, width / 2), yaw), -WallLine.Sink, height + WallLine.Sink);

    private static Prop TheHouse => House(1, HouseAt);

    [Fact]
    public void A_house_is_masonry()
    {
        Prop house = TheHouse;
        Assert.True(house.IsWall && house.StopsShells && house.IsSolid);
        Assert.False(house.IsFlimsy || house.IsRound || house.IsFoliage);
        Assert.Equal(500, house.MaxDurability);
        Assert.Equal(new Vector2(6.5f, 4f), house.RubbleFootprint.HalfExtents);
        Assert.Equal(house.Footprint with { HalfExtents = new Vector2(6.5f, 4f) }, house.RubbleFootprint);
        Assert.Equal((byte)5, (byte)PropKind.House);

        Prop brick = new WallLine(PropKind.BrickWall, new Vector2(98, 100), new Vector2(100, 100)).Segments(TestWorld.Flat(), 2).Single();
        Assert.Equal(brick.Footprint.HalfExtents + new Vector2(0, Prop.RubbleSpread), brick.RubbleFootprint.HalfExtents);
    }

    [Theory]
    [InlineData("light", AmmoType.AP, 8)]
    [InlineData("medium", AmmoType.AP, 5)]
    [InlineData("heavy", AmmoType.AP, 4)]
    [InlineData("td", AmmoType.AP, 3)]
    [InlineData("light", AmmoType.HE, 3)]
    [InlineData("medium", AmmoType.HE, 2)]
    [InlineData("heavy", AmmoType.HE, 2)]
    [InlineData("td", AmmoType.HE, 2)]
    public void Shots_to_bring_a_house_down_at_average_damage(string tank, AmmoType ammo, int shots)
    {
        TankSpec spec = TankCatalog.ById(tank);
        ShellSpec round = ammo == AmmoType.AP ? spec.Ap : spec.He;
        float average = round.Damage * (ammo == AmmoType.AP ? WallDamage.ApShare : 1f);
        Assert.Equal(shots, (int)MathF.Ceiling(TheHouse.MaxDurability / average));
    }

    [Fact]
    public void Medium_AP_brings_a_house_down_shot_by_shot()
    {
        // The shooter 40 m south of the front, a target 10 m behind the house, in line: every shell must stop on the house.
        var sim = new Simulation(TestWorld.FlatField(TheHouse));
        int shooter = sim.AddTank(TankCatalog.Medium, new Vector2(100, SouthFace + 40f), 0f);
        int target = sim.AddTank(TankCatalog.Medium, new Vector2(100, NorthFace - 10f), 0f);
        InputCmd aim = AimFrom(sim, 0, new Vector3(100, 1.2f, NorthFace - 10f));
        var hold = new InputCmd(0, 0, 0, 0f, 0f, false);
        int reload = (int)MathF.Ceiling(TankCatalog.Medium.ReloadTime * SimConstants.TickRate) + 1;

        int shots = 0, total = 0, damagedTick = -1, destroyedTick = -1;
        var all = new List<GameEvent>();
        while (sim.Field.IsStanding(1) && shots < 8)
        {
            shots++;
            for (int tick = 0; tick < reload; tick++)
            {
                sim.Step(new[] { aim with { Fire = tick == 0 }, hold });
                if (damagedTick == sim.Tick - 1) Assert.Equal(0, sim.ShellCount);   // the shell stopped on the house
                foreach (GameEvent e in sim.Events)
                {
                    all.Add(e);
                    if (e.Type == GameEventType.PropDestroyed) destroyedTick = sim.Tick;
                    if (e.Type != GameEventType.PropDamaged) continue;
                    Assert.Equal((1, shooter), (e.PropId, e.TankId));
                    Assert.NotEqual(Vector3.Zero, e.Velocity);   // a direct hit
                    Assert.True(total < 500, "hit again after it fell");
                    total += e.Damage;
                    damagedTick = sim.Tick;
                }
            }
            Assert.Equal(shots, all.Count(e => e.Type == GameEventType.PropDamaged));   // one per shot
        }

        Assert.False(sim.Field.IsStanding(1));
        Assert.InRange(shots, 4, 7);
        Assert.True(total >= 500, $"{total} damage in all");
        Assert.True(total - all.Last(e => e.Type == GameEventType.PropDamaged).Damage < 500, "it fell to the first shot that took it to 500");
        GameEvent down = Assert.Single(all, e => e.Type == GameEventType.PropDestroyed);
        Assert.Equal((damagedTick, 1, shooter), (destroyedTick, down.PropId, down.TankId));
        Assert.DoesNotContain(all, e => e.Type == GameEventType.TankHit && e.TargetTankId == target);
    }

    [Fact]
    public void HE_splash_hurts_a_house_like_a_wall()
    {
        // A rock slab 2 m in front of the house (its faces at z = 65.5 and 66), the shooter 40 m further south.
        var rock = new Prop(2, PropKind.Rock, new Obb2(new Vector2(100, 65.75f), new Vector2(1.5f, 0.25f), 0), -0.3f, 1.8f);
        var sim = new Simulation(TestWorld.FlatField(TheHouse, rock));
        sim.AddTank(TankCatalog.Medium, new Vector2(100, 106f), 0f);
        InputCmd aim = AimFrom(sim, 0, new Vector3(100, 0.8f, 66f), AmmoType.HE);
        var events = new List<GameEvent>();
        for (int tick = 0; tick < 15; tick++)
        {
            sim.Step(new[] { aim with { Fire = tick == 0 } });
            events.AddRange(sim.Events.ToArray());
        }

        GameEvent burst = Assert.Single(events, e => e.Type == GameEventType.ShellExploded);
        Assert.True(Vector3.Distance(burst.Position, new Vector3(100, 0.8f, 66f)) < 0.5f, $"burst at {burst.Position}");
        GameEvent splash = Assert.Single(events, e => e.Type == GameEventType.PropDamaged);
        Assert.Equal(1, splash.PropId);
        Assert.Equal(Vector3.Zero, splash.Velocity);
        Assert.Equal(WallDamage.Splash(TankCatalog.Medium.He, TheHouse.DistanceTo(burst.Position)), splash.Damage);
        Assert.True(splash.Damage > 0);
        Assert.Equal(500 - splash.Damage, sim.Field.Durability(1));
    }

    [Fact]
    public void A_standing_house_stops_rays_and_sight_and_its_ruin_does_not()
    {
        Battlefield field = TestWorld.FlatField(TheHouse);
        var from = new Vector3(100, 2f, 80f);
        var to = new Vector3(100, 2f, 40f);
        Assert.True(field.Raycast(from, to, out RayHit hit));
        Assert.Equal(1, hit.PropId);
        Assert.Equal(SouthFace, hit.Point.Z, 1e-3f);

        // The observer 36.5 m north of the house, the target 80 m behind it (south): 123.5 m apart, within its 168 m.
        var observer = new TankState { Id = 1, Spec = TankCatalog.Medium, Position = new Vector2(100, 20f), Yaw = MathF.PI, LastFired = TankState.NeverFired };
        var target = new TankState { Id = 2, Spec = TankCatalog.Light, Team = 1, Position = new Vector2(100, SouthFace + 80f), LastFired = TankState.NeverFired };
        Assert.False(Spotting.Look(observer, target, field, 1000, targetMoving: false).LineClear);

        field.SetDurability(1, 0);
        Assert.False(field.IsStanding(1));
        Assert.False(field.Raycast(from, to, out _));
        Sighting look = Spotting.Look(observer, target, field, 1000, targetMoving: false);
        Assert.True(look.LineClear);
        Assert.True(look.Seen);
    }

    [Fact]
    public void Driving_on_a_ruin_is_slowed_like_rubble()
    {
        float slowed = TankCatalog.Medium.MaxForwardSpeed * TankMotion.RubbleSpeedFactor;
        Assert.Equal(35f / 3.6f, slowed, 1e-4f);
        // Through the middle, east along the ridge.
        Assert.Equal(slowed, SpeedOverTheRuin(new Vector2(60, HouseAt.Y), -MathF.PI / 2), 1e-4f);
        // East along the front with the hull's edge 25 cm past the face: only on the half metre the ruin spreads.
        Assert.Equal(slowed, SpeedOverTheRuin(new Vector2(60, SouthFace + 0.25f + 1.6f), -MathF.PI / 2), 1e-4f);
        // North past the east end, likewise only on its spread: a house's ruin spreads at its ends too, unlike a wall's.
        Assert.Equal(slowed, SpeedOverTheRuin(new Vector2(EastEnd + 0.25f + 1.6f, 100), 0f), 1e-4f);
    }

    /// <summary>
    /// A medium tank from <paramref name="start"/> flat out along <paramref name="yaw"/> over the house shot to rubble: its
    /// speed on the last tick it starts over the ruin, having reached its top speed before it got there.
    /// </summary>
    private static float SpeedOverTheRuin(Vector2 start, float yaw)
    {
        Battlefield field = TestWorld.FlatField(TheHouse);
        field.SetDurability(1, 0);
        TankState tank = TestWorld.Tank(start.X, start.Y, yaw);
        Obb2 ruin = TheHouse.RubbleFootprint;
        bool wasOn = false, left = false;
        float topBefore = 0f, last = float.NaN;
        for (int i = 0; i < 10 * SimConstants.TickRate && !left; i++)
        {
            bool on = Obb2.TryGetPenetration(TankMotion.Footprint(tank.Spec, tank.Position, tank.Yaw), ruin, out _);
            if (!on && !wasOn) topBefore = tank.Speed;
            TankMotion.Drive(ref tank, Full, field, ReadOnlySpan<Obb2>.Empty);
            if (on) last = tank.Speed;
            left = wasOn && !on;
            wasOn |= on;
        }
        Assert.True(left, "the run crosses the ruin");
        Assert.Equal(tank.Spec.MaxForwardSpeed, topBefore, 1e-4f);
        return last;
    }

    [Fact]
    public void A_barrel_pushed_into_a_house_strikes_its_face()
    {
        // Nose 5 cm off the front: the medium's muzzle reaches 0.3 + 3.2 m past its centre, 0.2 m into the house.
        TankSpec spec = TankCatalog.Medium;
        var sim = new Simulation(TestWorld.FlatField(TheHouse));
        sim.AddTank(spec, new Vector2(100, SouthFace + 0.05f + spec.HullLength / 2), 0f);
        var hold = new InputCmd(0, 0, 0, 0f, 0f, false);
        for (int tick = 0; tick < 60; tick++) sim.Step(new[] { hold });
        Ballistics.Launch launch = Ballistics.Aim(sim.Tanks[0], sim.Field);
        Assert.True(launch.Blocked && launch.BlockPropId == 1, "the barrel is in the house");

        sim.Step(new[] { hold with { Fire = true } });
        GameEvent hurt = Assert.Single(sim.Events.ToArray(), e => e.Type == GameEventType.PropDamaged);
        Assert.Equal(1, hurt.PropId);
        Assert.NotEqual(Vector3.Zero, hurt.Velocity);
        Assert.Equal(SouthFace, hurt.Position.Z, 1e-2f);
        Assert.InRange(hurt.Damage, 83, 138);   // 220 × 0.75 … 1.25 × 0.5
        Assert.Equal(0, sim.ShellCount);
    }

    [Fact]
    public void Restore_skips_a_house_with_a_tank_on_it()
    {
        var sim = new Simulation(TestWorld.FlatField(TheHouse, House(2, new Vector2(40, 140))));
        sim.Field.SetDurability(1, 0);
        sim.Field.SetDurability(2, 0);
        sim.AddTank(TankCatalog.Medium, HouseAt, 0f);

        sim.RestoreProps();

        Assert.Equal((false, 0), (sim.Field.IsStanding(1), sim.Field.Durability(1)));
        Assert.Equal((true, 500), (sim.Field.IsStanding(2), sim.Field.Durability(2)));
    }

    [Fact]
    public void Houses_are_in_the_masonry_blocker_and_stopper_sets()
    {
        Prop rock = new(1, PropKind.Rock, new Obb2(new Vector2(40, 40), new Vector2(1, 1), 0), -0.3f, 2f);
        Battlefield field = TestWorld.FlatField(rock, House(2, HouseAt));
        int house = field.IndexOf(2);
        Assert.Contains(house, field.All(PropSet.Walls).ToArray());
        Assert.Contains(house, field.All(PropSet.Blockers).ToArray());
        Assert.Contains(house, field.All(PropSet.ShellStoppers).ToArray());
        Assert.DoesNotContain(house, field.All(PropSet.Flimsy).ToArray());
        Assert.DoesNotContain(house, field.All(PropSet.Foliage).ToArray());
    }

    [Fact]
    public void Shooting_houses_allocates_nothing_once_warmed_up()
    {
        Allocation.AssertNone(() =>
        {
            // Four tanks, two firing AP and two HE, into a row of three houses; every second they are put back, worn down to
            // 100 hit points, so the measured ticks keep bringing them down.
            Prop[] houses = { House(1, new Vector2(80, 100)), House(2, new Vector2(100, 100)), House(3, new Vector2(120, 100)) };
            var sim = new Simulation(TestWorld.FlatField(houses));
            for (int k = 0; k < 4; k++) sim.AddTank(Duel.Spec, new Vector2(85 + 10 * k, 150), 0f);
            var inputs = new InputCmd[4];
            for (int k = 0; k < 4; k++) inputs[k] = new InputCmd(0, 0, 0, 0f, -0.02f, true, k % 2 == 0 ? AmmoType.AP : AmmoType.HE);

            int hurt = 0, down = 0;
            void Run(int ticks)
            {
                for (int tick = 0; tick < ticks; tick++)
                {
                    sim.Step(inputs);
                    foreach (GameEvent e in sim.Events)
                    {
                        if (e.Type == GameEventType.PropDamaged) hurt++;
                        if (e.Type == GameEventType.PropDestroyed) down++;
                    }
                    if (sim.Tick % SimConstants.TickRate != 0) continue;
                    for (int i = 0; i < sim.Field.Props.Count; i++)
                    {
                        sim.Field.Restore(i);
                        sim.Field.Damage(i, 400);
                    }
                }
            }
            Run(12 * SimConstants.TickRate);   // past the first HE reload and a couple of shots each
            (hurt, down) = (0, 0);
            Allocation.Window window = Allocation.Start();
            Run(10 * SimConstants.TickRate);   // 300 ticks
            Allocation.Measured measured = window.Stop();
            Assert.True(hurt >= 6 && down >= 3, $"{hurt} houses hurt and {down} down while measured");
            return measured;
        });
    }

    /// <summary>
    /// Lays slot <paramref name="slot"/>'s gun on <paramref name="point"/> with <paramref name="ammo"/> loaded (two seconds
    /// to settle, and the reload a change of round starts); the other tanks hold their guns ahead.
    /// </summary>
    private static InputCmd AimFrom(Simulation sim, int slot, Vector3 point, AmmoType ammo = AmmoType.AP)
    {
        TankState shooter = sim.Tanks[slot];
        TankPose pose = TankPose.Compute(shooter, sim.Field.Terrain);
        var (yaw, pitch) = Angles.AimAngles(point - pose.Trunnion(shooter.Spec));
        var aim = new InputCmd(0, 0, 0, yaw, pitch, false, ammo);
        int settle = 2 * SimConstants.TickRate + (ammo == AmmoType.HE ? (int)MathF.Ceiling(shooter.Spec.ReloadTime * SimConstants.TickRate) : 0);
        var inputs = new InputCmd[sim.TankCount];
        for (int k = 0; k < inputs.Length; k++) inputs[k] = k == slot ? aim : new InputCmd(0, 0, 0, sim.Tanks[k].Yaw, 0f, false);
        for (int tick = 0; tick < settle; tick++) sim.Step(inputs);
        return aim;
    }
}
