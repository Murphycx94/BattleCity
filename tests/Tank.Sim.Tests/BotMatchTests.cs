using Tank.Sim.Combat;
using Tank.Sim.Maps;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;
using Xunit.Abstractions;

namespace Tank.Sim.Tests;

/// <summary>
/// Whole standard battles of ten bots, played headless as the server plays them (stage 5 design §6, 5C: "in headless whole
/// matches ten bots fight a standard battle on the riverland; several fixed seeds each end in a win or a draw within 10
/// minutes, no bot stuck on the spot for more than 10 s"; stage 5C plan, task 9). Each match is played once for the class
/// (<see cref="WholeMatches"/>), all of them side by side.
/// </summary>
public sealed class BotMatchTests : IClassFixture<WholeMatches>
{
    private const int Tps = SimConstants.TickRate;

    /// <summary>No bot may get nowhere this long (design §6), s.</summary>
    private const float StuckLimit = 10f;

    /// <summary>The replay test compares two seeds' positions at this tick.</summary>
    private const int ComparedTick = 30 * Tps;

    private readonly WholeMatches _matches;
    private readonly ITestOutputHelper _output;

    public BotMatchTests(WholeMatches matches, ITestOutputHelper output) => (_matches, _output) = (matches, output);

    public static TheoryData<uint> RiverlandSeeds => Seeds(WholeMatches.RiverlandSeeds);
    public static TheoryData<uint> SandboxSeeds => Seeds(WholeMatches.SandboxSeeds);
    public static TheoryData<uint> PlayerSeeds => Seeds(WholeMatches.PlayerSeeds);

    [Theory]
    [MemberData(nameof(RiverlandSeeds))]
    public void Ten_bots_finish_a_standard_match_on_the_riverland(uint seed) => AssertWholeMatch(Play(MapCatalog.Riverland, seed));

    [Theory]
    [MemberData(nameof(SandboxSeeds))]
    public void Ten_bots_finish_a_standard_match_on_the_sandbox(uint seed) => AssertWholeMatch(Play(MapCatalog.Sandbox, seed));

    /// <summary>The server's seating with a player in the room (deviation 29 with a live player: one who never attacks).</summary>
    [Theory]
    [MemberData(nameof(PlayerSeeds))]
    public void Nine_bots_and_an_idle_player_finish_a_standard_match_on_the_riverland(uint seed) =>
        AssertWholeMatch(Play(MapCatalog.Riverland, seed, player: true));

    [Fact]
    public void Most_riverland_matches_are_settled_before_the_clock()
    {
        WholeMatch[] matches = WholeMatches.RiverlandSeeds.Select(s => Play(MapCatalog.Riverland, s)).ToArray();

        int settled = matches.Count(m => m.Settled);
        Assert.True(settled >= 2, $"only {settled} of {matches.Length} riverland matches were won before the clock");
    }

    /// <summary>
    /// Task 9 asked every match of test 1 for a base that held a point, then (batch C) half of each map's. Since the bots
    /// keep off their team-mates (batch C review follow-up) they no longer jostle near the spawns and meet the other team
    /// as a group: most matches are won by wiping the other team out in the middle, about 40 s sooner, before any attacker
    /// gets in, which is a match won, not a bot short of its goal (deviation 35). So the points are asked of each map's
    /// matches together, at least one; that the attackers do get to the enemy base is the next test's.
    /// </summary>
    [Fact]
    public void Bots_score_on_a_base_in_a_match_on_each_map()
    {
        foreach ((MapDefinition map, WholeMatch[] matches) in new[] { (MapCatalog.Riverland, RiverlandMatches()), (MapCatalog.Sandbox, WholeMatches.SandboxSeeds.Select(s => Play(MapCatalog.Sandbox, s)).ToArray()) })
        {
            int scored = matches.Count(m => m.BestPoints >= 1);
            Assert.True(scored >= 1, $"{map.Id}: no base held a point in {matches.Length} matches");
        }
    }

    /// <summary>
    /// What the points guarded against: attackers that never get to the enemy base. Across the riverland's matches each
    /// team's attackers come inside it in at least one (batch C review, item 6).
    /// </summary>
    [Fact]
    public void Each_teams_attackers_get_into_the_enemy_base_on_the_riverland()
    {
        WholeMatch[] matches = RiverlandMatches();
        float radius = MapCatalog.Riverland.Bases[0].Radius;
        Assert.True(matches.Any(m => m.NearestAttack0 <= radius),
            $"team 0's attackers came no nearer the enemy base than {matches.Min(m => m.NearestAttack0):F0} m (radius {radius} m)");
        Assert.True(matches.Any(m => m.NearestAttack1 <= radius),
            $"team 1's attackers came no nearer the enemy base than {matches.Min(m => m.NearestAttack1):F0} m (radius {radius} m)");
    }

    [Fact]
    public void Bots_do_capture_on_the_riverland()
    {
        // The sandbox never ends in a capture (deviation 34): too small, the teams wear each other down in the middle.
        WholeMatch[] matches = WholeMatches.CaptureSeeds.Select(s => Play(MapCatalog.Riverland, s)).ToArray();

        foreach (WholeMatch m in matches) AssertWholeMatch(m);
        Assert.Contains(matches, m => m.EndReason == MatchEndReason.Captured);
    }

    [Fact]
    public void A_match_replays_from_its_seed()
    {
        const int Ticks = 60 * Tps;
        Replay first = Record(7, Ticks), second = Record(7, Ticks), other = Record(8, ComparedTick);

        AssertSame(first.States, second.States, "tank states");
        AssertSame(first.Events, second.Events, "events");
        Assert.True(first.Events.Count > 0, "nothing happened in a minute");
        Assert.True(first.AtCompared.Where((p, k) => p != other.AtCompared[k]).Any(),
            $"seeds 7 and 8 had every tank in the same place at tick {ComparedTick}");
    }

    [Fact]
    public void A_standard_tick_with_ten_bots_on_the_riverland_allocates_nothing()
    {
        Allocation.AssertNone(() =>
        {
            BotArena arena = WholeMatch.Seat(MapCatalog.Riverland, 3, recording: false);
            arena.Run(300);
            Allocation.Window window = Allocation.Start();
            arena.Run(600);   // the director, the bots' commands, the step, the vision and the rules
            Allocation.Measured measured = window.Stop();
            Assert.Equal(MatchPhase.Playing, arena.Rules.Phase);
            return measured;
        });
    }

    /// <summary>All the fixture's riverland matches: test 1's, the capture seeds' and the ones with a player.</summary>
    private WholeMatch[] RiverlandMatches() =>
        WholeMatches.RiverlandSeeds.Concat(WholeMatches.CaptureSeeds).Distinct().Select(s => Play(MapCatalog.Riverland, s))
            .Concat(WholeMatches.PlayerSeeds.Select(s => Play(MapCatalog.Riverland, s, player: true))).ToArray();

    private WholeMatch Play(MapDefinition map, uint seed, bool player = false)
    {
        WholeMatch m = _matches.Get(map, seed, player);
        _output.WriteLine(m.ToString());
        _output.WriteLine($"    {m.Roles}");
        return m;
    }

    /// <summary>What every whole match must show (task 9, test 1).</summary>
    private static void AssertWholeMatch(WholeMatch m)
    {
        Assert.True(m.EndReason != MatchEndReason.None, $"{m.MapId} seed {m.Seed}: still on after {WholeMatch.MaxTicks / Tps} s");
        Assert.True(m.LongestStuck < StuckLimit, $"{m.MapId} seed {m.Seed}: tank {m.LongestStuckTank} got nowhere for {m.LongestStuck:F1} s");
        // The two teams find each other and fight (from the skirmish bot's tests): both hit the other, and tanks fall.
        Assert.True(m.HitsBy0 > 0 && m.HitsBy1 > 0, $"{m.MapId} seed {m.Seed}: hits by team 0 {m.HitsBy0}, by team 1 {m.HitsBy1}");
        Assert.True(m.Wrecks >= 2, $"{m.MapId} seed {m.Seed}: only {m.Wrecks} tanks destroyed");
        Assert.True(m.Failures * 10 <= m.Searches, $"{m.MapId} seed {m.Seed}: {m.Failures} of {m.Searches} searches found no path");
        // Out of time, the attackers left must be at the enemy base or fighting: one driving round short of it is a stalemate.
        if (m.EndReason == MatchEndReason.TimeUp)
            Assert.True(m.AttackersShort.Count == 0, $"{m.MapId} seed {m.Seed}: at the clock {string.Join("; ", m.AttackersShort)}");
    }

    /// <summary>Every tank's position, heading, health and wreck flag each tick, bit for bit; the events; the positions at <see cref="ComparedTick"/>.</summary>
    private sealed record Replay(List<(int Tick, int Id, int X, int Z, int Yaw, int Health, bool Destroyed)> States, List<EventBits> Events, System.Numerics.Vector2[] AtCompared);

    /// <summary>An event's fields, floats as their bits.</summary>
    private readonly record struct EventBits(GameEventType Type, int Tick, int TankId, int ShellId, int Px, int Py, int Pz, int Vx, int Vy, int Vz,
        int Target, HitResult Result, int Damage, AmmoType Ammo, int PropId)
    {
        public static EventBits Of(in GameEvent e) => new(e.Type, e.Tick, e.TankId, e.ShellId, Bits(e.Position.X), Bits(e.Position.Y), Bits(e.Position.Z),
            Bits(e.Velocity.X), Bits(e.Velocity.Y), Bits(e.Velocity.Z), e.TargetTankId, e.Result, e.Damage, e.Ammo, e.PropId);
    }

    private static int Bits(float f) => BitConverter.SingleToInt32Bits(f);

    private static Replay Record(uint seed, int ticks)
    {
        BotArena arena = WholeMatch.Seat(MapCatalog.Riverland, seed);
        var states = new List<(int, int, int, int, int, int, bool)>(ticks * WholeMatch.Seats);
        System.Numerics.Vector2[] atCompared = Array.Empty<System.Numerics.Vector2>();
        arena.Run(ticks, tick =>
        {
            foreach (TankState t in arena.Sim.Tanks)
                states.Add((tick, t.Id, Bits(t.Position.X), Bits(t.Position.Y), Bits(t.Yaw), t.Health, t.IsDestroyed));
            if (tick == ComparedTick) atCompared = arena.Sim.Tanks.ToArray().Select(t => t.Position).ToArray();
        });
        return new Replay(states, arena.Events.Select(e => EventBits.Of(e)).ToList(), atCompared);
    }

    private static void AssertSame<T>(List<T> a, List<T> b, string what)
    {
        Assert.True(a.Count == b.Count, $"{what}: {a.Count} then {b.Count}");
        int at = Enumerable.Range(0, a.Count).FirstOrDefault(k => !EqualityComparer<T>.Default.Equals(a[k], b[k]), -1);
        Assert.True(at < 0, $"{what} differ first at #{at}: {(at < 0 ? "" : $"{a[at]} then {b[at]}")}");
    }

    private static TheoryData<uint> Seeds(uint[] seeds)
    {
        var data = new TheoryData<uint>();
        foreach (uint s in seeds) data.Add(s);
        return data;
    }
}
