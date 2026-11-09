using System.Numerics;
using Tank.Sim.Combat;
using Tank.Sim.Maps;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;
using Tank.Sim.Vision;

namespace Tank.Sim.Tests;

/// <summary>
/// What the crates do to a tank inside the simulation (stage 6 design §4.2): armour layers scale shell damage, reload
/// layers shorten the reload, optics layers stretch the view range, and <see cref="Simulation.TakeCrate"/> applies a crate.
/// </summary>
public sealed class CrateEffectsTests
{
    private static readonly TankSpec Spec = TankCatalog.Medium;
    private static readonly Vector3 HullFront = new(0, 1.02f, -3.1f);
    private static readonly int ReloadTicks = (int)MathF.Round(Spec.ReloadTime * SimConstants.TickRate);
    private const int Tick = 1000;

    /// <summary>One AP shot at the target's hull front, the target wearing <paramref name="armorLayers"/>; the same seed every time.</summary>
    private static (GameEvent Hit, Duel Duel) ApHit(byte armorLayers)
    {
        var duel = new Duel(targetYaw: MathF.PI);
        duel.Sim.TankRef(duel.TargetId).ArmorLayers = armorLayers;
        var events = duel.Fire(duel.AimAt(duel.OnTarget(HullFront)));
        return (Assert.Single(events, e => e.Type == GameEventType.TankHit), duel);
    }

    /// <summary>One HE shot at the target's hull front with a bystander alongside wearing <paramref name="armorLayers"/>: the splash on it.</summary>
    private static (GameEvent Splash, Duel Duel) HeSplash(byte armorLayers)
    {
        var duel = new Duel(targetYaw: MathF.PI);
        int bystander = duel.AddBystander(new Vector2(103.4f, 100), MathF.PI);
        duel.Sim.TankRef(bystander).ArmorLayers = armorLayers;
        InputCmd aim = duel.AimAt(duel.OnTarget(HullFront), AmmoType.HE);
        duel.Run(aim, ReloadTicks);
        var events = duel.Fire(aim);
        GameEvent splash = Assert.Single(events, e => e.Type == GameEventType.SplashDamage);
        Assert.Equal(bystander, splash.TargetTankId);
        return (splash, duel);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Armour_layers_scale_a_shells_damage_and_the_hit_event_reports_what_was_taken(byte layers)
    {
        var (bare, _) = ApHit(0);
        var (armoured, duel) = ApHit(layers);

        Assert.Equal(HitResult.Penetrated, bare.Result);
        Assert.Equal(bare.Result, armoured.Result);
        Assert.InRange(bare.Damage, 165, 275);
        Assert.Equal(CrateRules.ScaleShellDamage(bare.Damage, layers), armoured.Damage);
        Assert.True(armoured.Damage < bare.Damage);
        Assert.Equal(900 - armoured.Damage, duel.Target.Health);
    }

    [Fact]
    public void Three_armour_layers_leave_seven_tenths_of_the_damage()
    {
        var (bare, _) = ApHit(0);
        var (armoured, _) = ApHit(3);
        Assert.Equal(Penetration.RoundHalfUp(bare.Damage * 0.7f), armoured.Damage);
    }

    [Fact]
    public void Armour_layers_scale_splash_damage_too()
    {
        var (bare, _) = HeSplash(0);
        var (armoured, duel) = HeSplash(2);

        Assert.True(bare.Damage > 0);
        Assert.Equal(CrateRules.ScaleShellDamage(bare.Damage, 2), armoured.Damage);
        Assert.True(armoured.Damage < bare.Damage);
        Assert.Equal(900 - armoured.Damage, duel.Sim.Tanks[2].Health);
    }

    [Fact]
    public void The_zone_ignores_armour_layers()
    {
        var sim = new Simulation(TestWorld.FlatField());
        int id = sim.AddTank(Spec, new Vector2(100, 100), 0f);
        sim.TankRef(id).ArmorLayers = 3;
        sim.HurtByZone(id, 100);
        Assert.Equal(900 - 100, sim.Tanks[0].Health);
    }

    [Theory]
    [InlineData(0, 4f)]
    [InlineData(2, 3.36f)]
    public void Reload_layers_shorten_the_reload_after_a_shot_and_after_a_change_of_ammunition(byte layers, float reload)
    {
        var sim = new Simulation(TestWorld.FlatField());
        int id = sim.AddTank(Spec, new Vector2(100, 100), 0f);
        sim.TankRef(id).ReloadLayers = layers;

        sim.Step(new[] { TestWorld.Cmd(fire: true) });
        Assert.Equal(1, sim.Tanks[0].ShotCount);
        Assert.Equal(reload, sim.Tanks[0].Reload, 1e-5f);

        sim.Step(new[] { TestWorld.Cmd(ammo: AmmoType.HE) });
        Assert.Equal(reload, sim.Tanks[0].Reload, 1e-5f);
    }

    private static TankState Observer(byte opticsLayers) => new()
    {
        Id = 1, Spec = Spec, Position = new Vector2(200, 390), LastFired = TankState.NeverFired, OpticsLayers = opticsLayers,
    };

    /// <summary>A light tank parked <paramref name="distance"/> m north of the observer, side on; <paramref name="camo"/> for its concealment.</summary>
    private static TankState Target(float distance, float camo) => new()
    {
        Id = 2, Spec = TankCatalog.Light with { Camo = new Concealment(camo, camo) }, Team = 1,
        Position = new Vector2(200, 390 - distance), Yaw = MathF.PI / 2, LastFired = TankState.NeverFired,
    };

    [Fact]
    public void An_optics_layer_lets_a_medium_tank_see_290_m_across_open_ground_where_270_m_of_view_did_not()
    {
        Battlefield field = TestWorld.WideFlatField();
        TankState target = Target(290, camo: 0f);
        Assert.False(Spotting.Sees(Observer(0), target, field, Tick, false));
        Assert.True(Spotting.Sees(Observer(1), target, field, Tick, false));
        Assert.False(Spotting.Look(Observer(0), target, field, Tick, false).Seen);
        Assert.True(Spotting.Look(Observer(1), target, field, Tick, false).Seen);
    }

    [Fact]
    public void Optics_layers_stretch_the_detection_range_against_a_concealed_tank_too()
    {
        Battlefield field = TestWorld.WideFlatField();
        TankState target = Target(175, camo: 0.4f);   // 15 + 255 × 0.6 = 168 m unseen; 15 + 282 × 0.6 = 184.2 m seen
        Assert.False(Spotting.Sees(Observer(0), target, field, Tick, false));
        Assert.True(Spotting.Sees(Observer(1), target, field, Tick, false));
        Assert.Equal(Spotting.DetectionRange(297f, 0.4f), Spotting.Look(Observer(1), target, field, Tick, false).DetectionRange, 1e-3f);
        Assert.Equal(Spotting.DetectionRange(270f, 0.4f), Spotting.Look(Observer(0), target, field, Tick, false).DetectionRange, 1e-3f);
    }

    private static Crate At(CrateKind kind, float x = 100, float z = 100) => new(5, new Vector2(x, z), kind);

    /// <summary>A medium tank at (100, 100) on a slope (so the ground height isn't 0), one tick in.</summary>
    private static (Simulation Sim, int Id) OnASlope()
    {
        var sim = new Simulation(TestWorld.Field(TestWorld.RampNorth(0.1f)));
        int id = sim.AddTank(Spec, new Vector2(100, 100), 0f);
        sim.Step(new[] { TestWorld.Cmd() });
        return (sim, id);
    }

    private static GameEvent SinglePick(Simulation sim) => Assert.Single(sim.Events.ToArray(), e => e.Type == GameEventType.CratePicked);

    [Fact]
    public void A_repair_crate_restores_three_tenths_of_max_health_up_to_full_and_a_full_tank_wastes_it()
    {
        var (sim, id) = OnASlope();
        CrateTake take = sim.TakeCrate(id, At(CrateKind.Repair));
        Assert.Equal(new CrateTake(CrateKind.Repair, 0, 0, 0), take);
        Assert.True(take.Wasted);
        Assert.Equal(900, sim.Tanks[0].Health);
        Assert.Equal(0, SinglePick(sim).Damage);

        sim.Step(new[] { TestWorld.Cmd() });
        sim.TankRef(id).Health = 400;
        Assert.Equal(new CrateTake(CrateKind.Repair, 270, 0, 0), sim.TakeCrate(id, At(CrateKind.Repair)));
        Assert.Equal(670, sim.Tanks[0].Health);
        Assert.Equal(270, SinglePick(sim).Damage);

        sim.Step(new[] { TestWorld.Cmd() });
        sim.TankRef(id).Health = 800;
        Assert.Equal(new CrateTake(CrateKind.Repair, 100, 0, 0), sim.TakeCrate(id, At(CrateKind.Repair)));
        Assert.Equal(900, sim.Tanks[0].Health);
    }

    [Fact]
    public void An_ammo_crate_adds_half_of_each_rack_up_to_the_capacities()
    {
        var (sim, id) = OnASlope();
        sim.TankRef(id).ApLeft = 0;
        sim.TankRef(id).HeLeft = 0;
        Assert.Equal(new CrateTake(CrateKind.Ammo, 20, 8, 0), sim.TakeCrate(id, At(CrateKind.Ammo)));
        Assert.Equal((20, 8), (sim.Tanks[0].ApLeft, sim.Tanks[0].HeLeft));
        Assert.Equal(20, SinglePick(sim).Damage);

        sim.Step(new[] { TestWorld.Cmd() });
        sim.TankRef(id).ApLeft = 37;
        sim.TankRef(id).HeLeft = 12;
        Assert.Equal(new CrateTake(CrateKind.Ammo, 3, 3, 0), sim.TakeCrate(id, At(CrateKind.Ammo)));
        Assert.Equal((40, 15), (sim.Tanks[0].ApLeft, sim.Tanks[0].HeLeft));

        sim.Step(new[] { TestWorld.Cmd() });
        CrateTake full = sim.TakeCrate(id, At(CrateKind.Ammo));
        Assert.True(full.Wasted);
        Assert.Equal((40, 15), (sim.Tanks[0].ApLeft, sim.Tanks[0].HeLeft));

        sim.Step(new[] { TestWorld.Cmd() });
        sim.TankRef(id).HeLeft = 14;
        CrateTake heOnly = sim.TakeCrate(id, At(CrateKind.Ammo));
        Assert.Equal(new CrateTake(CrateKind.Ammo, 0, 1, 0), heOnly);
        Assert.False(heOnly.Wasted);
    }

    [Theory]
    [InlineData(CrateKind.Armor)]
    [InlineData(CrateKind.Reload)]
    [InlineData(CrateKind.Optics)]
    public void A_layer_crate_adds_a_layer_up_to_three_and_the_fourth_is_wasted(CrateKind kind)
    {
        var (sim, id) = OnASlope();
        for (byte expected = 1; expected <= CrateRules.MaxLayers; expected++)
        {
            Assert.Equal(new CrateTake(kind, 1, 0, expected), sim.TakeCrate(id, At(kind)));
            Assert.Equal(expected, CrateRules.Layers(sim.Tanks[0], kind));
        }
        CrateTake fourth = sim.TakeCrate(id, At(kind));
        Assert.Equal(new CrateTake(kind, 0, 0, 3), fourth);
        Assert.True(fourth.Wasted);
        Assert.Equal(3, CrateRules.Layers(sim.Tanks[0], kind));
        foreach (CrateKind other in Enum.GetValues<CrateKind>())
            if (other != kind) Assert.Equal(0, CrateRules.Layers(sim.Tanks[0], other));
        Assert.Equal(4, sim.Events.ToArray().Count(e => e.Type == GameEventType.CratePicked));
    }

    [Fact]
    public void Every_take_emits_a_CratePicked_event_naming_the_tank_the_crate_and_what_it_did_on_the_ground()
    {
        var (sim, id) = OnASlope();
        sim.TankRef(id).Health = 100;
        Crate crate = At(CrateKind.Repair, 120, 80);
        sim.TakeCrate(id, crate);

        GameEvent e = SinglePick(sim);
        Assert.Equal((id, 5, 270, sim.Tick), (e.TankId, e.PropId, e.Damage, e.Tick));
        Assert.Equal(0, e.ShellId);
        Assert.Equal(0, e.TargetTankId);
        float ground = sim.Field.Terrain.HeightAt(120, 80);
        Assert.True(ground > 1f, $"the slope should lift the crate: {ground}");
        Assert.Equal(new Vector3(120, ground, 80), e.Position);
        Assert.Equal(Vector3.Zero, e.Velocity);
    }

    [Fact]
    public void A_wreck_takes_nothing_and_emits_nothing()
    {
        var (sim, id) = OnASlope();
        sim.TankRef(id).IsDestroyed = true;
        sim.TankRef(id).Health = 0;
        int events = sim.Events.Length;
        Assert.Equal(new CrateTake(CrateKind.Repair, 0, 0, 0), sim.TakeCrate(id, At(CrateKind.Repair)));
        Assert.Equal(new CrateTake(CrateKind.Armor, 0, 0, 0), sim.TakeCrate(id, At(CrateKind.Armor)));
        Assert.Equal(0, sim.Tanks[0].Health);
        Assert.Equal(0, sim.Tanks[0].ArmorLayers);
        Assert.Equal(events, sim.Events.Length);
    }

    [Fact]
    public void Taking_for_a_tank_that_is_not_there_throws()
    {
        var (sim, _) = OnASlope();
        Assert.Throws<ArgumentException>(() => sim.TakeCrate(999, At(CrateKind.Repair)));
    }

    [Fact]
    public void A_respawn_clears_the_layers_a_change_of_vehicle_too_and_a_revive_keeps_them()
    {
        var sim = new Simulation(TestWorld.FlatField());
        int id = sim.AddTank(Spec, new Vector2(100, 100), 0f);
        ref TankState t = ref sim.TankRef(id);
        (t.ArmorLayers, t.ReloadLayers, t.OpticsLayers) = (1, 2, 3);
        t.IsDestroyed = true;
        t.Health = 0;
        sim.Revive(id);
        Assert.Equal((1, 2, 3), (sim.Tanks[0].ArmorLayers, sim.Tanks[0].ReloadLayers, sim.Tanks[0].OpticsLayers));

        sim.ChangeSpec(id, TankCatalog.Light);
        Assert.Equal((0, 0, 0), (sim.Tanks[0].ArmorLayers, sim.Tanks[0].ReloadLayers, sim.Tanks[0].OpticsLayers));

        sim.TankRef(id).OpticsLayers = 2;
        sim.Respawn(id, new Vector2(50, 50), 0f);
        Assert.Equal(0, sim.Tanks[0].OpticsLayers);
    }
}
