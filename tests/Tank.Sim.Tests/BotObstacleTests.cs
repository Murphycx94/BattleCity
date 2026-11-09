using System.Numerics;
using Tank.Sim.Ai;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;
using Xunit.Abstractions;

namespace Tank.Sim.Tests;

/// <summary>
/// Bots and what the grid can't see (5C batch B review follow-up): wrecks and tanks standing still are steered round; a
/// search that finds nothing counts as stuck without backing off; a final leg is never given up short of its target (an
/// attacker draws another capture point and tries again, a defender out of reach of every cover point holds its base
/// centre). Played on <see cref="BotArena"/>'s flat arena.
/// </summary>
public sealed class BotObstacleTests
{
    private const int Tps = SimConstants.TickRate;

    /// <summary>Team 1's corner: a still tank in the bush there is seen by nobody further than about 140 m off.</summary>
    private static readonly Vector2 Corner = new(385, 15);

    private static readonly Prop CornerBush = new(100, PropKind.Bush, new Obb2(Corner, new Vector2(4, 4), 0f), -0.2f, 4.5f);

    private readonly ITestOutputHelper _output;

    public BotObstacleTests(ITestOutputHelper output) => _output = output;

    // ---- a channel between two rows of rock (as BotDriveTests' stuck test), its waypoints at either end ----

    private static readonly Prop[] ChannelRocks =
    {
        new(1, PropKind.Rock, new Obb2(new Vector2(200, 193), new Vector2(50, 3), 0f), -1f, 4f),
        new(2, PropKind.Rock, new Obb2(new Vector2(200, 209), new Vector2(50, 3), 0f), -1f, 4f),
    };

    private static readonly Vector2 ChannelWest = new(130, 202), ChannelEast = new(270, 202);

    /// <summary>A wandering medium at the channel's west end heading east (seed 5 picks the east waypoint first), a heavy across the channel.</summary>
    private static BotArena Channel(out Bot bot, out int heavy)
    {
        var arena = new BotArena(map: BotArena.FlatMap(props: ChannelRocks, waypoints: new[] { ChannelWest, ChannelEast }));
        bot = arena.Bot(TankCatalog.Medium, 0, seed: 5, new Vector2(110, 202), BotArena.East);
        heavy = arena.Idle(TankCatalog.Heavy, 0, new Vector2(200, 201), BotArena.North);
        return arena;
    }

    /// <summary>
    /// Plays until the bot is within the waypoint radius of the channel's east end (at most 90 s), which it heads for once
    /// past the west one, 20 m on, and never gives up; returns the tick, or −1.
    /// </summary>
    private static int UntilEast(BotArena arena, Bot bot)
    {
        bool east = false;
        for (int k = 0; k < 90 * Tps; k++)
        {
            arena.Tick();
            if (east) Assert.Equal(ChannelEast, bot.Goal);   // never given up
            east |= bot.Goal == ChannelEast;
            if (Vector2.Distance(arena.Tank(bot.TankId).Position, ChannelEast) <= BotDirector.WaypointRadius) return arena.Sim.Tick;
        }
        return -1;
    }

    [Fact]
    public void Behind_a_tank_parked_in_its_way_it_finds_the_way_round()
    {
        BotArena arena = Channel(out Bot bot, out _);
        int at = UntilEast(arena, bot);
        _output.WriteLine($"east end after {at / (float)Tps:F1} s, {bot.Stucks} stucks, {bot.PathRequests} requests, longest stuck {arena.LongestStuckSeconds(bot.TankId):F1} s");
        Assert.True(at > 0, $"not at the east end in 90 s: at {arena.Tank(bot.TankId).Position}");
        Assert.True(arena.LongestStuckSeconds(bot.TankId) < 10f);
    }

    [Fact]
    public void A_wreck_in_its_way_is_driven_round_without_getting_stuck()
    {
        BotArena arena = Channel(out Bot bot, out int heavy);
        arena.Sim.TankRef(heavy).Health = 0;
        arena.Sim.TankRef(heavy).IsDestroyed = true;
        int at = UntilEast(arena, bot);
        _output.WriteLine($"east end after {at / (float)Tps:F1} s, {bot.Stucks} stucks, {bot.PathRequests} requests");
        Assert.True(at > 0, $"not at the east end in 90 s: at {arena.Tank(bot.TankId).Position}");
        Assert.Equal(0, bot.Stucks);
    }

    // ---- a wall of rock across the field at x = 200 from z 60 to 340, a gap on the middle lane ----

    /// <summary>The wall with a gap from z = 200 − <paramref name="half"/> to 200 + <paramref name="half"/> (the middle lane's via point (200, 200) in it).</summary>
    private static Prop[] Wall(float half) => new[]
    {
        new Prop(1, PropKind.Rock, new Obb2(new Vector2(200, (60 + 200 - half) / 2), new Vector2(3, (200 - half - 60) / 2), 0f), -1f, 4f),
        new Prop(2, PropKind.Rock, new Obb2(new Vector2(200, (200 + half + 340) / 2), new Vector2(3, (340 - 200 - half) / 2), 0f), -1f, 4f),
        CornerBush,
    };

    /// <summary>
    /// A standard battle: team 0 <paramref name="attackers"/> mediums sent down the middle lane to points apart inside the
    /// enemy base, a wreck of team 0 at each of <paramref name="wrecks"/>; team 1 one tank hidden in the corner.
    /// </summary>
    private static BotArena Choked(Prop[] props, Vector2[] wrecks, int attackers, out Bot[] bots)
    {
        var arena = new BotArena(BattleMode.Standard, BotArena.FlatMap(props: props));
        bots = new Bot[attackers];
        for (int k = 0; k < attackers; k++) bots[k] = arena.Bot(TankCatalog.Medium, 0, (uint)k + 1, new Vector2(110, 180 + 40 * k), BotArena.East);
        foreach (Vector2 w in wrecks)
        {
            int id = arena.Idle(TankCatalog.Heavy, 0, w, BotArena.North);
            arena.Sim.TankRef(id).Health = 0;
            arena.Sim.TankRef(id).IsDestroyed = true;
        }
        arena.Idle(TankCatalog.Light, 1, Corner, BotArena.West);
        arena.Director.AssignRoles();
        for (int k = 0; k < attackers; k++)
            arena.Director.Attack(bots[k], 1, BotArena.Bots.ViaFor(1, 0), BotArena.Base1 + new Vector2(-6, -8 + 16 * k));
        return arena;
    }

    /// <summary>Plays until every bot holds inside the enemy base (at most <paramref name="seconds"/>); returns the time it took, or −1.</summary>
    private static int UntilAllHoldInTheEnemyBase(BotArena arena, Bot[] bots, int seconds)
    {
        for (int k = 0; k < seconds * Tps; k++)
        {
            arena.Tick();
            if (bots.All(b => b.Activity == BotActivity.Holding && Vector2.Distance(arena.Tank(b.TankId).Position, BotArena.Base1) <= BotArena.BaseRadius))
                return arena.Sim.Tick;
        }
        return -1;
    }

    [Fact]
    public void An_attacker_whose_lane_is_choked_by_a_wreck_still_ends_inside_the_enemy_base()
    {
        // A gap 14 m wide, two rows of open cells: a heavy wreck across it leaves about 3 m either side and closes both rows.
        BotArena arena = Choked(Wall(7), new[] { new Vector2(200, 200) }, 1, out Bot[] bots);
        int at = UntilAllHoldInTheEnemyBase(arena, bots, 180);
        Bot bot = bots[0];
        _output.WriteLine($"holding in the enemy base after {at / (float)Tps:F1} s at {arena.Tank(bot.TankId).Position}, {bot.Stucks} stucks, " +
                          $"longest stuck {arena.LongestStuckSeconds(bot.TankId):F1} s, {arena.Director.Paths.Failures} of {arena.Director.Paths.Searches} searches failed");
        Assert.True(at > 0, $"not holding in the enemy base after 180 s: at {arena.Tank(bot.TankId).Position}, {bot.Activity}, heading for {bot.Goal}");
        Assert.Empty(arena.AttackersShortOfTheEnemyBase());
        Assert.True(arena.LongestStuckSeconds(bot.TankId) < 10f, $"stuck for {arena.LongestStuckSeconds(bot.TankId):F1} s");
    }

    [Fact]
    public void Two_wrecks_in_a_gap_and_two_attackers_behind_them_all_end_inside_the_enemy_base()
    {
        // A gap 28 m wide, six rows of open cells: the wreck at z 194 closes the three on its side, the one at z 208 the rest.
        BotArena arena = Choked(Wall(14), new[] { new Vector2(200, 194), new Vector2(200, 208) }, 2, out Bot[] bots);
        int at = UntilAllHoldInTheEnemyBase(arena, bots, 240);
        foreach (Bot bot in bots)
            _output.WriteLine($"tank {bot.TankId}: at {arena.Tank(bot.TankId).Position}, {bot.Activity}, {bot.Stucks} stucks, longest stuck {arena.LongestStuckSeconds(bot.TankId):F1} s");
        _output.WriteLine($"both holding in the enemy base after {at / (float)Tps:F1} s");
        Assert.True(at > 0, "not both holding in the enemy base after 240 s");
        Assert.Empty(arena.AttackersShortOfTheEnemyBase());
        foreach (Bot bot in bots) Assert.True(arena.LongestStuckSeconds(bot.TankId) < 10f, $"tank {bot.TankId} stuck for {arena.LongestStuckSeconds(bot.TankId):F1} s");
    }

    // ---- searches that find nothing ----

    /// <summary>A rock 28 m square round <paramref name="centre"/>: nothing open within 12 m of its centre.</summary>
    private static Prop BigRock(int id, Vector2 centre) => new(id, PropKind.Rock, new Obb2(centre, new Vector2(12, 12), 0f), -1f, 4f);

    [Fact]
    public void A_search_that_finds_nothing_counts_as_stuck_without_backing_off_and_is_asked_again_later()
    {
        var unreachable = new Vector2(200, 100);
        var arena = new BotArena(map: BotArena.FlatMap(props: new[] { BigRock(1, unreachable), CornerBush }));
        Bot bot = arena.Bot(TankCatalog.Medium, 0, seed: 1, new Vector2(110, 200), BotArena.East);
        arena.Idle(TankCatalog.Light, 1, Corner, BotArena.West);
        arena.Director.AssignRoles();
        var end = new Vector2(300, 300);
        arena.Director.Attack(bot, 0, new[] { unreachable }, end);

        var asked = new List<int>();
        int last = 0;
        arena.Run(20 * Tps, tick =>
        {
            Assert.NotEqual(BotActivity.Reversing, bot.Activity);
            if (bot.PathRequests != last) asked.Add(tick);
            last = bot.PathRequests;
        });
        _output.WriteLine($"asked at ticks {string.Join(", ", asked)}; {bot.Stucks} stucks; heading for {bot.Goal}");
        Assert.Equal(BotDirector.StuckRetries, bot.Stucks);
        Assert.InRange(asked[1] - asked[0], BotDirector.ReplanTicks - 2, BotDirector.ReplanTicks + 2);
        Assert.InRange(asked[2] - asked[1], BotDirector.ReplanTicks - 2, BotDirector.ReplanTicks + 2);
        Assert.Equal(end, bot.Goal);   // the third time it gave the via point up
        Assert.Equal(BotActivity.Driving, bot.Activity);
        Assert.True(Vector2.Distance(arena.Tank(bot.TankId).Position, new Vector2(110, 200)) > 20f, "on its way to the end");
    }

    [Fact]
    public void A_defender_out_of_reach_of_every_cover_point_holds_at_its_base_centre()
    {
        IReadOnlyList<Vector2> cover = BotArena.Bots.CoverOf(0);
        Prop[] rocks = cover.Select((c, k) => BigRock(k + 1, c)).Append(CornerBush).ToArray();
        var arena = new BotArena(BattleMode.Standard, BotArena.FlatMap(props: rocks));
        Bot td = arena.Bot(TankCatalog.Td, 0, seed: 1);
        arena.Idle(TankCatalog.Medium, 0, new Vector2(20, 385));   // a player in team 0's corner: the team has an attacker, so the bot stays at home
        arena.Idle(TankCatalog.Light, 1, Corner, BotArena.West);
        arena.Run(60 * Tps, _ => Assert.NotEqual(BotActivity.Reversing, td.Activity));

        TankState t = arena.Tank(td.TankId);
        _output.WriteLine($"cover {td.Cover}, heading for {td.Goal}, at {t.Position} ({Vector2.Distance(t.Position, BotArena.Base0):F1} m off the centre), {td.Stucks} stucks, {td.Activity}");
        Assert.Equal(BotRole.Defend, td.Role);
        Assert.Equal(-1, td.Cover);
        Assert.Equal(BotArena.Base0, td.Goal);
        Assert.Equal(BotDirector.StuckRetries * cover.Count, td.Stucks);   // each cover point tried three times
        Assert.Equal(BotActivity.Holding, td.Activity);
        Assert.True(Vector2.Distance(t.Position, BotArena.Base0) <= BotArena.CoverReach, $"at {t.Position}");
        Assert.True(arena.AtRoleEnd(td));
    }

    [Fact]
    public void An_attacker_that_cannot_reach_its_capture_point_draws_another_and_tries_again_the_hold_counted_as_stuck()
    {
        var blocked = new Vector2(362, 200);
        var arena = new BotArena(BattleMode.Standard, BotArena.FlatMap(props: new[] { BigRock(1, blocked), CornerBush }));
        Bot bot = arena.Bot(TankCatalog.Medium, 0, seed: 1);
        arena.Idle(TankCatalog.Light, 1, Corner, BotArena.West);
        arena.Director.AssignRoles();
        arena.Director.Attack(bot, 1, BotArena.Bots.ViaFor(1, 0), blocked);

        int pausedFrom = -1, pausedTo = -1;
        float stuckBefore = 0f, stuckAfter = 0f;
        int at = -1;
        for (int k = 0; k < 120 * Tps && at < 0; k++)
        {
            arena.Tick();
            Vector2 p = arena.Tank(bot.TankId).Position;
            bool inside = Vector2.Distance(p, BotArena.Base1) <= BotArena.BaseRadius;
            if (bot.Activity == BotActivity.Holding && !inside)
            {
                if (pausedFrom < 0) (pausedFrom, stuckBefore) = (arena.Sim.Tick, arena.LongestStuckSeconds(bot.TankId));
                Assert.NotEmpty(arena.AttackersShortOfTheEnemyBase());
                Assert.False(arena.AtRoleEnd(bot));
                (pausedTo, stuckAfter) = (arena.Sim.Tick, arena.LongestStuckSeconds(bot.TankId));
            }
            if (bot.Activity == BotActivity.Holding && inside) at = arena.Sim.Tick;
        }
        _output.WriteLine($"held outside from {pausedFrom / (float)Tps:F1} s to {pausedTo / (float)Tps:F1} s (longest stuck {stuckBefore:F1} → {stuckAfter:F1} s); " +
                          $"holding in the enemy base at {at / (float)Tps:F1} s, heading for {bot.Goal}, {bot.Stucks} stucks");
        Assert.True(pausedFrom > 0, "it never paused");
        Assert.InRange(pausedTo - pausedFrom + 1, BotDirector.RetryTicks - 1, BotDirector.RetryTicks + 1);
        Assert.True(stuckAfter - stuckBefore >= (BotDirector.RetryTicks - 2) / (float)Tps, "the hold short of the base didn't count as stuck");
        Assert.True(at > 0, $"not holding in the enemy base after 120 s: at {arena.Tank(bot.TankId).Position}, {bot.Activity}, heading for {bot.Goal}");
        Assert.NotEqual(blocked, bot.Goal);
        Assert.True(Vector2.Distance(bot.Goal, BotArena.Base1) <= BotArena.BaseRadius - BotDirector.CaptureMargin + 1e-3f, $"drew {bot.Goal}");
        Assert.Empty(arena.AttackersShortOfTheEnemyBase());
    }
}
