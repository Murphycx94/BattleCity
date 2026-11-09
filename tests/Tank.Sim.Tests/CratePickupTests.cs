using System.Numerics;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;
using Tank.Testing;

namespace Tank.Sim.Tests;

/// <summary>
/// Picking crates up in a match (stage 6 design §4.2): a live hull touching a crate eats it on the rules' update, the lower id
/// first, before the zone hurts; only a royale has crates.
/// </summary>
public sealed class CratePickupTests
{
    private const int Tps = SimConstants.TickRate;
    private static readonly int Max = TankCatalog.Medium.MaxHealth;

    /// <summary>Repair at (100, 100), ammo at (130, 100), armour at (160, 100), on the 200 m flat field.</summary>
    private static Crate[] Three() => new Crate[]
    {
        new(0, new Vector2(100, 100), CrateKind.Repair),
        new(1, new Vector2(130, 100), CrateKind.Ammo),
        new(2, new Vector2(160, 100), CrateKind.Armor),
    };

    /// <summary>A royale with the three crates and two medium tanks parked well away from them (at (40, 150) and (60, 150)).</summary>
    private static (RulesMatch M, int A, int B) Arrange(BattleMode mode = BattleMode.Royale, bool crates = true, IReadOnlyList<ZoneRing>? zone = null)
    {
        var m = new RulesMatch(mode, seed: 3, zone: zone, crates: crates ? Three() : null);
        int a = m.Join(0), b = m.Join(1);
        m.Place(a, 40, 150);
        m.Place(b, 60, 150);
        return (m, a, b);
    }

    private static int Picks(IEnumerable<GameEvent> events) => events.Count(e => e.Type == GameEventType.CratePicked);

    [Fact]
    public void A_tank_parked_on_a_crate_eats_it_on_the_next_update_and_then_it_is_gone()
    {
        var (m, a, _) = Arrange();
        CrateSet crates = Assert.IsType<CrateSet>(m.Rules.Crates);
        Assert.Equal(3, crates.StandingCount);
        m.Place(a, 100, 100);
        m.Weaken(a, 500);

        List<GameEvent> events = m.Run(1);
        Assert.False(crates.IsStanding(0));
        Assert.Equal(a, crates.TakenBy(0));
        Assert.Equal(2, crates.StandingCount);
        Assert.Equal(1, crates.Version);
        CratePickup pickup = Assert.Single(crates.Pickups.ToArray());
        Assert.Equal(new CratePickup(a, 0, new CrateTake(CrateKind.Repair, 270, 0, 0)), pickup);
        Assert.Equal(770, m.Tank(a).Health);
        GameEvent e = Assert.Single(events, e => e.Type == GameEventType.CratePicked);
        Assert.Equal((a, 0, 270, m.Sim.Tick), (e.TankId, e.PropId, e.Damage, e.Tick));
        Assert.Equal(new Vector3(100, 0, 100), e.Position);

        events = m.Run(1);
        Assert.Empty(crates.Pickups.ToArray());
        Assert.Equal(0, Picks(events));
        Assert.Equal(770, m.Tank(a).Health);
        Assert.Equal(1, crates.Version);
    }

    [Theory]
    [InlineData(100.9f, true)]    // the hull over the crate
    [InlineData(102.1f, true)]    // the hull's side (1.6 m out) 0.5 m from the crate's centre: touching the 0.6 m circle
    [InlineData(102.3f, false)]   // 0.7 m: not touching
    [InlineData(104f, false)]
    public void The_hull_touching_the_crates_circle_eats_it(float x, bool eaten)
    {
        var (m, a, _) = Arrange();
        m.Place(a, x, 100);   // facing north: the hull's 3.2 m width lies across x
        List<GameEvent> events = m.Run(1);
        Assert.Equal(eaten, !m.Rules.Crates!.IsStanding(0));
        Assert.Equal(eaten ? 1 : 0, Picks(events));
    }

    [Fact]
    public void Two_hulls_on_one_crate_in_the_same_tick_the_lower_id_eats_it()
    {
        var (m, a, b) = Arrange();
        Assert.True(a < b);
        m.Place(a, 100, 98, MathF.PI / 2);    // side on, 0.4 m short of the crate's centre from either side
        m.Place(b, 100, 102, MathF.PI / 2);
        m.Weaken(a, 500);
        m.Weaken(b, 500);

        List<GameEvent> events = m.Run(1);
        CrateSet crates = m.Rules.Crates!;
        Assert.Equal(a, crates.TakenBy(0));
        CratePickup pickup = Assert.Single(crates.Pickups.ToArray());
        Assert.Equal(a, pickup.TankId);
        Assert.Equal(770, m.Tank(a).Health);
        Assert.Equal(500, m.Tank(b).Health);
        Assert.Equal(1, Picks(events));
    }

    [Fact]
    public void A_wreck_on_a_crate_leaves_it_and_a_live_tank_under_protection_eats_it()
    {
        var (m, a, b) = Arrange();
        m.Place(a, 100, 100);
        m.Sim.TankRef(a).IsDestroyed = true;
        m.Sim.TankRef(a).Health = 0;
        m.Place(b, 130, 100);
        Assert.True(m.Sim.IsProtected(m.Tank(b)));

        List<GameEvent> events = m.Run(1);
        CrateSet crates = m.Rules.Crates!;
        Assert.True(crates.IsStanding(0));
        Assert.False(crates.IsStanding(1));
        Assert.Equal(b, crates.TakenBy(1));
        Assert.Equal(1, Picks(events));
    }

    [Fact]
    public void Only_a_royale_has_crates()
    {
        var (ffa, a, _) = Arrange(BattleMode.FreeForAll);
        Assert.Null(ffa.Rules.Crates);
        ffa.Place(a, 100, 100);
        Assert.Equal(0, Picks(ffa.Run(2)));

        var (royale, _, _) = Arrange(crates: false);
        Assert.Null(royale.Rules.Crates);

        Assert.True(ModeRules.HasCrates(BattleMode.Royale));
        Assert.True(ModeRules.HasCrates(BattleMode.Duos));
        foreach (BattleMode mode in ModeRules.All.Where(mode => mode is not (BattleMode.Royale or BattleMode.Duos))) Assert.False(ModeRules.HasCrates(mode));
    }

    /// <summary>Rings all centred on (20, 150), so the crates are outside the zone from ring 0.</summary>
    private static readonly ZoneRing[] FarRings =
    {
        new(new Vector2(20, 150), 40),
        new(new Vector2(20, 150), 30),
        new(new Vector2(20, 150), 20),
        new(new Vector2(20, 150), 12),
        new(new Vector2(20, 150), 6),
        new(new Vector2(20, 150), 0),
    };

    [Fact]
    public void A_repair_crate_is_eaten_before_the_zone_hurts_on_the_same_second()
    {
        var (m, a, b) = Arrange(zone: FarRings);
        m.Place(a, 20, 140);   // both inside the zone until the moment
        m.Place(b, 20, 165);
        int start = m.Rules.StartTick;
        int second = ZoneSchedule.ShrinkStart(2);   // ring 2 shrinking: 2 %
        m.Run(start + second - 1 - m.Sim.Tick);
        Assert.Equal(Max, m.Tank(a).Health);

        int bite = ZoneSchedule.DamageOf(Max, ZoneSchedule.Stage(2).DamagePercent);
        Assert.True(bite >= 10, $"the zone's bite ({bite}) must be enough to kill a tank on 10");
        m.Place(a, 100, 100);   // onto the repair crate, outside the zone, with 10 health left
        m.Weaken(a, 10);
        List<GameEvent> events = m.Run(1);

        Assert.Equal(start + second, m.Sim.Tick);
        Assert.False(m.Tank(a).IsDestroyed);
        Assert.Equal(10 + 270 - bite, m.Tank(a).Health);
        Assert.Equal(1, Picks(events));
        Assert.DoesNotContain(events, e => e.Type == GameEventType.TankDestroyed);
        Assert.Equal(MatchPhase.Playing, m.Rules.Phase);
    }

    [Fact]
    public void A_repair_eaten_this_tick_raises_the_cap_on_the_damage_the_next_hit_counts()
    {
        // NoteHealth runs last in MatchRules.Update, so the health a hit's damage is counted up to is the repaired one.
        var (m, a, b) = Arrange();
        m.Run(ModeRules.ProtectionTicks + 1);
        m.Place(a, 100, 150, 0f);            // the shooter, 50 m south of the repair crate, facing it
        m.Place(b, 100, 100, MathF.PI);      // the target on the crate, its hull front to the shooter
        m.Weaken(b, 100);
        Assert.Equal(1, Picks(m.Run(1)));
        Assert.Equal(370, m.Tank(b).Health);

        m.Attack(a, b);
        GameEvent hit = default;
        for (int i = 0; i < 20 * Tps && hit.Damage == 0; i++)
            hit = m.Run(1).FirstOrDefault(e => e.Type == GameEventType.TankHit && e.TankId == a && e.TargetTankId == b && e.Damage > 0);
        Assert.True(hit.Damage > 100, $"the hit did {hit.Damage}: not enough to tell the repaired cap from the old health");
        Assert.Equal(370 - hit.Damage, m.Tank(b).Health);
        Assert.Equal(hit.Damage, m.Rules.Of(0).Damage);   // not capped at the 100 the target had before the repair
    }

    /// <summary>A royale with one repair crate at (100, 130): between a shooter at (100, 150) and a target at (100, 110), or in a driver's path.</summary>
    private static RulesMatch CrateOnTheLine() =>
        new(BattleMode.Royale, seed: 3, crates: new Crate[] { new(0, new Vector2(100, 130), CrateKind.Repair) });

    [Fact]
    public void A_shell_flies_through_a_crate_to_the_tank_behind_it()
    {
        // Crates are not props (stage 6 design §4.2): nothing in the simulation strikes them, so they shelter no one.
        RulesMatch m = CrateOnTheLine();
        int a = m.Join(0), b = m.Join(1);
        m.Place(a, 100, 150, 0f);         // facing north, the crate 20 m ahead
        m.Place(b, 100, 110, MathF.PI);   // the target 40 m away, the crate halfway, its hull front to the shooter
        m.Run(ModeRules.ProtectionTicks + 1);

        m.Attack(a, b);
        var events = new List<GameEvent>();
        bool hit = false;
        for (int i = 0; i < 10 * Tps && !hit; i++)
        {
            List<GameEvent> tick = m.Run(1);
            events.AddRange(tick);
            hit = tick.Any(e => e.Type == GameEventType.TankHit && e.TankId == a && e.TargetTankId == b);
        }

        Assert.True(hit, "no shell reached the target behind the crate");
        Assert.DoesNotContain(events, e => e.Type == GameEventType.ShellExploded && Vector2.Distance(new Vector2(e.Position.X, e.Position.Z), new Vector2(100, 130)) < 3f);
        Assert.True(m.Rules.Crates!.IsStanding(0), "a shell took the crate");
        Assert.Equal(0, Picks(events));
    }

    [Fact]
    public void A_tank_drives_through_a_crate_as_fast_as_over_open_ground_and_eats_it()
    {
        RulesMatch m = CrateOnTheLine();
        int a = m.Join(0), b = m.Join(1);
        m.Place(a, 100, 150, 0f);   // the crate 20 m ahead
        m.Place(b, 60, 150, 0f);    // open ground alongside
        m.Order(a, new InputCmd(0, 1, 0, 0f, 0f, false));
        m.Order(b, new InputCmd(0, 1, 0, 0f, 0f, false));

        m.Run(5 * Tps);

        Assert.Equal(a, m.Rules.Crates!.TakenBy(0));
        Assert.True(m.Tank(a).Position.Y < 120, $"the driver stopped short at {m.Tank(a).Position}");
        Assert.Equal(m.Tank(b).Position.Y, m.Tank(a).Position.Y, 1e-3f);
        Assert.Equal(m.Tank(b).Speed, m.Tank(a).Speed, 1e-3f);
    }

    [Fact]
    public void Collecting_crates_allocates_nothing()
    {
        Allocation.AssertNone(() =>
        {
            var (m, a, b) = Arrange();
            CrateSet crates = m.Rules.Crates!;
            var inputs = new InputCmd[2];
            void Tick(sbyte throttle)
            {
                inputs[0] = new InputCmd(0, throttle, 0, 0f, 0f, false);
                inputs[1] = new InputCmd(0, throttle, 0, 0f, 0f, false);
                m.Sim.Step(inputs);
                m.Rules.Update();
            }
            m.Place(a, 160, 112);   // the warm-up drives the first tank over the armour crate
            m.Place(b, 60, 150);
            for (int i = 0; i < 3 * Tps; i++) Tick(1);
            Assert.False(crates.IsStanding(2));
            m.Place(a, 100, 112);   // the window drives both north over the repair and the ammo crates, then back and forth
            m.Place(b, 130, 112);
            int version = crates.Version;

            Allocation.Window window = Allocation.Start();
            for (int i = 0; i < 300; i++) Tick((sbyte)(i / (3 * Tps) % 2 == 0 ? 1 : -1));
            Allocation.Measured measured = window.Stop();

            Assert.True(crates.Version > version, "no crate eaten in the window");
            Assert.False(crates.IsStanding(0));
            Assert.False(crates.IsStanding(1));
            return measured;
        });
    }
}
