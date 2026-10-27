using Tank.Net.Battle;
using Tank.Net.Tests.Battle;
using Tank.Sim;
using Tank.Sim.Maps;
using Tank.Sim.Modes;
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
