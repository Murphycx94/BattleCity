using System.Numerics;
using Tank.Sim.Ai;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Modes;
using Tank.Sim.Navigation;
using Tank.Sim.Vehicles;
using Tank.Sim.Vision;
using Tank.Testing;
using Xunit.Abstractions;

namespace Tank.Sim.Tests;

/// <summary>
/// Bots fetch supply crates (stage 6 design §4.5; stage 6B plan, task 6): a bot that isn't fighting goes for a useful crate
/// it can see within <see cref="BotDirector.CrateRange"/> when the zone's timing allows, repair first when hurt, ammo when
/// low, then any layer it hasn't filled; it stops to fight on the way and goes on afterwards; it gives the crate up when
/// someone else ate it, after <see cref="BotDirector.CrateGiveUpTicks"/>, or when the ring changed and the crate lies outside
/// the new one; a crate given up is shunned for <see cref="BotDirector.CrateShunTicks"/> or until the next ring (batch C
/// review follow-up). Played on the flat 400 m arena with hand-placed crates and rings round its centre (<see cref="Rings"/>).
/// </summary>
public sealed class BotCrateTests
{
    private const int Tps = SimConstants.TickRate;

    private static readonly Vector2 Centre = new(200, 200);

    /// <summary>Where the bot starts in most tests, facing east.</summary>
    private static readonly Vector2 BotAt = new(100, 200);

    /// <summary>A repair crate 60 m east of <see cref="BotAt"/>.</summary>
    private static readonly Vector2 CrateAt = new(160, 200);

    private static readonly int Max = TankCatalog.Medium.MaxHealth;

    /// <summary>Rings round the arena's centre: ring 1 (200 m) holds every waypoint well inside, so a bot in it never runs; it shrinks from 90 s to 180 s.</summary>
    private static readonly ZoneRing[] Rings =
    {
        new(Centre, 283f), new(Centre, 200f), new(Centre, 150f), new(Centre, 100f), new(Centre, 50f), new(Centre, 0f),
    };

    /// <summary>Ring 1 only 100 m: a crate 105 m from the centre lies outside the ring the bots head for.</summary>
    private static readonly ZoneRing[] SmallRings =
    {
        new(Centre, 283f), new(Centre, 100f), new(Centre, 80f), new(Centre, 60f), new(Centre, 40f), new(Centre, 0f),
    };

    /// <summary>Ring 2 moves to (80, 200), 36 m: a bot a few metres east of (100, 200) is in it, the crate at (120, 200) isn't.</summary>
    private static readonly ZoneRing[] MovingRings =
    {
        new(Centre, 283f), new(Centre, 200f), new(new Vector2(80, 200), 36f), new(new Vector2(80, 200), 30f),
        new(new Vector2(80, 200), 20f), new(new Vector2(80, 200), 0f),
    };

    /// <summary>
    /// Rings 1 and 2 36 m and 30 m round the centre, smaller than <see cref="BotDirector.ZoneWanderRadius"/>: a bot in them
    /// holds where it is (looking for crates still), so nothing is eaten by chance on a wander; ring 2 is the target from 180 s.
    /// </summary>
    private static readonly ZoneRing[] HoldRings =
    {
        new(Centre, 283f), new(Centre, 36f), new(Centre, 30f), new(Centre, 24f), new(Centre, 18f), new(Centre, 0f),
    };

    /// <summary>Where the bot holds in <see cref="HoldRings"/>: 30 m west of the centre.</summary>
    private static readonly Vector2 HoldAt = new(170, 200);

    /// <summary>A crate 30 m east of the centre: 60 m from <see cref="HoldAt"/>, in every ring of <see cref="HoldRings"/> but the last.</summary>
    private static readonly Vector2 HeldCrateAt = new(230, 200);

    /// <summary>A far corner, out of sight of the bots' starts (more than a medium's 270 m view range from them).</summary>
    private static readonly Vector2 FarCorner = new(385, 385);

    private readonly ITestOutputHelper _output;

    public BotCrateTests(ITestOutputHelper output) => _output = output;

    private static Crate[] Crates(params (Vector2 At, CrateKind Kind)[] crates)
    {
        var all = new Crate[crates.Length];
        for (int i = 0; i < all.Length; i++) all[i] = new Crate((byte)i, crates[i].At, crates[i].Kind);
        return all;
    }

    /// <summary>
    /// A royale with <paramref name="crates"/> and a tank nobody drives in the far corner that keeps the match on
    /// (<see cref="RoyaleMatch.ToughHealth"/> times its health: the zone doesn't take it in time either).
    /// </summary>
    private static BotArena Arena(IReadOnlyList<Crate> crates, IReadOnlyList<ZoneRing>? rings = null, IReadOnlyList<Prop>? props = null)
    {
        var arena = new BotArena(BattleMode.Royale, BotArena.FlatMap(props: props), zone: rings ?? Rings, crates: crates);
        arena.Idle(TankCatalog.Medium with { MaxHealth = TankCatalog.Medium.MaxHealth * RoyaleMatch.ToughHealth }, 0, FarCorner);
        return arena;
    }

    /// <summary>A medium bot at <paramref name="at"/> facing east with <paramref name="health"/> of its health; without rounds unless <paramref name="armed"/>.</summary>
    private static Bot BotWith(BotArena arena, Vector2 at, float health, bool armed = false, uint seed = 1)
    {
        Bot bot = arena.Bot(TankCatalog.Medium, 0, seed, at, BotArena.East);
        ref TankState t = ref arena.Sim.TankRef(bot.TankId);
        t.Health = (int)(Max * health);
        if (!armed) (t.ApLeft, t.HeLeft) = (0, 0);
        return bot;
    }

    private static CrateSet Set(BotArena arena) => Assert.IsType<CrateSet>(arena.Rules.Crates);

    /// <summary>Plays until <paramref name="until"/> holds, at most <paramref name="seconds"/>: the tick it held at, or −1.</summary>
    private static int RunUntil(BotArena arena, int seconds, Func<bool> until)
    {
        for (int k = 0; k < seconds * Tps; k++)
        {
            arena.Tick();
            if (until()) return arena.Sim.Tick;
        }
        return -1;
    }

    /// <summary>
    /// Closes the eight cells round the crate's on the director's grid (<see cref="NavGrid.SetCostForTests"/>; the searches
    /// read that grid): no way to the crate, and nothing in the way of the sight of it. Returns what opens them again.
    /// </summary>
    private static Action WallIn(BotArena arena, Vector2 crate)
    {
        NavGrid grid = arena.Director.Grid;
        (int ci, int cj) = NavGrid.CellOf(crate);
        var cells = new List<(int I, int J, ushort Cost)>();
        for (int i = ci - 1; i <= ci + 1; i++)
            for (int j = cj - 1; j <= cj + 1; j++)
            {
                if (i == ci && j == cj) continue;
                cells.Add((i, j, grid.CostAt(i, j)));
                grid.SetCostForTests(i, j, NavGrid.Blocked);
            }
        return () =>
        {
            foreach ((int i, int j, ushort cost) in cells) grid.SetCostForTests(i, j, cost);
        };
    }

    /// <summary>The crate at <paramref name="crate"/> walled in (<see cref="WallIn"/>), the hurt bot's first run for it is given up (no way to it): the tick, and what opens the way.</summary>
    private static (int GaveUpAt, Action Open) GiveUpWalledIn(BotArena arena, Bot bot, Vector2 crate)
    {
        Action open = WallIn(arena, crate);
        int gaveUpAt = RunUntil(arena, 10, () => bot.CrateRuns == 1 && bot.Goal != crate);
        Assert.True(gaveUpAt > 0, "the run for the walled-in crate wasn't given up");
        Assert.Equal(1, arena.Director.Paths.Failures);
        Assert.True(Set(arena).IsStanding(0));
        return (gaveUpAt, open);
    }

    [Fact]
    public void A_hurt_bot_fetches_a_repair_crate_in_sight_and_eats_it()
    {
        BotArena arena = Arena(Crates((CrateAt, CrateKind.Repair)));
        Bot bot = BotWith(arena, BotAt, 0.5f);
        CrateSet crates = Set(arena);

        arena.Run(2 * Tps);
        Assert.Equal(1, bot.CrateRuns);
        Assert.Equal(CrateAt, bot.Goal);
        Assert.Equal(BotActivity.Driving, bot.Activity);

        int at = RunUntil(arena, 30, () => bot.CratesTaken == 1);
        _output.WriteLine($"ate the crate at {at / (float)Tps:F1} s; {arena.Director.Summary()}");
        Assert.True(at > 0, "it never ate the crate");
        Assert.False(crates.IsStanding(0));
        Assert.Equal(bot.TankId, crates.TakenBy(0));
        Assert.Equal(Max / 2 + CrateRules.RepairAmount(Max), arena.Tank(bot.TankId).Health);
        Assert.NotEqual(CrateAt, bot.Goal);
        Assert.StartsWith("1 crate runs, 1 fetched; ", arena.Director.Summary());
    }

    [Fact]
    public void A_bot_at_full_health_leaves_a_repair_crate()
    {
        BotArena arena = Arena(Crates((new Vector2(160, 230), CrateKind.Repair)));
        Bot bot = BotWith(arena, BotAt, 1f);

        arena.Run(10 * Tps);
        Assert.Equal(0, bot.CrateRuns);
        Assert.Equal(0, bot.CratesTaken);
        Assert.StartsWith("0 crate runs, 0 fetched; ", arena.Director.Summary());
    }

    /// <summary>Repair 50 m off, ammo 30 m off: below 60 % health repair comes first; below half the rounds ammo; otherwise the nearer useful one.</summary>
    [Theory]
    [InlineData(0.5f, 0.2f, CrateKind.Repair)]   // hurt and low: repair first
    [InlineData(0.8f, 0.2f, CrateKind.Ammo)]     // low on rounds: ammo first
    [InlineData(0.8f, 0.6f, CrateKind.Ammo)]     // both merely useful: the nearer
    [InlineData(0.8f, 1f, CrateKind.Repair)]     // full racks: ammo does nothing
    public void Repair_comes_first_when_hurt_then_ammo_when_low_then_the_nearer_useful_crate(float health, float ammo, CrateKind expected)
    {
        Vector2 repair = BotAt + new Vector2(50, 0), ammoAt = BotAt + new Vector2(30, 0);
        BotArena arena = Arena(Crates((repair, CrateKind.Repair), (ammoAt, CrateKind.Ammo)));
        Bot bot = BotWith(arena, BotAt, health, armed: true);
        ref TankState t = ref arena.Sim.TankRef(bot.TankId);
        (t.ApLeft, t.HeLeft) = ((int)(t.Spec.Ap.Capacity * ammo), (int)(t.Spec.He.Capacity * ammo));

        arena.Run(Tps);
        Assert.Equal(1, bot.CrateRuns);
        Assert.Equal(expected == CrateKind.Repair ? repair : ammoAt, bot.Goal);
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(3, 0)]
    public void An_armour_crate_is_fetched_only_while_a_layer_is_missing(int layers, int runs)
    {
        BotArena arena = Arena(Crates((BotAt + new Vector2(40, 0), CrateKind.Armor)));
        Bot bot = BotWith(arena, BotAt, 1f);
        arena.Sim.TankRef(bot.TankId).ArmorLayers = (byte)layers;

        arena.Run(Tps);
        Assert.Equal(runs, bot.CrateRuns);
    }

    [Theory]
    [InlineData(150f, 1)]
    [InlineData(151f, 0)]
    public void A_crate_is_fetched_within_the_crate_range_only(float distance, int runs)
    {
        Vector2 at = new(60, 200);
        BotArena arena = Arena(Crates((at + new Vector2(distance, 0), CrateKind.Repair)));
        Bot bot = BotWith(arena, at, 0.5f);

        arena.Run(VisionTracker.UpdateInterval);   // one look, before the tank has moved
        Assert.Equal(runs, bot.CrateRuns);
    }

    [Fact]
    public void A_crate_behind_a_wall_is_fetched_once_the_wall_has_fallen()
    {
        Prop[] wall = new WallLine(PropKind.BrickWall, new Vector2(130, 170), new Vector2(130, 230)).Segments(Ground400.Flat(), 1).ToArray();
        BotArena arena = Arena(Crates((CrateAt, CrateKind.Repair)), props: wall);
        Bot bot = BotWith(arena, BotAt, 0.5f);

        arena.Run(2 * Tps);
        Assert.Equal(0, bot.CrateRuns);

        foreach (Prop p in wall) arena.Sim.Field.SetDurability(p.Id, 0);
        arena.Run(2 * Tps);
        Assert.Equal(1, bot.CrateRuns);
        Assert.Equal(CrateAt, bot.Goal);
    }

    /// <summary>
    /// 60 m to the crate (× 1.3) at 60 % of a medium's top speed is 281 ticks, plus the 15 s margin 731: with 20 s left
    /// before ring 1 has shrunk the bot leaves it; with 60 s left it goes. The bot is put beside the crate at that time, hurt.
    /// </summary>
    [Theory]
    [InlineData(20, 0)]
    [InlineData(60, 1)]
    public void A_crate_is_left_when_the_time_to_it_and_on_into_the_ring_runs_short(int secondsLeft, int runs)
    {
        Vector2 crate = new(160, 240), at = new(100, 240);
        BotArena arena = Arena(Crates((crate, CrateKind.Repair)));
        Bot bot = BotWith(arena, BotAt, 1f);
        arena.Recording = false;

        arena.Run(ZoneSchedule.ShrinkEnd(1) - secondsLeft * Tps - arena.Sim.Tick);
        Assert.True(Set(arena).IsStanding(0), "the crate was eaten by chance before the look");
        Assert.False(bot.RunningToZone);
        Assert.Equal(0, bot.CrateRuns);
        arena.Place(bot.TankId, at, BotArena.East);
        arena.Sim.TankRef(bot.TankId).Health = Max / 2;

        arena.Run(2 * VisionTracker.UpdateInterval);
        Assert.Equal(runs, bot.CrateRuns);
    }

    [Theory]
    [InlineData(295f, 1)]   // 95 m from the centre: in ring 1 (100 m)
    [InlineData(305f, 0)]   // 105 m: outside it
    public void A_crate_outside_the_ring_the_bot_heads_for_is_left(float crateX, int runs)
    {
        Vector2 at = new(170, 200);
        BotArena arena = Arena(Crates((new Vector2(crateX, 200), CrateKind.Repair)), SmallRings);
        Bot bot = BotWith(arena, at, 0.5f);

        arena.Run(2 * VisionTracker.UpdateInterval);
        Assert.False(bot.RunningToZone);
        Assert.Equal(runs, bot.CrateRuns);
    }

    /// <summary>A bot fighting an enemy 30 m off is hurt (by the test) with a repair crate in sight: no run until the enemy is gone.</summary>
    [Fact]
    public void A_bot_with_a_target_starts_no_crate_run_until_the_target_is_gone()
    {
        BotArena arena = Arena(Crates((CrateAt, CrateKind.Repair)));
        Bot bot = BotWith(arena, BotAt, 1f, armed: true);
        int enemy = arena.Idle(TankCatalog.Medium, 0, BotAt + new Vector2(0, -30));
        Assert.True(RunUntil(arena, 3, () => bot.Activity == BotActivity.Engaging) > 0, "it never engaged the enemy");
        arena.Sim.TankRef(bot.TankId).Health = Max / 2;

        arena.Run(5 * Tps, tick => Assert.Equal(enemy, bot.TargetId));
        Assert.Equal(0, bot.CrateRuns);

        arena.Sim.TankRef(enemy).Health = 0;
        arena.Sim.TankRef(enemy).IsDestroyed = true;
        int at = RunUntil(arena, 10, () => bot.CrateRuns == 1);
        Assert.True(at > 0, "no crate run once the target was gone");
        Assert.Equal(CrateAt, bot.Goal);
    }

    [Fact]
    public void A_bot_on_its_way_to_a_crate_stops_to_fight_and_goes_on_afterwards()
    {
        BotArena arena = Arena(Crates((CrateAt, CrateKind.Repair)));
        Bot bot = BotWith(arena, BotAt, 0.5f, armed: true);
        int enemy = arena.Idle(TankCatalog.Medium, 0, new Vector2(385, 20));
        Assert.True(RunUntil(arena, 3, () => bot.CrateRuns == 1 && bot.Activity == BotActivity.Driving) > 0, "no crate run under way");

        arena.Place(enemy, new Vector2(130, 150), BotArena.North);
        int fightAt = RunUntil(arena, 5, () => bot.Activity == BotActivity.Engaging);
        Assert.True(fightAt > 0, "it never stopped to fight");
        Assert.Equal(enemy, bot.TargetId);
        Assert.Equal(CrateAt, bot.Goal);
        Assert.Equal(1, bot.CrateRuns);

        arena.Sim.TankRef(enemy).Health = 0;
        arena.Sim.TankRef(enemy).IsDestroyed = true;
        int ateAt = RunUntil(arena, 20, () => bot.CratesTaken == 1);
        _output.WriteLine($"fought from {fightAt / (float)Tps:F1} s, ate the crate at {ateAt / (float)Tps:F1} s");
        Assert.True(ateAt > 0, "it never went on to the crate");
        Assert.Equal(1, bot.CrateRuns);
    }

    [Fact]
    public void A_crate_someone_else_ate_is_given_up()
    {
        BotArena arena = Arena(Crates((CrateAt, CrateKind.Repair)));
        Bot bot = BotWith(arena, BotAt, 0.5f);
        int eater = arena.Idle(TankCatalog.Medium, 0, new Vector2(385, 20));
        CrateSet crates = Set(arena);
        Assert.True(RunUntil(arena, 3, () => bot.CrateRuns == 1 && bot.Activity == BotActivity.Driving) > 0, "no crate run under way");

        arena.Place(eater, CrateAt, BotArena.East);
        arena.Run(1);
        Assert.Equal(eater, crates.TakenBy(0));

        arena.Run(1);
        Assert.NotEqual(CrateAt, bot.Goal);
        Assert.Equal(0, bot.CratesTaken);
        Assert.Equal(1, bot.CrateRuns);
        Assert.Equal(Max / 2, arena.Tank(bot.TankId).Health);
    }

    /// <summary>
    /// A rock 1 m high across the arena at x = 130 (z 30 to 370): the bot sees the crate's sign over it, but the way round
    /// either end is some 350 m, over 20 s of driving, so the run times out and the bot wanders on; the crate, still in
    /// sight, is not picked again at the next look (batch C review follow-up: it used to be, 6 ticks on).
    /// </summary>
    [Fact]
    public void A_crate_run_is_given_up_after_the_give_up_time_and_the_crate_is_not_picked_again_at_once()
    {
        var rock = new Prop(1, PropKind.Rock, new Obb2(new Vector2(130, 200), new Vector2(1, 170), 0f), -1f, 2f);
        BotArena arena = Arena(Crates((CrateAt, CrateKind.Repair)), props: new[] { rock });
        Bot bot = BotWith(arena, BotAt, 0.5f);
        CrateSet crates = Set(arena);

        arena.Run(2 * Tps);
        Assert.Equal(1, bot.CrateRuns);
        int begun = arena.Sim.Tick;
        arena.Run(BotDirector.CrateGiveUpTicks - 3 * Tps);
        Assert.Equal(CrateAt, bot.Goal);   // still on its way
        Assert.True(crates.IsStanding(0));

        int gaveUpAt = RunUntil(arena, 6, () => bot.Goal != CrateAt);
        _output.WriteLine($"gave up {(gaveUpAt - begun) / (float)Tps:F1} s after the run was seen under way, at {arena.Tank(bot.TankId).Position}; {bot.Stucks} stucks");
        Assert.True(gaveUpAt > 0, "it never gave the crate up");
        Assert.True(gaveUpAt - begun <= BotDirector.CrateGiveUpTicks + VisionTracker.UpdateInterval, $"gave up only {(gaveUpAt - begun) / (float)Tps:F1} s later");
        Assert.True(crates.IsStanding(0));
        Assert.Equal(0, bot.CratesTaken);
        Assert.Equal(1, bot.CrateRuns);

        arena.Run(10 * Tps);   // shunned: the crate in sight 30 m off, the bot hurt, and no second run
        Assert.Equal(1, bot.CrateRuns);
        Assert.True(crates.IsStanding(0));
    }

    /// <summary>
    /// Four rocks 1 m high box the crate in (the sign in sight over them, no way to it: every search fails). Both reviewers'
    /// finding: given up, the crate used to be picked again at the next look, 6 ticks on, a full flood search each time
    /// (300 runs, 299 searches that found no path in 60 s, the bot motionless the whole while); a crate given up is now
    /// shunned for <see cref="BotDirector.CrateShunTicks"/>, and the bot wanders on.
    /// </summary>
    [Fact]
    public void A_crate_walled_in_by_rocks_is_given_up_once_and_the_bot_moves_on()
    {
        Prop[] rocks =
        {
            new(1, PropKind.Rock, new Obb2(new Vector2(160, 190), new Vector2(10, 1), 0f), -1f, 2f),
            new(2, PropKind.Rock, new Obb2(new Vector2(160, 210), new Vector2(10, 1), 0f), -1f, 2f),
            new(3, PropKind.Rock, new Obb2(new Vector2(150, 200), new Vector2(1, 10), 0f), -1f, 2f),
            new(4, PropKind.Rock, new Obb2(new Vector2(170, 200), new Vector2(1, 10), 0f), -1f, 2f),
        };
        BotArena arena = Arena(Crates((CrateAt, CrateKind.Repair)), props: rocks);
        Bot bot = BotWith(arena, BotAt, 0.5f);

        arena.Run(30 * Tps);
        float stuck = arena.LongestStuckSeconds(bot.TankId);
        _output.WriteLine($"after 30 s: {bot.CrateRuns} crate runs, {bot.Stucks} stucks, got nowhere for {stuck:F1} s at most, " +
                          $"at {arena.Tank(bot.TankId).Position}, {bot.Activity}; {arena.Director.Summary()}");
        Assert.True(bot.CrateRuns <= 2, $"{bot.CrateRuns} crate runs for the walled-in crate");
        Assert.True(bot.Stucks <= 2, $"{bot.Stucks} stucks");
        Assert.NotEqual(CrateAt, bot.Goal);
        Assert.True(stuck < 10f, $"got nowhere for {stuck:F1} s");
        Assert.True(arena.Director.Paths.Failures <= 2, $"{arena.Director.Paths.Failures} searches found no path");
        Assert.True(Set(arena).IsStanding(0));
    }

    /// <summary>
    /// The crate's cell walled in on the grid alone (<see cref="WallIn"/>: nothing in the way of the sight of it), the first
    /// run finds no way and the crate is given up; the cells opened again, the crate is in sight and in reach, yet the bot
    /// (holding in a ring too small to wander) leaves it until <see cref="BotDirector.CrateShunTicks"/> have passed since
    /// the give-up, then runs for it and eats it.
    /// </summary>
    [Fact]
    public void A_crate_given_up_is_fetched_again_once_the_shun_has_passed()
    {
        BotArena arena = Arena(Crates((HeldCrateAt, CrateKind.Repair)), HoldRings);
        Bot bot = BotWith(arena, HoldAt, 0.5f);
        (int gaveUpAt, Action open) = GiveUpWalledIn(arena, bot, HeldCrateAt);
        open();

        arena.Run(gaveUpAt + BotDirector.CrateShunTicks - VisionTracker.UpdateInterval - arena.Sim.Tick);
        Assert.Equal(1, bot.CrateRuns);
        Assert.Equal(BotActivity.Holding, bot.Activity);
        int againAt = RunUntil(arena, 2, () => bot.CrateRuns == 2);
        _output.WriteLine($"gave up at {gaveUpAt / (float)Tps:F1} s, ran for the crate again at {againAt / (float)Tps:F1} s");
        Assert.True(againAt > 0, "not fetched again once the shun had passed");
        Assert.True(againAt >= gaveUpAt + BotDirector.CrateShunTicks, $"ran for it again {(againAt - gaveUpAt) / (float)Tps:F1} s after giving it up");
        Assert.Equal(HeldCrateAt, bot.Goal);
        Assert.True(RunUntil(arena, 30, () => bot.CratesTaken == 1) > 0, "it never ate the crate");
    }

    /// <summary>
    /// A new ring clears the shun (a crate boxed in by parked tanks may be free in the next ring): the crate walled in on
    /// the grid, and opened again once the run was given up, 30 s before ring 2 becomes the target, it is left for those
    /// 30 s and run for right after the change, <see cref="BotDirector.CrateShunTicks"/> not having passed.
    /// </summary>
    [Fact]
    public void A_ring_change_clears_the_shun()
    {
        BotArena arena = Arena(Crates((HeldCrateAt, CrateKind.Repair)), HoldRings);
        Bot bot = BotWith(arena, HoldAt, 1f);   // whole: no run until hurt
        int far = arena.Rules.Of(0).TankId;    // the far corner's tank, outside the 36 m ring 1 from 98 s on
        arena.Recording = false;
        int change = ZoneSchedule.ShrinkEnd(1);

        arena.Run(change - 30 * Tps - arena.Sim.Tick);
        Assert.Equal(0, bot.CrateRuns);
        arena.Sim.TankRef(bot.TankId).Health = Max / 2;
        (int gaveUpAt, Action open) = GiveUpWalledIn(arena, bot, HeldCrateAt);
        open();

        arena.Run(change - 1 - arena.Sim.Tick);   // ring 1's last tick
        Assert.Equal(1, arena.Rules.Zone.Ring);
        Assert.Equal(1, bot.CrateRuns);
        arena.Sim.TankRef(far).Health = arena.Tank(far).Spec.MaxHealth;   // the zone would take it by 190 s, the match with it
        int againAt = RunUntil(arena, 1, () => bot.CrateRuns == 2);
        _output.WriteLine($"gave up at {gaveUpAt / (float)Tps:F1} s, ring 2 from {change / (float)Tps:F0} s, ran for the crate again at {againAt / (float)Tps:F1} s");
        Assert.Equal(2, arena.Rules.Zone.Ring);
        Assert.True(againAt > 0, "not run for again after the ring change");
        Assert.True(againAt - gaveUpAt < BotDirector.CrateShunTicks);
        Assert.Equal(HeldCrateAt, bot.Goal);
        Assert.True(RunUntil(arena, 30, () => bot.CratesTaken == 1) > 0, "it never ate the crate");
    }

    /// <summary>
    /// A run can outlast its ring only when it began within <see cref="BotDirector.CrateGiveUpTicks"/> of the ring's end,
    /// which the time rule allows only for a crate a few seconds off (15 s margin plus the way): the bot, put 20 m from
    /// the crate 19 s before ring 1 has shrunk and hurt, starts for it (3 s of driving × 1.3, plus the margin, is within the
    /// 19 s), then stops to fight a tough enemy and is still at it when ring 2, 36 m round (80, 200), becomes the target:
    /// the bot is in it, the crate isn't, so the crate is given up (for holding: the ring is too small to wander), not run from.
    /// </summary>
    [Fact]
    public void A_crate_outside_the_new_ring_is_given_up_when_the_ring_changes()
    {
        Vector2 crate = BotAt + new Vector2(20, 0);
        BotArena arena = Arena(Crates((crate, CrateKind.Repair)), MovingRings);
        Bot bot = BotWith(arena, new Vector2(300, 100), 1f);   // unarmed and whole until the moment: it only wanders meanwhile
        int enemy = arena.Idle(TankCatalog.Heavy with { MaxHealth = TankCatalog.Heavy.MaxHealth * RoyaleMatch.ToughHealth }, 0, new Vector2(385, 20));
        CrateSet crates = Set(arena);
        arena.Recording = false;
        arena.Run(ZoneSchedule.ShrinkEnd(1) - 19 * Tps - arena.Sim.Tick);
        Assert.True(crates.IsStanding(0), "the crate was eaten by chance before the moment");
        Assert.Equal(0, bot.CrateRuns);
        arena.Place(bot.TankId, BotAt, BotArena.East);
        ref TankState t = ref arena.Sim.TankRef(bot.TankId);
        (t.Health, t.ApLeft) = (Max / 2, 10_000);   // rounds enough to fire to the ring's end and beyond

        int begunAt = RunUntil(arena, 1, () => bot.CrateRuns == 1);
        Assert.True(begunAt > 0, "no crate run under way");
        Assert.Equal(crate, bot.Goal);
        arena.Place(enemy, BotAt + new Vector2(10, -60), BotArena.North);
        int fightAt = RunUntil(arena, 3, () => bot.Activity == BotActivity.Engaging);
        Assert.True(fightAt > 0, "it never stopped to fight");
        Assert.Equal(crate, bot.Goal);
        Assert.True(crates.IsStanding(0), "it reached the crate before the fight");

        arena.Run(ZoneSchedule.ShrinkEnd(1) - 1 - arena.Sim.Tick);   // ring 1's last tick
        Assert.Equal(1, arena.Rules.Zone.Ring);
        Assert.Equal(crate, bot.Goal);
        Assert.Equal(enemy, bot.TargetId);

        arena.Run(2);   // ring 2's first tick (the rules see it after the step), then the command that sees it
        TankState me = arena.Tank(bot.TankId);
        _output.WriteLine($"run begun at {begunAt / (float)Tps:F1} s, fighting from {fightAt / (float)Tps:F1} s; at ring 2 ({arena.Sim.Tick / (float)Tps:F1} s): " +
                          $"at {me.Position}, {bot.Activity}, goal {bot.Goal}, health {me.Health}; {arena.Director.Summary()}");
        Assert.Equal(2, arena.Rules.Zone.Ring);
        Assert.False(me.IsDestroyed);
        Assert.False(bot.RunningToZone);
        Assert.NotEqual(crate, bot.Goal);
        Assert.True(crates.IsStanding(0));
        Assert.Equal(0, bot.CratesTaken);
        Assert.Equal(1, bot.CrateRuns);
    }

    /// <summary>
    /// Four bots, six crates and a brick wall between the first bot and the crate nearest it (the rays of every look meet
    /// real props: a flat arena without any scans empty sets), over 10 s in which runs both start and end (crates eaten).
    /// </summary>
    [Fact]
    public void Fetching_crates_allocates_nothing()
    {
        Allocation.AssertNone(() =>
        {
            Prop[] wall = new WallLine(PropKind.BrickWall, new Vector2(140, 100), new Vector2(140, 140)).Segments(Ground400.Flat(), 1).ToArray();
            BotArena arena = Arena(Crates(
                (new Vector2(160, 120), CrateKind.Repair), (new Vector2(240, 120), CrateKind.Repair),
                (new Vector2(160, 280), CrateKind.Repair), (new Vector2(240, 280), CrateKind.Repair),
                (new Vector2(200, 160), CrateKind.Ammo), (new Vector2(200, 240), CrateKind.Armor)), props: wall);
            Bot[] bots =
            {
                BotWith(arena, new Vector2(120, 120), 1f, seed: 1), BotWith(arena, new Vector2(280, 120), 1f, seed: 2),
                BotWith(arena, new Vector2(120, 280), 1f, seed: 3), BotWith(arena, new Vector2(200, 220), 1f, seed: 4),
            };
            arena.Recording = false;
            const int Window = 10 * Tps;
            arena.Run(2 * Tps);
            foreach (Bot bot in bots) arena.Sim.TankRef(bot.TankId).Health = Max / 2;   // every one wants a repair now
            int runs = bots.Sum(b => b.CrateRuns), taken = bots.Sum(b => b.CratesTaken);
            Allocation.Window window = Allocation.Start();
            for (int k = 0; k < Window; k++) arena.Tick();
            Allocation.Measured measured = window.Stop();
            Assert.True(bots.Sum(b => b.CrateRuns) > runs, "no crate run started in the window");
            Assert.True(bots.Sum(b => b.CratesTaken) > taken, "no crate was fetched in the window");
            return measured;
        });
    }
}
