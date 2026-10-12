using Tank.Core;
using Tank.Core.Math;
using Tank.Core.World;
using Tank.Net.Protocol;
using Tank.Net.Transport;

namespace Tank.Net.Client;

/// <summary>
/// Network client. Owns prediction, interpolation and clock sync, and exposes ready-to-draw state.
/// Drive it with <see cref="Update"/> once per rendered frame.
/// </summary>
public sealed partial class GameClient : ITransportHandler, IDisposable
{
    private readonly IClientTransport _transport;
    private readonly Func<double> _clock;
    private readonly string _playerName;
    private readonly BitWriter _writer = new(512);
    private readonly SnapshotCodec _codec = new();
    private readonly Snapshot _decodeScratch = new();
    private readonly InputCmd[] _sendScratch = new InputCmd[ProtocolInfo.MaxRedundantInputs];
    private readonly List<RenderEntity> _entities = new(128);
    private readonly List<GameEvent> _localEvents = new(16);
    private readonly Queue<GameEvent> _fxEvents = new();
    private readonly Func<NetEntity, bool> _renderFilter;
    private readonly Func<int, Snapshot?> _findSnapshot;
    private bool _fireLatched;

    public GameClient(IClientTransport transport, string playerName, Func<double> clock, uint sessionToken = 0)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _playerName = playerName;
        SessionToken = sessionToken;
        Store = new SnapshotStore();
        Interpolator = new SnapshotInterpolator(Store);
        _renderFilter = IsRemotelyRendered;
        _findSnapshot = Store.Find;
    }

    public ClientPhase Phase { get; private set; } = ClientPhase.Connecting;
    public string? DisconnectReason { get; private set; }
    public byte Slot { get; private set; } = Teams.NoSlot;
    public uint SessionToken { get; private set; }
    public LobbyStateMessage? Lobby { get; private set; }
    public bool IsHost => Lobby is not null && Lobby.HostSlot == Slot;

    public SnapshotStore Store { get; }
    public SnapshotInterpolator Interpolator { get; }
    public ClockSync Clock { get; } = new();
    public ClientPredictor Predictor { get; } = new();

    /// <summary>Remote entities at render time (excludes the local tank and local bullets, which are predicted).</summary>
    public IReadOnlyList<RenderEntity> Entities => _entities;

    public NetStats Stats { get; private set; }

    /// <summary>Why the most recent packet was dropped, if any (shown in logs / net graph).</summary>
    public string? LastPacketError { get; private set; }

    public int DroppedPackets => _droppedPackets;

    public void Update(double dt, LocalInput input)
    {
        _transport.Poll(this);
        if (Phase == ClientPhase.InMatch && Map is not null)
        {
            _fireLatched |= input.FirePressed || input.FireHeld;
            int ticks = Clock.Advance(dt);
            for (int i = 0; i < ticks; i++) SimulateTick(input, PredictionMap ?? Map);

            double now = _clock();
            Interpolator.Advance(dt, now);
            ApplyDueEvents();
            Interpolator.Sample(_entities, _renderFilter);
            Predictor.DecayVisualOffset(dt);
            CheckMapChecksum();
        }
        UpdateStats(dt);
    }

    /// <summary>Presentation events (explosions, sparks...) in the order they should play.</summary>
    public bool TryDequeueEvent(out GameEvent e) => _fxEvents.TryDequeue(out e);

    // ---- lobby commands ----

    public void SetReady(bool ready) => SendLobby(LobbyCommandKind.SetReady, ready ? "1" : "0");

    public void SetTeam(byte team) => SendLobby(LobbyCommandKind.SetTeam, team.ToString());

    public void SetMode(GameMode mode) => SendLobby(LobbyCommandKind.SetMode, mode.ToString());

    public void SetMap(string mapName) => SendLobby(LobbyCommandKind.SetMap, mapName);

    public void StartMatch() => SendLobby(LobbyCommandKind.StartMatch, string.Empty);

    public void ReturnToLobby() => SendLobby(LobbyCommandKind.ReturnToLobby, string.Empty);

    public void Dispose() => _transport.Dispose();

    // ---- transport callbacks ----

    public void OnPeerConnected(int peerId)
    {
        _writer.Reset();
        Messages.Write(_writer, new HelloMessage(ProtocolInfo.Version, _playerName, SessionToken));
        _transport.Send(_writer.Data, Channel.Reliable);
    }

    public void OnPeerDisconnected(int peerId, string reason)
    {
        Phase = ClientPhase.Disconnected;
        DisconnectReason ??= reason;
    }

    public void OnData(int peerId, ReadOnlySpan<byte> data)
    {
        try
        {
            var r = new BitReader(data);
            switch ((MessageType)r.ReadByte())
            {
                case MessageType.Welcome: HandleWelcome(Messages.ReadWelcome(ref r)); break;
                case MessageType.Reject: DisconnectReason = Messages.ReadReject(ref r).Reason; Phase = ClientPhase.Disconnected; break;
                case MessageType.LobbyState: HandleLobby(Messages.ReadLobbyState(ref r)); break;
                case MessageType.MatchStart: HandleMatchStart(Messages.ReadMatchStart(ref r)); break;
                case MessageType.Snapshot: HandleSnapshot(ref r); break;
                case MessageType.Events: OnEventBatch(Messages.ReadEvents(ref r)); break;
            }
        }
        catch (Exception ex) when (ex is ProtocolException or FormatException or ArgumentException or InvalidOperationException)
        {
            // A malformed packet is dropped; the game keeps running and the reason is kept for diagnostics.
            _droppedPackets++;
            LastPacketError = ex.Message;
        }
    }

    private void HandleWelcome(WelcomeMessage welcome)
    {
        Slot = welcome.Slot;
        SessionToken = welcome.SessionToken;
        if (Phase == ClientPhase.Connecting) Phase = ClientPhase.Lobby;
    }

    private void HandleLobby(LobbyStateMessage lobby)
    {
        Lobby = lobby;
        if (!lobby.MatchRunning && Phase == ClientPhase.InMatch)
        {
            Phase = ClientPhase.Lobby;
            Map = null;
            PredictionMap = null;
            MatchInfo = null;
        }
    }

    private void SendLobby(LobbyCommandKind kind, string arg)
    {
        _writer.Reset();
        Messages.Write(_writer, new LobbyCommandMessage(kind, arg));
        _transport.Send(_writer.Data, Channel.Reliable);
    }

    private bool IsRemotelyRendered(NetEntity e)
    {
        if (e.Kind == EntityKind.Bullet && e.Slot == Slot) return false; // own bullets are predicted
        if (e.Kind == EntityKind.Tank && Predictor.HasTank && e.Id == Predictor.Current.Id) return false;
        return true;
    }
}
