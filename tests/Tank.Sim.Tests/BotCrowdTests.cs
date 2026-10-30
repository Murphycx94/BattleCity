using System.Numerics;
using Tank.Sim.Ai;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Modes;
using Tank.Sim.Navigation;
using Tank.Sim.Vehicles;
using Xunit.Abstractions;

namespace Tank.Sim.Tests;

/// <summary>
/// Bots crowding and getting stuck in a royale (batch C review follow-up; Bot.Crowd.cs): there every tank is another's
/// enemy, so the team-mate rules never applied. A bot steers round any tank, a holding bot makes room for one pressing on
/// it, a bot stuck turning backs off turning the other way, a bot the grid can't take anywhere drives for open ground, a
/// search from a closed cell at a cliff foot starts on the tank's own level, and a wander path that leaves the ring is
/// dropped. On the flat 400 m arena (and a plateau on it) with hand-made rings; the bots carry no rounds, so only the
/// zone and the way move them.
/// </summary>
public sealed class BotCrowdTests
{
    private const int Tps = SimConstants.TickRate;

    /// <summary>Hulls closer than this count as touching, m.</summary>
    private const float Touch = 0.3f;

    /// <summary>Ring 0 round the arena; rings 1–5 round (215, 200), ring 1 with room for a long wander.</summary>
    private static readonly ZoneRing[] WideRings =
    {
        new(new Vector2(200, 200), 283f),
        new(new Vector2(215, 200), 200f),
        new(new Vector2(215, 200), 150f),
        new(new Vector2(215, 200), 100f),
        new(new Vector2(215, 200), 60f),
        new(new Vector2(215, 200), 0f),
    };

    private readonly ITestOutputHelper _output;

    public BotCrowdTests(ITestOutputHelper output) => _output = output;

    private static BotArena Royale(IReadOnlyList<ZoneRing> rings, MapDefinition? map = null) =>
        new(BattleMode.Royale, map: map, zone: rings);

    private static Bot Unarmed(BotArena arena, Vector2 at, float yaw, uint seed = 1, TankSpec? spec = null)
    {
        Bot bot = arena.Bot(spec ?? TankCatalog.Medium, 0, seed, at, yaw);
        ref TankState t = ref arena.Sim.TankRef(bot.TankId);
        (t.ApLeft, t.HeLeft) = (0, 0);
        return bot;
    }

    private static Obb2 Hull(in TankState t, float grow) =>
        new(t.Position, new Vector2(t.Spec.HullWidth / 2 + grow, t.Spec.HullLength / 2 + grow), t.Yaw);

    private static bool Touching(in TankState a, in TankState b) => Obb2.TryGetPenetration(Hull(a, Touch / 2), Hull(b, Touch / 2), out _);

    /// <summary>
    /// A bot wandering east across the ring (its one waypoint at (340, 200)) and a tank nobody steers driving head-on at
    /// it: another player, not a team-mate, as everyone is in a royale. The bot bends round it as round a team-mate.
    /// </summary>
    [Fact]
    public void A_royale_bot_steers_round_any_tank_on_a_collision_course()
    {
        var arena = Royale(WideRings, BotArena.FlatMap(waypoints: new Vector2[] { new(340, 200) }));
        Bot bot = Unarmed(arena, new Vector2(90, 200), BotArena.East);
        int other = arena.Idle(TankCatalog.Medium, 0, new Vector2(240, 200), BotArena.West);
        arena.Order(other, new InputCmd(0, 1, 0, BotArena.West, 0f, false));
        int touching = 0;
        float nearest = float.MaxValue;

        arena.Run(20 * Tps, _ =>
        {
            TankState a = arena.Tank(bot.TankId), b = arena.Tank(other);
            nearest = MathF.Min(nearest, Vector2.Distance(a.Position, b.Position));
            if (Touching(a, b)) touching++;
        });
        TankState me = arena.Tank(bot.TankId);
        _output.WriteLine($"nearest {nearest:F1} m between centres, {touching} ticks touching; the bot at {me.Position}, {bot.Activity}, stucks {bot.Stucks}");
        Assert.Equal(0, touching);
        Assert.True(me.Position.X > 280f, $"the bot only got to {me.Position}");
    }

    /// <summary>Ring 1 small enough to hold in (under <see cref="BotDirector.ZoneWanderRadius"/>), round (200, 200).</summary>
    private static readonly ZoneRing[] HoldRings =
    {
        new(new Vector2(200, 200), 283f),
        new(new Vector2(200, 200), 30f),
        new(new Vector2(200, 200), 25f),
        new(new Vector2(200, 200), 25f),
        new(new Vector2(200, 200), 25f),
        new(new Vector2(200, 200), 0f),
    };

    /// <summary>
    /// A bot holding in a small ring, broadside on, and a bot pressed against its side driving for a point beyond it: the
    /// holder rolls along its hull out of the way at once, and the other gets by, rather than shoving it until it backs
    /// off and the searches have learnt the holder stands still.
    /// </summary>
    [Fact]
    public void A_holding_royale_bot_makes_room_for_one_pressing_on_it()
    {
        var arena = Royale(HoldRings);
        Bot holder = Unarmed(arena, new Vector2(200, 200), BotArena.North, seed: 1);
        Bot pusher = Unarmed(arena, new Vector2(194, 200), BotArena.East, seed: 2);
        arena.Director.AssignRoles();
        arena.Director.Attack(pusher, 0, Array.Empty<Vector2>(), new Vector2(222, 200));
        arena.Run(2);
        Assert.Equal(BotActivity.Holding, holder.Activity);

        Vector2 start = arena.Tank(holder.TankId).Position;
        int movedAt = -1, pastAt = -1;
        arena.Run(8 * Tps, tick =>
        {
            if (movedAt < 0 && Vector2.Distance(arena.Tank(holder.TankId).Position, start) >= 1f) movedAt = tick;
            if (pastAt < 0 && arena.Tank(pusher.TankId).Position.X >= 212f) pastAt = tick;
        });
        TankState h = arena.Tank(holder.TankId), p = arena.Tank(pusher.TankId);
        _output.WriteLine($"holder moved 1 m at {(movedAt < 0 ? "never" : $"{(movedAt - 2) / (float)Tps:F1} s")}, now at {h.Position} ({holder.Activity}); " +
                          $"the other past at {(pastAt < 0 ? "never" : $"{(pastAt - 2) / (float)Tps:F1} s")}, at {p.Position}, stucks {pusher.Stucks}");
        Assert.True(movedAt > 0 && movedAt - 2 <= Tps, "the holder didn't make room within 1 s");
        Assert.True(pastAt > 0 && pastAt - 2 <= 6 * Tps, "the other bot didn't get by within 6 s");
        Assert.Equal(BotActivity.Holding, holder.Activity);
        Assert.False(holder.RunningToZone);
    }

    /// <summary>
    /// A bot whose own cell is open but every cell round it closed (as on a rock's or a slope's closed cells, where
    /// backing off can leave a tank): no search from there finds a way. It drives for the nearest open ground and goes
    /// on from there, rather than sitting still asking one search after another.
    /// </summary>
    [Fact]
    public void A_royale_bot_the_grid_cant_take_anywhere_drives_for_open_ground()
    {
        var arena = Royale(WideRings, BotArena.FlatMap(waypoints: new Vector2[] { new(300, 200) }));
        Vector2 at = NavGrid.CentreOf(37, 50);   // (150, 202)
        for (int dj = -1; dj <= 1; dj++)
        for (int di = -1; di <= 1; di++)
            if (di != 0 || dj != 0) arena.Director.Grid.SetCostForTests(37 + di, 50 + dj, NavGrid.Blocked);
        Bot bot = Unarmed(arena, at, BotArena.East);

        arena.Run(15 * Tps);
        TankState t = arena.Tank(bot.TankId);
        _output.WriteLine($"after 15 s at {t.Position} ({Vector2.Distance(t.Position, at):F0} m on), {bot.Activity}, goal {bot.Goal}; {arena.Director.Summary()}");
        Assert.True(Vector2.Distance(t.Position, at) >= 40f, $"it only got to {t.Position}");
    }

    /// <summary>
    /// A bot at the foot of a 10 m cliff (a plateau west of x = 196) on a closed cell, the lowland cells by it closed too
    /// (rocks, say) so the nearest open cell is on top of the cliff: the search from the tank's own level finds the way
    /// east (the plateau has none down: a search from up there finds nothing, or a way the tank can't start on).
    /// </summary>
    [Fact]
    public void A_royale_bot_at_a_cliff_foot_searches_from_its_own_level()
    {
        MapDefinition map = BotArena.FlatMap(terrain: Ground400.Raised(x => x < 196f, 10f), waypoints: new Vector2[] { new(300, 200) });
        var arena = Royale(WideRings, map);
        NavGrid grid = arena.Director.Grid;
        for (int j = 47; j <= 52; j++)
        for (int i = 49; i <= 51; i++)
            grid.SetCostForTests(i, j, NavGrid.Blocked);
        Vector2 at = new(199.5f, 201f);
        (int ci, int cj) = NavGrid.CellOf(at);
        Assert.False(grid.IsOpen(ci, cj));
        Assert.True(grid.TryNearestOpen(at, PathFinder.SnapRadius, out int si, out _) && NavGrid.CentreOf(si, 0).X < 196f, "the nearest open cell isn't on the plateau");
        Bot bot = Unarmed(arena, at, BotArena.East);

        int awayAt = -1;
        arena.Run(20 * Tps, tick =>
        {
            if (awayAt < 0 && arena.Tank(bot.TankId).Position.X >= at.X + 30f) awayAt = tick;
        });
        TankState t = arena.Tank(bot.TankId);
        _output.WriteLine($"30 m east at {(awayAt < 0 ? "never" : $"{awayAt / (float)Tps:F1} s")}; after 20 s at {t.Position}, {bot.Activity}, stucks {bot.Stucks}; {arena.Director.Summary()}");
        Assert.True(awayAt > 0 && awayAt <= 6 * Tps, "it wasn't 30 m east of the cliff within 6 s");
    }

    /// <summary>Ring 1 round (200, 200), 60 m.</summary>
    private static readonly ZoneRing[] SplitRings =
    {
        new(new Vector2(200, 200), 283f),
        new(new Vector2(200, 200), 60f),
        new(new Vector2(200, 200), 50f),
        new(new Vector2(200, 200), 40f),
        new(new Vector2(200, 200), 30f),
        new(new Vector2(200, 200), 0f),
    };

    /// <summary>
    /// A line of closed cells across ring 1 at x = 200 (a ridge, say), open only beyond the ring: a bot wandering in the
    /// ring's west half to its east half would go out of the ring and round. It drops such a path for another point,
    /// and never leaves the ring.
    /// </summary>
    [Fact]
    public void A_royale_bot_never_wanders_out_of_the_ring_on_its_way()
    {
        var arena = Royale(SplitRings, BotArena.FlatMap(waypoints: new Vector2[] { new(230, 200), new(170, 210) }));
        NavGrid grid = arena.Director.Grid;
        for (int j = 30; j <= 70; j++) grid.SetCostForTests(50, j, NavGrid.Blocked);   // z 120 to 284
        Bot bot = Unarmed(arena, new Vector2(170, 200), BotArena.East);
        arena.Idle(TankCatalog.Medium, 0, new Vector2(200, 160));   // keeps the match on
        float farthest = 0f;

        arena.Run(40 * Tps, _ =>
        {
            farthest = MathF.Max(farthest, Vector2.Distance(arena.Tank(bot.TankId).Position, SplitRings[1].Centre));
            Assert.False(bot.RunningToZone, "it ran");
        });
        _output.WriteLine($"farthest {farthest:F1} m from ring 1's centre; at {arena.Tank(bot.TankId).Position}, {bot.Activity}, goal {bot.Goal}; {arena.Director.Summary()}");
        Assert.True(farthest <= SplitRings[1].Radius, $"it went {farthest:F1} m from the centre, out of the ring");
    }

    /// <summary>
    /// The ring's west half cut off from its east half (as in the test above), and the only waypoints east: every wander
    /// path leaves the ring and is dropped. Every third time running the bot backs off meanwhile, to look again from
    /// somewhere else, rather than wait where it stands (boxed in by tanks that every way inside the ring passes, a bot
    /// in a small ring got nowhere for over 10 s that way); it still never leaves the ring.
    /// </summary>
    [Fact]
    public void A_royale_bot_whose_wander_paths_keep_leaving_the_ring_backs_off()
    {
        var arena = Royale(SplitRings, BotArena.FlatMap(waypoints: new Vector2[] { new(230, 200), new(235, 215) }));
        NavGrid grid = arena.Director.Grid;
        for (int j = 30; j <= 70; j++) grid.SetCostForTests(50, j, NavGrid.Blocked);
        Bot bot = Unarmed(arena, new Vector2(170, 200), BotArena.East);
        arena.Idle(TankCatalog.Medium, 0, new Vector2(200, 160));   // keeps the match on
        int backedAt = -1;
        float farthest = 0f;

        arena.Run(10 * Tps, tick =>
        {
            if (backedAt < 0 && bot.Activity == BotActivity.Reversing) backedAt = tick;
            farthest = MathF.Max(farthest, Vector2.Distance(arena.Tank(bot.TankId).Position, SplitRings[1].Centre));
        });
        _output.WriteLine($"backed off at {(backedAt < 0 ? "never" : $"{backedAt / (float)Tps:F1} s")}; farthest {farthest:F1} m from the centre; {arena.Director.Summary()}");
        Assert.True(backedAt > 0, "it never backed off");
        Assert.True(farthest <= SplitRings[1].Radius, $"it went {farthest:F1} m from the centre, out of the ring");
    }

    /// <summary>
    /// A bot facing north with its way east, two rocks at its hull's corners that the turn east swings into (front right,
    /// back left): it gets stuck turning right on the spot, and backs off turning left, the way that is free, rather than
    /// the way the stucks alternate (right first), which gets it nowhere.
    /// </summary>
    [Fact]
    public void A_royale_bot_stuck_turning_backs_off_turning_the_other_way()
    {
        TankSpec spec = TankCatalog.Medium;
        float w = spec.HullWidth / 2, l = spec.HullLength / 2;
        Vector2 at = new(200, 200);
        Prop[] rocks =
        {
            new(1, PropKind.Rock, new Obb2(at + new Vector2(w + 1.1f, -l + 1f), new Vector2(1f, 1f), 0f), -1f, 4f),
            new(2, PropKind.Rock, new Obb2(at + new Vector2(-w - 1.1f, l - 1f), new Vector2(1f, 1f), 0f), -1f, 4f),
        };
        var arena = Royale(HoldRings, BotArena.FlatMap(props: rocks));
        Bot bot = Unarmed(arena, at, BotArena.North, spec: spec);
        arena.Director.AssignRoles();
        arena.Director.Attack(bot, 0, Array.Empty<Vector2>(), new Vector2(215, 200));
        sbyte turning = 0, backing = 0;
        int backedAt = -1;

        arena.Run(8 * Tps, tick =>
        {
            InputCmd cmd = arena.LastInput(bot.TankId);
            if (bot.Activity == BotActivity.Driving && backedAt < 0 && cmd.Steer != 0) turning = cmd.Steer;
            if (bot.Activity == BotActivity.Reversing && backedAt < 0) (backedAt, backing) = (tick, cmd.Steer);
        });
        TankState t = arena.Tank(bot.TankId);
        _output.WriteLine($"turning {turning}, first backed off at {backedAt / (float)Tps:F1} s steering {backing}; now at {t.Position}, yaw {t.Yaw * 180 / MathF.PI:F0}°, {bot.Activity}, stucks {bot.Stucks}");
        Assert.Equal(1, turning);
        Assert.True(backedAt > 0, "it never backed off");
        Assert.Equal(-1, backing);
        Assert.True(Vector2.Distance(t.Position, at) >= 5f, $"still at {t.Position}");
    }

    /// <summary>
    /// A bot on a 10 m plateau (west of x = 196) with a path that leads straight over its edge (the cliff's cells opened
    /// on the grid, as steering round a tank or cutting a corner takes a bot off a path): it stops short of the edge
    /// rather than drive over it, into whatever pocket lies below (a light tank steering round another at full speed up
    /// the riverland's south ramp went over its west edge into the rocks at its foot, and never got out).
    /// </summary>
    [Fact]
    public void A_royale_bot_stops_short_of_a_cliffs_edge_its_way_leads_over()
    {
        MapDefinition map = BotArena.FlatMap(terrain: Ground400.Raised(x => x < 196f, 10f));
        var arena = Royale(WideRings, map);
        NavGrid grid = arena.Director.Grid;
        ushort open = grid.CostAt(40, 50);
        for (int j = 44; j <= 56; j++)
        for (int i = 46; i <= 51; i++)
            grid.SetCostForTests(i, j, open);
        Bot bot = Unarmed(arena, new Vector2(160, 200), BotArena.East);
        arena.Director.AssignRoles();
        arena.Director.Attack(bot, 0, Array.Empty<Vector2>(), new Vector2(240, 200));
        float farthest = 0f;

        arena.Run(15 * Tps, _ => farthest = MathF.Max(farthest, arena.Tank(bot.TankId).Position.X));
        TankState t = arena.Tank(bot.TankId);
        _output.WriteLine($"farthest east {farthest:F1}; at {t.Position}, {bot.Activity}, stucks {bot.Stucks}");
        Assert.True(farthest < 194f, $"it went over the edge, to x = {farthest:F1}");
    }

    /// <summary>
    /// A bot waiting for a path it won't get (its goal shut in) and a bot pressed against its side driving for a point
    /// beyond it: it rolls along its hull out of the way, as a holding bot does (crowded in a small ring, bots that wait
    /// on each other's paths stood boxed in for good).
    /// </summary>
    [Fact]
    public void A_waiting_royale_bot_makes_room_for_one_pressing_on_it()
    {
        var arena = Royale(HoldRings);
        NavGrid grid = arena.Director.Grid;
        for (int j = 70; j <= 74; j++)
        for (int i = 70; i <= 74; i++)
            if (i != 72 || j != 72) grid.SetCostForTests(i, j, NavGrid.Blocked);
        Bot waiter = Unarmed(arena, new Vector2(200, 200), BotArena.North, seed: 1);
        Bot pusher = Unarmed(arena, new Vector2(194, 200), BotArena.East, seed: 2);
        arena.Director.AssignRoles();
        arena.Director.Attack(waiter, 0, Array.Empty<Vector2>(), NavGrid.CentreOf(72, 72));
        arena.Director.Attack(pusher, 0, Array.Empty<Vector2>(), new Vector2(222, 200));
        arena.Run(Tps / 2);
        Assert.Equal(BotActivity.Waiting, waiter.Activity);

        Vector2 start = arena.Tank(waiter.TankId).Position;
        int movedAt = -1, pastAt = -1;
        arena.Run(8 * Tps, tick =>
        {
            if (movedAt < 0 && Vector2.Distance(arena.Tank(waiter.TankId).Position, start) >= 1f) movedAt = tick;
            if (pastAt < 0 && arena.Tank(pusher.TankId).Position.X >= 212f) pastAt = tick;
        });
        TankState w = arena.Tank(waiter.TankId), p = arena.Tank(pusher.TankId);
        _output.WriteLine($"waiter moved 1 m at {(movedAt < 0 ? "never" : $"{movedAt / (float)Tps:F1} s")}, now at {w.Position} ({waiter.Activity}); " +
                          $"the other past at {(pastAt < 0 ? "never" : $"{pastAt / (float)Tps:F1} s")}, at {p.Position}, stucks {pusher.Stucks}");
        Assert.True(movedAt > 0, "the waiting bot never made room");
        Assert.True(pastAt > 0, "the other bot never got by");
    }

    /// <summary>
    /// A bot facing east between a rock just ahead and a bot holding just behind it, its way north-east past the rock: it
    /// can't turn there (the rock and the holder box it in), gets stuck, and backs off into the holder, which rolls out
    /// of its way as for a bot pressing on it (in a royale's last ring a bot boxed in so sat 46 s).
    /// </summary>
    [Fact]
    public void A_holding_royale_bot_makes_room_for_one_backing_into_it()
    {
        TankSpec spec = TankCatalog.Medium;
        float w = spec.HullWidth / 2, l = spec.HullLength / 2;
        var holderAt = new Vector2(200, 200);
        var boxedAt = holderAt + new Vector2(w + l + 0.3f, 0);
        Prop[] rocks = { new(1, PropKind.Rock, new Obb2(boxedAt + new Vector2(l + 1.3f, 0), new Vector2(1f, 6f), 0f), -1f, 4f) };
        var arena = Royale(HoldRings, BotArena.FlatMap(props: rocks));
        Bot holder = Unarmed(arena, holderAt, BotArena.North, seed: 1, spec: spec);
        Bot boxed = Unarmed(arena, boxedAt, BotArena.East, seed: 2, spec: spec);
        arena.Director.AssignRoles();
        arena.Director.Attack(boxed, 0, Array.Empty<Vector2>(), boxedAt + new Vector2(20, -30));
        arena.Run(2);
        Assert.Equal(BotActivity.Holding, holder.Activity);

        int movedAt = -1;
        arena.Run(12 * Tps, tick =>
        {
            if (movedAt < 0 && Vector2.Distance(arena.Tank(holder.TankId).Position, holderAt) >= 1f) movedAt = tick;
        });
        TankState h = arena.Tank(holder.TankId), b = arena.Tank(boxed.TankId);
        _output.WriteLine($"holder moved 1 m at {(movedAt < 0 ? "never" : $"{movedAt / (float)Tps:F1} s")}, at {h.Position}; the boxed bot at {b.Position}, " +
                          $"{boxed.Activity}, stucks {boxed.Stucks}");
        Assert.True(movedAt > 0, "the holder never made room");
        Assert.True(b.Position.Y < boxedAt.Y - 5f, $"the boxed bot got only to {b.Position}");
    }
}
