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
    private const int MaxUnreliableBytes = 1150;   // a snapshot bigger than this goes reliable: one UDP datagram under the usual MTU
    private const int WriterBytes = 4096;
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

    private BattleMode _mode;
    private int _bots;
    private byte _hostSlot = MatchRules.NoSlot;
    private long _frame;
    private int _matchNumber;
    private bool _roomDirty;

    // the match
    private MapData? _map;
    private Simulation? _sim;
    private VisionTracker? _vision;
    private MatchRules? _rules;
    private long _endedAtFrame = -1;

    public BattleServer(IServerTransport transport, BattleServerOptions? options = null, Action<string>? log = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _options = options ?? new BattleServerOptions();
        _log = log ?? (_ => { });
        _mode = _options.Mode;
        _bots = System.Math.Clamp(_options.Bots, 0, BattleProtocol.MaxPlayers - 1);
    }

    public BattleMode Mode => _mode;
    public int Bots => _bots;
    public bool MatchRunning => _sim is not null;
    public Simulation? Simulation => _sim;
    public VisionTracker? Vision => _vision;
    public MatchRules? Rules => _rules;
    public int ConnectedCount => _seats.Count(s => s is { Connected: true });

    /// <summary>The tank of the player in <paramref name="slot"/>, or 0.</summary>
    public int TankOf(byte slot) => _rules is not null && _rules.Of(slot).Active ? _rules.Of(slot).TankId : 0;

    public void Tick()
    {
        _frame++;
        _transport.Poll(this);
        ProcessPendingKicks();
        ExpireSilentPeers();
        ExpireDisconnected();
        if (_sim is not null) TickMatch();
        if (_roomDirty) BroadcastRoom();
    }

    /// <summary>Writes the room's answer to a LAN discovery broadcast.</summary>
    public bool WriteRoomInfo(BitWriter w)
    {
        BattleMessages.Write(w, new RoomInfoMessage(BattleProtocol.Version, _options.RoomName, _mode, ConnectedCount, BattleProtocol.MaxPlayers, MatchRunning, _options.Port));
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
                case BattleMessage.RequestMatchState: HandleResync(seat); break;
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
    /// has for interpolation; only a new match clears them.)
    /// </summary>
    private void HandleResync(BattleSeat seat)
    {
        if (_sim is null || _frame - seat.LastResyncFrame < ResyncCooldownFrames) return;
        seat.LastResyncFrame = _frame;
        seat.ForgetSnapshots();
        SendMatchStart(seat);
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
