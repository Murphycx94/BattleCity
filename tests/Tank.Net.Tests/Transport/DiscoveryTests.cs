using System.Diagnostics;
using System.Net;
using LiteNetLib;
using Tank.Net.Battle;
using Tank.Net.Protocol;
using Tank.Net.Transport;
using Tank.Sim.Modes;

namespace Tank.Net.Tests.Transport;

public sealed class DiscoveryTests
{
    private static readonly RoomInfoMessage Room = new(BattleProtocol.Version, "测试房间", BattleMode.FreeForAll, 3, BattleProtocol.MaxPlayers, true, 27015);

    [Fact]
    public void The_discovery_request_is_the_id_40_then_the_protocol_version()
    {
        var w = new BitWriter();
        BattleMessages.WriteDiscover(w);
        Assert.Equal(new byte[] { 40, BattleProtocol.Version }, w.Data.ToArray());
        Assert.Equal(40, (int)BattleMessage.Discover);
    }

    /// <summary>The real thing on 127.0.0.1. Where the sandbox forbids binding, the test does nothing.</summary>
    [Fact]
    public void A_room_answers_a_discovery_request_from_any_version_and_nothing_else()
    {
        bool answer = true;
        using var room = new LiteNetServerTransport(0, 2, BattleProtocol.ConnectKey, w =>
        {
            if (!answer) return false;
            BattleMessages.Write(w, Room);
            return true;
        });
        try
        {
            room.Start();
        }
        catch (InvalidOperationException)
        {
            return;   // no UDP here: nothing to test
        }

        var replies = new List<byte[]>();
        var listener = new EventBasedNetListener();
        listener.NetworkReceiveUnconnectedEvent += (_, reader, _) => replies.Add(reader.GetRemainingBytes());
        var probe = new NetManager(listener) { UnconnectedMessagesEnabled = true };
        Assert.True(probe.Start());
        try
        {
            var to = new IPEndPoint(IPAddress.Loopback, room.Manager.LocalPort);
            var idle = new Idle();
            void Pump(double seconds, Func<bool> done)
            {
                var clock = Stopwatch.StartNew();
                while (clock.Elapsed.TotalSeconds < seconds && !done())
                {
                    room.Poll(idle);
                    probe.PollEvents();
                    Thread.Sleep(5);
                }
            }

            probe.SendUnconnectedMessage(new byte[] { 40, 1 }, to);   // what a 4B client sends
            Pump(3, () => replies.Count > 0);
            Assert.Single(replies);
            var r = new BitReader(replies[0]);
            Assert.Equal((byte)BattleMessage.RoomInfo, r.ReadByte());
            Assert.Equal(Room, BattleMessages.ReadRoomInfo(ref r));

            probe.SendUnconnectedMessage(new byte[] { 42, 0 }, to);   // not a request, while the room would answer one
            probe.SendUnconnectedMessage(new byte[] { 41 }, to);
            Pump(0.5, () => replies.Count > 1);
            Assert.Single(replies);

            answer = false;
            probe.SendUnconnectedMessage(new byte[] { 40, BattleProtocol.Version }, to);   // a request the room declines
            Pump(0.5, () => replies.Count > 1);
            Assert.Single(replies);
        }
        finally
        {
            probe.Stop();
        }
    }

    /// <summary>The client side: <see cref="LanDiscovery"/> lists a room that answers and ignores a malformed answer.</summary>
    [Fact]
    public void LanDiscovery_lists_a_room_that_answers_and_ignores_a_malformed_answer()
    {
        int asked = 0;
        bool malformed = false;
        using var room = new LiteNetServerTransport(0, 2, BattleProtocol.ConnectKey, w =>
        {
            asked++;
            BattleMessages.Write(w, malformed ? Room with { Port = 0 } : Room);   // port 0 does not read back
            return true;
        });
        try
        {
            room.Start();
        }
        catch (InvalidOperationException)
        {
            return;   // no UDP here: nothing to test
        }
        var to = new IPEndPoint(IPAddress.Loopback, room.Manager.LocalPort);
        var idle = new Idle();

        using (var discovery = new LanDiscovery(BattleProtocol.DefaultPort, to))
        {
            Pump(room, idle, discovery, 3, () => discovery.Rooms.Count > 0);
            DiscoveredRoom found = Assert.Single(discovery.Rooms);
            Assert.Equal(Room, found.Info);
            Assert.Equal(new IPEndPoint(IPAddress.Loopback, Room.Port), found.EndPoint);   // the sender's address, the port the room says it uses
        }

        malformed = true;
        int before = asked;
        using (var discovery = new LanDiscovery(BattleProtocol.DefaultPort, to))
        {
            Pump(room, idle, discovery, 3, () => asked > before);
            Pump(room, idle, discovery, 0.5, () => false);   // time for the answer to come back
            Assert.True(asked > before);
            Assert.Empty(discovery.Rooms);
        }
    }

    private static void Pump(LiteNetServerTransport room, ITransportHandler handler, LanDiscovery discovery, double seconds, Func<bool> done)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed.TotalSeconds < seconds && !done())
        {
            room.Poll(handler);
            discovery.Poll();
            Thread.Sleep(5);
        }
    }

    private sealed class Idle : ITransportHandler
    {
        public void OnPeerConnected(int peerId) { }

        public void OnPeerDisconnected(int peerId, string reason) { }

        public void OnData(int peerId, ReadOnlySpan<byte> data) { }
    }
}
