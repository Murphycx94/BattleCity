using System.Numerics;
using Tank.Net.Battle;
using Tank.Sim.Maps;
using Tank.Sim.Modes;
using BitReader = Tank.Net.Protocol.BitReader;
using BitWriter = Tank.Net.Protocol.BitWriter;
using ProtocolException = Tank.Net.Protocol.ProtocolException;

namespace Tank.Net.Tests.Battle;

/// <summary>
/// Protocol v6 (stage 6 design §4.4): the crates on the wire — the ones a viewer sees in its snapshot (a position it
/// sees, and whether the crate still stands), its own buff layers in its own tank, the pickup message to the one player
/// who ate a crate, the crates a seat knows in a match start (a resync); every new field bounds-checked both ways, every
/// bad message refused. The crate table itself is never sent.
/// </summary>
public sealed class CrateWireTests
{
    private const int RingBytes = 3 * 4;
    private const int CrateBits = 8 + 3 + 2 * 32;   // a crate in a match start: id, kind (not padded), x, z; CrateBytes is the reserve's ceiling

    /// <summary>The bytes the crates part of a match start takes: the count byte and <paramref name="crates"/> crates, bit-packed, rounded up.</summary>
    private static int CratesPartBytes(int crates) => (8 + crates * CrateBits + 7) / 8;

    private static readonly ZoneRing R0 = new(new Vector2(600f, 600f), 806.1f);
    private static readonly ZoneRing R1 = new(new Vector2(560f, 640f), 420f);

    private static readonly Crate C0 = new(0, new Vector2(152.5f, 398.25f), CrateKind.Repair);
    private static readonly Crate C3 = new(3, new Vector2(600.5f, 712.25f), CrateKind.Ammo);
    private static readonly Crate C40 = new(40, new Vector2(1024f, 852f), CrateKind.Optics);

    [Fact]
    public void The_protocol_has_a_crate_message()
    {
        Assert.Equal(7, BattleProtocol.Version);   // v6: the crates; v7: duos (DuoWireTests)
        Assert.Equal(27, (byte)BattleMessage.Crate);
        Assert.Equal(10, BattleMessages.CrateBytes);
        Assert.Equal(64, CrateRules.MaxCrates);
    }

    // ---- the snapshot ----

    [Fact]
    public void The_crates_a_viewer_sees_and_its_own_layers_round_trip()
    {
        BattleSnapshot s = CrateSnapshot(900);
        BattleSnapshot back = RoundTrip(new BattleSnapshotCodec(), s, null, null);

        Assert.Equal(3, back.CrateCount);
        Assert.Equal(s.CrateSpan.ToArray(), back.CrateSpan.ToArray());
        Assert.False(back.Crates[1].Standing);
        Assert.Equal(s.Own, back.Own);
        Assert.Equal(((byte)1, (byte)2, (byte)3), (back.Own.ArmorLayers, back.Own.ReloadLayers, back.Own.OpticsLayers));

        // A layer alone changes the own tank, so it goes again; a row's every field counts.
        Assert.False(s.Own.Equals(s.Own with { ReloadLayers = 3 }));
        CrateView row = s.Crates[0];
        Assert.True(row.Equals(row with { }));
        Assert.False(row.Equals(row with { Standing = false }));
        Assert.False(row.Equals(row with { Kind = CrateKind.Armor }));
        Assert.False(row.Equals(row with { X = row.X + 1 }));
        Assert.False(row.Equals(row with { Id = 63 }));
    }

    [Fact]
    public void Unchanged_crates_go_as_one_bit_and_a_change_sends_them_whole()
    {
        var codec = new BattleSnapshotCodec();
        BattleSnapshot a = CrateSnapshot(900);
        BattleSnapshot fullA = RoundTrip(codec, a, null, null);
        BattleSnapshot b = CrateSnapshot(901);

        var w = new BitWriter();
        codec.Write(w, b, a, default);
        BattleSnapshot back = Decode(w.Data.ToArray(), tick => tick == fullA.Tick ? fullA : null);
        Assert.Equal(b.CrateSpan.ToArray(), back.CrateSpan.ToArray());
        Assert.True(w.ByteLength <= 20, $"{w.ByteLength} bytes");   // the header, the match block and three "same" bits: no crates, no own tank, no scoreboard

        // One crate eaten since: the block goes again.
        BattleSnapshot c = CrateSnapshot(902);
        c.Crates[2].Standing = false;
        BattleSnapshot cBack = RoundTrip(codec, c, a, fullA);
        Assert.Equal(c.CrateSpan.ToArray(), cBack.CrateSpan.ToArray());
        Assert.False(cBack.Crates[2].Standing);

        // Out of sight of every crate: none, against a baseline of three.
        BattleSnapshot d = CrateSnapshot(903);
        d.CrateCount = 0;
        BattleSnapshot dBack = RoundTrip(codec, d, a, fullA);
        Assert.Equal(0, dBack.CrateCount);

        // The first snapshot of a match: no baseline, the block in full.
        Assert.Equal(3, RoundTrip(codec, a, null, null).CrateCount);
    }

    public static IEnumerable<object[]> BadCrateBlocks()
    {
        yield return new object[] { "65 crates", (Action<BitWriter>)(w => w.WriteBits(65, 7)) };
        yield return new object[] { "127 crates", (Action<BitWriter>)(w => w.WriteBits(127, 7)) };
        yield return new object[] { "the same id twice", Rows((5, true, 1, 100, 100), (5, true, 1, 200, 200)) };
        yield return new object[] { "ids falling", Rows((7, true, 1, 100, 100), (3, true, 1, 200, 200)) };
        yield return new object[] { "kind 5", Rows((5, true, 5, 100, 100)) };
        yield return new object[] { "kind 7", Rows((5, true, 7, 100, 100)) };
        yield return new object[] { "x beyond 100 km", Rows((5, true, 1, BattleProtocol.MaxCoordinateCm + 1u, 100)) };
        yield return new object[] { "z beyond 100 km", Rows((5, true, 1, 100, BattleProtocol.MaxCoordinateCm + 1u)) };
        yield return new object[] { "z beyond an int", Rows((5, true, 1, 100, uint.MaxValue)) };
    }

    [Theory]
    [MemberData(nameof(BadCrateBlocks))]
    public void A_bad_crate_block_is_refused(string what, Action<BitWriter> crates)
    {
        Assert.NotNull(what);
        BattleSnapshot good = Decode(HandMadeSnapshot(Rows((5, true, 1, 100, 100), (63, false, 4, BattleProtocol.MaxCoordinateCm, 0))));   // the helper writes a valid snapshot
        Assert.Equal(2, good.CrateCount);
        Assert.Equal(new CrateView { Id = 63, Kind = CrateKind.Optics, Standing = false, X = BattleProtocol.MaxCoordinateCm, Z = 0 }, good.Crates[1]);
        Assert.Equal(0, Decode(HandMadeSnapshot(Rows())).CrateCount);
        Assert.Throws<ProtocolException>(() => Decode(HandMadeSnapshot(crates)));
    }

    [Fact]
    public void A_crate_block_the_reader_would_refuse_is_never_written()
    {
        foreach (Action<BattleSnapshot> spoil in new Action<BattleSnapshot>[]
                 {
                     s => s.CrateCount = CrateRules.MaxCrates + 1,
                     s => s.Crates[1].Id = s.Crates[0].Id,
                     s => s.Crates[1].Id = 1,
                     s => s.Crates[1].Kind = (CrateKind)5,
                     s => s.Crates[1].X = BattleProtocol.MaxCoordinateCm + 1,
                     s => s.Crates[1].Z = -1,
                 })
        {
            BattleSnapshot s = CrateSnapshot(900);
            spoil(s);
            var w = new BitWriter();
            Assert.ThrowsAny<ArgumentException>(() => new BattleSnapshotCodec().Write(w, s, null, default));
            Assert.Equal(0, w.ByteLength);   // checked before a byte is written
        }
        BattleSnapshot sixtyFive = CrateSnapshot(900);
        sixtyFive.CrateCount = CrateRules.MaxCrates + 1;
        Assert.Throws<ArgumentOutOfRangeException>(() => new BattleSnapshotCodec().Write(new BitWriter(), sixtyFive, null, default));

        // Sixty-four rows, ids 0–63, are the most, and go.
        BattleSnapshot full = CrateSnapshot(900);
        for (int i = 0; i < CrateRules.MaxCrates; i++) full.Crates[i] = new CrateView { Id = (byte)i, Kind = (CrateKind)(i % 5), Standing = i % 3 != 0, X = 3000 + i * 2500, Z = 117_000 - i * 1000 };
        full.CrateCount = CrateRules.MaxCrates;
        Assert.Equal(full.CrateSpan.ToArray(), RoundTrip(new BattleSnapshotCodec(), full, null, null).CrateSpan.ToArray());
    }

    [Fact]
    public void Layers_beyond_three_are_never_written_as_less()
    {
        BattleSnapshot s = CrateSnapshot(900);
        s.Own.ArmorLayers = 4;   // two bits would have cut it to 0
        s.Own.OpticsLayers = 255;
        BattleSnapshot back = RoundTrip(new BattleSnapshotCodec(), s, null, null);
        Assert.Equal(((byte)3, (byte)2, (byte)3), (back.Own.ArmorLayers, back.Own.ReloadLayers, back.Own.OpticsLayers));
    }

    [Fact]
    public void A_crate_snapshot_cut_short_anywhere_is_refused()
    {
        var w = new BitWriter();
        new BattleSnapshotCodec().Write(w, CrateSnapshot(900), null, default);
        byte[] whole = w.Data.ToArray();
        Decode(whole);
        for (int len = 1; len < whole.Length; len++)
        {
            byte[] cut = whole[..len];
            Assert.Throws<ProtocolException>(() => Decode(cut));
        }
    }

    [Fact]
    public void A_full_royale_snapshot_with_a_dozen_crates_fits_an_unreliable_packet()
    {
        BattleSnapshot s = WorstRoyaleSnapshot();
        for (int i = 0; i < 12; i++) s.Crates[i] = new CrateView { Id = (byte)(i * 5), Kind = (CrateKind)(i % 5), Standing = i % 4 != 0, X = 115_000 + i * 1013, Z = 98_000 - i * 777 };
        s.CrateCount = 12;

        var w = new BitWriter();
        new BattleSnapshotCodec().Write(w, s, null, new ClientFeedback { LastProcessedInputTick = 18003, InputSlack = -3 });

        Assert.True(w.ByteLength < BattleServer.MaxUnreliableBytes, $"{w.ByteLength} bytes");
        Assert.Equal(933, w.ByteLength);   // the royale worst case (846) plus a dozen rows of 10 bits and two 3-byte coordinates: 87 bytes
        BattleSnapshot back = Decode(w.Data.ToArray());
        Assert.Equal(s.CrateSpan.ToArray(), back.CrateSpan.ToArray());
        Assert.Equal(s.Own, back.Own);
        Assert.Equal(s.Players, back.Players);
    }

    [Fact]
    public void A_full_royale_snapshot_with_every_crate_in_sight_goes_reliable()
    {
        BattleSnapshot s = WorstRoyaleSnapshot();
        for (int i = 0; i < CratePlacer.Count; i++) s.Crates[i] = new CrateView { Id = (byte)i, Kind = (CrateKind)(i % 5), Standing = true, X = 115_000 + i * 1013, Z = 98_000 - i * 777 };
        s.CrateCount = CratePlacer.Count;

        var w = new BitWriter();
        new BattleSnapshotCodec().Write(w, s, null, new ClientFeedback { LastProcessedInputTick = 18003, InputSlack = -3 });

        Assert.True(w.ByteLength > BattleServer.MaxUnreliableBytes, $"{w.ByteLength} bytes");
        Assert.Equal(Tank.Net.Protocol.Channel.Reliable, BattleServer.ChannelFor(w.ByteLength));
        Assert.Equal(s.CrateSpan.ToArray(), Decode(w.Data.ToArray()).CrateSpan.ToArray());
    }

    // ---- the pickup message ----

    [Fact]
    public void A_pickup_round_trips()
    {
        var m = new CrateMessage(7, 12, CrateKind.Ammo, new Vector2(600.5f, 712.25f), 20, 8, 0);
        byte[] bytes = Encode(w => BattleMessages.Write(w, m));

        Assert.Equal((byte)BattleMessage.Crate, bytes[0]);
        Assert.Equal(1 + 1 + 1 + 2 * 4 + 1 + 1 + 1, bytes.Length);   // type, match, id, x z, amount, amount2; the kind's 3 bits and the layers' 2 share a byte
        Assert.Equal(m, ReadCrate(bytes));

        var layered = new CrateMessage(300_000, 63, CrateKind.Reload, new Vector2(-100_000f, 100_000f), 1, 0, 3);
        Assert.Equal(layered, ReadCrate(Encode(w => BattleMessages.Write(w, layered))));
        var wasted = new CrateMessage(0, 0, CrateKind.Repair, Vector2.Zero, 0, 0, 0);
        Assert.Equal(wasted, ReadCrate(Encode(w => BattleMessages.Write(w, wasted))));
    }

    public static IEnumerable<object[]> UnwritablePickups()
    {
        var good = new CrateMessage(7, 12, CrateKind.Ammo, new Vector2(600.5f, 712.25f), 20, 8, 0);
        yield return new object[] { "crate 64", good with { CrateId = 64 } };
        yield return new object[] { "crate 255", good with { CrateId = 255 } };
        yield return new object[] { "kind 5", good with { Kind = (CrateKind)5 } };
        yield return new object[] { "four layers", good with { Layers = 4 } };
        yield return new object[] { "a negative amount", good with { Amount = -1 } };
        yield return new object[] { "a negative second amount", good with { Amount2 = -1 } };
        yield return new object[] { "x beyond 100 km", good with { Position = new Vector2(100_001f, 0f) } };
        yield return new object[] { "z beyond 100 km", good with { Position = new Vector2(0f, -100_001f) } };
        yield return new object[] { "x NaN", good with { Position = new Vector2(float.NaN, 0f) } };
        yield return new object[] { "z infinite", good with { Position = new Vector2(0f, float.NegativeInfinity) } };
    }

    [Theory]
    [MemberData(nameof(UnwritablePickups))]
    public void A_pickup_the_reader_would_refuse_is_never_written(string what, CrateMessage m)
    {
        Assert.NotNull(what);
        var w = new BitWriter();
        Assert.Throws<ArgumentException>(() => BattleMessages.Write(w, m));
        Assert.Equal(0, w.ByteLength);
    }

    [Fact]
    public void A_pickup_of_a_negative_match_number_is_never_written()
    {
        var w = new BitWriter();
        Assert.Throws<ArgumentOutOfRangeException>(() => BattleMessages.Write(w, new CrateMessage(-1, 12, CrateKind.Ammo, Vector2.Zero, 20, 8, 0)));
        Assert.Equal(0, w.ByteLength);
    }

    public static IEnumerable<object[]> BadPickups()
    {
        yield return new object[] { "crate 64", (byte)64, 1u, 600f, 600f, 2u };
        yield return new object[] { "kind 5", (byte)12, 5u, 600f, 600f, 2u };
        yield return new object[] { "kind 7", (byte)12, 7u, 600f, 600f, 2u };
        yield return new object[] { "x beyond 100 km", (byte)12, 1u, 100_001f, 600f, 2u };
        yield return new object[] { "z beyond 100 km", (byte)12, 1u, 600f, -100_001f, 2u };
        yield return new object[] { "x NaN", (byte)12, 1u, float.NaN, 600f, 2u };
        yield return new object[] { "z infinite", (byte)12, 1u, 600f, float.PositiveInfinity, 2u };
        yield return new object[] { "an amount beyond an int", (byte)12, 1u, 600f, 600f, uint.MaxValue };
    }

    [Theory]
    [MemberData(nameof(BadPickups))]
    public void A_bad_pickup_is_refused(string what, byte id, uint kind, float x, float z, uint amount)
    {
        Assert.NotNull(what);
        Assert.Equal(new CrateMessage(7, 63, CrateKind.Optics, new Vector2(600f, 600f), 1, 0, 3), ReadCrate(HandMadePickup(63, 4, 600f, 600f, 1, 3)));   // the helper writes a valid message
        Assert.Throws<ProtocolException>(() => ReadCrate(HandMadePickup(id, kind, x, z, amount, 0)));
    }

    [Fact]
    public void A_pickup_cut_short_is_refused()
    {
        byte[] whole = Encode(w => BattleMessages.Write(w, new CrateMessage(7, 12, CrateKind.Ammo, new Vector2(600.5f, 712.25f), 20, 8, 0)));
        for (int len = 1; len < whole.Length; len++)
        {
            byte[] cut = whole[..len];
            Assert.Throws<ProtocolException>(() => ReadCrate(cut));
        }
    }

    // ---- the match start ----

    [Fact]
    public void A_match_start_without_crates_grows_by_the_count_byte_and_reads_none()
    {
        var m = new MatchStartMessage(BattleMode.TeamBattle, 1, 5, 900, null, Array.Empty<PropState>(), 2);
        byte[] bytes = Encode(w => BattleMessages.Write(w, m));

        byte[] v5 = HandMadeV5TeamStart();
        Assert.Equal(v5.Length + 1, bytes.Length);
        Assert.Equal(v5, bytes[..^1]);
        Assert.Equal(0, bytes[^1]);   // the crate count, last
        MatchStartMessage back = ReadMatchStart(bytes);
        Assert.NotNull(back.Crates);
        Assert.Empty(back.Crates);
        Assert.Equal(bytes, Encode(w => BattleMessages.Write(w, m with { Crates = Array.Empty<Crate>() })));

        // A v5 start, without the count: cut short.
        Assert.Throws<ProtocolException>(() => ReadMatchStart(v5));
    }

    [Fact]
    public void The_match_start_carries_the_crates_a_seat_knows()
    {
        MapData map = MapData.Of(SandboxMap.Create());
        PropState[] props = { new(300, 0), new(301, 128) };
        Crate[] crates = { C0, C3, C40 };
        var m = new MatchStartMessage(BattleMode.Royale, 3, 150, 18150, map, props, 7, null, new ZoneSync(120, new[] { R0, R1 }), crates);

        byte[] bytes = Encode(w => BattleMessages.Write(w, m));
        MatchStartMessage back = ReadMatchStart(bytes);

        Assert.Equal(9 + 53053 + 7 + 1 + 1 + (1 + 1 + 2 * RingBytes) + CratesPartBytes(3), bytes.Length);   // header · map · props · tank · no bases · the zone · three crates (30 bytes): 53127
        Assert.Equal(30, CratesPartBytes(3));
        Assert.True(CratesPartBytes(CrateRules.MaxCrates) <= 1 + CrateRules.MaxCrates * BattleMessages.CrateBytes);   // what the writer reserves
        Assert.Equal(crates, back.Crates);
        Assert.Equal(new[] { R0, R1 }, back.Zone!.Rings);
        Assert.Equal((BattleMode.Royale, 7), (back.Mode, back.MyTankId));

        // The prepared map writes the same bytes, the writer grown once.
        var encodedMap = new BitWriter(128 * 1024);
        BattleMessages.WriteMap(encodedMap, map);
        var w2 = new BitWriter(16);
        BattleMessages.Write(w2, m, encodedMap.Data);
        Assert.Equal(bytes, w2.Data.ToArray());
        Assert.InRange(w2.Capacity, w2.ByteLength, w2.ByteLength + 64);

        // Every crate of the riverland, without the map; no zone and crates (the tail's order: bases, zone, crates).
        Crate[] all = Enumerable.Range(0, CratePlacer.Count).Select(i => new Crate((byte)i, new Vector2(40f + i * 20f, 1160f - i * 20f), (CrateKind)(i % 5))).ToArray();
        MatchStartMessage allBack = ReadMatchStart(Encode(w => BattleMessages.Write(w, m with { Map = null, Zone = null, Crates = all })));
        Assert.Equal(all, allBack.Crates);
        Assert.Null(allBack.Zone);

        // Sixty-four, with gaps in the ids (the ones still standing of a set), is the most.
        Crate[] most = Enumerable.Range(0, CrateRules.MaxCrates).Select(i => new Crate((byte)i, new Vector2(i, -i), CrateKind.Armor)).ToArray();
        Assert.Equal(most, ReadMatchStart(Encode(w => BattleMessages.Write(w, m with { Map = null, Crates = most }))).Crates);
        Crate[] gaps = { C3, C40, new(63, new Vector2(1f, 1f), CrateKind.Reload) };
        Assert.Equal(gaps, ReadMatchStart(Encode(w => BattleMessages.Write(w, m with { Map = null, Crates = gaps }))).Crates);
    }

    public static IEnumerable<object[]> UnwritableCrates()
    {
        yield return new object[] { "65 crates", Enumerable.Range(0, 65).Select(i => new Crate((byte)i, Vector2.One, CrateKind.Repair)).ToArray() };
        yield return new object[] { "the same id twice", new[] { C3, C3 } };
        yield return new object[] { "ids falling", new[] { C40, C3 } };
        yield return new object[] { "a kind 5", new[] { C0, new Crate(3, Vector2.One, (CrateKind)5) } };
        yield return new object[] { "x 2e5 m", new[] { C0, new Crate(3, new Vector2(2e5f, 1f), CrateKind.Ammo) } };
        yield return new object[] { "z -2e5 m", new[] { C0, new Crate(3, new Vector2(1f, -2e5f), CrateKind.Ammo) } };
        yield return new object[] { "x NaN", new[] { C0, new Crate(3, new Vector2(float.NaN, 1f), CrateKind.Ammo) } };
        yield return new object[] { "z infinite", new[] { C0, new Crate(3, new Vector2(1f, float.PositiveInfinity), CrateKind.Ammo) } };
    }

    [Theory]
    [MemberData(nameof(UnwritableCrates))]
    public void A_match_start_with_crates_the_reader_would_refuse_writes_nothing(string what, Crate[] crates)
    {
        Assert.NotNull(what);
        MapData map = MapData.Of(SandboxMap.Create());
        var m = new MatchStartMessage(BattleMode.Royale, 1, 5, 900, null, new[] { new PropState(300, 0) }, 2, null, new ZoneSync(120, new[] { R0, R1 }), crates);
        var encodedMap = new BitWriter(128 * 1024);
        BattleMessages.WriteMap(encodedMap, map);

        var plain = new BitWriter();
        Assert.Throws<ArgumentException>(() => BattleMessages.Write(plain, m));
        Assert.Equal(0, plain.ByteLength);
        var withMap = new BitWriter();
        Assert.Throws<ArgumentException>(() => BattleMessages.Write(withMap, m with { Map = map }, encodedMap.Data));
        Assert.Equal(0, withMap.ByteLength);
    }

    public static IEnumerable<object[]> BadCrates()
    {
        yield return new object[] { "65 crates", (Action<BitWriter>)(w => w.WriteByte(65)) };
        yield return new object[] { "200 crates", (Action<BitWriter>)(w => w.WriteByte(200)) };
        yield return new object[] { "the same id twice", Crates((3, 1, 600f, 600f), (3, 1, 601f, 600f)) };
        yield return new object[] { "ids falling", Crates((7, 1, 600f, 600f), (3, 1, 601f, 600f)) };
        yield return new object[] { "id 64", Crates((64, 1, 600f, 600f)) };
        yield return new object[] { "kind 5", Crates((3, 5, 600f, 600f)) };
        yield return new object[] { "kind 7", Crates((3, 7, 600f, 600f)) };
        yield return new object[] { "x 2e5 m", Crates((3, 1, 2e5f, 600f)) };
        yield return new object[] { "z -2e5 m", Crates((3, 1, 600f, -2e5f)) };
        yield return new object[] { "x NaN", Crates((3, 1, float.NaN, 600f)) };
        yield return new object[] { "z infinite", Crates((3, 1, 600f, float.NegativeInfinity)) };
    }

    [Theory]
    [MemberData(nameof(BadCrates))]
    public void Bad_crates_in_a_match_start_are_refused(string what, Action<BitWriter> crates)
    {
        Assert.NotNull(what);
        Assert.Equal(2, ReadMatchStart(HandMadeMatchStart(Crates((3, 1, 600f, 600f), (40, 4, 1024f, 852f)))).Crates!.Count);   // the helper writes a valid message
        Assert.Empty(ReadMatchStart(HandMadeMatchStart(w => w.WriteByte(0))).Crates!);
        Assert.Throws<ProtocolException>(() => ReadMatchStart(HandMadeMatchStart(crates)));
    }

    [Fact]
    public void A_match_start_cut_short_anywhere_in_its_crates_is_refused()
    {
        var m = new MatchStartMessage(BattleMode.Royale, 3, 150, 18150, null, Array.Empty<PropState>(), 7, null, new ZoneSync(120, new[] { R0, R1 }), new[] { C3, C40 });
        byte[] whole = Encode(w => BattleMessages.Write(w, m));
        ReadMatchStart(whole);
        int countAt = whole.Length - CratesPartBytes(2);
        Assert.Equal(2, whole[countAt]);
        for (int len = countAt; len < whole.Length; len++)
        {
            byte[] cut = whole[..len];
            Assert.Throws<ProtocolException>(() => ReadMatchStart(cut));
        }
    }

    // ---- helpers ----

    /// <summary>A playing royale snapshot with three crates in sight (one eaten) and an own tank with layers 1, 2, 3.</summary>
    private static BattleSnapshot CrateSnapshot(int tick)
    {
        var s = new BattleSnapshot { Tick = tick };
        s.Match = new MatchBlock { Phase = MatchPhase.Playing, TicksLeft = 18000 - tick, WinnerTeam = MatchRules.NoTeam, WinnerSlot = MatchRules.NoSlot, Entrants = 16 };
        s.HasOwn = true;
        s.Own = new OwnTank { Id = 5, X = 600.5f, Z = 712.25f, Yaw = 1f, Reload = 2.5f, ApLeft = 20, HeLeft = 8, Health = 700, ArmorLayers = 1, ReloadLayers = 2, OpticsLayers = 3 };
        s.Crates[0] = new CrateView { Id = 3, Kind = CrateKind.Ammo, Standing = true, X = 60050, Z = 71225 };
        s.Crates[1] = new CrateView { Id = 17, Kind = CrateKind.Repair, Standing = false, X = 15250, Z = 39825 };
        s.Crates[2] = new CrateView { Id = 40, Kind = CrateKind.Optics, Standing = true, X = 102400, Z = 85200 };
        s.CrateCount = 3;
        return s;
    }

    /// <summary>The royale's worst case (RoyaleWireTests): 16 entities, the own tank with every layer, the ended match, every line at its widest.</summary>
    private static BattleSnapshot WorstRoyaleSnapshot()
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
        s.Own = new OwnTank
        {
            Id = 1000, X = 1150.25f, Z = -980.5f, Yaw = 3f, TurretYaw = -3f, GunPitch = 0.3f, Speed = 11.1f, Reload = 7.5f, Bloom = 0.9f, ApLeft = 40, HeLeft = 20,
            ShotCount = 300, Health = 2400, ProtectedUntil = 17000, LastFired = 17990, ArmorLayers = 3, ReloadLayers = 3, OpticsLayers = 3,
        };
        s.Match = new MatchBlock { Phase = MatchPhase.Ended, TicksLeft = 17999, Spotted = true, WinnerTeam = 7, WinnerSlot = 7, EndReason = MatchEndReason.Eliminated, Entrants = 16 };
        for (int p = 0; p < BattleProtocol.MaxPlayers; p++)
            s.Players[p] = new PlayerStats { Active = true, Team = (byte)p, Spec = 3, TankId = 1000 + p * 37, Kills = 15, Deaths = 1, Alive = p == 7, Rank = (byte)(p + 1), Damage = 30_000, Lasted = 600 };
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

    private static MatchStartMessage ReadMatchStart(byte[] packet)
    {
        BitReader r = Reader(packet, BattleMessage.MatchStart);
        return BattleMessages.ReadMatchStart(ref r);
    }

    private static CrateMessage ReadCrate(byte[] packet)
    {
        BitReader r = Reader(packet, BattleMessage.Crate);
        return BattleMessages.ReadCrate(ref r);
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

    /// <summary>
    /// A full snapshot written field by field: a playing royale, no own tank, the crate block as <paramref name="crates"/>
    /// writes it (after the own block, before the scoreboard), one scoreboard line, no entities.
    /// </summary>
    private static byte[] HandMadeSnapshot(Action<BitWriter> crates) => Encode(w =>
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
        w.WriteByte(16);       // entrants
        w.WriteBool(false);    // no own tank
        crates(w);             // no baseline: the count and the rows, no "same" bit
        w.WriteBool(false);    // the scoreboard, in full
        w.WriteBool(true);     // slot 0
        w.WriteBits(0, 4);
        w.WriteBits(1, 2);
        w.WriteVarUInt(1);
        w.WriteVarUInt(3);
        w.WriteVarUInt(1);
        w.WriteBool(true);
        w.WriteBits(1, 5);     // rank
        w.WriteBits(0, 7);     // capture
        w.WriteVarUInt(0);     // damage
        w.WriteVarUInt(0);     // lasted
        for (int i = 1; i < BattleProtocol.MaxPlayers; i++) w.WriteBool(false);
        w.WriteBool(false);    // no changed entities
        w.WriteBool(false);    // no removals
    });

    /// <summary>A snapshot's crate block: the 7-bit count, then each row's 6-bit id, standing bit, 3-bit kind, x and z in cm.</summary>
    private static Action<BitWriter> Rows(params (uint Id, bool Standing, uint Kind, uint X, uint Z)[] rows) => w =>
    {
        w.WriteBits((uint)rows.Length, 7);
        foreach ((uint id, bool standing, uint kind, uint x, uint z) in rows)
        {
            w.WriteBits(id, 6);
            w.WriteBool(standing);
            w.WriteBits(kind, 3);
            w.WriteVarUInt(x);
            w.WriteVarUInt(z);
        }
    };

    private static byte[] HandMadePickup(byte id, uint kind, float x, float z, uint amount, uint layers) => Encode(w =>
    {
        w.WriteByte((byte)BattleMessage.Crate);
        w.WriteVarUInt(7);     // match number
        w.WriteByte(id);
        w.WriteBits(kind, 3);
        BattleMessages.WriteFloat(w, x);
        BattleMessages.WriteFloat(w, z);
        w.WriteVarUInt(amount);
        w.WriteVarUInt(0);     // amount2
        w.WriteBits(layers, 2);
    });

    /// <summary>A team battle's match start without the map as v5 wrote it: up to the zone count (none), no crate count.</summary>
    private static byte[] HandMadeV5TeamStart() => Encode(w =>
    {
        w.WriteByte((byte)BattleMessage.MatchStart);
        w.WriteByte((byte)BattleMode.TeamBattle);
        w.WriteVarUInt(1);     // match number
        w.WriteVarUInt(5);     // tick
        w.WriteVarUInt(900);   // end tick
        w.WriteByte(0);        // flags: no map
        w.WriteVarUInt(0);     // no prop states
        w.WriteVarUInt(2);     // the client's tank
        w.WriteByte(0);        // no bases
        w.WriteByte(0);        // no zone
    });

    /// <summary>A royale match start without the map, field by field up to the crates, which <paramref name="crates"/> writes.</summary>
    private static byte[] HandMadeMatchStart(Action<BitWriter> crates) => Encode(w =>
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
        w.WriteByte(2);        // the zone: two rings
        w.WriteVarUInt(120);
        foreach (ZoneRing ring in new[] { R0, R1 })
        {
            BattleMessages.WriteFloat(w, ring.Centre.X);
            BattleMessages.WriteFloat(w, ring.Centre.Y);
            BattleMessages.WriteFloat(w, ring.Radius);
        }
        crates(w);
    });

    /// <summary>The crates part of a match start: the count, then each crate's id (a byte), 3-bit kind, x and z (floats).</summary>
    private static Action<BitWriter> Crates(params (byte Id, uint Kind, float X, float Z)[] crates) => w =>
    {
        w.WriteByte((byte)crates.Length);
        foreach ((byte id, uint kind, float x, float z) in crates)
        {
            w.WriteByte(id);
            w.WriteBits(kind, 3);
            BattleMessages.WriteFloat(w, x);
            BattleMessages.WriteFloat(w, z);
        }
    };
}
