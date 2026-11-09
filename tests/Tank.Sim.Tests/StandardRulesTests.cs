using System.Numerics;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Tests;

/// <summary>
/// The standard battle's rules (stage 5 design §3.1) on the flat field of <see cref="MatchRulesTests"/>: two bases, no
/// respawns, ten minutes; a wipe-out or a capture wins, both on one tick draw; and every mode records why it ended.
/// </summary>
public sealed class StandardRulesTests
{
    private const int Tps = SimConstants.TickRate;

    /// <summary>Team 0 along z = 150 facing north, team 1 along z = 100 facing south, 50 m apart; solo spawns in a row.</summary>
    private static readonly SpawnLayout Layout = new(
        Team: Enumerable.Range(0, 5).Select(i => new SpawnPoint(new Vector2(60 + 20 * i, 150), 0f, 0))
            .Concat(Enumerable.Range(0, 5).Select(i => new SpawnPoint(new Vector2(60 + 20 * i, 100), MathF.PI, 1))).ToArray(),
        Solo: Enumerable.Range(0, 10).Select(i => new SpawnPoint(new Vector2(20 + 18 * i, 100 + (i % 2) * 50), (i % 2) * MathF.PI, 0)).ToArray());

    /// <summary>Each 30 m behind its team's spawn line, every spawn point outside the circle.</summary>
    private static readonly TeamBase[] Bases =
    {
        new(0, new Vector2(100, 180), TeamBase.StandardRadius),
        new(1, new Vector2(100, 70), TeamBase.StandardRadius),
    };

    private sealed class Match
    {
        public readonly Simulation Sim = new(TestWorld.FlatField());
        public readonly MatchRules Rules;
        private readonly Dictionary<int, InputCmd> _orders = new();

        public Match(BattleMode mode = BattleMode.Standard, int? durationTicks = null) =>
            Rules = new MatchRules(mode, Sim, Layout, durationTicks, Bases);

        public int Join(byte slot, byte team = 0) => Rules.Join(slot, TankCatalog.Medium, team);

        public void Order(int tankId, InputCmd cmd) => _orders[tankId] = cmd;

        public List<GameEvent> Run(int ticks)
        {
            var events = new List<GameEvent>();
            var inputs = new InputCmd[Simulation.MaxTanks].AsSpan();
            for (int i = 0; i < ticks; i++)
            {
                inputs = inputs[..Sim.TankCount];
                for (int k = 0; k < inputs.Length; k++)
                {
                    TankState t = Sim.Tanks[k];
                    inputs[k] = _orders.TryGetValue(t.Id, out InputCmd cmd) ? cmd : new InputCmd(0, 0, 0, t.Yaw, 0f, false);
                }
                Sim.Step(inputs);
                Rules.Update();
                events.AddRange(Sim.Events);
            }
            return events;
        }

        /// <summary>Runs until the match ends or <paramref name="limit"/> ticks pass.</summary>
        public List<GameEvent> RunToEnd(int limit)
        {
            var events = new List<GameEvent>();
            for (int i = 0; i < limit && Rules.Phase == MatchPhase.Playing; i++) events.AddRange(Run(1));
            return events;
        }

        /// <summary>
        /// Lays <paramref name="shooter"/>'s gun on <paramref name="target"/>'s hull front and keeps firing, its spawn
        /// protection taken off (a protected tank holds its fire).
        /// </summary>
        public void Attack(int shooter, int target)
        {
            Sim.TankRef(shooter).ProtectedUntil = 0;
            TankState s = Tank(shooter), t = Tank(target);
            var point = new Vector3(t.Position.X, 1.02f, t.Position.Y) + Vector3.Transform(new Vector3(0, 0, -3.1f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, t.Yaw));
            var (yaw, pitch) = Angles.AimAngles(point - TankPose.Compute(s, Sim.Field.Terrain).Trunnion(s.Spec));
            Order(shooter, new InputCmd(0, 0, 0, yaw, pitch, true));
        }

        public TankState Tank(int id) => Sim.Tanks[Sim.SlotOf(id)];

        /// <summary>Takes the tank's spawn protection off and leaves it <paramref name="health"/>.</summary>
        public void Weaken(int id, int health)
        {
            Sim.TankRef(id).Health = health;
            Sim.TankRef(id).ProtectedUntil = 0;
        }

        public void Place(int id, float x, float z, float yaw)
        {
            Sim.TankRef(id).Position = new Vector2(x, z);
            Sim.TankRef(id).Yaw = yaw;
        }
    }

    [Fact]
    public void The_standard_mode_is_the_designs()
    {
        Assert.Equal(3, (byte)BattleMode.Standard);
        Assert.Equal(new[] { BattleMode.TeamBattle, BattleMode.Annihilation, BattleMode.FreeForAll, BattleMode.Standard, BattleMode.Royale, BattleMode.Duos }, ModeRules.All);
        Assert.Equal(new[] { "teambattle", "annihilation", "ffa", "standard", "royale", "duos" }, ModeRules.All.Select(ModeRules.Id));
        Assert.True(ModeRules.TryParse("STANDARD", out BattleMode standard) && standard == BattleMode.Standard);
        Assert.Equal(10 * 60 * Tps, ModeRules.DurationTicks(BattleMode.Standard));
        Assert.Equal(18000, ModeRules.DurationTicks(BattleMode.Standard));
        Assert.True(ModeRules.HasTeams(BattleMode.Standard));
        Assert.False(ModeRules.Respawns(BattleMode.Standard));
        Assert.True(ModeRules.JoinsMidMatch(BattleMode.Standard));
        foreach (BattleMode mode in ModeRules.All)
            Assert.Equal(mode == BattleMode.Standard, ModeRules.HasBases(mode));
        Assert.Equal(BattleMode.Duos, ModeRules.LastMode);
    }

    [Fact]
    public void A_standard_match_needs_both_bases_and_the_other_modes_ignore_them()
    {
        var sim = new Simulation(TestWorld.FlatField());
        Assert.Equal("bases", Assert.Throws<ArgumentException>(() => new MatchRules(BattleMode.Standard, sim, Layout)).ParamName);
        Assert.Throws<ArgumentException>(() => new MatchRules(BattleMode.Standard, sim, Layout, bases: Array.Empty<TeamBase>()));
        Assert.Throws<ArgumentException>(() => new MatchRules(BattleMode.Standard, sim, Layout, bases: new[] { Bases[0] }));

        var standard = new MatchRules(BattleMode.Standard, sim, Layout, bases: Bases);
        Assert.NotNull(standard.Capture);
        Assert.Equal(Bases, standard.Capture!.Bases);

        foreach (BattleMode mode in new[] { BattleMode.TeamBattle, BattleMode.Annihilation, BattleMode.FreeForAll })
        {
            Assert.Null(new MatchRules(mode, sim, Layout).Capture);
            Assert.Null(new MatchRules(mode, sim, Layout, bases: Bases).Capture);
            Assert.Null(new MatchRules(mode, sim, Layout, bases: new[] { Bases[0] }).Capture);   // not even checked
        }
    }

    [Fact]
    public void Wiping_out_the_other_team_wins_with_the_reason()
    {
        var m = new Match();
        int a = m.Join(0, 0);
        m.Join(1, 0);
        int b = m.Join(2, 1);
        m.Weaken(b, 1);
        m.Attack(a, b);
        List<GameEvent> events = m.RunToEnd(6 * Tps);
        GameEvent kill = Assert.Single(events, e => e.Type == GameEventType.TankDestroyed);
        Assert.Equal(b, kill.TargetTankId);
        Assert.Equal(MatchPhase.Ended, m.Rules.Phase);
        Assert.Equal(0, m.Rules.WinnerTeam);
        Assert.Equal(MatchRules.NoSlot, m.Rules.WinnerSlot);
        Assert.Equal(MatchEndReason.Eliminated, m.Rules.EndReason);
        Assert.Equal(kill.Tick, m.Rules.EndedAt);
    }

    /// <summary>Team 1's three tanks in team 0's base, 8 m apart and at most 8 m from its centre; team 0's one at home.</summary>
    private static (Match m, int defender) ThreeInTheBase()
    {
        var m = new Match();
        int defender = m.Join(0, 0);
        int[] attackers = { m.Join(1, 1), m.Join(2, 1), m.Join(3, 1) };
        for (int i = 0; i < attackers.Length; i++) m.Place(attackers[i], 92 + 8 * i, 180, MathF.PI);
        return (m, defender);
    }

    [Fact]
    public void Capturing_the_enemy_base_wins_on_the_exact_tick()
    {
        (Match m, _) = ThreeInTheBase();
        m.Run(BaseCapture.TicksToWin / 3 - 1);
        Assert.Equal(MatchPhase.Playing, m.Rules.Phase);
        Assert.Equal(MatchEndReason.None, m.Rules.EndReason);
        Assert.Equal(99, m.Rules.Capture!.Progress(0));
        m.Run(1);
        Assert.Equal(MatchPhase.Ended, m.Rules.Phase);
        Assert.Equal(1, m.Rules.WinnerTeam);
        Assert.Equal(MatchEndReason.Captured, m.Rules.EndReason);
        Assert.Equal(100, m.Rules.Capture.Progress(0));
        Assert.Equal(0, m.Rules.Capture.Progress(1));
        Assert.Equal(BaseCapture.TicksToWin / 3, m.Rules.EndedAt);
    }

    [Fact]
    public void A_defender_in_the_base_holds_the_capture()
    {
        (Match m, int defender) = ThreeInTheBase();
        m.Place(defender, 100, 195, 0f);   // in the circle, behind the attackers
        m.Run(1100);
        Assert.Equal(MatchPhase.Playing, m.Rules.Phase);
        Assert.Equal(0, m.Rules.Capture!.Progress(0));
        Assert.Equal(0, m.Rules.Capture.TicksOn(0));
    }

    /// <summary>
    /// Team 1's three attackers 999 ticks in team 0's base (2997 ticks, one short of taking it), then moved out of the
    /// circle, keeping their ticks; then team 1's fourth tank opens fire on team 0's only tank, left 1 health.
    /// </summary>
    private static (Match m, int[] attackers, int defender) ATickShortAndTheLastDefenderUnderFire()
    {
        var m = new Match();
        int defender = m.Join(0, 0);
        int[] attackers = { m.Join(1, 1), m.Join(2, 1), m.Join(3, 1) };
        int shooter = m.Join(4, 1);
        for (int i = 0; i < attackers.Length; i++) m.Place(attackers[i], 92 + 8 * i, 180, MathF.PI);
        m.Run(BaseCapture.TicksToWin / 3 - 1);
        Assert.Equal(BaseCapture.TicksToWin - 3, m.Rules.Capture!.TicksOn(0));
        for (int i = 0; i < attackers.Length; i++) m.Place(attackers[i], 140 + 10 * i, 180, MathF.PI);   // out of the circle, off the line of fire
        m.Place(defender, 100, 150, -MathF.PI / 2);   // facing east …
        m.Place(shooter, 140, 150, MathF.PI / 2);     // … to the shooter 40 m off, firing west along z = 150, 30 m from the base's centre
        m.Weaken(defender, 1);
        m.Attack(shooter, defender);
        return (m, attackers, defender);
    }

    [Fact]
    public void Taking_the_base_and_wiping_the_other_team_out_on_one_tick_wins_by_the_capture()
    {
        // The same match twice (the sim is deterministic). Left alone, the shell wipes team 0 out on tick K.
        (Match alone, _, int lastStanding) = ATickShortAndTheLastDefenderUnderFire();
        alone.RunToEnd(6 * Tps);
        Assert.Equal(MatchPhase.Ended, alone.Rules.Phase);
        Assert.Equal(MatchEndReason.Eliminated, alone.Rules.EndReason);
        Assert.Equal(1, alone.Rules.WinnerTeam);
        Assert.False(alone.Rules.Capture!.IsCaptured(0));
        int k = alone.Rules.EndedAt;

        // Put back in the base for tick K, the attackers' last three ticks take it on the tick the shell lands.
        (Match m, int[] attackers, int defender) = ATickShortAndTheLastDefenderUnderFire();
        Assert.Equal(lastStanding, defender);
        m.Run(k - 1 - m.Sim.Tick);
        Assert.Equal(MatchPhase.Playing, m.Rules.Phase);
        for (int i = 0; i < attackers.Length; i++) m.Place(attackers[i], 92 + 8 * i, 180, MathF.PI);
        List<GameEvent> last = m.Run(1);

        Assert.Equal(k, m.Sim.Tick);
        Assert.Contains(last, e => e.Type == GameEventType.TankDestroyed && e.TargetTankId == defender);
        Assert.True(m.Rules.Capture!.IsCaptured(0));
        Assert.Equal(0, m.Rules.AliveCount(0));
        Assert.Equal(MatchPhase.Ended, m.Rules.Phase);
        Assert.Equal(k, m.Rules.EndedAt);
        Assert.Equal(1, m.Rules.WinnerTeam);
        Assert.Equal(MatchEndReason.Captured, m.Rules.EndReason);   // a win both ways at once counts as a capture
    }

    /// <summary>
    /// The reset through the real simulation, not hand-made events: an attacker sits in team 0's base; an enemy's shell
    /// that does damage takes its points to 0, so does a team-mate's HE splash that does damage, and a hit for no damage
    /// (spawn protection) takes nothing. Every tick in between it earns one tick.
    /// </summary>
    [Fact]
    public void Real_shells_reset_an_attackers_points_when_they_do_damage_and_not_when_they_do_none()
    {
        var m = new Match();
        int enemy = m.Join(0, 0), attacker = m.Join(1, 1), mate = m.Join(2, 1);
        m.Place(attacker, 100, 180, 0f);   // in team 0's base, nose north to the team-mate
        m.Place(mate, 100, 130, MathF.PI);   // 50 m ahead of it, facing it; outside both circles
        m.Place(enemy, 130, 180, 0f);   // 30 m east of the base's centre: outside the circle, not a defender
        BaseCapture capture = m.Rules.Capture!;

        // One tick, checking that the attacker earned one tick, or dropped to 0 if a hit or splash did it damage.
        List<GameEvent> Step()
        {
            int before = capture.TicksOf(attacker);
            List<GameEvent> events = m.Run(1);
            bool hurt = events.Any(e => (e.Type is GameEventType.TankHit or GameEventType.SplashDamage) && e.TargetTankId == attacker && e.Damage > 0);
            Assert.Equal(hurt ? 0 : before + 1, capture.TicksOf(attacker));
            return events;
        }

        void Sit(int ticks)
        {
            for (int i = 0; i < ticks; i++) Step();
        }

        // Steps until an event matching wanted; returns it and the attacker's ticks before that tick.
        (GameEvent Event, int Before) RunUntil(Func<GameEvent, bool> wanted, int limit)
        {
            for (int i = 0; i < limit; i++)
            {
                int before = capture.TicksOf(attacker);
                List<GameEvent> events = Step();
                int found = events.FindIndex(e => wanted(e));
                if (found >= 0) return (events[found], before);
            }
            throw new Xunit.Sdk.XunitException($"nothing wanted came in {limit} ticks");
        }

        m.Sim.TankRef(attacker).Health = 100_000;   // it takes every shell and lives
        m.Sim.TankRef(attacker).ProtectedUntil = 0;
        Sit(60);
        Assert.Equal(60, capture.TicksOf(attacker));

        // An enemy shell that does damage.
        m.Attack(enemy, attacker);
        (GameEvent shot, int before) = RunUntil(e => e.Type == GameEventType.TankHit && e.TankId == enemy && e.TargetTankId == attacker && e.Damage > 0, 10 * Tps);
        Assert.True(before >= 60, $"{before} ticks before the shot");
        Assert.True(shot.Damage > 0);
        Assert.Equal(0, capture.TicksOf(attacker));
        m.Order(enemy, new InputCmd(0, 0, 0, 0f, 0f, false));

        // A team-mate's HE bursting on the ground a metre before its nose.
        Sit(60);
        Assert.True(capture.TicksOf(attacker) >= 60);
        m.Sim.TankRef(mate).ProtectedUntil = 0;
        TankState mateTank = m.Tank(mate);
        var (yaw, pitch) = Angles.AimAngles(new Vector3(100, 0, 180 - 4.1f) - TankPose.Compute(mateTank, m.Sim.Field.Terrain).Trunnion(mateTank.Spec));
        m.Order(mate, new InputCmd(0, 0, 0, yaw, pitch, true, AmmoType.HE));
        (GameEvent splash, before) = RunUntil(e => e.Type == GameEventType.SplashDamage && e.TankId == mate && e.TargetTankId == attacker && e.Damage > 0, 20 * Tps);
        Assert.True(before >= 60, $"{before} ticks before the splash");
        Assert.True(splash.Damage > 0);
        Assert.Equal(0, capture.TicksOf(attacker));
        m.Order(mate, new InputCmd(0, 0, 0, MathF.PI, 0f, false));

        // An enemy hit for no damage: the attacker under spawn protection again.
        Sit(60);
        m.Sim.Protect(attacker, 20 * Tps);
        m.Attack(enemy, attacker);
        (GameEvent glance, before) = RunUntil(e => e.Type == GameEventType.TankHit && e.TankId == enemy && e.TargetTankId == attacker, 20 * Tps);
        Assert.Equal(0, glance.Damage);
        Assert.True(before >= 60, $"{before} ticks before the hit");
        Assert.Equal(before + 1, capture.TicksOf(attacker));
    }

    [Fact]
    public void Both_bases_falling_on_one_tick_is_a_draw()
    {
        var m = new Match();
        int a = m.Join(0, 0), b = m.Join(1, 1);
        m.Place(a, 100, 70, 0f);     // in team 1's base
        m.Place(b, 100, 180, MathF.PI);   // in team 0's base
        m.Run(BaseCapture.TicksToWin - 1);
        Assert.Equal(MatchPhase.Playing, m.Rules.Phase);
        m.Run(1);
        Assert.Equal(MatchPhase.Ended, m.Rules.Phase);
        Assert.Equal(MatchRules.NoTeam, m.Rules.WinnerTeam);
        Assert.Equal(MatchEndReason.Captured, m.Rules.EndReason);
        Assert.True(m.Rules.Capture!.IsCaptured(0) && m.Rules.Capture.IsCaptured(1));
    }

    [Fact]
    public void Both_teams_wiped_out_on_one_tick_is_a_draw()
    {
        var m = new Match();
        int a = m.Join(0, 0), b = m.Join(1, 1);
        m.Place(a, 100, 150, 0f);
        m.Place(b, 100, 110, MathF.PI);   // 40 m apart, face to face
        m.Weaken(a, 1);
        m.Weaken(b, 1);
        m.Attack(a, b);
        m.Attack(b, a);
        List<GameEvent> events = m.RunToEnd(6 * Tps);
        GameEvent[] kills = events.Where(e => e.Type == GameEventType.TankDestroyed).ToArray();
        Assert.Equal(2, kills.Length);
        Assert.Equal(kills[0].Tick, kills[1].Tick);
        Assert.Equal(MatchPhase.Ended, m.Rules.Phase);
        Assert.Equal(kills[0].Tick, m.Rules.EndedAt);
        Assert.Equal(MatchRules.NoTeam, m.Rules.WinnerTeam);
        Assert.Equal(MatchEndReason.Eliminated, m.Rules.EndReason);
    }

    [Fact]
    public void Time_up_is_a_draw()
    {
        var m = new Match(durationTicks: 300);
        m.Join(0, 0);
        m.Join(1, 1);
        m.Run(299);
        Assert.Equal(MatchPhase.Playing, m.Rules.Phase);
        m.Run(1);
        Assert.Equal(MatchPhase.Ended, m.Rules.Phase);
        Assert.Equal(300, m.Rules.EndedAt);
        Assert.Equal(MatchRules.NoTeam, m.Rules.WinnerTeam);
        Assert.Equal(MatchEndReason.TimeUp, m.Rules.EndReason);
    }

    [Fact]
    public void A_player_may_join_a_running_standard_match_as_a_new_tank()
    {
        var m = new Match();
        int a = m.Join(0, 0), b = m.Join(1, 1);
        m.Run(5);
        Assert.True(m.Rules.AcceptsJoins);
        int c = m.Rules.Join(2, TankCatalog.Medium, team: 1);
        Assert.True(c > 0);
        Assert.Equal(1, m.Tank(c).Team);
        Assert.Equal(TankCatalog.Medium.MaxHealth, m.Tank(c).Health);
        Assert.True(m.Sim.IsProtected(m.Tank(c)));
        Assert.Equal(2, m.Rules.AliveCount(1));

        m.Weaken(b, 1);
        m.Attack(a, b);
        m.Run(3 * Tps);
        Assert.True(m.Tank(b).IsDestroyed);
        Assert.Equal(-1, m.Rules.Of(1).RespawnAt);
        Assert.Equal(MatchPhase.Playing, m.Rules.Phase);   // the newcomer still stands
        Assert.Equal(1, m.Rules.AliveCount(1));
    }

    [Fact]
    public void Every_mode_records_why_it_ended()
    {
        var teamBattle = new Match(BattleMode.TeamBattle, durationTicks: 60);
        teamBattle.Join(0, 0);
        teamBattle.Join(1, 1);
        teamBattle.Run(59);
        Assert.Equal(MatchEndReason.None, teamBattle.Rules.EndReason);
        teamBattle.Run(1);
        Assert.Equal(MatchEndReason.TimeUp, teamBattle.Rules.EndReason);

        var wipeOut = new Match(BattleMode.Annihilation);
        int a = wipeOut.Join(0, 0), b = wipeOut.Join(1, 1);
        wipeOut.Weaken(b, 1);
        wipeOut.Attack(a, b);
        wipeOut.RunToEnd(6 * Tps);
        Assert.Equal(0, wipeOut.Rules.WinnerTeam);
        Assert.Equal(MatchEndReason.Eliminated, wipeOut.Rules.EndReason);

        var annihilationClock = new Match(BattleMode.Annihilation, durationTicks: 60);
        annihilationClock.Join(0, 0);
        annihilationClock.Join(1, 1);
        annihilationClock.Run(60);
        Assert.Equal(MatchEndReason.TimeUp, annihilationClock.Rules.EndReason);

        var lastOne = new Match(BattleMode.FreeForAll);
        int x = lastOne.Join(0), y = lastOne.Join(1);
        lastOne.Place(x, 100, 150, 0f);
        lastOne.Place(y, 100, 110, MathF.PI);
        lastOne.Weaken(y, 1);
        lastOne.Attack(x, y);
        lastOne.RunToEnd(6 * Tps);
        Assert.Equal(0, lastOne.Rules.WinnerSlot);
        Assert.Equal(MatchEndReason.Eliminated, lastOne.Rules.EndReason);

        var soloClock = new Match(BattleMode.FreeForAll, durationTicks: 60);
        soloClock.Join(0);
        soloClock.Join(1);
        soloClock.Run(59);
        Assert.Equal(MatchEndReason.None, soloClock.Rules.EndReason);
        soloClock.Run(1);
        Assert.Equal(MatchEndReason.TimeUp, soloClock.Rules.EndReason);
    }

    [Fact]
    public void Capture_best_is_kept_per_slot()
    {
        var m = new Match();
        m.Join(0, 0);
        int attacker = m.Join(3, 1);
        m.Place(attacker, 100, 180, MathF.PI);
        m.Run(95);
        Assert.Equal(3, m.Rules.CaptureBest(3));
        Assert.Equal(0, m.Rules.CaptureBest(0));
        Assert.Equal(0, m.Rules.CaptureBest(5));   // nobody in that seat

        var teamBattle = new Match(BattleMode.TeamBattle);
        teamBattle.Join(0, 0);
        int other = teamBattle.Join(3, 1);
        teamBattle.Place(other, 100, 180, MathF.PI);
        teamBattle.Run(95);
        Assert.Equal(0, teamBattle.Rules.CaptureBest(3));
    }
}
