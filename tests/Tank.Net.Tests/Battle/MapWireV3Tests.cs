using System.Numerics;
using Tank.Net.Battle;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Modes;
using BitReader = Tank.Net.Protocol.BitReader;
using BitWriter = Tank.Net.Protocol.BitWriter;
using ProtocolException = Tank.Net.Protocol.ProtocolException;

namespace Tank.Net.Tests.Battle;

/// <summary>
/// Protocol v3's map (riverland design §5.1): the v2 bytes unchanged, then a flags byte, the water level when there is
/// water, and the ground's kinds run-length encoded; larger prop limits, prop kinds up to the sim's last, and every limit
/// checked before anything is allocated for it.
/// </summary>
public sealed class MapWireV3Tests
{
    private const int Size = 41;                              // samples per side of the small map
    private const int Cells = (Size - 1) * (Size - 1);        // 1600

    // ---- the sandbox map: v2 bytes unchanged ----

    [Fact]
    public void The_v2_part_of_the_sandbox_map_is_byte_for_byte_unchanged()
    {
        byte[] bytes = Encode(MapData.Of(SandboxMap.Create()));

        Assert.Equal(53053, bytes.Length);
        Assert.Equal(0xF0121BE3E7E7EC72UL, Fnv1a64(bytes.AsSpan(0, 53047)));   // the v2 encoding, measured on 80d6eca
        Assert.Equal(new byte[] { 0x00, 0x01, 0xC0, 0xB8, 0x02, 0x00 }, bytes[53047..]);   // no water · 1 run · 40000 · grass
    }

    // ---- round trips ----

    [Fact]
    public void Surface_water_trees_and_houses_round_trip()
    {
        MapData map = SmallMap(waterLevelCm: -37);

        MapData back = Decode(Encode(map));

        Assert.Equal((map.SamplesX, map.SamplesZ, map.CellSize, map.Border), (back.SamplesX, back.SamplesZ, back.CellSize, back.Border));
        Assert.Equal(map.HeightsCm, back.HeightsCm);
        Assert.Equal(map.Props, back.Props);
        Assert.Equal(map.Surface, back.Surface);
        Assert.Equal(-37, back.WaterLevelCm);
        Assert.Equal(4, back.Surface.Distinct().Count());   // all four kinds were there to carry
        Assert.Contains(back.Props, p => p.Kind == PropKind.Tree);
        Assert.Contains(back.Props, p => p.Kind == PropKind.House);
        Assert.Contains(back.Props, p => p.Kind == PropKind.BrickWall);
        Battlefield field = back.Build();
        Assert.Equal(-0.37f, field.WaterLevel!.Value, 5);
        Assert.Equal(map.Surface, field.Surface.Cells.ToArray());
    }

    [Fact]
    public void A_map_without_water_has_no_level()
    {
        MapData map = SmallMap(waterLevelCm: null);
        byte[] bytes = Encode(map);

        Assert.Equal(0, bytes[V2Length(map)]);   // the flags byte
        Assert.Null(Decode(bytes).WaterLevelCm);
        Assert.Equal(1, Encode(SmallMap(waterLevelCm: 5))[V2Length(map)]);
    }

    // ---- refusals ----

    [Fact]
    public void Unknown_flag_bits_are_refused()
    {
        MapData map = SmallMap(waterLevelCm: null);
        byte[] bytes = Encode(map);
        Decode(bytes);   // fine as written

        foreach (byte flags in new byte[] { 0x02, 0x80 })
        {
            byte[] bad = (byte[])bytes.Clone();
            bad[V2Length(map)] = flags;
            Assert.Throws<ProtocolException>(() => Decode(bad));
        }
    }

    [Fact]
    public void Run_lengths_must_add_up_to_the_cells()
    {
        MapData map = SmallMap(waterLevelCm: null);
        Decode(WithTrailer(map, w => Runs(w, (Cells, 0))));   // the helper writes a valid map

        Assert.Throws<ProtocolException>(() => Decode(WithTrailer(map, w => Runs(w, (Cells - 1, 0)))));
        Assert.Throws<ProtocolException>(() => Decode(WithTrailer(map, w => Runs(w, (Cells + 1, 0)))));
        Assert.Throws<ProtocolException>(() => Decode(WithTrailer(map, w => Runs(w, (Cells - 1, 0), (2, 1)))));
        Assert.Throws<ProtocolException>(() => Decode(WithTrailer(map, w => Runs(w, (0, 2), (Cells, 0)))));
    }

    [Fact]
    public void Surface_kinds_and_prop_kinds_beyond_the_last_are_refused()
    {
        MapData map = SmallMap(waterLevelCm: null);
        Decode(WithTrailer(map, w => Runs(w, (Cells - 1, 0), (1, (byte)SurfaceMap.LastKind))));
        Assert.Throws<ProtocolException>(() => Decode(WithTrailer(map, w => Runs(w, (Cells - 1, 0), (1, (byte)SurfaceMap.LastKind + 1)))));

        MapData withKind(PropKind kind) => new(map.SamplesX, map.SamplesZ, map.CellSize, map.HeightsCm,
            new[] { new Prop(1, kind, new Obb2(new Vector2(10f, 10f), new Vector2(0.5f, 0.5f), 0f), -0.3f, 6f) }, map.Border);
        Assert.Equal(PropKind.House, Assert.Single(Decode(Encode(withKind(PropKind.House))).Props).Kind);
        Assert.Equal(PropKind.Tree, Assert.Single(Decode(Encode(withKind(PropKind.Tree))).Props).Kind);
        Assert.Throws<ProtocolException>(() => Decode(Encode(withKind(Prop.LastKind + 1))));
    }

    [Fact]
    public void Too_many_props_or_runs_are_refused_before_allocating()
    {
        // As many props as the limit allows go through; one more is refused although every byte of it is there.
        Assert.Equal(16384, BattleMessages.MaxMapProps);
        Assert.Equal(BattleMessages.MaxMapProps, Decode(Encode(TinyMap(BattleMessages.MaxMapProps))).Props.Length);
        Assert.Throws<ProtocolException>(() => Decode(Encode(TinyMap(BattleMessages.MaxMapProps + 1))));

        // A map that claims the limit's props but carries 10 bytes: refused before 16384 props are allocated.
        var w = new BitWriter();
        w.WriteVarUInt(2); w.WriteVarUInt(2); BattleMessages.WriteFloat(w, 2f); BattleMessages.WriteFloat(w, 0f);
        for (int k = 0; k < 4; k++) w.WriteVarInt(0);
        w.WriteVarUInt(BattleMessages.MaxMapProps);
        for (int k = 0; k < 10; k++) w.WriteByte(0);
        byte[] lie = w.Data.ToArray();
        Assert.Throws<ProtocolException>(() => Decode(lie));   // warm up: the first call may JIT
        long before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Throws<ProtocolException>(() => Decode(lie));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 64 * 1024, $"{allocated} bytes allocated");

        // More runs than cells, with every byte of them there.
        MapData map = SmallMap(waterLevelCm: null);
        var tooMany = new (int, byte)[Cells + 1];
        Array.Fill(tooMany, (1, (byte)0));
        Assert.Throws<ProtocolException>(() => Decode(WithTrailer(map, w2 => Runs(w2, tooMany))));
        // A run count the bytes left could not hold (each run is at least 2 bytes), though within the cells.
        Assert.Throws<ProtocolException>(() => Decode(WithTrailer(map, w2 => { w2.WriteVarUInt(Cells); w2.WriteVarUInt(1); w2.WriteByte(0); })));
    }

    [Fact]
    public void A_water_level_beyond_the_limit_is_refused()
    {
        MapData map = SmallMap(waterLevelCm: null);
        foreach (int level in new[] { MapData.MaxWaterLevelCm, -MapData.MaxWaterLevelCm })
            Assert.Equal(level, Decode(WithTrailer(map, w => Runs(w, (Cells, 0)), level)).WaterLevelCm);
        foreach (int level in new[] { MapData.MaxWaterLevelCm + 1, -MapData.MaxWaterLevelCm - 1, int.MaxValue, int.MinValue })
            Assert.Throws<ProtocolException>(() => Decode(WithTrailer(map, w => Runs(w, (Cells, 0)), level)));
    }

    [Fact]
    public void Every_truncation_of_a_v3_map_is_a_protocol_error()
    {
        byte[] bytes = Encode(SmallMap(waterLevelCm: -37));
        Decode(bytes);
        for (int len = 0; len < bytes.Length; len++)
        {
            byte[] cut = bytes[..len];
            Assert.Throws<ProtocolException>(() => Decode(cut));
        }
    }

    [Fact]
    public void More_prop_states_than_map_props_are_refused()
    {
        MapData map = SmallMap(waterLevelCm: -37);
        Assert.Equal(5, map.Props.Length);
        PropState[] states = Enumerable.Range(1, 6).Select(id => new PropState(id, 9)).ToArray();

        byte[] five = MatchStart(map, states[..5]);
        var r = new BitReader(five);
        r.ReadByte();
        Assert.Equal(states[..5], BattleMessages.ReadMatchStart(ref r).Props);

        byte[] six = MatchStart(map, states);
        Assert.Throws<ProtocolException>(() => { var r6 = new BitReader(six); r6.ReadByte(); BattleMessages.ReadMatchStart(ref r6); });
    }

    [Fact]
    public void A_prepared_map_writes_the_same_match_start()
    {
        MapData map = MapData.Of(SandboxMap.Create());
        var m = new MatchStartMessage(BattleMode.TeamBattle, 3, 150, 12750, map, new[] { new PropState(300, 0), new PropState(301, 128) }, 7);
        var w = new BitWriter(128 * 1024);
        BattleMessages.Write(w, m);
        byte[] encoded = Encode(map);

        var w2 = new BitWriter(16);   // grows as the map is copied in
        BattleMessages.Write(w2, m, encoded);

        Assert.Equal(w.Data.ToArray(), w2.Data.ToArray());
        Assert.Equal(8 + 53053 + 7 + 1 + 1, w2.ByteLength);   // header and flags · map · two prop states · tank · no bases: 53070
        Assert.InRange(w2.Capacity, w2.ByteLength, w2.ByteLength + 64);   // grown once, to what the message takes, not doubled past it
        Assert.Throws<ArgumentException>(() => BattleMessages.Write(new BitWriter(), m with { Map = null }, encoded));
    }

    [Fact]
    public void A_match_start_without_the_map_round_trips_in_a_few_bytes()
    {
        var m = new MatchStartMessage(BattleMode.FreeForAll, 4, 9000, 21600, null, new[] { new PropState(300, 0), new PropState(5601, 128) }, 7);
        var w = new BitWriter();
        BattleMessages.Write(w, m);

        Assert.Equal(new byte[] { (byte)BattleMessage.MatchStart, (byte)BattleMode.FreeForAll, 4 }, w.Data[..3].ToArray());
        Assert.Equal(0, w.Data[3 + 2 + 3]);              // the flags after the two ticks (2 and 3 bytes): no map
        Assert.Equal(18, w.ByteLength);                  // v3's 17 and the base count (none)
        var r = new BitReader(w.Data);
        r.ReadByte();
        MatchStartMessage back = BattleMessages.ReadMatchStart(ref r);
        Assert.Null(back.Map);
        Assert.Equal((m.Mode, m.MatchNumber, m.CurrentTick, m.EndTick, m.MyTankId), (back.Mode, back.MatchNumber, back.CurrentTick, back.EndTick, back.MyTankId));
        Assert.Equal(m.Props, back.Props);
    }

    [Fact]
    public void Unknown_match_start_and_request_flags_are_refused()
    {
        var w = new BitWriter();
        BattleMessages.Write(w, new MatchStartMessage(BattleMode.TeamBattle, 1, 5, 100, null, Array.Empty<PropState>(), 2));
        byte[] start = w.Data.ToArray();
        start[5] = 2;   // type, mode, three one-byte numbers, then the flags
        ProtocolException ex = Assert.Throws<ProtocolException>(() => { var r = new BitReader(start); r.ReadByte(); BattleMessages.ReadMatchStart(ref r); });
        Assert.Contains("flags", ex.Message);

        w.Reset();
        BattleMessages.Write(w, new MatchStateRequest(NeedMap: true));
        Assert.Equal(new byte[] { (byte)BattleMessage.RequestMatchState, 1 }, w.Data.ToArray());
        byte[] request = { (byte)BattleMessage.RequestMatchState, 3 };
        Assert.Throws<ProtocolException>(() => { var r = new BitReader(request); r.ReadByte(); BattleMessages.ReadMatchStateRequest(ref r); });
        var ok = new BitReader(new byte[] { (byte)BattleMessage.RequestMatchState, 0 });
        ok.ReadByte();
        Assert.False(BattleMessages.ReadMatchStateRequest(ref ok).NeedMap);
    }

    [Fact]
    public void WriteAligned_needs_a_byte_boundary()
    {
        var w = new BitWriter(4);
        w.WriteBool(true);
        Assert.Throws<InvalidOperationException>(() => w.WriteAligned(new byte[] { 1, 2 }));

        w.Reset();
        w.WriteByte(0xAB);
        w.WriteAligned(new byte[] { 1, 2, 3, 4, 5, 6 });
        w.WriteAligned(ReadOnlySpan<byte>.Empty);
        Assert.Equal(7, w.ByteLength);
        Assert.Equal(56, w.BitLength);
        w.WriteBits(1, 1);
        Assert.Equal(new byte[] { 0xAB, 1, 2, 3, 4, 5, 6, 1 }, w.Data.ToArray());
    }

    // ---- helpers ----

    /// <summary>
    /// 41 × 41 samples at 2 m, all four ground kinds in runs of many lengths, and five props: two trees, a house, a brick
    /// wall segment and a bush.
    /// </summary>
    private static MapData SmallMap(int? waterLevelCm)
    {
        var heights = new int[Size * Size];
        for (int j = 0; j < Size; j++)
        for (int i = 0; i < Size; i++)
            heights[j * Size + i] = (i * 37 + j * 91) % 500 - 250;
        var surface = new SurfaceKind[Cells];
        for (int k = 0; k < Cells; k++)
        {
            int i = k % (Size - 1), j = k / (Size - 1);
            surface[k] = j < 4 ? SurfaceKind.Grass : (SurfaceKind)((i / 7 + j / 5 + (i * j) % 3 / 2) % 4);
        }
        var props = new[]
        {
            new Prop(1, PropKind.Tree, new Obb2(new Vector2(20f, 30f), new Vector2(0.4f, 0.4f), 0f), -0.3f, 9.3f),
            new Prop(2, PropKind.Tree, new Obb2(new Vector2(25.5f, 33f), new Vector2(0.55f, 0.55f), 0f), -0.3f, 12.1f),
            new Prop(3, PropKind.House, new Obb2(new Vector2(50f, 50f), new Vector2(5f, 3.5f), 0.7f), -0.3f, 6.8f),
            new Prop(4, PropKind.BrickWall, new Obb2(new Vector2(60f, 40f), new Vector2(1f, 0.25f), 1.2f), -0.3f, 2.3f),
            new Prop(5, PropKind.Bush, new Obb2(new Vector2(70f, 20f), new Vector2(1.5f, 1.5f), 0f), 0f, 1.2f),
        };
        return new MapData(Size, Size, 2f, heights, props, 4f, surface, waterLevelCm);
    }

    /// <summary>2 × 2 flat samples and <paramref name="props"/> rocks.</summary>
    private static MapData TinyMap(int props)
    {
        var list = new Prop[props];
        for (int k = 0; k < props; k++)
            list[k] = new Prop(k + 1, PropKind.Rock, new Obb2(new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), 0f), 0f, 1f);
        return new MapData(2, 2, 2f, new int[4], list, 0f);
    }

    private static byte[] Encode(MapData map)
    {
        var w = new BitWriter(64 * 1024);
        BattleMessages.WriteMap(w, map);
        return w.Data.ToArray();
    }

    private static MapData Decode(byte[] bytes)
    {
        var r = new BitReader(bytes);
        return BattleMessages.ReadMap(ref r);
    }

    /// <summary>How long the v2 part of <paramref name="map"/>'s encoding is: all of it but the trailer of a grass map without water.</summary>
    private static int V2Length(MapData map)
    {
        var grass = new MapData(map.SamplesX, map.SamplesZ, map.CellSize, map.HeightsCm, map.Props, map.Border);
        var trailer = new BitWriter();
        trailer.WriteByte(0);
        Runs(trailer, ((map.SamplesX - 1) * (map.SamplesZ - 1), 0));
        return Encode(grass).Length - trailer.ByteLength;
    }

    /// <summary><paramref name="map"/>'s v2 bytes, then flags and the water level as given, then <paramref name="surface"/>.</summary>
    private static byte[] WithTrailer(MapData map, Action<BitWriter> surface, int? waterLevelCm = null)
    {
        byte[] v2 = Encode(map)[..V2Length(map)];
        var w = new BitWriter(v2.Length + 4096);
        foreach (byte b in v2) w.WriteByte(b);
        w.WriteByte(waterLevelCm is null ? (byte)0 : (byte)1);
        if (waterLevelCm is { } level) w.WriteVarInt(level);
        surface(w);
        return w.Data.ToArray();
    }

    private static void Runs(BitWriter w, params (int Length, byte Kind)[] runs)
    {
        w.WriteVarUInt((uint)runs.Length);
        foreach ((int length, byte kind) in runs)
        {
            w.WriteVarUInt((uint)length);
            w.WriteByte(kind);
        }
    }

    private static byte[] MatchStart(MapData map, PropState[] states)
    {
        var w = new BitWriter(64 * 1024);
        BattleMessages.Write(w, new MatchStartMessage(BattleMode.FreeForAll, 1, 5, 900, map, states, 2));
        return w.Data.ToArray();
    }

    private static ulong Fnv1a64(ReadOnlySpan<byte> bytes)
    {
        ulong hash = 14695981039346656037UL;
        foreach (byte b in bytes)
        {
            hash ^= b;
            hash *= 1099511628211UL;
        }
        return hash;
    }
}
