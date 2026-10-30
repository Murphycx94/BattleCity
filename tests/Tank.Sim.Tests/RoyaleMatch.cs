using System.Numerics;
using Tank.Sim.Ai;
using Tank.Sim.Maps;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Tests;

/// <summary>
/// One whole royale on the riverland, started as the server starts one (stage 6A plan, task 6): the rings drawn from the
/// seed before any tank (<see cref="BotArena.Royale"/>), an idle player in seat 0 (a medium, its commands all zero) and
/// fifteen bots in seats 1–15, each tank drawn from the seed in seat order and each bot seeded from its seat
/// (<see cref="BotSeating"/>), played until it ends or for <see cref="MaxTicks"/>. Only what the tests look at is kept.
/// </summary>
internal sealed record RoyaleMatch(
    uint Seed,
    RoyaleBots Bots,
    MatchEndReason EndReason,
    int EndedAt,               // ticks from the start
    byte WinnerSlot,
    string WinnerTank,
    bool WinnerAlive,
    int ZoneKills,
    int ShellKills,
    int ShellHits,
    int FirstBotZoneDeath,     // ticks from the start; −1: none
    float LongestStuck,        // the longest of the bots' BotArena.LongestStuckSeconds, s
    int LongestStuckTank,
    int[] OutsideAtRingEnd,    // [k − 1]: the live bots outside the safe circle the tick ring k finished shrinking; −1 if the match was over by then
    IReadOnlyList<string> OutsideDetail,
    int[] UrgentOnsets,        // [k − 1]: times a live bot became ZoneUrgent while ring k was the zone's (waiting or shrinking; ring 0's go with ring 1)
    int[] FightToRun,          // [k − 1]: of those, the ones by a bot that was stopped to fight (Engaging) the tick before
    bool LastKillByZone,       // the last tank destroyed went in the zone
    IReadOnlyList<GameEvent> Events,
    string Summary)
{
    /// <summary>Seats: the player and fifteen bots.</summary>
    public const int Seats = ModeRules.RoyaleMaxPlayers;

    /// <summary>A royale's clock: 10 minutes.</summary>
    public static readonly int MaxTicks = ModeRules.DurationTicks(BattleMode.Royale);

    private const int Tps = SimConstants.TickRate;

    /// <summary>Every tank's health × this in a <see cref="RoyaleBots.Tough"/> match (the zone takes a share of it: it kills as ever).</summary>
    public const int ToughHealth = 50;

    /// <summary>
    /// The riverland royale of <paramref name="seed"/>, seated, roles given out, nothing played yet. With
    /// <see cref="RoyaleBots.Unarmed"/> the bots have no rounds, so nothing but the zone takes a tank out and every ring is
    /// run; with <see cref="RoyaleBots.Tough"/> every tank (the player's too) has <see cref="ToughHealth"/> times its
    /// health: the bots fight as ever, but shells hardly kill, so the late rings are run fighting (batch C review follow-up).
    /// </summary>
    public static BotArena Seat(uint seed, bool recording = true, RoyaleBots bots = RoyaleBots.Armed)
    {
        BotArena arena = BotArena.Royale(MapCatalog.Riverland, seed);
        arena.Recording = recording;
        var seating = new BotSeating(seed);
        arena.Join(Spec(TankCatalog.Medium, bots), 0);   // seat 0: the player, who came into the room first
        for (int slot = 1; slot < Seats; slot++)
        {
            Bot bot = arena.Bot(Spec(TankCatalog.All[seating.NextTankIndex()], bots), 0, seating.BotSeed(slot));
            if (bots != RoyaleBots.Unarmed) continue;
            ref TankState t = ref arena.Sim.TankRef(bot.TankId);
            (t.ApLeft, t.HeLeft) = (0, 0);
        }
        arena.Director.AssignRoles();
        return arena;
    }

    private static TankSpec Spec(TankSpec spec, RoyaleBots bots) =>
        bots == RoyaleBots.Tough ? spec with { MaxHealth = spec.MaxHealth * ToughHealth } : spec;

    /// <summary>Plays the riverland royale of <paramref name="seed"/> (<paramref name="bots"/>: see <see cref="Seat"/>) to its end (or <see cref="MaxTicks"/>).</summary>
    public static RoyaleMatch Play(uint seed, RoyaleBots bots = RoyaleBots.Armed)
    {
        BotArena arena = Seat(seed, bots: bots);
        MatchRules rules = arena.Rules;
        var outside = new int[ZoneSchedule.Rings];
        Array.Fill(outside, -1);
        var detail = new List<string>();
        int[] onsets = new int[ZoneSchedule.Rings], fightToRun = new int[ZoneSchedule.Rings];
        var urgent = new bool[Simulation.MaxTanks + 1];
        var engaging = new bool[Simulation.MaxTanks + 1];
        while (rules.Phase == MatchPhase.Playing && arena.Sim.Tick - rules.StartTick < MaxTicks)
        {
            arena.Tick();
            int elapsed = arena.Sim.Tick - rules.StartTick;
            WatchUrgent(arena, urgent, engaging, onsets, fightToRun);
            for (int k = 1; k <= ZoneSchedule.Rings; k++)
                if (elapsed == ZoneSchedule.ShrinkEnd(k)) outside[k - 1] = CountOutside(arena, k, detail);
        }

        int zoneKills = 0, shellKills = 0, hits = 0, firstZone = -1;
        foreach (GameEvent e in arena.Events)
        {
            if (e.Type == GameEventType.TankHit) hits++;
            if (e.Type != GameEventType.TankDestroyed) continue;
            if (e.Cause == KillCause.Shell)
            {
                shellKills++;
                continue;
            }
            zoneKills++;
            if (firstZone < 0 && arena.Director.Find(e.TargetTankId) is not null) firstZone = e.Tick - rules.StartTick;
        }
        int worstTank = 0;
        float worst = 0f;
        foreach (Bot bot in arena.Director.Bots)
        {
            float s = arena.LongestStuckSeconds(bot.TankId);
            if (s > worst) (worst, worstTank) = (s, bot.TankId);
        }
        byte winner = rules.WinnerSlot;
        string winnerTank = "none";
        bool winnerAlive = false;
        if (winner != MatchRules.NoSlot)
        {
            TankState t = arena.Tank(rules.Of(winner).TankId);
            (winnerTank, winnerAlive) = (t.Spec.Id, !t.IsDestroyed);
        }
        bool lastByZone = false;
        foreach (GameEvent e in arena.Events)
            if (e.Type == GameEventType.TankDestroyed) lastByZone = e.Cause == KillCause.Zone;
        return new RoyaleMatch(seed, bots, rules.EndReason, rules.EndedAt - rules.StartTick, winner, winnerTank, winnerAlive, zoneKills, shellKills, hits,
            firstZone, worst, worstTank, outside, detail, onsets, fightToRun, lastByZone, arena.Events.ToArray(), arena.Director.Summary());
    }

    /// <summary>Counts each live bot's turning <see cref="Bot.ZoneUrgent"/> this tick under the zone's ring, and those that were stopped to fight the tick before.</summary>
    private static void WatchUrgent(BotArena arena, bool[] urgent, bool[] engaging, int[] onsets, int[] fightToRun)
    {
        int ring = Math.Clamp(arena.Rules.Zone.Ring, 1, ZoneSchedule.Rings);
        foreach (Bot bot in arena.Director.Bots)
        {
            int id = bot.TankId;
            bool now = bot.ZoneUrgent && !arena.Tank(id).IsDestroyed;
            if (now && !urgent[id])
            {
                onsets[ring - 1]++;
                if (engaging[id]) fightToRun[ring - 1]++;
            }
            (urgent[id], engaging[id]) = (now, bot.Activity == BotActivity.Engaging);
        }
    }

    /// <summary>The live bots outside the safe circle as ring <paramref name="ring"/> finishes shrinking, each noted in <paramref name="detail"/>.</summary>
    private static int CountOutside(BotArena arena, int ring, List<string> detail)
    {
        ZoneState zone = arena.Rules.Zone;
        int n = 0;
        foreach (Bot bot in arena.Director.Bots)
        {
            TankState t = arena.Tank(bot.TankId);
            if (t.IsDestroyed || !zone.IsOutside(t.Position)) continue;
            n++;
            float d = Vector2.Distance(t.Position, zone.Current.Centre) - zone.Current.Radius;
            detail.Add($"ring {ring}: tank {bot.TankId} ({t.Spec.Id}) {d:F0} m out, {bot.Activity}, running {bot.RunningToZone}, health {t.Health}/{t.Spec.MaxHealth}");
        }
        return n;
    }

    /// <summary>The one line the tests print.</summary>
    public override string ToString() =>
        $"royale seed {Seed}{Bots switch { RoyaleBots.Unarmed => " (unarmed bots)", RoyaleBots.Tough => $" (×{ToughHealth} health)", _ => "" }}: {EndReason} at {EndedAt / (float)Tps:F1} s, " +
        $"winner seat {(WinnerSlot == MatchRules.NoSlot ? "none" : WinnerSlot.ToString())} " +
        $"({WinnerTank}, {(WinnerAlive ? "alive" : "gone")}); kills: {ShellKills} by shells, {ZoneKills} by the zone (first bot at {(FirstBotZoneDeath < 0 ? "never" : $"{FirstBotZoneDeath / (float)Tps:F1} s")}); " +
        $"{ShellHits} hits; live bots outside at rings 1–5's end: {string.Join(", ", OutsideAtRingEnd.Select(n => n < 0 ? "-" : n.ToString()))}" +
        $"{(OutsideDetail.Count == 0 ? "" : $" ({string.Join("; ", OutsideDetail)})")}; urgent in rings 1–5: {string.Join(", ", UrgentOnsets)} " +
        $"(from a fight: {string.Join(", ", FightToRun)}); longest stuck {LongestStuck:F1} s (tank {LongestStuckTank}); {Summary}";
}

/// <summary>Who the bots of a <see cref="RoyaleMatch"/> are.</summary>
public enum RoyaleBots
{
    /// <summary>As the server seats them.</summary>
    Armed,
    /// <summary>Without rounds: only the zone takes a tank out.</summary>
    Unarmed,
    /// <summary>Armed, every tank with <see cref="RoyaleMatch.ToughHealth"/> times its health: the late rings run fighting.</summary>
    Tough,
}

/// <summary>
/// The royales of <see cref="RoyaleMatchTests"/>, each played once however many tests read it (a class fixture), all side
/// by side from the first read: each of <see cref="Seeds"/> armed, each of <see cref="UnarmedSeeds"/> unarmed, each of
/// <see cref="ToughSeeds"/> with tough tanks, and seed 3 armed twice, for the replay.
/// </summary>
public sealed class RoyaleMatches : IDisposable
{
    public static readonly uint[] Seeds = { 3, 7, 11, 19 };

    /// <summary>
    /// The <see cref="RoyaleBots.Unarmed"/> matches (batch C review follow-up): seed 8 is where three bots waited on each
    /// other's paths in the last ring for 42 s. Seed 3, left out of the batch C follow-up (two bots wedged at the rocks
    /// west of the south ramp's foot, (226, 333) and (197, 318), outside as ring 3 finished shrinking), is back since the
    /// cliff-foot traps were fixed (plan, deviation 28).
    /// </summary>
    public static readonly uint[] UnarmedSeeds = { 3, 7, 8, 11, 19 };

    /// <summary>The <see cref="RoyaleBots.Tough"/> matches (batch C review follow-up): <see cref="Seeds"/> and seed 8.</summary>
    public static readonly uint[] ToughSeeds = { 3, 7, 8, 11, 19 };

    /// <summary>The seed played a second time.</summary>
    public const uint ReplaySeed = 3;

    /// <summary>The longest a read waits for its match.</summary>
    public static readonly TimeSpan Patience = TimeSpan.FromMinutes(10);

    private readonly object _gate = new();
    private Dictionary<(uint Seed, int Run, RoyaleBots Bots), Task<RoyaleMatch>>? _matches;

    internal RoyaleMatch Get(uint seed, int run = 0, RoyaleBots bots = RoyaleBots.Armed)
    {
        lock (_gate) _matches ??= StartAll();
        Task<RoyaleMatch> match = _matches[(seed, run, bots)];
        try
        {
            if (!match.Wait(Patience)) throw new TimeoutException($"royale seed {seed} still playing after {Patience.TotalMinutes:F0} min");
        }
        catch (AggregateException) { }   // it failed: thrown as itself just below
        return match.GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_matches is null) return;
            try { Task.WaitAll(_matches.Values.ToArray(), Patience); }
            catch (AggregateException) { }   // the test that read it failed with it already
        }
    }

    private static Dictionary<(uint, int, RoyaleBots), Task<RoyaleMatch>> StartAll()
    {
        var all = new Dictionary<(uint, int, RoyaleBots), Task<RoyaleMatch>>();
        foreach (uint seed in ToughSeeds) all[(seed, 0, RoyaleBots.Tough)] = Start(seed, RoyaleBots.Tough);   // the longest first
        foreach (uint seed in UnarmedSeeds) all[(seed, 0, RoyaleBots.Unarmed)] = Start(seed, RoyaleBots.Unarmed);
        foreach (uint seed in Seeds) all[(seed, 0, RoyaleBots.Armed)] = Start(seed, RoyaleBots.Armed);
        all[(ReplaySeed, 1, RoyaleBots.Armed)] = Start(ReplaySeed, RoyaleBots.Armed);
        return all;
    }

    private static Task<RoyaleMatch> Start(uint seed, RoyaleBots bots) =>
        Task.Factory.StartNew(() => RoyaleMatch.Play(seed, bots), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
}
