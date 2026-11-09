using System.Numerics;
using Tank.Net.Battle;
using Tank.Net.Tests.Battle;
using Tank.Sim;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;
using Xunit.Abstractions;

namespace Tank.Net.Tests;

/// <summary>
/// What nine bots cost the server (stage 5 design §6, 5C: "with nine bots the server's tick stays under 2 ms at p99";
/// stage 5C plan, task 9), measured through the fighting (batch C review follow-up: the first minute alone had none).
/// Wall-clock: in the <see cref="TimingCollection"/>. Debug builds only print and check a loose limit; the real one holds
/// in Release.
/// </summary>
[Collection(TimingCollection.Name)]
public sealed class BotLoadTests
{
    private const int Tps = SimConstants.TickRate;

    /// <summary>The timed window ends with the match, or after this long, s.</summary>
    private const int MaxWindowSeconds = 300;

    /// <summary>The window is played in steps this long, s (the match's end is looked for between them: a step's few ticks of results at most are timed).</summary>
    private const double Step = 0.1;

    /// <summary>Duos: the player is left alone this long into the window, then put in an enemy's way (<see cref="PutBeforeAnEnemy"/>), s.</summary>
    private const int AliveSeconds = 10;

    private readonly ITestOutputHelper _output;

    public BotLoadTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Nine_bots_on_the_riverland_keep_the_server_tick_p99_under_2_ms()
    {
        var log = new List<string>();
        var h = new BattleHarness(new BattleServerOptions { Seed = 3, Map = MapCatalog.Riverland, Maps = new[] { MapCatalog.Riverland } }, log: log.Add);
        h.AddClient("A");
        h.StartMatch(BattleMode.Standard, bots: 9);
        Assert.True(h.Server.MatchRunning, "the match didn't start");
        Assert.Equal(9, h.Server.Director!.Bots.Count);

        h.Run(3, Drive);   // warm-up: the first searches and the JIT's first tiers (1–7 ms ticks were seen there)
        int hits = 0, wrecksBefore = Wrecks(h.Server.Simulation!);
        h.TickTimes = new List<double>(MaxWindowSeconds * Tps + 1);
        h.AfterServerTick = server =>
        {
            if (server.Simulation is not { } sim) return;
            foreach (ref readonly GameEvent e in sim.Events)
                if (e.Type == GameEventType.TankHit) hits++;
        };
        while (h.Server.Rules is { Phase: MatchPhase.Playing } && h.TickTimes.Count < MaxWindowSeconds * Tps) h.Run(Step, Drive);
        h.AfterServerTick = null;

        Assert.True(h.Server.MatchRunning, "the match was over before the window was");
        Simulation simulation = h.Server.Simulation!;
        int wrecks = Wrecks(simulation) - wrecksBefore;
        double[] sorted = h.TickTimes.Order().ToArray();
        double p50 = sorted[sorted.Length / 2], p99 = sorted[(int)(sorted.Length * 0.99)], max = sorted[^1];
        _output.WriteLine($"server tick with nine bots on the riverland over {sorted.Length} ticks ({sorted.Length / (double)Tps:F0} s, " +
                          $"{(h.Server.Rules!.Phase == MatchPhase.Ended ? $"to the match's end, {h.Server.Rules.EndReason}" : "the match still on")}): " +
                          $"p50 {p50:F3} ms, p99 {p99:F3} ms, max {max:F3} ms; {hits} hits, {wrecks} wrecks; bots: {h.Server.Director!.Summary()}");
        Assert.True(hits >= 10, $"only {hits} hits in the window: hardly any fighting");
        Assert.True(wrecks >= 2, $"only {wrecks} tanks destroyed in the window");
#if DEBUG
        const double Limit = 10.0;
#else
        const double Limit = 2.0;
#endif
        Assert.True(p99 < Limit, $"p99 {p99:F3} ms (limit {Limit} ms)");
    }

    /// <summary>
    /// A royale's sixteen tanks (stage 6 design §3.5; stage 6A plan, task 6): an idle player and fifteen bots on the
    /// riverland, the server's tick timed from a few seconds in to the match's end (or <see cref="MaxWindowSeconds"/>);
    /// the player's snapshots (size, mean and largest) and bytes in a second (the largest) printed beside it, apart while
    /// it is alive (it sees what it sees) and once it is destroyed (a free-for-all's dead watch everything: all sixteen
    /// tanks, the most any seat is sent; batch C review follow-up). From 6B the match has its 48 crates: the time the
    /// server's crate sight passes took is printed too, with what sixteen human seats would cost a pass by that average (one
    /// seat is looked for here, and the average takes in the passes after it died, which cast no rays: a lower bound; Sim's
    /// <c>CrateSetVisibilityTimingTests</c> times sixteen live tanks from the spawns, the real number).
    /// </summary>
    [Fact]
    public void Fifteen_bots_in_a_royale_on_the_riverland_keep_the_server_tick_p99_under_2_ms() => SixteenOnTheRiverland(BattleMode.Royale);

    /// <summary>
    /// The same in duos (stage 6 design §5; 6C task 4): an idle player partnered by a bot and seven teams of two bots. The
    /// player's snapshots are printed in three parts: alive, destroyed while its team-mate lives (it watches through the
    /// team's eyes) and with the whole team out (everything, as a royale's dead). Seed 3's player team lived to the end, so
    /// <see cref="AliveSeconds"/> into the window the player is put in front of an enemy bot to be destroyed
    /// (<see cref="PutBeforeAnEnemy"/>; batch B review follow-up): the middle state always has samples, while the team-mate
    /// plays on. The crate sight pass looks from both of the player's team (the bot's tank too). Same limit as the royale.
    /// </summary>
    [Fact]
    public void Fifteen_bots_in_duos_on_the_riverland_keep_the_server_tick_p99_under_2_ms() => SixteenOnTheRiverland(BattleMode.Duos);

    private void SixteenOnTheRiverland(BattleMode mode)
    {
        bool duos = ModeRules.IsDuos(mode);
        var log = new List<string>();
        var h = new BattleHarness(new BattleServerOptions { Seed = 3, Map = MapCatalog.Riverland, Maps = new[] { MapCatalog.Riverland } }, log: log.Add);
        BattleClient player = h.AddClient("A");
        h.StartMatch(mode, bots: 15);
        Assert.True(h.Server.MatchRunning, "the match didn't start");
        Assert.Equal(mode, h.Server.Rules!.Mode);
        Assert.Equal(15, h.Server.Director!.Bots.Count);

        h.Run(3);   // warm-up, the player idle throughout (LocalCommand.None)
        int hits = 0, wrecksBefore = Wrecks(h.Server.Simulation!);
        var alive = new SnapshotSizes();
        var watching = new SnapshotSizes();   // destroyed: in a royale it watches everything; in duos through its team-mate while that one lives
        var teamOut = new SnapshotSizes();    // duos: destroyed with the whole team out, watching everything
        h.TickTimes = new List<double>(MaxWindowSeconds * Tps + 1);
        h.AfterServerTick = server =>
        {
            if (server.Simulation is not { } sim) return;
            foreach (ref readonly GameEvent e in sim.Events)
                if (e.Type == GameEventType.TankHit) hits++;
        };
        var gc = new GcWindow();   // so a one-off slow tick (31.6 ms was seen once, never again) can be laid at the collector's door or not
        bool putBefore = !duos;
        while (h.Server.Rules is { Phase: MatchPhase.Playing } && h.TickTimes.Count < MaxWindowSeconds * Tps)
        {
            if (!putBefore && h.TickTimes.Count >= AliveSeconds * Tps)
            {
                PutBeforeAnEnemy(h, player);   // between two ticks: the ticks after it are timed as any
                putBefore = true;
            }
            h.Run(Step);
            BattleStats stats = player.Stats;
            if (stats.SnapshotBytes <= 0) continue;
            if (!IsDestroyed(h.Server.Simulation!, player.OwnTankId)) alive.Add(stats);
            else if (duos && h.Server.Rules.AliveCount(h.Server.Rules.Of(player.Slot).Team) == 0) teamOut.Add(stats);
            else watching.Add(stats);
        }
        h.AfterServerTick = null;

        Simulation simulation = h.Server.Simulation!;
        int wrecks = Wrecks(simulation) - wrecksBefore;
        double[] sorted = h.TickTimes.Order().ToArray();
        double p50 = sorted[sorted.Length / 2], p99 = sorted[(int)(sorted.Length * 0.99)], max = sorted[^1];
        string match = duos ? "duos (the player partnered by a bot)" : "a royale";
        _output.WriteLine($"server tick with an idle player and fifteen bots in {match} on the riverland over {sorted.Length} ticks ({sorted.Length / (double)Tps:F0} s, " +
                          $"{(h.Server.Rules!.Phase == MatchPhase.Ended ? $"to the match's end, {h.Server.Rules.EndReason}" : "the match still on")}): " +
                          $"p50 {p50:F3} ms, p99 {p99:F3} ms, max {max:F3} ms ({gc}); {hits} hits, {wrecks} wrecks; bots: {h.Server.Director!.Summary()}");
        _output.WriteLine($"the player's snapshots, alive (the tanks it sees): {alive}");
        if (duos)
        {
            _output.WriteLine($"the player's snapshots, destroyed with its team-mate alive (the team's eyes): {watching}");
            _output.WriteLine($"the player's snapshots, destroyed with the whole team out (everything: the busiest seat): {teamOut}");
        }
        else _output.WriteLine($"the player's snapshots, destroyed and watching (a free-for-all's dead see all sixteen: the busiest seat): {watching}");
        double sightMs = h.Server.CrateSightTime.TotalMilliseconds, perPass = sightMs / Math.Max(1, h.Server.CrateSightPasses);
        CrateSet crates = h.Server.Rules.Crates!;
        // in duos a pass looks once a team, from each of its live tanks: sixteen human seats are eight teams' looks
        int lookers = duos ? ModeRules.DuoTeams : BattleProtocol.MaxPlayers;
        _output.WriteLine($"crate sight: {h.Server.CrateSightPasses} passes over {crates.Count} crates for the one human seat{(duos ? " (both of its team looking)" : "")} took {sightMs:F1} ms in all, " +
                          $"{perPass:F4} ms a pass averaged over every pass, those after the {(duos ? "team was out" : "player died")} included (no rays then); sixteen human seats by that average {perPass * lookers:F3} ms a pass (5 a second), a lower bound — Sim's CrateSetVisibilityTimingTests has the sixteen-tank number; " +
                          $"{crates.Count - crates.StandingCount} crates taken");
        Assert.True(h.Server.CrateSightPasses > 0, "no crate sight pass was made");
        Assert.True(alive.Samples > 0, "the player got no snapshots");
        // duos: dead with the team-mate alive is set up (PutBeforeAnEnemy); with the whole team out comes as the match goes
        Assert.True(watching.Samples > 0, duos ? "no snapshots of the player dead with its team-mate alive" : "the player was never destroyed: no snapshots of a dead seat");
        Assert.True(hits >= 10, $"only {hits} hits in the window: hardly any fighting");
        Assert.True(wrecks >= 2, $"only {wrecks} tanks destroyed in the window");
#if DEBUG
        const double Limit = 10.0;
#else
        const double Limit = 2.0;
#endif
        Assert.True(p99 < Limit, $"p99 {p99:F3} ms (limit {Limit} ms)");
    }

    /// <summary>
    /// Duos: the idle player's tank (alive, its team-mate too) put 25 m in front of the nearest live enemy, facing it, at 1 hit point
    /// (the zone's bite, which the rules note only as health): the enemy's first hit destroys it, a kill the rules hear of as any other.
    /// </summary>
    private static void PutBeforeAnEnemy(BattleHarness h, BattleClient player)
    {
        Simulation sim = h.Server.Simulation!;
        TankState me = h.ServerTankOf(player);
        Assert.False(me.IsDestroyed, "the player was destroyed before it was put in an enemy's way");
        Assert.Equal(2, h.Server.Rules!.AliveCount(me.Team));
        TankState enemy = sim.Tanks.ToArray().Where(t => !t.IsDestroyed && t.Team != me.Team).MinBy(t => Vector2.DistanceSquared(t.Position, me.Position));
        Vector2 ahead = Angles.Forward(enemy.Yaw);
        sim.Respawn(me.Id, enemy.Position + ahead * 25f, Angles.YawOf(-ahead));
        sim.HurtByZone(me.Id, me.Spec.MaxHealth - 1);
    }

    private static bool IsDestroyed(Simulation sim, int tankId)
    {
        int slot = sim.SlotOf(tankId);
        return slot >= 0 && sim.Tanks[slot].IsDestroyed;
    }

    /// <summary>The garbage collector's work since this was made: collections by generation and the time the collector paused the process.</summary>
    private sealed class GcWindow
    {
        private readonly int _gen0 = GC.CollectionCount(0), _gen1 = GC.CollectionCount(1), _gen2 = GC.CollectionCount(2);
        private readonly TimeSpan _paused = GC.GetTotalPauseDuration();

        public override string ToString() =>
            $"GC in the window: {GC.CollectionCount(0) - _gen0} gen0, {GC.CollectionCount(1) - _gen1} gen1, {GC.CollectionCount(2) - _gen2} gen2, " +
            $"{(GC.GetTotalPauseDuration() - _paused).TotalMilliseconds:F1} ms paused";
    }

    /// <summary>A client's snapshot sizes, sampled: mean and largest, and the most bytes in a second.</summary>
    private sealed class SnapshotSizes
    {
        private long _bytes;
        private int _biggest;
        private double _mostIn;

        public int Samples { get; private set; }

        public void Add(BattleStats stats)
        {
            (Samples, _bytes, _biggest) = (Samples + 1, _bytes + stats.SnapshotBytes, Math.Max(_biggest, stats.SnapshotBytes));
            _mostIn = Math.Max(_mostIn, stats.BytesInPerSecond);
        }

        public override string ToString() =>
            Samples == 0 ? "none" : $"{_bytes / (double)Samples:F0} bytes on average, {_biggest} at most ({Samples} samples); at most {_mostIn / 1024:F1} KB/s in";
    }

    private static int Wrecks(Simulation sim)
    {
        int n = 0;
        foreach (ref readonly Tank.Sim.Vehicles.TankState t in sim.Tanks)
            if (t.IsDestroyed) n++;
        return n;
    }

    /// <summary>The soak's driving: about, turning often, the turret swinging, firing a lot (a worst-case-ish human).</summary>
    private static LocalCommand Drive(int client, double t)
    {
        int phase = (int)(t / (0.6 + client * 0.1)) + client * 3;
        sbyte throttle = (sbyte)((phase % 4) switch { 0 => 1, 1 => 1, 2 => -1, _ => 0 });
        sbyte steer = (sbyte)((phase % 3) switch { 0 => 1, 1 => -1, _ => 0 });
        bool fire = ((int)(t * 8) + client) % 7 == 0;
        return new LocalCommand(throttle, steer, (float)(t * 0.5 + client), 0.02f, fire);
    }
}
