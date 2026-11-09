using System.Numerics;
using Tank.Sim.Ai;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;
using Tank.Sim.Vision;
using Tank.Testing;
using Xunit.Abstractions;

namespace Tank.Sim.Tests;

/// <summary>
/// Bots in duos keep with their team-mate (stage 6 design §5; stage 6C plan, task 5): a bot whose live team-mate is a person,
/// or a bot with a lower tank id, follows it — it doesn't wander, and heads for the team-mate (<see cref="Bot.Regrouping"/>)
/// once more than <see cref="BotDirector.MateRange"/> off, stopping within <see cref="BotDirector.MateArrive"/>; not while the
/// team-mate is out of the ring, while running the zone itself, or on a crate run. The two run for one point of the ring, the
/// follower's a little further in; each prefers the enemy its team-mate is fighting. Batch C review follow-up: a follower
/// plays on once its team-mate falls or leaves, a leader waits for a follower heading for it and turns back while the
/// follower waits out a regroup it gave up, a regroup whose way leaves the ring is dropped, a follower's crates lie near its
/// team-mate, and a fast follower stops short of a team-mate standing still. Played on the flat 400 m arena.
/// </summary>
public sealed class BotDuoTests
{
    private const int Tps = SimConstants.TickRate;

    private static readonly Vector2 Centre = new(200, 200);

    /// <summary>Ring 1 (200 m) round the arena's centre holds every waypoint well inside: nobody runs before 90 s.</summary>
    private static readonly ZoneRing[] BigRings =
    {
        new(Centre, 283f), new(Centre, 200f), new(Centre, 150f), new(Centre, 100f), new(Centre, 50f), new(Centre, 0f),
    };

    /// <summary>Ring 1 80 m round (300, 300), then closing on (320, 300) (as the royale's zone tests have it).</summary>
    private static readonly ZoneRing[] OffRings =
    {
        new(Centre, 283f),
        new(new Vector2(300, 300), 80f),
        new(new Vector2(310, 300), 50f),
        new(new Vector2(320, 300), 30f),
        new(new Vector2(320, 300), 25f),
        new(new Vector2(320, 300), 0f),
    };

    /// <summary>A far corner the enemy that keeps the match on stands in, out of everyone's sight.</summary>
    private static readonly Vector2 FarCorner = new(385, 385);

    private readonly ITestOutputHelper _output;

    public BotDuoTests(ITestOutputHelper output) => _output = output;

    private static BotArena Duos(IReadOnlyList<ZoneRing>? rings, IReadOnlyList<Crate>? crates = null, MapDefinition? map = null) =>
        new(BattleMode.Duos, map: map, zone: rings, crates: crates);

    /// <summary>A bot of <paramref name="team"/> without rounds (a medium unless <paramref name="spec"/>): it never stops to fight, so only its team-mate and the zone move it.</summary>
    private static Bot Unarmed(BotArena arena, byte team, Vector2 at, uint seed, TankSpec? spec = null)
    {
        Bot bot = arena.Bot(spec ?? TankCatalog.Medium, team, seed, at, BotArena.East);
        ref TankState t = ref arena.Sim.TankRef(bot.TankId);
        (t.ApLeft, t.HeLeft) = (0, 0);
        return bot;
    }

    /// <summary>An enemy nobody sees, so that the match goes on (one team alone ends it).</summary>
    private static void KeepOn(BotArena arena, byte team = 7) => arena.Idle(TankCatalog.Medium, team, FarCorner);

    private static float Apart(BotArena arena, int a, int b) => Vector2.Distance(arena.Tank(a).Position, arena.Tank(b).Position);

    [Fact]
    public void A_bot_heads_for_its_person_team_mate_once_too_far_stops_near_it_and_stays()
    {
        var arena = Duos(BigRings);
        int player = arena.Idle(TankCatalog.Medium, 0, new Vector2(100, 200));
        Bot bot = Unarmed(arena, 0, new Vector2(180, 200), 1);
        KeepOn(arena);

        arena.Run(Tps);
        Assert.True(bot.Regrouping, "not heading for its team-mate 80 m off");
        Assert.Equal(1, bot.Regroups);
        Assert.True(Vector2.Distance(bot.Goal, arena.Tank(player).Position) <= 5f, $"heading for {bot.Goal}, not its team-mate");

        int arrivedAt = -1;
        arena.Run(30 * Tps, tick =>
        {
            if (arrivedAt < 0 && !bot.Regrouping) arrivedAt = tick;
        });
        Assert.True(arrivedAt > 0, "never got to its team-mate");
        Assert.Equal(BotActivity.Holding, bot.Activity);
        Assert.InRange(Apart(arena, player, bot.TankId), 0f, BotDirector.MateArrive + 1f);

        Vector2 held = arena.Tank(bot.TankId).Position;
        arena.Run(20 * Tps, _ =>
        {
            Assert.False(bot.Regrouping);
            Assert.True(Vector2.Distance(arena.Tank(bot.TankId).Position, held) <= 2f, $"wandered off to {arena.Tank(bot.TankId).Position}");
        });

        arena.Place(player, held + new Vector2(70, 0), BotArena.East);
        arena.Run(Tps);
        _output.WriteLine($"arrived at {arrivedAt / (float)Tps:F1} s; {arena.Director.Summary()}");
        Assert.True(bot.Regrouping, "not heading for its team-mate moved 70 m off");
        Assert.Equal(2, bot.Regroups);
        Assert.Contains("2 regroups; ", arena.Director.Summary());
    }

    [Fact]
    public void Of_two_bots_the_higher_id_follows_and_the_lower_never_turns_back_for_it()
    {
        var arena = Duos(BigRings);
        Bot leader = Unarmed(arena, 0, new Vector2(100, 200), 1);
        Bot follower = Unarmed(arena, 0, new Vector2(200, 200), 2);
        KeepOn(arena);
        Assert.True(leader.TankId < follower.TankId);

        bool leaderTurned = false;
        int arrivedAt = -1;
        arena.Run(2, _ => { });
        Assert.True(follower.Regrouping, "the follower isn't heading for its leader 100 m off");
        arena.Run(40 * Tps, tick =>
        {
            leaderTurned |= leader.Regrouping;
            if (arrivedAt < 0 && follower.Regroups >= 1 && !follower.Regrouping) arrivedAt = tick;
        });
        _output.WriteLine($"the follower got there at {arrivedAt / (float)Tps:F1} s; {follower.Regroups} regroups; {arena.Director.Summary()}");
        Assert.False(leaderTurned, "the leader headed for its follower");
        Assert.Equal(0, leader.Regroups);
        Assert.True(arrivedAt > 0, "the follower never got to its leader");
        Assert.True(arena.Director.Follows(follower, arena.Tank(follower.TankId)));
        Assert.False(arena.Director.Follows(leader, arena.Tank(leader.TankId)));
    }

    [Fact]
    public void A_bot_does_not_head_for_a_team_mate_out_of_the_ring()
    {
        var arena = Duos(OffRings);
        arena.Idle(TankCatalog.Medium, 0, new Vector2(150, 150));   // out of ring 1, 212 m off
        Bot bot = Unarmed(arena, 0, new Vector2(300, 300), 1);
        KeepOn(arena);

        arena.Run(10 * Tps, _ => Assert.False(bot.Regrouping, "heading for a team-mate out of the ring"));
        Assert.Equal(0, bot.Regroups);
        Assert.Equal(BotActivity.Holding, bot.Activity);   // a follower doesn't wander
    }

    [Fact]
    public void A_bot_running_the_zone_does_not_head_for_its_team_mate()
    {
        var arena = Duos(OffRings);
        int mate = arena.Idle(TankCatalog.Medium, 0, new Vector2(300, 300));
        Bot bot = Unarmed(arena, 0, new Vector2(100, 100), 1);   // out of ring 1, 283 m off its team-mate
        KeepOn(arena);

        bool ran = false;
        arena.Run(60 * Tps, _ =>
        {
            ran |= bot.RunningToZone;
            if (bot.RunningToZone) Assert.False(bot.Regrouping, "heading for its team-mate while running");
        });
        Assert.True(ran, "never ran");
        Assert.False(bot.RunningToZone, "still running after 60 s");
        Assert.InRange(Apart(arena, mate, bot.TankId), 0f, BotDirector.MateRange);
    }

    [Fact]
    public void A_crate_run_is_not_cut_short_for_the_team_mate()
    {
        // the crate 50 m from the person (a follower fetches only crates within MateRange of its team-mate), 41 m from the bot
        var arena = Duos(BigRings, new[] { new Crate(0, new Vector2(130, 240), CrateKind.Armor) });
        int player = arena.Idle(TankCatalog.Medium, 0, new Vector2(100, 200));
        Bot bot = Unarmed(arena, 0, new Vector2(140, 200), 1);
        KeepOn(arena);

        arena.Run(VisionTracker.UpdateInterval + 1);
        Assert.Equal(1, bot.CrateRuns);
        arena.Place(player, new Vector2(40, 200), BotArena.East);   // the team-mate now 100 m off, the run under way

        int ateAt = -1;
        arena.Run(40 * Tps, tick =>
        {
            if (ateAt < 0 && bot.CratesTaken == 1) ateAt = tick;
            if (ateAt < 0) Assert.False(bot.Regrouping, $"the crate run was cut short {Apart(arena, player, bot.TankId):F0} m off the team-mate");
        });
        Assert.Equal(1, bot.CrateRuns);
        Assert.True(ateAt > 0, "never ate the crate");
        Assert.True(bot.Regroups >= 1, "never headed back for its team-mate 100 m off");
    }

    [Fact]
    public void Once_its_team_mate_is_destroyed_a_follower_wanders_again()
    {
        var arena = Duos(null);
        int player = arena.Idle(TankCatalog.Medium, 0, new Vector2(100, 200));
        Bot bot = Unarmed(arena, 0, new Vector2(200, 200), 1);
        KeepOn(arena);

        arena.Run(Tps);
        Assert.True(bot.Regrouping);
        ref TankState t = ref arena.Sim.TankRef(player);
        (t.IsDestroyed, t.Health) = (true, 0);
        arena.Run(2);
        Assert.False(bot.Regrouping, "still heading for a wreck");
        Assert.Contains(bot.Goal, BotArena.Waypoints);
        arena.Run(2 * Tps);
        Assert.Equal(BotActivity.Driving, bot.Activity);
        Assert.False(arena.Director.Follows(bot, arena.Tank(bot.TankId)));
    }

    [Fact]
    public void A_duo_runs_for_one_point_of_the_ring_the_follower_a_little_further_in()
    {
        var arena = Duos(OffRings);
        Bot leader = Unarmed(arena, 0, new Vector2(100, 100), 1);
        Bot follower = Unarmed(arena, 0, new Vector2(110, 100), 2);
        Bot other = Unarmed(arena, 1, new Vector2(100, 300), 3);
        arena.Run(1);
        ZoneRing ring = OffRings[1];

        Vector2 a = arena.Director.ZonePoint(leader, ring, 0), b = arena.Director.ZonePoint(follower, ring, 0);
        Vector2 c = arena.Director.ZonePoint(other, ring, 0);
        float ra = Vector2.Distance(a, ring.Centre), rb = Vector2.Distance(b, ring.Centre);
        _output.WriteLine($"leader {a} ({ra:F1} m from the centre), follower {b} ({rb:F1} m), another team's {c}");
        Assert.InRange(Turn(a - ring.Centre, b - ring.Centre), 0f, 12f);   // the cells they snap to: 4 m apart
        Assert.InRange(ra - rb, BotDirector.ZoneMateSpread - 4f, BotDirector.ZoneMateSpread + 4f);
        Assert.True(Vector2.Distance(a, c) >= 10f, $"another team runs for {c}, by the duo's {a}");

        ZoneRing small = OffRings[4];   // 25 m: the point 12.5 m out, the follower's moved half that at most
        Vector2 sa = arena.Director.ZonePoint(leader, small, 0), sb = arena.Director.ZonePoint(follower, small, 0);
        Assert.True(Turn(sa - small.Centre, sb - small.Centre) <= 45f, $"the follower's point {sb} crosses the centre from {sa}");
    }

    [Fact]
    public void In_a_royale_two_bots_run_for_points_apart()
    {
        var arena = new BotArena(BattleMode.Royale, zone: OffRings);
        Bot one = Unarmed(arena, 0, new Vector2(100, 100), 1);
        Bot two = Unarmed(arena, 0, new Vector2(110, 100), 2);
        arena.Run(1);
        ZoneRing ring = OffRings[1];

        Vector2 a = arena.Director.ZonePoint(one, ring, 0), b = arena.Director.ZonePoint(two, ring, 0);
        Assert.True(Turn(a - ring.Centre, b - ring.Centre) > 60f, $"{a} and {b} lie the same way");
        Assert.Equal(-1, arena.Director.MateSlotOf(arena.Tank(one.TankId)));
        Assert.DoesNotContain("regroups", arena.Director.Summary());
    }

    [Fact]
    public void The_picker_puts_the_enemy_preferred_first_when_it_is_a_candidate()
    {
        var arena = Duos(BigRings);
        int me = arena.Idle(TankCatalog.Medium, 0, new Vector2(150, 200));
        int near = arena.Idle(TankCatalog.Medium, 1, new Vector2(250, 190));
        int far = arena.Idle(TankCatalog.Medium, 2, new Vector2(250, 262));
        int unseen = arena.Idle(TankCatalog.Medium, 3, FarCorner);
        arena.Run(VisionTracker.UpdateInterval + 1);
        int tick = arena.Sim.Tick + 1;

        Assert.Equal(near, new TargetPicker(me).Pick(arena.Sim, arena.Vision, tick));
        Assert.Equal(far, new TargetPicker(me).Pick(arena.Sim, arena.Vision, tick, prefer: far));
        Assert.Equal(near, new TargetPicker(me).Pick(arena.Sim, arena.Vision, tick, prefer: unseen));   // not a candidate: ranked as ever
    }

    [Fact]
    public void A_bot_turns_to_the_enemy_its_bot_team_mate_fights()
    {
        var arena = Duos(BigRings);
        Bot mate = arena.Bot(TankCatalog.Medium, 0, 2, new Vector2(150, 240), BotArena.East);   // the lower id: it picks first, the far one
        Bot bot = arena.Bot(TankCatalog.Medium, 0, 1, new Vector2(150, 200), BotArena.East);
        int near = arena.Idle(Tough(), 1, new Vector2(250, 190));
        int far = arena.Idle(Tough(), 2, new Vector2(250, 262));

        arena.Sim.TankRef(mate.TankId).Reload = 10f;
        arena.Run(4 * Tps, _ => arena.Sim.TankRef(mate.TankId).Reload = 10f);   // the team-mate never fires: the far one stays whole
        _output.WriteLine($"bot {bot.TankId} on {bot.TargetId}, its team-mate on {mate.TargetId} (near {near}, far {far})");
        Assert.Equal(far, mate.TargetId);
        Assert.Equal(far, bot.TargetId);
        Assert.Equal(far, arena.Director.MateTarget(arena.Tank(bot.TankId), arena.Sim.Tick + 1));
    }

    [Fact]
    public void A_bot_turns_to_the_enemy_its_person_team_mate_hit_for_three_seconds()
    {
        var arena = Duos(BigRings);
        Bot bot = arena.Bot(TankCatalog.Medium, 0, 1, new Vector2(150, 200), BotArena.East);
        int player = arena.Idle(TankCatalog.Medium, 0, new Vector2(150, 240));
        int near = arena.Idle(Tough(), 1, new Vector2(250, 190));
        int far = arena.Idle(Tough(), 2, new Vector2(250, 262));

        arena.Run(Tps);
        Assert.Equal(near, bot.TargetId);
        Assert.Equal(0, arena.Director.MateTarget(arena.Tank(bot.TankId), arena.Sim.Tick + 1));

        int hitAt = arena.Sim.Tick;
        arena.Director.HearEvent(new GameEvent(GameEventType.TankHit, hitAt, player, 0, default, default, TargetTankId: far));
        arena.Run(TargetPicker.HoldTicks + VisionTracker.UpdateInterval);
        Assert.Equal(far, bot.TargetId);
        Assert.Equal(far, arena.Director.MateTarget(arena.Tank(bot.TankId), hitAt + TargetPicker.HitMemoryTicks));
        Assert.Equal(0, arena.Director.MateTarget(arena.Tank(bot.TankId), hitAt + TargetPicker.HitMemoryTicks + 1));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_follower_holding_for_its_team_mate_plays_on_once_the_team_mate_falls_or_leaves(bool leaves)
    {
        var arena = Duos(BigRings);
        int player = arena.Idle(TankCatalog.Medium, 0, new Vector2(100, 200));
        Bot bot = Unarmed(arena, 0, new Vector2(120, 200), 1);
        KeepOn(arena);
        Assert.Equal(player, arena.Rules.Of(0).TankId);

        arena.Run(2 * Tps);
        Assert.Equal(BotActivity.Holding, bot.Activity);
        Assert.False(bot.Regrouping);
        Vector2 held = bot.Goal;

        if (leaves) arena.Rules.Leave(0);
        else
        {
            ref TankState t = ref arena.Sim.TankRef(player);
            (t.IsDestroyed, t.Health) = (true, 0);
        }
        arena.Run(2);
        Assert.True(Vector2.Distance(bot.Goal, held) > BotDirector.WaypointRadius, $"still holding at {held}, its team-mate {(leaves ? "gone" : "destroyed")}");
        Assert.True(BotDirector.InRing(bot.Goal, BigRings[1]), $"heading for {bot.Goal}, out of the ring");
        arena.Run(2 * Tps);
        Assert.Equal(BotActivity.Driving, bot.Activity);
    }

    [Fact]
    public void A_leader_waits_for_a_slower_follower_heading_for_it()
    {
        var arena = Duos(BigRings);
        Bot leader = Unarmed(arena, 0, new Vector2(100, 200), 1, TankCatalog.Light);
        Bot follower = Unarmed(arena, 0, new Vector2(110, 200), 2, TankCatalog.Heavy);
        KeepOn(arena);
        Assert.True(leader.TankId < follower.TankId);

        int ticks = 0, over = 0, firstArrival = -1, ends = 0;
        float peak = 0f;
        bool wasRegrouping = false, wanderedOn = false;
        arena.Run(80 * Tps, tick =>
        {
            float d = Apart(arena, leader.TankId, follower.TankId);
            (ticks, over, peak) = (ticks + 1, over + (d > 100f ? 1 : 0), MathF.Max(peak, d));
            if (wasRegrouping && !follower.Regrouping)
            {
                ends++;
                if (firstArrival < 0) firstArrival = tick;
            }
            wasRegrouping = follower.Regrouping;
            wanderedOn |= firstArrival > 0 && tick > firstArrival && leader.Activity == BotActivity.Driving;
        });
        float share = over / (float)ticks;
        _output.WriteLine($"{share * 100f:F1}% of the ticks over 100 m apart, {peak:F0} m at most; the follower headed for its leader {follower.Regroups} times " +
                          $"({ends} ended), first there at {firstArrival / (float)Tps:F1} s; {arena.Director.Summary()}");
        Assert.True(follower.Regroups >= 1, "the follower never headed for its leader");
        Assert.True(firstArrival > 0, "the follower never got to its leader");
        Assert.True(share <= 0.05f, $"{share * 100f:F1}% of the ticks over 100 m apart");
        Assert.True(peak <= 110f, $"{peak:F0} m apart");
        Assert.True(wanderedOn, "the leader never wandered on once its follower was there");
        Assert.Equal(0, leader.Regroups);
    }

    /// <summary>
    /// A fast follower brakes in time to stop about <see cref="BotDirector.MateArrive"/> from a team-mate standing still,
    /// rather than coasting up to it (6C batch C review follow-up: a light heading for its waiting leader at 16.7 m/s
    /// coasted to 7 m of it and, by the cliffs, shut the leader's only way out for a minute).
    /// </summary>
    [Fact]
    public void A_fast_follower_stops_short_of_a_team_mate_standing_still()
    {
        var arena = Duos(BigRings);
        int player = arena.Idle(TankCatalog.Medium, 0, new Vector2(60, 200));
        Bot bot = Unarmed(arena, 0, new Vector2(260, 200), 1, TankCatalog.Light);
        KeepOn(arena);

        bool regrouped = false;
        int stoppedAt = -1;
        arena.Run(30 * Tps, tick =>
        {
            regrouped |= bot.Regrouping;
            if (stoppedAt < 0 && regrouped && !bot.Regrouping && MathF.Abs(arena.Tank(bot.TankId).Speed) < Steering.StoppedSpeed) stoppedAt = tick;
        });
        float apart = Apart(arena, player, bot.TankId);
        _output.WriteLine($"stopped {apart:F1} m from its team-mate at {stoppedAt / (float)Tps:F1} s; {arena.Director.Summary()}");
        Assert.True(stoppedAt > 0, "never got there and stopped");
        Assert.InRange(apart, BotDirector.MateArrive - 4f, BotDirector.MateArrive + 1f);
    }

    [Fact]
    public void A_leader_turns_back_while_its_follower_waits_out_a_regroup_given_up()
    {
        // the follower walled in on every side: its searches find no way out, and the third gives the regroup up
        Vector2 box = new(300, 200);
        Prop[] walls =
        {
            Rock(1, box + new Vector2(-15, 0), new Vector2(1f, 16f)), Rock(2, box + new Vector2(15, 0), new Vector2(1f, 16f)),
            Rock(3, box + new Vector2(0, -15), new Vector2(16f, 1f)), Rock(4, box + new Vector2(0, 15), new Vector2(16f, 1f)),
        };
        var arena = Duos(BigRings, map: BotArena.FlatMap(props: walls));
        Bot leader = Unarmed(arena, 0, new Vector2(200, 200), 1);
        Bot follower = Unarmed(arena, 0, box, 2);
        KeepOn(arena);

        int gaveUp = -1;
        for (int k = 0; k < 30 * Tps && gaveUp < 0; k++)
        {
            arena.Tick();
            if (follower.Regroups == 1 && !follower.Regrouping && follower.Stucks > 0) gaveUp = arena.Sim.Tick;
        }
        Assert.True(gaveUp > 0, $"the follower never gave its regroup up ({follower.Regroups} regroups, {follower.Stucks} stucks)");
        Assert.False(follower.CanRegroup(gaveUp + BotDirector.RegroupQuietTicks - 1), "free to head for its leader again inside the cool-down");
        Assert.True(follower.CanRegroup(gaveUp + BotDirector.RegroupQuietTicks), "not free to head for its leader once the cool-down is over");

        bool leaderCame = false;
        arena.Run(BotDirector.RegroupQuietTicks, _ =>
        {
            leaderCame |= leader.Regrouping;
            Assert.Equal(1, follower.Regroups);
        });
        _output.WriteLine($"gave up at {gaveUp / (float)Tps:F1} s; leader {leader.Regroups} regroups; {arena.Director.Summary()}");
        Assert.True(leaderCame, "the leader never turned back for its follower while the follower waited");
        Assert.Equal(1, leader.Regroups);
    }

    [Fact]
    public void A_leader_turns_back_for_a_follower_on_a_crate_run()
    {
        // a repair crate 35 m beyond the hurt follower, away from the leader (55 m from it: a follower's crate lies within
        // MateRange of its team-mate); the leader, whole, has no use for it
        var arena = Duos(BigRings, new[] { new Crate(0, new Vector2(255, 200), CrateKind.Repair) });
        Bot leader = Unarmed(arena, 0, new Vector2(200, 200), 1);
        Bot follower = Unarmed(arena, 0, new Vector2(220, 200), 2);
        arena.Sim.TankRef(follower.TankId).Health = TankCatalog.Medium.MaxHealth / 2;
        KeepOn(arena);

        arena.Run(VisionTracker.UpdateInterval + 2);
        Assert.Equal(1, follower.CrateRuns);
        arena.Place(leader.TankId, arena.Tank(follower.TankId).Position - new Vector2(100, 0), BotArena.West);   // 100 m off, the run under way

        bool leaderCame = false;
        CrateSet crates = arena.Rules.Crates!;
        arena.Run(20 * Tps, _ =>
        {
            if (crates.IsStanding(0)) leaderCame |= leader.Regrouping;
            Assert.False(leader.Regrouping && follower.Regrouping, "the two heading for each other");
        });
        _output.WriteLine($"leader {leader.Regroups} regroups, follower {follower.Regroups}; {arena.Director.Summary()}");
        Assert.Equal(1, follower.CratesTaken);
        Assert.True(leaderCame, "the leader never turned back while its follower was on its crate run");
        Assert.Equal(1, leader.Regroups);
    }

    [Fact]
    public void A_regroup_whose_way_leaves_the_ring_is_dropped()
    {
        // a wall right across the 70 m ring: the only way between the two runs round its ends, outside the ring
        ZoneRing[] rings = { new(Centre, 283f), new(Centre, 70f), new(Centre, 50f), new(Centre, 30f), new(Centre, 20f), new(Centre, 0f) };
        Prop[] wall = { Rock(1, Centre, new Vector2(1f, 90f)) };
        var arena = Duos(rings, map: BotArena.FlatMap(props: wall));
        int player = arena.Idle(TankCatalog.Medium, 0, new Vector2(170, 200));
        Bot bot = Unarmed(arena, 0, new Vector2(235, 200), 1);
        KeepOn(arena);

        arena.Run(30 * Tps, _ => Assert.True(BotDirector.InRing(arena.Tank(bot.TankId).Position, rings[1]),
            $"left the ring for {arena.Tank(bot.TankId).Position}, {Apart(arena, player, bot.TankId):F0} m off its team-mate"));
        _output.WriteLine($"{bot.Regroups} regroups, {bot.ZoneRuns} zone runs; {arena.Director.Summary()}");
        Assert.True(bot.Regroups >= 1, "never headed for its team-mate 65 m off");
        Assert.Equal(0, bot.ZoneRuns);
    }

    [Fact]
    public void A_follower_fetches_only_crates_near_its_team_mate()
    {
        var far = Duos(BigRings, new[] { new Crate(0, new Vector2(209, 200), CrateKind.Armor) });   // 50 m from the bot, 109 m from its team-mate
        far.Idle(TankCatalog.Medium, 0, new Vector2(100, 200));
        Bot held = Unarmed(far, 0, new Vector2(159, 200), 1);
        KeepOn(far);
        far.Run(10 * Tps);
        Assert.Equal(0, held.CrateRuns);
        Assert.Equal(0, held.Regroups);

        var near = Duos(BigRings, new[] { new Crate(0, new Vector2(120, 234.64f), CrateKind.Armor) });   // 40 m from both
        near.Idle(TankCatalog.Medium, 0, new Vector2(100, 200));
        Bot fetcher = Unarmed(near, 0, new Vector2(140, 200), 1);
        KeepOn(near);
        near.Run(20 * Tps);
        Assert.Equal(1, fetcher.CrateRuns);
        Assert.Equal(1, fetcher.CratesTaken);
    }

    [Fact]
    public void A_bot_heading_for_a_team_mate_that_stands_still_asks_for_no_new_path_while_it_fights()
    {
        var arena = Duos(BigRings);
        int player = arena.Idle(TankCatalog.Medium, 0, new Vector2(100, 200));
        Bot bot = arena.Bot(TankCatalog.Medium, 0, 1, new Vector2(200, 200), BotArena.East);
        int enemy = arena.Idle(Tough(), 1, new Vector2(260, 200), BotArena.West);

        arena.Run(2 * Tps);
        Assert.True(bot.Regrouping, "not heading for its team-mate 100 m off");
        Assert.Equal(enemy, bot.TargetId);
        int asked = bot.PathRequests;
        arena.Run(4 * BotDirector.ReplanTicks);
        _output.WriteLine($"{bot.PathRequests - asked} paths asked for over {4 * BotDirector.ReplanTicks / Tps} s engaging; {bot.Activity}");
        Assert.Equal(BotActivity.Engaging, bot.Activity);
        Assert.True(bot.Regrouping);
        Assert.Equal(asked, bot.PathRequests);

        Vector2 moved = new(100, 230);   // the team-mate 30 m on: the goal follows at the next aim
        arena.Place(player, moved, BotArena.East);
        arena.Run(BotDirector.ReplanTicks + 1);
        Assert.Equal(moved, bot.Goal);
    }

    [Fact]
    public void A_person_hitting_its_own_team_mate_keeps_the_enemy_it_last_hit()
    {
        var arena = Duos(BigRings);
        Bot bot = arena.Bot(TankCatalog.Medium, 0, 1, new Vector2(150, 200), BotArena.East);
        int player = arena.Idle(TankCatalog.Medium, 0, new Vector2(150, 240));
        int enemy = arena.Idle(Tough(), 1, new Vector2(250, 190));
        arena.Run(1);

        int tick = arena.Sim.Tick;
        arena.Director.HearEvent(new GameEvent(GameEventType.TankHit, tick, player, 0, default, default, TargetTankId: enemy));
        arena.Director.HearEvent(new GameEvent(GameEventType.SplashDamage, tick, player, 0, default, default, TargetTankId: bot.TankId));
        Assert.Equal(enemy, arena.Director.MateTarget(arena.Tank(bot.TankId), tick));
    }

    [Fact]
    public void A_bot_team_mate_fights_nobody_while_its_target_is_lost()
    {
        Prop[] rock = { Rock(1, new Vector2(250, 300), new Vector2(2f, 20f)) };
        var arena = Duos(BigRings, map: BotArena.FlatMap(props: rock));
        Bot mate = arena.Bot(TankCatalog.Medium, 0, 2, new Vector2(150, 240), BotArena.East);
        Bot bot = arena.Bot(TankCatalog.Medium, 0, 1, new Vector2(150, 200), BotArena.East);
        int enemy = arena.Idle(Tough(), 1, new Vector2(250, 220));
        arena.Run(2 * Tps, _ => arena.Sim.TankRef(mate.TankId).Reload = 10f);
        Assert.Equal(enemy, mate.TargetId);

        arena.Place(enemy, new Vector2(270, 300), BotArena.West);   // behind the rock: no longer a candidate, the target is kept a moment, lost
        int lost = 0;
        arena.Run(3 * VisionTracker.UpdateInterval, _ =>
        {
            if (!mate.Picker.TargetLost) return;
            lost++;
            Assert.Equal(0, arena.Director.MateTarget(arena.Tank(bot.TankId), arena.Sim.Tick + 1));
        });
        Assert.True(lost > 0, "the team-mate's target was never lost");
    }

    /// <summary>
    /// Pins the riverland cliff-foot trap (batch C review): a royale bot wandering west from (432, 229) wedged itself at the
    /// foot of the cliff by the rocks at about (415, 239) for good, forward, back and turning all getting nowhere — seen in
    /// the unarmed duos match of seed 3 at task 5 (11 s; after the follow-up the same match has a leader turning back for its
    /// follower standing at about (432, 221) get nowhere for 11 s, its searches there finding no way). A navigation issue of
    /// the map, not of keeping together; it can happen in a royale too. Alone, as here, the bot has not been seen to wedge
    /// (its searches take it round): the whole matches reproduce it.
    /// </summary>
    [Fact(Skip = "riverland cliff-foot trap near (415, 239) — known issue, see PROGRESS")]
    public void A_royale_bot_wandering_west_from_432_229_is_not_trapped_at_the_cliff_foot()
    {
        ZoneRing[] whole = { new(new Vector2(600, 600), 850f), new(new Vector2(600, 600), 800f), new(new Vector2(600, 600), 700f),
                             new(new Vector2(600, 600), 600f), new(new Vector2(600, 600), 500f), new(new Vector2(600, 600), 0f) };
        var arena = new BotArena(BattleMode.Royale, map: MapCatalog.Riverland, zone: whole);
        Bot bot = Unarmed(arena, 0, new Vector2(432, 229), 1);
        arena.Place(bot.TankId, new Vector2(432, 229), BotArena.West);
        arena.Idle(TankCatalog.Medium, 1, new Vector2(1100, 1100));   // keeps the match on, out of sight
        arena.Director.AssignRoles();
        bot.Wander(new[] { new Vector2(356, 280) });   // the waypoint west past the spot

        arena.Run(30 * Tps);
        _output.WriteLine($"at {arena.Tank(bot.TankId).Position}, {bot.Activity}; {bot.Stucks} stucks; stuck {arena.LongestStuckSeconds(bot.TankId):F1} s at most");
        Assert.True(arena.LongestStuckSeconds(bot.TankId) < 6f, $"got nowhere for {arena.LongestStuckSeconds(bot.TankId):F1} s near {arena.Tank(bot.TankId).Position}");
    }

    [Fact]
    public void Duo_bots_allocate_nothing()
    {
        Allocation.AssertNone(() =>
        {
            var arena = Duos(BigRings);
            int player = arena.Idle(TankCatalog.Medium, 0, new Vector2(60, 60));
            Bot[] bots =
            {
                arena.Bot(TankCatalog.Medium, 0, 1, new Vector2(150, 60), BotArena.East),
                arena.Bot(TankCatalog.Medium, 1, 2, new Vector2(340, 340), BotArena.West),
                arena.Bot(TankCatalog.Light, 1, 3, new Vector2(240, 340), BotArena.West),
            };
            arena.Recording = false;
            arena.Run(3 * Tps);
            arena.Director.HearEvent(new GameEvent(GameEventType.TankHit, arena.Sim.Tick, player, 0, default, default, TargetTankId: bots[1].TankId));
            Allocation.Window window = Allocation.Start();
            arena.Run(600);
            Allocation.Measured measured = window.Stop();
            Assert.True(bots.Sum(b => b.Regroups) >= 1, "nobody regrouped");
            return measured;
        });
    }

    /// <summary>A rock <paramref name="half"/> each way round <paramref name="at"/>, 4 m high.</summary>
    private static Prop Rock(int id, Vector2 at, Vector2 half) => new(id, PropKind.Rock, new Obb2(at, half, 0f), -1f, 4f);

    private static TankSpec Tough() => TankCatalog.Medium with { MaxHealth = TankCatalog.Medium.MaxHealth * 50 };

    /// <summary>The angle between two directions, degrees.</summary>
    private static float Turn(Vector2 a, Vector2 b) =>
        MathF.Abs(MathF.Atan2(a.X * b.Y - a.Y * b.X, Vector2.Dot(a, b))) * 180f / MathF.PI;
}
