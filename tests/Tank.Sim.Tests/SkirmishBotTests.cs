using System.Numerics;
using Tank.Sim.Ai;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;
using Tank.Sim.Vision;

namespace Tank.Sim.Tests;

/// <summary>The room-filling bot on the sandbox map (stage 4 design §4).</summary>
public sealed class SkirmishBotTests
{
    private const int Tps = SimConstants.TickRate;

    private sealed class Arena
    {
        public readonly Simulation Sim;
        public readonly VisionTracker Vision;
        public readonly IReadOnlyList<Vector2> Waypoints;
        public readonly List<SkirmishBot> Bots = new();
        public readonly Dictionary<int, InputCmd> Orders = new();
        public readonly List<GameEvent> Events = new();

        /// <summary>The sandbox map and its waypoints, or a flat 200 m field (or the sandbox map) with <paramref name="waypoints"/>.</summary>
        public Arena(IReadOnlyList<Vector2>? waypoints = null, bool sandbox = false)
            : this(new Simulation(waypoints is null || sandbox ? SandboxMap.Create() : TestWorld.FlatField()), waypoints ?? SandboxMap.Waypoints) { }

        /// <summary>Any simulation with <paramref name="waypoints"/>.</summary>
        public Arena(Simulation sim, IReadOnlyList<Vector2> waypoints)
        {
            Sim = sim;
            Waypoints = waypoints;
            Vision = new VisionTracker(Sim);
        }

        public int AddBot(TankSpec spec, Vector2 at, float yaw, byte team, uint seed = 7)
        {
            int id = Sim.AddTank(spec, at, yaw, team);
            Bots.Add(new SkirmishBot(id, Waypoints, seed));
            return id;
        }

        public int AddDummy(TankSpec spec, Vector2 at, float yaw, byte team)
        {
            int id = Sim.AddTank(spec, at, yaw, team);
            Orders[id] = new InputCmd(0, 0, 0, yaw, 0f, false);
            return id;
        }

        public void Run(int ticks, Action<int>? each = null)
        {
            var inputs = new InputCmd[Simulation.MaxTanks];
            for (int i = 0; i < ticks; i++)
            {
                int tick = Sim.Tick + 1;
                for (int k = 0; k < Sim.TankCount; k++)
                {
                    int id = Sim.Tanks[k].Id;
                    SkirmishBot? bot = Bots.Find(b => b.TankId == id);
                    inputs[k] = bot is not null ? bot.Command(Sim, Vision, tick) : Orders[id] with { Tick = tick };
                }
                Sim.Step(inputs.AsSpan(0, Sim.TankCount));
                Vision.Update();
                Events.AddRange(Sim.Events);
                each?.Invoke(tick);
            }
        }

        public TankState Tank(int id) => Sim.Tanks[Sim.SlotOf(id)];
    }

    [Fact]
    public void It_drives_from_waypoint_to_waypoint_without_getting_stuck()
    {
        var arena = new Arena();
        int bot = arena.AddBot(TankCatalog.Medium, SandboxMap.Spawns.Team[0].Position, SandboxMap.Spawns.Team[0].Yaw, 0, seed: 3);
        var visited = new HashSet<int>();
        float travelled = 0f;
        Vector2 last = arena.Tank(bot).Position;
        int idleTicks = 0, longestIdle = 0;
        arena.Run(120 * Tps, tick =>
        {
            visited.Add(arena.Bots[0].Waypoint);
            TankState t = arena.Tank(bot);
            travelled += Vector2.Distance(t.Position, last);
            last = t.Position;
            idleTicks = MathF.Abs(t.Speed) < 0.3f ? idleTicks + 1 : 0;
            longestIdle = Math.Max(longestIdle, idleTicks);
        });
        Assert.True(travelled > 400f, $"only {travelled:F0} m in two minutes");
        Assert.True(visited.Count >= 4, $"only {visited.Count} waypoints");
        Assert.True(longestIdle < 8 * Tps, $"stood still for {longestIdle / (float)Tps:F1} s");   // turning on the spot after a waypoint takes a few seconds
        Assert.DoesNotContain(arena.Events, e => e.Type == GameEventType.ShellFired);   // nobody to shoot at
        Assert.All(arena.Sim.Field.Props.Where(p => p.Kind != PropKind.Bush), p => Assert.True(arena.Sim.Field.IsStanding(p.Id), $"knocked down {p.Kind} {p.Id}"));
    }

    [Fact]
    public void It_fires_at_a_visible_enemy_and_hits_it()
    {
        var arena = new Arena(new Vector2[] { new(20, 20), new(180, 180), new(20, 180) });   // flat, open ground
        int bot = arena.AddBot(TankCatalog.Medium, new Vector2(100, 180), 0f, 0);
        int enemy = arena.AddDummy(TankCatalog.Heavy, new Vector2(100, 80), MathF.PI, 1);   // 100 m north, facing it
        arena.Run(12 * Tps);
        Assert.Equal(enemy, arena.Bots[0].TargetId);
        Assert.Equal(0f, arena.Tank(bot).Speed);
        GameEvent[] shots = arena.Events.Where(e => e.Type == GameEventType.ShellFired && e.TankId == bot).ToArray();
        Assert.True(shots.Length >= 2, $"{shots.Length} shots in 12 s");
        Assert.Contains(arena.Events, e => e.Type == GameEventType.TankHit && e.TargetTankId == enemy);
        Assert.True(Vector2.Distance(arena.Tank(bot).Position, new Vector2(100, 180)) < 3f, "it stopped to shoot");
    }

    [Fact]
    public void It_only_shoots_what_its_team_can_see()
    {
        // A team mate stands watch at the watch point, 109 m from the first bush patch, where a light tank standing in the
        // bushes is hidden (two bushes: 40.5 m); the same tank 24 m outside the patch is seen, by the team mate, and the
        // bot (which has driven off a little by then) engages it on its team's word.
        var arena = new Arena();
        int bot = arena.AddBot(TankCatalog.Medium, SandboxMap.PatrolWatch + new Vector2(-10, 0), 0f, 0);
        arena.AddDummy(TankCatalog.Medium, SandboxMap.PatrolWatch, 0f, 0);
        int hidden = arena.AddDummy(TankCatalog.Light, SandboxMap.BushPatches[2], MathF.PI, 1);
        arena.Run(3 * Tps);
        Assert.False(arena.Vision.IsVisible(hidden, 0));
        Assert.Equal(0, arena.Bots[0].TargetId);
        Assert.DoesNotContain(arena.Events, e => e.Type == GameEventType.ShellFired);
        Assert.True(arena.Tank(bot).Position != SandboxMap.PatrolWatch + new Vector2(-10, 0), "it drove on, looking for a waypoint");

        arena.Sim.TankRef(hidden).Position = SandboxMap.BushPatches[2] + new Vector2(-24, 0);
        arena.Run(8 * Tps);
        Assert.True(arena.Vision.IsVisible(hidden, 0));
        Assert.Equal(hidden, arena.Bots[0].TargetId);
        Assert.Contains(arena.Events, e => e.Type == GameEventType.ShellFired && e.TankId == bot);
    }

    /// <summary>A bot at (60, 200) with a team mate standing next to an enemy heavy <paramref name="away"/> m east, so the team sees it.</summary>
    private static Arena SpottedEnemy(float away, out int bot, out int enemy)
    {
        var arena = new Arena(new Vector2[] { new(60, 200), new(63, 200), new(60, 203) }, sandbox: true);   // it potters about its spot (the flat field is only 200 m)
        bot = arena.AddBot(TankCatalog.Medium, new Vector2(60, 200), 0f, 0);
        arena.AddDummy(TankCatalog.Medium, new Vector2(60 + away, 215), MathF.PI, 0);
        enemy = arena.AddDummy(TankCatalog.Heavy, new Vector2(60 + away, 200), MathF.PI, 1);
        return arena;
    }

    [Fact]
    public void It_ignores_enemies_beyond_250_m_and_engages_the_nearest_of_several()
    {
        Arena arena = SpottedEnemy(260f, out _, out int far);
        arena.Run(3 * Tps);
        Assert.True(arena.Vision.IsVisible(far, 0), "the team mate sees it");
        Assert.Equal(0, arena.Bots[0].TargetId);
        Assert.DoesNotContain(arena.Events, e => e.Type == GameEventType.ShellFired);

        Arena near = SpottedEnemy(240f, out _, out int enemy);
        int nearer = near.AddDummy(TankCatalog.Light, new Vector2(60 + 180, 190), MathF.PI, 1);   // 180 m: the one to shoot at
        near.Run(3 * Tps);
        Assert.Equal(nearer, near.Bots[0].TargetId);
        _ = enemy;
    }

    [Fact]
    public void A_wreck_holds_still_and_carries_on_once_revived()
    {
        Arena arena = SpottedEnemy(100f, out int bot, out int enemy);
        arena.Run(8 * Tps);
        Assert.Equal(enemy, arena.Bots[0].TargetId);
        Assert.Contains(arena.Events, e => e.Type == GameEventType.ShellFired && e.TankId == bot);   // in the middle of a fight …
        arena.Sim.TankRef(bot).IsDestroyed = true;   // … it is knocked out
        arena.Sim.TankRef(bot).Health = 0;
        InputCmd cmd = arena.Bots[0].Command(arena.Sim, arena.Vision, arena.Sim.Tick + 1);
        Assert.Equal((0, 0, false), (cmd.Throttle, cmd.Steer, cmd.Fire));
        Assert.Equal(0, arena.Bots[0].TargetId);
        arena.Events.Clear();
        arena.Run(2 * Tps);
        Assert.DoesNotContain(arena.Events, e => e.Type == GameEventType.ShellFired && e.TankId == bot);

        arena.Sim.Revive(bot);
        arena.Run(8 * Tps);
        Assert.Equal(enemy, arena.Bots[0].TargetId);
        Assert.Contains(arena.Events, e => e.Type == GameEventType.ShellFired && e.TankId == bot);
    }

    [Fact]
    public void An_enemy_above_the_guns_elevation_is_left_alone_and_the_bot_drives_on()
    {
        // A medium (20° elevation) at the foot of a 45° cliff; a heavy 30 m up it, 70 m north (23° up: out of reach), spotted
        // by a team mate beside it. The bot doesn't stand there trying to lay: it drives off to its waypoints — and from
        // further away, where the angle is under 20°, it may well engage after all.
        var sim = new Simulation(TestWorld.Field(TestWorld.CliffNorth(110, 1)));
        var arena = new Arena(sim, new Vector2[] { new(100, 150), new(100, 190), new(40, 170) });
        int bot = arena.AddBot(TankCatalog.Medium, new Vector2(100, 150), 0f, 0);
        arena.AddDummy(TankCatalog.Medium, new Vector2(110, 80), MathF.PI, 0);
        int high = arena.AddDummy(TankCatalog.Heavy, new Vector2(100, 80), MathF.PI, 1);
        Vector2 start = arena.Tank(bot).Position;
        arena.Run(2 * Tps, tick =>
        {
            Assert.True(arena.Vision.IsVisible(high, 0) || tick < 2, "the team sees it");
            Assert.Equal(0, arena.Bots[0].TargetId);   // seen, but not a target: the gun can't reach
        });
        Assert.DoesNotContain(arena.Events, e => e.Type == GameEventType.ShellFired);
        arena.Run(8 * Tps);
        Assert.True(Vector2.Distance(arena.Tank(bot).Position, start) > 10f, "it drove on instead of standing there");
    }

    [Fact]
    public void Stuck_against_a_rock_for_three_seconds_it_picks_another_waypoint()
    {
        var rock = new Prop(1, PropKind.Rock, new Obb2(new Vector2(100, 88), new Vector2(12, 3), 0f), -1f, 4f);
        var arena = new Arena(new Simulation(TestWorld.FlatField(rock)), new Vector2[] { new(100, 20), new(100, 180) });
        int bot = arena.AddBot(TankCatalog.Medium, new Vector2(100, 100), 0f, 0, seed: 1);
        if (arena.Bots[0].Waypoint != 0) arena = Retry(arena, rock);   // the first pick is random: we want the one past the rock
        // The first 3 s window covers the approach (it moved); the next finds it stuck at the rock.
        arena.Run((int)(2.5f * Tps));
        Assert.Equal(0, arena.Bots[0].Waypoint);   // still pushing at the rock
        Assert.True(arena.Tank(bot).Position.Y > 90f);
        arena.Run(4 * Tps);
        Assert.Equal(1, arena.Bots[0].Waypoint);   // gave up on it
    }

    private static Arena Retry(Arena _, Prop rock)
    {
        for (uint seed = 2; ; seed++)
        {
            var arena = new Arena(new Simulation(TestWorld.FlatField(rock)), new Vector2[] { new(100, 20), new(100, 180) });
            arena.AddBot(TankCatalog.Medium, new Vector2(100, 100), 0f, 0, seed);
            if (arena.Bots[0].Waypoint == 0) return arena;
        }
    }

    [Fact]
    public void Out_of_AP_it_drives_on_instead_of_standing_in_the_open()
    {
        Arena arena = SpottedEnemy(100f, out int bot, out int enemy);
        arena.Sim.TankRef(bot).ApLeft = 0;
        arena.Run(5 * Tps);
        Assert.True(arena.Vision.IsVisible(enemy, 0));
        Assert.Equal(0, arena.Bots[0].TargetId);
        Assert.DoesNotContain(arena.Events, e => e.Type == GameEventType.ShellFired);
        InputCmd cmd = arena.Bots[0].Command(arena.Sim, arena.Vision, arena.Sim.Tick + 1);
        Assert.False(cmd.Fire);
        _ = bot;
    }

    [Fact]
    public void It_turns_in_place_towards_a_waypoint_behind_it_without_giving_up_on_it()
    {
        // A heavy (26°/s) facing north with a waypoint 90 m behind it: turning takes ~7 s, longer than the 3 s stuck window.
        Arena arena;
        int bot;
        uint seed = 0;
        do
        {
            arena = new Arena(new Vector2[] { new(100, 20), new(100, 190) });
            bot = arena.AddBot(TankCatalog.Heavy, new Vector2(100, 100), 0f, 0, seed: ++seed);
        } while (arena.Bots[0].Waypoint != 1);
        arena.Run(8 * Tps);
        Assert.Equal(1, arena.Bots[0].Waypoint);
        TankState t = arena.Tank(bot);
        Assert.True(MathF.Abs(Angles.Wrap(t.Yaw - MathF.PI)) < 0.3f, $"turned round: yaw {t.Yaw:F2}");
        Assert.True(t.Position.Y > 105f, $"on its way: {t.Position}");
    }

    [Fact]
    public void Two_bots_on_opposite_teams_find_and_fight_each_other()
    {
        var arena = new Arena();
        int a = arena.AddBot(TankCatalog.Medium, SandboxMap.Spawns.Team[0].Position, SandboxMap.Spawns.Team[0].Yaw, 0, seed: 11);
        int b = arena.AddBot(TankCatalog.Medium, SandboxMap.Spawns.Team[5].Position, SandboxMap.Spawns.Team[5].Yaw, 1, seed: 23);
        arena.Run(180 * Tps);
        Assert.Contains(arena.Events, e => e.Type == GameEventType.TankHit && (e.TargetTankId == a || e.TargetTankId == b));
    }

    [Fact]
    public void Its_choices_replay_the_same_from_the_same_seed()
    {
        var x = new Arena();
        var y = new Arena();
        x.AddBot(TankCatalog.Light, SandboxMap.Spawns.Solo[0].Position, 0f, 0, seed: 5);
        y.AddBot(TankCatalog.Light, SandboxMap.Spawns.Solo[0].Position, 0f, 0, seed: 5);
        x.Run(30 * Tps);
        y.Run(30 * Tps);
        Assert.Equal(x.Sim.Tanks[0], y.Sim.Tanks[0]);
        Assert.Equal(x.Bots[0].Waypoint, y.Bots[0].Waypoint);
        Assert.Throws<ArgumentException>(() => new SkirmishBot(1, new[] { Vector2.Zero }, 1));
    }
}
