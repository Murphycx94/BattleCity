using Tank.Sim.Modes;
using Tank.Testing;

namespace Tank.Sim.Tests;

/// <summary>
/// Duos judged and ranked by team (stage 6 design §2.3, §5): the last team with a live tank wins; the rest place by the
/// tick their last tank went out, those out on one tick by the health that tank (or those tanks) had going into it, then
/// by kills, then by team number; both of a team share its place.
/// </summary>
public sealed class DuoRankTests
{
    private const int Tps = SimConstants.TickRate;

    /// <summary>More than any tank's health: the zone's bite that wrecks it on the spot.</summary>
    private const int Wreck = 10_000;

    /// <summary>A duos match with seat k joined on <paramref name="teams"/>[k]; the tank ids by seat.</summary>
    private static (RulesMatch Match, int[] Ids) Duos(byte[] teams, int? durationTicks = null)
    {
        var m = new RulesMatch(BattleMode.Duos, seed: 3, durationTicks: durationTicks);
        int[] ids = new int[teams.Length];
        for (byte slot = 0; slot < teams.Length; slot++) ids[slot] = m.Join(slot, teams[slot]);
        return (m, ids);
    }

    /// <summary>One tick with every tank holding still; <paramref name="wreck"/> are wrecked by the zone on it, all on that tick.</summary>
    private static void Step(RulesMatch m, params int[] wreck)
    {
        var inputs = new InputCmd[m.Sim.TankCount];
        for (int k = 0; k < inputs.Length; k++) inputs[k] = new InputCmd(0, 0, 0, m.Sim.Tanks[k].Yaw, 0f, false);
        m.Sim.Step(inputs);
        foreach (int id in wreck) m.Sim.HurtByZone(id, Wreck);
        m.Rules.Update();
    }

    /// <summary>Sets <paramref name="id"/>'s health and lets a tick go by, so the rules note it as the health going into the next.</summary>
    private static void Settle(RulesMatch m, int id, int health)
    {
        m.Weaken(id, health);
        Step(m);
    }

    /// <summary>The place both seats of a team got, checking they got the same.</summary>
    private static int TeamRank(RulesMatch m, byte a, byte b)
    {
        Assert.Equal(m.Rules.Of(a).Rank, m.Rules.Of(b).Rank);
        return m.Rules.Of(a).Rank;
    }

    [Fact]
    public void The_last_team_with_a_tank_wins_and_the_rest_place_by_when_their_last_tank_went_out()
    {
        var (m, ids) = Duos(new byte[] { 0, 0, 1, 1, 2, 2 });
        Assert.Equal(3, m.Rules.AliveTeams);
        Assert.Equal(3, m.Rules.Places);

        Step(m, ids[2]);
        Assert.Equal(MatchPhase.Playing, m.Rules.Phase);
        Assert.Equal(3, m.Rules.AliveTeams);
        Step(m);
        Step(m, ids[3]);
        Assert.Equal(MatchPhase.Playing, m.Rules.Phase);
        Assert.Equal(2, m.Rules.AliveTeams);
        Assert.Equal(0, m.Rules.Of(2).Rank);   // no places while the match is on
        Step(m);
        Step(m, ids[4], ids[5]);

        Assert.Equal(MatchPhase.Ended, m.Rules.Phase);
        Assert.Equal(MatchEndReason.Eliminated, m.Rules.EndReason);
        Assert.Equal(1, m.Rules.AliveTeams);
        Assert.Equal((1, 2, 3), (TeamRank(m, 0, 1), TeamRank(m, 4, 5), TeamRank(m, 2, 3)));
        Assert.Equal(0, m.Rules.WinnerTeam);
        Assert.Equal(MatchRules.NoSlot, m.Rules.WinnerSlot);
        Assert.Equal(3, m.Rules.Places);
        Assert.Equal(6, m.Rules.Entrants);
        Assert.Equal(0, m.Rules.Of(6).Rank);   // never joined
        Assert.Equal(0, m.Rules.Of(15).Rank);
    }

    [Fact]
    public void Teams_out_on_one_tick_place_by_the_health_their_last_tanks_had_going_into_it()
    {
        var (m, ids) = Duos(new byte[] { 0, 0, 1, 1, 2, 2 });
        // The first of each fall together, team 1's nearly dead and team 2's whole: that doesn't count, only the last tick's fallen do.
        Settle(m, ids[2], 50);
        Step(m, ids[2], ids[4]);
        Settle(m, ids[3], 300);
        Settle(m, ids[5], 100);
        Step(m, ids[3], ids[5]);

        Assert.Equal(MatchPhase.Ended, m.Rules.Phase);
        Assert.Equal(m.Rules.Of(3).EliminatedAt, m.Rules.Of(5).EliminatedAt);
        Assert.Equal((1, 2, 3), (TeamRank(m, 0, 1), TeamRank(m, 2, 3), TeamRank(m, 4, 5)));
    }

    [Fact]
    public void Teams_out_on_one_tick_with_the_same_health_place_by_kills()
    {
        var (m, ids) = Duos(new byte[] { 0, 0, 1, 1, 2, 2 });
        int shooter = ids[4], victim = ids[0];   // team 2 kills one of team 0
        m.Place(shooter, 60, 150, 0f);
        m.Place(victim, 60, 100, MathF.PI);   // 50 m in front, facing it
        m.Place(ids[1], 180, 30, 0f);
        m.Place(ids[2], 20, 20, 0f);
        m.Place(ids[3], 20, 60, 0f);
        m.Place(ids[5], 180, 180, 0f);
        m.Run(ModeRules.ProtectionTicks + 1);
        m.Weaken(victim, 1);
        m.Attack(shooter, victim);
        for (int s = 0; s < 10 && !m.Tank(victim).IsDestroyed; s++) m.Run(Tps);
        Assert.True(m.Tank(victim).IsDestroyed);
        Assert.Equal(1, m.Rules.Of(4).Kills);
        m.Order(shooter, new InputCmd(0, 0, 0, m.Tank(shooter).Yaw, 0f, false));
        m.Run(2 * Tps);   // whatever else it fired has landed
        Assert.Equal(MatchPhase.Playing, m.Rules.Phase);

        foreach (int id in new[] { ids[2], ids[3], ids[4], ids[5] }) m.Weaken(id, 500);
        Step(m);
        Step(m, ids[2], ids[3], ids[4], ids[5]);   // both teams out on one tick, 1000 between each's two going into it

        Assert.Equal(MatchPhase.Ended, m.Rules.Phase);
        Assert.Equal(0, m.Rules.WinnerTeam);
        Assert.Equal((1, 2, 3), (TeamRank(m, 0, 1), TeamRank(m, 4, 5), TeamRank(m, 2, 3)));   // team 2 has the kill
    }

    [Fact]
    public void Teams_out_on_one_tick_with_the_same_health_and_kills_place_by_team_number_not_seat()
    {
        var (m, ids) = Duos(new byte[] { 5, 5, 3, 3, 6, 6 });
        Step(m, ids[0], ids[1], ids[2], ids[3]);

        Assert.Equal(MatchPhase.Ended, m.Rules.Phase);
        Assert.Equal(6, m.Rules.WinnerTeam);
        Assert.Equal((1, 2, 3), (TeamRank(m, 4, 5), TeamRank(m, 2, 3), TeamRank(m, 0, 1)));
    }

    [Fact]
    public void A_team_is_out_when_its_last_tank_is_however_early_the_first_went()
    {
        var (m, ids) = Duos(new byte[] { 0, 0, 1, 1, 2, 2 });
        Step(m, ids[2]);        // team 1's first, before any of team 2's
        Step(m);
        Step(m, ids[4], ids[5]);
        Assert.Equal(MatchPhase.Playing, m.Rules.Phase);
        Assert.Equal(2, m.Rules.AliveTeams);
        Step(m);
        Step(m, ids[3]);        // team 1's last, after team 2's

        Assert.Equal(MatchPhase.Ended, m.Rules.Phase);
        Assert.Equal((1, 2, 3), (TeamRank(m, 0, 1), TeamRank(m, 2, 3), TeamRank(m, 4, 5)));
    }

    [Fact]
    public void On_the_clock_the_teams_still_in_place_by_the_health_of_their_living()
    {
        var (m, ids) = Duos(new byte[] { 0, 0, 1, 1 }, durationTicks: 60);
        m.Weaken(ids[0], 400);
        m.Weaken(ids[1], 400);   // team 0: two alive, 800 between them
        Step(m, ids[3]);         // team 1: one alive, at a medium's full 900
        Assert.Equal(900, m.Tank(ids[2]).Health);
        m.Run(58);
        Assert.Equal(MatchPhase.Playing, m.Rules.Phase);
        m.Run(1);

        Assert.Equal(MatchPhase.Ended, m.Rules.Phase);
        Assert.Equal(MatchEndReason.TimeUp, m.Rules.EndReason);
        Assert.Equal(1, m.Rules.WinnerTeam);
        Assert.Equal(MatchRules.NoSlot, m.Rules.WinnerSlot);
        Assert.Equal((1, 2), (TeamRank(m, 2, 3), TeamRank(m, 0, 1)));
    }

    [Fact]
    public void On_the_clock_a_teams_living_count_by_the_sum_of_their_health_not_the_best_of_them()
    {
        var (m, ids) = Duos(new byte[] { 0, 0, 1, 1 }, durationTicks: 60);
        m.Weaken(ids[0], 500);
        m.Weaken(ids[1], 500);   // team 0: 1000 between its two, though neither has more than 500
        Step(m, ids[3]);         // team 1: one alive, at a medium's full 900
        Assert.Equal(900, m.Tank(ids[2]).Health);
        m.Run(59);

        Assert.Equal(MatchPhase.Ended, m.Rules.Phase);
        Assert.Equal(MatchEndReason.TimeUp, m.Rules.EndReason);
        Assert.Equal(0, m.Rules.WinnerTeam);
        Assert.Equal((1, 2), (TeamRank(m, 0, 1), TeamRank(m, 2, 3)));
    }

    [Fact]
    public void Teams_out_on_one_tick_count_the_health_of_all_their_fallen_on_it_not_just_one()
    {
        var (m, ids) = Duos(new byte[] { 0, 0, 1, 1, 2, 2 });
        Step(m, ids[4]);           // team 2's first, early
        Settle(m, ids[2], 300);
        Settle(m, ids[3], 300);    // team 1: 600 between the two going into the tick
        Settle(m, ids[5], 500);    // team 2: 500, more than either of team 1's
        Step(m, ids[2], ids[3], ids[5]);

        Assert.Equal(MatchPhase.Ended, m.Rules.Phase);
        Assert.Equal(m.Rules.Of(2).EliminatedAt, m.Rules.Of(3).EliminatedAt);
        Assert.Equal(m.Rules.Of(3).EliminatedAt, m.Rules.Of(5).EliminatedAt);
        Assert.All(new byte[] { 2, 3, 4, 5 }, slot => Assert.Equal(0, m.Rules.Of(slot).Kills));   // the health decides, not the kills
        Assert.Equal(0, m.Rules.WinnerTeam);
        Assert.Equal((1, 2, 3), (TeamRank(m, 0, 1), TeamRank(m, 2, 3), TeamRank(m, 4, 5)));
    }

    [Fact]
    public void Every_team_out_on_one_tick_ends_it_with_the_healthiest_going_into_it_first()
    {
        var (m, ids) = Duos(new byte[] { 0, 0, 1, 1 });
        m.Weaken(ids[0], 100);
        m.Weaken(ids[1], 100);
        m.Weaken(ids[2], 400);
        m.Weaken(ids[3], 400);
        Step(m);
        Step(m, ids[0], ids[1], ids[2], ids[3]);

        Assert.Equal(MatchPhase.Ended, m.Rules.Phase);
        Assert.Equal(MatchEndReason.Eliminated, m.Rules.EndReason);
        Assert.Equal(0, m.Rules.AliveTeams);
        Assert.Equal(1, m.Rules.WinnerTeam);
        Assert.Equal((1, 2), (TeamRank(m, 2, 3), TeamRank(m, 0, 1)));
    }

    [Fact]
    public void A_seat_that_leaves_on_the_start_tick_and_comes_back_on_another_team_takes_its_old_team_out_of_the_places()
    {
        var m = new RulesMatch(BattleMode.Duos, seed: 3);
        m.Join(0, 0);
        m.Rules.Leave(0);
        int a = m.Join(0, 3);
        int b = m.Join(1, 5);
        Assert.Equal(2, m.Rules.Places);

        m.Join(2, 0);   // team 0 is empty again: both its spots are free
        m.Join(3, 0);
        Assert.Equal(3, m.Rules.Places);
        Assert.Equal(4, m.Rules.Entrants);
        Step(m, a, b);   // teams 3 and 5 out on one tick, alike in all but number

        Assert.Equal(MatchPhase.Ended, m.Rules.Phase);
        Assert.Equal(0, m.Rules.WinnerTeam);
        int[] ranks = Enumerable.Range(0, 4).Select(slot => (int)m.Rules.Of((byte)slot).Rank).Distinct().Order().ToArray();
        Assert.Equal(Enumerable.Range(1, m.Rules.Places), ranks);   // 1 … Places, no place without a team in it
        Assert.Equal((1, 2, 3), (TeamRank(m, 2, 3), (int)m.Rules.Of(0).Rank, (int)m.Rules.Of(1).Rank));
    }

    [Fact]
    public void A_match_with_only_one_team_in_ends_on_its_first_update()
    {
        var (m, _) = Duos(new byte[] { 3, 3 });
        Step(m);

        Assert.Equal(MatchPhase.Ended, m.Rules.Phase);
        Assert.Equal(MatchEndReason.Eliminated, m.Rules.EndReason);
        Assert.Equal(3, m.Rules.WinnerTeam);
        Assert.Equal(1, TeamRank(m, 0, 1));
        Assert.Equal(1, m.Rules.Places);
    }

    [Fact]
    public void A_team_whose_two_both_leave_is_out_and_still_counts_among_the_places()
    {
        var (m, ids) = Duos(new byte[] { 0, 0, 1, 1, 2, 2 });
        Step(m);
        m.Rules.Leave(2);
        m.Rules.Leave(3);
        Step(m);
        Assert.Equal(MatchPhase.Playing, m.Rules.Phase);
        Assert.Equal(2, m.Rules.AliveTeams);
        Assert.Equal(3, m.Rules.Places);
        Assert.True(m.Rules.Of(2).EliminatedAt >= 0 && m.Rules.Of(3).EliminatedAt >= 0);
        Step(m);
        Step(m, ids[4], ids[5]);

        Assert.Equal(MatchPhase.Ended, m.Rules.Phase);
        Assert.Equal(3, m.Rules.Places);
        Assert.Equal((1, 2, 3), (TeamRank(m, 0, 1), TeamRank(m, 4, 5), TeamRank(m, 2, 3)));
    }

    [Fact]
    public void Outside_duos_no_teams_are_counted_and_the_places_are_the_entrants()
    {
        foreach (BattleMode mode in new[] { BattleMode.Royale, BattleMode.FreeForAll, BattleMode.Annihilation })
        {
            var m = new RulesMatch(mode);
            m.Join(0, 0);
            m.Join(1, 1);
            m.Join(2, 1);
            m.Rules.Leave(2);
            Assert.Equal(0, m.Rules.AliveTeams);
            Assert.Equal(m.Rules.Entrants, m.Rules.Places);
            Assert.Equal(3, m.Rules.Places);
        }
    }

    [Fact]
    public void Playing_a_duos_match_to_its_end_allocates_nothing()
    {
        Allocation.AssertNone(() =>
        {
            var (m, ids) = Duos(new byte[] { 0, 0, 1, 1, 2, 2, 7, 7 }, durationTicks: 4 * Tps);
            var inputs = new InputCmd[m.Sim.TankCount];
            void Tick(int wreck)
            {
                for (int k = 0; k < inputs.Length; k++) inputs[k] = new InputCmd(0, 0, 0, m.Sim.Tanks[k].Yaw, 0f, false);
                m.Sim.Step(inputs);
                if (wreck != 0) m.Sim.HurtByZone(wreck, Wreck);
                m.Rules.Update();
            }
            for (int i = 0; i < Tps; i++) Tick(0);
            Allocation.Window window = Allocation.Start();
            Tick(ids[2]);
            Tick(ids[3]);
            Tick(ids[6]);
            while (m.Rules.Phase == MatchPhase.Playing) Tick(0);   // to the clock, ranking on the last tick
            Allocation.Measured measured = window.Stop();
            Assert.Equal(MatchEndReason.TimeUp, m.Rules.EndReason);
            Assert.Equal((1, 2, 3, 4), (TeamRank(m, 0, 1), TeamRank(m, 4, 5), TeamRank(m, 6, 7), TeamRank(m, 2, 3)));
            return measured;
        });
    }
}
