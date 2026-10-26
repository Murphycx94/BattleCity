using Tank.Net.Protocol;
using Tank.Net.Timing;
using Tank.Net.Transport;
using Tank.Sim;
using Tank.Sim.Ai;
using Tank.Sim.Maps;
using Tank.Sim.Modes;
using Tank.Sim.Vision;

namespace Tank.Net.Battle;

/// <summary>
/// The authoritative battle server (stage 4 design §5.8): a room of up to ten seats and, while a match runs, a
/// <see cref="Simulation"/> with its <see cref="VisionTracker"/>, <see cref="MatchRules"/> and bots. Transport- and
/// clock-agnostic: call <see cref="Tick"/> at exactly <see cref="SimConstants.TickRate"/> (see <see cref="ServerRunner"/>)
/// or from a test with a virtual clock. Every client gets its own snapshots and events, holding only what it may see.
/// </summary>
public sealed partial class BattleServer : ITransportHandler, ITickServer
{
    /// <summary>
    /// A snapshot bigger than this goes reliable (<see cref="ChannelFor"/>). LiteNetLib 2.1.4 starts at an MTU of 1024 with
    /// MTU discovery off (our transports leave it off), so one unreliable packet carries at most 1023 bytes; from 1024 bytes
    /// <c>Send</c> throws <c>TooBigPacketException</c>, which used to drop the rest of that tick's sends. Kept under 1023 with a margin.
    /// </summary>
    internal const int MaxUnreliableBytes = 1000;
    private const int WriterBytes = 4096;
    /// <summary>
    /// A match start with the sandbox map's 53 KB fits; for a bigger map the writer grows once, to the size of that match
    /// start (<see cref="BattleMessages.Write(BitWriter, MatchStartMessage, ReadOnlySpan{byte})"/> reserves it up front).
    /// </summary>
    private const int MapWriterBytes = 128 * 1024;
    private const int HelloTimeoutFrames = 3 * SimConstants.TickRate;
    private const int ResyncCooldownFrames = SimConstants.TickRate;
    private const int RejectDisconnectDelayFrames = 15;

    private readonly IServerTransport _transport;
    private readonly BattleServerOptions _options;
    private readonly Action<string> _log;
    private readonly BattleSeat?[] _seats = new BattleSeat?[BattleProtocol.MaxPlayers];
    private readonly Dictionary<int, BattleSeat> _byPeer = new();
    private readonly BitWriter _writer = new(WriterBytes);
    private readonly BitWriter _mapWriter = new(MapWriterBytes);
    private readonly BattleSnapshotCodec _codec = new();
    private readonly InputCmd[] _packetInputs = new InputCmd[BattleProtocol.MaxRedundantInputs];
    private readonly InputCmd[] _tickInputs = new InputCmd[Simulation.MaxTanks];
    private readonly List<GameEvent> _sentEvents = new(64);
    private readonly List<PropState> _propChanges = new(16);
    private readonly List<(int PeerId, long AtFrame)> _pendingKicks = new();
    private readonly Dictionary<int, long> _awaitingHello = new();
    private readonly ServerMaps _maps;

    private BattleMode _mode;
    private int _bots;
    private byte _hostSlot = MatchRules.NoSlot;
    private long _frame;
    private int _matchNumber;
    private bool _roomDirty;
    private MapDefinition _roomMap;
    private MapState _sentMapState;   // what the last room state told of the map: the room goes out again when it changes

    // the match
    private PreparedMap? _matchMap;
    private Simulation? _sim;
    private VisionTracker? _vision;
    private MatchRules? _rules;
    private long _endedAtFrame = -1;

    public BattleServer(IServerTransport transport, BattleServerOptions? options = null, Action<string>? log = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _options = options ?? new BattleServerOptions();
        _log = log ?? (_ => { });
        CheckMaps(_options);
        _mode = _options.Mode;
        _bots = System.Math.Clamp(_options.Bots, 0, BattleProtocol.MaxPlayers - 1);
        _roomMap = _options.Map;
        _maps = new ServerMaps(_options.PrepareMapsInBackground, _log);
        _maps.Prepare(_roomMap);
    }

    /// <summary>
    /// The maps must go on the wire and be found again by id: every id fits <see cref="BattleProtocol.MaxMapIdBytes"/>
    /// in UTF-8 (a longer one would be cut, and the cut id names no map), no two ids differ only in case
    /// (<c>SetMap</c> ignores it), and the first map is one of them (the host could never switch back to it otherwise).
    /// </summary>
    private static void CheckMaps(BattleServerOptions options)
    {
        if (options.Map is null) throw new ArgumentNullException(nameof(options), "The options name no map.");
        if (options.Maps is null) throw new ArgumentNullException(nameof(options), "The options list no maps.");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool listed = false;
        foreach (MapDefinition? map in options.Maps)
        {
            if (map is null) throw new ArgumentNullException(nameof(options), "The options' maps include a null.");
            if (System.Text.Encoding.UTF8.GetByteCount(map.Id) > BattleProtocol.MaxMapIdBytes)
                throw new ArgumentException($"The map id '{map.Id}' is longer than {BattleProtocol.MaxMapIdBytes} bytes in UTF-8.", nameof(options));
            if (!ids.Add(map.Id)) throw new ArgumentException($"Two maps have the id '{map.Id}' (ignoring case).", nameof(options));
            listed |= ReferenceEquals(map, options.Map);
        }
        if (!listed) throw new ArgumentException($"The room's first map '{options.Map.Id}' isn't one of its maps.", nameof(options));
    }

    public BattleMode Mode => _mode;
    public int Bots => _bots;
    public bool MatchRunning => _sim is not null;
    public Simulation? Simulation => _sim;
    public VisionTracker? Vision => _vision;
    public MatchRules? Rules => _rules;
    public int ConnectedCount => _seats.Count(s => s is { Connected: true });

    /// <summary>The map the room's next match is played on.</summary>
    public MapDefinition RoomMap => _roomMap;

    /// <summary>Whether <see cref="RoomMap"/> is generated and encoded: a match can't start before.</summary>
    public bool RoomMapReady => _maps.IsReady(_roomMap);

    /// <summary>Where <see cref="RoomMap"/> is: still generating, ready, or failed for good (no match can start on it).</summary>
    public MapState RoomMapState => _maps.StateOf(_roomMap);

    /// <summary>Where <paramref name="map"/> is (a dedicated server watches its first map: a failed one is fatal there).</summary>
    public MapState MapStateOf(MapDefinition map) => _maps.StateOf(map ?? throw new ArgumentNullException(nameof(map)));

    /// <summary>The maps this server has prepared (tests: how often a map was encoded).</summary>
    internal ServerMaps PreparedMaps => _maps;

    /// <summary>The tank of the player in <paramref name="slot"/>, or 0.</summary>
    public int TankOf(byte slot) => _rules is not null && _rules.Of(slot).Active ? _rules.Of(slot).TankId : 0;

    /// <summary>The channel for a snapshot of <paramref name="bytes"/>: reliable once it is bigger than <see cref="MaxUnreliableBytes"/>.</summary>
    internal static Channel ChannelFor(int bytes) => bytes > MaxUnreliableBytes ? Channel.Reliable : Channel.Unreliable;

    public void Tick()
    {
        _frame++;
        _transport.Poll(this);
        ProcessPendingKicks();
        ExpireSilentPeers();
        ExpireDisconnected();
        if (_sim is not null) TickMatch();
        if (RoomMapState != _sentMapState) _roomDirty = true;   // ready (or failed) in the background since: the room is told
        if (_roomDirty) BroadcastRoom();
    }

    /// <summary>Writes the room's answer to a LAN discovery broadcast.</summary>
    public bool WriteRoomInfo(BitWriter w)
    {
        BattleMessages.Write(w, new RoomInfoMessage(BattleProtocol.Version, _options.RoomName, _mode, ConnectedCount, BattleProtocol.MaxPlayers, MatchRunning, _options.Port, _roomMap.Id));
        return true;
    }

    // ---- transport callbacks ----

    public void OnPeerConnected(int peerId)
    {
        _awaitingHello[peerId] = _frame;
        _log($"peer {peerId} connected, waiting for hello");
    }

    public void OnPeerDisconnected(int peerId, string reason)
    {
        _awaitingHello.Remove(peerId);
        _pendingKicks.RemoveAll(k => k.PeerId == peerId);
        if (!_byPeer.Remove(peerId, out BattleSeat? seat)) return;
        _log($"slot {seat.Slot} ({seat.Name}) disconnected: {reason}");
        seat.PeerId = -1;
        seat.DisconnectedAtFrame = _frame;
        if (_sim is null) FreeSeat(seat.Slot);
        if (_hostSlot == seat.Slot) ElectHost();
        _roomDirty = true;
    }

    public void OnData(int peerId, ReadOnlySpan<byte> data)
    {
        try
        {
            var r = new BitReader(data);
            var type = (BattleMessage)r.ReadByte();
            if (type == BattleMessage.Hello)
            {
                HandleHello(peerId, BattleMessages.ReadHello(ref r));
                return;
            }
            if (!_byPeer.TryGetValue(peerId, out BattleSeat? seat)) return;   // must say hello first
            switch (type)
            {
                case BattleMessage.Input: HandleInput(seat, ref r); break;
                case BattleMessage.RoomCommand: HandleRoomCommand(seat, BattleMessages.ReadRoomCommand(ref r)); break;
                case BattleMessage.RequestMatchState: HandleResync(seat, BattleMessages.ReadMatchStateRequest(ref r)); break;
            }
        }
        catch (ProtocolException ex)
        {
            _log($"dropped malformed packet from peer {peerId}: {ex.Message}");
        }
        catch (Exception ex)
        {
            // One bad packet must never stall the tick for everyone else.
            _log($"error handling packet from peer {peerId}: {ex}");
        }
    }

    private void HandleInput(BattleSeat seat, ref BitReader r)
    {
        int count = BattleInputPacket.Read(ref r, out int ack, _packetInputs);
        if (_sim is null) return;
        if (ack <= _sim.Tick && ack > seat.AckedSnapshotTick) seat.AckedSnapshotTick = ack;
        for (int i = 0; i < count; i++) seat.Inputs.Add(_packetInputs[i], _sim.Tick);
    }

    /// <summary>
    /// The client has lost the thread (a baseline it never got): send the match state again and start the snapshot
    /// chain over — the server forgets what the client acknowledged, so the next snapshot is a full one and no delta
    /// against a baseline the client lacks follows. (A client resynced within the same match keeps the snapshots it
    /// has for interpolation; only a new match clears them.) The map is left out when this connection was already sent
    /// this match's, unless the client says it has none: a resync costs a few bytes, not the whole map.
    /// </summary>
    private void HandleResync(BattleSeat seat, MatchStateRequest request)
    {
        if (_sim is null || _frame - seat.LastResyncFrame < ResyncCooldownFrames) return;
        seat.LastResyncFrame = _frame;
        seat.ForgetSnapshots();
        SendMatchStart(seat, withMap: request.NeedMap || seat.MapSentForMatch != _matchNumber);
    }

    private void Send(BattleSeat seat, ReadOnlySpan<byte> data, Channel channel)
    {
        if (seat.Connected) _transport.Send(seat.PeerId, data, channel);
    }

    private void SendToAll(ReadOnlySpan<byte> data, Channel channel)
    {
        foreach (BattleSeat seat in _byPeer.Values) _transport.Send(seat.PeerId, data, channel);
    }

    private void ExpireDisconnected()
    {
        double graceFrames = _options.ReconnectGraceSeconds * SimConstants.TickRate;
        for (byte slot = 0; slot < _seats.Length; slot++)
        {
            BattleSeat? seat = _seats[slot];
            if (seat is null || seat.IsBot || seat.Connected || _frame - seat.DisconnectedAtFrame < graceFrames) continue;
            _log($"slot {slot} reconnect window expired");
            FreeSeat(slot);
            _roomDirty = true;
        }
    }

    /// <summary>Disconnects rejected peers a little later, so the reject reason actually reaches them.</summary>
    private void ProcessPendingKicks()
    {
        for (int i = _pendingKicks.Count - 1; i >= 0; i--)
        {
            if (_frame < _pendingKicks[i].AtFrame) continue;
            _transport.Disconnect(_pendingKicks[i].PeerId);
            _pendingKicks.RemoveAt(i);
        }
    }

    /// <summary>Peers that connect but never introduce themselves must not hold a connection forever.</summary>
    private void ExpireSilentPeers()
    {
        int expired;
        do   // one at a time: the dictionary can't be changed while it is walked, and this path is all but idle
        {
            expired = -1;
            foreach (var (peerId, since) in _awaitingHello)
            {
                if (_frame - since < HelloTimeoutFrames) continue;
                expired = peerId;
                break;
            }
            if (expired < 0) return;
            _awaitingHello.Remove(expired);   // queued once: not again next tick
            _log($"peer {expired} never said hello; disconnecting");
            _pendingKicks.Add((expired, _frame));
        }
        while (true);
    }

    /// <summary>Empties a seat; a player in the match leaves it. Bots are removed with <see cref="RemoveBots"/> instead.</summary>
    private void FreeSeat(byte slot)
    {
        BattleSeat? seat = _seats[slot];
        if (seat is null) return;
        if (_rules is not null && seat.InMatch)
        {
            _rules.Leave(slot);
            seat.InMatch = false;
        }
        _seats[slot] = null;
    }
}
