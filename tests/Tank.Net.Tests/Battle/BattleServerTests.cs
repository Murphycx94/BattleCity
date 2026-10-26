using Tank.Net.Battle;
using Tank.Sim;
using Tank.Sim.Combat;
using Tank.Sim.Modes;
using Xunit.Abstractions;
using BitReader = Tank.Net.Protocol.BitReader;
using BitWriter = Tank.Net.Protocol.BitWriter;

namespace Tank.Net.Tests.Battle;

/// <summary>
/// The battle server driven through a <see cref="FakeServerTransport"/>, one tick at a time: what it allocates in a
/// match, how it resyncs, refuses a start, hands a seat to a new connection, keeps a match alive for a dropped player
/// and treats a peer that never speaks.
/// </summary>
public sealed class BattleServerTests
{
    private const int HelloTimeoutTicks = 3 * SimConstants.TickRate;   // the server's own constant is private

    private readonly ITestOutputHelper _output;

    public BattleServerTests(ITestOutputHelper output) => _output = output;

    // ---- the packets a client sends ----

    private static byte[] Encode(Action<BitWriter> write)
    {
        var w = new BitWriter();
        write(w);
        return w.Data.ToArray();
    }

    private static byte[] Hello(string name, uint token = 0) => Encode(w => BattleMessages.Write(w, new HelloMessage(BattleProtocol.Version, name, token)));

    private static byte[] Command(RoomCommandKind kind, string arg = "") => Encode(w => BattleMessages.Write(w, new RoomCommandMessage(kind, arg)));

    private static byte[] ResyncRequest() => Encode(w => BattleMessages.Write(w, new MatchStateRequest(NeedMap: false)));

    private static byte[] Input(int tick, int ack, sbyte throttle = 1, sbyte steer = 0, float yaw = 0f, bool fire = false) => Encode(w =>
    {
        var inputs = new InputCmd[3];
        for (int i = 0; i < inputs.Length; i++) inputs[i] = new InputCmd(tick - i, throttle, steer, yaw, 0f, fire && i == 0, AmmoType.AP);
        BattleInputPacket.Write(w, ack, inputs);
    });

    // ---- the rig ----

    private sealed class Rig
    {
        public Rig(BattleServerOptions options)
        {
            Net = new FakeServerTransport { Capture = true };
            Server = new BattleServer(Net, options, Log.Add);
        }

        public FakeServerTransport Net { get; }
        public BattleServer Server { get; }
        public List<string> Log { get; } = new();

        public void Tick(int count = 1)
        {
            for (int i = 0; i < count; i++) Server.Tick();
        }

        /// <summary>Connects <paramref name="peer"/>, says hello and ticks once; returns the welcome it got.</summary>
        public WelcomeMessage Join(int peer, string name, uint token = 0)
        {
            Net.Connect(peer);
            Net.Receive(peer, Hello(name, token));
            Tick();
            return WelcomeOf(peer);
        }

        public void Send(int peer, byte[] packet, int ticks = 1)
        {
            Net.Receive(peer, packet);
            Tick(ticks);
        }

        public WelcomeMessage WelcomeOf(int peer) => Read(Net.LastTo(peer, BattleMessage.Welcome), BattleMessages.ReadWelcome);

        public RoomStateMessage RoomOf(int peer) => Read(Net.LastTo(peer, BattleMessage.RoomState), BattleMessages.ReadRoomState);

        public MatchStartMessage MatchStartOf(int peer) => Read(Net.LastTo(peer, BattleMessage.MatchStart), BattleMessages.ReadMatchStart);

        public void StartMatch(int hostPeer) => Send(hostPeer, Command(RoomCommandKind.StartMatch));

        private delegate T Reader<out T>(ref BitReader r);

        private static T Read<T>(SentPacket packet, Reader<T> read)
        {
            var r = new BitReader(packet.Data);
            r.ReadByte();
            return read(ref r);
        }
    }

    private static int SeatsInRoom(Rig rig, int peer) => rig.RoomOf(peer).Players.Count;

    // ---- a. no allocation while a match runs ----

    [Theory]
    [InlineData(1, 3)]
    [InlineData(2, 3)]
    public void A_running_match_allocates_nothing_per_tick(int humans, int bots)
    {
        Allocation.AssertNone(() =>
        {
            const int WarmUpTicks = 60, MeasuredTicks = 300;
            var rig = new Rig(new BattleServerOptions { Bots = bots, Seed = 1 });
            for (int h = 1; h <= humans; h++) rig.Join(h, "p" + h);
            rig.StartMatch(1);
            Assert.True(rig.Server.MatchRunning);
            Assert.Equal(humans + bots, rig.Server.Simulation!.Tanks.Length);
            rig.Net.Capture = false;   // copying packets for inspection allocates; counting them doesn't

            // Every input packet, for every tick the loop will run, is built before anything is measured.
            int firstTick = rig.Server.Simulation!.Tick + 1;
            int total = WarmUpTicks + MeasuredTicks;
            var packets = new byte[humans][][];
            for (int h = 0; h < humans; h++)
            {
                packets[h] = new byte[total][];
                for (int i = 0; i < total; i++)
                    packets[h][i] = Input(firstTick + i, ack: System.Math.Max(0, firstTick + i - 2), steer: (sbyte)((i / 40 % 3) - 1), yaw: (i % 100) / 20f, fire: i % 15 == 0);
            }

            for (int i = 0; i < WarmUpTicks; i++)
            {
                for (int h = 0; h < humans; h++) rig.Net.Receive(h + 1, packets[h][i]);
                rig.Server.Tick();
            }

            Allocation.Window window = Allocation.Start();
            for (int i = WarmUpTicks; i < total; i++)
            {
                for (int h = 0; h < humans; h++) rig.Net.Receive(h + 1, packets[h][i]);
                rig.Server.Tick();
            }
            Allocation.Measured measured = window.Stop();

            _output.WriteLine($"{humans} humans + {bots} bots: {measured.Bytes} bytes allocated over {MeasuredTicks} ticks");
            Assert.True(rig.Server.MatchRunning);
            for (int h = 1; h <= humans; h++) Assert.True(rig.Net.CountTo(h, BattleMessage.Snapshot) >= total, $"peer {h} got {rig.Net.CountTo(h, BattleMessage.Snapshot)} snapshots");
            return measured;
        });
    }

    [Fact]
    public void A_running_standard_match_with_bots_allocates_nothing_per_tick()
    {
        Allocation.AssertNone(() =>
        {
            const int WarmUpTicks = 60, MeasuredTicks = 300, Bots = 3;
            var rig = new Rig(TestMaps.Options(TestMaps.Standard("s")) with { Mode = BattleMode.Standard, Bots = Bots });
            rig.Join(1, "p1");
            rig.StartMatch(1);
            Assert.True(rig.Server.MatchRunning);
            Assert.Equal(1 + Bots, rig.Server.Simulation!.Tanks.Length);
            rig.Net.Capture = false;   // copying packets for inspection allocates; counting them doesn't

            // Straight east from team 0's spawns (x 40) through team 1's base (x 50–100): the capture is counted and sent.
            int firstTick = rig.Server.Simulation!.Tick + 1;
            var packets = new byte[WarmUpTicks + MeasuredTicks][];
            for (int i = 0; i < packets.Length; i++)
                packets[i] = Input(firstTick + i, ack: System.Math.Max(0, firstTick + i - 2), fire: i % 15 == 0);

            for (int i = 0; i < WarmUpTicks; i++)
            {
                rig.Net.Receive(1, packets[i]);
                rig.Server.Tick();
            }

            Allocation.Window window = Allocation.Start();
            for (int i = WarmUpTicks; i < packets.Length; i++)
            {
                rig.Net.Receive(1, packets[i]);
                rig.Server.Tick();
            }
            Allocation.Measured measured = window.Stop();

            _output.WriteLine($"standard, 1 human + {Bots} bots: {measured.Bytes} bytes allocated over {MeasuredTicks} ticks");
            Assert.True(rig.Server.MatchRunning);
            Assert.True(rig.Server.Rules!.CaptureBest(0) > 0, "the human never counted in the enemy base");
            Assert.True(rig.Net.CountTo(1, BattleMessage.Snapshot) >= packets.Length);
            return measured;
        });
    }

    // ---- b. resync ----

    [Fact]
    public void A_resync_request_gets_the_match_start_again_and_a_snapshot_with_no_baseline()
    {
        var rig = new Rig(new BattleServerOptions { Bots = 1, Seed = 1 });
        rig.Join(1, "a");
        rig.StartMatch(1);
        int tick = rig.Server.Simulation!.Tick;
        for (int i = 0; i < 20; i++) rig.Send(1, Input(++tick, ack: tick - 1));   // acking every snapshot: deltas
        Assert.NotEqual(0, BaseTickOf(rig.Net.LastTo(1, BattleMessage.Snapshot)));
        int starts = rig.Net.CountTo(1, BattleMessage.MatchStart);
        int snapshots = rig.Net.CountTo(1, BattleMessage.Snapshot);

        // The request, with an input that still acks a snapshot from before it (one that was in flight): no delta against that.
        rig.Net.Receive(1, ResyncRequest());
        rig.Send(1, Input(++tick, ack: tick - 1));
        Assert.Equal(starts + 1, rig.Net.CountTo(1, BattleMessage.MatchStart));
        Assert.Equal(snapshots + 1, rig.Net.CountTo(1, BattleMessage.Snapshot));
        SentPacket first = rig.Net.LastTo(1, BattleMessage.Snapshot);
        Assert.Equal(0, BaseTickOf(first));
        Assert.Equal(rig.Server.Simulation.Tick, TickOf(first));
        Assert.True(rig.Net.Sent.IndexOf(rig.Net.LastTo(1, BattleMessage.MatchStart)) < rig.Net.Sent.IndexOf(first), "the match start comes first");
        Assert.Equal(rig.Server.TankOf(0), rig.MatchStartOf(1).MyTankId);
        DecodeWithoutBaseline(first);   // a client that threw its snapshots away can read it

        // Asking again at once is ignored (a flood of requests must not become a flood of 60 KB maps).
        rig.Send(1, ResyncRequest());
        Assert.Equal(starts + 1, rig.Net.CountTo(1, BattleMessage.MatchStart));

        // Once the client acks a snapshot of the new chain, deltas resume.
        rig.Send(1, Input(++tick, ack: tick - 1), ticks: 2);
        Assert.NotEqual(0, BaseTickOf(rig.Net.LastTo(1, BattleMessage.Snapshot)));
    }

    [Fact]
    public void A_resync_in_a_standard_match_without_the_map_still_carries_the_bases()
    {
        var rig = new Rig(TestMaps.Options(TestMaps.Standard("s")) with { Mode = BattleMode.Standard, Bots = 1 });
        rig.Join(1, "a");
        rig.StartMatch(1);
        Assert.True(rig.Server.MatchRunning);
        Assert.Equal(TestMaps.StandardBases, rig.MatchStartOf(1).Bases);
        rig.Tick(SimConstants.TickRate);   // past the resync cooldown
        int starts = rig.Net.CountTo(1, BattleMessage.MatchStart);

        rig.Send(1, ResyncRequest());

        Assert.Equal(starts + 1, rig.Net.CountTo(1, BattleMessage.MatchStart));
        MatchStartMessage again = rig.MatchStartOf(1);
        Assert.Null(again.Map);   // this connection has the map
        Assert.Equal(TestMaps.StandardBases, again.Bases);
        Assert.Equal(BattleMode.Standard, again.Mode);
    }

    private static int TickOf(SentPacket snapshot)
    {
        var r = new BitReader(snapshot.Data);
        r.ReadByte();
        return (int)r.ReadVarUInt();
    }

    private static int BaseTickOf(SentPacket snapshot)
    {
        var r = new BitReader(snapshot.Data);
        r.ReadByte();
        r.ReadVarUInt();
        return (int)r.ReadVarUInt();
    }

    private static void DecodeWithoutBaseline(SentPacket snapshot)
    {
        var r = new BitReader(snapshot.Data);
        r.ReadByte();
        new BattleSnapshotCodec().Read(ref r, _ => null, new BattleSnapshot(), out _, out int baseTick);
        Assert.Equal(0, baseTick);
    }

    // ---- c. the start guard ----

    [Fact]
    public void A_refused_start_leaves_the_room_as_it_was_and_seats_no_bots()
    {
        // Nobody seated, one bot asked for: the attempt seats it, finds a single tank and must take it out again.
        var rig = new Rig(new BattleServerOptions { Bots = 1, Seed = 1 });
        Assert.False(rig.Server.TryStartMatch(out string? reason));
        Assert.Equal("fewer than two tanks", reason);
        Assert.False(rig.Server.MatchRunning);
        rig.Join(1, "a");
        Assert.Equal(new[] { (byte)0 }, rig.RoomOf(1).Players.Select(p => p.Slot).ToArray());   // only the human: no bot was left behind

        // One human, no bots: the host's start is refused and nothing in the room state changes.
        var solo = new Rig(new BattleServerOptions { Seed = 1 });
        solo.Join(1, "a");
        RoomStateMessage before = solo.RoomOf(1);
        solo.StartMatch(1);
        Assert.False(solo.Server.MatchRunning);
        Assert.Contains(solo.Log, l => l.StartsWith("can't start: fewer than two tanks"));
        RoomStateMessage after = solo.RoomOf(1);
        Assert.Equal((before.Mode, before.HostSlot, before.Bots, before.MatchRunning), (after.Mode, after.HostSlot, after.Bots, after.MatchRunning));
        Assert.Equal(before.Players, after.Players);
        Assert.Equal(0, solo.Net.CountTo(1, BattleMessage.MatchStart));
    }

    [Fact]
    public void A_start_with_an_empty_team_or_from_a_guest_is_refused_and_a_valid_one_then_works()
    {
        var rig = new Rig(new BattleServerOptions { Mode = BattleMode.TeamBattle, Seed = 1 });
        rig.Join(1, "host");
        rig.Join(2, "guest");
        rig.Send(2, Command(RoomCommandKind.SetTeam, "0"));
        rig.Send(1, Command(RoomCommandKind.SetTeam, "0"));
        Assert.All(rig.RoomOf(1).Players, p => Assert.Equal(0, p.Team));
        RoomStateMessage before = rig.RoomOf(1);

        rig.StartMatch(1);   // both on team 0
        Assert.False(rig.Server.MatchRunning);
        Assert.Contains(rig.Log, l => l.StartsWith("can't start: a team is empty"));
        rig.StartMatch(2);   // not the host
        Assert.False(rig.Server.MatchRunning);
        Assert.Equal(before.Players, rig.RoomOf(1).Players);
        Assert.Equal(0, rig.Net.CountTo(1, BattleMessage.MatchStart) + rig.Net.CountTo(2, BattleMessage.MatchStart));
        Assert.Equal(2, rig.Server.ConnectedCount);

        rig.Send(2, Command(RoomCommandKind.SetTeam, "1"));
        rig.StartMatch(1);
        Assert.True(rig.Server.MatchRunning);
        Assert.Equal(1, rig.Net.CountTo(1, BattleMessage.MatchStart));
        Assert.False(rig.Server.TryStartMatch(out string? reason));
        Assert.Equal("a match is running", reason);
    }

    // ---- d. a reconnect takes the seat over ----

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void A_new_connection_with_a_seats_token_takes_it_over_while_the_old_one_is_still_up(bool owner, bool echoKicks)
    {
        const uint OwnerToken = 777;
        var rig = new Rig(new BattleServerOptions { Seed = 1, Bots = 1, OwnerToken = owner ? OwnerToken : null });
        rig.Net.EchoKicks = echoKicks;
        WelcomeMessage first = rig.Join(1, "a", owner ? OwnerToken : 5);   // a non-owner's own token is the server's to hand out
        rig.Join(9, "other");
        Assert.Equal(2, rig.Server.ConnectedCount);
        Assert.Equal(2, SeatsInRoom(rig, 9));
        if (owner) Assert.Equal(OwnerToken, first.SessionToken);

        WelcomeMessage second = rig.Join(2, "a again", first.SessionToken);
        Assert.Equal(first, second);                          // same seat, same token
        Assert.Equal(2, rig.Server.ConnectedCount);           // the old connection no longer counts
        Assert.Equal(2, SeatsInRoom(rig, 2));                 // and no third seat appeared
        Assert.Equal(new[] { 1 }, rig.Net.Kicked);            // the old peer was dropped, once

        // Bound to the new peer: its commands count (and its readiness shows), the old peer's are ignored, and only it gets the room.
        int roomsToOld = rig.Net.CountTo(1, BattleMessage.RoomState);
        rig.Send(2, Command(RoomCommandKind.SetReady, "1"));
        rig.Send(1, Command(RoomCommandKind.SetReady, "0"));
        RoomStateMessage room = rig.RoomOf(2);
        Assert.True(room.Players.Single(p => p.Slot == first.Slot).Ready);
        Assert.Equal(roomsToOld, rig.Net.CountTo(1, BattleMessage.RoomState));

        // The old connection's disconnect arrives late (or, with echoing, already did): the rebound seat is untouched.
        if (!echoKicks) rig.Net.Drop(1);
        rig.Tick(3);
        Assert.Contains(1, rig.Net.Disconnected);
        Assert.Equal(2, rig.Server.ConnectedCount);
        Assert.Equal(new[] { 1 }, rig.Net.Kicked);
        int roomsToNew = rig.Net.CountTo(2, BattleMessage.RoomState);
        rig.Send(2, Command(RoomCommandKind.SetReady, "0"));
        Assert.False(rig.RoomOf(2).Players.Single(p => p.Slot == first.Slot).Ready);
        Assert.True(rig.Net.CountTo(2, BattleMessage.RoomState) > roomsToNew);
        Assert.True(rig.RoomOf(2).Players.Single(p => p.Slot == first.Slot).Connected);
        Assert.Equal(2, SeatsInRoom(rig, 2));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_takeover_during_a_match_keeps_the_tank_and_resumes_the_snapshots(bool owner)
    {
        const uint OwnerToken = 777;
        var rig = new Rig(new BattleServerOptions { Seed = 1, Bots = 2, OwnerToken = owner ? OwnerToken : null });
        WelcomeMessage welcome = rig.Join(1, "a", owner ? OwnerToken : 5);
        rig.StartMatch(1);
        Assert.True(rig.Server.MatchRunning);
        int tank = rig.Server.TankOf(welcome.Slot);
        Assert.NotEqual(0, tank);
        int tanks = rig.Server.Simulation!.Tanks.Length;
        rig.Tick(10);
        int oldSnapshots = rig.Net.CountTo(1, BattleMessage.Snapshot);

        rig.Join(2, "a again", welcome.SessionToken);
        rig.Tick(10);
        Assert.Equal(new[] { 1 }, rig.Net.Kicked);
        Assert.Equal(tank, rig.Server.TankOf(welcome.Slot));
        Assert.Equal(tanks, rig.Server.Simulation.Tanks.Length);
        Assert.Equal(1, rig.Server.ConnectedCount);
        Assert.Equal(tank, rig.MatchStartOf(2).MyTankId);   // the new connection is told which tank is its own
        Assert.Equal(oldSnapshots, rig.Net.CountTo(1, BattleMessage.Snapshot));
        Assert.True(rig.Net.CountTo(2, BattleMessage.Snapshot) >= 10);
        Assert.True(rig.Server.MatchRunning);

        rig.Net.Drop(1);   // the dead connection finally times out
        rig.Tick(5);
        Assert.Equal(tank, rig.Server.TankOf(welcome.Slot));
        Assert.Equal(1, rig.Server.ConnectedCount);
    }

    // ---- e. the last human leaves ----

    [Fact]
    public void A_match_outlives_its_last_human_for_the_grace_period_and_ends_when_nobody_returns()
    {
        const int GraceSeconds = 2;
        var rig = new Rig(new BattleServerOptions { Bots = 3, Seed = 1, ReconnectGraceSeconds = GraceSeconds });
        WelcomeMessage welcome = rig.Join(1, "a");
        rig.StartMatch(1);
        rig.Tick(30);
        Assert.True(rig.Server.MatchRunning);

        rig.Net.Drop(1);
        rig.Tick(SimConstants.TickRate);   // a second away, a second of grace left
        Assert.True(rig.Server.MatchRunning, "the match goes on while its player is away");
        Assert.Equal(0, rig.Server.ConnectedCount);
        int tank = rig.Server.TankOf(welcome.Slot);
        Assert.NotEqual(0, tank);

        rig.Join(2, "a", welcome.SessionToken);
        rig.Tick(10);
        Assert.Equal(1, rig.Server.ConnectedCount);
        Assert.Equal(tank, rig.Server.TankOf(welcome.Slot));
        Assert.True(rig.Net.CountTo(2, BattleMessage.Snapshot) >= 10, "the returning player gets snapshots");
        Assert.Equal(tank, rig.MatchStartOf(2).MyTankId);

        rig.Net.Drop(2);
        rig.Tick(GraceSeconds * SimConstants.TickRate + 10);   // nobody comes back
        Assert.False(rig.Server.MatchRunning);
        Assert.Contains(rig.Log, l => l.Contains("reconnect window expired"));

        WelcomeMessage stranger = rig.Join(3, "late", welcome.SessionToken);   // the seat is gone: this is a new one, on a new token
        Assert.NotEqual(welcome.SessionToken, stranger.SessionToken);
        Assert.Equal(new[] { stranger.Slot }, rig.RoomOf(3).Players.Select(p => p.Slot).ToArray());   // no bots, no ghost
    }

    // ---- f. a peer that never speaks ----

    [Fact]
    public void A_peer_that_never_says_hello_is_kicked_once_within_the_timeout()
    {
        var rig = new Rig(new BattleServerOptions());
        rig.Net.EchoKicks = true;
        rig.Net.Connect(5);
        rig.Net.Connect(6);
        rig.Tick(2);
        rig.Net.Receive(6, Hello("talker"));
        rig.Tick(HelloTimeoutTicks - 10);
        Assert.Empty(rig.Net.Kicked);   // not yet

        rig.Tick(15);                   // past the timeout, with the tick the kick is queued for
        Assert.Equal(new[] { 5 }, rig.Net.Kicked);
        rig.Tick(HelloTimeoutTicks * 2);
        Assert.Equal(new[] { 5 }, rig.Net.Kicked);   // once, however long the transport takes to let go
        Assert.Equal(new[] { 5 }, rig.Net.Disconnected);
        Assert.Equal(1, rig.Server.ConnectedCount);   // the one that spoke is still seated
    }
}
