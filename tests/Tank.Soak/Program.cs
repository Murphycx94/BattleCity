using System.Diagnostics;
using Tank.Core.Math;
using Tank.Core.World;
using Tank.Net.Client;
using Tank.Net.Server;
using Tank.Net.Transport;

namespace Tank.Soak;

/// <summary>
/// Network soak test (docs/04 §4): 8 bots on a deterministic simulated network under several link qualities.
/// Prints the acceptance metrics from docs/03 §11 and exits non-zero if any are violated.
/// </summary>
public static class Program
{
    private sealed record Scenario(string Name, LinkConditions Link, GameMode Mode, double MaxMispredictionsPerMinute, int Enemies = 40, bool Fire = true);

    private const int Bots = 8;
    private const double MatchSeconds = 60;
    /// <summary>Tick timings from the first seconds are dominated by JIT/tiered compilation, not by the game.</summary>
    private const double TimingWarmupSeconds = 2.5;

    /// <summary>
    /// Server tick p99 budget (docs/03 §11: 2 ms on real hardware). Shared CI VMs add scheduler noise to
    /// wall-clock timing, so CI raises it via SOAK_TICK_BUDGET_MS to only catch gross regressions.
    /// </summary>
    private static readonly double TickBudgetMs =
        double.TryParse(Environment.GetEnvironmentVariable("SOAK_TICK_BUDGET_MS"), System.Globalization.CultureInfo.InvariantCulture, out double b) ? b : 2.0;
    private const double FrameDt = 1.0 / 144;

    public static int Main()
    {
        var scenarios = new[]
        {
            new Scenario("wired LAN", new LinkConditions(0.5, 0.2, 0), GameMode.Deathmatch, 10),
            new Scenario("wifi 30±20ms 5% loss", new LinkConditions(30, 20, 5), GameMode.Deathmatch, 30),
            new Scenario("awful 60±40ms 20% loss", new LinkConditions(60, 40, 20), GameMode.Deathmatch, double.MaxValue),
            new Scenario("co-op AI load, wired", new LinkConditions(0.5, 0.2, 0), GameMode.Coop, 30),
        };

        // Untimed warm-up so the first real scenario does not pay for JIT compilation.
        Run(scenarios[0] with { Name = "(warm-up)" }, seconds: 8, report: false);

        Console.WriteLine($"tick budget: p99 < {TickBudgetMs} ms");
        Console.WriteLine($"{"scenario",-26}{"mispred/min",12}{"extrap",8}{"snap/s",8}{"down KB/s",10}{"interp ms",10}{"tick p50/p99 ms",17}{"drops",7}  result");
        bool ok = true;
        foreach (var s in scenarios) ok &= Run(s);
        return ok ? 0 : 1;
    }

    private static bool Run(Scenario scenario, double seconds = MatchSeconds, bool report = true)
    {
        var net = new LoopbackNetwork(seed: 11);
        var server = new GameServer(net.CreateServer(), new ServerOptions { Seed = 5, Mode = scenario.Mode, EnemyTotal = scenario.Enemies });
        var clients = Enumerable.Range(0, Bots)
            .Select(i => new GameClient(net.Connect(scenario.Link, scenario.Link), $"bot{i}", () => net.Now))
            .ToList();

        var tickTimes = new List<double>();
        double serverAccumulator = 0;
        bool started = false;
        int frames = (int)((seconds + 1) / FrameDt);
        for (int f = 0; f < frames; f++)
        {
            net.AdvanceTime(FrameDt);
            serverAccumulator += FrameDt;
            while (serverAccumulator >= 1.0 / 60)
            {
                long t0 = Stopwatch.GetTimestamp();
                server.Tick();
                if (started && net.Now > 0.5 + TimingWarmupSeconds) tickTimes.Add(Stopwatch.GetElapsedTime(t0).TotalMilliseconds);
                serverAccumulator -= 1.0 / 60;
            }
            for (int i = 0; i < clients.Count; i++) clients[i].Update(FrameDt, BotInput(i, net.Now, scenario.Fire));

            // Start, and restart whenever a round ends (bots happily shoot their own eagle in co-op).
            if (net.Now > 0.5 && !server.MatchRunning && f % 30 == 0)
            {
                var host = clients.FirstOrDefault(c => c.IsHost && c.Phase == ClientPhase.Lobby);
                host?.StartMatch();
                started |= host is not null;
            }
        }

        int mispredictions = clients.Sum(c => c.Stats.Mispredictions);
        int extrapolations = clients.Sum(c => c.Stats.Extrapolations);
        double snaps = clients.Average(c => c.Stats.SnapshotsPerSecond);
        double down = clients.Average(c => c.Stats.BytesInPerSecond) / 1024;
        double interp = clients.Average(c => c.Stats.InterpDelayMs);
        tickTimes.Sort();
        double p99 = tickTimes.Count == 0 ? 0 : tickTimes[(int)(tickTimes.Count * 0.99)];
        double p50 = tickTimes.Count == 0 ? 0 : tickTimes[tickTimes.Count / 2];
        int dropped = clients.Count(c => c.Phase == ClientPhase.Disconnected);
        double perMinute = mispredictions / (double)Bots / (seconds / 60);

        if (!report) return true;
        bool pass = dropped == 0 && perMinute <= scenario.MaxMispredictionsPerMinute && down < 50 && p99 < TickBudgetMs;
        Console.WriteLine($"{scenario.Name,-26}{perMinute,12:F1}{extrapolations,8}{snaps,8:F0}{down,10:F1}{interp,10:F1}{$"{p50:F3}/{p99:F3}",17}{dropped,7}  {(pass ? "PASS" : "FAIL")}");
        return pass;
    }

    /// <summary>Wander, turn often, shoot a lot: a worst-case-ish human.</summary>
    private static LocalInput BotInput(int bot, double t, bool shoot = true)
    {
        int phase = (int)(t / (0.3 + bot * 0.05)) + bot;
        Dir? dir = (phase % 6) switch { 0 => Dir.Up, 1 => Dir.Left, 2 => Dir.Down, 3 => Dir.Right, 4 => Dir.Up, _ => null };
        bool fire = shoot && ((int)(t * 8) + bot) % 3 == 0;
        return new LocalInput(dir, fire, fire);
    }
}
