using Tank.Sim.Maps;
using Tank.Sim.Modes;
using Tank.Testing;
using Xunit.Abstractions;

namespace Tank.Sim.Tests;

/// <summary>
/// Whole royales on the riverland, an idle player and fifteen bots, played headless as the server plays them (stage 6
/// design §3.4, §6; stage 6A plan, task 6): each ends with one tank left, soon after the zone has closed at the latest;
/// nobody gets stuck; the bots fight; they run the zone, so none dies in it before ring 3 has shrunk and at most one is
/// left outside as each ring finishes shrinking (none, fighting through every ring with tough tanks). Each match is
/// played once for the class (<see cref="RoyaleMatches"/>).
/// </summary>
public sealed class RoyaleMatchTests : IClassFixture<RoyaleMatches>
{
    private const int Tps = SimConstants.TickRate;

    /// <summary>No bot may get nowhere this long (stage 5 design §6), s.</summary>
    private const float StuckLimit = 10f;

    /// <summary>A match ends at most this long after the zone has closed (everyone outside loses 10 % a second), ticks.</summary>
    private const int AfterClose = 20 * Tps;

    private readonly RoyaleMatches _matches;
    private readonly ITestOutputHelper _output;

    public RoyaleMatchTests(RoyaleMatches matches, ITestOutputHelper output) => (_matches, _output) = (matches, output);

    public static TheoryData<uint> Seeds => TheoryOf(RoyaleMatches.Seeds);

    public static TheoryData<uint> UnarmedSeeds => TheoryOf(RoyaleMatches.UnarmedSeeds);

    public static TheoryData<uint> ToughSeeds => TheoryOf(RoyaleMatches.ToughSeeds);

    private static TheoryData<uint> TheoryOf(uint[] seeds)
    {
        var data = new TheoryData<uint>();
        foreach (uint seed in seeds) data.Add(seed);
        return data;
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Fifteen_bots_and_an_idle_player_play_a_royale_through(uint seed)
    {
        RoyaleMatch m = Play(seed);

        Assert.Equal(MatchEndReason.Eliminated, m.EndReason);
        Assert.True(m.EndedAt <= ZoneSchedule.ClosedAt + AfterClose, $"seed {seed}: over at {m.EndedAt / (float)Tps:F1} s");
        Assert.NotEqual(MatchRules.NoSlot, m.WinnerSlot);
        Assert.True(m.LongestStuck < StuckLimit, $"seed {seed}: tank {m.LongestStuckTank} got nowhere for {m.LongestStuck:F1} s");
        Assert.True(m.ShellHits >= 20, $"seed {seed}: only {m.ShellHits} hits");
        Assert.True(m.ShellKills >= 6, $"seed {seed}: only {m.ShellKills} tanks destroyed by shells");
        Assert.True(m.FirstBotZoneDeath < 0 || m.FirstBotZoneDeath >= ZoneSchedule.ShrinkEnd(3),
            $"seed {seed}: a bot died in the zone at {m.FirstBotZoneDeath / (float)Tps:F1} s, before ring 3 had shrunk");
        for (int k = 1; k < ZoneSchedule.Rings; k++)
        {
            if (OverBefore(m, k)) continue;
            Assert.True(m.OutsideAtRingEnd[k - 1] is >= 0 and <= 1, $"seed {seed}: {m.OutsideAtRingEnd[k - 1]} live bots outside as ring {k} finished shrinking");
        }
    }

    /// <summary>
    /// Fifteen bots fighting through every ring (batch C review follow-up): every tank has <see cref="RoyaleMatch.ToughHealth"/>
    /// times its health, so shells hardly kill and the zone (a share of the health a second) decides. The bots get into
    /// rings 1–4 in time, all of them, fighting meanwhile; in rings 2–5 some drop a fight to run when time runs short
    /// (design §3.4: "剩余时间不够时放下交战先跑"; the switch the armed matches, over before ring 2 has shrunk, never
    /// reach); nobody gets stuck; someone wins standing, or the last die in the zone.
    /// </summary>
    [Theory]
    [MemberData(nameof(ToughSeeds))]
    public void Fifteen_tough_bots_fight_through_every_ring_of_a_royale(uint seed)
    {
        RoyaleMatch m = Play(seed, RoyaleBots.Tough);

        Assert.Equal(MatchEndReason.Eliminated, m.EndReason);
        Assert.InRange(m.EndedAt, ZoneSchedule.ShrinkEnd(ZoneSchedule.Rings - 1), ZoneSchedule.ClosedAt + AfterClose);
        Assert.True(m.WinnerAlive || m.LastKillByZone, $"seed {seed}: the winner is gone and the last kill wasn't the zone's");
        Assert.True(m.LongestStuck < StuckLimit, $"seed {seed}: tank {m.LongestStuckTank} got nowhere for {m.LongestStuck:F1} s");
        Assert.True(m.ShellHits >= 20, $"seed {seed}: only {m.ShellHits} hits");
        for (int k = 1; k < ZoneSchedule.Rings; k++)
            Assert.True(m.OutsideAtRingEnd[k - 1] == 0, $"seed {seed}: {m.OutsideAtRingEnd[k - 1]} live bots outside as ring {k} finished shrinking");
        Assert.True(m.UrgentOnsets.Skip(1).Sum() > 0, $"seed {seed}: no bot had to hurry in rings 2–5");
        Assert.True(m.FightToRun.Skip(1).Sum() > 0, $"seed {seed}: no bot stopped to fight dropped the fight to run in rings 2–5");
    }

    /// <summary>
    /// The armed matches are over before ring 2 has shrunk, so the later rings are run here: fifteen bots without rounds
    /// (nothing but the zone takes a tank out) play every ring through. They get into each ring in time, rings 1–4 leave
    /// at most one outside as they finish shrinking and the zone takes none before ring 3 has shrunk; nobody gets stuck,
    /// crowded as they are in the small rings. The match ends on the closed zone.
    /// </summary>
    [Theory]
    [MemberData(nameof(UnarmedSeeds))]
    public void Fifteen_unarmed_bots_run_every_ring_of_a_royale(uint seed)
    {
        RoyaleMatch m = Play(seed, RoyaleBots.Unarmed);

        Assert.Equal(MatchEndReason.Eliminated, m.EndReason);
        Assert.InRange(m.EndedAt, ZoneSchedule.ClosedAt - 60 * Tps, ZoneSchedule.ClosedAt + AfterClose);
        Assert.Equal(0, m.ShellKills);
        Assert.True(m.LongestStuck < StuckLimit, $"seed {seed}: tank {m.LongestStuckTank} got nowhere for {m.LongestStuck:F1} s");
        Assert.True(m.FirstBotZoneDeath >= ZoneSchedule.ShrinkEnd(3), $"seed {seed}: a bot died in the zone at {m.FirstBotZoneDeath / (float)Tps:F1} s, before ring 3 had shrunk");
        for (int k = 1; k < ZoneSchedule.Rings; k++)
            Assert.True(m.OutsideAtRingEnd[k - 1] is >= 0 and <= 1, $"seed {seed}: {m.OutsideAtRingEnd[k - 1]} live bots outside as ring {k} finished shrinking");
    }

    /// <summary>Someone wins standing: the other way is the last few dying in the zone on one second (deviation 3).</summary>
    [Fact]
    public void Most_royales_are_won_by_a_tank_still_standing()
    {
        RoyaleMatch[] matches = RoyaleMatches.Seeds.Select(Play).ToArray();
        int standing = matches.Count(m => m.WinnerAlive);
        Assert.True(standing >= 3, $"only {standing} of {matches.Length} royales won by a tank still standing");
    }

    [Fact]
    public void A_royale_replays_from_its_seed()
    {
        RoyaleMatch first = _matches.Get(RoyaleMatches.ReplaySeed), second = _matches.Get(RoyaleMatches.ReplaySeed, run: 1);

        Assert.Equal(first.EndedAt, second.EndedAt);
        Assert.Equal(first.WinnerSlot, second.WinnerSlot);
        Assert.Equal(first.Events.Count, second.Events.Count);
        for (int k = 0; k < first.Events.Count; k++) Assert.Equal(first.Events[k], second.Events[k]);
    }

    /// <summary>From ring 1's shrink on (tanks outside the ring, runs under way, the zone hurting), with the fighting.</summary>
    [Fact]
    public void A_royale_tick_allocates_nothing()
    {
        Allocation.AssertNone(() =>
        {
            BotArena arena = RoyaleMatch.Seat(3, recording: false);
            arena.Run(ZoneSchedule.ShrinkStart(1));
            Allocation.Window window = Allocation.Start();
            arena.Run(300);   // the director, the bots' commands, the step, the vision and the rules with the zone
            Allocation.Measured measured = window.Stop();
            Assert.Equal(MatchPhase.Playing, arena.Rules.Phase);
            Assert.Equal(ZonePhase.Shrinking, arena.Rules.Zone.Phase);
            return measured;
        });
    }

    /// <summary>Whether <paramref name="m"/> was over before ring <paramref name="ring"/> finished shrinking: there is no count to look at (said so).</summary>
    private bool OverBefore(RoyaleMatch m, int ring)
    {
        if (m.OutsideAtRingEnd[ring - 1] >= 0) return false;
        Assert.True(m.EndedAt < ZoneSchedule.ShrinkEnd(ring), $"seed {m.Seed}: no count for ring {ring}, yet the match went on to {m.EndedAt / (float)Tps:F1} s");
        _output.WriteLine($"ring {ring}: the match was over before it had shrunk ({m.EndedAt / (float)Tps:F1} s < {ZoneSchedule.ShrinkEnd(ring) / (float)Tps:F0} s)");
        return true;
    }

    private RoyaleMatch Play(uint seed) => Play(seed, RoyaleBots.Armed);

    private RoyaleMatch Play(uint seed, RoyaleBots bots)
    {
        RoyaleMatch m = _matches.Get(seed, bots: bots);
        _output.WriteLine(m.ToString());
        return m;
    }
}
