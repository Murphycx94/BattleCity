using System.Numerics;
using Tank.Net.Protocol;
using Tank.Sim;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Modes;

namespace Tank.Net.Battle;

public sealed record HelloMessage(int ProtocolVersion, string Name, uint SessionToken);

public sealed record WelcomeMessage(byte Slot, uint SessionToken);

public sealed record RejectMessage(string Reason);

/// <summary>One seat in the room. <see cref="TankIndex"/> is an index into <c>TankCatalog.All</c>.</summary>
public sealed record RoomPlayer(byte Slot, string Name, byte Team, byte TankIndex, bool Ready, bool Connected, bool IsBot);

public sealed record RoomStateMessage(BattleMode Mode, byte HostSlot, byte Bots, bool MatchRunning, IReadOnlyList<RoomPlayer> Players);

public enum RoomCommandKind : byte
{
    SetMode = 0,
    SetTeam = 1,
    SetTank = 2,
    SetBots = 3,
    SetReady = 4,
    StartMatch = 5,
    ReturnToRoom = 6,
}

public sealed record RoomCommandMessage(RoomCommandKind Kind, string Arg);

/// <summary>A destructible prop's hit points, quantised (see <see cref="BattleProtocol.QuantizeDurability"/>).</summary>
public readonly record struct PropState(int PropId, byte Durability);

/// <summary>
/// Everything a client needs to join a match in progress (design §5.4): the map itself, the props that aren't as new,
/// the clock, and which tank is the client's (0: none, it spectates or waits).
/// </summary>
public sealed record MatchStartMessage(BattleMode Mode, int MatchNumber, int CurrentTick, int EndTick, MapData Map, IReadOnlyList<PropState> Props, int MyTankId);

/// <summary>A tick's events for one client, with the new hit points of every prop that changed (design §5.6).</summary>
public readonly record struct EventBatch(int Tick, IReadOnlyList<GameEvent> Events, IReadOnlyList<PropState> Props);

public sealed record RoomInfoMessage(int ProtocolVersion, string RoomName, BattleMode Mode, int Players, int MaxPlayers, bool MatchRunning, int Port);

/// <summary>Encoders and decoders for everything but inputs and snapshots. Decoders assume the type byte was read.</summary>
public static class BattleMessages
{
    private const int MaxEventsPerBatch = 1024;
    private const int MaxPropsPerMessage = 4096;
    private const int MaxMapSamples = 1024 * 1024;

    // ---- Hello / Welcome / Reject ----

    public static void Write(BitWriter w, HelloMessage m)
    {
        w.WriteByte((byte)BattleMessage.Hello);
        w.WriteVarUInt((uint)m.ProtocolVersion);
        w.WriteString(m.Name, BattleProtocol.MaxNameBytes);
        w.WriteBits(m.SessionToken, 32);
    }

    public static HelloMessage ReadHello(ref BitReader r) => new(ReadNonNegative(ref r), r.ReadString(BattleProtocol.MaxNameBytes), r.ReadBits(32));

    public static void Write(BitWriter w, WelcomeMessage m)
    {
        w.WriteByte((byte)BattleMessage.Welcome);
        w.WriteByte(m.Slot);
        w.WriteBits(m.SessionToken, 32);
    }

    public static WelcomeMessage ReadWelcome(ref BitReader r) => new(r.ReadByte(), r.ReadBits(32));

    public static void Write(BitWriter w, RejectMessage m)
    {
        w.WriteByte((byte)BattleMessage.Reject);
        w.WriteString(m.Reason, BattleProtocol.MaxRejectReasonBytes);
    }

    public static RejectMessage ReadReject(ref BitReader r) => new(r.ReadString(BattleProtocol.MaxRejectReasonBytes));

    // ---- Room ----

    public static void Write(BitWriter w, RoomStateMessage m)
    {
        w.WriteByte((byte)BattleMessage.RoomState);
        w.WriteByte((byte)m.Mode);
        w.WriteByte(m.HostSlot);
        w.WriteByte(m.Bots);
        w.WriteBool(m.MatchRunning);
        w.WriteVarUInt((uint)m.Players.Count);
        foreach (RoomPlayer p in m.Players)
        {
            w.WriteByte(p.Slot);
            w.WriteString(p.Name, BattleProtocol.MaxNameBytes);
            w.WriteBits(p.Team, 4);
            w.WriteBits(p.TankIndex, 2);
            w.WriteBool(p.Ready);
            w.WriteBool(p.Connected);
            w.WriteBool(p.IsBot);
        }
    }

    public static RoomStateMessage ReadRoomState(ref BitReader r)
    {
        BattleMode mode = ReadMode(ref r);
        byte host = r.ReadByte(), bots = r.ReadByte();
        bool running = r.ReadBool();
        uint count = r.ReadVarUInt();
        if (count > BattleProtocol.MaxPlayers) throw new ProtocolException("Too many room players.");
        var players = new List<RoomPlayer>((int)count);
        for (int i = 0; i < count; i++)
            players.Add(new RoomPlayer(r.ReadByte(), r.ReadString(BattleProtocol.MaxNameBytes), (byte)r.ReadBits(4), (byte)r.ReadBits(2), r.ReadBool(), r.ReadBool(), r.ReadBool()));
        return new RoomStateMessage(mode, host, bots, running, players);
    }

    public static void Write(BitWriter w, RoomCommandMessage m)
    {
        w.WriteByte((byte)BattleMessage.RoomCommand);
        w.WriteByte((byte)m.Kind);
        w.WriteString(m.Arg);
    }

    public static RoomCommandMessage ReadRoomCommand(ref BitReader r)
    {
        byte kind = r.ReadByte();
        if (kind > (byte)RoomCommandKind.ReturnToRoom) throw new ProtocolException("Unknown room command.");
        return new RoomCommandMessage((RoomCommandKind)kind, r.ReadString());
    }

    // ---- Match start ----

    public static void Write(BitWriter w, MatchStartMessage m)
    {
        w.WriteByte((byte)BattleMessage.MatchStart);
        w.WriteByte((byte)m.Mode);
        w.WriteVarUInt((uint)m.MatchNumber);
        w.WriteVarUInt((uint)m.CurrentTick);
        w.WriteVarUInt((uint)m.EndTick);
        WriteMap(w, m.Map);
        WriteProps(w, m.Props);
        w.WriteVarUInt((uint)m.MyTankId);
    }

    public static MatchStartMessage ReadMatchStart(ref BitReader r)
    {
        BattleMode mode = ReadMode(ref r);
        int number = ReadNonNegative(ref r), tick = ReadTick(ref r), end = ReadTick(ref r);
        MapData map = ReadMap(ref r);
        PropState[] props = ReadProps(ref r);
        int mine = ReadNonNegative(ref r);
        return new MatchStartMessage(mode, number, tick, end, map, props, mine);
    }

    /// <summary>
    /// The map (design §5.4): heights as the difference from the sample before, props field by field. About 60 KB for
    /// the sandbox map's 201 × 201 samples and 450 props.
    /// </summary>
    public static void WriteMap(BitWriter w, MapData map)
    {
        w.WriteVarUInt((uint)map.SamplesX);
        w.WriteVarUInt((uint)map.SamplesZ);
        WriteFloat(w, map.CellSize);
        WriteFloat(w, map.Border);
        int previous = 0;
        foreach (int h in map.HeightsCm)
        {
            w.WriteVarInt(h - previous);
            previous = h;
        }
        w.WriteVarUInt((uint)map.Props.Length);
        foreach (Prop p in map.Props)
        {
            w.WriteVarUInt((uint)p.Id);
            w.WriteByte((byte)p.Kind);
            WriteFloat(w, p.Footprint.Center.X);
            WriteFloat(w, p.Footprint.Center.Y);
            WriteFloat(w, p.Footprint.HalfExtents.X);
            WriteFloat(w, p.Footprint.HalfExtents.Y);
            WriteFloat(w, p.Footprint.Yaw);
            WriteFloat(w, p.Bottom);
            WriteFloat(w, p.Height);
        }
    }

    public static MapData ReadMap(ref BitReader r)
    {
        int sx = ReadNonNegative(ref r), sz = ReadNonNegative(ref r);
        if (sx < 2 || sz < 2 || (long)sx * sz > MaxMapSamples) throw new ProtocolException("Bad map size.");
        float cell = ReadFloat(ref r), border = ReadFloat(ref r);
        if (r.RemainingBits < (long)sx * sz * 8) throw new ProtocolException("Map shorter than its size says.");   // a height is at least a byte: don't allocate for a lie
        var heights = new int[sx * sz];
        int previous = 0;
        for (int k = 0; k < heights.Length; k++) heights[k] = previous += r.ReadVarInt();
        uint count = r.ReadVarUInt();
        if (count > MaxPropsPerMessage) throw new ProtocolException("Too many props.");
        var props = new Prop[count];
        for (int i = 0; i < count; i++)
        {
            int id = ReadNonNegative(ref r);
            byte kind = r.ReadByte();
            if (kind > (byte)PropKind.Fence) throw new ProtocolException("Unknown prop kind.");
            var center = new Vector2(ReadFloat(ref r), ReadFloat(ref r));
            var half = new Vector2(ReadFloat(ref r), ReadFloat(ref r));
            float yaw = ReadFloat(ref r), bottom = ReadFloat(ref r), height = ReadFloat(ref r);
            props[i] = new Prop(id, (PropKind)kind, new Obb2(center, half, yaw), bottom, height);
        }
        try
        {
            return new MapData(sx, sz, cell, heights, props, border);
        }
        catch (ArgumentException ex)
        {
            throw new ProtocolException($"Bad map: {ex.Message}");
        }
    }

    // ---- Events ----

    public static void Write(BitWriter w, in EventBatch batch)
    {
        w.WriteByte((byte)BattleMessage.Events);
        w.WriteVarUInt((uint)batch.Tick);
        w.WriteVarUInt((uint)batch.Events.Count);
        for (int i = 0; i < batch.Events.Count; i++)   // indexed: a foreach over the interface boxes an enumerator every tick
        {
            GameEvent e = batch.Events[i];
            w.WriteByte((byte)e.Type);
            w.WriteVarUInt((uint)e.TankId);
            w.WriteVarUInt((uint)e.ShellId);
            WriteVector(w, e.Position);
            WriteVector(w, e.Velocity);
            w.WriteVarUInt((uint)e.TargetTankId);
            w.WriteBits((uint)e.Result, 2);
            w.WriteVarUInt((uint)e.Damage);
            w.WriteBits((uint)e.Ammo, 1);
            w.WriteVarUInt((uint)e.PropId);
        }
        WriteProps(w, batch.Props);
    }

    public static EventBatch ReadEvents(ref BitReader r)
    {
        int tick = ReadTick(ref r);
        uint count = r.ReadVarUInt();
        if (count > MaxEventsPerBatch) throw new ProtocolException("Too many events.");
        var events = new GameEvent[count];
        for (int i = 0; i < count; i++)
        {
            byte type = r.ReadByte();
            if (type > (byte)GameEventType.PropDestroyed) throw new ProtocolException("Unknown event type.");
            int tank = ReadNonNegative(ref r), shell = ReadNonNegative(ref r);
            Vector3 position = ReadVector(ref r), velocity = ReadVector(ref r);
            int target = ReadNonNegative(ref r);
            var result = (HitResult)r.ReadBits(2);
            int damage = ReadNonNegative(ref r);
            var ammo = (AmmoType)r.ReadBits(1);
            int prop = ReadNonNegative(ref r);
            events[i] = new GameEvent((GameEventType)type, tick, tank, shell, position, velocity, target, result, damage, ammo, prop);
        }
        return new EventBatch(tick, events, ReadProps(ref r));
    }

    private static void WriteProps(BitWriter w, IReadOnlyList<PropState> props)
    {
        w.WriteVarUInt((uint)props.Count);
        for (int i = 0; i < props.Count; i++)
        {
            PropState p = props[i];
            w.WriteVarUInt((uint)p.PropId);
            w.WriteByte(p.Durability);
        }
    }

    private static PropState[] ReadProps(ref BitReader r)
    {
        uint count = r.ReadVarUInt();
        if (count > MaxPropsPerMessage) throw new ProtocolException("Too many prop states.");
        var props = new PropState[count];
        for (int i = 0; i < count; i++) props[i] = new PropState(ReadNonNegative(ref r), r.ReadByte());
        return props;
    }

    // ---- Discovery ----

    public static void Write(BitWriter w, RoomInfoMessage m)
    {
        w.WriteByte((byte)BattleMessage.RoomInfo);
        w.WriteVarUInt((uint)m.ProtocolVersion);
        w.WriteString(m.RoomName, BattleProtocol.MaxRoomNameBytes);
        w.WriteByte((byte)m.Mode);
        w.WriteVarUInt((uint)m.Players);
        w.WriteVarUInt((uint)m.MaxPlayers);
        w.WriteBool(m.MatchRunning);
        w.WriteVarUInt((uint)m.Port);
    }

    public static RoomInfoMessage ReadRoomInfo(ref BitReader r)
    {
        int version = ReadNonNegative(ref r);
        string name = r.ReadString(BattleProtocol.MaxRoomNameBytes);
        BattleMode mode = ReadMode(ref r);
        int players = ReadNonNegative(ref r), max = ReadNonNegative(ref r);
        bool running = r.ReadBool();
        int port = ReadNonNegative(ref r);
        if (max > BattleProtocol.MaxPlayers || players > max) throw new ProtocolException("Bad player counts.");
        if (port is < 1 or > ushort.MaxValue) throw new ProtocolException("Bad port.");   // an endpoint can't be made of it
        return new RoomInfoMessage(version, name, mode, players, max, running, port);
    }

    // ---- primitives ----

    /// <summary>A count, tick or id: a VarUInt that must fit a non-negative int (a bigger one is garbage, not a negative number).</summary>
    public static int ReadNonNegative(ref BitReader r)
    {
        uint value = r.ReadVarUInt();
        return value <= int.MaxValue ? (int)value : throw new ProtocolException("Value out of range.");
    }

    /// <summary>A tick: non-negative and at most <see cref="BattleProtocol.MaxTick"/>, so clock arithmetic on it cannot overflow.</summary>
    public static int ReadTick(ref BitReader r)
    {
        int tick = ReadNonNegative(ref r);
        return tick <= BattleProtocol.MaxTick ? tick : throw new ProtocolException("Tick out of range.");
    }

    public static void WriteFloat(BitWriter w, float value) => w.WriteBits(BitConverter.SingleToUInt32Bits(value), 32);

    public static float ReadFloat(ref BitReader r)
    {
        float value = BitConverter.UInt32BitsToSingle(r.ReadBits(32));
        return float.IsFinite(value) ? value : throw new ProtocolException("Non-finite number.");
    }

    public static void WriteVector(BitWriter w, Vector3 v)
    {
        WriteFloat(w, v.X);
        WriteFloat(w, v.Y);
        WriteFloat(w, v.Z);
    }

    public static Vector3 ReadVector(ref BitReader r) => new(ReadFloat(ref r), ReadFloat(ref r), ReadFloat(ref r));

    private static BattleMode ReadMode(ref BitReader r)
    {
        byte mode = r.ReadByte();
        return mode <= (byte)BattleMode.FreeForAll ? (BattleMode)mode : throw new ProtocolException("Unknown battle mode.");
    }
}
