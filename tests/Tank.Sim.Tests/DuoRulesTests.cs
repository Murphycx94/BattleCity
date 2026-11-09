using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Modes;
using Tank.Sim.Vision;
using Tank.Testing;

namespace Tank.Sim.Tests;

/// <summary>Duos in the match rules: eight teams 0–7, two to a team, the two side by side on their team's point (stage 6 design §2.2, §5).</summary>
public sealed class DuoRulesTests
{
    private const int Tps = SimConstants.TickRate;
    private const uint Seed = 7;

    /// <summary>The solo points' order for <paramref name="seed"/>, worked out here the way the rules shuffle them.</summary>
    private static int[] Order(uint seed, int count)
    {
        int[] order = Enumerable.Range(0, count).ToArray();
        var rng = new XorShift(seed ^ MatchRules.SpawnSalt);
        for (int i = count - 1; i > 0; i--)
        {
            int j = (int)(rng.NextUInt() % (uint)(i + 1));
            (order[i], order[j]) = (order[j], order[i]);
        }
        return order;
    }

    /// <summary>A duos match (or <paramref name="mode"/>) with seats 0–3 joined on teams 0, 0, 5, 5; their tank ids.</summary>
    private static (RulesMatch Match, int[] Ids) FourOnTwoTeams(BattleMode mode = BattleMode.Duos, uint seed = Seed)
    {
        var m = new RulesMatch(mode, seed);
        byte[] teams = { 0, 0, 5, 5 };
        int[] ids = new int[4];
        for (byte slot = 0; slot < 4; slot++) ids[slot] = m.Join(slot, teams[slot]);
        return (m, ids);
    }

    [Fact]
    public void The_two_of_a_team_stand_side_by_side_on_their_teams_point_of_the_seeded_order()
    {
        var (m, ids) = FourOnTwoTeams();
        int[] order = Order(Seed, RulesMatch.Layout.Solo.Count);

        Assert.Equal(4, m.Sim.TankCount);
        for (int k = 0; k < 4; k++)
        {
            byte team = (byte)(k < 2 ? 0 : 5);
            SpawnPoint expected = DuoSpawns.Member(RulesMatch.Layout.Solo[order[team]], k % 2, m.Sim.Field);
            Assert.Equal(expected.Position, m.Tank(ids[k]).Position);
            Assert.Equal(expected.Yaw, m.Tank(ids[k]).Yaw);
            Assert.Equal(team, m.Tank(ids[k]).Team);
            Assert.Equal(team, m.Rules.Of((byte)k).Team);
            Assert.True(m.Rules.Of((byte)k).Active);
        }
        Assert.Equal(DuoSpawns.Spacing, Vector2.Distance(m.Tank(ids[0]).Position, m.Tank(ids[1]).Position), 0.01f);
        Assert.Equal(DuoSpawns.Spacing, Vector2.Distance(m.Tank(ids[2]).Position, m.Tank(ids[3]).Position), 0.01f);
        Assert.Equal(4, m.Rules.Entrants);
    }

    [Fact]
    public void A_third_seat_on_a_team_or_a_team_past_seven_is_refused_without_a_tank()
    {
        var (m, _) = FourOnTwoTeams();

        var full = Assert.Throws<InvalidOperationException>(() => m.Join(4, 0));
        Assert.Contains("Team 0 is full", full.Message);
        Assert.Throws<ArgumentOutOfRangeException>("team", () => m.Join(4, 8));
        Assert.Throws<ArgumentOutOfRangeException>("team", () => m.Join(4, MatchRules.NoTeam));
        Assert.Equal(4, m.Sim.TankCount);
        Assert.False(m.Rules.Of(4).Active);
        m.Join(4, 7);   // the seat may still go on a team with room
        Assert.Equal(5, m.Sim.TankCount);
    }

    [Fact]
    public void A_seat_that_leaves_on_the_start_tick_and_comes_back_to_its_team_takes_the_spot_it_freed_beside_its_mate()
    {
        var m = new RulesMatch(BattleMode.Duos, Seed);
        m.Join(0, 0);
        int mate = m.Join(1, 0);   // on the right
        m.Rules.Leave(0);

        int back = m.Join(0, 0);   // the team has room for it again, and it goes back on the left

        Assert.Equal(2, m.Sim.TankCount);
        Assert.Equal(DuoSpawns.Spacing, Vector2.Distance(m.Tank(back).Position, m.Tank(mate).Position), 0.01f);
        Assert.Equal(1, m.Rules.Places);
        Assert.Contains("Team 0 is full", Assert.Throws<InvalidOperationException>(() => m.Join(2, 0)).Message);
    }

    [Fact]
    public void A_seat_that_leaves_on_the_start_tick_for_another_team_leaves_its_mates_spot_alone()
    {
        var m = new RulesMatch(BattleMode.Duos, Seed);
        int left = m.Join(0, 0);
        int right = m.Join(1, 0);
        Vector2 leftSpot = m.Tank(left).Position, rightSpot = m.Tank(right).Position;
        m.Rules.Leave(1);
        m.Join(1, 4);

        int newcomer = m.Join(2, 0);   // the right-hand spot its mate gave up

        Assert.Equal(rightSpot, m.Tank(newcomer).Position);
        Assert.Equal(leftSpot, m.Tank(left).Position);
        Assert.Equal(2, m.Rules.Places);
    }

    [Fact]
    public void HasSpot_counts_a_leavers_spot_as_taken_except_for_the_leaver_itself_and_never_a_team_past_seven()
    {
        var m = new RulesMatch(BattleMode.Duos, Seed);
        m.Join(0, 0);
        m.Join(1, 0);
        m.Join(2, 1);
        m.Rules.Leave(1);

        Assert.False(m.Rules.HasSpot(5, 0));   // the leaver still holds its spot
        Assert.True(m.Rules.HasSpot(1, 0));    // ... which it may take again
        Assert.True(m.Rules.HasSpot(1, 3));
        m.Join(1, 3);
        Assert.Equal(3, m.Rules.Places);
        Assert.True(m.Rules.HasSpot(5, 0));    // gone to team 3, it gave team 0's spot back
        Assert.False(m.Rules.HasSpot(4, 8));
        Assert.False(m.Rules.HasSpot(4, MatchRules.NoTeam));
        Allocation.AssertNone(() =>
        {
            bool any = false;
            Allocation.Window window = Allocation.Start();
            for (byte team = 0; team < ModeRules.DuoTeams; team++) any |= m.Rules.HasSpot(5, team);
            Allocation.Measured measured = window.Stop();
            Assert.True(any);
            return measured;
        });
    }

    [Fact]
    public void Join_refuses_exactly_where_HasSpot_says_no()
    {
        var m = new RulesMatch(BattleMode.Duos, Seed);
        m.Join(0, 2);
        m.Join(1, 2);

        Assert.False(m.Rules.HasSpot(3, 2));
        Assert.Contains("Team 2 is full", Assert.Throws<InvalidOperationException>(() => m.Join(3, 2)).Message);
        Assert.False(m.Rules.HasSpot(3, 8));
        Assert.Throws<ArgumentOutOfRangeException>("team", () => m.Join(3, 8));
        Assert.True(m.Rules.HasSpot(3, 6));
        m.Join(3, 6);
        Assert.True(m.Rules.HasSpot(4, 6));
        m.Join(4, 6);
        Assert.False(m.Rules.HasSpot(5, 6));
        Assert.Equal(4, m.Sim.TankCount);
    }

    [Fact]
    public void HasSpot_never_refuses_outside_duos()
    {
        var m = new RulesMatch(BattleMode.Royale, Seed);
        m.Join(0, 0);
        m.Join(1, 0);
        m.Join(2, 0);

        Assert.True(m.Rules.HasSpot(3, 0));
        Assert.True(m.Rules.HasSpot(3, 8));
    }

    [Fact]
    public void Duos_wants_a_solo_point_for_each_of_its_eight_teams()
    {
        SpawnPoint[] four = RulesMatch.Layout.Solo.Take(4).ToArray();
        var few = new SpawnLayout(RulesMatch.Layout.Team, four);

        var refused = Assert.Throws<InvalidOperationException>(() => new MatchRules(BattleMode.Duos, new Simulation(TestWorld.FlatField()), few));
        Assert.Contains("8", refused.Message);
        Assert.Contains("4", refused.Message);
        new MatchRules(BattleMode.Royale, new Simulation(TestWorld.FlatField()), few);   // a royale's seats look for room instead
        Assert.True(RiverlandMap.Spawns.Solo.Count >= ModeRules.DuoTeams);
        new MatchRules(BattleMode.Duos, new Simulation(TestWorld.FlatField()), RiverlandMap.Spawns);
        new MatchRules(BattleMode.Duos, new Simulation(TestWorld.FlatField()), few with { Solo = RulesMatch.Layout.Solo.Take(8).ToArray() });
    }

    [Fact]
    public void A_team_shares_its_sight()
    {
        var (m, ids) = FourOnTwoTeams();
        var vision = new VisionTracker(m.Sim);
        m.Place(ids[0], 100, 150, 0f);    // sees the enemy 40 m ahead
        m.Place(ids[1], 20, 20, 0f);      // its mate, blind: it sees the enemy through the other
        m.Place(ids[2], 100, 110, 0f);    // the enemy
        m.Place(ids[3], 180, 20, 0f);
        vision.SetWatching(ids[1], false);
        vision.SetWatching(ids[2], false);
        vision.SetWatching(ids[3], false);

        m.Run(1);
        vision.Update();

        Assert.True(vision.IsVisible(ids[2], 0));
        Assert.True(vision.IsVisible(ids[1], 0));   // its own team always
        Assert.False(vision.IsVisible(ids[0], 5));  // team 5 isn't watching
    }

    [Fact]
    public void Shooting_a_team_mate_counts_neither_damage_nor_a_kill()
    {
        var (m, ids) = FourOnTwoTeams();
        int a = ids[0], mate = ids[1];
        m.Place(a, 60, 150, 0f);
        m.Place(mate, 60, 100, MathF.PI);   // 50 m in front, facing it
        m.Place(ids[2], 180, 30, 0f);
        m.Place(ids[3], 180, 180, 0f);
        m.Run(ModeRules.ProtectionTicks + 1);
        int full = m.Tank(mate).Health;

        m.Attack(a, mate);
        m.Run(2 * Tps);
        Assert.True(m.Tank(mate).Health < full, "the shells hurt the mate");
        Assert.Equal(0, m.Rules.Of(0).Damage);
        m.Weaken(mate, 1);
        for (int s = 0; s < 10 && !m.Tank(mate).IsDestroyed; s++) m.Run(Tps);   // till the next shell lands

        Assert.True(m.Tank(mate).IsDestroyed);
        Assert.Equal(0, m.Rules.Of(0).Kills);
        Assert.Equal(0, m.Rules.Of(0).Damage);
        Assert.Equal(1, m.Rules.Of(1).Deaths);
        Assert.Equal(0, m.Rules.TeamScore(0));
    }

    [Fact]
    public void In_a_royale_the_team_asked_for_is_ignored_and_each_seat_is_its_own_team()
    {
        var (m, ids) = FourOnTwoTeams(BattleMode.Royale);

        for (byte slot = 0; slot < 4; slot++)
        {
            Assert.Equal(slot, m.Rules.Of(slot).Team);
            Assert.Equal(slot, m.Tank(ids[slot]).Team);
        }
        Assert.Equal(4, m.Sim.Tanks.ToArray().Select(t => t.Position).Distinct().Count());
    }

    [Fact]
    public void The_spawns_come_from_the_match_seed()
    {
        static Vector2[] Spots(uint seed) => FourOnTwoTeams(seed: seed).Match.Sim.Tanks.ToArray().Select(t => t.Position).ToArray();

        Assert.Equal(Spots(Seed), Spots(Seed));
        Assert.NotEqual(Spots(Seed), Spots(Seed + 1));
    }

    [Fact]
    public void Sixteen_seats_on_eight_teams_take_eight_points_two_to_each()
    {
        var m = new RulesMatch(BattleMode.Duos, Seed);
        for (byte slot = 0; slot < 16; slot++) m.Join(slot, (byte)(slot / 2));   // eight teams of two on the layout's ten points

        Vector2[] at = m.Sim.Tanks.ToArray().Select(t => t.Position).ToArray();
        Assert.Equal(16, at.Distinct().Count());
        int[] order = Order(Seed, RulesMatch.Layout.Solo.Count);
        for (int team = 0; team < ModeRules.DuoTeams; team++)
        {
            Vector2 point = RulesMatch.Layout.Solo[order[team]].Position;
            Assert.True(Vector2.Distance(point, (at[2 * team] + at[2 * team + 1]) / 2) < 1e-3f, $"team {team} isn't on its point {point}");
        }
    }
}
