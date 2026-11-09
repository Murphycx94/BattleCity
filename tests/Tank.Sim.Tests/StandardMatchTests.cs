using System.Numerics;
using Tank.Sim.Ai;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;
using Tank.Sim.Vision;
using Xunit.Abstractions;

namespace Tank.Sim.Tests;

/// <summary>
/// Whole standard matches on the two real maps, without a screen (stage 5 design §6): ended by a wipe-out, a capture
/// and the ten-minute clock; ten bots play one through; and a tick of it allocates nothing. Players join on their
/// team's spawns as in a real match; tanks are only moved with <c>TankRef</c>, the maps are never changed.
/// </summary>
public sealed class StandardMatchTests
{
    private const int Tps = SimConstants.TickRate;

    private readonly ITestOutputHelper _output;

    public StandardMatchTests(ITestOutputHelper output) => _output = output;

    public static TheoryData<string> Maps => new() { "sandbox", "riverland" };

    /// <summary>A standard match on a catalog map, stepped like the server: the bot director and the commands, then the step, vision and the rules.</summary>
    private sealed class Game
    {
        public readonly MapDefinition Map;
        public readonly Simulation Sim;
        public readonly MatchRules Rules;
        public readonly VisionTracker Vision;
        public readonly int StartTick;
        private readonly Dictionary<int, InputCmd> _orders = new();
        private readonly InputCmd[] _inputs = new InputCmd[Simulation.MaxTanks];

        public Game(string mapId)
        {
            Map = MapCatalog.Get(mapId);
            Sim = new Simulation(Map.NewBattlefield());   // the catalog generates each map once per run
            Rules = new MatchRules(BattleMode.Standard, Sim, Map.Spawns, bases: Map.Bases);
            Vision = new VisionTracker(Sim);
            StartTick = Sim.Tick;
        }

        /// <summary>The match's bots, made with the first one (a match without bots has none, as on the server).</summary>
        public BotDirector? Director { get; private set; }

        public int Join(byte slot, byte team, TankSpec? spec = null) => Rules.Join(slot, spec ?? TankCatalog.Medium, team);

        /// <summary>Hands <paramref name="tankId"/> to a bot of the match's <see cref="BotDirector"/>.</summary>
        public void Bot(int tankId, uint seed) => (Director ??= new BotDirector(Sim, Vision, Rules, Map, Map.NewNavGrid())).Add(tankId, seed);

        public void Order(int tankId, InputCmd cmd) => _orders[tankId] = cmd;

        public TankState Tank(int id) => Sim.Tanks[Sim.SlotOf(id)];

        public Vector2 TeamSpawnCentre(byte team)
        {
            Vector2 sum = Vector2.Zero;
            int n = 0;
            foreach (SpawnPoint p in Map.Spawns.Team)
                if (p.Team == team) (sum, n) = (sum + p.Position, n + 1);
            return sum / n;
        }

        public TeamBase BaseOf(byte team) => Map.Bases.Single(b => b.Team == team);

        public void Place(int id, Vector2 at, Vector2 facing)
        {
            Sim.TankRef(id).Position = at;
            Sim.TankRef(id).Yaw = Angles.YawOf(facing - at);
        }

        /// <summary>One tick; allocation-free.</summary>
        public void Step()
        {
            Director?.Update();
            int tick = Sim.Tick + 1, n = Sim.TankCount;
            for (int k = 0; k < n; k++) _inputs[k] = Command(Sim.Tanks[k], tick);
            Sim.Step(_inputs.AsSpan(0, n));
            Vision.Update();
            Rules.Update();
        }

        public void Run(int ticks)
        {
            for (int i = 0; i < ticks; i++) Step();
        }

        /// <summary>Runs until the match ends or <paramref name="limit"/> ticks pass.</summary>
        public void RunToEnd(int limit)
        {
            for (int i = 0; i < limit && Rules.Phase == MatchPhase.Playing; i++) Step();
        }

        /// <summary>
        /// Lays <paramref name="shooter"/>'s gun on <paramref name="target"/>'s hull front, on the ground where it stands,
        /// and keeps firing.
        /// </summary>
        public void Attack(int shooter, int target)
        {
            TankState s = Tank(shooter), t = Tank(target);
            TankPose aimed = TankPose.Compute(t, Sim.Field.Terrain);
            Vector3 point = aimed.Position + Vector3.Transform(new Vector3(0, 1.02f, -3.1f), aimed.Rotation);
            var (yaw, pitch) = Angles.AimAngles(point - TankPose.Compute(s, Sim.Field.Terrain).Trunnion(s.Spec));
            Order(shooter, new InputCmd(0, 0, 0, yaw, pitch, true));
        }

        private InputCmd Command(in TankState t, int tick)
        {
            if (Director?.Find(t.Id) is not null) return Director.Command(t.Id, tick);
            return _orders.TryGetValue(t.Id, out InputCmd cmd) ? cmd : new InputCmd(tick, 0, 0, t.Yaw, 0f, false);
        }
    }

    [Theory]
    [MemberData(nameof(Maps))]
    public void A_standard_match_ends_when_one_side_is_wiped_out(string id)
    {
        var g = new Game(id);
        int a = g.Join(0, 0), b = g.Join(1, 1);
        g.Run(ModeRules.ProtectionTicks + 1);
        Assert.False(g.Sim.IsProtected(g.Tank(a)));

        // b on team 0's base centre; a 40 m from it towards team 0's spawns (open flat ground on both maps), face to face.
        Vector2 centre = g.BaseOf(0).Centre;
        Vector2 shooterAt = centre + Vector2.Normalize(g.TeamSpawnCentre(0) - centre) * 40f;
        g.Place(b, centre, shooterAt);
        g.Place(a, shooterAt, centre);
        g.Sim.TankRef(b).Health = 1;
        g.Attack(a, b);
        int from = g.Sim.Tick;
        g.RunToEnd(10 * Tps);

        Assert.Equal(MatchPhase.Ended, g.Rules.Phase);
        Assert.Equal(0, g.Rules.WinnerTeam);
        Assert.Equal(MatchEndReason.Eliminated, g.Rules.EndReason);
        Assert.True(g.Tank(b).IsDestroyed);
        Assert.InRange(g.Rules.EndedAt - from, 1, 10 * Tps - 1);
        _output.WriteLine($"{id}: b knocked out {g.Rules.EndedAt - from} ticks after a opened fire");
    }

    [Theory]
    [MemberData(nameof(Maps))]
    public void A_standard_match_ends_when_a_base_is_captured(string id)
    {
        var g = new Game(id);
        int[] attackers = { g.Join(0, 0), g.Join(1, 0), g.Join(2, 0) };
        int defender = g.Join(5, 1);
        Vector2 defenderSpawn = g.Tank(defender).Position;

        // Three spots 5–10 m from team 1's base centre, a third of a turn apart; the defender stays on its spawn.
        TeamBase target = g.BaseOf(1);
        float[] distances = { 5f, 7.5f, 10f };
        for (int i = 0; i < attackers.Length; i++)
        {
            Vector2 at = target.Centre + Angles.Forward(i * 2f * MathF.PI / 3f) * distances[i];
            g.Place(attackers[i], at, target.Centre);
        }
        int from = g.Sim.Tick;
        g.Run(BaseCapture.TicksToWin / attackers.Length - 1);
        Assert.Equal(MatchPhase.Playing, g.Rules.Phase);
        Assert.Equal(99, g.Rules.Capture!.Progress(1));
        g.Run(1);

        Assert.Equal(MatchPhase.Ended, g.Rules.Phase);
        Assert.Equal(1000, g.Rules.EndedAt - from);
        Assert.Equal(0, g.Rules.WinnerTeam);
        Assert.Equal(MatchEndReason.Captured, g.Rules.EndReason);
        Assert.Equal(100, g.Rules.Capture.Progress(1));
        Assert.Equal(0, g.Rules.Capture.Progress(0));
        for (byte slot = 0; slot < 3; slot++) Assert.Equal(33, g.Rules.CaptureBest(slot));
        Assert.Equal(0, g.Rules.CaptureBest(5));
        Assert.Equal(defenderSpawn, g.Tank(defender).Position);
        Assert.All(attackers, t => Assert.Equal(TankCatalog.Medium.MaxHealth, g.Tank(t).Health));
    }

    [Theory]
    [MemberData(nameof(Maps))]
    public void A_standard_match_nobody_settles_is_a_draw_at_ten_minutes(string id)
    {
        var g = new Game(id);
        g.Join(0, 0);
        g.Join(1, 1);
        int duration = ModeRules.DurationTicks(BattleMode.Standard);
        Assert.Equal(18_000, duration);
        g.Run(duration - 1);
        Assert.Equal(MatchPhase.Playing, g.Rules.Phase);
        Assert.Equal(1, g.Rules.TicksLeft);
        Assert.Equal(MatchEndReason.None, g.Rules.EndReason);
        g.Run(1);

        Assert.Equal(MatchPhase.Ended, g.Rules.Phase);
        Assert.Equal(MatchRules.NoTeam, g.Rules.WinnerTeam);
        Assert.Equal(MatchEndReason.TimeUp, g.Rules.EndReason);
        Assert.Equal(duration, g.Rules.EndedAt - g.StartTick);
        Assert.Equal(0, g.Rules.Capture!.Progress(0));
        Assert.Equal(0, g.Rules.Capture.Progress(1));
    }

    [Theory]
    [MemberData(nameof(Maps))]
    public void Ten_bots_play_a_standard_match_without_trouble(string id)
    {
        var g = TenBots(id);
        for (int i = 0; i < 60 * Tps && g.Rules.Phase == MatchPhase.Playing; i++)
        {
            g.Step();
            Assert.InRange(g.Rules.Capture!.Progress(0), 0, 100);
            Assert.InRange(g.Rules.Capture.Progress(1), 0, 100);
        }

        MatchRules r = g.Rules;
        _output.WriteLine($"{id}: tick {g.Sim.Tick}, {r.Phase}, winner {r.WinnerTeam}, {r.EndReason}; alive {r.AliveCount(0)}–{r.AliveCount(1)}; " +
                          $"bases {r.Capture!.Progress(0)}–{r.Capture.Progress(1)}; kills {r.TeamScore(0)}–{r.TeamScore(1)}");
        if (r.Phase == MatchPhase.Playing)
        {
            Assert.Equal(MatchEndReason.None, r.EndReason);
            Assert.True(r.AliveCount(0) > 0 && r.AliveCount(1) > 0);
            return;
        }
        Assert.NotEqual(MatchEndReason.None, r.EndReason);
        Assert.Equal(g.Sim.Tick, r.EndedAt);
        if (r.EndReason == MatchEndReason.Eliminated) Assert.True(r.AliveCount(0) == 0 || r.AliveCount(1) == 0);
        if (r.EndReason == MatchEndReason.Captured) Assert.True(r.Capture.IsCaptured(0) || r.Capture.IsCaptured(1));
    }

    [Fact]
    public void A_standard_tick_with_ten_bots_allocates_nothing()
    {
        Allocation.AssertNone(() =>
        {
            var g = TenBots("riverland");
            g.Run(10 * Tps);
            Allocation.Window window = Allocation.Start();
            g.Run(10 * Tps);
            Allocation.Measured measured = window.Stop();
            Assert.Equal(MatchPhase.Playing, g.Rules.Phase);   // the rules ran every tick, not just returned
            return measured;
        });
    }

    /// <summary>Five against five in every kind of tank, each driven by a bot of the director (seeds 1–10).</summary>
    private static Game TenBots(string id)
    {
        var g = new Game(id);
        TankSpec[] kinds = { TankCatalog.Medium, TankCatalog.Heavy, TankCatalog.Light, TankCatalog.Td };
        for (byte slot = 0; slot < 10; slot++)
        {
            int tank = g.Join(slot, (byte)(slot % 2), kinds[slot / 2 % kinds.Length]);
            g.Bot(tank, seed: slot + 1u);
        }
        return g;
    }
}
