using System.Numerics;
using Tank.Sim.Ai;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Terrain;
using Tank.Sim.Vehicles;
using Tank.Sim.Vision;

namespace Tank.Sim.Tests;

/// <summary>
/// A bot's choice of enemy (stage 5 design §4.3) on the flat 400 m square: only what its team sees, within 400 m, that the
/// gun reaches and its trunnion can see; the one that hit it lately, then the weakest, the nearest, the lower id; held for
/// two seconds.
/// </summary>
public sealed class TargetPickerTests
{
    private static readonly float East = Angles.YawOf(new Vector2(1, 0));

    /// <summary>Tanks standing still on the 400 m square, the vision kept up after every step.</summary>
    private sealed class Ground
    {
        private readonly InputCmd[] _inputs = new InputCmd[Simulation.MaxTanks];

        public Ground(params Prop[] props)
            : this(Ground400.Flat(), props)
        {
        }

        public Ground(HeightField terrain, params Prop[] props)
        {
            Sim = new Simulation(Ground400.Field(terrain, props));
            Vision = new VisionTracker(Sim);
        }

        public Simulation Sim { get; }
        public VisionTracker Vision { get; }
        public int Tick => Sim.Tick;

        public int Add(float x, float z, byte team, TankSpec? spec = null) => Sim.AddTank(spec ?? TankCatalog.Medium, new Vector2(x, z), East, team);

        /// <summary>Steps <paramref name="ticks"/> times with every tank holding still, the vision updated after each.</summary>
        public void Run(int ticks = 1)
        {
            for (int k = 0; k < ticks; k++)
            {
                ReadOnlySpan<TankState> tanks = Sim.Tanks;
                for (int i = 0; i < tanks.Length; i++)
                    _inputs[i] = new InputCmd(Sim.Tick + 1, 0, 0, Angles.Wrap(tanks[i].Yaw + tanks[i].TurretYaw), tanks[i].GunPitch, false);
                Sim.Step(_inputs.AsSpan(0, tanks.Length));
                Vision.Update();
            }
        }

        public int Pick(TargetPicker picker) => picker.Pick(Sim, Vision, Sim.Tick);
    }

    [Fact]
    public void It_picks_only_enemies_its_team_sees_within_400_m()
    {
        var ground = new Ground();
        int me = ground.Add(20, 200, team: 0);
        int enemy = ground.Add(370, 200, team: 1);   // 350 m: beyond what the bot sees itself
        int mate = ground.Add(370, 140, team: 0);    // 60 m from it
        ground.Run();
        var picker = new TargetPicker(me);
        Assert.Equal(enemy, ground.Pick(picker));
        Assert.Equal(enemy, picker.TargetId);

        ground.Sim.TankRef(mate).Position = new Vector2(20, 380);   // out of sight of it
        ground.Run(VisionTracker.LingerTicks + VisionTracker.UpdateInterval + 1);
        Assert.False(ground.Vision.IsVisible(enemy, 0));
        Assert.Equal(enemy, ground.Pick(picker));   // lost, but kept until the next vision update could bring it back
        Assert.True(picker.TargetLost);
        ground.Run(VisionTracker.UpdateInterval - 1);
        Assert.Equal(enemy, ground.Pick(picker));
        ground.Run();
        Assert.Equal(0, ground.Pick(picker));
        Assert.Equal(0, picker.TargetId);
        Assert.False(picker.TargetLost);

        var far = new Ground();
        int me2 = far.Add(20, 20, team: 0);
        int enemy2 = far.Add(320, 320, team: 1);   // 424 m, corner to corner
        far.Add(320, 260, team: 0);
        far.Run();
        Assert.True(far.Vision.IsVisible(enemy2, 0));
        Assert.Equal(0, far.Pick(new TargetPicker(me2)));
    }

    [Fact]
    public void One_that_hit_me_in_the_last_3_s_comes_first()
    {
        var ground = new Ground();
        int me = ground.Add(50, 200, team: 0);
        int a = ground.Add(200, 180, team: 1);   // about 150 m
        int b = ground.Add(300, 220, team: 1);   // about 250 m
        ground.Add(250, 150, team: 0);           // a team mate that sees them both
        ground.Run(100);
        int tick = ground.Tick;

        Assert.Equal(a, ground.Pick(new TargetPicker(me)));

        var hit = new TargetPicker(me);
        hit.NoteHit(b, tick - 60);
        Assert.Equal(b, ground.Pick(hit));

        var stale = new TargetPicker(me);
        stale.NoteHit(b, tick - TargetPicker.HitMemoryTicks - 1);
        Assert.Equal(a, ground.Pick(stale));

        var edge = new TargetPicker(me);
        edge.NoteHit(b, tick - TargetPicker.HitMemoryTicks);
        Assert.Equal(b, ground.Pick(edge));
    }

    [Fact]
    public void Then_the_lowest_health_share_then_the_nearest_then_the_lower_id()
    {
        var ground = new Ground();
        int me = ground.Add(50, 200, team: 0);
        int a = ground.Add(350, 200, team: 1);   // 300 m
        int b = ground.Add(150, 220, team: 1);   // about 100 m
        ground.Add(250, 150, team: 0);
        ground.Run();
        ground.Sim.TankRef(a).Health = TankCatalog.Medium.MaxHealth / 2;
        ground.Sim.TankRef(b).Health = TankCatalog.Medium.MaxHealth * 8 / 10;
        Assert.Equal(a, ground.Pick(new TargetPicker(me)));

        ground.Sim.TankRef(a).Health = TankCatalog.Medium.MaxHealth * 8 / 10;
        Assert.Equal(b, ground.Pick(new TargetPicker(me)));

        // A share, not hit points: 700 of the heavy's 1400 is weaker than 600 of the medium's 900.
        var mixed = new Ground();
        int me3 = mixed.Add(50, 200, team: 0);
        int medium = mixed.Add(150, 200, team: 1);
        int heavy = mixed.Add(300, 200, team: 1, TankCatalog.Heavy);
        mixed.Add(250, 150, team: 0);
        mixed.Run();
        mixed.Sim.TankRef(medium).Health = 600;
        mixed.Sim.TankRef(heavy).Health = 700;
        Assert.Equal(heavy, mixed.Pick(new TargetPicker(me3)));

        var level = new Ground();
        int me2 = level.Add(50, 200, team: 0);
        int first = level.Add(150, 150, team: 1);
        level.Add(150, 250, team: 1);   // the same distance the other side
        level.Run();
        Assert.Equal(first, level.Pick(new TargetPicker(me2)));
    }

    [Fact]
    public void A_target_is_held_for_2_s_unless_it_is_lost()
    {
        var ground = new Ground();
        int me = ground.Add(50, 200, team: 0);
        int a = ground.Add(150, 200, team: 1);   // the nearer
        int b = ground.Add(250, 220, team: 1);
        ground.Add(200, 150, team: 0);
        ground.Run();
        var picker = new TargetPicker(me);
        Assert.Equal(a, ground.Pick(picker));
        int picked = ground.Tick;

        ground.Run();
        picker.NoteHit(b, ground.Tick);
        for (int k = 1; k < TargetPicker.HoldTicks; k++)
        {
            if (k > 1) ground.Run();
            Assert.Equal(a, ground.Pick(picker));
        }
        ground.Run();
        Assert.Equal(picked + TargetPicker.HoldTicks, ground.Tick);
        Assert.Equal(b, ground.Pick(picker));

        var lost = new TargetPicker(me);
        Assert.Equal(a, ground.Pick(lost));
        ground.Run();
        ground.Sim.TankRef(a).Health = 0;
        ground.Sim.TankRef(a).IsDestroyed = true;
        Assert.Equal(b, ground.Pick(lost));
    }

    [Fact]
    public void A_target_lost_at_the_edge_is_kept_a_moment_and_taken_back_without_a_new_hold()
    {
        // About 400 m off corner to corner, the enemy steps out of range and back every few ticks: the bot keeps it throughout.
        var ground = new Ground();
        Vector2 from = new(20, 20), way = Vector2.Normalize(new Vector2(1, 1));
        int me = ground.Add(from.X, from.Y, team: 0);
        Vector2 inRange = from + way * (TargetPicker.EngageRange - 1), outOfRange = from + way * (TargetPicker.EngageRange + 2);
        int enemy = ground.Add(inRange.X, inRange.Y, team: 1);
        ground.Add(inRange.X, inRange.Y - 60, team: 0);
        ground.Run();
        var picker = new TargetPicker(me);
        Assert.Equal(enemy, ground.Pick(picker));
        for (int k = 0; k < 4 * VisionTracker.UpdateInterval; k++)
        {
            ground.Sim.TankRef(enemy).Position = k % 6 < 3 ? outOfRange : inRange;
            ground.Run();
            Assert.Equal(enemy, ground.Pick(picker));
            Assert.Equal(k % 6 < 3, picker.TargetLost);
        }

        // Lost for good, it is dropped once a vision update has gone by (one destroyed is dropped at once: see above).
        ground.Sim.TankRef(enemy).Position = outOfRange;
        int kept = 0;
        for (int k = 0; k < 2 * VisionTracker.UpdateInterval && ground.Pick(picker) == enemy; k++, kept++) ground.Run();
        Assert.Equal(VisionTracker.UpdateInterval, kept);
        Assert.Equal(0, picker.TargetId);
    }

    [Fact]
    public void Without_a_target_it_looks_again_once_a_vision_update_has_gone_by()
    {
        var ground = new Ground();
        int me = ground.Add(20, 200, team: 0);
        int enemy = ground.Add(370, 200, team: 1);
        int mate = ground.Add(20, 380, team: 0);   // too far to see it
        ground.Run();
        var picker = new TargetPicker(me);
        int looked = ground.Tick;
        Assert.Equal(0, ground.Pick(picker));

        ground.Sim.TankRef(mate).Position = new Vector2(370, 140);
        while (!ground.Vision.IsVisible(enemy, 0)) ground.Run();
        Assert.True(ground.Tick < looked + VisionTracker.UpdateInterval, $"seen at {ground.Tick}, looked at {looked}");
        while (ground.Tick < looked + VisionTracker.UpdateInterval)
        {
            Assert.Equal(0, ground.Pick(picker));
            ground.Run();
        }
        Assert.Equal(enemy, ground.Pick(picker));
    }

    [Fact]
    public void Once_the_hold_is_over_it_changes_only_for_one_that_ranks_ahead()
    {
        var ground = new Ground();
        int me = ground.Add(50, 200, team: 0);
        int a = ground.Add(150, 200, team: 1);
        int b = ground.Add(250, 220, team: 1);
        ground.Add(200, 150, team: 0);
        ground.Run();
        var picker = new TargetPicker(me);
        Assert.Equal(a, ground.Pick(picker));
        for (int k = 0; k < 3 * TargetPicker.HoldTicks; k++)
        {
            ground.Run();
            Assert.Equal(a, ground.Pick(picker));
        }
        ground.Sim.TankRef(b).Health = 100;   // b is the weaker now: the next review takes it
        int changed = -1;
        for (int k = 0; k < VisionTracker.UpdateInterval && changed < 0; k++)
        {
            ground.Run();
            if (ground.Pick(picker) == b) changed = k;
        }
        Assert.True(changed >= 0, "kept the stronger one past a review");
    }

    [Fact]
    public void A_target_behind_a_house_is_not_picked_even_when_a_team_mate_sees_it()
    {
        var house = new Prop(9, PropKind.House, new Obb2(new Vector2(125, 200), new Vector2(4, 6), 0f), -WallLine.Sink, 6f + WallLine.Sink);

        var open = new Ground();
        int me = open.Add(50, 200, team: 0);
        int enemy = open.Add(200, 200, team: 1);
        open.Add(200, 140, team: 0);
        open.Run();
        Assert.Equal(enemy, open.Pick(new TargetPicker(me)));

        var blocked = new Ground(house);
        int me2 = blocked.Add(50, 200, team: 0);
        int enemy2 = blocked.Add(200, 200, team: 1);
        blocked.Add(200, 140, team: 0);
        blocked.Run();
        Assert.True(blocked.Vision.IsVisible(enemy2, 0));
        Assert.Equal(0, blocked.Pick(new TargetPicker(me2)));
    }

    [Fact]
    public void Over_a_crest_from_an_enemy_below_it_is_not_named_backing_off_would_lose_it()
    {
        // A heavy on the edge of an 8 m shelf, a light 16 m off at its foot: about 29° down, the heavy dips 6°. From 6 m
        // further back the edge comes between them, so it isn't named to back away from.
        var shelf = new Ground(Ground400.Raised(x => x <= 100, 8f));
        int me = shelf.Add(96, 200, team: 0, TankCatalog.Heavy);
        int light = shelf.Add(112, 200, team: 1, TankCatalog.Light);
        shelf.Run();
        Assert.True(shelf.Vision.IsVisible(light, 0));
        var picker = new TargetPicker(me);
        Assert.Equal(0, shelf.Pick(picker));
        Assert.Equal(0, picker.TooClose);
    }

    [Fact]
    public void A_target_behind_a_wreck_is_not_picked()
    {
        // A wreck stops shells: a bot that fired at an enemy behind one spent every round on it (5C stalemate fix 2).
        var ground = new Ground();
        int me = ground.Add(50, 200, team: 0);
        int enemy = ground.Add(150, 200, team: 1);
        int wreck = ground.Add(100, 200, team: 1, TankCatalog.Heavy);
        ground.Run();
        Assert.Equal(wreck, ground.Pick(new TargetPicker(me)));   // alive, it is the nearer target

        ground.Sim.TankRef(wreck).IsDestroyed = true;
        Assert.Equal(0, ground.Pick(new TargetPicker(me)));
        Assert.True(ground.Vision.IsVisible(enemy, 0));

        ground.Sim.TankRef(wreck).Position = new Vector2(100, 210);   // off the line of fire
        Assert.Equal(enemy, ground.Pick(new TargetPicker(me)));
    }

    [Fact]
    public void Out_of_ap_it_picks_for_he_and_out_of_both_nothing()
    {
        var ground = new Ground();
        int me = ground.Add(50, 200, team: 0);
        int enemy = ground.Add(150, 200, team: 1);
        ground.Run();
        var picker = new TargetPicker(me);
        Assert.Equal(enemy, ground.Pick(picker));

        ground.Sim.TankRef(me).ApLeft = 0;
        Assert.Equal(enemy, ground.Pick(picker));
        Assert.Equal(enemy, ground.Pick(new TargetPicker(me)));

        ground.Sim.TankRef(me).HeLeft = 0;
        Assert.Equal(0, ground.Pick(picker));
        Assert.Equal(0, ground.Pick(new TargetPicker(me)));
    }

    [Fact]
    public void An_enemy_too_close_to_lay_on_is_named_but_not_picked()
    {
        // A heavy and a light side by side, 6 m apart: neither the light's hull nor its turret is within the heavy's 6° of depression.
        var ground = new Ground();
        int me = ground.Add(100, 200, team: 0, TankCatalog.Heavy);
        int light = ground.Add(100, 206, team: 1, TankCatalog.Light);
        ground.Add(170, 200, team: 1, TankCatalog.Light);   // a candidate further off is picked over it
        ground.Run();
        var picker = new TargetPicker(me);
        Assert.NotEqual(0, ground.Pick(picker));
        Assert.NotEqual(light, picker.TargetId);
        Assert.Equal(0, picker.TooClose);

        // Alone, it is named, not picked.
        var alone = new Ground();
        me = alone.Add(100, 200, team: 0, TankCatalog.Heavy);
        light = alone.Add(100, 206, team: 1, TankCatalog.Light);
        alone.Run();
        picker = new TargetPicker(me);
        Assert.Equal(0, alone.Pick(picker));
        Assert.Equal(light, picker.TooClose);

        // Out of both rounds, nothing is named.
        (alone.Sim.TankRef(me).ApLeft, alone.Sim.TankRef(me).HeLeft) = (0, 0);
        alone.Run(VisionTracker.UpdateInterval);
        Assert.Equal(0, alone.Pick(picker));
        Assert.Equal(0, picker.TooClose);
    }

    [Fact]
    public void Clearing_forgets_the_target_and_the_hits()
    {
        var ground = new Ground();
        int me = ground.Add(50, 200, team: 0);
        int a = ground.Add(150, 200, team: 1);
        int b = ground.Add(250, 220, team: 1);
        ground.Add(200, 150, team: 0);
        ground.Run();
        var picker = new TargetPicker(me);
        picker.NoteHit(b, ground.Tick);
        Assert.Equal(b, ground.Pick(picker));

        picker.Clear();
        Assert.Equal(0, picker.TargetId);
        Assert.Equal(a, ground.Pick(picker));
    }

    [Fact]
    public void Picking_allocates_nothing()
    {
        Allocation.AssertNone(() =>
        {
            var ground = new Ground();
            int me = ground.Add(50, 200, team: 0);
            ground.Add(150, 200, team: 1);
            int b = ground.Add(250, 220, team: 1);
            ground.Add(200, 150, team: 0);
            var picker = new TargetPicker(me);
            for (int k = 0; k < 60; k++)
            {
                ground.Run();
                ground.Pick(picker);
            }
            Allocation.Window window = Allocation.Start();
            for (int k = 0; k < 300; k++)
            {
                ground.Run();
                if (k == 250) picker.NoteHit(b, ground.Tick);   // the review after the hold takes b, and it is kept to the end
                ground.Pick(picker);
            }
            Allocation.Measured measured = window.Stop();
            Assert.Equal(b, picker.TargetId);
            return measured;
        });
    }
}
