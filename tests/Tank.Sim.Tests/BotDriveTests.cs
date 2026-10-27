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
/// Bots driving and fighting under the director (stage 5 design §4.1–4.4): searched paths, asked for again every five
/// seconds, backing off and asking again when stuck, wandering between waypoints outside a standard battle, and stopping
/// before they fire at what their team sees.
/// </summary>
public sealed class BotDriveTests
{
    private const int Tps = SimConstants.TickRate;

    private readonly ITestOutputHelper _output;

    public BotDriveTests(ITestOutputHelper output) => _output = output;

    private static int ShotsBy(BotArena arena, int tankId) => arena.Events.Count(e => e.Type == GameEventType.ShellFired && e.TankId == tankId);

    [Fact]
    public void A_wandering_bot_drives_from_waypoint_to_waypoint_along_its_paths()
    {
        var arena = new BotArena(BattleMode.TeamBattle, MapCatalog.Sandbox);
        Bot bot = arena.Bot(TankCatalog.Medium, 0, seed: 3);
        arena.Idle(TankCatalog.Medium, 1, new Vector2(385, 15), BotArena.West);   // the far corner, out of the way
        var reached = new HashSet<Vector2>();
        arena.Run(180 * Tps, _ =>
        {
            if (Vector2.Distance(arena.Tank(bot.TankId).Position, bot.Goal) <= BotDirector.WaypointRadius) reached.Add(bot.Goal);
        });
        _output.WriteLine($"waypoints reached {reached.Count}, requests {bot.PathRequests}, stucks {bot.Stucks}, longest stuck {arena.LongestStuckSeconds(bot.TankId):F1} s");
        Assert.True(reached.Count >= 3, $"only {reached.Count} waypoints reached in three minutes");
        Assert.All(reached, w => Assert.Contains(w, MapCatalog.Sandbox.Waypoints));
        Assert.True(arena.LongestStuckSeconds(bot.TankId) < 10f, $"stuck for {arena.LongestStuckSeconds(bot.TankId):F1} s");
        Assert.True(bot.PathRequests >= 3, $"{bot.PathRequests} path requests");
        Assert.Equal(BotRole.Wander, bot.Role);
    }

    [Fact]
    public void It_stops_before_it_fires_at_a_visible_enemy_and_hits_it()
    {
        var arena = new BotArena();
        Bot bot = arena.Bot(TankCatalog.Medium, 0, seed: 1, new Vector2(150, 200), BotArena.East);
        int enemy = arena.Idle(TankCatalog.Heavy, 1, new Vector2(300, 200), BotArena.West);
        float? speedAtFirstShot = null;
        arena.Run(10 * Tps, _ =>
        {
            if (speedAtFirstShot is null && arena.Sim.Events.ToArray().Any(e => e.Type == GameEventType.ShellFired && e.TankId == bot.TankId))
                speedAtFirstShot = arena.Tank(bot.TankId).Speed;
        });
        Assert.NotNull(speedAtFirstShot);
        Assert.True(MathF.Abs(speedAtFirstShot.Value) < BotDirector.FireSpeed, $"fired at {speedAtFirstShot:F2} m/s");
        Assert.Contains(arena.Events, e => e.Type == GameEventType.TankHit && e.TankId == bot.TankId && e.TargetTankId == enemy);
        Assert.Equal(enemy, bot.TargetId);
        Assert.Equal(BotActivity.Engaging, bot.Activity);
        Assert.True(Vector2.Distance(arena.Tank(bot.TankId).Position, new Vector2(150, 200)) < 5f, "it stopped to shoot");
    }

    [Fact]
    public void It_only_shoots_what_its_team_can_see()
    {
        // A light tank standing in a bush with another just west of it is hidden from the west (a team mate 60 m off and
        // the bot 150 m off); 24 m out of them the team mate sees it, and the bot engages it on its team's word.
        var bushes = new Prop[]
        {
            new(1, PropKind.Bush, new Obb2(new Vector2(300, 200), new Vector2(4, 4), 0f), -0.2f, 4.5f),
            new(2, PropKind.Bush, new Obb2(new Vector2(295, 200), new Vector2(4, 4), 0f), -0.2f, 4.5f),
        };
        var arena = new BotArena(map: BotArena.FlatMap(props: bushes, waypoints: new Vector2[] { new(150, 40), new(150, 360) }));
        Bot bot = arena.Bot(TankCatalog.Medium, 0, seed: 7, new Vector2(150, 200), BotArena.North);
        arena.Idle(TankCatalog.Medium, 0, new Vector2(240, 200), BotArena.East);
        int hidden = arena.Idle(TankCatalog.Light, 1, new Vector2(300, 200), BotArena.West);
        arena.Run(3 * Tps, _ => Assert.Equal(0, bot.TargetId));
        Assert.False(arena.Vision.IsVisible(hidden, 0));
        Assert.Equal(0, ShotsBy(arena, bot.TankId));
        Assert.Equal(BotActivity.Driving, bot.Activity);   // looking for its waypoint meanwhile

        arena.Sim.TankRef(hidden).Position = new Vector2(300, 224);
        arena.Run(8 * Tps);
        Assert.True(arena.Vision.IsVisible(hidden, 0));
        Assert.Equal(hidden, bot.TargetId);
        Assert.True(ShotsBy(arena, bot.TankId) > 0, "it fired");
    }

    [Fact]
    public void A_wreck_holds_still_and_carries_on_once_revived()
    {
        var arena = new BotArena();
        Bot bot = arena.Bot(TankCatalog.Medium, 0, seed: 2, new Vector2(150, 200), BotArena.East);
        int enemy = arena.Idle(TankCatalog.Heavy, 1, new Vector2(270, 200), BotArena.West);
        arena.Run(8 * Tps);
        Assert.Equal(enemy, bot.TargetId);
        Assert.True(ShotsBy(arena, bot.TankId) > 0, "in the middle of a fight …");

        arena.Sim.TankRef(bot.TankId).IsDestroyed = true;   // … it is knocked out
        arena.Sim.TankRef(bot.TankId).Health = 0;
        arena.Director.Update();   // as the server does every tick before the commands
        InputCmd cmd = arena.Director.Command(bot.TankId, arena.Sim.Tick + 1);
        Assert.Equal((0, 0, false), (cmd.Throttle, cmd.Steer, cmd.Fire));
        Assert.Equal(0, bot.TargetId);
        int shots = ShotsBy(arena, bot.TankId);
        Vector2 wreck = arena.Tank(bot.TankId).Position;
        arena.Run(2 * Tps);
        Assert.Equal(shots, ShotsBy(arena, bot.TankId));
        Assert.Equal(wreck, arena.Tank(bot.TankId).Position);

        arena.Sim.RemoveTank(enemy);   // nothing left to fight: once back, it drives
        int requests = bot.PathRequests;
        arena.Sim.Revive(bot.TankId);
        arena.Run(6 * Tps);
        Assert.True(bot.PathRequests > requests, "it asked for a path again");
        Assert.True(Vector2.Distance(arena.Tank(bot.TankId).Position, wreck) > 5f, "it drove off");
    }

    /// <summary>A bot 380 or 420 m from an enemy its team mate sees from 40 m, its waypoints leading away from it.</summary>
    private static BotArena Spotted(float distance, out Bot bot, out int enemy)
    {
        var e = new Vector2(360, 360);
        Vector2 away = Vector2.Normalize(new Vector2(-1, -1));
        var arena = new BotArena(map: BotArena.FlatMap(waypoints: new Vector2[] { new(15, 15), new(15, 40) }));
        bot = arena.Bot(TankCatalog.Medium, 0, seed: 4, e + away * distance, Angles.YawOf(away));
        arena.Idle(TankCatalog.Medium, 0, e + new Vector2(-40, 0), BotArena.East);
        enemy = arena.Idle(TankCatalog.Heavy, 1, e, BotArena.West);
        return arena;
    }

    [Fact]
    public void It_reaches_400_m_and_no_further()
    {
        BotArena near = Spotted(380f, out Bot inRange, out int enemy);
        near.Run(2 * Tps);
        Assert.True(near.Vision.IsVisible(enemy, 0), "the team mate sees it");
        Assert.Equal(enemy, inRange.TargetId);
        Assert.Equal(BotActivity.Engaging, inRange.Activity);

        BotArena far = Spotted(420f, out Bot outOfRange, out int beyond);
        Vector2 start = far.Tank(outOfRange.TankId).Position;
        far.Run(4 * Tps, _ => Assert.Equal(0, outOfRange.TargetId));
        Assert.True(far.Vision.IsVisible(beyond, 0), "the team mate sees it");
        Assert.Equal(0, ShotsBy(far, outOfRange.TankId));
        Assert.Equal(BotActivity.Driving, outOfRange.Activity);
        Assert.True(Vector2.Distance(far.Tank(outOfRange.TankId).Position, start) > 3f, "it drove on");
    }

    /// <summary>
    /// Two rows of rock 100 m long make a channel 10 m wide (z 196–206); the grid leaves one row of cells open along it
    /// (z 200–204). A heavy of the bot's own team stands across it, shifted 1.2 m along the channel's width every
    /// <see cref="ShuffleTicks"/>: never still for 3 s, it is no obstacle the searches know of. The bot wanders between
    /// the channel's ends (seed 5 takes the east one first).
    /// </summary>
    private sealed class Shuffling
    {
        public const int ShuffleTicks = 40;
        private static readonly Prop[] Rocks =
        {
            new(1, PropKind.Rock, new Obb2(new Vector2(200, 193), new Vector2(50, 3), 0f), -1f, 4f),
            new(2, PropKind.Rock, new Obb2(new Vector2(200, 209), new Vector2(50, 3), 0f), -1f, 4f),
            new(3, PropKind.Bush, new Obb2(new Vector2(385, 15), new Vector2(4, 4), 0f), -0.2f, 4.5f),
        };

        public static readonly Vector2 West = new(130, 202), East = new(270, 202);

        public Shuffling()
        {
            Arena = new BotArena(map: BotArena.FlatMap(props: Rocks, waypoints: new[] { West, East }));
            Bot = Arena.Bot(TankCatalog.Medium, 0, seed: 5, new Vector2(110, 202), BotArena.East);
            Heavy = Arena.Idle(TankCatalog.Heavy, 0, new Vector2(200, 200.4f), BotArena.North);
            Enemy = Arena.Idle(TankCatalog.Heavy, 1, new Vector2(385, 15), BotArena.West);   // hidden in the far corner's bush
        }

        public BotArena Arena { get; }
        public Bot Bot { get; }
        public int Heavy { get; }
        public int Enemy { get; }

        public void Tick()
        {
            if (Arena.Sim.Tick % ShuffleTicks == 0)
                Arena.Place(Heavy, new Vector2(200, Arena.Sim.Tick / ShuffleTicks % 2 == 0 ? 200.4f : 201.6f), BotArena.North);
            Arena.Tick();
        }

        public void Run(int ticks, Action<int>? each = null)
        {
            for (int k = 0; k < ticks; k++)
            {
                Tick();
                each?.Invoke(Arena.Sim.Tick);
            }
        }

        /// <summary>Plays until the bot's first stuck (at most 40 s); returns its tick, or −1.</summary>
        public int UntilStuck()
        {
            for (int k = 0; k < 40 * Tps; k++)
            {
                Tick();
                if (Bot.Stucks > 0) return Arena.Sim.Tick;
            }
            return -1;
        }
    }

    [Fact]
    public void Stuck_behind_a_parked_tank_it_backs_off_then_tries_again()
    {
        var first = new Shuffling();
        int stuckAt = first.UntilStuck();
        Assert.True(stuckAt > 0, "it never got stuck");
        Assert.Equal(Shuffling.East, first.Bot.Goal);
        float measured = first.Arena.LongestStuckSeconds(first.Bot.TankId);
        _output.WriteLine($"first stuck at tick {stuckAt}, measured stuck {measured:F1} s");
        Assert.True(measured >= 2.5f, $"nose against the heavy, yet measured stuck only {measured:F1} s");   // the measure still catches it

        // Replay to the same moment, then watch the backing off.
        var s = new Shuffling();
        Bot bot = s.Bot;
        s.Run(stuckAt);
        Assert.Equal(1, bot.Stucks);
        float x = s.Arena.Tank(bot.TankId).Position.X;
        Assert.InRange(x, 185f, 200f);   // nose against the heavy's flank
        int requests = bot.PathRequests;
        bool backing = false;
        sbyte firstSteer = s.Arena.LastInput(bot.TankId).Steer;
        Assert.NotEqual(0, firstSteer);   // backing off with the hull turning, so the next try comes at it another way
        s.Run(BotDirector.ReverseTicks - 1, _ =>
        {
            Assert.Equal(BotActivity.Reversing, bot.Activity);
            Assert.Equal(firstSteer, s.Arena.LastInput(bot.TankId).Steer);
            backing |= s.Arena.Tank(bot.TankId).Speed < 0f;
        });
        Assert.True(backing, "it backed off");
        Assert.Equal(requests, bot.PathRequests);
        s.Run(1);
        Assert.Equal(requests + 1, bot.PathRequests);   // then asked for a path again
        Assert.NotEqual(BotActivity.Reversing, bot.Activity);

        int gaveUp = -1, stucks = 0;
        var steers = new List<sbyte> { firstSteer };
        s.Run(60 * Tps, tick =>
        {
            if (bot.Activity == BotActivity.Reversing && bot.Stucks > steers.Count) steers.Add(s.Arena.LastInput(bot.TankId).Steer);
            if (gaveUp < 0 && bot.Goal != Shuffling.East) (gaveUp, stucks) = (tick, bot.Stucks);
        });
        _output.WriteLine($"gave the leg up at tick {gaveUp}; backed off steering {string.Join(", ", steers)}");
        Assert.True(gaveUp > 0, "it never gave the leg up");
        Assert.Equal(BotDirector.StuckRetries, stucks);   // on its third time stuck
        Assert.Equal(Shuffling.West, bot.Goal);
        Assert.Equal(new[] { firstSteer, (sbyte)-firstSteer, firstSteer }, steers.Take(3));   // each way in turn
    }

    [Fact]
    public void A_short_engagement_does_not_restart_the_stuck_watch()
    {
        int stuckAt = new Shuffling().UntilStuck();
        Assert.True(stuckAt > 0, "it never got stuck");

        // The same, but an enemy shows itself behind the bot in the channel for a second before the stuck check.
        var s = new Shuffling();
        s.Run(stuckAt - 60);
        Assert.Equal(0, s.Bot.Stucks);
        s.Arena.Place(s.Enemy, new Vector2(130, 202), BotArena.East);
        bool engaged = false;
        s.Run(30, _ => engaged |= s.Bot.Activity == BotActivity.Engaging);
        s.Arena.Place(s.Enemy, new Vector2(385, 15), BotArena.West);
        int again = -1;
        s.Run(5 * Tps, tick =>
        {
            if (again < 0 && s.Bot.Stucks > 0) again = tick;
        });
        _output.WriteLine($"stuck at tick {stuckAt} without the enemy, at {again} with it");
        Assert.True(engaged, "it never engaged the enemy");
        Assert.InRange(again, stuckAt + 1, stuckAt + 45);   // the watch held still while it fired, then carried on
    }

    [Fact]
    public void Paths_are_asked_for_again_every_5_s_while_driving()
    {
        var arena = new BotArena(map: BotArena.FlatMap(waypoints: new Vector2[] { new(370, 370), new(30, 370) }));
        Bot bot = arena.Bot(TankCatalog.Medium, 0, seed: 1, new Vector2(30, 20), BotArena.South);
        var asked = new List<int>();
        int last = 0;
        arena.Run(BotDirector.ReplanTicks * 2 + 20, tick =>
        {
            if (bot.PathRequests != last) asked.Add(tick);
            last = bot.PathRequests;
            if (tick > 10) Assert.Equal(BotActivity.Driving, bot.Activity);
        });
        Assert.Equal(3, asked.Count);
        Assert.InRange(asked[0], 0, 2);
        Assert.InRange(asked[1], BotDirector.ReplanTicks - 2, BotDirector.ReplanTicks + 2);
        Assert.InRange(asked[2], 2 * BotDirector.ReplanTicks - 2, 2 * BotDirector.ReplanTicks + 2);
    }

    [Fact]
    public void A_bot_whose_tank_left_the_battle_holds()
    {
        var arena = new BotArena();
        Bot bot = arena.Bot(TankCatalog.Medium, 0, seed: 1);
        arena.Idle(TankCatalog.Medium, 1, new Vector2(350, 350));
        arena.Run(2 * Tps);
        arena.Sim.RemoveTank(bot.TankId);
        arena.Run(2 * Tps);
        arena.Director.Update();   // as the server does every tick before the commands
        InputCmd cmd = arena.Director.Command(bot.TankId, arena.Sim.Tick + 1);
        Assert.Equal((0, 0, false), (cmd.Throttle, cmd.Steer, cmd.Fire));
        Assert.Equal(0, bot.TargetId);
        Assert.Throws<ArgumentException>(() => arena.Director.Command(999, arena.Sim.Tick + 1));
    }

    [Fact]
    public void Fallen_props_reach_the_bots_grid()
    {
        var house = new Prop(7, PropKind.House, new Obb2(new Vector2(200, 260), new Vector2(6, 4), 0f), -0.3f, 3f);
        var arena = new BotArena(map: BotArena.FlatMap(props: new[] { house }));
        arena.Bot(TankCatalog.Medium, 0, seed: 1);
        int gunner = arena.Idle(TankCatalog.Medium, 0, new Vector2(200, 230), BotArena.South);
        arena.Run(5);
        NavGrid grid = arena.Director.Grid;
        Assert.False(grid.IsOpenAt(house.Footprint.Center));
        Assert.Equal(0, grid.Version);

        arena.Sim.Field.SetDurability(house.Id, 1);   // one shot brings it down
        arena.Order(gunner, new InputCmd(0, 0, 0, BotArena.South, 0f, true));
        bool down = false;
        arena.Run(2 * Tps, _ =>
        {
            if (down) return;
            down = arena.Sim.Events.ToArray().Any(e => e.Type == GameEventType.PropDestroyed && e.PropId == house.Id);
            if (down) Assert.False(grid.IsOpenAt(house.Footprint.Center));   // the director hears of it on its next update
        });
        Assert.True(down, "the house came down");
        Assert.True(grid.IsOpenAt(house.Footprint.Center), "rubble is open ground");
        Assert.Equal(1, grid.Version);
    }

    /// <summary>The sandbox, a team battle, four bots a side from their spawns.</summary>
    private static BotArena FourAside(uint seed)
    {
        var arena = new BotArena(BattleMode.TeamBattle, MapCatalog.Sandbox);
        TankSpec[] specs = { TankCatalog.Light, TankCatalog.Medium, TankCatalog.Heavy, TankCatalog.Td };
        for (int k = 0; k < 8; k++) arena.Bot(specs[k % 4], (byte)(k % 2), seed + (uint)k * 7919);
        return arena;
    }

    [Fact]
    public void Directing_bots_allocates_nothing()
    {
        Allocation.AssertNone(() =>
        {
            BotArena arena = FourAside(11);
            arena.Recording = false;
            arena.Run(600);
            Allocation.Window window = Allocation.Start();
            for (int k = 0; k < 600; k++) arena.Tick();
            return window.Stop();
        });
    }

    [Fact]
    public void The_same_seeds_drive_the_same_way()
    {
        BotArena x = FourAside(21), y = FourAside(21);
        for (int k = 0; k < 900; k++)
        {
            x.Tick();
            y.Tick();
            Assert.Equal(x.Sim.TankCount, y.Sim.TankCount);
            for (int i = 0; i < x.Sim.TankCount; i++)
            {
                TankState a = x.Sim.Tanks[i], b = y.Sim.Tanks[i];
                Assert.Equal((a.Position, a.Yaw, a.Health), (b.Position, b.Yaw, b.Health));
            }
        }
        Assert.True(x.Events.Count > 0);
        Assert.Equal(x.Events, y.Events);

        BotArena z = FourAside(22);
        z.Run(900);
        bool differs = false;
        for (int i = 0; i < x.Sim.TankCount; i++) differs |= x.Sim.Tanks[i].Position != z.Sim.Tanks[i].Position;
        Assert.True(differs, "another seed drives another way");
    }

    [Fact]
    public void A_goal_behind_is_turned_to_on_the_spot_without_counting_as_stuck()
    {
        // A heavy (26°/s) facing north, both waypoints behind it: turning round takes about 7 s, longer than the 3 s stuck window.
        var arena = new BotArena(map: BotArena.FlatMap(waypoints: new Vector2[] { new(200, 330), new(150, 340) }));
        var start = new Vector2(200, 150);
        Bot bot = arena.Bot(TankCatalog.Heavy, 0, seed: 1, start, BotArena.North);
        arena.Run(2 * Tps);
        TankState t = arena.Tank(bot.TankId);
        Assert.Equal(BotActivity.Driving, bot.Activity);
        Assert.True(MathF.Abs(t.Speed) < 0.5f, $"turning on the spot, not driving off north: {t.Speed:F2} m/s");
        Assert.True(MathF.Abs(Angles.Wrap(t.Yaw - BotArena.North)) > Angles.Deg(20), "it is turning");
        Assert.True(Vector2.Distance(t.Position, start) < 1.5f);

        arena.Run(10 * Tps, _ => Assert.Equal(0, bot.Stucks));
        t = arena.Tank(bot.TankId);
        Assert.True(t.Position.Y > start.Y + 10f, $"on its way south: {t.Position}");
        Assert.Equal(0, bot.Stucks);
        // Nor by the measure: turning 20° is progress, as in the bot's own watch (it took the 7 s turn for a stuck).
        Assert.True(arena.LongestStuckSeconds(bot.TankId) < 2f, $"measured stuck {arena.LongestStuckSeconds(bot.TankId):F1} s");
    }

    [Fact]
    public void Out_of_both_rounds_or_with_an_enemy_above_its_gun_it_drives_on()
    {
        var arena = new BotArena();
        Bot bot = arena.Bot(TankCatalog.Medium, 0, seed: 3, new Vector2(150, 200), BotArena.East);
        int enemy = arena.Idle(TankCatalog.Heavy, 1, new Vector2(250, 200), BotArena.West);
        (arena.Sim.TankRef(bot.TankId).ApLeft, arena.Sim.TankRef(bot.TankId).HeLeft) = (0, 0);   // out of AP alone it fires HE (BotFireTests)
        Vector2 start = arena.Tank(bot.TankId).Position;
        arena.Run(6 * Tps, _ => Assert.Equal(0, bot.TargetId));
        Assert.True(arena.Vision.IsVisible(enemy, 0));
        Assert.Equal(0, ShotsBy(arena, bot.TankId));
        Assert.True(Vector2.Distance(arena.Tank(bot.TankId).Position, start) > 10f, "out of both rounds it drove on");

        // A medium (20° elevation) below a 30 m shelf; a heavy on it 60 m away (about 25° up), seen by a team mate up there.
        var shelf = new BotArena(map: BotArena.FlatMap(Ground400.Raised(x => x >= 150, 30f),
            waypoints: new Vector2[] { new(40, 100), new(40, 300), new(100, 300) }));
        Bot low = shelf.Bot(TankCatalog.Medium, 0, seed: 3, new Vector2(100, 100), BotArena.East);
        shelf.Idle(TankCatalog.Medium, 0, new Vector2(170, 120), BotArena.North);
        int high = shelf.Idle(TankCatalog.Heavy, 1, new Vector2(160, 100), BotArena.West);
        Vector2 from = shelf.Tank(low.TankId).Position;
        shelf.Run(2 * Tps, tick => Assert.Equal(0, low.TargetId));
        Assert.True(shelf.Vision.IsVisible(high, 0), "the team sees it");
        Assert.Equal(0, ShotsBy(shelf, low.TankId));
        shelf.Run(6 * Tps);
        Assert.True(Vector2.Distance(shelf.Tank(low.TankId).Position, from) > 10f, "it drove on instead of standing there");
    }

    public static TheoryData<int, string> Lanes()
    {
        var data = new TheoryData<int, string>();
        for (int lane = 0; lane < 3; lane++)
            foreach (TankSpec spec in TankCatalog.All)
                data.Add(lane, spec.Id);
        return data;
    }

    [Theory]
    [MemberData(nameof(Lanes))]
    public void A_bot_drives_a_riverland_lane_to_the_enemy_base(int lane, string tank)
    {
        MapDefinition map = MapCatalog.Riverland;
        var arena = new BotArena(BattleMode.TeamBattle, map);
        Bot bot = arena.Bot(TankCatalog.ById(tank), 0, seed: 9);
        arena.Director.AssignRoles();
        TeamBase target = map.Bases.Single(b => b.Team == 1);
        arena.Director.Attack(bot, lane, map.Bots.ViaFor(lane, 0), target.Centre);
        Assert.Equal(BotRole.Attack, bot.Role);
        Assert.Equal(lane, bot.Lane);

        const int limit = 300 * Tps;
        int arrived = -1;
        arena.Run(limit, tick =>
        {
            if (arrived < 0 && Vector2.Distance(arena.Tank(bot.TankId).Position, target.Centre) <= target.Radius) arrived = tick;
        });
        _output.WriteLine($"{map.Bots.Lanes[lane].Name} lane, {tank}: in the enemy base after {arrived / (float)Tps:F1} s, " +
                          $"{bot.Stucks} stucks, longest stuck {arena.LongestStuckSeconds(bot.TankId):F1} s, {bot.PathRequests} requests, " +
                          $"{arena.Director.Paths.Searches} searches, {arena.Director.Paths.Failures} failed");
        Assert.True(arrived > 0, $"not in the enemy base after {limit / Tps} s: at {arena.Tank(bot.TankId).Position}, heading for {bot.Goal}");
        Assert.True(arena.LongestStuckSeconds(bot.TankId) < 10f, $"stuck for {arena.LongestStuckSeconds(bot.TankId):F1} s");
        Assert.Equal(BotActivity.Holding, bot.Activity);
    }
}
