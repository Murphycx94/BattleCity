using System.Numerics;
using Tank.Sim.Ai;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Tests;

/// <summary>
/// One whole standard battle, seated as the server seats it (<see cref="BotSeating"/>: stage 5C plan, task 9; the server's
/// own helper since the batch C review follow-up) and played to its end on a <see cref="BotArena"/>; only what the tests
/// look at is kept. Ten bots: the riverland probe's <c>match MAP --ten</c>, every slot's tank drawn from the seed in slot
/// order, teams alternating (as <c>ThinnerTeam</c> fills an empty room). With a player: the probe's <c>match MAP</c> (the
/// server's seating with one player in the room): an idle medium in slot 0 on team 0, who came into the room first, its
/// commands all zero (what the server takes from a client that sends nothing but <c>LocalCommand.None</c>), and nine
/// bots in slots 1–9.
/// </summary>
internal sealed record WholeMatch(
    string MapId,
    uint Seed,
    bool WithPlayer,
    MatchEndReason EndReason,
    int EndedAt,
    byte WinnerTeam,
    int Wrecks0,
    int Wrecks1,
    int HitsBy0,
    int HitsBy1,
    int BestPoints0,
    int BestPoints1,
    float NearestAttack0,     // the nearest a live attacker of team 0 came to the enemy base's centre, m
    float NearestAttack1,     // … of team 1
    float LongestStuck,       // the longest of the bots' BotArena.LongestStuckSeconds, s
    int LongestStuckTank,
    int Stucks,
    int Searches,
    int Failures,
    IReadOnlyList<string> AttackersShort,
    string Roles,
    string Summary)
{
    /// <summary>The room's seats, every one taken.</summary>
    public const int Seats = 10;

    /// <summary>A standard battle's clock: 10 minutes.</summary>
    public static readonly int MaxTicks = ModeRules.DurationTicks(BattleMode.Standard);

    private const int Tps = SimConstants.TickRate;

    /// <summary>Won by wiping the other team out or taking its base, not on the clock.</summary>
    public bool Settled => EndReason is MatchEndReason.Eliminated or MatchEndReason.Captured;

    /// <summary>Wrecks of both teams at the end.</summary>
    public int Wrecks => Wrecks0 + Wrecks1;

    /// <summary>The most whole points either base was seen to hold.</summary>
    public int BestPoints => Math.Max(BestPoints0, BestPoints1);

    /// <summary>
    /// A standard battle on <paramref name="map"/> seated from <paramref name="seed"/> as the server does it, roles given
    /// out: ten bots, or (<paramref name="player"/>) an idle player in slot 0 and nine bots.
    /// </summary>
    public static BotArena Seat(MapDefinition map, uint seed, bool recording = true, bool player = false)
    {
        var arena = new BotArena(BattleMode.Standard, map) { Recording = recording };
        var seating = new BotSeating(seed);
        Span<int> onTeam = stackalloc int[2];
        for (int slot = 0; slot < Seats; slot++)
        {
            byte team = BotSeating.ThinnerTeam(onTeam[0], onTeam[1]);
            onTeam[team]++;
            if (player && slot == 0) arena.Join(TankCatalog.Medium, team);   // a seat starts with the medium
            else arena.Bot(TankCatalog.All[seating.NextTankIndex()], team, seating.BotSeed(slot));
        }
        arena.Director.AssignRoles();
        return arena;
    }

    /// <summary>
    /// Plays the match seated from <paramref name="seed"/> on <paramref name="map"/> (with an idle player when
    /// <paramref name="player"/>) until it ends, or for <see cref="MaxTicks"/> at most.
    /// </summary>
    public static WholeMatch Play(MapDefinition map, uint seed, bool player = false)
    {
        BotArena arena = Seat(map, seed, player: player);
        string roles = arena.Director.Describe();
        BaseCapture capture = arena.Rules.Capture!;
        var enemyBase = new Vector2[2];
        foreach (TeamBase b in map.Bases) enemyBase[1 - b.Team] = b.Centre;
        int best0 = 0, best1 = 0;
        var nearest = new[] { float.MaxValue, float.MaxValue };
        while (arena.Rules.Phase == MatchPhase.Playing && arena.Sim.Tick < MaxTicks)
        {
            arena.Tick();
            best0 = Math.Max(best0, capture.Progress(0));
            best1 = Math.Max(best1, capture.Progress(1));
            foreach (Bot bot in arena.Director.Bots)
            {
                if (bot.Role != BotRole.Attack) continue;
                TankState t = arena.Tank(bot.TankId);
                if (!t.IsDestroyed) nearest[t.Team] = MathF.Min(nearest[t.Team], Vector2.Distance(t.Position, enemyBase[t.Team]));
            }
        }

        int wrecks0 = 0, wrecks1 = 0;
        foreach (TankState t in arena.Sim.Tanks)
        {
            if (!t.IsDestroyed) continue;
            if (t.Team == 0) wrecks0++;
            else wrecks1++;
        }
        int hitsBy0 = 0, hitsBy1 = 0;
        foreach (GameEvent e in arena.Events)
        {
            if (e.Type != GameEventType.TankHit) continue;
            byte shooter = arena.Tank(e.TankId).Team, struck = arena.Tank(e.TargetTankId).Team;
            if (shooter == struck) continue;
            if (shooter == 0) hitsBy0++;
            else hitsBy1++;
        }
        int worstTank = 0, stucks = 0;
        float worst = 0f;
        foreach (Bot bot in arena.Director.Bots)
        {
            stucks += bot.Stucks;
            float s = arena.LongestStuckSeconds(bot.TankId);
            if (s > worst) (worst, worstTank) = (s, bot.TankId);
        }
        return new WholeMatch(map.Id, seed, player, arena.Rules.EndReason, arena.Rules.EndedAt, arena.Rules.WinnerTeam, wrecks0, wrecks1,
            hitsBy0, hitsBy1, best0, best1, nearest[0], nearest[1], worst, worstTank, stucks, arena.Director.Paths.Searches, arena.Director.Paths.Failures,
            arena.AttackersShortOfTheEnemyBase(), roles, arena.Director.Summary());
    }

    /// <summary>The one line the tests print.</summary>
    public override string ToString() =>
        $"{MapId} seed {Seed}{(WithPlayer ? " (nine bots and an idle player)" : "")}: {EndReason} at {EndedAt / (float)Tps:F1} s, winner {(WinnerTeam == MatchRules.NoTeam ? "none" : WinnerTeam.ToString())}; " +
        $"wrecks team 0 {Wrecks0}, team 1 {Wrecks1}; hits by team 0 {HitsBy0}, by team 1 {HitsBy1}; best points on base 0 {BestPoints0}, base 1 {BestPoints1}; " +
        $"nearest an attacker came to the enemy base {System.Math.Min(NearestAttack0, NearestAttack1):F0} m (team 0 {NearestAttack0:F0} m, team 1 {NearestAttack1:F0} m); longest stuck {LongestStuck:F1} s (tank {LongestStuckTank}); " +
        $"{Summary}; attackers short of the enemy base at the end: {(AttackersShort.Count == 0 ? "none" : string.Join("; ", AttackersShort))}";
}

/// <summary>
/// The whole matches of <see cref="BotMatchTests"/>, each played once however many tests read it (a class fixture). The
/// first read starts them all, each on a thread of its own, so they play side by side; a read waits for its own, for
/// <see cref="Patience"/> at most.
/// </summary>
public sealed class WholeMatches : IDisposable
{
    public static readonly uint[] RiverlandSeeds = { 1, 2, 3 };
    public static readonly uint[] SandboxSeeds = { 1, 2 };
    public static readonly uint[] CaptureSeeds = { 7, 20, 26 };    // riverland: the probe's captures since the bots keep off their team-mates (deviations 34, 35)
    public static readonly uint[] PlayerSeeds = { 1, 2, 3 };       // riverland, nine bots and an idle player: the server's seating (deviation 35)

    /// <summary>The longest a read waits for its match (a Debug match takes well under a minute; many side by side, longer).</summary>
    public static readonly TimeSpan Patience = TimeSpan.FromMinutes(5);

    private readonly object _gate = new();
    private Dictionary<(string Map, uint Seed, bool Player), Task<WholeMatch>>? _matches;

    internal WholeMatch Get(MapDefinition map, uint seed, bool player = false)
    {
        lock (_gate) _matches ??= StartAll();
        Task<WholeMatch> match = _matches[(map.Id, seed, player)];
        try
        {
            if (!match.Wait(Patience))
                throw new TimeoutException($"{map.Id} seed {seed}{(player ? " with a player" : "")} still playing after {Patience.TotalMinutes:F0} min");
        }
        catch (AggregateException) { }   // it failed: thrown as itself just below
        return match.GetAwaiter().GetResult();
    }

    /// <summary>Waits for any match still playing (a filtered run reads only some), so none outlives the class.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_matches is null) return;
            try { Task.WaitAll(_matches.Values.ToArray(), Patience); }
            catch (AggregateException) { }   // the test that read it failed with it already
        }
    }

    private static Dictionary<(string, uint, bool), Task<WholeMatch>> StartAll()
    {
        var all = new Dictionary<(string, uint, bool), Task<WholeMatch>>();
        foreach (uint seed in RiverlandSeeds.Concat(CaptureSeeds).Distinct()) Start(all, MapCatalog.Riverland, seed, player: false);
        foreach (uint seed in PlayerSeeds) Start(all, MapCatalog.Riverland, seed, player: true);
        foreach (uint seed in SandboxSeeds) Start(all, MapCatalog.Sandbox, seed, player: false);
        return all;
    }

    private static void Start(Dictionary<(string, uint, bool), Task<WholeMatch>> all, MapDefinition map, uint seed, bool player) =>
        all[(map.Id, seed, player)] = Task.Factory.StartNew(() => WholeMatch.Play(map, seed, player), CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);
}
