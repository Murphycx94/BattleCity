using System.Numerics;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;
using Tank.Testing;

namespace Tank.Sim.Tests;

/// <summary>The zone hurts (stage 6 design §3.2): a share of max health each whole second outside the circle, protection or not, credited to no one.</summary>
public sealed class ZoneDamageTests
{
    private const int Tps = SimConstants.TickRate;

    /// <summary>Small rings on the 200 m flat field: (100, 100) inside them all up to ring 3, (20, 100) outside ring 0.</summary>
    private static readonly ZoneRing[] Rings =
    {
        new(new Vector2(100, 100), 40),
        new(new Vector2(110, 100), 30),
        new(new Vector2(112, 100), 20),
        new(new Vector2(112, 100), 12),
        new(new Vector2(112, 100), 6),
        new(new Vector2(112, 100), 0),
    };

    private static readonly int Max = TankCatalog.Medium.MaxHealth;

    /// <summary>A royale (or <paramref name="mode"/>) with the small rings: slot 0 outside ring 0 at (20, 100), slot 1 inside at (100, 100).</summary>
    private static (RulesMatch M, int Out, int In) Arrange(BattleMode mode = BattleMode.Royale, bool zone = true)
    {
        var m = new RulesMatch(mode, seed: 3, zone: zone ? Rings : null);
        int outside = m.Join(0), inside = m.Join(1);
        m.Place(outside, 20, 100);
        m.Place(inside, 100, 100);
        return (m, outside, inside);
    }

    private static int Elapsed(RulesMatch m) => m.Sim.Tick - m.Rules.StartTick;

    /// <summary>Runs until <paramref name="elapsed"/> ticks have passed since the start.</summary>
    private static void RunTo(RulesMatch m, int elapsed) => m.Run(elapsed - Elapsed(m));

    /// <summary>As <see cref="RunTo"/>, topping every player's tank up to full health every five seconds, so the zone kills no one on the way.</summary>
    private static void RunToKeepingAll(RulesMatch m, int elapsed)
    {
        while (Elapsed(m) < elapsed)
        {
            m.Run(Math.Min(5 * Tps, elapsed - Elapsed(m)));
            for (byte slot = 0; slot < 2; slot++) m.Weaken(m.Rules.Of(slot).TankId, Max);
        }
    }

    private static int Health(RulesMatch m, int id) => m.Tank(id).Health;

    [Fact]
    public void Hurt_by_zone_ignores_protection_and_at_zero_knocks_out_with_a_kill_credited_to_no_one()
    {
        var (m, a, _) = Arrange();
        Assert.True(m.Sim.IsProtected(m.Tank(a)));
        m.Run(1);
        m.Sim.HurtByZone(a, 100);
        Assert.Equal(Max - 100, Health(m, a));
        Assert.DoesNotContain(m.Sim.Events.ToArray(), e => e.Type is GameEventType.TankDestroyed or GameEventType.TankHit);

        m.Run(1);
        m.Sim.HurtByZone(a, Max);
        Assert.True(m.Tank(a).IsDestroyed);
        Assert.Equal(0, Health(m, a));
        GameEvent[] events = m.Sim.Events.ToArray();
        GameEvent kill = Assert.Single(events, e => e.Type == GameEventType.TankDestroyed);
        Assert.Equal((0, 0, a, KillCause.Zone), (kill.TankId, kill.ShellId, kill.TargetTankId, kill.Cause));
        Assert.Equal(m.Sim.Tick, kill.Tick);
        Assert.Equal(20f, kill.Position.X, 3);
        Assert.Equal(100f, kill.Position.Z, 3);
        Assert.DoesNotContain(events, e => e.Type == GameEventType.TankHit);

        int count = m.Sim.Events.Length;
        m.Sim.HurtByZone(a, 50);   // a wreck: nothing
        m.Sim.HurtByZone(a, 0);
        Assert.Equal(count, m.Sim.Events.Length);
        Assert.Throws<ArgumentException>(() => m.Sim.HurtByZone(999, 10));
    }

    [Fact]
    public void Damage_of_nothing_or_less_does_nothing()
    {
        var (m, a, _) = Arrange();
        m.Run(1);
        m.Sim.HurtByZone(a, 0);
        m.Sim.HurtByZone(a, -5);
        Assert.Equal(Max, Health(m, a));
    }

    [Fact]
    public void Shell_kills_carry_the_shell_cause()
    {
        var m = new RulesMatch(BattleMode.FreeForAll);
        int a = m.Join(0), b = m.Join(1), c = m.Join(2);
        m.LineUp(a, b, c);
        m.Run(ModeRules.ProtectionTicks + 1);
        m.Weaken(b, 1);
        m.Attack(a, b);
        List<GameEvent> events = m.Run(2 * Tps);
        GameEvent kill = Assert.Single(events, e => e.Type == GameEventType.TankDestroyed);
        Assert.Equal((a, KillCause.Shell), (kill.TankId, kill.Cause));
        Assert.All(events, e => Assert.Equal(KillCause.Shell, e.Cause));
    }

    [Fact]
    public void Ring_0_hurts_no_one()
    {
        var (m, a, b) = Arrange();
        RunTo(m, ZoneSchedule.ShrinkStart(1) - 1);
        Assert.Equal(Max, Health(m, a));
        Assert.Equal(Max, Health(m, b));
        Assert.Equal(ZonePhase.Waiting, m.Rules.Zone.Phase);
        Assert.Equal(0, m.Rules.Zone.DamagePercent);
    }

    [Fact]
    public void Outside_the_circle_a_tank_loses_the_rings_share_each_whole_second()
    {
        var (m, a, b) = Arrange();
        int one = ZoneSchedule.DamageOf(Max, 1), two = ZoneSchedule.DamageOf(Max, 2);
        RunTo(m, 2700);   // ring 1 begins to shrink: 1 %
        Assert.Equal(ZonePhase.Shrinking, m.Rules.Zone.Phase);
        Assert.Equal(Max - one, Health(m, a));
        RunTo(m, 2729);
        Assert.Equal(Max - one, Health(m, a));
        RunTo(m, 2730);
        Assert.Equal(Max - 2 * one, Health(m, a));
        Assert.Equal(Max, Health(m, b));   // inside

        RunTo(m, 5399);
        m.Weaken(a, Max);   // topped up so it lasts
        RunTo(m, 5400);   // ring 2 waits: ring 1's 1 % still applies
        Assert.Equal((2, ZonePhase.Waiting), (m.Rules.Zone.Ring, m.Rules.Zone.Phase));
        Assert.Equal(Max - one, Health(m, a));
        RunTo(m, 7170);
        Assert.Equal(Max - one * 60, Health(m, a));
        m.Weaken(a, Max);
        RunTo(m, 7199);
        Assert.Equal(Max, Health(m, a));
        RunTo(m, 7200);   // ring 2 shrinks: 2 %
        Assert.Equal(Max - two, Health(m, a));
        Assert.Equal(Max, Health(m, b));
    }

    [Fact]
    public void A_tank_that_drives_out_starts_losing_health_on_the_next_whole_second()
    {
        var (m, _, b) = Arrange();
        RunTo(m, 2705);
        Assert.Equal(Max, Health(m, b));
        m.Place(b, 180, 100);
        RunTo(m, 2729);
        Assert.Equal(Max, Health(m, b));
        RunTo(m, 2730);
        Assert.Equal(Max - ZoneSchedule.DamageOf(Max, 1), Health(m, b));
    }

    [Fact]
    public void Once_closed_ten_percent_a_second_knocks_a_full_tank_out_on_the_tenth_and_no_one_scores()
    {
        var (m, a, b) = Arrange();
        RunToKeepingAll(m, ZoneSchedule.ClosedAt - 1);
        RunTo(m, ZoneSchedule.ClosedAt);   // 16200
        Assert.Equal(ZonePhase.Closed, m.Rules.Zone.Phase);
        Assert.Equal(Max - ZoneSchedule.DamageOf(Max, 10), Health(m, a));
        RunTo(m, 16469);
        Assert.False(m.Tank(a).IsDestroyed);
        Assert.Equal(MatchPhase.Playing, m.Rules.Phase);
        RunTo(m, 16470);
        Assert.True(m.Tank(a).IsDestroyed);
        int at = m.Rules.StartTick + 16470;
        Assert.Equal((0, 1, false, at), (m.Rules.Of(0).Kills, m.Rules.Of(0).Deaths, m.Rules.Of(0).Alive, m.Rules.Of(0).EliminatedAt));
        Assert.Equal((0, 1, at), (m.Rules.Of(1).Kills, m.Rules.Of(1).Deaths, m.Rules.Of(1).EliminatedAt));   // inside it too: it closed on both
        Assert.Equal(MatchPhase.Ended, m.Rules.Phase);
        Assert.Equal(MatchEndReason.Eliminated, m.Rules.EndReason);
        Assert.Equal(16470, m.Rules.LastedTicks(0));
    }

    [Fact]
    public void Outside_a_royale_the_rings_are_ignored()
    {
        var (m, a, _) = Arrange(BattleMode.FreeForAll);
        Assert.Empty(m.Rules.ZoneRings);
        RunTo(m, 2760);
        Assert.Equal(Max, Health(m, a));
        Assert.Equal(ZonePhase.None, m.Rules.Zone.Phase);
        Assert.Equal(0, m.Rules.KnownRings);
        Assert.Equal(0, m.Rules.AnnouncedRings.Length);
    }

    [Fact]
    public void A_royale_without_rings_has_no_zone()
    {
        var (m, a, _) = Arrange(zone: false);
        Assert.Empty(m.Rules.ZoneRings);
        Assert.Equal(ZonePhase.None, m.Rules.Zone.Phase);
        Assert.Equal(0, m.Rules.KnownRings);
        RunTo(m, 2760);
        Assert.Equal(Max, Health(m, a));
        Assert.Equal(ZonePhase.None, m.Rules.Zone.Phase);
        Assert.Equal(0, m.Rules.KnownRings);
    }

    [Fact]
    public void Bad_rings_are_refused()
    {
        var sim = new Simulation(TestWorld.FlatField());
        var e = Assert.Throws<ArgumentException>(() => new MatchRules(BattleMode.Royale, sim, RulesMatch.Layout, zone: Rings.Take(5).ToArray()));
        Assert.Equal("zone", e.ParamName);
        ZoneRing[] wide = Rings.ToArray();
        wide[2] = new ZoneRing(new Vector2(112, 100), 35);
        Assert.Throws<ArgumentException>(() => new MatchRules(BattleMode.Royale, sim, RulesMatch.Layout, zone: wide));
    }

    [Fact]
    public void The_rules_keep_their_own_copy_of_the_rings()
    {
        ZoneRing[] rings = Rings.ToArray();
        var m = new RulesMatch(BattleMode.Royale, zone: rings);
        rings[1] = new ZoneRing(new Vector2(100, 100), 1);
        Assert.Equal(Rings, m.Rules.ZoneRings);
        Assert.Equal(Rings[0], m.Rules.Zone.Current);
        Assert.Equal(Rings[1], m.Rules.Zone.Next);
    }

    [Fact]
    public void Rings_are_announced_as_their_waiting_begins()
    {
        var (m, _, _) = Arrange();
        Assert.Equal(2, m.Rules.KnownRings);
        Assert.Equal(Rings[..2], m.Rules.AnnouncedRings.ToArray());
        Assert.Equal((1, ZonePhase.Waiting), (m.Rules.Zone.Ring, m.Rules.Zone.Phase));
        RunTo(m, 5399);
        Assert.Equal(2, m.Rules.KnownRings);
        RunTo(m, 5400);
        Assert.Equal(3, m.Rules.KnownRings);
        Assert.Equal(Rings[..3], m.Rules.AnnouncedRings.ToArray());
        RunToKeepingAll(m, 14099);
        Assert.Equal(5, m.Rules.KnownRings);
        RunTo(m, 14100);
        Assert.Equal(6, m.Rules.KnownRings);
        Assert.Equal(Rings, m.Rules.AnnouncedRings.ToArray());
    }

    [Fact]
    public void Of_two_outside_the_weaker_falls_first_and_places_behind_the_last_one_left()
    {
        var m = new RulesMatch(BattleMode.Royale, zone: Rings);
        int a = m.Join(0), b = m.Join(1);
        m.Place(a, 20, 100);
        m.Place(b, 180, 100);
        m.Weaken(a, 100);
        RunTo(m, 2700 + 20 * Tps);
        Assert.True(m.Tank(a).IsDestroyed);
        Assert.False(m.Tank(b).IsDestroyed);
        Assert.Equal(MatchPhase.Ended, m.Rules.Phase);
        Assert.Equal(MatchEndReason.Eliminated, m.Rules.EndReason);
        Assert.Equal(1, m.Rules.WinnerSlot);
        Assert.Equal((1, 2), (m.Rules.Of(1).Rank, m.Rules.Of(0).Rank));
        Assert.Equal(0, m.Rules.Of(1).Kills);
    }

    /// <summary>Steps the simulation <paramref name="ticks"/> times without the rules' update (a server that fell behind, say).</summary>
    private static void StepOnly(RulesMatch m, int ticks)
    {
        var inputs = new InputCmd[m.Sim.TankCount];
        for (int i = 0; i < ticks; i++)
        {
            for (int k = 0; k < inputs.Length; k++) inputs[k] = new InputCmd(0, 0, 0, m.Sim.Tanks[k].Yaw, 0f, false);
            m.Sim.Step(inputs);
        }
    }

    [Fact]
    public void An_update_skipped_over_a_whole_second_still_hurts_once_for_it()
    {
        var (m, a, _) = Arrange();
        int one = ZoneSchedule.DamageOf(Max, 1);
        RunTo(m, 2729);
        Assert.Equal(Max - one, Health(m, a));   // 2700
        StepOnly(m, 2);   // 2730 goes by without an update
        m.Run(1);   // 2732
        Assert.Equal(Max - 2 * one, Health(m, a));
        RunTo(m, 2759);
        Assert.Equal(Max - 2 * one, Health(m, a));   // and not again on the way to the next second

        StepOnly(m, 31);   // 2760 and 2790 both go by
        m.Run(1);   // 2791
        Assert.Equal(Max - 4 * one, Health(m, a));
        RunTo(m, 2819);
        Assert.Equal(Max - 4 * one, Health(m, a));
        RunTo(m, 2820);
        Assert.Equal(Max - 5 * one, Health(m, a));
    }

    [Fact]
    public void On_the_tick_a_ring_starts_to_shrink_it_hasnt_moved_but_costs_the_shrinks_share()
    {
        var m = new RulesMatch(BattleMode.Royale, zone: Rings);
        int far = m.Join(0), near = m.Join(1), safe = m.Join(2);
        m.Place(far, 20, 100);     // outside ring 1 (and ring 0)
        m.Place(near, 75, 100);    // inside ring 0 (25 m from its centre) but outside ring 1 (35 m from (110, 100))
        m.Place(safe, 110, 100);
        RunToKeepingAll3(m, 5400);
        RunTo(m, 7199);
        int healthNear = Health(m, near);
        RunTo(m, 7200);   // ring 2 begins to shrink, still ring 1 for now: 2 %
        ZoneState zone = m.Rules.Zone;
        Assert.Equal((2, ZonePhase.Shrinking, 2), (zone.Ring, zone.Phase, zone.DamagePercent));
        Assert.Equal(Rings[1], zone.Current);
        Assert.Equal(healthNear - ZoneSchedule.DamageOf(Max, 2), Health(m, near));   // outside ring 1: 2 % now, not ring 1's 1 %
        Assert.Equal(Max, Health(m, safe));

        var first = new RulesMatch(BattleMode.Royale, zone: Rings);   // ring 1's start: ring 0 still, 1 %
        int inRing0 = first.Join(0), outRing0 = first.Join(1);
        first.Place(inRing0, 75, 100);
        first.Place(outRing0, 20, 100);
        RunTo(first, 2700);
        Assert.Equal(Rings[0], first.Rules.Zone.Current);
        Assert.Equal(Max, Health(first, inRing0));
        Assert.Equal(Max - ZoneSchedule.DamageOf(Max, 1), Health(first, outRing0));
    }

    /// <summary>As <see cref="RunToKeepingAll"/> for three seats.</summary>
    private static void RunToKeepingAll3(RulesMatch m, int elapsed)
    {
        while (Elapsed(m) < elapsed)
        {
            m.Run(Math.Min(5 * Tps, elapsed - Elapsed(m)));
            for (byte slot = 0; slot < 3; slot++) m.Weaken(m.Rules.Of(slot).TankId, Max);
        }
    }

    [Fact]
    public void Two_the_zone_takes_on_one_tick_rank_by_the_health_they_had_before_it()
    {
        // Once closed every tank is outside. Slot 1 goes into the killing second with more health than slot 0: it places first.
        var m = new RulesMatch(BattleMode.Royale, zone: Rings);
        int a = m.Join(0), b = m.Join(1);
        m.Place(a, 112, 100);
        m.Place(b, 112, 104);
        RunToKeepingAll(m, ZoneSchedule.ClosedAt - 10);
        int tenth = ZoneSchedule.DamageOf(Max, 10);
        m.Weaken(a, tenth / 4);
        m.Weaken(b, tenth - 1);
        RunTo(m, ZoneSchedule.ClosedAt);
        Assert.True(m.Tank(a).IsDestroyed && m.Tank(b).IsDestroyed);
        Assert.Equal(m.Rules.Of(0).EliminatedAt, m.Rules.Of(1).EliminatedAt);
        Assert.Equal(MatchPhase.Ended, m.Rules.Phase);
        Assert.Equal(1, m.Rules.WinnerSlot);
        Assert.Equal((1, 2), (m.Rules.Of(1).Rank, m.Rules.Of(0).Rank));
    }

    [Fact]
    public void A_shell_and_the_zone_on_one_tick_credit_the_shooter_the_shells_damage_and_the_kill_to_the_zone()
    {
        // Dry runs find the tick to fire on so the shot lands on a whole second in ring 1's shrinking, and what it does then (its
        // rolls come from the tick it was fired on, so the real run fires on that very tick); the real run leaves the target one
        // point above that: the shell leaves 1, the zone takes it on the same tick.
        const int Second = 2850;
        int fire = Second - 5, landed = 0, damage = 0;
        for (int attempt = 0; attempt < 4 && landed != Second; attempt++)
        {
            (landed, damage) = Shot(fire);
            if (landed != Second) fire += Second - landed;
        }
        Assert.Equal(Second, landed);
        Assert.True(fire > Second - Tps, $"fired at {fire}: a whole second before the hit would hurt the target first");

        var m = ShootingMatch(out int shooter, out int target);
        RunToKeepingAll(m, fire - 1);
        m.Weaken(target, damage + 1);
        m.Run(1);   // the rules note the health
        m.Attack(shooter, target);
        List<GameEvent> events = m.Run(Second - Elapsed(m));
        GameEvent hit = Assert.Single(events, e => e.Type == GameEventType.TankHit && e.TargetTankId == target);
        GameEvent kill = Assert.Single(events, e => e.Type == GameEventType.TankDestroyed);
        Assert.Equal(m.Rules.StartTick + Second, hit.Tick);
        Assert.Equal(hit.Tick, kill.Tick);
        Assert.Equal(damage, hit.Damage);
        Assert.Equal((0, KillCause.Zone, target), (kill.TankId, kill.Cause, kill.TargetTankId));
        Assert.Equal(damage, m.Rules.Of(0).Damage);
        Assert.Equal(0, m.Rules.Of(0).Kills);
        Assert.Equal(1, m.Rules.Of(1).Deaths);
    }

    /// <summary>Slot 0 at (62, 100) inside ring 0 facing west at slot 1, 42 m off at (20, 100) outside it, facing east.</summary>
    private static RulesMatch ShootingMatch(out int shooter, out int target)
    {
        var m = new RulesMatch(BattleMode.Royale, zone: Rings);
        shooter = m.Join(0);
        target = m.Join(1);
        m.Place(shooter, 62, 100, MathF.PI / 2);
        m.Place(target, 20, 100, -MathF.PI / 2);
        return m;
    }

    /// <summary>Fires once at <paramref name="fireAt"/> ticks in; the tick (since the start) it lands on and the damage it does.</summary>
    private static (int Landed, int Damage) Shot(int fireAt)
    {
        var m = ShootingMatch(out int shooter, out int target);
        RunToKeepingAll(m, fireAt - 1);
        m.Run(1);
        m.Attack(shooter, target);
        for (int t = 1; t <= 3 * Tps; t++)
        {
            foreach (GameEvent e in m.Run(1))
                if (e.Type == GameEventType.TankHit && e.TargetTankId == target && e.Damage > 0) return (Elapsed(m), e.Damage);
        }
        Assert.Fail("the shot never landed");
        return default;
    }

    [Fact]
    public void Hurting_outside_the_circle_allocates_nothing()
    {
        Allocation.AssertNone(() =>
        {
            var (m, a, _) = Arrange();
            var inputs = new InputCmd[2];
            void Tick()
            {
                for (int k = 0; k < 2; k++) inputs[k] = new InputCmd(0, 0, 0, m.Sim.Tanks[k].Yaw, 0f, false);
                m.Sim.Step(inputs);
                m.Rules.Update();
            }
            while (Elapsed(m) < 2700) Tick();   // warm-up into ring 1's shrinking
            Allocation.Window window = Allocation.Start();
            for (int i = 0; i < 300; i++) Tick();
            Allocation.Measured measured = window.Stop();
            Assert.Equal(Max - 11 * ZoneSchedule.DamageOf(Max, 1), Health(m, a));
            return measured;
        });
    }
}
