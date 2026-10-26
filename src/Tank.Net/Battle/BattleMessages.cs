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

/// <summary>Where the server is with a room's map. Values are on the wire: append only.</summary>
public enum MapState : byte
{
    /// <summary>Being generated and encoded: a match can start once it is ready.</summary>
    Generating = 0,

    /// <summary>Generated and encoded: a match can start on it.</summary>
    Ready = 1,

    /// <summary>The generator threw (a bug, logged on the server, never retried): no match can start on it.</summary>
    Failed = 2,
}

/// <summary>The room as everyone in it sees it (stage 5 design §2).</summary>
/// <param name="Map">The id of the map the next match is played on (a <c>MapCatalog</c> id).</param>
/// <param name="MapState">Whether the server has that map ready: only then can a match start.</param>
public sealed record RoomStateMessage(BattleMode Mode, byte HostSlot, byte Bots, bool MatchRunning, IReadOnlyList<RoomPlayer> Players,
    string Map = SandboxMap.Id, MapState MapState = MapState.Ready);

public enum RoomCommandKind : byte
{
    SetMode = 0,
    SetTeam = 1,
    SetTank = 2,
    SetBots = 3,
    SetReady = 4,
    StartMatch = 5,
    ReturnToRoom = 6,

    /// <summary>The host picks the room's map by its id (ignoring case); only in the room, not while a match runs.</summary>
    SetMap = 7,
}

public sealed record RoomCommandMessage(RoomCommandKind Kind, string Arg);

/// <summary>A destructible prop's hit points, quantised (see <see cref="BattleProtocol.QuantizeDurability"/>).</summary>
public readonly record struct PropState(int PropId, byte Durability);

/// <summary>
/// Everything a client needs to join a match in progress (design §5.4): the map itself, the props that aren't as new,
/// the clock, which tank is the client's (0: none, it spectates or waits), and the teams' bases.
/// </summary>
/// <param name="Map">
/// The map; null when the server left it out: its answer to a <see cref="MatchStateRequest"/> from a client that already
/// has this match's map, which keeps its own (a resync costs a few bytes, not the whole map).
/// </param>
/// <param name="Bases">
/// The teams' bases in a standard match (stage 5 design §3.3), for the rings on the ground and the minimap: null or empty
/// for none, else one per team (<see cref="TeamBase.Check"/>). Never null once read: an empty array.
/// </param>
public sealed record MatchStartMessage(BattleMode Mode, int MatchNumber, int CurrentTick, int EndTick, MapData? Map, IReadOnlyList<PropState> Props, int MyTankId,
    IReadOnlyList<TeamBase>? Bases = null);

/// <summary>A client asks for the match state again (<see cref="BattleMessage.RequestMatchState"/>).</summary>
/// <param name="NeedMap">
/// The client has no map for the running match (it was sent a match start without one it can't use): the answer must
/// carry the map even if the server thinks this connection already has it.
/// </param>
public sealed record MatchStateRequest(bool NeedMap);

/// <summary>A tick's events for one client, with the new hit points of every prop that changed (design §5.6).</summary>
public readonly record struct EventBatch(int Tick, IReadOnlyList<GameEvent> Events, IReadOnlyList<PropState> Props);

/// <summary>A room's answer to a LAN discovery broadcast.</summary>
/// <param name="Map">The room's map id; only on the wire from version 3 on, so a v2 room's answer reads as <c>""</c>.</param>
public sealed record RoomInfoMessage(int ProtocolVersion, string RoomName, BattleMode Mode, int Players, int MaxPlayers, bool MatchRunning, int Port,
    string Map = SandboxMap.Id);

/// <summary>Encoders and decoders for everything but inputs and snapshots. Decoders assume the type byte was read.</summary>
public static class BattleMessages
{
    /// <summary>The most props a map may have (riverland design §5.1: the riverland map has about 5600).</summary>
    public const int MaxMapProps = 16384;

    /// <summary>The most bases a match start may carry: one per team of a standard match.</summary>
    public const int MaxBases = 2;

    private const int MaxEventsPerBatch = 1024;
    private const int MaxPropsPerMessage = 4096;   // prop states in an event batch
    private const int MaxMapSamples = 1024 * 1024;
    private const int MinPropBits = 30 * 8;        // a map prop: id (at least a byte), kind, seven floats
    private const int MinRunBits = 2 * 8;          // a ground run: length (at least a byte), kind
    private const int MinPropStateBits = 2 * 8;    // a prop state: id (at least a byte), hit points
    private const byte WaterFlag = 1;
    private const byte MapIncludedFlag = 1;           // match start: the map follows the header
    private const byte NeedMapFlag = 1;               // match state request: send the map whatever the server thinks
    private const int MaxVarUIntBytes = 5;
    private const int MaxMatchStartHeaderBytes = 3 + 3 * MaxVarUIntBytes;   // type, mode, flags and three numbers
    private const int MapInRoomInfoVersion = 3;   // a room's LAN answer names its map from this version on
    private const int BaseBytes = 1 + 3 * 4;      // a base in a match start: team, x, z, radius
    private const float MaxBaseCoordinateMetres = BattleProtocol.MaxCoordinateCm * BattleProtocol.PositionUnit;   // a base centre, either axis: ±100 km

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
        w.WriteString(m.Map, BattleProtocol.MaxMapIdBytes);
        w.WriteByte((byte)m.MapState);
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
        string map = r.ReadString(BattleProtocol.MaxMapIdBytes);
        byte mapState = r.ReadByte();
        if (mapState > (byte)MapState.Failed) throw new ProtocolException("Unknown map state.");
        return new RoomStateMessage(mode, host, bots, running, players, map, (MapState)mapState);
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
        if (kind > (byte)RoomCommandKind.SetMap) throw new ProtocolException("Unknown room command.");
        return new RoomCommandMessage((RoomCommandKind)kind, r.ReadString());
    }

    // ---- Match start ----

    /// <summary>
    /// The type byte, the mode, the match number, the tick and the end tick; a flags byte (bit 0: the map follows; the
    /// others 0); the map (<see cref="WriteMap"/>) when there is one; the props that aren't as new; the client's tank; the
    /// number of bases (a byte, 0 or 2) and each base's team (a byte), centre x and z and radius (floats).
    /// </summary>
    /// <exception cref="ArgumentException"><c>m.Bases</c> are ones the reader would refuse (<see cref="CheckedBases"/>).</exception>
    public static void Write(BitWriter w, MatchStartMessage m)
    {
        IReadOnlyList<TeamBase> bases = CheckedBases(m);   // before a byte is written: a bad message writes nothing
        WriteMatchStartHeader(w, m);
        if (m.Map is not null) WriteMap(w, m.Map);
        WriteMatchStartTail(w, m, bases);
    }

    /// <summary>
    /// The same bytes as <see cref="Write(BitWriter, MatchStartMessage)"/>, with the map copied in from
    /// <paramref name="encodedMap"/> (what <see cref="WriteMap"/> wrote for <c>m.Map</c>) instead of encoded again: a
    /// server encodes a map once and sends it to every seat. Only whole bytes come before the map, so it lands aligned.
    /// The writer is grown once, to all the message can take, before anything is written.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <c>m.Map</c> is null (a match start without the map has nothing to copy in), or <c>m.Bases</c> are ones the reader
    /// would refuse (<see cref="CheckedBases"/>).
    /// </exception>
    public static void Write(BitWriter w, MatchStartMessage m, ReadOnlySpan<byte> encodedMap)
    {
        if (m.Map is null) throw new ArgumentException("A match start without its map takes no encoded map.", nameof(m));
        IReadOnlyList<TeamBase> bases = CheckedBases(m);   // before a byte is written: a bad message writes nothing
        w.Reserve(MaxMatchStartHeaderBytes + encodedMap.Length + MaxMatchStartTailBytes(m.Props.Count));
        WriteMatchStartHeader(w, m);
        w.WriteAligned(encodedMap);
        WriteMatchStartTail(w, m, bases);
    }

    /// <summary>
    /// <c>m.Bases</c> as the reader takes them (<see cref="ReadBases"/>): <see cref="TeamBase.Check"/>'s rules, and each
    /// centre within the wire's coordinate range (±100 km). ArgumentException otherwise.
    /// </summary>
    private static IReadOnlyList<TeamBase> CheckedBases(MatchStartMessage m)
    {
        IReadOnlyList<TeamBase> bases = TeamBase.Check(m.Bases, nameof(m));
        foreach (TeamBase b in bases)
            if (MathF.Abs(b.Centre.X) > MaxBaseCoordinateMetres || MathF.Abs(b.Centre.Y) > MaxBaseCoordinateMetres)
                throw new ArgumentException($"Team {b.Team}'s base centre {b.Centre} is beyond the wire's ±{MaxBaseCoordinateMetres} m.", nameof(m));
        return bases;
    }

    private static void WriteMatchStartHeader(BitWriter w, MatchStartMessage m)
    {
        w.WriteByte((byte)BattleMessage.MatchStart);
        w.WriteByte((byte)m.Mode);
        w.WriteVarUInt((uint)m.MatchNumber);
        w.WriteVarUInt((uint)m.CurrentTick);
        w.WriteVarUInt((uint)m.EndTick);
        w.WriteByte(m.Map is null ? (byte)0 : MapIncludedFlag);
    }

    /// <param name="bases"><c>m.Bases</c> as <see cref="CheckedBases"/> passed them.</param>
    private static void WriteMatchStartTail(BitWriter w, MatchStartMessage m, IReadOnlyList<TeamBase> bases)
    {
        WriteProps(w, m.Props);
        w.WriteVarUInt((uint)m.MyTankId);
        w.WriteByte((byte)bases.Count);
        for (int i = 0; i < bases.Count; i++)
        {
            TeamBase b = bases[i];
            w.WriteByte(b.Team);
            WriteFloat(w, b.Centre.X);
            WriteFloat(w, b.Centre.Y);
            WriteFloat(w, b.Radius);
        }
    }

    /// <summary>The most bytes the tail can take: the prop count, each prop's id and hit points, the tank id, the bases.</summary>
    private static int MaxMatchStartTailBytes(int props) =>
        MaxVarUIntBytes + props * (MaxVarUIntBytes + 1) + MaxVarUIntBytes + 1 + MaxBases * BaseBytes;

    public static MatchStartMessage ReadMatchStart(ref BitReader r)
    {
        BattleMode mode = ReadMode(ref r);
        int number = ReadNonNegative(ref r), tick = ReadTick(ref r), end = ReadTick(ref r);
        byte flags = r.ReadByte();
        if ((flags & ~MapIncludedFlag) != 0) throw new ProtocolException("Unknown match start flags.");
        MapData? map = (flags & MapIncludedFlag) != 0 ? ReadMap(ref r) : null;
        PropState[] props = ReadProps(ref r, map?.Props.Length ?? MaxMapProps);   // a prop that isn't on the map has no state (the client checks a map it keeps)
        int mine = ReadNonNegative(ref r);
        return new MatchStartMessage(mode, number, tick, end, map, props, mine, ReadBases(ref r));
    }

    /// <summary>The bases of a match start: at most <see cref="MaxBases"/>, by <see cref="TeamBase.Check"/>'s rules, within the coordinate limit.</summary>
    private static TeamBase[] ReadBases(ref BitReader r)
    {
        byte count = r.ReadByte();
        if (count == 0) return Array.Empty<TeamBase>();
        if (count > MaxBases) throw new ProtocolException("Too many bases.");
        var bases = new TeamBase[count];
        for (int i = 0; i < count; i++)
        {
            byte team = r.ReadByte();
            float x = ReadFloat(ref r), z = ReadFloat(ref r), radius = ReadFloat(ref r);
            if (MathF.Abs(x) > MaxBaseCoordinateMetres || MathF.Abs(z) > MaxBaseCoordinateMetres) throw new ProtocolException("Base centre out of range.");
            bases[i] = new TeamBase(team, new Vector2(x, z), radius);
        }
        try
        {
            TeamBase.Check(bases, nameof(bases));
        }
        catch (ArgumentException ex)
        {
            throw new ProtocolException($"Bad bases: {ex.Message}");
        }
        return bases;
    }

    // ---- Match state request ----

    /// <summary>The type byte, then a flags byte (bit 0: send the map; the others 0).</summary>
    public static void Write(BitWriter w, MatchStateRequest m)
    {
        w.WriteByte((byte)BattleMessage.RequestMatchState);
        w.WriteByte(m.NeedMap ? NeedMapFlag : (byte)0);
    }

    public static MatchStateRequest ReadMatchStateRequest(ref BitReader r)
    {
        byte flags = r.ReadByte();
        if ((flags & ~NeedMapFlag) != 0) throw new ProtocolException("Unknown match state request flags.");
        return new MatchStateRequest((flags & NeedMapFlag) != 0);
    }

    /// <summary>
    /// The map (stage 4 design §5.4, riverland design §5.1): heights as the difference from the sample before, props field
    /// by field (the v2 bytes); then a flags byte (bit 0: water; the others 0), the water level in centimetres when there is
    /// water, and the ground's kinds run-length encoded: the number of runs, then each run's length (at least 1) and kind,
    /// the lengths adding up to the cells. 53 KB for the sandbox map's 201 × 201 samples and 351 props.
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
        w.WriteByte(map.WaterLevelCm is null ? (byte)0 : WaterFlag);
        if (map.WaterLevelCm is { } level) w.WriteVarInt(level);
        WriteSurface(w, map.Surface);
    }

    private static void WriteSurface(BitWriter w, SurfaceKind[] surface)
    {
        uint runs = 0;
        for (int k = 0; k < surface.Length; k++)
            if (k == 0 || surface[k] != surface[k - 1]) runs++;
        w.WriteVarUInt(runs);
        for (int start = 0; start < surface.Length;)
        {
            int end = start + 1;
            while (end < surface.Length && surface[end] == surface[start]) end++;
            w.WriteVarUInt((uint)(end - start));
            w.WriteByte((byte)surface[start]);
            start = end;
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
        if (count > MaxMapProps) throw new ProtocolException("Too many props.");
        if (count > r.RemainingBits / MinPropBits) throw new ProtocolException("Map shorter than its props say.");   // don't allocate for a lie
        var props = new Prop[count];
        for (int i = 0; i < count; i++)
        {
            int id = ReadNonNegative(ref r);
            byte kind = r.ReadByte();
            if (kind > (byte)Prop.LastKind) throw new ProtocolException("Unknown prop kind.");
            var center = new Vector2(ReadFloat(ref r), ReadFloat(ref r));
            var half = new Vector2(ReadFloat(ref r), ReadFloat(ref r));
            float yaw = ReadFloat(ref r), bottom = ReadFloat(ref r), height = ReadFloat(ref r);
            props[i] = new Prop(id, (PropKind)kind, new Obb2(center, half, yaw), bottom, height);
        }
        byte flags = r.ReadByte();
        if ((flags & ~WaterFlag) != 0) throw new ProtocolException("Unknown map flags.");
        int? waterLevelCm = null;
        if ((flags & WaterFlag) != 0)
        {
            int level = r.ReadVarInt();
            if (level < -MapData.MaxWaterLevelCm || level > MapData.MaxWaterLevelCm) throw new ProtocolException("Water level out of range.");
            waterLevelCm = level;
        }
        SurfaceKind[] surface = ReadSurface(ref r, (sx - 1) * (sz - 1));   // fewer than the samples: no overflow
        try
        {
            return new MapData(sx, sz, cell, heights, props, border, surface, waterLevelCm);
        }
        catch (ArgumentException ex)
        {
            throw new ProtocolException($"Bad map: {ex.Message}");
        }
    }

    private static SurfaceKind[] ReadSurface(ref BitReader r, int cells)
    {
        uint runs = r.ReadVarUInt();
        if (runs > cells) throw new ProtocolException("More ground runs than cells.");
        if (runs > r.RemainingBits / MinRunBits) throw new ProtocolException("Map shorter than its ground runs say.");
        var surface = new SurfaceKind[cells];   // no bigger than the heights already read
        int filled = 0;
        for (uint run = 0; run < runs; run++)
        {
            uint length = r.ReadVarUInt();
            byte kind = r.ReadByte();
            if (length == 0) throw new ProtocolException("Empty ground run.");
            if (length > cells - filled) throw new ProtocolException("Ground runs longer than the cells.");
            if (kind > (byte)SurfaceMap.LastKind) throw new ProtocolException("Unknown ground kind.");
            surface.AsSpan(filled, (int)length).Fill((SurfaceKind)kind);
            filled += (int)length;
        }
        if (filled != cells) throw new ProtocolException("Ground runs shorter than the cells.");
        return surface;
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
        return new EventBatch(tick, events, ReadProps(ref r, MaxPropsPerMessage));
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

    /// <param name="max">The most prop states the message may carry.</param>
    private static PropState[] ReadProps(ref BitReader r, int max)
    {
        uint count = r.ReadVarUInt();
        if (count > max) throw new ProtocolException("Too many prop states.");
        if (count > r.RemainingBits / MinPropStateBits) throw new ProtocolException("Packet shorter than its prop states say.");
        var props = new PropState[count];
        for (int i = 0; i < count; i++) props[i] = new PropState(ReadNonNegative(ref r), r.ReadByte());
        return props;
    }

    // ---- Discovery ----

    /// <summary>The broadcast that asks rooms to answer with their <see cref="RoomInfoMessage"/>: the id, then the version (rooms answer whatever it says).</summary>
    public static void WriteDiscover(BitWriter w)
    {
        w.WriteByte((byte)BattleMessage.Discover);
        w.WriteVarUInt(BattleProtocol.Version);
    }

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
        if (m.ProtocolVersion >= MapInRoomInfoVersion) w.WriteString(m.Map, BattleProtocol.MaxMapIdBytes);
    }

    /// <summary>
    /// A room's LAN answer. The mode is checked only in a room of our version: another version's room keeps its mode byte
    /// as it came (perhaps a mode this build doesn't know), so the room list still shows it, as another version, instead of
    /// dropping it.
    /// </summary>
    public static RoomInfoMessage ReadRoomInfo(ref BitReader r)
    {
        int version = ReadNonNegative(ref r);
        string name = r.ReadString(BattleProtocol.MaxRoomNameBytes);
        BattleMode mode = version == BattleProtocol.Version ? ReadMode(ref r) : (BattleMode)r.ReadByte();
        int players = ReadNonNegative(ref r), max = ReadNonNegative(ref r);
        bool running = r.ReadBool();
        int port = ReadNonNegative(ref r);
        if (max > BattleProtocol.MaxPlayers || players > max) throw new ProtocolException("Bad player counts.");
        if (port is < 1 or > ushort.MaxValue) throw new ProtocolException("Bad port.");   // an endpoint can't be made of it
        // A v2 room ends at the port: read it all the same, so the room list shows it as another version instead of dropping it.
        string map = version >= MapInRoomInfoVersion ? r.ReadString(BattleProtocol.MaxMapIdBytes) : string.Empty;
        return new RoomInfoMessage(version, name, mode, players, max, running, port, map);
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
        return mode <= (byte)ModeRules.LastMode ? (BattleMode)mode : throw new ProtocolException("Unknown battle mode.");
    }
}
