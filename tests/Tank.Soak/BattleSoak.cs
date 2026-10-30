using System.Diagnostics;
using Tank.Net.Battle;
using Tank.Net.Transport;
using Tank.Sim;
using Tank.Sim.Maps;
using Tank.Sim.Modes;

namespace Tank.Soak;

/// <summary>
/// Battle soak (stage 4 design §6): one battle server, 4 scripted clients and 4 bots on a deterministic simulated
/// network under several link qualities, a minute each; then a royale on the riverland (stage 6 design §6): the same
/// 4 clients and 12 bots, 16 tanks. Prints the design's acceptance numbers — downstream under
/// 20 KB/s for each client throughout, corrections small (and how many came with another tank within 10 m, a shove most
/// likely, and how many without), nobody dropped, the server tick within budget — and returns
/// false if any is violated. The client must also be seen to work: it has its tank at the end, takes at least
/// <see cref="MinSnapshotsPerSecond"/> snapshots a second, and on the wired link drops no packet and never asks for a resync.
/// </summary>
public static class BattleSoak
{
    /// <param name="Riverland">The match on the riverland (generated with the server, before the clock starts); else the sandbox.</param>
    private sealed record Scenario(string Name, LinkConditions Link, float MaxCorrectionM, bool Clean = false,
        BattleMode Mode = BattleMode.TeamBattle, int Bots = 4, bool Riverland = false);

    private const int Clients = 4;
    private const double MatchSeconds = 60;
    private const double DownBudgetKBps = 20;
    private const double MinSnapshotsPerSecond = 15;   // of the 30 sent (a lossy link loses some; a client ignoring them gets none)

    /// <summary>The map (about 60 KB, once) arrives with the match start: the downstream budget is for the match itself, so sampling starts after it.</summary>
    private const double JoinBurstSeconds = 3;
    private const double BandwidthSampleInterval = 1;
    private const double FrameDt = 1.0 / 144;
    private const double ServerDt = 1.0 / SimConstants.TickRate;
    /// <summary>Tick timings from the first seconds are dominated by JIT/tiered compilation, not by the game.</summary>
    private const double TimingWarmupSeconds = 2.5;

    /// <summary>A correction counts as "near" with another tank this close to the client's on the server, m (a shove, most likely).</summary>
    private const float NearM = 10f;

    public static bool Run(double tickBudgetMs)
    {
        var scenarios = new[]
        {
            new Scenario("wired LAN", new LinkConditions(0.5, 0.2, 0), 0.6f, Clean: true),
            new Scenario("wifi 30±20ms 5% loss", new LinkConditions(30, 20, 5), 2f),
            new Scenario("mobile 80±30ms 2% loss", new LinkConditions(80, 30, 2), 2f),
            new Scenario("awful 60±40ms 20% loss", new LinkConditions(60, 40, 20), 3f),
            // 60 s: ring 1 is still waiting (it shrinks from 90 s) and ring 2 isn't announced (180 s), so this row has no zone
            // damage, zone kills or Zone messages in it, only the tanks' traffic (16 of them); kept short on purpose
            new Scenario("royale wifi 30±20ms 5% loss", new LinkConditions(30, 20, 5), 2f, Mode: BattleMode.Royale, Bots: 12, Riverland: true),
        };

        Run(scenarios[0] with { Name = "(warm-up)" }, tickBudgetMs, seconds: 8, report: false);

        Console.WriteLine($"battle: {Clients} clients + 4 bots, team battle on the sandbox; the last row a royale on the riverland, {Clients} clients + 12 bots (16 tanks); {MatchSeconds:F0} s each; every client's down < {DownBudgetKBps} KB/s in every second after the first {JoinBurstSeconds:F0}, snap/s >= {MinSnapshotsPerSecond}, tick p99 < {tickBudgetMs} ms");
        Console.WriteLine($"{"scenario",-26}{"corr/min",10}{"near/apart",12}{"mean m",8}{"max m",8}{"down max KB/s",15}{"up KB/s",9}{"snap/s min",11}{"snap B",8}{"interp ms",10}{"tick p50/p99 ms",17}{"kills",7}{"left",6}{"bad pkts",9}{"resyncs",8}  result");
        bool ok = true;
        foreach (Scenario s in scenarios) ok &= Run(s, tickBudgetMs);
        return ok;
    }

    private static bool Run(Scenario scenario, double tickBudgetMs, double seconds = MatchSeconds, bool report = true)
    {
        var net = new LoopbackNetwork(seed: 11);
        var options = new BattleServerOptions { Seed = 5, Mode = scenario.Mode, Bots = scenario.Bots };
        if (scenario.Riverland) options = options with { Map = MapCatalog.Riverland, Maps = new[] { MapCatalog.Riverland } };
        var server = new BattleServer(net.CreateServer(), options);
        var clients = Enumerable.Range(0, Clients)
            .Select(i => new BattleClient(net.Connect(scenario.Link, scenario.Link), $"player{i}", () => net.Now))
            .ToList();

        var tickTimes = new List<double>();
        double serverAccumulator = 0;
        bool started = false;
        double nextSample = 0, maxDown = 0;   // maxDown: the largest downstream any client had in any sampled second, B/s
        var seen = new int[Clients];          // each client's corrections so far
        int near = 0, apart = 0;              // the corrections made with another tank within NearM of the client's, and without
        int frames = (int)((seconds + 1) / FrameDt);
        for (int f = 0; f < frames; f++)
        {
            net.AdvanceTime(FrameDt);
            serverAccumulator += FrameDt;
            while (serverAccumulator >= ServerDt - 1e-9)
            {
                long t0 = Stopwatch.GetTimestamp();
                server.Tick();
                if (started && net.Now > 1 + TimingWarmupSeconds) tickTimes.Add(Stopwatch.GetElapsedTime(t0).TotalMilliseconds);
                serverAccumulator -= ServerDt;
            }
            for (int i = 0; i < clients.Count; i++) clients[i].Update(FrameDt, Drive(i, net.Now));
            for (int i = 0; i < clients.Count; i++)
            {
                int now = clients[i].Stats.Corrections;
                if (now == seen[i]) continue;
                if (NearAnother(server, clients[i])) near += now - seen[i];
                else apart += now - seen[i];
                seen[i] = now;
            }

            // The host starts once everyone is in the room; a team battle or a royale lasts longer than the soak, so no restarts.
            if (!started && net.Now > 0.5 && f % 30 == 0 && clients.All(c => c.Phase == BattlePhase.Room))
            {
                clients.Single(c => c.IsHost).StartMatch();
                started = true;
                nextSample = net.Now + JoinBurstSeconds;
            }
            if (started && net.Now >= nextSample)
            {
                foreach (BattleClient c in clients) maxDown = System.Math.Max(maxDown, c.Stats.BytesInPerSecond);
                nextSample += BandwidthSampleInterval;
            }
        }

        int corrections = clients.Sum(c => c.Stats.Corrections);
        double perMinute = corrections / (double)Clients / (seconds / 60);
        double mean = clients.Average(c => c.Stats.MeanCorrection);
        float max = clients.Max(c => c.Stats.MaxCorrection);
        double down = maxDown / 1024;
        double up = clients.Average(c => c.Stats.BytesOutPerSecond) / 1024;
        double minSnapshots = clients.Min(c => c.Stats.SnapshotsPerSecond);
        int badPackets = clients.Sum(c => c.Stats.DroppedPackets), resyncs = clients.Sum(c => c.Resyncs);
        bool allHaveTanks = clients.All(c => c.HasTank);
        int snapBytes = (int)clients.Average(c => c.Stats.SnapshotBytes);
        double interp = clients.Average(c => c.Stats.InterpDelayMs);
        tickTimes.Sort();
        double p99 = tickTimes.Count == 0 ? 0 : tickTimes[(int)(tickTimes.Count * 0.99)];
        double p50 = tickTimes.Count == 0 ? 0 : tickTimes[tickTimes.Count / 2];
        int dropped = clients.Count(c => c.Phase != BattlePhase.InMatch);
        int kills = server.Rules is { } rules ? Enumerable.Range(0, BattleProtocol.MaxPlayers).Sum(slot => rules.Of((byte)slot).Kills) : 0;

        if (!report) return true;
        bool pass = started && server.MatchRunning && dropped == 0 && allHaveTanks && minSnapshots >= MinSnapshotsPerSecond
            && max <= scenario.MaxCorrectionM && down < DownBudgetKBps && p99 < tickBudgetMs
            && (!scenario.Clean || (badPackets == 0 && resyncs == 0));
        double perMinuteOf(int n) => n / (double)Clients / (seconds / 60);
        Console.WriteLine($"{scenario.Name,-26}{perMinute,10:F1}{$"{perMinuteOf(near):F1}/{perMinuteOf(apart):F1}",12}{mean,8:F3}{max,8:F2}{down,15:F1}{up,9:F1}{minSnapshots,11:F0}{snapBytes,8}{interp,10:F1}{$"{p50:F3}/{p99:F3}",17}{kills,7}{dropped,6}{badPackets,9}{resyncs,8}  {(pass ? "PASS" : "FAIL")}");
        return pass;
    }

    /// <summary>Whether another tank is within <see cref="NearM"/> of <paramref name="client"/>'s on the server (false out of a match).</summary>
    private static bool NearAnother(BattleServer server, BattleClient client)
    {
        if (server.Simulation is not { } sim || server.Rules is not { } rules) return false;
        int slot = sim.SlotOf(rules.Of(client.Slot).TankId);
        if (slot < 0) return false;
        System.Numerics.Vector2 at = sim.Tanks[slot].Position;
        for (int k = 0; k < sim.TankCount; k++)
            if (k != slot && System.Numerics.Vector2.Distance(sim.Tanks[k].Position, at) < NearM) return true;
        return false;
    }

    /// <summary>Drive about, turn often, swing the turret and shoot a lot: a worst-case-ish human.</summary>
    private static LocalCommand Drive(int client, double t)
    {
        int phase = (int)(t / (0.6 + client * 0.1)) + client * 3;
        sbyte throttle = (sbyte)((phase % 4) switch { 0 => 1, 1 => 1, 2 => -1, _ => 0 });
        sbyte steer = (sbyte)((phase % 3) switch { 0 => 1, 1 => -1, _ => 0 });
        bool fire = ((int)(t * 8) + client) % 7 == 0;
        return new LocalCommand(throttle, steer, (float)(t * 0.5 + client), 0.02f, fire);   // one round type: a swap restarts the reload
    }
}
