using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using Tank.Net.Battle;
using Tank.Net.Protocol;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Modes;

namespace Tank.Net.Tests.Battle;

/// <summary>
/// Choosing the room's map (stage 5 design §2): the room state and the LAN answer carry it, only the host picks it and only
/// in the room, and a match can't start while the chosen map is still being generated.
/// </summary>
public sealed class RoomMapTests
{
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(30);   // generous: background generation competes with the other tests; the waits end as soon as it is ready

    /// <summary>The small map with one rock in the middle, so a match on it can be told from one on <see cref="TestMaps.Small"/>.</summary>
    private static MapDefinition WithRock(string id) => new(id, id,
        () => TestWorld.FlatField(new Prop(1, PropKind.Rock, new Obb2(new Vector2(100f, 60f), new Vector2(2f, 2f), 0f), -0.3f, 3f)),
        TestMaps.Spawns, TestMaps.Waypoints);

    private static BitReader ReaderFor(BitWriter w, BattleMessage type)
    {
        var r = new BitReader(w.Data);
        Assert.Equal((byte)type, r.ReadByte());
        return r;
    }

    private static RoomInfoMessage RoomInfoOf(BattleServer server)
    {
        var w = new BitWriter();
        Assert.True(server.WriteRoomInfo(w));
        BitReader r = ReaderFor(w, BattleMessage.RoomInfo);
        return BattleMessages.ReadRoomInfo(ref r);
    }

    private static bool WaitFor(Func<bool> condition, TimeSpan timeout, Action? step = null)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.Elapsed > timeout) return false;
            step?.Invoke();
            Thread.Sleep(5);
        }
        return true;
    }

    // ---- the wire ----

    [Theory]
    [InlineData(MapState.Generating)]
    [InlineData(MapState.Ready)]
    [InlineData(MapState.Failed)]
    public void Room_state_round_trips_the_map_and_its_state(MapState mapState)
    {
        var players = new[] { new RoomPlayer(0, "安娜", 0, 1, true, true, false) };
        var w = new BitWriter();
        BattleMessages.Write(w, new RoomStateMessage(BattleMode.TeamBattle, 0, 1, false, players, "riverland", mapState));
        BitReader r = ReaderFor(w, BattleMessage.RoomState);

        RoomStateMessage state = BattleMessages.ReadRoomState(ref r);

        Assert.Equal(("riverland", mapState), (state.Map, state.MapState));
        Assert.Equal(players, state.Players);
        var plain = new RoomStateMessage(BattleMode.TeamBattle, 0, 0, false, players);
        Assert.Equal(("sandbox", MapState.Ready), (plain.Map, plain.MapState));
        w.Reset();
        BattleMessages.Write(w, plain);
        r = ReaderFor(w, BattleMessage.RoomState);
        RoomStateMessage back = BattleMessages.ReadRoomState(ref r);
        Assert.Equal(("sandbox", MapState.Ready), (back.Map, back.MapState));
    }

    [Fact]
    public void An_unknown_map_state_is_refused()
    {
        Assert.Equal((0, 1, 2), ((int)MapState.Generating, (int)MapState.Ready, (int)MapState.Failed));
        var w = new BitWriter();   // a room state with no players, the sandbox and map state 3
        w.WriteByte((byte)BattleMessage.RoomState);
        w.WriteByte((byte)BattleMode.TeamBattle);
        w.WriteByte(0);
        w.WriteByte(0);
        w.WriteBool(false);
        w.WriteVarUInt(0);
        w.WriteString("sandbox", BattleProtocol.MaxMapIdBytes);
        w.WriteByte(3);

        ProtocolException ex = Assert.Throws<ProtocolException>(() => { BitReader r = ReaderFor(w, BattleMessage.RoomState); BattleMessages.ReadRoomState(ref r); });
        Assert.Contains("map state", ex.Message);
    }

    [Fact]
    public void Room_info_round_trips_the_map()
    {
        var info = new RoomInfoMessage(BattleProtocol.Version, "周末房", BattleMode.FreeForAll, 3, 10, true, 27015, "riverland");
        var w = new BitWriter();
        BattleMessages.Write(w, info);
        BitReader r = ReaderFor(w, BattleMessage.RoomInfo);

        Assert.Equal(info, BattleMessages.ReadRoomInfo(ref r));
    }

    [Fact]
    public void A_v2_room_info_reads_without_a_map()
    {
        var w = new BitWriter();   // the v2 layout: no map after the port
        w.WriteByte((byte)BattleMessage.RoomInfo);
        w.WriteVarUInt(2);
        w.WriteString("老房间", BattleProtocol.MaxRoomNameBytes);
        w.WriteByte((byte)BattleMode.TeamBattle);
        w.WriteVarUInt(2);
        w.WriteVarUInt(10);
        w.WriteBool(false);
        w.WriteVarUInt(27015);
        BitReader r = ReaderFor(w, BattleMessage.RoomInfo);

        RoomInfoMessage info = BattleMessages.ReadRoomInfo(ref r);

        Assert.Equal(new RoomInfoMessage(2, "老房间", BattleMode.TeamBattle, 2, 10, false, 27015, ""), info);
    }

    [Fact]
    public void A_map_id_longer_than_the_limit_is_refused()
    {
        Assert.Equal(16, BattleProtocol.MaxMapIdBytes);
        string tooLong = new('m', BattleProtocol.MaxMapIdBytes + 1);

        var w = new BitWriter();   // a room state with no players and a 17-byte map
        w.WriteByte((byte)BattleMessage.RoomState);
        w.WriteByte((byte)BattleMode.TeamBattle);
        w.WriteByte(0);
        w.WriteByte(0);
        w.WriteBool(false);
        w.WriteVarUInt(0);
        w.WriteString(tooLong);
        w.WriteByte((byte)MapState.Ready);
        Assert.Throws<ProtocolException>(() => { BitReader r = ReaderFor(w, BattleMessage.RoomState); BattleMessages.ReadRoomState(ref r); });

        w.Reset();   // a v3 room info with the same map
        w.WriteByte((byte)BattleMessage.RoomInfo);
        w.WriteVarUInt(BattleProtocol.Version);
        w.WriteString("room", BattleProtocol.MaxRoomNameBytes);
        w.WriteByte((byte)BattleMode.TeamBattle);
        w.WriteVarUInt(1);
        w.WriteVarUInt(10);
        w.WriteBool(false);
        w.WriteVarUInt(27015);
        w.WriteString(tooLong);
        Assert.Throws<ProtocolException>(() => { BitReader r = ReaderFor(w, BattleMessage.RoomInfo); BattleMessages.ReadRoomInfo(ref r); });

        w.Reset();   // a writer never sends one: it cuts the id at the limit
        BattleMessages.Write(w, new RoomInfoMessage(BattleProtocol.Version, "room", BattleMode.TeamBattle, 1, 10, false, 27015, tooLong));
        BitReader ok = ReaderFor(w, BattleMessage.RoomInfo);
        Assert.Equal(tooLong[..BattleProtocol.MaxMapIdBytes], BattleMessages.ReadRoomInfo(ref ok).Map);
    }

    [Fact]
    public void The_set_map_command_is_known_and_the_next_is_not()
    {
        Assert.Equal(7, (int)RoomCommandKind.SetMap);
        var w = new BitWriter();
        w.WriteByte((byte)BattleMessage.RoomCommand);
        w.WriteByte(7);
        w.WriteString("riverland");
        BitReader r = ReaderFor(w, BattleMessage.RoomCommand);
        Assert.Equal(new RoomCommandMessage(RoomCommandKind.SetMap, "riverland"), BattleMessages.ReadRoomCommand(ref r));

        w.Reset();
        w.WriteByte((byte)BattleMessage.RoomCommand);
        w.WriteByte(8);
        w.WriteString("riverland");
        Assert.Throws<ProtocolException>(() => { BitReader bad = ReaderFor(w, BattleMessage.RoomCommand); BattleMessages.ReadRoomCommand(ref bad); });
    }

    // ---- the server ----

    [Fact]
    public void The_host_switches_the_map_and_everyone_sees_it()
    {
        MapDefinition a = TestMaps.Small("a"), b = WithRock("b");
        var h = new BattleHarness(new BattleServerOptions { Seed = 1, Map = a, Maps = new[] { a, b } });
        BattleClient host = h.AddClient("A");
        BattleClient guest = h.AddClient("B");
        h.Run(0.3);
        Assert.True(host.IsHost);
        Assert.All(new[] { host, guest }, c => Assert.Equal("a", c.Room!.Map));

        host.SetMap("B");   // ignore case
        h.Run(0.2);

        Assert.Same(b, h.Server.RoomMap);
        foreach (BattleClient client in new[] { host, guest })
        {
            Assert.Equal("b", client.Room!.Map);
            Assert.Equal(MapState.Ready, client.Room.MapState);
        }

        h.StartMatch(BattleMode.TeamBattle);

        foreach (BattleClient client in new[] { host, guest })
        {
            Assert.Equal(BattlePhase.InMatch, client.Phase);
            Prop rock = Assert.Single(client.Field!.Props);
            Assert.Equal(PropKind.Rock, rock.Kind);
            Assert.Equal("b", client.Room!.Map);
        }
    }

    [Fact]
    public void Only_the_host_and_only_in_the_room_may_switch()
    {
        MapDefinition a = TestMaps.Small("a"), b = WithRock("b");
        var h = new BattleHarness(new BattleServerOptions { Seed = 1, Map = a, Maps = new[] { a, b } });
        BattleClient host = h.AddClient("A");
        BattleClient guest = h.AddClient("B");
        h.Run(0.3);

        guest.SetMap("b");
        h.Run(0.2);
        Assert.Same(a, h.Server.RoomMap);
        Assert.Equal("a", host.Room!.Map);

        host.SetMap("nope");
        h.Run(0.2);
        Assert.Same(a, h.Server.RoomMap);
        Assert.Equal("a", guest.Room!.Map);

        h.StartMatch(BattleMode.TeamBattle);
        Assert.True(h.Server.MatchRunning);
        host.SetMap("b");
        h.Run(0.2);
        Assert.Same(a, h.Server.RoomMap);
        Assert.Equal("a", guest.Room!.Map);
        Assert.Empty(host.Field!.Props);

        host.ReturnToRoom();
        h.Run(0.3);
        Assert.False(h.Server.MatchRunning);
        Assert.Same(a, h.Server.RoomMap);
        Assert.Equal("a", guest.Room!.Map);
    }

    [Fact]
    public void A_map_still_generating_holds_the_start()
    {
        using var gate = new ManualResetEventSlim();
        MapDefinition a = TestMaps.Small("a");
        MapDefinition g = TestMaps.Gated("g", gate, () => { });
        var log = new ConcurrentQueue<string>();
        try
        {
            var h = new BattleHarness(new BattleServerOptions { Seed = 1, Map = a, Maps = new[] { a, g }, PrepareMapsInBackground = true }, log: log.Enqueue);
            BattleClient host = h.AddClient("A");
            BattleClient guest = h.AddClient("B");
            Assert.True(WaitFor(() => h.Server.RoomMapReady, ReadyTimeout), "the first map never became ready");
            h.Run(0.3);
            Assert.Equal(MapState.Ready, host.Room!.MapState);

            host.SetMap("g");
            h.Run(0.2);
            Assert.Same(g, h.Server.RoomMap);
            Assert.Equal("g", host.Room!.Map);
            Assert.Equal(MapState.Generating, host.Room.MapState);
            Assert.Equal(MapState.Generating, guest.Room!.MapState);

            host.StartMatch();
            h.Run(0.2);
            Assert.False(h.Server.MatchRunning);
            Assert.False(host.Room!.MatchRunning);
            Assert.Contains(log, line => line == "can't start: the map is still being generated");

            gate.Set();
            Assert.True(WaitFor(() => host.Room!.MapState == MapState.Ready && guest.Room!.MapState == MapState.Ready, ReadyTimeout, () => h.Run(1.0 / 30)),
                "the room never said the map was ready");

            host.StartMatch();
            h.Run(0.5);
            Assert.True(h.Server.MatchRunning);
            Assert.True(host.Room!.MatchRunning);
            Assert.Equal(BattlePhase.InMatch, host.Phase);
        }
        finally
        {
            gate.Set();
        }
    }

    [Fact]
    public void Lan_discovery_answers_with_the_rooms_map()
    {
        MapDefinition a = TestMaps.Small("a"), b = WithRock("b");
        var h = new BattleHarness(new BattleServerOptions { Seed = 1, Map = a, Maps = new[] { a, b } });
        BattleClient host = h.AddClient("A");
        h.Run(0.3);

        RoomInfoMessage before = RoomInfoOf(h.Server);
        Assert.Equal(BattleProtocol.Version, before.ProtocolVersion);
        Assert.Equal(h.Server.RoomMap.Id, before.Map);
        Assert.Equal("a", before.Map);

        host.SetMap("b");
        h.Run(0.2);

        Assert.Equal("b", RoomInfoOf(h.Server).Map);
    }

    [Fact]
    public void Switching_back_needs_no_second_preparation()
    {
        MapDefinition a = TestMaps.Small("a"), b = WithRock("b");
        var h = new BattleHarness(new BattleServerOptions { Seed = 1, Map = a, Maps = new[] { a, b } });
        BattleClient host = h.AddClient("A");
        h.Run(0.3);
        Assert.Equal(1, h.Server.PreparedMaps.Encodings);

        host.SetMap("b");
        h.Run(0.2);
        host.SetMap("a");
        h.Run(0.2);

        Assert.Same(a, h.Server.RoomMap);
        Assert.Equal("a", host.Room!.Map);
        Assert.Equal(MapState.Ready, host.Room.MapState);
        Assert.Equal(2, h.Server.PreparedMaps.Encodings);
    }
}
