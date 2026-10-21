using Tank.Net.Protocol;
using Tank.Net.Transport;

namespace Tank.Net.Tests.Transport;

public sealed class LoopbackNetworkTests
{
    [Fact]
    public void Packets_arrive_by_arrival_time_then_in_the_order_they_were_sent()
    {
        var net = new LoopbackNetwork();
        var server = net.CreateServer();
        var slow = net.Connect(new LinkConditions(LatencyMs: 50));
        var fast = net.Connect(new LinkConditions(LatencyMs: 10));
        var got = new Recorder();
        server.Poll(got);

        slow.Send(new byte[] { 1 }, Channel.Unreliable);
        fast.Send(new byte[] { 2 }, Channel.Unreliable);
        fast.Send(new byte[] { 3 }, Channel.Reliable);
        fast.Send(new byte[] { 4 }, Channel.Unreliable);
        net.AdvanceTime(0.005);
        server.Poll(got);
        Assert.Empty(got.Packets);

        net.AdvanceTime(0.1);
        server.Poll(got);
        Assert.Equal(new byte[] { 2, 3, 4, 1 }, got.Packets.Select(p => p[0]));
    }

    [Fact]
    public void A_packet_sent_while_packets_are_being_handed_over_waits_for_the_next_poll()
    {
        var net = new LoopbackNetwork();
        var server = net.CreateServer();
        var client = net.Connect();
        var got = new Recorder();
        server.Poll(got);
        got.OnPacket = data => { if (data[0] == 1) client.Send(new byte[] { 2 }, Channel.Unreliable); };

        client.Send(new byte[] { 1 }, Channel.Unreliable);
        server.Poll(got);
        Assert.Equal(new byte[] { 1 }, got.Packets.Select(p => p[0]));

        server.Poll(got);
        Assert.Equal(new byte[] { 1, 2 }, got.Packets.Select(p => p[0]));
    }

    [Fact]
    public void A_handler_that_throws_leaves_the_packets_after_it_in_flight()
    {
        var net = new LoopbackNetwork();
        var server = net.CreateServer();
        var client = net.Connect();
        var got = new Recorder();
        server.Poll(got);
        got.OnPacket = data => { if (data[0] == 2) throw new InvalidOperationException("boom"); };

        for (byte i = 1; i <= 4; i++) client.Send(new[] { i }, Channel.Unreliable);
        Assert.Throws<InvalidOperationException>(() => server.Poll(got));
        server.Poll(got);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, got.Packets.Select(p => p[0]));
    }

    [Fact]
    public void Loss_follows_the_seed_exactly()
    {
        // Seed 7, 20% loss: xorshift32 draws whose value mod 10000 is under 2000 drop packets 7, 8 and 13. Recorded
        // before the generator moved to Tank.Sim, so seeded runs (the soak) replay unchanged.
        var net = new LoopbackNetwork(seed: 7);
        var server = net.CreateServer();
        var client = net.Connect(new LinkConditions(LossPercent: 20));
        var got = new Recorder();
        server.Poll(got);

        for (byte i = 0; i < 20; i++) client.Send(new[] { i }, Channel.Unreliable);
        client.Send(new byte[] { 100 }, Channel.Reliable);   // never lost
        server.Poll(got);

        byte[] expected = Enumerable.Range(0, 20).Where(i => i is not (7 or 8 or 13)).Select(i => (byte)i).Append((byte)100).ToArray();
        Assert.Equal(expected, got.Packets.Select(p => p[0]));
    }

    private sealed class Recorder : ITransportHandler
    {
        public List<byte[]> Packets { get; } = new();

        public Action<byte[]>? OnPacket { get; set; }

        public void OnPeerConnected(int peerId) { }

        public void OnPeerDisconnected(int peerId, string reason) { }

        public void OnData(int peerId, ReadOnlySpan<byte> data)
        {
            byte[] copy = data.ToArray();
            Packets.Add(copy);
            OnPacket?.Invoke(copy);
        }
    }
}
