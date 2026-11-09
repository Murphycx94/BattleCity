using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;
using Tank.Sim.Vision;

namespace Tank.Sim.Tests;

/// <summary>A medium tank at (100, 190) looks north (−Z) at a light tank straight ahead of it, on flat ground.</summary>
public sealed class SpottingTests
{
    private static readonly Vector2 ObserverAt = new(100, 190);
    private const int Tick = 1000;

    private static TankState Observer() => new() { Id = 1, Spec = TankCatalog.Medium, Position = ObserverAt, LastFired = TankState.NeverFired };

    /// <summary>The light tank <paramref name="distance"/> m north of the observer, side on.</summary>
    private static TankState Target(float distance) => new()
    {
        Id = 2, Spec = TankCatalog.Light, Team = 1, Position = ObserverAt - new Vector2(0, distance), Yaw = MathF.PI / 2,
        LastFired = TankState.NeverFired,
    };

    private static Prop Bush(int id, Vector2 at, float radius = 3.5f) =>
        new(id, PropKind.Bush, new Obb2(at, new Vector2(radius, radius), 0), -0.2f, 4.5f);

    /// <summary>A rock slab across the line of sight at <paramref name="z"/>, <paramref name="height"/> m tall.</summary>
    private static Prop Slab(int id, float z, float height) =>
        new(id, PropKind.Rock, new Obb2(new Vector2(100, z), new Vector2(6, 0.5f), 0), -1f, height + 1f);

    private static Sighting Look(TankState target, bool moving = false, params Prop[] props) =>
        Spotting.Look(Observer(), target, TestWorld.FlatField(props), Tick, moving);

    [Theory]
    [InlineData(270f, 0f, 270f)]
    [InlineData(270f, 0.4f, 168f)]
    [InlineData(270f, 0.65f, 104.25f)]
    [InlineData(270f, 0.9f, 40.5f)]
    [InlineData(270f, 1f, 15f)]
    public void Detection_range_shrinks_from_the_view_range_towards_15_m(float view, float camo, float range) =>
        Assert.Equal(range, Spotting.DetectionRange(view, camo), 1e-3f);

    [Fact]
    public void Concealment_is_moving_or_still_cut_by_a_recent_shot_and_raised_by_bushes_up_to_a_cap()
    {
        var light = TankCatalog.Light.Camo;   // 0.30 moving, 0.40 still
        Assert.Equal(0.40f, Spotting.EffectiveCamo(light, moving: false, firedRecently: false, bushes: 0), 1e-5f);
        Assert.Equal(0.30f, Spotting.EffectiveCamo(light, moving: true, firedRecently: false, bushes: 0), 1e-5f);
        Assert.Equal(0.10f, Spotting.EffectiveCamo(light, moving: false, firedRecently: true, bushes: 0), 1e-5f);
        Assert.Equal(0.65f, Spotting.EffectiveCamo(light, moving: false, firedRecently: false, bushes: 1), 1e-5f);
        Assert.Equal(0.35f, Spotting.EffectiveCamo(light, moving: false, firedRecently: true, bushes: 1), 1e-5f);
        Assert.Equal(0.90f, Spotting.EffectiveCamo(light, moving: false, firedRecently: false, bushes: 2), 1e-5f);   // 0.90 exactly
        Assert.Equal(0.80f, Spotting.EffectiveCamo(light, moving: true, firedRecently: false, bushes: 3), 1e-5f);    // two at most
        Assert.Equal(0.90f, Spotting.EffectiveCamo(TankCatalog.Td.Camo, moving: false, firedRecently: false, bushes: 2), 1e-5f);   // 0.95 capped
    }

    [Fact]
    public void A_shot_gives_a_tank_away_for_three_seconds()
    {
        var tank = new TankState { LastFired = 500 };
        Assert.True(Spotting.FiredRecently(tank, 500));
        Assert.True(Spotting.FiredRecently(tank, 500 + Spotting.FirePenaltyTicks - 1));
        Assert.False(Spotting.FiredRecently(tank, 500 + Spotting.FirePenaltyTicks));
        Assert.False(Spotting.FiredRecently(new TankState { LastFired = TankState.NeverFired }, 0));
        Assert.Equal(90, Spotting.FirePenaltyTicks);
    }

    [Theory]
    [InlineData(160f, false, true)]    // still: seen out to 168 m
    [InlineData(175f, false, false)]
    [InlineData(175f, true, true)]     // moving: seen out to 193.5 m
    [InlineData(200f, true, false)]
    public void In_the_open_a_tank_is_seen_within_the_detection_range(float distance, bool moving, bool seen)
    {
        Sighting look = Look(Target(distance), moving);
        Assert.Equal(seen, look.Seen);
        Assert.True(look.LineClear);
        Assert.Equal(distance, look.Distance, 1e-3f);
        Assert.Equal(moving ? 193.5f : 168f, look.DetectionRange, 1e-3f);
    }

    [Fact]
    public void Within_15_m_a_tank_is_seen_whatever_stands_between()
    {
        Sighting look = Look(Target(12f), false, Slab(1, 184, 6f));
        Assert.False(look.LineClear);
        Assert.True(look.Seen);
    }

    [Fact]
    public void A_tall_wall_between_hides_a_tank_in_range()
    {
        Sighting look = Look(Target(60f), false, Slab(1, 160, 6f));
        Assert.False(look.LineClear);
        Assert.False(look.Seen);
    }

    [Fact]
    public void One_clear_sight_line_is_enough_a_low_wall_hides_only_the_hull()
    {
        // 1.8 m tall, 21.5 m short of the target: the line to its hull centre passes at about 1.4 m (blocked), the one
        // to its turret top at about 2.4 m (clear).
        Sighting look = Look(Target(60f), false, Slab(1, 131.5f, 1.8f));
        Assert.True(look.LineClear);
        Assert.True(look.Seen);
    }

    [Fact]
    public void A_rise_in_the_ground_between_hides_a_tank_in_range()
    {
        Assert.False(Spotting.Look(Observer(), Target(60f), TestWorld.RidgeField(160f, 5f), Tick, false).Seen);
        Sighting low = Spotting.Look(Observer(), Target(60f), TestWorld.RidgeField(160f, 1f), Tick, false);   // under both sight lines
        Assert.True(low.LineClear);
        Assert.True(low.Seen);
    }

    [Fact]
    public void Fences_bushes_and_rubble_do_not_block_the_view()
    {
        Prop fence = new WallLine(PropKind.Fence, new Vector2(99, 160), new Vector2(101, 160)).Segments(TestWorld.Flat(), 1).Single();
        Prop wall = new WallLine(PropKind.BrickWall, new Vector2(97, 150), new Vector2(99, 150)).Segments(TestWorld.Flat(), 2).Single();
        Prop wall2 = new WallLine(PropKind.BrickWall, new Vector2(99, 150), new Vector2(101, 150)).Segments(TestWorld.Flat(), 3).Single();
        Battlefield field = TestWorld.FlatField(fence, wall, wall2, Bush(4, new Vector2(100, 170)));
        Assert.False(Spotting.Look(Observer(), Target(60f), field, Tick, false).LineClear, "the standing wall blocks");
        field.Destroy(field.IndexOf(3));   // the wall segment in the line turns to rubble
        Assert.True(Spotting.Look(Observer(), Target(60f), field, Tick, false).LineClear);   // tanks aren't props: rays never meet them
    }

    [Fact]
    public void A_bush_the_target_sits_in_adds_a_quarter()
    {
        Sighting look = Look(Target(80f), false, Bush(1, ObserverAt - new Vector2(-2f, 80f), 3f));   // hull centre 2 m in: within 0.75 × 3
        Assert.Equal(1, look.Bushes);
        Assert.Equal(0.65f, look.Camo, 1e-5f);
        Assert.Equal(104.25f, look.DetectionRange, 1e-3f);
        Assert.True(look.Seen);
    }

    [Fact]
    public void A_bush_short_of_the_target_hides_it_only_if_the_sight_line_cuts_it()
    {
        // The sight line runs along x = 100. A 3 m bush 5 m short of the target, centred 4 m east, misses it; centred
        // 2.5 m east the line cuts its edge.
        Assert.Equal(0, Look(Target(80f), false, Bush(1, ObserverAt - new Vector2(-4f, 75f), 3f)).Bushes);
        Assert.Equal(1, Look(Target(80f), false, Bush(1, ObserverAt - new Vector2(-2.5f, 75f), 3f)).Bushes);
    }

    [Theory]
    [InlineData(-2.5f, 0f, 0)]    // on its east rim, the line passing 2.5 m from its middle (beyond 0.75 × 3 = 2.25 m)
    [InlineData(0f, 2.5f, 1)]     // on its far rim: the line runs through its middle first
    [InlineData(0f, -2.5f, 0)]    // on its near rim, the bush behind it
    [InlineData(0f, 2f, 1)]       // 2 m in: in it
    public void A_bush_the_target_stands_in_hides_it_only_from_its_inner_part(float east, float south, int bushes)
    {
        Vector2 target = ObserverAt - new Vector2(0, 80f);
        Sighting look = Look(Target(80f), false, Bush(1, target + new Vector2(-east, south), 3f));
        Assert.Equal(bushes, look.Bushes);
    }

    [Theory]
    [InlineData(10f, 1)]    // on the line 10 m short of the target
    [InlineData(14f, 1)]    // its edge within 15 m
    [InlineData(25f, 0)]    // too far from the target to hide it
    public void A_bush_on_the_sight_line_hides_the_target_only_near_it(float shortOf, int bushes)
    {
        Sighting look = Look(Target(80f), false, Bush(1, ObserverAt - new Vector2(0, 80f - shortOf), 2f));
        Assert.Equal(bushes, look.Bushes);
    }

    [Theory]
    [InlineData(17.26f, 0)]   // the line is inside the bush only 16.5–18 m short of the target: beyond the reach
    [InlineData(13f, 1)]      // the same bush nearer: the line runs through its edge within 15 m
    public void A_bush_the_line_grazes_counts_only_where_it_grazes_within_15_m(float shortOf, int bushes)
    {
        // 2.9 m off the line, radius 3: its centre is within 15 m + its radius of the target either way.
        Sighting look = Look(Target(80f), false, Bush(1, ObserverAt - new Vector2(-2.9f, 80f - shortOf), 3f));
        Assert.Equal(bushes, look.Bushes);
    }

    [Fact]
    public void Bushes_count_once_each_and_at_most_two()
    {
        Vector2 at = ObserverAt - new Vector2(0, 80f);
        // The target's own bush also lies on the line; two more on the line close by.
        Sighting look = Look(Target(80f), false, Bush(1, at, 4f), Bush(2, at + new Vector2(0, 6), 2f), Bush(3, at + new Vector2(0, 11), 2f));
        Assert.Equal(2, look.Bushes);
        Assert.Equal(Spotting.MaxCamo, look.Camo, 1e-5f);

        Sighting one = Look(Target(80f), false, Bush(1, at, 4f));
        Assert.Equal(1, one.Bushes);   // in it and behind it: still one bush
    }

    [Fact]
    public void A_bush_shot_away_hides_nothing()
    {
        Battlefield field = TestWorld.FlatField(Bush(1, ObserverAt - new Vector2(0, 80f), 4f));
        field.Destroy(0);
        Assert.Equal(0, Spotting.Look(Observer(), Target(80f), field, Tick, false).Bushes);
    }

    [Fact]
    public void A_shot_from_a_bush_gives_the_tank_away()
    {
        TankState hidden = Target(150f) with { LastFired = Tick - 30 };
        Sighting look = Look(hidden, false, Bush(1, ObserverAt - new Vector2(0, 150f), 4f));
        Assert.True(look.FiredRecently);
        Assert.Equal(0.35f, look.Camo, 1e-5f);
        Assert.True(look.Seen, "181 m against 150 m");
        Assert.False(Look(Target(150f), false, Bush(1, ObserverAt - new Vector2(0, 150f), 4f)).Seen, "104 m without the shot");
    }

    [Fact]
    public void Sees_gives_the_same_answer_as_a_full_look()
    {
        // Two observers on the sandbox map (rocks, hills, bushes, walls) and targets all round them, still, moving and
        // just after a shot, light and heavy.
        var sim = new Simulation(SandboxMap.Create());
        sim.AddTank(TankCatalog.Medium, SandboxMap.Spawn, 0f);
        sim.AddTank(TankCatalog.Light, new Vector2(224, 352), 0f);
        sim.AddTank(TankCatalog.Light, Vector2.Zero, 0.7f, team: 1);
        sim.AddTank(TankCatalog.Heavy, Vector2.Zero, 2.1f, team: 1);
        int seen = 0, unseen = 0;
        for (int o = 0; o < 2; o++)
        for (float x = 30; x <= 370; x += 12)
        for (float z = 30; z <= 370; z += 12)
        for (int kind = 0; kind < 6; kind++)
        {
            TankState target = sim.Tanks[2 + kind % 2] with { Position = new Vector2(x, z), LastFired = kind >= 4 ? Tick - 10 : TankState.NeverFired };
            bool moving = kind is 2 or 3;
            bool full = Spotting.Look(sim.Tanks[o], target, sim.Field, Tick, moving).Seen;
            Assert.True(full == Spotting.Sees(sim.Tanks[o], target, sim.Field, Tick, moving), $"observer {o}, {target.Spec.Id} at {target.Position}, kind {kind}");
            if (full) seen++; else unseen++;
        }
        Assert.True(seen > 1000 && unseen > 1000, $"{seen} seen, {unseen} unseen");
    }

    [Fact]
    public void The_eye_is_on_the_turret_top()
    {
        TankState medium = Observer();
        Vector3 top = Spotting.TurretTop(medium, TankPose.Compute(medium, TestWorld.Flat()));
        Assert.Equal(TankCatalog.Medium.TurretArmor.Top, top.Y, 1e-4f);
        Assert.Equal(ObserverAt.Y - TankCatalog.Medium.TurretOffset, top.Z, 1e-4f);
    }

    [Fact]
    public void Without_optics_layers_the_view_range_is_the_specs_and_the_numbers_are_as_they_were()
    {
        TankState observer = Observer();
        Assert.Equal(0, observer.OpticsLayers);
        Assert.Equal(TankCatalog.Medium.ViewRange, CrateRules.ViewRange(observer));
        Assert.Equal(168f, Look(Target(160f)).DetectionRange, 1e-3f);
        Assert.Equal(193.5f, Look(Target(175f), moving: true).DetectionRange, 1e-3f);
        Battlefield field = TestWorld.FlatField();
        Assert.True(Spotting.Sees(observer, Target(160f), field, Tick, false));
        Assert.False(Spotting.Sees(observer, Target(175f), field, Tick, false));
        Assert.True(Spotting.Sees(observer, Target(175f), field, Tick, true));
        Assert.False(Spotting.Sees(observer, Target(200f), field, Tick, true));
    }
}
