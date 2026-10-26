using System.Collections.Concurrent;
using System.Diagnostics;
using Tank.Net.Battle;
using Tank.Net.Transport;
using Tank.Sim;
using Tank.Sim.Maps;
using Tank.Sim.Modes;
using Xunit.Abstractions;

namespace Tank.Net.Tests.Transport;

/// <summary>
/// The riverland's match start (about 542 KiB, reliable, fragmented) over real UDP on 127.0.0.1: what loopback tests
/// can't show — LiteNetLib's own packet limits and fragmentation. Where the sandbox forbids binding, the test does nothing.
/// </summary>
public sealed class RiverlandUdpMatchTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    private const double TickSeconds = 1.0 / SimConstants.TickRate;

    private readonly ITestOutputHelper _output;

    public RiverlandUdpMatchTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Two_clients_start_a_riverland_match_over_real_udp()
    {
        var log = new ConcurrentQueue<string>();
        BattleServer? server = null;
        using var transport = new LiteNetServerTransport(0, BattleProtocol.MaxPlayers, BattleProtocol.ConnectKey, w => server!.WriteRoomInfo(w));
        try
        {
            transport.Start();
        }
        catch (InvalidOperationException)
        {
            return;   // no UDP here: nothing to test
        }
        server = new BattleServer(transport, new BattleServerOptions
        {
            Seed = 1, Map = MapCatalog.Riverland, PrepareMapsInBackground = true, Port = transport.Manager.LocalPort,
        }, log.Enqueue);
        var clock = Stopwatch.StartNew();
        double Now() => clock.Elapsed.TotalSeconds;
        using var linkA = new LiteNetClientTransport("127.0.0.1", transport.Manager.LocalPort, BattleProtocol.ConnectKey);
        using var linkB = new LiteNetClientTransport("127.0.0.1", transport.Manager.LocalPort, BattleProtocol.ConnectKey);
        var clients = new[] { new BattleClient(linkA, "A", Now), new BattleClient(linkB, "B", Now) };

        double serverTime = 0, last = 0;
        bool startAsked = false;
        double startedAt = 0;
        while (clock.Elapsed < Deadline && !clients.All(c => c.Phase == BattlePhase.InMatch))
        {
            double now = Now(), dt = now - last;
            last = now;
            for (; serverTime + TickSeconds <= now; serverTime += TickSeconds) server.Tick();   // 30 Hz of real time, as ServerRunner does
            foreach (BattleClient c in clients) c.Update(dt, LocalCommand.None);
            BattleClient? hosting = clients.FirstOrDefault(c => c.IsHost);   // whoever said hello first
            if (!startAsked && server.RoomMapReady && hosting?.Room?.Players.Count == 2 && clients.All(c => c.Phase == BattlePhase.Room))
            {
                hosting.StartMatch();
                startAsked = true;
                startedAt = now;
            }
            Thread.Sleep(2);
        }

        _output.WriteLine($"both in the match {Now() - startedAt:F2} s after the start was asked; {Now():F2} s in all");
        Assert.True(startAsked, $"the match was never asked to start (map {server.RoomMapState}, {string.Join(", ", clients.Select(c => $"{c.Phase} host {c.IsHost}"))})");
        foreach (BattleClient c in clients)
        {
            Assert.Equal(BattlePhase.InMatch, c.Phase);
            Assert.Null(c.LastPacketError);
            Assert.Equal(0, c.Stats.DroppedPackets);
            Assert.Equal(MapCatalog.Riverland.Data.HeightsCm.Length, c.Field!.Terrain.SamplesX * c.Field.Terrain.SamplesZ);
            Assert.Equal(MapCatalog.Riverland.Data.Props.Length, c.Field.Props.Count);
        }
        Assert.True(server.MatchRunning);
        Assert.DoesNotContain(log, line => line.Contains("error", StringComparison.OrdinalIgnoreCase) || line.Contains("malformed", StringComparison.OrdinalIgnoreCase));
    }
}
