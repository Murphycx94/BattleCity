using System.Collections.Concurrent;
using System.Numerics;
using Tank.Net.Battle;
using Tank.Sim;
using Tank.Sim.Maps;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;
using Xunit.Abstractions;
using BitReader = Tank.Net.Protocol.BitReader;
using BitWriter = Tank.Net.Protocol.BitWriter;

namespace Tank.Net.Tests.Battle;

/// <summary>
/// The room's map (riverland design §5.2, stage 5 design §2): matches are played on it, with its spawns and waypoints;
/// it is generated and encoded once per server — on a worker for a real host, right away otherwise — and a room can't
/// start a match on a map that isn't ready.
/// </summary>
public sealed class ServerMapTests
{
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(30);   // generous: background generation competes with the other tests; the waits end as soon as it is ready
    private const int ResyncBudgetBytes = 10 * 1024;
    private const int RiverlandMapBytes = 500 * 1024;   // the riverland's encoding is about 542 KiB: anything above this carries it

    private readonly ITestOutputHelper _output;

    public ServerMapTests(ITestOutputHelper output) => _output = output;

    // ---- the packets a client sends ----

    private static byte[] Encode(Action<BitWriter> write)
    {
        var w = new BitWriter();
        write(w);
        return w.Data.ToArray();
    }

    private static byte[] Hello(string name, uint token = 0) => Encode(w => BattleMessages.Write(w, new HelloMessage(BattleProtocol.Version, name, token)));

    private static byte[] Command(RoomCommandKind kind) => Encode(w => BattleMessages.Write(w, new RoomCommandMessage(kind, string.Empty)));

    private static byte[] ResyncRequest(bool needMap = false) => Encode(w => BattleMessages.Write(w, new MatchStateRequest(needMap)));

    /// <summary>A server on a fake transport, its log kept safe for the map worker to write to.</summary>
    private sealed class Rig
    {
        private readonly ConcurrentQueue<string> _log = new();

        public Rig(BattleServerOptions options)
        {
            Net = new FakeServerTransport { Capture = true };
            Server = new BattleServer(Net, options, _log.Enqueue);
        }

        public FakeServerTransport Net { get; }
        public BattleServer Server { get; }
        public IReadOnlyCollection<string> Log => _log;

        public void Tick(int count = 1)
        {
            for (int i = 0; i < count; i++) Server.Tick();
        }

        public void Join(int peer, string name, uint token = 0)
        {
            Net.Connect(peer);
            Net.Receive(peer, Hello(name, token));
            Tick();
        }

        /// <summary>The newest match start sent to <paramref name="peer"/>: its size and what it says.</summary>
        public (int Bytes, MatchStartMessage Message) MatchStartTo(int peer)
        {
            byte[] packet = Net.LastTo(peer, BattleMessage.MatchStart).Data;
            var r = new BitReader(packet);
            r.ReadByte();
            return (packet.Length, BattleMessages.ReadMatchStart(ref r));
        }

        public void Send(int peer, byte[] packet, int ticks = 1)
        {
            Net.Receive(peer, packet);
            Tick(ticks);
        }
    }

    private static bool WaitFor(Func<bool> condition, TimeSpan timeout)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.Elapsed > timeout) return false;
            Thread.Sleep(5);
        }
        return true;
    }

    private static float NearestWaypoint(Vector2 p, IReadOnlyList<Vector2> waypoints) => waypoints.Min(w => Vector2.Distance(p, w));

    // ---- the room's map ----

    [Fact]
    public void The_default_room_map_is_the_sandbox()
    {
        var options = new BattleServerOptions();
        Assert.Same(MapCatalog.Sandbox, options.Map);
        Assert.False(options.PrepareMapsInBackground);

        var server = new BattleServer(new FakeServerTransport(), options);

        Assert.Same(MapCatalog.Sandbox, server.RoomMap);
        Assert.True(server.RoomMapReady);
    }

    [Fact]
    public void A_match_is_played_on_the_rooms_map()
    {
        var h = new BattleHarness(TestMaps.Options(TestMaps.Small("small")));
        BattleClient a = h.AddClient("A");
        BattleClient b = h.AddClient("B");

        h.StartMatch(BattleMode.TeamBattle);

        foreach (BattleClient client in new[] { a, b })
        {
            Assert.Equal(BattlePhase.InMatch, client.Phase);
            Assert.Equal(101, client.Field!.Terrain.SamplesX);
            Assert.Equal(200f, client.Field.Terrain.SizeX);
            Assert.Empty(client.Field.Props);
            Vector2 at = h.ServerTankOf(client).Position;
            Assert.Contains(TestMaps.TeamSpawns, s => Vector2.Distance(s.Position, at) < 0.05f);
        }
    }

    [Fact]
    public void Bots_drive_towards_the_maps_waypoints()
    {
        var log = new ConcurrentQueue<string>();
        var h = new BattleHarness(TestMaps.Options(TestMaps.Small("small")), log: log.Enqueue);
        h.AddClient("A");
        h.StartMatch(BattleMode.FreeForAll, bots: 1);   // the player and the bot start in opposite corners, out of each other's sight
        Simulation sim = h.Server.Simulation!;
        int botTank = sim.Tanks.ToArray().Single(t => t.Id != h.Server.TankOf(h.Clients[0].Slot)).Id;
        TankState TankOfBot() => sim.Tanks[sim.SlotOf(botTank)];
        Vector2 now = TankOfBot().Position;   // a second into the match: the bot may be on its way already
        Vector2 start = TestMaps.SoloSpawns.Select(s => s.Position).MinBy(p => Vector2.Distance(p, now));
        Assert.True(Vector2.Distance(start, now) < 10f, $"the bot at {now} isn't near any of the map's spawns");

        h.Run(20);

        Vector2 end = TankOfBot().Position;
        Assert.DoesNotContain(log, line => line.Contains("error", StringComparison.OrdinalIgnoreCase));
        Assert.True(Vector2.Distance(start, end) >= 20f, $"the bot only got from {start} to {end}");
        Assert.True(NearestWaypoint(end, TestMaps.Waypoints) < NearestWaypoint(start, TestMaps.Waypoints),
            $"the bot at {end} is no nearer a waypoint than at {start}");
    }

    // ---- encoded once ----

    [Fact]
    public void Preparing_a_map_builds_the_bots_navigation_grid()
    {
        // The tick thread only copies the grid at a match start (about 20–40 ms to build on the riverland in Release).
        MapDefinition map = TestMaps.Small("small");
        Assert.False(map.IsNavGridReady);

        var rig = new Rig(TestMaps.Options(map));

        Assert.True(rig.Server.RoomMapReady);
        Assert.True(map.IsNavGridReady);
    }

    [Fact]
    public void The_map_is_encoded_once_however_many_join()
    {
        var rig = new Rig(TestMaps.Options(TestMaps.Small("small")) with { Bots = 1 });
        rig.Join(1, "host");
        rig.Send(1, Command(RoomCommandKind.StartMatch));
        Assert.True(rig.Server.MatchRunning);
        rig.Join(2, "late");                                     // joins the running match
        rig.Tick(SimConstants.TickRate + 1);                     // past the resync cooldown
        rig.Send(1, ResyncRequest());
        rig.Send(1, Command(RoomCommandKind.ReturnToRoom));
        Assert.False(rig.Server.MatchRunning);
        rig.Send(1, Command(RoomCommandKind.StartMatch));        // a second match
        Assert.True(rig.Server.MatchRunning);

        int starts = rig.Net.CountTo(1, BattleMessage.MatchStart) + rig.Net.CountTo(2, BattleMessage.MatchStart);
        Assert.Equal(5, starts);                                 // host 1 + late joiner 1 + resync 1, then both again
        Assert.Equal(1, rig.Server.PreparedMaps.Encodings);
    }

    // ---- in the background and in the foreground ----

    [Fact]
    public void In_the_background_the_map_is_generated_off_the_calling_thread()
    {
        using var gate = new ManualResetEventSlim();
        int creatorThread = 0;
        MapDefinition map = TestMaps.Gated("gated", gate, () => creatorThread = Environment.CurrentManagedThreadId);
        try
        {
            var rig = new Rig(TestMaps.Options(map) with { Bots = 2, PrepareMapsInBackground = true });
            Assert.True(WaitFor(() => Volatile.Read(ref creatorThread) != 0, ReadyTimeout), "the generator never ran");
            Assert.NotEqual(Environment.CurrentManagedThreadId, creatorThread);

            Assert.False(rig.Server.RoomMapReady);
            Assert.False(rig.Server.TryStartMatch(out string? why));
            Assert.Equal("the map is still being generated", why);
            Assert.False(rig.Server.MatchRunning);

            gate.Set();
            Assert.True(WaitFor(() => rig.Server.RoomMapReady, ReadyTimeout), "the map never became ready");
            Assert.True(rig.Server.TryStartMatch(out why), why);
            // The worker logs just after it marks the map ready: wait for the line rather than race it.
            Assert.True(WaitFor(() => rig.Log.Any(line => line.StartsWith("map gated ready in ", StringComparison.Ordinal)), ReadyTimeout), "no ready line");
        }
        finally
        {
            gate.Set();
        }
    }

    [Fact]
    public void In_the_foreground_the_map_is_ready_at_once()
    {
        using var gate = new ManualResetEventSlim(initialState: true);
        int creatorThread = 0;
        MapDefinition map = TestMaps.Gated("gated", gate, () => creatorThread = Environment.CurrentManagedThreadId);

        var rig = new Rig(TestMaps.Options(map) with { Bots = 2 });

        Assert.True(rig.Server.RoomMapReady);
        Assert.Equal(Environment.CurrentManagedThreadId, creatorThread);
        Assert.Equal(1, rig.Server.PreparedMaps.Encodings);
    }

    [Fact]
    public void A_failed_generation_is_logged_and_never_ready()
    {
        using var gate = new ManualResetEventSlim();
        MapDefinition broken = TestMaps.Broken("broken", gate);
        var rig = new Rig(TestMaps.Options(broken) with { Bots = 2, PrepareMapsInBackground = true });
        rig.Join(1, "host");
        Assert.Equal(MapState.Generating, RoomStateTo(rig, 1).MapState);
        Assert.False(rig.Server.TryStartMatch(out string? early));
        Assert.Equal("the map is still being generated", early);

        gate.Set();
        Assert.True(WaitFor(() => rig.Server.RoomMapState == MapState.Failed, ReadyTimeout), "the map never failed");
        // The worker logs just after it marks the map failed: wait for the line rather than race it.
        Assert.True(WaitFor(() => rig.Log.Any(line => line.StartsWith("map broken failed", StringComparison.Ordinal)), ReadyTimeout), "no failure line");
        Assert.Contains(rig.Log, line => line.Contains("no ground", StringComparison.Ordinal));
        rig.Tick();
        Assert.Equal(MapState.Failed, RoomStateTo(rig, 1).MapState);   // the room says so: not "generating" for good
        for (int i = 0; i < 3; i++)
        {
            rig.Tick(10);
            Assert.False(rig.Server.RoomMapReady);
            Assert.Equal(MapState.Failed, rig.Server.RoomMapState);
            Assert.False(rig.Server.TryStartMatch(out string? why));
            Assert.Equal("the map failed to generate", why);
        }
        Assert.Equal(0, rig.Server.PreparedMaps.Encodings);
        Assert.Equal(MapState.Failed, RoomStateTo(rig, 1).MapState);
    }

    [Fact]
    public void A_map_that_fails_in_the_foreground_is_reported_and_the_room_goes_on()
    {
        MapDefinition small = TestMaps.Small("small"), broken = TestMaps.Broken("broken");
        var rig = new Rig(TestMaps.Options(small) with { Maps = new[] { small, broken }, Bots = 1 });
        rig.Join(1, "host");
        Assert.Equal(MapState.Ready, RoomStateTo(rig, 1).MapState);

        rig.Send(1, SetMap("broken"));                      // generated right here, inside the room command: it throws

        Assert.Same(broken, rig.Server.RoomMap);
        Assert.Equal(MapState.Failed, rig.Server.RoomMapState);
        RoomStateMessage room = RoomStateTo(rig, 1);
        Assert.Equal(("broken", MapState.Failed), (room.Map, room.MapState));
        Assert.Contains(rig.Log, line => line.StartsWith("map broken failed", StringComparison.Ordinal));
        Assert.DoesNotContain(rig.Log, line => line.StartsWith("error handling packet", StringComparison.Ordinal));
        rig.Send(1, Command(RoomCommandKind.StartMatch));
        Assert.False(rig.Server.MatchRunning);
        Assert.Contains(rig.Log, line => line == "can't start: the map failed to generate");

        rig.Send(1, SetMap("small"));                       // the room still works: back to a good map, and a match
        Assert.Equal(("small", MapState.Ready), (RoomStateTo(rig, 1).Map, RoomStateTo(rig, 1).MapState));
        rig.Send(1, Command(RoomCommandKind.StartMatch));
        Assert.True(rig.Server.MatchRunning);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_ready_map_stays_ready_when_the_log_throws(bool background)
    {
        MapDefinition small = TestMaps.Small("small");
        int lines = 0;
        var maps = new ServerMaps(background, _ =>
        {
            Interlocked.Increment(ref lines);
            throw new IOException("disk full");
        });

        Exception? thrown = Record.Exception(() => maps.Prepare(small));   // in the foreground the log's own exception reaches the caller

        Assert.True(WaitFor(() => Volatile.Read(ref lines) > 0, ReadyTimeout), "nothing was logged");
        Assert.Equal(background ? null : typeof(IOException), thrown?.GetType());
        Assert.Equal(MapState.Ready, maps.StateOf(small));   // the "ready" line threw: the map is ready all the same
        Assert.False(maps.IsFailed(small));
        Assert.Equal(1, maps.Encodings);
    }

    private static byte[] SetMap(string id) => Encode(w => BattleMessages.Write(w, new RoomCommandMessage(RoomCommandKind.SetMap, id)));

    private static RoomStateMessage RoomStateTo(Rig rig, int peer)
    {
        var r = new BitReader(rig.Net.LastTo(peer, BattleMessage.RoomState).Data);
        r.ReadByte();
        return BattleMessages.ReadRoomState(ref r);
    }

    // ---- the bytes on the wire ----

    [Fact]
    public void Riverland_match_starts_send_the_prepared_bytes()
    {
        var rig = new Rig(new BattleServerOptions { Seed = 1, Bots = 1, Map = MapCatalog.Riverland });   // one of MapCatalog.All, the default Maps
        rig.Join(1, "host");
        rig.Send(1, Command(RoomCommandKind.StartMatch));
        Assert.True(rig.Server.MatchRunning);

        byte[] packet = rig.Net.LastTo(1, BattleMessage.MatchStart).Data;
        byte[] prepared = rig.Server.PreparedMaps.Get(MapCatalog.Riverland).EncodedMap;
        var r = new BitReader(packet);
        r.ReadByte();
        MatchStartMessage start = BattleMessages.ReadMatchStart(ref r);
        var header = new BitWriter();   // what comes before the map: the type, the mode, three numbers and the flags (the map follows)
        header.WriteByte((byte)BattleMessage.MatchStart);
        header.WriteByte((byte)start.Mode);
        header.WriteVarUInt((uint)start.MatchNumber);
        header.WriteVarUInt((uint)start.CurrentTick);
        header.WriteVarUInt((uint)start.EndTick);
        header.WriteByte(1);

        Assert.True(packet.AsSpan(header.ByteLength, prepared.Length).SequenceEqual(prepared), "the map in the match start isn't the prepared bytes");
        var fresh = new BitWriter(1 << 20);
        BattleMessages.WriteMap(fresh, MapCatalog.Riverland.Data);
        Assert.True(fresh.Data.SequenceEqual(prepared), "the prepared bytes aren't the map's encoding");
        Assert.Equal(MapCatalog.Riverland.Data.HeightsCm, start.Map!.HeightsCm);
        Assert.Equal(1, rig.Server.PreparedMaps.Encodings);
    }

    // ---- a resync leaves the map out ----

    [Fact]
    public void A_resync_on_riverland_leaves_the_map_out_and_joiners_and_reconnects_still_get_it()
    {
        var rig = new Rig(new BattleServerOptions { Seed = 1, Bots = 1, Map = MapCatalog.Riverland });
        rig.Join(1, "host");
        rig.Send(1, Command(RoomCommandKind.StartMatch));
        var (startBytes, start) = rig.MatchStartTo(1);
        Assert.NotNull(start.Map);
        Assert.True(startBytes > RiverlandMapBytes, $"the match start is {startBytes} bytes");

        rig.Tick(SimConstants.TickRate + 1);   // a while into the match
        rig.Send(1, ResyncRequest());
        var (resyncBytes, resync) = rig.MatchStartTo(1);
        _output.WriteLine($"riverland: match start {startBytes} bytes, resync {resyncBytes} bytes");
        Assert.Equal(2, rig.Net.CountTo(1, BattleMessage.MatchStart));
        Assert.True(resyncBytes < ResyncBudgetBytes, $"the resync is {resyncBytes} bytes");
        Assert.Null(resync.Map);
        Assert.Equal((start.MatchNumber, start.MyTankId), (resync.MatchNumber, resync.MyTankId));
        Assert.True(resync.CurrentTick > start.CurrentTick);

        rig.Join(2, "late");                   // joins the running match: a new client, the whole map
        var (lateBytes, late) = rig.MatchStartTo(2);
        Assert.NotNull(late.Map);
        Assert.True(lateBytes > RiverlandMapBytes, $"the late joiner's match start is {lateBytes} bytes");

        uint token = WelcomeTo(rig, 1).SessionToken;
        rig.Join(3, "host", token);            // the host again, from a new connection (a restarted client): the whole map
        var (againBytes, again) = rig.MatchStartTo(3);
        Assert.NotNull(again.Map);
        Assert.True(againBytes > RiverlandMapBytes, $"the reconnect's match start is {againBytes} bytes");
        rig.Tick(SimConstants.TickRate + 1);
        rig.Send(3, ResyncRequest());           // and once it has the map, a resync on that connection leaves it out
        Assert.Null(rig.MatchStartTo(3).Message.Map);
        Assert.Equal(1, rig.Server.PreparedMaps.Encodings);
    }

    [Fact]
    public void A_client_that_says_it_lacks_the_map_gets_it_with_the_resync()
    {
        var rig = new Rig(TestMaps.Options(TestMaps.Small("small")) with { Bots = 1 });
        rig.Join(1, "host");
        rig.Send(1, Command(RoomCommandKind.StartMatch));
        rig.Tick(SimConstants.TickRate + 1);

        rig.Send(1, ResyncRequest(needMap: true));

        Assert.Equal(2, rig.Net.CountTo(1, BattleMessage.MatchStart));
        Assert.NotNull(rig.MatchStartTo(1).Message.Map);
    }

    [Fact]
    public void A_new_match_sends_the_map_again_to_everyone()
    {
        var rig = new Rig(TestMaps.Options(TestMaps.Small("small")) with { Bots = 1 });
        rig.Join(1, "host");
        rig.Send(1, Command(RoomCommandKind.StartMatch));
        rig.Send(1, Command(RoomCommandKind.ReturnToRoom));
        rig.Send(1, Command(RoomCommandKind.StartMatch));
        Assert.NotNull(rig.MatchStartTo(1).Message.Map);

        rig.Tick(SimConstants.TickRate + 1);
        rig.Send(1, ResyncRequest());

        (int _, MatchStartMessage resync) = rig.MatchStartTo(1);
        Assert.Equal(2, resync.MatchNumber);
        Assert.Null(resync.Map);
    }

    private static WelcomeMessage WelcomeTo(Rig rig, int peer)
    {
        var r = new BitReader(rig.Net.LastTo(peer, BattleMessage.Welcome).Data);
        r.ReadByte();
        return BattleMessages.ReadWelcome(ref r);
    }

    // ---- the maps a server is given ----

    [Fact]
    public void The_server_checks_its_maps()
    {
        MapDefinition small = TestMaps.Small("small"), other = TestMaps.Small("other");
        BattleServer Make(BattleServerOptions options) => new(new FakeServerTransport(), options);

        Assert.Throws<ArgumentNullException>(() => Make(new BattleServerOptions { Map = null! }));
        Assert.Throws<ArgumentNullException>(() => Make(new BattleServerOptions { Maps = null! }));
        Assert.Throws<ArgumentNullException>(() => Make(TestMaps.Options(small) with { Maps = new[] { small, null! } }));
        ArgumentException missing = Assert.Throws<ArgumentException>(() => Make(TestMaps.Options(small) with { Maps = new[] { other } }));
        Assert.Contains("small", missing.Message);
        Assert.Throws<ArgumentException>(() => Make(TestMaps.Options(small) with { Maps = new[] { small, TestMaps.Small("SMALL") } }));
        Assert.Same(small, Make(TestMaps.Options(small) with { Maps = new[] { other, small } }).RoomMap);

        string longest = new('m', BattleProtocol.MaxMapIdBytes), tooLong = longest + "m", wide = new('河', 6);   // 18 bytes in UTF-8, 6 chars
        Assert.Throws<ArgumentException>(() => Make(TestMaps.Options(TestMaps.Small(tooLong))));
        Assert.Throws<ArgumentException>(() => Make(TestMaps.Options(TestMaps.Small(wide))));
        Assert.Throws<ArgumentException>(() => Make(TestMaps.Options(small) with { Maps = new[] { small, TestMaps.Small(tooLong) } }));
        Assert.Equal(longest, Make(TestMaps.Options(TestMaps.Small(longest))).RoomMap.Id);
    }
}
