using System.Numerics;
using Tank.Net.Battle;
using Tank.Sim;
using Tank.Sim.Maps;
using Tank.Sim.Modes;
using BitReader = Tank.Net.Protocol.BitReader;
using BitWriter = Tank.Net.Protocol.BitWriter;
using ProtocolException = Tank.Net.Protocol.ProtocolException;

namespace Tank.Net.Tests.Battle;

/// <summary>
/// Protocol v5 (stage 6 design §3.5): the royale on the wire — 16 seats and 5-bit ranks, the entrants in the match
/// block, each player's damage and time lasted on the scoreboard, the kill cause on events, the ring announcements, the
/// zone in the match start; every new field bounds-checked, every bad message refused.
/// </summary>
public sealed class RoyaleWireTests
{
    private const int RingBytes = 3 * 4;   // x, z, radius

    private static readonly ZoneRing R0 = new(new Vector2(600f, 600f), 806.1f);
    private static readonly ZoneRing R1 = new(new Vector2(560f, 640f), 420f);
    private static readonly ZoneRing R2 = new(new Vector2(520f, 610f), 250f);

    [Fact]
    public void The_protocol_has_sixteen_seats_and_a_zone_message()
    {
        Assert.Equal(7, BattleProtocol.Version);   // v6: the crates (CrateWireTests); v7: duos (DuoWireTests)
        Assert.Equal(26, (byte)BattleMessage.Zone);
        Assert.Equal(16, BattleProtocol.MaxPlayers);
        Assert.Equal("tank3d-v2", BattleProtocol.ConnectKey);
    }

    // ---- the snapshot ----

    [Fact]
    public void Sixteen_seats_with_ranks_damage_lasted_and_the_entrants_round_trip()
    {
        BattleSnapshot s = RoyaleSnapshot(900);
        BattleSnapshot back = RoundTrip(new BattleSnapshotCodec(), s, null, null);

        Assert.Equal(s.Match, back.Match);
        Assert.Equal(16, back.Match.Entrants);
        Assert.Equal(s.Players, back.Players);
        for (int p = 0; p < 16; p++) Assert.Equal(p + 1, back.Players[p].Rank);
        Assert.Equal((12345, 539), (back.Players[7].Damage, back.Players[7].Lasted));
    }

    [Fact]
    public void A_rank_is_none_or_one_of_the_seats_both_ways()
    {
        // 0 is "no rank yet" (every line while the match is on); 1–16 a place
        BattleSnapshot s = RoyaleSnapshot(900);
        s.Players[3].Rank = 0;
        s.Players[4].Rank = BattleProtocol.MaxPlayers;
        BattleSnapshot back = RoundTrip(new BattleSnapshotCodec(), s, null, null);
        Assert.Equal((0, 16), (back.Players[3].Rank, back.Players[4].Rank));

        Assert.Equal(16, Decode(HandMadeSnapshot(rank: 16)).Players[0].Rank);
        foreach (byte rank in new byte[] { 17, 31 })
        {
            Assert.Contains("Rank", Assert.Throws<ProtocolException>(() => Decode(HandMadeSnapshot(rank: rank))).Message);
            s.Players[3].Rank = rank;
            var w = new BitWriter();
            Assert.Throws<ArgumentOutOfRangeException>(() => new BattleSnapshotCodec().Write(w, s, null, default));
            Assert.Equal(0, w.ByteLength);   // checked before a byte is written
        }
    }

    [Fact]
    public void Negative_damage_or_time_lasted_is_never_written()
    {
        foreach (Action<BattleSnapshot> spoil in new Action<BattleSnapshot>[] { s => s.Players[5].Damage = -1, s => s.Players[5].Lasted = -1 })
        {
            BattleSnapshot s = RoyaleSnapshot(900);
            spoil(s);
            var w = new BitWriter();
            Assert.Throws<ArgumentOutOfRangeException>(() => new BattleSnapshotCodec().Write(w, s, null, default));
            Assert.Equal(0, w.ByteLength);
        }
        BattleSnapshot inactive = RoyaleSnapshot(900);
        inactive.Players[5] = new PlayerStats { Damage = -1, Rank = 200 };   // an empty line goes as one bit: nothing of it is written
        RoundTrip(new BattleSnapshotCodec(), inactive, null, null);
    }

    [Fact]
    public void An_unchanged_scoreboard_goes_as_the_same_bit()
    {
        var codec = new BattleSnapshotCodec();
        BattleSnapshot a = RoyaleSnapshot(900);
        BattleSnapshot fullA = RoundTrip(codec, a, null, null);
        BattleSnapshot b = RoyaleSnapshot(901);

        var w = new BitWriter();
        codec.Write(w, b, a, default);
        BattleSnapshot back = Decode(w.Data.ToArray(), tick => tick == fullA.Tick ? fullA : null);

        Assert.Equal(b.Players, back.Players);
        Assert.True(w.ByteLength <= 20, $"{w.ByteLength} bytes");   // the header, the match block and five bits (v6: the crates' "same" bit too): no scoreboard

        // Damage or time lasted alone changes the line, so the scoreboard goes again.
        Assert.False(a.Players[7].Equals(a.Players[7] with { Damage = 12346 }));
        Assert.False(a.Players[7].Equals(a.Players[7] with { Lasted = 540 }));
        Assert.False(a.Match.Equals(a.Match with { Entrants = 15 }));
        BattleSnapshot c = RoyaleSnapshot(902);
        c.Players[7].Lasted = 540;
        var w2 = new BitWriter();
        codec.Write(w2, c, a, default);
        Assert.True(w2.ByteLength > 60, $"{w2.ByteLength} bytes");
        Assert.Equal(540, Decode(w2.Data.ToArray(), tick => tick == fullA.Tick ? fullA : null).Players[7].Lasted);
    }

    [Fact]
    public void Entrants_beyond_the_seats_are_refused()
    {
        Decode(HandMadeSnapshot(entrants: 16));   // the helper writes a valid snapshot
        Assert.Equal((2, 4321, 77), DecodeLine(HandMadeSnapshot(rank: 2, damage: 4321, lasted: 77)));
        foreach (byte entrants in new byte[] { 17, 255 })
            Assert.Contains("Entrants", Assert.Throws<ProtocolException>(() => Decode(HandMadeSnapshot(entrants: entrants))).Message);

        static (int, int, int) DecodeLine(byte[] packet)
        {
            PlayerStats p = Decode(packet).Players[0];
            return (p.Rank, p.Damage, p.Lasted);
        }
    }

    [Fact]
    public void A_royale_snapshot_cut_short_anywhere_is_refused()
    {
        var w = new BitWriter();
        new BattleSnapshotCodec().Write(w, RoyaleSnapshot(900), null, default);
        byte[] whole = w.Data.ToArray();
        Decode(whole);
        for (int len = 1; len < whole.Length; len++)
        {
            byte[] cut = whole[..len];
            Assert.Throws<ProtocolException>(() => Decode(cut));
        }
    }

    [Fact]
    public void A_full_royale_snapshot_fits_an_unreliable_packet()
    {
        var s = new BattleSnapshot { Tick = 17999 };
        for (int i = 0; i < BattleSnapshot.MaxEntities; i++)
        {
            s.Entities[i] = new BattleEntity
            {
                Id = 1000 + i * 37, Slot = (byte)(i % 16), Spec = (byte)(i % 4), Team = (byte)(i % 16),
                X = 115_000 + i * 1013, Z = -98_000 - i * 777, Yaw = 4000, TurretYaw = 4095, Pitch = 1023,
                Speed = -321, Health = 2400, Flags = TankFlags.Destroyed | TankFlags.Protected,
            };
        }
        s.EntityCount = BattleSnapshot.MaxEntities;
        s.HasOwn = true;
        s.Own = new OwnTank { Id = 1000, X = 1150.25f, Z = -980.5f, Yaw = 3f, TurretYaw = -3f, GunPitch = 0.3f, Speed = 11.1f, Reload = 7.5f, Bloom = 0.9f, ApLeft = 40, HeLeft = 20, ShotCount = 300, Health = 2400, ProtectedUntil = 17000, LastFired = 17990 };
        s.Match = new MatchBlock
        {
            Phase = MatchPhase.Ended, TicksLeft = 17999, Spotted = true, WinnerTeam = 7, WinnerSlot = 7,
            EndReason = MatchEndReason.Eliminated, Entrants = 16,
        };
        for (int p = 0; p < BattleProtocol.MaxPlayers; p++)   // the end of a 10-minute match: every number at its widest
        {
            s.Players[p] = new PlayerStats
            {
                Active = true, Team = (byte)p, Spec = 3, TankId = 1000 + p * 37, Kills = 15, Deaths = 1, Alive = p == 7, Rank = (byte)(p + 1),
                Damage = 30_000, Lasted = 600,
            };
        }

        var w = new BitWriter();
        new BattleSnapshotCodec().Write(w, s, null, new ClientFeedback { LastProcessedInputTick = 18003, InputSlack = -3 });

        Assert.True(w.ByteLength < BattleServer.MaxUnreliableBytes, $"{w.ByteLength} bytes");
        Assert.Equal(846, w.ByteLength);   // v5's 845 (every line's damage 3 bytes and time lasted 2: 30 more than the standard worst case) plus v6's 13 bits: the layers (6) and an empty crate block (7)
        BattleSnapshot back = Decode(w.Data.ToArray());
        Assert.Equal(s.Match, back.Match);
        Assert.Equal(s.Players, back.Players);
    }

    // ---- events ----

    [Fact]
    public void The_kill_cause_rides_on_every_event()
    {
        GameEvent zoneKill = new(GameEventType.TankDestroyed, 40, 0, 0, new Vector3(9, 0, 9), Vector3.Zero, TargetTankId: 5, Cause: KillCause.Zone);
        GameEvent shellKill = new(GameEventType.TankDestroyed, 40, 3, 17, new Vector3(9, 0, 9), Vector3.Zero, TargetTankId: 6);
        GameEvent shot = new(GameEventType.ShellFired, 40, 3, 18, Vector3.One, Vector3.UnitZ);
        var batch = new EventBatch(40, new[] { zoneKill, shellKill, shot }, Array.Empty<PropState>());

        EventBatch back = ReadEvents(Encode(w => BattleMessages.Write(w, batch)));

        Assert.Equal(batch.Events, back.Events);
        Assert.Equal(new[] { KillCause.Zone, KillCause.Shell, KillCause.Shell }, back.Events.Select(e => e.Cause));
    }

    [Fact]
    public void An_unknown_kill_cause_is_refused()
    {
        ReadEvents(HandMadeEvents(cause: 1));   // the helper writes a valid batch
        foreach (uint cause in new uint[] { 2, 3 })
            Assert.Contains("kill cause", Assert.Throws<ProtocolException>(() => ReadEvents(HandMadeEvents(cause))).Message);
    }

    // ---- the zone message ----

    [Fact]
    public void A_ring_announcement_round_trips()
    {
        var m = new ZoneMessage(7, 2, R2);
        byte[] bytes = Encode(w => BattleMessages.Write(w, m));

        Assert.Equal(1 + 1 + 1 + RingBytes, bytes.Length);
        Assert.Equal((byte)BattleMessage.Zone, bytes[0]);
        Assert.Equal(m, ReadZone(bytes));
        Assert.Equal(new ZoneMessage(300, 5, new ZoneRing(R2.Centre, 0f)), ReadZone(Encode(w => BattleMessages.Write(w, new ZoneMessage(300, 5, new ZoneRing(R2.Centre, 0f))))));
    }

    public static IEnumerable<object[]> BadAnnouncements()
    {
        yield return new object[] { "ring 0", (byte)0, 600f, 600f, 420f };
        yield return new object[] { "ring 6", (byte)6, 600f, 600f, 420f };
        yield return new object[] { "radius NaN", (byte)1, 600f, 600f, float.NaN };
        yield return new object[] { "radius −1", (byte)1, 600f, 600f, -1f };
        yield return new object[] { "radius 2e5", (byte)1, 600f, 600f, 2e5f };
        yield return new object[] { "x 2e5", (byte)1, 2e5f, 600f, 420f };
        yield return new object[] { "z −2e5", (byte)1, 600f, -2e5f, 420f };
        yield return new object[] { "x infinite", (byte)1, float.PositiveInfinity, 600f, 420f };
    }

    [Theory]
    [MemberData(nameof(BadAnnouncements))]
    public void A_bad_ring_announcement_is_refused(string what, byte ring, float x, float z, float radius)
    {
        Assert.NotNull(what);
        ReadZone(HandMadeZone(1, 600f, 600f, 420f));   // the helper writes a valid message
        Assert.Throws<ProtocolException>(() => ReadZone(HandMadeZone(ring, x, z, radius)));
        Assert.Throws<ArgumentException>(() => BattleMessages.Write(new BitWriter(), new ZoneMessage(1, ring, new ZoneRing(new Vector2(x, z), radius))));
    }

    [Fact]
    public void A_ring_announcement_of_a_negative_match_number_is_never_written()
    {
        var w = new BitWriter();
        Assert.Throws<ArgumentOutOfRangeException>(() => BattleMessages.Write(w, new ZoneMessage(-1, 2, R2)));
        Assert.Equal(0, w.ByteLength);
    }

    [Fact]
    public void A_ring_announcement_cut_short_is_refused()
    {
        byte[] whole = Encode(w => BattleMessages.Write(w, new ZoneMessage(7, 2, R2)));
        for (int len = 1; len < whole.Length; len++)
        {
            byte[] cut = whole[..len];
            Assert.Throws<ProtocolException>(() => ReadZone(cut));
        }
    }

    // ---- the match start ----

    [Fact]
    public void The_match_start_carries_the_zone()
    {
        MapData map = MapData.Of(SandboxMap.Create());
        PropState[] props = { new(300, 0), new(301, 128) };
        var m = new MatchStartMessage(BattleMode.Royale, 3, 150, 18150, map, props, 7, null, new ZoneSync(120, new[] { R0, R1, R2 }));

        byte[] bytes = Encode(w => BattleMessages.Write(w, m));
        MatchStartMessage back = ReadMatchStart(bytes);

        Assert.Equal(9 + 53053 + 7 + 1 + 1 + (1 + 1 + 3 * RingBytes) + 1, bytes.Length);   // header · map · props · tank · no bases · the zone · no crates: 53110
        Assert.NotNull(back.Zone);
        Assert.Equal(120, back.Zone.StartTick);
        Assert.Equal(new[] { R0, R1, R2 }, back.Zone.Rings);
        Assert.Equal((BattleMode.Royale, 7), (back.Mode, back.MyTankId));
        Assert.Empty(back.Bases!);

        // The prepared map writes the same bytes, the writer grown once.
        var encodedMap = new BitWriter(128 * 1024);
        BattleMessages.WriteMap(encodedMap, map);
        var w2 = new BitWriter(16);
        BattleMessages.Write(w2, m, encodedMap.Data);
        Assert.Equal(bytes, w2.Data.ToArray());
        Assert.InRange(w2.Capacity, w2.ByteLength, w2.ByteLength + 64);

        // Without the zone: one byte, read as null.
        byte[] none = Encode(w => BattleMessages.Write(w, m with { Zone = null }));
        Assert.Equal(bytes.Length - 1 - 3 * RingBytes, none.Length);
        Assert.Equal(0, none[^2]);   // the ring count; the crate count (none) follows
        Assert.Null(ReadMatchStart(none).Zone);

        // All six rings, a late start tick.
        var six = m with { Map = null, Zone = new ZoneSync(9_000_000, new[] { R0, R1, R2, R2, R2, new ZoneRing(R2.Centre, 0f) }) };
        MatchStartMessage sixBack = ReadMatchStart(Encode(w => BattleMessages.Write(w, six)));
        Assert.Equal(9_000_000, sixBack.Zone!.StartTick);
        Assert.Equal(six.Zone!.Rings, sixBack.Zone.Rings);
    }

    public static IEnumerable<object[]> UnwritableZones()
    {
        yield return new object[] { "one ring", new ZoneSync(120, new[] { R0 }) };
        yield return new object[] { "seven rings", new ZoneSync(120, new[] { R0, R1, R2, R2, R2, R2, R2 }) };
        yield return new object[] { "no rings", new ZoneSync(120, Array.Empty<ZoneRing>()) };
        yield return new object[] { "a centre 2e5 m out", new ZoneSync(120, new[] { R0, new ZoneRing(new Vector2(2e5f, 0f), 10f) }) };
        yield return new object[] { "a negative radius", new ZoneSync(120, new[] { R0, new ZoneRing(R1.Centre, -1f) }) };
        yield return new object[] { "a NaN radius", new ZoneSync(120, new[] { R0, new ZoneRing(R1.Centre, float.NaN) }) };
        yield return new object[] { "a negative start tick", new ZoneSync(-1, new[] { R0, R1 }) };
    }

    [Theory]
    [MemberData(nameof(UnwritableZones))]
    public void A_match_start_with_a_zone_the_reader_would_refuse_writes_nothing(string what, ZoneSync zone)
    {
        Assert.NotNull(what);
        MapData map = MapData.Of(SandboxMap.Create());
        var m = new MatchStartMessage(BattleMode.Royale, 1, 5, 900, null, new[] { new PropState(300, 0) }, 2, null, zone);
        var encodedMap = new BitWriter(128 * 1024);
        BattleMessages.WriteMap(encodedMap, map);

        var plain = new BitWriter();
        Assert.Throws<ArgumentException>(() => BattleMessages.Write(plain, m));
        Assert.Equal(0, plain.ByteLength);
        var withMap = new BitWriter();
        Assert.Throws<ArgumentException>(() => BattleMessages.Write(withMap, m with { Map = map }, encodedMap.Data));
        Assert.Equal(0, withMap.ByteLength);
    }

    public static IEnumerable<object[]> BadZones()
    {
        yield return new object[] { "1 ring", Zone(120, (600f, 600f, 806f)) };
        yield return new object[] { "7 rings", Zone(120, (600f, 600f, 806f), (1f, 1f, 1f), (1f, 1f, 1f), (1f, 1f, 1f), (1f, 1f, 1f), (1f, 1f, 1f), (1f, 1f, 1f)) };
        yield return new object[] { "255 rings", (Action<BitWriter>)(w => w.WriteByte(255)) };
        yield return new object[] { "a start tick beyond the last", (Action<BitWriter>)(w => { w.WriteByte(2); w.WriteVarUInt(BattleProtocol.MaxTick + 1u); }) };
        yield return new object[] { "a negative radius", Zone(120, (600f, 600f, 806f), (600f, 600f, -1f)) };
        yield return new object[] { "a radius over 100 km", Zone(120, (600f, 600f, 2e5f), (600f, 600f, 1f)) };
        yield return new object[] { "a centre 2e5 m out", Zone(120, (600f, 600f, 806f), (2e5f, 600f, 1f)) };
        yield return new object[] { "a NaN centre", Zone(120, (600f, float.NaN, 806f), (600f, 600f, 1f)) };
    }

    [Theory]
    [MemberData(nameof(BadZones))]
    public void A_bad_zone_in_a_match_start_is_refused(string what, Action<BitWriter> zone)
    {
        Assert.NotNull(what);
        Assert.Equal(2, ReadMatchStart(HandMadeMatchStart(Zone(120, (600f, 600f, 806f), (560f, 640f, 420f)))).Zone!.Rings.Count);   // the helper writes a valid message
        Assert.Null(ReadMatchStart(HandMadeMatchStart(w => w.WriteByte(0))).Zone);
        Assert.Throws<ProtocolException>(() => ReadMatchStart(HandMadeMatchStart(zone)));
    }

    [Fact]
    public void A_match_start_cut_short_anywhere_in_its_zone_is_refused()
    {
        var m = new MatchStartMessage(BattleMode.Royale, 3, 150, 18150, null, Array.Empty<PropState>(), 7, null, new ZoneSync(120, new[] { R0, R1 }));
        byte[] whole = Encode(w => BattleMessages.Write(w, m));
        ReadMatchStart(whole);
        int countAt = whole.Length - 1 - 1 - 1 - 2 * RingBytes;   // the crate count (none) last
        Assert.Equal(2, whole[countAt]);
        for (int len = countAt; len < whole.Length; len++)
        {
            byte[] cut = whole[..len];
            Assert.Throws<ProtocolException>(() => ReadMatchStart(cut));
        }
    }

    // ---- the LAN answer ----

    [Fact]
    public void A_room_of_sixteen_seats_is_listed_and_seventeen_is_refused()
    {
        var info = new RoomInfoMessage(BattleProtocol.Version, "吃鸡房", BattleMode.Royale, 16, 16, true, 27015, RiverlandMap.Id);
        Assert.Equal(info, ReadRoomInfo(Encode(w => BattleMessages.Write(w, info))));
        Assert.Throws<ProtocolException>(() => ReadRoomInfo(Encode(w => BattleMessages.Write(w, info with { MaxPlayers = 17 }))));
        Assert.Throws<ProtocolException>(() => ReadRoomInfo(Encode(w => BattleMessages.Write(w, info with { Players = 17, MaxPlayers = 17 }))));
    }

    // ---- helpers ----

    private static BattleSnapshot RoyaleSnapshot(int tick)
    {
        var s = new BattleSnapshot { Tick = tick };
        s.Match = new MatchBlock { Phase = MatchPhase.Playing, TicksLeft = 18000 - tick, WinnerTeam = MatchRules.NoTeam, WinnerSlot = MatchRules.NoSlot, Entrants = 16 };
        for (int p = 0; p < 16; p++)
            s.Players[p] = new PlayerStats { Active = true, Team = (byte)p, Spec = (byte)(p % 4), TankId = p + 1, Kills = p % 3, Deaths = p < 8 ? 1 : 0, Alive = p >= 8, Rank = (byte)(p + 1), Damage = p * 100, Lasted = p * 10 };
        s.Players[7].Damage = 12345;
        s.Players[7].Lasted = 539;
        return s;
    }

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

    private static EventBatch ReadEvents(byte[] packet)
    {
        BitReader r = Reader(packet, BattleMessage.Events);
        return BattleMessages.ReadEvents(ref r);
    }

    private static ZoneMessage ReadZone(byte[] packet)
    {
        BitReader r = Reader(packet, BattleMessage.Zone);
        return BattleMessages.ReadZone(ref r);
    }

    private static BattleSnapshot Decode(byte[] packet, Func<int, BattleSnapshot?>? baseline = null)
    {
        BitReader r = Reader(packet, BattleMessage.Snapshot);
        var output = new BattleSnapshot();
        new BattleSnapshotCodec().Read(ref r, baseline ?? (_ => null), output, out _, out _);
        return output;
    }

    private static BattleSnapshot RoundTrip(BattleSnapshotCodec codec, BattleSnapshot s, BattleSnapshot? baseline, BattleSnapshot? decodedBaseline)
    {
        var w = new BitWriter();
        codec.Write(w, s, baseline, default);
        return Decode(w.Data.ToArray(), tick => decodedBaseline is not null && tick == decodedBaseline.Tick ? decodedBaseline : null);
    }

    /// <summary>A full snapshot written field by field: a playing match with <paramref name="entrants"/>, one scoreboard line, no entities.</summary>
    private static byte[] HandMadeSnapshot(byte entrants = 16, byte rank = 1, uint damage = 0, uint lasted = 0) => Encode(w =>
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
        w.WriteByte(0);
        w.WriteByte(0);
        w.WriteBits((uint)MatchEndReason.None, 2);
        w.WriteByte(entrants);
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
        w.WriteBits(rank, 5);
        w.WriteBits(0, 7);     // capture
        w.WriteVarUInt(damage);
        w.WriteVarUInt(lasted);
        for (int i = 1; i < BattleProtocol.MaxPlayers; i++) w.WriteBool(false);
        w.WriteBool(false);    // no changed entities
        w.WriteBool(false);    // no removals
    });

    /// <summary>A batch of one zone kill written field by field, its cause bits <paramref name="cause"/>.</summary>
    private static byte[] HandMadeEvents(uint cause) => Encode(w =>
    {
        w.WriteByte((byte)BattleMessage.Events);
        w.WriteVarUInt(40);    // tick
        w.WriteVarUInt(1);     // one event
        w.WriteByte((byte)GameEventType.TankDestroyed);
        w.WriteVarUInt(0);     // nobody's kill
        w.WriteVarUInt(0);
        BattleMessages.WriteVector(w, new Vector3(9, 0, 9));
        BattleMessages.WriteVector(w, Vector3.Zero);
        w.WriteVarUInt(5);     // the target
        w.WriteBits(0, 2);
        w.WriteVarUInt(0);
        w.WriteBits(0, 1);
        w.WriteVarUInt(0);     // prop
        w.WriteBits(cause, 2);
        w.WriteVarUInt(0);     // no prop states
    });

    private static byte[] HandMadeZone(byte ring, float x, float z, float radius) => Encode(w =>
    {
        w.WriteByte((byte)BattleMessage.Zone);
        w.WriteVarUInt(7);     // match number
        w.WriteByte(ring);
        BattleMessages.WriteFloat(w, x);
        BattleMessages.WriteFloat(w, z);
        BattleMessages.WriteFloat(w, radius);
    });

    /// <summary>A royale match start without the map, field by field up to the zone, which <paramref name="zone"/> writes.</summary>
    private static byte[] HandMadeMatchStart(Action<BitWriter> zone) => Encode(w =>
    {
        w.WriteByte((byte)BattleMessage.MatchStart);
        w.WriteByte((byte)BattleMode.Royale);
        w.WriteVarUInt(1);     // match number
        w.WriteVarUInt(5);     // tick
        w.WriteVarUInt(900);   // end tick
        w.WriteByte(0);        // flags: no map
        w.WriteVarUInt(0);     // no prop states
        w.WriteVarUInt(2);     // the client's tank
        w.WriteByte(0);        // no bases
        zone(w);
        w.WriteByte(0);        // no crates
    });

    /// <summary>The zone part of a match start: the ring count, the start tick, each ring's x, z and radius.</summary>
    private static Action<BitWriter> Zone(uint startTick, params (float X, float Z, float Radius)[] rings) => w =>
    {
        w.WriteByte((byte)rings.Length);
        w.WriteVarUInt(startTick);
        foreach ((float x, float z, float radius) in rings)
        {
            BattleMessages.WriteFloat(w, x);
            BattleMessages.WriteFloat(w, z);
            BattleMessages.WriteFloat(w, radius);
        }
    };
}
