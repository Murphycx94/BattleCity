using Tank.Core;
using Tank.Core.World;
using Tank.Net.Protocol;
using Tank.Net.Transport;

namespace Tank.Net.Server;

/// <summary>
/// Authoritative game server. Transport-agnostic and clock-agnostic: call <see cref="Tick"/> at exactly
/// 60Hz (see <see cref="ServerRunner"/>) or from a test with a virtual clock.
/// </summary>
public sealed partial class GameServer : ITransportHandler, IRoomInfoProvider
{
    private const int HistorySize = 64;
    private const int MaxUnreliableBytes = 1150;

    private readonly IServerTransport _transport;
    private readonly ServerOptions _options;
    private readonly Action<string> _log;
    private readonly ClientConnection?[] _slots = new ClientConnection?[GameState.MaxPlayers];
    private readonly Dictionary<int, ClientConnection> _byPeer = new();
    private readonly BitWriter _writer = new(4096);
    private readonly SnapshotCodec _codec = new();
    private readonly Snapshot[] _history = new Snapshot[HistorySize];
    private readonly InputCmd[] _packetInputs = new InputCmd[ProtocolInfo.MaxRedundantInputs];
    private readonly InputCmd[] _tickInputs = new InputCmd[GameState.MaxPlayers];
    private readonly List<(int PeerId, long AtFrame)> _pendingKicks = new();
    private readonly Dictionary<int, long> _awaitingHello = new();
    private const int HelloTimeoutFrames = 3 * Units.TickRate;
    private const int ResyncCooldownFrames = Units.TickRate;

    private GameMode _mode;
    private string _mapName;
    private byte _hostSlot = Teams.NoSlot;
    private Simulation? _sim;
    private long _frame;
    private long _endedAtFrame = -1;
    private int _matchNumber;
    private bool _lobbyDirty;

    public GameServer(IServerTransport transport, ServerOptions? options = null, Action<string>? log = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _options = options ?? new ServerOptions();
        _log = log ?? (_ => { });
        _mode = _options.Mode;
        _mapName = _options.MapName ?? MapLibrary.DefaultFor(_mode);
        for (int i = 0; i < HistorySize; i++) _history[i] = new Snapshot { Tick = -1 };
    }

    public Simulation? Simulation => _sim;
    public bool MatchRunning => _sim is not null;
    public GameMode Mode => _mode;
    public string MapName => _mapName;
    public int ConnectedCount => _slots.Count(c => c is { Connected: true });

    public void Tick()
    {
        _frame++;
        _transport.Poll(this);
        ProcessPendingKicks();
        ExpireSilentPeers();
        ExpireDisconnected();
        if (_sim is not null) TickMatch(_sim);
        if (_lobbyDirty) BroadcastLobby();
    }

    public RoomInfoMessage GetRoomInfo() =>
        new(ProtocolInfo.Version, _options.RoomName, _mode, _mapName, ConnectedCount, _options.MaxPlayers, MatchRunning, _options.Port);

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
        if (!_byPeer.Remove(peerId, out var conn)) return;
        _log($"slot {conn.Slot} ({conn.Name}) disconnected: {reason}");
        conn.PeerId = -1;
        conn.DisconnectedAtTick = (int)_frame;
        if (_sim is null) FreeSlot(conn.Slot);
        if (_hostSlot == conn.Slot) ElectHost();
        _lobbyDirty = true;
    }

    public void OnData(int peerId, ReadOnlySpan<byte> data)
    {
        try
        {
            var r = new BitReader(data);
            var type = (MessageType)r.ReadByte();
            if (type == MessageType.Hello)
            {
                HandleHello(peerId, Messages.ReadHello(ref r));
                return;
            }
            if (!_byPeer.TryGetValue(peerId, out var conn)) return; // must say hello first
            switch (type)
            {
                case MessageType.Input: HandleInput(conn, ref r); break;
                case MessageType.LobbyCommand: HandleLobbyCommand(conn, Messages.ReadLobbyCommand(ref r)); break;
                case MessageType.RequestResync: HandleResync(conn); break;
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

    private void HandleResync(ClientConnection conn)
    {
        if (_sim is null || _frame - conn.LastResyncFrame < ResyncCooldownFrames) return;
        conn.LastResyncFrame = _frame;
        SendMatchStart(conn, _sim);
    }

    private void HandleInput(ClientConnection conn, ref BitReader r)
    {
        int count = InputPacket.Read(ref r, out int ack, _packetInputs);
        if (_sim is null) return;
        if (ack <= _sim.State.Tick && ack > conn.AckedSnapshotTick) conn.AckedSnapshotTick = ack;
        for (int i = 0; i < count; i++) conn.Inputs.Add(_packetInputs[i], _sim.State.Tick);
    }

    // ---- match loop ----

    private void TickMatch(Simulation sim)
    {
        int tick = sim.State.Tick + 1;
        for (int slot = 0; slot < GameState.MaxPlayers; slot++)
        {
            var conn = _slots[slot];
            _tickInputs[slot] = conn is not null && sim.State.Players[slot].Active ? conn.Inputs.Take(tick) : InputCmd.Idle(tick);
        }

        sim.Step(_tickInputs);

        if (sim.Events.Count > 0 || sim.State.Map.Changes.Count > 0)
        {
            _writer.Reset();
            Messages.Write(_writer, tick, sim.Events, sim.State.Map.Changes);
            SendToAll(_writer.Data, Channel.Reliable);
        }

        var snapshot = _history[tick % HistorySize];
        SnapshotBuilder.Build(sim.State, snapshot);
        foreach (var conn in _byPeer.Values) SendSnapshot(conn, sim, snapshot);

        if (sim.State.Phase == MatchPhase.Ended)
        {
            if (_endedAtFrame < 0) _endedAtFrame = _frame;
            if (_frame - _endedAtFrame >= _options.ReturnToLobbySeconds * Units.TickRate) EndMatch();
        }
    }

    private void SendSnapshot(ClientConnection conn, Simulation sim, Snapshot snapshot)
    {
        int tick = snapshot.Tick, ack = conn.AckedSnapshotTick;
        Snapshot? baseline = null;
        if (ack > 0 && ack < tick && tick - ack < HistorySize && _history[ack % HistorySize].Tick == ack)
            baseline = _history[ack % HistorySize];

        var header = new ClientHeader
        {
            LastProcessedInputTick = tick,
            InputSlack = conn.Inputs.SlackAt(tick),
        };
        int tankIndex = sim.State.FindTankIndex(sim.State.Players[conn.Slot].TankId);
        if (tankIndex >= 0)
        {
            ref readonly var t = ref sim.State.Tanks[tankIndex];
            header.OwnSlideTicks = t.SlideTicks;
            header.OwnSpawnTicks = t.SpawnTicks;
            header.OwnFireCooldown = t.FireCooldown;
        }

        _writer.Reset();
        _codec.Write(_writer, snapshot, baseline, header);
        // Oversized snapshots (huge bullet storms on a full baseline) fall back to the fragmenting reliable channel.
        var channel = _writer.ByteLength > MaxUnreliableBytes ? Channel.Reliable : Channel.Unreliable;
        _transport.Send(conn.PeerId, _writer.Data, channel);
    }

    private void SendToAll(ReadOnlySpan<byte> data, Channel channel)
    {
        foreach (var conn in _byPeer.Values) _transport.Send(conn.PeerId, data, channel);
    }

    private void ExpireDisconnected()
    {
        double graceFrames = _options.ReconnectGraceSeconds * Units.TickRate;
        for (byte slot = 0; slot < _slots.Length; slot++)
        {
            var conn = _slots[slot];
            if (conn is null || conn.Connected || _frame - conn.DisconnectedAtTick < graceFrames) continue;
            _log($"slot {slot} reconnect window expired");
            FreeSlot(slot);
            _lobbyDirty = true;
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
        foreach (var (peerId, since) in _awaitingHello)
        {
            if (_frame - since < HelloTimeoutFrames || _pendingKicks.Exists(k => k.PeerId == peerId)) continue;
            _log($"peer {peerId} never said hello; disconnecting");
            _pendingKicks.Add((peerId, _frame));
        }
    }

    private void FreeSlot(byte slot)
    {
        _slots[slot] = null;
        if (_sim is not null && _sim.State.Players[slot].Active) _sim.RemovePlayer(slot);
    }
}
