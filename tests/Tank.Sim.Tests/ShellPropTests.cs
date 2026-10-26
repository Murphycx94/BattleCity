using System.Numerics;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Terrain;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Tests;

/// <summary>Shells against walls, fences and bushes, fired across a <see cref="Duel"/> (shooter at z = 150, target at z = 100).</summary>
public sealed class ShellPropTests
{
    private static readonly Vector3 HullFront = new(0, 1.02f, -3.1f);
    private static readonly int ReloadTicks = (int)MathF.Round(Duel.Spec.ReloadTime * SimConstants.TickRate);

    /// <summary>A three-segment line across the line of fire at <paramref name="z"/>, x = 97 … 103: ids 1, 2, 3 (2 is in the way).</summary>
    private static Prop[] Line(PropKind kind, float z, int firstId = 1) =>
        new WallLine(kind, new Vector2(97, z), new Vector2(103, z)).Segments(TestWorld.Flat(), firstId).ToArray();

    private static Prop Bush(int id, float x, float z, float radius) =>
        new(id, PropKind.Bush, new Obb2(new Vector2(x, z), new Vector2(radius, radius), 0), -0.2f, 4f);

    private static (Duel Duel, InputCmd Aim) Aimed(AmmoType ammo, params Prop[] props)
    {
        var duel = new Duel(targetYaw: MathF.PI, props);
        InputCmd aim = duel.AimAt(duel.OnTarget(HullFront), ammo);
        if (ammo == AmmoType.HE) duel.Run(aim, ReloadTicks);   // swapping to HE restarts the reload
        return (duel, aim);
    }

    [Fact]
    public void AP_on_a_wall_takes_half_its_rolled_damage_and_stops_there()
    {
        var (duel, aim) = Aimed(AmmoType.AP, Line(PropKind.BrickWall, 125));
        var events = duel.Fire(aim);

        GameEvent hurt = Assert.Single(events, e => e.Type == GameEventType.PropDamaged);
        Assert.Equal(2, hurt.PropId);
        Assert.Equal(duel.ShooterId, hurt.TankId);
        Assert.InRange(hurt.Damage, 83, 138);   // 220 × 0.75 … 1.25 × 0.5
        Assert.Equal(125.3f, hurt.Position.Z, 1e-2f);   // the wall's near face
        Assert.True(hurt.Velocity.Z < -700f, "a direct hit carries the shell's velocity");
        Assert.Equal(300 - hurt.Damage, duel.Sim.Field.Durability(2));
        Assert.Equal(hurt.Position, Assert.Single(events, e => e.Type == GameEventType.ShellExploded).Position);
        Assert.DoesNotContain(events, e => e.Type == GameEventType.TankHit);
        Assert.Equal(Duel.Spec.MaxHealth, duel.Target.Health);
    }

    [Fact]
    public void HE_on_a_wall_takes_its_full_rolled_damage_then_bursts_and_splashes_the_segments_beside_it()
    {
        var (duel, aim) = Aimed(AmmoType.HE, Line(PropKind.StoneWall, 125));
        var events = duel.Fire(aim);

        GameEvent[] hurt = events.Where(e => e.Type == GameEventType.PropDamaged).ToArray();
        Assert.Equal(new[] { 2, 1, 3 }, hurt.Select(e => e.PropId));   // the struck wall first, then the splash in prop order
        Assert.InRange(hurt[0].Damage, 210, 350);                       // 280 × 0.75 … 1.25
        GameEvent burst = Assert.Single(events, e => e.Type == GameEventType.ShellExploded);
        Assert.True(events.IndexOf(hurt[0]) < events.IndexOf(burst) && events.IndexOf(burst) < events.IndexOf(hurt[1]));
        foreach (GameEvent splash in hurt.Skip(1))
        {
            Prop wall = duel.Sim.Field.PropById(splash.PropId);
            Assert.Equal(burst.Position, splash.Position);
            Assert.Equal(Vector3.Zero, splash.Velocity);   // a splash, not a direct hit
            Assert.Equal(WallDamage.Splash(Duel.Spec.He, wall.DistanceTo(burst.Position)), splash.Damage);
            Assert.InRange(splash.Damage, 1, hurt[0].Damage - 1);
        }
        Assert.Equal(900 - hurt[0].Damage, duel.Sim.Field.Durability(2));
    }

    [Fact]
    public void Enough_hits_turn_a_wall_to_rubble_and_the_next_shell_flies_over_it_into_the_tank()
    {
        var (duel, aim) = Aimed(AmmoType.AP, Line(PropKind.BrickWall, 125));
        List<GameEvent> events = new();
        for (int shot = 0; shot < 6 && duel.Sim.Field.IsStanding(2); shot++)
        {
            events = duel.Fire(aim);
            duel.Run(aim, ReloadTicks);
        }

        GameEvent down = Assert.Single(events, e => e.Type == GameEventType.PropDestroyed);
        GameEvent last = Assert.Single(events, e => e.Type == GameEventType.PropDamaged);
        GameEvent burst = Assert.Single(events, e => e.Type == GameEventType.ShellExploded);
        Assert.True(events.IndexOf(last) < events.IndexOf(down) && events.IndexOf(down) < events.IndexOf(burst), "damaged, destroyed, then the burst");
        Assert.Equal((2, last.ShellId, duel.ShooterId), (down.PropId, down.ShellId, down.TankId));
        Assert.Equal(new Vector3(0, 0, -1), down.Velocity, new Vec3Comparer(0.05f));   // pushed the way the shell flew (north)
        Assert.Equal(0, duel.Sim.Field.Durability(2));

        var through = duel.Fire(aim);
        Assert.Contains(through, e => e.Type == GameEventType.TankHit && e.TargetTankId == duel.TargetId);
        Assert.DoesNotContain(through, e => e.Type is GameEventType.PropDamaged or GameEventType.PropDestroyed);
    }

    [Fact]
    public void A_shell_knocks_a_fence_flat_on_its_way_and_still_hits_the_tank()
    {
        var (duel, aim) = Aimed(AmmoType.AP, Line(PropKind.Fence, 110));
        var events = duel.Fire(aim);

        GameEvent down = Assert.Single(events, e => e.Type == GameEventType.PropDestroyed);
        Assert.Equal((2, duel.ShooterId), (down.PropId, down.TankId));
        Assert.Equal(110f, down.Position.Z, 0.15f);
        Assert.Equal(new Vector3(0, 0, -1), down.Velocity, new Vec3Comparer(0.05f));   // falls the way the shell flew (north)
        Assert.DoesNotContain(events, e => e.Type == GameEventType.PropDamaged);      // a fence is only ever knocked down
        GameEvent hit = Assert.Single(events, e => e.Type == GameEventType.TankHit);
        Assert.True(events.IndexOf(down) < events.IndexOf(hit));
        Assert.False(duel.Sim.Field.IsStanding(2));
        Assert.True(duel.Sim.Field.IsStanding(1) && duel.Sim.Field.IsStanding(3));
    }

    [Fact]
    public void A_shell_shoots_away_a_bush_it_flies_through()
    {
        var (duel, aim) = Aimed(AmmoType.AP, Bush(1, 100, 115, 3f), Bush(2, 110, 115, 3f));
        var events = duel.Fire(aim);

        GameEvent down = Assert.Single(events, e => e.Type == GameEventType.PropDestroyed);
        Assert.Equal(1, down.PropId);
        Assert.DoesNotContain(events, e => e.Type == GameEventType.PropDamaged);   // a bush is only ever shot away
        Assert.Contains(events, e => e.Type == GameEventType.TankHit);
        Assert.False(duel.Sim.Field.IsStanding(1));
        Assert.True(duel.Sim.Field.IsStanding(2));
    }

    [Fact]
    public void An_HE_burst_flattens_the_fences_and_bushes_within_its_radius()
    {
        // The burst is on the target's front plate at (100, 1.02, 103.1); medium HE splashes 3 m.
        Prop near = new WallLine(PropKind.Fence, new Vector2(102, 104.5f), new Vector2(104, 104.5f)).Segments(TestWorld.Flat(), 1).Single();
        Prop far = new WallLine(PropKind.Fence, new Vector2(106, 104.5f), new Vector2(108, 104.5f)).Segments(TestWorld.Flat(), 2).Single();
        var (duel, aim) = Aimed(AmmoType.HE, near, far, Bush(3, 97, 105, 1f));
        var events = duel.Fire(aim);

        GameEvent burst = Assert.Single(events, e => e.Type == GameEventType.ShellExploded);
        GameEvent[] down = events.Where(e => e.Type == GameEventType.PropDestroyed).ToArray();
        Assert.Equal(new[] { 1, 3 }, down.Select(e => e.PropId));
        Assert.All(down, e => Assert.Equal((burst.Position, AmmoType.HE), (e.Position, e.Ammo)));
        Assert.True(down[0].Velocity.X > 0 && down[1].Velocity.X < 0, "pushed away from the burst");
        Assert.True(duel.Sim.Field.IsStanding(2));
    }

    [Fact]
    public void A_barrel_pushed_into_a_wall_fires_point_blank_into_it()
    {
        // The wall's near face is 3 m ahead of the shooter's trunnion: the barrel pokes into it.
        float face = Duel.ShooterAt.Y - Duel.Spec.TurretOffset - 3f;
        Prop[] wall = new WallLine(PropKind.BrickWall, new Vector2(97, face - 0.3f), new Vector2(103, face - 0.3f)).Segments(TestWorld.Flat(), 1).ToArray();
        var duel = new Duel(targetYaw: MathF.PI, wall);
        var aim = new InputCmd(0, 0, 0, 0f, 0f, false);
        duel.Run(aim, 60);   // two seconds, as Duel.AimAt gives turret, gun and aim circle (here already level, ahead and closed)
        var events = duel.Fire(aim);

        GameEvent hurt = Assert.Single(events, e => e.Type == GameEventType.PropDamaged);
        Assert.Equal(2, hurt.PropId);
        Assert.InRange(hurt.Damage, 83, 138);
        Assert.Equal(face, hurt.Position.Z, 1e-2f);
        Assert.Equal(0, duel.Sim.ShellCount);
    }

    [Fact]
    public void Direct_damage_rolls_like_a_piercing_hit_on_a_tank_and_AP_does_half()
    {
        for (uint seed = 1; seed < 200; seed++)
        {
            // On 1 mm of armour head-on every round pierces, so the tank's damage is just the roll.
            int heOnTank = Penetration.HeDirect(Duel.Spec.He, 1f, 0f, seed).Damage;
            int apOnTank = Penetration.Ap(Duel.Spec.Ap, Duel.Spec.Caliber, 1f, 0f, seed).Damage;
            Assert.Equal(heOnTank, WallDamage.Direct(Duel.Spec.He, AmmoType.HE, seed));
            Assert.InRange(WallDamage.Direct(Duel.Spec.Ap, AmmoType.AP, seed), apOnTank / 2, (apOnTank + 1) / 2);
        }
    }

    [Fact]
    public void A_fence_or_bush_past_where_the_shell_stops_stays_standing()
    {
        // This tick's path reaches past both the target (z ≈ 103) and the wall (z = 125.3), to z ≈ 94.5 and 120.5.
        Prop[] behindTank = Line(PropKind.Fence, 96, firstId: 1);
        var (duel, aim) = Aimed(AmmoType.AP, behindTank.Append(Bush(4, 100, 92.5f, 3f)).ToArray());
        var events = duel.Fire(aim);
        Assert.Contains(events, e => e.Type == GameEventType.TankHit);
        Assert.DoesNotContain(events, e => e.Type == GameEventType.PropDestroyed);

        Prop[] wallThenFence = Line(PropKind.StoneWall, 125, firstId: 1).Concat(Line(PropKind.Fence, 122, firstId: 4)).ToArray();
        (duel, aim) = Aimed(AmmoType.AP, wallThenFence);
        events = duel.Fire(aim);
        Assert.Contains(events, e => e.Type == GameEventType.PropDamaged && e.PropId == 2);
        Assert.DoesNotContain(events, e => e.Type == GameEventType.PropDestroyed);
    }

    [Fact]
    public void A_tank_firing_from_inside_a_bush_keeps_its_cover()
    {
        var (duel, aim) = Aimed(AmmoType.AP, Bush(1, Duel.ShooterAt.X, Duel.ShooterAt.Y, 4.2f), Bush(2, 100, 115, 3f));
        var events = duel.Fire(aim);
        Assert.True(duel.Sim.Field.IsStanding(1), "the bush the gun fired from");
        GameEvent down = Assert.Single(events, e => e.Type == GameEventType.PropDestroyed);
        Assert.Equal(2, down.PropId);   // the one it flew through
        Assert.Contains(events, e => e.Type == GameEventType.TankHit);
    }

    [Fact]
    public void A_wall_one_shell_brings_down_lets_the_next_through_in_the_same_tick()
    {
        // One brick segment, worn down to 1 hit point, 2.5 m in front of the target; two shooters either side fire at the
        // target together, both lines crossing the segment.
        Prop wall = new WallLine(PropKind.BrickWall, new Vector2(99, 106), new Vector2(101, 106)).Segments(TestWorld.Flat(), 1).Single();
        var sim = new Simulation(TestWorld.FlatField(wall));
        sim.Field.Damage(0, wall.MaxDurability - 1);
        int left = sim.AddTank(Duel.Spec, new Vector2(96, 150), 0f), right = sim.AddTank(Duel.Spec, new Vector2(104, 150), 0f);
        int target = sim.AddTank(Duel.Spec, Duel.TargetAt, MathF.PI);
        var plate = new Vector3(100, 1.02f, 103.1f);
        Vector3 Trunnion(int slot) => TankPose.Compute(sim.Tanks[slot], sim.Field.Terrain).Trunnion(Duel.Spec);
        InputCmd Aim(int slot, bool fire)
        {
            var (yaw, pitch) = Angles.AimAngles(plate - Trunnion(slot));
            return new InputCmd(0, 0, 0, yaw, pitch, fire);
        }
        Assert.True(Intersect.SegmentPrism(wall.Footprint, wall.Bottom, wall.Top, Trunnion(1), plate, out _),
            "the right-hand shooter's line to the plate crosses the segment, so only its fall lets that shell through");
        var events = new List<GameEvent>();
        for (int tick = 0; tick < 90; tick++)
        {
            bool fire = tick == 60;
            sim.Step(new[] { Aim(0, fire), Aim(1, fire), new InputCmd(0, 0, 0, MathF.PI, 0f, false) });
            events.AddRange(sim.Events);
        }

        GameEvent down = Assert.Single(events, e => e.Type == GameEventType.PropDestroyed);
        Assert.Equal(left, down.TankId);   // the lower slot's shell goes first
        GameEvent hit = Assert.Single(events, e => e.Type == GameEventType.TankHit);
        Assert.Equal((right, target, down.Tick), (hit.TankId, hit.TargetTankId, hit.Tick));
    }

    [Fact]
    public void A_barrel_in_a_wall_that_came_down_this_tick_fires_out_through_the_gap()
    {
        // Two heavy tanks nose to nose across one brick segment, 5 cm off it, both barrels in it; both fire HE on the same
        // tick. The first burst brings the segment down; the second barrel is then clear, so its shell flies out of the
        // muzzle and into the first tank's front.
        TankSpec heavy = TankCatalog.Heavy;
        Prop wall = new WallLine(PropKind.BrickWall, new Vector2(99, 100), new Vector2(101, 100)).Segments(TestWorld.Flat(), 1).Single();
        var sim = new Simulation(TestWorld.FlatField(wall));
        float back = 0.3f + 0.05f + heavy.HullLength / 2;
        Assert.True(back - heavy.TurretOffset - 0.3f < heavy.GunLength, "both barrels reach past the wall's face");
        int north = sim.AddTank(heavy, new Vector2(100, 100 - back), MathF.PI);   // facing south
        int south = sim.AddTank(heavy, new Vector2(100, 100 + back), 0f);         // facing north
        int reload = (int)MathF.Ceiling(heavy.ReloadTime * SimConstants.TickRate);
        var events = new List<GameEvent>();
        for (int tick = 0; tick <= reload + 2; tick++)
        {
            bool fire = tick == reload + 1;   // after the reload that switching to HE starts
            sim.Step(new[] { new InputCmd(0, 0, 0, MathF.PI, 0f, fire, AmmoType.HE), new InputCmd(0, 0, 0, 0f, 0f, fire, AmmoType.HE) });
            events.AddRange(sim.Events);
        }

        Assert.Equal(2, events.Count(e => e.Type == GameEventType.ShellFired));
        GameEvent hurt = Assert.Single(events, e => e.Type == GameEventType.PropDamaged);
        Assert.Equal(north, hurt.TankId);
        GameEvent down = Assert.Single(events, e => e.Type == GameEventType.PropDestroyed);
        Assert.Equal(north, down.TankId);
        GameEvent hit = Assert.Single(events, e => e.Type == GameEventType.TankHit);
        Assert.Equal((south, north), (hit.TankId, hit.TargetTankId));
        Assert.True(events.IndexOf(down) < events.IndexOf(hit));
    }

    [Fact]
    public void Shooting_walls_fences_and_bushes_allocates_nothing_once_warmed_up()
    {
        Allocation.AssertNone(() =>
        {
            // Four tanks, two firing AP and two HE, into a stone wall with fences and bushes before it; everything is put
            // back every second, so the measured ticks keep knocking props down.
            HeightField flat = TestWorld.Flat();
            var props = new List<Prop>();
            props.AddRange(new WallLine(PropKind.StoneWall, new Vector2(90, 100), new Vector2(110, 100)).Segments(flat, 1));
            props.AddRange(new WallLine(PropKind.Fence, new Vector2(90, 110), new Vector2(110, 110)).Segments(flat, props.Count + 1));
            for (int k = 0; k < 4; k++) props.Add(Bush(props.Count + 1, 92 + 5 * k, 120, 2f));
            var sim = new Simulation(TestWorld.Field(flat, props.ToArray()));
            for (int k = 0; k < 4; k++) sim.AddTank(Duel.Spec, new Vector2(94 + 4 * k, 150), 0f);
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
                    if (sim.Tick % SimConstants.TickRate == 0)
                        for (int i = 0; i < sim.Field.Props.Count; i++) sim.Field.Restore(i);
                }
            }
            Run(12 * SimConstants.TickRate);   // past the first HE reload and a couple of shots each
            (hurt, down) = (0, 0);
            Allocation.Window window = Allocation.Start();
            Run(12 * SimConstants.TickRate);
            Allocation.Measured measured = window.Stop();
            Assert.True(hurt >= 6 && down >= 6, $"{hurt} walls hurt and {down} props down while measured");
            return measured;
        });
    }

    [Theory]
    [InlineData(0f, 140)]
    [InlineData(1.5f, 70)]
    [InlineData(2.999f, 0)]
    [InlineData(3f, 0)]
    [InlineData(10f, 0)]
    public void Splash_is_half_damage_fading_to_nothing_at_the_radius(float distance, int damage) =>
        Assert.Equal(damage, WallDamage.Splash(Duel.Spec.He, distance));   // 280, 3 m

    [Fact]
    public void AP_does_not_splash() => Assert.Equal(0, WallDamage.Splash(Duel.Spec.Ap, 0f));

    private sealed class Vec3Comparer(float tolerance) : IEqualityComparer<Vector3>
    {
        public bool Equals(Vector3 a, Vector3 b) => Vector3.Distance(a, b) <= tolerance;
        public int GetHashCode(Vector3 v) => 0;
    }
}
