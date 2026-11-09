using System.Numerics;
using Tank.Net.Battle;
using Tank.Sim.Maps;
using Tank.Sim.Modes;
using BitReader = Tank.Net.Protocol.BitReader;
using BitWriter = Tank.Net.Protocol.BitWriter;
using ProtocolException = Tank.Net.Protocol.ProtocolException;

namespace Tank.Net.Tests.Battle;

/// <summary>
/// Protocol v4 (stage 5 design §3.3): the standard battle on the wire — the mode in the room, the bases' progress and
/// the end reason in the match block, each player's capture on the scoreboard, the bases in the match start; every new
/// field bounds-checked, every truncation refused.
/// </summary>
public sealed class StandardWireTests
{
    private const int BaseBytes = 1 + 3 * 4;   // team, x, z, radius

    [Fact]
    public void A_standard_match_start_takes_two_bases_at_most()
    {
        Assert.Equal(2, BattleMessages.MaxBases);   // the version is asserted in RoyaleWireTests
    }

    // ---- the mode ----

    [Fact]
    public void Standard_mode_reads_in_the_room_and_the_match_start()
    {
        var w = new BitWriter();
        var players = new[] { new RoomPlayer(0, "安娜", 0, 1, true, true, false), new RoomPlayer(1, "Bot 1", 1, 2, true, true, true) };
        BattleMessages.Write(w, new RoomStateMessage(BattleMode.Standard, 0, 1, false, players, RiverlandMap.Id, MapState.Ready));
        BitReader r = Reader(w.Data.ToArray(), BattleMessage.RoomState);
        RoomStateMessage room = BattleMessages.ReadRoomState(ref r);
        Assert.Equal((BattleMode.Standard, (byte)0, (byte)1, false, RiverlandMap.Id, MapState.Ready),
            (room.Mode, room.HostSlot, room.Bots, room.MatchRunning, room.Map, room.MapState));
        Assert.Equal(players, room.Players);

        var info = new RoomInfoMessage(4, "周末房", BattleMode.Standard, 3, 10, true, 27015, RiverlandMap.Id);
        Assert.Equal(info, ReadRoomInfo(Encode(w2 => BattleMessages.Write(w2, info))));

        var start = new MatchStartMessage(BattleMode.Standard, 2, 150, 18150, null, Array.Empty<PropState>(), 5, RiverlandMap.Bases);
        MatchStartMessage back = ReadMatchStart(Encode(w2 => BattleMessages.Write(w2, start)));
        Assert.Equal((start.Mode, start.MatchNumber, start.CurrentTick, start.EndTick, start.MyTankId),
            (back.Mode, back.MatchNumber, back.CurrentTick, back.EndTick, back.MyTankId));

        byte[] unknown = w.Data.ToArray();
        unknown[1] = (byte)ModeRules.LastMode + 1;   // the mode byte right after the type
        Assert.Throws<ProtocolException>(() => { BitReader rr = Reader(unknown, BattleMessage.RoomState); BattleMessages.ReadRoomState(ref rr); });
    }

    [Fact]
    public void A_room_of_another_version_with_a_mode_we_do_not_know_is_still_listed()
    {
        RoomInfoMessage other = ReadRoomInfo(HandMadeRoomInfo(version: BattleProtocol.Version + 1, mode: 9));
        Assert.Equal(BattleProtocol.Version + 1, other.ProtocolVersion);
        Assert.Equal(9, (byte)other.Mode);
        Assert.Equal(("老房", 2, 10, 27016, "riverland"), (other.RoomName, other.Players, other.MaxPlayers, other.Port, other.Map));

        Assert.Throws<ProtocolException>(() => ReadRoomInfo(HandMadeRoomInfo(version: BattleProtocol.Version, mode: 9)));
        Assert.Throws<ProtocolException>(() => ReadRoomInfo(HandMadeRoomInfo(version: BattleProtocol.Version, mode: (byte)ModeRules.LastMode + 1)));
        Assert.Equal(BattleMode.Standard, ReadRoomInfo(HandMadeRoomInfo(version: BattleProtocol.Version, mode: (byte)BattleMode.Standard)).Mode);
    }

    // ---- the match block ----

    [Fact]
    public void The_match_block_carries_both_bases_and_the_end_reason()
    {
        var codec = new BattleSnapshotCodec();
        BattleSnapshot a = StandardSnapshot(300);
        a.Match.BaseProgress0 = 37;
        a.Match.BaseProgress1 = 100;
        a.Match.EndReason = MatchEndReason.Captured;

        BattleSnapshot full = RoundTrip(codec, a, null, null);
        Assert.Equal(a.Match, full.Match);
        Assert.Equal(((byte)37, (byte)100, MatchEndReason.Captured), (full.Match.BaseProgress0, full.Match.BaseProgress1, full.Match.EndReason));

        BattleSnapshot b = StandardSnapshot(301);
        b.Match = a.Match with { BaseProgress0 = 38, EndReason = MatchEndReason.TimeUp };
        BattleSnapshot delta = RoundTrip(codec, b, a, full);
        Assert.Equal(b.Match, delta.Match);

        foreach (MatchEndReason reason in Enum.GetValues<MatchEndReason>())
        {
            BattleSnapshot c = StandardSnapshot(302);
            c.Match.EndReason = reason;
            Assert.Equal(reason, RoundTrip(codec, c, null, null).Match.EndReason);
        }

        Assert.False(a.Match.Equals(a.Match with { BaseProgress1 = 99 }));
        Assert.False(a.Match.Equals(a.Match with { BaseProgress0 = 36 }));
        Assert.False(a.Match.Equals(a.Match with { EndReason = MatchEndReason.Eliminated }));
        Assert.True(a.Match.Equals(a.Match with { }));
    }

    [Fact]
    public void A_base_progress_over_100_is_refused()
    {
        Decode(HandMadeSnapshot(progress0: 100, progress1: 100));   // the helper writes a valid snapshot
        foreach ((byte p0, byte p1) in new[] { ((byte)101, (byte)0), ((byte)0, (byte)101), ((byte)255, (byte)255) })
        {
            ProtocolException ex = Assert.Throws<ProtocolException>(() => Decode(HandMadeSnapshot(progress0: p0, progress1: p1)));
            Assert.Contains("Base progress", ex.Message);
        }
    }

    [Fact]
    public void The_writer_never_sends_a_base_progress_over_100()
    {
        BattleSnapshot s = StandardSnapshot(300);
        s.Match.BaseProgress0 = 101;
        s.Match.BaseProgress1 = 200;

        BattleSnapshot back = RoundTrip(new BattleSnapshotCodec(), s, null, null);

        Assert.Equal(((byte)100, (byte)100), (back.Match.BaseProgress0, back.Match.BaseProgress1));
    }

    // ---- the scoreboard ----

    [Fact]
    public void The_scoreboard_carries_each_players_capture()
    {
        var codec = new BattleSnapshotCodec();
        BattleSnapshot a = StandardSnapshot(300);
        a.Players[0].Capture = 33;
        a.Players[4].Capture = 100;
        BattleSnapshot full = RoundTrip(codec, a, null, null);
        Assert.Equal(a.Players, full.Players);
        Assert.Equal(((byte)33, (byte)100), (full.Players[0].Capture, full.Players[4].Capture));

        // Only a capture changes: the encoder sees the scoreboard changed and sends it whole.
        BattleSnapshot b = StandardSnapshot(301);
        Array.Copy(a.Players, b.Players, a.Players.Length);
        b.Players[0].Capture = 34;
        Assert.False(a.Players[0].Equals(b.Players[0]));
        BattleSnapshot delta = RoundTrip(codec, b, a, full);
        Assert.Equal(34, delta.Players[0].Capture);
        Assert.Equal(b.Players, delta.Players);

        Decode(HandMadeSnapshot(capture: 100));
        foreach (byte capture in new byte[] { 101, 127 })
            Assert.Throws<ProtocolException>(() => Decode(HandMadeSnapshot(capture: capture)));
    }

    [Fact]
    public void The_writer_never_sends_a_capture_over_100()
    {
        BattleSnapshot s = StandardSnapshot(300);
        s.Players[0].Capture = 101;
        s.Players[4].Capture = 200;   // 7 bits would have cut it to 72

        BattleSnapshot back = RoundTrip(new BattleSnapshotCodec(), s, null, null);

        Assert.Equal(((byte)100, (byte)100), (back.Players[0].Capture, back.Players[4].Capture));
    }

    [Fact]
    public void A_standard_snapshot_cut_short_anywhere_is_refused()
    {
        BattleSnapshot s = StandardSnapshot(300);
        s.Match.BaseProgress0 = 99;
        s.Match.BaseProgress1 = 37;
        s.Match.EndReason = MatchEndReason.Eliminated;
        s.Players[0].Capture = 100;
        var w = new BitWriter();
        new BattleSnapshotCodec().Write(w, s, null, default);
        byte[] whole = w.Data.ToArray();
        Decode(whole);
        for (int len = 1; len < whole.Length; len++)
        {
            byte[] cut = whole[..len];
            Assert.Throws<ProtocolException>(() => Decode(cut));
        }
    }

    // ---- the match start ----

    [Fact]
    public void The_match_start_carries_the_bases()
    {
        MapData map = MapData.Of(SandboxMap.Create());
        PropState[] props = { new(300, 0), new(301, 128) };
        var m = new MatchStartMessage(BattleMode.Standard, 3, 150, 18150, map, props, 7, RiverlandMap.Bases);

        byte[] bytes = Encode(w => BattleMessages.Write(w, m));
        MatchStartMessage back = ReadMatchStart(bytes);
        Assert.Equal(9 + 53053 + 7 + 1 + 1 + 2 * BaseBytes + 1 + 1, bytes.Length);   // header and flags · map · two prop states · tank · two bases · no zone · no crates: 53099
        Assert.Equal(2, back.Bases!.Count);
        for (int i = 0; i < 2; i++)
        {
            TeamBase sent = RiverlandMap.Bases[i], got = back.Bases[i];
            Assert.Equal(sent.Team, got.Team);
            Assert.Equal(BitConverter.SingleToUInt32Bits(sent.Centre.X), BitConverter.SingleToUInt32Bits(got.Centre.X));
            Assert.Equal(BitConverter.SingleToUInt32Bits(sent.Centre.Y), BitConverter.SingleToUInt32Bits(got.Centre.Y));
            Assert.Equal(BitConverter.SingleToUInt32Bits(sent.Radius), BitConverter.SingleToUInt32Bits(got.Radius));
        }
        Assert.Equal(m.Props, back.Props);
        Assert.Equal(7, back.MyTankId);

        // Without bases: one byte, read back as an empty array (never null).
        byte[] none = Encode(w => BattleMessages.Write(w, m with { Bases = null }));
        Assert.Equal(bytes.Length - 2 * BaseBytes, none.Length);
        Assert.Equal(0, none[^3]);   // the base count; the ring count and the crate count (none) follow
        MatchStartMessage noneBack = ReadMatchStart(none);
        Assert.NotNull(noneBack.Bases);
        Assert.Empty(noneBack.Bases);
        Assert.Equal(none, Encode(w => BattleMessages.Write(w, m with { Bases = Array.Empty<TeamBase>() })));

        // The prepared map writes the same bytes, and the writer is grown once, to what the message takes.
        var encodedMap = new BitWriter(128 * 1024);
        BattleMessages.WriteMap(encodedMap, map);
        var w2 = new BitWriter(16);
        BattleMessages.Write(w2, m, encodedMap.Data);
        Assert.Equal(bytes, w2.Data.ToArray());
        Assert.InRange(w2.Capacity, w2.ByteLength, w2.ByteLength + 64);
    }

    [Fact]
    public void A_match_start_with_bad_bases_is_not_written()
    {
        var m = new MatchStartMessage(BattleMode.Standard, 1, 5, 900, null, Array.Empty<PropState>(), 2,
            new[] { new TeamBase(0, Vector2.Zero, 25f), new TeamBase(0, Vector2.One, 25f) });
        Assert.Throws<ArgumentException>(() => BattleMessages.Write(new BitWriter(), m));
        Assert.Throws<ArgumentException>(() => BattleMessages.Write(new BitWriter(), m with { Bases = new[] { new TeamBase(0, Vector2.Zero, 25f) } }));
    }

    /// <summary>Bases the reader would refuse: a broken pair, and centres beyond the wire's ±100 km.</summary>
    public static IEnumerable<object[]> UnwritableBases()
    {
        static TeamBase[] Pair(Vector2 centre1) => new[] { new TeamBase(0, new Vector2(100, 200), 25f), new TeamBase(1, centre1, 25f) };
        yield return new object[] { "two for team 0", new[] { new TeamBase(0, Vector2.Zero, 25f), new TeamBase(0, Vector2.One, 25f) } };
        yield return new object[] { "x 1e7 m", Pair(new Vector2(1e7f, 1f)) };
        yield return new object[] { "z -1e7 m", Pair(new Vector2(1f, -1e7f)) };
        yield return new object[] { "x just past 100 km", Pair(new Vector2(100_001f, 1f)) };
    }

    [Theory]
    [MemberData(nameof(UnwritableBases))]
    public void A_match_start_with_bases_the_reader_would_refuse_writes_nothing(string what, TeamBase[] bases)
    {
        Assert.NotNull(what);
        MapData map = MapData.Of(SandboxMap.Create());
        var m = new MatchStartMessage(BattleMode.Standard, 1, 5, 900, null, new[] { new PropState(300, 0) }, 2, bases);
        var encodedMap = new BitWriter(128 * 1024);
        BattleMessages.WriteMap(encodedMap, map);

        var plain = new BitWriter();
        Assert.Throws<ArgumentException>(() => BattleMessages.Write(plain, m));
        Assert.Equal(0, plain.ByteLength);
        var withMap = new BitWriter();
        Assert.Throws<ArgumentException>(() => BattleMessages.Write(withMap, m with { Map = map }, encodedMap.Data));
        Assert.Equal(0, withMap.ByteLength);
    }

    [Fact]
    public void A_base_centre_at_the_edge_of_the_wire_range_is_written_and_read()
    {
        var bases = new[] { new TeamBase(0, new Vector2(-100_000f, 0f), 25f), new TeamBase(1, new Vector2(100_000f, 100_000f), 25f) };
        var m = new MatchStartMessage(BattleMode.Standard, 1, 5, 900, null, Array.Empty<PropState>(), 2, bases);

        Assert.Equal(bases, ReadMatchStart(Encode(w => BattleMessages.Write(w, m))).Bases);
    }

    public static IEnumerable<object[]> BadBases()
    {
        static (byte, float, float, float) Good(byte team) => (team, 100f * (team + 1), 200f, 25f);
        yield return new object[] { "three bases", Bases(Good(0), Good(1), Good(0)) };
        yield return new object[] { "one base", Bases(Good(0)) };
        yield return new object[] { "two for team 0", Bases(Good(0), Good(0)) };
        yield return new object[] { "a team 2", Bases(Good(0), (2, 1f, 1f, 25f)) };
        yield return new object[] { "radius 0", Bases(Good(0), (1, 1f, 1f, 0f)) };
        yield return new object[] { "radius negative", Bases(Good(0), (1, 1f, 1f, -5f)) };
        yield return new object[] { "radius NaN", Bases(Good(0), (1, 1f, 1f, float.NaN)) };
        yield return new object[] { "radius 101", Bases(Good(0), (1, 1f, 1f, 101f)) };
        yield return new object[] { "x 1e7 m", Bases(Good(0), (1, 1e7f, 1f, 25f)) };
        yield return new object[] { "z -1e7 m", Bases(Good(0), (1, 1f, -1e7f, 25f)) };
        yield return new object[] { "x infinite", Bases(Good(0), (1, float.PositiveInfinity, 1f, 25f)) };
        yield return new object[] { "255 bases", (Action<BitWriter>)(w => w.WriteByte(255)) };
    }

    [Theory]
    [MemberData(nameof(BadBases))]
    public void Bad_bases_in_a_match_start_are_refused(string what, Action<BitWriter> bases)
    {
        Assert.NotNull(what);
        ReadMatchStart(HandMadeMatchStart(Bases((0, 152f, 398f, 25f), (1, 1024f, 852f, 100f))));   // the helper writes a valid message
        Assert.Throws<ProtocolException>(() => ReadMatchStart(HandMadeMatchStart(bases)));
    }

    [Fact]
    public void A_match_start_cut_short_anywhere_in_its_bases_is_refused()
    {
        var m = new MatchStartMessage(BattleMode.Standard, 3, 150, 18150, null, new[] { new PropState(300, 0) }, 7, SandboxMap.Bases);
        byte[] whole = Encode(w => BattleMessages.Write(w, m));
        ReadMatchStart(whole);
        int countAt = whole.Length - 1 - 1 - 2 * BaseBytes - 1;   // the ring count and the crate count (none) last
        Assert.Equal(2, whole[countAt]);
        for (int len = countAt; len < whole.Length; len++)
        {
            byte[] cut = whole[..len];
            Assert.Throws<ProtocolException>(() => ReadMatchStart(cut));
        }
    }

    // ---- size ----

    [Fact]
    public void A_full_standard_snapshot_fits_an_unreliable_packet()
    {
        var s = new BattleSnapshot { Tick = 17999 };
        for (int i = 0; i < BattleSnapshot.MaxEntities; i++)
        {
            s.Entities[i] = new BattleEntity
            {
                Id = 1000 + i * 37, Slot = (byte)(i % 10), Spec = (byte)(i % 4), Team = (byte)(i % 2),
                X = 115_000 + i * 1013, Z = -98_000 - i * 777, Yaw = 4000, TurretYaw = 4095, Pitch = 1023,
                Speed = -321, Health = 2400, Flags = TankFlags.Destroyed | TankFlags.Protected,
            };
        }
        s.EntityCount = BattleSnapshot.MaxEntities;
        s.HasOwn = true;
        s.Own = new OwnTank { Id = 1000, X = 1150.25f, Z = -980.5f, Yaw = 3f, TurretYaw = -3f, GunPitch = 0.3f, Speed = 11.1f, Reload = 7.5f, Bloom = 0.9f, ApLeft = 40, HeLeft = 20, ShotCount = 300, Health = 2400, ProtectedUntil = 17000, LastFired = 17990 };
        s.Match = new MatchBlock
        {
            Phase = MatchPhase.Playing, TicksLeft = 17999, Spotted = true, WinnerTeam = MatchRules.NoTeam, WinnerSlot = MatchRules.NoSlot,
            TeamScore0 = 999, TeamScore1 = 999, BaseProgress0 = 99, BaseProgress1 = 37, EndReason = MatchEndReason.None,
        };
        for (int p = 0; p < BattleProtocol.MaxPlayers; p++)
            s.Players[p] = new PlayerStats { Active = true, Team = (byte)(p % 2), Spec = 3, TankId = 1000 + p * 37, Kills = 120, Deaths = 130, Alive = true, Rank = (byte)p, Capture = 100 };

        var w = new BitWriter();
        new BattleSnapshotCodec().Write(w, s, null, new ClientFeedback { LastProcessedInputTick = 18003, InputSlack = -3 });

        Assert.True(w.ByteLength < BattleServer.MaxUnreliableBytes, $"{w.ByteLength} bytes");
        Assert.Equal(816, w.ByteLength);
        Assert.Equal(6528, w.BitLength);   // v4's 6235 (ten lines 5881, six more seats 59 bits each); v5: the entrants 8, a line 17 more (rank 5 bits, damage and lasted a byte each at 0); v6: the layers 6, an empty crate block 7
        Assert.Equal(1000, BattleServer.MaxUnreliableBytes);
        BattleSnapshot back = Decode(w.Data.ToArray());
        Assert.Equal(s.Match, back.Match);
        Assert.Equal(s.Players, back.Players);
        Assert.Equal(s.EntitySpan.ToArray(), back.EntitySpan.ToArray());
    }

    // ---- helpers ----

    private static BitReader Reader(byte[] packet, BattleMessage expected)
    {
        var r = new BitReader(packet);
        Assert.Equal(expected, (BattleMessage)r.ReadByte());
        return r;
    }

    private static byte[] Encode(Action<BitWriter> write)
    {
        var w = new BitWriter();
        write(w);
        return w.Data.ToArray();
    }

    private static RoomInfoMessage ReadRoomInfo(byte[] packet)
    {
        BitReader r = Reader(packet, BattleMessage.RoomInfo);
        return BattleMessages.ReadRoomInfo(ref r);
    }

    private static MatchStartMessage ReadMatchStart(byte[] packet)
    {
        BitReader r = Reader(packet, BattleMessage.MatchStart);
        return BattleMessages.ReadMatchStart(ref r);
    }

    private static BattleSnapshot Decode(byte[] packet, Func<int, BattleSnapshot?>? baseline = null)
    {
        BitReader r = Reader(packet, BattleMessage.Snapshot);
        var output = new BattleSnapshot();
        new BattleSnapshotCodec().Read(ref r, baseline ?? (_ => null), output, out _, out _);
        return output;
    }

    /// <summary>Writes <paramref name="s"/> against <paramref name="baseline"/> and reads it back against <paramref name="decodedBaseline"/>.</summary>
    private static BattleSnapshot RoundTrip(BattleSnapshotCodec codec, BattleSnapshot s, BattleSnapshot? baseline, BattleSnapshot? decodedBaseline)
    {
        var w = new BitWriter();
        codec.Write(w, s, baseline, default);
        return Decode(w.Data.ToArray(), tick => decodedBaseline is not null && tick == decodedBaseline.Tick ? decodedBaseline : null);
    }

    private static BattleSnapshot StandardSnapshot(int tick)
    {
        var s = new BattleSnapshot { Tick = tick };
        s.Match = new MatchBlock { Phase = MatchPhase.Playing, TicksLeft = 18000 - tick, WinnerTeam = MatchRules.NoTeam, WinnerSlot = MatchRules.NoSlot, TeamScore0 = 2, TeamScore1 = 1 };
        s.Players[0] = new PlayerStats { Active = true, Team = 0, Spec = 1, TankId = 1, Kills = 2, Deaths = 0, Alive = true, Rank = 1 };
        s.Players[4] = new PlayerStats { Active = true, Team = 1, Spec = 3, TankId = 7, Kills = 1, Deaths = 1, Alive = false, Rank = 2 };
        return s;
    }

    /// <summary>
    /// A full snapshot written field by field (the codec writes whatever it is given): a playing match with the bases'
    /// progress as given and no end reason, no own tank, one scoreboard line with <paramref name="capture"/>, no entities.
    /// </summary>
    private static byte[] HandMadeSnapshot(byte progress0 = 0, byte progress1 = 0, byte capture = 0) => Encode(w =>
    {
        w.WriteByte((byte)BattleMessage.Snapshot);
        w.WriteVarUInt(100);   // tick
        w.WriteVarUInt(0);     // no baseline
        w.WriteVarInt(0);      // feedback
        w.WriteVarInt(0);
        w.WriteBits(0, 1);     // the match block: playing
        w.WriteVarUInt(1000);
        w.WriteBool(false);
        w.WriteByte(MatchRules.NoTeam);
        w.WriteByte(MatchRules.NoSlot);
        w.WriteVarUInt(0);
        w.WriteVarUInt(0);
        w.WriteVarUInt(0);
        w.WriteByte(progress0);
        w.WriteByte(progress1);
        w.WriteBits((uint)MatchEndReason.None, 2);
        w.WriteByte(0);        // no entrants
        w.WriteBool(false);    // no own tank
        w.WriteBits(0, 7);     // no crates in sight (no baseline: no "same" bit)
        w.WriteBool(false);    // the scoreboard, in full
        w.WriteBool(true);     // slot 0
        w.WriteBits(0, 4);
        w.WriteBits(1, 2);
        w.WriteVarUInt(1);
        w.WriteVarUInt(3);
        w.WriteVarUInt(1);
        w.WriteBool(true);
        w.WriteBits(1, 5);     // rank
        w.WriteBits(capture, 7);
        w.WriteVarUInt(0);     // damage
        w.WriteVarUInt(0);     // lasted
        for (int i = 1; i < BattleProtocol.MaxPlayers; i++) w.WriteBool(false);
        w.WriteBool(false);    // no changed entities
        w.WriteBool(false);    // no removals
    });

    /// <summary>A room's LAN answer written field by field, so the mode byte can be anything.</summary>
    private static byte[] HandMadeRoomInfo(int version, byte mode) => Encode(w =>
    {
        w.WriteByte((byte)BattleMessage.RoomInfo);
        w.WriteVarUInt((uint)version);
        w.WriteString("老房", BattleProtocol.MaxRoomNameBytes);
        w.WriteByte(mode);
        w.WriteVarUInt(2);
        w.WriteVarUInt(10);
        w.WriteBool(false);
        w.WriteVarUInt(27016);
        w.WriteString("riverland", BattleProtocol.MaxMapIdBytes);
    });

    /// <summary>A standard match start without the map, field by field up to the bases, which <paramref name="bases"/> writes.</summary>
    private static byte[] HandMadeMatchStart(Action<BitWriter> bases) => Encode(w =>
    {
        w.WriteByte((byte)BattleMessage.MatchStart);
        w.WriteByte((byte)BattleMode.Standard);
        w.WriteVarUInt(1);     // match number
        w.WriteVarUInt(5);     // tick
        w.WriteVarUInt(900);   // end tick
        w.WriteByte(0);        // flags: no map
        w.WriteVarUInt(0);     // no prop states
        w.WriteVarUInt(2);     // the client's tank
        bases(w);
        w.WriteByte(0);        // no zone
        w.WriteByte(0);        // no crates
    });

    /// <summary>The bases part of a match start: the count, then each base's team, x, z and radius.</summary>
    private static Action<BitWriter> Bases(params (byte Team, float X, float Z, float Radius)[] bases) => w =>
    {
        w.WriteByte((byte)bases.Length);
        foreach ((byte team, float x, float z, float radius) in bases)
        {
            w.WriteByte(team);
            BattleMessages.WriteFloat(w, x);
            BattleMessages.WriteFloat(w, z);
            BattleMessages.WriteFloat(w, radius);
        }
    };
}
