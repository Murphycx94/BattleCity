using System.Numerics;
using Tank.Sim.Ai;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;
using Tank.Sim.Vision;

namespace Tank.Sim.Tests;

/// <summary>The modes' rules on a flat field with close-set spawns, so kills come quickly (stage 4 design §2).</summary>
public sealed class MatchRulesTests
{
    private const int Tps = SimConstants.TickRate;

    private static readonly SpawnLayout Layout = RulesMatch.Layout;
    private static readonly TeamBase[] Bases = RulesMatch.Bases;

    [Fact]
    public void Players_spawn_on_their_teams_points_facing_the_enemy_under_protection()
    {
        var m = new RulesMatch(BattleMode.TeamBattle);
        int a = m.Join(0, 0), b = m.Join(1, 1), c = m.Join(2, 0);
        Assert.Equal(new Vector2(60, 150), m.Tank(a).Position);
        Assert.Equal(new Vector2(60, 100), m.Tank(b).Position);
        Assert.Equal(new Vector2(80, 150), m.Tank(c).Position);   // the next free point of team 0
        Assert.Equal(0f, m.Tank(a).Yaw);
        Assert.Equal(MathF.PI, m.Tank(b).Yaw);
        Assert.True(m.Sim.IsProtected(m.Tank(a)));
        Assert.Equal(1, m.Tank(b).Team);
        Assert.Equal(a, m.Rules.Of(0).TankId);
        Assert.True(m.Rules.Of(0).Alive);
        m.Run(ModeRules.ProtectionTicks);
        Assert.True(m.Sim.IsProtected(m.Tank(a)), "protected through the 150th step");
        m.Run(1);
        Assert.False(m.Sim.IsProtected(m.Tank(a)));
        Assert.Throws<InvalidOperationException>(() => m.Join(0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => m.Join(3, 2));
    }

    [Fact]
    public void In_a_free_for_all_every_player_is_their_own_team()
    {
        var m = new RulesMatch(BattleMode.FreeForAll);
        for (byte slot = 0; slot < 10; slot++) m.Join(slot, team: 1);
        Assert.Equal(10, m.Sim.TankCount);
        Assert.Equal(Enumerable.Range(0, 10).Select(i => (byte)i), m.Sim.Tanks.ToArray().Select(t => t.Team));
        Assert.Equal(Layout.Solo.Select(p => p.Position), m.Sim.Tanks.ToArray().Select(t => t.Position));
    }

    [Fact]
    public void A_kill_scores_for_the_killer_and_the_victim_respawns_protected_after_ten_seconds()
    {
        var m = new RulesMatch(BattleMode.TeamBattle);
        int a = m.Join(0, 0), b = m.Join(1, 1);
        m.Run(ModeRules.ProtectionTicks + 1);
        m.Weaken(b, 1);
        m.Attack(a, b);
        m.Run(2 * Tps);
        int fell = m.Rules.Of(1).RespawnAt - ModeRules.RespawnTicks;
        Assert.True(fell > 0, "b was not knocked out");
        Assert.True(m.Tank(b).IsDestroyed);
        Assert.False(m.Rules.Of(1).Alive);
        Assert.Equal(1, m.Rules.Of(0).Kills);
        Assert.Equal(1, m.Rules.Of(1).Deaths);
        Assert.Equal(1, m.Rules.TeamScore(0));
        Assert.Equal(0, m.Rules.TeamScore(1));
        m.Order(a, new InputCmd(0, 0, 0, 0f, 0f, false));
        m.Run(fell + ModeRules.RespawnTicks - m.Sim.Tick - 1);
        Assert.True(m.Tank(b).IsDestroyed, "not yet");
        m.Run(1);   // tick RespawnAt: the rules put it back; the simulation reports it on the next step
        Assert.False(m.Tank(b).IsDestroyed);
        var events = m.Run(1);
        Assert.Contains(events, e => e.Type == GameEventType.TankRevived && e.TargetTankId == b);
        Assert.True(m.Rules.Of(1).Alive);
        Assert.True(m.Sim.IsProtected(m.Tank(b)));
        Assert.Equal(1, m.Tank(b).Team);
        Assert.Contains(m.Tank(b).Position, Layout.Team.Where(p => p.Team == 1).Select(p => p.Position));
        Assert.Equal(MatchPhase.Playing, m.Rules.Phase);
    }

    [Fact]
    public void Killing_a_team_mate_scores_nothing()
    {
        var m = new RulesMatch(BattleMode.TeamBattle);
        int a = m.Join(0, 0), c = m.Join(1, 0);
        m.Sim.TankRef(c).Position = new Vector2(60, 100);   // move the mate into the line of fire
        m.Sim.TankRef(c).Yaw = MathF.PI;
        m.Run(ModeRules.ProtectionTicks + 1);
        m.Weaken(c, 1);
        m.Attack(a, c);
        m.Run(2 * Tps);
        Assert.True(m.Tank(c).IsDestroyed);
        Assert.Equal(0, m.Rules.Of(0).Kills);
        Assert.Equal(1, m.Rules.Of(1).Deaths);
        Assert.Equal(0, m.Rules.TeamScore(0));
    }

    [Fact]
    public void A_team_battle_ends_on_the_clock_for_the_higher_score_or_a_draw()
    {
        var m = new RulesMatch(BattleMode.TeamBattle);
        m.Join(0, 0);
        m.Join(1, 1);
        Assert.Equal(7 * 60 * Tps, m.Rules.TicksLeft);
        m.Run(m.Rules.TicksLeft - 1);
        Assert.Equal(MatchPhase.Playing, m.Rules.Phase);
        m.Run(1);
        Assert.Equal(MatchPhase.Ended, m.Rules.Phase);
        Assert.Equal(MatchRules.NoTeam, m.Rules.WinnerTeam);   // 0 : 0
        Assert.Equal(m.Rules.EndTick, m.Rules.EndedAt);
        Assert.Equal(0, m.Rules.TicksLeft);

        var n = new RulesMatch(BattleMode.TeamBattle);
        int a = n.Join(0, 0), b = n.Join(1, 1);
        n.Run(ModeRules.ProtectionTicks + 1);
        n.Weaken(b, 1);
        n.Attack(a, b);
        n.Run(2 * Tps);
        n.Order(a, new InputCmd(0, 0, 0, 0f, 0f, false));
        n.Run(n.Rules.TicksLeft);
        Assert.Equal(0, n.Rules.WinnerTeam);
    }

    [Fact]
    public void A_leavers_kills_stay_on_the_team_score()
    {
        var m = new RulesMatch(BattleMode.TeamBattle);
        int a = m.Join(0, 0), b = m.Join(1, 1);
        m.Join(2, 0);
        m.Run(ModeRules.ProtectionTicks + 1);
        m.Weaken(b, 1);
        m.Attack(a, b);
        m.Run(2 * Tps);
        Assert.Equal(1, m.Rules.Of(0).Kills);
        Assert.Equal(1, m.Rules.TeamScore(0));
        m.Rules.Leave(0);
        Assert.Equal(1, m.Rules.TeamScore(0));
        Assert.Equal(0, m.Rules.TeamScore(1));
        m.Run(m.Rules.TicksLeft);
        Assert.Equal(0, m.Rules.WinnerTeam);
    }

    [Fact]
    public void Annihilation_has_no_respawns_and_ends_when_a_team_is_wiped_out()
    {
        var m = new RulesMatch(BattleMode.Annihilation);
        int a = m.Join(0, 0), b = m.Join(1, 1), c = m.Join(2, 1);
        m.Run(ModeRules.ProtectionTicks + 1);
        m.Weaken(b, 1);
        m.Attack(a, b);
        m.Run(2 * Tps);
        Assert.True(m.Tank(b).IsDestroyed);
        Assert.Equal(-1, m.Rules.Of(1).RespawnAt);
        Assert.True(m.Rules.Of(1).EliminatedAt > 0);
        Assert.Equal(MatchPhase.Playing, m.Rules.Phase);   // c still stands
        m.Run(ModeRules.RespawnTicks + 10);
        Assert.True(m.Tank(b).IsDestroyed, "no respawn");
        m.Weaken(c, 1);
        m.Attack(a, c);
        m.Run(6 * Tps);   // a reload and the turret's swing
        Assert.Equal(MatchPhase.Ended, m.Rules.Phase);
        Assert.Equal(0, m.Rules.WinnerTeam);
        Assert.Equal(2, m.Rules.Of(0).Kills);
    }

    [Fact]
    public void Annihilation_on_the_clock_is_a_draw_and_a_leaver_counts_as_out()
    {
        var m = new RulesMatch(BattleMode.Annihilation);
        m.Join(0, 0);
        m.Join(1, 1);
        int c = m.Join(2, 1);
        m.Run(10);
        m.Rules.Leave(2);
        Assert.Equal(2, m.Sim.TankCount);
        Assert.Equal(-1, m.Sim.SlotOf(c));
        Assert.False(m.Rules.Of(2).Active);
        Assert.Equal(10, m.Rules.Of(2).EliminatedAt);
        Assert.Equal(1, m.Rules.AliveCount(1));
        Assert.False(m.Rules.AcceptsJoins);
        m.Run(m.Rules.TicksLeft);
        Assert.Equal(MatchPhase.Ended, m.Rules.Phase);
        Assert.Equal(MatchRules.NoTeam, m.Rules.WinnerTeam);
    }

    [Fact]
    public void A_free_for_all_ends_with_the_last_one_standing_and_ranks_the_fallen_by_when_they_fell()
    {
        var m = new RulesMatch(BattleMode.FreeForAll);
        int a = m.Join(0), b = m.Join(1), c = m.Join(2);
        // Line them up: b and c in front of a, 40 m apart, facing it.
        m.Sim.TankRef(a).Position = new Vector2(100, 150);
        m.Sim.TankRef(a).Yaw = 0f;
        m.Sim.TankRef(b).Position = new Vector2(100, 110);
        m.Sim.TankRef(b).Yaw = MathF.PI;
        m.Sim.TankRef(c).Position = new Vector2(140, 110);
        m.Sim.TankRef(c).Yaw = MathF.PI;
        m.Run(ModeRules.ProtectionTicks + 1);
        m.Weaken(b, 1);
        m.Attack(a, b);
        m.Run(2 * Tps);
        Assert.True(m.Tank(b).IsDestroyed);
        Assert.Equal(MatchPhase.Playing, m.Rules.Phase);
        m.Weaken(c, 1);
        m.Attack(a, c);
        m.Run(6 * Tps);
        Assert.Equal(MatchPhase.Ended, m.Rules.Phase);
        Assert.Equal(0, m.Rules.WinnerSlot);
        Assert.Equal(1, m.Rules.Of(0).Rank);
        Assert.Equal(2, m.Rules.Of(2).Rank);   // fell last
        Assert.Equal(3, m.Rules.Of(1).Rank);
        Assert.Equal(2, m.Rules.Of(0).Kills);
    }

    [Fact]
    public void In_a_free_for_all_a_leaver_is_out_and_ranked_and_ties_go_to_the_one_with_more_kills()
    {
        var m = new RulesMatch(BattleMode.FreeForAll);
        int a = m.Join(0), b = m.Join(1);
        int c = m.Join(2);
        m.LineUp(a, b, c);
        m.Run(ModeRules.ProtectionTicks + 1);
        m.Rules.Leave(2);   // c quits first: last place
        Assert.True(m.Rules.Of(2).EliminatedAt > 0);
        m.Weaken(b, 1);
        m.Attack(a, b);
        m.Run(2 * Tps);
        Assert.Equal(MatchPhase.Ended, m.Rules.Phase);
        Assert.Equal(0, m.Rules.WinnerSlot);
        Assert.Equal(1, m.Rules.Of(0).Rank);
        Assert.Equal(2, m.Rules.Of(1).Rank);
        Assert.Equal(3, m.Rules.Of(2).Rank);
        Assert.Equal(-1, m.Sim.SlotOf(c));

        // Time up with equal health: the one with a kill ranks higher.
        var n = new RulesMatch(BattleMode.FreeForAll);
        int x = n.Join(0), y = n.Join(1), z = n.Join(2);
        n.LineUp(y, z, x);
        n.Run(ModeRules.ProtectionTicks + 1);
        n.Weaken(z, 1);
        n.Attack(y, z);
        n.Run(2 * Tps);
        n.Order(y, new InputCmd(0, 0, 0, 0f, 0f, false));
        Assert.Equal(n.Tank(x).Health, n.Tank(y).Health);
        n.Run(n.Rules.TicksLeft);
        Assert.Equal(1, n.Rules.WinnerSlot);
        Assert.Equal(1, n.Rules.Of(1).Rank);
        Assert.Equal(2, n.Rules.Of(0).Rank);
    }

    [Fact]
    public void A_free_for_all_on_the_clock_ranks_the_living_by_health()
    {
        var m = new RulesMatch(BattleMode.FreeForAll);
        int a = m.Join(0), b = m.Join(1), c = m.Join(2);
        m.Weaken(a, 300);
        m.Weaken(c, 500);
        Assert.Equal(10 * 60 * Tps, m.Rules.TicksLeft);
        m.Run(m.Rules.TicksLeft);
        Assert.Equal(MatchPhase.Ended, m.Rules.Phase);
        Assert.Equal(1, m.Rules.WinnerSlot);   // b at full health
        Assert.Equal(1, m.Rules.Of(1).Rank);
        Assert.Equal(2, m.Rules.Of(2).Rank);
        Assert.Equal(3, m.Rules.Of(0).Rank);
        Assert.Equal(MatchRules.NoTeam, m.Rules.WinnerTeam);
    }

    [Fact]
    public void Team_battle_and_standard_take_players_in_mid_match()
    {
        foreach (BattleMode mode in ModeRules.All)
        {
            var m = new RulesMatch(mode);
            Assert.True(m.Rules.AcceptsJoins);
            m.Join(0, 0);
            m.Join(1, 1);
            m.Run(5);
            Assert.Equal(ModeRules.JoinsMidMatch(mode), m.Rules.AcceptsJoins);
            if (mode is BattleMode.TeamBattle or BattleMode.Standard) Assert.Equal(3, m.Join(2, 1) > 0 ? m.Sim.TankCount : 0);
            else Assert.Throws<InvalidOperationException>(() => m.Join(2, 1));
        }
        Assert.Equal(new[] { "teambattle", "annihilation", "ffa", "standard", "royale", "duos" }, ModeRules.All.Select(ModeRules.Id));
        Assert.True(ModeRules.TryParse("FFA", out BattleMode ffa) && ffa == BattleMode.FreeForAll);
        Assert.False(ModeRules.TryParse("coop", out _));
    }

    [Fact]
    public void Spawn_points_in_use_are_skipped_and_reused_once_free()
    {
        var m = new RulesMatch(BattleMode.TeamBattle);
        for (byte slot = 0; slot < 5; slot++) m.Join(slot, 0);
        Assert.Equal(Layout.Team.Where(p => p.Team == 0).Select(p => p.Position), m.Sim.Tanks.ToArray().Select(t => t.Position));
        m.Sim.TankRef(m.Rules.Of(2).TankId).Position = new Vector2(100, 190);   // slot 2 drives off its point
        int f = m.Join(5, 0);
        Assert.Equal(new Vector2(100, 150), m.Tank(f).Position);   // the freed point

        // A wreck on a point keeps it busy: slot 1's tank dies on (80, 150) — the first point that would otherwise be
        // free once slot 5 drives off it too — so the next joiner goes to the one after.
        m.Sim.TankRef(f).Position = new Vector2(120, 190);
        m.Sim.TankRef(m.Rules.Of(1).TankId).IsDestroyed = true;
        m.Sim.TankRef(m.Rules.Of(1).TankId).Health = 0;
        int g = m.Join(6, 0);
        Assert.Equal(new Vector2(100, 150), m.Tank(g).Position);
    }

    [Fact]
    public void With_every_point_taken_the_sixth_player_is_put_beside_the_roomiest_not_on_top_of_anyone()
    {
        var m = new RulesMatch(BattleMode.TeamBattle);
        for (byte slot = 0; slot < 5; slot++) m.Join(slot, 0);
        int f = m.Join(5, 0);
        Vector2 at = m.Tank(f).Position;
        foreach (TankState t in m.Sim.Tanks)
            if (t.Id != f) Assert.True(Vector2.Distance(t.Position, at) >= 5f, $"{at} is on top of {t.Position}");
        Assert.True(Layout.Team.Where(p => p.Team == 0).Min(p => Vector2.Distance(p.Position, at)) <= 3 * 6f + 0.01f, $"{at} is near a point");
        m.Run(10);
        Assert.All(m.Sim.Tanks.ToArray(), t => Assert.Equal(0f, t.Speed));   // nobody is being shoved about
    }

    [Fact]
    public void A_tank_may_respawn_on_the_point_its_own_wreck_lies_on()
    {
        var m = new RulesMatch(BattleMode.TeamBattle);
        int a = m.Join(0, 0), b = m.Join(1, 1);
        Vector2 home = m.Tank(a).Position;
        m.Run(ModeRules.ProtectionTicks + 1);
        m.Weaken(a, 1);
        m.Attack(b, a);
        m.Run(2 * Tps);
        Assert.True(m.Tank(a).IsDestroyed);
        Assert.Equal(home, m.Tank(a).Position);   // died where it stood
        m.Run(ModeRules.RespawnTicks + 1);
        Assert.False(m.Tank(a).IsDestroyed);
        Assert.Equal(home, m.Tank(a).Position);   // and came back there, its wreck not counting against it
    }

    [Fact]
    public void A_shell_in_flight_from_a_player_who_left_still_scores_for_the_team()
    {
        var m = new RulesMatch(BattleMode.TeamBattle);
        int a = m.Join(0, 0), b = m.Join(1, 1);
        m.Run(ModeRules.ProtectionTicks + 1);
        m.Weaken(b, 1);
        m.Attack(a, b);
        var events = new List<GameEvent>();
        while (!events.Any(e => e.Type == GameEventType.ShellFired)) events.AddRange(m.Run(1));
        m.Rules.Leave(0);   // gone while the shell flies
        m.Run(Tps);
        Assert.True(m.Tank(b).IsDestroyed);
        Assert.Equal(1, m.Rules.Of(0).Kills);
        Assert.Equal(1, m.Rules.TeamScore(0));
    }

    [Fact]
    public void A_no_respawn_match_with_one_side_missing_is_decided_at_once()
    {
        // The room never starts such a match (two players, one per team); the rules alone call it on the first update.
        var alone = new RulesMatch(BattleMode.FreeForAll);
        alone.Join(0);
        alone.Run(1);
        Assert.Equal(MatchPhase.Ended, alone.Rules.Phase);
        Assert.Equal(0, alone.Rules.WinnerSlot);

        var oneSided = new RulesMatch(BattleMode.Annihilation);
        oneSided.Join(0, 0);
        oneSided.Join(1, 0);
        oneSided.Run(1);
        Assert.Equal(MatchPhase.Ended, oneSided.Rules.Phase);
        Assert.Equal(0, oneSided.Rules.WinnerTeam);

        var teamBattle = new RulesMatch(BattleMode.TeamBattle);   // a team battle waits for the clock regardless
        teamBattle.Join(0, 0);
        teamBattle.Run(10);
        Assert.Equal(MatchPhase.Playing, teamBattle.Rules.Phase);
    }

    [Fact]
    public void The_mode_constants_are_the_designs_numbers()
    {
        Assert.Equal(16, ModeRules.MaxPlayers);
        Assert.Equal(16, ModeRules.MaxPlayersFor(BattleMode.Royale));
        Assert.Equal(16, ModeRules.MaxPlayersFor(BattleMode.Duos));
        foreach (BattleMode mode in ModeRules.All.Where(m => !ModeRules.HasZone(m))) Assert.Equal(10, ModeRules.MaxPlayersFor(mode));
        Assert.Equal(2, ModeRules.MinPlayers);
        Assert.Equal(5 * Tps, ModeRules.ProtectionTicks);
        Assert.Equal(10 * Tps, ModeRules.RespawnTicks);
        Assert.Equal(8 * Tps, ModeRules.ResultsTicks);
        Assert.Equal(7 * 60 * Tps, ModeRules.DurationTicks(BattleMode.TeamBattle));
        Assert.Equal(7 * 60 * Tps, ModeRules.DurationTicks(BattleMode.Annihilation));
        Assert.Equal(10 * 60 * Tps, ModeRules.DurationTicks(BattleMode.FreeForAll));
        Assert.Equal(10 * 60 * Tps, ModeRules.DurationTicks(BattleMode.Standard));
    }

    [Theory]
    [InlineData(BattleMode.TeamBattle)]
    [InlineData(BattleMode.Annihilation)]
    [InlineData(BattleMode.FreeForAll)]
    [InlineData(BattleMode.Standard)]
    public void Updating_the_rules_and_the_bots_allocates_nothing_even_as_players_come_and_go(BattleMode mode)
    {
        Allocation.AssertNone(() =>
        {
            var sim = new Simulation(BotField.NewBattlefield());
            var rules = new MatchRules(mode, sim, Layout, bases: Bases);
            var vision = new VisionTracker(sim);
            var director = new BotDirector(sim, vision, rules, BotField, BotField.NewNavGrid());
            for (byte slot = 0; slot < 6; slot++)
            {
                int id = rules.Join(slot, TankCatalog.Medium, (byte)(slot % 2));
                if (slot != 3) director.Add(id, seed: (uint)(slot + 1));   // the bots are seated once, at the start; slot 3 is a player's, idle
            }
            director.AssignRoles();
            var inputs = new InputCmd[Simulation.MaxTanks];
            void Tick()
            {
                director.Update();
                int tick = sim.Tick + 1;
                for (int k = 0; k < sim.TankCount; k++)
                {
                    int id = sim.Tanks[k].Id;
                    inputs[k] = director.Find(id) is not null ? director.Command(id, tick) : new InputCmd(tick, 0, 0, 0f, 0f, false);
                }
                sim.Step(inputs.AsSpan(0, sim.TankCount));
                vision.Update();
                rules.Update();
            }
            for (int i = 0; i < 2 * Tps; i++) Tick();
            Allocation.Window window = Allocation.Start();
            for (int i = 0; i < 5 * Tps; i++)
            {
                if (i == Tps) rules.Leave(3);                              // one goes …
                if (i == 2 * Tps && rules.AcceptsJoins) rules.Join(3, TankCatalog.Light, 1);   // … and another takes the seat (a player's: it idles)
                Tick();
            }
            Allocation.Measured measured = window.Stop();
            Assert.Equal(MatchPhase.Playing, rules.Phase);
            Assert.Equal(ModeRules.JoinsMidMatch(mode), rules.Of(3).Active);
            return measured;
        });
    }

    /// <summary>The flat field with <see cref="Layout"/>, <see cref="Bases"/> and four waypoints, for bots (its battlefield and grid built once).</summary>
    private static readonly MapDefinition BotField = new("flat", "flat", () => TestWorld.FlatField(), Layout,
        new Vector2[] { new(40, 40), new(160, 160), new(40, 160), new(160, 40) }, Bases);

    [Fact]
    public void A_configured_duration_sets_the_clock_and_a_match_started_on_a_later_tick_counts_from_there()
    {
        var sim = new Simulation(TestWorld.FlatField());
        void Step()
        {
            var inputs = new InputCmd[sim.TankCount];
            for (int k = 0; k < inputs.Length; k++) inputs[k] = new InputCmd(sim.Tick + 1, 0, 0, sim.Tanks[k].Yaw, 0f, false);
            sim.Step(inputs);
        }
        for (int i = 0; i < 7; i++) Step();   // the simulation is already running when the match is made
        var rules = new MatchRules(BattleMode.Annihilation, sim, Layout, durationTicks: 90);
        Assert.Equal(7 + 90, rules.EndTick);
        Assert.Equal(90, rules.TicksLeft);
        Assert.True(rules.AcceptsJoins, "the tick the match began on");
        rules.Join(0, TankCatalog.Medium, 0);
        rules.Join(1, TankCatalog.Medium, 1);
        for (int i = 0; i < 89; i++)
        {
            Step();
            rules.Update();
        }
        Assert.False(rules.AcceptsJoins, "a no-respawn match takes nobody once it is under way");
        Assert.Equal(MatchPhase.Playing, rules.Phase);
        Assert.Equal(1, rules.TicksLeft);
        Step();
        rules.Update();
        Assert.Equal(MatchPhase.Ended, rules.Phase);
        Assert.Equal(0, rules.TicksLeft);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void A_match_of_no_ticks_is_refused(int ticks)
    {
        var sim = new Simulation(TestWorld.FlatField());
        Assert.Throws<ArgumentOutOfRangeException>(() => new MatchRules(BattleMode.TeamBattle, sim, Layout, durationTicks: ticks));
    }
}
