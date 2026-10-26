using System.Diagnostics;
using LiteNetLib;
using Tank.Net.Battle;
using Tank.Net.Protocol;
using Tank.Net.Transport;

namespace Tank.Net.Tests.Battle;

/// <summary>
/// A snapshot that goes unreliable must fit one LiteNetLib packet: 2.1.4 (MTU 1024, discovery off) sends at most 1023
/// unreliable bytes and throws <see cref="TooBigPacketException"/> beyond that.
/// </summary>
public sealed class UnreliableLimitTests
{
    [Fact]
    public void The_unreliable_limit_fits_one_LiteNetLib_packet()
    {
        Assert.True(BattleServer.MaxUnreliableBytes <= NetConstants.InitialMtu - NetConstants.HeaderSize);
    }

    [Fact]
    public void Bigger_snapshots_go_reliable()
    {
        Assert.Equal(Channel.Unreliable, BattleServer.ChannelFor(1000));
        Assert.Equal(Channel.Reliable, BattleServer.ChannelFor(1001));
    }

    /// <summary>The real thing on 127.0.0.1. Where the sandbox forbids binding, the test does nothing.</summary>
    [Fact]
    public void LiteNetLib_carries_the_limit_and_refuses_a_byte_more()
    {
        using var server = new LiteNetServerTransport(0, 2, BattleProtocol.ConnectKey);
        try
        {
            server.Start();
        }
        catch (InvalidOperationException)
        {
            return;   // no UDP here: nothing to test
        }
        using var client = new LiteNetClientTransport("127.0.0.1", server.Manager.LocalPort, BattleProtocol.ConnectKey);
        var serverSide = new Recorder();
        var clientSide = new Recorder();
        void Pump(double seconds, Func<bool> done)
        {
            var clock = Stopwatch.StartNew();
            while (clock.Elapsed.TotalSeconds < seconds && !done())
            {
                server.Poll(serverSide);
                client.Poll(clientSide);
                Thread.Sleep(5);
            }
        }

        Pump(2, () => serverSide.Connected.Count > 0 && client.IsConnected);
        int peer = Assert.Single(serverSide.Connected);
        Assert.True(client.IsConnected);

        var payload = new byte[BattleServer.MaxUnreliableBytes];
        for (int i = 0; i < payload.Length; i++) payload[i] = (byte)i;
        server.Send(peer, payload, Channel.Unreliable);
        Pump(2, () => clientSide.Received.Count > 0);
        byte[] got = Assert.Single(clientSide.Received);
        Assert.Equal(payload, got);

        NetPeer netPeer = server.Manager.FirstPeer!;
        Assert.NotNull(netPeer);
        Assert.Throws<TooBigPacketException>(() =>
            netPeer.Send(new byte[1024], (byte)Channel.Unreliable, DeliveryMethod.Unreliable));
    }

    private sealed class Recorder : ITransportHandler
    {
        public List<int> Connected { get; } = new();
        public List<byte[]> Received { get; } = new();

        public void OnPeerConnected(int peerId) => Connected.Add(peerId);

        public void OnPeerDisconnected(int peerId, string reason) { }

        public void OnData(int peerId, ReadOnlySpan<byte> data) => Received.Add(data.ToArray());
    }
}
