using System.Numerics;
using Tank.Net.Battle;
using BitReader = Tank.Net.Protocol.BitReader;
using BitWriter = Tank.Net.Protocol.BitWriter;
using ProtocolException = Tank.Net.Protocol.ProtocolException;
using Tank.Sim;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Modes;

namespace Tank.Net.Tests.Battle;

public sealed class BattleProtocolTests
{
    private static BitReader ReaderFor(BitWriter w, BattleMessage expected)
    {
        var r = new BitReader(w.Data);
        Assert.Equal(expected, (BattleMessage)r.ReadByte());
        return r;
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(3.1f)]
    [InlineData(-3.14f)]
    [InlineData(1.2345f)]
    public void Angles_survive_quantisation_to_within_a_step(float angle)
    {
        float back = BattleProtocol.DequantizeAngle(BattleProtocol.QuantizeAngle(angle, BattleProtocol.AimBits), BattleProtocol.AimBits);
        Assert.True(MathF.Abs(Angles.Wrap(back - angle)) <= Angles.TwoPi / 65536, $"{angle} -> {back}");
        float coarse = BattleProtocol.DequantizeAngle(BattleProtocol.QuantizeAngle(angle, BattleProtocol.YawBits), BattleProtocol.YawBits);
        Assert.True(MathF.Abs(Angles.Wrap(coarse - angle)) <= Angles.TwoPi / 4096);
        float pitch = System.Math.Clamp(angle / 2, -1.5f, 1.5f);
        float pitchBack = BattleProtocol.DequantizePitch(BattleProtocol.QuantizePitch(pitch, BattleProtocol.PitchBits), BattleProtocol.PitchBits);
        Assert.True(MathF.Abs(pitchBack - pitch) <= Angles.Pi / 1023);
        // Quantising a quantised value changes nothing: a client predicting with QuantizedAim matches the server exactly.
        var (qy, qp) = BattleProtocol.QuantizedAim(angle, pitch);
        Assert.Equal((qy, qp), BattleProtocol.QuantizedAim(qy, qp));
    }

    [Fact]
    public void Durability_quantises_with_down_and_untouched_kept_exact()
    {
        Assert.Equal(0, BattleProtocol.QuantizeDurability(0, 900));
        Assert.Equal(255, BattleProtocol.QuantizeDurability(900, 900));
        Assert.InRange(BattleProtocol.QuantizeDurability(1, 900), 1, 254);
        Assert.InRange(BattleProtocol.QuantizeDurability(899, 900), 1, 254);
        Assert.Equal(0, BattleProtocol.DequantizeDurability(0, 900));
        Assert.Equal(900, BattleProtocol.DequantizeDurability(255, 900));
        Assert.InRange(BattleProtocol.DequantizeDurability(BattleProtocol.QuantizeDurability(450, 900), 900), 440, 460);
        Assert.Equal(1, BattleProtocol.DequantizeDurability(BattleProtocol.QuantizeDurability(1, 1), 1));
        for (int q = 0; q <= 255; q++) Assert.InRange(BattleProtocol.DequantizeDurability((byte)q, 1), 0, 1);   // nothing to clamp between 1 and 0
    }

    [Fact]
    public void Inputs_round_trip_with_the_aim_quantised()
    {
        var w = new BitWriter();
        var (yaw, pitch) = BattleProtocol.QuantizedAim(2.5f, -0.3f);
        var inputs = new[]
        {
            new InputCmd(107, 1, -1, yaw, pitch, true, AmmoType.HE),
            new InputCmd(106, -1, 0, yaw, pitch, false, AmmoType.AP),
            new InputCmd(105, 0, 1, 0f, 0f, true, AmmoType.AP),
        };
        BattleInputPacket.Write(w, 98, inputs);
        Assert.True(w.ByteLength <= 1 + 2 + 2 + 1 + 3 * 5, $"{w.ByteLength} bytes");
        BitReader r = ReaderFor(w, BattleMessage.Input);
        var output = new InputCmd[BattleProtocol.MaxRedundantInputs];
        int count = BattleInputPacket.Read(ref r, out int ack, output);
        Assert.Equal(3, count);
        Assert.Equal(98, ack);
        Assert.Equal(inputs[0], output[0]);
        Assert.Equal(inputs[1], output[1]);
        Assert.Equal(inputs[2] with { AimYaw = BattleProtocol.QuantizedAim(0f, 0f).Yaw, AimPitch = BattleProtocol.QuantizedAim(0f, 0f).Pitch }, output[2]);
    }

    [Fact]
    public void Room_messages_round_trip()
    {
        var w = new BitWriter();
        var players = new[] { new RoomPlayer(0, "安娜", 0, 1, true, true, false), new RoomPlayer(3, "Bot 2", 1, 3, true, true, true) };
        BattleMessages.Write(w, new RoomStateMessage(BattleMode.Annihilation, 0, 2, false, players));
        BitReader r = ReaderFor(w, BattleMessage.RoomState);
        RoomStateMessage state = BattleMessages.ReadRoomState(ref r);
        Assert.Equal(BattleMode.Annihilation, state.Mode);
        Assert.Equal(2, state.Bots);
        Assert.Equal(players, state.Players);

        w.Reset();
        BattleMessages.Write(w, new RoomCommandMessage(RoomCommandKind.SetTank, "heavy"));
        r = ReaderFor(w, BattleMessage.RoomCommand);
        Assert.Equal(new RoomCommandMessage(RoomCommandKind.SetTank, "heavy"), BattleMessages.ReadRoomCommand(ref r));

        w.Reset();
        BattleMessages.Write(w, new HelloMessage(2, "Bob", 0xDEADBEEF));
        r = ReaderFor(w, BattleMessage.Hello);
        Assert.Equal(new HelloMessage(2, "Bob", 0xDEADBEEF), BattleMessages.ReadHello(ref r));

        w.Reset();
        BattleMessages.Write(w, new RoomInfoMessage(2, "周末房", BattleMode.FreeForAll, 3, 10, true, 27015));
        r = ReaderFor(w, BattleMessage.RoomInfo);
        Assert.Equal(new RoomInfoMessage(2, "周末房", BattleMode.FreeForAll, 3, 10, true, 27015, Map: ""), BattleMessages.ReadRoomInfo(ref r));   // v2 carries no map
    }

    [Fact]
    public void The_sandbox_map_goes_over_the_wire_in_under_90_KB_and_comes_back_the_same()
    {
        MapData map = MapData.Of(SandboxMap.Create());
        var w = new BitWriter(128 * 1024);
        var props = new[] { new PropState(300, 0), new PropState(301, 128) };
        BattleMessages.Write(w, new MatchStartMessage(BattleMode.TeamBattle, 3, 150, 12750, map, props, 7));
        Assert.True(w.ByteLength < 90 * 1024, $"{w.ByteLength / 1024} KB");
        BitReader r = ReaderFor(w, BattleMessage.MatchStart);
        MatchStartMessage m = BattleMessages.ReadMatchStart(ref r);
        Assert.Equal(BattleMode.TeamBattle, m.Mode);
        Assert.Equal((3, 150, 12750, 7), (m.MatchNumber, m.CurrentTick, m.EndTick, m.MyTankId));
        Assert.NotNull(m.Map);
        Assert.Equal(map.HeightsCm, m.Map.HeightsCm);
        Assert.Equal(map.Props, m.Map.Props);
        Assert.Equal((map.SamplesX, map.SamplesZ, map.CellSize, map.Border), (m.Map.SamplesX, m.Map.SamplesZ, m.Map.CellSize, m.Map.Border));
        Assert.Equal(props, m.Props);
        Battlefield rebuilt = m.Map.Build();
        Assert.Equal(SandboxMap.Create().Props, rebuilt.Props);
    }

    [Fact]
    public void Events_round_trip_field_for_field()
    {
        var events = new[]
        {
            new GameEvent(GameEventType.ShellFired, 40, 3, 17, new Vector3(1.5f, 2.25f, -3f), new Vector3(100.125f, -20f, 700f), Ammo: AmmoType.HE),
            new GameEvent(GameEventType.TankHit, 40, 3, 17, new Vector3(9, 1, 9), Vector3.UnitZ, 5, HitResult.Ricochet, 0, AmmoType.AP),
            new GameEvent(GameEventType.PropDamaged, 40, 3, 17, new Vector3(9, 1, 9), new Vector3(0, 0, 1), Damage: 120, PropId: 333),
            new GameEvent(GameEventType.TankDestroyed, 40, 3, 17, new Vector3(9, 0, 9), Vector3.Zero, TargetTankId: 5),
        };
        var props = new[] { new PropState(333, 200) };
        var w = new BitWriter();
        BattleMessages.Write(w, new EventBatch(40, events, props));
        BitReader r = ReaderFor(w, BattleMessage.Events);
        EventBatch batch = BattleMessages.ReadEvents(ref r);
        Assert.Equal(40, batch.Tick);
        Assert.Equal(events, batch.Events);
        Assert.Equal(props, batch.Props);
    }

    private static BattleSnapshot Snapshot(int tick, params BattleEntity[] entities)
    {
        var s = new BattleSnapshot { Tick = tick, EntityCount = entities.Length };
        Array.Copy(entities, s.Entities, entities.Length);
        s.Match = new MatchBlock { Phase = MatchPhase.Playing, TicksLeft = 12000 - tick, Spotted = tick % 2 == 0, WinnerTeam = MatchRules.NoTeam, WinnerSlot = MatchRules.NoSlot, TeamScore0 = 3, TeamScore1 = tick % 5 };
        s.HasOwn = true;
        s.Own = new OwnTank { Id = 1, X = 100.123f, Z = 200.456f, Yaw = 0.5f, TurretYaw = -0.25f, GunPitch = 0.05f, Speed = 3.3f, Reload = 1.25f, Bloom = 0.4f, Ammo = AmmoType.HE, ApLeft = 30, HeLeft = 12, ShotCount = tick, Health = 650, ProtectedUntil = 150, LastFired = 30 };
        s.Players[0] = new PlayerStats { Active = true, Team = 0, Spec = 1, TankId = 1, Kills = 2, Deaths = 1, Alive = true };
        s.Players[4] = new PlayerStats { Active = true, Team = 1, Spec = 3, TankId = 7, Kills = 0, Deaths = 3, Alive = false, Rank = 2 };
        return s;
    }

    private static BattleEntity Entity(int id, int x, int z, ushort health = 900) => new()
    {
        Id = id, Slot = (byte)(id % 10), Spec = 2, Team = (byte)(id % 2), X = x, Z = z, Yaw = 1000, TurretYaw = 2000, Pitch = 512, Speed = -23, Health = health, Flags = TankFlags.Protected,
    };

    [Fact]
    public void A_full_snapshot_round_trips_and_a_delta_carries_only_changes()
    {
        var codec = new BattleSnapshotCodec();
        BattleSnapshot a = Snapshot(100, Entity(2, 10000, 20000), Entity(7, 30000, 40000));
        var w = new BitWriter();
        codec.Write(w, a, null, new ClientFeedback { LastProcessedInputTick = 103, InputSlack = 2 });
        int fullBytes = w.ByteLength;
        BitReader r = ReaderFor(w, BattleMessage.Snapshot);
        var decodedA = new BattleSnapshot();
        codec.Read(ref r, _ => null, decodedA, out ClientFeedback feedback, out int baseTick);
        Assert.Equal(0, baseTick);
        Assert.Equal((103, 2), (feedback.LastProcessedInputTick, feedback.InputSlack));
        Assert.Equal(a.Tick, decodedA.Tick);
        Assert.Equal(a.EntitySpan.ToArray(), decodedA.EntitySpan.ToArray());
        Assert.Equal(a.Own, decodedA.Own);
        Assert.Equal(a.Match, decodedA.Match);
        Assert.Equal(a.Players, decodedA.Players);

        // Tick 101: entity 2 moved a little and took a hit (health, flags, speed), 7 is gone (out of sight), 9 appeared;
        // the own tank drove on and one scoreboard line changed.
        BattleEntity hit = Entity(2, 10050, 20000, 640);
        hit.Flags = TankFlags.None;
        hit.Speed = 12;
        BattleSnapshot b = Snapshot(101, hit, Entity(9, 1, 2, 300));
        b.Own = a.Own with { X = a.Own.X + 0.75f, ShotCount = a.Own.ShotCount + 1 };
        b.Players[4] = a.Players[4] with { Kills = 1, Alive = true };
        b.Match = a.Match with { TicksLeft = a.Match.TicksLeft - 1, TeamScore1 = a.Match.TeamScore1 + 1 };
        w.Reset();
        codec.Write(w, b, a, default);
        Assert.True(w.ByteLength < fullBytes, $"delta {w.ByteLength} bytes vs full {fullBytes}");   // this one changes a lot; the quiet one below is the small case
        r = ReaderFor(w, BattleMessage.Snapshot);
        var decodedB = new BattleSnapshot();
        codec.Read(ref r, tick => tick == 100 ? decodedA : null, decodedB, out _, out baseTick);
        Assert.Equal(100, baseTick);
        Assert.Equal(b.EntitySpan.ToArray(), decodedB.EntitySpan.ToArray());
        Assert.Equal(b.Own, decodedB.Own);
        Assert.Equal(b.Players, decodedB.Players);
        Assert.Equal(b.Match, decodedB.Match);

        // Tick 102 against 101: the same entities in the same order, nothing changed but the clock — the smallest delta.
        BattleSnapshot c = Snapshot(102, hit, Entity(9, 1, 2, 300));
        c.Own = b.Own;
        c.Players[4] = b.Players[4];
        c.Match = b.Match with { TicksLeft = b.Match.TicksLeft - 1, Spotted = b.Match.Spotted };
        w.Reset();
        codec.Write(w, c, b, default);
        int quiet = w.ByteLength;
        r = ReaderFor(w, BattleMessage.Snapshot);
        var decodedC = new BattleSnapshot();
        codec.Read(ref r, tick => tick == 101 ? decodedB : null, decodedC, out _, out _);
        Assert.Equal(c.EntitySpan.ToArray(), decodedC.EntitySpan.ToArray());
        Assert.Equal(c.Own, decodedC.Own);
        Assert.Equal(c.Players, decodedC.Players);
        Assert.True(quiet < 24, $"a quiet delta is {quiet} bytes");

        // Without the baseline the delta can't be read.
        byte[] delta = w.Data.ToArray();
        Assert.Throws<ProtocolException>(() =>
        {
            var rr = new BitReader(delta);
            rr.ReadByte();
            codec.Read(ref rr, _ => null, new BattleSnapshot(), out _, out _);
        });
    }

    [Fact]
    public void A_snapshot_without_an_own_tank_and_a_truncated_packet()
    {
        var codec = new BattleSnapshotCodec();
        BattleSnapshot s = Snapshot(5, Entity(3, 1, 1));
        s.HasOwn = false;
        var w = new BitWriter();
        codec.Write(w, s, null, default);
        BitReader r = ReaderFor(w, BattleMessage.Snapshot);
        var decoded = new BattleSnapshot();
        codec.Read(ref r, _ => null, decoded, out _, out _);
        Assert.False(decoded.HasOwn);
        Assert.Equal(default, decoded.Own);

        // Every truncation is a ProtocolException, never an out-of-range read.
        byte[] whole = w.Data.ToArray();
        for (int len = 1; len < whole.Length; len++)
        {
            byte[] cut = whole[..len];
            Assert.Throws<ProtocolException>(() =>
            {
                var rr = new BitReader(cut);
                rr.ReadByte();
                codec.Read(ref rr, _ => null, new BattleSnapshot(), out _, out _);
            });
        }
    }

    [Fact]
    public void Every_truncation_of_an_event_batch_or_a_match_start_is_a_protocol_error()
    {
        var w = new BitWriter(128 * 1024);
        BattleMessages.Write(w, new EventBatch(40, new[] { new GameEvent(GameEventType.ShellFired, 40, 3, 17, Vector3.One, Vector3.UnitZ) }, new[] { new PropState(3, 7) }));
        byte[] events = w.Data.ToArray();
        for (int len = 1; len < events.Length; len++)
        {
            byte[] cut = events[..len];
            Assert.Throws<ProtocolException>(() => { var r = new BitReader(cut); r.ReadByte(); BattleMessages.ReadEvents(ref r); });
        }
        w.Reset();
        BattleMessages.Write(w, new MatchStartMessage(BattleMode.TeamBattle, 1, 5, 12605, MapData.Of(TestWorld.FlatField()), Array.Empty<PropState>(), 2));
        byte[] start = w.Data.ToArray();
        for (int len = 1; len < start.Length; len += 97)   // a stride: the map is tens of KB
        {
            byte[] cut = start[..len];
            Assert.Throws<ProtocolException>(() => { var r = new BitReader(cut); r.ReadByte(); BattleMessages.ReadMatchStart(ref r); });
        }
    }

    [Fact]
    public void Garbage_in_the_headers_is_rejected_before_it_can_do_harm()
    {
        var w = new BitWriter();
        // An input packet whose tick doesn't fit an int, then one with no inputs, then too many, then a bad axis.
        void Inputs(uint newest, uint ack, int count, uint throttle)
        {
            w.Reset();
            w.WriteByte((byte)BattleMessage.Input);
            w.WriteVarUInt(newest);
            w.WriteVarUInt(ack);
            w.WriteBits((uint)count, 3);
            for (int i = 0; i < count; i++) { w.WriteBits(throttle, 2); w.WriteBits(0, 2); w.WriteBits(0, 16); w.WriteBits(0, 16); w.WriteBool(false); w.WriteBits(0, 1); }
        }
        var scratch = new InputCmd[BattleProtocol.MaxRedundantInputs];
        Inputs(uint.MaxValue, 0, 1, 0);
        Assert.Throws<ProtocolException>(() => { BitReader r = ReaderFor(w, BattleMessage.Input); BattleInputPacket.Read(ref r, out _, scratch); });
        Inputs(10, 0, 0, 0);
        Assert.Throws<ProtocolException>(() => { BitReader r = ReaderFor(w, BattleMessage.Input); BattleInputPacket.Read(ref r, out _, scratch); });
        Inputs(10, 0, 5, 0);
        Assert.Throws<ProtocolException>(() => { BitReader r = ReaderFor(w, BattleMessage.Input); BattleInputPacket.Read(ref r, out _, scratch); });
        Inputs(10, 0, 1, 3);
        Assert.Throws<ProtocolException>(() => { BitReader r = ReaderFor(w, BattleMessage.Input); BattleInputPacket.Read(ref r, out _, scratch); });
        Inputs(10, 0, 1, 2);
        { BitReader r = ReaderFor(w, BattleMessage.Input); Assert.Equal(1, BattleInputPacket.Read(ref r, out int ack, scratch)); Assert.Equal(0, ack); }

        // A room state with eleven players; an event batch with too many events; a room info with an impossible port.
        w.Reset();
        w.WriteByte((byte)BattleMessage.RoomState); w.WriteBits(0, 2); w.WriteByte(0); w.WriteByte(0); w.WriteBool(false); w.WriteVarUInt(11);
        Assert.Throws<ProtocolException>(() => { BitReader r = ReaderFor(w, BattleMessage.RoomState); BattleMessages.ReadRoomState(ref r); });
        w.Reset();
        w.WriteByte((byte)BattleMessage.Events); w.WriteVarUInt(40); w.WriteVarUInt(100000);
        Assert.Throws<ProtocolException>(() => { BitReader r = ReaderFor(w, BattleMessage.Events); BattleMessages.ReadEvents(ref r); });
        foreach (int port in new[] { 0, 70000 })
        {
            w.Reset();
            BattleMessages.Write(w, new RoomInfoMessage(BattleProtocol.Version, "room", BattleMode.TeamBattle, 1, 10, false, port));
            Assert.Throws<ProtocolException>(() => { BitReader r = ReaderFor(w, BattleMessage.RoomInfo); BattleMessages.ReadRoomInfo(ref r); });
        }
        // A map that claims a million samples but carries none: refused before the heights are allocated.
        w.Reset();
        w.WriteByte((byte)BattleMessage.MatchStart); w.WriteByte((byte)BattleMode.TeamBattle); w.WriteVarUInt(1); w.WriteVarUInt(1); w.WriteVarUInt(100);
        w.WriteByte(1);   // the map follows
        w.WriteVarUInt(1000); w.WriteVarUInt(1000); BattleMessages.WriteFloat(w, 2f); BattleMessages.WriteFloat(w, 4f);
        Assert.Contains("shorter", Assert.Throws<ProtocolException>(() => { BitReader r = ReaderFor(w, BattleMessage.MatchStart); BattleMessages.ReadMatchStart(ref r); }).Message);
    }

    [Fact]
    public void The_protocol_constants_are_the_designs_numbers()
    {
        Assert.Equal(7, BattleProtocol.Version);
        Assert.Equal("tank3d-v2", BattleProtocol.ConnectKey);
        Assert.Equal(16, BattleProtocol.MaxPlayers);
        Assert.Equal(64, BattleSeat.HistorySize);
        Assert.Equal(30.0, new BattleServerOptions().ReconnectGraceSeconds);
        Assert.Equal(8.0, new BattleServerOptions().ReturnToRoomSeconds);
        Assert.Equal((16, 12, 10), (BattleProtocol.AimBits, BattleProtocol.YawBits, BattleProtocol.PitchBits));
    }

    // ---- decoders refuse garbage with a ProtocolException, and nothing else ----

    /// <summary>Runs the decoder for the packet's type byte over the rest of it.</summary>
    private static void Decode(byte[] packet, Func<int, BattleSnapshot?>? baseline = null)
    {
        var r = new BitReader(packet);
        switch ((BattleMessage)r.ReadByte())
        {
            case BattleMessage.Hello: BattleMessages.ReadHello(ref r); break;
            case BattleMessage.Input: BattleInputPacket.Read(ref r, out _, new InputCmd[BattleProtocol.MaxRedundantInputs]); break;
            case BattleMessage.RoomCommand: BattleMessages.ReadRoomCommand(ref r); break;
            case BattleMessage.RoomState: BattleMessages.ReadRoomState(ref r); break;
            case BattleMessage.RoomInfo: BattleMessages.ReadRoomInfo(ref r); break;
            case BattleMessage.MatchStart: BattleMessages.ReadMatchStart(ref r); break;
            case BattleMessage.Events: BattleMessages.ReadEvents(ref r); break;
            case BattleMessage.RequestMatchState: BattleMessages.ReadMatchStateRequest(ref r); break;
            case BattleMessage.Snapshot: new BattleSnapshotCodec().Read(ref r, baseline ?? (_ => null), new BattleSnapshot(), out _, out _); break;
            default: throw new InvalidOperationException("No decoder for this packet in the test.");
        }
    }

    private static void AssertProtocolError(byte[] packet, Func<int, BattleSnapshot?>? baseline = null) =>
        Assert.Throws<ProtocolException>(() => Decode(packet, baseline));

    private static byte[] Bytes(BitWriter w) => w.Data.ToArray();

    private const uint PastInt = (uint)int.MaxValue + 1;   // a VarUInt that is no non-negative int: a "negative" tick or count

    /// <summary>A full snapshot written field by field (the codec can't write a speed or health that doesn't fit its types).</summary>
    private static byte[] HandMadeSnapshot(uint tick = 100, uint baseTick = 0, uint ticksLeft = 1000, Action<BitWriter>? own = null, Action<BitWriter>? entity = null)
    {
        var w = new BitWriter();
        w.WriteByte((byte)BattleMessage.Snapshot);
        w.WriteVarUInt(tick);
        w.WriteVarUInt(baseTick);
        w.WriteVarInt(0);   // feedback
        w.WriteVarInt(0);
        w.WriteBits(0, 1);   // the match block
        w.WriteVarUInt(ticksLeft);
        w.WriteBool(false);
        w.WriteByte(MatchRules.NoTeam);
        w.WriteByte(MatchRules.NoSlot);
        w.WriteVarUInt(0);
        w.WriteVarUInt(0);
        w.WriteVarUInt(0);
        w.WriteByte(0);   // the bases' progress
        w.WriteByte(0);
        w.WriteBits(0, 2);   // no end reason
        w.WriteByte(0);      // no entrants
        w.WriteBool(own is not null);
        if (own is not null)
        {
            w.WriteBool(false);   // not "same as the baseline"
            own(w);
        }
        if (baseTick != 0) w.WriteBool(false);   // the crates: not "same as the baseline"
        w.WriteBits(0, 7);    // none in sight
        w.WriteBool(false);   // the scoreboard, in full: sixteen empty lines
        for (int i = 0; i < BattleProtocol.MaxPlayers; i++) w.WriteBool(false);
        if (entity is not null)
        {
            w.WriteBool(true);
            w.WriteVarUInt(1);
            w.WriteBits(511, 9);   // a new entity: every field
            entity(w);
        }
        w.WriteBool(false);   // no more changed entities
        w.WriteBool(false);   // no removals
        return Bytes(w);
    }

    private static Action<BitWriter> EntityFields(int speed = 0, uint health = 900) => w =>
    {
        w.WriteByte(0);
        w.WriteBits(0, 2);
        w.WriteBits(0, 4);
        w.WriteVarInt(0);
        w.WriteVarInt(0);
        w.WriteBits(0, BattleProtocol.YawBits);
        w.WriteBits(0, BattleProtocol.YawBits);
        w.WriteBits(0, BattleProtocol.PitchBits);
        w.WriteVarInt(speed);
        w.WriteVarUInt(health);
        w.WriteBits(0, 2);
    };

    private static Action<BitWriter> OwnFields(uint id = 1, float x = 1f) => w =>
    {
        w.WriteVarUInt(id);
        BattleMessages.WriteFloat(w, x);
        for (int i = 0; i < 7; i++) BattleMessages.WriteFloat(w, 0f);   // z, yaw, turret, pitch, speed, reload, bloom
        w.WriteBits(0, 1);
        for (int i = 0; i < 4; i++) w.WriteVarUInt(0);   // ap, he, shots, health
        w.WriteVarInt(0);
        w.WriteVarInt(0);
        w.WriteBool(false);
        w.WriteBits(0, 6);   // no layers
    };

    [Fact]
    public void The_hand_made_snapshot_is_valid_at_the_limits_of_speed_and_health()
    {
        foreach ((int speed, uint health) in new[] { (0, 0u), (short.MaxValue, (uint)ushort.MaxValue), (short.MinValue, 1u) })
        {
            var r = new BitReader(HandMadeSnapshot(entity: EntityFields(speed, health), own: OwnFields()));
            r.ReadByte();
            var decoded = new BattleSnapshot();
            new BattleSnapshotCodec().Read(ref r, _ => null, decoded, out _, out _);
            Assert.Equal(1, decoded.EntityCount);
            Assert.True(decoded.HasOwn);
            Assert.Equal((1, speed, health), (decoded.Entities[0].Id, decoded.Entities[0].Speed, (uint)decoded.Entities[0].Health));
        }
    }

    public static IEnumerable<object[]> GarbageFields()
    {
        yield return new object[] { "delta speed above short", HandMadeSnapshot(entity: EntityFields(speed: short.MaxValue + 1)) };
        yield return new object[] { "delta speed below short", HandMadeSnapshot(entity: EntityFields(speed: short.MinValue - 1)) };
        yield return new object[] { "delta speed int.MinValue", HandMadeSnapshot(entity: EntityFields(speed: int.MinValue)) };
        yield return new object[] { "delta health above ushort", HandMadeSnapshot(entity: EntityFields(health: ushort.MaxValue + 1u)) };
        yield return new object[] { "delta health past int", HandMadeSnapshot(entity: EntityFields(health: uint.MaxValue)) };
        yield return new object[] { "snapshot tick past int", HandMadeSnapshot(tick: PastInt) };
        yield return new object[] { "snapshot tick uint.MaxValue", HandMadeSnapshot(tick: uint.MaxValue) };
        yield return new object[] { "snapshot tick above MaxTick", HandMadeSnapshot(tick: BattleProtocol.MaxTick + 1u) };
        yield return new object[] { "snapshot base tick above MaxTick", HandMadeSnapshot(baseTick: BattleProtocol.MaxTick + 1u) };
        yield return new object[] { "snapshot base tick past int", HandMadeSnapshot(baseTick: PastInt) };
        yield return new object[] { "snapshot ticks left past int", HandMadeSnapshot(ticksLeft: uint.MaxValue) };
        yield return new object[] { "own tank id past int", HandMadeSnapshot(own: OwnFields(id: PastInt)) };
        yield return new object[] { "own tank position NaN", HandMadeSnapshot(own: OwnFields(x: float.NaN)) };
        yield return new object[] { "own tank position infinite", HandMadeSnapshot(own: OwnFields(x: float.PositiveInfinity)) };
        yield return new object[] { "event batch tick past int", Encode(w => { w.WriteByte((byte)BattleMessage.Events); w.WriteVarUInt(PastInt); w.WriteVarUInt(0); w.WriteVarUInt(0); }) };
        yield return new object[] { "event batch tick uint.MaxValue", Encode(w => { w.WriteByte((byte)BattleMessage.Events); w.WriteVarUInt(uint.MaxValue); w.WriteVarUInt(0); w.WriteVarUInt(0); }) };
        yield return new object[] { "event batch tick above MaxTick", Encode(w => { w.WriteByte((byte)BattleMessage.Events); w.WriteVarUInt(BattleProtocol.MaxTick + 1u); w.WriteVarUInt(0); w.WriteVarUInt(0); }) };
        yield return new object[] { "hello version past int", Encode(w => { w.WriteByte((byte)BattleMessage.Hello); w.WriteVarUInt(PastInt); w.WriteString("a", BattleProtocol.MaxNameBytes); w.WriteBits(1, 32); }) };
        yield return new object[] { "room info version past int", Encode(w => { w.WriteByte((byte)BattleMessage.RoomInfo); w.WriteVarUInt(uint.MaxValue); }) };
        yield return new object[] { "input newest tick past int", InputPacket(PastInt, 0) };
        yield return new object[] { "input ack past int", InputPacket(10, PastInt) };
        yield return new object[] { "match number past int", MatchStartPacket(number: PastInt) };
        yield return new object[] { "match start tick past int", MatchStartPacket(tick: PastInt) };
        yield return new object[] { "match end tick uint.MaxValue", MatchStartPacket(end: uint.MaxValue) };
        yield return new object[] { "match start tick above MaxTick", MatchStartPacket(tick: BattleProtocol.MaxTick + 1u) };
        yield return new object[] { "match end tick above MaxTick", MatchStartPacket(end: BattleProtocol.MaxTick + 1u) };
        yield return new object[] { "match start tank id past int", MatchStartPacket(tank: PastInt) };
    }

    [Theory]
    [MemberData(nameof(GarbageFields))]
    public void An_out_of_range_field_is_a_protocol_error(string what, byte[] packet)
    {
        string reason = what.Contains("NaN") || what.Contains("infinite") ? "Non-finite" : "out of range";   // refused for the field's own fault, not by running dry
        ProtocolException ex = Assert.Throws<ProtocolException>(() => Decode(packet));
        Assert.Contains(reason, ex.Message);
    }

    private static byte[] Encode(Action<BitWriter> write)
    {
        var w = new BitWriter();
        write(w);
        return Bytes(w);
    }

    private static byte[] InputPacket(uint newest, uint ack) => Encode(w =>
    {
        w.WriteByte((byte)BattleMessage.Input);
        w.WriteVarUInt(newest);
        w.WriteVarUInt(ack);
        w.WriteBits(1, 3);
        w.WriteBits(1, 2);
        w.WriteBits(1, 2);
        w.WriteBits(0, 16);
        w.WriteBits(0, 16);
        w.WriteBool(false);
        w.WriteBits(0, 1);
    });

    private static byte[] MatchStartPacket(uint number = 1, uint tick = 5, uint end = 100, uint tank = 2)
    {
        var w = new BitWriter(128 * 1024);
        w.WriteByte((byte)BattleMessage.MatchStart);
        w.WriteByte((byte)BattleMode.TeamBattle);
        w.WriteVarUInt(number);
        w.WriteVarUInt(tick);
        w.WriteVarUInt(end);
        w.WriteByte(1);      // the map follows
        BattleMessages.WriteMap(w, MapData.Of(TestWorld.FlatField()));
        w.WriteVarUInt(0);   // no props
        w.WriteVarUInt(tank);
        w.WriteByte(0);      // no bases
        w.WriteByte(0);      // no zone
        w.WriteByte(0);      // no crates
        return Bytes(w);
    }

    [Fact]
    public void The_match_start_helper_is_valid_before_it_is_corrupted()
    {
        var r = new BitReader(MatchStartPacket());
        r.ReadByte();
        MatchStartMessage m = BattleMessages.ReadMatchStart(ref r);
        Assert.Equal((1, 5, 100, 2), (m.MatchNumber, m.CurrentTick, m.EndTick, m.MyTankId));
        Decode(InputPacket(10, 4));
    }

    // ---- every prefix of a packet is a protocol error ----

    public static IEnumerable<object[]> TruncatedPackets()
    {
        foreach (string name in new[] { "delta snapshot", "input", "room state", "room command", "hello", "room info", "match start without the map", "match state request" })
            yield return new object[] { name };
    }

    [Theory]
    [MemberData(nameof(TruncatedPackets))]
    public void Every_truncation_is_a_protocol_error(string name)
    {
        var w = new BitWriter();
        Func<int, BattleSnapshot?>? baseline = null;
        switch (name)
        {
            case "delta snapshot":
                var codec = new BattleSnapshotCodec();
                BattleSnapshot a = Snapshot(100, Entity(2, 10000, 20000), Entity(7, 30000, 40000));
                BattleSnapshot b = Snapshot(101, Entity(2, 10050, 20000, 640), Entity(9, 1, 2, 300));
                b.Own = a.Own with { X = a.Own.X + 0.75f };
                codec.Write(w, b, a, new ClientFeedback { LastProcessedInputTick = 103, InputSlack = 2 });
                baseline = tick => tick == 100 ? a : null;
                break;
            case "input":
                BattleInputPacket.Write(w, 98, new[] { new InputCmd(107, 1, -1, 1f, 0.2f, true, AmmoType.HE), new InputCmd(106, 0, 1, -2f, 0f, false, AmmoType.AP) });
                break;
            case "room state":
                BattleMessages.Write(w, new RoomStateMessage(BattleMode.Annihilation, 0, 2, true, new[] { new RoomPlayer(0, "安娜", 0, 1, true, true, false), new RoomPlayer(3, "Bot 2", 1, 3, true, true, true) }));
                break;
            case "room command":
                BattleMessages.Write(w, new RoomCommandMessage(RoomCommandKind.SetTank, "heavy"));
                break;
            case "hello":
                BattleMessages.Write(w, new HelloMessage(BattleProtocol.Version, "Bob", 0xDEADBEEF));
                break;
            case "match start without the map":
                BattleMessages.Write(w, new MatchStartMessage(BattleMode.TeamBattle, 2, 150, 12750, null, new[] { new PropState(300, 0), new PropState(301, 128) }, 7));
                break;
            case "match state request":
                BattleMessages.Write(w, new MatchStateRequest(NeedMap: true));
                break;
            default:
                BattleMessages.Write(w, new RoomInfoMessage(BattleProtocol.Version, "周末房", BattleMode.FreeForAll, 3, 10, true, 27015));
                break;
        }
        byte[] whole = Bytes(w);
        Decode(whole, baseline);   // the whole packet is fine
        for (int len = 1; len < whole.Length; len++) AssertProtocolError(whole[..len], baseline);
    }

    // ---- room info counts ----

    [Theory]
    [InlineData(5, 3)]     // more players than the room holds
    [InlineData(17, 17)]   // a room bigger than the protocol's sixteen seats
    [InlineData(1, 17)]
    [InlineData(300, 300)]
    [InlineData(0, 255)]
    public void A_room_info_with_impossible_counts_is_a_protocol_error(int players, int max)
    {
        var w = new BitWriter();
        BattleMessages.Write(w, new RoomInfoMessage(BattleProtocol.Version, "room", BattleMode.TeamBattle, players, max, false, 27015));
        AssertProtocolError(Bytes(w));
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(10, 10)]
    [InlineData(2, 4)]
    public void A_room_info_within_the_protocols_seats_is_read(int players, int max)
    {
        var w = new BitWriter();
        BattleMessages.Write(w, new RoomInfoMessage(BattleProtocol.Version, "room", BattleMode.TeamBattle, players, max, false, 27015));
        Decode(Bytes(w));
    }

    // ---- fuller deltas ----

    private static BattleSnapshot Decoded(BattleSnapshotCodec codec, byte[] packet, Func<int, BattleSnapshot?> baseline)
    {
        var r = new BitReader(packet);
        r.ReadByte();
        var output = new BattleSnapshot();
        codec.Read(ref r, baseline, output, out _, out _);
        return output;
    }

    private static byte[] Written(BattleSnapshotCodec codec, BattleSnapshot current, BattleSnapshot? baseline)
    {
        var w = new BitWriter();
        codec.Write(w, current, baseline, default);
        return Bytes(w);
    }

    private static void Mutate(ref BattleEntity e, int field)
    {
        switch (field)
        {
            case 0: e.Slot = (byte)(e.Slot + 1); e.Spec = 3; e.Team = (byte)(1 - e.Team); break;   // meta
            case 1: e.X += 137; break;
            case 2: e.Z -= 2; break;
            case 3: e.Yaw = 3000; break;
            case 4: e.TurretYaw = 4095; break;
            case 5: e.Pitch = 1023; break;
            case 6: e.Speed = 120; break;
            case 7: e.Health = 1; break;
            default: e.Flags = TankFlags.Destroyed; break;
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void A_delta_with_one_changed_field_round_trips_exactly_and_stays_small(int field)
    {
        var codec = new BattleSnapshotCodec();
        BattleSnapshot a = Snapshot(100, Entity(2, 10000, 20000), Entity(5, 30000, 40000));
        BattleEntity changed = a.Entities[1];
        Mutate(ref changed, field);
        BattleSnapshot b = Snapshot(101, a.Entities[0], changed);
        b.Match = a.Match;
        b.Own = a.Own;
        b.Players.AsSpan().Clear();
        a.Players.CopyTo(b.Players, 0);

        BattleSnapshot decodedA = Decoded(codec, Written(codec, a, null), _ => null);
        byte[] delta = Written(codec, b, a);
        BattleSnapshot decodedB = Decoded(codec, delta, tick => tick == 100 ? decodedA : null);

        Assert.Equal(b.EntitySpan.ToArray(), decodedB.EntitySpan.ToArray());
        Assert.Equal(b.Own, decodedB.Own);
        Assert.Equal(b.Players, decodedB.Players);
        Assert.True(delta.Length < 40, $"changing field {field} costs a {delta.Length}-byte delta");
    }

    [Fact]
    public void A_delta_merges_new_and_changed_entities_around_the_baselines_order_and_drops_the_gone()
    {
        var codec = new BattleSnapshotCodec();
        BattleSnapshot a = Snapshot(100, Entity(2, 10000, 20000), Entity(5, 30000, 40000), Entity(8, 50000, 60000));
        BattleEntity two = a.Entities[0], five = a.Entities[1];
        Mutate(ref two, 2);   // Z
        Mutate(ref two, 3);   // yaw
        Mutate(ref five, 4);  // turret
        Mutate(ref five, 5);  // pitch
        // 8 is gone; 1 arrives before the baseline's first, 6 between its second and third, 9 after its last.
        BattleSnapshot b = Snapshot(101, Entity(1, 5, 6, 100), two, five, Entity(6, 7, 8, 200), Entity(9, 9, 10, 300));

        BattleSnapshot decodedA = Decoded(codec, Written(codec, a, null), _ => null);
        BattleSnapshot decodedB = Decoded(codec, Written(codec, b, a), tick => tick == 100 ? decodedA : null);

        Assert.Equal(new[] { 1, 2, 5, 6, 9 }, decodedB.EntitySpan.ToArray().Select(e => e.Id).ToArray());
        Assert.Equal(b.EntitySpan.ToArray(), decodedB.EntitySpan.ToArray());
        Assert.False(decodedB.TryFind(8, out _));
        Assert.True(decodedB.TryFind(6, out BattleEntity six));
        Assert.Equal(200, six.Health);
    }

    // ---- the scoreboard ----

    [Fact]
    public void An_inactive_scoreboard_line_goes_over_the_wire_blank_whatever_it_still_holds()
    {
        var codec = new BattleSnapshotCodec();
        BattleSnapshot clean = Snapshot(7, Entity(3, 1, 1));
        BattleSnapshot stale = Snapshot(7, Entity(3, 1, 1));
        stale.Players[2] = new PlayerStats { Active = false, Team = 1, Spec = 2, TankId = 5, Kills = 9, Deaths = 4, Alive = true, Rank = 3 };
        Assert.Equal(Written(codec, clean, null), Written(codec, stale, null));   // the leaver's stale line costs nothing and says nothing

        BattleSnapshot decoded = Decoded(codec, Written(codec, stale, null), _ => null);
        Assert.Equal(default, decoded.Players[2]);
        Assert.Equal(clean.Players[0], decoded.Players[0]);
        Assert.Equal(clean.Players[4], decoded.Players[4]);

        // A player who leaves between two snapshots: the delta blanks the line.
        BattleSnapshot next = Snapshot(8, Entity(3, 1, 1));
        next.Players[4] = default;
        BattleSnapshot decodedClean = Decoded(codec, Written(codec, clean, null), _ => null);
        BattleSnapshot decodedNext = Decoded(codec, Written(codec, next, clean), tick => tick == 7 ? decodedClean : null);
        Assert.Equal(default, decodedNext.Players[4]);
        Assert.Equal(clean.Players[0], decodedNext.Players[0]);
    }
}
