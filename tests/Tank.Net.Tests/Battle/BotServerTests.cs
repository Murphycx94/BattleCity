using System.Numerics;
using Tank.Net.Battle;
using Tank.Sim;
using Tank.Sim.Ai;
using Tank.Sim.Maps;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;
using Xunit.Abstractions;

namespace Tank.Net.Tests.Battle;

/// <summary>
/// The server's bots run on the <see cref="BotDirector"/> (stage 5C plan, task 8): made when a match with bots starts,
/// each bot seat handed to it with its own seed, roles given out and logged; asked once a tick before the inputs; its
/// summary logged when the match ends; gone with the match.
/// </summary>
public sealed class BotServerTests
{
    private const int Tps = SimConstants.TickRate;

    /// <summary>A defender counts as at its cover point this close, m.</summary>
    private const float AtCover = 6f;

    private readonly ITestOutputHelper _output;

    public BotServerTests(ITestOutputHelper output) => _output = output;

    private static BattleHarness Harness(MapDefinition map, List<string> log, uint seed = 1, double? matchSeconds = null, double graceSeconds = 30) =>
        new(new BattleServerOptions { Seed = seed, Map = map, Maps = new[] { map }, MatchSeconds = matchSeconds, ReconnectGraceSeconds = graceSeconds }, log: log.Add);

    private static bool IsSummary(string line) => line.StartsWith("bots: ", StringComparison.Ordinal) && line.Contains("paths", StringComparison.Ordinal);

    /// <summary>The riverland, a standard battle (seed 3): one player, who doesn't move, and nine bots.</summary>
    private static BattleHarness RiverlandWithNineBots(List<string> log)
    {
        BattleHarness h = Harness(MapCatalog.Riverland, log, seed: 3);
        h.AddClient("A");
        h.StartMatch(BattleMode.Standard, bots: 9);
        Assert.True(h.Server.MatchRunning, "the match didn't start");
        return h;
    }

    private static TankState TankOf(BattleHarness h, int id)
    {
        Simulation sim = h.Server.Simulation!;
        return sim.Tanks[sim.SlotOf(id)];
    }

    private static Vector2 EnemyBaseOf(MapDefinition map, byte team) => map.Bases.Single(b => b.Team != team).Centre;

    [Fact]
    public void A_standard_match_seats_bots_with_roles()
    {
        var log = new List<string>();
        BattleHarness h = RiverlandWithNineBots(log);

        BotDirector director = h.Server.Director!;
        Assert.NotNull(director);
        Assert.Equal(9, director.Bots.Count);
        Simulation sim = h.Server.Simulation!;
        Assert.Equal(10, sim.TankCount);
        Assert.Equal(5, sim.Tanks.ToArray().Count(t => t.Team == 0));
        Assert.Equal(5, sim.Tanks.ToArray().Count(t => t.Team == 1));
        Assert.Equal(0, h.ServerTankOf(h.Clients[0]).Team);
        for (byte team = 0; team <= 1; team++)
        {
            Bot[] mine = director.Bots.Where(b => TankOf(h, b.TankId).Team == team).ToArray();
            Assert.True(mine.Count(b => b.Role == BotRole.Defend) >= 1, $"team {team} has no defender");
            Assert.True(mine.Count(b => b.Role == BotRole.Attack) >= 2, $"team {team} has fewer than two attackers");
        }
        string line = Assert.Single(log, l => l.StartsWith("bots: ", StringComparison.Ordinal));
        _output.WriteLine(line);
        Assert.StartsWith("bots: team 0", line);
    }

    [Fact]
    public void Bots_head_down_their_lanes()
    {
        var log = new List<string>();
        BattleHarness h = RiverlandWithNineBots(log);
        BotDirector director = h.Server.Director!;
        var start = director.Bots.ToDictionary(b => b.TankId, b => TankOf(h, b.TankId).Position);
        var roles = director.Bots.ToDictionary(b => b.TankId, b => b.Role);

        h.Run(60);

        Assert.True(h.Server.MatchRunning);
        Assert.Same(director, h.Server.Director);
        foreach (Bot bot in director.Bots)
        {
            TankState t = TankOf(h, bot.TankId);
            Vector2 from = start[bot.TankId], enemy = EnemyBaseOf(MapCatalog.Riverland, t.Team);
            _output.WriteLine($"tank {bot.TankId} team {t.Team} {roles[bot.TankId]} → {bot.Role} {bot.Activity}: {from} → {t.Position}, " +
                              $"{Vector2.Distance(from, t.Position):F0} m driven, {Vector2.Distance(from, enemy) - Vector2.Distance(t.Position, enemy):F0} m nearer the enemy base");
            if (roles[bot.TankId] == BotRole.Attack)
            {
                Assert.True(Vector2.Distance(from, t.Position) >= 80f, $"attacker {bot.TankId} only got from {from} to {t.Position}");
                Assert.True(Vector2.Distance(from, enemy) - Vector2.Distance(t.Position, enemy) >= 60f,
                    $"attacker {bot.TankId} at {t.Position} isn't 60 m nearer the enemy base than at {from}");
            }
            else
            {
                Assert.Equal(BotRole.Defend, roles[bot.TankId]);
                IReadOnlyList<Vector2> cover = director.CoverOf(t.Team);
                Assert.True(cover.Any(c => Vector2.Distance(c, t.Position) <= AtCover),
                    $"defender {bot.TankId} at {t.Position} is at none of its cover points ({string.Join(", ", cover)})");
            }
        }
    }

    [Fact]
    public void A_standard_match_on_a_map_without_bot_data_still_attacks()
    {
        var log = new List<string>();
        // The walled small map: the wall hides each team's spawns from the other's attackers, so they don't stop on the way
        // to shoot at the players standing there (whose fronts a bot's rounds may not get through).
        MapDefinition map = TestMaps.StandardWalled("s");
        BattleHarness h = Harness(map, log);
        h.AddClient("A");
        h.AddClient("B");
        h.StartMatch(BattleMode.Standard, bots: 4);
        Assert.True(h.Server.MatchRunning, "the match didn't start");
        BotDirector director = h.Server.Director!;
        Assert.Equal(4, director.Bots.Count);
        Bot[] attackers = director.Bots.Where(b => b.Role == BotRole.Attack).ToArray();
        Assert.NotEmpty(attackers);
        var start = attackers.ToDictionary(b => b.TankId, b => TankOf(h, b.TankId).Position);
        var nearest = attackers.ToDictionary(b => b.TankId, b => float.MaxValue);

        // Sampled every second while it attacks: one called back to its own base, when the other side's attacker gets in
        // there, heads away from the enemy base for a while, as it should.
        for (int second = 0; second < 20; second++)
        {
            h.Run(1);
            foreach (Bot bot in attackers)
            {
                TankState t = TankOf(h, bot.TankId);
                if (bot.Role == BotRole.Attack && !bot.FallingBack && !bot.Retreating && !t.IsDestroyed)
                    nearest[bot.TankId] = System.Math.Min(nearest[bot.TankId], Vector2.Distance(t.Position, EnemyBaseOf(map, t.Team)));
            }
        }

        foreach (Bot bot in attackers)
        {
            TankState t = TankOf(h, bot.TankId);
            float from = Vector2.Distance(start[bot.TankId], EnemyBaseOf(map, t.Team));
            _output.WriteLine($"tank {bot.TankId} team {t.Team}: {from:F0} m from the enemy base at the start, {nearest[bot.TankId]:F0} m at the nearest; now at {t.Position}, {bot.Activity}");
            Assert.True(nearest[bot.TankId] < from, $"attacker {bot.TankId} never got nearer the enemy base than its {from:F0} m at the start");
        }
        Assert.DoesNotContain(log, line => line.Contains("error", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_match_end_logs_what_the_bots_did()
    {
        var log = new List<string>();
        BattleHarness h = Harness(MapCatalog.Sandbox, log, matchSeconds: 5);
        h.AddClient("A");
        h.StartMatch(BattleMode.Standard, bots: 3);
        Assert.True(h.Server.MatchRunning, "the match didn't start");

        h.Run(6);

        Assert.Contains(log, l => l.StartsWith("match ended: ", StringComparison.Ordinal));
        string summary = Assert.Single(log, l => l.StartsWith("bots: ", StringComparison.Ordinal) && l.Contains("paths", StringComparison.Ordinal));
        _output.WriteLine(summary);
        int ended = log.FindIndex(l => l.StartsWith("match ended: ", StringComparison.Ordinal));
        Assert.Equal(ended + 1, log.IndexOf(summary));
    }

    [Fact]
    public void Bots_are_gone_with_the_match()
    {
        var log = new List<string>();
        BattleHarness h = Harness(TestMaps.Small("small"), log);
        h.AddClient("A");
        h.StartMatch(BattleMode.TeamBattle, bots: 1);
        BotDirector first = h.Server.Director!;
        Assert.NotNull(first);
        Assert.Single(first.Bots);

        h.Clients[0].ReturnToRoom();
        h.Run(0.5);
        Assert.False(h.Server.MatchRunning);
        Assert.Null(h.Server.Director);

        h.StartMatch(BattleMode.TeamBattle, bots: 1);
        Assert.True(h.Server.MatchRunning);
        BotDirector second = h.Server.Director!;
        Assert.NotNull(second);
        Assert.NotSame(first, second);
        Assert.Single(second.Bots);
        Assert.Equal(h.Server.Simulation!.Tanks.ToArray().Single(t => t.Id != h.Server.TankOf(h.Clients[0].Slot)).Id, second.Bots[0].TankId);
    }

    [Fact]
    public void A_match_left_for_the_room_logs_what_the_bots_did_once()
    {
        var log = new List<string>();
        BattleHarness h = Harness(TestMaps.Small("small"), log);
        h.AddClient("A");
        h.StartMatch(BattleMode.TeamBattle, bots: 2);
        h.Run(2);

        h.Clients[0].ReturnToRoom();   // before the match's end: the summary comes with the way back to the room
        h.Run(0.5);

        Assert.False(h.Server.MatchRunning);
        string summary = Assert.Single(log, IsSummary);
        _output.WriteLine(summary);
        Assert.Equal(log.IndexOf("match over, back to the room") + 1, log.IndexOf(summary));
    }

    [Fact]
    public void A_match_whose_end_was_logged_logs_the_bots_once_on_the_way_back()
    {
        var log = new List<string>();
        BattleHarness h = Harness(MapCatalog.Sandbox, log, matchSeconds: 3);
        h.AddClient("A");
        h.StartMatch(BattleMode.Standard, bots: 3);

        h.Run(3 + ModeRules.ResultsTicks / (double)Tps + 1);   // the clock, the results, back to the room

        Assert.False(h.Server.MatchRunning);
        Assert.Contains("match over, back to the room", log);
        Assert.Single(log, IsSummary);
    }

    [Fact]
    public void Bots_that_fail_to_start_leave_the_room_as_it_was()
    {
        var log = new List<string>();
        BattleHarness h = Harness(TestMaps.Small("small"), log);
        BattleClient a = h.AddClient("A");
        h.Server.DirectorMade = _ => throw new InvalidOperationException("the bots broke");

        h.StartMatch(BattleMode.TeamBattle, bots: 3);

        Assert.False(h.Server.MatchRunning);
        Assert.Null(h.Server.Simulation);
        Assert.Null(h.Server.Rules);
        Assert.Null(h.Server.Director);
        Assert.Equal(BattlePhase.Room, a.Phase);
        Assert.Single(a.Room!.Players);   // the bot seats went again
        Assert.Contains(log, l => l.StartsWith("match not started: the bots failed: ", StringComparison.Ordinal) && l.Contains("the bots broke", StringComparison.Ordinal));
        Assert.DoesNotContain(log, l => l.StartsWith("match started", StringComparison.Ordinal));

        h.Server.DirectorMade = null;
        h.StartMatch(BattleMode.TeamBattle, bots: 3);
        Assert.True(h.Server.MatchRunning);
        Assert.Equal(3, h.Server.Director!.Bots.Count);
        Assert.Equal(BattlePhase.InMatch, a.Phase);
    }

    [Fact]
    public void Players_who_leave_and_join_mid_match_leave_the_bots_at_work()
    {
        var log = new List<string>();
        BattleHarness h = Harness(MapCatalog.Riverland, log, seed: 3, graceSeconds: 1);
        BattleClient a = h.AddClient("A"), b = h.AddClient("B");
        h.StartMatch(BattleMode.Standard, bots: 8);
        Assert.True(h.Server.MatchRunning, "the match didn't start");
        BotDirector director = h.Server.Director!;
        Assert.Equal(8, director.Bots.Count);
        int bTank = h.Server.TankOf(b.Slot);
        h.Run(5);

        h.Links[h.Clients.IndexOf(b)].Dispose();   // B quits, and stays away past the grace period: its tank goes
        h.Run(2.5);
        Assert.Equal(-1, h.Server.Simulation!.SlotOf(bTank));
        BattleClient c = h.AddClient("C");          // C comes into the running match: a tank of its own
        h.Run(1.5);
        Assert.Equal(BattlePhase.InMatch, c.Phase);
        int cTank = h.Server.TankOf(c.Slot);
        Assert.NotEqual(0, cTank);
        Assert.Null(director.Find(cTank));

        var at = director.Bots.ToDictionary(bot => bot.TankId, bot => TankOf(h, bot.TankId).Position);
        h.Run(20);

        Assert.True(h.Server.MatchRunning);
        Assert.Same(director, h.Server.Director);
        foreach (Bot bot in director.Bots)
            _output.WriteLine($"tank {bot.TankId} {bot.Role} {bot.Activity}: {Vector2.Distance(at[bot.TankId], TankOf(h, bot.TankId).Position):F0} m since C came{(TankOf(h, bot.TankId).IsDestroyed ? ", a wreck" : "")}");
        _output.WriteLine(director.Summary());
        // The attackers drive on (or stop to fight), the defenders keep their cover points.
        Bot[] attackers = director.Bots.Where(bot => bot.Role == BotRole.Attack && !TankOf(h, bot.TankId).IsDestroyed).ToArray();
        Assert.NotEmpty(attackers);
        foreach (Bot bot in attackers)
            Assert.True(bot.Activity == BotActivity.Engaging || Vector2.Distance(at[bot.TankId], TankOf(h, bot.TankId).Position) >= 20f,
                $"attacker {bot.TankId} went {Vector2.Distance(at[bot.TankId], TankOf(h, bot.TankId).Position):F0} m in 20 s, {bot.Activity}");
        Assert.True(director.Paths.Searches > 0);
        Assert.DoesNotContain(log, l => l.Contains("exception", StringComparison.OrdinalIgnoreCase) || l.Contains("error", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Each_match_gets_the_bots_the_room_asks_for()
    {
        var log = new List<string>();
        BattleHarness h = Harness(TestMaps.Small("small"), log);
        h.AddClient("A");
        h.AddClient("B");
        BotDirector? previous = null;
        foreach (int bots in new[] { 3, 1, 0 })
        {
            h.StartMatch(BattleMode.TeamBattle, bots: bots);
            Assert.True(h.Server.MatchRunning, $"the match with {bots} bots didn't start");
            Simulation sim = h.Server.Simulation!;
            Assert.Equal(2 + bots, sim.TankCount);
            BotDirector? director = h.Server.Director;
            if (bots == 0) Assert.Null(director);
            else
            {
                Assert.NotNull(director);
                Assert.NotSame(previous, director);
                int[] players = h.Clients.Select(cl => h.Server.TankOf(cl.Slot)).ToArray();
                Assert.Equal(sim.Tanks.ToArray().Select(t => t.Id).Where(id => !players.Contains(id)).Order(), director.Bots.Select(bot => bot.TankId));
            }
            previous = director;
            h.Clients.Single(cl => cl.IsHost).ReturnToRoom();
            h.Run(0.5);
            Assert.False(h.Server.MatchRunning);
            Assert.Null(h.Server.Director);
        }
    }

    [Fact]
    public void A_match_with_bots_replays_from_its_seed()
    {
        // The loopback host's promise (--loopback --seed): the same seed and the same inputs play the same match.
        var logX = new List<string>();
        var logY = new List<string>();
        BattleHarness x = RiverlandWithNineBots(logX), y = RiverlandWithNineBots(logY);
        for (int second = 0; second < 15; second++)
        {
            x.Run(1);
            y.Run(1);
            TankState[] a = x.Server.Simulation!.Tanks.ToArray(), b = y.Server.Simulation!.Tanks.ToArray();
            Assert.Equal(a.Length, b.Length);
            for (int i = 0; i < a.Length; i++)
                Assert.Equal((a[i].Id, a[i].Position, a[i].Yaw, a[i].Health, a[i].IsDestroyed), (b[i].Id, b[i].Position, b[i].Yaw, b[i].Health, b[i].IsDestroyed));
        }
        Assert.Equal(x.Server.Director!.Paths.Searches, y.Server.Director!.Paths.Searches);
        static bool Timed(string line) => line.Contains(" ready in ", StringComparison.Ordinal);   // the map's preparation time
        Assert.Equal(logX.Where(l => !Timed(l)), logY.Where(l => !Timed(l)));
    }

    /// <summary>
    /// The tests' whole matches (Sim's <c>WholeMatch</c> on a <c>BotArena</c>) prove what the server plays only if they
    /// play what it plays (batch C review follow-up): the riverland, one player who doesn't move and nine bots, the
    /// server's match against the same seating (<see cref="BotSeating"/>) stepped as the arena steps it, the player's
    /// commands all zero. After 30 s every tank is in the same place, bit for bit.
    /// </summary>
    [Fact]
    public void The_server_plays_the_match_the_tests_whole_matches_play()
    {
        const uint Seed = 3;
        const int Ticks = 30 * Tps;
        var log = new List<string>();
        BattleHarness h = RiverlandWithNineBots(log);   // seed 3
        while (h.Server.Simulation!.Tick < Ticks) h.Run(1.0 / Tps);
        Assert.Equal(Ticks, h.Server.Simulation.Tick);

        MapDefinition map = MapCatalog.Riverland;
        var sim = new Simulation(map.NewBattlefield());
        var vision = new Tank.Sim.Vision.VisionTracker(sim);
        var rules = new MatchRules(BattleMode.Standard, sim, map.Spawns, null, map.Bases);
        var director = new BotDirector(sim, vision, rules, map, map.NewNavGrid());
        var seating = new BotSeating(Seed);
        int[] onTeam = new int[2];
        for (byte slot = 0; slot < 10; slot++)
        {
            byte team = BotSeating.ThinnerTeam(onTeam[0], onTeam[1]);
            onTeam[team]++;
            if (slot == 0) rules.Join(slot, TankCatalog.Medium, team);   // the player, first into the room, in the seat's medium
            else director.Add(rules.Join(slot, TankCatalog.All[seating.NextTankIndex()], team), seating.BotSeed(slot));
        }
        director.AssignRoles();
        Vector2[] start = sim.Tanks.ToArray().Select(t => t.Position).ToArray();
        var inputs = new InputCmd[Simulation.MaxTanks];
        while (sim.Tick < Ticks)
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

        TankState[] server = h.Server.Simulation.Tanks.ToArray(), tests = sim.Tanks.ToArray();
        Assert.Equal(tests.Length, server.Length);
        float moved = 0f;
        for (int i = 0; i < server.Length; i++)
        {
            Assert.Equal((tests[i].Id, tests[i].Team, tests[i].Spec.Id), (server[i].Id, server[i].Team, server[i].Spec.Id));
            Assert.True(tests[i].Position == server[i].Position && tests[i].Yaw == server[i].Yaw && tests[i].Health == server[i].Health,
                $"tank {server[i].Id}: on the server at {server[i].Position} yaw {server[i].Yaw} health {server[i].Health}, " +
                $"in the tests at {tests[i].Position} yaw {tests[i].Yaw} health {tests[i].Health}");
            moved = System.Math.Max(moved, Vector2.Distance(server[i].Position, start[i]));
        }
        Assert.True(moved > 50f, "nobody went anywhere in 30 s");
        Assert.Equal(h.Server.Director!.Paths.Searches, director.Paths.Searches);
    }

    [Fact]
    public void A_match_without_bots_has_no_director()
    {
        var log = new List<string>();
        BattleHarness h = Harness(TestMaps.Small("small"), log);
        h.AddClient("A");
        h.AddClient("B");
        h.StartMatch(BattleMode.TeamBattle);
        Assert.True(h.Server.MatchRunning);
        Assert.Null(h.Server.Director);
        Assert.DoesNotContain(log, l => l.StartsWith("bots: ", StringComparison.Ordinal));
    }
}
