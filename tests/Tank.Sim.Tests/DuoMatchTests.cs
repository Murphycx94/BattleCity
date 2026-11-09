using Tank.Sim.Modes;
using Xunit.Abstractions;

namespace Tank.Sim.Tests;

/// <summary>
/// Whole duos matches on the riverland, an idle player and fifteen bots in eight teams of two, played headless as the server
/// plays them (stage 6 design §5; stage 6C plan, task 5): each is decided within its 10 minutes, every team gets a place of
/// its own, both seats of a team the same; nobody gets stuck; the bots eat crates on the way; and the two of a team keep
/// together (<see cref="MateMedianLimit"/>, <see cref="BotOver100Limit"/>). Without rounds only the zone takes a tank out and
/// the bots run every ring. Each match is played once for the class (<see cref="DuoMatches"/>).
/// </summary>
public sealed class DuoMatchTests : IClassFixture<DuoMatches>
{
    private const int Tps = SimConstants.TickRate;

    /// <summary>No bot may get nowhere this long, s (plan: the royale's; measured after the batch C review follow-up 2.1–5.3 s armed, 4.1 s unarmed).</summary>
    private const float StuckLimit = 6f;

    /// <summary>The bots eat at least this many crates in an armed match (measured 17–23 on the four seeds).</summary>
    private const int BotCratesShort = 3;

    /// <summary>A match ends at most this long after the zone has closed (everyone outside loses 10 % a second), ticks.</summary>
    private const int AfterClose = 20 * Tps;

    /// <summary>
    /// The median distance between the two of a team with both alive, m (plan: 60 at most, set within 1.5 times what was
    /// measured at task 5: 28, 48, 49, 51 m on seeds 19, 3, 7, 11; after the batch C review follow-up 31, 33, 43, 55 m on
    /// seeds 3, 19, 11, 7 and 36 m unarmed). The 55 m is seed 7's, where the idle player is left behind by the ring.
    /// </summary>
    private const float MateMedianLimit = 60f;

    /// <summary>
    /// The share of the samples of the seven teams of two bots with the two over 100 m apart, at most (batch C review
    /// follow-up). At task 5 it was 24.6, 4.9, 18.5, 12.1 % on seeds 3, 7, 11, 19 (13.6 % unarmed): a slower follower chased
    /// a wandering leader for minutes, never closing in; a leader now waits for a follower heading for it. Measured after:
    /// 4.2, 4.7, 11.4, 9.0 % (0.5 % unarmed). The rest was not traced; the rules still leave the two apart while they run
    /// the zone at their own speeds (no regrouping while running) and on a leader's crate runs. Set at 15 % (1.5 times the
    /// worst would be 17 %).
    /// </summary>
    private const float BotOver100Limit = 0.15f;

    private readonly DuoMatches _matches;
    private readonly ITestOutputHelper _output;

    public DuoMatchTests(DuoMatches matches, ITestOutputHelper output) => (_matches, _output) = (matches, output);

    public static TheoryData<uint> Seeds
    {
        get
        {
            var data = new TheoryData<uint>();
            foreach (uint seed in DuoMatches.Seeds) data.Add(seed);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Fifteen_bots_and_an_idle_player_play_duos_through(uint seed)
    {
        DuoMatch m = Play(seed);

        Assert.True(m.EndReason is MatchEndReason.Eliminated or MatchEndReason.TimeUp, $"seed {seed}: ended by {m.EndReason}");
        Assert.InRange(m.EndedAt, 1, DuoMatch.MaxTicks);
        Assert.NotEqual(MatchRules.NoTeam, m.WinnerTeam);
        AssertPlaces(m);
        Assert.True(m.BotCratesTaken >= BotCratesShort, $"seed {seed}: the bots ate only {m.BotCratesTaken} crates");
        Assert.True(m.LongestStuck < StuckLimit, $"seed {seed}: tank {m.LongestStuckTank} got nowhere for {m.LongestStuck:F1} s");
        Assert.True(m.MateMedian <= MateMedianLimit, $"seed {seed}: team-mates {m.MateMedian:F0} m apart (median)");
        Assert.True(m.BotMateOver100 <= BotOver100Limit, $"seed {seed}: two bots of a team over 100 m apart {m.BotMateOver100 * 100f:F1}% of the time");
        Assert.True(m.Regroups > 0, $"seed {seed}: no bot headed for its team-mate");
    }

    /// <summary>Bots without rounds (or racks an ammo crate could fill): only the zone takes a tank out, so every ring is run.</summary>
    [Fact]
    public void Fifteen_unarmed_bots_run_every_ring_of_duos()
    {
        DuoMatch m = Play(DuoMatches.UnarmedSeed, RoyaleBots.Unarmed);

        Assert.InRange(m.EndedAt, ZoneSchedule.ClosedAt - 60 * Tps, Math.Min(DuoMatch.MaxTicks, ZoneSchedule.ClosedAt + AfterClose));
        Assert.DoesNotContain(m.Events, e => e.Type == GameEventType.TankDestroyed && e.Cause == KillCause.Shell);
        AssertPlaces(m);
        Assert.True(m.LongestStuck < StuckLimit, $"tank {m.LongestStuckTank} got nowhere for {m.LongestStuck:F1} s");
        Assert.True(m.BotMateOver100 <= BotOver100Limit, $"two bots of a team over 100 m apart {m.BotMateOver100 * 100f:F1}% of the time");
        for (int k = 1; k < ZoneSchedule.Rings; k++)
            Assert.True(m.OutsideAtRingEnd[k - 1] == 0, $"{m.OutsideAtRingEnd[k - 1]} live bots outside as ring {k} finished shrinking");
    }

    [Fact]
    public void A_duos_match_replays_from_its_seed()
    {
        DuoMatch first = _matches.Get(DuoMatches.ReplaySeed), second = _matches.Get(DuoMatches.ReplaySeed, run: 1);

        Assert.Equal(first.EndedAt, second.EndedAt);
        Assert.Equal(first.WinnerTeam, second.WinnerTeam);
        Assert.Equal(first.TeamRanks, second.TeamRanks);
        Assert.Equal(first.Events.Count, second.Events.Count);
        for (int k = 0; k < first.Events.Count; k++) Assert.Equal(first.Events[k], second.Events[k]);
    }

    /// <summary>Eight teams placed 1–8, one each, both seats of a team alike.</summary>
    private static void AssertPlaces(DuoMatch m)
    {
        Assert.Equal(ModeRules.DuoTeams, m.Places);
        Assert.True(m.RanksAgree, $"seed {m.Seed}: the two seats of a team have different places");
        Assert.Equal(Enumerable.Range(1, ModeRules.DuoTeams), m.TeamRanks.Select(r => (int)r).Order());
    }

    private DuoMatch Play(uint seed, RoyaleBots bots = RoyaleBots.Armed)
    {
        DuoMatch m = _matches.Get(seed, bots: bots);
        _output.WriteLine(m.ToString());
        return m;
    }
}
