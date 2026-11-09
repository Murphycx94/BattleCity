using System.Numerics;
using Tank.Sim.Combat;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Tests;

/// <summary>What the rules count for the scoreboard besides kills (stage 6 design §2.4): damage dealt, entrants, time lasted.</summary>
public sealed class MatchStatsTests
{
    private const int Tps = SimConstants.TickRate;

    private static readonly ZoneRing[] Rings =
    {
        new(new Vector2(100, 100), 40),
        new(new Vector2(110, 100), 30),
        new(new Vector2(112, 100), 20),
        new(new Vector2(112, 100), 12),
        new(new Vector2(112, 100), 6),
        new(new Vector2(112, 100), 0),
    };

    /// <summary>Runs tick by tick until <paramref name="shooter"/>'s shell does <paramref name="target"/> harm (at most 20 s); its events.</summary>
    private static List<GameEvent> RunUntilHurt(RulesMatch m, int shooter, int target)
    {
        var events = new List<GameEvent>();
        for (int i = 0; i < 20 * Tps; i++)
        {
            events.AddRange(m.Run(1));
            if (events.Any(e => e.Type is GameEventType.TankHit or GameEventType.SplashDamage && e.TankId == shooter && e.TargetTankId == target && e.Damage > 0))
                return events;
        }
        Assert.Fail($"tank {shooter} never hurt tank {target}");
        return events;
    }

    private static int Dealt(IEnumerable<GameEvent> events, int shooter, int target) =>
        events.Where(e => e.Type is GameEventType.TankHit or GameEventType.SplashDamage && e.TankId == shooter && e.TargetTankId == target).Sum(e => e.Damage);

    private static void Hold(RulesMatch m, int id) => m.Order(id, new InputCmd(0, 0, 0, m.Tank(id).Yaw, 0f, false));

    [Fact]
    public void A_hit_counts_as_damage_dealt_but_never_more_than_the_target_had_left()
    {
        var m = new RulesMatch(BattleMode.FreeForAll);
        int a = m.Join(0), b = m.Join(1), c = m.Join(2);
        m.LineUp(a, b, c);
        m.Run(ModeRules.ProtectionTicks + 1);
        m.Attack(a, b);
        List<GameEvent> first = RunUntilHurt(m, a, b);
        Hold(m, a);
        first.AddRange(m.Run(Tps));   // nothing else in flight: a reloads for longer than its shell flies
        int dealt = Dealt(first, a, b);
        Assert.True(dealt > 50, $"the first hit did {dealt}");
        Assert.Equal(TankCatalog.Medium.MaxHealth - dealt, m.Tank(b).Health);
        Assert.Equal(dealt, m.Rules.Of(0).Damage);
        Assert.Equal((0, 0), (m.Rules.Of(1).Damage, m.Rules.Of(2).Damage));

        m.Weaken(b, 50);
        m.Run(1);   // the rules see it down to 50
        m.Attack(a, b);
        List<GameEvent> second = RunUntilHurt(m, a, b);
        Assert.True(m.Tank(b).IsDestroyed);
        Assert.True(Dealt(second, a, b) > 50, $"the killing hit did {Dealt(second, a, b)}");
        Assert.Equal(dealt + 50, m.Rules.Of(0).Damage);
        m.Run(Tps);
        Assert.Equal(dealt + 50, m.Rules.Of(0).Damage);   // a wreck takes no more
    }

    [Fact]
    public void Two_shells_on_one_tick_count_no_more_than_the_target_had_left()
    {
        // Slots 1 and 2 stand mirrored 40 m either side of slot 0's line of fire and both shoot at its hull front: the shells land on
        // one tick. A dry run gives the two hits' damage; the real one leaves the target between the first's and both's, so the
        // second counts only what the first left.
        (GameEvent first, GameEvent second) = TwoHits(targetHealth: null);
        Assert.Equal(first.Tick, second.Tick);
        int health = first.Damage + Math.Max(1, second.Damage / 2);
        Assert.True(health < first.Damage + second.Damage);

        (GameEvent a, GameEvent b) = TwoHits(targetHealth: health, out RulesMatch m);
        Assert.Equal((first.Damage, second.Damage, first.TankId), (a.Damage, b.Damage, a.TankId));
        Assert.True(m.Tank(a.TargetTankId).IsDestroyed);
        byte slotA = (byte)(a.TankId == m.Rules.Of(1).TankId ? 1 : 2), slotB = (byte)(3 - slotA);
        Assert.Equal(a.Damage, m.Rules.Of(slotA).Damage);
        Assert.Equal(health - a.Damage, m.Rules.Of(slotB).Damage);
        Assert.Equal(health, m.Rules.Of(1).Damage + m.Rules.Of(2).Damage);
        Assert.Equal(1, m.Rules.Of(slotB).Kills);
    }

    private static (GameEvent First, GameEvent Second) TwoHits(int? targetHealth) => TwoHits(targetHealth, out _);

    /// <summary>Slot 0 at (100, 110) facing south (+z); slots 1 and 2 at (80, 150) and (120, 150) firing at it; the first two hits on it, in order.</summary>
    private static (GameEvent First, GameEvent Second) TwoHits(int? targetHealth, out RulesMatch m)
    {
        m = new RulesMatch(BattleMode.FreeForAll);
        int target = m.Join(0), left = m.Join(1), right = m.Join(2);
        m.Place(target, 100, 110, MathF.PI);
        m.Place(left, 80, 150, MathF.Atan2(-20f, 36.9f));   // facing it
        m.Place(right, 120, 150, MathF.Atan2(20f, 36.9f));
        m.Run(ModeRules.ProtectionTicks + 1);
        m.Weaken(target, targetHealth ?? TankCatalog.Medium.MaxHealth);
        m.Run(1);   // the rules note the health; both runs fire on the same tick, so their shots roll the same
        m.Attack(left, target);
        m.Attack(right, target);
        var hits = new List<GameEvent>();
        for (int i = 0; i < 3 * Tps && hits.Count < 2; i++)
            hits.AddRange(m.Run(1).Where(e => e.Type == GameEventType.TankHit && e.TargetTankId == target && e.Damage > 0));
        Assert.True(hits.Count >= 2, $"only {hits.Count} hits");
        return (hits[0], hits[1]);
    }

    [Fact]
    public void Hurting_a_team_mate_or_oneself_counts_for_nothing()
    {
        var m = new RulesMatch(BattleMode.TeamBattle);
        int a = m.Join(0, 0), mate = m.Join(1, 0), enemy = m.Join(2, 1);
        m.LineUp(a, mate, enemy);
        m.Run(ModeRules.ProtectionTicks + 1);
        m.Attack(a, mate, AmmoType.HE);
        List<GameEvent> events = RunUntilHurt(m, a, mate);
        Hold(m, a);
        events.AddRange(m.Run(Tps));
        Assert.True(m.Tank(mate).Health < TankCatalog.Medium.MaxHealth, "the team-mate was hurt");
        Assert.DoesNotContain(events, e => e.Type == GameEventType.SplashDamage && e.TargetTankId == a);   // a shooter's own burst spares it
        Assert.Equal(0, m.Rules.Of(0).Damage);
        Assert.Equal(0, m.Rules.Of(1).Damage);
    }

    [Fact]
    public void The_zone_credits_its_damage_to_no_one()
    {
        var m = new RulesMatch(BattleMode.Royale, zone: Rings);
        int a = m.Join(0), b = m.Join(1);
        m.Place(a, 20, 100);
        m.Place(b, 100, 100);
        m.Run(2700 + 10 * Tps);
        Assert.True(m.Tank(a).Health < TankCatalog.Medium.MaxHealth);
        Assert.Equal((0, 0), (m.Rules.Of(0).Damage, m.Rules.Of(1).Damage));
    }

    [Fact]
    public void Entrants_count_everyone_who_joined_leavers_included()
    {
        var ffa = new RulesMatch(BattleMode.FreeForAll);
        for (byte slot = 0; slot < 3; slot++) ffa.Join(slot);
        Assert.Equal(3, ffa.Rules.Entrants);

        var team = new RulesMatch(BattleMode.TeamBattle);
        team.Join(0, 0);
        team.Join(1, 1);
        Assert.Equal(2, team.Rules.Entrants);
        team.Run(5 * Tps);
        team.Join(2, 0);   // mid-match
        Assert.Equal(3, team.Rules.Entrants);
        team.Rules.Leave(1);
        Assert.Equal(3, team.Rules.Entrants);
    }

    [Fact]
    public void Entrants_count_a_seat_that_leaves_and_comes_back_once()
    {
        // a team battle takes players mid-match: the wire holds at most 16 entrants, so a seat coming back must not count again
        var team = new RulesMatch(BattleMode.TeamBattle);
        team.Join(0, 0);
        team.Join(1, 1);
        for (int i = 0; i < 20; i++)
        {
            team.Join(2, 0);
            team.Run(2);
            team.Rules.Leave(2);
            team.Run(2);
        }
        Assert.Equal(3, team.Rules.Entrants);
        team.Join(2, 1);
        team.Join(9, 0);
        Assert.Equal(4, team.Rules.Entrants);
    }

    [Fact]
    public void A_zone_kill_ends_the_time_lasted()
    {
        var m = new RulesMatch(BattleMode.Royale, zone: Rings);
        int a = m.Join(0), b = m.Join(1), c = m.Join(2);
        m.Place(a, 20, 100);
        m.Place(b, 100, 100);
        m.Place(c, 112, 108);
        m.Weaken(a, 100);   // 12 seconds of 1 %: out at 2700 + 11 s
        m.Run(2700 + 20 * Tps);
        int fell = 2700 + 11 * Tps;
        Assert.Equal(fell, m.Rules.Of(0).EliminatedAt);
        Assert.Equal(fell, m.Rules.LastedTicks(0));
        Assert.Equal(MatchPhase.Playing, m.Rules.Phase);
        Assert.Equal(m.Sim.Tick, m.Rules.LastedTicks(1));   // still in: up to now
        Assert.Equal(0, m.Rules.LastedTicks(3));
    }
}
