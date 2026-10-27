using System.Numerics;
using Tank.Sim.Ai;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;
using Tank.Testing;
using Xunit.Abstractions;

namespace Tank.Sim.Tests;

/// <summary>
/// Bots in a standard battle (stage 5 design §4.2–4.3, stage 5C task 7): roles given out at the start, defenders on
/// cover points, attackers down their lanes to points apart inside the enemy base, the two nearest called back when the
/// own base starts to fall and sent back to their roles once it is quiet, the badly hurt pulling back to a friend or the
/// base, and the defaults for a map without bot data. Played on <see cref="BotArena"/>'s flat arena: lanes 0 north,
/// 1 middle, 2 south; the flank lane is 0 (north and south stray as far from the line between the bases: the lower index).
/// </summary>
public sealed class BotRoleTests
{
    private const int Tps = SimConstants.TickRate;

    /// <summary>Team 1's corner: a still tank in the bush there is seen by nobody further than about 140 m off.</summary>
    private static readonly Vector2 Corner = new(385, 15);

    private static readonly Prop[] CornerBush = { new(1, PropKind.Bush, new Obb2(Corner, new Vector2(4, 4), 0f), -0.2f, 4.5f) };

    /// <summary>The way the enemy comes at team 0's base: the mean of the lanes' first via points.</summary>
    private static readonly Vector2 Approach0 = new(200, 200);

    private readonly ITestOutputHelper _output;

    public BotRoleTests(ITestOutputHelper output) => _output = output;

    private static BotArena Standard(bool botData = true) =>
        new(BattleMode.Standard, BotArena.FlatMap(props: CornerBush, botData: botData));

    /// <summary>Team 1's only tank, hidden in the corner bush (a standard battle with nobody on one side is over at once).</summary>
    private static int HiddenEnemy(BotArena arena) => arena.Idle(TankCatalog.Light, 1, Corner, BotArena.West);

    private static bool InCircle(Vector2 p, Vector2 centre) => Vector2.Distance(p, centre) <= BotArena.BaseRadius;

    private static Bot[] Team0(BattleMode mode, params TankSpec[] specs)
    {
        var arena = mode == BattleMode.Standard ? Standard() : new BotArena(mode, BotArena.FlatMap(props: CornerBush));
        var bots = new Bot[specs.Length];
        for (int k = 0; k < specs.Length; k++) bots[k] = arena.Bot(specs[k], 0, (uint)k + 1);
        HiddenEnemy(arena);
        arena.Director.AssignRoles();
        return bots;
    }

    private static void AssertAttack(Bot bot, int lane)
    {
        Assert.Equal(BotRole.Attack, bot.Role);
        Assert.Equal(lane, bot.Lane);
        Assert.Equal(-1, bot.Cover);
    }

    private static void AssertDefend(Bot bot)
    {
        Assert.Equal(BotRole.Defend, bot.Role);
        Assert.Equal(-1, bot.Lane);
        Assert.InRange(bot.Cover, 0, BotArena.Bots.CoverOf(0).Count - 1);
    }

    [Fact]
    public void Roles_follow_the_design()
    {
        TankSpec td = TankCatalog.Td, heavy = TankCatalog.Heavy, medium = TankCatalog.Medium, light = TankCatalog.Light;

        Bot[] b = Team0(BattleMode.Standard, td, medium, light);
        AssertDefend(b[0]);
        AssertAttack(b[2], 0);   // the light tank takes the flank
        AssertAttack(b[1], 1);

        b = Team0(BattleMode.Standard, heavy, medium, medium, light);
        AssertDefend(b[0]);
        AssertAttack(b[3], 0);
        AssertAttack(b[1], 1);
        AssertAttack(b[2], 2);

        b = Team0(BattleMode.Standard, medium, light);   // two tanks: nobody stays at home
        AssertAttack(b[1], 0);
        AssertAttack(b[0], 1);

        b = Team0(BattleMode.Standard, td, td, td, medium);   // two defenders at most: the third tank destroyer attacks
        AssertDefend(b[0]);
        AssertDefend(b[1]);
        Assert.NotEqual(b[0].Cover, b[1].Cover);
        AssertAttack(b[2], 0);
        AssertAttack(b[3], 1);

        // One bot and two players make three tanks: the bot stays at home.
        var arena = Standard();
        arena.Join(medium, 0);
        arena.Join(medium, 0);
        Bot alone = arena.Bot(medium, 0, seed: 1);
        HiddenEnemy(arena);
        arena.Director.AssignRoles();
        AssertDefend(alone);
        Assert.Equal(3, arena.Director.LaneCount);
        Assert.Equal(0, arena.Director.FlankLane);

        foreach (TankSpec[] team in new[] { new[] { td, medium, light }, new[] { heavy, medium, medium, light }, new[] { td, td, td } })
        {
            foreach (Bot bot in Team0(BattleMode.TeamBattle, team))
            {
                Assert.Equal(BotRole.Wander, bot.Role);
                Assert.Equal(-1, bot.Lane);
                Assert.Equal(-1, bot.Cover);
            }
        }
        var battle = new BotArena(BattleMode.TeamBattle);
        battle.Bot(medium, 0, seed: 1);
        battle.Director.AssignRoles();
        Assert.Equal(0, battle.Director.LaneCount);
    }

    [Fact]
    public void Defenders_settle_on_different_cover_points_facing_the_way_the_enemy_comes()
    {
        var arena = Standard();
        Bot first = arena.Bot(TankCatalog.Td, 0, seed: 1), second = arena.Bot(TankCatalog.Td, 0, seed: 2);
        arena.Idle(TankCatalog.Medium, 0, HomeCorner);   // a player: the team has an attacker, so the two stay at home
        HiddenEnemy(arena);
        arena.Run(30 * Tps);
        Assert.NotEqual(first.Cover, second.Cover);
        foreach (Bot bot in new[] { first, second })
        {
            AssertDefend(bot);
            Vector2 cover = arena.Director.CoverOf(0)[bot.Cover];
            TankState t = arena.Tank(bot.TankId);
            float facing = Angles.Wrap(t.Yaw - Angles.YawOf(Approach0 - cover));
            _output.WriteLine($"tank {bot.TankId}: cover {bot.Cover} {cover}, at {t.Position}, {Vector2.Distance(t.Position, cover):F1} m off, " +
                              $"{t.Speed:F2} m/s, facing {facing * 180 / MathF.PI:F1}° off the approach");
            Assert.True(Vector2.Distance(t.Position, cover) <= 4f, $"tank {bot.TankId} at {t.Position}, its cover point {cover}");
            Assert.True(MathF.Abs(t.Speed) < 0.3f, $"{t.Speed:F2} m/s");
            Assert.True(MathF.Abs(facing) <= Angles.Deg(25), $"facing {facing * 180 / MathF.PI:F1}° off the approach");
            Assert.Equal(BotActivity.Holding, bot.Activity);
            Assert.True(arena.AtRoleEnd(bot));
            Assert.True(arena.LongestStuckSeconds(bot.TankId) < 10f, $"stuck for {arena.LongestStuckSeconds(bot.TankId):F1} s");   // holding on cover is no stuck
        }
    }

    [Fact]
    public void Attackers_drive_their_lanes_and_stop_inside_the_enemy_base_apart()
    {
        // Four tanks: the tank destroyer stays at home, so the three mediums all attack (three tanks would keep one back).
        var arena = Standard();
        arena.Bot(TankCatalog.Td, 0, seed: 4);
        Bot[] attackers = { arena.Bot(TankCatalog.Medium, 0, seed: 1), arena.Bot(TankCatalog.Medium, 0, seed: 2), arena.Bot(TankCatalog.Medium, 0, seed: 3) };
        HiddenEnemy(arena);
        BaseCapture capture = arena.Rules.Capture!;
        var nearestVia = new float[attackers.Length];
        Array.Fill(nearestVia, float.MaxValue);
        int allIn = -1, ticksAtAllIn = 0;
        bool threeEachTick = true;
        int last = 0;
        const int limit = 180 * Tps;
        for (int k = 0; k < limit && arena.Rules.Phase == MatchPhase.Playing; k++)
        {
            arena.Tick();
            bool inside = true;
            for (int a = 0; a < attackers.Length; a++)
            {
                Vector2 at = arena.Tank(attackers[a].TankId).Position;
                Vector2 via = BotArena.Bots.ViaFor(attackers[a].Lane, 0)[0];
                nearestVia[a] = MathF.Min(nearestVia[a], Vector2.Distance(at, via));
                inside &= InCircle(at, BotArena.Base1);
            }
            int on = capture.TicksOn(1);
            if (allIn < 0 && inside) (allIn, ticksAtAllIn) = (arena.Sim.Tick, on);
            else if (allIn >= 0 && on < BaseCapture.TicksToWin) threeEachTick &= on - last == 3;
            last = on;
        }

        int expected = allIn + (BaseCapture.TicksToWin - ticksAtAllIn + 2) / 3;
        _output.WriteLine($"all three in the enemy base at {allIn / (float)Tps:F1} s with {ticksAtAllIn} ticks already; " +
                          $"captured at {arena.Rules.EndedAt / (float)Tps:F1} s, {arena.Rules.EndedAt - allIn} ticks later (expected {expected - allIn}); " +
                          $"nearest via {string.Join(", ", nearestVia.Select(d => d.ToString("F1")))} m");
        Assert.True(allIn > 0, "the three never were in the enemy base together");
        for (int a = 0; a < attackers.Length; a++)
        {
            AssertAttack(attackers[a], a);
            Assert.True(nearestVia[a] <= BotDirector.ViaRadius, $"attacker {a} came no nearer than {nearestVia[a]:F1} m to its via point");
            Assert.Equal(BotActivity.Holding, attackers[a].Activity);
            Vector2 p = arena.Tank(attackers[a].TankId).Position;
            Assert.True(InCircle(p, BotArena.Base1), $"attacker {a} at {p}");
            Assert.True(arena.LongestStuckSeconds(attackers[a].TankId) < 10f, $"attacker {a} stuck for {arena.LongestStuckSeconds(attackers[a].TankId):F1} s");   // half a minute holding in the base is no stuck
            for (int b = a + 1; b < attackers.Length; b++)
            {
                float apart = Vector2.Distance(p, arena.Tank(attackers[b].TankId).Position);
                Assert.True(apart >= BotDirector.CaptureSpacing, $"attackers {a} and {b} only {apart:F1} m apart");
            }
        }
        Assert.Empty(arena.AttackersShortOfTheEnemyBase());
        Assert.True(threeEachTick, "all three counted every tick once in");
        Assert.Equal(MatchPhase.Ended, arena.Rules.Phase);
        Assert.Equal(MatchEndReason.Captured, arena.Rules.EndReason);
        Assert.Equal(0, arena.Rules.WinnerTeam);
        Assert.InRange(arena.Rules.EndedAt, expected - 1, expected + 1);
    }

    /// <summary>
    /// Team 0: a tank destroyer on its cover point, a medium 120 m from the base and one 250 m off (or as given);
    /// team 1: one medium nobody drives, protected (hits on it clear nothing), in team 0's base (or as given).
    /// </summary>
    private sealed record Siege(BotArena Arena, Bot Td, Bot Near, Bot Far, int Invader);

    private static Siege Besiege(Vector2? nearAt = null, Vector2? farAt = null, Vector2? invaderAt = null)
    {
        var arena = Standard();
        Bot td = arena.Bot(TankCatalog.Td, 0, seed: 1);
        Bot near = arena.Bot(TankCatalog.Medium, 0, seed: 2);
        Bot far = arena.Bot(TankCatalog.Medium, 0, seed: 3);
        int invader = arena.Join(TankCatalog.Medium, 1);
        arena.Director.AssignRoles();
        arena.Place(td.TankId, arena.Director.CoverOf(0)[td.Cover], BotArena.East);
        arena.Place(near.TankId, nearAt ?? BotArena.Base0 + new Vector2(120, 0), BotArena.East);
        arena.Place(far.TankId, farAt ?? BotArena.Base0 + new Vector2(250, 0), BotArena.East);
        Invade(arena, invader, invaderAt ?? BotArena.Base0);
        return new Siege(arena, td, near, far, invader);
    }

    private static void Invade(BotArena arena, int invader, Vector2 at)
    {
        arena.Place(invader, at, BotArena.West);
        arena.Sim.Protect(invader, 1_000_000);
    }

    /// <summary>Plays until team 0's base score first rises, then one more tick (the team watch sees it at the next update).</summary>
    private static void UntilTheBaseFalls(BotArena arena)
    {
        for (int k = 0; k < 5 * Tps && arena.Rules.Capture!.TicksOn(0) == 0; k++) arena.Tick();
        Assert.True(arena.Rules.Capture!.TicksOn(0) > 0, "the invader earns nothing");
        arena.Tick();
    }

    /// <summary>Plays until both bots called back are in the base (at most 20 s), then 2 s more; returns the time it took, s.</summary>
    private static float UntilBothAreHome(Siege s, Bot a, Bot b)
    {
        int start = s.Arena.Sim.Tick, inside = -1;
        for (int k = 0; k < 20 * Tps && inside < 0; k++)
        {
            s.Arena.Tick();
            if (InCircle(s.Arena.Tank(a.TankId).Position, BotArena.Base0) && InCircle(s.Arena.Tank(b.TankId).Position, BotArena.Base0))
                inside = s.Arena.Sim.Tick;
        }
        Assert.True(inside > 0, $"not both home in 20 s: {s.Arena.Tank(a.TankId).Position}, {s.Arena.Tank(b.TankId).Position}");
        s.Arena.Run(2 * Tps);
        return (inside - start) / (float)Tps;
    }

    [Fact]
    public void When_our_base_starts_to_fall_the_two_nearest_bots_come_back()
    {
        Siege s = Besiege();
        UntilTheBaseFalls(s.Arena);
        Assert.True(s.Td.FallingBack);
        Assert.True(s.Near.FallingBack);
        Assert.False(s.Far.FallingBack);
        Assert.Equal(BotRole.Attack, s.Far.Role);
        Assert.Equal(1, s.Arena.Director.FallBacks(0));
        Assert.Equal(0, s.Arena.Director.FallBacks(1));
        Assert.True(InCircle(s.Td.Goal, BotArena.Base0) && InCircle(s.Near.Goal, BotArena.Base0), $"{s.Td.Goal}, {s.Near.Goal}");

        float took = UntilBothAreHome(s, s.Td, s.Near);
        int score = s.Arena.Rules.Capture!.TicksOn(0);
        s.Arena.Run(5 * Tps);
        _output.WriteLine($"both home after {took:F1} s; base score {score} ticks, then {s.Arena.Rules.Capture.TicksOn(0)}");
        Assert.Equal(score, s.Arena.Rules.Capture.TicksOn(0));   // a defender in the circle: the capture stands still
        Assert.Equal(1, s.Arena.Director.FallBacks(0));   // topped up or not, one fall of the base is one call
        Assert.True(s.Td.FallingBack && s.Near.FallingBack);
    }

    [Fact]
    public void A_bot_capturing_the_enemy_base_is_not_called_back()
    {
        // The medium in the enemy base is nearer home (305 m) than the other one (380 m), but it is capturing.
        Siege s = Besiege(nearAt: BotArena.Base1 + new Vector2(-5, 0), farAt: new Vector2(380, 380));
        UntilTheBaseFalls(s.Arena);
        Assert.True(s.Td.FallingBack);
        Assert.False(s.Near.FallingBack);
        Assert.True(s.Far.FallingBack);
        Assert.Equal(1, s.Arena.Director.FallBacks(0));
    }

    [Fact]
    public void Bots_coming_back_fire_on_the_move()
    {
        Siege s = Besiege();
        UntilTheBaseFalls(s.Arena);
        var speeds = new List<float>();
        s.Arena.Run(20 * Tps, _ =>
        {
            foreach (ref readonly GameEvent e in s.Arena.Sim.Events)
                if (e.Type == GameEventType.ShellFired && e.TankId == s.Near.TankId) speeds.Add(s.Arena.Tank(s.Near.TankId).Speed);
        });
        _output.WriteLine($"shots on the way back at {string.Join(", ", speeds.Select(v => v.ToString("F1")))} m/s");
        Assert.Contains(speeds, v => MathF.Abs(v) > 1f);
    }

    [Fact]
    public void They_go_back_to_their_roles_once_the_base_is_quiet()
    {
        Siege s = Besiege();
        UntilTheBaseFalls(s.Arena);
        UntilBothAreHome(s, s.Td, s.Near);
        s.Arena.Sim.RemoveTank(s.Invader);   // its points go with it
        s.Arena.Run(BotDirector.ReleaseTicks - 1);
        Assert.True(s.Td.FallingBack && s.Near.FallingBack, "released too soon");
        Assert.Equal(0, s.Arena.Rules.Capture!.TicksOn(0));
        s.Arena.Tick();
        Assert.False(s.Td.FallingBack);
        Assert.False(s.Near.FallingBack);
        Assert.Equal(s.Arena.Director.CoverOf(0)[s.Td.Cover], s.Td.Goal);
        IReadOnlyList<Vector2> lane = BotArena.Bots.ViaFor(s.Near.Lane, 0);
        Assert.True(lane.Contains(s.Near.Goal) || InCircle(s.Near.Goal, BotArena.Base1), $"heading for {s.Near.Goal}");
        s.Arena.Run(10 * Tps);
        Assert.True(Vector2.Distance(s.Arena.Tank(s.Near.TankId).Position, BotArena.Base0) > 60f, "the medium went back to its lane");

        // The invader drives out of the circle and stops 40 m from its centre, in sight: nothing rises, but it is near.
        s = Besiege();
        UntilTheBaseFalls(s.Arena);
        UntilBothAreHome(s, s.Td, s.Near);
        Invade(s.Arena, s.Invader, BotArena.Base0 + new Vector2(0, 40));
        int score = s.Arena.Rules.Capture!.TicksOn(0);
        s.Arena.Run(BotDirector.ReleaseTicks + 2 * Tps);
        Assert.Equal(score, s.Arena.Rules.Capture.TicksOn(0));
        Assert.True(s.Arena.Vision.IsVisible(s.Invader, 0));
        Assert.True(s.Td.FallingBack && s.Near.FallingBack, "released with an enemy 40 m from the base");
    }

    [Fact]
    public void Below_30_percent_a_bot_falls_back_to_the_nearest_ally_or_its_base()
    {
        var arena = Standard();
        Bot bot = arena.Bot(TankCatalog.Medium, 0, seed: 1, new Vector2(250, 200), BotArena.West);
        var allyAt = new Vector2(170, 200);
        int ally = arena.Idle(TankCatalog.Medium, 0, allyAt, BotArena.East);
        HiddenEnemy(arena);
        arena.Sim.TankRef(bot.TankId).Health = TankCatalog.Medium.MaxHealth / 4;
        arena.Tick();
        Assert.True(bot.Retreating);
        Assert.Equal(BotRole.Attack, bot.Role);
        Assert.Equal(allyAt, bot.Goal);   // 80 m off, the base 205 m
        float before = Vector2.Distance(arena.Tank(bot.TankId).Position, allyAt);
        arena.Run(5 * Tps);
        float after = Vector2.Distance(arena.Tank(bot.TankId).Position, allyAt);
        Assert.True(before - after >= 10f, $"{before:F1} m → {after:F1} m from the ally in 5 s");
        arena.Run(15 * Tps);
        TankState t = arena.Tank(bot.TankId);
        _output.WriteLine($"with an ally: {before:F1} m → {after:F1} m in 5 s, then {Vector2.Distance(t.Position, allyAt):F1} m at {t.Speed:F2} m/s");
        Assert.True(Vector2.Distance(t.Position, allyAt) <= BotDirector.RetreatArrive, $"{Vector2.Distance(t.Position, allyAt):F1} m from the ally");
        Assert.True(MathF.Abs(t.Speed) < 0.3f);
        Assert.True(bot.Retreating);
        Assert.Equal(BotActivity.Holding, bot.Activity);
        Assert.Equal(ally, arena.Tank(ally).Id);

        // No team mate: back to the base.
        arena = Standard();
        bot = arena.Bot(TankCatalog.Medium, 0, seed: 1, new Vector2(250, 200), BotArena.West);
        HiddenEnemy(arena);
        arena.Sim.TankRef(bot.TankId).Health = TankCatalog.Medium.MaxHealth / 4;
        arena.Tick();
        Assert.True(bot.Retreating);
        Assert.Equal(BotArena.Base0, bot.Goal);

        // Capturing the enemy base, or at home, it stays.
        foreach (Vector2 at in new[] { BotArena.Base1, BotArena.Base0 })
        {
            arena = Standard();
            bot = arena.Bot(TankCatalog.Medium, 0, seed: 1, at, BotArena.East);
            HiddenEnemy(arena);
            arena.Sim.TankRef(bot.TankId).Health = TankCatalog.Medium.MaxHealth / 4;
            arena.Run(2 * Tps);
            Assert.False(bot.Retreating, $"retreating from {at}");
        }
    }

    [Fact]
    public void A_hurt_bot_falls_back_only_to_an_ally_nearer_home_or_holding_else_to_its_base()
    {
        // A team mate 60 m further forward is not followed.
        var arena = Standard();
        Bot bot = arena.Bot(TankCatalog.Medium, 0, seed: 1, new Vector2(200, 200), BotArena.West);
        arena.Idle(TankCatalog.Medium, 0, new Vector2(260, 200), BotArena.East);
        HiddenEnemy(arena);
        arena.Sim.TankRef(bot.TankId).Health = TankCatalog.Medium.MaxHealth / 4;
        arena.Tick();
        Assert.True(bot.Retreating);
        Assert.Equal(BotArena.Base0, bot.Goal);

        // Nor one nearer home by less than RetreatArrive, however near it is.
        arena = Standard();
        bot = arena.Bot(TankCatalog.Medium, 0, seed: 1, new Vector2(200, 200), BotArena.West);
        arena.Idle(TankCatalog.Medium, 0, new Vector2(200 - BotDirector.RetreatArrive + 1, 210), BotArena.East);
        HiddenEnemy(arena);
        arena.Sim.TankRef(bot.TankId).Health = TankCatalog.Medium.MaxHealth / 4;
        arena.Tick();
        Assert.Equal(BotArena.Base0, bot.Goal);

        // A bot holding is a place to rally to, nearer home or not: here a tank destroyer on its cover point, 20 m
        // to the side of the hurt one (both about as far from home).
        arena = Standard();
        Bot td = arena.Bot(TankCatalog.Td, 0, seed: 1);
        bot = arena.Bot(TankCatalog.Medium, 0, seed: 2);
        HiddenEnemy(arena);
        arena.Director.AssignRoles();
        Vector2 cover = arena.Director.CoverOf(0)[td.Cover];
        arena.Place(td.TankId, cover, BotArena.East);
        arena.Run(5 * Tps);
        Assert.Equal(BotActivity.Holding, td.Activity);
        Vector2 away = Vector2.Normalize(cover - BotArena.Base0), side = new(-away.Y, away.X);
        Vector2 at = cover + side * 20f;
        Assert.True(Vector2.Distance(at, BotArena.Base0) - Vector2.Distance(arena.Tank(td.TankId).Position, BotArena.Base0) < BotDirector.RetreatArrive);
        arena.Place(bot.TankId, at, BotArena.West);
        arena.Sim.TankRef(bot.TankId).Health = TankCatalog.Medium.MaxHealth / 4;
        arena.Tick();
        Assert.True(bot.Retreating);
        Assert.Equal(arena.Tank(td.TankId).Position, bot.Goal);
    }

    public static TheoryData<string> Maps() => new() { "riverland", "sandbox" };

    [Theory]
    [MemberData(nameof(Maps))]
    public void Capture_and_fall_back_points_stay_well_inside_the_circle(string mapId)
    {
        // The riverland has rocks on both bases' rims: a draw near the edge snapped to an open cell may land farther out.
        MapDefinition map = MapCatalog.Get(mapId);
        var arena = new BotArena(BattleMode.Standard, map);
        var bots = new Bot[8];
        for (int k = 0; k < bots.Length; k++) bots[k] = arena.Bot(TankCatalog.Medium, (byte)(k % 2), (uint)(k * 7919 + 1));
        arena.Director.AssignRoles();
        var taken = new List<Vector2>();
        float farthest = 0f;
        foreach (TeamBase b in map.Bases)
        {
            float reach = b.Radius - BotDirector.CaptureMargin;
            for (int n = 0; n < 400; n++)
            {
                if (taken.Count == 4) taken.Clear();
                Vector2 p = arena.Director.DrawPoint(bots[n % bots.Length], b, taken.ToArray());
                taken.Add(p);
                float d = Vector2.Distance(p, b.Centre);
                farthest = MathF.Max(farthest, d / reach);
                Assert.True(d <= reach + 1e-3f, $"{p} is {d:F1} m from base {b.Team}'s centre, beyond {reach} m");
                Assert.True(arena.Director.Grid.IsOpenAt(p), $"{p} is closed");
            }
        }
        _output.WriteLine($"{mapId}: farthest draw at {farthest:P0} of the reach");
    }

    [Fact]
    public void A_bot_added_once_the_roles_are_out_gets_one_by_the_same_rules()
    {
        var arena = Standard();
        Bot td = arena.Bot(TankCatalog.Td, 0, seed: 1);
        Bot medium = arena.Bot(TankCatalog.Medium, 0, seed: 2);
        Bot light = arena.Bot(TankCatalog.Light, 0, seed: 3);
        HiddenEnemy(arena);
        arena.Director.AssignRoles();
        AssertDefend(td);
        AssertAttack(light, 0);
        AssertAttack(medium, 1);

        AssertAttack(arena.Bot(TankCatalog.Medium, 0, seed: 4), 2);   // the lane with the fewest attackers
        Bot second = arena.Bot(TankCatalog.Td, 0, seed: 5);            // a second tank destroyer stays at home too …
        AssertDefend(second);
        Assert.Equal((td.Cover + arena.Director.CoverOf(0).Count / 2) % arena.Director.CoverOf(0).Count, second.Cover);
        AssertAttack(arena.Bot(TankCatalog.Td, 0, seed: 6), 0);        // … a third attacks
        AssertAttack(arena.Bot(TankCatalog.Light, 0, seed: 7), arena.Director.FlankLane);
        Assert.Equal(7, arena.Director.Bots.Count);
        Assert.Contains("team 0 — 5 attack", arena.Director.Describe());

        // A team of three without a defender keeps the newcomer at home.
        arena = Standard();
        arena.Bot(TankCatalog.Medium, 0, seed: 1);
        arena.Bot(TankCatalog.Light, 0, seed: 2);
        HiddenEnemy(arena);
        arena.Director.AssignRoles();
        AssertDefend(arena.Bot(TankCatalog.Medium, 0, seed: 3));

        // Outside a standard battle it wanders, and drives off.
        var battle = new BotArena(BattleMode.TeamBattle, BotArena.FlatMap(props: CornerBush));
        battle.Bot(TankCatalog.Medium, 0, seed: 1);
        HiddenEnemy(battle);
        battle.Run(Tps);
        Bot late = battle.Bot(TankCatalog.Medium, 0, seed: 2);
        Assert.Equal(BotRole.Wander, late.Role);
        Vector2 from = battle.Tank(late.TankId).Position;
        battle.Run(10 * Tps);
        Assert.True(Vector2.Distance(battle.Tank(late.TankId).Position, from) > 10f, "the late bot stood still");
    }

    [Fact]
    public void A_map_without_bot_data_gets_one_direct_lane_and_cover_round_its_base()
    {
        var arena = Standard(botData: false);
        Bot bot = arena.Bot(TankCatalog.Medium, 0, seed: 1);
        HiddenEnemy(arena);
        arena.Director.AssignRoles();
        Assert.Equal(1, arena.Director.LaneCount);
        Assert.Equal(0, arena.Director.FlankLane);
        foreach (byte team in new byte[] { 0, 1 })
        {
            Vector2 own = team == 0 ? BotArena.Base0 : BotArena.Base1, other = team == 0 ? BotArena.Base1 : BotArena.Base0;
            IReadOnlyList<Vector2> cover = arena.Director.CoverOf(team);
            Assert.Equal(4, cover.Count);
            foreach (Vector2 p in cover)
            {
                Assert.True(arena.Director.Grid.IsOpenAt(p), $"{p} is closed");
                Assert.InRange(Vector2.Distance(p, own), BotArena.BaseRadius + 15f - 3f, BotArena.BaseRadius + 15f + 3f);
                Assert.True(Vector2.Dot(p - own, other - own) > 0, $"{p} lies behind team {team}'s base");
            }
        }
        AssertAttack(bot, 0);
        float stray = 0f;
        int arrived = -1;
        arena.Run(60 * Tps, tick =>
        {
            Vector2 p = arena.Tank(bot.TankId).Position;
            stray = MathF.Max(stray, MathF.Abs(p.Y - 200f));
            if (arrived < 0 && InCircle(p, BotArena.Base1)) arrived = tick;
        });
        _output.WriteLine($"in the enemy base after {arrived / (float)Tps:F1} s, {stray:F1} m off the line at most");
        Assert.True(arrived > 0, "never got to the enemy base");
        Assert.True(stray < 30f, $"{stray:F1} m off the straight way");
        Assert.True(InCircle(bot.Goal, BotArena.Base1));
    }

    [Fact]
    public void Roles_and_falling_back_allocate_nothing()
    {
        Allocation.AssertNone(() =>
        {
            Siege s = Besiege(invaderAt: Corner);
            s.Arena.Recording = false;
            s.Arena.Run(300);
            Allocation.Window window = Allocation.Start();
            Invade(s.Arena, s.Invader, BotArena.Base0);
            for (int k = 0; k < 150; k++) s.Arena.Tick();
            Invade(s.Arena, s.Invader, Corner);
            for (int k = 0; k < 450; k++) s.Arena.Tick();
            Allocation.Measured measured = window.Stop();
            Assert.Equal(1, s.Arena.Director.FallBacks(0));
            Assert.False(s.Td.FallingBack || s.Near.FallingBack || s.Far.FallingBack, "not released in the window");
            return measured;
        });
    }

    // ---- a team with no attacker left sends its defenders forward (5C stalemate fix) ----

    /// <summary>Team 0's corner, away from everything: where a team-0 player who never moves is parked.</summary>
    private static readonly Vector2 HomeCorner = new(20, 385);

    /// <summary>Two tank destroyers a side: all four stay at home at the start (two defenders at most, both theirs).</summary>
    [Fact]
    public void The_summary_leaves_out_a_team_without_bots()
    {
        var arena = Standard();
        arena.Bot(TankCatalog.Medium, 0, 1);
        arena.Bot(TankCatalog.Heavy, 0, 2);
        HiddenEnemy(arena);
        arena.Director.AssignRoles();

        string summary = arena.Director.Summary();
        _output.WriteLine(summary);
        Assert.StartsWith("team 0 fell back 0 times; ", summary);
        Assert.DoesNotContain("team 1", summary);
        Assert.DoesNotContain("team 1", arena.Director.Describe());
    }

    private static BotArena DefendersOnly()
    {
        var arena = Standard();
        for (int k = 0; k < 4; k++) arena.Bot(TankCatalog.Td, (byte)(k / 2), (uint)k * 7919 + 11);
        arena.Director.AssignRoles();
        return arena;
    }

    [Fact]
    public void Two_teams_of_defenders_both_go_on_the_attack_and_settle_the_match()
    {
        BotArena x = DefendersOnly(), y = DefendersOnly();
        Assert.Equal("team 0 — no attackers, 2 defend; team 1 — no attackers, 2 defend", x.Director.Describe());
        x.Tick();
        y.Tick();
        foreach (Bot bot in x.Director.Bots)
        {
            Assert.Equal(BotRole.Attack, bot.Role);
            Assert.True(bot.SentForward);
        }
        Assert.Equal("team 0 — 2 attack (north 1, middle 1, south 0), 0 defend; team 1 — 2 attack (north 1, middle 1, south 0), 0 defend",
            x.Director.Describe());

        const int limit = 120 * Tps;   // ten minutes on the clock; two tank destroyers a side settle it in about half a minute
        for (int k = 0; k < limit && x.Rules.Phase == MatchPhase.Playing; k++)
        {
            x.Tick();
            y.Tick();
            for (int i = 0; i < x.Sim.TankCount; i++)
            {
                TankState a = x.Sim.Tanks[i], b = y.Sim.Tanks[i];
                Assert.Equal((a.Position, a.Yaw, a.Health, a.IsDestroyed), (b.Position, b.Yaw, b.Health, b.IsDestroyed));
            }
        }
        Assert.Equal(x.Events, y.Events);   // the same seeds send the same bots forward the same way
        _output.WriteLine($"{x.Rules.EndReason} at {x.Rules.EndedAt / (float)Tps:F1} s, winner {x.Rules.WinnerTeam}; {x.Director.Summary()}");
        foreach (Bot bot in x.Director.Bots)
            _output.WriteLine($"tank {bot.TankId}: {bot.Role} lane {bot.Lane}, {bot.Activity}, longest stuck {x.LongestStuckSeconds(bot.TankId):F1} s");
        Assert.Equal(MatchPhase.Ended, x.Rules.Phase);
        Assert.NotEqual(MatchEndReason.TimeUp, x.Rules.EndReason);
        Assert.All(x.Director.Bots, bot => Assert.True(x.LongestStuckSeconds(bot.TankId) < 10f, $"tank {bot.TankId} stuck for {x.LongestStuckSeconds(bot.TankId):F1} s"));
    }

    [Fact]
    public void A_team_whose_last_attacker_is_knocked_out_sends_its_defenders_forward()
    {
        var arena = Standard();
        Bot td = arena.Bot(TankCatalog.Td, 0, seed: 1);
        Bot medium = arena.Bot(TankCatalog.Medium, 0, seed: 2);
        HiddenEnemy(arena);
        arena.Run(5 * Tps);
        AssertDefend(td);
        Assert.False(td.SentForward);
        AssertAttack(medium, 0);

        arena.Sim.TankRef(medium.TankId).IsDestroyed = true;
        arena.Tick();
        AssertAttack(td, 0);   // the lanes by the rules at the start: the fewest attackers (none live), the lower index
        Assert.True(td.SentForward);
        Assert.Equal(BotArena.Bots.ViaFor(0, 0)[0], td.Goal);   // its lane from its cover point: no via point passed yet
        arena.Run(20 * Tps);
        float out_ = Vector2.Distance(arena.Tank(td.TankId).Position, BotArena.Base0);
        _output.WriteLine($"20 s later the tank destroyer is {out_:F1} m from home, {td.Activity}, heading for {td.Goal}");
        Assert.True(out_ > 80f, $"only {out_:F1} m from home");
        Assert.Equal(BotRole.Attack, td.Role);
    }

    [Fact]
    public void A_team_with_a_live_player_keeps_its_defenders_at_home_until_the_player_is_knocked_out()
    {
        var arena = Standard();
        Bot td = arena.Bot(TankCatalog.Td, 0, seed: 1);
        int player = arena.Idle(TankCatalog.Medium, 0, HomeCorner);   // a player counts as an attacker, wherever it is
        HiddenEnemy(arena);
        arena.Run(30 * Tps);
        AssertDefend(td);
        Assert.False(td.SentForward);
        Assert.Equal(BotActivity.Holding, td.Activity);
        Assert.True(arena.AtRoleEnd(td));

        arena.Sim.TankRef(player).IsDestroyed = true;
        arena.Tick();
        AssertAttack(td, 0);
        Assert.True(td.SentForward);
    }

    [Fact]
    public void A_bot_that_retreated_joins_the_attack_and_does_not_retreat_again()
    {
        // Team 0: a tank destroyer at home, a medium badly hurt 160 m out, another medium attacking down its lane.
        var arena = Standard();
        Bot td = arena.Bot(TankCatalog.Td, 0, seed: 1);
        Bot hurt = arena.Bot(TankCatalog.Medium, 0, seed: 2, new Vector2(250, 200), BotArena.West);
        Bot last = arena.Bot(TankCatalog.Medium, 0, seed: 3);
        HiddenEnemy(arena);
        arena.Sim.TankRef(hurt.TankId).Health = TankCatalog.Medium.MaxHealth / 4;
        int home = -1;
        for (int k = 0; k < 60 * Tps && home < 0; k++)
        {
            arena.Tick();
            if (hurt.Retreating && hurt.Activity == BotActivity.Holding) home = arena.Sim.Tick;
        }
        Assert.True(home > 0, $"the hurt medium never got back: at {arena.Tank(hurt.TankId).Position}, {hurt.Activity}");
        Vector2 heldAt = arena.Tank(hurt.TankId).Position;
        Assert.Equal(BotRole.Attack, last.Role);
        Assert.False(last.Retreating);
        AssertDefend(td);
        Assert.False(hurt.SentForward);

        arena.Sim.TankRef(last.TankId).IsDestroyed = true;
        arena.Tick();
        Assert.False(hurt.Retreating);
        Assert.True(hurt.SentForward);
        Assert.Equal(BotRole.Attack, hurt.Role);
        Assert.Equal(BotRole.Attack, td.Role);
        Assert.NotEqual(hurt.Lane, td.Lane);   // the two spread over the lanes as at the start

        float before = Vector2.Distance(heldAt, BotArena.Base1);
        arena.Run(30 * Tps, _ => Assert.False(hurt.Retreating, "it retreated again"));
        TankState t = arena.Tank(hurt.TankId);
        float after = Vector2.Distance(t.Position, BotArena.Base1);
        _output.WriteLine($"held at {heldAt} from {home / (float)Tps:F1} s; 30 s after it was sent forward {before:F1} → {after:F1} m from the enemy base, " +
                          $"health {t.Health}/{t.Spec.MaxHealth}, {hurt.Activity}");
        Assert.True(t.Health < BotDirector.RetreatHealth * t.Spec.MaxHealth);
        Assert.True(before - after > 60f, $"{before:F1} → {after:F1} m from the enemy base");
    }

    [Fact]
    public void Sending_defenders_forward_allocates_nothing()
    {
        Allocation.AssertNone(() =>
        {
            var arena = Standard();
            Bot td = arena.Bot(TankCatalog.Td, 0, seed: 1);
            Bot medium = arena.Bot(TankCatalog.Medium, 0, seed: 2);
            Bot hurt = arena.Bot(TankCatalog.Medium, 0, seed: 3, new Vector2(250, 200), BotArena.West);
            HiddenEnemy(arena);
            arena.Recording = false;
            arena.Sim.TankRef(hurt.TankId).Health = TankCatalog.Medium.MaxHealth / 4;
            arena.Run(300);
            Allocation.Window window = Allocation.Start();
            arena.Sim.TankRef(medium.TankId).IsDestroyed = true;
            for (int k = 0; k < 600; k++) arena.Tick();
            Allocation.Measured measured = window.Stop();
            Assert.True(td.SentForward && hurt.SentForward, "not sent forward in the window");
            Assert.False(hurt.Retreating);
            return measured;
        });
    }

    private static BotArena FourAside(uint seed)
    {
        var arena = Standard();
        TankSpec[] specs = { TankCatalog.Light, TankCatalog.Medium, TankCatalog.Heavy, TankCatalog.Td };
        for (int k = 0; k < 8; k++) arena.Bot(specs[k % 4], (byte)(k % 2), seed + (uint)k * 7919);
        return arena;
    }

    [Fact]
    public void The_same_seeds_play_the_same_standard_battle()
    {
        BotArena x = FourAside(5), y = FourAside(5);
        for (int k = 0; k < 60 * Tps; k++)
        {
            x.Tick();
            y.Tick();
            for (int i = 0; i < x.Sim.TankCount; i++)
            {
                TankState a = x.Sim.Tanks[i], b = y.Sim.Tanks[i];
                Assert.Equal((a.Position, a.Yaw, a.Health), (b.Position, b.Yaw, b.Health));
            }
        }
        Assert.Equal(x.Events, y.Events);
        Assert.Equal(x.Director.Describe(), y.Director.Describe());
        for (int b = 0; b < x.Director.Bots.Count; b++)
            Assert.Equal((x.Director.Bots[b].Role, x.Director.Bots[b].Lane, x.Director.Bots[b].Cover),
                         (y.Director.Bots[b].Role, y.Director.Bots[b].Lane, y.Director.Bots[b].Cover));
        _output.WriteLine($"bots: {x.Director.Describe()}");
        _output.WriteLine($"bots: {x.Director.Summary()}");
        Assert.StartsWith("team 0 — ", x.Director.Describe());
        Assert.Contains("fell back", x.Director.Summary());

        BotArena z = FourAside(6);
        z.Run(60 * Tps);
        bool differs = false;
        for (int i = 0; i < x.Sim.TankCount; i++) differs |= x.Sim.Tanks[i].Position != z.Sim.Tanks[i].Position;
        Assert.True(differs, "another seed plays another way");
    }
}
