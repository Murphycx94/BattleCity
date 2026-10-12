using Tank.Core.World;

namespace Tank.Net.Protocol;

public sealed record HelloMessage(int ProtocolVersion, string Name, uint SessionToken);

public sealed record WelcomeMessage(byte Slot, uint SessionToken);

public sealed record RejectMessage(string Reason);

public sealed record LobbyPlayer(byte Slot, string Name, byte Team, bool Ready, bool Connected);

public sealed record LobbyStateMessage(GameMode Mode, string MapName, byte HostSlot, bool MatchRunning, IReadOnlyList<LobbyPlayer> Players);

public sealed record MatchStartMessage(
    GameMode Mode,
    string MapName,
    string MapText,
    uint Seed,
    int Stage,
    int EnemyTotal,
    int CurrentTick,
    byte[] Cells,
    int MatchNumber = 0);

public enum LobbyCommandKind : byte
{
    SetTeam = 0,
    SetReady = 1,
    SetMode = 2,
    SetMap = 3,
    StartMatch = 4,
    ReturnToLobby = 5,
}

public sealed record LobbyCommandMessage(LobbyCommandKind Kind, string Arg);

public sealed record RoomInfoMessage(int ProtocolVersion, string RoomName, GameMode Mode, string MapName, int Players, int MaxPlayers, bool MatchRunning, int Port);

public readonly record struct EventBatch(int Tick, IReadOnlyList<GameEvent> Events, IReadOnlyList<CellChange> Cells);

/// <summary>Encoders/decoders for everything except snapshots. Decoders assume the type byte was read.</summary>
public static class Messages
{
    private const int MaxMapTextBytes = 16 * 1024;
    private const int MaxCells = 256 * 256;
    private const int MaxEventsPerBatch = 512;

    public static MessageType PeekType(ReadOnlySpan<byte> data) =>
        data.Length == 0 ? throw new ProtocolException("Empty packet.") : (MessageType)data[0];

    // ---- Hello / Welcome / Reject ----

    public static void Write(BitWriter w, HelloMessage m)
    {
        w.WriteByte((byte)MessageType.Hello);
        w.WriteVarUInt((uint)m.ProtocolVersion);
        w.WriteString(m.Name, ProtocolInfo.MaxPlayerNameBytes);
        w.WriteBits(m.SessionToken, 32);
    }

    public static HelloMessage ReadHello(ref BitReader r) =>
        new((int)r.ReadVarUInt(), r.ReadString(ProtocolInfo.MaxPlayerNameBytes), r.ReadBits(32));

    public static void Write(BitWriter w, WelcomeMessage m)
    {
        w.WriteByte((byte)MessageType.Welcome);
        w.WriteByte(m.Slot);
        w.WriteBits(m.SessionToken, 32);
    }

    public static WelcomeMessage ReadWelcome(ref BitReader r) => new(r.ReadByte(), r.ReadBits(32));

    public static void Write(BitWriter w, RejectMessage m)
    {
        w.WriteByte((byte)MessageType.Reject);
        w.WriteString(m.Reason, 128);
    }

    public static RejectMessage ReadReject(ref BitReader r) => new(r.ReadString(128));

    // ---- Lobby ----

    public static void Write(BitWriter w, LobbyStateMessage m)
    {
        w.WriteByte((byte)MessageType.LobbyState);
        w.WriteByte((byte)m.Mode);
        w.WriteString(m.MapName);
        w.WriteByte(m.HostSlot);
        w.WriteBool(m.MatchRunning);
        w.WriteVarUInt((uint)m.Players.Count);
        foreach (var p in m.Players)
        {
            w.WriteByte(p.Slot);
            w.WriteString(p.Name, ProtocolInfo.MaxPlayerNameBytes);
            w.WriteByte(p.Team);
            w.WriteBool(p.Ready);
            w.WriteBool(p.Connected);
        }
    }

    public static LobbyStateMessage ReadLobbyState(ref BitReader r)
    {
        var mode = ReadMode(ref r);
        string map = r.ReadString();
        byte host = r.ReadByte();
        bool running = r.ReadBool();
        uint count = r.ReadVarUInt();
        if (count > GameState.MaxPlayers) throw new ProtocolException("Too many lobby players.");
        var players = new List<LobbyPlayer>((int)count);
        for (int i = 0; i < count; i++)
            players.Add(new LobbyPlayer(r.ReadByte(), r.ReadString(ProtocolInfo.MaxPlayerNameBytes), r.ReadByte(), r.ReadBool(), r.ReadBool()));
        return new LobbyStateMessage(mode, map, host, running, players);
    }

    public static void Write(BitWriter w, LobbyCommandMessage m)
    {
        w.WriteByte((byte)MessageType.LobbyCommand);
        w.WriteByte((byte)m.Kind);
        w.WriteString(m.Arg);
    }

    public static LobbyCommandMessage ReadLobbyCommand(ref BitReader r)
    {
        byte kind = r.ReadByte();
        if (kind > (byte)LobbyCommandKind.ReturnToLobby) throw new ProtocolException("Unknown lobby command.");
        return new LobbyCommandMessage((LobbyCommandKind)kind, r.ReadString());
    }

    // ---- Match start ----

    public static void Write(BitWriter w, MatchStartMessage m)
    {
        w.WriteByte((byte)MessageType.MatchStart);
        w.WriteByte((byte)m.Mode);
        w.WriteString(m.MapName);
        w.WriteBytes(System.Text.Encoding.UTF8.GetBytes(m.MapText));
        w.WriteBits(m.Seed, 32);
        w.WriteVarUInt((uint)m.Stage);
        w.WriteVarUInt((uint)m.EnemyTotal);
        w.WriteVarUInt((uint)m.CurrentTick);
        w.WriteBytes(m.Cells);
        w.WriteVarUInt((uint)m.MatchNumber);
    }

    public static MatchStartMessage ReadMatchStart(ref BitReader r)
    {
        var mode = ReadMode(ref r);
        string map = r.ReadString();
        string text = System.Text.Encoding.UTF8.GetString(r.ReadBytes(MaxMapTextBytes));
        uint seed = r.ReadBits(32);
        int stage = (int)r.ReadVarUInt();
        int enemies = (int)r.ReadVarUInt();
        int tick = (int)r.ReadVarUInt();
        byte[] cells = r.ReadBytes(MaxCells);
        int matchNumber = (int)r.ReadVarUInt();
        return new MatchStartMessage(mode, map, text, seed, stage, enemies, tick, cells, matchNumber);
    }

    // ---- Events ----

    public static void Write(BitWriter w, int tick, IReadOnlyList<GameEvent> events, IReadOnlyList<CellChange> cells)
    {
        w.WriteByte((byte)MessageType.Events);
        w.WriteVarUInt((uint)tick);
        w.WriteVarUInt((uint)events.Count);
        for (int i = 0; i < events.Count; i++)
        {
            var e = events[i];
            w.WriteByte((byte)e.Type);
            w.WriteVarInt(e.X);
            w.WriteVarInt(e.Z);
            w.WriteVarInt(e.A);
            w.WriteVarInt(e.B);
        }
        w.WriteVarUInt((uint)cells.Count);
        for (int i = 0; i < cells.Count; i++)
        {
            var c = cells[i];
            w.WriteVarUInt((uint)c.Index);
            w.WriteBits((uint)c.Type, 3);
        }
    }

    public static EventBatch ReadEvents(ref BitReader r)
    {
        int tick = (int)r.ReadVarUInt();
        uint count = r.ReadVarUInt();
        if (count > MaxEventsPerBatch) throw new ProtocolException("Too many events.");
        var events = new GameEvent[count];
        for (int i = 0; i < count; i++)
            events[i] = new GameEvent((GameEventType)r.ReadByte(), tick, r.ReadVarInt(), r.ReadVarInt(), r.ReadVarInt(), r.ReadVarInt());
        uint cellCount = r.ReadVarUInt();
        if (cellCount > MaxCells) throw new ProtocolException("Too many cell changes.");
        var cells = new CellChange[cellCount];
        for (int i = 0; i < cellCount; i++) cells[i] = new CellChange((int)r.ReadVarUInt(), (CellType)r.ReadBits(3));
        return new EventBatch(tick, events, cells);
    }

    // ---- Discovery ----

    public static void WriteDiscover(BitWriter w)
    {
        w.WriteByte((byte)MessageType.Discover);
        w.WriteVarUInt(ProtocolInfo.Version);
    }

    public static void Write(BitWriter w, RoomInfoMessage m)
    {
        w.WriteByte((byte)MessageType.RoomInfo);
        w.WriteVarUInt((uint)m.ProtocolVersion);
        w.WriteString(m.RoomName, 48);
        w.WriteByte((byte)m.Mode);
        w.WriteString(m.MapName);
        w.WriteVarUInt((uint)m.Players);
        w.WriteVarUInt((uint)m.MaxPlayers);
        w.WriteBool(m.MatchRunning);
        w.WriteVarUInt((uint)m.Port);
    }

    public static RoomInfoMessage ReadRoomInfo(ref BitReader r) =>
        new((int)r.ReadVarUInt(), r.ReadString(48), ReadMode(ref r), r.ReadString(), (int)r.ReadVarUInt(),
            (int)r.ReadVarUInt(), r.ReadBool(), (int)r.ReadVarUInt());

    private static GameMode ReadMode(ref BitReader r)
    {
        byte mode = r.ReadByte();
        return mode <= (byte)GameMode.Deathmatch ? (GameMode)mode : throw new ProtocolException("Unknown game mode.");
    }
}
